using System;
using System.Collections.Generic;
using System.Linq;
using BridGHe.Common.Gpc.Legacy;
using GPC.Model.LoadCases;
using GPC.Model.Loads;

namespace BridGHe.Common.Gpc.Builders
{
    /// <summary>
    /// R6.2, lane A (elements, attributes, loads): the GPC builders of the node drafts. Each builder reads the draft of its input as the
    /// reader would read the former value with the change (<see cref="LegacyModelImport.NodeDraft"/>): same collections, same order, same
    /// records; it never changes its input (a <see cref="GpcDrafts.Copy(LegacyNodeDraft)"/>) and ends with the identity given
    /// (<see cref="GpcDrafts.Reidentify(LegacyNodeDraft, Guid)"/>). Units: those of the draft (the definition units).
    /// <para>R6.1 (P2N): <see cref="WithForce"/> is the worked example of the frozen API (Node Load).</para>
    /// <para>R6.2 lane A (R62A): the pieces of a node are those of the reader (<c>LegacyModelImport.NewNode</c>, <c>RestraintAssignment</c>,
    /// <c>StiffnessAssignment</c>, <c>FrameAssignment</c>, <c>MomentLoad</c>, <c>Temperature</c>), the same expressions on the same numbers.
    /// A builder refuses (an <see cref="ArgumentException"/> or a <see cref="NotSupportedException"/>, the message of the reader) what the reader
    /// refuses; the caller reports the failure without attaching a draft.
    /// Signatures reported to lane B (stable): <see cref="Create"/>, <see cref="WithFrame"/>, <see cref="WithRestraint"/>, <see cref="WithStiffness"/>,
    /// <see cref="WithLabel"/>, <see cref="WithForce"/>, <see cref="WithMoment"/>, <see cref="WithTemperature"/>.</para>
    /// </summary>
    public static class GpcNodeBuilders
    {
        /// <summary>Node Load: a force on the node in a load case (the reader: a point load of the force times the force unit, no moment, at
        /// the position of the node, named <see cref="LegacyModelImport.ForceRecord"/>, after the loads of the draft; the records take the
        /// load (kind, case) and the definition of its case).</summary>
        public static LegacyNodeDraft WithForce(LegacyNodeDraft node, LegacyCaseData loadCase, double fx, double fy, double fz, Guid identity)
        {
            var d = GpcDrafts.Copy(node ?? throw new ArgumentNullException(nameof(node)));
            var c = LegacyModelImport.ElementCase(d.Node, d.Records, loadCase);
            d.Node.Loads.Add(new PointLoad(new GPC.Geometry.Vector3d(fx, fy, fz) * d.Report.Units.Force, LegacyModelImport.Zero, d.Node.Position, c) { Name = LegacyModelImport.ForceRecord });
            d.Records.LoadList.Add((LegacyModelImport.ForceRecord, loadCase.Name)); d.Records.LoadCaseList.Add(loadCase);
            return GpcDrafts.Reidentify(d, identity);
        }

        /// <summary>A new node (Bridge Bearings, the node of a generator): the draft the reader gives a former node with this position (definition
        /// units) and node ID, its groups (each a chain of names from the root to the group, as the former groups with their parents) and nothing
        /// else; number 1, the identity as GUID and owner.</summary>
        public static LegacyNodeDraft Create(double x, double y, double z, string nodeId, IReadOnlyList<IReadOnlyList<string>> groups, Guid identity)
        {
            var units = GpcBeamDrafts.DraftUnits;
            var report = new LegacyImportReport { Units = units, Owner = identity, Bridge = new GPC.Checkers.CompositeBridge.BridgeModelData() };
            var node = LegacyModelImport.NewNode(identity, new GPC.Geometry.Point3d(units.ToMm(x), units.ToMm(y), units.ToMm(z)), nodeId ?? "", 1, identity);
            var groupOf = new DraftGroups();
            foreach (var chain in groups ?? Array.Empty<IReadOnlyList<string>>()) node.AddGroup(groupOf.Of(chain));
            return new LegacyNodeDraft(node, report) { Records = new LegacyElementRecords() };
        }

