using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using BridGHe.Common.Checks;
using BridGHe.Common.Reports;
using BridGHe.Grasshopper.DataTypes.Fem;
using BridGHe.Grasshopper.Display;
using BridGHe.Grasshopper.Helpers;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Special;
using Grasshopper.Kernel.Types;

namespace BridGHe.Grasshopper.Components.Utilities;

public sealed class BridgeReportComponent : GH_Component
{
    private bool _run, configuring;
    public BridgeReportComponent() : base("Bridge Report", "Report", "Generate a Word report from native GPC results and a DOCX/DOTX template. Choose chapters with an automatic checklist Value List. Press Generate; never writes automatically on reopening.",
        CategoryNameConstants.CATEGORY_MODEL, CategoryNameConstants.SUBCATEGORY_RESULTS)
    { Params.ParameterSourcesChanged += (_, _) => ConfigureTopics(); }
    public override Guid ComponentGuid => new Guid("a9c85c3b-2ce0-4479-a147-cf13eb3f5d95");
    protected override System.Drawing.Bitmap Icon => Properties.ComponentIcons.For(GetType());
    public override void CreateAttributes()
    {
        var attributes = new ComponentAttributes.ComponentOneButtonAttributes(this, "Generate");
        attributes.ButtonPressed += () => { _run = true; ExpireSolution(true); }; m_attributes = attributes;
    }
    protected override void RegisterInputParams(GH_InputParamManager p)
    {
        p.AddGenericParameter("Results", "R", "BridgeStressResult tree from Section, Super Element or Detail Check. One document for the entire tree; original paths and native provenance are retained.", GH_ParamAccess.tree);
        p.AddTextParameter("Template", "T", "Word .docx/.dotx with {{BG.Report}}, or {{BG.Summary}} and individual chapter markers on separate body paragraphs. Relative to the saved GH definition.", GH_ParamAccess.item);
        p.AddTextParameter("File", "F", "Destination .docx. Relative to the saved GH definition. The template cannot be overwritten.", GH_ParamAccess.item);
        p.AddIntegerParameter("Topics", "Ch", "0 Model; 1 Sections; 2 Phases; 3 Checks; 4 Critical; 5 Plots; 6 full Appendix. Connect a Value List to populate a checklist. Disconnected uses 0..5; an attached empty checklist keeps only Summary.", GH_ParamAccess.list); p[3].Optional = true;
        p.AddGenericParameter("Model", "M", "Optional assembled N-mm BridGHe model for workflow/case coefficients and identity checks. Does not recalculate or certify source revision.", GH_ParamAccess.item); p[4].Optional = true;
        p.AddTextParameter("Title", "Title", "Report title.", GH_ParamAccess.item, "Relazione delle verifiche del ponte");
        p.AddTextParameter("Project", "Prj", "Project or job name.", GH_ParamAccess.item, "");
        p.AddTextParameter("Author", "By", "Author printed in the template; no signature is generated.", GH_ParamAccess.item, "");
        p.AddTextParameter("Revision", "Rev", "Document revision.", GH_ParamAccess.item, "01");
        p.AddTextParameter("SVG", "SVG", "Optional SVGs from Bridge Plot components, in document order. Verify their correspondence to Results; SVG does not carry authenticated result provenance.", GH_ParamAccess.tree); p[9].Optional = true;
        p.AddBooleanParameter("Overwrite", "O", "Explicitly replace the destination DOCX. Default false; close it in Word first.", GH_ParamAccess.item, false);
    }
    protected override void RegisterOutputParams(GH_OutputParamManager p)
    {
        p.AddTextParameter("File", "F", "Written DOCX; populated only after a successful Generate.", GH_ParamAccess.item);
        p.AddTextParameter("Summary", "S", "Export counts and selected chapters.", GH_ParamAccess.list);
        p.AddTextParameter("Warnings", "W", "Missing provenance and coverage limitations, also written in the report.", GH_ParamAccess.list);
        p.AddTextParameter("Report ID", "ID", "Export identity included in the DOCX and embedded numerical archive.", GH_ParamAccess.item);
    }
    public override void AddedToDocument(GH_Document document) { base.AddedToDocument(document); ConfigureTopics(); }
    private void ConfigureTopics()
    {
        if (configuring || Params.Input.Count < 4) return; configuring = true;
        try
        {
            foreach (var list in Params.Input[3].Sources.OfType<GH_ValueList>())
            {
                var names = BridgeReportData.TopicNames;
                if (list.ListMode == GH_ValueListMode.CheckList && list.ListItems.Count == names.Length
                    && list.ListItems.Select((v, i) => v.Name == names[i] && v.Expression == i.ToString(CultureInfo.InvariantCulture)).All(x => x)) continue;
                bool known = list.ListItems.Any(v => names.Contains(v.Name));
                var chosen = list.ListItems.Where(v => v.Selected).Select(v => v.Name).ToHashSet();
                list.ListMode = GH_ValueListMode.CheckList; list.ListItems.Clear();
                for (int i = 0; i < names.Length; i++) list.ListItems.Add(new GH_ValueListItem(names[i], i.ToString(CultureInfo.InvariantCulture))
                { Selected = known ? chosen.Contains(names[i]) : BridgeReportData.DefaultTopics.Contains((BridgeReportTopic)i) });
                list.ExpireSolution(false); Params.Input[3].ExpireSolution(false);
            }
        }
        finally { configuring = false; }
    }
    protected override void SolveInstance(IGH_DataAccess da)
    {
        bool run = _run; _run = false; Message = "Generate";
        if (!run) { da.SetDataList(1, new[] { "Premere Generate per creare il DOCX. Nessuna scrittura automatica." }); return; }
        try
        {
            GH_Structure<IGH_Goo> tree; string template = null, output = null, title = null, project = "", author = "", revision = "";
            GH_Model model = null; bool overwrite = false; var topics = new List<int>();
            if (!da.GetDataTree(0, out tree) || !da.GetData(1, ref template) || !da.GetData(2, ref output) || !da.GetData(5, ref title)) return;
            da.GetDataList(3, topics); da.GetData(4, ref model); da.GetData(6, ref project); da.GetData(7, ref author); da.GetData(8, ref revision); da.GetData(10, ref overwrite);
            if (Params.Input[4].VolatileDataCount > 0 && model?.Value == null) throw new ArgumentException("Model must be a BridGHe model.");
            if (topics.Count == 0 && Params.Input[3].SourceCount == 0 && Params.Input[3].VolatileDataCount == 0) topics.AddRange(BridgeReportData.DefaultTopics.Select(t => (int)t));
            if (topics.Any(t => !Enum.IsDefined((BridgeReportTopic)t))) throw new ArgumentException("Topics must be integers 0..6.");
            var entries = new List<BridgeReportEntry>();
            for (int b = 0; b < tree.PathCount; b++) for (int i = 0; i < tree.Branches[b].Count; i++)
            {
                var goo = tree.Branches[b][i]; var value = goo is GH_ObjectWrapper w ? w.Value : goo?.ScriptVariable();
                if (value is not BridgeStressResult r) throw new ArgumentException("Results must contain BridgeStressResult objects.");
                entries.Add(new(r, tree.Paths[b] + "[" + i + "]"));
            }
            var data = BridgeReportData.Create(entries, new(title, project, author, revision), model?.Value);
            var figures = new List<BridgeReportFigure>();
            if (topics.Contains((int)BridgeReportTopic.Plots) && da.GetDataTree(9, out GH_Structure<GH_String> svgs))
                foreach (var item in svgs.AllData(true))
                {
                    string svg = item?.ScriptVariable() as string ?? throw new ArgumentException("SVG must contain text from a Bridge Plot.");
                    using var preview = new PlotSvgPreview(); preview.Set(svg, 1500);
                    using var memory = new MemoryStream(); preview.Image.Save(memory, System.Drawing.Imaging.ImageFormat.Png);
                    var xml = XDocument.Parse(svg); XNamespace ns = "http://www.w3.org/2000/svg";
                    string caption = xml.Root.Element(ns + "title")?.Value ?? xml.Descendants(ns + "text").FirstOrDefault()?.Value ?? "Grafico " + (figures.Count + 1);
                    figures.Add(new(caption, memory.ToArray(), preview.Image.Width, preview.Image.Height, svg));
                }
            var result = BridgeWordReport.Write(Resolve(template), Resolve(output), data, topics.Select(t => (BridgeReportTopic)t), figures, overwrite);
            da.SetData(0, result.File); da.SetData(3, result.Id); da.SetDataList(2, result.Warnings);
            da.SetDataList(1, new[] { $"{result.Results} risultati; {result.Checks} voci; {result.Missing} esiti attivi senza WR.",
                "Capitoli: Riepilogo, " + string.Join(", ", topics.Distinct().OrderBy(t => t).Select(t => BridgeReportData.TopicNames[t])), "Archivio incorporato: bridghe/report.json" });
            if (result.Warnings.Length > 0 || result.Missing > 0) AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Report generato con segnalazioni: consultare Warnings e il riepilogo nel documento.");
            Message = "DOCX";
        }
        catch (Exception e) { Message = "Error"; AddRuntimeMessage(GH_RuntimeMessageLevel.Error, e.Message); }
    }
    private string Resolve(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("Supply a template and output path.");
        if (Path.IsPathRooted(path)) return Path.GetFullPath(path);
        string document = OnPingDocument()?.FilePath;
        if (string.IsNullOrWhiteSpace(document)) throw new ArgumentException("Save the GH definition first or use absolute paths.");
        return Path.GetFullPath(Path.Combine(Path.GetDirectoryName(document), path));
    }
}
