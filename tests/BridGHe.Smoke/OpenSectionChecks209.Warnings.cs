using System;
using System.Linq;
using BridGHe.Common.Gpc;
using BridGHe.Common.Gpc.Legacy;
using GPC.Model.ElementProperties;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Parameters;
using Grasshopper.Kernel.Types;

internal static partial class OpenSectionChecks190
{
    internal static int Warnings209()
    {
        checks=failed=0;plugin=BridgeMergeTests.Initialize();
        // Option B (D7 of 17/11/2026): J and shear centre of T, C and Angle are the numerical ones of Model, also for the thick profiles,
        // so the advisory warning of task 209 (decision 14b) and of D5 of 0.4.2 part 1 is gone. These cases are those that warned.
        const string message="Thick open section: thin-wall torsion and shear centre may be inaccurate.";
        foreach(string type in new[]{"T","C","Angle"})
        foreach(double scale in new[]{1d,1000d})
        foreach(bool mirror in type=="T"?new[]{false}:new[]{false,true})
        {
            var thick=type=="T"?(48d,40d,18d,16d,true):(40d,30d,14d,12d,true);
            var cases=new[]{(100d,200d,10d,10d,false),(100d,200d,30d,10d,false),(100d,200d,30.0001,10d,true),
                (100d,200d,10d,60d,false),(100d,200d,10d,60.0001,true),thick};
            if(type=="C")cases=cases.Append((300d,80d,12d,10d,false)).ToArray();
            foreach(var item in cases)
            {
                using var doc=new GH_Document();
                var component=(GH_Component)Activator.CreateInstance(plugin.GetType("BridGHe.Grasshopper.Components.Sections.BeamSection"+type+"Component",true));
                component.CreateAttributes();doc.AddObject(component,false);
                double[] values={item.Item1/scale,item.Item3/scale,item.Item2/scale,item.Item4/scale};
                for(int i=0;i<4;i++){var parameter=(Param_Number)component.Params.Input[i];parameter.PersistentData.Clear();parameter.SetPersistentData(values[i]);}
                if(type!="T")
                {
                    var flag=(Param_Boolean)component.Params.Input[4];flag.PersistentData.Clear();flag.SetPersistentData(mirror);
                    var gap=(Param_Number)component.Params.Input[5];gap.PersistentData.Clear();gap.SetPersistentData(mirror?120/scale:0);
                }
                GH_Document.EnableSolutions=true;doc.Enabled=true;doc.NewSolution(false,GH_SolutionMode.Silent);
                string label=$"209 {type} {item.Item1}x{item.Item2} tf={item.Item3} tw={item.Item4}, scale={scale}, mirror={mirror}";
                var warnings=component.RuntimeMessages(GH_RuntimeMessageLevel.Warning);
                Check(warnings.Count==0,label+" option B: no thick-section warning (formerly "+item.Item5+")");
                Check(component.RuntimeMessages(GH_RuntimeMessageLevel.Error).Count==0&&component.Params.Output[0].VolatileData.DataCount==1,label+" still produces a section without errors");
                var goo=component.Params.Output[0].VolatileData.AllData(true).Single();
                var recipe=(LegacySectionRecipe)goo.GetType().GetProperty("Gpc").GetValue(goo);
                Check(recipe.B==values[0]&&recipe.T1==values[1]&&recipe.D==values[2]&&recipe.T2==values[3]&&recipe.MirrorGapA==(mirror?120/scale:0),label+" recipe values unchanged");
            }
        }
        // D5 of part 1 is gone with the warning: a section given as a text (a Panel) to Beam Property or Section Geometric Property
        // warns in neither component, thick or thin.
        IGH_Goo built=null;
        {
            using var doc=new GH_Document();
            var t=(GH_Component)Activator.CreateInstance(plugin.GetType("BridGHe.Grasshopper.Components.Sections.BeamSectionTComponent",true));
            t.CreateAttributes();doc.AddObject(t,false);
            double[] values={48,18,40,16};
            for(int i=0;i<4;i++){var parameter=(Param_Number)t.Params.Input[i];parameter.PersistentData.Clear();parameter.SetPersistentData(values[i]);}
            GH_Document.EnableSolutions=true;doc.Enabled=true;doc.NewSolution(false,GH_SolutionMode.Silent);
            Check(t.RuntimeMessages(GH_RuntimeMessageLevel.Warning).Count==0,"option B: the thick T of the component gives no warning");
            built=t.Params.Output[0].VolatileData.AllData(true).Single().Duplicate();
        }
        var texts=new (object Section,string Label)[]{(new GH_String("T40x48x18x16"),"T40x48x18x16"),(new GH_String("C30x40x14x12"),"C30x40x14x12"),
            (new GH_String("L40x30x14x12"),"L40x30x14x12"),(new GH_String("2xC30x40x14x12x120"),"2xC30x40x14x12x120"),(new GH_String("2xL30x40x14x12x120"),"2xL30x40x14x12x120"),
            (new GH_String("T200x100x10x10"),"T200x100x10x10"),(new GH_String("C200x100x10x10"),"C200x100x10x10"),(new GH_String("L100x100x10x10"),"L100x100x10x10"),
            (new GH_String("2xL100x80x8x10x12"),"2xL100x80x8x10x12"),(new GH_String("I400x200x200x16x16x10"),"I400x200x200x16x16x10"),(built,"thick T of the component")};
        foreach(var (section,text) in texts)
        foreach(string type in new[]{"ElementProperties.BeamPropertyComponent","Sections.SectionGeometricPropertyComponent"})
        {
            using var doc=new GH_Document();
            var component=(GH_Component)Activator.CreateInstance(plugin.GetType("BridGHe.Grasshopper.Components."+type,true));
            component.CreateAttributes();doc.AddObject(component,false);
            int input=0;
            if(type.StartsWith("Element"))
            {
                var name=(Param_String)component.Params.Input[0];name.PersistentData.Clear();name.SetPersistentData("P");
                var kind=(Param_Integer)component.Params.Input[1];kind.PersistentData.Clear();kind.SetPersistentData(5);
                // Every input is required: without a material Grasshopper never solves the component.
                var material=(Param_GenericObject)component.Params.Input[3];material.PersistentData.Clear();material.SetPersistentData(R4aFixtures.Material(R4aFixtures.S355()));
                input=2;
            }
            var parameter=(Param_GenericObject)component.Params.Input[input];parameter.PersistentData.Clear();parameter.SetPersistentData(section);
            GH_Document.EnableSolutions=true;doc.Enabled=true;doc.NewSolution(false,GH_SolutionMode.Silent);
            bool warned=component.RuntimeMessages(GH_RuntimeMessageLevel.Warning).Contains(message);
            Check(!warned,$"option B: {component.Name} with the section {text} gives no thick-section warning");
        }
        foreach(var row in Measurements209)
        {
            var model=Model198(row.Case,DefinitionUnits.Nmm,0,0,false);
            var property=(ElasticBeamProperty)model.Model.BeamElements.Values.Single().BeamProperty;
            Check(Expected198(property,row.Case).SequenceEqual(row.Gpc),"209 all eleven live GPC properties retain the measured values "+row.Case.Label);
        }
        Console.WriteLine($"SECTION WARNINGS 209: {checks-failed}/{checks} PASS, {failed} FAIL");
        return failed==0?0:1;
    }
}
