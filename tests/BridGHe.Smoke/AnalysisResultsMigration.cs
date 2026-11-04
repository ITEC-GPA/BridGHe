using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using BridGHe.Common.Helpers;
using BridGHe.Common.Models;
using GH_IO.Serialization;
using GH_IO.Types;

/// <summary>
/// 4/11/2026: moves the analysis results of a saved document next to their .st7 files, as the components do at the next save in
/// Rhino, without opening the document in Grasshopper. A document opened here would miss the Grasshopper libraries that only
/// Rhino loads, and Grasshopper would stop at the «missing objects» dialog.
/// --results-outside &lt;in.ghx&gt; &lt;out.ghx&gt; [report.json]
/// The archive is changed only in the analysis components with results:
/// - Straus R3 Analysis loses "Data" and "ElementGuids";
/// - Moving Static Analysis loses the "MovingResults" chunk;
/// - both get "ResultsFile" and "ResultsHash", as their Write does.
/// The .st7 path is the text of the panel, or the persistent value, wired to their output path input.
/// The check rereads out.ghx and its files. Every result must be byte-identical, and nothing else in the archive may differ.
/// The input file is never written.
/// </summary>
internal static class AnalysisResultsMigration
{
    private static readonly Guid Straus = new Guid("c9985217-7bc3-4eb0-a8a1-d251e17c8140");
    private static readonly Guid Moving = new Guid("b6edaa43-c87c-40b4-8ce7-03260a524fd4");
    private const string FileItem = "ResultsFile", HashItem = "ResultsHash";

    internal static int Run(string input, string output, string report)
    {
        input = Path.GetFullPath(input); output = Path.GetFullPath(output);
        if (string.Equals(input, output, StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("The output must be another file.");
        string inputHash = Hash(File.ReadAllBytes(input));
        var watch = Stopwatch.StartNew();
        var archive = Read(input);
        double readSeconds = watch.Elapsed.TotalSeconds;
        watch.Restart(); archive.Serialize_Binary(); double embeddedSerialize = watch.Elapsed.TotalSeconds;

        var texts = new Dictionary<Guid, string>();
        foreach (var (_, container) in Objects(archive))
            if (container.ItemExists("InstanceGuid") && container.ItemExists("UserText")) texts[container.GetGuid("InstanceGuid")] = container.GetString("UserText");

        var rows = new List<Row>();
        foreach (var (type, container) in Objects(archive).Where(o => o.Type == Straus || o.Type == Moving))
        {
            var row = new Row { Instance = container.GetGuid("InstanceGuid"), Component = container.GetString("NickName"), Kind = type == Straus ? "Straus R3 Analysis" : "Moving Static Analysis" };
            rows.Add(row);
            var holder = type == Straus ? container : container.ChunkExists("MovingResults") ? (GH_Chunk)container.FindChunk("MovingResults") : null;
            if (holder == null || !holder.ItemExists("Data")) { row.Status = container.ItemExists(HashItem) ? "already outside" : "no results saved"; continue; }
            byte[] data = holder.GetByteArray("Data");
            var guids = holder.ItemExists("ElementGuids") ? JsonSerializer.Deserialize<Guid[]>(holder.GetString("ElementGuids")) : Array.Empty<Guid>();
            row.ArchiveBytes = data.Length; row.ArchiveHash = Hash(data);
            if (!AnalysisResultsFile.HasResults(ModelArchive.Read<ModelModel>(data))) { row.Status = "model without results, kept in the document"; continue; }
            string path = OutputPath(container, texts);
            if (path == null) { row.Status = "no output path: kept in the document"; continue; }
            row.File = AnalysisResultsFile.PathFor(path);
            row.FileHash = AnalysisResultsFile.WriteArchive(row.File, data, guids);
            row.FileBytes = new FileInfo(row.File).Length;
            if (type == Straus) { container.RemoveItem("Data"); container.RemoveItem("ElementGuids"); }
            else container.RemoveChunk("MovingResults");
            container.SetString(FileItem, row.File);
            container.SetString(HashItem, row.FileHash);
            row.Status = "moved";
        }
        watch.Restart();
        if (!archive.WriteToFile(output, true, false)) throw new IOException("Cannot write " + output);
        double writeSeconds = watch.Elapsed.TotalSeconds;

        // Check: reread out.ghx and the files.
        watch.Restart();
        var moved = Read(output);
        double rereadSeconds = watch.Elapsed.TotalSeconds;
        watch.Restart(); moved.Serialize_Binary(); double movedSerialize = watch.Elapsed.TotalSeconds;
        var differences = new List<string>();
        Compare(Read(input).GetRootNode, moved.GetRootNode, "", differences);
        foreach (var row in rows.Where(r => r.Status == "moved"))
        {
            var container = Objects(moved).Single(o => (o.Type == Straus || o.Type == Moving) && o.Container.GetGuid("InstanceGuid") == row.Instance).Container;
            byte[] back = AnalysisResultsFile.ReadArchive(container.GetString(FileItem), container.GetString(HashItem), out _);
            row.Identical = container.GetString(HashItem) == row.FileHash && Hash(back) == row.ArchiveHash;
        }
        bool untouched = inputHash == Hash(File.ReadAllBytes(input));
        bool passed = untouched && differences.Count == 0 && rows.Count > 0 && rows.All(r => r.Status != "moved" || r.Identical);
        var summary = new
        {
            input, output, inputBytes = new FileInfo(input).Length, outputBytes = new FileInfo(output).Length,
            seconds = new { readSeconds, embeddedSerialize, writeSeconds, rereadSeconds, movedSerialize },
            inputUntouched = untouched, otherDifferences = differences.Take(50).ToArray(), components = rows, passed
        };
        string json = JsonSerializer.Serialize(summary, new JsonSerializerOptions { WriteIndented = true });
        if (!string.IsNullOrEmpty(report)) File.WriteAllText(report, json);
        Console.WriteLine(json);
        Console.WriteLine(passed ? "RESULTS OUTSIDE PASS" : "RESULTS OUTSIDE FAIL");
        return passed ? 0 : 1;
    }

    private sealed class Row
    {
        public Guid Instance { get; set; }
        public string Component { get; set; }
        public string Kind { get; set; }
        public string Status { get; set; }
        public long ArchiveBytes { get; set; }
        public string ArchiveHash { get; set; }
        public string File { get; set; }
        public long FileBytes { get; set; }
        public string FileHash { get; set; }
        public bool Identical { get; set; }
    }

    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));

