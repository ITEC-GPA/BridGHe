using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BridGHe.Common.Diagnostics;
using BridGHe.Common.Geometry;
using BridGHe.Grasshopper.Components.Utilities;
using BridGHe.Grasshopper.DataTypes;
using BridGHe.Grasshopper.DataTypes.Fem;
using BridGHe.Grasshopper.Helpers;
using GH_IO.Serialization;
using Grasshopper.Kernel;

namespace BridGHe.Grasshopper.Components.Geometry;

public sealed class BridgeSegmentExcelComponent : BridgeUtilityComponent
{
    private bool run;
    private string cachePath;
    private string[] cache, warnings = Array.Empty<string>();
    public BridgeSegmentExcelComponent() : base("Bridge Segment Excel", "SegmentExcel",
        "Count fabrication segments before assigning sections. Create an Excel template from geometry, then read its dimensions into Bridge Segment Sections. File access only on Run.",
        CategoryNameConstants.SUBCATEGORY_SECTIONS) { }
    public override Guid ComponentGuid => new Guid("f9286827-2ca1-4268-a6dc-79c8ba6d1c9f");
    public override void CreateAttributes()
    {
        var a = new ComponentAttributes.ComponentOneButtonAttributes(this, "Run");
        a.ButtonPressed += () => { run = true; ExpireSolution(true); }; m_attributes = a;
    }
    protected override void RegisterInputParams(GH_InputParamManager p)
    {
        p.AddGenericParameter("Bridge Geometry", "BG", "Bridge Geometry from Simple Bridge, before section assignment. Counts fabrication segments, not FEM elements.", GH_ParamAccess.item);
        p.AddTextParameter("File", "F", "Excel .xlsx path. Relative to the saved GH definition. Used only on Run; not required for counts.", GH_ParamAccess.item, "Sezioni_conci.xlsx");
        p.AddIntegerParameter("Mode", "M", "0 Create template; 1 Read completed workbook. Press Run to access the file.", GH_ParamAccess.item, 0);
        p.AddGenericParameter("Base Property", "P", "Optional steel I Beam Property: prefill numeric dimensions when creating Excel. Without it, '*' inherits Base Property later in Bridge Segment Sections.", GH_ParamAccess.item); p[3].Optional = true;
        p.AddBooleanParameter("Overwrite", "O", "Allow explicit Create to replace an existing Excel file. False by default. Use a new file after changing geometry to preserve your previous dimensions.", GH_ParamAccess.item, false);
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
        BridgeGeometryGoo geometry = null; GH_BeamProperty property = null;
        string file = "Sezioni_conci.xlsx"; int mode = 0; bool overwrite = false;
        if (!da.GetData(0, ref geometry)) return;
        if (Params.Input[0].VolatileDataCount != 1) throw new ArgumentException("Connect exactly one Bridge Geometry.");
        var segments = BridgeSegmentSections.Segments(geometry.Value);
        var groups = segments.GroupBy(s => s.Girder).ToArray();
        da.SetData(0, segments.Length); da.SetDataList(1, segments.Select(BridgeSegmentSections.Id));
        da.SetDataList(2, groups.Select(g => g.Key)); da.SetDataList(3, groups.Select(g => g.Count()));
        da.GetData(1, ref file); da.GetData(2, ref mode); da.GetData(3, ref property); da.GetData(4, ref overwrite);
        if (Params.Input[3].VolatileDataCount > 1) throw new ArgumentException("Connect at most one Base Property.");
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
                var table = BridgeSegmentWorkbook.Template(geometry.Value, property?.Value);
                if (execute) BridgeSegmentWorkbook.Write(path, geometry.Value, property?.Value, overwrite);
                da.SetDataList(4, table);
                info.Add(execute ? "Created. Edit the yellow cells, save Excel, select Mode=1 and press Run. ID and station columns must remain paired." : "Run creates the file. Section Table currently contains the proposed template, not the contents of an Excel file.");
                Message = segments.Length + (execute ? " | created" : " | template");
            }
            else
            {
                if (execute)
                {
                    cache = null; cachePath = null; warnings = Array.Empty<string>();
                    var result = BridgeSegmentWorkbook.Read(path, geometry.Value);
                    cache = result.Table; warnings = result.Warnings; cachePath = path;
                }
                if (cache == null || !string.Equals(path, cachePath, StringComparison.OrdinalIgnoreCase))
                {
                    Message = segments.Length + " | Run: read";
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Press Run in Read mode to supply Section Table. No workbook data have been loaded for this path.");
                }
                else
                {
                    // Validate every time: a changed mesh is allowed; changed fabrication IDs/ranges are rejected.
                    var table = BridgeSegmentSections.ValidateTable(geometry.Value, cache);
                    da.SetDataList(4, table); info.AddRange(warnings);
                    info.Add(execute ? "Workbook read. Section Table is ready for Bridge Segment Sections." : "Using the last imported table. To apply Excel edits, recalculate/save Excel and press Run again.");
                    Message = segments.Length + " | " + (execute ? "read" : "cached");
                    foreach (string warning in warnings) AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, warning);
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
        }
        return base.Write(writer);
    }
    public override bool Read(GH_IReader reader)
    {
        cache = null; cachePath = null; warnings = Array.Empty<string>();
        if (reader.ItemExists("ExcelPath") && reader.ItemExists("SectionTable"))
        {
            cachePath = reader.GetString("ExcelPath"); cache = reader.GetString("SectionTable").Split('\n');
            if (reader.ItemExists("ExcelWarnings")) warnings = reader.GetString("ExcelWarnings").Split('\n', StringSplitOptions.RemoveEmptyEntries);
        }
        return base.Read(reader);
    }
}
