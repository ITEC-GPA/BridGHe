using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using BridGHe.Common.Diagnostics;
using BridGHe.Common.Geometry;
using BridGHe.Grasshopper.Components.Utilities;
using BridGHe.Grasshopper.DataTypes;
using BridGHe.Grasshopper.DataTypes.Fem;
using BridGHe.Grasshopper.Helpers;
using GH_IO.Serialization;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;

namespace BridGHe.Grasshopper.Components.Geometry;

public class BridgeSegmentExcelComponent : BridgeUtilityComponent
{
    private bool run;
    private string cachePath;
    private string[] cache, warnings = Array.Empty<string>();
    // 0.7.2 (proposal 4): the time and SHA-256 of the workbook last read and the time of the reading, saved with the table; the watcher of Auto Read.
    private DateTime? fileTimeUtc, readAtUtc;
    private string fileSha;
    private GH_FileWatcher watcher;
    private string watchedPath;
    public BridgeSegmentExcelComponent() : base("Bridge Segment Excel", "SegmentExcel",
        "Count fabrication segments before assigning sections. Create an Excel template from geometry, then read its dimensions into Bridge Segment Sections. File access only on Run.",
        CategoryNameConstants.SUBCATEGORY_SECTIONS) { }
    protected BridgeSegmentExcelComponent(string name, string nickname) : base(name, nickname,
        "Fabrication section workbook, in mm. File access only on Run; counts are available before mesh generation.", CategoryNameConstants.SUBCATEGORY_SECTIONS) { }
    protected virtual int? FixedMode => null;
    public override GH_Exposure Exposure => FixedMode == null ? GH_Exposure.hidden : GH_Exposure.primary;
    public override Guid ComponentGuid => new Guid("f9286827-2ca1-4268-a6dc-79c8ba6d1c9f");
    public override void CreateAttributes()
    {
        var a = new ComponentAttributes.ComponentOneButtonAttributes(this, "Run");
        a.ButtonPressed += () => { run = true; ExpireSolution(true); }; m_attributes = a;
    }
    protected override void RegisterInputParams(GH_InputParamManager p)
    {
        p.AddGenericParameter("Fabrication Plan", "C", "From Bridge Fabrication Segments. Existing Bridge Geometry is also accepted. No FEM mesh is required.", GH_ParamAccess.item);
        p.AddTextParameter("File", "F", "Excel .xlsx path. Relative to the saved GH definition. Used only on Run; not required for counts.", GH_ParamAccess.item, "Sezioni_conci.xlsx");
        if (FixedMode == null) p.AddIntegerParameter("Mode", "M", "0 Create template; 1 Read completed workbook. Press Run to access the file.", GH_ParamAccess.item, 0);
        if (FixedMode == 1) { p.AddBooleanParameter("Auto Read", "AR", "True: read the workbook again by itself when it is saved (Grasshopper file watcher) or found changed. Default false: read only on Run; a workbook saved after the last reading gives a warning.", GH_ParamAccess.item, false); p[2].Optional = true; }
        if (FixedMode != 1)
        {
            p.AddGenericParameter("Base Property", "P", "Optional steel I property to prefill dimensions. Without it, '*' inherits the base section later.", GH_ParamAccess.item); p[FixedMode == null ? 3 : 2].Optional = true;
            p.AddBooleanParameter("Overwrite", "O", "Explicit permission to replace an existing workbook on Run. Default false.", GH_ParamAccess.item, false);
        }
    }
    protected override void RegisterOutputParams(GH_OutputParamManager p)
    {
        p.AddIntegerParameter("Count", "N", "Total fabrication segments, all girders. Available without a section or Excel file.", GH_ParamAccess.item);
        p.AddTextParameter("Segment IDs", "ID", "One unique G1-C1 etc. per girder/concio. Ordered by girder then concio.", GH_ParamAccess.list);
        p.AddIntegerParameter("Girder IDs", "G", "Girder IDs, paired with Counts per Girder.", GH_ParamAccess.list);
        p.AddIntegerParameter("Counts per Girder", "NG", "Number of fabrication segments in each girder, paired with Girder IDs.", GH_ParamAccess.list);
        p.AddTextParameter("Section Table", "T", "Connect to Bridge Segment Sections > Section Table. Create: current template. Read: validated workbook dimensions retained until next Run or invalidation.", GH_ParamAccess.list);
        p.AddTextParameter("File", "F", "Resolved path when available.", GH_ParamAccess.item);
        p.AddTextParameter("Information", "I", "Counts, file operation and cache warnings. Dimensions and stations are in mm.", GH_ParamAccess.list);
    }
    protected override void Solve(IGH_DataAccess da)
    {
        bool execute = run; run = false;
        IGH_Goo geometry = null; GH_BeamProperty property = null;
        string file = "Sezioni_conci.xlsx"; int mode = FixedMode ?? 0; bool overwrite = false, auto = false;
        if (!da.GetData(0, ref geometry)) return;
        if (Params.Input[0].VolatileDataCount != 1) throw new ArgumentException("Connect exactly one Bridge Geometry.");
        var plan = BridgeDefinitionGoo.Segments(geometry);
        var segments = BridgeSegmentSections.Segments(plan);
        var groups = segments.GroupBy(s => s.Girder).ToArray();
        da.SetData(0, segments.Length); da.SetDataList(1, segments.Select(BridgeSegmentSections.Id));
        da.SetDataList(2, groups.Select(g => g.Key)); da.SetDataList(3, groups.Select(g => g.Count()));
        da.GetData(1, ref file);
        if (FixedMode == null) da.GetData(2, ref mode);
        if (FixedMode == 1) da.GetData(2, ref auto);
        if (FixedMode != 1)
        {
            int pi = FixedMode == null ? 3 : 2; da.GetData(pi, ref property); da.GetData(pi + 1, ref overwrite);
            if (Params.Input[pi].VolatileDataCount > 1) throw new ArgumentException("Connect at most one Base Property.");
        }
        var info = new List<string> { $"{segments.Length} fabrication segments across {groups.Length} girders. Counts do not depend on FEM mesh subdivisions.",
            "Excel: one row per ID; dimensions in mm. '*' inherits the corresponding Base Property dimension in Bridge Segment Sections. Blank cells are rejected." };
        try
        {
            if (mode is not (0 or 1)) throw new ArgumentException("Mode must be 0 (Create) or 1 (Read).");
            string path = file;
            if (!string.IsNullOrWhiteSpace(path) && !Path.IsPathRooted(path))
            {
                string document = OnPingDocument()?.FilePath;
                path = string.IsNullOrEmpty(document) ? null : Path.Combine(Path.GetDirectoryName(document), path);
            }
            if (!string.IsNullOrWhiteSpace(path)) { path = BridgeCombinationWorkbook.FilePath(path); da.SetData(5, path); }
            if (execute && path == null) throw new ArgumentException("Save the GH definition or supply an absolute .xlsx path before Run.");
            if (mode == 0)
            {
                // R6.5 P1 lane 2b / P2K (P1): the template on the GPC datum of the base property (rule of passo 75); without one, the plain template.
                var gpcBase = property?.Gpc;
                var table = BridgeSegmentWorkbook.TemplateGpc(plan, gpcBase);
                if (execute) BridgeSegmentWorkbook.Write(path, plan, overwrite: overwrite, template: table);
                da.SetDataList(4, table);
                info.Add(execute ? "Created. Edit the yellow cells, save Excel, then press Run on Bridge Read Section Excel. ID and station columns must remain paired." : "Run creates the file. Section Table currently contains the proposed template, not the contents of an Excel file.");
                Message = segments.Length + (execute ? " | created" : " | template");
            }
            else
            {
                Watch(auto ? path : null);
                bool samePath = cache != null && string.Equals(path, cachePath, StringComparison.OrdinalIgnoreCase);
                DateTime? saved = path != null && File.Exists(path) ? File.GetLastWriteTimeUtc(path) : null;
                bool automatic = false;
                if (execute)
                {
                    cache = null; cachePath = null; warnings = Array.Empty<string>(); fileTimeUtc = readAtUtc = null; fileSha = null;
                    var result = BridgeSegmentWorkbook.Read(path, plan);
                    cache = result.Table; warnings = result.Warnings; cachePath = path; Record(path, saved);
                }
                else if (auto && saved != null && (!samePath || fileTimeUtc != saved))
                {
                    // Auto Read: the workbook never read for this path, or saved after the last reading (a file of an earlier version, without
                    // its time, is read again once). A failed reading keeps the table read before, with a warning; Run still clears it.
                    try
                    {
                        var result = BridgeSegmentWorkbook.Read(path, plan);
                        cache = result.Table; warnings = result.Warnings; cachePath = path; Record(path, saved); automatic = true;
                    }
                    catch (Exception e) when (e is ArgumentException or InvalidOperationException or IOException or UnauthorizedAccessException)
                    {
                        AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, $"Auto Read: {Path.GetFileName(path)} not read again ({e.Message})" + (samePath ? $" The table read at {Time(readAtUtc)} is kept." : ""));
                    }
                }
                if (cache == null || !string.Equals(path, cachePath, StringComparison.OrdinalIgnoreCase))
                {
                    Message = segments.Length + " | Run: read";
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Press Run on the reader to supply Section Table. No workbook data have been loaded for this path.");
                }
                else
                {
                    // Validate every time: a changed mesh is allowed; changed fabrication IDs/ranges are rejected.
                    var table = BridgeSegmentSections.ValidateTable(plan, cache);
                    da.SetDataList(4, table); info.AddRange(warnings);
                    info.Add(execute ? "Workbook read. Section Table is ready for Bridge Segment Sections." : automatic ? "Auto Read: workbook read again after it was saved. Section Table is ready for Bridge Segment Sections." : "Using the last imported table. To apply Excel edits, recalculate/save Excel and press Run again.");
                    Message = segments.Length + " | " + (execute ? "read" : automatic ? "auto read" : "cached");
                    foreach (string warning in warnings) AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, warning);
                    // 0.7.2 (proposal 4): only the time of the file is compared, the file is not read. A table of an earlier version has no time.
                    if (fileTimeUtc != null && saved != null && saved != fileTimeUtc)
                    {
                        string name = Path.GetFileName(path);
                        string changed = saved > fileTimeUtc ? $"{name} saved at {Time(saved)}, after the last read at {Time(readAtUtc ?? fileTimeUtc)}: press Run."
                            : $"{name} saved at {Time(saved)}, different from the file read at {Time(readAtUtc ?? fileTimeUtc)}: press Run.";
                        AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, changed); info.Add(changed);
                        Message = segments.Length + " | file changed";
                    }
                }
            }
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException or IOException or UnauthorizedAccessException)
        { Message = "Excel error"; AddRuntimeMessage(GH_RuntimeMessageLevel.Error, e.Message); info.Add(e.Message); }
        da.SetDataList(6, info);
    }
    public override bool Write(GH_IWriter writer)
    {
        if (cache != null)
        {
            writer.SetString("ExcelPath", cachePath); writer.SetString("SectionTable", string.Join("\n", cache));
            writer.SetString("ExcelWarnings", string.Join("\n", warnings));
            if (fileTimeUtc != null) writer.SetString("ExcelFileTimeUtc", fileTimeUtc.Value.Ticks.ToString(CultureInfo.InvariantCulture));
            if (readAtUtc != null) writer.SetString("ExcelReadUtc", readAtUtc.Value.Ticks.ToString(CultureInfo.InvariantCulture));
            if (fileSha != null) writer.SetString("ExcelSha256", fileSha);
        }
        return base.Write(writer);
    }
    public override bool Read(GH_IReader reader)
    {
        cache = null; cachePath = null; warnings = Array.Empty<string>(); fileTimeUtc = readAtUtc = null; fileSha = null;
        if (reader.ItemExists("ExcelPath") && reader.ItemExists("SectionTable"))
        {
            cachePath = reader.GetString("ExcelPath"); cache = reader.GetString("SectionTable").Split('\n');
            if (reader.ItemExists("ExcelWarnings")) warnings = reader.GetString("ExcelWarnings").Split('\n', StringSplitOptions.RemoveEmptyEntries);
            fileTimeUtc = Ticks(reader, "ExcelFileTimeUtc"); readAtUtc = Ticks(reader, "ExcelReadUtc");
            if (reader.ItemExists("ExcelSha256")) fileSha = reader.GetString("ExcelSha256");
        }
        return base.Read(reader);
    }
    private static DateTime? Ticks(GH_IReader reader, string name) =>
        reader.ItemExists(name) && long.TryParse(reader.GetString(name), NumberStyles.Integer, CultureInfo.InvariantCulture, out long ticks) ? new DateTime(ticks, DateTimeKind.Utc) : null;
    /// <summary>0.7.2 (proposal 4): the time of the file read and of the reading, and its SHA-256 (kept in the GH file with the table).</summary>
    private void Record(string path, DateTime? saved)
    {
        fileTimeUtc = saved ?? (File.Exists(path) ? File.GetLastWriteTimeUtc(path) : null); readAtUtc = DateTime.UtcNow;
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            fileSha = Convert.ToHexString(SHA256.HashData(stream));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { fileSha = null; }
    }
    /// <summary>Local time of a UTC instant: hh:mm today, dd/MM hh:mm on another day.</summary>
    private static string Time(DateTime? utc)
    {
        if (utc == null) return "?";
        var local = utc.Value.ToLocalTime();
        return local.ToString(local.Date == DateTime.Now.Date ? "HH:mm" : "dd/MM HH:mm", CultureInfo.InvariantCulture);
    }
    /// <summary>Auto Read: a Grasshopper file watcher on the workbook; a change expires the solution, which reads the file again.</summary>
    private void Watch(string path)
    {
        if (path == null) { StopWatching(); return; }
        if (watcher != null && !watcher.IsDisposed && string.Equals(watchedPath, path, StringComparison.OrdinalIgnoreCase)) return;
        StopWatching();
        string folder = Path.GetDirectoryName(path);
        if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) return;
        try { watcher = GH_FileWatcher.CreateFileWatcher(path, GH_FileWatcherEvents.All, FileSaved); watchedPath = path; }
        catch (Exception e) when (e is ArgumentException or IOException or UnauthorizedAccessException or InvalidOperationException) { watcher = null; watchedPath = null; }
    }
    private void StopWatching() { watcher?.Dispose(); watcher = null; watchedPath = null; }
    /// <summary>The callback of the watcher (Grasshopper calls it on its UI thread): a new solution, which reads the workbook if its time changed.</summary>
    internal void FileSaved(string file) { if (OnPingDocument() != null) ExpireSolution(true); }
    internal bool Watching => watcher != null && !watcher.IsDisposed;
    public override void RemovedFromDocument(GH_Document document) { StopWatching(); base.RemovedFromDocument(document); }
    public override void DocumentContextChanged(GH_Document document, GH_DocumentContext context)
    {
        base.DocumentContextChanged(document, context);
        if (context is GH_DocumentContext.Close or GH_DocumentContext.Unloaded) StopWatching();
    }
}
