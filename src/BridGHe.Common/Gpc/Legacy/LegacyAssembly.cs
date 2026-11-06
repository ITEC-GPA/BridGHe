using System;
using System.Collections.Generic;
using System.Linq;
using GPC.Geometry;
using GPC.Model.Attributes;
using GPC.Model.Combinations;
using GPC.Model.Constraints;
using GPC.Model.ElementProperties;
using GPC.Model.Elements;
using GPC.Model.LoadCases;
using GPC.Model.Loads;
using GPC.Model.Models;
using GPC.Model.Restraints;
using GPC.Model.Structure.Assignments;

namespace BridGHe.Common.Gpc.Legacy
{
    /// <summary>Chain E (e2): a case of the conversion with the gravity load of the model it brings (null: none), in the order of the model.</summary>
    internal sealed record LegacyCase(string Name, LoadCaseBase Case, ModelGravityLoad Gravity);

    /// <summary>
    /// Chain E (e2, P2J): the model part of the conversion of the legacy reader, shared by the conversion of a whole former model
    /// (<see cref="LegacyModelImport.Import"/>: element drafts built with the identities of the model, then these steps) and by Build Model on
    /// the element drafts of the GH types (e3). It owns what belongs to the model and not to one element:
    /// - the model, its identity and the definition units of the report;
    /// - the cases (in the order of the conversion, with the gravity loads) and the groups (by full name, parents first, in the order met);
    /// - the nodes in their order and numbering, the merge of the element ends by tolerance (former StrausNodeIndex: <see cref="NodeLookup"/>),
    ///   the rigid links from the <see cref="RigidConnectionAttribute"/> of the node drafts (former RigidConnectionExport rules; the attributes
    ///   are lifted into <see cref="RigidLink"/> and removed);
    /// - the property registries: one beam property per former name (the first wins; a later different one gives the warning "already
    ///   exists" and the beam takes the first), one plate property per name;
    /// - the beams and plates in their order, the moving paths, the stages and the combinations.
    /// The units: the element drafts of a conversion of a whole former model are converted with the units of the model (the per-quantity
    /// factors of <see cref="BridgeUnits"/>), so their numbers are already N-mm. The drafts of the GH types are in the definition units
    /// (<see cref="GpcBeamDrafts.DraftUnits"/>): Build Model on GPC normalises them here, in one place (<see cref="PlaceNode"/>,
    /// <see cref="PlaceBeam"/>, <see cref="PlacePlate"/>, <see cref="NormaliseBeamProperty"/>, <see cref="NormalisePlateProperty"/>; coordinator
    /// decision 1 of 5/11, e3 point 5).
    /// </summary>
    public sealed class LegacyAssembly
    {
        /// <summary>R6.3: the key of <see cref="Exception.Data"/> of the refusal of a beam in two super elements: (beam name, first, second), for
        /// the former message of Build Model (the refusal stays an <see cref="ArgumentException"/> with the message of the reader).</summary>
        public const string TwoSuperElementsKey = "LegacyBeamInTwoSuperElements";

        private static ArgumentException TwoSuperElements(string beam, string first, string second)
        {
            var x = new ArgumentException($"LegacyBeamInTwoSuperElements: beam {beam} is in the super elements '{first}' and '{second}'; a beam belongs to one super element.");
            x.Data[TwoSuperElementsKey] = new[] { beam, first, second };
            return x;
        }

        internal Model Model { get; }
        internal LegacyImportReport Report { get; }
        internal BridgeUnits Units { get; }
        internal Guid Owner { get; }
        /// <summary>The merge tolerance in the definition units.</summary>
        internal double Tolerance { get; }
        /// <summary>The cases of the model by name (the instances the elements use).</summary>
        internal Dictionary<string, LoadCaseBase> Cases { get; } = new Dictionary<string, LoadCaseBase>(StringComparer.Ordinal);
        /// <summary>The groups of the model by full name.</summary>
        internal Dictionary<string, Group> Groups { get; } = new Dictionary<string, Group>(StringComparer.Ordinal);

        private readonly List<NodeElement> nodes = new List<NodeElement>();
        private NodeLookup index;
        private readonly Dictionary<string, (BeamProperty Property, GPC.Model.Structure.Assignments.BeamFormulation Formulation, double Angle, string Signature, GPC.Converter.Straus7.Straus7SectionDefinition Native)> beamProperties
            = new Dictionary<string, (BeamProperty, GPC.Model.Structure.Assignments.BeamFormulation, double, string, GPC.Converter.Straus7.Straus7SectionDefinition)>(StringComparer.Ordinal);
        private readonly Dictionary<string, PlateProperty> plateProperties = new Dictionary<string, PlateProperty>(StringComparer.Ordinal);

        /// <summary>The model (GUID = <paramref name="owner"/>, the owner of the element identities) and the report (units, owner, empty bridge data).</summary>
        internal LegacyAssembly(string name, BridgeUnits units, Guid owner, double toleranceDefinitionUnits, LegacyImportReport report)
        {
            Report = report ?? throw new ArgumentNullException(nameof(report));
            Units = units; Owner = owner; Tolerance = toleranceDefinitionUnits;
            report.Units = units; report.Owner = owner; report.Bridge = new GPC.Checkers.CompositeBridge.BridgeModelData();
            Model = new Model(owner, name);
        }

        /// <summary>The cases of the model, in order, with the gravity load of each (self-weight cases).</summary>
        internal void RegisterCases(IEnumerable<LegacyCase> cases)
        {
            foreach (var c in cases)
            {
                Model.LoadCases.Add(c.Case.Name, c.Case);
                if (c.Gravity != null) Model.ModelLoads.Add(c.Gravity);
                Cases.Add(c.Name, c.Case);
            }
        }

        /// <summary>The group of a former group value (full name, parents first), registered in the model the first time it is met.</summary>
        internal Group GroupOf(LegacyValue g)
        {
            string full = LegacyModelImport.FullName(g);
            if (Groups.TryGetValue(full, out var known)) return known;
            var parent = g["Parent"].IsNull ? null : GroupOf(g["Parent"]);
            var group = Group.Create(GpcIdentity.Group(full), full);
            parent?.AddChild(group);
            Groups.Add(full, group); Model.Groups.Add(group.Name, group);
            return group;
        }

