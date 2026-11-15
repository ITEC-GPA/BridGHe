using System;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using BridGHe.Common.Helpers;
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
            // Rhino 8.36 uses this Windows implementation. The netcore root DLL
            // is a throwing stub; loading it changes HostUtils.DeviceId and can
            // invalidate the Cloud Zoo lease of the interactive Rhino session.
            System.Runtime.Loader.AssemblyLoadContext.Default.LoadFromAssemblyPath(
                @"C:\Program Files\Rhino 8\System\netcore\runtimes\win\lib\net7.0\System.Management.dll");
            RhinoInside.Resolver.Initialize(@"C:\Program Files\Rhino 8\System");
            if(args.Contains("--regression") && args.SkipWhile(a=>a!="--regression").Skip(1).FirstOrDefault()=="bridge-report")
                return RunBridgeReport();
            // gPC section bench on a capture: no Rhino host, no Straus.
            if(args.Contains("--gpc-bench")) return GpcSectionBench.Run(args);
            // F6A: legacy FEM inputs -> GPC -> Converter .st7, compared with the former exporter (no Rhino host).
            if(args.Contains("--legacy-export")) return LegacyExportComparison.Run(args);
            if(args.Contains("--legacy-results-compare")) return LegacyResultsComparison.Run(args);
            if(args.Contains("--legacy-results-reader-check")) return LegacyResultsReader.Check(args);
            if(args.Contains("--c5-describe")) return C5WorkflowComparison.Describe(args.SkipWhile(a=>a!="--c5-describe").Skip(1).First());
            if(args.Contains("--c5-compare")) return C5WorkflowComparison.Compare(args);
            if(args.Contains("--st7-beam")) return St7BeamProbe.Run(args);
            if(args.Contains("--legacy-sections")) return LegacySectionDump.Run(args.SkipWhile(a=>a!="--legacy-sections").Skip(1).First());
            if(args.Contains("--legacy-capture-compare")) return LegacyNativeExamplesComparison.CompareCapture(args.SkipWhile(a=>a!="--legacy-capture-compare").Skip(1).First());
            if(args.Contains("--legacy-examples-compare")) return LegacyNativeExamplesComparison.Compare(args.SkipWhile(a=>a!="--legacy-examples-compare").Skip(1).First());
            // P2CUT (13/11): the draft check host is retired with the FEM values it compared with (no GH type attaches a datum to a FEM value).
            return Run(args);
        }
        catch(Exception ex) { Console.Error.WriteLine(ex);return 1; }
        finally { ShutdownDispatcher(); }
    }
    /// <summary>
    /// Exit hang of 4/11 (example 50 native, C5 with --rhino): at process exit the finalizer runs SystemEvents.Shutdown, which joins the
    /// SystemEvents window thread; that thread was raising a system event through DispatcherSynchronizationContext.Send to the WPF
    /// dispatcher of this thread (created by the Rhino host), which no longer pumps while this thread waits for the exit handlers.
    /// Shutting the dispatcher down before leaving Main makes such a Send return at once. Test runner only; no dispatcher, nothing done.
    /// </summary>
    private static void ShutdownDispatcher()
    {
        try
        {
            var type = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == "WindowsBase")?.GetType("System.Windows.Threading.Dispatcher");
            var dispatcher = type?.GetMethod("FromThread", new[] { typeof(System.Threading.Thread) })?.Invoke(null, new object[] { System.Threading.Thread.CurrentThread });
            if (dispatcher != null) type.GetMethod("InvokeShutdown", Type.EmptyTypes)?.Invoke(dispatcher, null);
        }
        catch (Exception ex) { Console.Error.WriteLine("Dispatcher shutdown: " + ex.Message); }
        Console.Out.Flush();
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
        string childMode = args.FirstOrDefault(SmokeChildProcess.IsChildMode);
        if (childMode != null)
            return SmokeChildProcess.RunHost(childMode, args.SkipWhile(a => a != childMode).Skip(1).First());
        // --pure does not start Rhino, useful for CI and unlicensed environments.
        using var rhino=args.Contains("--pure")?null:new RhinoCore(new[]{"/nosplash"},WindowStyle.Hidden);
        if(args.Contains("--gpc-pilot")) { Console.WriteLine("RETIRED BY NAME (P2CB): --gpc-pilot: "+P2rRetiredP2cb.Reason("native|gpc-pilot")); return 1; } // P2CB (13/11): the F4 pilot retired with its FEM types
        GpcDomainCache.Configure();
        if(args.Contains("--import-dump")) return ImportDump.Run(args);
        if(args.Contains("--build-model-scan")) return BuildModelScan.Run(args);
        if(args.Contains("--material-scan")) return GpcMaterialKindChecks.Scan(args);
        if(args.Contains("--gpc-only")) return GpcOnlyReplay.Regression(args.SkipWhile(a=>a!="--gpc-only").Skip(1).First()); // R6.5 P1 (P2S)
        if(args.Contains("--legacy-payload-scan")) return GpcDraftArchiveChecks.PayloadScan(args);
        if(args.Contains("--composite-texts")) { Console.WriteLine("RETIRED BY NAME (P2CD): --composite-texts: "+P2rRetiredP2cd.Reason("tool|--composite-texts")); return 1; } // P2CD (13/11): the P2X evidence of the FEM composite texts retired
        if(args.Contains("--inr-scan")) return InrInertia.Scan(args); // lane INR (12/11): the homogenised composite properties of the examples on the section axes (measurement, not native)
        if(args.Contains("--migration-scale")) return MigrationScale.Run();
        if(args.Contains("--tidy-example")) return ExampleTidy.Run(args.SkipWhile(a=>a!="--tidy-example").Skip(1).FirstOrDefault(),args.Contains("--publish"),args.Contains("--capture"));
        if(args.Contains("--migrate-bridge-examples")) return BridgeExampleMigration.Run(args.SkipWhile(a=>a!="--migrate-bridge-examples").Skip(1).FirstOrDefault(a=>!a.StartsWith("--")),args.Contains("--publish"));
        if(args.Contains("--gpc-results-native")) return GpcResultsFileNative.Run();
        if(args.Contains("--gpc-only-analysis-native")) return GpcOnlyAnalysisNative.Run(); // L3e: targeted (the same cases run under --core-only --native)
        if(args.Contains("--c3")) return C3Checks.Native(); // C3: materials readback, native-section preview
        if(args.Contains("--c1")) return args.Contains("--native") ? GpcReviewFixesC1Native.Run() : GpcReviewFixesC1.Scan(args.SkipWhile(a=>a!="--c1").Skip(1).First()); // C1: Table Material readback, BGHGPC1 scan
        if(args.Contains("--c2-parity-native")) return C2ParityNative.Run(); // C2: targeted (the same cases run under --core-only --native)
        if(args.Contains("--c4-parity-native")) return C4ParityNative.Run(); // C4: targeted (the same cases run under --core-only --native)
        if(args.Contains("--c5-parity-native")) return C5ParityChecks.Native(); // C5: targeted (the same cases run under --core-only --native)
        if(args.Contains("--p2d-remake-examples")) return P2dExampleRemake.Run(args.Contains("write")); // P2D: 24, 42, 43, 47, 48 without FemmR6 (U2)
        if(args.Contains("--u2-remake-examples")) return U2ExampleRemake.Run(args); if(args.Contains("--u2-check-examples")) return U2ExampleRemake.Check(args); if(args.Contains("--u2-check-drafts")) return U2ExampleRemake.Drafts(args); // U2 (13/11): the 7 examples (9 definitions) remade on GPC data, and their outputs compared
        if(args.Contains("--p2d-save-reopen")) { var r=args.SkipWhile(a=>a!="--p2d-save-reopen").Skip(1).ToArray(); return P2dSaveReopen.Run(r[0],r[1],r.Length>2&&!r[2].StartsWith("--")?r[2]:null); } // P2D: measures of P2 (native)
        if(args.Contains("--phase-examples-native")) return NativePhaseExamples.Run(args.SkipWhile(a=>a!="--phase-examples-native").Skip(1).FirstOrDefault(a=>!a.StartsWith("--")));
        if(args.Contains("--legacy-beam-results")) { var r=args.SkipWhile(a=>a!="--legacy-beam-results").Skip(1).ToArray(); return LegacySectionDump.BeamResults(r[0],r[1],r.Length>2?r[2]:null); }
        if(args.Contains("--modular-bridge-native")) return NativeModularBridgeTests.Run(args.SkipWhile(a=>a!="--modular-bridge-native").Skip(1).FirstOrDefault());
        if(args.Contains("--modular-layout-capture")) return Example50HorizontalLayout.CaptureModular();
        if(args.Contains("--example99")) { Console.WriteLine("RETIRED BY NAME (P2CB): --example99: "+P2rRetiredP2cb.Reason("generator|--example99")); return 1; } // P2CB (13/11): the generator of example 99 retired (E4)
        if(args.Contains("--combination-excel-read")) { BridgeCombinationExcelTests.ReadExternal(args.SkipWhile(a => a != "--combination-excel-read").Skip(1).First()); return 0; }
        if(args.Contains("--capture-bridge-baseline")) { BridgeNumericalBaseline.Capture();return 0; }
        if(args.Contains("--benchmark-plots")) { PlotTests.Run();PlotTests.Benchmark();return 0; }
        if(args.Contains("--centrifugal-probe")) return MovingLoadBenchmarks.CentrifugalProbe(args.SkipWhile(a=>a!="--centrifugal-probe").Skip(1).FirstOrDefault(a=>!a.StartsWith("--")));
        if(args.Contains("--moving-load-tests")) return MovingLoadBenchmarks.Run(args.Contains("--native"),args.SkipWhile(a=>a!="--moving-load-tests").Skip(1).FirstOrDefault(a=>!a.StartsWith("--"))??"all");
        if(args.Contains("--example-layout")) return ExampleLayoutTests.Run(args.SkipWhile(a=>a!="--example-layout").Skip(1).FirstOrDefault(a=>!a.StartsWith("--")), args.Contains("--refresh-contracts"), args.Contains("--solve-portable"));
        if(args.Contains("--utility-examples")) { BridgeUtilityTests.Run(); BridgeUtilityExamples.Run(true); return 0; }
        if(args.Contains("--combination-excel-example")) { BridgeCombinationExcelExample.Run(true); return 0; }
        if(args.Contains("--bridge-workflow")) return BridgeWorkflowExample.Run(args.Contains("--native"));
        if(args.Contains("--inventory")) { CheckGrasshopper(false,true);return 0; }
        if(args.Contains("--components-only")) { CheckGrasshopper(false);return 0; }
        if(args.Contains("--example50-horizontal")) return Example50HorizontalLayout.Run();
        if(args.Contains("--example50-rev001-inputs")) { Console.WriteLine("RETIRED BY NAME (P2CD): --example50-rev001-inputs: "+P2rRetiredP2cd.Reason("generator|--example50-rev001-inputs")); return 1; } // P2CD (13/11): the authoring tool of 50 REV001 retired (E4)
        if(args.Contains("--example50-rev002")||args.Contains("--example50-rev002-check")) { Console.WriteLine("RETIRED BY NAME (P2CB): --example50-rev002: "+P2rRetiredP2cb.Reason("generator|--example50-rev002")); return 1; } // P2CB (13/11): the generator of 50 REV002 retired (E4)
        if(args.Contains("--example50-rev003")) return BridgeMovingRevisionExample.Run(args);
        if(args.Contains("--saved-bridge-audit")) return SavedBridgeWorkflowAudit.Run();
        if(args.Contains("--prepare99-rev004")) return SavedBridgeWorkflowAudit.Run(true);
        if(args.Contains("--bridge-details-plot-viewer")) { BridgeDetailsModelExample.GeneratePlotViewer(); return 0; }
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
        // P2CUT (13/11): the solid circle of the Section components on the GPC path (GpcSections.Standard), the same analytic values as the
        // former FEM section; the FEM archive, export and copy checks are retired by name with their types (P2rRetired.Checks).
        var section=(GPC.Model.Sections.Section)BridGHe.Common.Gpc.GpcSections.Standard(BridGHe.Common.Gpc.BridgeSectionKind.SolidCircle,"Roundtrip",20,0,0,0,0,0,0,0);
        Assert(Math.Abs(section.Area-Math.PI*100)<1e-10,"circle analytical area");
        Assert(Math.Abs(section.Jxx-Math.PI*10000/4)<1e-8,"circle analytical inertia");
        var path=new MovingPath { LoadCaseName="Traffic", Coordinates=new double[]{0,0,0,10,0,0,0,1,0},Template=new MovingTemplate { Vehicles=new[]{new MovingVehicle { Loads=new MovingObject[]{new MovingPointForce { Force=new double[]{0,0,-100} }} }} }};
        path.Validate();
        var allLoads=Enumerable.Range(0,5).Select(i=>(MovingObject)new MovingDistributedForce { Mobility=i,X1=0,X2=5,Y1=-1,Y2=1,Force=new double[]{0,0,-10} })
            .Concat(Enumerable.Range(0,2).Select(i=>(MovingObject)new MovingPointForce { Mobility=i,Force=new double[]{0,0,-100} }))
            .Append(new MovingHeatSource { SizeX=1,SizeY=1,Heat=100 }).ToArray();
        var allTemplate=new MovingTemplate { LaneFactors=new[]{1.0,0.8},Centrifugal=new MovingCentrifugal { ForceUnit=6 },Vehicles=new[]{new MovingVehicle { Lanes=new[]{1,2},Loads=allLoads }} };
        allTemplate.Validate();var allCopy=(MovingTemplate)allTemplate.Clone();
        Assert(allCopy.Vehicles[0].Loads.Length==8 && allCopy.LaneFactors[1]==0.8,"all API moving load modes roundtrip");
        var movingCopy=(MovingPath)path.Clone();movingCopy.Template.Vehicles[0].Loads[0]=new MovingHeatSource { SizeX=1,SizeY=1 };
        Assert(path.Template.Vehicles[0].Loads[0] is MovingPointForce,"moving graph deep copy");
        CheckBridge();
        CheckPhaseAssembly();
        Console.WriteLine($"RETIRED BY NAME (P2CUT): {P2rRetired.Checks.Count(c=>c.Key.StartsWith("core|",StringComparison.Ordinal))} core checks of FEM types (P2rRetired.Checks)");
        if(!args.Contains("--pure"))
        {
        CheckGrasshopper(args.Contains("--installed-plugins"));
        if (!args.Contains("--core-only") && RegressionTests.Run("all") != 0) return 1;
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
        P2fChecks.GooArchives(assembly,Assert); // P2F (12/11): on the GPC goo; the FEM fixture of CheckGooArchives is no longer written (P2D)
    }

    private static void CheckNative()
    {
        CheckNativeSections();
        CheckNativeCantilever();
        CheckNativePlateAngle();
        CheckNativeGpcOnlySections();
        Assert(ParityNative.Run()==0,"PAR (R6.5 P1): T, C, angle and Brep sections, cutoff bar and shell table written through the GPC path as BridGHe 0.3.0 wrote them");
        Assert(GpcOnlyAnalysisNative.Run()==0,"L3E (R6.5 P1): analysis and Export on GPC-only input; A05, A06 and A09 written as BridGHe 0.3.0 wrote them");
        Assert(GpcReviewFixesC1Native.Run()==0,"C1 (11/11): Table Material with α and ρ written to Straus7 with its table, α and ρ");
        Assert(C2ParityNative.Run()==0,"C2 (P26-01...07): labels, plate properties, cases solved, solver settings and combinations of Straus7 as BridGHe 0.3.0");
        Assert(C3Checks.Native()==0,"C3 (11/11): materials with explicit G, damping, thermal data or a table written through the GPC-only path as 0.3.0 wrote them; preview of the native sections");
        Assert(C4ParityNative.Run()==0,"C4 (11/11): rotated sections with supports, gradients and taper, moving cases of an ordinary run and solver 1 as BridGHe 0.3.0");
        Assert(C5ParityChecks.Native()==0,"C5 (11/11): releases of rotated beams (angle, double angle, Phase Model) as BridGHe 0.3.0");
        Assert(RegressionTests.Run("p2k-components")==0,"P8 (P2K): cable refused before any Straus7 call"); // R6 (P2CUT): the probe of P2K in core-native
    }

    /// <summary>P2Z (R6.5 P1 lane 2a): the Straus7 section components replayed in memory with the switch of the property producers, Straus7
    /// computing the sections (the native part of the regression gpc-only-properties, <see cref="GpcOnlyReplay.NativeSections"/>).</summary>
    private static void CheckNativeGpcOnlySections()
    {
        Assert(GpcOnlyReplay.NativeSections()==0,"Straus7 section components with the property producers on GPC only: the recipe alone, the same outputs modulo GUIDs and messages, no FEM value");
    }

    /// <summary>P2X (user decision of 8/11): the plate local axis angle natively, Plate Results equal to BridGHe 0.3.0 (<see cref="PlateAngleNative"/>).</summary>
    private static void CheckNativePlateAngle()
    {
        string folder=Path.Combine("artifacts","native",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
        var (rows,errors)=PlateAngleNative.Solve(Path.GetFullPath(Path.Combine(folder,"plate-angle.st7")));
        PlateAngleNative.Write(Path.Combine(folder,"plate-results.tsv"),rows);
        Assert(errors.Length==0,"plate angle native: no error ("+string.Join(" | ",errors)+")");
        double worst=PlateAngleNative.Compare(PlateAngleNative.Read(PlateAngleNative.Reference),rows,out int values,out int bent);
        Console.WriteLine($"PLATE ANGLE NATIVE: {values} values of Plate Results, worst relative difference from 0.3.0 {worst:R} (criterion of passo 1, 1e-9); {bent} of them Force Von Mises with transverse shears (P2Y: the 0.3.0 value, compared)");
        Assert(worst<=1e-9 && bent>0,"plate local axis angle: Plate Results of the turned plates equal to 0.3.0, Force Von Mises of the bent plates included");
        // P2Y: the combination "Both" (1.0 Pressure + 0.5 Prestress) against its 0.3.0 reference, Force Von Mises of the combined tensor with its shears.
        var (combined,combinedErrors)=PlateAngleNative.Solve(Path.GetFullPath(Path.Combine(folder,"plate-angle-combination.st7")),combination:true);
        PlateAngleNative.Write(Path.Combine(folder,"plate-results-combination.tsv"),combined);
        Assert(combinedErrors.Length==0,"plate angle native, combination: no error ("+string.Join(" | ",combinedErrors)+")");
        double worstCombined=PlateAngleNative.Compare(PlateAngleNative.Read(PlateAngleNative.CombinationReference),combined,out int combinedValues,out int combinedBent);
        Console.WriteLine($"PLATE ANGLE NATIVE (combination): {combinedValues} values of Plate Results, worst relative difference from 0.3.0 {worstCombined:R}; {combinedBent} Force Von Mises with transverse shears");
        Assert(worstCombined<=1e-9 && combinedBent>0,"plate Force Von Mises of a combination with transverse shears equal to 0.3.0 (Straus7 combined value)");
        var (warped,warpedErrors)=PlateAngleNative.Solve(Path.GetFullPath(Path.Combine(folder,"plate-angle-warped.st7")),true);
        PlateAngleNative.Write(Path.Combine(folder,"plate-results-warped.tsv"),warped);
        Console.WriteLine($"PLATE ANGLE NATIVE (warped): {warped.Count} rows of Plate Results; errors: {string.Join(" | ",warpedErrors)}");
        Assert(warpedErrors.Length==0 && warped.Count==PlateAngleNative.Read(PlateAngleNative.Reference).Count,"plate local axis angle on a quadrangle out of plane (within the planarity of GPC): solved, as 0.3.0");
    }

    private static void CheckNativeSections()
    {
        var previews=new System.Collections.Generic.List<object>();
        foreach(var shape in StrausSectionCatalog.All)
        {
            double Dimension(string n) => n.StartsWith("R")||n.StartsWith("alpha")?0:n=="D"?400:n=="B2"?100:n.StartsWith("B")?200:n=="L"?20:10;
            // A bulb with zero toe radius and slope is degenerate; use a finite bulb.
            var dimensions=shape.Key=="BglBulb" ? new double[]{200,50,10,10,0,5,0,0,30} : shape.Dimensions.Select(Dimension).ToArray();
            // P2CUT (13/11): the section as the GPC path resolves it (GpcNativeSections, the Straus7 Section components and Beam Property): the
            // reference properties and the display mesh of Straus7; the FEM solid preview and FEM archive checks are retired by name (P2rRetired.Checks).
            var resolved=BridGHe.Common.Gpc.GpcNativeSections.Resolve(shape.Key,dimensions,null,null,null,1);
            var data=resolved.Section.ReferenceProperties;
            Assert(data[0]>0 && data[1]>0 && data[2]>0,"native section "+shape.Key);
            Assert(resolved.Faces.Length>0 && resolved.Vertices.Length>0,"native section boundary "+shape.Key);
            double meshArea=resolved.Faces.Sum(f=>Math.Abs(Enumerable.Range(0,f.Length).Sum(k=>{var p=resolved.Vertices[f[k]];var q=resolved.Vertices[f[(k+1)%f.Length]];return p[0]*q[1]-q[0]*p[1];}))/2);
            Console.WriteLine($"PREVIEW {shape.Key}: A={meshArea:G17}, native={data[0]:G17}, points={resolved.Vertices.Length}");
            Assert(Math.Abs(meshArea/data[0]-1)<.03,"native preview area within 3 percent of exact area "+shape.Key);
            previews.Add(new { shape.Key,SectionArea=data[0],resolved.Vertices,resolved.Faces });
            if(shape.Key=="BglRectangle") Assert(Math.Abs(data[0]-11600)<1e-6,"BGL rectangular hollow analytical area");
        }
        Directory.CreateDirectory("artifacts/native-preview");
        File.WriteAllText("artifacts/native-preview/sections.json",System.Text.Json.JsonSerializer.Serialize(previews));
    }

    private static void CheckNativeCantilever()
    {
        // One Euler-Bernoulli beam, units N-mm, no self weight. Native test only after a successful St7Init.
        // P2CUT (13/11): on GPC (the builders of the components and Build Model on GPC, GpcNativeExamples.Beam: the same material, section and
        // restraint as the former FEM model), the GPC-only analysis and the GPC result readers.
        const double length=3000, force=1000, modulus=210000, inertia=450000000;
        var b=GpcNativeExamples.Beam("p2cut-cantilever");b.Force(b.Nodes.Count-1,"P",0,0,-force);
        var input=b.Build("Cantilever").Input;
        string folder=Path.Combine("artifacts","native",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
        var result=new GpcNativeExamples.Results(StrausAnalysis.Run(input,Path.GetFullPath(Path.Combine(folder,"cantilever.st7")),0,new[]{"P"},false,out _));
        double actual=result.Displacement(result.NodeCount-1,"P")[2];
        double expected=-force*Math.Pow(length,3)/(3*modulus*inertia);
        Assert(Math.Abs(actual-expected)<=Math.Abs(expected)*1e-5,"native cantilever tip displacement FL^3/(3EI)");
        Assert(Math.Abs(Math.Abs(result.Force(0,"P").Bending2)-force*length)<=force*length*1e-5,"native plane-2 cantilever moment FL");
        Assert(input.Model.AllElements.Sum(e=>e.Results.Count)+input.Model.Datasets.Count==0,"native analysis does not mutate input results");
        CheckNativeSectionAnalysis();
        NativeExamples.CheckMovingLibrary();
    }

    /// <summary>F6A: a native Straus7 section in a kN-m model goes through the GPC Converter (re-resolved in mm from the model-unit
    /// dimensions), with the analytic axial and bending response; a second run on the same path replaces the former files.
    /// Materials are N-mm (MPa) in every BridGHe model, as the former exporter assumed; the results come back in the model units (m, kN),
    /// as before F6 (C4: the GPC N-mm results are converted back; the earlier test gave E in kPa and read the N-mm results, two errors
    /// of 1000 that cancelled).</summary>
    private static void CheckNativeSectionAnalysis()
    {
        // P2CUT (13/11, R6): on GPC. The Straus7 section of the Section component in the definition units (kN-m: dimensions in m), the Beam
        // Property and the element builders of the components, Build Model on GPC in kN-m (drafts in the definition units, normalised by the
        // assembly), the GPC-only analysis and the GPC result readers (the rows of the Results components, in the definition units).
        const double length=3, force=10, modulus=2.1e8, b=0.2, d=0.4; // modulus in kN/m2, the model units
        var native=BridGHe.Common.Gpc.GpcNativeSections.Resolve("RectangleSolid",new[]{b,d},null,null,null,1).Section.ReferenceProperties; // as given (former Calculate)
        double area=native[0],i11=native[1],i22=native[2],shearA1=native[6],shearA2=native[7];
        Assert(Math.Abs(area-b*d)<=b*d*1e-12,"native section in model units: area B*D");
        Guid Id(string what)=>BridGHe.Common.Gpc.GpcIdentity.Derive(BridGHe.Common.Gpc.GpcIdentity.NameSpace,"p2cut-native-section:"+what);
        var material=BridGHe.Common.Gpc.Builders.GpcMaterialBuilders.Material(new BridGHe.Common.Gpc.Legacy.LegacyMaterialRecipe { Name="S355",Modulus=modulus/1000,PoissonRatio=0.3 }); // MPa
        var property=BridGHe.Common.Gpc.Builders.GpcPropertyBuilders.Beam("NativeRect",5,BridGHe.Common.Gpc.Builders.GpcSectionBuilders.Native("RectangleSolid",new[]{b,d},null,null,null,0),material);
        var loadCase=BridGHe.Common.Gpc.Builders.GpcCaseBuilders.Case("P",false);
        var first=BridGHe.Common.Gpc.Builders.GpcNodeBuilders.WithRestraint(BridGHe.Common.Gpc.Builders.GpcNodeBuilders.Create(0,0,0,"N1",null,Id("n1")),true,true,true,true,true,true,Id("r1"));
        var end=BridGHe.Common.Gpc.Builders.GpcNodeBuilders.WithForce(BridGHe.Common.Gpc.Builders.GpcNodeBuilders.Create(length,0,0,"N2",null,Id("n2")),loadCase,force,0,-force,Id("f2"));
        var beam=BridGHe.Common.Gpc.Builders.GpcBeamBuilders.New(first.Node.Position,end.Node.Position,property,null,0,0,0,Id("b1"),"1");
        var input=BridGHe.Common.Gpc.Legacy.LegacyBuildModel.Build("native-section",new BridGHe.Common.Gpc.BridgeUnits(BridGHe.Common.Gpc.DefinitionUnits.Knm),1e-6,
            new System.Collections.Generic.List<(Guid,BridGHe.Common.Gpc.Legacy.LegacyNodeDraft)>{(first.Node.Guid,first),(end.Node.Guid,end)},
            new System.Collections.Generic.List<(Guid,BridGHe.Common.Gpc.Legacy.LegacyBeamDraft)>{(beam.Beam.Guid,beam)},
            new System.Collections.Generic.List<(Guid,BridGHe.Common.Gpc.Legacy.LegacyPlateDraft)>(),
            new System.Collections.Generic.List<BridGHe.Common.Gpc.Legacy.LegacyCombinationDraft>(),new System.Collections.Generic.List<BridGHe.Common.Gpc.Legacy.LegacyStageDraft>());
        string folder=Path.Combine("artifacts","native",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
        string path=Path.GetFullPath(Path.Combine(folder,"native-section.st7"));
        for(int run=0;run<2;run++)
        {
            var analysed=StrausAnalysis.Run(input,path,0,new[]{"P"},false,out _);
            var result=new GpcNativeExamples.Results(analysed);
            var u=result.Displacement(1,"P");
            // R6.6 R0 (replaces the C4 check against the FEM results): Node, Beam and Plate Results on the GPC-only model show the reader rows.
            // and the result consumers on the reader rows (ConsumersCheck, as ResultsComponentsCheck.Verify did on the former run).
            Assert(GpcNativeExamples.ComponentsCheck(input,analysed,"kN-m native section, run "+(run+1))>0 && ConsumersCheck.Verify(analysed,"kN-m native section, run "+(run+1),new[]{"P"},input,0)>0,
                "Results components on the GPC-only kN-m model: outputs equal to the GPC reader rows, run "+(run+1));
            Assert(Math.Abs(Math.Abs(result.Force(0,"P").Bending2)-force*length)<=force*length*1e-5,"kN-m results in model units: fixed-end moment FL [kN m], run "+(run+1));
            Assert(Math.Abs(result.BeamRows(0,"P").Max(r=>r.Station)-length)<=1e-9,"kN-m results in model units: stations [m], run "+(run+1));
            double axial=force*length/(modulus*b*d);
            // Vertical bending about a principal axis, with the shear deformation of the native shear areas (Timoshenko):
            // FL^3/(3EI) + FL/(G As), with the inertia and shear area pair that the native axes give.
            double shearModulus=modulus/(2*1.3);
            var candidates=new[]{(i11,shearA2),(i22,shearA1),(i11,shearA1),(i22,shearA2)}
                .Select(p=>force*Math.Pow(length,3)/(3*modulus*p.Item1)+force*length/(shearModulus*p.Item2)).ToArray();
            Assert(Math.Abs(u[0]-axial)<=axial*1e-5,"native section axial elongation FL/(EA), run "+(run+1));
            double match=candidates.OrderBy(c=>Math.Abs(-u[2]-c)).First();
            Assert(Math.Abs(-u[2]-match)<=match*1e-4,$"native section tip deflection FL^3/(3EI)+FL/(G As) {-u[2]:G9} vs {match:G9} (candidates {string.Join(", ",candidates.Select(c=>c.ToString("G9")))}), run "+(run+1));
            Assert(Math.Abs(i11*i22-b*d*d*d/12*d*b*b*b/12)<=1e-12*i11*i22*1e4,"native section inertias B*D^3/12 and D*B^3/12");
        }
        Assert(Directory.GetFiles(folder,"native-section.*").Length>=2,"rerun on the same path replaces the model and result files");
        var mapped=input.Model.BeamElements.Values.Single();
        Assert(Math.Abs(((GPC.Model.ElementProperties.ElasticBeamProperty)mapped.BeamProperty).Section.Area-b*d*1e6)<=b*d*1e6*1e-12,"GPC property from the resolved native section");
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
        checker.GetLinearAnalysisResultByStages(out var planar,out _,out _,out _,0,0,invalidForce,zero,zero,true);
        Assert(Math.Abs(planar.Point8_SteelSigma)<1e-10 && invalidForce.T==1000 && planar.Assumptions.Contains(BridGHe.Common.Checks.BridgeCheckAdapter.PlanarAssumption),"planar torsion exclusion is declared without mutating FEM forces");
        var exportedAxes = new GPC.Geometry.CoordinateSystem(GPC.Geometry.Point3d.Origin, new GPC.Geometry.Vector3d(-1,0,0), new GPC.Geometry.Vector3d(0,-1,0));
        // P2CUT (13/11): the Straus7 diagram as a merged sample of the GPC path (BM2 = 1e6), the same conversion as the former FEM result.
        var diagram = new BridGHe.Common.Checks.BeamForceSample(0,0,0,0,0,0,1e6);
        var converted = BridGHe.Common.Checks.BridgeForceConvention.FromStraus(diagram,"Bending",exportedAxes);
        Assert(converted.ToGlobalCoordinateSystem().M1==1e6 && converted.M1==-1e6,"Straus plane-2 moment rotated into checker destination axes");
        checker.GetLinearAnalysisResultByStages(out var bending,out _,out _,out _,0,0,converted,zero,zero,true);
        Assert(Math.Abs(bending.Point5_SteelSigma+1e6*150/h.Jxx)<1e-8 && Math.Abs(bending.Point8_SteelSigma-1e6*150/h.Jxx)<1e-8,"positive sagging moment compresses top and tensions bottom");
    }

    private static void CheckPhaseAssembly()
    {
        // P2CUT (13/11): the index of the GPC bridge model (merged samples, the phases of the cases from the recipe); the slab strains come
        // from the recipe on the GPC path (the index has none), so the strain check of the former FEM loads is retired by name (P2rRetired.Checks).
        static BridGHe.Common.Checks.BeamForceSample Axial(double n) => new BridGHe.Common.Checks.BeamForceSample(0,n,0,0,0,0,0);
        var index=BridGHe.Common.Checks.BridgePhaseAssembler.CreateIndex("B",new[]{("S",Axial(10)),("I",Axial(100)),("Q",Axial(1000))},1e-8);
        var phases=new System.Collections.Generic.Dictionary<string,BridGHe.Common.Checks.BridgeCasePhase>{["S"]=BridGHe.Common.Checks.BridgeCasePhase.OnlySteel,
            ["I"]=BridGHe.Common.Checks.BridgeCasePhase.NInfinite,["Q"]=BridGHe.Common.Checks.BridgeCasePhase.NInstant,["R"]=BridGHe.Common.Checks.BridgeCasePhase.StrainInfinite};
        var factors=new[]{("S",1.0),("I",2.0),("Q",3.0),("R",2.0)};
        var result=index.Assemble(0,factors,phases,GPC.Geometry.CoordinateSystem.Global);
        Assert(result.Steel.N==10 && result.Infinite.N==200 && result.Instant.N==3000,"phase assembly keeps instantaneous and long-term actions distinct");
        bool rejected=false;try {index.Assemble(0.5,factors,phases,GPC.Geometry.CoordinateSystem.Global);}catch(InvalidOperationException){rejected=true;}
        Assert(rejected,"missing station results fail instead of silently adding zero forces");
    }
}
