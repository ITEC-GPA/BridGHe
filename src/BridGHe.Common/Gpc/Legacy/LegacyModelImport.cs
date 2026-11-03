using System;
using System.Collections.Generic;
using System.Linq;
using GPC.Geometry;
using GPC.Model.Attributes;
using GPC.Model.Combinations;
using GPC.Model.Constraints;
using GPC.Model.Core.Identity;
using GPC.Model.ElementProperties;
using GPC.Model.Elements;
using GPC.Model.LoadCases;
using GPC.Model.Loads;
using GPC.Model.Materials;
using GPC.Model.Models;
using GPC.Model.Restraints;
using GPC.Model.Sections;
using GPC.Model.Structure.Assignments;

namespace BridGHe.Common.Gpc.Legacy
{
    /// <summary>Diagnostics of a legacy conversion. Nothing is dropped silently: every legacy datum that has no GPC
    /// destination yet is listed (with the element count), and data that changes the analysis is rejected.</summary>
    public sealed class LegacyImportReport
    {
        public List<string> Warnings { get; } = new List<string>();
        public Dictionary<string, int> NotExported { get; } = new Dictionary<string, int>(StringComparer.Ordinal);
        internal void Carry(string what) { NotExported.TryGetValue(what, out int n); NotExported[what] = n + 1; }
    }

    /// <summary>Converts a former BridGHe FEM model (BGHA1 archive, read as data by <see cref="LegacyArchive"/>) into a GPC
    /// model in N-mm with the semantics of the former Straus7 exporter: node order and numbering, node merging by tolerance
    /// at the beam and link ends, load-case set with the default "Dead" self-weight case, one group per element.
    /// Analysis results and caches of the former model are not converted (they are recomputed).</summary>
    public static class LegacyModelImport
    {
        public const string Program = "BridGHe";

