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
        /// <summary>Resolved native Straus7 sections (mm) of the beams with a former native section, for the Converter write plan.</summary>
        public Dictionary<Guid, GPC.Converter.Straus7.Straus7SectionDefinition> NativeSections { get; } = new Dictionary<Guid, GPC.Converter.Straus7.Straus7SectionDefinition>();
        /// <summary>F6 chain C: the definition units of the converted model (BridGHe data; the GPC model is N-mm).</summary>
        public BridgeUnits Units { get; internal set; }
        /// <summary>F6 chain C: the BridGHe angle (degrees) of every converted beam by its GPC number, the principal frame of its actions
        /// (<see cref="GpcBeamAxes.Principal"/>), as the results catalog of an analysis keeps it.</summary>
        public Dictionary<int, double> BeamAngles { get; } = new Dictionary<int, double>();
        /// <summary>F6 chain D: how many of the first cases of the conversion were registered in the cases of the former model (the others
        /// come from element loads, combinations, stages, moving paths, or are the default "Dead" case).</summary>
        public int RegisteredCases { get; internal set; }
        /// <summary>F6 chain D: the owner of the element identities of the conversion (its model GUID; a transformed model keeps it with the
        /// element identities and derives from it the identities of its new moving paths, as the conversion of its FEM output would).</summary>
        public Guid Owner { get; internal set; }
        /// <summary>F6 chain D: where the former model referred to every case of the conversion (the legacy reader places a case at its first
        /// origin, in the order of <see cref="LegacyCaseOrigins"/>), by case name; what a model transformer needs to give its cases the order of
        /// the conversion of its FEM output.</summary>
        public Dictionary<string, LegacyCaseOrigins> CaseOrigins { get; } = new Dictionary<string, LegacyCaseOrigins>(StringComparer.Ordinal);
        /// <summary>F6 chain D: the data of every registered beam property by its name (the GPC property of that name in the model): formulation,
        /// principal angle of the section, resolved native section, digest of the former content.</summary>
        public Dictionary<string, LegacyBeamPropertyData> BeamProperties { get; } = new Dictionary<string, LegacyBeamPropertyData>(StringComparer.Ordinal);
        /// <summary>F6 chain D: the beams (GPC numbers) whose former property had the name of an earlier, different property: they got the earlier
        /// one, as with the former exporter ("The beam property ... already exists"); their own content is not in the conversion.</summary>
        public HashSet<int> ShadowedPropertyBeams { get; } = new HashSet<int>();
        /// <summary>F6 chain D (Construction Sequence): the load cases of the former loads of every element, by element, in the order of the
        /// elements of the former model (the order in which the legacy reader meets the cases of the element loads); a transformer that keeps
        /// a subset of the elements orders the cases of its output from it.</summary>
        public List<LegacyElementCases> ElementLoadCases { get; } = new List<LegacyElementCases>();
        /// <summary>F6 chain D (Construction Sequence): the warnings of <see cref="Warnings"/> that concern one element (also in
        /// <see cref="Warnings"/>), so that a transformer that removes or renumbers elements gives the warnings of the conversion of its output.</summary>
        public List<LegacyElementWarning> ElementWarnings { get; } = new List<LegacyElementWarning>();
        /// <summary>F6 (L12, user decision of 5/11): the bridge data of the converted model (member metadata, super element, local section
        /// state of every beam, by beam GUID), outside the generic GPC model: <c>new BridgeModel(model, Bridge)</c>. Never null.</summary>
        public GPC.Checkers.CompositeBridge.BridgeModelData Bridge { get; internal set; } = new GPC.Checkers.CompositeBridge.BridgeModelData();

        /// <summary>A warning about one element: in <see cref="Warnings"/> and <see cref="ElementWarnings"/>.</summary>
        internal void Warn(GPC.Model.Core.Identity.EntityFamily family, int number, string text, bool numberInText = false, bool restraint = false)
        {
            Warnings.Add(text);
            string format = text.Replace("{", "{{").Replace("}", "}}");
            if (numberInText)
            {
                string suffix = number.ToString(System.Globalization.CultureInfo.InvariantCulture);
                if (!text.EndsWith(suffix, StringComparison.Ordinal)) throw new InvalidOperationException("LegacyElementWarningWithoutNumber: " + text);
                format = text.Substring(0, text.Length - suffix.Length).Replace("{", "{{").Replace("}", "}}") + "{0}";
            }
            ElementWarnings.Add(new LegacyElementWarning(family, number, format, restraint));
        }

        /// <summary>A copy of this report for a model transformed from the converted one (same element identities).</summary>
        internal LegacyImportReport CopyFor(int registeredCases)
        {
            var copy = new LegacyImportReport { Units = Units, RegisteredCases = registeredCases, Owner = Owner, Bridge = Bridge.Copy() };
            copy.Warnings.AddRange(Warnings);
            copy.ElementLoadCases.AddRange(ElementLoadCases);
            copy.ElementWarnings.AddRange(ElementWarnings);
            foreach (var p in NotExported) copy.NotExported.Add(p.Key, p.Value);
            foreach (var p in NativeSections) copy.NativeSections.Add(p.Key, p.Value);
            foreach (var p in BeamAngles) copy.BeamAngles.Add(p.Key, p.Value);
            foreach (var p in CaseOrigins) copy.CaseOrigins.Add(p.Key, p.Value);
            foreach (var p in BeamProperties) copy.BeamProperties.Add(p.Key, p.Value);
            copy.ShadowedPropertyBeams.UnionWith(ShadowedPropertyBeams);
            return copy;
        }
        internal void Carry(string what) { NotExported.TryGetValue(what, out int n); NotExported[what] = n + 1; }
        internal const string ExistingPropertyWarning = "The beam property {0} already exists";
    }

    /// <summary>F6 chain D: the origins of a case in the former model, in the order the legacy reader takes them: registered in the cases of
    /// the model (without the cases generated by a former moving analysis), element loads, combinations, stage increments, moving paths, and
    /// the default "Dead" self-weight case.</summary>
    [Flags]
    public enum LegacyCaseOrigins { None = 0, Registered = 1, ElementLoad = 2, Combination = 4, Stage = 8, Moving = 16, DefaultDead = 32 }

    /// <summary>F6 chain D: what the conversion of a former beam property gives besides the GPC property: the formulation (a datum of the
    /// element in GPC), the principal angle of the section (as <see cref="GpcBeamAxes.Section"/> takes it), the resolved native Straus7 section (or
    /// null) and the SHA-256 digest of the former content (the former exporter kept the first property of a name).</summary>
    public sealed record LegacyBeamPropertyData(BeamFormulation Formulation, double Angle, GPC.Converter.Straus7.Straus7SectionDefinition Native, string Signature);

    /// <summary>F6 chain D: the load cases of the former loads of one element (GPC family and number), in load order.</summary>
    public sealed record LegacyElementCases(GPC.Model.Core.Identity.EntityFamily Family, int Number, IReadOnlyList<string> Cases);

    /// <summary>F6 chain D: a warning of the conversion about one element (GPC family and number): its text is
    /// <c>string.Format(Format, number)</c> (the number in the text when the warning names it); <see cref="Restraint"/>: a warning about the
    /// restraints of a node (it goes with them).</summary>
    public sealed record LegacyElementWarning(GPC.Model.Core.Identity.EntityFamily Family, int Number, string Format, bool Restraint)
    {
        public string Text(int number) => string.Format(System.Globalization.CultureInfo.InvariantCulture, Format, number);
    }

    /// <summary>F6 chain D: a former beam property converted on its own (<see cref="LegacyModelImport.BeamProperty"/>).</summary>
    public sealed record LegacyBeamProperty(BeamProperty Property, LegacyBeamPropertyData Data);

    /// <summary>Chain E (d): the GPC draft of one former beam value (<see cref="LegacyModelImport.BeamDraft"/>): the beam (no nodes, definition
    /// units), its bridge data (null: none), its BridGHe angle (degrees), the data of its property (formulation, principal angle, native section,
    /// signature) and the report of the conversion (warnings, data carried but not converted).</summary>
    public sealed record LegacyBeamDraft(BeamElement Beam, GPC.Checkers.CompositeBridge.BridgeBeamData Bridge, double AngleDeg, LegacyBeamPropertyData Property, LegacyImportReport Report);

    /// <summary>Converts a former BridGHe FEM model (BGHA1 archive, read as data by <see cref="LegacyArchive"/>) into a GPC
    /// model in N-mm with the semantics of the former Straus7 exporter: node order and numbering, node merging by tolerance
    /// at the beam and link ends, load-case set with the default "Dead" self-weight case, one group per element.
    /// Analysis results and caches of the former model are not converted (they are recomputed).</summary>
    public static class LegacyModelImport
    {
        public const string Program = "BridGHe";

        /// <param name="femPropertySignature">F6 (L11): the former section-state signature (<c>BridgeSectionAssignment.Signature</c>) of the
        /// property of the former beam k (0-based), when the former model is at hand (<see cref="GpcModels"/>); a section state whose
        /// signature differs is stale (the property changed after Bridge State Override). Null: every state is taken as current.</param>
        public static Model Import(LegacyValue legacy, Guid owner, double toleranceDefinitionUnits, LegacyImportReport report, Func<int, string> femPropertySignature = null)
        {
            if (legacy == null || !legacy.Is("ModelModel")) throw new ArgumentException("LegacyModelModelExpected");
            report ??= new LegacyImportReport();
            var units = new BridgeUnits(BridgeUnits.Parse(legacy.Enum("Units")));
            report.Units = units; report.Owner = owner; report.Bridge = new GPC.Checkers.CompositeBridge.BridgeModelData();
            string name = legacy.String("Name") ?? "";
            var model = new Model(owner, name);
            var elements = legacy.Field("Elements").Items;
            var nodes = elements.Where(e => e.Is("NodeModel")).ToList();
            var beams = elements.Where(e => e.Is("BeamModel")).ToList();
            var plates = elements.Where(e => e.Is("PlateModel")).ToList();
            var others = elements.Where(e => !e.Is("NodeModel") && !e.Is("BeamModel") && !e.Is("PlateModel")).ToList();
            if (others.Count != 0) throw new NotSupportedException("LegacyElementTypeNotMappedYet: " + string.Join(", ", others.Select(o => o.ShortType).Distinct()));
            // F6 chain D: the cases of the former loads of every element, in the order of the former elements (numbers as below).
            int nodeCount = 0, beamCount = 0, plateCount = 0;
            foreach (var e in elements)
            {
                var family = e.Is("NodeModel") ? GPC.Model.Core.Identity.EntityFamily.Node : e.Is("BeamModel") ? GPC.Model.Core.Identity.EntityFamily.Beam : GPC.Model.Core.Identity.EntityFamily.Shell;
                int number = family == GPC.Model.Core.Identity.EntityFamily.Node ? ++nodeCount : family == GPC.Model.Core.Identity.EntityFamily.Beam ? ++beamCount : ++plateCount;
                report.ElementLoadCases.Add(new LegacyElementCases(family, number, e.Field("Loads").Items.Select(l => l.Field("LoadCase").String("Name")).ToArray()));
            }
            string movingJson = legacy.Has("MovingPaths") ? legacy.String("MovingPaths") : "[]";
            if (legacy.Has("BridgeWorkflow")) report.Carry("BridgeWorkflow (checks recipe, not exported)");

            var cases = Cases(legacy, model, MovingCaseNames(movingJson), report);
            var groups = new Dictionary<string, Group>(StringComparer.Ordinal);
            Group GroupOf(LegacyValue g)
            {
                string full = FullName(g);
                if (groups.TryGetValue(full, out var known)) return known;
                var parent = g["Parent"].IsNull ? null : GroupOf(g["Parent"]);
                var group = Group.Create(GpcIdentity.Group(full), full);
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
            var properties = new Dictionary<string, (BeamProperty Property, BeamFormulation Formulation, double Angle, string Signature, GPC.Converter.Straus7.Straus7SectionDefinition Native)>(StringComparer.Ordinal);
            var shearAreas = new Dictionary<string, (double, double)>(StringComparer.Ordinal);
            var members = new BridgeMembers(report.Bridge);
            for (int k = 0; k < beams.Count; k++)
            {
                var b = beams[k]; string beamId = b.String("BeamId") ?? "";
                var from = Point(b["PointFrom"], units); var to = Point(b["PointTo"], units);
                var (i, mi) = index.Find(from); var (j, mj) = index.Find(to);
                if (mi > 1) report.Warn(GPC.Model.Core.Identity.EntityFamily.Beam, k + 1, $"Collapsing {mi} nodes at coordinates {from}");
                if (mj > 1) report.Warn(GPC.Model.Core.Identity.EntityFamily.Beam, k + 1, $"Collapsing {mj} nodes at coordinates {to}");
                string propertyName = PropertyName(b["BeamProperty"]); string signature = Signature(b["BeamProperty"]);
                if (!properties.TryGetValue(propertyName, out var p))
                {
                    var converted = BeamProperty(b["BeamProperty"], Unique(model, propertyName), units, shearAreas, report, signature);
                    model.BeamProperties.Add(converted.Property.Name, converted.Property);
                    report.BeamProperties[propertyName] = converted.Data;
                    properties.Add(propertyName, p = (converted.Property, converted.Data.Formulation, converted.Data.Angle, signature, converted.Data.Native));
                }
                else if (p.Signature != signature) { report.Warnings.Add(string.Format(LegacyImportReport.ExistingPropertyWarning, propertyName)); report.ShadowedPropertyBeams.Add(k + 1); }
                var beam = Beam(b, k, GpcIdentity.Derive(owner, "beam:" + k), owner, gpcNodes[i].Position, gpcNodes[j].Position, p.Property, p.Formulation, p.Angle,
                    created => created.ConnectNodes(gpcNodes[i], gpcNodes[j]), GroupOf, cases, units, report, members, femPropertySignature);
                if (p.Native != null) report.NativeSections.Add(beam.Guid, p.Native);
                model.BeamElements.Add(beam);
            }

            if (movingJson != "[]") GpcMoving.FromLegacyJson(movingJson, model, cases, units, owner);
            // Plates: former order and numbering, nodes by tolerance; one property per name as the former exporter.
            var plateProperties = new Dictionary<string, PlateProperty>(StringComparer.Ordinal);
            for (int k = 0; k < plates.Count; k++)
            {
                var pl = plates[k]; string plateId = pl.String("PlateId") ?? "";
                int count = pl.Int("NPoints"); if (count != 3 && count != 4) throw new NotSupportedException("LegacyPlateWithMoreThanFourNodes");
                var connected = Enumerable.Range(0, count).Select(i => { var (n, m) = index.Find(Point(pl["Point" + i], units)); if (m > 1) report.Warn(GPC.Model.Core.Identity.EntityFamily.Shell, k + 1, $"Collapsing {m} nodes at plate {k + 1}", numberInText: true); return gpcNodes[n]; }).ToArray();
                var pp = pl.Field("PlateProperty"); string ppName = pp.Any("Name", "_name").Text ?? pp.Any("Id", "_id").Text ?? "Plate property";
                if (!plateProperties.TryGetValue(ppName, out var property)) plateProperties.Add(ppName, property = PlateProperty(pp, ppName, units));
                if (!model.PlateProperties.ContainsKey(property.Name)) model.PlateProperties.Add(property.Name, property);
                var plate = new AreaElement(GpcIdentity.Derive(owner, "plate:" + k), null, property, null, plateId, k + 1)
                { Source = new SourceIdentity(Program, owner.ToString("D"), EntityFamily.Shell, plateId.Length != 0 ? plateId : (k + 1).ToString()) };
                plate.ConnectNodes(connected);
                if (pl.Double("Angle") != 0) throw new NotSupportedException("LegacyPlateAngleNotMappedYet");
                if (pl.Double("Offset") != 0) plate.Assignments.Offset = units.ToMm(pl.Double("Offset"));
                var pg = pl.Field("Groups").Items; if (pg.Count > 0) plate.AddGroup(GroupOf(pg[0]));
                if (pg.Count > 1) report.Warn(GPC.Model.Core.Identity.EntityFamily.Shell, k + 1, $"Straus7 allows assigning only one single group to the plate {k + 1}", numberInText: true);
                foreach (var a in pl.Field("Attributes").Items)
                {
                    if (a.Is("ElementIdAttributeModel")) Label(plate, a, report);
                    else if (a.Is("LoadPatchTypeAttributeModel"))
                    {
                        var edges = new List<int>(); for (int e = 0; a.Has("SelectedEdges" + e); e++) edges.Add(a.Int("SelectedEdges" + e));
                        var weights = new double[4]; for (int e = 0; e < 4 && a.Has("Weights" + e); e++) weights[e] = a.Double("Weights" + e);
                        (plate.Assignments.Physical ??= new ShellPhysicalData()).LoadPatch = new LoadPatchDistribution
                        { Kind = (LoadPatchDistributionKind)(int)Enum.Parse(typeof(PatchKinds), a.Enum("PatchType")), SelectedEdges = edges.ToArray(), EdgeWeights = weights };
                    }
                    else if (a.Is("PlateSupportAttributeModel"))
                    {
                        // Former exporter: the -Z face, in every load-case number used as freedom case (one freedom case exists).
                        (plate.Assignments.Physical ??= new ShellPhysicalData()).Supports.Add(new ShellDistributedSupport
                        {
                            Stiffness = a.Double("Stiffness") * units.Force / (units.Length * units.Length * units.Length), Gap = 0, Face = ShellFace.NegativeNormal,
                            Behavior = a.Bool("CompressionOnly") ? DistributedSpringBehavior.CompressionOnly : DistributedSpringBehavior.Bilateral
                        });
                        report.Carry("Plate support face " + a.Enum("Face") + " (the former exporter wrote the -Z face)");
                    }
                    else throw new NotSupportedException("LegacyPlateAttributeNotMappedYet: " + a.ShortType);
                }
                foreach (var l in pl.Field("Loads").Items)
                {
                    var loadCase = cases[l.Field("LoadCase").String("Name")];
                    if (l.Is("PlatePreStressModel"))
                    {
                        var stress = Vector(l["LocalPressure"]);
                        (plate.Assignments.Physical ??= new ShellPhysicalData()).InitialStresses.Add(new ShellInitialStress { Case = loadCase, Components = new Vector3d(units.ToMPa(stress.X), units.ToMPa(stress.Y), units.ToMPa(stress.Z)) });
                        continue;
                    }
                    if (l.Is("PlateNSMassModel"))
                    {
                        if (!units.IsIdentity) throw new NotSupportedException("LegacyPlateNonStructuralMassUnitsRequireNmm");
                        (plate.Assignments.Physical ??= new ShellPhysicalData()).NonStructuralMasses.Add(new ShellNonStructuralMass { Case = loadCase, MassPerArea = l.Double("Mass"), DynamicFactor = l.Double("DynFactor"), GlobalOffset = Vector(l["Offset"]) });
                        continue;
                    }
                    if (!l.Is("PlatePressureModel")) throw new NotSupportedException("LegacyPlateLoadNotMappedYet: " + l.ShortType);
                    var pressure = Vector(l["Pressure"]);
                    // Review L1 B2: local pressure = St7SetPlateNormalPressure2 with one value (the -Z face); global = St7SetPlateGlobalPressure3S.
                    plate.Loads.Add(l["CoordinateSystem"].IsNull
                        ? ShellFacePressure.Normal(loadCase, ShellFace.NegativeNormal, units.ToMPa(pressure.Z))
                        : ShellFacePressure.Global(loadCase, l.Enum("Face") == "Top" ? ShellFace.PositiveNormal : ShellFace.NegativeNormal,
                            new Vector3d(units.ToMPa(pressure.X), units.ToMPa(pressure.Y), units.ToMPa(pressure.Z)),
                            l.Bool("Projected") ? SurfacePressureProjection.Resultant : SurfacePressureProjection.None));
                }
                model.AreaElements.Add(plate);
            }

            var stages = legacy.Has("StagedConstruction") ? legacy.Field("StagedConstruction").Items : Array.Empty<LegacyValue>();
            if (stages.Count != 0) Stages(stages, model, cases, groups, report);
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

        /// <summary>
        /// One former beam as a GPC beam (the body of the beam loop of <see cref="Import"/>, chain E (d): also the conversion of a single beam
        /// value, <see cref="BeamDraft"/>): on the given ends and property, connected by <paramref name="connect"/> right after its creation;
        /// formulation, section axes from the BridGHe angle and the principal angle of the property, offsets in the principal axes, the first
        /// group, attributes, loads in its assignments, physical data, at least 5 output stations, the local section state.
        /// </summary>
        private static BeamElement Beam(LegacyValue b, int k, Guid guid, Guid owner, Point3d start, Point3d end, BeamProperty property, BeamFormulation formulation,
            double principalAngle, Action<BeamElement> connect, Func<LegacyValue, Group> groupOf, Dictionary<string, LoadCaseBase> cases, BridgeUnits units,
            LegacyImportReport report, BridgeMembers members, Func<int, string> femPropertySignature)
        {
            string beamId = b.String("BeamId") ?? "";
            var beam = new BeamElement(guid, start, end, property, null, beamId, k + 1)
            { Source = new SourceIdentity(Program, owner.ToString("D"), EntityFamily.Beam, beamId.Length != 0 ? beamId : (k + 1).ToString()) };
            connect?.Invoke(beam);
            double angleDeg = b.Double("Angle");
            report.BeamAngles[k + 1] = angleDeg;
            var section = GpcBeamAxes.Section(start, end, angleDeg, principalAngle);
            var principal = GpcBeamAxes.Principal(start, end, angleDeg);
            beam.Assignments.Formulation = formulation;
            beam.Assignments.SectionAxes = section;
            var offset = b["Offset"]; double ox = units.ToMm(offset.Double("X")), oy = units.ToMm(offset.Double("Y"));
            var globalOffset = principal.V1 * ox + principal.V2 * oy;
            if (ox != 0 || oy != 0)
            {
                beam.Assignments.OffsetAxes = principal;
                beam.Assignments.OffsetI = new Vector3d(ox, oy, 0); beam.Assignments.OffsetJ = new Vector3d(ox, oy, 0);
            }
            var bg = b.Field("Groups").Items;
            if (bg.Count > 0) beam.AddGroup(groupOf(bg[0]));
            if (bg.Count > 1) report.Warn(GPC.Model.Core.Identity.EntityFamily.Beam, k + 1, $"Straus7 allows assigning only one single group to the beam {k + 1}", numberInText: true);
            BeamData(b, beam, principal, globalOffset, cases, units, report, members);
            // The former result reader asked Straus7 for at least 5 stations per beam unless the beam had an output-stations attribute.
            (beam.Assignments.Physical ??= new BeamPhysicalData()).MinimumOutputStations ??= 5;
            SectionState(b, k, beam, femPropertySignature, report);
            return beam;
        }

        /// <summary>
        /// Chain E (d): the draft of one former beam value (a <c>GH_Beam</c> datum upstream of Build Model): the GPC beam with the rules of the
        /// conversion of a whole model (<see cref="Beam"/>), without nodes (its own ends), number 1, its property converted on its own
        /// (<see cref="BeamProperty(LegacyValue, BridgeUnits)"/>: name as given), its groups by full name (<see cref="GpcIdentity.Group"/>, with
        /// their parents), the cases of its loads by name (<see cref="GpcIdentity.Case"/>, as the cases of a whole model), and its bridge data
        /// (member, super element, local section state; L12) in <see cref="LegacyBeamDraft.Bridge"/>. Dimensions in the definition
        /// <paramref name="units"/> (identity for a draft: Build Model normalises). <paramref name="femPropertySignature"/>: the former section-state
        /// signature of its property (a state whose signature differs is stale); null: a state is current.
        /// </summary>
        public static LegacyBeamDraft BeamDraft(LegacyValue b, BridgeUnits units, Guid guid, string femPropertySignature = null)
        {
            if (b == null || b.IsNull || !b.Is("BeamModel")) throw new ArgumentException("LegacyBeamModelExpected");
            var report = new LegacyImportReport { Units = units, Owner = guid, Bridge = new GPC.Checkers.CompositeBridge.BridgeModelData() };
            var property = BeamProperty(b["BeamProperty"], units);
            var groups = new Dictionary<string, Group>(StringComparer.Ordinal);
            Group GroupOf(LegacyValue g)
            {
                string full = FullName(g);
                if (groups.TryGetValue(full, out var known)) return known;
                var parent = g["Parent"].IsNull ? null : GroupOf(g["Parent"]);
                var group = Group.Create(GpcIdentity.Group(full), full);
                parent?.AddChild(group);
                groups.Add(full, group);
                return group;
            }
            var first = new Dictionary<string, LegacyValue>(StringComparer.Ordinal);
            foreach (var c in b.Field("Loads").Items.Select(l => l.Field("LoadCase")))
            {
                string name = c.String("Name");
                if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Caso di carico nullo o senza nome.");
                if (!first.TryGetValue(name, out var known)) first.Add(name, c);
                else if (!SameCase(known, c)) throw new ArgumentException("Definizioni incompatibili per il caso di carico " + name);
            }
            var cases = first.ToDictionary(pair => pair.Key, pair => Case(pair.Key, pair.Value), StringComparer.Ordinal);
            var beam = Beam(b, 0, guid, guid, Point(b["PointFrom"], units), Point(b["PointTo"], units), property.Property, property.Data.Formulation, property.Data.Angle,
                null, GroupOf, cases, units, report, new BridgeMembers(report.Bridge), femPropertySignature == null ? null : _ => femPropertySignature);
            return new LegacyBeamDraft(beam, report.Bridge.Beam(beam.Guid), b.Double("Angle"), property.Data, report);
        }

        /// <summary>The GPC case of a former load case (null: the default "Dead" case): self weight with gravity, the masses of its inertia
        /// loads; the identity by name. Model loads (gravity) are added by the caller.</summary>
        private static LoadCaseBase Case(string name, LegacyValue c)
        {
            bool dead = c == null && name == "Dead";
            bool gravity = dead || c != null && c.Enum("GlobalInertiaLoad") == "Gravity";
            if (c != null && c.Enum("GlobalInertiaLoad") != "Gravity" && c.Enum("GlobalInertiaLoad") != "None")
                throw new NotSupportedException("LegacyInertiaLoadNotMappedYet: " + c.Enum("GlobalInertiaLoad"));
            LoadCaseBase gpc = gravity ? new LoadCase(GpcIdentity.Case(name), name, LoadCase.LoadCaseTypes.SelfWeight) : new LoadCaseBase(GpcIdentity.Case(name), name);
            bool structural = dead || c != null && c.Bool("StructuralMassAcceleration"), nonStructural = c != null && c.Bool("NonStructuralMassAcceleration");
            gpc.InertiaMasses = new InertiaMassSelection { Structural = structural, NonStructural = nonStructural };
            return gpc;
        }

        /// <summary>The former case set: model cases (not generated by moving loads), the cases of element loads and of the
        /// combinations, grouped by name; the default "Dead" case (self weight along −Z, 9806.65 mm/s², structural mass);
        /// numbering by name with Dead first (the writer numbers by ordinal name).</summary>
        private static IEnumerable<string> MovingCaseNames(string json)
        {
            using var document = System.Text.Json.JsonDocument.Parse(json);
            return document.RootElement.EnumerateArray().Select(p => p.GetProperty("LoadCaseName").GetString()).ToArray();
        }

        private static Dictionary<string, LoadCaseBase> Cases(LegacyValue legacy, Model model, IEnumerable<string> movingCases, LegacyImportReport report)
        {
            // Static cases generated by a former moving-load analysis are results (recomputed): their names are not input cases.
            var generated = new HashSet<string>(StringComparer.Ordinal);
            if (legacy.Has("GeneratedMovingCases") && legacy.String("GeneratedMovingCases") != "{}")
            {
                using var document = System.Text.Json.JsonDocument.Parse(legacy.String("GeneratedMovingCases"));
                foreach (var item in document.RootElement.EnumerateObject()) generated.Add(item.Value.GetString());
                report.Carry("Generated moving cases of a former analysis (cache, recomputed): " + generated.Count);
            }
            var candidates = legacy.Field("LoadCases").Items.Where(c => !generated.Contains(c.String("Name")))
                .Concat(legacy.Field("Elements").Items.SelectMany(e => e.Field("Loads").Items).Select(l => l.Field("LoadCase")))
                .Concat(legacy.Field("LoadCombinations").Items.SelectMany(c => c.Field("Keys").Items))
                .Concat((legacy.Has("StagedConstruction") ? legacy.Field("StagedConstruction").Items : Array.Empty<LegacyValue>())
                    .SelectMany(st => st.Field("_loadCombinations").Items).SelectMany(c => c.Field("Keys").Items)).ToList();
            var result = new Dictionary<string, LoadCaseBase>(StringComparer.Ordinal);
            var first = new Dictionary<string, LegacyValue>(StringComparer.Ordinal);
            foreach (var c in candidates)
            {
                string name = c.String("Name");
                if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Caso di carico nullo o senza nome.");
                if (!first.TryGetValue(name, out var known)) first.Add(name, c);
                else if (!SameCase(known, c)) throw new ArgumentException("Definizioni incompatibili per il caso di carico " + name);
            }
            // F6 chain D: the cases registered in the former model come first (its LoadCases, without generated moving cases).
            report.RegisteredCases = legacy.Field("LoadCases").Items.Select(c => c.String("Name")).Where(n => !generated.Contains(n)).Distinct(StringComparer.Ordinal).Count();
            // F6 chain D: every origin of every case, for the model transformers.
            var origins = report.CaseOrigins; origins.Clear();
            void Origin(string name, LegacyCaseOrigins origin) { origins.TryGetValue(name, out var o); origins[name] = o | origin; }
            foreach (var c in legacy.Field("LoadCases").Items.Where(c => !generated.Contains(c.String("Name")))) Origin(c.String("Name"), LegacyCaseOrigins.Registered);
            foreach (var l in legacy.Field("Elements").Items.SelectMany(e => e.Field("Loads").Items)) Origin(l.Field("LoadCase").String("Name"), LegacyCaseOrigins.ElementLoad);
            foreach (var k in legacy.Field("LoadCombinations").Items.SelectMany(c => c.Field("Keys").Items)) Origin(k.String("Name"), LegacyCaseOrigins.Combination);
            foreach (var k in (legacy.Has("StagedConstruction") ? legacy.Field("StagedConstruction").Items : Array.Empty<LegacyValue>())
                .SelectMany(st => st.Field("_loadCombinations").Items).SelectMany(c => c.Field("Keys").Items)) Origin(k.String("Name"), LegacyCaseOrigins.Stage);
            foreach (var name in movingCases) Origin(name, LegacyCaseOrigins.Moving);
            foreach (var name in movingCases) if (!first.ContainsKey(name)) first.Add(name, null);
            if (!first.ContainsKey("Dead")) { first.Add("Dead", null); Origin("Dead", LegacyCaseOrigins.DefaultDead); }
            foreach (var pair in first)
            {
                var c = pair.Value;
                bool dead = c == null && pair.Key == "Dead";
                bool gravity = dead || c != null && c.Enum("GlobalInertiaLoad") == "Gravity";
                var gpc = Case(pair.Key, c);
                if (c != null && (c.Double("Temperature") != 0 || c.Has("Stages") && c.Field("Stages").Items.Count != 0)) report.Carry("Load case temperature or stage list (not exported by the former writer)");
                model.LoadCases.Add(gpc.Name, gpc);
                if (gravity)
                {
                    // Gravity value in mm/s², already N-mm in the former model (Dead: −9806.65 along Z).
                    double value = dead ? -9806.65 : c.Double("GravityValue");
                    string direction = dead ? "Z" : c.Enum("GravityDirection");
                    var axis = direction == "X" ? Vector3d.XAxis : direction == "Y" ? Vector3d.YAxis : direction == "Z" ? Vector3d.ZAxis : throw new NotSupportedException("LegacyGravityDirection: " + direction);
                    if (value != 0) model.ModelLoads.Add(new ModelGravityLoad(axis, value, gpc, CoordinateSystem.Global, "Gravity " + pair.Key));
                }
                result.Add(pair.Key, gpc);
            }
            return result;
        }

        /// <summary>Former staged construction: one native stage per definition, in order; the listed groups are the complete set of
        /// active groups (the others are disabled); Morph = birth on deformed coordinates, Rotate = rotate incoming clusters;
        /// each stage combination is one load increment. Legacy stage groups without elements did not exist natively and are ignored.</summary>
        private static void Stages(IReadOnlyList<LegacyValue> stages, Model model, Dictionary<string, LoadCaseBase> cases, Dictionary<string, Group> groups, LegacyImportReport report)
        {
            var freedom = new GPC.Model.FreedomCases.FreedomCase(GpcIdentity.FreedomCase("Default restraints"), "Default restraints");
            model.FreedomCases.Add(freedom.Name, freedom);
            for (int k = 0; k < stages.Count; k++)
            {
                // StagedConstructionModel is [Serializable] without ISerializable: field names.
                var st = stages[k]; string name = st.String("_name");
                var stage = new GPC.Model.Stages.Stage(GpcIdentity.Stage(name), k + 1, name) { Definition = new GPC.Model.Stages.StageDefinition(k + 1, 0) };
                var active = new List<Guid>();
                foreach (var g in st.Field("_groups").Items)
                {
                    if (groups.TryGetValue(FullName(g), out var group)) { if (!active.Contains(group.Guid)) active.Add(group.Guid); }
                    else report.Carry("Stage group without elements (not created natively by the former exporter)");
                }
                stage.AnalysisState = new GPC.Model.Stages.StageAnalysisState
                {
                    BirthGeometry = st.Bool("_morph") ? GPC.Model.Stages.StageBirthGeometry.DeformedCoordinates : GPC.Model.Stages.StageBirthGeometry.OriginalCoordinates,
                    MoveRestrainedNodes = false, RotateIncomingClusters = st.Bool("_rotate"),
                    ActiveGroupGuids = active.ToArray(), ActiveFreedomCaseGuids = new[] { freedom.Guid },
                    Increments = st.Field("_loadCombinations").Items.Select(c =>
                    {
                        var keys = c.Field("Keys").Items; var values = c.Field("Values").Items;
                        return new GPC.Model.Stages.StageLoadIncrement
                        {
                            Name = c.String("Name"),
                            LoadFactors = keys.Select((key, q) => new GPC.Model.Stages.StageCaseFactor { CaseGuid = cases[key.String("Name")].Guid, Factor = values[q].AsDouble() }).ToArray(),
                        };
                    }).ToArray(),
                };
                model.Stages.Add(stage);
            }
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
            // Former exporter: the first stiffness attribute, in the restraint UCS of freedom case 1 (St7SetNodeKTranslation3F/KRotation3F).
            var stiffness = attributes.FirstOrDefault(a => a.Is("NodeStiffnessAttributeModel"));
            if (attributes.Count(a => a.Is("NodeStiffnessAttributeModel")) > 1) report.Warn(GPC.Model.Core.Identity.EntityFamily.Node, node.Id, $"Node {node.Name}: only the first stiffness attribute was exported", restraint: true);
            if (restraints.Length != 0 || stiffness != null)
            {
                if (n.Field("Loads").Items.Any(l => l.Is("NodeDisplacementLoadModel"))) throw new NotSupportedException("LegacyPrescribedDisplacementNotMappedYet");
                var names = new[] { "Tx", "Ty", "Tz", "Rx", "Ry", "Rz" }; var dofs = new List<DofRestrain>();
                for (int k = 0; k < 6; k++)
                {
                    bool fixedDof = restraints.Any(r => r.Bool(names[k]));
                    double spring = stiffness == null ? 0 : stiffness.Double(names[k]) * (k < 3 ? units.Force / units.Length : units.Force * units.Length);
                    if (fixedDof && spring != 0) report.Warn(GPC.Model.Core.Identity.EntityFamily.Node, node.Id, $"Node {node.Name}: spring on a restrained DOF {names[k]} (the restraint governs)", restraint: true);
                    dofs.Add(fixedDof || spring == 0 ? new DofRestrain((GeometryRestrain.DOF)k, fixedDof) : new DofRestrain((GeometryRestrain.DOF)k, spring));
                }
                node.Assignments.Restrains.Add(new RestrainAssignment { Restrain = new NodeRestrain(node, axes ?? CoordinateSystem.Global, dofs), SourceRecord = "NodeRestraint/StiffnessAttributeModel" });
            }
            foreach (var a in attributes)
            {
                if (a.Is("NodeCoordinateAttributeModel") || a.Is("NodeRestraintAttributeModel") || a.Is("RigidConnectionAttributeModel")) continue;
                if (a.Is("ElementIdAttributeModel")) { Label(node, a, report); continue; }
                if (a.Is("NodeStiffnessAttributeModel")) continue; // with the restraints above
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
                else if (l.Is("NodeTemperatureLoadModel"))
                {
                    // The former exporter wrote only the value (St7SetNodeTemperature1): the native default type is the reference temperature.
                    if (l.Enum("LoadType") != "Reference") report.Warn(GPC.Model.Core.Identity.EntityFamily.Node, node.Id, $"Node {node.Name}: temperature type {l.Enum("LoadType")} was not exported (reference temperature used, as before)");
                    (node.Assignments.Physical ??= new NodePhysicalData()).Temperatures.Add(new NodalTemperatureAssignment { Case = cases[c], Kind = NodalTemperatureKind.Reference, Celsius = units.ToCelsius(l.Double("Value")) });
                }
                else throw new NotSupportedException("LegacyNodeLoadNotMappedYet: " + l.ShortType);
            }
            foreach (var s in sums) node.Loads.Add(new PointLoad(s.Value.F, s.Value.M, node.Position, cases[s.Key]));
        }
        private static readonly Vector3d Zero = new Vector3d(0, 0, 0);

        private static void BeamData(LegacyValue b, BeamElement beam, CoordinateSystem principal, Vector3d offset, Dictionary<string, LoadCaseBase> cases, BridgeUnits units, LegacyImportReport report,
            BridgeMembers members)
        {
            foreach (var a in b.Field("Attributes").Items)
            {
                switch (a.ShortType)
                {
                    case "ElementIdAttributeModel": Label(beam, a, report); break;
                    case "SuperElementAttributeModel": members.SuperElement(a.String("Value"), beam); break;
                    case "BridgeMemberAttributeModel": members.Member(a.Field("BridgeMetadata"), beam, units); break;
                    case "BridgeSectionStateAttributeModel": break; // after the beam is complete (SectionState)
                    case "BeamOutputStationsModel":
                        (beam.Assignments.Physical ??= new BeamPhysicalData()).MinimumOutputStations = a.Int("MinimumNumberOfStations"); break;
                    case "BeamReleasesAttributeModel":
                        if (beam.Attributes.Values.Any(x => x is GPC.Model.Attributes.BeamReleasesAttribute)) throw new ArgumentException($"Cannot assign more than 1 set of releases for beam {beam.Id}");
                        beam.Attributes.Add(Releases(a, units)); break;
                    case "BeamSupportAttributeModel":
                        (beam.Assignments.Physical ??= new BeamPhysicalData()).Supports.Add(Support(a, beam.Assignments.SectionAxes, units)); break;
                    case "BeamTaperAttributeModel":
                        if (beam.Assignments.Physical?.Taper != null) throw new ArgumentException("LegacyDuplicateBeamTaper");
                        (beam.Assignments.Physical ??= new BeamPhysicalData()).Taper = new BeamSectionTaper
                        {
                            ScaleI1 = a.Double("NFromX"), ScaleJ1 = a.Double("NToX"), ScaleI2 = a.Double("NFromY"), ScaleJ2 = a.Double("NToY"),
                            Alignment1 = TaperAlignmentOf(a.Enum("AlignmentX")), Alignment2 = TaperAlignmentOf(a.Enum("AlignmentY")),
                        };
                        break;
                    case "BeamStringGroupAttributeModel":
                        (beam.Assignments.Physical ??= new BeamPhysicalData()).StringGroup = GpcIdentity.Derive(GpcIdentity.NameSpace, "string-group:" + a.Int("ID")); break;
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
                    // The slab eigenstrain of a composite beam: a checks input (the strain of the workflow shrinkage action), not an FE load.
                    case "CompositeBeamSlabStrainModel": report.Carry("Composite slab strain (checks input: the strain of the workflow shrinkage action)"); break;
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

        /// <summary>F6 chain D: a former beam property converted on its own, with the rules of the conversion of a whole model (the model
        /// transformers that replace the property of beams, e.g. Bridge Phase Model); dimensions in the definition <paramref name="units"/>.</summary>
        public static LegacyBeamProperty BeamProperty(LegacyValue property, BridgeUnits units)
        {
            if (property == null || property.IsNull || !property.Is("BeamPropertyModel")) throw new ArgumentException("LegacyBeamPropertyModelExpected");
            return BeamProperty(property, PropertyName(property), units, new Dictionary<string, (double, double)>(StringComparer.Ordinal), new LegacyImportReport(), Signature(property));
        }

        private static string Digest(string signature) => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(signature)));

        /// <summary>The GPC material of a beam property of the former exporter (<see cref="GpcMaterials.Elastic"/>: E, ν, G with Use Poisson,
        /// density, thermal expansion, damping, conductivity, specific heat, stress-strain table), the one rule of the reader for every beam
        /// property; chain E: the steel of the analysis properties of a composite section (<c>GpcModels.Material</c>).</summary>
        public static Material ElasticMaterial(LegacyValue m)
        {
            if (m == null || m.IsNull) throw new ArgumentException("LegacyMaterialModelExpected");
            return GpcMaterials.Elastic(m.String("Name"), m.Double("Modulus"), m.Double("PoissonRatio"), m.Double("ShearModulus"), m.Bool("UsePoisson"),
                m.Double("Density"), m.Double("ThermalExpansion"), m.Double("ViscousDamping"), m.Double("DampingRatio"), m.Double("Conductivity"), m.Double("SpecificHeat"),
                StressStrain(m["StressStrainTable"]));
        }

        private static LegacyBeamProperty BeamProperty(LegacyValue p, string propertyName, BridgeUnits units, Dictionary<string, (double, double)> shearAreas, LegacyImportReport report, string signature)
        {
            var (property, formulation, angle, native) = Property(p, propertyName, units, shearAreas, report);
            return new LegacyBeamProperty(property, new LegacyBeamPropertyData(formulation, angle, native, Digest(signature)));
        }

        private static (BeamProperty, BeamFormulation, double, GPC.Converter.Straus7.Straus7SectionDefinition) Property(LegacyValue p, string propertyName, BridgeUnits units,
            Dictionary<string, (double, double)> shearAreas, LegacyImportReport report)
        {
            string type = p.Enum("BeamPropertyType");
            var formulation = type == "Beam" ? BeamFormulation.StraightTwoNode : type == "Truss" ? BeamFormulation.Truss : type == "Cable" ? BeamFormulation.Cable
                : type == "CutoffBar" ? BeamFormulation.CutoffBar : type == "PointContact" ? BeamFormulation.PointContact : type == "Pipe" ? BeamFormulation.Pipe
                : type == "Connection" ? BeamFormulation.Connection : throw new NotSupportedException("LegacyBeamPropertyType: " + type);
            if (formulation == BeamFormulation.CutoffBar) throw new NotSupportedException("LegacyCutoffBarNotMappedYet");
            var m = p.Field("Material"); var s = p.Field("SectionModel");
            if (s.Enum("MirrorType") != "None") throw new NotSupportedException("LegacyMirroredSectionNotMappedYet");
            var material = ElasticMaterial(m);
            if (!m["StressStrainTable"].IsNull && type != "Beam" && type != "Truss") material.AnalysisData.StressStrainPairs = null; // former exporter: tables only for beams and trusses
            if (!s["NativeSection"].IsNull)
            {
                // Former exporter: the native section was assigned again in the model units (standard and BGL dimensions as numbers in
                // the model length unit; BXS and library sections only when read in the model length unit); Straus7 computed the properties.
                var n = s["NativeSection"]; string nativeKey = n.String("Key");
                if (GpcNativeSections.IsLibraryOrBxs(nativeKey) && n.Int("LengthUnit") != GpcNativeSections.Straus7LengthUnit(units.Length))
                    throw new ArgumentException("Le unità del modello differiscono da quelle usate per leggere la sezione di libreria/BXS. Ricalcolare la sezione nelle unità del modello.");
                var dimensions = n["Dimensions"].IsNull ? Array.Empty<double>() : n["Dimensions"].Items.Select(v => v.AsDouble()).ToArray();
                var resolved = GpcNativeSections.Resolve(nativeKey, dimensions, n.String("BxsPath"), n.String("LibraryName"), n.String("LibraryItem"), units.Length).Section;
                var nativeProperty = resolved.ToBeamProperty(propertyName, material);
                report.Carry("Native section preview contours (display only)");
                return (nativeProperty, formulation, resolved.ReferenceProperties[10], resolved);
            }
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
                // Shear areas from the GPC section geometry (user decision C2; Straus7 no longer derives them): De Saint Venant at nu = 0
                // (user decision 4/11, public GPCModel API of L6), so they depend on the geometry only.
                string key = kind + "/" + string.Join(",", new[] { "D", "B", "B1", "B2", "T", "T1", "T2", "T3" }.Select(f => s.Double(f).ToString("R")));
                if (!shearAreas.TryGetValue(key, out var areas))
                {
                    var computed = GPC.Model.Sections.SectionShearAreas.Compute(section);
                    shearAreas.Add(key, areas = (computed.Area1, computed.Area2));
                }
                (shear1, shear2) = areas;
            }
            var property = new ElasticBeamProperty(propertyName, material, section, shear1, shear2);
            return (property, formulation, angle, null);
        }

        private enum PatchKinds { Auto1, Auto2, Auto3, Auto4, AngleSplit, Manual }

        /// <summary>The fingerprint of a section state the former property no longer matches (stale: never equal to a property fingerprint).</summary>
        public const string StaleSectionStatePrefix = "BridGHe stale section state: ";

        /// <summary>F6 (L11): the former local section state of a beam (Bridge State Override, a checks input) as the descriptive GPC
        /// <see cref="GPC.Model.Structure.Bridges.BridgeSectionStateAttribute"/> (skipped by the Straus7 writer, outside the analysis fingerprint):
        /// behaviour, concrete excluded, modular ratio, classification case (empty: none) and the fingerprint of the GPC property of the beam
        /// (<see cref="GPC.Checkers.CompositeBridge.BridgeWorkflowDefinition.PropertyFingerprint"/>, the one D3 uses) when the former state was
        /// current for its property, else a stale marker (the checks refuse it, as the former ones refused the stale signature). Only the first
        /// state of a beam is kept (Bridge State Override writes one).</summary>
        private static void SectionState(LegacyValue b, int k, BeamElement beam, Func<int, string> femPropertySignature, LegacyImportReport report)
        {
            var states = b.Field("Attributes").Items.Where(a => a.Is("BridgeSectionStateAttributeModel")).ToArray();
            if (states.Length == 0) return;
            if (states.Length > 1) report.Warn(GPC.Model.Core.Identity.EntityFamily.Beam, beam.Id, $"Beam {beam.Name}: only the first bridge section state is kept");
            var s = states[0].Field("SectionAssignment");
            if (!Enum.TryParse<GPC.Checkers.CompositeBridge.BridgeActionBehavior>(s.Enum("Behavior"), false, out var behavior) || behavior == GPC.Checkers.CompositeBridge.BridgeActionBehavior.Steel)
                throw new ArgumentException("LegacyBridgeSectionStateBehavior: " + s.Enum("Behavior"));
            string signature = s.String("PropertySignature") ?? "";
            bool current = femPropertySignature == null || femPropertySignature(k) == signature;
            string classification = s.String("ClassificationCase");
            // L12: the state is bridge data of the beam (by GUID), outside the generic model.
            report.Bridge.SetSectionState(beam.Guid, new GPC.Checkers.CompositeBridge.BridgeLocalSectionState(behavior, s.Bool("ConcreteExcluded"), s.Double("ModularRatio"),
                string.IsNullOrEmpty(classification) ? null : classification,
                current ? GPC.Checkers.CompositeBridge.BridgeWorkflowDefinition.PropertyFingerprint(beam) : StaleSectionStatePrefix + signature));
        }

        /// <summary>Former releases: per end and kind, a released DOF with zero partial value is released, with a value it is a partial
        /// (absolute) stiffness; the others stay continuous. Beam principal axes, as St7SetBeamTRelease3/RRelease3.</summary>
        /// <summary>C1: the former Element ID (ElementIdComponent: the user's identifier of a node, beam or plate) as the GPC element label,
        /// descriptive (the Straus7 writer does not write it; the former exporter numbered the native elements itself).</summary>
        private static void Label(Element element, LegacyValue a, LegacyImportReport report)
        {
            string value = a.String("Value");
            if (string.IsNullOrEmpty(value)) { report.Carry("Empty Element ID attribute"); return; }
            if (element.Attributes.Values.OfType<ElementLabelAttribute>().Any()) { report.Warn(GPC.Model.Models.Model.FamilyOf(element), element.Id, $"Element {element.Name}: only the first Element ID is kept"); return; }
            element.Attributes.Add(new ElementLabelAttribute(value));
        }

        /// <summary>
        /// C1 (L12, user decision of 5/11): the bridge identities of the former beams as bridge data of the beam (by GUID, <see cref="LegacyImportReport.Bridge"/>),
        /// never in the generic model:
        /// - bridge member metadata (BridgeMemberAttributeModel: the physical member of the FE part, its stations in mm, section up and geometry
        ///   id) → <c>SetMember</c>; one member per beam (<c>LegacyDuplicateBridgeMemberAttribute</c>); the parts of a member are its beams in
        ///   numbering order (<c>BridgeModel.PhysicalMember</c>);
        /// - super elements (SuperElementAttributeModel: the beams checked together by Super Element Check) → <c>SetSuperElement</c>; two
        ///   attributes with the same name are one super element, two names are an error (<c>LegacyBeamInTwoSuperElements</c>, lead decision of 5/11).
        /// </summary>
        private sealed class BridgeMembers
        {
            private readonly GPC.Checkers.CompositeBridge.BridgeModelData data;
            public BridgeMembers(GPC.Checkers.CompositeBridge.BridgeModelData data) { this.data = data; }
            public void SuperElement(string name, BeamElement beam)
            {
                if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("LegacyEmptySuperElementName");
                string existing = data.SuperElement(beam.Guid);
                if (existing == null) data.SetSuperElement(beam.Guid, name);
                else if (!string.Equals(existing, name, StringComparison.Ordinal))
                    throw new ArgumentException($"LegacyBeamInTwoSuperElements: beam {beam.Name} is in the super elements '{existing}' and '{name}'; a beam belongs to one super element.");
            }
            public void Member(LegacyValue m, BeamElement beam, BridgeUnits units)
            {
                string id = m.String("PhysicalId");
                if (!Enum.TryParse<GPC.Checkers.CompositeBridge.Layout.BridgeMemberFamily>(m.Enum("Family"), false, out var family)) throw new ArgumentException("LegacyBridgeMemberFamily: " + m.Enum("Family"));
                var up = m.Field("SectionUp");
                var metadata = new GPC.Checkers.CompositeBridge.Layout.BridgeMemberMetadata(id, family, m.Int("Group"), m.Int("Segment"), m.Int("Span"),
                    units.ToMm(m.Double("StationFrom")), units.ToMm(m.Double("StationTo")), new Vector3d(up.Double("X"), up.Double("Y"), up.Double("Z")), m.String("GeometryId") ?? "");
                if (data.Member(beam.Guid) != null) throw new ArgumentException("LegacyDuplicateBridgeMemberAttribute: beam " + beam.Name);
                data.SetMember(beam.Guid, metadata);
            }
        }

        private static GPC.Model.Attributes.BeamReleasesAttribute Releases(LegacyValue a, BridgeUnits units)
        {
            var release = new GPC.Model.Attributes.BeamReleasesAttribute { SourceRecord = "BeamReleasesAttributeModel" };
            void End(GPC.Model.Attributes.BeamDofConnection[] target, string trans, string rot, string transPartial, string rotPartial)
            {
                var tp = Vector(a[transPartial]); var rp = Vector(a[rotPartial]);
                var t = new[] { tp.X, tp.Y, tp.Z }; var r = new[] { rp.X, rp.Y, rp.Z };
                for (int k = 0; k < 3; k++)
                {
                    if (a.Bool(trans + k)) target[k] = t[k] == 0 ? new GPC.Model.Attributes.BeamDofConnection(GPC.Model.Attributes.BeamConnectionKind.Released)
                        : new GPC.Model.Attributes.BeamDofConnection(GPC.Model.Attributes.BeamConnectionKind.AbsoluteStiffness, t[k] * units.Force / units.Length);
                    if (a.Bool(rot + k)) target[3 + k] = r[k] == 0 ? new GPC.Model.Attributes.BeamDofConnection(GPC.Model.Attributes.BeamConnectionKind.Released)
                        : new GPC.Model.Attributes.BeamDofConnection(GPC.Model.Attributes.BeamConnectionKind.AbsoluteStiffness, r[k] * units.Force * units.Length);
                }
            }
            End(release.I, "TransNode1", "RotNode1", "TransN1Partial", "RotN1Partial");
            End(release.J, "TransNode2", "RotNode2", "TransN2Partial", "RotN2Partial");
            return release;
        }

        /// <summary>Former BeamSupportExport: simple springs along +-1/+-2 of the beam axes; tension-only flips the direction.</summary>
        private static BeamDistributedSupport Support(LegacyValue a, CoordinateSystem axes, BridgeUnits units)
        {
            if (a.Enum("SpringType") != "Simple" || a.Enum("SpringTensionOrientation") != "BeamLocalAxis")
                throw new NotSupportedException("Straus beam supports require simple springs along principal local axes 1 or 2.");
            double stiffness = a.Double("Stiffness"), gap = a.Double("Gap");
            if (!(stiffness > 0) || double.IsInfinity(stiffness) || double.IsNaN(gap) || double.IsInfinity(gap)) throw new ArgumentException("Beam support requires finite positive stiffness [F/L2] and finite gap [L].");
            Vector3d direction;
            switch (a.Enum("LocalAxisDirection"))
            {
                case "negative1Axis": direction = new Vector3d(-1, 0, 0); break;
                case "positive1Axis": direction = new Vector3d(1, 0, 0); break;
                case "negative2Axis": direction = new Vector3d(0, -1, 0); break;
                case "positive2Axis": direction = new Vector3d(0, 1, 0); break;
                default: throw new NotSupportedException("Straus beam supports accept only +-1 and +-2, not longitudinal axis 3.");
            }
            DistributedSpringBehavior behavior;
            switch (a.Enum("SimpleSpringTypes"))
            {
                case "TensionCompression": behavior = DistributedSpringBehavior.Bilateral; break;
                case "CompressionOnly": behavior = DistributedSpringBehavior.CompressionOnly; break;
                case "TensionOnly": behavior = DistributedSpringBehavior.TensionOnly; break;
                default: throw new ArgumentException("Unknown spring behaviour.");
            }
            if (behavior == DistributedSpringBehavior.Bilateral && gap != 0) throw new ArgumentException("A gap requires a compression-only spring.");
            return new BeamDistributedSupport
            {
                Stiffness = stiffness * units.Force / (units.Length * units.Length), Gap = units.ToMm(gap), Behavior = behavior,
                Direction = direction, Axes = new CoordinateSystem(new Point3d(0, 0, 0), axes.V1, axes.V2)
            };
        }

        private static TaperAlignment TaperAlignmentOf(string code)
        {
            switch (code)
            {
                case "Bottom": return TaperAlignment.NegativeSide;
                case "Center": return TaperAlignment.Symmetric;
                case "Top": return TaperAlignment.PositiveSide;
                default: throw new NotSupportedException("LegacyTaperAlignment: " + code);
            }
        }

        private static PlateProperty PlateProperty(LegacyValue p, string name, BridgeUnits units)
        {
            string type = p.Any("PropertyType", "_platePropertyType").Text?.Trim();
            if (type == "LoadPatch") return new ElasticPlateProperty(name, null, 0, 0, ElasticPlateFormulation.LoadPatch);
            var m = p.Any("Material", "_material");
            var material = GpcMaterials.Elastic(m.String("Name"), m.Double("Modulus"), m.Double("PoissonRatio"), m.Double("ShearModulus"), true,
                m.Double("Density"), m.Double("ThermalExpansion"), m.Double("ViscousDamping"), m.Double("DampingRatio"), m.Double("Conductivity"), m.Double("SpecificHeat"), null);
            if (!m["StressStrainTable"].IsNull) throw new NotSupportedException("LegacyPlateStressStrainTableNotMappedYet");
            double membrane = units.ToMm(p.Any("MembraneThickness", "_membraneThickness").AsDouble()), bending = units.ToMm(p.Any("BendingThickness", "_bendingThickness").AsDouble());
            switch (type)
            {
                case "ShellThin": case "ShellThick": case "PlateThin": case "PlateThick":
                    // The former exporter wrote all four as the native ptPlateShell with the two thicknesses.
                    return new ElasticPlateProperty(name, material, bending, membrane, ElasticPlateFormulation.Shell);
                case "Membrane": return new ElasticPlateProperty(name, material, 0, membrane, ElasticPlateFormulation.Membrane);
                case "ShearPanel": return new ElasticPlateProperty(name, material, bending, membrane, ElasticPlateFormulation.ShearPanel);
                default: throw new NotSupportedException("LegacyPlatePropertyType: " + type);
            }
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
