using System;
using System.Collections.Generic;
using System.Linq;
using BridGHe.Common.Helpers;
using Rhino.Geometry;

internal static class RegressionTests
{
    public static int Run(string selection)
    {
        BridgeMergeTests.Initialize(); // Register before the first GH document can trigger plugin discovery.
        var tests = new Dictionary<string, Action>
        {
            ["bridge-combination-excel"] = BridgeCombinationExcelTests.Run,
            ["replay-temporary-paths"] = ReplayTemporaryPaths.Regression,
            ["concrete-shrinkage"] = ConcreteShrinkageTests.Run,
            ["bridge-detail-stations"] = BridgeSupportReactionTests.Run,
            ["bridge-combination-excel-component"] = () => BridgeCombinationExcelExample.Run(),
            ["simple-bridge"] = SimpleBridgeTests.Run,
            ["modular-bridge"] = BridgeDefinitionTests.Run,
            ["bridge-moving-workflow"] = BridgeMovingWorkflowTests.Run,
            ["build-model-parameters"] = BuildModelParameterTests.Run,
            ["parametric-bridge"] = ParametricBridgeTests.Run,
            ["bridge-segment-sections"] = BridgeSegmentSectionsTests.Run,
            ["bridge-segment-excel"] = BridgeSegmentExcelTests.Run,
            ["bridge-alignment-inputs"] = BridgeAlignmentInputsTests.Run,
            ["bridge-segment-override"] = BridgeSegmentOverrideTests.Run,
            ["get-beam-property"] = GetBeamPropertyTests.Run,
            ["shared-properties"] = SharedPropertiesChecks.Run,
            ["beam-property-override"] = BeamPropertyOverrideTests.Run,
            ["bridge-moment-state"] = BridgeMomentStateTests.Run,
            ["bridge-segment-composite"] = BridgeSegmentCompositeTests.Run,
            ["bridge-deck-grillage"] = BridgeDeckGrillageTests.Run,
            ["reinforcement-material"] = ReinforcementMaterialTests.Run,
            ["gh-node-merge"] = GrasshopperNodeMergeTests.Run,
            ["wired-load-schema"] = WiredLoadSchemaTests.Run,
            ["bridge-span-advisor"] = BridgeSpanAdvisorTests.Run,
            ["bridge-assignment"] = BridgeAssignmentTests.Run,
            ["bridge-bearings"] = BridgeBearingTests.Run,
            ["bridge-connections"] = BridgeConnectionTests.Run,
            ["bridge-bracing"] = BridgeMeshTests.Bracing,
            ["bridge-deck"] = BridgeMeshTests.Deck,
            ["bridge-construction"] = BridgeConstructionTests.Run,
            ["bridge-detail-layout"] = BridgeDetailLayoutTests.Run,
            ["bridge-details-example"] = () => BridgeDetailsExample.Run(),
            ["bridge-deflection"] = BridgeDeflectionTests.Run,
            ["bridge-fatigue-history"] = BridgeFatigueTests.Run,
            ["bridge-check-map"] = BridgeCheckMapTests.Run,
            ["bridge-check-summary"] = BridgeCheckSummaryTests.Run,
            ["bridge-result-labels"] = BridgeResultLabelsTests.Run,
            ["bridge-report"] = BridgeReportTests.Run,
            ["bridge-report-component"] = () => BridgeReportExample.Run(),
            ["bridge-design-audit"] = BridgeDesignAuditTests.Run,
            ["bridge-design-components"] = () => BridgeDesignExamples.Run(),
            ["simple-bridge-components"] = () => SimpleBridgeExamples.Run(),
            ["bridge-utilities"] = BridgeUtilityTests.Run,
            ["bridge-utility-components"] = () => BridgeUtilityExamples.Run(),
            ["plot-preview"] = PlotPreviewTests.Run,
            ["bridge-workflow"] = BridgeWorkflowTests.Run,
            ["bridge-stations"] = BridgeStationTests.Run,
            ["component-layout"] = ComponentLayoutTests.Run,
            ["bridge-plots"] = PlotTests.Run,
            ["bridge-detail-plots"] = BridgeDetailPlotTests.Run,
            ["native-bridge-components"] = NativeBridgeComponentTests.Run,
            ["bridge-numerical-baseline"] = BridgeNumericalBaseline.Verify,
            ["bridge-linear-small-loads"] = BridgeLinearSmallLoadsTests.Run,
            ["composite-component"] = CompositeComponentTests.Run,
            ["slab-reinforcement"] = SlabReinforcementTests.Run,
            ["composite-property-phases"] = CompositePropertyTests.Run,
            ["display-settings"] = DisplayTests.Run,
            ["bridge-merge-origin"] = BridgeMergeTests.Run,
            ["bridge-history-workflow"] = BridgeHistoryWorkflowTests.Run,
            ["merge-mesh"] = MergeMesh,
            ["model-guids"] = ModelGuids,
            ["indexed-phases"] = IndexedPhases,
            ["bridge-planar-noise"] = BridgePlanarNoise,
            ["bridge-instant-modulus"] = BridgeInstantModulus,
            ["bridge-example-parameters"] = BridgeExampleParameters,
            ["bridge-detail-archives"] = BridgeDetailArchives,
            ["gpc-release-set"] = GpcReleaseSetTests.Run,
            ["rhino-child-lifecycle"] = RhinoChildLifecycleTests.Run,
            ["plugin-build-check"] = PluginBuildCheckTests.Run
        };
        // F6A: conversions to GPC (lane P2B). Inserted before the release-set guards, which require the pinned lib/GPC set.
        var gpc = new Dictionary<string, Action> { ["gpc-legacy-import"] = GpcLegacyImportTests.Run, ["gh-model-gpc"] = GhModelGpcChecks.Run, ["gpc-results-file"] = GhModelGpcChecks.ResultsFile, ["gpc-materials"] = GhModelGpcChecks.Materials, ["gpc-model-transforms"] = GpcInputComparison.Regression, ["gpc-composite-sections"] = GpcCompositeSectionChecks.Regression, ["gpc-state-override"] = GpcStateOverrideChecks.Regression, ["gpc-element-drafts"] = GpcElementDraftChecks.Regression, ["gpc-draft-merge"] = GpcDraftMergeChecks.Regression, ["gpc-build-model"] = GpcBuildModelChecks.Regression, ["gpc-material-kinds"] = GpcMaterialKindChecks.Regression, ["gpc-draft-archive"] = GpcDraftArchiveChecks.Regression, ["gpc-builders-a"] = GpcBuilderChecksA.Regression, ["gpc-builders-b"] = GpcBuilderChecksB.Regression, ["gpc-builders-c"] = GpcBuilderChecksC.Regression, ["gpc-result-selection"] = GpcResultSelectionChecks.Regression, ["gpc-analysis-drafts"] = GpcAnalysisDraftsChecks.Regression };
        gpc["gpc-null-values"] = GpcNullValueChecks.Regression; // R6.5 P0 (7/11): the GH types given by their GPC datum alone
        gpc["gpc-build-model-legacy"] = R3LegacyBuildModel.Regression; // R6.6 lane R3 (11/11): Build Model on the drafts of the 0.3.0 models read by value = the import of the reader (the FEM-input coverage of gpc-build-model)
        gpc["gpc-c3"] = C3Checks.Regression; // C3 (11/11): materials with analysis data and no table; preview of the GPC drafts (gaps 1 and 2 of R2a)
        gpc["palette-dynamic"] = PaletteDynamicChecks.Regression; // Task 145: dynamic/restored ports; no fixture changes
        gpc["palette-contract"] = PaletteContractChecks.Regression; // P2CI (14/11, user decision 3; tasks 56, 78): the palette and its ports against the frozen table
        gpc["p2d"] = P2dChecks.Regression; // P2D (12/11): format BGHGPC1 schema 1 (U1), frame rule (U6), preview of the homogenised composite property (U5), FemmR6 by value, slab strain (U4)
        gpc["r2b-merge-forces"] = R2bMergeForcesChecks.Regression; // R6.6 lane R2b (11/11): merge, reactions and Super Element Check forces on GPC data alone (explicit counterparts of the native GpcMergeCheck and GpcSecForcesCheck)
        gpc["inr-inertia"] = InrInertia.Regression; // lane INR (12/11, user decision option B): the homogenised composite property on the section axes, I11 about the horizontal axis
        gpc["p2k-components"] = P2kChecks.Regression; // P2K (12/11): slab datum, composite beam GUID, cable refused before Straus7, generators without FEM, report streams
        gpc["p2k-natives"] = P2kNative.Regression; // P2K (12/11): 50 and 99 natives without the transitional FEM checks, Bridge Report of the 50; only with BRIDGHE_P2K_NATIVE (0.4.2: 50 REV004 and 99 REV005; 50-rev003 and 99-rev004 by name)
        gpc["gpc-review-fixes"] = GpcReviewFixesC1.Regression; // C1 (11/11): the product fixes of the ChatGPT reviews (R65-C3-01/02, R25-01, P30-01/02/04, Table Material)
        gpc["gpc-c5"] = C5ParityChecks.Regression; // C5 (11/11): releases of rotated beams as 0.3.0 (write plan recorded)
        gpc["p2ce-reader"] = P2ceReader.Regression; // P2CE phase 2a (13/11): the 0.3.0 reader without FEM CLR types (inventory, by value = CLR for «LoadCases»; retired in 2b) and P2P-01 (stage case records)
        gpc["gpc-only-elements"] = GpcOnlyReplay.Elements; // R6.5 P1 lane 1a (7/11): the element components replayed with GPC-only inputs
        gpc["gpc-only-generators"] = GpcOnlyReplay.Generators; // R6.5 P1 lane 1b (7/11): the bridge generators replayed with GPC-only inputs and outputs
        gpc["gpc-only-properties"] = GpcOnlyReplay.Properties; // R6.5 P1 lane 2a (P2Z, 8/11): properties and data replayed with GPC-only inputs and outputs; L2B (8/11): with lane 2b (composite and bridge properties, State Override, coordinate systems)
        gpc["gpc-only-models"] = GpcOnlyReplay.Models; // R6.5 P1 lane 3a (L3a, 8/11): models, composite beams and workflow objects GPC-only; a gate step of its own (limit 5,400 s), not in regression all
        gpc["export-preparation"] = ExportPreparationChecks.Regression; // Task 118: analytical managed export boundary.
        // D6 of the final review of 0.4.1 (0.4.2): the non-native section groups of tasks 190, 198 and 209 (about 45 s together) in regression
        // all; until 0.4.1 they ran only by their own flags, which stay (--sections-190, --sections-190-open, --sections-198-oracle,
        // --sections-209-replay, --sections-209-warnings). The native --sections-190-native stays a gate step of its own, under the Straus7 lock.
        gpc["sections-190"] = () => Exit(UniqueSectionChecks.Run(false), "sections-190");
        gpc["sections-190-open"] = () => Exit(OpenSectionChecks190.Run(), "sections-190-open");
        gpc["sections-198-oracle"] = () => Exit(OpenSectionChecks190.Oracle198(), "sections-198-oracle");
        gpc["sections-209-replay"] = () => Exit(OpenSectionChecks190.Replay209(), "sections-209-replay");
        gpc["sections-209-warnings"] = () => Exit(OpenSectionChecks190.Warnings209(), "sections-209-warnings");
        tests = gpc.Concat(tests).ToDictionary(t => t.Key, t => t.Value);
        P2cfChecks.RegressionKeys(tests.Keys, selection == "all"); // P2CF (13/11, item 4): the regression keys of P2rRetired.Checks against the regressions
        if (selection != "all" && !tests.ContainsKey(selection))
            throw new ArgumentException(P2rRetired.Checks.TryGetValue("regression|" + selection, out var retired) ? "Regression retired by name (P2CUT): " + selection + ": " + retired : "Unknown regression: " + selection);
        if (selection == "all") Console.WriteLine($"RETIRED BY NAME (P2CUT): {P2rRetired.Checks.Count(c => c.Key.StartsWith("regression|", StringComparison.Ordinal))} regressions of FEM types (P2rRetired.Checks)");
        int count = 0, failed = 0;
        foreach (var test in tests.Where(t => selection == "all" || t.Key == selection))
        {
            if (selection == "all" && test.Key is "plot-preview" or "bridge-plots" or "bridge-report-component") { Console.WriteLine("SEPARATE HOST: " + test.Key + " (executed by tools/build.ps1 -Smoke)"); continue; }
            if (selection == "all" && test.Key == "gpc-only-models") { Console.WriteLine("SEPARATE GATE STEP: gpc-only-models (--regression gpc-only-models, limit 5,400 s)"); continue; } // L3a (coordinator decision 3, 8/11)
            Console.WriteLine("RUN REGRESSION: " + test.Key);
            try { test.Value(); }
            catch (Exception error)
            {
                // Report before RhinoCore.Dispose: the native host may otherwise terminate
                // while unwinding, hiding the managed failure and returning exit code zero.
                Console.Error.WriteLine("FAIL REGRESSION: " + test.Key + "\n" + error);
                Console.Error.Flush();
                // P2S (7/11): BRIDGHE_REGRESSION_CONTINUE=1 runs the rest and fails at the end (diagnostics; the gates stop at the first).
                if (Environment.GetEnvironmentVariable("BRIDGHE_REGRESSION_CONTINUE") != "1") return 1;
                failed++; continue;
            }
            count++; Console.WriteLine("PASS REGRESSION: " + test.Key);
        }
        Console.WriteLine($"REGRESSION TOTAL {count}" + (failed > 0 ? $", {failed} FAILED" : ""));
        return failed > 0 ? 1 : 0;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("REGRESSION FAILED: " + message);
    }

