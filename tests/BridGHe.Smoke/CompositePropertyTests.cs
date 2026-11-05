using System;
using System.Linq;
using System.Reflection;
using BridGHe.Common.Helpers;
using BridGHe.Common.Models;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Parameters;
using Grasshopper.Kernel.Types;

internal static class CompositePropertyTests
{
    internal static void Run()
    {
        void Require(bool ok,string message){if(!ok)throw new Exception(message);}
        void Near(double a,double b)=>Require(Math.Abs(a-b)<=1e-12*Math.Max(1,Math.Abs(b)), $"{a} != {b}");
        var assembly=Assembly.LoadFrom(BuildPaths.GrasshopperAssembly);
        var serverField=typeof(Grasshopper.Instances).GetField("m_comServer",BindingFlags.NonPublic|BindingFlags.Static);
        if(serverField.GetValue(null)==null)
        {
            var server=new GH_ComponentServer();serverField.SetValue(null,server);
            var parse=typeof(GH_ComponentServer).GetMethod("ParseGHA",BindingFlags.NonPublic|BindingFlags.Public|BindingFlags.Instance,null,new[]{typeof(Assembly),typeof(string)},null);
            foreach(var a in new[]{typeof(GH_Document).Assembly,assembly})parse.Invoke(server,new object[]{a,a.Location});
        }
        var property=BridgeGrillageExample.Composite(new());property.ConcreteSection=new(250,3000,2000,40,1500,55);property.ConsiderRebar=true;
        Near(property.NShrinkage,property.NInfinite);Near(ModelArchive.Clone(property).NShrinkage,property.NInfinite);
        property.NShrinkage=12;Near(ModelArchive.Clone(property).NShrinkage,12);Near(new CompositeBeamPropertyModel(property).NShrinkage,12);
        property.SteelMaterial.ThermalExpansion=1.2e-5;
        var shrink=BridGHe.Common.Checks.CompositeShrinkageActions.Calculate(property,-.0002);
        var zero=BridGHe.Common.Checks.CompositeShrinkageActions.Calculate(property,0);
        Require(zero.FreeStrain==0 && zero.Curvature==0,"Zero eigenstrain must generate zero FEM actions.");
        var twice=BridGHe.Common.Checks.CompositeShrinkageActions.Calculate(property,-.0004);
        Near(twice.PreStrain,2*shrink.PreStrain);Near(twice.ThermalGradient,2*shrink.ThermalGradient);
        var shProps=property.CreateHomogenizedProperty(12,"test");
        Near(property.SteelMaterial.Modulus*shProps.SectionArea*shrink.FreeStrain,shrink.EigenForce);
        Near(property.SteelMaterial.Modulus*shProps.I11*shrink.Curvature,shrink.EigenMoment);
        Require(shrink.FreeStrain<0 && shrink.PreStrain>0,"Straus prestrain contraction sign reversed.");
#if GPC_PILOT
        GpcShrinkage(property,Require,Near);
#endif
        using var doc=new GH_Document();
        var c=(GH_Component)Activator.CreateInstance(assembly.GetType("BridGHe.Grasshopper.Components.BridgeChecks.BeamPropertyCompositeComponent",true));c.CreateAttributes();doc.AddObject(c,false);
        IGH_Goo Wrap(string name,object value){var t=assembly.GetType("BridGHe.Grasshopper.DataTypes."+name,true);var g=(IGH_Goo)Activator.CreateInstance(t);t.GetProperty("Value").SetValue(g,value);return g;}
        void Generic(int i,IGH_Goo g){var p=(Param_GenericObject)c.Params.Input[i];p.PersistentData.Clear();p.PersistentData.Append(g);p.ExpireSolution(false);}
        void Number(int i,double x){var p=(Param_Number)c.Params.Input[i];p.PersistentData.Clear();p.SetPersistentData(x);p.ExpireSolution(false);}
        Generic(1,new GH_ObjectWrapper(property.ConcreteSection));Generic(2,Wrap("Fem.GH_BeamSection",property.SteelSection));Generic(3,Wrap("Fem.GH_Material",property.ConcreteMaterial));Generic(4,Wrap("Fem.GH_Material",property.SteelMaterial));Generic(5,Wrap("Fem.GH_Material",property.RebarMaterial));Number(6,7);Number(7,18);
        void Solve(){c.ExpireSolution(false);GH_Document.EnableSolutions=true;doc.Enabled=true;doc.NewSolution(false,GH_SolutionMode.Silent);Require(c.RuntimeMessages(GH_RuntimeMessageLevel.Error).Count==0,string.Join(";",c.RuntimeMessages(GH_RuntimeMessageLevel.Error)));}
        BeamPropertyModel Output(int i)=>(BeamPropertyModel)c.Params.Output[i].VolatileData.AllData(true).Single().ScriptVariable();
        int ShrinkagePort()=>c.Params.Input.FindIndex(p=>p.Name=="n ritiro");
        // F6 (0.4.0): the name is required (the composite section is a GPC definition with a name).
        c.ExpireSolution(false);GH_Document.EnableSolutions=true;doc.Enabled=true;doc.NewSolution(false,GH_SolutionMode.Silent);
        Require(c.RuntimeMessages(GH_RuntimeMessageLevel.Error).Any(m=>m.StartsWith("Name required",StringComparison.Ordinal)) && !c.Params.Output[0].VolatileData.AllData(true).Any(),"An empty composite section name must be an error: "+string.Join(";",c.RuntimeMessages(GH_RuntimeMessageLevel.Error)));
        {var p=(Param_String)c.Params.Input[0];p.PersistentData.Clear();p.SetPersistentData("Composite test");p.ExpireSolution(false);}
        Solve();Require(c.Params.Input.Count==11 && c.Params.Output.Count==5 && c.Params.Input[ShrinkagePort()].Optional,"Shrinkage port count or optionality changed.");
        Near(Output(3).SectionArea,Output(4).SectionArea);Near(Output(3).I11,Output(4).I11);
#if GPC_PILOT
        GpcComponent(c,Output,null,Require,Near);
#endif
        var old0=Output(2);var oldInf=Output(3);Number(ShrinkagePort(),12);Solve();
        Near(Output(2).I11,old0.I11);Near(Output(3).I11,oldInf.I11);
        var expected=property.GetHomogeneizedMechanicalProperties(12);var actual=Output(4);
        Near(actual.SectionArea,expected.areaH/12);Near(actual.I11,expected.J11H/12);Near(actual.I22,expected.J22H/12);Near(actual.J,property.GetOnlySteelMechanicalProperties().Jt);
        Require(actual.Material.Modulus==property.SteelMaterial.Modulus && actual.Section.NativeSection==null && actual.Section.SectionType==SectionModel.SectionTypes.Generic && actual.IsValid,"Shrinkage property must be a valid generic steel-referenced Straus property.");
        Near(((CompositeBeamPropertyModel)c.Params.Output[0].VolatileData.AllData(true).Single().ScriptVariable()).NShrinkage,12);
#if GPC_PILOT
        var definition=GpcComponent(c,Output,12,Require,Near);
#endif
        // Saved definitions can retain the original end position or the reordered local layout.
        foreach(int port in new[]{10,8})
        {
            var shrinkageParam=c.Params.Input[ShrinkagePort()];c.Params.Input.Remove(shrinkageParam);c.Params.Input.Insert(port,shrinkageParam);c.Params.OnParametersChanged();
            foreach(var p in c.Params.Input)p.ExpireSolution(false);
            Solve();Near(((CompositeBeamPropertyModel)c.Params.Output[0].VolatileData.AllData(true).Single().ScriptVariable()).NShrinkage,12);
            Near(Output(4).I11,actual.I11);
#if GPC_PILOT
            Near(DefinitionOf(c).NShrinkage,12);
#endif
            var rebarInput=(Param_Boolean)c.Params.Input.Single(p=>p.Name=="Consider Rebars");
            rebarInput.PersistentData.Clear();rebarInput.SetPersistentData(false);
            var concreteInput=(Param_Boolean)c.Params.Input.Single(p=>p.Name=="Consider Concrete");
            bool wasOptional=concreteInput.Optional;concreteInput.Optional=true;concreteInput.PersistentData.Clear();
            var archive=new GH_IO.Serialization.GH_LooseChunk("Component");Require(c.Write(archive),"Cannot serialize composite component.");
            using(var reopenedDoc=new GH_Document())
            {
                var reopened=(GH_Component)Activator.CreateInstance(c.GetType());reopened.CreateAttributes();
                Require(reopened.Read(archive),"Cannot reopen composite component.");reopenedDoc.AddObject(reopened,false);
                Require(reopened.Params.Input.Select(p=>p.GetType()).SequenceEqual(c.Params.Input.Select(p=>p.GetType())),"Saved parameter order restored with wrong input types.");
                Require(reopened.Params.Input.Select(p=>p.InstanceGuid).SequenceEqual(c.Params.Input.Select(p=>p.InstanceGuid)),"Reopening changed input GUIDs and would break wires.");
                // Geometry/materials normally arrive from upstream components. Supply those
                // again; this roundtrip tests the three saved scalar ports, not GH_ObjectWrapper.
                for(int i=1;i<=5;i++)
                {
                    var input=(Param_GenericObject)reopened.Params.Input[i];input.PersistentData.Clear();
                    foreach(var goo in ((Param_GenericObject)c.Params.Input[i]).PersistentData.AllData(true))input.PersistentData.Append(goo.Duplicate());
                }
                reopenedDoc.Enabled=true;reopenedDoc.NewSolution(false,GH_SolutionMode.Silent);
                Require(reopened.RuntimeMessages(GH_RuntimeMessageLevel.Error).Count==0,string.Join(";",reopened.RuntimeMessages(GH_RuntimeMessageLevel.Error)));
                var savedProperty=(CompositeBeamPropertyModel)reopened.Params.Output[0].VolatileData.AllData(true).Single().ScriptVariable();
                Near(savedProperty.NShrinkage,12);Require(savedProperty.ConsiderConcrete&&!savedProperty.ConsiderRebar,"Reopening swapped the concrete/rebar options.");
#if GPC_PILOT
                var savedDefinition=DefinitionOf(reopened);
                Near(savedDefinition.NShrinkage,12);Require(savedDefinition.ConsiderConcrete&&!savedDefinition.ConsiderRebar,"GPC: Reopening swapped the concrete/rebar options of the definition.");
#endif
            }
            rebarInput.PersistentData.Clear();rebarInput.SetPersistentData(true);rebarInput.ExpireSolution(false);
            concreteInput.Optional=wasOptional;concreteInput.SetPersistentData(true);concreteInput.ExpireSolution(false);
        }
        var beam=new BeamModel("Preview",Rhino.Geometry.Point3d.Origin,new Rhino.Geometry.Point3d(10000,0,0),actual);
        Require(beam.Shapes.Length==2 && beam.Shapes.All(b=>b.IsValid && b.IsSolid),"Override composite preview must contain solid steel and slab.");
        Near(beam.Shapes[0].GetBoundingBox(true).Max.Z,beam.Shapes[1].GetBoundingBox(true).Min.Z);
        var archived=ModelArchive.Clone(beam);Require(archived.Shapes.Length==2 && archived.BeamProperty.Section.CompositePreview.Slab.BottomRebarArea==1500,"Archived generic section lost its physical preview.");
        Near(archived.BeamProperty.I11,actual.I11);
        var asym=ModelArchive.Clone(property);asym.SteelSection.B1=900;asym.SteelSection.B2=400;
        var composite=new CompositeBeamModel(Rhino.Geometry.Point3d.Origin,new Rhino.Geometry.Point3d(10000,0,0),asym);
        Near(composite.Shapes[0].GetBoundingBox(true).Max.Z,composite.Shapes[1].GetBoundingBox(true).Min.Z);
#if GPC_PILOT
        GpcPreview(definition,property,asym,Require,Near);
#endif
        Number(ShrinkagePort(),0);c.ExpireSolution(false);doc.NewSolution(false,GH_SolutionMode.Silent);Require(c.RuntimeMessages(GH_RuntimeMessageLevel.Error).Count>0 && !c.Params.Output[4].VolatileData.AllData(true).Any(g=>g!=null),"Invalid shrinkage ratio produced a section.");
#if GPC_PILOT
        Require(!c.Params.Output[0].VolatileData.AllData(true).Any(g=>g!=null),"GPC: an invalid shrinkage ratio produced a composite definition.");
        GpcInvalidRatio(Require);
        Console.WriteLine($"COMPOSITE PROPERTY GPC PASS: {gpcChecks} checks on definitions built in GPC (shrinkage actions, homogenised values, component definitions, archive, preview, invalid ratios), equal bit for bit to the FEM path.");
#endif
    }
#if GPC_PILOT
    // P2U (F6, chain E): the same checks on the composite section as the GPC contract of the Checker (BridgeCompositeSectionDefinition, the
    // datum of GH_CompositeBeamProperty), built directly in GPC (GpcCompositeDefinitions), on the GPC consumers: Composite Shrinkage Actions,
    // GpcCompositeSections (homogenised analysis values: Composite Section Geometric Property and Bridge State Override), the definition the
    // component gives (GH_CompositeBeamProperty.Definition), the preview of a composite beam from its definition. Parity (removed with the
    // FeMM models): the definitions equal bit for bit to those of the FEM properties, the actions and the values to those of the FEM path.
    // Still FEM until chain E: the component's inputs and its beam property outputs (GH_BeamProperty), the preview of a FEM beam.
    private static int gpcChecks;

