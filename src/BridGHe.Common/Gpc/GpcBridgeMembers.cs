using System.Collections.Generic;

namespace BridGHe.Common.Gpc
{
    /// <summary>F6: the bridge identity of the beams of a GPC model (physical member and family from Bridge Member Assignment), read in one
    /// place: the bridge data of the model (<see cref="GPC.Checkers.CompositeBridge.BridgeModelData"/>, by beam GUID; lane L12, user decision
    /// of 5/11: the bridge vocabulary is outside GPCModel).</summary>
    internal static class GpcBridgeMembers
    {
        internal sealed record Member(string PhysicalId, bool Girder);

        /// <summary>The bridge member of every beam with one, by beam number.</summary>
        internal static Dictionary<int, Member> Of(GPC.Model.Models.Model model, GPC.Checkers.CompositeBridge.BridgeModelData data)
        {
            var result = new Dictionary<int, Member>();
            foreach (var beam in model.BeamElements.Values)
            {
                var m = data?.Member(beam.Guid);
                if (m != null) result[beam.Id] = new Member(m.PhysicalId, m.Family == GPC.Checkers.CompositeBridge.Layout.BridgeMemberFamily.Girder);
            }
            return result;
        }
    }
}