    /// <summary>D6 (0.4.2): a check run by its own flag returns an exit code; in a regression, a code other than 0 is a failure.</summary>
    private static void Exit(int code, string name) => Require(code == 0, name + " returned exit code " + code);

    private static void BridgeDetailArchives()
    {
        var assembly=System.Reflection.Assembly.LoadFrom(BuildPaths.GrasshopperAssembly);
        var type=assembly.GetType("BridGHe.Grasshopper.DataTypes.BridgeDetailGoo",true);
        object[] definitions={
            new GPC.Checkers.CompositeBridge.BridgeStudOptions{Enabled=true,CountPerRow=3,Diameter=22,LongitudinalPitch=180},
            new GPC.Checkers.CompositeBridge.BridgeIntermediateStiffener{Enabled=true,Layout=GPC.Checkers.CompositeBridge.BridgeStiffenerLayout.Unequal,Width=200,RightWidth=250,ExternalCompressionKN=123.4},
            new GPC.Checkers.CompositeBridge.BridgeSupportStiffener{Enabled=true,Location=GPC.Checkers.CompositeBridge.BridgeSupportLocation.RightEnd,ReactionKN=1234.5,LoadX=-20}
        };
        foreach(var value in definitions)
        {
            var goo=(Grasshopper.Kernel.Types.IGH_Goo)Activator.CreateInstance(type,new[]{value});
            var archive=new GH_IO.Serialization.GH_Archive();Require(archive.AppendObject(goo,"detail"),"detail serializes");
            var restored=(Grasshopper.Kernel.Types.IGH_Goo)Activator.CreateInstance(type);
            Require(archive.ExtractObject(restored,"detail")&&restored.IsValid,"detail deserializes");
            var copy=type.GetProperty("Value").GetValue(restored);
            Require(copy.Equals(value),"all gPC scalar options and enums survive GH archive");
            Require(type.GetProperty("Value").GetValue(goo.Duplicate()).Equals(value),"immutable detail duplication");
        }
        var bad=new GH_IO.Serialization.GH_LooseChunk("bad");bad.SetString("Kind","ArbitraryType");bad.SetString("Data","{}");
        Require(!(bool)type.GetMethod("Read").Invoke(Activator.CreateInstance(type),new object[]{bad}),"unknown detail type rejected");
        using var document=new Grasshopper.Kernel.GH_Document();
        var studs=(Grasshopper.Kernel.GH_Component)Activator.CreateInstance(assembly.GetType("BridGHe.Grasshopper.Components.BridgeDetails.BridgeStudsComponent",true));
        studs.CreateAttributes();document.AddObject(studs,false);
        var count=(Grasshopper.Kernel.Parameters.Param_Number)studs.Params.Input[1];count.PersistentData.Clear();count.SetPersistentData(2.5);
        Grasshopper.Kernel.GH_Document.EnableSolutions=true;document.Enabled=true;document.NewSolution(false,Grasshopper.Kernel.GH_SolutionMode.Silent);
        Require(studs.RuntimeMessages(Grasshopper.Kernel.GH_RuntimeMessageLevel.Error).Any(m=>m.Contains("integer"))&&studs.Params.Output[0].VolatileData.AllData(true).All(g=>g==null||!g.IsValid),"fractional stud count rejected by real component");
        count.PersistentData.Clear();count.SetPersistentData(2);studs.ExpireSolution(false);document.NewSolution(false,Grasshopper.Kernel.GH_SolutionMode.Silent);
        Require(studs.RuntimeMessages(Grasshopper.Kernel.GH_RuntimeMessageLevel.Error).Count==0&&studs.Params.Output[0].VolatileDataCount==1,"valid stud definition recovers after invalid input");
    }