        public static Model Import(LegacyValue legacy, Guid owner, double toleranceDefinitionUnits, LegacyImportReport report)
        {
            if (legacy == null || !legacy.Is("ModelModel")) throw new ArgumentException("LegacyModelModelExpected");
            report ??= new LegacyImportReport();
            var units = new BridgeUnits(BridgeUnits.Parse(legacy.Enum("Units")));
            string name = legacy.String("Name") ?? "";
            var model = new Model(owner, name);
            var elements = legacy.Field("Elements").Items;
            var nodes = elements.Where(e => e.Is("NodeModel")).ToList();
            var beams = elements.Where(e => e.Is("BeamModel")).ToList();
            var others = elements.Where(e => !e.Is("NodeModel") && !e.Is("BeamModel")).ToList();
            if (others.Count != 0) throw new NotSupportedException("LegacyElementTypeNotMappedYet: " + string.Join(", ", others.Select(o => o.ShortType).Distinct()));
            if (legacy.Has("MovingPaths") && legacy.String("MovingPaths") != "[]") throw new NotSupportedException("LegacyMovingPathsNotMappedYet");
            if (legacy.Has("StagedConstruction") && legacy.Field("StagedConstruction").Items.Count != 0) throw new NotSupportedException("LegacyStagedConstructionNotMappedYet");
            if (legacy.Has("BridgeWorkflow")) report.Carry("BridgeWorkflow (checks recipe, not exported)");

            var cases = Cases(legacy, model, report);
            var groups = new Dictionary<string, Group>(StringComparer.Ordinal);
            Group GroupOf(LegacyValue g)
            {
                string full = FullName(g);
                if (groups.TryGetValue(full, out var known)) return known;
                var parent = g["Parent"].IsNull ? null : GroupOf(g["Parent"]);
                var group = new Group(GpcIdentity.Group(full), full);
                parent?.AddChild(group);
                groups.Add(full, group); model.Groups.Add(group.Name, group);
                return group;
            }

            // Nodes: former order and numbering.
            var gpcNodes = new List<NodeElement>();
            for (int i = 0; i < nodes.Count; i++)
            {
                var n = nodes[i]; string nodeId = n.String("NodeId") ?? "";
                var node = new NodeElement(GpcIdentity.Derive(owner, "node:" + i), Point(n["Position"], units), null, nodeId, i + 1)
                { Source = new SourceIdentity(Program, owner.ToString("D"), EntityFamily.Node, nodeId.Length != 0 ? nodeId : (i + 1).ToString()) };
                gpcNodes.Add(node); model.NodesElements.Add(node);
            }
            // Node groups: organizational metadata (Straus7 nodes have no group; the writer creates the groups, as the former exporter).
            for (int i = 0; i < nodes.Count; i++) foreach (var g in nodes[i].Field("Groups").Items) gpcNodes[i].AddGroup(GroupOf(g));
            var index = new NodeLookup(gpcNodes, units.ToMm(toleranceDefinitionUnits));
            for (int i = 0; i < nodes.Count; i++) NodeData(nodes[i], gpcNodes[i], cases, units, report);
            // Rigid links in node order, as the former RigidConnectionExport.
            var pairs = new HashSet<(int, int)>();
            for (int i = 0; i < nodes.Count; i++)
                foreach (var a in nodes[i].Field("Attributes").Items.Where(a => a.Is("RigidConnectionAttributeModel")))
                {
                    var (target, matches) = index.Find(Point(a["Target"], units));
                    if (matches != 1) throw new ArgumentException("Rigid link target matches more than one FEM node. Merge nodes before export.");
                    if (target == i) throw new ArgumentException("Rigid link collapses to the same node within model tolerance.");
                    if (!pairs.Add((Math.Min(i, target), Math.Max(i, target)))) throw new ArgumentException("Duplicate rigid link after node merging.");
                    model.AddCostrains(new[] { new RigidLink(gpcNodes[i], gpcNodes[target]) });
                }

            // Beams: former order and numbering; nodes found by tolerance (StrausNodeIndex semantics).
            // The former exporter created one native property per property name: later properties with the same name used the first.
            var properties = new Dictionary<string, (BeamProperty Property, BeamFormulation Formulation, double Angle, string Signature)>(StringComparer.Ordinal);
            var shearAreas = new Dictionary<string, (double, double)>(StringComparer.Ordinal);
            for (int k = 0; k < beams.Count; k++)
            {
                var b = beams[k]; string beamId = b.String("BeamId") ?? "";
                var from = Point(b["PointFrom"], units); var to = Point(b["PointTo"], units);
                var (i, mi) = index.Find(from); var (j, mj) = index.Find(to);
                if (mi > 1) report.Warnings.Add($"Collapsing {mi} nodes at coordinates {from}");
                if (mj > 1) report.Warnings.Add($"Collapsing {mj} nodes at coordinates {to}");
                string propertyName = PropertyName(b["BeamProperty"]); string signature = Signature(b["BeamProperty"]);
                if (!properties.TryGetValue(propertyName, out var p))
                {
                    var (property, formulation, angle) = Property(b["BeamProperty"], propertyName, model, units, shearAreas, report);
                    properties.Add(propertyName, p = (property, formulation, angle, signature));
                }
                else if (p.Signature != signature) report.Warnings.Add($"The beam property {propertyName} already exists");
                var start = gpcNodes[i].Position; var end = gpcNodes[j].Position;
                var beam = new BeamElement(GpcIdentity.Derive(owner, "beam:" + k), start, end, p.Property, null, beamId, k + 1)
                { Source = new SourceIdentity(Program, owner.ToString("D"), EntityFamily.Beam, beamId.Length != 0 ? beamId : (k + 1).ToString()) };
                beam.ConnectNodes(gpcNodes[i], gpcNodes[j]);
                double angleDeg = b.Double("Angle");
                var section = GpcBeamAxes.Section(start, end, angleDeg, p.Angle);
                var principal = GpcBeamAxes.Principal(start, end, angleDeg);
                beam.Assignments.Formulation = p.Formulation;
                beam.Assignments.SectionAxes = section;
                var offset = b["Offset"]; double ox = units.ToMm(offset.Double("X")), oy = units.ToMm(offset.Double("Y"));
                var globalOffset = principal.V1 * ox + principal.V2 * oy;
                if (ox != 0 || oy != 0)
                {
                    beam.Assignments.OffsetAxes = principal;
                    beam.Assignments.OffsetI = new Vector3d(ox, oy, 0); beam.Assignments.OffsetJ = new Vector3d(ox, oy, 0);
                }
                var bg = b.Field("Groups").Items;
                if (bg.Count > 0) beam.AddGroup(GroupOf(bg[0]));
                if (bg.Count > 1) report.Warnings.Add($"Straus7 allows assigning only one single group to the beam {k + 1}");
                BeamData(b, beam, principal, globalOffset, cases, units, report);
                model.BeamElements.Add(beam);
            }

            foreach (var c in legacy.Field("LoadCombinations").Items)
            {
                string cname = c.String("Name");
                var combination = new Combination(cname, GpcIdentity.Combination(cname));
                var keys = c.Field("Keys").Items; var values = c.Field("Values").Items;
                if (keys.Count != values.Count) throw new ArgumentException("InvalidLegacyCombination: " + cname);
                for (int q = 0; q < keys.Count; q++) combination.AddLoadCaseCoefficient(cases[keys[q].String("Name")], values[q].AsDouble());
                model.Combinations.Add(combination.Name, combination);
            }
            return model;
        }

