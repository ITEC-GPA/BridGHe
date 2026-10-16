using System;
using System.Collections.Generic;
using System.Linq;
using BridGHe.Common.Helpers;
using BridGHe.Common.Models;
using Rhino.Geometry;

internal static class RegressionTests
{
    public static int Run(string selection)
    {
        var tests = new Dictionary<string, Action>
        {
            ["composite-archive"] = CompositeArchive,
            ["composite-preview"] = CompositePreview,
            ["nodal-actions"] = NodalActions,
            ["load-case-identity"] = LoadCaseIdentity,
            ["merge-node-data"] = MergeNodeData,
            ["merge-mesh"] = MergeMesh,
            ["beam-property-validity"] = BeamPropertyValidity,
            ["native-beam-validity"] = NativeBeamValidity,
            ["model-guids"] = ModelGuids,
            ["deep-copies"] = DeepCopies,
            ["staged-load-cases"] = StagedLoadCases,
            ["staged-selection"] = StagedSelection,
            ["merge-plate-connectivity"] = MergePlateConnectivity,
            ["indexed-phases"] = IndexedPhases,
            ["node-index"] = NodeIndex,
            ["archived-attribute-updates"] = ArchivedAttributeUpdates,
            ["plate-point-updates"] = PlatePointUpdates
        };
        if (selection != "all" && !tests.ContainsKey(selection)) throw new ArgumentException("Unknown regression: " + selection);
        int count = 0;
        foreach (var test in tests.Where(t => selection == "all" || t.Key == selection))
        {
            test.Value(); count++; Console.WriteLine("PASS REGRESSION: " + test.Key);
        }
        Console.WriteLine($"REGRESSION TOTAL {count}");
        return 0;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("REGRESSION FAILED: " + message);
    }

    private static CompositeBeamModel CompositeBeam()
    {
        var steel = new SectionModel { SectionType = SectionModel.SectionTypes.I, D = 500, B1 = 200, B2 = 200, T1 = 20, T2 = 20, T3 = 10 };
        var material = new MaterialModel("S355") { Modulus = 210000 };
        var property = new CompositeBeamPropertyModel("C") { SteelSection = steel,
            ConcreteSection = new SectionConcreteSlabModel(200, 1000, 1000, 40), SteelMaterial = material,
            ConcreteMaterial = material, RebarMaterial = material };
        return new CompositeBeamModel(Point3d.Origin, new Point3d(3000, 0, 0), property);
    }

    private static void CompositeArchive()
    {
        var beam = CompositeBeam();
        var loadCase = new LoadCaseModel("1", "Q");
        beam.Results.Add(new BeamForceResultModel(loadCase, 500, 0, "") { AxialForce = 1234 });
        var copy = ModelArchive.Clone(beam);
        Require(copy.IsValid && copy.Shapes.Length == 2 && copy.Length == 3000, "composite geometry after archive");
        Require(((BeamForceResultModel)copy.Results[0]).AxialForce == 1234, "composite results after archive");
        Require(ReferenceEquals(copy.StartBeamProperty, copy.EndBeamProperty), "shared composite property after archive");
    }

    private static void CompositePreview()
    {
        var beam = CompositeBeam();
        for (int i = 0; i < 3; i++)
        {
            beam.PointTo = new Point3d(4000 + i * 1000, 0, 0);
            Require(beam.Shapes.Length == 2, "preview solids must be replaced, not accumulated");
            Require(Math.Abs(beam.Shapes.Max(s => s.GetBoundingBox(true).Max.X) - beam.PointTo.X) < 1e-6, "preview follows current endpoint");
        }
        beam.EndBeamProperty = null;
        Require(!beam.IsValid && beam.Shapes.Length == 0, "missing section clears obsolete preview");
    }