    private static void BridgeExampleParameters()
    {
        new BridgeGrillageExample.Parameters().Validate();
        new BridgeGrillageExample.Parameters{Panels=24}.Validate();
        new BridgeGrillageExample.Parameters{Panels=96}.Validate(refined:true);
        foreach(var p in new[]{new BridgeGrillageExample.Parameters{Panels=48},new BridgeGrillageExample.Parameters{Panels=13},new BridgeGrillageExample.Parameters{AxleSpacing=30000},new BridgeGrillageExample.Parameters{SlabThickness=double.NaN}})
        {
            bool rejected=false;try{p.Validate();}catch(ArgumentException){rejected=true;}
            Require(rejected,"invalid bridge recipe rejected before native execution");
        }
    }

    private static void BridgeInstantModulus()
    {
        const double es=210000,ec=32836.56803133079;
        Require(BridGHe.Common.Checks.BridgeCreepCoefficient.FromModularRatio(es/ec,es,ec)==0,"instantaneous modular ratio must yield exactly zero creep");
        Require(Math.Abs(BridGHe.Common.Checks.BridgeCreepCoefficient.FromModularRatio(3*es/ec,es,ec)-2)<1e-12,"long-term ratio yields phi=2");
        foreach(double n in new[]{.99*es/ec,0,-1,double.NaN,double.PositiveInfinity})
        {
            bool rejected=false;try{BridGHe.Common.Checks.BridgeCreepCoefficient.FromModularRatio(n,es,ec);}catch(ArgumentOutOfRangeException){rejected=true;}
            Require(rejected,"invalid physical modular ratio remains rejected");
        }
    }

