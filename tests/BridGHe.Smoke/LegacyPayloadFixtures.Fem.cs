using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using BridGHe.Common.Gpc;
using BridGHe.Common.Gpc.Legacy;
using BridGHe.Common.Helpers;
using BridGHe.Common.Models;
using R = Rhino.Geometry;

/// <summary>
/// FEM path (removed with the FeMM models, R6.5): the generator of the frozen BGHA1 payloads of the tests (coordinator decision of 6/11,
/// R6.6 point 1) and their parity with the FEM writer while it exists. The payloads are written once by the FEM writer of this branch
/// (<see cref="ModelArchive.Write"/>), never regenerated (<c>FileMode.CreateNew</c>; the folder must be new):
/// <list type="bullet">
/// <item>the models of <c>gpc-legacy-import</c> (units, bridge identities, the same super element twice, a beam in two super elements) in the
/// four unit systems, and the former local axes of the beams of the units model (the reference of the beam axes);</item>
/// <item>the models of <c>ImportDump</c> (nodes, a labelled node with a rigid link, beams, plates in the four unit systems; the five
/// duplicates refused);</item>
/// <item>more refusals of the reader (prescribed displacements, a rigid link on its own node, two restraint frames, an empty super element,
/// beam supports without stiffness or with a gap on a bilateral spring, a mirrored section, a plate mass in kN-m).</item>
/// </list>
/// Usage: <c>BRIDGHE_LEGACY_FIXTURES=write:&lt;new folder&gt;</c> with <c>--regression gpc-legacy-import</c>. Next to the payloads: <c>index.tsv</c>
/// (name, units, bytes, SHA-256, the outcome of the reader of this branch: <c>ACCEPTED</c> or <c>REFUSED &lt;type&gt;: &lt;message&gt;</c>) and
/// <c>units-axes.tsv</c>. <c>PROVENIENZA.md</c> is written by hand from the index (commit, generator, SHA-256 of every file).
/// </summary>
internal static partial class LegacyPayloadFixtures
{
    internal const string Variable = "BRIDGHE_LEGACY_FIXTURES";

    private static readonly ModelModel.ModelUnits[] AllUnits = { ModelModel.ModelUnits.Nmm, ModelModel.ModelUnits.Knm, ModelModel.ModelUnits.SI, ModelModel.ModelUnits.IPS };

    /// <summary>The points of the units model.</summary>
    private static R.Point3d[] UnitPoints() => new[] { new R.Point3d(0, 0, 0), new R.Point3d(10, 0, 0), new R.Point3d(20, 5, 1), new R.Point3d(10, 0, 4), new R.Point3d(10, 0, -3) };

