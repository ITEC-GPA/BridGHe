using System;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using BridGHe.Common.Helpers;
using BridGHe.Common.Models;
using BridGHe.Common.MovingLoads;
using BridGHe.Common.Straus;
using Rhino.Geometry;
using Rhino.Runtime.InProcess;

internal static class Program
{
    private static int checks;
    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            RhinoInside.Resolver.Initialize(@"C:\Program Files\Rhino 8\System");
            if(args.Contains("--regression") && args.SkipWhile(a=>a!="--regression").Skip(1).FirstOrDefault()=="bridge-report")
                return RunBridgeReport();
            return Run(args);
        }
        catch(Exception ex) { Console.Error.WriteLine(ex);return 1; }
    }
    private static void Assert(bool test,string name) { if(!test)throw new Exception("FAIL: "+name);checks++;Console.WriteLine("PASS: "+name); }
    // Run managed Word packaging tests without initializing the native Rhino host.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int RunBridgeReport()
    {
        Console.WriteLine("RUN REGRESSION: bridge-report");
        BridgeReportTests.Run();
        Console.WriteLine("PASS REGRESSION: bridge-report");
        return 0;
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int Run(string[] args)
    {
        // --pure does not start Rhino, useful for CI and unlicensed environments.
        using var rhino=args.Contains("--pure")?null:new RhinoCore(new[]{"/nosplash"},WindowStyle.Hidden);
        if(args.Contains("--combination-excel-read")) { BridgeCombinationExcelTests.ReadExternal(args.SkipWhile(a => a != "--combination-excel-read").Skip(1).First()); return 0; }
        if(args.Contains("--capture-bridge-baseline")) { BridgeNumericalBaseline.Capture();return 0; }
        if(args.Contains("--benchmark-plots")) { PlotTests.Run();PlotTests.Benchmark();return 0; }
        if(args.Contains("--moving-load-tests")) return MovingLoadBenchmarks.Run(args.Contains("--native"),args.SkipWhile(a=>a!="--moving-load-tests").Skip(1).FirstOrDefault(a=>!a.StartsWith("--"))??"all");
        if(args.Contains("--example-layout")) return ExampleLayoutTests.Run(args.SkipWhile(a=>a!="--example-layout").Skip(1).FirstOrDefault(a=>!a.StartsWith("--")));
        if(args.Contains("--utility-examples")) { BridgeUtilityTests.Run(); BridgeUtilityExamples.Run(true); return 0; }
        if(args.Contains("--combination-excel-example")) { BridgeCombinationExcelExample.Run(true); return 0; }
        if(args.Contains("--bridge-workflow")) return BridgeWorkflowExample.Run(args.Contains("--native"));
        if(args.Contains("--workflow-stations-native")) return NativeWorkflowStationTests.Run(args.Contains("--capture-station-fixture"));
        if(args.Contains("--inventory")) { CheckGrasshopper(false,true);return 0; }
        if(args.Contains("--components-only")) { CheckGrasshopper(false);return 0; }
        if(args.Contains("--simple-bridge-examples")) { SimpleBridgeTests.Run(); SimpleBridgeExamples.Run(true); return 0; }
        if(args.Contains("--bridge-design-examples")) { BridgeDesignExamples.Run(true); return 0; }
        if(args.Contains("--bridge-details-example")) { BridgeDetailsExample.Run(true); return 0; }
        if(args.Contains("--bridge-details-model-example")) { BridgeDetailsModelExample.Generate(); return 0; }
        if(args.Contains("--bridge-details-plot-viewer")) { BridgeDetailsModelExample.GeneratePlotViewer(); return 0; }
        if(args.Contains("--bridge-report-example")) { BridgeReportExample.Run(true); return 0; }
        if(args.Contains("--bridge-bearings-native")) { BridgeBearingTests.Run(); BridgeBearingTests.Native(); return 0; }
        if(args.Contains("--bridge-connections-native")) { BridgeConnectionTests.Run(); BridgeConnectionTests.Native(); return 0; }
        if(args.Contains("--beam-support")) { NativeBeamSupportTests.Run();return 0; }
        if(args.Contains("--benchmark-shear")) { BridgePerformanceTests.Run();return 0; }
        if(args.Contains("--sections-only")) { CheckNativeSections();return 0; }
        if(args.Contains("--examples")) return NativeExamples.Run(args.SkipWhile(a=>a!="--examples").Skip(1).FirstOrDefault()??"all");
        if(args.Contains("--bridge-native")) return NativeBridgePhases.Run();
        if(args.Contains("--bridge-grillage")) return BridgeGrillageExample.Run(args.Contains("--gh"));
        if(args.Contains("--bridge-spine")) return BridgeSpineExamples.Run(args.Contains("--gh"),args.SkipWhile(a=>a!="--bridge-spine").Skip(1).FirstOrDefault(a=>!a.StartsWith("--"))??"all");
        if(args.Contains("--bridge-suite")) return BridgeSuiteExample.Run(args.Contains("--gh"),args.Contains("--probe"),args.SkipWhile(a=>a!="--bridge-suite").Skip(1).FirstOrDefault(a=>!a.StartsWith("--"))??"all");
        if(args.Contains("--grillage-gh-replay")) return GrasshopperBridgeExample.Replay();
        if(args.Contains("--bridge-gh-replay")) return GrasshopperBridgeSuite.Replay(args.SkipWhile(a=>a!="--bridge-gh-replay").Skip(1).FirstOrDefault()??"12");
        if(args.Contains("--gh-examples")) return GrasshopperExamples.Run(args.SkipWhile(a=>a!="--gh-examples").Skip(1).FirstOrDefault()??"all");
        if(args.Contains("--regression"))
            return RegressionTests.Run(args.SkipWhile(a=>a!="--regression").Skip(1).FirstOrDefault()??"all");
        var model=new ModelModel(ModelModel.ModelUnits.Nmm);
        model.Name="Roundtrip";
        var section=new SectionModel { SectionType=SectionModel.SectionTypes.SolidCircle,D=20 };
        section.CalculateSection();
        Assert(Math.Abs(section.SectionArea-Math.PI*100)<1e-10,"circle analytical area");
        Assert(Math.Abs(section.I11-Math.PI*10000/4)<1e-8,"circle analytical inertia");
        var copy=ModelArchive.Clone(section);
        Assert(copy.D==20 && copy.SectionArea==section.SectionArea,"section archive roundtrip");
        var path=new MovingPath { LoadCaseName="Traffic", Coordinates=new double[]{0,0,0,10,0,0,0,1,0},Template=new MovingTemplate { Vehicles=new[]{new MovingVehicle { Loads=new MovingObject[]{new MovingPointForce { Force=new double[]{0,0,-100} }} }} }};
        path.Validate();model.MovingPaths.Add(path);
        ModelHelper.GetLoadCases(model,out var exportCases);
        Assert(exportCases.Any(c=>c.Name=="Traffic") && exportCases[0].Name=="Dead","standalone moving case included in export");
        var allLoads=Enumerable.Range(0,5).Select(i=>(MovingObject)new MovingDistributedForce { Mobility=i,X1=0,X2=5,Y1=-1,Y2=1,Force=new double[]{0,0,-10} })
            .Concat(Enumerable.Range(0,2).Select(i=>(MovingObject)new MovingPointForce { Mobility=i,Force=new double[]{0,0,-100} }))
            .Append(new MovingHeatSource { SizeX=1,SizeY=1,Heat=100 }).ToArray();
        var allTemplate=new MovingTemplate { LaneFactors=new[]{1.0,0.8},Centrifugal=new MovingCentrifugal { ForceUnit=6 },Vehicles=new[]{new MovingVehicle { Lanes=new[]{1,2},Loads=allLoads }} };
        allTemplate.Validate();var allCopy=(MovingTemplate)allTemplate.Clone();
        Assert(allCopy.Vehicles[0].Loads.Length==8 && allCopy.LaneFactors[1]==0.8,"all API moving load modes roundtrip");
        var movingCopy=(MovingPath)path.Clone();movingCopy.Template.Vehicles[0].Loads[0]=new MovingHeatSource { SizeX=1,SizeY=1 };
        Assert(path.Template.Vehicles[0].Loads[0] is MovingPointForce,"moving graph deep copy");
        var modelCopy=ModelArchive.Clone(model);
        Assert(modelCopy.Name==model.Name && modelCopy.MovingPaths.Count==1,"model and moving paths archive roundtrip");
        modelCopy.GeneratedMovingCases.Add(3,"Old moving case");modelCopy.LoadCases.Add(new LoadCaseModel("3","Old moving case"));
        var replaced=MovingModelBuilder.ReplacePaths(modelCopy,new[]{path});
        Assert(replaced.GeneratedMovingCases.Count==0 && replaced.LoadCases.All(c=>c.Name!="Old moving case") && replaced.LoadCases.Any(c=>c.Name=="Traffic"),"replacing moving paths removes obsolete generated cases");
        Assert(modelCopy.GeneratedMovingCases.Count==1,"replacing moving paths leaves original model intact");
        CheckBridge();
        CheckPhaseAssembly();
        if(!args.Contains("--pure"))
        {
        var material=new MaterialModel("S355") { Name="S355",Modulus=210000,MinimumYieldStress=355,MinimumTensileStress=510 };
        var property=new BeamPropertyModel("P1") { Name="P1",Material=material,Section=new SectionModel { SectionType=SectionModel.SectionTypes.Generic,SectionArea=100,I11=100,I22=100,J=100 },PropertyType=BeamPropertyType.Beam };
        var node1=new NodeModel("N1",new Point3d(0,0,0));
        node1.Attributes.Add(new NodeRestraintAttributeModel(true,true,true,true,true,true));
        var node2=new NodeModel("N2",new Point3d(1000,0,0));
        var beam=new BeamModel("B1",node1.Position,node2.Position,property);
        var traffic=new LoadCaseModel("1","Traffic");model.LoadCases.Add(traffic);
        beam.Results.Add(new BeamForceResultModel(traffic,0,0,"") { AxialForce=1000 });
        model.AddElements(new ElementModel[]{node1,node2,beam});
        var graph=ModelArchive.Clone(model);
        Assert(graph.Nodes.Count==2 && graph.Beams.Count==1 && graph.GetBeam(graph.Beams[0].BeamId)!=null,"FEM graph and indexes roundtrip");
        Assert(graph.Nodes[0].Attributes.Count==1 && graph.Beams[0].Results.Count==1,"attributes and results roundtrip");
        Assert(object.ReferenceEquals(graph.LoadCases[0],graph.Beams[0].Results[0].LoadCase),"shared load case identity preserved");
        var graphCopy=new ModelModel(model);
        Assert(graphCopy.Beams.Count==1 && graphCopy.Nodes[0].Attributes.Count==1,"FEM copy constructor with cloned attributes");
        CheckGrasshopper(args.Contains("--installed-plugins"));
        RegressionTests.Run("all");
        }
        Assert(StrausSectionCatalog.All.Length==21 && StrausSectionCatalog.All.Select(s=>s.Key).Distinct().Count()==21,"all 15 standard and 6 BGL sections");
        bool invalid=false;try {new MovingPointForce { Mobility=2 }.Validate();}catch(ArgumentException){invalid=true;}
        Assert(invalid,"invalid moving mobility rejected");
        if(args.Contains("--native"))CheckNative();
        Console.WriteLine($"PASS TOTAL {checks}");return 0;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void CheckGrasshopper(bool installedPlugins,bool inventoryOnly=false)
    {
        System.Reflection.Assembly.LoadFrom(@"C:\Program Files\Rhino 8\Plug-ins\Grasshopper\Grasshopper.dll");
        System.Reflection.Assembly.LoadFrom(@"C:\Program Files\Rhino 8\Plug-ins\Grasshopper\GH_IO.dll");
        string path=BuildPaths.GrasshopperAssembly;
        var assembly=System.Reflection.Assembly.LoadFrom(path);
        var guids=new System.Collections.Generic.HashSet<Guid>();int count=0,bridgeChecks=0;
        var iconIssues=new System.Collections.Generic.List<string>();
        var iconHashes=new System.Collections.Generic.HashSet<string>();
        var items=new System.Collections.Generic.List<object>();
        foreach(var type in assembly.GetTypes().Where(t=>!t.IsAbstract&&t.GetConstructor(Type.EmptyTypes)!=null))
        {
            var guidProperty=type.GetProperty("ComponentGuid");if(guidProperty==null)continue;
            var instance=Activator.CreateInstance(type);Guid guid=(Guid)guidProperty.GetValue(instance);
            Assert(guids.Add(guid),"unique component GUID: "+type.Name);count++;
            if(type.Namespace.EndsWith(".BridgeChecks"))bridgeChecks++;
            var component=instance as Grasshopper.Kernel.GH_Component;
            var documentObject=(Grasshopper.Kernel.IGH_DocumentObject)instance;
            using(var resource=assembly.GetManifestResourceStream("BridGHe.Grasshopper.Resources.Icons."+type.Name+".png"))
            {
                if(resource==null)iconIssues.Add(type.Name+": missing resource");
                else
                {
                    using var expected=new System.Drawing.Bitmap(resource);
                    var actual=documentObject.Icon_24x24;
                    bool matches=actual!=null && actual.Width==24 && actual.Height==24;
                    // GDI+ rounds RGB when copying semi-transparent pixels into cached bitmaps.
                    // Compare visible premultiplied channels within two 8-bit levels; opaque pixels and alpha remain exact.
                    bool SameRenderedPixel(System.Drawing.Color a,System.Drawing.Color b)
                    {
                        if(a.A!=b.A)return false;
                        int tolerance=a.A==255?0:2*255;
                        return Math.Abs(a.R*a.A-b.R*b.A)<=tolerance
                            && Math.Abs(a.G*a.A-b.G*b.A)<=tolerance
                            && Math.Abs(a.B*a.A-b.B*b.A)<=tolerance;
                    }
                    if(matches)
                        for(int y=0;y<24;y++)for(int x=0;x<24;x++)
                            matches &= SameRenderedPixel(actual.GetPixel(x,y),expected.GetPixel(x,y));
                    if(!matches)iconIssues.Add(type.Name+": icon not wired to its asset");
                    resource.Position=0;
                    iconHashes.Add(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(resource)));
                }
            }
            object[] Ports(System.Collections.Generic.IEnumerable<Grasshopper.Kernel.IGH_Param> ports) => ports.Select(p=>(object)new {
                name=p.Name,nickname=p.NickName,description=p.Description,access=p.Access.ToString(),optional=p.Optional,type=p.TypeName }).ToArray();
            items.Add(new {type=type.FullName,guid,kind=component==null?"parameter":"component",name=documentObject.Name,nickname=documentObject.NickName,category=documentObject.Category,subcategory=documentObject.SubCategory,description=documentObject.Description,
                inputs=component==null?Array.Empty<object>():Ports(component.Params.Input),outputs=component==null?Array.Empty<object>():Ports(component.Params.Output)});
        }
        if(inventoryOnly)
        {
            File.WriteAllText("artifacts/component-runtime.json",System.Text.Json.JsonSerializer.Serialize(items,new System.Text.Json.JsonSerializerOptions {WriteIndented=true}));
            Console.WriteLine($"INVENTORY {count} objects; icon validation deferred until assets are generated.");return;
        }
        Assert(bridgeChecks==22,"all 22 bridge workflow and plot components instantiate in Rhino 8");
        Assert(iconIssues.Count==0,"all component and parameter icons load at 24 px: "+string.Join(", ",iconIssues));
        Assert(iconHashes.Count==count,"each component and parameter has its own distinct icon");
        var info=(Grasshopper.Kernel.GH_AssemblyInfo)Activator.CreateInstance(assembly.GetType("BridGHe.Grasshopper.BridGHeInfo",true));
        Assert(info.Icon is {Width:24,Height:24},"plugin icon is present at 24 px");
        if(installedPlugins)
        {
            var priority=(Grasshopper.Kernel.GH_AssemblyPriority)Activator.CreateInstance(assembly.GetType("BridGHe.Grasshopper.BridGHeAssemblyPriority",true));
            Assert(priority.PriorityLoad()==Grasshopper.Kernel.GH_LoadingInstruction.Proceed,"category icon registration succeeds");
            Assert(Grasshopper.Instances.ComponentServer.GetCategoryIcon("BridGHe") is {Width:16,Height:16},"the single category icon has native 16 px dimensions");
        }
        else Console.WriteLine("SKIPPED: category registration with installed plugins (opt in with --installed-plugins)");
        File.WriteAllText("artifacts/component-runtime.json",System.Text.Json.JsonSerializer.Serialize(items,new System.Text.Json.JsonSerializerOptions {WriteIndented=true}));
        Console.WriteLine($"COMPONENTS {count}, BRIDGE_CHECKS {bridgeChecks}");
        CheckGooArchives(assembly);
    }

    private static void CheckGooArchives(System.Reflection.Assembly assembly)
    {
        var material=new MaterialModel("S355") { Name="S355",Modulus=210000 };
        var section=new SectionModel { Name="Native metadata",SectionType=SectionModel.SectionTypes.Generic,SectionArea=100,Ixx=123,Iyy=234,Ixy=12,NativeSection=new StrausSection { Key="TriangleSolid",Dimensions=new double[]{20,30} } };
        var property=new BeamPropertyModel("P") { Name="P",Section=section,Material=material };
        var composite=new CompositeBeamPropertyModel("C") { Name="C",SteelSection=section,ConcreteSection=new SectionConcreteSlabModel(200,1000,1000,40),ConcreteMaterial=material,SteelMaterial=material,RebarMaterial=material,NInfinite=18,NInstant=6,ConsiderConcrete=true,ConsiderRebar=true };
        foreach(var pair in new[]{("BridGHe.Grasshopper.DataTypes.Fem.GH_BeamSection",(object)section),("BridGHe.Grasshopper.DataTypes.Fem.GH_BeamProperty",(object)property),("BridGHe.Grasshopper.DataTypes.GH_CompositeBeamProperty",(object)composite)})
        {
            var type=assembly.GetType(pair.Item1,true);
            var value=Activator.CreateInstance(type,pair.Item2);
            var chunkType=Type.GetType("GH_IO.Serialization.GH_LooseChunk, GH_IO",true);
            var chunk=Activator.CreateInstance(chunkType,new object[]{"Test"});
            Assert((bool)type.GetMethod("Write").Invoke(value,new[]{chunk}),"GH archive write "+type.Name);
            var copy=Activator.CreateInstance(type);
            Assert((bool)type.GetMethod("Read").Invoke(copy,new[]{chunk}),"GH archive read "+type.Name);
            object restored=type.GetProperty("Value").GetValue(copy);
            if(restored is SectionModel s)Assert(s.Ixy==12 && s.NativeSection.Key=="TriangleSolid","GH native section and moments preserved");
            if(restored is BeamPropertyModel b)Assert(b.Section.NativeSection.Dimensions[1]==30,"GH nested beam section preserved");
            if(restored is CompositeBeamPropertyModel c)Assert(c.IsValid && c.NInfinite==18 && c.NInstant==6 && c.ConsiderConcrete && c.ConsiderRebar,"GH composite validity, modular ratios and flags preserved");
        }
    }

    private static void CheckNative()
    {
        CheckNativeSections();
        CheckNativeCantilever();
    }

    private static void CheckNativeSections()
    {
        StrausRuntime.EnsureInitialized();
        var previews=new System.Collections.Generic.List<object>();
        foreach(var shape in StrausSectionCatalog.All)
        {
            double Dimension(string n) => n.StartsWith("R")||n.StartsWith("alpha")?0:n=="D"?400:n=="B2"?100:n.StartsWith("B")?200:n=="L"?20:10;
            // A bulb with zero toe radius and slope is degenerate; use a finite bulb.
            var dimensions=shape.Key=="BglBulb" ? new double[]{200,50,10,10,0,5,0,0,30} : shape.Dimensions.Select(Dimension).ToArray();
            var section=new StrausSection { Key=shape.Key,Dimensions=dimensions }.Calculate();
            Assert(section.SectionArea>0 && section.I11>0 && section.I22>0,"native section "+shape.Key);
            Assert(section.NativeSection.PreviewContours.Length>0,"native section boundary "+shape.Key);
            var previewBeam=new BeamModel(Point3d.Origin,new(1000,0,0),new BeamPropertyModel("Preview"){Section=section,Material=new MaterialModel("S"),PropertyType=BeamPropertyType.Beam});
            Assert(previewBeam.Shapes.Length>0 && previewBeam.Shapes.All(b=>b.IsSolid),"native solid preview "+shape.Key);
            double volume=previewBeam.Shapes.Sum(b=> { using var mass=VolumeMassProperties.Compute(b);return mass.Volume; });
            Console.WriteLine($"PREVIEW {shape.Key}: A={volume/1000:G17}, native={section.SectionArea:G17}, points={section.NativeSection.PreviewContours.Sum(x=>x.Length/2)}");
            Assert(Math.Abs(volume/1000/section.SectionArea-1)<.03,"native preview area within 3 percent of exact area "+shape.Key);
            var copy=ModelArchive.Clone(section);
            Assert(copy.NativeSection.PreviewContours.SelectMany(x=>x).SequenceEqual(section.NativeSection.PreviewContours.SelectMany(x=>x)),"native preview archive "+shape.Key);
            previews.Add(new { shape.Key,section.SectionArea,section.NativeSection.PreviewContours });
            if(shape.Key=="BglRectangle") Assert(Math.Abs(section.SectionArea-11600)<1e-6,"BGL rectangular hollow analytical area");
        }
        Directory.CreateDirectory("artifacts/native-preview");
        File.WriteAllText("artifacts/native-preview/sections.json",System.Text.Json.JsonSerializer.Serialize(previews));
    }

    private static void CheckNativeCantilever()
    {
        // One Euler-Bernoulli beam, units N-mm, no self weight. Native test only after a successful St7Init.
        const double length=3000, force=1000, modulus=210000, inertia=450000000;
        var loadCase=new LoadCaseModel("P","P");
        var material=new MaterialModel("S355") { Name="S355",Modulus=modulus,PoissonRatio=0.3 };
        var section=new SectionModel { SectionType=SectionModel.SectionTypes.Generic,SectionArea=60000,
            I11=inertia,I22=200000000,J=100000000,ShearA1=0,ShearA2=0 };
        var property=new BeamPropertyModel("P") { Name="P",Material=material,Section=section,PropertyType=BeamPropertyType.Beam };
        var first=new NodeModel("N1",Point3d.Origin);
        first.Attributes.Add(new NodeRestraintAttributeModel(true,true,true,true,true,true));
        var end=new NodeModel("N2",new Point3d(length,0,0));
        end.Loads.Add(new NodeForceLoadModel(loadCase,new Vector3d(0,0,-force)));
        var beam=new BeamModel("1",first.Position,end.Position,property);
        var input=new ModelModel(ModelModel.ModelUnits.Nmm);
        input.LoadCases.Add(loadCase);input.AddElements(new ElementModel[]{first,end,beam});
        string folder=Path.Combine("artifacts","native",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
        var result=StrausAnalysis.Run(input,Path.GetFullPath(Path.Combine(folder,"cantilever.st7")),0,new[]{"P"},1e-6,false,out _);
        double actual=result.Nodes.Single(n=>Math.Abs(n.Position.X-length)<1e-6).Results.OfType<NodeDisplResultModel>().Single().Displacement.Z;
        double expected=-force*Math.Pow(length,3)/(3*modulus*inertia);
        Assert(Math.Abs(actual-expected)<=Math.Abs(expected)*1e-5,"native cantilever tip displacement FL^3/(3EI)");
        var fixedEnd=result.Beams[0].Results.OfType<BeamForceResultModel>().OrderBy(r=>r.Station).First();
        Assert(Math.Abs(Math.Abs(fixedEnd.My)-force*length)<=force*length*1e-5,"native plane-2 cantilever moment FL");
        Assert(input.Beams[0].Results.Count==0,"native analysis does not mutate input results");
    }

    private static void CheckBridge()
    {
        var concrete=new GPC.Model.Materials.ConcreteMaterialEN1992("C30",30,GPC.Model.Materials.ConcreteMaterial.CompressionStressStrainDiagrams.ParabolaRectangle);
        var steel=new GPC.Model.Materials.SteelMaterialEN1992("S355",210000,355,510,0.1,GPC.Model.Materials.SteelMaterial.StressStrainCurveType.ElasticPerfectPlastic,GPC.Model.Materials.SteelMaterial.SteelTypes.Structural);
        var h=new GPC.Model.Sections.SectionH(300,10,200,20,200,20,"H");
        GPC.Model.Sections.Concrete.ReinforcedConcreteSection Create() => BridGHe.Common.Checks.CompositeSectionFactory.Create(1000,200,concrete,null,100,50,null,100,h,steel,50,0,"Test");
        var p=CompositePropertyAdapter.Calculate(Create(),0,false,false,true);
        double n0=steel.ElasticModulusTension/concrete.ElasticModulusCompression;
        Assert(Math.Abs(p.areaH/n0-h.Area)<1e-6,"steel-only homogenized area");
        Assert(Math.Abs(p.JxxH/n0-h.Jxx)<1e-4,"steel-only homogenized inertia");
        Assert(Math.Abs(p.centroidH.Y+150)<1e-8,"steel-only centroid");
        var cs=new GPC.Geometry.CoordinateSystem(new GPC.Geometry.Point2d(500,-150),new GPC.Geometry.Vector3d(1,0,0),new GPC.Geometry.Vector3d(0,1,0));
        var options=new GPC.Checkers.CompositeBridge.BridgeAnalysisOptions { Standard=GPC.Checkers.CompositeBridge.BridgeStandard.Eurocode4, LimitState=GPC.Checkers.CompositeBridge.BridgeLimitState.Rare, WebBuckling=false,TopFlangeBuckling=false,BottomFlangeBuckling=false};
        var checker=new BridGHe.Common.Checks.BridgeCheckAdapter(Create(),cs, options);
        var force=new GPC.Model.Results.ResultBeamForces(10000,0,10000,0,0,0,cs);
        var zero=new GPC.Model.Results.ResultBeamForces(0,0,0,0,0,0,cs);
        checker.GetLinearAnalysisResultByStages(out var first,out _,out var last,out var tauB,0,0,force,zero,zero,true);
        Assert(Math.Abs(first.Point5_SteelSigma-10000/h.Area)<1e-8,"bridge axial stress N/A");
        double tau=10000.0*(200*20*140)/(h.Jxx*10);
        Assert(Math.Abs(first.Point6_SteelTau-tau)<1e-8 && Math.Abs(first.Point7_SteelTau-tau)<1e-8,"bridge point shear VQ/It");
        Assert(double.IsNaN(tauB),"unmapped TauB stays explicit NaN");
        bool rejected=false;try { BridGHe.Common.Checks.CompositeSectionFactory.Create(1000,200,concrete,null,100,50,null,100,h,steel,50,10,"Invalid"); }catch(NotSupportedException){rejected=true;}
        Assert(rejected,"vertical offset is not reinterpreted horizontally");
        var invalidForce=new GPC.Model.Results.ResultBeamForces(0,0,0,1000,0,0,cs);
        rejected=false;try {checker.GetLinearAnalysisResultByStages(out _,out _,out _,out _,0,0,invalidForce,zero,zero,true);}catch(NotSupportedException){rejected=true;}
        Assert(rejected,"unsupported torsion rejected, not discarded");
        var exportedAxes = new GPC.Geometry.CoordinateSystem(GPC.Geometry.Point3d.Origin, new GPC.Geometry.Vector3d(-1,0,0), new GPC.Geometry.Vector3d(0,-1,0));
        var diagram = new BeamForceResultModel(new LoadCaseModel("Bending"),0,0,"") { My=1e6 };
        var converted = BridGHe.Common.Checks.BridgeForceConvention.FromStraus(diagram,exportedAxes);
        Assert(converted.ToGlobalCoordinateSystem().M1==1e6 && converted.M1==-1e6,"Straus plane-2 moment rotated into checker destination axes");
        checker.GetLinearAnalysisResultByStages(out var bending,out _,out _,out _,0,0,converted,zero,zero,true);
        Assert(Math.Abs(bending.Point5_SteelSigma+1e6*150/h.Jxx)<1e-8 && Math.Abs(bending.Point8_SteelSigma-1e6*150/h.Jxx)<1e-8,"positive sagging moment compresses top and tensions bottom");
    }

    private static void CheckPhaseAssembly()
    {
        LoadCaseModel Case(string name, LoadCaseModel.Stage.BridgePhases phase)
        {
            var load=new LoadCaseModel(name,name); load.Stages.Add(new LoadCaseModel.Stage(name) { BridgePhase=phase });return load;
        }
        var steel=Case("S",LoadCaseModel.Stage.BridgePhases.OnlySteel);
        var infinite=Case("I",LoadCaseModel.Stage.BridgePhases.NInfinite);
        var instant=Case("Q",LoadCaseModel.Stage.BridgePhases.NInstant);
        var shrink=Case("R",LoadCaseModel.Stage.BridgePhases.StrainInfinite);
        var combo=new LoadCombinationModel("Combination");
        combo.Values.Add(steel,1);combo.Values.Add(infinite,2);combo.Values.Add(instant,3);combo.Values.Add(shrink,2);
        var beam=new ElementModel();
        beam.Results.Add(new BeamForceResultModel(steel,0,0,""){AxialForce=10});
        beam.Results.Add(new BeamForceResultModel(infinite,0,0,""){AxialForce=100});
        beam.Results.Add(new BeamForceResultModel(instant,0,0,""){AxialForce=1000});
        beam.Loads.Add(new CompositeBeamSlabStrainModel(shrink,new CompositeBeamSlabStrainModel.LoadData{Strain=-0.0001}));
        beam.Loads.Add(new CompositeBeamSlabStrainModel(shrink,new CompositeBeamSlabStrainModel.LoadData{Strain=-0.0002}));
        var result=BridGHe.Common.Checks.BridgePhaseAssembler.Assemble(beam,1,0,combo,GPC.Geometry.CoordinateSystem.Global,1e-8);
        Assert(result.Steel.N==10 && result.Infinite.N==200 && result.Instant.N==3000,"phase assembly keeps instantaneous and long-term actions distinct");
        Assert(Math.Abs(result.StrainInfinite+0.0006)<1e-12 && result.StrainInstant==0,"shrinkage contributions accumulate in the correct phase");
        bool rejected=false;try {BridGHe.Common.Checks.BridgePhaseAssembler.Assemble(beam,1,0.5,combo,GPC.Geometry.CoordinateSystem.Global,1e-8);}catch(InvalidOperationException){rejected=true;}
        Assert(rejected,"missing station results fail instead of silently adding zero forces");
    }
}

