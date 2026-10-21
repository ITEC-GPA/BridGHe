using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Xml.Linq;
using BridGHe.Common.Checks;
using BridGHe.Common.Models;
using GPC.Checkers.CompositeBridge;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Parameters;
using Grasshopper.Kernel.Special;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;

internal static class BridgeDetailPlotTests
{
    private static int assertions;
    private static void Require(bool ok,string message){if(!ok){Console.Error.WriteLine("DETAIL PLOT FAILED: "+message);throw new Exception("DETAIL PLOT: "+message);}assertions++;}
    internal static BridgeStressResult Result()
    {
        var input=BridgeCheckSummaryTests.SampleResult().Analysis.Input with {
            Studs=BridgeDetailLayoutTests.Studs() with{Fu=450,GammaV=1.25},
            Intermediate=BridgeDetailLayoutTests.Stiffener() with{LeftPanel=2000,RightPanel=2000,LengthFactor=1,CheckWelds=true,WeldThroat=6},
            Support=new BridgeSupportStiffener{Enabled=true,Width=200,Thickness=25,LeftPanel=2000,RightPanel=2000,ReactionKN=200,
                LengthFactor=1,FootprintLength=400,FootprintWidth=600,CheckWelds=true,WeldThroat=10}
        };
        var analysis=HBridgeSection.Calculate(input);return new(analysis,analysis.Stages.Count-1);
    }
    internal static void Run()
    {
        assertions=0;var result=Result();
        foreach(var role in Enum.GetValues<StiffenerPlotRole>())
        {
            var checks=BridgeStiffenerPlotChecks.Checks(result,role);
            Require(checks.Length>5 && checks.All(c=>BridgeStiffenerPlotChecks.Choices(role).Contains(c.Name)),"Native check absent from individual selector.");
            var envelope=BridgeStiffenerPlotChecks.Read(result,role,null,CheckPlotDisplay.Utilization);
            Require(envelope.Unavailable=="" && envelope.Value==checks.Max(c=>c.Ratio.Value),"WR envelope differs from native maximum: "+envelope.Unavailable);
            foreach(var check in checks)
            {
                var reading=BridgeStiffenerPlotChecks.Read(result,role,check.Name,CheckPlotDisplay.DemandAndResistance);
                Require(reading.Value==check.Demand && reading.Limit==check.Resistance && reading.Unit==check.Unit,"Comparison changed gPC values or units.");
            }
        }
        // Synthetic result records isolate data selection, including native boolean WR=2 (not Demand/Resistance).
        BridgeStressResult WithChecks(params BridgeLocalCheck[] checks)
        {
            var stages=result.Analysis.Stages.ToList();stages[^1]=result.Stage with{Shear=result.Stage.Shear with{Checks=checks.ToList()}};
            return new(result.Analysis with{Stages=stages},stages.Count-1);
        }
        var tied=WithChecks(new("Appoggio · A",.8,1,"MPa",.8),new("Terminale · B",0,1,"—",2),new("Seconda coppia terminale · C",10,5,"mm",2));
        var native=BridgeStiffenerPlotChecks.Read(tied,StiffenerPlotRole.Support,null,CheckPlotDisplay.Utilization);
        Require(native.Value==2 && native.Governing.Contains("Terminale · B") && native.Governing.Contains("Seconda coppia terminale · C"),"Lost boolean native WR, second pair or ties.");
        var missing=WithChecks(new("Intermedio · A",1,2,"MPa",.5),new("Intermedio · collegamenti",0,0,"—",null,"Saldature non verificate"));
        var incomplete=BridgeStiffenerPlotChecks.Read(missing,StiffenerPlotRole.Intermediate,null,CheckPlotDisplay.Utilization);
        Require(double.IsNaN(incomplete.Value)&&incomplete.Unavailable.Contains("Saldature"),"Incomplete envelope presented as a numeric approval.");
        Require(BridgeStiffenerPlotChecks.Read(missing,StiffenerPlotRole.Intermediate,"Intermedio · A",CheckPlotDisplay.Utilization).Value==.5,"Available individual WR was lost.");
        Require(double.IsNaN(BridgeStiffenerPlotChecks.Read(missing,StiffenerPlotRole.Support,null,CheckPlotDisplay.Utilization).Value),"Absent stiffener became zero.");
        Require(double.IsNaN(BridgeStiffenerPlotChecks.Read(WithChecks(new BridgeLocalCheck("Intermedio · A",1,0,"MPa",double.PositiveInfinity)),StiffenerPlotRole.Intermediate,null,CheckPlotDisplay.Utilization).Value),"Non-finite WR accepted.");
        Components(result,missing);
        Console.WriteLine($"DETAIL PLOTS PASS: {assertions} assertions; native WR, ties, missing checks, discrete locations, units, paired curves and automatic selectors.");
    }

