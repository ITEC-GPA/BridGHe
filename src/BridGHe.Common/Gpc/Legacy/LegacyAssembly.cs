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
    /// The units: the element drafts of a conversion are converted with the units of the model (the per-quantity factors of
    /// <see cref="BridgeUnits"/>), so their numbers are already N-mm; Build Model (e3) gives drafts converted with the units of its input.
    /// </summary>
    public sealed class LegacyAssembly
    {
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