        /// <summary>The former case set: model cases (not generated by moving loads), the cases of element loads and of the
        /// combinations, grouped by name; the default "Dead" case (self weight along −Z, 9806.65 mm/s², structural mass);
        /// numbering by name with Dead first (the writer numbers by ordinal name).</summary>
        private static Dictionary<string, LoadCaseBase> Cases(LegacyValue legacy, Model model, LegacyImportReport report)
        {
            var generated = new HashSet<string>(StringComparer.Ordinal);
            if (legacy.Has("GeneratedMovingCases") && legacy.String("GeneratedMovingCases") != "{}") throw new NotSupportedException("LegacyGeneratedMovingCasesNotMappedYet");
            var candidates = legacy.Field("LoadCases").Items
                .Concat(legacy.Field("Elements").Items.SelectMany(e => e.Field("Loads").Items).Select(l => l.Field("LoadCase")))
                .Concat(legacy.Field("LoadCombinations").Items.SelectMany(c => c.Field("Keys").Items)).ToList();
            var result = new Dictionary<string, LoadCaseBase>(StringComparer.Ordinal);
            var first = new Dictionary<string, LegacyValue>(StringComparer.Ordinal);
            foreach (var c in candidates)
            {
                string name = c.String("Name");
                if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Caso di carico nullo o senza nome.");
                if (!first.TryGetValue(name, out var known)) first.Add(name, c);
                else if (!SameCase(known, c)) throw new ArgumentException("Definizioni incompatibili per il caso di carico " + name);
            }
            if (!first.ContainsKey("Dead")) first.Add("Dead", null);
            foreach (var pair in first)
            {
                var c = pair.Value;
                bool gravity = c == null || c.Enum("GlobalInertiaLoad") == "Gravity";
                if (c != null && c.Enum("GlobalInertiaLoad") != "Gravity" && c.Enum("GlobalInertiaLoad") != "None")
                    throw new NotSupportedException("LegacyInertiaLoadNotMappedYet: " + c.Enum("GlobalInertiaLoad"));
                if (c != null && (c.Double("Temperature") != 0 || c.Has("Stages") && c.Field("Stages").Items.Count != 0)) report.Carry("Load case temperature or stage list (not exported by the former writer)");
                LoadCaseBase gpc = gravity ? new LoadCase(GpcIdentity.Case(pair.Key), pair.Key, LoadCase.LoadCaseTypes.SelfWeight)
                    : new LoadCaseBase(GpcIdentity.Case(pair.Key), pair.Key);
                bool structural = c == null || c.Bool("StructuralMassAcceleration"), nonStructural = c != null && c.Bool("NonStructuralMassAcceleration");
                gpc.InertiaMasses = new InertiaMassSelection { Structural = structural, NonStructural = nonStructural };
                model.LoadCases.Add(gpc.Name, gpc);
                if (gravity)
                {
                    // Gravity value in mm/s², already N-mm in the former model (Dead: −9806.65 along Z).
                    double value = c == null ? -9806.65 : c.Double("GravityValue");
                    string direction = c == null ? "Z" : c.Enum("GravityDirection");
                    var axis = direction == "X" ? Vector3d.XAxis : direction == "Y" ? Vector3d.YAxis : direction == "Z" ? Vector3d.ZAxis : throw new NotSupportedException("LegacyGravityDirection: " + direction);
                    if (value != 0) model.ModelLoads.Add(new ModelGravityLoad(axis, value, gpc, CoordinateSystem.Global, "Gravity " + pair.Key));
                }
                result.Add(pair.Key, gpc);
            }
            return result;
        }

