using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using BridGHe.Common.Gpc;
using BridGHe.Common.Gpc.Legacy;
using GPC.Model.Models;

/// <summary>
/// Chain E (e2, P2J): the conversion of the legacy reader written to text, model by model, so that two builds can be compared file by file
/// (the conversion before and after the split of <see cref="LegacyModelImport.Import"/> into element drafts and the assembly): the canonical
/// XML document of the model (<see cref="GpcInputComparison.Document"/>, after the analysis fingerprint completes the lazy values), the
/// analysis fingerprint, and every field of the report (warnings in order, carried data, BridGHe angles, case origins, property data,
/// shadowed beams, element cases and warnings, native sections, bridge data). Corpus: the 77 captured FEM inputs of the former exporter
/// (<c>results/capture-1</c>, with their tolerance), the examples 01–10 captured with it (<c>results/examples-1</c>) and the examples 01–10
/// built in memory (<see cref="NativeExamples.Create"/>). Not native, writes only into the given folder.
/// Usage: <c>--import-dump &lt;output folder&gt;</c>.
/// <para>R6.6 lane R3 (11/11): every model is read as bytes (<see cref="R3LegacyCorpus"/>), with no FEM class: the captures and the captured
/// examples from their files, the node, beam and plate data and the refused duplicates from their frozen payloads (the bytes the FEM writer
/// gave for the former node, beam and plate data, same ids), the former section-state signatures from the fixture
/// (<see cref="R3LegacyCorpus.Signatures"/>). P2CC (13/11/2026): the FEM beam builder went too (its callers read the frozen beams-Knm). P2CA (13/11/2026, U3): the examples 01–10 built in memory (<c>memory-*</c>, the FEM catalogue written by the FEM writer, no
/// frozen payload) are retired by name (<see cref="P2rRetired.Checks"/>); the node builder went with the FEM writer of the fixtures.</para>
/// </summary>
internal static class ImportDump
{
    internal static int Run(string[] args)
    {
        string output = args.SkipWhile(a => a != "--import-dump").Skip(1).FirstOrDefault() ?? throw new ArgumentException("--import-dump <output folder>");
        string only = args.SkipWhile(a => a != "--import-dump").Skip(2).FirstOrDefault(a => !a.StartsWith("--"));
        Directory.CreateDirectory(output);
        int written = 0, failed = 0;
        void Dump(string id, byte[] archive, Func<int, string> signatures, double tolerance) { if (only == null || id.StartsWith(only, StringComparison.Ordinal)) DumpOne(id, archive, signatures, tolerance); }
        void DumpOne(string id, byte[] archive, Func<int, string> signatures, double tolerance)
        {
            var report = new LegacyImportReport();
            string text;
            try
            {
                var model = LegacyModelImport.Import(LegacyArchive.Read(archive), GpcIdentity.Derive(GpcIdentity.NameSpace, "import-dump:" + id), tolerance, report, signatures);
                string fingerprint = model.AnalysisFingerprint();
                text = "FINGERPRINT " + fingerprint + "\n" + Report(report) + "\nDOCUMENT\n" + (GpcInputComparison.Document(model) ?? "beyond the serializer limit");
            }
            catch (Exception e) when (e is ArgumentException || e is NotSupportedException || e is InvalidOperationException) { text = "ERROR " + e.GetType().Name + ": " + e.Message; failed++; }
            File.WriteAllText(Path.Combine(output, id + ".txt"), text);
            written++;
        }
        void Item(R3LegacyCorpus.Item item) => Dump(item.Id, item.Bytes, item.Signatures, item.Tolerance);
        foreach (var item in R3LegacyCorpus.Captures()) Item(item);
        foreach (var item in R3LegacyCorpus.Examples()) Item(item);
        // P2CA (13/11/2026, U3): the examples 01-10 built in memory by the FEM catalogue and written by the FEM writer (memory-*) are retired by
        // name (P2rRetired.Checks, scope import-dump); the same ten examples as 0.3.0 saved them stay in the corpus (examples-*).
        Console.WriteLine($"RETIRED BY NAME (P2CA): {P2rRetired.Checks.Count(c => c.Key.StartsWith("import-dump|", StringComparison.Ordinal))} conversions of FEM models written by the FEM writer (P2rRetired.Checks)");
        // e3 (P2J): node data the corpus above lacks (rigid links, a labelled node with a rigid link, two labels, restraints with springs
        // and a coordinate system, two stiffnesses, forces and moments in one case, temperature kinds), in every unit system; a labelled node
        // with a rigid link: the F6 reader refused it (Model.AddCostrain -> AddNode wants case names for node attributes).
        // e3 (P2K): beam and plate data at list level (two Element IDs before another attribute, groups, stations, releases, taper, string
        // group, supports, super elements, bridge members, section states, every beam load kind, plate supports, load patch, pressures,
        // prestress), in every unit system, and one model per refused duplicate. R3: their frozen payloads.
        foreach (var item in R3LegacyCorpus.Fixtures()) Item(item);
        Console.WriteLine($"IMPORT DUMP: {written} conversions written to {output} ({failed} with the same kind of error text)");
        return 0;
    }

