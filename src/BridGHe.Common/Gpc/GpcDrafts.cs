using System;
using System.IO;
using System.Linq;
using System.Text;
using BridGHe.Common.Gpc.Legacy;
using GPC.Model.Elements;
using GPC.Model.Persistence;

namespace BridGHe.Common.Gpc
{
    /// <summary>
    /// R6.1 (frozen API for R6.2, §1bis): the primitives the GPC builders of the producer components start from. A builder never changes
    /// the draft of its input (immutable by convention): it takes a <see cref="Copy(LegacyNodeDraft)"/>, changes the element, its records
    /// and its report as the reader would read the changed former value (<see cref="LegacyModelImport.NodeDraft"/> and the others: same
    /// collections, same order, same records), then gives it the identity of its output with <see cref="Reidentify(LegacyNodeDraft, Guid)"/>
    /// (until R6.5 the identity of the conversion of the FEM output, <see cref="GpcBeamDrafts.Identity(Models.NodeModel)"/>).
    /// Regression <c>gpc-draft-archive</c>: on every node, beam and plate of the corpus, the copy re-identified equals the conversion with that
    /// identity bit for bit.
    /// </summary>
    public static class GpcDrafts
    {
        /// <summary>A deep copy of a node draft (element by contract copy, records, report): the same identity.</summary>
        public static LegacyNodeDraft Copy(LegacyNodeDraft draft)
        {
            if (draft == null) throw new ArgumentNullException(nameof(draft));
            return new LegacyNodeDraft(GPC.Model.Core.ModelValues.CopyValue(draft.Node), Copy(draft.Report, draft.Report.Owner, null)) { Records = draft.Records.Copy() };
        }

        public static LegacyBeamDraft Copy(LegacyBeamDraft draft)
        {
            if (draft == null) throw new ArgumentNullException(nameof(draft));
            var beam = GPC.Model.Core.ModelValues.CopyValue(draft.Beam);
            var report = Copy(draft.Report, draft.Report.Owner, null);
            return new LegacyBeamDraft(beam, report.Bridge.Beam(beam.Guid), draft.AngleDeg, draft.Property, report) { Records = draft.Records.Copy() };
        }

        public static LegacyPlateDraft Copy(LegacyPlateDraft draft)
        {
            if (draft == null) throw new ArgumentNullException(nameof(draft));
            return new LegacyPlateDraft(GPC.Model.Core.ModelValues.CopyValue(draft.Plate), draft.Corners.ToArray(), Copy(draft.Report, draft.Report.Owner, null)) { Records = draft.Records.Copy(), AngleDeg = draft.AngleDeg };
        }

        /// <summary>The draft with another identity: the GUID of the element, the owner of its source identity and of the report, and the key
        /// of its bridge data (as the reader gives a value converted with that identity).</summary>
        public static LegacyNodeDraft Reidentify(LegacyNodeDraft draft, Guid identity)
        {
            if (draft == null) throw new ArgumentNullException(nameof(draft));
            var node = Reidentify(draft.Node, draft.Node.Guid, identity);
            return new LegacyNodeDraft(node, Copy(draft.Report, identity, null)) { Records = draft.Records.Copy() };
        }

        public static LegacyBeamDraft Reidentify(LegacyBeamDraft draft, Guid identity)
        {
            if (draft == null) throw new ArgumentNullException(nameof(draft));
            var beam = Reidentify(draft.Beam, draft.Beam.Guid, identity);
            var report = Copy(draft.Report, identity, (draft.Beam.Guid, identity));
            return new LegacyBeamDraft(beam, report.Bridge.Beam(identity), draft.AngleDeg, draft.Property, report) { Records = draft.Records.Copy() };
        }

        public static LegacyPlateDraft Reidentify(LegacyPlateDraft draft, Guid identity)
        {
            if (draft == null) throw new ArgumentNullException(nameof(draft));
            var plate = Reidentify(draft.Plate, draft.Plate.Guid, identity);
            return new LegacyPlateDraft(plate, draft.Corners.ToArray(), Copy(draft.Report, identity, null)) { Records = draft.Records.Copy(), AngleDeg = draft.AngleDeg };
        }

        /// <summary>An element with another GUID: its contract archive with the old GUID (the element and the owner of its source identity, both
        /// written as "D") replaced, read back. Nothing else of a draft element carries that GUID (cases, groups and strings by name).</summary>
        private static T Reidentify<T>(T element, Guid from, Guid to) where T : Element
        {
            if (from == to) return GPC.Model.Core.ModelValues.CopyValue(element);
            string xml;
            using (var stream = new MemoryStream()) { ModelValueArchive.Save(element, stream); xml = Encoding.UTF8.GetString(stream.ToArray()); }
            string a = from.ToString("D"), b = to.ToString("D");
            if (xml.IndexOf(a, StringComparison.OrdinalIgnoreCase) < 0) throw new InvalidOperationException("GpcDraftIdentityNotFound");
            xml = xml.Replace(a, b).Replace(a.ToUpperInvariant(), b.ToUpperInvariant());
            using (var back = new MemoryStream(Encoding.UTF8.GetBytes(xml))) return ModelValueArchive.Load<T>(back);
        }

        /// <summary>A copy of a beam draft report whose bridge data are those of the beam given (none: empty), the owner and the rest kept.</summary>
        internal static LegacyImportReport WithBridge(LegacyImportReport report, GPC.Checkers.CompositeBridge.BridgeBeamData bridge)
        {
            var copy = Copy(report, report.Owner, null);
            var data = new GPC.Checkers.CompositeBridge.BridgeModelData { Workflow = report.Bridge.Workflow };
            if (bridge != null) data.Set(bridge);
            copy.Bridge = data;
            return copy;
        }

        /// <summary>A copy of a draft report with the owner given (and the bridge data of one beam moved to another GUID).</summary>
        private static LegacyImportReport Copy(LegacyImportReport report, Guid owner, (Guid From, Guid To)? beam)
        {
            var copy = new LegacyImportReport { Units = report.Units, Owner = owner, RegisteredCases = report.RegisteredCases, Bridge = new GPC.Checkers.CompositeBridge.BridgeModelData { Workflow = report.Bridge.Workflow } };
            foreach (var b in report.Bridge.Beams)
            {
                var key = beam.HasValue && b.BeamGuid == beam.Value.From ? beam.Value.To : b.BeamGuid;
                copy.Bridge.Set(new GPC.Checkers.CompositeBridge.BridgeBeamData(key, b.Member, b.SuperElement, b.SectionState, b.CompositeSection));
            }
            copy.Warnings.AddRange(report.Warnings);
            foreach (var p in report.NotExported) copy.NotExported.Add(p.Key, p.Value);
            copy.ElementWarnings.AddRange(report.ElementWarnings);
            foreach (var p in report.BeamAngles) copy.BeamAngles.Add(p.Key, p.Value);
            foreach (var p in report.NativeSections) copy.NativeSections.Add(p.Key, p.Value);
            foreach (var p in report.CaseOrigins) copy.CaseOrigins.Add(p.Key, p.Value);
            foreach (var p in report.BeamProperties) copy.BeamProperties.Add(p.Key, p.Value);
            copy.ShadowedPropertyBeams.UnionWith(report.ShadowedPropertyBeams);
            copy.ElementLoadCases.AddRange(report.ElementLoadCases);
            return copy;
        }
    }
}