        private static bool SameCase(LegacyValue a, LegacyValue b) =>
            a.Double("Temperature") == b.Double("Temperature") && a.Bool("StructuralMassAcceleration") == b.Bool("StructuralMassAcceleration")
            && a.Bool("NonStructuralMassAcceleration") == b.Bool("NonStructuralMassAcceleration") && a.Enum("GlobalInertiaLoad") == b.Enum("GlobalInertiaLoad")
            && a.Enum("GravityDirection") == b.Enum("GravityDirection") && a.Double("GravityValue") == b.Double("GravityValue");

        private static void NodeData(LegacyValue n, NodeElement node, Dictionary<string, LoadCaseBase> cases, BridgeUnits units, LegacyImportReport report)
        {
            var attributes = n.Field("Attributes").Items;
            var frames = attributes.Where(a => a.Is("NodeCoordinateAttributeModel")).Select(a => a["Frame"]).ToArray();
            if (frames.Length > 1 && frames.Skip(1).Any(f => !SamePlane(f, frames[0]))) throw new ArgumentException($"Node {node.Name}: conflicting/invalid restraint coordinate systems.");
            var axes = frames.Length == 0 ? null : Frame(frames[0], units);
            var restraints = attributes.Where(a => a.Is("NodeRestraintAttributeModel")).ToArray();
            if (restraints.Length != 0)
            {
                var dofs = new[] { "Tx", "Ty", "Tz", "Rx", "Ry", "Rz" }.Select((d, k) => new DofRestrain((GeometryRestrain.DOF)k, restraints.Any(r => r.Bool(d)))).ToList();
                if (n.Field("Loads").Items.Any(l => l.Is("NodeDisplacementLoadModel"))) throw new NotSupportedException("LegacyPrescribedDisplacementNotMappedYet");
                node.Assignments.Restrains.Add(new RestrainAssignment { Restrain = new NodeRestrain(node, axes ?? CoordinateSystem.Global, dofs), SourceRecord = "NodeRestraintAttributeModel" });
            }
            foreach (var a in attributes)
            {
                if (a.Is("NodeCoordinateAttributeModel") || a.Is("NodeRestraintAttributeModel") || a.Is("RigidConnectionAttributeModel")) continue;
                if (a.Is("ElementIdAttributeModel")) { report.Carry("Element ID attribute (native user ID: the writer uses the GPC numbering)"); continue; }
                throw new NotSupportedException("LegacyNodeAttributeNotMappedYet: " + a.ShortType);
            }
            var sums = new Dictionary<string, (Vector3d F, Vector3d M)>(StringComparer.Ordinal);
            foreach (var l in n.Field("Loads").Items)
            {
                string c = l.Field("LoadCase").String("Name");
                if (l.Is("NodeForceLoadModel") || l.Is("NodeMomentLoadModel"))
                {
                    sums.TryGetValue(c, out var s); var v = Vector(l); // NodeForceLoadModel/NodeMomentLoadModel store X, Y, Z
                    sums[c] = l.Is("NodeForceLoadModel") ? ((s.F ?? Zero) + v * units.Force, s.M ?? Zero) : (s.F ?? Zero, (s.M ?? Zero) + v * (units.Force * units.Length));
                }
                else throw new NotSupportedException("LegacyNodeLoadNotMappedYet: " + l.ShortType);
            }
            foreach (var s in sums) node.Loads.Add(new PointLoad(s.Value.F, s.Value.M, node.Position, cases[s.Key]));
        }
        private static readonly Vector3d Zero = new Vector3d(0, 0, 0);