    /// <summary>The units model of <c>gpc-legacy-import</c> (the former test, unchanged): five nodes (restraints, a restraint frame, a nodal
    /// force, a rigid link, a group), three beams (asymmetric I, rectangle, explicit generic section with an offset) with uniform gravity,
    /// local keystone, projected, pre-tension and gradient loads, a combination, nested groups.</summary>
    internal static (ModelModel Model, BeamModel[] Beams) Units(ModelModel.ModelUnits units)
    {
        string u = units.ToString();
        var steel = new MaterialModel("S355") { Name = "S355", Modulus = 210000, PoissonRatio = 0.3, UsePoisson = true, Density = 7.85e-9, ThermalExpansion = 1.2e-5 };
        var i = new SectionModel { Name = "I", SectionType = SectionModel.SectionTypes.I, D = 0.5, B1 = 0.3, B2 = 0.1, T1 = 0.02, T2 = 0.01, T3 = 0.005 };
        var rect = new SectionModel { Name = "R", SectionType = SectionModel.SectionTypes.SolidRectangle, B = 0.3, D = 0.5 };
        var generic = new SectionModel { Name = "G", SectionType = SectionModel.SectionTypes.Generic, SectionArea = 0.02, I11 = 3e-4, I22 = 1e-4, J = 2e-5, ShearA1 = 0.01, ShearA2 = 0.015, Centroid = new R.Point2d(0.1, 0.2) };
        BeamPropertyModel Property(string name, SectionModel s) => new BeamPropertyModel(name) { Name = name, Material = steel, Section = s, PropertyType = BeamPropertyType.Beam };
        var pi = Property("PI", i); var pr = Property("PR", rect); var pg = Property("PG", generic);
        var traffic = new LoadCaseModel("2", "Traffic"); var wind = new LoadCaseModel("3", "Wind");
        var p = UnitPoints();
        var nodes = p.Select((x, k) => new NodeModel((k + 1).ToString(), x)).ToArray();
        var frame = new R.Plane(p[0], new R.Vector3d(1, 1, 0), new R.Vector3d(-1, 1, 0));
        nodes[0].Attributes.Add(new NodeRestraintAttributeModel(true, true, true, false, false, true));
        nodes[0].Attributes.Add(new NodeCoordinateAttributeModel(frame));
        nodes[2].Attributes.Add(new NodeRestraintAttributeModel(true, true, true, true, true, true));
        nodes[1].Loads.Add(new NodeForceLoadModel(traffic, new R.Vector3d(1, 2, -3)));
        nodes[1].Attributes.Add(new RigidConnectionAttributeModel(p[4]));
        var girder = new GroupModel("Girder"); var span = new GroupModel("Span1", girder); var bearing = new GroupModel("Bearing/S1");
        nodes[0].Groups.Add(bearing);
        var beams = new[]
        {
            new BeamModel(p[0], p[1], pi, span, 0) { BeamId = "1" },
            new BeamModel(p[1], p[2], pr, span, 30) { BeamId = "2" },
            new BeamModel(p[1], p[3], pg, girder, 0) { BeamId = "3", Offset = new R.Vector2d(0.05, -0.1) },
        };
        beams[0].Loads.Add(new BeamDistributedLoadModel(traffic, new BeamDistributedLoadModel.LoadData { LoadSchema = BeamDistributedLoadModel.LoadSchema.Uniform, CoordinateSystem = CoordinateSystemModel.Global,
            LoadDirection = BeamDistributedLoadModel.LoadDirection.Gravity, PA = 10 }, BeamDistributedLoadModel.LoadType.Force));
        beams[1].Loads.Add(new BeamDistributedLoadModel(wind, new BeamDistributedLoadModel.LoadData { LoadSchema = BeamDistributedLoadModel.LoadSchema.Keystone, CoordinateSystem = null,
            LoadDirection = BeamDistributedLoadModel.LoadDirection.Y, PA = 2, PB = 4, A = 0.25, B = 0.25 }, BeamDistributedLoadModel.LoadType.Force));
        beams[1].Loads.Add(new BeamDistributedLoadModel(wind, new BeamDistributedLoadModel.LoadData { LoadSchema = BeamDistributedLoadModel.LoadSchema.Uniform, CoordinateSystem = CoordinateSystemModel.Global,
            LoadDirection = BeamDistributedLoadModel.LoadDirection.Z_Projected, PA = 3 }, BeamDistributedLoadModel.LoadType.Force));
        beams[2].Loads.Add(new BeamPreLoadModel(traffic, 7, BeamPreLoadModel.LoadType.Tension));
        beams[2].Loads.Add(new BeamGradientLoadModel(wind, new BeamGradientLoadModel.LoadData { Axis1 = 0.5, Axis2 = -0.25 }));
        var legacy = new ModelModel(units) { Name = "units " + u };
        legacy.AddElements(nodes.Cast<ElementModel>().Concat(beams));
        var combination = new LoadCombinationModel("SLU"); combination.Values.Add(traffic, 1.35); combination.Values.Add(wind, 0.9); legacy.AddLoadCombinations(new[] { combination });
        return (legacy, beams);
    }