    private static void NodalActions()
    {
        var q = new LoadCaseModel("", "Q");
        var loads = new LoadModel[] {
            new NodeForceLoadModel(q, new Vector3d(10, 0, -100)),
            new NodeForceLoadModel(new LoadCaseModel("", "Q"), new Vector3d(-10, 0, -200)),
            new NodeMomentLoadModel(q, new Vector3d(0, 30, 0)),
            new NodeMomentLoadModel(q, new Vector3d(0, -10, 0)),
            new NodeForceLoadModel(new LoadCaseModel("", "R"), new Vector3d(0, 0, 999)) };
        var sum = BridGHe.Common.Straus.NodalLoadAccumulator.Sum(loads);
        Require(sum.Count == 2 && sum["Q"].Force == new Vector3d(0, 0, -300), "sum forces and cancellation in the same case");
        Require(sum["Q"].Moment == new Vector3d(0, 20, 0) && sum["R"].Force.Z == 999, "sum moments without mixing cases");
        Require(((NodeForceLoadModel)loads[0]).Value.Z == -100, "aggregation leaves inputs unchanged");
    }

    private static void LoadCaseIdentity()
    {
        var a = new LoadCaseModel("", "Q"); var b = new LoadCaseModel("99", "Q");
        var node = new NodeModel("1", Point3d.Origin);
        node.Loads.Add(new NodeForceLoadModel(a, Vector3d.ZAxis));
        node.Loads.Add(new NodeForceLoadModel(b, Vector3d.XAxis));
        var model = new ModelModel(ModelModel.ModelUnits.Nmm); model.AddElements(new[] { node });
        ModelHelper.GetLoadCases(model, out var cases);
        var numbers = ModelHelper.GetLoadCaseNumbers(cases);
        Require(cases.Count == 2 && numbers[a.Name] == 2 && numbers[b.Name] == 2, "same name uses one canonical export number");
        Require(a.Id == "" && b.Id == "99", "export numbering must not mutate model IDs");
        b.GravityValue = 123;
        bool rejected = false;
        try { ModelHelper.GetLoadCases(model, out _); } catch (ArgumentException) { rejected = true; }
        Require(rejected, "conflicting definitions with the same name must be rejected");
    }

    private static void MergeNodeData()
    {
        var q = new LoadCaseModel("", "Q");
        var first = new NodeModel("7", Point3d.Origin);
        first.Loads.Add(new NodeForceLoadModel(q, new Vector3d(0, 0, -100)));
        first.Attributes.Add(new NodeRestraintAttributeModel(true, false, false, false, false, false));
        first.Groups.Add(new GroupModel("G"));
        var second = new NodeModel("8", new Point3d(0.0001, 0, 0));
        second.Loads.Add(new NodeForceLoadModel(q, new Vector3d(0, 0, -100)));
        second.Attributes.Add(new NodeRestraintAttributeModel(false, true, false, false, false, false));
        var nodes = new List<NodeModel> { first, second };
        var beams = new List<BeamModel>(); var plates = new List<PlateModel>();
        ModelHelper.MergeModel(ref nodes, ref beams, ref plates, 0.001);
        Require(nodes.Count == 1 && nodes[0].NodeId == "7" && nodes[0].Groups.Count == 1, "merge preserves first node identity and groups");
        Require(nodes[0].Attributes.Count == 2 && nodes[0].Loads.Count == 2, "merge retains restraints and equal-valued independent loads");
        Require(BridGHe.Common.Straus.NodalLoadAccumulator.Sum(nodes[0].Loads)["Q"].Force.Z == -200, "coincident node loads add");
        ((NodeForceLoadModel)nodes[0].Loads[0]).Value = Vector3d.Zero;
        Require(first.Loads.Count == 1 && ((NodeForceLoadModel)first.Loads[0]).Value.Z == -100, "node merge isolates its inputs");
        Require(ReferenceEquals(nodes[0].Loads[0].LoadCase, nodes[0].Loads[1].LoadCase), "node merge keeps shared case identity");
    }