        private static void BeamData(LegacyValue b, BeamElement beam, CoordinateSystem principal, Vector3d offset, Dictionary<string, LoadCaseBase> cases, BridgeUnits units, LegacyImportReport report)
        {
            foreach (var a in b.Field("Attributes").Items)
            {
                switch (a.ShortType)
                {
                    case "ElementIdAttributeModel": report.Carry("Element ID attribute (native user ID: the writer uses the GPC numbering)"); break;
                    case "SuperElementAttributeModel": report.Carry("Super element name (checks input, not exported)"); break;
                    case "BridgeMemberAttributeModel": report.Carry("Bridge member metadata (checks input, not exported)"); break;
                    case "BridgeSectionStateAttributeModel": report.Carry("Bridge section state (checks input, not exported)"); break;
                    case "BeamOutputStationsModel":
                        (beam.Assignments.Physical ??= new BeamPhysicalData()).MinimumOutputStations = a.Int("MinimumNumberOfStations"); break;
                    default: throw new NotSupportedException("LegacyBeamAttributeNotMappedYet: " + a.ShortType);
                }
            }
            int loadIndex = 0; var preLoadCases = new HashSet<string>(StringComparer.Ordinal);
            foreach (var l in b.Field("Loads").Items)
            {
                var loadCase = cases[l.Field("LoadCase").String("Name")]; string source = "BeamLoad/" + beam.Id + "/" + (++loadIndex);
                switch (l.ShortType)
                {
                    case "BeamDistributedLoadModel": beam.Assignments.Loads.AddRange(Distributed(l, beam, principal, offset, loadCase, units, source)); break;
                    case "BeamPointLoadModel": beam.Assignments.Loads.Add(PointLoad(l, beam, principal, offset, loadCase, units, source)); break;
                    case "BeamPreLoadModel":
                        if (!preLoadCases.Add(loadCase.Name)) throw new ArgumentException("Beam " + beam.Id + " has multiple prestrain in loadcase " + loadCase.Name);
                        bool strain = l.Enum("LoadType") == "Strain";
                        (beam.Assignments.Physical ??= new BeamPhysicalData()).InitialStates.Add(new BeamInitialState
                        { Case = loadCase, Kind = strain ? BeamInitialStateKind.AxialStrain : BeamInitialStateKind.AxialForce, Value = strain ? l.Double("Value") : units.ToN(l.Double("Value")) });
                        break;
                    case "BeamGradientLoadModel":
                        (beam.Assignments.Physical ??= new BeamPhysicalData()).TemperatureGradients.Add(new BeamTemperatureGradient
                        { Case = loadCase, Axis1 = units.ToCelsiusPerMm(l.Double("Axis1")), Axis2 = units.ToCelsiusPerMm(l.Double("Axis2")) });
                        break;
                    default: throw new NotSupportedException("LegacyBeamLoadNotMappedYet: " + l.ShortType);
                }
            }
        }

