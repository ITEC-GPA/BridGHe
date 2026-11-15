using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using BridGHe.Common.Gpc;
using BridGHe.Common.Gpc.Legacy;
using GPC.Model.Elements;

/// <summary>
/// Chain E (e3, P2K): regression <c>gpc-draft-merge</c> (not native). The merge by GH GUID of two element drafts
/// (<see cref="LegacyBuildModel.Merge(LegacyBeamDraft, LegacyBeamDraft)"/> and the node and plate overloads), resolved
/// (<see cref="LegacyAssembly.ResolveNode"/>, <see cref="LegacyAssembly.ResolveBeam"/>, <see cref="LegacyAssembly.ResolvePlate"/>), against the
/// draft of the former merge (<c>new XModel(first).Merge(other)</c>, the 0.3.0 <c>ModelHelper.BuildModel</c>) resolved the same way: the
/// element (loads with their records, attributes with their ids, assignments, groups), the list-level records, the bridge data and the
/// warnings, bit for bit, or the same error. Cases: the same datum merged with itself, two branches of one datum (each with its own loads
/// and attributes), loads in the stage-less "Dead" case, two loads equal within the other datum, Element IDs, super elements, bridge
/// members and section states (always appended), releases, supports, taper, string group, output stations, restraints, springs,
/// coordinate systems, rigid connections, temperatures, plate pressures, prestress, supports and load patch.
/// <para><b>P2CA (13/11/2026, user decision «Sì, congelale», about 07:00):</b> the oracle is the frozen fixture <c>Fixtures/draft-merge</c>,
/// written once by the former FEM merge (the input drafts of every comparison in BGHGPC1 and the expected texts, with their SHA-256; generator:
/// commit d922b94, <c>BRIDGHE_DRAFT_MERGE_FIXTURE=write:&lt;folder&gt; --regression gpc-draft-merge</c>, see its PROVENIENZA.md). The regression
/// merges the drafts read from the fixture and compares the resolved text with the frozen one; no FEM class.</para>
/// </summary>
internal static class GpcDraftMergeChecks
{
    private static string Fp(params object[] values) => GPC.Model.Core.ModelValues.Fingerprint(values);

    private static string Text(Element e, LegacyElementRecords records, GPC.Checkers.CompositeBridge.BridgeBeamData bridge, LegacyImportReport report)
    {
        var parts = new List<string> { "name " + e.Name, "groups " + string.Join(",", e.Groups.Values.Select(g => g.Name)), "loads " + Fp(e.Loads.Values.ToArray()), "attributes " + Fp(e.Attributes.Values.ToArray()),
            "attribute ids " + string.Join(",", e.Attributes.Keys), "attributes max " + e.Attributes.MaxId };
        if (e is NodeElement n) parts.AddRange(new[] { "position " + Fp(n.Position), "restraints " + Fp(n.Assignments.Restrains.Select(r => new object[] { r.SourceRecord, r.Restrain?.CoordinateSystem, r.Restrain?.Restrains }).ToArray()),
            "physical " + Fp(n.Assignments.Physical) });
        if (e is BeamElement b) parts.AddRange(new[] { "ends " + Fp(b.StartPoint, b.EndPoint), "beam loads " + Fp(b.Assignments.Loads.ToArray()),
            "beam load records " + string.Join(",", b.Assignments.Loads.Select(l => l.OriginalAssignmentId + "~" + l.SourceRecord)), "physical " + Fp(b.Assignments.Physical),
            "axes " + Fp(b.Assignments.SectionAxes, b.Assignments.OffsetAxes, b.Assignments.OffsetI, b.Assignments.OffsetJ), "property " + Fp(b.BeamProperty) });
        if (e is AreaElement p) parts.AddRange(new[] { "physical " + Fp(p.Assignments.Physical), "offset " + p.Assignments.Offset, "property " + Fp(p.PlateProperty) });
        if (records != null) parts.AddRange(new[] { "record groups " + string.Join(",", records.Groups), "record labels " + string.Join(",", records.Labels), "record super elements " + string.Join(",", records.SuperElements),
            $"record counts {records.Members} {records.SectionStates} {records.Tapers}", "record loads " + string.Join(",", records.LoadRecords.Select(l => l.Kind + ":" + l.Case)) });
        if (bridge != null) parts.Add("bridge " + (bridge.Member == null ? "-" : Fp(bridge.Member)) + " " + (bridge.SuperElement ?? "-") + " " + (bridge.SectionState == null ? "-" : bridge.SectionState.Behavior + " " + bridge.SectionState.PropertyFingerprint));
        parts.Add("warnings " + string.Join(" | ", report.Warnings)); parts.Add("element warnings " + string.Join(" | ", report.ElementWarnings.Select(w => w.Family + ":" + w.Number + ":" + w.Format)));
        parts.Add("carried " + string.Join(" | ", report.NotExported.Select(x => x.Key + "=" + x.Value)));
        return string.Join("\n", parts);
    }