    private static void MergeMesh()
    {
        var mesh = new Mesh();
        mesh.Vertices.Add(0, 0, 0); mesh.Vertices.Add(10, 0, 0); mesh.Vertices.Add(0, 10, 0); mesh.Vertices.Add(10, 10, 0);
        mesh.Faces.AddFace(0, 1, 2); mesh.Faces.AddFace(1, 3, 2);
        var nodes = new List<Point3d> { new Point3d(0.0001, 0, 0) };
        var beams = new List<Curve>(); var meshes = new List<Mesh> { mesh };
        ModelHelper.MergeGeometry(ref nodes, ref beams, ref meshes, 0.001);
        Require(meshes[0].Faces.Count == 2 && meshes[0].Vertices.Count == 4 && meshes[0].IsValid, "all adjacent faces survive vertex snapping");
        Require(meshes[0].Vertices.Point3dAt(0).DistanceTo(nodes[0]) < 1e-12, "existing canonical point is used");
        Require(mesh.Vertices.Point3dAt(0) == Point3d.Origin, "source mesh unchanged");
        Require(meshes[0].FaceNormals.All(n => n.Z > 0), "face winding preserved");
        var collapsed = new Mesh();
        collapsed.Vertices.Add(0, 0, 0); collapsed.Vertices.Add(0.0001, 0, 0); collapsed.Vertices.Add(0, 10, 0);
        collapsed.Faces.AddFace(0, 1, 2);
        nodes = new List<Point3d>(); meshes = new List<Mesh> { collapsed };
        ModelHelper.MergeGeometry(ref nodes, ref beams, ref meshes, 0.001);
        Require(meshes[0].Faces.Count == 0, "only genuinely collapsed faces are removed");
    }

    private static void BeamPropertyValidity()
    {
        var p = new BeamPropertyModel("P") { Material = new MaterialModel("S"),
            Section = new SectionModel { SectionType = SectionModel.SectionTypes.Generic }, PropertyType = BeamPropertyType.Beam };
        Require(p.IsValid && ((BeamPropertyBaseModel)p).IsValid, "normal beam does not require cutoff settings");
        p.PropertyType = BeamPropertyType.CutoffBar;
        Require(!p.IsValid, "cutoff bar requires a cutoff law");
        p.CutoffType = CutoffType.Brittle;
        Require(p.IsValid, "valid cutoff bar is accepted");
        p.Section = null;
        Require(!p.IsValid && !((BeamPropertyBaseModel)p).IsValid, "missing section rejected through base and derived types");
    }

    private static System.Reflection.Assembly Plugin()
    {
        System.Reflection.Assembly.LoadFrom(@"C:\Program Files\Rhino 8\Plug-ins\Grasshopper\Grasshopper.dll");
        System.Reflection.Assembly.LoadFrom(@"C:\Program Files\Rhino 8\Plug-ins\Grasshopper\GH_IO.dll");
        return System.Reflection.Assembly.LoadFrom(System.IO.Path.GetFullPath("src/BridGHe.Grasshopper/bin/Release/net8.0-windows/BridGHe.Grasshopper.gha"));
    }

    private static void PlatePointUpdates()
    {
        var plate = new PlateModel(new[] { Point3d.Origin, new Point3d(10, 0, 0), new Point3d(0, 10, 0) }, new PlatePropertyModel("P") { PropertyType = PlatePropertyType.LoadPatch });
        plate.Points = new System.Collections.ObjectModel.ObservableCollection<Point3d>(new[] { Point3d.Origin, new Point3d(20, 0, 0), new Point3d(0, 10, 0) });
        Require(plate.Shape.GetBoundingBox(true).Max.X == 20, "replacing plate points updates preview");
        plate.Points[1] = new Point3d(30, 0, 0);
        Require(plate.Shape.GetBoundingBox(true).Max.X == 30, "replacement point collection remains observable");
        plate.Points.Clear();
        Require(!plate.IsValid && plate.Shape == null, "empty point collection clears obsolete preview safely");
        plate.Points.Add(Point3d.Origin); plate.Points.Add(new Point3d(10, 0, 0)); plate.Points.Add(new Point3d(0, 10, 0));
        Require(plate.IsValid && plate.Shape.GetBoundingBox(true).Max.X == 10, "plate can be rebuilt incrementally");
    }

