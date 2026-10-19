using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using BridGHe.Grasshopper.Display;
using BridGHe.Grasshopper.Helpers;
using Grasshopper.Kernel;

namespace BridGHe.Grasshopper.Components.Preview;

public sealed class BridgePlotPreviewComponent : GH_Component
{
    internal PlotSvgPreview Preview { get; } = new();
    private PlotPreviewWindow window;
    private bool received;
    internal int CanvasWidth { get; private set; } = 600;

    public BridgePlotPreviewComponent() : base("Bridge Plot Preview", "PlotPreview",
        "Show Bridge Plot SVGs on the Grasshopper canvas. Double-click the preview to enlarge. No file required.",
        CategoryNameConstants.CATEGORY_MODEL, CategoryNameConstants.SUBCATEGORY_PREVIEW)
    {
        Params.Input[0].DataMapping = GH_DataMapping.Flatten;
    }

    protected override void RegisterInputParams(GH_InputParamManager p)
    {
        p.AddTextParameter("SVG", "SVG", "Connect the SVG output of one or more Bridge Plot components", GH_ParamAccess.list);
        p.AddIntegerParameter("Index", "I", "Zero-based index of the chart to display", GH_ParamAccess.item, 0);
        p.AddIntegerParameter("Width", "W", "Canvas preview width in pixels, from 240 to 1600", GH_ParamAccess.item, 600);
    }
    protected override void RegisterOutputParams(GH_OutputParamManager p) =>
        p.AddTextParameter("SVG", "SVG", "Selected SVG, unchanged", GH_ParamAccess.item);

    public override void CreateAttributes() => m_attributes = new ComponentAttributes.PlotPreviewAttributes(this);
    protected override void BeforeSolveInstance() => received = false;
    protected override void SolveInstance(IGH_DataAccess da)
    {
        var plots = new List<string>(); int index = 0, width = 600;
        if (!da.GetDataList(0, plots) || !da.GetData(1, ref index) || !da.GetData(2, ref width)) return;
        try
        {
            if (index < 0 || index >= plots.Count) throw new ArgumentOutOfRangeException(nameof(index), $"Index must be between 0 and {plots.Count - 1}.");
            Preview.Set(plots[index], width);
            CanvasWidth = width; received = true;
            da.SetData(0, plots[index]);
            Message = $"{index + 1}/{plots.Count}";
        }
        catch (Exception e) { ClearPreview(); AddRuntimeMessage(GH_RuntimeMessageLevel.Error, e.Message); }
    }
    protected override void AfterSolveInstance()
    {
        if (!received) ClearPreview();
        if (window != null && !window.IsDisposed) window.UpdatePlot(Preview.Source);
        Attributes.ExpireLayout(); OnDisplayExpired(true);
    }

    private void ClearPreview() { Preview.Clear(); Message = ""; }
    internal void OpenPreview()
    {
        if (Preview.Image == null) return;
        if (window == null || window.IsDisposed)
        {
            window = new PlotPreviewWindow();
            window.UpdatePlot(Preview.Source);
            window.Show(global::Grasshopper.Instances.DocumentEditor);
        }
        else { window.UpdatePlot(Preview.Source); window.Activate(); }
    }
    protected override void AppendAdditionalComponentMenuItems(ToolStripDropDown menu)
    {
        base.AppendAdditionalComponentMenuItems(menu);
        Menu_AppendItem(menu, "Apri grafico", (_, _) => OpenPreview(), Preview.Image != null);
    }
    public override void RemovedFromDocument(GH_Document document)
    {
        window?.Close(); window = null; Preview.Dispose();
        base.RemovedFromDocument(document);
    }
    public override void DocumentContextChanged(GH_Document document, GH_DocumentContext context)
    {
        if (context == GH_DocumentContext.Close) { window?.Close(); window = null; Preview.Dispose(); }
        base.DocumentContextChanged(document, context);
    }
    protected override Bitmap Icon => Properties.ComponentIcons.For(GetType());
    public override Guid ComponentGuid => new Guid("17ad3e86-b699-435c-b036-565d04c8cdb6");
}
