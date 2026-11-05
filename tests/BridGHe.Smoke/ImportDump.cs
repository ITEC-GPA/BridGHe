#if GPC_PILOT
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using BridGHe.Common.Gpc;
using BridGHe.Common.Gpc.Legacy;
using BridGHe.Common.Models;
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
/// </summary>
internal static class ImportDump
{
    private const string Results = @"C:\Users\g.pacini\Documents\Repository\gpc-f6-claude\P2B\results";

    internal static int Run(string[] args)
    {
        string output = args.SkipWhile(a => a != "--import-dump").Skip(1).FirstOrDefault() ?? throw new ArgumentException("--import-dump <output folder>");
        Directory.CreateDirectory(output);
        int written = 0, failed = 0;
        void Dump(string id, byte[] archive, ModelModel fem, double tolerance)
        {
            var report = new LegacyImportReport();
            string text;
            try
            {
                var model = LegacyModelImport.Import(LegacyArchive.Read(archive), GpcIdentity.Derive(GpcIdentity.NameSpace, "import-dump:" + id), tolerance, report,
                    fem == null ? null : GpcModels.SectionStateSignatures(fem));
                string fingerprint = model.AnalysisFingerprint();
                text = "FINGERPRINT " + fingerprint + "\n" + Report(report) + "\nDOCUMENT\n" + (GpcInputComparison.Document(model) ?? "beyond the serializer limit");
            }
            catch (Exception e) when (e is ArgumentException || e is NotSupportedException || e is InvalidOperationException) { text = "ERROR " + e.GetType().Name + ": " + e.Message; failed++; }
            File.WriteAllText(Path.Combine(output, id + ".txt"), text);
            written++;
        }
        foreach (var file in Directory.GetFiles(Path.Combine(Results, "capture-1"), "*.bgmodel").OrderBy(f => f, StringComparer.Ordinal))
        {
            string info = Path.ChangeExtension(file, ".txt");
            double tolerance = File.Exists(info) ? double.Parse(File.ReadAllLines(info)[1], CultureInfo.InvariantCulture) : 1e-6;
            var bytes = File.ReadAllBytes(file);
            Dump("capture-" + Path.GetFileNameWithoutExtension(file), bytes, BridGHe.Common.Helpers.ModelArchive.Read<ModelModel>(bytes), tolerance);
        }
        foreach (var file in Directory.GetFiles(Path.Combine(Results, "examples-1"), "*.bgmodel", SearchOption.AllDirectories).OrderBy(f => f, StringComparer.Ordinal))
        {
            var bytes = File.ReadAllBytes(file);
            Dump("examples-" + Path.GetFileName(Path.GetDirectoryName(file)) + "-" + Path.GetFileNameWithoutExtension(file), bytes, BridGHe.Common.Helpers.ModelArchive.Read<ModelModel>(bytes), 1e-6);
        }
        foreach (var example in NativeExamples.Create())
            Dump("memory-" + example.Id, BridGHe.Common.Helpers.ModelArchive.Write(example.Model), example.Model, 1e-6);
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
        s.Append("BRIDGE ").Append(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(r.Bridge.Save()))).Append(' ').Append(r.Bridge.Beams.Count()).Append('\n');
        return s.ToString();
    }
}
#endif