    private static void ArchivedAttributeUpdates()
    {
        var property = new BeamPropertyModel("P") { Section = new SectionModel { SectionType = SectionModel.SectionTypes.SolidRectangle, B = 20, D = 40 }, Material = new MaterialModel("S"), PropertyType = BeamPropertyType.Beam };
        var original = new BeamModel(Point3d.Origin, new Point3d(0, 0, 1000), property);
        var beam = ModelArchive.Clone(original);
        double width = beam.Shapes[0].GetBoundingBox(true).Diagonal.Y;
        beam.Attributes.Add(new BeamTaperAttributeModel(new BeamTaperAttributeModel.AttributeData { NFromX = 2, NToX = 2 }));
        Require(Math.Abs(beam.Shapes[0].GetBoundingBox(true).Diagonal.Y - 2 * width) < 1e-6, "attributes added after archive regenerate preview");
        Require(Math.Abs(original.Shapes[0].GetBoundingBox(true).Diagonal.Y - width) < 1e-6, "archive callbacks do not alter the source");
        beam.Attributes.Clear();
        Require(Math.Abs(beam.Shapes[0].GetBoundingBox(true).Diagonal.Y - width) < 1e-6, "removing taper restores current geometry");
    }

    private static void NodeIndex()
    {
        var nodes = new List<NodeModel>(); var random = new Random(717);
        for (int i = 0; i < 100; i++) nodes.Add(new NodeModel(i.ToString(), new Point3d(random.NextDouble(), random.NextDouble(), random.NextDouble())));
        nodes.Add(new NodeModel("duplicate", nodes[0].Position));
        using var index = new BridGHe.Common.Straus.StrausNodeIndex(nodes, 0.4);
        foreach (var node in nodes)
        {
            var expected = nodes.Select((n, i) => (Node: n, Number: i + 1)).Where(n => n.Node.Position.DistanceTo(node.Position) < 0.4)
                .OrderBy(n => n.Node.Position.X).ThenBy(n => n.Node.Position.Y).ThenBy(n => n.Node.Position.Z).ThenBy(n => n.Number).ToArray();
            var actual = index.Find(node.Position);
            Require(actual.Number == expected[0].Number && actual.Matches == expected.Length, "spatial index agrees with brute force and stable coordinate ordering");
        }
        bool missing = false;
        try { index.Find(new Point3d(10, 10, 10)); } catch (InvalidOperationException) { missing = true; }
        Require(missing, "unconnected elements fail explicitly");
        using var boundary = new BridGHe.Common.Straus.StrausNodeIndex(new[] { new NodeModel("1", Point3d.Origin) }, 1);
        missing = false;
        try { boundary.Find(new Point3d(1, 0, 0)); } catch (InvalidOperationException) { missing = true; }
        Require(missing, "export preserves strict less-than tolerance");
    }

    private static void IndexedPhases()
    {
        var q = new LoadCaseModel("1", "Q"); q.Stages.Add(new LoadCaseModel.Stage("S") { BridgePhase = LoadCaseModel.Stage.BridgePhases.NInstant });
        var combination = new LoadCombinationModel("C"); combination.Values.Add(q, -2);
        var beam = new ElementModel();
        beam.Results.Add(new BeamForceResultModel(q, 0.5, 0, "") { NormalizedLength = true, AxialForce = 12, Mx = 5, My = 7 });
        beam.Results.Add(new BeamForceResultModel(q, 20, 0, "") { AxialForce = 30 });
        var index = BridGHe.Common.Checks.BridgePhaseAssembler.CreateIndex(beam, 100, 0.125);
        var result = index.Assemble(50.125, combination, GPC.Geometry.CoordinateSystem.Global);
        Require(result.Instant.N == -24 && result.Instant.M1 == -14 && result.Instant.M2 == 10, "indexed phases preserve coefficients, moment conversion, normalized stations and tolerance boundary");
        Require(index.Assemble(20, combination, GPC.Geometry.CoordinateSystem.Global).Instant.N == -60, "absolute stations use the same index");
        bool missing = false;
        try { index.Assemble(50.25, combination, GPC.Geometry.CoordinateSystem.Global); } catch (InvalidOperationException) { missing = true; }
        Require(missing, "outside-tolerance results rejected");
        beam.Results.Add(new BeamForceResultModel(q, 50, 0, ""));
        index = BridGHe.Common.Checks.BridgePhaseAssembler.CreateIndex(beam, 100, 0.125);
        bool duplicate = false;
        try { index.Assemble(50, combination, GPC.Geometry.CoordinateSystem.Global); } catch (InvalidOperationException) { duplicate = true; }
        Require(duplicate, "duplicate station/case results remain an error");
        foreach (int stations in new[] { 100, 400, 800 })
        {
            var many = new ElementModel(); var combo = new LoadCombinationModel("Benchmark");
            for (int c = 0; c < 8; c++)
            {
                var load = new LoadCaseModel(c.ToString(), "L" + c); load.Stages.Add(new LoadCaseModel.Stage("S") { BridgePhase = LoadCaseModel.Stage.BridgePhases.NInstant });
                combo.Values.Add(load, 1);
                for (int i = 0; i < stations; i++) many.Results.Add(new BeamForceResultModel(load, i, 0, "") { AxialForce = c + 1 });
            }
            var times = new List<double>(); long bytes = 0;
            for (int repeat = 0; repeat < 4; repeat++)
            {
                long before = GC.GetAllocatedBytesForCurrentThread(); var watch = System.Diagnostics.Stopwatch.StartNew();
                var lookup = BridGHe.Common.Checks.BridgePhaseAssembler.CreateIndex(many, stations, 1e-6); double sum = 0;
                for (int c = 0; c < 10; c++) for (int i = 0; i < stations; i++) sum += lookup.Assemble(i, combo, GPC.Geometry.CoordinateSystem.Global).Instant.N;
                watch.Stop(); if (repeat > 0) times.Add(watch.Elapsed.TotalMilliseconds); bytes = GC.GetAllocatedBytesForCurrentThread() - before;
                Require(sum == stations * 10 * 36, "indexed benchmark analytical checksum");
            }
            times.Sort(); Console.WriteLine($"PHASE INDEX {stations} stations: {times[1]:F1} ms, {bytes / 1048576.0:F1} MiB (includes index construction)");
        }
    }

