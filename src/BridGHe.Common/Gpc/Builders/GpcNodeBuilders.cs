using System;
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
    }
}
