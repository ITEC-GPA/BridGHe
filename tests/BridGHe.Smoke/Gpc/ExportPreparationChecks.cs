using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using GPC.Converter.Straus7;
using GPC.Geometry;
using GPC.Model.Constraints;
using GPC.Model.ElementProperties;
using GPC.Model.Elements;
using GPC.Model.LoadCases;
using GPC.Model.Loads;
using GPC.Model.Loads.Assignments;
using GPC.Model.Materials;
using GPC.Model.Models;
using GPC.Model.Sections;
using GPC.Model.Stages;
using GPC.Model.Structure.Assignments;

/// <summary>Task 118 (rebased from task 79): analytical arguments at the managed export boundary. No native writer, files or solver.</summary>
internal static class ExportPreparationChecks
{
    internal static void Regression()
    {
        SpringsAndRigidLink(); NodalActions(); BeamActions(); PlatePressures();
        PlateSupportsAndOffset(); PatchDistribution(); PlateMaterials(); GravityMasks(); StageTime();
    }

    private static readonly Guid ModelId = new("a60cf875-3fc8-4bf4-bdce-72c4f7ea7381");
    private static Material Steel() => new("E210", 210000, .3, 7.85e-9, 1.2e-5);
    private static Model Assemble(params Element[] elements) => ModelAssembly.Assemble(elements, ModelId);
    private static BeamElement Beam()
    {
        var a = new NodeElement(new Point3d(100, 200, 300)); var b = new NodeElement(new Point3d(1100, 200, 300));
        var beam = new BeamElement(a.Position, b.Position, new ElasticBeamProperty("rectangle", Steel(), new SectionRectangular(20, 10)));
        beam.ConnectNodes(a, b); beam.Assignments.Formulation = BeamFormulation.StraightTwoNode;
        // Principal axes 1 = +Y, 2 = +Z, 3 = +X; the non-zero origin cannot affect intensities.
        beam.Assignments.SectionAxes = new CoordinateSystem(a.Position, new Vector3d(0, 1, 0), new Vector3d(0, 0, 1));
        return beam;
    }
    private static AreaElement Plate(bool triangle = false)
    {
        var corners = triangle ? new[] { new Point3d(0, 0, 0), new Point3d(100, 0, 0), new Point3d(0, 100, 0) }
            : new[] { new Point3d(0, 0, 0), new Point3d(100, 0, 0), new Point3d(100, 100, 0), new Point3d(0, 100, 0) };
        var plate = new AreaElement(null, new ElasticPlateProperty("plate", Steel(), 10, 10));
        plate.ConnectNodes(corners.Select(p => new NodeElement(p)).ToArray()); return plate;
    }
    private static C5Recorder Record(Model model)
    {
        if (model.LoadCases.Count == 0) model.AddLoadCase(new LoadCaseBase("empty"));
        var api = DispatchProxy.Create<IC5Straus7Recording, C5Recorder>();
        Straus7ModelWritePlan.Prepare(model).Write(api, model); return (C5Recorder)(object)api;
    }
    private static (string Name, object[] Args)[] Calls(C5Recorder log, string name) => log.Calls.Where(c => c.Name == name).ToArray();
    private static object[] One(C5Recorder log, string name)
    {
        var calls = Calls(log, name); Require(calls.Length == 1, name + ": expected one call, got " + calls.Length); return calls[0].Args;
    }
    private static void Require(bool value, string label) { if (!value) throw new Exception("export-preparation: " + label); }
    private static void Near(double actual, double expected, string label)
    {
        double tolerance = 1e-10 * Math.Max(1, Math.Abs(expected));
        Require(!double.IsNaN(actual) && !double.IsInfinity(actual) && Math.Abs(actual - expected) <= tolerance,
            label + ": expected " + expected.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + ", got " + actual.ToString("R", System.Globalization.CultureInfo.InvariantCulture));
    }
    private static void Values(double[] actual, double[] expected, string label)
    {
        Require(actual.Length == expected.Length, label + " length");
        for (int i = 0; i < actual.Length; i++) Near(actual[i], expected[i], label + "[" + i + "]");
    }
    private static void Reject(Action action, string message)
    {
        try { action(); }
        catch (Exception ex) when (ex is ArgumentException || ex is NotSupportedException)
        { Require(ex.Message.Contains(message), "wrong rejection: " + ex.Message); return; }
        throw new Exception("export-preparation: expected rejection " + message);
    }