    private static void MergePlateConnectivity()
    {
        var property = new PlatePropertyModel("P") { MembraneThickness = 1, PropertyType = PlatePropertyType.ShellThin };
        var plate = new PlateModel("8", new[] { Point3d.Origin, new Point3d(10, 0, 0), new Point3d(10, 10, 0), new Point3d(0, 10, 0) }, property);
        var nodes = new List<NodeModel> { new NodeModel("1", new Point3d(0.0001, 0, 0)) };
        var beams = new List<BeamModel>(); var plates = new List<PlateModel> { plate };
        ModelHelper.MergeModel(ref nodes, ref beams, ref plates, 0.001);
        Require(plates.Count == 1 && plates[0].Points.Count == 4 && nodes.Count == 4, "plate merge uses FEM nodes, not solid preview vertices");
        Require(plates[0].Points[0] == nodes[0].Position && plate.Points[0] == Point3d.Origin, "plate connectivity snaps without mutating input");
        Require(plates[0].PlateId == "8" && plates[0].Points[2] == plate.Points[2], "plate ID and node order preserved");
        var archive = ModelArchive.Clone(plates[0]);
        Require(archive.Points[0] == nodes[0].Position && archive.Shape.IsValid, "snapped FEM connectivity survives serialization");
    }

    private static void StagedSelection()
    {
        var q = new LoadCaseModel("", "Q");
        var combination = new LoadCombinationModel("C"); combination.Values.Add(q, 1);
        var model = new ModelModel(ModelModel.ModelUnits.Nmm);
        foreach (string name in new[] { "Phase 1", "Phase 2" })
        {
            var stage = new StagedConstructionModel(name); stage.AddLoadCombinations(new[] { combination });
            model.AddStagedConstructions(new[] { stage });
        }
        var selected = BridGHe.Common.Straus.StrausResultSelection.Select(model, 2, new[] { "C [Phase 2]" });
        Require(selected.Count == 1 && selected[0].Increment == 2 && selected[0].Case.Name == "C [Phase 2]", "staged selection preserves original result index");
        Require(combination.Name == "C", "catalog must not rename input combinations");
        foreach (string invalid in new[] { "Q", "C", "missing" })
        {
            bool rejected = false;
            try { BridGHe.Common.Straus.StrausResultSelection.Select(model, 2, new[] { invalid }); } catch (ArgumentException) { rejected = true; }
            Require(rejected, "staged selection rejects unavailable or ambiguous names");
        }
        model.AddLoadCombinations(new[] { combination });
        Require(BridGHe.Common.Straus.StrausResultSelection.Select(model, 1, new[] { "C" })[0].Increment == 1, "nonlinear catalog uses combination increments");
        bool caseRejected = false;
        try { BridGHe.Common.Straus.StrausResultSelection.Select(model, 1, new[] { "Q" }); } catch (ArgumentException) { caseRejected = true; }
        Require(caseRejected, "ordinary cases rejected for nonlinear analysis before native export");
    }

