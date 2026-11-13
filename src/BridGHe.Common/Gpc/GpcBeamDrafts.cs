using System;
using System.Linq;
using System.Security.Cryptography;
using BridGHe.Common.Gpc.Legacy;
using BridGHe.Common.Helpers;

namespace BridGHe.Common.Gpc
{
    /// <summary>
    /// Chain E (d): the GPC draft of a beam upstream of Build Model (§1bis, L12 §5.4: the <c>GH_Beam</c> carries the <see cref="GPC.Model.Elements.BeamElement"/>
    /// and its <see cref="GPC.Checkers.CompositeBridge.BridgeBeamData"/>), while the beams are still FEM values: the per-value conversion of the
    /// legacy reader (<see cref="LegacyModelImport.BeamDraft"/>, the rules of the conversion of a whole model), numbers in the definition units
    /// (Build Model normalises). The results of the former value are not converted. Identity: from the content of the former value (the same
    /// value gives the same GUID), or the one given (a GPC transformer keeps the GUID of its input beam; the comparisons convert its FEM output
    /// with it).
    /// </summary>
    public static class GpcBeamDrafts
    {
        /// <summary>Draft units: the numbers of the definition (identity factors).</summary>
        public static readonly BridgeUnits DraftUnits = new BridgeUnits(DefinitionUnits.Nmm);







        /// <summary>A copy of a draft with another beam and bridge data (the report and the property data of the draft are kept).</summary>
        /// <remarks>R6.1 (found by the former draft check on the native 50): the bridge data of the report follow the bridge data given (the report of the
        /// conversion of a beam value holds the data of that beam), so the copy describes its beam as the conversion would; the rest of the report
        /// is kept. Nothing reads the bridge data of a draft report (the assembly takes the bridge data of the draft): no number changes.</remarks>
        public static LegacyBeamDraft With(LegacyBeamDraft draft, GPC.Model.Elements.BeamElement beam, GPC.Checkers.CompositeBridge.BridgeBeamData bridge, LegacyBeamPropertyData property = null) =>
            new LegacyBeamDraft(beam, bridge, draft.AngleDeg, property ?? draft.Property, GpcDrafts.WithBridge(draft.Report, bridge)) { Records = draft.Records };
    }
}