    private static void BridgePlanarNoise()
    {
        var concrete = new GPC.Model.Materials.ConcreteMaterialEN1992("C30",30,GPC.Model.Materials.ConcreteMaterial.CompressionStressStrainDiagrams.ParabolaRectangle);
        var steel = new GPC.Model.Materials.SteelMaterialEN1992("S355",210000,355,510,.1,GPC.Model.Materials.SteelMaterial.StressStrainCurveType.ElasticPerfectPlastic,GPC.Model.Materials.SteelMaterial.SteelTypes.Structural);
        var section = BridGHe.Common.Checks.CompositeSectionFactory.Create(1000,200,concrete,null,100,40,null,100,new GPC.Model.Sections.SectionH(300,10,200,20,200,20,"H"),steel,40,0,"Test");
        var options = new GPC.Checkers.CompositeBridge.BridgeAnalysisOptions { Standard=GPC.Checkers.CompositeBridge.BridgeStandard.Eurocode4, LimitState=GPC.Checkers.CompositeBridge.BridgeLimitState.Rare };
        var checker = new BridGHe.Common.Checks.BridgeCheckAdapter(section,GPC.Geometry.CoordinateSystem.Global, options, ignoreSecondaryActions:false);
        double Analyze(double torque, double lateral=0, double minor=0)
        {
            var force = new GPC.Model.Results.ResultBeamForces(0,lateral,1000,torque,1e6,minor,GPC.Geometry.CoordinateSystem.Global);
            checker.GetLinearAnalysisResultByStages(out var _,out var _,out var result,out _,0,0,force,force,force,true);
            return result.Point8_SteelSigma;
        }
        double baseline=Analyze(0);
        Require(Math.Abs(Analyze(1e-5,1e-8,-1e-5)-baseline)<1e-10,"symmetric native roundoff does not reject or change planar stresses");
        foreach(var values in new[]{(T:.01,V:0.0,M:0.0),(T:0.0,V:.0001,M:0.0),(T:0.0,V:0.0,M:.01)})
        {
            bool rejected=false;try{Analyze(values.T,values.V,values.M);}catch(NotSupportedException){rejected=true;}
            Require(rejected,"real unsupported action remains rejected");
        }
        bool nonfinite=false;try{Analyze(double.NaN);}catch(ArgumentException){nonfinite=true;}
        Require(nonfinite,"nonfinite unsupported component cannot silently pass the domain guard");
        checker = new BridGHe.Common.Checks.BridgeCheckAdapter(section,GPC.Geometry.CoordinateSystem.Global, options);
        Require(Math.Abs(Analyze(1e9,3e6,-2e9)-baseline)<1e-10,"declared planar approximation ignores T, M2 and V1 without changing N-Mx-Vy stresses");
        nonfinite=false;try{Analyze(0,double.NaN);}catch(ArgumentException){nonfinite=true;}
        Require(nonfinite,"planar projection still rejects nonfinite excluded actions");
        var supplied=new GPC.Model.Results.ResultBeamForces(10,3e6,1000,1e9,1e6,-2e9,GPC.Geometry.CoordinateSystem.Global);
        checker.GetLinearAnalysisResultByStages(out var _,out var _,out var projected,out _,0,0,supplied,supplied,supplied,true);
        Require(supplied.T==1e9 && supplied.M2==-2e9 && supplied.V1==3e6,"planar approximation must not mutate supplied FEM forces");
        Require(projected.Assumptions.Contains(BridGHe.Common.Checks.BridgeCheckAdapter.PlanarAssumption) &&
            projected.WithSource(new("B",0,"C")).Assumptions.SequenceEqual(projected.Assumptions),"planar assumptions follow the result and its provenance");
    }

