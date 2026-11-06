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
            string nodeId = n.String("NodeId") ?? "";
            var node = new NodeElement(guid, Point(n["Position"], units), null, nodeId, i + 1)
            { Source = new SourceIdentity(Program, owner.ToString("D"), EntityFamily.Node, nodeId.Length != 0 ? nodeId : (i + 1).ToString()) };
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
            var frames = attributes.Where(a => a.Is("NodeCoordinateAttributeModel")).Select(a => a["Frame"]).ToArray();
            if (frames.Length > 1 && frames.Skip(1).Any(f => !SamePlane(f, frames[0]))) throw new ArgumentException($"Node {node.Name}: conflicting/invalid restraint coordinate systems.");
            bool restrained = attributes.Any(a => a.Is("NodeRestraintAttributeModel") || a.Is("NodeStiffnessAttributeModel"));
            if (restrained && n.Field("Loads").Items.Any(l => l.Is("NodeDisplacementLoadModel"))) throw new NotSupportedException("LegacyPrescribedDisplacementNotMappedYet");
            var names = new[] { "Tx", "Ty", "Tz", "Rx", "Ry", "Rz" };
            foreach (var a in attributes)
            {
                if (a.Is("RigidConnectionAttributeModel")) continue; // the rigid connections after the attributes (Node)
                if (a.Is("NodeCoordinateAttributeModel"))
                    node.Assignments.Restrains.Add(new RestrainAssignment { Restrain = new NodeRestrain(node, Frame(a["Frame"], units), Enumerable.Range(0, 6).Select(k => new DofRestrain((GeometryRestrain.DOF)k, false)).ToList()), SourceRecord = FrameRecord });
                else if (a.Is("NodeRestraintAttributeModel"))
                    node.Assignments.Restrains.Add(new RestrainAssignment { Restrain = new NodeRestrain(node, CoordinateSystem.Global, Enumerable.Range(0, 6).Select(k => new DofRestrain((GeometryRestrain.DOF)k, a.Bool(names[k]))).ToList()), SourceRecord = RestraintRecord });
                else if (a.Is("NodeStiffnessAttributeModel"))
                    node.Assignments.Restrains.Add(new RestrainAssignment
                    {
                        Restrain = new NodeRestrain(node, CoordinateSystem.Global, Enumerable.Range(0, 6).Select(k =>
                        {
                            double spring = a.Double(names[k]) * (k < 3 ? units.Force / units.Length : units.Force * units.Length);
                            return spring == 0 ? new DofRestrain((GeometryRestrain.DOF)k, false) : new DofRestrain((GeometryRestrain.DOF)k, spring);
                        }).ToList()),
                        SourceRecord = StiffnessRecord
                    });
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
                if (l.Is("NodeForceLoadModel")) node.Loads.Add(new PointLoad(Vector(l) * units.Force, Zero, node.Position, c) { Name = ForceRecord });
                else if (l.Is("NodeMomentLoadModel")) node.Loads.Add(new PointLoad(Zero, Vector(l) * (units.Force * units.Length), node.Position, c) { Name = MomentRecord });
                else if (l.Is("NodeTemperatureLoadModel"))
                {
                    string type = l.Enum("LoadType");
                    var kind = type == "Reference" ? NodalTemperatureKind.Reference : type == "Fixed" ? NodalTemperatureKind.Prescribed : type == "Initial" ? NodalTemperatureKind.Initial
                        : throw new NotSupportedException("LegacyNodeTemperatureType: " + type);
                    (node.Assignments.Physical ??= new NodePhysicalData()).Temperatures.Add(new NodalTemperatureAssignment { Case = c, Kind = kind, Celsius = units.ToCelsius(l.Double("Value")) });
                }
                else throw new NotSupportedException("LegacyNodeLoadNotMappedYet: " + l.ShortType);
            }
        }

        /// <summary>The kinds of the records of a node draft (<see cref="NodeData"/>): the <see cref="RestrainAssignment.SourceRecord"/> of its
        /// coordinate systems, restraints and stiffnesses, and the name of its force and moment loads.</summary>
        internal const string FrameRecord = "NodeCoordinateAttributeModel", RestraintRecord = "NodeRestraintAttributeModel", StiffnessRecord = "NodeStiffnessAttributeModel",
            ForceRecord = "NodeForceLoadModel", MomentRecord = "NodeMomentLoadModel";
    }
}
