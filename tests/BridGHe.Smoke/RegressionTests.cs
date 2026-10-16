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
            ["deep-copies"] = DeepCopies
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