    /// <summary>The bridge identities model of <c>gpc-legacy-import</c> (the former test, unchanged): Element IDs, super elements, bridge
    /// members; <paramref name="variant"/> "twice": the first beam has its super element twice; "two": the first beam is in two super elements.</summary>
    internal static ModelModel BridgeIdentities(ModelModel.ModelUnits units, string variant = null)
    {
        string u = units.ToString();
        var steel = new MaterialModel("S355") { Name = "S355", Modulus = 210000, PoissonRatio = 0.3, UsePoisson = true, Density = 7.85e-9, ThermalExpansion = 1.2e-5 };
        var rect = new SectionModel { Name = "R", SectionType = SectionModel.SectionTypes.SolidRectangle, B = 0.3, D = 0.5 };
        var property = new BeamPropertyModel("P") { Name = "P", Material = steel, Section = rect, PropertyType = BeamPropertyType.Beam };
        var p = Enumerable.Range(0, 4).Select(k => new R.Point3d(10 * k, 0, 0)).ToArray();
        var nodes = p.Select((x, k) => new NodeModel((k + 1).ToString(), x)).ToArray();
        nodes[0].Attributes.Add(new NodeRestraintAttributeModel(true, true, true, true, true, true));
        nodes[3].Attributes.Add(new NodeRestraintAttributeModel(true, true, true, false, false, false));
        nodes[1].Attributes.Add(new ElementIdAttributeModel("N2"));
        var group = new GroupModel("Girder");
        var beams = Enumerable.Range(0, 3).Select(k => new BeamModel(p[k], p[k + 1], property, group, 0) { BeamId = (k + 1).ToString() }).ToArray();
        for (int k = 0; k < 3; k++)
        {
            beams[k].Attributes.Add(new ElementIdAttributeModel("B" + (k + 1)));
            beams[k].Attributes.Add(new SuperElementAttributeModel(k < 2 ? "G1" : "G2"));
            beams[k].Attributes.Add(new BridgeMemberAttributeModel(new BridgeMemberMetadata("Girder:1:1", BridGHe.Common.Geometry.BridgeMemberFamily.Girder, 1, 1, 1,
                10 * k, 10 * (k + 1), new R.Vector3d(0, 0, 1), "Girder:01:01:000" + (k + 1))));
        }
        var legacy = new ModelModel(units) { Name = "bridge identities " + u };
        legacy.AddElements(nodes.Cast<ElementModel>().Concat(beams));
        if (variant == "twice") { var twice = new ModelModel(legacy); twice.Beams[0].Attributes.Add(new SuperElementAttributeModel("G1")); return twice; }
        if (variant == "two") { var two = new ModelModel(legacy); two.Beams[0].Attributes.Add(new SuperElementAttributeModel("G2")); return two; }
        return legacy;
    }

    /// <summary>A small N-mm model (two nodes, one rectangular steel beam, the first node fixed) changed by <paramref name="change"/>.</summary>
    private static ModelModel Small(Action<NodeModel[], BeamModel[], LoadCaseModel> change, ModelModel.ModelUnits units = ModelModel.ModelUnits.Nmm)
    {
        var steel = new MaterialModel("S355") { Name = "S355", Modulus = 210000, PoissonRatio = 0.3, UsePoisson = true, Density = 7.85e-9, ThermalExpansion = 1.2e-5 };
        var rect = new SectionModel { Name = "R", SectionType = SectionModel.SectionTypes.SolidRectangle, B = 300, D = 500 };
        var property = new BeamPropertyModel("P") { Name = "P", Material = steel, Section = rect, PropertyType = BeamPropertyType.Beam };
        var p = new[] { new R.Point3d(0, 0, 0), new R.Point3d(5000, 0, 0) };
        var nodes = p.Select((x, k) => new NodeModel((k + 1).ToString(), x)).ToArray();
        nodes[0].Attributes.Add(new NodeRestraintAttributeModel(true, true, true, true, true, true));
        var beams = new[] { new BeamModel(p[0], p[1], property, new GroupModel("Beams"), 0) { BeamId = "1" } };
        var q = new LoadCaseModel("2", "Q");
        change(nodes, beams, q);
        var model = new ModelModel(units) { Name = "small" };
        model.AddElements(nodes.Cast<ElementModel>().Concat(beams));
        return model;
    }

