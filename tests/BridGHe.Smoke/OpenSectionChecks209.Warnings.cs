using System;
using System.Linq;
using BridGHe.Common.Gpc;
using BridGHe.Common.Gpc.Legacy;
using GPC.Model.ElementProperties;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Parameters;

internal static partial class OpenSectionChecks190
{
    internal static int Warnings209()
    {
        checks=failed=0;plugin=BridgeMergeTests.Initialize();
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
                Check(item.Item5?warnings.Count==1&&warnings[0]==message:warnings.Count==0,label+" expected warning="+item.Item5);
                Check(component.RuntimeMessages(GH_RuntimeMessageLevel.Error).Count==0&&component.Params.Output[0].VolatileData.DataCount==1,label+" still produces a section without errors");
                var goo=component.Params.Output[0].VolatileData.AllData(true).Single();
                var recipe=(LegacySectionRecipe)goo.GetType().GetProperty("Gpc").GetValue(goo);
                Check(recipe.B==values[0]&&recipe.T1==values[1]&&recipe.D==values[2]&&recipe.T2==values[3]&&recipe.MirrorGapA==(mirror?120/scale:0),label+" recipe values unchanged");
                // Reusing a warned component with thin inputs clears the warning on the next solution.
                foreach(int index in new[]{1,3})
                {
                    var parameter=(Param_Number)component.Params.Input[index];parameter.PersistentData.Clear();parameter.SetPersistentData(1/scale);
                }
                component.ExpireSolution(false);doc.NewSolution(false,GH_SolutionMode.Silent);
                Check(component.RuntimeMessages(GH_RuntimeMessageLevel.Warning).Count==0,label+" warning clears after changing to thin inputs");
            }
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