        /// <summary>A coordinate system of the restraints of the node (former <c>NodeCoordinateAttributeModel</c>, Bridge Bearings): origin, X and Y
        /// axes of the Rhino plane as written. A node with a coordinate system already is refused (the reader accepts a second one only when it is
        /// the same plane; no component of lane A gives one).</summary>
        public static LegacyNodeDraft WithFrame(LegacyNodeDraft node, double ox, double oy, double oz, double xx, double xy, double xz, double yx, double yy, double yz, Guid identity)
        {
            var d = GpcDrafts.Copy(node ?? throw new ArgumentNullException(nameof(node)));
            if (d.Node.Assignments.Restrains.Any(r => r.SourceRecord == LegacyModelImport.FrameRecord)) throw new ArgumentException("GpcNodeBuilderSecondCoordinateSystem");
            d.Node.Assignments.Restrains.Add(LegacyModelImport.FrameAssignment(d.Node,
                LegacyModelImport.FrameOf(ox, oy, oz, new GPC.Geometry.Vector3d(xx, xy, xz), new GPC.Geometry.Vector3d(yx, yy, yz), d.Report.Units)));
            return GpcDrafts.Reidentify(d, identity);
        }

        /// <summary>Node Restraints (and the fixed bearings): a former restraint, fixed or free Tx, Ty, Tz, Rx, Ry, Rz, global axes, after the
        /// restraints of the draft.</summary>
        public static LegacyNodeDraft WithRestraint(LegacyNodeDraft node, bool tx, bool ty, bool tz, bool rx, bool ry, bool rz, Guid identity)
        {
            var d = GpcDrafts.Copy(node ?? throw new ArgumentNullException(nameof(node)));
            d.Node.Assignments.Restrains.Add(LegacyModelImport.RestraintAssignment(d.Node, new[] { tx, ty, tz, rx, ry, rz }));
            return GpcDrafts.Reidentify(d, identity);
        }

        /// <summary>Node Stiffness (and the elastic bearings): a former stiffness, springs Tx, Ty, Tz (force/length) and Rx, Ry, Rz (force·length)
        /// in the units of the draft, a zero spring a free DOF, after the restraints of the draft.</summary>
        public static LegacyNodeDraft WithStiffness(LegacyNodeDraft node, double tx, double ty, double tz, double rx, double ry, double rz, Guid identity)
        {
            var d = GpcDrafts.Copy(node ?? throw new ArgumentNullException(nameof(node)));
            d.Node.Assignments.Restrains.Add(LegacyModelImport.StiffnessAssignment(d.Node, new[] { tx, ty, tz, rx, ry, rz }, d.Report.Units));
            return GpcDrafts.Reidentify(d, identity);
        }

        /// <summary>Element ID on a node: a former <c>ElementIdAttributeModel</c> after the attributes of the node. The reader puts every non-empty
        /// value as an <see cref="GPC.Model.Attributes.ElementLabelAttribute"/> in attribute order and the rigid connections after the attributes:
        /// the label goes after the labels and before the rigid connections (the attributes renumbered as the reader numbers them); an empty value
        /// is carried; the first Element ID of the records is the first value.</summary>
        public static LegacyNodeDraft WithLabel(LegacyNodeDraft node, string value, Guid identity)
        {
            var d = GpcDrafts.Copy(node ?? throw new ArgumentNullException(nameof(node)));
            d.Records.FirstLabel ??= value ?? "";
            if (string.IsNullOrEmpty(value)) { d.Report.Carry("Empty Element ID attribute"); d.Records.EmptyLabels++; return GpcDrafts.Reidentify(d, identity); }
            var attributes = d.Node.Attributes.Values.OrderBy(a => a.Id).ToList();
            var rigid = attributes.OfType<GPC.Model.Attributes.RigidConnectionAttribute>().ToList();
            if (rigid.Count == 0) d.Node.Attributes.Add(new GPC.Model.Attributes.ElementLabelAttribute(value));
            else
            {
                if (attributes.Any(a => !(a is GPC.Model.Attributes.ElementLabelAttribute) && !(a is GPC.Model.Attributes.RigidConnectionAttribute)))
                    throw new NotSupportedException("GpcNodeBuilderUnknownAttribute");
                // The reader adds the labels, then the rigid connections, each with the next id: the same instances in that order.
                var labels = attributes.Where(a => a is GPC.Model.Attributes.ElementLabelAttribute).Select(a => ((GPC.Model.Attributes.ElementLabelAttribute)a).Label).ToList();
                var targets = rigid.Select(r => r.TargetMm).ToList();
                d.Node.Attributes.Clear();
                foreach (var label in labels) d.Node.Attributes.Add(new GPC.Model.Attributes.ElementLabelAttribute(label));
                d.Node.Attributes.Add(new GPC.Model.Attributes.ElementLabelAttribute(value));
                foreach (var target in targets) d.Node.Attributes.Add(new GPC.Model.Attributes.RigidConnectionAttribute(target));
            }
            return GpcDrafts.Reidentify(d, identity);
        }