        /// <summary>The merge tolerance must be positive (former StrausNodeIndex).</summary>
        internal void RequireTolerance()
        {
            if (!(Units.ToMm(Tolerance) > 0)) throw new ArgumentOutOfRangeException("tolerance");
        }

        /// <summary>The nodes in their order and numbering, the lookup of the element ends, then the rigid links of the node drafts in node
        /// order (one target node within the tolerance, not the node itself, no duplicate pair after the merge); the rigid connections are
        /// removed from the nodes once lifted.</summary>
        internal void AddNodes(IReadOnlyList<NodeElement> drafts)
        {
            foreach (var node in drafts) { nodes.Add(node); Model.NodesElements.Add(node); }
            index = new NodeLookup(nodes, Units.ToMm(Tolerance));
            // The rigid connections leave every node before the first link: Model.AddCostrain registers both nodes again (Model.AddNode),
            // which refuses a node attribute whose name is not a case name (found by gpc-legacy-import, p2j2).
            // The same holds for the Element ID of a node (ElementLabelAttribute, no case name): the F6 reader refused a labelled node with
            // a rigid link (found by the e3 corpus of --import-dump, P2J; the former exporter accepted it). The other attributes leave the
            // nodes while the links are made and come back with their ids.
            var targets = nodes.Select(n => n.Attributes.Values.OfType<RigidConnectionAttribute>().ToArray()).ToArray();
            var kept = targets.Any(t => t.Length != 0) ? nodes.Select(n => n.Attributes.Values.Where(a => !(a is RigidConnectionAttribute)).ToArray()).ToArray() : null;
            if (kept != null) foreach (var node in nodes) if (node.Attributes.Count != 0) Keep(node.Attributes, a => false);
            var pairs = new HashSet<(int, int)>();
            for (int i = 0; i < nodes.Count; i++)
                foreach (var a in targets[i])
                {
                    var (target, matches) = index.Find(a.TargetMm);
                    if (matches != 1) throw new ArgumentException("Rigid link target matches more than one FEM node. Merge nodes before export.");
                    if (target == i) throw new ArgumentException("Rigid link collapses to the same node within model tolerance.");
                    if (!pairs.Add((Math.Min(i, target), Math.Max(i, target)))) throw new ArgumentException("Duplicate rigid link after node merging.");
                    Model.AddCostrains(new[] { new RigidLink(nodes[i], nodes[target]) });
                }
            if (kept != null) for (int i = 0; i < nodes.Count; i++) foreach (var a in kept[i]) nodes[i].Attributes.Add(a);
        }

        /// <summary>
        /// Chain E (e3, coordinator decision 2 of 5/11): the resolution of a node draft whose records (<see cref="LegacyModelImport"/> NodeData:
        /// one per former attribute and load, possibly merged from several GH data with the same GUID) give the node of the former exporter,
        /// with the warnings in the former order:
        /// - coordinate systems: all the same (else the former error), the first is the frame of the restraint;
        /// - one restraint: a DOF fixed if any restraint record fixes it, the springs of the first stiffness record ("only the first stiffness
        ///   attribute was exported" for the others), the restraint governing a spring on a fixed DOF (warning);
        /// - the first Element ID ("only the first Element ID is kept" for each other);
        /// - forces and moments summed per case in the order of the records (the arithmetic of the former reader: the records are never summed
        ///   before), one point load per case in the order of the first record of each case;
        /// - temperatures: the reference temperature for every kind, as the former exporter (warning for the others).
        /// The node is changed in place: a draft of a GH datum is copied first.
        /// </summary>
        public static NodeElement ResolveNode(NodeElement node, LegacyImportReport report)
        {
            var records = node.Assignments.Restrains.ToArray();
            var frames = records.Where(r => r.SourceRecord == LegacyModelImport.FrameRecord).Select(r => r.Restrain.CoordinateSystem).ToArray();
            if (frames.Length > 1 && frames.Skip(1).Any(f => !SameFrame(f, frames[0]))) throw new ArgumentException($"Node {node.Name}: conflicting/invalid restraint coordinate systems.");
            var axes = frames.Length == 0 ? null : frames[0];
            var restraints = records.Where(r => r.SourceRecord == LegacyModelImport.RestraintRecord).ToArray();
            var stiffness = records.Where(r => r.SourceRecord == LegacyModelImport.StiffnessRecord).ToArray();
            if (records.Length != frames.Length + restraints.Length + stiffness.Length) throw new ArgumentException($"Node {node.Name}: restraint record of an unknown kind.");
            node.Assignments.Restrains.Clear();
            if (stiffness.Length > 1) report.Warn(GPC.Model.Core.Identity.EntityFamily.Node, node.Id, $"Node {node.Name}: only the first stiffness attribute was exported", restraint: true);
            if (restraints.Length != 0 || stiffness.Length != 0)
            {
                var names = new[] { "Tx", "Ty", "Tz", "Rx", "Ry", "Rz" }; var dofs = new List<DofRestrain>();
                for (int k = 0; k < 6; k++)
                {
                    bool fixedDof = restraints.Any(r => r.Restrain.Restrains[k].IsRestrained);
                    var first = stiffness.Length == 0 ? null : stiffness[0].Restrain.Restrains[k];
                    double spring = first == null || !first.HasStiffness ? 0 : first.Stiffness;
                    if (fixedDof && spring != 0) report.Warn(GPC.Model.Core.Identity.EntityFamily.Node, node.Id, $"Node {node.Name}: spring on a restrained DOF {names[k]} (the restraint governs)", restraint: true);
                    dofs.Add(fixedDof || spring == 0 ? new DofRestrain((GeometryRestrain.DOF)k, fixedDof) : new DofRestrain((GeometryRestrain.DOF)k, spring));
                }
                node.Assignments.Restrains.Add(new RestrainAssignment { Restrain = new NodeRestrain(node, axes ?? CoordinateSystem.Global, dofs), SourceRecord = "NodeRestraint/StiffnessAttributeModel" });
            }
            var labels = node.Attributes.Values.OfType<ElementLabelAttribute>().ToArray();
            for (int k = 1; k < labels.Length; k++) report.Warn(Model.FamilyOf(node), node.Id, $"Element {node.Name}: only the first Element ID is kept");
            if (labels.Length > 1) Keep(node.Attributes, a => !labels.Skip(1).Any(x => ReferenceEquals(x, a)));
            var sums = new Dictionary<string, (Vector3d F, Vector3d M, LoadCaseBase Case)>(StringComparer.Ordinal);
            var loads = node.Loads.Values.ToArray();
            foreach (var load in loads)
            {
                if (!(load is PointLoad l) || l.Name != LegacyModelImport.ForceRecord && l.Name != LegacyModelImport.MomentRecord) throw new ArgumentException($"Node {node.Name}: load record of an unknown kind.");
                var c = (LoadCaseBase)l.LoadCase; sums.TryGetValue(c.Name, out var s);
                sums[c.Name] = l.Name == LegacyModelImport.ForceRecord ? ((s.F ?? LegacyModelImport.Zero) + new Vector3d(l.F1, l.F2, l.F3), s.M ?? LegacyModelImport.Zero, s.Case ?? c)
                    : (s.F ?? LegacyModelImport.Zero, (s.M ?? LegacyModelImport.Zero) + new Vector3d(l.M1, l.M2, l.M3), s.Case ?? c);
            }
            node.Loads.Clear();
            foreach (var t in node.Assignments.Physical?.Temperatures ?? new List<NodalTemperatureAssignment>())
                if (t.Kind != NodalTemperatureKind.Reference)
                {
                    report.Warn(GPC.Model.Core.Identity.EntityFamily.Node, node.Id, $"Node {node.Name}: temperature type {(t.Kind == NodalTemperatureKind.Prescribed ? "Fixed" : t.Kind.ToString())} was not exported (reference temperature used, as before)");
                    t.Kind = NodalTemperatureKind.Reference;
                }
            foreach (var s in sums) node.Loads.Add(new PointLoad(s.Value.F, s.Value.M, node.Position, s.Value.Case));
            return node;
        }

