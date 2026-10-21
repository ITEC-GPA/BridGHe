using System;
using System.Linq;
using BridGHe.Common.Models;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Parameters;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;

internal static class CompositeComponentTests
{
    internal static void Run()
    {
        var assembly=System.Reflection.Assembly.LoadFrom(BuildPaths.GrasshopperAssembly);
        IGH_Goo Goo(string name,object value)
        {
            var type=assembly.GetType("BridGHe.Grasshopper.DataTypes."+name,true);
            var goo=(IGH_Goo)Activator.CreateInstance(type);type.GetProperty("Value").SetValue(goo,value);return goo;
        }
        using var document=new GH_Document();
        var component=(GH_Component)Activator.CreateInstance(assembly.GetType("BridGHe.Grasshopper.Components.BridgeChecks.CompositeBeamComponent",true));
        component.CreateAttributes();document.AddObject(component,false);
        var p=new BridgeGrillageExample.Parameters();var section=BridgeGrillageExample.Composite(p);
        var beam=new BeamModel(Point3d.Origin,new(10000,0,0),BridgeGrillageExample.LongProperty(p,0),new GroupModel("Original")){BeamId="B1"};
        beam.Attributes.Add(new SuperElementAttributeModel("Spine"));
        void Set(int port,params IGH_Goo[] values)
        {
            var param=(Param_GenericObject)component.Params.Input[port];param.PersistentData.Clear();
            foreach(var value in values)param.PersistentData.Append(value);param.ExpireSolution(false);
        }
        Set(0,Goo("Fem.GH_Beam",beam));Set(1,Goo("GH_CompositeBeamProperty",section));
        Set(3,Goo("Fem.GH_Group",new GroupModel("Additional")),Goo("Fem.GH_Group",new GroupModel("Original")));
        GH_Document.EnableSolutions=true;document.Enabled=true;
        CompositeBeamModel Solve()
        {
            document.NewSolution(false,GH_SolutionMode.Silent);
            if(component.RuntimeMessages(GH_RuntimeMessageLevel.Error).Count>0)throw new Exception(string.Join("; ",component.RuntimeMessages(GH_RuntimeMessageLevel.Error)));
            return (CompositeBeamModel)component.Params.Output[0].VolatileData.AllData(true).Single().ScriptVariable();
        }
        var output=Solve();
        if(!component.Params.Input[2].Optional||output.EndBeamProperty.SteelSection.D!=section.SteelSection.D)throw new Exception("Omitted end section must use the start section.");
        if(output.BeamId!="B1"||output.Groups.Count!=2||!output.Groups.Any(g=>g.Name=="Additional")||beam.Groups.Count!=1||output.Attributes.Count!=1)
            throw new Exception("Composite assignment must retain identity/data, add unique groups and leave input unchanged.");
        var end=BridgeGrillageExample.Composite(p with{SteelHeight=1800});Set(2,Goo("GH_CompositeBeamProperty",end));output=Solve();
        if(output.StartBeamProperty.SteelSection.D!=1600||output.EndBeamProperty.SteelSection.D!=1800)throw new Exception("Explicit tapered end section was not retained.");
        VerifyLegacyStrainComponent(assembly, beam, Goo);
    }

    private static void VerifyLegacyStrainComponent(System.Reflection.Assembly assembly, BeamModel beam,
        Func<string, object, IGH_Goo> goo)
    {
        var type = assembly.GetType("BridGHe.Grasshopper.Components.BridgeChecks.CompositeBeamSlabStrainComponent", true);
        var component = (GH_Component)Activator.CreateInstance(type);
        var loadCase = new LoadCaseModel("SH", "Legacy shrinkage");
        const double strain = -.0002;
        ((Param_GenericObject)component.Params.Input[0]).PersistentData.Append(goo("Fem.GH_Beam", beam));
        ((Param_GenericObject)component.Params.Input[1]).PersistentData.Append(goo("Fem.GH_LoadCase", loadCase));
        ((Param_Number)component.Params.Input[2]).PersistentData.Append(new GH_Number(strain));

        using var document = new GH_Document { Enabled = true };
        component.CreateAttributes();
        document.AddObject(component, false);
        Verify(component, document);

        var archive = new GH_IO.Serialization.GH_Archive();
        var restored = (GH_Component)Activator.CreateInstance(type);
        if (!archive.AppendObject(component, "Legacy strain") || !archive.ExtractObject(restored, "Legacy strain"))
            throw new Exception("The obsolete strain component must retain its archived inputs.");
        using var restoredDocument = new GH_Document { Enabled = true };
        restored.CreateAttributes();
        restoredDocument.AddObject(restored, false);
        Verify(restored, restoredDocument);
        if (beam.Loads.Count != 0) throw new Exception("The obsolete strain component must leave the input beam unchanged.");

        void Verify(GH_Component candidate, GH_Document owner)
        {
            if (!candidate.Obsolete || candidate.Message != "Obsolete"
                || candidate.ComponentGuid != new Guid("adf0a668-9fee-4d5d-8665-57aa769319b0"))
                throw new Exception("The retained strain component must advertise its obsolete status and preserve its GUID.");
            candidate.ExpireSolution(false);
            owner.NewSolution(false, GH_SolutionMode.Silent);
            if (candidate.RuntimeMessages(GH_RuntimeMessageLevel.Error).Count != 0)
                throw new Exception(string.Join("; ", candidate.RuntimeMessages(GH_RuntimeMessageLevel.Error)));
            if (!candidate.RuntimeMessages(GH_RuntimeMessageLevel.Warning).Any(m => m.Contains("Bridge Shrinkage Action")))
                throw new Exception("The obsolete strain component must explain the replacement workflow.");
            var result = (BeamModel)candidate.Params.Output[0].VolatileData.AllData(true).Single().ScriptVariable();
            var load = result.Loads.OfType<CompositeBeamSlabStrainModel>().Single();
            if (result.BeamId != beam.BeamId || result.Loads.Count != 1 || load.LoadCase.Name != loadCase.Name || load.Value.Strain != strain)
                throw new Exception("Deprecation must preserve the beam, load case and unscaled dimensionless strain.");
        }
    }
}