    private static void SpringsAndRigidLink()
    {
        var beam = Beam(); var matrix = new double[36];
        for (int i = 0; i < 6; i++) matrix[i * 7] = (i + 1) * 100;
        var frame = new CoordinateSystem(Point3d.Origin, new Vector3d(0, 1, 0), new Vector3d(-1, 0, 0));
        beam.NodeJ.Assignments.GroundSprings.Add(new SpringMatrix(matrix, frame));
        var model = Assemble(beam); var log = Record(model); var springs = Calls(log, "SetNodeSpring");
        Require(springs.Length == 2, "one translational and one rotational spring family");
        foreach (var call in springs)
        {
            Require((int)call.Args[0] == model.BeamElements.Values.Single().NodeJ.Id && (int)call.Args[1] == 1, "spring node/freedom case");
            Values((double[])call.Args[4], (bool)call.Args[3] ? new[] { 400d, 500, 600 } : new[] { 100d, 200, 300 }, "spring values");
            var ucs = Calls(log, "CreateUcs").Single(c => (int)c.Args[0] == (int)call.Args[2]);
            // UCS stores origin, a point on local X, a point in local XY.
            Values((double[])ucs.Args[1], new[] { 0d, 0, 0, 0, 1, 0, -1, 0, 0, 0 }, "rotated spring UCS");
        }
        beam = Beam(); matrix = new double[36]; matrix[0] = matrix[7] = 100; matrix[1] = matrix[6] = 5;
        beam.NodeJ.Assignments.GroundSprings.Add(new SpringMatrix(matrix, CoordinateSystem.Global));
        Reject(() => Record(Assemble(beam)), "CoupledGroundSpringRequiresMatrixLinkWriter");
        model = Assemble(Beam()); var b = model.BeamElements.Values.Single(); model.AddCostrain(new RigidLink(b.NodeI, b.NodeJ));
        var rigid = One(Record(model), "SetRigidLink");
        Require((int)rigid[0] == 1 && (int)rigid[1] == 1 && (int)rigid[2] == b.NodeI.Id && (int)rigid[3] == b.NodeJ.Id, "rigid link identities");
    }

    private static void NodalActions()
    {
        var beam = Beam(); var c = new LoadCaseBase("P"); var n = beam.NodeJ;
        // r=(2,0,0), F=(0,3,0): r cross F=(0,0,6); direct Mz=4 gives 10 N mm.
        n.Loads.Add(new PointLoad(0, 3, 0, 0, 0, 4, n.Position + new Vector3d(2, 0, 0), c));
        var model = Assemble(beam); var log = Record(model); var loads = Calls(log, "SetNodeLoad");
        Require(loads.Length == 2, "nodal force and moment only");
        Values((double[])loads.Single(x => !(bool)x.Args[2]).Args[3], new[] { 0d, 3, 0 }, "nodal force");
        Values((double[])loads.Single(x => (bool)x.Args[2]).Args[3], new[] { 0d, 0, 10 }, "eccentric nodal moment");
        // All domain temperature kinds, including the reference type beyond the two UI choices.
        foreach (var kind in new[] { NodalTemperatureKind.Reference, NodalTemperatureKind.Prescribed, NodalTemperatureKind.Initial })
        {
            beam = Beam(); beam.NodeJ.Assignments.Physical = new NodePhysicalData();
            beam.NodeJ.Assignments.Physical.Temperatures.Add(new NodalTemperatureAssignment { Case = new LoadCaseBase("T"), Kind = kind, Celsius = -17 });
            model = Assemble(beam); var t = One(Record(model), "SetNodeTemperature");
            Require((int)t[0] == model.BeamElements.Values.Single().NodeJ.Id && (int)t[1] == 1 && (NodalTemperatureKind)t[2] == kind, "temperature identity/type"); Near((double)t[3], -17, "temperature Celsius");
        }
    }