        /// <summary>
        /// Chain E (e3, P2K, coordinator decision 2 of 5/11): the resolution of a beam draft at list level (<see cref="LegacyElementRecords"/>,
        /// possibly merged from several GH data with the same GUID) into the beam of the former exporter, with its number and name in the
        /// model, in the order of the former reader: the first group (warning for more), the first Element ID (warning for each other), one
        /// super element (two names: <c>LegacyBeamInTwoSuperElements</c>), one bridge member, one release set, one taper, one prestrain per case
        /// (the former errors), at least 5 output stations unless a record gives them, the first section state (warning for more).
        /// The beam is changed in place: a draft of a GH datum is copied first.
        /// </summary>
        public static BeamElement ResolveBeam(BeamElement beam, LegacyElementRecords records, LegacyImportReport report)
        {
            if (records.Groups.Count > 1) report.Warn(GPC.Model.Core.Identity.EntityFamily.Beam, beam.Id, $"Straus7 allows assigning only one single group to the beam {beam.Id}", numberInText: true);
            for (int k = 1; k < records.Labels.Count; k++) report.Warn(Model.FamilyOf(beam), beam.Id, $"Element {beam.Name}: only the first Element ID is kept");
            if (records.SuperElements.Count > 1)
                throw TwoSuperElements(beam.Name, records.SuperElements[0], records.SuperElements[1]);
            if (records.Members > 1) throw new ArgumentException("LegacyDuplicateBridgeMemberAttribute: beam " + beam.Name);
            if (beam.Attributes.Values.OfType<BeamReleasesAttribute>().Count() > 1) throw new ArgumentException($"Cannot assign more than 1 set of releases for beam {beam.Id}");
            if (records.Tapers > 1) throw new ArgumentException("LegacyDuplicateBeamTaper");
            var prestrained = new HashSet<string>(StringComparer.Ordinal);
            foreach (var s in beam.Assignments.Physical?.InitialStates ?? new List<BeamInitialState>())
                if (!prestrained.Add(((LoadCaseBase)s.Case).Name)) throw new ArgumentException("Beam " + beam.Id + " has multiple prestrain in loadcase " + ((LoadCaseBase)s.Case).Name);
            // The former result reader asked Straus7 for at least 5 stations per beam unless the beam had an output-stations attribute.
            (beam.Assignments.Physical ??= new BeamPhysicalData()).MinimumOutputStations ??= 5;
            if (records.SectionStates > 1) report.Warn(GPC.Model.Core.Identity.EntityFamily.Beam, beam.Id, $"Beam {beam.Name}: only the first bridge section state is kept");
            return beam;
        }

        /// <summary>Chain E (e3): the resolution of a plate draft at list level: the first group (warning for more) and the first Element ID
        /// (warning for each other), with the number and name of the plate in the model, in the order of the former reader.</summary>
        public static AreaElement ResolvePlate(AreaElement plate, LegacyElementRecords records, LegacyImportReport report)
        {
            if (records.Groups.Count > 1) report.Warn(GPC.Model.Core.Identity.EntityFamily.Shell, plate.Id, $"Straus7 allows assigning only one single group to the plate {plate.Id}", numberInText: true);
            for (int k = 1; k < records.Labels.Count; k++) report.Warn(Model.FamilyOf(plate), plate.Id, $"Element {plate.Name}: only the first Element ID is kept");
            return plate;
        }

        /// <summary>Keeps the items of an id collection that pass <paramref name="keep"/>, with their ids, as if the others had never been added:
        /// <see cref="GPC.Model.Collections.UniqueIdCollection{T}.Remove(T)"/> keeps the removed ids counted, and the counter is archived
        /// with the element (<c>AttributesMaxId</c>), so a removal would change the document of the model.</summary>
        private static void Keep<T>(GPC.Model.Collections.UniqueIdCollection<T> items, Func<T, bool> keep) where T : GPC.Model.ModelObjectId, System.Runtime.Serialization.ISerializable
        {
            var kept = items.Values.Where(keep).ToArray();
            items.Clear();
            foreach (var item in kept) items.Add(item);
        }

