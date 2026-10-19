using System;
using System.Collections.Generic;
using System.Linq;
using BridGHe.Common.Checks;
using BridGHe.Common.Models;
using GPC.Checkers.CompositeBridge;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Parameters;
using Grasshopper.Kernel.Types;

internal static class NativeBridgeComponentTests
{
    internal static void Run()
    {
        var assembly=System.Reflection.Assembly.LoadFrom(BuildPaths.GrasshopperAssembly);
        var detailType=assembly.GetType("BridGHe.Grasshopper.DataTypes.BridgeDetailGoo",true);
        IGH_Goo Detail(object value)=>(IGH_Goo)Activator.CreateInstance(detailType,new[]{value});
        var materialType=assembly.GetType("BridGHe.Grasshopper.DataTypes.Fem.GH_Material",true);
        IGH_Goo Material(MaterialModel value)=>(IGH_Goo)Activator.CreateInstance(materialType,new object[]{value});
        using var doc=new GH_Document();
        var definitions=assembly.GetTypes().Where(t=>!t.IsAbstract&&t.BaseType?.IsGenericType==true&&t.BaseType.GetGenericTypeDefinition().Name=="DetailDefinition`1")
            .Select(t=>(GH_Component)Activator.CreateInstance(t)).ToArray();
        foreach(var component in definitions){component.CreateAttributes();doc.AddObject(component,false);}
        Grasshopper.Kernel.GH_Document.EnableSolutions=true;doc.Enabled=true;doc.NewSolution(false,GH_SolutionMode.Silent);
        var definitionsByType=new Dictionary<Type,object>();
        foreach(var component in definitions)
        {
            if(component.RuntimeMessages(GH_RuntimeMessageLevel.Error).Count>0||component.Params.Output[0].VolatileDataCount!=1)
                throw new Exception("Default gPC definition failed: "+component.Name);
            var goo=component.Params.Output[0].VolatileData.AllData(true).Single();
            var value=detailType.GetProperty("Value").GetValue(goo);definitionsByType.Add(value.GetType(),value);
            var archive=new GH_IO.Serialization.GH_Archive();archive.AppendObject(goo,"data");var copy=Detail(value);
            if(!archive.ExtractObject(copy,"data")||!detailType.GetProperty("Value").GetValue(copy).Equals(value))throw new Exception("gPC definition archive changed: "+value.GetType().Name);
            foreach(var input in component.Params.Input)if(!input.Optional)throw new Exception("Definition with defaults must allow omitted input: "+input.Name);
        }
        var concrete=new MaterialModel("C30"){Name="C30",SpecificCompressiveStrength=30};
        var steel=new MaterialModel("S355"){Name="S355",Modulus=210000,MinimumYieldStress=355,MinimumTensileStress=510};
        var rebar=new MaterialModel("B450"){Name="B450",Modulus=200000,MinimumYieldStress=450,MinimumTensileStress=540};
        var phases=new[]{new BridgePhase{Name="Ignored",Active=false,ShearKN=999999},new BridgePhase{Name="Steel",Kind=BridgePhaseKind.SteelOnly,MomentKNm=800,ShearKN=300},
            new BridgePhase{Name="Long",MomentKNm=200,ShearKN=100,Phi=2},new BridgePhase{Name="Instant",MomentKNm=400,ShearKN=-50},new BridgePhase{Name="Shrinkage",Kind=BridgePhaseKind.Shrinkage,ShrinkageMicrostrain=-200,Phi=2}};
        var checker=(GH_Component)Activator.CreateInstance(assembly.GetType("BridGHe.Grasshopper.Components.BridgeDetails.BridgeSectionCheckComponent",true));
        checker.CreateAttributes();doc.AddObject(checker,false);
        void Set(int port,params IGH_Goo[] values){var p=(Param_GenericObject)checker.Params.Input[port];p.PersistentData.Clear();foreach(var v in values)p.PersistentData.Append(v);p.ExpireSolution(false);}
        Set(1,Material(concrete));Set(2,Material(steel));Set(3,Material(rebar));Set(4,phases.Select(p=>Detail(p)).ToArray());
        var row=(BridgeRebarRow)definitionsByType[typeof(BridgeRebarRow)];Set(5,Detail(row));Set(6,Detail(row));
        var options=(BridgeAnalysisOptions)definitionsByType[typeof(BridgeAnalysisOptions)];options=options with{LimitState=BridgeLimitState.Ultimate,Class4=false};Set(8,Detail(options));
        var studs=(BridgeStudOptions)definitionsByType[typeof(BridgeStudOptions)];
        var details=new object[]{studs,definitionsByType[typeof(BridgeTransverseReinforcement)],definitionsByType[typeof(BridgeFatigueOptions)]};
        foreach(var kind in new[]{BridgeSteelSectionType.H,BridgeSteelSectionType.InclinedWebH,BridgeSteelSectionType.Box})
        {
            var geometry=((HSectionDimensions)definitionsByType[typeof(HSectionDimensions)]) with{SectionType=kind,WebOffset=kind==BridgeSteelSectionType.H?0:100,BottomWidth=kind==BridgeSteelSectionType.Box?2200:600,SlabWidth=kind==BridgeSteelSectionType.Box?5000:2200};
            Set(0,Detail(geometry));
            var current=details.ToList();if(kind==BridgeSteelSectionType.Box)current.Add(new BridgeBoxOptions{Enabled=true,BracingThickness=8,BearingSpacing=1800});
            var currentPhases=phases.Select(p=>kind==BridgeSteelSectionType.Box&&p.Kind!=BridgePhaseKind.Shrinkage?p with{TorsionKNm=30}:p).ToArray();
            Set(4,currentPhases.Select(p=>Detail(p)).ToArray());Set(7,current.Select(Detail).ToArray());checker.ExpireSolution(false);doc.NewSolution(false,GH_SolutionMode.Silent);
            var errors=checker.RuntimeMessages(GH_RuntimeMessageLevel.Error);if(errors.Count>0)throw new Exception(kind+": "+string.Join("; ",errors));
            var outputs=checker.Params.Output[0].VolatileData.AllData(true).Select(g=>(BridgeStressResult)g.ScriptVariable()).ToArray();
            var expected=HBridgeSection.Calculate(BridgeGpc.WithDetails(new HBridgeInput{Geometry=geometry,Materials=BridgeGpc.Materials(concrete,steel,rebar),TopRebars=row,BottomRebars=row,Options=options,Phases=currentPhases.Where(p=>p.Active).ToArray()},current));
            if(outputs.Length!=4)throw new Exception("Expected three mechanical phases and shrinkage; inactive phases excluded.");
            for(int i=0;i<outputs.Length;i++)
            {
                if(!outputs[i].Stage.Points.Select(p=>p.Stress).SequenceEqual(expected.Stages[i].Points.Select(p=>p.Stress)))throw new Exception($"Native gPC stresses changed: requested {kind}, output {outputs[i].Analysis.Input.Geometry.SectionType}, stage {i}; actual {string.Join(",",outputs[i].Stage.Points.Select(p=>p.Stress))}; expected {string.Join(",",expected.Stages[i].Points.Select(p=>p.Stress))}");
                if(outputs[i].Stage.Shear.TauMaximum!=expected.Stages[i].Shear.TauMaximum||outputs[i].Stage.Studs.Flow!=expected.Stages[i].Studs.Flow)throw new Exception("Native tau or connection flow changed.");
            }
            foreach(string name in new[]{"BridgeShearResultsComponent","BridgeStudResultsComponent","BridgeVerificationResultsComponent"})
            {
                var extractor=(GH_Component)Activator.CreateInstance(assembly.GetType("BridGHe.Grasshopper.Components.BridgeDetails."+name,true));extractor.CreateAttributes();doc.AddObject(extractor,false);
                ((Param_GenericObject)extractor.Params.Input[0]).PersistentData.Append(new GH_ObjectWrapper(outputs.Last()));doc.NewSolution(false,GH_SolutionMode.Silent);
                if(extractor.RuntimeMessages(GH_RuntimeMessageLevel.Error).Count>0||extractor.Params.Output[0].VolatileDataCount==0)throw new Exception("Result extraction failed: "+name);
                doc.RemoveObject(extractor,false);
            }
            if(!BridgeGpc.Checks(outputs.Last().Stage).Any(c=>c.Name.Contains("Pioli")))throw new Exception("Missing native stud checks.");
            Console.WriteLine("GPC COMPONENT: "+kind+", three phases + shrinkage, direct library results exactly equal.");
        }
    }
}