    private static void BeamActions()
    {
        var beam = Beam(); var c = new LoadCaseBase("P");
        beam.Assignments.Loads.Add(new BeamLoadAssignment {
            Start = .2, End = .8, LengthConvention = BeamLoadLengthConvention.ActualLength,
            StartIntensity = new LineLoad(0, 0, -2, 5, 0, 0, beam.Line, c), EndIntensity = new LineLoad(0, 0, -4, 7, 0, 0, beam.Line, c),
            Eccentricity = new Vector3d(0, 3, 0), EccentricityCoordinateSystem = CoordinateSystem.Global });
        var log = Record(Assemble(beam)); var calls = Calls(log, "SetBeamDistributedLoad");
        Require(calls.Length == 2, "distributed force Z and principal moment 3 only");
        foreach (var call in calls)
        {
            Require((int)call.Args[3] == 3, "distributed axis");
            // mx + ey*fz: 5-6=-1, 7-12=-5. Axis 3 is global X.
            Values((double[])call.Args[5], (bool)call.Args[4] ? new[] { -1d, -5, 0, 0, .2, .2 } : new[] { -2d, -4, 0, 0, .2, .2 }, "distributed arguments");
        }
        beam = Beam(); beam.Assignments.Loads.Add(new BeamLoadAssignment {
            Start = .25, End = .25, LengthConvention = BeamLoadLengthConvention.ActualLength,
            Concentrated = new PointLoad(0, 0, -2, 5, 0, 0, beam.StartPoint + new Vector3d(250, 0, 0), c),
            Eccentricity = new Vector3d(0, 3, 0), EccentricityCoordinateSystem = CoordinateSystem.Global });
        calls = Calls(Record(Assemble(beam)), "SetBeamPointLoad"); Require(calls.Length == 2, "beam point force and moment");
        foreach (var call in calls) Values((double[])call.Args[4], (bool)call.Args[3] ? new[] { -1d, 0, 0, .25 } : new[] { 0d, 0, -2, .25 }, "point arguments");
        foreach (var kind in new[] { BeamInitialStateKind.AxialForce, BeamInitialStateKind.AxialStrain })
        {
            beam = Beam(); beam.Assignments.Physical = new BeamPhysicalData(); double value = kind == BeamInitialStateKind.AxialForce ? 1200 : -.0002;
            beam.Assignments.Physical.InitialStates.Add(new BeamInitialState { Case = new LoadCaseBase("I"), Kind = kind, Value = value });
            var initial = One(Record(Assemble(beam)), "SetBeamInitialState"); Require((BeamInitialStateKind)initial[2] == kind, "initial state kind"); Near((double)initial[3], value, "initial state value");
        }
    }

    private static void PlatePressures()
    {
        foreach (bool triangle in new[] { true, false })
        {
            var plate = Plate(triangle); var corners = plate.Nodes.Select(n => n.Position).ToArray();
            plate.Loads.Add(new NonUniformPlatePressure(corners, triangle ? new[] { 3d, 0, 0 } : new[] { 1d, 5, 5, 1 }, new LoadCaseBase("P"), CoordinateSystem.Global));
            var model = Assemble(plate); var log = Record(model); var forces = Calls(log, "SetNodeLoad").Where(c => !(bool)c.Args[2]).ToArray();
            Require(forces.Length == corners.Length, "pressure nodal force count");
            Require(Calls(log, "SetPlatePressure").Length + Calls(log, "SetNormalSurfacePressure").Length + Calls(log, "SetGlobalSurfacePressure").Length == 0, "no duplicate uniform pressure");
            // Triangle A/12*(sum p+p_i); rectangle A/36*(4p_i+2p_adj1+2p_adj2+p_opp).
            double[] expected = triangle ? new[] { 2500d, 1250, 1250 } : new[] { 17500d / 3, 27500d / 3, 27500d / 3, 17500d / 3 };
            for (int i = 0; i < corners.Length; i++)
            {
                int node = model.NodesElements.Values.Single(n => n.Position.DistanceTo(corners[i]) < 1e-9).Id;
                Values((double[])forces.Single(c => (int)c.Args[0] == node).Args[3], new[] { 0d, 0, expected[i] }, "pressure vertex " + i);
            }
            Near(forces.Sum(c => ((double[])c.Args[3])[2]), triangle ? 5000 : 30000, "pressure resultant");
        }
        var partial = Plate(); partial.Loads.Add(new NonUniformPlatePressure(new[] { new Point3d(0, 0, 0), new Point3d(50, 0, 0), new Point3d(50, 100, 0), new Point3d(0, 100, 0) }, new[] { 1d, 1, 1, 1 }, new LoadCaseBase("partial"), CoordinateSystem.Global));
        Reject(() => Record(Assemble(partial)), "NonUniformPlatePressureRequiresThePlateCorners");
    }

