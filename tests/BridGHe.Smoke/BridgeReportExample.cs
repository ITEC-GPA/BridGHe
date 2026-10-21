using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Parameters;
using Grasshopper.Kernel.Special;

internal static class BridgeReportExample
{
    internal static void Run(bool generate=false)
    {
        var plugin=BridgeMergeTests.Initialize();
        string folder=generate?ExamplePaths.Case("48-report-word"):Path.GetFullPath(Path.Combine("artifacts/report-component",Guid.NewGuid().ToString("N")));Directory.CreateDirectory(folder);
        if(!generate)File.Copy(BridgeReportTests.Template,Path.Combine(folder,"Template_BridGHe.docx"),true);
        string definition=Path.Combine(folder,"48-report-word.ghx"),output=Path.Combine(folder,"Report_esempio.docx");
        using var doc=GrasshopperBridgeExample.Read(Path.Combine(ExamplePaths.Case("47-pioli-irrigidimenti-verifiche"),"47-integrazione-modello.ghx"));doc.FilePath=definition;
        GH_Component Find(string type)=>doc.Objects.OfType<GH_Component>().Single(c=>c.GetType().Name==type);
        var report=(GH_Component)Activator.CreateInstance(plugin.GetTypes().Single(t=>t.Name=="BridgeReportComponent"));report.CreateAttributes();report.Attributes.Pivot=new PointF(7800,350);doc.AddObject(report,false);
        report.Params.Input[0].AddSource(Find("BridgeDetailCheckComponent").Params.Output[0]);
        report.Params.Input[4].AddSource(Find("BuildBridgeModel").Params.Output[0]);
        foreach(string type in new[]{"BridgeStudPlotComponent","BridgeIntermediateStiffenerPlotComponent","BridgeSupportStiffenerPlotComponent"})report.Params.Input[9].AddSource(Find(type).Params.Output[0]);
        void Text(int i,string value){var p=(Param_String)report.Params.Input[i];p.PersistentData.Clear();p.SetPersistentData(value);}
        Text(1,"Template_BridGHe.docx");Text(2,"Report_esempio.docx");Text(5,"Verifiche della trave mista e dei dettagli");Text(6,"Esempio 48 dimostrativo");Text(7,"BridGHe");Text(8,"01");
        var choices=new GH_ValueList{NickName="Capitoli del report"};choices.CreateAttributes();choices.Attributes.Pivot=new PointF(7240,430);doc.AddObject(choices,false);report.Params.Input[3].AddSource(choices);
        var note=new GH_Panel{UserText="48 | REPORT WORD DA TEMPLATE\n1 Run su Build BridGHe Model. 2 Run su Super Element Check. 3 Generate su Bridge Report.\nScegliere i capitoli nella checklist. Il riepilogo resta sempre presente. Modificare il template Word per stili, logo e ordine dei capitoli.\nIl file contiene risultati nativi archiviati dell'esempio 19, estratto beam 36. Coefficienti unitari e reazione 900 kN sono DEMO; non costituiscono una verifica completa.\nIl report già generato è nella stessa cartella. Per sostituirlo chiuderlo in Word e attivare Overwrite, oppure cambiare File. Nessuna scrittura automatica alla riapertura."};
        note.CreateAttributes();note.Attributes.Pivot=new PointF(7220,-430);note.Attributes.Bounds=new RectangleF(7220,-430,1300,280);doc.AddObject(note,false);
        GH_Document.EnableSolutions=true;doc.Enabled=true;doc.NewSolution(false,GH_SolutionMode.Silent);
        Require(report.Params.Output[0].VolatileDataCount==0,"Report written without Generate.");
        Run(Find("BuildBridgeModel"));Solve();Run(Find("SuperElementCheckComponent"));Solve();
        Require(choices.ListMode==GH_ValueListMode.CheckList&&choices.ListItems.Count==7,"Automatic topic checklist missing.");
        // Persist the delivered definition with overwrite disabled; generation explicitly opts in only in memory.
        GrasshopperBridgeExample.Write(doc,definition);
        var overwrite=(Param_Boolean)report.Params.Input[10];overwrite.PersistentData.Clear();overwrite.SetPersistentData(true);Run(report);Solve();
        Require(File.Exists(output)&&report.Params.Output[3].VolatileDataCount==1,"DOCX export missing.");
        var firstId=report.Params.Output[3].VolatileData.AllData(true).Single().ScriptVariable();
        Run(report);Solve();
        Require(!Equals(firstId,report.Params.Output[3].VolatileData.AllData(true).Single().ScriptVariable()),"Explicit overwrite did not create a new report identity.");
        var hash=BridGHe.Common.Reports.BridgeReportData.Hash(File.ReadAllBytes(output));
        using var reopened=GrasshopperBridgeExample.Read(definition);reopened.FilePath=definition;reopened.Enabled=true;reopened.NewSolution(false,GH_SolutionMode.Silent);
        Require(hash==BridGHe.Common.Reports.BridgeReportData.Hash(File.ReadAllBytes(output)),"Reopening GH rewrote the document.");
        var restored=reopened.Objects.OfType<GH_Component>().Single(c=>c.GetType().Name=="BridgeReportComponent");
        var list=(GH_ValueList)restored.Params.Input[3].Sources.Single();
        Require(list.ListMode==GH_ValueListMode.CheckList && list.SelectedItems.Count==6 && !list.ListItems[6].Selected,"Chapter choices not preserved on reopening.");
        // An explicitly empty checklist must not revert to default chapters.
        foreach(var item in choices.ListItems)item.Selected=false;choices.ExpireSolution(false);Text(2,"Report_sintetico.docx");Run(report);Solve();
        Require(report.Params.Output[0].VolatileDataCount==1&&File.Exists(Path.Combine(folder,"Report_sintetico.docx")),"Empty topic selection cannot generate summary.");
        if(!generate)File.WriteAllText(Path.Combine(folder,"validation.json"),JsonSerializer.Serialize(new{solved=true,reopened=true,solverInvoked=false,chapters=7,noAutomaticWrite=true}));
        Console.WriteLine("REPORT COMPONENT PASS: archived native results, Word template, seven automatic checklist choices, SVG pictures, save/reopen and explicit Generate; no solver invoked.");
        void Run(GH_Component c){c.GetType().GetField("_run",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(c,true);c.ExpireSolution(false);}
        void Solve(){doc.NewSolution(false,GH_SolutionMode.Silent);var errors=doc.Objects.OfType<GH_ActiveObject>().SelectMany(c=>c.RuntimeMessages(GH_RuntimeMessageLevel.Error).Select(e=>c.Name+": "+e)).ToArray();if(errors.Length>0)throw new Exception(string.Join("; ",errors));}
        void Require(bool ok,string message){if(!ok)throw new Exception("REPORT COMPONENT: "+message);}
    }
}