        private static IEnumerable<GPC.Model.Loads.Assignments.BeamLoadAssignment> Distributed(LegacyValue l, BeamElement beam, CoordinateSystem principal, Vector3d offset,
            LoadCaseBase loadCase, BridgeUnits units, string source)
        {
            var schema = (BridgeLoadSchema)Enum.Parse(typeof(BridgeLoadSchema), l.Enum("LoadSchema"));
            bool moment = l.Enum("LoadType") == "Moment"; string direction = l.Enum("LoadDirection");
            double pa = l.Double("PA"), pb = l.Double("PB"), p1 = l.Double("P1"), p2 = l.Double("P2"), a = l.Double("A"), b = l.Double("B");
            double scale = moment ? units.ToNmmPerMm(1) : units.ToNPerMm(1);
            if (l["CoordinateSystem"].IsNull)
            {
                // Principal (local) axes: X, Y, Z are the beam principal axes 1, 2, 3.
                var axis = direction == "X" ? Vector3d.XAxis : direction == "Y" ? Vector3d.YAxis : direction == "Z" ? Vector3d.ZAxis : throw new NotSupportedException("LegacyLocalLoadDirection: " + direction);
                return GpcBeamLoads.Distributed(loadCase, beam.StartPoint, beam.EndPoint, schema, pa, pb, p1, p2, a, b, axis, moment, principal, null, offset, scale, source);
            }
            // The former exporter wrote every global distributed action as a global force (also LoadType = Moment).
            if (moment) throw new NotSupportedException("LegacyGlobalDistributedMomentWasExportedAsForce");
            if (!IsGlobal(l["CoordinateSystem"])) throw new NotSupportedException("LegacyNonGlobalLoadCoordinateSystemWasIgnored");
            bool gravity = direction.StartsWith("Gravity", StringComparison.Ordinal);
            bool projected = direction.EndsWith("_Projected", StringComparison.Ordinal);
            string d = gravity ? "Z" : direction.Substring(0, 1);
            var global = d == "X" ? Vector3d.XAxis : d == "Y" ? Vector3d.YAxis : Vector3d.ZAxis;
            // Gravity loads point down: positive values act along −Z.
            var vector = gravity ? new Vector3d(0, 0, -1) : global;
            return GpcBeamLoads.Distributed(loadCase, beam.StartPoint, beam.EndPoint, schema, pa, pb, p1, p2, a, b, vector, false, null,
                projected ? global : null, offset, scale, source);
        }

        private static GPC.Model.Loads.Assignments.BeamLoadAssignment PointLoad(LegacyValue l, BeamElement beam, CoordinateSystem principal, Vector3d offset,
            LoadCaseBase loadCase, BridgeUnits units, string source)
        {
            // The former archive did not store the coordinate system of a beam point load ("TODO" in BeamPointLoadModel), so every
            // analysis (which cloned the model first) exported it in the principal axes: read it as such.
            bool moment = l.Enum("LoadType") == "Moment";
            return GpcBeamLoads.Concentrated(loadCase, beam.StartPoint, beam.EndPoint, l.Double("A"), Vector(l["Force"]), moment,
                principal, offset, moment ? units.Force * units.Length : units.Force, source);
        }

        private static string PropertyName(LegacyValue p) => p.String("Name") ?? p.String("Id") ?? "Property";
        /// <summary>Content of a legacy property as text (to detect different properties with the same name).</summary>
        private static string Signature(LegacyValue v)
        {
            if (v.IsNull) return "null";
            if (v.Text != null) return v.Text;
            if (v.IsCollection) return "[" + string.Join(",", v.Items.Select(Signature)) + "]";
            return "{" + string.Join(";", v.Fields.Where(f => f.Key != "CompositePreview").OrderBy(f => f.Key, StringComparer.Ordinal).Select(f => f.Key + "=" + Signature(f.Value))) + "}";
        }

