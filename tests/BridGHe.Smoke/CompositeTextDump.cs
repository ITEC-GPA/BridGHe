using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using Grasshopper.Kernel;

/// <summary>
/// P2X (8/11, steel 3b40e2a0 in BridGHe): the canonical texts of the composite values of the native capture (keys <c>composite|…</c>, kept
/// there only as SHA-256) from the inputs of the native runs of 50 (the frozen REV003 input) and 99 (REV004, the migrated candidate), solved
/// without analyses (Straus R3 Analysis, Moving Static Analysis and Bridge Report locked: nothing is written next to the definitions).
/// Diagnosis for the declared capture rule <see cref="NativeParityCapture.SteelRule"/>: run with the Checker libraries before and after, the
/// SHA-256 of the texts are the captured values and the new ones, and the texts give the size of each difference.
/// <c>--composite-texts &lt;output .jsonl&gt;</c>: one JSON line per value (label, key, FEM text, definition text).
/// </summary>
internal static class CompositeTextDump
{
    internal static int Run(string output)
    {
        BridgeExampleMigration.Initialize();
        BridgeMergeTests.Initialize();
        using var writer = new StreamWriter(output, false, new UTF8Encoding(false)) { NewLine = "\n" };
        var inputs = new[]
        {
            ("50", BridgeMovingRevisionExample.CaptureInput),
            ("99", Path.GetFullPath(Path.Combine("artifacts/modular-bridge/candidates", "99-Ponte completo GPC", "REV004", "flusso-acciaio-misto_REV004.ghx"))),
        };
        foreach (var (label, file) in inputs)
        {
            if (!File.Exists(file)) throw new FileNotFoundException("COMPOSITE TEXTS: input missing", file);
            using var doc = GrasshopperBridgeExample.Read(file); doc.FilePath = file;
            foreach (var o in doc.Objects.OfType<IGH_ActiveObject>())
                if (o.GetType().Name is "StrausR3AnalysisComponent" or "MovingStaticAnalysisComponent" or "BridgeReportComponent") o.Locked = true;
            BridgeSegmentExcelTests.Solve(doc, true);
            foreach (var read in doc.Objects.OfType<GH_Component>().Where(c => c.GetType().Name == "BridgeSectionReadComponent")) { BridgeSegmentExcelTests.Press(read); read.ExpireSolution(false); }
            BridgeSegmentExcelTests.Solve(doc, true);
            int count = CompositeAnalysisPropertyChecks.Texts(doc, label, (key, fem, gpc) =>
                writer.WriteLine(JsonSerializer.Serialize(new { label, key, fem, gpc, femValue = NativeParityCapture.Value(fem), gpcValue = NativeParityCapture.Value(gpc) })));
            Console.WriteLine($"COMPOSITE TEXTS {label}: {count} values from {Path.GetFileName(file)}");
        }
        Console.WriteLine("COMPOSITE TEXTS PASS: " + output);
        return 0;
    }
}