    private static void PlateSupportsAndOffset()
    {
        foreach (var face in new[] { ShellFace.PositiveNormal, ShellFace.NegativeNormal })
            foreach (var behavior in new[] { DistributedSpringBehavior.Bilateral, DistributedSpringBehavior.CompressionOnly })
            {
                var plate = Plate(); plate.Assignments.Offset = 12.5; plate.Assignments.Physical = new ShellPhysicalData();
                plate.Assignments.Physical.Supports.Add(new ShellDistributedSupport { Face = face, Behavior = behavior, Stiffness = .25, Gap = behavior == DistributedSpringBehavior.Bilateral ? 0 : 2 });
                var log = Record(Assemble(plate)); Near((double)One(log, "SetPlateOffset")[1], 12.5, "plate offset mm"); var support = One(log, "SetPlateSupport");
                Require((ShellFace)support[1] == face && (int)support[2] == 1 && (bool)support[3] == (behavior == DistributedSpringBehavior.CompressionOnly), "support face/freedom/behavior");
                Near((double)support[4], .25, "support N/mm3"); Near((double)support[5], behavior == DistributedSpringBehavior.Bilateral ? 0 : 2, "support gap mm");
            }
        var invalid = Plate(); invalid.Assignments.Physical = new ShellPhysicalData();
        invalid.Assignments.Physical.Supports.Add(new ShellDistributedSupport { Face = ShellFace.PositiveNormal, Behavior = DistributedSpringBehavior.TensionOnly, Stiffness = .25 });
        Reject(() => Record(Assemble(invalid)), "StrausShellTensionOnlySupportRequiresExplicitFaceConvention");
    }

    private static void PatchDistribution()
    {
        var kinds = new[] { LoadPatchDistributionKind.AutomaticOneEdge, LoadPatchDistributionKind.AutomaticTwoEdges, LoadPatchDistributionKind.AutomaticThreeEdges,
            LoadPatchDistributionKind.AutomaticAllEdges, LoadPatchDistributionKind.AngleSplit, LoadPatchDistributionKind.Manual };
        var masks = new[] { 1, 5, 7, 15, 5, 5 };
        var native = new[] { 3, 2, 1, 0, 4, 5 }; // Legacy/API enum contract, not computed by the provider under test.
        for (int i = 0; i < kinds.Length; i++)
        {
            // P2CJ: a load patch carries load without stiffness: no material and no thickness (GPCModel ElasticPlateProperty.Validate,
            // LoadPatchHasNoMaterialOrThickness; the export preflight refused the steel plate of 10 mm with the formulation switched).
            var plate = Plate(); plate.PlateProperty = new ElasticPlateProperty("patch", null, 0, 0, ElasticPlateFormulation.LoadPatch);
            plate.Assignments.Physical = new ShellPhysicalData { LoadPatch = new LoadPatchDistribution { Kind = kinds[i], SelectedEdges = i == 0 ? new[] { 1 } : i == 2 ? new[] { 1, 2, 3 } : i == 3 ? new[] { 1, 2, 3, 4 } : new[] { 1, 3 }, EdgeWeights = new[] { .25, 0, .75, 0 } } };
            var p = One(Record(Assemble(plate)), "SetLoadPatch"); Require((int)p[1] == native[i] && (int)p[2] == masks[i], "patch kind/edge mask");
            Values((double[])p[3], kinds[i] == LoadPatchDistributionKind.Manual ? new[] { .25, 0, .75, 0 } : new double[4], "patch weights");
        }
    }

    private static void PlateMaterials()
    {
        var plate = Plate(); ((ElasticPlateProperty)plate.PlateProperty).Formulation = ElasticPlateFormulation.ShearPanel;
        var p = One(Record(Assemble(plate)), "CreatePlateProperty"); Require((int)p[2] == 5 && !(bool)p[6], "shear panel formulation");
        Near(((double[])p[5])[0], 210000 / 2.6, "shear modulus N/mm2");
        plate = Plate(); var material = new Material("ortho", 1000, .2, 0, 0) { Orthotropy = new OrthotropicElasticity {
            E1 = 1000, E2 = 500, E3 = 100, G12 = 200, G23 = 40, G31 = 50, Nu12 = .2, Nu23 = .1, Nu31 = .05 } };
        plate.PlateProperty = new ElasticPlateProperty("ortho", material, 10, 10);
        // P2CJ: an orthotropic plate needs its material axes (Straus7ModelWritePlan, OrthotropicPlateRequiresMaterialAxes): 1 = global X,
        // 2 = global Y, in the plane of the plate (XY); the order of the expected values below is the one of these axes.
        plate.Assignments.LayerAxes = CoordinateSystem.Global; p = One(Record(Assemble(plate)), "CreatePlateProperty");
        Require((bool)p[6], "orthotropic flag"); Values(((double[])p[5]).Take(9).ToArray(), new[] { 1000d, 500, 100, 200, 40, 50, .2, .1, .05 }, "orthotropic axes/order");
    }