    private static GpcCompositeDefinitions.Numbers TestNumbers(string name,double nShort,double nLong,double nShrinkage) =>
        GpcCompositeDefinitions.Grillage(new()) with{Name=name,SlabWidth=3000,SlabHeight=250,TopArea=2000,TopCover=40,BottomArea=1500,BottomCover=55,
            ConsiderRebar=true,NShortTerm=nShort,NLongTerm=nLong,NShrinkage=nShrinkage,SteelThermalExpansion=1.2e-5};

    private static GPC.Checkers.CompositeBridge.BridgeCompositeSectionDefinition DefinitionOf(GH_Component component)
    {
        var goo=component.Params.Output[0].VolatileData.AllData(true).Single();
        return (GPC.Checkers.CompositeBridge.BridgeCompositeSectionDefinition)goo.GetType().GetProperty("Definition").GetValue(goo);
    }

    private static bool SameBits(params (double A,double B)[] pairs)=>pairs.All(p=>GpcCompositeDefinitions.Bits(p.A)==GpcCompositeDefinitions.Bits(p.B));

    private static bool SameActions(BridGHe.Common.Checks.CompositeShrinkageActions a,BridGHe.Common.Checks.CompositeShrinkageActions b)=>
        SameBits((a.FreeStrain,b.FreeStrain),(a.Curvature,b.Curvature),(a.PreStrain,b.PreStrain),(a.ThermalGradient,b.ThermalGradient),(a.EigenForce,b.EigenForce),(a.EigenMoment,b.EigenMoment),(a.Centroid,b.Centroid));

