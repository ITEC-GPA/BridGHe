using System;
using System.IO;
using System.Linq;
using System.Reflection;
using BridGHe.Common.Checks;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Parameters;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;

internal static class SlabReinforcementTests
{
    internal static void Run()
    {
        void Require(bool ok, string why) { if (!ok) throw new Exception(why); }
        void Near(double actual, double expected) => Require(Math.Abs(actual-expected)<1e-8*Math.Max(1,Math.Abs(expected)), $"{actual} != {expected}");
        // P2CB (13/11, rule U3): the slab is the GPC datum of Beam Section Concrete Slab (ConcreteSlabSection, the numbers and the validity rule of the
        // former value): the legacy single-row slab read by value from its archive, the copy of a two-row slab, the invalid rows. The checks of the FEM
        // values (the FEM slab archive, the rows and the homogenised values of a FEM composite property, the parity of the definitions with the FEM
        // properties) are retired by name (P2rRetiredP2cb); GpcRows checks the same rows and values on the definitions built in GPC.
        var single=BridGHe.Common.Gpc.Legacy.LegacyArchive.Read(File.ReadAllBytes("tests/BridGHe.Smoke/Fixtures/slab-single-row.bgha"));
        var legacy=new ConcreteSlabSection(single.Double("_height"),single.Double("_width"),single.Double("_topRebarArea"),single.Double("_topCover"),single.Double("_bottomRebarArea",0),50);
        Require(legacy.Height==250 && legacy.Width==3000 && legacy.TopRebarArea==2000 && legacy.TopCover==40 && legacy.BottomRebarArea==0 && legacy.IsValid,"Legacy slab archive changed.");
        var slab=new ConcreteSlabSection(250,3000,2000,40,1500,55);
        var copy=slab with{};
        Require(copy.BottomRebarArea==1500 && copy.BottomCover==55 && copy.TotalRebarArea==3500,"Two-row copy lost bottom reinforcement.");
        foreach(var invalid in new[]{slab with{BottomRebarArea=-1},slab with{BottomCover=250},slab with{TopCover=200,BottomCover=60},slab with{BottomRebarArea=double.NaN}})
            Require(!invalid.IsValid,"Invalid reinforcement row accepted.");
        GpcRows(Require,Near); // R3: explicit numbers and the slab read by value

        var assembly=Assembly.LoadFrom(BuildPaths.GrasshopperAssembly);
        GH_Component Component(GH_Document d,string name) { var c=(GH_Component)Activator.CreateInstance(assembly.GetType("BridGHe.Grasshopper.Components.BridgeChecks."+name,true));c.CreateAttributes();d.AddObject(c,false);return c; }
        void Set(GH_Component c,int port,IGH_Goo value) {var p=(Param_GenericObject)c.Params.Input[port];p.PersistentData.Clear();p.PersistentData.Append(value);p.ExpireSolution(false);}
        void Solve(GH_Document d,GH_Component c,bool button=false) {if(button){c.GetType().GetField("_run",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(c,true);c.ExpireSolution(false);}GH_Document.EnableSolutions=true;d.Enabled=true;d.NewSolution(false,GH_SolutionMode.Silent);Require(c.RuntimeMessages(GH_RuntimeMessageLevel.Error).Count==0,string.Join("; ",c.RuntimeMessages(GH_RuntimeMessageLevel.Error)));}
        using(var doc=new GH_Document())
        {
            var c=Component(doc,"BeamSectionConcreteSlabComponent");
            void Number(int i,double value) {var p=(Param_Number)c.Params.Input[i];p.PersistentData.Clear();p.PersistentData.Append(new GH_Number(value));p.ExpireSolution(false);}
            Number(0,250);Number(1,3000);Solve(doc,c);
            // P2R (T3): the output is the GPC datum of the slab since P2K (P10), BridGHe.Common.Checks.ConcreteSlabSection.
            Require(((BridGHe.Common.Checks.ConcreteSlabSection)c.Params.Output[0].VolatileData.AllData(true).Single().ScriptVariable()).TotalRebarArea==0,"Default optional rows should be disabled.");
            Number(2,2000);Number(3,40);Number(4,1500);Number(5,55);Solve(doc,c);
            Require(((BridGHe.Common.Checks.ConcreteSlabSection)c.Params.Output[0].VolatileData.AllData(true).Single().ScriptVariable()).BottomCover==55 && c.Params.Input.Skip(2).All(p=>p.Optional),"GH bottom ports or optional flags invalid.");
        }
        // Decision (i) = A of the user (7/11): Super Element Check runs only on the GPC bridge model of Build BridGHe Model; without it a clear
        // error and no outputs. R3: on a GH model and a composite beam given by their GPC data alone (no GPC bridge model attached). P2CB (13/11):
        // the same on the FEM carriers (a FEM model and a FEM composite beam) is retired by name.
        {
            using var doc=new GH_Document();var c=Component(doc,"SuperElementCheckComponent");
            var test=new GpcTestModel("Slab reinforcement without bridge");test.Beam("B1",test.Node("1",0,0,0),test.Node("2",10000,0,0));
            var conversion=BridGHe.Common.Gpc.GpcModels.Conversion.FromGpc(test.Structure(),new BridGHe.Common.Gpc.BridgeUnits(BridGHe.Common.Gpc.DefinitionUnits.Nmm),new System.Collections.Generic.Dictionary<int,double>{[1]=0},1e-3);
            IGH_Goo From(string name,params object[] args)=>(IGH_Goo)assembly.GetType("BridGHe.Grasshopper.DataTypes."+name,true).GetMethod("FromGpc",BindingFlags.Public|BindingFlags.Static).Invoke(null,args);
            var draft=BridGHe.Common.Gpc.Builders.GpcBeamBuilders.New(new GPC.Geometry.Point3d(0,0,0),new GPC.Geometry.Point3d(10000,0,0),GpcFixtures.RectangleProperty(),null,0,0,0,
                BridGHe.Common.Gpc.GpcIdentity.Derive(BridGHe.Common.Gpc.GpcIdentity.NameSpace,"r66-r3-slab-error:B1"));draft.Beam.Name="B1";
            var definition=GpcCompositeDefinitions.Create(Slab(250,3000,2000,40,1500,55));
            var datum=Activator.CreateInstance(assembly.GetType("BridGHe.Grasshopper.DataTypes.GpcCompositeBeam",true),draft,definition,definition,(System.Collections.Generic.IReadOnlyList<string>)Array.Empty<string>());
            Set(c,0,From("Fem.GH_Model",conversion));Set(c,1,From("GH_CompositeBeam",datum,null));
            c.GetType().GetField("_run",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(c,true);c.ExpireSolution(false);GH_Document.EnableSolutions=true;doc.Enabled=true;doc.NewSolution(false,GH_SolutionMode.Silent);
            Require(c.RuntimeMessages(GH_RuntimeMessageLevel.Error).Any(m=>m.Contains("Build BridGHe Model"))&&c.Params.Output.All(o=>o.VolatileData.IsEmpty),
                "GPC data alone: Super Element Check without the GPC bridge model of Build BridGHe Model: expected its error and no outputs.");
            gpcChecks++;
        }
        foreach(bool tapered in new[]{false,true})GpcSuperElement(assembly,tapered,Require,Near);
        Console.WriteLine($"SLAB REINFORCEMENT GPC PASS: {gpcChecks} checks on definitions built in GPC (rows of bars, analysis section, invalid rows) and on Super Element Check with D3 on a GPC bridge model; without it, the error of decision (i) = A and no outputs.");
    }
    // P2U (F6, chain E (c) and C5): the same checks on the composite section as the GPC contract of the Checker (BridgeCompositeSectionDefinition)
    // built directly in GPC (GpcCompositeDefinitions), and Super Element Check on the GPC path (D3 on the GPC bridge model of Build BridGHe
    // Model, from an action source built in GPC; the composite beam carries definitions built in GPC).
    // R6.6 lane R3 (11/11): with no FEM class: the numbers of the definitions written explicitly (the slab of the fixtures: 250 × 3000, top row
    // 2000 mm² at 40, bottom row 1500 mm² at 55), the legacy single-row slab read by value from its archive, Super Element Check on a GH model
    // and a composite beam given by their GPC data alone (GH_Model.FromGpc with the GPC bridge model attached, GH_CompositeBeam.FromGpc).
    // P2CB (13/11, rule U3): the transitional parts (the parity with the FEM properties and with the FEM merge, the run of Super Element Check on
    // the FEM carriers) are retired by name with the FEM types; the slab is the GPC datum of the component (ConcreteSlabSection, since P2K).
    private static int gpcChecks;

    /// <summary>The numbers of a definition of the grillage example (GpcCompositeDefinitions.Grillage) with the slab and rows given.</summary>
    private static GpcCompositeDefinitions.Numbers Slab(double height,double width,double topArea,double topCover,double bottomArea,double bottomCover,double? nShrinkage=null)
    {
        var x=GpcCompositeDefinitions.Grillage(new()) with{SlabWidth=width,SlabHeight=height,TopArea=topArea,TopCover=topCover,BottomArea=bottomArea,BottomCover=bottomCover,ConsiderRebar=true};
        return nShrinkage==null?x:x with{NShrinkage=nShrinkage.Value};
    }

    /// <summary>The legacy slab (one row of bars, 0.3.0 archive of the fixture) read by value: height, width, top area and cover; the bottom row
    /// the archive does not have (0, and the cover of the bottom row of the fixtures, which a zero row does not use).</summary>
    private static GpcCompositeDefinitions.Numbers LegacySlab(Action<bool,string> require)
    {
        var v=BridGHe.Common.Gpc.Legacy.LegacyArchive.Read(File.ReadAllBytes("tests/BridGHe.Smoke/Fixtures/slab-single-row.bgha"));
        require(v.Is("SectionConcreteSlabModel") && v.Double("_height")==250 && v.Double("_width")==3000 && v.Double("_topRebarArea")==2000 && v.Double("_topCover")==40
            && v.Double("_bottomRebarArea",0)==0 && !v.Has("_bottomCover"),"GPC: Legacy slab archive changed (read by value).");
        return Slab(v.Double("_height"),v.Double("_width"),v.Double("_topRebarArea"),v.Double("_topCover"),v.Double("_bottomRebarArea",0),55);
    }

    /// <summary>The rows of bars of a definition built in GPC: both rows in the analysis section (one bar per row, whole area) and in the section
    /// of the checks (three bars per row) at their covers; rows that are not valid rejected; a zero bottom row whatever its cover gives the
    /// values of a section without it (the legacy single-row slab read by value).</summary>
    private static void GpcRows(Action<bool,string> require,Action<double,double> near)
    {
        var two=Slab(250,3000,2000,40,1500,55);var definition=GpcCompositeDefinitions.Create(two);
        foreach(var section in new[]{definition.AnalysisSection(),definition.Section})
        {
            near(section.Rebars.Where(b=>Math.Abs(b.Position.Y-210)<1e-9).Sum(b=>b.RebarSection.Area),2000);
            near(section.Rebars.Where(b=>Math.Abs(b.Position.Y-55)<1e-9).Sum(b=>b.RebarSection.Area),1500);
        }
        require(definition.RebarRows.BottomAreaMm2==1500 && definition.RebarRows.BottomCoverMm==55 && definition.Copy().RebarRows.BottomAreaMm2==1500,"GPC: Two-row definition lost bottom reinforcement.");
        foreach(var (area,cover) in new[]{(-1.0,55.0),(double.NaN,55.0),(1500,double.NaN)})
        {
            bool rejected=false;try{GpcCompositeDefinitions.Create(two with{BottomArea=area,BottomCover=cover});}catch(ArgumentException){rejected=true;}
            require(rejected,"GPC: Invalid reinforcement row accepted.");
        }
        var without=GpcCompositeDefinitions.Create(LegacySlab(require));var zero=GpcCompositeDefinitions.Create(Slab(250,3000,2000,40,0,55));
        var a=BridGHe.Common.Gpc.GpcCompositeSections.AnalysisProperties(without,without.NLongTerm,true,true,true);var b=BridGHe.Common.Gpc.GpcCompositeSections.AnalysisProperties(zero,zero.NLongTerm,true,true,true);
        require(a.Equals(b),"GPC: Zero bottom reinforcement changes existing numerical properties.");
        gpcChecks+=10;
    }

    /// <summary>
    /// Super Element Check on the D3 path: the GH model carries the GPC bridge model of Build BridGHe Model (GpcBridgeMerge) of a source built
    /// in GPC (BM2 1e7 for the three phases, 5e6 for the shrinkage case «Shrink FEM», at 0, 5 and 10 m) and the recipe steel → long term →
    /// short term → shrinkage (that case and slab strain −0.0002); the composite beam B1 carries definitions built in GPC (constant, or tapered:
    /// bottom row 1500 → 2500 mm² at cover 55 → 65, n,sh 12 → 14). Same assertions: three stations, bottom row enabled at its interpolated
    /// cover and area, five stages (steel, long, instant, shrinkage case, autostress), n,sh of the shrinkage contributions.
    /// R3: on a GH model and a composite beam given by their GPC data alone. P2CB (13/11, rule U3): the same on the FEM carriers, with the parity
    /// of the bridge model and of the definitions with the FEM merge and properties, is retired by name.
    /// </summary>
    private static void GpcSuperElement(System.Reflection.Assembly assembly,bool tapered,Action<bool,string> require,Action<double,double> near)
    {
        var stations=new[]{0d,5000,10000};var phaseCases=new[]{"Phase0","Phase1","Phase2"};
        GPC.Model.Models.Model Gpc(bool analyse)
        {
            var test=new GpcTestModel("Slab reinforcement source");
            foreach(var name in phaseCases.Append("Shrink FEM"))test.Case(name);
            var beam=test.Beam("B1",test.Node("1",0,0,0),test.Node("2",10000,0,0));
            foreach(var name in phaseCases.Append("Shrink FEM"))foreach(double s in stations)test.Forces(beam,name,s,0,bm2:name=="Shrink FEM"?5e6:1e7);
            return analyse?test.Analyse():test.Structure();
        }
        var input=Gpc(false);var gpc=Gpc(true);
        BridgePhaseDefinition Phase(int order,BridgeSectionState state,BridgeActionDefinition action)=>new(){Order=order,Name="Phase "+order,State=state,Actions=new[]{action}};
        BridgeActionDefinition Action(string key,string lc,BridgeActionBehavior behavior,double? strain=null)=>new(){Key=key,GpcSource=gpc,Case=lc,Behavior=behavior,Strain=strain};
        var phases=new[]{Phase(10,BridgeSectionState.Steel,Action("G","Phase0",BridgeActionBehavior.Steel)),Phase(20,BridgeSectionState.Composite,Action("L","Phase1",BridgeActionBehavior.LongTerm)),
            Phase(30,BridgeSectionState.Composite,Action("I","Phase2",BridgeActionBehavior.ShortTerm)),Phase(40,BridgeSectionState.Composite,Action("R","Shrink FEM",BridgeActionBehavior.Shrinkage,-0.0002))};
        var table=new[]{new[]{"","G","L","I","R"},new[]{"Test","1","1","1","1"}};
        // The super element of the beam, as the bridge data of the GPC input of a model of Build Model carry it (Super Element Check reads it there).
        var data=new GPC.Checkers.CompositeBridge.BridgeModelData();data.SetSuperElement(input.BeamElements.Values.Single().Guid,"Main");
        var bridge=GpcBridgeMerge.Build(new[]{(input,gpc)},phases,table,"slab reinforcement "+tapered,bridge:data);
        // The definitions of the composite beam, from explicit numbers (start: the two-row slab, n,sh 12; end: the same, or the tapered one).
        var startDefinition=GpcCompositeDefinitions.Create(Slab(250,3000,2000,40,1500,55,12));
        var endDefinition=tapered?GpcCompositeDefinitions.Create(Slab(250,3000,2000,40,2500,65,14)):startDefinition;
        IGH_Goo Type(string name,string method,params object[] args)=>(IGH_Goo)assembly.GetType("BridGHe.Grasshopper.DataTypes."+name,true).GetMethod(method,System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.Static).Invoke(null,args);
        void Check(IGH_Goo model,IGH_Goo beam,string carriers)
        {
            using var doc=new GH_Document();
            var c=(GH_Component)Activator.CreateInstance(assembly.GetType("BridGHe.Grasshopper.Components.BridgeChecks.SuperElementCheckComponent",true));c.CreateAttributes();doc.AddObject(c,false);
            void Set(int port,IGH_Goo value){var p=(Param_GenericObject)c.Params.Input[port];p.PersistentData.Clear();p.PersistentData.Append(value);p.ExpireSolution(false);}
            Set(0,model);Set(1,beam);
            c.GetType().GetField("_run",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(c,true);c.ExpireSolution(false);
            GH_Document.EnableSolutions=true;doc.Enabled=true;doc.NewSolution(false,GH_SolutionMode.Silent);
            require(c.RuntimeMessages(GH_RuntimeMessageLevel.Error).Count==0,"GPC ("+carriers+"): "+string.Join("; ",c.RuntimeMessages(GH_RuntimeMessageLevel.Error)));
            var results=c.Params.Output[0].VolatileData.AllData(true).Select(g=>(BridgeStressResult)g.ScriptVariable()).ToArray();require(results.Length==3,"GPC ("+carriers+"): Expected three checked stations.");
            for(int i=0;i<3;i++)
            {
                var input3=results[i].Analysis.Input;var row=input3.BottomRebars;require(row.Enabled,"GPC ("+carriers+"): Bottom row lost before gPC checker.");
                require(results[i].Analysis.Stages.Count==5,"GPC ("+carriers+"): Expected steel, long, instant, shrinkage FEM and autostress contributions: "+results[i].Analysis.Stages.Count);
                near(results[i].Stage.Contributions[3].HomogenizationN,12+(tapered?i:0));near(results[i].Stage.Contributions.Last().HomogenizationN,12+(tapered?i:0));
                near(row.AxisDistance,55+(tapered?5*i:0));near(Math.PI*row.Diameter*row.Diameter/4*input3.Geometry.SlabWidth/row.Pitch,1500+(tapered?500*i:0));
            }
            gpcChecks+=3+3*6;
            // Study item 16 (Brecciarola, 19/11/2026): the optional inputs Options and ULS Combinations at the end of the component.
            void Run(){c.GetType().GetField("_run",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(c,true);c.ExpireSolution(false);doc.NewSolution(false,GH_SolutionMode.Silent);}
            BridgeStressResult[] Results()=>c.Params.Output[0].VolatileData.AllData(true).Select(g=>(BridgeStressResult)g.ScriptVariable()).ToArray();
            string Errors()=>string.Join("; ",c.RuntimeMessages(GH_RuntimeMessageLevel.Error));
            double[] Numbers(BridgeStressResult r)=>new[]{r.Point1_ConcreteSigma,r.Point2_RebarSigma,r.Point4_ConcreteSigma,r.Point5_SteelSigma,r.Point6_SteelSigma,r.Point7_SteelSigma,r.Point8_SteelSigma,r.Point6_SteelTau,r.Point7_SteelTau};
            require(c.Params.Input.Count==7&&c.Params.Input[5].Optional&&c.Params.Input[6].Optional&&c.Params.Output.Count==3,"GPC ("+carriers+"): Options and ULS Combinations must be optional inputs 5 and 6, outputs unchanged.");
            require(results.All(r=>r.Analysis.Input.Options.LimitState==GPC.Checkers.CompositeBridge.BridgeLimitState.Rare)&&c.Message=="N–Mx–Vy","GPC ("+carriers+"): without the new inputs the check must stay at the rare SLS.");
            // Options equal to the defaults of the component: the same numbers, bit for bit, and a remark that the toggles are not used.
            var goo=assembly.GetType("BridGHe.Grasshopper.DataTypes.BridgeDetailGoo",true);
            Set(5,(IGH_Goo)Activator.CreateInstance(goo,(object)new GPC.Checkers.CompositeBridge.BridgeAnalysisOptions{Standard=GPC.Checkers.CompositeBridge.BridgeStandard.Eurocode4,LimitState=GPC.Checkers.CompositeBridge.BridgeLimitState.Rare}));Run();
            require(Errors()=="","GPC ("+carriers+"): Options: "+Errors());
            var same=Results();require(same.Length==3&&Enumerable.Range(0,3).All(i=>Numbers(same[i]).SequenceEqual(Numbers(results[i]))),"GPC ("+carriers+"): default Options changed the numbers.");
            require(c.RuntimeMessages(GH_RuntimeMessageLevel.Remark).Any(m=>m.StartsWith("Options collegate")),"GPC ("+carriers+"): missing remark on the toggles replaced by Options.");
            ((Param_GenericObject)c.Params.Input[5]).PersistentData.Clear();
            // ULS Combinations by prefix: every combination at the ULS, the message says so; an unknown name is an error before any calculation.
            var uls=(Param_String)c.Params.Input[6];uls.PersistentData.Clear();uls.SetPersistentData("Te*");uls.ExpireSolution(false);Run();
            require(Errors()==""&&Results().Length==3&&Results().All(r=>r.Analysis.Input.Options.LimitState==GPC.Checkers.CompositeBridge.BridgeLimitState.Ultimate)&&c.Message=="N–Mx–Vy · SLU 1/1",
                "GPC ("+carriers+"): ULS Combinations by prefix not applied: "+Errors()+" / "+c.Message);
            uls.PersistentData.Clear();uls.SetPersistentData("Test");uls.ExpireSolution(false);Run();
            require(Errors()==""&&Results().All(r=>r.Analysis.Input.Options.LimitState==GPC.Checkers.CompositeBridge.BridgeLimitState.Ultimate),"GPC ("+carriers+"): ULS Combinations by name not applied.");
            uls.PersistentData.Clear();uls.SetPersistentData("Nope");uls.ExpireSolution(false);Run();
            require(c.RuntimeMessages(GH_RuntimeMessageLevel.Error).Any(m=>m.StartsWith("Combinazioni SLU sconosciute: Nope"))&&c.Params.Output[0].VolatileData.IsEmpty,"GPC ("+carriers+"): an unknown ULS combination must be an error, without outputs.");
            gpcChecks+=8;
        }
        // R3: the GH model and the composite beam given by their GPC data alone.
        var conversion=BridGHe.Common.Gpc.GpcModels.Conversion.FromGpc(input,new BridGHe.Common.Gpc.BridgeUnits(BridGHe.Common.Gpc.DefinitionUnits.Nmm),new System.Collections.Generic.Dictionary<int,double>{[1]=0},1e-3,bridge:data);
        var gpcModel=Type("Fem.GH_Model","FromGpc",conversion);gpcModel.GetType().GetMethod("AttachBridge").Invoke(gpcModel,new object[]{bridge.Bridge});
        var draft=BridGHe.Common.Gpc.Builders.GpcBeamBuilders.New(new GPC.Geometry.Point3d(0,0,0),new GPC.Geometry.Point3d(10000,0,0),GpcFixtures.RectangleProperty(),null,0,0,0,
            BridGHe.Common.Gpc.GpcIdentity.Derive(BridGHe.Common.Gpc.GpcIdentity.NameSpace,"r66-r3-slab:B1"));
        draft.Beam.Name="B1";
        var datum=Activator.CreateInstance(assembly.GetType("BridGHe.Grasshopper.DataTypes.GpcCompositeBeam",true),draft,startDefinition,endDefinition,(System.Collections.Generic.IReadOnlyList<string>)Array.Empty<string>());
        Check(gpcModel,Type("GH_CompositeBeam","FromGpc",datum,null),"GPC data alone");

    }
}
