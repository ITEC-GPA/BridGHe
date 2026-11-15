using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace BridGHe.Common.Reports;

/// <summary>Task 195 (15/11/2026): how the Word report is written.</summary>
public enum BridgeReportLayout
{
    /// <summary>One document up to <see cref="BridgeWordReport.DefaultAttachmentResults"/> results; above, an index with attachments (user decision of 15/11/2026).</summary>
    Automatic,
    /// <summary>One document with every chapter and the embedded numerical archive (the report up to 0.4.0).</summary>
    Single,
    /// <summary>An index with the global chapters and the attachments with the per-result chapters, grouped by combination.</summary>
    IndexWithAttachments,
}

/// <summary>One attachment of a divided report: the results of one combination (a part of it when the combination has more results than the
/// limit, or several consecutive small combinations together), in the order of the report.</summary>
internal sealed record BridgeReportPart(int Number, string Code, string File, string[] Combinations, int Piece, int Pieces, BridgeReportSample[] Samples)
{
    /// <summary>The text of the combinations of the part, as the index and the attachment show it.</summary>
    internal string Description => (Combinations.Length == 1 ? "combinazione " + Combinations[0] : "combinazioni " + string.Join(", ", Combinations))
        + (Pieces > 1 ? ", parte " + Piece + " di " + Pieces : "");
}

/// <summary>Task 195: the plan of a report divided into an index and attachments (variant C of task 186, chosen by the user on 15/11/2026).
/// The results are grouped by combination in the order of the model (then by first appearance; results without FEM provenance last); a
/// combination with more results than the limit is divided into equal consecutive parts and consecutive small combinations share an attachment
/// up to the limit. Within an attachment the results keep the order of the report. The governing cases are selected once on all the results,
/// before the division (<see cref="Cards"/>): no attachment computes its own maxima.</summary>
internal sealed class BridgeReportSplit
{
    internal const string NoSource = "senza provenienza FEM";
    internal string Stem { get; }
    internal string IndexFile { get; }
    internal string Folder { get; }
    internal string Archive { get; }
    internal IReadOnlyList<BridgeReportPart> Parts { get; }
    /// <summary>The cards of the governing cases of the whole report, in the order of the single document (one per result with its rows).</summary>
    internal IReadOnlyList<(BridgeReportSample Sample, BridgeReportRow[] Rows)> Cards { get; }
    private readonly Dictionary<string, BridgeReportPart> partOf;
    private readonly HashSet<string> carded;

    private BridgeReportSplit(string output, IReadOnlyList<BridgeReportPart> parts, IReadOnlyList<(BridgeReportSample, BridgeReportRow[])> cards)
    {
        Stem = Path.GetFileNameWithoutExtension(output); IndexFile = Path.GetFileName(output);
        Folder = Stem + "_allegati"; Archive = Stem + "_archivio.zip";
        Parts = parts; Cards = cards;
        partOf = parts.SelectMany(p => p.Samples.Select(s => (s.Id, p))).ToDictionary(x => x.Id, x => x.p, StringComparer.Ordinal);
        carded = cards.Select(c => c.Item1.Id).ToHashSet(StringComparer.Ordinal);
    }

    internal BridgeReportPart PartOf(string sampleId) => partOf[sampleId];
    internal bool HasCard(string sampleId) => carded.Contains(sampleId);

    /// <summary>The relative URI of an attachment from the index (folder/file), its path segments escaped.</summary>
    internal string LinkFromIndex(BridgeReportPart part) => Uri.EscapeDataString(Folder) + "/" + Uri.EscapeDataString(part.File);
    /// <summary>The relative URI of the index from an attachment (one folder up).</summary>
    internal string LinkToIndex => "../" + Uri.EscapeDataString(IndexFile);

    /// <summary>The attachment codes of results, consecutive numbers as ranges («A01–A36»), as <see cref="BridgeReportData.ResultIds"/> does for results.</summary>
    internal string Codes(IEnumerable<string> sampleIds)
    {
        var numbers = sampleIds.Select(id => PartOf(id).Number).Distinct().OrderBy(n => n).ToArray();
        var runs = new List<string>();
        for (int i = 0; i < numbers.Length;)
        {
            int j = i; while (j + 1 < numbers.Length && numbers[j + 1] == numbers[j] + 1) j++;
            runs.Add(j - i >= 2 ? Parts[numbers[i] - 1].Code + "–" + Parts[numbers[j] - 1].Code
                : string.Join(", ", Enumerable.Range(i, j - i + 1).Select(k => Parts[numbers[k] - 1].Code)));
            i = j + 1;
        }
        return string.Join(", ", runs);
    }

    internal static BridgeReportSplit Plan(BridgeReportData data, string output, int limit)
    {
        if (limit < 1) throw new ArgumentException("The number of results of an attachment must be positive.");
        // The combinations in the order of the model table, then those found only in the results, then the results without provenance.
        var order = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var c in data.Model?.Combinations ?? Array.Empty<BridgeReportCombination>()) order.TryAdd(c.Name ?? "", order.Count);
        var groups = new List<(string Key, List<BridgeReportSample> Samples)>(); var index = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var s in data.Samples)
        {
            string key = s.Source == null ? null : s.Source.Combination ?? "";
            string slot = key ?? "\0";
            if (!index.TryGetValue(slot, out int g)) { index.Add(slot, g = groups.Count); groups.Add((key, new List<BridgeReportSample>())); }
            groups[g].Samples.Add(s);
        }
        var ordered = groups.Select((g, i) => (g, i)).OrderBy(x => x.g.Key == null ? 2 : order.ContainsKey(x.g.Key) ? 0 : 1)
            .ThenBy(x => x.g.Key != null && order.TryGetValue(x.g.Key, out int o) ? o : x.i).Select(x => x.g).ToArray();
        var pending = new List<(string[] Names, int Piece, int Pieces, BridgeReportSample[] Samples)>();
        var names = new List<string>(); var current = new List<BridgeReportSample>();
        void Flush() { if (current.Count == 0) return; pending.Add((names.ToArray(), 1, 1, current.ToArray())); names.Clear(); current.Clear(); }
        foreach (var (key, samples) in ordered)
        {
            string name = key ?? NoSource;
            if (samples.Count > limit)
            {
                Flush();
                int pieces = (samples.Count + limit - 1) / limit;
                for (int p = 0, start = 0; p < pieces; p++)
                {
                    int count = samples.Count / pieces + (p < samples.Count % pieces ? 1 : 0);
                    pending.Add((new[] { name }, p + 1, pieces, samples.GetRange(start, count).ToArray())); start += count;
                }
                continue;
            }
            if (current.Count + samples.Count > limit) Flush();
            names.Add(name); current.AddRange(samples);
        }
        Flush();
        int digits = Math.Max(2, pending.Count.ToString(CultureInfo.InvariantCulture).Length);
        string stem = Path.GetFileNameWithoutExtension(output);
        var parts = pending.Select((p, i) =>
        {
            string code = "A" + (i + 1).ToString(CultureInfo.InvariantCulture).PadLeft(digits, '0');
            return new BridgeReportPart(i + 1, code, stem + "_" + code + ".docx", p.Names, p.Piece, p.Pieces, p.Samples);
        }).ToArray();
        // The cards exactly as the single document selects and orders them (Critical): all ties, the global maxima.
        var cards = data.Governing(false).GroupBy(r => r.Sample.Id, StringComparer.Ordinal).Select(g => (g.First().Sample, g.ToArray())).ToArray();
        return new BridgeReportSplit(output, parts, cards);
    }
}
