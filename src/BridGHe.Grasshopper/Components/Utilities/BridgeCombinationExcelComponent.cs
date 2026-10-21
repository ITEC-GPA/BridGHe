using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BridGHe.Common.Checks;
using BridGHe.Common.Diagnostics;
using BridGHe.Grasshopper.DataTypes;
using BridGHe.Grasshopper.DataTypes.Fem;
using BridGHe.Grasshopper.Helpers;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;

namespace BridGHe.Grasshopper.Components.Utilities;

public sealed class BridgeCombinationExcelComponent : GH_Component
{
    private bool _run;
    public BridgeCombinationExcelComponent() : base("Bridge Combination Excel", "ComboExcel",
        "Create an Excel template from Models and numbered Phases, or read completed coefficients into Build BridGHe Model. Press Run to access the file.",
        CategoryNameConstants.CATEGORY_MODEL, CategoryNameConstants.SUBCATEGORY_DEFINITIONS) { }
    public override Guid ComponentGuid => new Guid("2776b9ba-309e-4a25-91ad-4177c557bc13");
    protected override System.Drawing.Bitmap Icon => Properties.ComponentIcons.For(GetType());
    public override void CreateAttributes()
    {
        var attributes = new ComponentAttributes.ComponentOneButtonAttributes(this, "Run");
        attributes.ButtonPressed += () => { _run = true; ExpireSolution(true); };
        m_attributes = attributes;
    }
    protected override void RegisterInputParams(GH_InputParamManager p)
    {
        p.AddGenericParameter("Models", "M", "Reference Models in N-mm-MPa, as for Build BridGHe Model. Each action carries its own source model and case.", GH_ParamAccess.list);
        p.AddGenericParameter("Phases", "P", "Numbered Bridge Phase objects. Their exact Action IDs become the Excel column headers.", GH_ParamAccess.list);
        p.AddTextParameter("File", "F", ".xlsx file. Relative paths are resolved beside the saved GH definition; use an absolute path for an unsaved definition.", GH_ParamAccess.item);
        p.AddIntegerParameter("Mode", "Mode", "0 Create template; 1 Read completed workbook. Press Run after selecting the mode. No automatic file access on reopening GH.", GH_ParamAccess.item, 0);
        p.AddIntegerParameter("Rows", "Rows", "Blank combination rows in a new template, 1..10000. No factors are assigned automatically.", GH_ParamAccess.item, 20);
        p.AddBooleanParameter("Overwrite", "O", "Allow Create to replace an existing file. Default false protects user coefficients. Close Excel before replacing a workbook.", GH_ParamAccess.item, false);
    }
    protected override void RegisterOutputParams(GH_OutputParamManager p)
    {
        p.AddTextParameter("File", "F", "Resolved Excel path.", GH_ParamAccess.item);
        p.AddTextParameter("Table", "T", "Read mode: header and combination rows, ready for Build BridGHe Model > Combinations. Empty after Create until you fill and read the workbook.", GH_ParamAccess.tree);
        p.AddTextParameter("Action IDs", "ID", "Exact action identifiers ordered by phase number. No prefixes to add.", GH_ParamAccess.list);
        p.AddTextParameter("Summary", "S", "Action/source mapping and file-operation diagnostics.", GH_ParamAccess.list);
    }
    protected override void SolveInstance(IGH_DataAccess da)
    {
        bool run = _run; _run = false;
        var models = new List<GH_Model>(); var phases = new List<BridgeWorkflowGoo>();
        string path = null; int mode = 0, rows = 20; bool overwrite = false;
        if (!da.GetDataList(0, models) || !da.GetDataList(1, phases) || !da.GetData(2, ref path) ||
            !da.GetData(3, ref mode) || !da.GetData(4, ref rows) || !da.GetData(5, ref overwrite)) return;
        try
        {
            if (mode is not (0 or 1)) throw new ArgumentException("Mode must be 0 (Create) or 1 (Read).");
            if (models.Any(m => m?.Value == null) || phases.Any(p => p?.Value is not BridgePhaseDefinition))
                throw new ArgumentException("Connect Models and Bridge Phase objects to the corresponding inputs.");
            if (!Path.IsPathRooted(path))
            {
                string documentPath = OnPingDocument()?.FilePath;
                if (string.IsNullOrEmpty(documentPath)) throw new ArgumentException("Save the GH definition first, or supply an absolute Excel path.");
                path = Path.Combine(Path.GetDirectoryName(documentPath), path);
            }
            path = BridgeCombinationWorkbook.FilePath(path);
            var catalog = new BridgeCombinationCatalog(models.Select(m => m.Value).ToArray(), phases.Select(p => (BridgePhaseDefinition)p.Value));
            da.SetData(0, path); da.SetDataList(2, catalog.ActionIds);
            var summary = catalog.ReferenceRows.Skip(1).Select(r => $"{r[0]} | phase {r[1]} {r[2]} | {r[5]} / {r[6]} | {r[7]} | strain {r[8]}").ToList();
            var warnings = catalog.Warnings;
            if (run)
            {
                if (mode == 0)
                {
                    BridgeCombinationWorkbook.Write(path, catalog, rows, overwrite);
                    summary.Add("Created. Fill Combinations: one name per row and all coefficients, including explicit 0. Keep Actions unchanged. Then select Mode=1 and press Run.");
                    Message = "Created";
                }
                else
                {
                    var result = BridgeCombinationWorkbook.Read(path, catalog);
                    var tree = new GH_Structure<GH_String>();
                    for (int i = 0; i < result.Table.Length; i++) tree.AppendRange(result.Table[i].Select(s => new GH_String(s)), new GH_Path(i));
                    da.SetDataTree(1, tree); warnings = result.Warnings;
                    summary.Add($"Read {result.Table.Length - 1} combinations. Connect Table to Build BridGHe Model > Combinations.");
                    Message = "Read";
                }
            }
            else Message = mode == 0 ? "Run: create" : "Run: read";
            summary.AddRange(warnings); da.SetDataList(3, summary);
            foreach (string warning in warnings) AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, warning);
        }
        catch (Exception e) { Message = "Error"; AddRuntimeMessage(GH_RuntimeMessageLevel.Error, e.Message); }
    }
}
