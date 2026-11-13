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
        Gpc();
    }

    private static Assembly Plugin()
    {
        var assembly=Assembly.LoadFrom(BuildPaths.GrasshopperAssembly);
        var serverField=typeof(Grasshopper.Instances).GetField("m_comServer",BindingFlags.NonPublic|BindingFlags.Static);
        if(serverField.GetValue(null)==null)
        {
            var server=new GH_ComponentServer();serverField.SetValue(null,server);
            var parse=typeof(GH_ComponentServer).GetMethod("ParseGHA",BindingFlags.NonPublic|BindingFlags.Public|BindingFlags.Instance,null,new[]{typeof(Assembly),typeof(string)},null);
            foreach(var a in new[]{typeof(GH_Document).Assembly,assembly})parse.Invoke(server,new object[]{a,a.Location});
        }
        return assembly;
    }

    // R6.6 R2a (P2U, F6, chain E): the checks on the composite section as the GPC contract of the Checker (BridgeCompositeSectionDefinition,
    // the datum of GH_CompositeBeamProperty), built directly in GPC (GpcCompositeDefinitions), on the GPC consumers: Composite Shrinkage
    // Actions, GpcCompositeSections (homogenised analysis values), and Composite Beam Property on inputs given by their GPC data alone (no FEM
    // value: the steel section recipe, the materials by kind; the slab is its numbers, SectionConcreteSlabModel, as the slab component gives
    // it): its definition and its four beam properties by their GPC data alone. The parity with the FEM path (definitions, actions, values and
    // preview outlines equal bit for bit to those of the FEM properties) is retired with the FEM model: every check below has its own
    // expected value (analytical relations, the definitions built from the numbers, the homogenised values of the definition).
    private static int gpcChecks;

    /// <summary>The composite section of the fixture: the grillage example section with the slab 3000 × 250, the rows 2000 at cover 40 and
    /// 1500 at cover 55, Consider Rebar, α of the steel 1.2E-5 and the ratios given.</summary>
    private static GpcCompositeDefinitions.Numbers TestNumbers(string name,double nShort,double nLong,double nShrinkage) =>
        GpcCompositeDefinitions.Grillage(new()) with{Name=name,SlabWidth=3000,SlabHeight=250,TopArea=2000,TopCover=40,BottomArea=1500,BottomCover=55,
            ConsiderRebar=true,NShortTerm=nShort,NLongTerm=nLong,NShrinkage=nShrinkage,SteelThermalExpansion=1.2e-5};

    private static GPC.Checkers.CompositeBridge.BridgeCompositeSectionDefinition DefinitionOf(GH_Component component)
    {
        var goo=component.Params.Output[0].VolatileData.AllData(true).Single();
        if(R2aDesignFixtures.HasFem(goo)) throw new Exception("GPC: the composite section of GPC-only inputs must have no FEM value.");
        return (GPC.Checkers.CompositeBridge.BridgeCompositeSectionDefinition)goo.GetType().GetProperty("Definition").GetValue(goo);
    }

    private static bool SameBits(params (double A,double B)[] pairs)=>pairs.All(p=>GpcCompositeDefinitions.Bits(p.A)==GpcCompositeDefinitions.Bits(p.B));

    private static void Gpc()
    {
        void Require(bool ok,string message){if(!ok)throw new Exception(message);}
        void Near(double a,double b)=>Require(Math.Abs(a-b)<=1e-12*Math.Max(1,Math.Abs(b)), $"{a} != {b}");
        var assembly=Plugin();
        gpcChecks=0;
        var grillage=GpcCompositeDefinitions.Grillage(new());
        GpcShrinkage(grillage.NShortTerm,grillage.NLongTerm,Require,Near);
        using var doc=new GH_Document();
        var c=(GH_Component)Activator.CreateInstance(assembly.GetType("BridGHe.Grasshopper.Components.BridgeChecks.BeamPropertyCompositeComponent",true));c.CreateAttributes();doc.AddObject(c,false);
        void Generic(int i,IGH_Goo g){var p=(Param_GenericObject)c.Params.Input[i];p.PersistentData.Clear();p.PersistentData.Append(g);p.ExpireSolution(false);}
        void Number(int i,double x){var p=(Param_Number)c.Params.Input[i];p.PersistentData.Clear();p.SetPersistentData(x);p.ExpireSolution(false);}
        // The inputs by their GPC data alone: the steel I recipe of the example (H 1600 × 600 × 40, web 16), the materials of the example without a kind (Custom): C30 (E = Ec, ν 0.2),
        // S355 (E 210000, ν 0.3, fy 355, fu 510, α 1.2E-5), B450 bars (E 210000, fy 450, fu 540); the slab of the fixture.
        var p0=new BridgeGrillageExample.Parameters();
        var steelSection=BridGHe.Common.Gpc.Builders.GpcSectionBuilders.Standard(BridGHe.Common.Gpc.BridgeSectionKind.I,p0.SteelHeight,b1:p0.FlangeWidth,b2:p0.FlangeWidth,t1:p0.FlangeThickness,t2:p0.FlangeThickness,t3:p0.WebThickness);
        GPC.Model.Materials.Material M(BridGHe.Common.Gpc.Legacy.LegacyMaterialRecipe r)=>BridGHe.Common.Gpc.Builders.GpcMaterialBuilders.Material(r);
        var concrete=M(new(){Kind="Undefined",Name="C30",Modulus=BridgeGrillageExample.Ec,PoissonRatio=BridgeGrillageExample.NuConcrete,SpecificCompressiveStrength=30});
        var steel=M(new(){Kind="Undefined",Name="S355",Modulus=BridgeGrillageExample.Es,PoissonRatio=BridgeGrillageExample.NuSteel,MinimumYieldStress=355,MinimumTensileStress=510,ThermalExpansion=1.2e-5});
        var rebar=M(new(){Kind="Undefined",Name="B450",Modulus=BridgeGrillageExample.Es,MinimumYieldStress=450,MinimumTensileStress=540});
        Generic(1,new GH_ObjectWrapper(new BridGHe.Common.Checks.ConcreteSlabSection(250,3000,2000,40,1500,55))); /* P2CA: the slab by its GPC datum (Concrete Slab Section, P10), the same numbers */ Generic(2,R2aDesignFixtures.Goo("GH_BeamSection",steelSection));
        Generic(3,R2aDesignFixtures.MaterialGoo(concrete,"Undefined"));Generic(4,R2aDesignFixtures.MaterialGoo(steel,"Undefined"));Generic(5,R2aDesignFixtures.MaterialGoo(rebar,"Undefined"));Number(6,7);Number(7,18);
        void Solve(){c.ExpireSolution(false);GH_Document.EnableSolutions=true;doc.Enabled=true;doc.NewSolution(false,GH_SolutionMode.Silent);Require(c.RuntimeMessages(GH_RuntimeMessageLevel.Error).Count==0,string.Join(";",c.RuntimeMessages(GH_RuntimeMessageLevel.Error)));}
        BridGHe.Common.Gpc.Legacy.LegacyBeamProperty Output(int i)
        {
            var goo=c.Params.Output[i].VolatileData.AllData(true).Single();
            Require(!R2aDesignFixtures.HasFem(goo),"GPC: the beam properties of GPC-only inputs must have no FEM value.");
            return R2aDesignFixtures.Datum<BridGHe.Common.Gpc.Legacy.LegacyBeamProperty>(goo);
        }
        int ShrinkagePort()=>c.Params.Input.FindIndex(p=>p.Name=="n ritiro");
        // F6 (0.4.0): the name is required (the composite section is a GPC definition with a name).
        c.ExpireSolution(false);GH_Document.EnableSolutions=true;doc.Enabled=true;doc.NewSolution(false,GH_SolutionMode.Silent);
        Require(c.RuntimeMessages(GH_RuntimeMessageLevel.Error).Any(m=>m.StartsWith("Name required",StringComparison.Ordinal)) && !c.Params.Output[0].VolatileData.AllData(true).Any(),"An empty composite section name must be an error: "+string.Join(";",c.RuntimeMessages(GH_RuntimeMessageLevel.Error)));
        {var p=(Param_String)c.Params.Input[0];p.PersistentData.Clear();p.SetPersistentData("Composite test");p.ExpireSolution(false);}
        Solve();Require(c.Params.Input.Count==11 && c.Params.Output.Count==5 && c.Params.Input[ShrinkagePort()].Optional,"Shrinkage port count or optionality changed.");
        Near(R2aPropertyFixtures.Area(Output(3)),R2aPropertyFixtures.Area(Output(4)));Near(Output(3).Data.Section.I11,Output(4).Data.Section.I11);
        GpcComponent(c,Output,null,Require,Near);
        var old0=Output(2);var oldInf=Output(3);Number(ShrinkagePort(),12);Solve();
        Near(Output(2).Data.Section.I11,old0.Data.Section.I11);Near(Output(3).Data.Section.I11,oldInf.Data.Section.I11);
        var actual=Output(4);
        var definition=GpcComponent(c,Output,12,Require,Near);
        Require(R2aPropertyFixtures.Material(actual).ElasticModulusTension==BridgeGrillageExample.Es && actual.Data.Native==null && actual.Data.Section.Kind==BridGHe.Common.Gpc.BridgeSectionKind.Generic
            && actual.Data.Formulation==GPC.Model.Structure.Assignments.BeamFormulation.StraightTwoNode && actual.Property!=null,"Shrinkage property must be a valid generic steel-referenced Straus property.");
        Near(DefinitionOf(c).NShrinkage,12);
        // Saved definitions can retain the original end position or the reordered local layout.
        foreach(int port in new[]{10,8})
        {
            var shrinkageParam=c.Params.Input[ShrinkagePort()];c.Params.Input.Remove(shrinkageParam);c.Params.Input.Insert(port,shrinkageParam);c.Params.OnParametersChanged();
            foreach(var p in c.Params.Input)p.ExpireSolution(false);
            Solve();Near(DefinitionOf(c).NShrinkage,12);
            Near(Output(4).Data.Section.I11,actual.Data.Section.I11);
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
                var savedDefinition=DefinitionOf(reopened);
                Near(savedDefinition.NShrinkage,12);Require(savedDefinition.ConsiderConcrete&&!savedDefinition.ConsiderRebar,"Reopening swapped the concrete/rebar options.");
                gpcChecks+=2;
            }
            rebarInput.PersistentData.Clear();rebarInput.SetPersistentData(true);rebarInput.ExpireSolution(false);
            concreteInput.Optional=wasOptional;concreteInput.SetPersistentData(true);concreteInput.ExpireSolution(false);
        }
        // The shrinkage property keeps its homogenised values and its composite preview through its GPC archive, the definition its rows.
        var written=BridGHe.Common.Gpc.GpcDraftArchive.Write(actual);var archived=BridGHe.Common.Gpc.GpcDraftArchive.ReadBeamProperty(written.Data,written.Records);
        Require(archived.Data.Section.CompositePreview && SameBits((archived.Data.Section.I11,actual.Data.Section.I11),(archived.Data.Section.Area,actual.Data.Section.Area)),"Archived generic section lost its physical preview.");
        Require(GpcCompositeDefinitions.Read(BridGHe.Common.Gpc.GpcDraftArchive.ReadComposite(BridGHe.Common.Gpc.GpcDraftArchive.Write(definition))).Numbers.BottomArea==1500,"Archived composite section lost its bottom rebar area.");
        gpcChecks+=2;
        GpcPreview(definition,Require,Near);
        Number(ShrinkagePort(),0);c.ExpireSolution(false);doc.NewSolution(false,GH_SolutionMode.Silent);Require(c.RuntimeMessages(GH_RuntimeMessageLevel.Error).Count>0 && !c.Params.Output[4].VolatileData.AllData(true).Any(g=>g!=null),"Invalid shrinkage ratio produced a section.");
        Require(!c.Params.Output[0].VolatileData.AllData(true).Any(g=>g!=null),"GPC: an invalid shrinkage ratio produced a composite definition.");
        GpcInvalidRatio(Require);
        Console.WriteLine($"COMPOSITE PROPERTY GPC PASS: {gpcChecks} checks on definitions built in GPC (shrinkage actions, homogenised values, component definitions and properties by their GPC data alone, archive, preview, invalid ratios).");
    }

    /// <summary>Composite Shrinkage Actions on a definition built in GPC with the numbers of the fixture (two rows, n,sh = 12, α = 1.2e-5): the
    /// ratio and the archive of the definition, zero actions for a zero strain, linearity, the eigen force and moment from the homogenised
    /// shrinkage values (E A ε, E I κ) and the signs.</summary>
    private static void GpcShrinkage(double n0,double nInf,Action<bool,string> require,Action<double,double> near)
    {
        var definition=GpcCompositeDefinitions.Create(TestNumbers("Composite",n0,nInf,12));
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
        require(e==BridgeGrillageExample.Es,"GPC: the shrinkage actions must be steel-referenced.");
        gpcChecks+=10;
    }

    /// <summary>The definition the component gives (n0 = 7, n∞ = 18, n,sh = <paramref name="nShrinkage"/> or, omitted, n∞): equal to the
    /// definition built in GPC with the same numbers; the component's beam property outputs (n0, n∞, n,sh) are the homogenised analysis values
    /// of the definition bit for bit (their section recipes, with the composite preview); n,sh changes only the shrinkage values.</summary>
    private static GPC.Checkers.CompositeBridge.BridgeCompositeSectionDefinition GpcComponent(GH_Component component,Func<int,BridGHe.Common.Gpc.Legacy.LegacyBeamProperty> output,double? nShrinkage,
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
        // The beam property outputs of the component, by their GPC data alone: the homogenised values of the definition.
        string[] suffixes={"_N0_istant","_N_infinite","_N_shrinkage"};
        for(int k=0;k<3;k++)
        {
            var property=output(2+k);var s=property.Data.Section;var v=values[k];
            require(property.Property.Name=="Composite test"+suffixes[k] && s.CompositePreview && SameBits((s.Area,v.Area),(s.I11,v.I11),(s.I22,v.I22),(s.J,v.J),(s.ShearA1,v.ShearArea1),(s.ShearA2,v.ShearArea2),
                (s.CentroidX,v.CentroidX),(s.CentroidY,v.CentroidY)),$"GPC: homogenised values of output {2+k} differ from the values of the definition.");
        }
        gpcChecks+=nShrinkage==null?7:12;
        return definition;
    }

    /// <summary>The outlines of a composite section from its definition (the preview of a composite beam on its definitions): steel and slab,
    /// the slab on the top flange, also for an asymmetric H (bottom flange 900, top flange 400).</summary>
    private static void GpcPreview(GPC.Checkers.CompositeBridge.BridgeCompositeSectionDefinition definition,Action<bool,string> require,Action<double,double> near)
    {
        var asymmetric=GpcCompositeDefinitions.Create(TestNumbers("Composite asymmetric",7,18,12) with{BottomWidth=900,TopWidth=400});
        foreach(var gpc in new[]{definition,asymmetric})
        {
            var outlines=new CompositeSectionPreviewGeometry(gpc).Outlines().ToArray();
            require(outlines.Length==2 && outlines.All(o=>o.IsClosed),"GPC: the composite preview must contain steel and slab.");
            near(outlines[0].GetBoundingBox(true).Max.Y,outlines[1].GetBoundingBox(true).Min.Y);
        }
        gpcChecks+=4;
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

}