        /// <summary>Node Moment: a moment on the node in a load case (the reader: a point load with no force and the moment times force·length,
        /// at the position of the node, named <see cref="LegacyModelImport.MomentRecord"/>, after the loads of the draft; the records take the load
        /// and the definition of its case).</summary>
        public static LegacyNodeDraft WithMoment(LegacyNodeDraft node, LegacyCaseData loadCase, double mx, double my, double mz, Guid identity)
        {
            var d = GpcDrafts.Copy(node ?? throw new ArgumentNullException(nameof(node)));
            var c = LegacyModelImport.ElementCase(d.Node, d.Records, loadCase);
            d.Node.Loads.Add(LegacyModelImport.MomentLoad(d.Node, new GPC.Geometry.Vector3d(mx, my, mz), c, d.Report.Units));
            d.Records.LoadList.Add((LegacyModelImport.MomentRecord, loadCase.Name)); d.Records.LoadCaseList.Add(loadCase);
            return GpcDrafts.Reidentify(d, identity);
        }

        /// <summary>Node Temperature: a temperature of the node in a load case, by the name of its former type (Reference, Fixed, Initial; another
        /// name is refused with the message of the reader), after the temperatures of the draft; the records take the load and its case.</summary>
        public static LegacyNodeDraft WithTemperature(LegacyNodeDraft node, LegacyCaseData loadCase, string type, double value, Guid identity)
        {
            var d = GpcDrafts.Copy(node ?? throw new ArgumentNullException(nameof(node)));
            var c = LegacyModelImport.ElementCase(d.Node, d.Records, loadCase);
            LegacyModelImport.AddTemperature(d.Node, LegacyModelImport.Temperature(c, type, value, d.Report.Units));
            d.Records.LoadList.Add((LegacyModelImport.TemperatureRecord, loadCase.Name)); d.Records.LoadCaseList.Add(loadCase);
            return GpcDrafts.Reidentify(d, identity);
        }
    }

    /// <summary>R6.2 lane A: the groups of one new draft by full name, with their parents, as the reader makes them for one former element
    /// (<c>LegacyModelImport.ElementCases</c>): one GPC group per full name (the names of the chain joined by a backslash, GUID by full name), its
    /// parent first, the child added to the parent.</summary>
    internal sealed class DraftGroups
    {
        private readonly Dictionary<string, GPC.Model.Attributes.Group> groups = new Dictionary<string, GPC.Model.Attributes.Group>(StringComparer.Ordinal);

        internal static string FullName(IReadOnlyList<string> chain) => string.Join("\\", chain);

        internal GPC.Model.Attributes.Group Of(IReadOnlyList<string> chain) => Of(chain, chain.Count);

        private GPC.Model.Attributes.Group Of(IReadOnlyList<string> chain, int count)
        {
            if (count < 1) throw new ArgumentException("GpcDraftGroupWithoutName");
            string full = FullName(chain.Take(count).ToArray());
            if (groups.TryGetValue(full, out var known)) return known;
            var parent = count == 1 ? null : Of(chain, count - 1);
            // L2C (lane 2c, passo 76): the name as a string of its own. The full name of a group of one name is that name, the same string as
            // the input; the archive writes a string met twice as a reference to the first, so a property or a label named as the group (one
            // text given to both) was written as a reference to the group name and its record had another XML form than the record the reader
            // gives (its strings are its own). One form only: the record of the reader.
            var group = GPC.Model.Attributes.Group.Create(GpcIdentity.Group(full), new string(full.AsSpan()));
            parent?.AddChild(group);
            groups.Add(full, group);
            return group;
        }
    }
}