        private static bool SameFrame(CoordinateSystem a, CoordinateSystem b) => Same(a.Origin, b.Origin) && Same(a.V1, b.V1) && Same(a.V2, b.V2) && Same(a.V3, b.V3);
        private static bool Same(Point3d a, Point3d b) => a.X == b.X && a.Y == b.Y && a.Z == b.Z;
        private static bool Same(Vector3d a, Vector3d b) => a.X == b.X && a.Y == b.Y && a.Z == b.Z;

        /// <summary>The node <paramref name="i"/> (0-based) of the model.</summary>
        internal NodeElement Node(int i) => nodes[i];

        /// <summary>The nodes of the two ends of beam <paramref name="number"/> by tolerance, with the warnings of the former exporter when
        /// more than one node is within the tolerance.</summary>
        internal (int I, int J) BeamEnds(Point3d from, Point3d to, int number)
        {
            var (i, mi) = index.Find(from); var (j, mj) = index.Find(to);
            if (mi > 1) Report.Warn(GPC.Model.Core.Identity.EntityFamily.Beam, number, $"Collapsing {mi} nodes at coordinates {from}");
            if (mj > 1) Report.Warn(GPC.Model.Core.Identity.EntityFamily.Beam, number, $"Collapsing {mj} nodes at coordinates {to}");
            return (i, j);
        }

        /// <summary>The registered beam property of a former property name for beam <paramref name="number"/>: the first of the name is
        /// converted (<paramref name="convert"/>, with the name it takes in the model) and registered; a later one with a different content
        /// (<paramref name="signature"/>) takes it, with the warning of the former exporter.</summary>
        internal (BeamProperty Property, GPC.Model.Structure.Assignments.BeamFormulation Formulation, double Angle, string Signature, GPC.Converter.Straus7.Straus7SectionDefinition Native)
            BeamProperty(string propertyName, string signature, int number, Func<string, LegacyBeamProperty> convert)
        {
            if (!beamProperties.TryGetValue(propertyName, out var p))
            {
                var converted = convert(Unique(propertyName));
                Model.BeamProperties.Add(converted.Property.Name, converted.Property);
                Report.BeamProperties[propertyName] = converted.Data;
                beamProperties.Add(propertyName, p = (converted.Property, converted.Data.Formulation, converted.Data.Angle, signature, converted.Data.Native));
            }
            else if (p.Signature != signature) { Report.Warnings.Add(string.Format(LegacyImportReport.ExistingPropertyWarning, propertyName)); Report.ShadowedPropertyBeams.Add(number); }
            return p;
        }

        private string Unique(string name)
        {
            if (!Model.BeamProperties.ContainsKey(name)) return name;
            for (int k = 2; ; k++) if (!Model.BeamProperties.ContainsKey(name + " #" + k)) return name + " #" + k;
        }

        /// <summary>A beam of the model, in order (its resolved native section in the report).</summary>
        internal void AddBeam(BeamElement beam, GPC.Converter.Straus7.Straus7SectionDefinition native)
        {
            if (native != null) Report.NativeSections.Add(beam.Guid, native);
            Model.BeamElements.Add(beam);
        }

        /// <summary>e3: the group of the model for a group of a draft (by full name, parents first, as <see cref="GroupOf(LegacyValue)"/>).</summary>
        internal Group GroupOf(Group draft)
        {
            if (Groups.TryGetValue(draft.Name, out var known)) return known;
            var parent = draft.Parent == null ? null : GroupOf(draft.Parent);
            var group = Group.Create(GpcIdentity.Group(draft.Name), draft.Name);
            parent?.AddChild(group);
            Groups.Add(draft.Name, group); Model.Groups.Add(group.Name, group);
            return group;
        }

        private LoadCaseBase CaseOf(GPC.Model.LoadCases.ILoadCase draft) => Cases[((LoadCaseBase)draft).Name];
        private static CoordinateSystem Copy(CoordinateSystem cs) => cs == null ? null : GPC.Model.Core.ModelValues.CopyValue(cs);
        /// <summary>A beam load of a draft in the beam axes (the frame <see cref="GpcBeamLoads.ComponentFrame"/>, without a name) or global (a load
        /// built without a frame takes the global coordinate system).</summary>
        private static bool Local(Load l) => l.CoordinateSystem != null && l.CoordinateSystem.Name != CoordinateSystem.Global.Name;
        /// <summary>A string instance of its own. The archive of a model writes a string shared by two fields once, and the documents compare
        /// the graph: Build Model gave a beam or plate its first Element ID as BridGHe ID (one string for both), while a node was copied once more
        /// (its attributes cloned: two strings).</summary>
        private static string Distinct(string s) => s.Length == 0 ? s : new string(s.ToCharArray());

        // ---------------------------------------------------------------------------------------------------------------------------------
        // Units: the normalisation of the drafts of the GH types, in one place (coordinator decision 1 of 5/11, e3 point 5).
        // A draft keeps the numbers of the definition as written (identity factors, GpcBeamDrafts.DraftUnits); here they take the factors of
        // the units of the model, with the operations of the reader (LegacyModelImport): positions, offsets, gaps, thicknesses, stations and
        // rigid-link targets ToMm; node springs a·(F/L) or a·(F·L); release stiffnesses (t·F)/L or (r·F)·L; beam supports (s·F)/(L·L), plate
        // supports (s·F)/(L·L·L); node forces ·F, moments ·(F·L); concentrated beam actions ·F or ·(F·L); distributed beam actions per
        // component ·ToNPerMm(1) or ·ToNmmPerMm(1) (the reader scales the value before the 0/±1 direction: the same bits); prestrain forces
        // ToN; gradients ToCelsiusPerMm; temperatures ToCelsius; pressures and plate prestress ToMPa; the sections from their definition in the
        // definition units (LegacySectionRecipe, LegacyModelImport.SectionProperty); a plate non-structural mass only in N-mm (former error).
        // The derived values (section and principal axes, eccentricities, load lines and points, support axes) are computed on the model in mm
        // (re-anchoring). Every factor is 1 in N-mm, where the operations above are exact: N-mm models stay bit for bit (decision 1 (a)). In
        // other units the only value the draft keeps derived is a restraint frame away from the global origin, built again from its three unit
        // axes (unitised again by the constructor: exact on the measured corpus, one unit in the last place possible; with the cross product of
        // two axes it differed by 2.1E-16). The measure of decision 1 (b) is in the notes (P2B, step 47): no difference on the corpus.
        // ---------------------------------------------------------------------------------------------------------------------------------