    private static void StagedLoadCases()
    {
        var q = new LoadCaseModel("", "StageOnly");
        var combination = new LoadCombinationModel("Increment"); combination.Values.Add(q, 1.0);
        var stage = new StagedConstructionModel("Phase"); stage.AddLoadCombinations(new[] { combination });
        var model = new ModelModel(ModelModel.ModelUnits.Nmm); model.AddStagedConstructions(new[] { stage });
        ModelHelper.GetLoadCases(model, out var cases);
        Require(cases.Count == 2 && cases.Contains(q), "staged-only loads belong to the export case catalog");
        Require(ModelHelper.GetLoadCaseNumbers(cases)[q.Name] > 0, "staged cases have an export number");
    }

    private static void DeepCopies()
    {
        var q = new LoadCaseModel("1", "Q");
        var node = new NodeModel("1", Point3d.Origin);
        node.Loads.Add(new NodeForceLoadModel(q, new Vector3d(0, 0, -100)));
        node.Attributes.Add(new ElementIdAttributeModel("original"));
        var beam = new BeamModel(Point3d.Origin, new Point3d(1000, 0, 0), new BeamPropertyModel("P") {
            Material = new MaterialModel("S"), Section = new SectionModel { SectionType = SectionModel.SectionTypes.Generic }, PropertyType = BeamPropertyType.Beam });
        beam.BeamId = "1";
        beam.Results.Add(new BeamForceResultModel(q, 0, 0, "") { AxialForce = 100 });
        var combination = new LoadCombinationModel("C"); combination.Values.Add(q, 1.5);
        var model = new ModelModel(ModelModel.ModelUnits.Nmm);
        model.AddElements(new ElementModel[] { node, beam }); model.AddLoadCases(new[] { q }); model.AddLoadCombinations(new[] { combination });
        var copy = new ModelModel(model);
        Require(ReferenceEquals(copy.Nodes[0].Loads[0].LoadCase, copy.LoadCases[0]) && ReferenceEquals(copy.LoadCombinations[0].Values.Keys.Single(), copy.LoadCases[0]), "model copy preserves internal case references");
        ((NodeForceLoadModel)copy.Nodes[0].Loads[0]).Value = Vector3d.Zero;
        copy.Beams[0].BeamProperty.Section.SectionArea = 456;
        ((BeamForceResultModel)copy.Beams[0].Results[0]).AxialForce = 200;
        ((ElementIdAttributeModel)copy.Nodes[0].Attributes[0]).Value = "copy";
        Require(((NodeForceLoadModel)node.Loads[0]).Value.Z == -100 && beam.BeamProperty.Section.SectionArea != 456 && ((BeamForceResultModel)beam.Results[0]).AxialForce == 100 && ((ElementIdAttributeModel)node.Attributes[0]).Value == "original", "model copy owns loads, properties, attributes and results");
        var nodeCopy = new NodeModel(node); ((NodeForceLoadModel)nodeCopy.Loads[0]).Value = Vector3d.Zero;
        var beamCopy = new BeamModel(beam); beamCopy.BeamProperty.Material.Modulus = 123;
        Require(((NodeForceLoadModel)node.Loads[0]).Value.Z == -100 && beam.BeamProperty.Material.Modulus != 123, "element copy constructors isolate mutable data");
        var composite = CompositeBeam(); var compositeCopy = new CompositeBeamModel(composite);
        Require(!ReferenceEquals(composite.StartBeamProperty, compositeCopy.StartBeamProperty) && ReferenceEquals(compositeCopy.StartBeamProperty, compositeCopy.EndBeamProperty), "composite copy isolates shared sections");
        var assembly = Plugin();
        var plate = new PlateModel(new[] { Point3d.Origin, new Point3d(1000, 0, 0), new Point3d(0, 1000, 0) }, new PlatePropertyModel("P"));
        foreach (var entry in new[] { ("Node", (ElementModel)node), ("Beam", (ElementModel)beam), ("Plate", (ElementModel)plate) })
        {
            dynamic goo = Activator.CreateInstance(assembly.GetType("BridGHe.Grasshopper.DataTypes.Fem.GH_" + entry.Item1, true), entry.Item2, null);
            dynamic duplicated = goo.DuplicateGeometry();
            Require(!ReferenceEquals(duplicated.Value, goo.Value) && duplicated.Guid == goo.Guid, "Goo geometry copy owns data and keeps identity");
        }
    }

