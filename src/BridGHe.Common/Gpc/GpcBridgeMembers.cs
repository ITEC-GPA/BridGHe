using System.Collections.Generic;
using System.Linq;

namespace BridGHe.Common.Gpc
{
    /// <summary>F6: the bridge identity of the beams of a GPC model (physical member and family from Bridge Member Assignment), read in one
    /// place. Today the bridge metadata of the parts of <c>Model.PhysicalMembers</c> (the legacy reader puts it there); the bridge vocabulary
    /// leaves GPCModel with lane L12 (<c>BridgeModel</c> of the Checker, user decision of 5/11), and only this helper changes.</summary>
    internal static class GpcBridgeMembers
    {
        internal sealed record Member(string PhysicalId, bool Girder);

        /// <summary>The bridge member of every beam with one, by beam number.</summary>
        internal static Dictionary<int, Member> Of(GPC.Model.Models.Model model)
        {
            var result = new Dictionary<int, Member>();
            foreach (var definition in model.PhysicalMembers.Values)
                foreach (var part in definition.Parts.Where(p => p.BridgeMetadata != null))
                    result[part.BeamId] = new Member(part.BridgeMetadata.PhysicalId, part.BridgeMetadata.Family == GPC.Model.Structure.Members.BridgeMemberFamily.Girder);
            return result;
        }

        /// <summary>The prefix of the identity of a super element member (<c>LegacyModelImport</c>).</summary>
        internal const string SuperElementSource = "BridGHe super element";
    }
}
