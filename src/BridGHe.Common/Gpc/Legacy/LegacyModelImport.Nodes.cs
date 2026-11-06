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
    /// <summary>R6.1 (lane A of R6.2): the reader of the former node values (node, attributes, loads).</summary>
    public static partial class LegacyModelImport
    {
        /// <summary>
        /// One former node as a GPC node (the body of the node loop of <see cref="Import"/>, chain E (e1): also the conversion of a single node
        /// value, <see cref="NodeDraft"/>): position, name (BridGHe node ID), source, groups (all, by full name), restraints, springs, Element ID,
        /// loads and temperatures (<see cref="NodeData"/>), and the rigid links of the former node as <see cref="RigidConnectionAttribute"/> with
        /// the target point, after its own attributes (L12 §5.1; the assembly makes the <see cref="RigidLink"/> and removes them).
        /// </summary>
        private static NodeElement Node(LegacyValue n, int i, Guid guid, Guid owner, Func<LegacyValue, Group> groupOf, Dictionary<string, LoadCaseBase> cases,
            BridgeUnits units, LegacyImportReport report, LegacyElementRecords records = null)
        {
            var node = NewNode(guid, Point(n["Position"], units), n.String("NodeId") ?? "", i + 1, owner);
            foreach (var g in n.Field("Groups").Items) node.AddGroup(groupOf(g));
            NodeData(n, node, cases, units, report);
            if (records != null) records.EmptyLabels = n.Field("Attributes").Items.Count(a => a.Is("ElementIdAttributeModel") && string.IsNullOrEmpty(a.String("Value")));
            foreach (var a in n.Field("Attributes").Items.Where(a => a.Is("RigidConnectionAttributeModel"))) node.Attributes.Add(new RigidConnectionAttribute(Point(a["Target"], units)));
            return node;
        }

        /// <summary>
        /// Chain E (e1): the draft of one former node value (a <c>GH_Node</c> datum upstream of Build Model): the GPC node with the rules of the
        /// conversion of a whole model (<see cref="Node"/>), number 1, its groups by full name (with their parents), the cases of its loads by
        /// name, its rigid links as <see cref="RigidConnectionAttribute"/> (target point in the draft units). Dimensions in the definition
        /// <paramref name="units"/> (identity for a draft).
        /// </summary>
        public static LegacyNodeDraft NodeDraft(LegacyValue n, BridgeUnits units, Guid guid)
        {
            if (n == null || n.IsNull || !n.Is("NodeModel")) throw new ArgumentException("LegacyNodeModelExpected");
            var report = new LegacyImportReport { Units = units, Owner = guid, Bridge = new GPC.Checkers.CompositeBridge.BridgeModelData() };
            var records = new LegacyElementRecords();
            var cases = ElementCases(n, out var groupOf, records);
            return new LegacyNodeDraft(Node(n, 0, guid, guid, groupOf, cases, units, report, records), report) { Records = records };
        }

        /// <summary>
        /// Chain E (e3, coordinator decision 2 of 5/11): the data of a former node as records of its draft, one per former attribute and
        /// load, in their order, never resolved: what a component appends to a node and what Build Model merges by GH GUID before the
        /// resolution (<see cref="LegacyAssembly.ResolveNode"/>): coordinate systems, restraints and stiffnesses as
        /// <see cref="RestrainAssignment"/> records (<see cref="FrameRecord"/>, <see cref="RestraintRecord"/>, <see cref="StiffnessRecord"/>),
        /// every Element ID as <see cref="ElementLabelAttribute"/>, forces and moments as one <see cref="PointLoad"/> each
        /// (<see cref="ForceRecord"/>, <see cref="MomentRecord"/>), temperatures with their own kind. The checks of one value stay here
        /// (coordinate systems of the value, prescribed displacements, data not mapped yet).
        /// </summary>
        private static void NodeData(LegacyValue n, NodeElement node, Dictionary<string, LoadCaseBase> cases, BridgeUnits units, LegacyImportReport report)
        {
            var attributes = n.Field("Attributes").Items;
            // R6.2 lane A (user decision of 6/11 via the coordinator): prescribed displacements are refused in 0.4.0, on every node (the former
            // exporter applied them only on restrained nodes, from one case, to every linear case); the correct behaviour comes after the release.
            if (n.Field("Loads").Items.Any(l => l.Is("NodeDisplacementLoadModel"))) throw new NotSupportedException(PrescribedDisplacementNotSupported);
            var frames = attributes.Where(a => a.Is("NodeCoordinateAttributeModel")).Select(a => a["Frame"]).ToArray();
            if (frames.Length > 1 && frames.Skip(1).Any(f => !SamePlane(f, frames[0]))) throw new ArgumentException($"Node {node.Name}: conflicting/invalid restraint coordinate systems.");
            var names = new[] { "Tx", "Ty", "Tz", "Rx", "Ry", "Rz" };
            foreach (var a in attributes)
            {
                if (a.Is("RigidConnectionAttributeModel")) continue; // the rigid connections after the attributes (Node)
                if (a.Is("NodeCoordinateAttributeModel"))
                    node.Assignments.Restrains.Add(FrameAssignment(node, Frame(a["Frame"], units)));
                else if (a.Is("NodeRestraintAttributeModel"))
                    node.Assignments.Restrains.Add(RestraintAssignment(node, names.Select(x => a.Bool(x)).ToArray()));
                else if (a.Is("NodeStiffnessAttributeModel"))
                    node.Assignments.Restrains.Add(StiffnessAssignment(node, names.Select(x => a.Double(x)).ToArray(), units));
                else if (a.Is("ElementIdAttributeModel"))
                {
                    string value = a.String("Value");
                    if (string.IsNullOrEmpty(value)) report.Carry("Empty Element ID attribute"); else node.Attributes.Add(new ElementLabelAttribute(value));
                }
                else throw new NotSupportedException("LegacyNodeAttributeNotMappedYet: " + a.ShortType);
            }
            foreach (var l in n.Field("Loads").Items)
            {
                var c = cases[l.Field("LoadCase").String("Name")];
                if (l.Is("NodeForceLoadModel")) node.Loads.Add(ForceLoad(node, Vector(l), c, units));
                else if (l.Is("NodeMomentLoadModel")) node.Loads.Add(MomentLoad(node, Vector(l), c, units));
                else if (l.Is("NodeTemperatureLoadModel")) AddTemperature(node, Temperature(c, l.Enum("LoadType"), l.Double("Value"), units));
                else throw new NotSupportedException("LegacyNodeLoadNotMappedYet: " + l.ShortType);
            }
        }

        /// <summary>The kinds of the records of a node draft (<see cref="NodeData"/>): the <see cref="RestrainAssignment.SourceRecord"/> of its
        /// coordinate systems, restraints and stiffnesses, and the name of its force and moment loads.</summary>
        internal const string FrameRecord = "NodeCoordinateAttributeModel", RestraintRecord = "NodeRestraintAttributeModel", StiffnessRecord = "NodeStiffnessAttributeModel",
            ForceRecord = "NodeForceLoadModel", MomentRecord = "NodeMomentLoadModel", TemperatureRecord = "NodeTemperatureLoadModel";

        /// <summary>
        /// R6.2 lane A (user decision of 6/11, through the coordinator): the message of the refusal of prescribed node displacements in 0.4.0,
        /// given by the Node Displacement component (kept, with its GUID and ports, only to open the existing files) and by the reader of a
        /// former node (an internalized 0.3.0 value, a whole former model) with a <c>NodeDisplacementLoadModel</c>.
        /// </summary>
        public const string PrescribedDisplacementNotSupported = "Prescribed node displacements are not supported by BridGHe 0.4.0: the Node Displacement component is kept only to open existing files; the correct behaviour (a displacement acting only in its own load case) comes in a later version.";

        // R6.2 lane A: the pieces of a former node shared by the reader (NodeData, Node) and the GPC builders of the node drafts
        // (GpcNodeBuilders): the same expressions on the same numbers, so a builder gives the draft the reader gives the changed value.

        /// <summary>A GPC node as the reader creates it: GUID, position, name = BridGHe node ID, number, source (the node ID, or the number when empty).</summary>
        internal static NodeElement NewNode(Guid guid, Point3d position, string nodeId, int number, Guid owner) =>
            new NodeElement(guid, position, null, nodeId, number)
            { Source = new SourceIdentity(Program, owner.ToString("D"), EntityFamily.Node, nodeId.Length != 0 ? nodeId : number.ToString()) };

        /// <summary>A former coordinate system of the restraints of a node (<c>NodeCoordinateAttributeModel</c>): six free DOFs in the frame.</summary>
        internal static RestrainAssignment FrameAssignment(NodeElement node, CoordinateSystem frame) =>
            new RestrainAssignment { Restrain = new NodeRestrain(node, frame, Enumerable.Range(0, 6).Select(k => new DofRestrain((GeometryRestrain.DOF)k, false)).ToList()), SourceRecord = FrameRecord };

        /// <summary>A former restraint of a node (<c>NodeRestraintAttributeModel</c>): Tx, Ty, Tz, Rx, Ry, Rz fixed or free, global axes.</summary>
        internal static RestrainAssignment RestraintAssignment(NodeElement node, IReadOnlyList<bool> dofs) =>
            new RestrainAssignment { Restrain = new NodeRestrain(node, CoordinateSystem.Global, Enumerable.Range(0, 6).Select(k => new DofRestrain((GeometryRestrain.DOF)k, dofs[k])).ToList()), SourceRecord = RestraintRecord };

        /// <summary>A former stiffness of a node (<c>NodeStiffnessAttributeModel</c>): Tx, Ty, Tz (force/length), Rx, Ry, Rz (force·length) in the
        /// units given; a zero spring is a free DOF.</summary>
        internal static RestrainAssignment StiffnessAssignment(NodeElement node, IReadOnlyList<double> values, BridgeUnits units) =>
            new RestrainAssignment
            {
                Restrain = new NodeRestrain(node, CoordinateSystem.Global, Enumerable.Range(0, 6).Select(k =>
                {
                    double spring = values[k] * (k < 3 ? units.Force / units.Length : units.Force * units.Length);
                    return spring == 0 ? new DofRestrain((GeometryRestrain.DOF)k, false) : new DofRestrain((GeometryRestrain.DOF)k, spring);
                }).ToList()),
                SourceRecord = StiffnessRecord
            };

        /// <summary>A former node force (<c>NodeForceLoadModel</c>, global X, Y, Z in force units) as a point load at the node.</summary>
        internal static PointLoad ForceLoad(NodeElement node, Vector3d value, LoadCaseBase c, BridgeUnits units) =>
            new PointLoad(value * units.Force, Zero, node.Position, c) { Name = ForceRecord };

        /// <summary>A former node moment (<c>NodeMomentLoadModel</c>, global X, Y, Z in force·length units) as a point load at the node.</summary>
        internal static PointLoad MomentLoad(NodeElement node, Vector3d value, LoadCaseBase c, BridgeUnits units) =>
            new PointLoad(Zero, value * (units.Force * units.Length), node.Position, c) { Name = MomentRecord };

        /// <summary>A former node temperature (<c>NodeTemperatureLoadModel</c>) by the name of its former type: Reference, Fixed (prescribed),
        /// Initial; another name is refused.</summary>
        internal static NodalTemperatureAssignment Temperature(LoadCaseBase c, string type, double value, BridgeUnits units)
        {
            var kind = type == "Reference" ? NodalTemperatureKind.Reference : type == "Fixed" ? NodalTemperatureKind.Prescribed : type == "Initial" ? NodalTemperatureKind.Initial
                : throw new NotSupportedException("LegacyNodeTemperatureType: " + type);
            return new NodalTemperatureAssignment { Case = c, Kind = kind, Celsius = units.ToCelsius(value) };
        }

        internal static void AddTemperature(NodeElement node, NodalTemperatureAssignment temperature) => (node.Assignments.Physical ??= new NodePhysicalData()).Temperatures.Add(temperature);

        /// <summary>A former coordinate system (Rhino plane: origin, X and Y axes) as the reader reads it (<c>Frame</c>): the origin in mm with the
        /// units given, the axes as written.</summary>
        internal static CoordinateSystem FrameOf(double ox, double oy, double oz, Vector3d xAxis, Vector3d yAxis, BridgeUnits u) =>
            new CoordinateSystem(new Point3d(u.ToMm(ox), u.ToMm(oy), u.ToMm(oz)), xAxis, yAxis);
    }
}