    private static void ModelGuids()
    {
        var model = new ModelModel(ModelModel.ModelUnits.Nmm);
        model.AddElements(new[] { new NodeModel("1", Point3d.Origin), new NodeModel("2", new Point3d(1, 0, 0)) });
        var type = Plugin().GetType("BridGHe.Grasshopper.DataTypes.Fem.GH_Model", true);
        dynamic goo = Activator.CreateInstance(type); goo.Value = model;
        Guid first = goo.GetGuid(model.Nodes[0]);
        dynamic copy = goo.Duplicate();
        Require(copy.Guids.Count == 2 && copy.GetGuid(copy.Value.Nodes[0]) == first, "result models duplicate with complete GUID maps");
        dynamic derived = type.GetMethod("FromModel").Invoke(null, new object[] { ModelArchive.Clone(model), goo });
        derived.SynchronizeGuids(goo); derived.SynchronizeGuids(goo);
        Require(derived.GetGuid(derived.Value.Nodes[0]) == first, "GUID restoration is idempotent");
        var chunk = new GH_IO.Serialization.GH_LooseChunk("model");
        Require(goo.Write(chunk), "write model and GUID map");
        var loadedChunk = new GH_IO.Serialization.GH_LooseChunk("loaded");
        loadedChunk.Deserialize_Binary(chunk.Serialize_Binary());
        dynamic loaded = Activator.CreateInstance(type);
        Require(loaded.Read(loadedChunk) && loaded.GetGuid(loaded.Value.Nodes[0]) == first, "GUIDs survive GH archive round trip");
        var legacy = new GH_IO.Serialization.GH_LooseChunk("legacy"); legacy.SetByteArray("Data", ModelArchive.Write(model));
        Require(loaded.Read(legacy) && loaded.Guids.Count == 2, "legacy archives regenerate complete maps");
    }

    private static void NativeBeamValidity()
    {
        var section = new SectionModel { SectionType = SectionModel.SectionTypes.Generic, SectionArea = 300,
            NativeSection = new BridGHe.Common.Straus.StrausSection { Key = "TriangleSolid", Dimensions = new[] { 20.0, 30.0 } } };
        var property = new BeamPropertyModel("P") { Section = section, Material = new MaterialModel("S"), PropertyType = BeamPropertyType.Beam };
        var beam = new BeamModel(Point3d.Origin, new Point3d(1000, 0, 0), property);
        Require(beam.IsValid && beam.Shapes.Length == 0, "native beam validity does not depend on a solid preview");
        var assembly = Plugin();
        var type = assembly.GetType("BridGHe.Grasshopper.DataTypes.Fem.GH_Beam", true);
        var goo = (Grasshopper.Kernel.Types.IGH_GeometricGoo)Activator.CreateInstance(type, beam, null);
        Require(goo.IsValid && goo.Boundingbox.Contains(beam.PointTo), "native beam Goo remains valid and visible");
        var resultType = assembly.GetType("BridGHe.Grasshopper.DataTypes.Fem.GH_BeamResults", true);
        object[] arguments = { null };
        bool converted = (bool)type.GetMethod("CastTo").MakeGenericMethod(resultType).Invoke(goo, arguments);
        Require(converted, "native beam supports result conversion");
        beam.PointTo = beam.PointFrom;
        Require(!beam.IsValid, "zero-length native beam is invalid");
    }
}