    private static void MergeMesh()
    {
        var mesh = new Mesh();
        mesh.Vertices.Add(0, 0, 0); mesh.Vertices.Add(10, 0, 0); mesh.Vertices.Add(0, 10, 0); mesh.Vertices.Add(10, 10, 0);
        mesh.Faces.AddFace(0, 1, 2); mesh.Faces.AddFace(1, 3, 2);
        var nodes = new List<Point3d> { new Point3d(0.0001, 0, 0) };
        var beams = new List<Curve>(); var meshes = new List<Mesh> { mesh };
        BridGHe.Common.Geometry.BridgeGeometryMerge.Merge(ref nodes, ref beams, ref meshes, 0.001); // P2CE: the product merge (the former ModelHelper.MergeGeometry, the same code)
        Require(meshes[0].Faces.Count == 2 && meshes[0].Vertices.Count == 4 && meshes[0].IsValid, "all adjacent faces survive vertex snapping");
        Require(meshes[0].Vertices.Point3dAt(0).DistanceTo(nodes[0]) < 1e-12, "existing canonical point is used");
        Require(mesh.Vertices.Point3dAt(0) == Point3d.Origin, "source mesh unchanged");
        Require(meshes[0].FaceNormals.All(n => n.Z > 0), "face winding preserved");
        var collapsed = new Mesh();
        collapsed.Vertices.Add(0, 0, 0); collapsed.Vertices.Add(0.0001, 0, 0); collapsed.Vertices.Add(0, 10, 0);
        collapsed.Faces.AddFace(0, 1, 2);
        nodes = new List<Point3d>(); meshes = new List<Mesh> { collapsed };
        BridGHe.Common.Geometry.BridgeGeometryMerge.Merge(ref nodes, ref beams, ref meshes, 0.001); // P2CE: the product merge (the former ModelHelper.MergeGeometry, the same code)
        Require(meshes[0].Faces.Count == 0, "only genuinely collapsed faces are removed");
    }

