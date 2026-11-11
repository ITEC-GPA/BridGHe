using System;
using System.Linq;
using BridGHe.Common.Checks;
using BridGHe.Common.Diagnostics;
using BridGHe.Common.Models;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Parameters;
using Grasshopper.Kernel.Special;
using Grasshopper.Kernel.Types;

internal static class BridgePreAnalysisActionTests
{
    internal static void Run()
    {
        var assembly=BridgeMergeTests.Initialize();
#if GPC_PILOT
        // R6.6 R2b: the source is the GH model given by its GPC input alone, not analysed (no FEM model, no results).
        var input=R2bFixtures.Input(GpcWorkflowSources.Source("Unanalysed",100,analyse:false));
#else
        var model=BridgeWorkflowTests.Source("Unanalysed",100);
        foreach(var e in model.Elements)e.Results.Clear();
#endif
        using var doc=new GH_Document();
        var actions=new[]{"BridgeActionComponent","BridgeShrinkageActionComponent"}.Select(name=>
        {
            var c=(GH_Component)Activator.CreateInstance(assembly.GetTypes().Single(t=>t.Name==name));
            c.CreateAttributes();doc.AddObject(c,false);
            ((Param_String)c.Params.Input[0]).SetPersistentData(name);
#if GPC_PILOT
            var goo=R2bFixtures.Model(input);
#else
            var goo=(IGH_Goo)Activator.CreateInstance(assembly.GetType("BridGHe.Grasshopper.DataTypes.Fem.GH_Model"));
            goo.GetType().GetProperty("Value").SetValue(goo,model);
#endif
            ((Param_GenericObject)c.Params.Input[1]).PersistentData.Append(goo);
            var cases=new GH_ValueList();cases.CreateAttributes();cases.ListItems.Clear();cases.ListItems.Add(new("G","\"G\""){Selected=true});
            doc.AddObject(cases,false);c.Params.Input[2].AddSource(cases);
            if(name=="BridgeShrinkageActionComponent")((Param_Number)c.Params.Input[3]).SetPersistentData(-200e-6);
            return c;
        }).ToArray();
        GH_Document.EnableSolutions=true;doc.Enabled=true;doc.NewSolution(false,GH_SolutionMode.Silent);
        var definitions=actions.Select(c=>c.Params.Output[0].VolatileData.AllData(true).Single().ScriptVariable()).Cast<BridgeActionDefinition>().ToArray();
        var phases=new[]{new BridgePhaseDefinition{Order=10,Name="Pending",State=BridgeSectionState.Composite,Actions=definitions}};
#if GPC_PILOT
        BridgeWorkflowTests.Require(definitions.All(a=>a.Case=="G" && a.Source==null && a.GpcSource==null && a.GpcInput!=null && a.GpcInput.Model.AllElements.All(e=>e.Results.Count==0)),
            "GPC: Pre-analysis actions lost their defined case or fabricated results.");
        var catalog=BridgeCombinationCatalog.FromGpc(new[]{input},phases);
        BridgeWorkflowTests.Require(catalog.ActionIds.Length==2 && catalog.Warnings.Length==2,"GPC: Pre-analysis action catalogue must retain both actions with missing-result diagnostics.");
        bool rejected=false;
        try{BridGHe.Common.Gpc.GpcBridgeBuilder.Build(new[]{BridGHe.Common.Gpc.GpcBridgeSource.Reference(input,null)},phases,new[]{new[]{"Name",definitions[0].Key,definitions[1].Key},new[]{"C","1","1"}},
            BridgeWorkflowMethod.Compatible,1e-6,false,BridGHe.Common.Gpc.GpcIdentity.Derive(BridGHe.Common.Gpc.GpcIdentity.NameSpace,"r66-r2b-pre-analysis"),structureInput:true);}
        catch(ArgumentException){rejected=true;}
        BridgeWorkflowTests.Require(rejected,"GPC: Build must still reject unanalysed sources; an action definition is not an analysis result.");
        Console.WriteLine("PRE-ANALYSIS ACTIONS PASS: mechanical and shrinkage definitions, case lists and Excel catalogue work; Build still requires analysis results (GPC data alone).");
#else
        BridgeWorkflowTests.Require(definitions.All(a=>a.Case=="G" && a.Source.Elements.All(e=>e.Results.Count==0)),"Pre-analysis actions lost their defined case or fabricated results.");
        var catalog=new BridgeCombinationCatalog(new[]{model},phases);
        BridgeWorkflowTests.Require(catalog.ActionIds.Length==2 && catalog.Warnings.Length==2,"Pre-analysis action catalogue must retain both actions with missing-result diagnostics.");
        bool rejected=false;
        try{BridgeWorkflowBuilder.Build(new[]{model},phases,new[]{new[]{"Name",definitions[0].Key,definitions[1].Key},new[]{"C","1","1"}},BridgeWorkflowMethod.Compatible,1e-6);}
        catch(ArgumentException){rejected=true;}
        BridgeWorkflowTests.Require(rejected,"Build must still reject unanalysed sources; an action definition is not a FEM result.");
        Console.WriteLine("PRE-ANALYSIS ACTIONS PASS: mechanical and shrinkage definitions, case lists and Excel catalogue work; Build still requires FEM results.");
#endif
    }
}