    /// <summary>Every fixture: name, units and builder (in the order of the index).</summary>
    internal static IEnumerable<(string Name, string Units, Func<ModelModel> Build)> All()
    {
        foreach (var u in AllUnits) yield return ("units-" + u, u.ToString(), () => Units(u).Model);
        foreach (var u in AllUnits)
        {
            yield return ("bridge-identities-" + u, u.ToString(), () => BridgeIdentities(u));
            yield return ("bridge-identities-twice-" + u, u.ToString(), () => BridgeIdentities(u, "twice"));
            yield return ("bridge-identities-two-" + u, u.ToString(), () => BridgeIdentities(u, "two"));
        }
        // ImportDump (the ids of its corpus): every unit system of the former enumeration.
        foreach (ModelModel.ModelUnits u in Enum.GetValues(typeof(ModelModel.ModelUnits)))
        {
            yield return ("nodes-" + u, u.ToString(), () => ImportDump.NodeData(u));
            yield return ("nodes-labelled-rigid-" + u, u.ToString(), () => ImportDump.NodeData(u, true));
        }
        foreach (ModelModel.ModelUnits u in Enum.GetValues(typeof(ModelModel.ModelUnits)))
        {
            yield return ("beams-" + u, u.ToString(), () => ImportDump.BeamData(u, null));
            yield return ("plates-" + u, u.ToString(), () => ImportDump.PlateData(u));
        }
        foreach (var duplicate in new[] { "releases", "taper", "super-elements", "members", "prestrain" })
            yield return ("beams-duplicate-" + duplicate, "Nmm", () => ImportDump.BeamData(ModelModel.ModelUnits.Nmm, duplicate));
        // More refusals of the reader.
        var displacement = new NodeDisplacementLoadModel.LoadData { Translation = new R.Vector3d(0, 0, -10), Rotation = R.Vector3d.Zero };
        yield return ("refusal-prescribed-restrained", "Nmm", () => Small((n, b, q) => n[0].Loads.Add(new NodeDisplacementLoadModel(q, displacement))));
        yield return ("refusal-prescribed-free", "Nmm", () => Small((n, b, q) => n[1].Loads.Add(new NodeDisplacementLoadModel(q, displacement))));
        yield return ("refusal-rigid-collapse", "Nmm", () => Small((n, b, q) => n[1].Attributes.Add(new RigidConnectionAttributeModel(n[1].Position))));
        yield return ("refusal-restraint-frames", "Nmm", () => Small((n, b, q) =>
        {
            n[0].Attributes.Add(new NodeCoordinateAttributeModel(new R.Plane(n[0].Position, new R.Vector3d(1, 1, 0), new R.Vector3d(-1, 1, 0))));
            n[0].Attributes.Add(new NodeCoordinateAttributeModel(new R.Plane(n[0].Position, new R.Vector3d(1, 0, 0), new R.Vector3d(0, 1, 0))));
        }));
        yield return ("refusal-empty-super-element", "Nmm", () => Small((n, b, q) => b[0].Attributes.Add(new SuperElementAttributeModel(" "))));
        BeamSupportAttributeModel Support(double stiffness, double gap, BeamSupportAttributeModel.SimpleSpringTypes kind) => new BeamSupportAttributeModel(new BeamSupportAttributeModel.AttributeData
        {
            SpringType = BeamSupportAttributeModel.SpringType.Simple, Stiffness = stiffness, Gap = gap, SimpleSpringType = kind,
            LocalAxisDirection = BeamSupportAttributeModel.LocalAxisDirection.positive2Axis, SpringTensionOrientation = BeamSupportAttributeModel.SpringTensionOrientation.BeamLocalAxis,
        });
        yield return ("refusal-support-stiffness", "Nmm", () => Small((n, b, q) => b[0].Attributes.Add(Support(0, 0, BeamSupportAttributeModel.SimpleSpringTypes.CompressionOnly))));
        yield return ("refusal-support-gap", "Nmm", () => Small((n, b, q) => b[0].Attributes.Add(Support(3, 0.5, BeamSupportAttributeModel.SimpleSpringTypes.TensionCompression))));
        yield return ("refusal-mirrored-section", "Nmm", () => Small((n, b, q) => b[0].BeamProperty.Section.Mirror = SectionModel.MirrorTypes.Left));
        yield return ("refusal-plate-mass-Knm", "Knm", () =>
        {
            var model = ImportDump.PlateData(ModelModel.ModelUnits.Knm);
            var dead = new LoadCaseModel(LoadCaseModel.Dead);
            model.Plates[0].Loads.Add(new PlateNSMassModel(dead, new PlateNSMassModel.LoadData { Mass = 0.2, DynFactor = 1, Offset = R.Vector3d.Zero }));
            return model;
        });
    }