        private static (BeamProperty, BeamFormulation, double) Property(LegacyValue p, string propertyName, Model model, BridgeUnits units,
            Dictionary<string, (double, double)> shearAreas, LegacyImportReport report)
        {
            string type = p.Enum("BeamPropertyType");
            var formulation = type == "Beam" ? BeamFormulation.StraightTwoNode : type == "Truss" ? BeamFormulation.Truss : type == "Cable" ? BeamFormulation.Cable
                : type == "CutoffBar" ? BeamFormulation.CutoffBar : type == "PointContact" ? BeamFormulation.PointContact : type == "Pipe" ? BeamFormulation.Pipe
                : type == "Connection" ? BeamFormulation.Connection : throw new NotSupportedException("LegacyBeamPropertyType: " + type);
            if (formulation == BeamFormulation.CutoffBar) throw new NotSupportedException("LegacyCutoffBarNotMappedYet");
            var m = p.Field("Material"); var s = p.Field("SectionModel");
            if (!s["NativeSection"].IsNull) throw new NotSupportedException("LegacyNativeSectionNotMappedYet");
            if (s.Enum("MirrorType") != "None") throw new NotSupportedException("LegacyMirroredSectionNotMappedYet");
            var material = GpcMaterials.Elastic(m.String("Name"), m.Double("Modulus"), m.Double("PoissonRatio"), m.Double("ShearModulus"), m.Bool("UsePoisson"),
                m.Double("Density"), m.Double("ThermalExpansion"), m.Double("ViscousDamping"), m.Double("DampingRatio"), m.Double("Conductivity"), m.Double("SpecificHeat"),
                StressStrain(m["StressStrainTable"]));
            if (!m["StressStrainTable"].IsNull && type != "Beam" && type != "Truss") material.AnalysisData.StressStrainPairs = null; // former exporter: tables only for beams and trusses
            var kind = (BridgeSectionKind)Enum.Parse(typeof(BridgeSectionKind), s.Enum("SectionType"));
            string sectionName = s.String("Name") ?? ""; double angle = s.Double("AngleX1");
            double L(string f) => units.ToMm(s.Double(f));
            ISectionShape section; double shear1, shear2;
            if (kind == BridgeSectionKind.Generic || kind == BridgeSectionKind.Composite)
            {
                if (kind == BridgeSectionKind.Composite) throw new NotSupportedException("LegacyCompositeSectionTypeNotMappedYet");
                var c = s["Centroid"];
                section = GpcSections.Explicit(sectionName, units.ToMm2(s.Double("Area")), units.ToMm4(s.Double("I11")), units.ToMm4(s.Double("I22")), units.ToMm4(s.Double("J")),
                    units.ToMm(c.Double("X")), units.ToMm(c.Double("Y")), units.ToMm(s.Double("ShearL1")), units.ToMm(s.Double("ShearL2")), angle);
                shear1 = units.ToMm2(s.Double("ShearA1")); shear2 = units.ToMm2(s.Double("ShearA2"));
                if (!s["CompositePreview"].IsNull) report.Carry("Composite preview geometry (preview and checks input, not exported)");
            }
            else
            {
                if (angle != 0) throw new NotSupportedException("LegacyStandardSectionWithPrincipalAngleNotMappedYet");
                section = GpcSections.Standard(kind, sectionName, L("D"), L("B"), L("B1"), L("B2"), L("T"), L("T1"), L("T2"), L("T3"));
                // Shear areas from the GPC section geometry (user decision C2; Straus7 no longer derives them).
                string key = kind + "/" + string.Join(",", new[] { "D", "B", "B1", "B2", "T", "T1", "T2", "T3" }.Select(f => s.Double(f).ToString("R"))) + "/" + material.Ni.ToString("R");
                if (!shearAreas.TryGetValue(key, out var areas)) shearAreas.Add(key, areas = GPC.Converter.SectionShearAreas.Compute(section, material.Ni));
                (shear1, shear2) = areas;
            }
            var property = new ElasticBeamProperty(Unique(model, propertyName), material, section, shear1, shear2);
            model.BeamProperties.Add(property.Name, property);
            return (property, formulation, angle);
        }

        private static string Unique(Model model, string name)
        {
            if (!model.BeamProperties.ContainsKey(name)) return name;
            for (int k = 2; ; k++) if (!model.BeamProperties.ContainsKey(name + " #" + k)) return name + " #" + k;
        }