    /// <summary>Composite Shrinkage Actions on a definition built in GPC with the numbers of the FEM fixture (two rows, n,sh = 12, α = 1.2e-5).</summary>
    private static void GpcShrinkage(CompositeBeamPropertyModel property,Action<bool,string> require,Action<double,double> near)
    {
        var definition=GpcCompositeDefinitions.Create(TestNumbers(property.Name,property.NInstant,property.NInfinite,12));
        near(definition.NShrinkage,12);var copy=definition.Copy();near(copy.NShrinkage,12);
        require(copy.Fingerprint()==definition.Fingerprint(),"GPC: the archive of the definition changed it.");
        var shrink=BridGHe.Common.Checks.CompositeShrinkageActions.Calculate(definition,-.0002);
        var zero=BridGHe.Common.Checks.CompositeShrinkageActions.Calculate(definition,0);
        require(zero.FreeStrain==0 && zero.Curvature==0,"GPC: Zero eigenstrain must generate zero FEM actions.");
        var twice=BridGHe.Common.Checks.CompositeShrinkageActions.Calculate(definition,-.0004);
        near(twice.PreStrain,2*shrink.PreStrain);near(twice.ThermalGradient,2*shrink.ThermalGradient);
        var sh=BridGHe.Common.Gpc.GpcCompositeSections.Homogenised(definition,12,true);double e=definition.AnalysisSteel.ElasticModulusTension;
        near(e*sh.Area*shrink.FreeStrain,shrink.EigenForce);near(e*sh.I11*shrink.Curvature,shrink.EigenMoment);
        require(shrink.FreeStrain<0 && shrink.PreStrain>0,"GPC: Straus prestrain contraction sign reversed.");
        // Parity (removed with the FeMM models).
        GpcCompositeDefinitions.RequireSame(definition,BridGHe.Common.Gpc.GpcCompositeSections.Definition(property,property.Name),"shrinkage fixture");
        require(SameActions(shrink,BridGHe.Common.Checks.CompositeShrinkageActions.Calculate(property,-.0002)) && SameActions(zero,BridGHe.Common.Checks.CompositeShrinkageActions.Calculate(property,0))
            && SameActions(twice,BridGHe.Common.Checks.CompositeShrinkageActions.Calculate(property,-.0004)),"GPC: shrinkage actions differ from the FEM ones.");
        gpcChecks+=13;
    }