    private static string Resolved(Func<(Element Element, LegacyElementRecords Records, GPC.Checkers.CompositeBridge.BridgeBeamData Bridge, LegacyImportReport Report)> draft)
    {
        try
        {
            var d = draft(); var report = new LegacyImportReport();
            foreach (var x in d.Report.NotExported) report.NotExported[x.Key] = x.Value;
            if (d.Element is NodeElement n) LegacyAssembly.ResolveNode(n, report);
            else if (d.Element is BeamElement b) LegacyAssembly.ResolveBeam(b, d.Records, report);
            else LegacyAssembly.ResolvePlate((AreaElement)d.Element, d.Records, report);
            return Text(d.Element, d.Element is NodeElement ? null : d.Records, d.Bridge, report);
        }
        catch (Exception e) when (e is ArgumentException || e is NotSupportedException || e is InvalidOperationException) { return "ERROR " + e.Message; }
    }

    /// <summary>P2CA (13/11/2026, user decision «Sì, congelale»): the folder of the frozen fixture (from the root of the BridGHe checkout, the
    /// working folder of the Smoke runs).</summary>
    internal static string Folder => Path.GetFullPath(Path.Combine("tests", "BridGHe.Smoke", "Fixtures", "draft-merge"));

    private static string Sha(byte[] b) => Convert.ToHexString(SHA256.HashData(b));

    /// <summary>The bytes of a file of the fixture, checked against its size and SHA-256.</summary>
    private static byte[] Checked(string path, string bytes, string sha)
    {
        var b = File.ReadAllBytes(path);
        if (b.Length.ToString(CultureInfo.InvariantCulture) != bytes || Sha(b) != sha) throw new Exception("GPC DRAFT MERGE FAIL: " + path + " differs from its size and SHA-256 in the fixture");
        return b;
    }

    internal static void Regression()
    {
        int compared = 0, errors = 0;
        // The drafts of the fixture (data and records in BGHGPC1), checked against drafts.tsv.
        var drafts = new Dictionary<string, (byte[] Data, byte[] Records)>(StringComparer.Ordinal);
        foreach (var c in File.ReadLines(Path.Combine(Folder, "drafts.tsv")).Skip(1).Where(l => l.Length > 0).Select(l => l.Split('\t')))
            drafts.Add(c[0], (Checked(Path.Combine(Folder, "drafts", c[0] + ".data"), c[2], c[3]), c[4] == "-1" ? null : Checked(Path.Combine(Folder, "drafts", c[0] + ".records"), c[4], c[5])));
        foreach (var c in File.ReadLines(Path.Combine(Folder, "index.tsv")).Skip(1).Where(l => l.Length > 0).Select(l => l.Split('\t')))
        {
            string label = c[0], kind = c[1];
            var text = File.ReadAllBytes(Path.Combine(Folder, "expected", c[4]));
            if (Sha(text) != c[5]) throw new Exception("GPC DRAFT MERGE FAIL: the expected text of " + label + " differs from its SHA-256 in the fixture");
            string expected = Encoding.UTF8.GetString(text);
            var (x, y) = (drafts[c[2]], drafts[c[3]]);
            // The merge of the two drafts read back from the fixture, resolved (the GPC merge of Build Model), against the frozen text of the former merge.
            string actual = kind switch
            {
                "node" => Resolved(() => { var d = LegacyBuildModel.Merge(GpcDraftArchive.ReadNode(x.Data, x.Records), GpcDraftArchive.ReadNode(y.Data, y.Records)); return (d.Node, d.Records, null, d.Report); }),
                "beam" => Resolved(() => { var d = LegacyBuildModel.Merge(GpcDraftArchive.ReadBeam(x.Data, x.Records), GpcDraftArchive.ReadBeam(y.Data, y.Records)); return (d.Beam, d.Records, d.Bridge, d.Report); }),
                "plate" => Resolved(() => { var d = LegacyBuildModel.Merge(GpcDraftArchive.ReadPlate(x.Data, x.Records), GpcDraftArchive.ReadPlate(y.Data, y.Records)); return (d.Plate, d.Records, null, d.Report); }),
                _ => throw new Exception("GPC DRAFT MERGE FAIL: unknown kind " + kind),
            };
            if (expected != actual)
            {
                var e = expected.Split('\n'); var a = actual.Split('\n');
                int line = Enumerable.Range(0, Math.Min(e.Length, a.Length)).FirstOrDefault(i => e[i] != a[i], -1);
                throw new Exception($"GPC DRAFT MERGE FAIL ({label}): " + (line < 0 ? $"{e.Length} / {a.Length} lines" : $"former '{e[line]}' / GPC '{a[line]}'"));
            }
            if ((c[6] == "True") != expected.StartsWith("ERROR ", StringComparison.Ordinal)) throw new Exception("GPC DRAFT MERGE FAIL: the error flag of " + label + " differs from its text");
            compared++; if (expected.StartsWith("ERROR ", StringComparison.Ordinal)) errors++;
        }
        if (compared != 17) throw new Exception("GPC DRAFT MERGE FAIL: " + compared + " cases in the fixture, 17 expected");
        Console.WriteLine($"GPC DRAFT MERGE PASS: {compared} merges of node, beam and plate drafts equal to the drafts of the former merge after the resolution ({errors} with the same error)");
    }
}