    /// <summary>Writes the fixtures into the new <paramref name="folder"/> (never into an existing one).</summary>
    internal static void Write(string folder)
    {
        if (Directory.Exists(folder) && Directory.EnumerateFileSystemEntries(folder).Any()) throw new IOException("Legacy payload fixtures already written (never regenerated): " + folder);
        Directory.CreateDirectory(folder);
        var index = new StringBuilder("name\tunits\tbytes\tsha256\toutcome\n");
        int written = 0, refused = 0, deterministic = 0; var skipped = new List<string>();
        foreach (var (name, units, build) in All())
        {
            ModelModel model;
            try { model = build(); }
            catch (Exception e) when (e is ArgumentException || e is InvalidOperationException) { skipped.Add(name + " (the FEM classes refuse it: " + e.Message + ")"); continue; }
            byte[] bytes = ModelArchive.Write(model);
            if (ModelArchive.Write(build()).AsSpan().SequenceEqual(bytes)) deterministic++;
            using (var f = new FileStream(Path.Combine(folder, name + ".bgha1"), FileMode.CreateNew)) f.Write(bytes, 0, bytes.Length);
            string outcome = Outcome(name, bytes); if (outcome != "ACCEPTED") refused++;
            index.Append(name).Append('\t').Append(units).Append('\t').Append(bytes.Length).Append('\t').Append(Convert.ToHexString(SHA256.HashData(bytes))).Append('\t').Append(outcome).Append('\n');
            written++;
        }
        File.WriteAllText(Path.Combine(folder, "index.tsv"), index.ToString(), new UTF8Encoding(false));
        // The former local axes of the beams of the units model (BeamModel.GetLocalAxes(true)): the reference of the beam axes.
        var axes = new StringBuilder("units\tbeam\tv1\tv2\tv3\n");
        static string V(R.Vector3d v) => string.Join(" ", new[] { v.X, v.Y, v.Z }.Select(x => BitConverter.DoubleToInt64Bits(x).ToString("X16")));
        foreach (var u in AllUnits)
        {
            var (_, beams) = Units(u);
            for (int k = 0; k < beams.Length; k++) { beams[k].GetLocalAxes(true, out var v1, out var v2, out var v3, out _); axes.Append(u).Append('\t').Append(k + 1).Append('\t').Append(V(v1)).Append('\t').Append(V(v2)).Append('\t').Append(V(v3)).Append('\n'); }
        }
        File.WriteAllText(Path.Combine(folder, "units-axes.tsv"), axes.ToString(), new UTF8Encoding(false));
        Console.WriteLine($"LEGACY PAYLOAD FIXTURES WRITTEN: {written} payloads ({refused} refused by the reader, {deterministic} written twice to the same bytes) in {folder}"
            + (skipped.Count == 0 ? "" : "; skipped: " + string.Join("; ", skipped)));
    }

    /// <summary>The version of BridGHe.Common that wrote the fixtures.</summary>
    // Frozen: the parity must not depend on the assembly version (the fixtures are never regenerated for a new version).
    private const string FixtureVersion = "0.2.0.0";

    /// <summary>P2S (7/11, master dry run): the FEM archive names its types by their assembly-qualified name (hexadecimal), with the version of
    /// BridGHe.Common; the fixtures were written by <see cref="FixtureVersion"/>. A fixture as the writer of the current version gives it: the
    /// same bytes but for that version, replaced in the hexadecimal names (the same length, so no length prefix changes).</summary>
    private static byte[] SameVersion(byte[] fixture)
    {
        static byte[] Hex(string s) => Encoding.ASCII.GetBytes(Convert.ToHexString(Encoding.UTF8.GetBytes(s)));
        var from = Hex("BridGHe.Common, Version=" + FixtureVersion); var to = Hex("BridGHe.Common, Version=" + typeof(ModelArchive).Assembly.GetName().Version);
        if (from.AsSpan().SequenceEqual(to)) return fixture;
        if (from.Length != to.Length) throw new Exception("LEGACY PAYLOAD FIXTURES FAIL: a version of another length: " + typeof(ModelArchive).Assembly.GetName().Version);
        var result = (byte[])fixture.Clone();
        for (int i = result.AsSpan().IndexOf(from); i >= 0; )
        {
            to.CopyTo(result, i);
            int next = result.AsSpan(i + from.Length).IndexOf(from); i = next < 0 ? -1 : i + from.Length + next;
        }
        return result;
    }

    /// <summary>Parity until R6.5: every fixture equals the payload the FEM writer gives now (byte for byte), the outcome of the reader is
    /// the recorded one, and the former local axes are the recorded ones. The number of checks.</summary>
    internal static int Parity()
    {
        int checks = 0;
        var builders = All().ToDictionary(x => x.Name, x => x.Build, StringComparer.Ordinal);
        foreach (var f in Index())
        {
            if (!builders.TryGetValue(f.Name, out var build)) throw new Exception("LEGACY PAYLOAD FIXTURES FAIL: no builder for " + f.Name);
            if (!ModelArchive.Write(build()).AsSpan().SequenceEqual(SameVersion(Bytes(f.Name)))) throw new Exception("LEGACY PAYLOAD FIXTURES FAIL: the FEM writer gives other bytes now for " + f.Name);
            checks++;
        }
        foreach (var u in AllUnits)
        {
            var (_, beams) = Units(u);
            for (int k = 0; k < beams.Length; k++)
            {
                beams[k].GetLocalAxes(true, out var v1, out var v2, out var v3, out _);
                var a = Axes(u.ToString(), k + 1);
                if (a.V1 != (v1.X, v1.Y, v1.Z) || a.V2 != (v2.X, v2.Y, v2.Z) || a.V3 != (v3.X, v3.Y, v3.Z)) throw new Exception($"LEGACY PAYLOAD FIXTURES FAIL: former local axes of beam {k + 1} ({u})");
                checks++;
            }
        }
        return checks;
    }
}