        private Point3d Mm(Point3d p) => new Point3d(Units.ToMm(p.X), Units.ToMm(p.Y), Units.ToMm(p.Z));
        /// <summary>A vector of a draft times a factor of the units (the reader: <c>Vector3d × factor</c>); unchanged in N-mm.</summary>
        private Vector3d Scaled(double x, double y, double z, double factor) => Units.IsIdentity ? new Vector3d(x, y, z) : new Vector3d(x, y, z) * factor;
        /// <summary>A restraint frame of a draft with its origin in mm (axes as they are; a new frame only for an origin away from zero).</summary>
        private CoordinateSystem Frame(CoordinateSystem cs)
        {
            if (cs == null) return null;
            var o = cs.Origin;
            if (Units.IsIdentity || o.X == 0 && o.Y == 0 && o.Z == 0) return Copy(cs);
            // The three axes of the draft (the reader computed the third from the former plane axes; a cross product of the unit axes would
            // differ in its last bits): unitised again by the constructor, exact for most unit vectors.
            return new CoordinateSystem(Mm(o), cs.V1, cs.V2, cs.V3, cs.Name);
        }
        /// <summary>A spring of a node stiffness record in N-mm (the reader: a·(F/L) for translations, a·(F·L) for rotations).</summary>
        private DofRestrain Spring(DofRestrain d)
        {
            if (Units.IsIdentity || !d.HasStiffness) return GPC.Model.Core.ModelValues.CopyValue(d);
            return new DofRestrain(d.Dof, d.Stiffness * ((int)d.Dof < 3 ? Units.Force / Units.Length : Units.Force * Units.Length));
        }
        /// <summary>A release of a draft in N-mm (the reader: (t·F)/L for translations, (r·F)·L for rotations).</summary>
        private BeamDofConnection Release(BeamDofConnection c, int dof)
        {
            if (Units.IsIdentity || c == null || c.Kind != BeamConnectionKind.AbsoluteStiffness) return c;
            return new BeamDofConnection(BeamConnectionKind.AbsoluteStiffness, dof < 3 ? c.Value * Units.Force / Units.Length : c.Value * Units.Force * Units.Length);
        }

        /// <summary>
        /// Chain E (e3 point 5): the beam property of a draft in N-mm, registered by Build Model. The length unit of a library or BXS section
        /// is checked against the units of the model (former error); in N-mm the property of the draft is the one of the model; in other units
        /// the section is built again from its definition in the definition units (<see cref="LegacyBeamPropertyData.Section"/>) with the
        /// operations of the reader (<see cref="LegacyModelImport.SectionProperty"/>), on a copy of the material of the draft (N-mm, never
        /// scaled). A property built on GPC has no such definition: refused outside N-mm.
        /// </summary>
        internal LegacyBeamProperty NormaliseBeamProperty(BeamProperty draft, LegacyBeamPropertyData data)
        {
            var r = data.Section;
            if (r?.NativeKey != null && GpcNativeSections.IsLibraryOrBxs(r.NativeKey) && r.NativeLengthUnit != GpcNativeSections.Straus7LengthUnit(Units.Length))
                throw new ArgumentException(LegacyModelImport.LibraryUnitsMessage);
            if (Units.IsIdentity) return new LegacyBeamProperty(draft, data);
            if (r == null) throw new NotSupportedException("GpcBuildModelPropertyWithoutSectionDefinition: the beam property '" + draft.Name + "' was built on GPC, without a section in the definition units; Build Model takes it only in N-mm.");
            if (!(draft is ElasticBeamProperty elastic)) throw new NotSupportedException("GpcBuildModelPropertyKind: " + draft.GetType().Name);
            var (property, angle, native) = LegacyModelImport.SectionProperty(r, draft.Name, GPC.Model.Core.ModelValues.CopyValue(elastic.Material), Units,
                new Dictionary<string, (double, double)>(StringComparer.Ordinal), new LegacyImportReport());
            // R6.3: the signature of the property in the model is the GPC fingerprint of the normalised property (the conversion of the FEM
            // model reads it in N-mm); the drafts compare their signatures in the definition units (the registry, one unit system).
            return new LegacyBeamProperty(property, data with { Angle = angle, Native = native, Signature = LegacyModelImport.PropertySignature(property) });
        }

        /// <summary>Chain E (e3 point 5): the plate property of a draft in N-mm (the reader: the thicknesses ToMm, the material N-mm).</summary>
        internal PlateProperty NormalisePlateProperty(PlateProperty draft)
        {
            if (Units.IsIdentity) return draft;
            if (!(draft is ElasticPlateProperty e)) throw new NotSupportedException("GpcBuildModelPlatePropertyKind: " + draft.GetType().Name);
            return new ElasticPlateProperty(e.Name, e.Material == null ? null : GPC.Model.Core.ModelValues.CopyValue(e.Material), Units.ToMm(e.BendingThickness), Units.ToMm(e.MembraneThickness), e.Formulation);
        }