    private static void GravityMasks()
    {
        foreach (bool structural in new[] { false, true }) foreach (bool nsm in new[] { false, true })
        {
            var model = Assemble(Beam()); var c = new LoadCase("G", LoadCase.LoadCaseTypes.SelfWeight) { InertiaMasses = new InertiaMassSelection { Structural = structural, NonStructural = nsm } };
            model.AddLoadCase(c); model.ModelLoads.Add(new ModelGravityLoad(new Vector3d(0, 0, -1), 9806.65, c, CoordinateSystem.Global));
            var log = Record(model); var mask = One(log, "SetInertiaMassSelection");
            Require((int)mask[0] == 1 && (bool)mask[1] == structural && (bool)mask[2] == nsm, "inertia mass mask");
            Values((double[])One(log, "SetGravity")[1], new[] { 0d, 0, -9806.65 }, "gravity mm/s2");
        }
        var oblique = Assemble(Beam()); var lc = new LoadCase("G", LoadCase.LoadCaseTypes.SelfWeight); oblique.AddLoadCase(lc);
        oblique.ModelLoads.Add(new ModelGravityLoad(new Vector3d(1, 0, -1), 9806.65, lc, CoordinateSystem.Global));
        Reject(() => Record(oblique), "ObliqueGravityRequiresExplicitMassAccelerationConversion");
    }

    private static void StageTime()
    {
        foreach (bool timed in new[] { true, false })
        {
            var model = Assemble(Beam()); var c = new LoadCaseBase("P"); model.AddLoadCase(c);
            var group = model.AddGroup("beam"); var beam = model.BeamElements.Values.Single(); beam.Groups.Add(group.Name, group);
            model.Stages.Add(new Stage(1, "S1") { Definition = new StageDefinition(1, 0), AnalysisState = new StageAnalysisState {
                ActiveGroupGuids = new[] { group.Guid }, FluidLevelMm = timed ? 1250 : (double?)null,
                Increments = new[] { new StageLoadIncrement { Name = "load", PseudoTimeSeconds = timed ? 10 : (double?)null, LoadFactors = new[] { new StageCaseFactor { CaseGuid = c.Guid, Factor = 1.35 } } },
                    new StageLoadIncrement { Name = "unload", PseudoTimeSeconds = timed ? 20 : (double?)null } } } });
            var plan = Straus7StageWritePlan.Prepare(model, new Dictionary<Guid, int> { [group.Guid] = 1 }, new Dictionary<Guid, int> { [c.Guid] = 1 }, new Dictionary<Guid, int>());
            var api = DispatchProxy.Create<IStraus7StageWriteApi, C5Recorder>(); plan.Write(api, model); var log = (C5Recorder)(object)api;
            var stage = One(log, "CreateStage"); Require(((int[])stage[2])[3] == (timed ? 1 : 0), "fluid-level flag");
            Require(timed ? (double)stage[3] == 1250 : stage[3] == null, "fluid level mm");
            Require((bool)One(log, "SetStagePseudoTime")[1] == timed, "pseudo-time enable");
            var increments = Calls(log, "AddStageIncrement"); Require(increments.Length == 2, "stage increment count");
            for (int i = 0; i < 2; i++) Require(timed ? (double)increments[i].Args[3] == 10 * (i + 1) : increments[i].Args[3] == null, "pseudo-time seconds");
            var factors = Calls(log, "SetStageFactor"); Require(factors.Length == 2, "stage load factor count");
            Near((double)factors[0].Args[4], 1.35, "loading factor"); Near((double)factors[1].Args[4], 0, "explicit unloading");
        }
    }
}