    private static void Components(BridgeStressResult result,BridgeStressResult missing)
    {
        var plugin=BridgeMergeTests.Initialize();
        using var doc=new GH_Document{Enabled=true};
        var model=new ModelModel(ModelModel.ModelUnits.Nmm);
        model.AddElements(new[]{new NodeModel("1",Point3d.Origin)});
        model.AddLoadCombinations(new[]{new LoadCombinationModel("C1"),new LoadCombinationModel("C2")});
        var beam=new CompositeBeamModel{PointFrom=Point3d.Origin,PointTo=new Point3d(3000,0,0)};
        var load=new LoadCaseModel("Q","Q");
        foreach(double station in new[]{0.0,1000,2000,3000})beam.Results.Add(new BeamForceResultModel(load,station,0,""));
        var modelGoo=(IGH_Goo)Activator.CreateInstance(plugin.GetType("BridGHe.Grasshopper.DataTypes.Fem.GH_Model",true));modelGoo.GetType().GetProperty("Value").SetValue(modelGoo,model);
        var beamGoo=(IGH_Goo)Activator.CreateInstance(plugin.GetType("BridGHe.Grasshopper.DataTypes.GH_CompositeBeam",true),new object[]{beam,null});
        string folder = Path.GetFullPath(Path.Combine("artifacts/detail-plots", Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(folder);
        XNamespace ns="http://www.w3.org/2000/svg";
        foreach(var (type,role) in new[]{("BridgeIntermediateStiffenerPlotComponent",StiffenerPlotRole.Intermediate),("BridgeSupportStiffenerPlotComponent",StiffenerPlotRole.Support),("BridgeStudPlotComponent",StiffenerPlotRole.Intermediate)})
        {
            Console.WriteLine("DETAIL PLOT: " + type);
            var component=(GH_Component)Activator.CreateInstance(plugin.GetTypes().Single(t=>t.Name==type));component.CreateAttributes();doc.AddObject(component,false);
            ((Param_GenericObject)component.Params.Input[0]).PersistentData.Append(modelGoo);
            ((Param_GenericObject)component.Params.Input[1]).PersistentData.Append(beamGoo);
            var data=((Param_GenericObject)component.Params.Input[2]).PersistentData;
            for(int s=0;s<4;s++)for(int c=0;c<2;c++)data.Append(new GH_ObjectWrapper(result),new GH_Path(0,s));
            var quantity=(Param_Integer)component.Params.Input[3];quantity.PersistentData.Clear();
            var live=(Param_Boolean)component.Params.Input[8];live.PersistentData.Clear();live.SetPersistentData(true);
            bool studs=type=="BridgeStudPlotComponent";
            var display=studs?null:(Param_Integer)component.Params.Input[9];
            var stations=studs?null:(Param_Number)component.Params.Input[10];
            if(!studs)stations.SetPersistentData(0,2,3);
            quantity.SetPersistentData(studs?5:0);Solve();
            var xml=Svg();
            Require(xml.Descendants(ns+"text").Any(e=>e.Value=="0"),"Missing zero axis.");
            if(studs)
            {
                Require(TitledPaths(xml).Any(p=>p.Element(ns+"title").Value=="C1 / Ed")&&TitledPaths(xml).Any(p=>p.Element(ns+"title").Value=="C1 / Rd"),"Missing paired stud curves.");
                Require(Number(2)==Math.Min(result.Stage.Studs.ForcePerStud,result.Stage.Studs.ResistancePerStud),"Wrong comparison extrema or kN conversion.");
                quantity.PersistentData.Clear();quantity.SetPersistentData(6);Solve();
                Require(Number(3)==Math.Max(Math.Abs(result.Stage.Studs.Flow),result.Stage.Studs.ResistancePerLength),"Wrong flow comparison units.");
                quantity.PersistentData.Clear();quantity.SetPersistentData(4);Solve();
                Require(TitledPaths(Svg()).Any(p=>p.Element(ns+"title").Value=="Reference 1"),"Missing WR=1 reference.");
            }
            else
            {
                var paths=TitledPaths(xml).Where(p=>p.Element(ns+"title").Value=="C1").ToArray();
                Require(paths.Length==1 && paths[0].Attribute("d").Value.Count(c=>c=='M')==3 && !paths[0].Attribute("d").Value.Contains('L'),"Stiffeners were interpolated between actual locations.");
                Require(component.Params.Output[4].VolatileData.Paths.Select(p=>p.ToString()).SequenceEqual(new[]{"{0;0}","{0;2}","{0;3}"}),"Governing checks lost location paths.");
                Require(Number(3)==BridgeStiffenerPlotChecks.Checks(result,role).Max(c=>c.Ratio.Value),"Reference line corrupted WR extrema.");
                stations.PersistentData.Clear();stations.SetPersistentData(0);Solve();
                Require(TitledPaths(Svg()).Single(p=>p.Element(ns+"title").Value=="Reference 1").Attribute("d").Value.Contains("L1465,"),"WR=1 reference collapsed to a point for one stiffener.");
                stations.PersistentData.Clear();stations.SetPersistentData(0,2,3);Solve();
                var list=new GH_ValueList();list.CreateAttributes();doc.AddObject(list,false);quantity.AddSource(list);Solve();
                var choices=BridgeStiffenerPlotChecks.Choices(role);
                Require(list.ListMode==GH_ValueListMode.DropDown && list.ListItems.Count==choices.Length,"Value List not populated.");
                string checkName=(role==StiffenerPlotRole.Intermediate?"Intermedio":"Appoggio")+" · stabilità e pressoflessione II ordine";
                int selected=Array.IndexOf(choices,checkName);
                for(int i=0;i<list.ListItems.Count;i++)list.ListItems[i].Selected=i==selected;
                list.ExpireSolution(false);display.PersistentData.Clear();display.SetPersistentData(3);Solve();
                var check=BridgeStiffenerPlotChecks.Checks(result,role).Single(c=>c.Name==checkName);
                Require(Number(3)==Math.Max(check.Demand,check.Resistance)&&Svg().Root.Value.Contains("[MPa]"),"Physical-unit comparison does not match gPC.");
                Require(TitledPaths(Svg()).Any(p=>p.Element(ns+"title").Value=="C1 / Rd"),"Missing stiffener resistance series.");
                // Selecting all checks forces WR even if a dimensional display was previously selected.
                for(int i=0;i<list.ListItems.Count;i++)list.ListItems[i].Selected=i==0;
                list.ExpireSolution(false);Solve();Require(Svg().Root.Value.Contains("[WR]"),"Envelope mixed physical units.");
                stations.PersistentData.Clear();stations.SetPersistentData(1.5);Solve(true);
                Require(component.RuntimeMessages(GH_RuntimeMessageLevel.Error).Any(s=>s.Contains("No result station")),"Unmatched station was silently dropped.");
                stations.PersistentData.Clear();stations.SetPersistentData(0,2,3);Solve();
                if(role==StiffenerPlotRole.Intermediate)
                {
                    data.Clear();for(int s=0;s<4;s++)for(int c=0;c<2;c++)data.Append(new GH_ObjectWrapper(s==2?missing:result),new GH_Path(0,s));
                    Solve();Require(component.RuntimeMessages(GH_RuntimeMessageLevel.Warning).Count>0 && component.Params.Output[5].VolatileData.AllData(true).Any(g=>g.ScriptVariable().ToString().Contains("collegamenti"))
                        && Svg().Root.Value.Contains("WR incompleti/non disponibili"),"Incomplete WR not reported in component and exported chart.");
                }
                quantity.RemoveAllSources();doc.RemoveObject(list,false);Solve();
            }
            Console.WriteLine("DETAIL PLOT: exporting PNG " + type);
            var file=(Param_String)component.Params.Input[6];file.PersistentData.Clear();file.SetPersistentData(Path.Combine(folder,type+".png"));Solve();
            using(var png=System.Drawing.Image.FromFile((string)component.Params.Output[1].VolatileData.AllData(true).Single().ScriptVariable()))
                Require(png.Width==1500&&png.Height>680,"Native PNG export failed.");
            var before = File.ReadAllBytes(Path.Combine(folder,type+".png"));
            var title = (Param_String)component.Params.Input[7]; title.PersistentData.Clear(); title.SetPersistentData("Updated chart"); Solve();
            Require(!before.SequenceEqual(File.ReadAllBytes(Path.Combine(folder,type+".png"))), "PNG overwrite retained the old chart.");
            File.WriteAllText(Path.Combine(folder,type+".svg"),Svg().ToString(SaveOptions.DisableFormatting));
            doc.RemoveObject(component,false);

            System.Collections.Generic.IEnumerable<XElement> TitledPaths(XDocument x)=>x.Descendants(ns+"path").Where(p=>p.Element(ns+"title")!=null);
            double Number(int port)=>(double)component.Params.Output[port].VolatileData.AllData(true).Single().ScriptVariable();
            XDocument Svg()=>XDocument.Parse((string)component.Params.Output[0].VolatileData.AllData(true).Single().ScriptVariable());
            void Solve(bool error=false)
            {
                foreach(var p in component.Params.Input)p.ExpireSolution(false);
                component.ExpireSolution(false);GH_Document.EnableSolutions=true;doc.NewSolution(false,GH_SolutionMode.Silent);
                var errors=component.RuntimeMessages(GH_RuntimeMessageLevel.Error);
                Require(error?errors.Count>0&&component.Params.Output[0].VolatileData.AllData(true).All(g=>g?.ScriptVariable() is not string s||string.IsNullOrWhiteSpace(s))
                    :errors.Count==0&&component.Params.Output[0].VolatileDataCount==1,"Component error: "+string.Join("; ",errors));
            }
        }
    }
}
