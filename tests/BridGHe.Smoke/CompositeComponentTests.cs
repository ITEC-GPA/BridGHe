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
        var beamGoo=Goo("Fem.GH_Beam",beam);
#if GPC_PILOT
        var analysed=GpcAnalysedBeam();
        beamGoo.GetType().GetProperty("AnalysedGpc").SetValue(beamGoo,analysed.Beam);beamGoo.GetType().GetProperty("AnalysedGpcModel").SetValue(beamGoo,analysed.Model);
#endif
        Set(0,beamGoo);Set(1,Goo("GH_CompositeBeamProperty",section));
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
#if GPC_PILOT
        int checks=Gpc(component,p,null,analysed);
#endif
        var end=BridgeGrillageExample.Composite(p with{SteelHeight=1800});Set(2,Goo("GH_CompositeBeamProperty",end));output=Solve();
        if(output.StartBeamProperty.SteelSection.D!=1600||output.EndBeamProperty.SteelSection.D!=1800)throw new Exception("Explicit tapered end section was not retained.");
#if GPC_PILOT
        checks+=Gpc(component,p,1800,analysed);
        Console.WriteLine($"COMPOSITE BEAM GPC PASS: {checks} checks on the definitions of the composite beam (omitted and tapered end), equal to definitions built in GPC, and on its link to the analysed GPC beam.");
#endif
        VerifyLegacyStrainComponent(assembly, beam, Goo);
    }

#if GPC_PILOT
    /// <summary>P2U: the analysed GPC beam of the FEM fixture (B1, 10 m along X), built in GPC, the link that Composite Beam passes from
    /// its beam to the composite beam (Girder Results and Super Element Check read the GPC results through it).</summary>
    private static (GPC.Model.Elements.BeamElement Beam, GPC.Model.Models.Model Model) GpcAnalysedBeam()
    {
        var test=new GpcTestModel("Composite beam component");test.Case("Q");
        var beam=test.Beam("B1",test.Node("1",0,0,0),test.Node("2",10000,0,0));
        foreach(double s in new[]{0d,5000,10000})test.Forces(beam,"Q",s,s);
        return (beam,test.Analyse());
    }

    /// <summary>P2U (F6, chain E (c)): the definitions the composite beam carries (what Super Element Check, Linear Tension and the preview
    /// read): with the end omitted the end definition is the start one (a constant section for the rule of Super Element Check); with an
    /// explicit tapered end the start and end definitions keep their steel heights (a variable section). Both equal bit for bit to the
    /// definitions built in GPC from the numbers of the composite properties (GpcCompositeDefinitions), the steel H named by the property.
    /// The composite beam keeps the link of its beam to the analysed GPC beam and model. Still FEM until chain E (e): the beam input
    /// (GH_Beam) and its BridGHe data (ID, groups, attributes), checked above.</summary>
    private static int Gpc(GH_Component component,BridgeGrillageExample.Parameters p,double? endHeight,(GPC.Model.Elements.BeamElement Beam,GPC.Model.Models.Model Model) analysed)
    {
        void Require(bool ok,string message){if(!ok)throw new Exception("GPC: "+message);}
        var goo=component.Params.Output[0].VolatileData.AllData(true).Single();
        T Get<T>(string name)=>(T)goo.GetType().GetProperty(name).GetValue(goo);
        var start=Get<GPC.Checkers.CompositeBridge.BridgeCompositeSectionDefinition>("StartDefinition");
        var end=Get<GPC.Checkers.CompositeBridge.BridgeCompositeSectionDefinition>("EndDefinition");
        Require(start!=null && end!=null,"the composite beam must carry its start and end definitions.");
        double Height(GPC.Checkers.CompositeBridge.BridgeCompositeSectionDefinition d)=>((GPC.Model.Sections.SectionH)d.Section.SteelSections[0].Section.SectionShape).Height;
        if(endHeight==null)
        {
            Require(ReferenceEquals(end,start) && Height(end)==p.SteelHeight,"Omitted end section must use the start definition.");
            Require(BridGHe.Common.Gpc.GpcCompositeSections.IsConstant(start,end),"Omitted end section must give a constant section.");
        }
        else
        {
            Require(Height(start)==p.SteelHeight && Height(end)==endHeight.Value,"Explicit tapered end definition was not retained.");
            Require(!BridGHe.Common.Gpc.GpcCompositeSections.IsConstant(start,end),"An explicit tapered end must give a variable section.");
        }
        Require(ReferenceEquals(Get<GPC.Model.Elements.Element>("AnalysedGpc"),analysed.Beam) && ReferenceEquals(Get<GPC.Model.Models.Model>("AnalysedGpcModel"),analysed.Model),
            "Composite assignment must retain the link to the analysed GPC beam.");
        // The definitions built in GPC from the numbers of the composite properties (BridgeGrillageExample.Composite).
        GpcCompositeDefinitions.RequireSame(GpcCompositeDefinitions.Create(GpcCompositeDefinitions.Grillage(p)),start,"composite beam start");
        GpcCompositeDefinitions.RequireSame(GpcCompositeDefinitions.Create(GpcCompositeDefinitions.Grillage(p with{SteelHeight=endHeight??p.SteelHeight})),end,"composite beam end");
        return 6;
    }
#endif

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