        /// <summary>
        /// Chain E (e3, P2K): node <paramref name="i"/> (0-based) of the model from a node draft at list level (definition units, normalised
        /// here), as the former reader builds it (<c>LegacyModelImport.Node</c>): identity of the model, BridGHe ID <paramref name="name"/>,
        /// groups of the model by full name, the records (coordinate systems, restraints, stiffnesses, Element IDs, forces, moments, temperatures)
        /// with the cases of the model, then the rigid connections; resolved (<see cref="ResolveNode"/>).
        /// </summary>
        internal NodeElement PlaceNode(LegacyNodeDraft draft, int i, string name)
        {
            var d = draft.Node;
            var node = new NodeElement(GpcIdentity.Derive(Owner, "node:" + i), Mm(d.Position), null, name, i + 1)
            { Source = new GPC.Model.Core.Identity.SourceIdentity(LegacyModelImport.Program, Owner.ToString("D"), GPC.Model.Core.Identity.EntityFamily.Node, name.Length != 0 ? name : (i + 1).ToString()) };
            foreach (var g in d.Groups.Values) node.AddGroup(GroupOf(g));
            foreach (var r in d.Assignments.Restrains)
                node.Assignments.Restrains.Add(new RestrainAssignment { Restrain = new NodeRestrain(node, Frame(r.Restrain.CoordinateSystem),
                    r.Restrain.Restrains.Select(x => r.SourceRecord == LegacyModelImport.StiffnessRecord ? Spring(x) : GPC.Model.Core.ModelValues.CopyValue(x)).ToList()), SourceRecord = r.SourceRecord });
            foreach (var a in d.Attributes.Values) if (a is ElementLabelAttribute label) node.Attributes.Add(new ElementLabelAttribute(Distinct(label.Label)));
            foreach (var l in d.Loads.Values.OfType<PointLoad>())
                node.Loads.Add(new PointLoad(Scaled(l.F1, l.F2, l.F3, Units.Force), Scaled(l.M1, l.M2, l.M3, Units.Force * Units.Length), node.Position, CaseOf(l.LoadCase)) { Name = l.Name });
            foreach (var t in d.Assignments.Physical?.Temperatures ?? new List<NodalTemperatureAssignment>())
                (node.Assignments.Physical ??= new NodePhysicalData()).Temperatures.Add(new NodalTemperatureAssignment { Case = CaseOf(t.Case), Kind = t.Kind, Celsius = Units.ToCelsius(t.Celsius) });
            foreach (var a in d.Attributes.Values) if (a is RigidConnectionAttribute rigid) node.Attributes.Add(new RigidConnectionAttribute(Mm(rigid.TargetMm)));
            if (d.Attributes.Values.Any(a => !(a is ElementLabelAttribute) && !(a is RigidConnectionAttribute)) || d.Loads.Values.Any(l => !(l is PointLoad)))
                throw new ArgumentException("LegacyNodeDraftRecordUnknown: node " + name);
            return ResolveNode(node, Report);
        }

