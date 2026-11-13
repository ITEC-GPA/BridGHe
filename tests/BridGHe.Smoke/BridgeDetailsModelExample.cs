using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using BridGHe.Common.Checks;
using BridGHe.Common.Helpers;
using BridGHe.Common.Models;
using GPC.Checkers.CompositeBridge;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Parameters;
using Grasshopper.Kernel.Special;
using Grasshopper.Kernel.Types;

/// <summary>Portable replay of archived Straus results: no new global solve or fabricated FEM forces.</summary>
internal static class BridgeDetailsModelExample
{
    private static readonly string Folder = ExamplePaths.Case("47-pioli-irrigidimenti-verifiche");
    private static readonly string FileName = Path.Combine(Folder, "47-integrazione-modello.ghx");
    private static GH_Component Find(GH_Document d, string type) => d.Objects.OfType<GH_Component>().Single(c => c.GetType().Name == type);
    /// <summary>R6.6 R4a: true when the document is solved on GPC data alone (see Replay).</summary>
    private static bool gpcOnly;
    private static void RunButton(GH_Component c) { c.GetType().GetField("_run", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(c, true); c.ExpireSolution(false); }
    private static void Solve(GH_Document d)
    {
        GH_Document.EnableSolutions = true; d.Enabled = true; d.NewSolution(false, GH_SolutionMode.Silent);
        var errors = d.Objects.OfType<GH_ActiveObject>().SelectMany(c => c.RuntimeMessages(GH_RuntimeMessageLevel.Error).Select(e => c.Name + ": " + e)).ToArray();
        if (errors.Length > 0) throw new Exception(string.Join("; ", errors));
    }
    private static void Calculate(GH_Document d)
    { RunButton(Find(d, "BuildBridgeModel")); Solve(d); RunButton(Find(d, "SuperElementCheckComponent")); Solve(d); }
    private static int Validate(GH_Document d)
    {
        var source = Find(d, "SuperElementCheckComponent").Params.Output[0].VolatileData;
        var details = Find(d, "BridgeDetailCheckComponent").Params.Output[0].VolatileData;
        BridgeWorkflowTests.Require(source.DataCount > 2 && source.DataCount == details.DataCount, "Lost station/combination results between super-element and detail checker.");
        BridgeWorkflowTests.Require(source.Paths.SequenceEqual(details.Paths), "Station tree paths changed in detail checking.");
        var summary = Find(d, "BridgeCheckSummaryComponent");
        BridgeWorkflowTests.Require(summary.Params.Input[1].Optional && summary.Params.Input[1].VolatileDataCount == 0
            && summary.Params.Output[0].VolatileDataCount > source.DataCount && summary.RuntimeMessages(GH_RuntimeMessageLevel.Warning).Count == 0,
            "Summary must generate station labels with only Results connected.");
        var a = source.AllData(true).Select(g => (BridgeStressResult)g.ScriptVariable()).ToArray();
        var b = details.AllData(true).Select(g => (BridgeStressResult)g.ScriptVariable()).ToArray();
        // R6.6 lane R4a: the bridge model of Build BridGHe Model is the GPC bridge model alone (no FEM value): its one beam (the extract of
        // beam 36, the documented example), the local stations of its merged samples and the one combination of the DEMO table of the document.
        var output = Find(d, "BuildBridgeModel").Params.Output[0].VolatileData.AllData(true).Single();
        BridgeWorkflowTests.Require(!gpcOnly || output.GetType().GetProperty("Value").GetValue(output) == null, "GPC: the Build BridGHe Model output has a FEM value.");
        var bridge = R2bFixtures.Get<BridGHe.Common.Gpc.GpcBridge>(output, "GpcBridge") ?? throw new Exception("GPC: no GPC bridge model on the output.");
        BridgeWorkflowTests.Require(bridge.Model.BeamElements.Count == 1 && bridge.Samples.Length == 1 && bridge.Combinations.Select(c => c.Name).SequenceEqual(new[] { "DEMO" }),
            "GPC: the bridge model is not the one beam of the extract with the DEMO combination.");
        var stations = bridge.Samples[0].Select(s => s.Sample.Station).Distinct().OrderBy(x => x).ToArray();
        var expectedSources = stations.SelectMany(s => bridge.Combinations.Select(combo => new BridgeResultSource("36", s, combo.Name, "G2"))).ToArray();
        BridgeWorkflowTests.Require(a.Select(r => r.Source).SequenceEqual(expectedSources), "Super-element result provenance does not match the beam, local stations and combinations.");
        var expectedLabels = b.SelectMany(r => BridgeGpc.Checks(r.Stage).Select(_ => r.Source.Label));
        BridgeWorkflowTests.Require(summary.Params.Output[0].VolatileData.AllData(true).Select(g => (string)g.ScriptVariable()).SequenceEqual(expectedLabels), "Automatic summary labels lost the source identity.");
        for (int i = 0; i < a.Length; i++)
        {
            BridgeWorkflowTests.Require(a[i].Source == b[i].Source, "Detail checking lost result provenance.");
            BridgeWorkflowTests.Require(a[i].Assumptions.Count>0 && a[i].Assumptions.SequenceEqual(b[i].Assumptions),"Detail checking lost the declared planar force assumptions.");
            BridgeWorkflowTests.Require(a[i].Analysis.Input.Options.LimitState == BridgeLimitState.Rare && b[i].Analysis.Input.Options.LimitState == BridgeLimitState.Ultimate, "Explicit limit state override not applied.");
            BridgeWorkflowTests.Require(a[i].Analysis.Input.Phases.SequenceEqual(b[i].Analysis.Input.Phases), "Detail checking changed already combined FEM actions or strain.");
            var s = b[i].Stage.Studs;
            BridgeWorkflowTests.Require(s.Enabled && s.ResistancePerStud > 0 && Math.Abs(s.ForcePerStud - Math.Abs(s.Flow) * .1) < 1e-8, "Incorrect connected stud force.");
            BridgeWorkflowTests.Require(BridgeGpc.Checks(b[i].Stage).Any(c => c.Name.StartsWith("Intermedio")) && BridgeGpc.Checks(b[i].Stage).Any(c => c.Name.StartsWith("Appoggio")), "Missing stiffener checks.");
        }
        foreach(var plot in d.Objects.OfType<GH_Component>().Where(c=>c.GetType().Name is "BridgeStudPlotComponent" or "BridgeIntermediateStiffenerPlotComponent" or "BridgeSupportStiffenerPlotComponent"))
        {
            var svg=(string)plot.Params.Output[0].VolatileData.AllData(true).Single().ScriptVariable();
            BridgeWorkflowTests.Require(svg.Contains("<svg") && svg.Contains("Station [m]"),"Missing native detail plot.");
            if(plot.GetType().Name!="BridgeStudPlotComponent")
                BridgeWorkflowTests.Require(plot.Params.Output[4].VolatileDataCount>0 && plot.Params.Output[5].VolatileData.AllData(true).All(g=>string.IsNullOrEmpty(g.ScriptVariable() as string)),"Incomplete stiffener WR in the tutorial.");
        }
        var previews=d.Objects.OfType<GH_Component>().Where(c=>c.GetType().Name=="BridgePlotPreviewComponent").ToArray();
        BridgeWorkflowTests.Require(previews.Length==3&&previews.All(c=>c.Params.Output[0].VolatileDataCount==1),"Detail plots are not visible in their three GH previews.");
        return a.Length;
    }
    internal static void GeneratePlotViewer()
    {
        WritePlotViewer(BridgeMergeTests.Initialize(),Path.Combine(Folder,"grafici"));
        Console.WriteLine("DETAIL PLOT VIEWER PASS: saved, reopened and all three SVG snapshots selected.");
    }

    private static void WritePlotViewer(Assembly plugin,string plotFolder)
    {
        Console.WriteLine("DETAIL EXAMPLE: creating snapshot viewer.");
        using var doc=new GH_Document();
        var data=new Param_String{NickName="Snapshot dei tre grafici dell'esempio 47"};data.CreateAttributes();data.Attributes.Pivot=new PointF(100,100);doc.AddObject(data,false);
        foreach(var type in new[]{"BridgeStudPlotComponent","BridgeIntermediateStiffenerPlotComponent","BridgeSupportStiffenerPlotComponent"})
            data.PersistentData.Append(new GH_String(File.ReadAllText(Path.Combine(plotFolder,type+".svg"))));
        var list=new GH_ValueList{NickName="Grafico",ListMode=GH_ValueListMode.DropDown};list.CreateAttributes();list.Attributes.Pivot=new PointF(100,240);list.ListItems.Clear();
        foreach(var (name,index) in new[]{("Pioli PEd / PRd",0),("Intermedio WR",1),("Appoggio WR",2)})list.ListItems.Add(new GH_ValueListItem(name,index.ToString()){Selected=index==0});doc.AddObject(list,false);
        var preview=(GH_Component)Activator.CreateInstance(plugin.GetTypes().Single(t=>t.Name=="BridgePlotPreviewComponent"));preview.CreateAttributes();preview.Attributes.Pivot=new PointF(450,80);doc.AddObject(preview,false);
        preview.Params.Input[0].AddSource(data);preview.Params.Input[1].AddSource(list);
        var note=new GH_Panel{UserText="47 | ANTEPRIMA DEI GRAFICI\nSnapshot dei risultati dell'esempio integrato, non ricalcolati qui. Scegliere il grafico nella Value List; doppio clic per ingrandire.\nPer cambiare combinazioni, verifiche o progressive aprire 47-integrazione-modello.ghx. Il WR massimo non comprende verifiche non implementate o non attivate."};
        note.CreateAttributes();note.Attributes.Pivot=new PointF(50,-200);note.Attributes.Bounds=new RectangleF(50,-200,1080,150);doc.AddObject(note,false);
        Solve(doc);Console.WriteLine("DETAIL EXAMPLE: snapshot viewer solved.");
        string path=Path.Combine(Folder,"47-anteprima-grafici.ghx");GrasshopperBridgeExample.Write(doc,path);
        using var reopened=GrasshopperBridgeExample.Read(path);
        var restoredList=reopened.Objects.OfType<GH_ValueList>().Single();
        var restored=reopened.Objects.OfType<GH_Component>().Single();
        for(int i=0;i<3;i++)
        {
            for(int j=0;j<3;j++)restoredList.ListItems[j].Selected=j==i;
            restoredList.ExpireSolution(false);Solve(reopened);
            BridgeWorkflowTests.Require(restored.Params.Output[0].VolatileDataCount==1,"Cannot reopen/select detail chart snapshot.");
        }
        Console.WriteLine("DETAIL EXAMPLE: snapshot viewer saved, reopened and all three charts selected.");
    }
}