    /// <summary>The definition the component gives (n0 = 7, n∞ = 18, n,sh = <paramref name="nShrinkage"/> or, omitted, n∞): equal to the
    /// definition built in GPC with the same numbers; the homogenised analysis values of the GPC definition give the component's beam property
    /// outputs (n0, n∞, n,sh) bit for bit; n,sh changes only the shrinkage values.</summary>
    private static GPC.Checkers.CompositeBridge.BridgeCompositeSectionDefinition GpcComponent(GH_Component component,Func<int,BeamPropertyModel> output,double? nShrinkage,
        Action<bool,string> require,Action<double,double> near)
    {
        var given=DefinitionOf(component);
        var definition=GpcCompositeDefinitions.Create(TestNumbers("Composite test",7,18,nShrinkage??18));
        GpcCompositeDefinitions.RequireSame(definition,given,"component, n,sh "+(nShrinkage?.ToString()??"omitted"));
        if(nShrinkage==null)near(given.NShrinkage,given.NLongTerm);else near(given.NShrinkage,nShrinkage.Value);
        var values=new[]{definition.NShortTerm,definition.NLongTerm,definition.NShrinkage}.Select(n=>BridGHe.Common.Gpc.GpcCompositeSections.Homogenised(definition,n,definition.ConsiderConcrete)).ToArray();
        if(nShrinkage==null){near(values[1].Area,values[2].Area);near(values[1].I11,values[2].I11);}
        else
        {
            var omitted=GpcCompositeDefinitions.Create(TestNumbers("Composite test",7,18,18));
            near(values[0].I11,BridGHe.Common.Gpc.GpcCompositeSections.Homogenised(omitted,7,true).I11);near(values[1].I11,BridGHe.Common.Gpc.GpcCompositeSections.Homogenised(omitted,18,true).I11);
            var p=BridGHe.Common.Gpc.GpcCompositeSections.AnalysisProperties(definition,12,true,true,true);
            near(values[2].Area,p.areaH/12);near(values[2].I11,p.J11H/12);near(values[2].I22,p.J22H/12);near(values[2].J,BridGHe.Common.Gpc.GpcCompositeSections.SteelTorsionConstant(definition));
            require(definition.AnalysisSteel.ElasticModulusTension==BridgeGrillageExample.Es,"GPC: the shrinkage values must be steel-referenced.");
        }
        // Parity (removed with the FeMM models): the beam property outputs of the component (FEM until chain E).
        for(int k=0;k<3;k++)
        {
            var fem=output(2+k);var v=values[k];
            require(SameBits((fem.SectionArea,v.Area),(fem.I11,v.I11),(fem.I22,v.I22),(fem.J,v.J),(fem.Section.ShearA1,v.ShearArea1),(fem.Section.ShearA2,v.ShearArea2),
                (fem.Section.Centroid.X,v.CentroidX),(fem.Section.Centroid.Y,v.CentroidY)),$"GPC: homogenised values of output {2+k} differ from the component's FEM property.");
        }
        gpcChecks+=nShrinkage==null?6:12;
        return definition;
    }