        /// <summary>
        /// Chain E (e3, P2K): beam <paramref name="k"/> (0-based) of the model from a beam draft at list level, on its nodes and the registered
        /// property, as the former reader builds it (<c>LegacyModelImport.Beam</c>): the axes from the ends of the model (re-anchoring: section
        /// axes with the BridGHe angle and the principal angle of the registered property, offsets in the principal axes, the global
        /// eccentricity of the loads, the lines of the distributed loads, the points of the concentrated loads, the axes of the supports), the
        /// first group, the label and the release sets, the physical data, the loads with the cases of the model and the record of the model
        /// number, the bridge data on the beam of the model (the state of a current property with the fingerprint of the beam of the model);
        /// resolved (<see cref="ResolveBeam"/>).
        /// </summary>
        internal BeamElement PlaceBeam(LegacyBeamDraft draft, int k, string name, NodeElement ni, NodeElement nj,
            (BeamProperty Property, GPC.Model.Structure.Assignments.BeamFormulation Formulation, double Angle, string Signature, GPC.Converter.Straus7.Straus7SectionDefinition Native) p)
        {
            var d = draft.Beam;
            var beam = new BeamElement(GpcIdentity.Derive(Owner, "beam:" + k), ni.Position, nj.Position, p.Property, null, name, k + 1)
            { Source = new GPC.Model.Core.Identity.SourceIdentity(LegacyModelImport.Program, Owner.ToString("D"), GPC.Model.Core.Identity.EntityFamily.Beam, name.Length != 0 ? name : (k + 1).ToString()) };
            beam.ConnectNodes(ni, nj);
            Report.BeamAngles[k + 1] = draft.AngleDeg;
            var start = beam.StartPoint; var end = beam.EndPoint;
            var section = GpcBeamAxes.Section(start, end, draft.AngleDeg, p.Angle);
            var principal = GpcBeamAxes.Principal(start, end, draft.AngleDeg);
            beam.Assignments.Formulation = p.Formulation;
            beam.Assignments.SectionAxes = section;
            double ox = Units.ToMm(d.Assignments.OffsetI?.X ?? 0), oy = Units.ToMm(d.Assignments.OffsetI?.Y ?? 0);
            var globalOffset = principal.V1 * ox + principal.V2 * oy;
            if (ox != 0 || oy != 0)
            {
                beam.Assignments.OffsetAxes = principal;
                beam.Assignments.OffsetI = new Vector3d(ox, oy, 0); beam.Assignments.OffsetJ = new Vector3d(ox, oy, 0);
            }
            if (d.Groups.Count != 0) beam.AddGroup(GroupOf(d.Groups.Values.First()));
            foreach (var a in d.Attributes.Values)
            {
                if (a is ElementLabelAttribute label) beam.Attributes.Add(new ElementLabelAttribute(label.Label == name ? name : Distinct(label.Label)));
                else if (a is BeamReleasesAttribute r)
                {
                    var copy = new BeamReleasesAttribute { CoordinateSystem = Copy(r.CoordinateSystem), Phase = r.Phase, SourceRecord = r.SourceRecord };
                    for (int q = 0; q < r.I.Length; q++) copy.I[q] = Release(r.I[q], q);
                    for (int q = 0; q < r.J.Length; q++) copy.J[q] = Release(r.J[q], q);
                    beam.Attributes.Add(copy);
                }
                else throw new ArgumentException("LegacyBeamDraftRecordUnknown: beam " + name + ", " + a.GetType().Name);
            }
            var dp = d.Assignments.Physical;
            if (dp != null)
            {
                var bp = beam.Assignments.Physical = new BeamPhysicalData { MinimumOutputStations = dp.MinimumOutputStations, StringGroup = dp.StringGroup, Taper = dp.Taper == null ? null : GPC.Model.Core.ModelValues.CopyValue(dp.Taper) };
                foreach (var s in dp.Supports)
                    bp.Supports.Add(new BeamDistributedSupport { Stiffness = Units.IsIdentity ? s.Stiffness : s.Stiffness * Units.Force / (Units.Length * Units.Length), Gap = Units.ToMm(s.Gap), Behavior = s.Behavior,
                        Direction = new Vector3d(s.Direction.X, s.Direction.Y, s.Direction.Z), Axes = new CoordinateSystem(new Point3d(0, 0, 0), section.V1, section.V2) });
                foreach (var s in dp.InitialStates) bp.InitialStates.Add(new BeamInitialState { Case = CaseOf(s.Case), Kind = s.Kind, Value = s.Kind == BeamInitialStateKind.AxialForce ? Units.ToN(s.Value) : s.Value });
                foreach (var g in dp.TemperatureGradients) bp.TemperatureGradients.Add(new BeamTemperatureGradient { Case = CaseOf(g.Case), Axis1 = Units.ToCelsiusPerMm(g.Axis1), Axis2 = Units.ToCelsiusPerMm(g.Axis2) });
            }
            string from = LegacyBuildModel.BeamLoadRecord(d.Id, 0), to = LegacyBuildModel.BeamLoadRecord(k + 1, 0);
            from = from.Substring(0, from.Length - 1); to = to.Substring(0, to.Length - 1);
            // One line per former distributed load, shared by its pieces (GpcBeamLoads.Distributed): the same graph as the former reader.
            var lines = new Dictionary<string, GPC.Geometry.Line3d>(StringComparer.Ordinal);
            foreach (var a in d.Assignments.Loads)
            {
                if (!a.OriginalAssignmentId.StartsWith(from, StringComparison.Ordinal) || !a.SourceRecord.StartsWith(from, StringComparison.Ordinal)) throw new ArgumentException("LegacyBeamDraftLoadRecord: " + a.SourceRecord);
                var placed = new GPC.Model.Loads.Assignments.BeamLoadAssignment
                {
                    Start = a.Start, End = a.End, Eccentricity = globalOffset, EccentricityCoordinateSystem = CoordinateSystem.Global, LengthConvention = a.LengthConvention,
                    ProjectionPlaneNormal = a.ProjectionPlaneNormal == null ? null : new Vector3d(a.ProjectionPlaneNormal.X, a.ProjectionPlaneNormal.Y, a.ProjectionPlaneNormal.Z),
                    StationDomain = a.StationDomain, OriginalAssignmentId = to + a.OriginalAssignmentId.Substring(from.Length), SourceRecord = to + a.SourceRecord.Substring(from.Length),
                };
                if (a.Concentrated is PointLoad c)
                {
                    double station = a.Start;
                    var point = new Point3d(start.X + (end.X - start.X) * station, start.Y + (end.Y - start.Y) * station, start.Z + (end.Z - start.Z) * station);
                    var axes = Local(c) ? GpcBeamLoads.ComponentFrame(principal) : null;
                    var f = Scaled(c.F1, c.F2, c.F3, Units.Force); var m = Scaled(c.M1, c.M2, c.M3, Units.Force * Units.Length);
                    placed.Concentrated = axes == null ? new PointLoad(f, m, point, CaseOf(c.LoadCase)) : new PointLoad(f, m, point, CaseOf(c.LoadCase), axes);
                }
                else
                {
                    if (!lines.TryGetValue(a.OriginalAssignmentId, out var line)) lines.Add(a.OriginalAssignmentId, line = new GPC.Geometry.Line3d(start, end));
                    LineLoad Line(LineLoad l)
                    {
                        var axes = Local(l) ? GpcBeamLoads.ComponentFrame(principal) : null;
                        var f = Scaled(l.F1, l.F2, l.F3, Units.ToNPerMm(1)); var m = Scaled(l.M1, l.M2, l.M3, Units.ToNmmPerMm(1));
                        return axes == null ? new LineLoad(f, m, line, CaseOf(l.LoadCase)) : new LineLoad(f, m, line, CaseOf(l.LoadCase), axes);
                    }
                    placed.StartIntensity = Line((LineLoad)a.StartIntensity); placed.EndIntensity = Line((LineLoad)a.EndIntensity);
                }
                beam.Assignments.Loads.Add(placed);
            }
            var bridge = draft.Bridge;
            if (bridge?.Member != null)
            {
                var m = bridge.Member;
                Report.Bridge.SetMember(beam.Guid, Units.IsIdentity ? m : new GPC.Checkers.CompositeBridge.Layout.BridgeMemberMetadata(m.PhysicalId, m.Family, m.Group, m.Segment, m.Span,
                    Units.ToMm(m.StationFromMm), Units.ToMm(m.StationToMm), m.SectionUp, m.GeometryId));
            }
            if (bridge?.SuperElement != null) Report.Bridge.SetSuperElement(beam.Guid, bridge.SuperElement);
            ResolveBeam(beam, draft.Records, Report);
            if (bridge?.SectionState != null)
            {
                var s = bridge.SectionState;
                // A current state takes the fingerprint of the beam of the model (its registered property, N-mm); a stale marker stays.
                string fingerprint = s.PropertyFingerprint.StartsWith(LegacyModelImport.StaleSectionStatePrefix, StringComparison.Ordinal) ? s.PropertyFingerprint
                    : GPC.Checkers.CompositeBridge.BridgeWorkflowDefinition.PropertyFingerprint(beam);
                Report.Bridge.SetSectionState(beam.Guid, new GPC.Checkers.CompositeBridge.BridgeLocalSectionState(s.Behavior, s.ConcreteExcluded, s.ModularRatio, s.ClassificationCase, fingerprint));
            }
            if (bridge?.CompositeSection != null) Report.Bridge.SetCompositeSection(beam.Guid, bridge.CompositeSection);
            return beam;
        }