        private static (double stress, double strain)[] StressStrain(LegacyValue table)
        {
            if (table.IsNull) return null;
            return table.Items.Select(t => (t.Double("Item1"), t.Double("Item2"))).ToArray();
        }

        internal static string FullName(LegacyValue group)
        {
            var names = new List<string>();
            for (var g = group; !g.IsNull; g = g["Parent"]) names.Insert(0, g.String("Name"));
            return string.Join("\\", names);
        }

        internal static Point3d Point(LegacyValue p, BridgeUnits u) => new Point3d(u.ToMm(p.Double("X")), u.ToMm(p.Double("Y")), u.ToMm(p.Double("Z")));
        internal static Vector3d Vector(LegacyValue p) => new Vector3d(p.Double("X"), p.Double("Y"), p.Double("Z"));
        private static bool SamePlane(LegacyValue a, LegacyValue b) =>
            new[] { "m_origin", "m_xaxis", "m_yaxis", "m_zaxis" }.All(f => Vector(a[f]).Equals(Vector(b[f])));
        private static CoordinateSystem Frame(LegacyValue plane, BridgeUnits u)
        {
            var o = Point(plane["m_origin"], u);
            return new CoordinateSystem(o, Vector(plane["m_xaxis"]), Vector(plane["m_yaxis"]));
        }
        private static bool IsGlobal(LegacyValue cs) =>
            Vector(cs["Origin"]).Equals(new Vector3d(0, 0, 0)) && Vector(cs["Point1"]).Equals(new Vector3d(1, 0, 0)) && Vector(cs["Point2"]).Equals(new Vector3d(0, 1, 0));

        /// <summary>Former StrausNodeIndex: among the nodes within the tolerance the lowest by (X, Y, Z), then by number.</summary>
        private sealed class NodeLookup
        {
            private readonly List<NodeElement> nodes; private readonly double tolerance; private readonly Dictionary<(long, long, long), List<int>> cells = new Dictionary<(long, long, long), List<int>>();
            internal NodeLookup(List<NodeElement> nodes, double tolerance)
            {
                if (!(tolerance > 0)) throw new ArgumentOutOfRangeException(nameof(tolerance));
                this.nodes = nodes; this.tolerance = tolerance;
                for (int i = 0; i < nodes.Count; i++) { var key = Cell(nodes[i].Position); if (!cells.TryGetValue(key, out var list)) cells.Add(key, list = new List<int>()); list.Add(i); }
            }
            private (long, long, long) Cell(Point3d p) => ((long)Math.Floor(p.X / tolerance), (long)Math.Floor(p.Y / tolerance), (long)Math.Floor(p.Z / tolerance));
            internal (int Index, int Matches) Find(Point3d p)
            {
                var c = Cell(p); int found = -1, matches = 0;
                for (long x = c.Item1 - 1; x <= c.Item1 + 1; x++) for (long y = c.Item2 - 1; y <= c.Item2 + 1; y++) for (long z = c.Item3 - 1; z <= c.Item3 + 1; z++)
                    {
                        if (!cells.TryGetValue((x, y, z), out var list)) continue;
                        foreach (int i in list)
                        {
                            var q = nodes[i].Position; double dx = q.X - p.X, dy = q.Y - p.Y, dz = q.Z - p.Z;
                            if (Math.Sqrt(dx * dx + dy * dy + dz * dz) >= tolerance) continue;
                            matches++;
                            int order = found < 0 ? -1 : Compare(q, nodes[found].Position);
                            if (order < 0 || order == 0 && i < found) found = i;
                        }
                    }
                if (found < 0) throw new InvalidOperationException($"Nessun nodo FEM entro tolleranza alle coordinate {p}.");
                return (found, matches);
            }
            private static int Compare(Point3d a, Point3d b) { int r = a.X.CompareTo(b.X); if (r != 0) return r; r = a.Y.CompareTo(b.Y); return r != 0 ? r : a.Z.CompareTo(b.Z); }
        }
    }
}