    /// <summary>The outlines of a composite section from its definition (the preview of a composite beam on its definitions): the slab on
    /// the top flange, also for an asymmetric H; equal bit for bit to those of the FEM properties.</summary>
    private static void GpcPreview(GPC.Checkers.CompositeBridge.BridgeCompositeSectionDefinition definition,CompositeBeamPropertyModel property,CompositeBeamPropertyModel asym,
        Action<bool,string> require,Action<double,double> near)
    {
        string Bits(CompositeSectionPreviewGeometry g)=>string.Join(";",g.Outlines().SelectMany(c=>c.ToPolyline()).Select(q=>GpcCompositeDefinitions.Bits(q.X).ToString("X16")+","+GpcCompositeDefinitions.Bits(q.Y).ToString("X16")));
        var asymmetric=GpcCompositeDefinitions.Create(TestNumbers(asym.Name,asym.NInstant,asym.NInfinite,asym.NShrinkage) with{BottomWidth=900,TopWidth=400});
        foreach(var (gpc,fem) in new[]{(definition,property),(asymmetric,asym)})
        {
            var outlines=new CompositeSectionPreviewGeometry(gpc).Outlines().ToArray();
            require(outlines.Length==2 && outlines.All(o=>o.IsClosed),"GPC: the composite preview must contain steel and slab.");
            near(outlines[0].GetBoundingBox(true).Max.Y,outlines[1].GetBoundingBox(true).Min.Y);
            // Parity (removed with the FeMM models).
            require(Bits(new CompositeSectionPreviewGeometry(gpc))==Bits(new CompositeSectionPreviewGeometry(fem)),"GPC: the preview of the definition differs from that of the FEM property.");
        }
        GpcCompositeDefinitions.RequireSame(asymmetric,BridGHe.Common.Gpc.GpcCompositeSections.Definition(asym,asym.Name),"asymmetric H");
        gpcChecks+=7;
    }

    /// <summary>A modular ratio that is not positive gives no definition and no homogenised values.</summary>
    private static void GpcInvalidRatio(Action<bool,string> require)
    {
        foreach(double n in new[]{0,-1,double.NaN})
        {
            bool rejected=false;try{GpcCompositeDefinitions.Create(TestNumbers("Composite test",7,18,n));}catch(ArgumentException){rejected=true;}
            require(rejected,"GPC: an invalid shrinkage ratio produced a definition.");
            rejected=false;try{BridGHe.Common.Gpc.GpcCompositeSections.Homogenised(GpcCompositeDefinitions.Create(TestNumbers("Composite test",7,18,12)),n,true);}catch(ArgumentOutOfRangeException){rejected=true;}
            require(rejected,"GPC: an invalid modular ratio produced homogenised values.");
        }
        gpcChecks+=6;
    }
#endif
}