    private static System.Reflection.Assembly Plugin()
    {
        System.Reflection.Assembly.LoadFrom(@"C:\Program Files\Rhino 8\Plug-ins\Grasshopper\Grasshopper.dll");
        System.Reflection.Assembly.LoadFrom(@"C:\Program Files\Rhino 8\Plug-ins\Grasshopper\GH_IO.dll");
        return System.Reflection.Assembly.LoadFrom(BuildPaths.GrasshopperAssembly);
    }

    private static void IndexedPhases()
    {
        // P2CUT (13/11): the index of the GPC bridge model (merged samples at their stations in mm, the phase of each case from the recipe),
        // the same sums as the former index of the FEM results; the normalised stations of the FEM results do not exist on the GPC path.
        static BridGHe.Common.Checks.BeamForceSample Sample(double station, double n, double m1 = 0, double m2 = 0) => new BridGHe.Common.Checks.BeamForceSample(station, n, 0, 0, 0, m1, m2);
        var phases = new Dictionary<string, BridGHe.Common.Checks.BridgeCasePhase> { ["Q"] = BridGHe.Common.Checks.BridgeCasePhase.NInstant };
        var combination = new[] { ("Q", -2.0) };
        var samples = new List<(string, BridGHe.Common.Checks.BeamForceSample)> { ("Q", Sample(50, 12, 5, 7)), ("Q", Sample(20, 30)) };
        var index = BridGHe.Common.Checks.BridgePhaseAssembler.CreateIndex("B", samples, 0.125);
        var result = index.Assemble(50.125, combination, phases, GPC.Geometry.CoordinateSystem.Global);
        Require(result.Instant.N == -24 && result.Instant.M1 == -14 && result.Instant.M2 == 10, "indexed phases preserve coefficients, moment conversion and tolerance boundary");
        Require(index.Assemble(20, combination, phases, GPC.Geometry.CoordinateSystem.Global).Instant.N == -60, "absolute stations use the same index");
        bool missing = false;
        try { index.Assemble(50.25, combination, phases, GPC.Geometry.CoordinateSystem.Global); } catch (InvalidOperationException) { missing = true; }
        Require(missing, "outside-tolerance results rejected");
        samples.Add(("Q", Sample(50, 0)));
        index = BridGHe.Common.Checks.BridgePhaseAssembler.CreateIndex("B", samples, 0.125);
        bool duplicate = false;
        try { index.Assemble(50, combination, phases, GPC.Geometry.CoordinateSystem.Global); } catch (InvalidOperationException) { duplicate = true; }
        Require(duplicate, "duplicate station/case results remain an error");
        foreach (int stations in new[] { 100, 400, 800 })
        {
            var many = new List<(string, BridGHe.Common.Checks.BeamForceSample)>(); var combo = new List<(string, double)>(); var manyPhases = new Dictionary<string, BridGHe.Common.Checks.BridgeCasePhase>();
            for (int c = 0; c < 8; c++)
            {
                string load = "L" + c; manyPhases[load] = BridGHe.Common.Checks.BridgeCasePhase.NInstant;
                combo.Add((load, 1));
                for (int i = 0; i < stations; i++) many.Add((load, Sample(i, c + 1)));
            }
            var times = new List<double>(); long bytes = 0;
            for (int repeat = 0; repeat < 4; repeat++)
            {
                long before = GC.GetAllocatedBytesForCurrentThread(); var watch = System.Diagnostics.Stopwatch.StartNew();
                var lookup = BridGHe.Common.Checks.BridgePhaseAssembler.CreateIndex("B", many, 1e-6); double sum = 0;
                for (int c = 0; c < 10; c++) for (int i = 0; i < stations; i++) sum += lookup.Assemble(i, combo, manyPhases, GPC.Geometry.CoordinateSystem.Global).Instant.N;
                watch.Stop(); if (repeat > 0) times.Add(watch.Elapsed.TotalMilliseconds); bytes = GC.GetAllocatedBytesForCurrentThread() - before;
                Require(sum == stations * 10 * 36, "indexed benchmark analytical checksum");
            }
            times.Sort(); Console.WriteLine($"PHASE INDEX {stations} stations: {times[1]:F1} ms, {bytes / 1048576.0:F1} MiB (includes index construction)");
        }
    }

    private static void ModelGuids()
    {
        // P2CUT (13/11): the GUID map of the FEM value (GetGuid, FromModel, SynchronizeGuids) is retired by name (P2rRetired.Checks); the GPC
        // part of P2F stays. P2CE phase 2b: the 0.3.0 payload of two nodes was written from the FEM type: that check is retired by name with it.
        P2fChecks.ModelGuids(Plugin(), Require, null); // P2F (12/11): P2D reads a model back as its GPC input alone
    }

}