    private static GH_Archive Read(string path)
    {
        var archive = new GH_Archive();
        if (!archive.ReadFromFile(path)) throw new IOException("Cannot read " + path);
        return archive;
    }

    private static IEnumerable<(Guid Type, GH_Chunk Container)> Objects(GH_Archive archive)
    {
        var objects = archive.GetRootNode.FindChunk("Definition").FindChunk("DefinitionObjects");
        for (int i = 0; i < objects.ChunkCount; i++)
        {
            var o = (GH_Chunk)objects.Chunks[i];
            if (o.ItemExists("GUID") && o.ChunkExists("Container")) yield return (o.GetGuid("GUID"), (GH_Chunk)o.FindChunk("Container"));
        }
    }

    // The .st7 path of an analysis component: the persistent value of its output path input, or the text of the panel wired to it.
    private static string OutputPath(GH_Chunk container, Dictionary<Guid, string> texts)
    {
        for (int i = 0; i < container.ChunkCount; i++)
        {
            if (container.Chunks[i] is not GH_Chunk p || p.Name != "param_input" || !p.ItemExists("Name")) continue;
            if (!string.Equals(p.GetString("Name"), "Output Path", StringComparison.OrdinalIgnoreCase)) continue;
            int sources = p.ItemExists("SourceCount") ? p.GetInt32("SourceCount") : 0;
            if (sources == 1 && texts.TryGetValue(p.GetGuid("Source", 0), out string text)) return Single(text);
            if (sources == 0 && p.ChunkExists("PersistentData"))
            {
                var strings = new List<string>();
                Collect(p.FindChunk("PersistentData"), strings);
                return strings.Count == 1 ? Single(strings[0]) : null;
            }
        }
        return null;
    }

    private static string Single(string text)
    {
        var lines = text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return lines.Length == 1 && lines[0].EndsWith(".st7", StringComparison.OrdinalIgnoreCase) ? lines[0] : null;
    }

    private static void Collect(GH_IReader chunk, List<string> strings)
    {
        foreach (var item in chunk.Items) if (item.Type == GH_Types.gh_string) strings.Add(item._string);
        foreach (var child in chunk.Chunks.Cast<GH_IReader>()) Collect(child, strings);
    }

    // Every chunk and item of the two archives, except the items the migration changes in the analysis components.
    private static void Compare(GH_IReader a, GH_IReader b, string path, List<string> differences)
    {
        if (differences.Count > 50) return;
        bool analysis = a.Name == "Container" && (b.ItemExists(HashItem) || a.ItemExists(HashItem));
        string Key(GH_Item i) => i.Name + "#" + i.Index;
        var itemsA = a.Items.Where(i => !analysis || (i.Name != "Data" && i.Name != "ElementGuids" && i.Name != FileItem && i.Name != HashItem)).ToDictionary(Key);
        var itemsB = b.Items.Where(i => !analysis || (i.Name != "Data" && i.Name != "ElementGuids" && i.Name != FileItem && i.Name != HashItem)).ToDictionary(Key);
        foreach (var key in itemsA.Keys.Union(itemsB.Keys))
        {
            if (!itemsA.TryGetValue(key, out var x) || !itemsB.TryGetValue(key, out var y)) { differences.Add(path + "/" + key + " missing"); continue; }
            if (x.Type != y.Type || !Same(x.InternalData, y.InternalData)) differences.Add(path + "/" + key + " differs");
        }
        string ChunkKey(GH_IReader c) => c.Name + "#" + c.Index;
        var chunksA = a.Chunks.Cast<GH_IReader>().Where(c => !(analysis && c.Name == "MovingResults")).ToDictionary(ChunkKey);
        var chunksB = b.Chunks.Cast<GH_IReader>().ToDictionary(ChunkKey);
        foreach (var key in chunksA.Keys.Union(chunksB.Keys))
        {
            if (!chunksA.TryGetValue(key, out var x) || !chunksB.TryGetValue(key, out var y)) { differences.Add(path + "/" + key + " chunk missing"); continue; }
            Compare(x, y, path + "/" + key, differences);
        }
    }

    private static bool Same(object x, object y)
    {
        if (x is byte[] p && y is byte[] q) return p.AsSpan().SequenceEqual(q);
        // The thumbnail is a new bitmap object at every read: compare its pixels.
        if (x is System.Drawing.Bitmap a && y is System.Drawing.Bitmap b)
        {
            if (a.Width != b.Width || a.Height != b.Height) return false;
            for (int i = 0; i < a.Width; i++) for (int j = 0; j < a.Height; j++) if (a.GetPixel(i, j) != b.GetPixel(i, j)) return false;
            return true;
        }
        return Equals(x, y);
    }
}