    private static string B(double v) => BitConverter.DoubleToInt64Bits(v).ToString("X16");

    internal static string Report(LegacyImportReport r)
    {
        var s = new StringBuilder();
        s.Append("UNITS ").Append(r.Units.Units.ToString()).Append('\n');
        s.Append("OWNER ").Append(r.Owner).Append('\n');
        s.Append("REGISTERED ").Append(r.RegisteredCases).Append('\n');
        foreach (var w in r.Warnings) s.Append("WARNING ").Append(w).Append('\n');
        foreach (var p in r.NotExported) s.Append("CARRY ").Append(p.Key).Append(" = ").Append(p.Value).Append('\n');
        foreach (var p in r.NativeSections) s.Append("NATIVE ").Append(p.Key).Append(' ').Append(p.Value?.GetType().Name ?? "null").Append('\n');
        foreach (var p in r.BeamAngles) s.Append("ANGLE ").Append(p.Key).Append(' ').Append(B(p.Value)).Append('\n');
        foreach (var p in r.CaseOrigins) s.Append("ORIGIN ").Append(p.Key).Append(' ').Append(p.Value).Append('\n');
        foreach (var p in r.BeamProperties) s.Append("PROPERTY ").Append(p.Key).Append(' ').Append(p.Value.Formulation).Append(' ').Append(B(p.Value.Angle)).Append(' ')
            .Append(p.Value.Native == null ? "-" : "native").Append(' ').Append(p.Value.Signature).Append('\n');
        foreach (var k in r.ShadowedPropertyBeams.OrderBy(k => k)) s.Append("SHADOWED ").Append(k).Append('\n');
        foreach (var c in r.ElementLoadCases) s.Append("CASES ").Append(c.Family).Append(':').Append(c.Number).Append(' ').Append(string.Join(",", c.Cases)).Append('\n');
        foreach (var w in r.ElementWarnings) s.Append("ELEMENT WARNING ").Append(w.Family).Append(':').Append(w.Number).Append(' ').Append(w.Restraint).Append(' ').Append(w.Format).Append('\n');
        // P2K: the archive of the bridge data differs between two runs, so every record is written instead of its digest.
        s.Append("BRIDGE ").Append(r.Bridge.Beams.Count()).Append('\n');
        foreach (var b in r.Bridge.Beams) s.Append("BRIDGE BEAM ").Append(b.BeamGuid).Append(" member ").Append(b.Member == null ? "-" : GPC.Model.Core.ModelValues.Fingerprint(new object[] { b.Member })).Append(" super ").Append(b.SuperElement ?? "-")
            .Append(" state ").Append(b.SectionState == null ? "-" : $"{b.SectionState.Behavior} {b.SectionState.ConcreteExcluded} {B(b.SectionState.ModularRatio)} {b.SectionState.ClassificationCase ?? "-"} {b.SectionState.PropertyFingerprint}").Append(" composite ").Append(b.CompositeSection == null ? "-" : "yes").Append('\n');
        return s.ToString();
    }
}