        /// <summary>
        /// Chain E (e3, P2K): plate <paramref name="k"/> (0-based) of the model from a plate draft at list level, on its nodes and the registered
        /// property, as the former reader builds it (<c>LegacyModelImport.Plate</c>): offset, first group, label, load patch, supports,
        /// prestress, non-structural masses and pressures with the cases of the model; resolved (<see cref="ResolvePlate"/>).
        /// </summary>
        internal AreaElement PlacePlate(LegacyPlateDraft draft, int k, string name, PlateProperty property, NodeElement[] corners)
        {
            var d = draft.Plate;
            var plate = new AreaElement(GpcIdentity.Derive(Owner, "plate:" + k), null, property, null, name, k + 1)
            { Source = new GPC.Model.Core.Identity.SourceIdentity(LegacyModelImport.Program, Owner.ToString("D"), GPC.Model.Core.Identity.EntityFamily.Shell, name.Length != 0 ? name : (k + 1).ToString()) };
            plate.ConnectNodes(corners);
            if (d.Assignments.Offset.HasValue) plate.Assignments.Offset = Units.IsIdentity ? d.Assignments.Offset : Units.ToMm(d.Assignments.Offset.Value);
            if (d.Groups.Count != 0) plate.AddGroup(GroupOf(d.Groups.Values.First()));
            foreach (var a in d.Attributes.Values)
            {
                if (a is ElementLabelAttribute label) plate.Attributes.Add(new ElementLabelAttribute(label.Label == name ? name : Distinct(label.Label)));
                else throw new ArgumentException("LegacyPlateDraftRecordUnknown: plate " + name + ", " + a.GetType().Name);
            }
            var dp = d.Assignments.Physical;
            if (dp != null)
            {
                var pp = plate.Assignments.Physical = new ShellPhysicalData { LoadPatch = dp.LoadPatch == null ? null : GPC.Model.Core.ModelValues.CopyValue(dp.LoadPatch) };
                foreach (var s in dp.Supports)
                {
                    var support = GPC.Model.Core.ModelValues.CopyValue(s);
                    if (!Units.IsIdentity) support.Stiffness = s.Stiffness * Units.Force / (Units.Length * Units.Length * Units.Length);
                    pp.Supports.Add(support);
                }
                foreach (var s in dp.InitialStresses) pp.InitialStresses.Add(new ShellInitialStress { Case = CaseOf(s.Case), Components = new Vector3d(Units.ToMPa(s.Components.X), Units.ToMPa(s.Components.Y), Units.ToMPa(s.Components.Z)) });
                // The former reader converted a non-structural mass only in N-mm.
                if (dp.NonStructuralMasses.Count != 0 && !Units.IsIdentity) throw new NotSupportedException("LegacyPlateNonStructuralMassUnitsRequireNmm");
                foreach (var m in dp.NonStructuralMasses)
                    pp.NonStructuralMasses.Add(new ShellNonStructuralMass { Case = CaseOf(m.Case), MassPerArea = m.MassPerArea, DynamicFactor = m.DynamicFactor, GlobalOffset = new Vector3d(m.GlobalOffset.X, m.GlobalOffset.Y, m.GlobalOffset.Z) });
            }
            foreach (var l in d.Loads.Values)
            {
                if (!(l is ShellFacePressure s)) throw new ArgumentException("LegacyPlateDraftRecordUnknown: plate " + name + ", " + l.GetType().Name);
                plate.Loads.Add(s.NormalPressure.HasValue ? ShellFacePressure.Normal(CaseOf(s.LoadCase), s.Face, Units.ToMPa(s.NormalPressure.Value))
                    : ShellFacePressure.Global(CaseOf(s.LoadCase), s.Face, new Vector3d(Units.ToMPa(s.GlobalPressure.X), Units.ToMPa(s.GlobalPressure.Y), Units.ToMPa(s.GlobalPressure.Z)), s.Projection));
            }
            return ResolvePlate(plate, draft.Records, Report);
        }

        /// <summary>The moving paths of the former model (JSON of the former MovingPath records).</summary>
        internal void AddMovingPaths(string json)
        {
            if (json != "[]") GpcMoving.FromLegacyJson(json, Model, Cases, Units, Owner);
        }

        /// <summary>The nodes of the corners of plate <paramref name="number"/> by tolerance, with the warning of the former exporter.</summary>
        internal NodeElement[] PlateNodes(IEnumerable<Point3d> corners, int number) =>
            corners.Select(p =>
            {
                var (n, m) = index.Find(p);
                if (m > 1) Report.Warn(GPC.Model.Core.Identity.EntityFamily.Shell, number, $"Collapsing {m} nodes at plate {number}", numberInText: true);
                return nodes[n];
            }).ToArray();

        /// <summary>The plate property of a name: one per name, the first wins (former exporter), registered in the model.</summary>
        internal PlateProperty PlateProperty(string name, Func<PlateProperty> convert)
        {
            if (!plateProperties.TryGetValue(name, out var property)) plateProperties.Add(name, property = convert());
            if (!Model.PlateProperties.ContainsKey(property.Name)) Model.PlateProperties.Add(property.Name, property);
            return property;
        }

        internal void AddPlate(AreaElement plate) => Model.AreaElements.Add(plate);

        /// <summary>A combination of the model: the factors of its cases by name, in order.</summary>
        internal void AddCombination(string name, IEnumerable<(string Case, double Factor)> factors)
        {
            var combination = new Combination(name, GpcIdentity.Combination(name));
            foreach (var (c, factor) in factors) combination.AddLoadCaseCoefficient(Cases[c], factor);
            Model.Combinations.Add(combination.Name, combination);
        }
    }

    /// <summary>Former StrausNodeIndex: among the nodes within the tolerance the lowest by (X, Y, Z), then by number.</summary>
    internal sealed class NodeLookup
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

