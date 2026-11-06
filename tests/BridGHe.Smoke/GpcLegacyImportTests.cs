#if GPC_PILOT
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BridGHe.Common.Gpc;
using BridGHe.Common.Gpc.Legacy;
using GPC.Converter.Straus7;
using GPC.Model.ElementProperties;
using GPC.Model.LoadCases;
using GPC.Model.Loads;
using GPC.Model.Sections;
using GPC.Model.Structure.Assignments;
using R = Rhino.Geometry;
using G = GPC.Geometry;

/// <summary>F6A conversions: former FEM payloads (BGHA1) read as data and converted to GPC with the semantics of the former
/// Straus7 exporter.
/// <para>R6.6 (P2U): this file has no FeMM class. The former FEM models of the tests are the frozen payloads of
/// <c>tests/BridGHe.Smoke/Fixtures/legacy-payloads</c> (<see cref="LegacyPayloadFixtures"/>, written once by the FEM writer: every unit
/// system, the refusals of the reader), the former local axes of their beams the frozen ones of the same folder; the GH data archive takes its
/// former payloads from the example definitions saved by BridGHe 0.3.0 (the locations the R6.0 capture <c>payloads</c> lists). While the
/// FEM models exist, <c>GpcLegacyImportTests.Fem.cs</c> checks the fixtures against the FEM writer (and writes them once, with
/// <c>BRIDGHE_LEGACY_FIXTURES=write:&lt;folder&gt;</c>).</para></summary>
internal static partial class GpcLegacyImportTests
{
    private static int checks;
    private static void Assert(bool condition, string name) { if (!condition) throw new Exception("GPC LEGACY IMPORT FAIL: " + name); checks++; }
    private static bool Near(double a, double b, double tolerance = 1e-12) => Math.Abs(a - b) <= tolerance * Math.Max(1, Math.Max(Math.Abs(a), Math.Abs(b)));
    private static bool Near(G.Vector3d a, (double X, double Y, double Z) b, double tolerance = 1e-12) => Near(a.X, b.X, tolerance) && Near(a.Y, b.Y, tolerance) && Near(a.Z, b.Z, tolerance);
    private static bool Near(G.Vector3d a, R.Vector3d b, double tolerance = 1e-12) => Near(a.X, b.X, tolerance) && Near(a.Y, b.Y, tolerance) && Near(a.Z, b.Z, tolerance);

    /// <summary>The checks of the fixtures against the FEM writer while it exists (<c>GpcLegacyImportTests.Fem.cs</c>; it also writes the
    /// fixtures once); the call disappears with that file.</summary>
    static partial void FemCases();

    /// <summary>The factors of the unit systems (mm and N per definition unit; IPS in Fahrenheit), as the tests state them.</summary>
    private static readonly (string Units, double Length, double Force, bool Fahrenheit)[] UnitSystems =
        { ("Nmm", 1, 1, false), ("Knm", 1000, 1000, false), ("SI", 1000, 1, false), ("IPS", 25.4, 4.4482216152605, true) };

    internal static void Run()
    {
        checks = 0;
        FemCases();
        foreach (var u in UnitSystems) Units(u.Units, u.Length, u.Force, u.Fahrenheit);
        Schemas();
        Archives();
        foreach (var u in UnitSystems) BridgeIdentities(u.Units, u.Length);
        int refusals = Outcomes();
        Console.WriteLine($"GPC LEGACY IMPORT PASS: {checks} checks (units Nmm, Knm, SI, IPS, nodes, restraint frames, rigid links, beam axes, I order, sections, loads, cases, combinations, write plan; " +
            $"{LegacyPayloadFixtures.Index().Count} frozen payloads, {refusals} refusals of the reader as recorded).");
    }

    private static GPC.Model.Models.Model Import(string fixture, out LegacyImportReport report)
    {
        report = new LegacyImportReport();
        var tree = LegacyArchive.Read(LegacyPayloadFixtures.Bytes(fixture));
        Assert(tree.Is("ModelModel"), "legacy payload read as data");
        return LegacyModelImport.Import(tree, GpcIdentity.Derive(GpcIdentity.NameSpace, "test"), 1e-3, report);
    }

    /// <summary>The units model (<c>units-&lt;units&gt;</c>, see <c>LegacyPayloadFixtures.Units</c>): its numbers are those below.</summary>
    private static void Units(string u, double length, double force, bool fahrenheit)
    {
        var p = new[] { new R.Point3d(0, 0, 0), new R.Point3d(10, 0, 0), new R.Point3d(20, 5, 1), new R.Point3d(10, 0, 4), new R.Point3d(10, 0, -3) };
        var frame = new R.Plane(p[0], new R.Vector3d(1, 1, 0), new R.Vector3d(-1, 1, 0));
        var model = Import("units-" + u, out var report);
        Assert(model.NodesElements.Count == 5 && model.BeamElements.Count == 3, u + ": nodes and beams in the former order");
        for (int k = 0; k < 5; k++)
        {
            var n = model.NodesElements[k + 1];
            Assert(n.Position.X == p[k].X * length && n.Position.Y == p[k].Y * length && n.Position.Z == p[k].Z * length, u + ": node position in mm " + (k + 1));
        }
        var r0 = model.NodesElements[1].Assignments.Restrains.Single().Restrain;
        Assert(r0.Restrains.Where(d => d.IsRestrained).Select(d => (int)d.Dof).SequenceEqual(new[] { 0, 1, 2, 5 }), u + ": restrained DOFs");
        Assert(Near(r0.CoordinateSystem.V1, frame.XAxis) && Near(r0.CoordinateSystem.V2, frame.YAxis), u + ": restraint frame");
        Assert(model.NodesElements[1].Groups.ContainsKey("Bearing/S1"), u + ": node group kept as metadata");
        var link = (GPC.Model.Constraints.RigidLink)model.Costrains.Values.Single();
        Assert(link.StartNode.Id == 2 && link.EndNodes.Single().Id == 5, u + ": rigid link master/slave as the former export");
        var nodal = (PointLoad)model.NodesElements[2].Loads.Values.Single(); var f = nodal.GetGlobalForces();
        Assert(f[0] == 1 * force && f[1] == 2 * force && f[2] == -3 * force, u + ": nodal force in N");
        for (int k = 0; k < 3; k++)
        {
            var axes = LegacyPayloadFixtures.Axes(u, k + 1);
            var a = model.BeamElements[k + 1].Assignments.SectionAxes;
            Assert(Near(a.V1, axes.V1, 1e-12) && Near(a.V2, axes.V2, 1e-12) && Near(a.V3, axes.V3, 1e-12), u + ": beam axes equal the former local axes " + (k + 1));
        }
        var h = (SectionH)((ElasticBeamProperty)model.BeamElements[1].BeamProperty).Section;
        Assert(Near(h.LenghtBottomFlange, 0.3 * length) && Near(h.ThicknessBottomFlange, 0.02 * length) && Near(h.LenghtTopFlange, 0.1 * length)
            && Near(h.ThicknessTopFlange, 0.01 * length) && Near(h.ThicknessWeb, 0.005 * length) && Near(h.Height, 0.5 * length), u + ": I section in the native order (B1/T1 bottom)");
        var hp = (ElasticBeamProperty)model.BeamElements[1].BeamProperty;
        Assert(hp.ShearArea1 > 0 && hp.ShearArea2 > 0 && hp.Material.E == 210000 && hp.Material.Density == 7.85e-9, u + ": GPC shear areas and N-mm material");
        var gp = (ElasticBeamProperty)model.BeamElements[3].BeamProperty; var gs = (Section)gp.Section;
        Assert(Near(gs.Area, 0.02 * length * length) && Near(gs.J11, 3e-4 * Math.Pow(length, 4)) && Near(gs.Jt, 2e-5 * Math.Pow(length, 4))
            && Near(gp.ShearArea1, 0.01 * length * length) && Near(gp.ShearArea2, 0.015 * length * length) && Near(gs.Centroid.Y, 0.2 * length), u + ": explicit generic section");
        var offset = model.BeamElements[3].Assignments.OffsetI;
        Assert(Near(offset.X, 0.05 * length) && Near(offset.Y, -0.1 * length) && offset.Z == 0, u + ": beam offset in principal axes, mm");
        var gravity = model.BeamElements[1].Assignments.Loads.Single(); var g0 = gravity.StartIntensity.GetGlobalForces();
        Assert(gravity.Start == 0 && gravity.End == 1 && g0[0] == 0 && g0[1] == 0 && Near(g0[2], -10 * force / length), u + ": gravity load acts along -Z, N/mm");
        Assert(Near(gravity.Eccentricity.X, 0) && gravity.LengthConvention == GPC.Model.Loads.Assignments.BeamLoadLengthConvention.ActualLength, u + ": no eccentricity without offset");
        var local = model.BeamElements[2].Assignments.Loads.Where(l => l.LengthConvention == GPC.Model.Loads.Assignments.BeamLoadLengthConvention.ActualLength).Single();
        var axis2 = LegacyPayloadFixtures.Axes(u, 2).V2;
        var l0 = local.StartIntensity.GetGlobalForces(); var l1 = local.EndIntensity.GetGlobalForces();
        Assert(local.Start == 0.25 && local.End == 0.75 && Near(l0[0], 2 * axis2.Item1 * force / length, 1e-12) && Near(l0[2], 2 * axis2.Item3 * force / length, 1e-12)
            && Near(l1[1], 4 * axis2.Item2 * force / length, 1e-12), u + ": local keystone along principal axis 2, independent of the beam position");
        var projected = model.BeamElements[2].Assignments.Loads.Single(l => l.LengthConvention == GPC.Model.Loads.Assignments.BeamLoadLengthConvention.ProjectedLength);
        Assert(projected.ProjectionPlaneNormal.Z == 1 && Near(projected.StartIntensity.GetGlobalForces()[2], 3 * force / length), u + ": projected global load");
        var eccentric = model.BeamElements[3].Assignments.Physical;
        Assert(eccentric.InitialStates.Single().Value == 7 * force && eccentric.InitialStates.Single().Kind == BeamInitialStateKind.AxialForce, u + ": pre-tension in N");
        double delta(double v) => fahrenheit ? v * 5 / 9 : v;
        Assert(Near(eccentric.TemperatureGradients.Single().Axis1, delta(0.5) / length) && Near(eccentric.TemperatureGradients.Single().Axis2, delta(-0.25) / length), u + ": gradients in °C/mm");
        var dead = model.LoadCases["Dead"];
        Assert(dead is LoadCase lc && lc.LoadCaseType == LoadCase.LoadCaseTypes.SelfWeight && dead.InertiaMasses.Structural && !dead.InertiaMasses.NonStructural, u + ": default Dead case with structural mass");
        var weight = (ModelGravityLoad)model.ModelLoads.Values.Single();
        Assert(ReferenceEquals(weight.LoadCase, dead) && Near(weight.Acceleration, -9806.65) && weight.GravityVersor.Z == 1, u + ": Dead gravity -9806.65 mm/s² along Z");
        Assert(model.LoadCases.Count == 3 && dead.Guid == GpcIdentity.Case("Dead"), u + ": case set with name-derived identities");
        var slu = model.Combinations["SLU"]; Assert(slu.GetLoadCaseCoefficient(model.LoadCases["Traffic"]) == 1.35 && slu.GetLoadCaseCoefficient(model.LoadCases["Wind"]) == 0.9, u + ": combination factors");
        Assert(model.Groups.ContainsKey("Girder\\Span1") && model.Groups["Girder\\Span1"].Parent == model.Groups["Girder"], u + ": group path names and hierarchy");
        Assert(report.NotExported.Count == 0 && report.Warnings.Count == 0, u + ": nothing silently dropped");
        var preflight = GpcPreflight.Run(model);
        Assert(preflight.Passed && preflight.Warnings.Count == 0, u + ": domain preflight without diagnostics: " + string.Join("; ", preflight.Errors.Concat(preflight.Warnings)));
        var plan = Straus7ModelWritePlan.Prepare(model);
        Assert(plan.IdentityMap.Entries.Count == 8, u + ": the Converter accepts the converted model");
    }

    /// <summary>C1: Element IDs as GPC labels (nodes, beams, plates), bridge member metadata and super elements as GPC physical members;
    /// the Straus7 writer accepts the labelled model (labels are descriptive). The fixtures <c>bridge-identities[-twice|-two]-&lt;units&gt;</c>.</summary>
    private static void BridgeIdentities(string u, double length)
    {
        var model = Import("bridge-identities-" + u, out var report);
        string Label(GPC.Model.Elements.Element e) => e.Attributes.Values.OfType<GPC.Model.Attributes.ElementLabelAttribute>().SingleOrDefault()?.Label;
        Assert(Label(model.NodesElements[2]) == "N2" && Label(model.NodesElements[1]) == null, u + ": node Element ID as label");
        Assert(Enumerable.Range(1, 3).All(k => Label(model.BeamElements[k]) == "B" + k), u + ": beam Element IDs as labels");
        // L12: the bridge identities are bridge data by beam GUID (BridgeModel), never in the generic model.
        var bridge = new GPC.Checkers.CompositeBridge.BridgeModel(model, report.Bridge);
        Assert(model.PhysicalMembers.Count == 0 && bridge.Validate().Count == 0, u + ": no bridge member in the generic model, valid bridge data");
        var member = bridge.PhysicalMember("Girder:1:1");
        Assert(member.Select(x => x.Beam.Id).SequenceEqual(new[] { 1, 2, 3 }), u + ": bridge member with its FE parts in order");
        var m2 = member[1].Metadata;
        Assert(m2.PhysicalId == "Girder:1:1" && m2.Family == GPC.Checkers.CompositeBridge.Layout.BridgeMemberFamily.Girder && m2.Group == 1 && m2.Segment == 1 && m2.Span == 1
            && Near(m2.StationFromMm, 10 * length) && Near(m2.StationToMm, 20 * length) && m2.SectionUp.Z == 1 && m2.GeometryId == "Girder:01:01:0002", u + ": part metadata, stations in mm");
        Assert(bridge.SuperElementNames().SequenceEqual(new[] { "G1", "G2" }) && bridge.SuperElement("G1").Select(b => b.Id).SequenceEqual(new[] { 1, 2 })
            && bridge.SuperElement("G2").Single().Id == 3, u + ": super elements of the beams");
        Assert(report.NotExported.Count == 0 && report.Warnings.Count == 0, u + ": nothing silently dropped: " + string.Join("; ", report.NotExported.Keys.Concat(report.Warnings)));
        var plan = Straus7ModelWritePlan.Prepare(model);
        Assert(plan.IdentityMap.Entries.Count == 7, u + ": the writer accepts the labelled model");
        var copy = GpcGooArchive.ReadBridgeModel(GpcGooArchive.BridgeModel(bridge));
        Assert(Label(copy.Model.BeamElements[2]) == "B2" && copy.Data.Fingerprint() == bridge.Data.Fingerprint()
            && copy.PhysicalMember("Girder:1:1")[2].Metadata.GeometryId == "Girder:01:01:0003", u + ": GPC archive round trip (BridgeModel)");
        // Lead decision of 5/11: a beam in two super elements is an import error; two attributes with the same name are one super element.
        var twiceModel = Import("bridge-identities-twice-" + u, out var twiceReport);
        Assert(twiceModel.BeamElements.Count == 3 && twiceReport.Bridge.SuperElement(twiceModel.BeamElements[1].Guid) == "G1", u + ": the same super element twice is one");
        try { Import("bridge-identities-two-" + u, out _); Assert(false, u + ": a beam in two super elements accepted"); }
        catch (ArgumentException e) when (e.Message.StartsWith("LegacyBeamInTwoSuperElements", StringComparison.Ordinal)) { }
    }

    /// <summary>Every frozen payload read by the reader now: the outcome recorded when it was written (accepted, or refused with the same
    /// error type and message). The number of refusals.</summary>
    private static int Outcomes()
    {
        int refusals = 0;
        foreach (var e in LegacyPayloadFixtures.Index())
        {
            string now = LegacyPayloadFixtures.Outcome(e.Name, LegacyPayloadFixtures.Bytes(e.Name));
            Assert(now == e.Outcome, $"{e.Name}: the reader gives '{now}', recorded '{e.Outcome}'");
            if (e.Outcome != "ACCEPTED") refusals++;
        }
        return refusals;
    }
    /// <summary>The first BGHA1 payload of the example definition <paramref name="file"/> (relative to the example folder) whose root is a
    /// <paramref name="rootType"/>, as BridGHe 0.3.0 saved it, with its location; the R6.0 capture <c>payloads</c> must list that location.</summary>
    internal static (byte[] Bytes, string Location) ExamplePayload(string file, string rootType)
    {
        var archive = new GH_IO.Serialization.GH_Archive();
        string path = Path.Combine(ExamplePaths.Root, file);
        if (!archive.ReadFromFile(path)) throw new Exception("GPC LEGACY IMPORT FAIL: example not readable: " + path);
        byte[] found = null; string location = null;
        void Walk(GH_IO.Serialization.GH_IChunk chunk, string where)
        {
            foreach (var item in chunk.Items)
            {
                if (found != null || item.Type != GH_IO.Types.GH_Types.gh_bytearray || !LegacyArchive.IsLegacy(item._bytearray)) continue;
                if (LegacyArchive.Read(item._bytearray).ShortType == rootType) { found = item._bytearray; location = file + ":" + where + "/" + item.Name; }
            }
            foreach (var c in chunk.Chunks) if (found == null) Walk(c, where + "/" + c.Name + (c.Index > 0 ? "[" + c.Index + "]" : ""));
        }
        Walk(archive.GetRootNode, "");
        if (found == null) throw new Exception($"GPC LEGACY IMPORT FAIL: no {rootType} payload in {file}");
        string index = Path.Combine(GpcCompositeSectionChecks.CaptureFolder(), "payloads.tsv");
        if (!File.ReadLines(index).Any(l => l.StartsWith(location + "\t", StringComparison.Ordinal))) throw new Exception($"GPC LEGACY IMPORT FAIL: {location} is not in the R6.0 capture ({index})");
        return (found, location);
    }

    /// <summary>GH data archive (BGHGPC1): GPC values and models round trip with their fingerprint; legacy payloads and foreign or
    /// corrupted bytes are recognised and refused, never read as GPC data. The former payloads: a steel material of example 19 and the
    /// internalized model of example 01, as BridGHe 0.3.0 saved them.</summary>
    private static void Archives()
    {
        var material = new GPC.Model.Materials.Material("S355", 210000, 0.3, 7.85e-9, 1.2e-5);
        var bytes = GpcGooArchive.Value(material); var copy = GpcGooArchive.ReadValue<GPC.Model.Materials.Material>(bytes);
        Assert(GpcGooArchive.IsGpc(bytes) && !GpcGooArchive.IsLegacy(bytes), "GH archive header");
        Assert(copy.Name == "S355" && copy.E == 210000 && copy.Ni == 0.3 && copy.Density == 7.85e-9 && copy.AlfaThermalExpansion == 1.2e-5, "GH archive: material values");
        Assert(GPC.Model.Core.ModelValues.Fingerprint(new object[] { copy }) == GPC.Model.Core.ModelValues.Fingerprint(new object[] { material }), "GH archive: material fingerprint");
        var (saved, where) = ExamplePayload(Path.Combine("01-cantilever", "01-cantilever.ghx"), "ModelModel");
        var tree = LegacyArchive.Read(saved);
        Assert(tree.Is("ModelModel"), "legacy payload read as data (" + where + ")");
        var model = LegacyModelImport.Import(tree, GpcIdentity.Derive(GpcIdentity.NameSpace, "test"), 1e-3, new LegacyImportReport());
        var modelBytes = GpcGooArchive.Model(model); var reopened = GpcGooArchive.ReadModel(modelBytes);
        Assert(model.NodesElements.Count > 0 && GPC.Model.Models.ModelRevisions.AnalysisFingerprint(reopened) == GPC.Model.Models.ModelRevisions.AnalysisFingerprint(model)
            && reopened.NodesElements.Count == model.NodesElements.Count, "GH archive: model fingerprint (" + where + ")");
        bool Refused(Action read) { try { read(); return false; } catch (System.Runtime.Serialization.SerializationException) { return true; } }
        var (payload, payloadWhere) = ExamplePayload(Path.Combine("19-flusso-acciaio-misto", "19-flusso-acciaio-misto_REV003.ghx"), "MaterialModel");
        Assert(GpcGooArchive.IsLegacy(payload) && Refused(() => GpcGooArchive.ReadValue<GPC.Model.Materials.Material>(payload)), "GH archive: legacy payload recognised and refused (" + payloadWhere + ")");
        Assert(Refused(() => GpcGooArchive.ReadModel(bytes)), "GH archive: kind mismatch refused");
        var version = (byte[])bytes.Clone(); version[7] = 9; Assert(Refused(() => GpcGooArchive.ReadValue<GPC.Model.Materials.Material>(version)), "GH archive: unknown version refused");
        Assert(Refused(() => GpcGooArchive.ReadValue<GPC.Model.Materials.Material>(new byte[] { 1, 2, 3 })), "GH archive: foreign bytes refused");
        bool Fails(Action read) { try { read(); return false; } catch (Exception) { return true; } }
        Assert(Fails(() => GpcGooArchive.ReadValue<GPC.Model.Materials.Material>(bytes.Take(bytes.Length / 2).ToArray())), "GH archive: truncated data refused");
    }

    /// <summary>The six distributions decomposed into linear pieces: each piece reproduces the native law at its ends.</summary>
    private static void Schemas()
    {
        const double pa = 3, pb = 5, p1 = 1, p2 = 7, a = 0.2, b = 0.3;
        var expected = new Dictionary<BridgeLoadSchema, (double, double)[]>
        {
            [BridgeLoadSchema.Uniform] = new[] { (a, pa), (1 - b, pa) },
            [BridgeLoadSchema.Keystone] = new[] { (a, pa), (1 - b, pb) },
            [BridgeLoadSchema.Triangular] = new[] { (0.0, p1), (a, pa), (1.0, p2) },
            [BridgeLoadSchema.TriangularEnd1_Keystone] = new[] { (0.0, p1), (a, pa), (1 - b, pb) },
            [BridgeLoadSchema.Keystone_TriangularEnd2] = new[] { (a, pa), (1 - b, pb), (1.0, p2) },
            [BridgeLoadSchema.TriangularEnds_Keystone] = new[] { (0.0, p1), (a, pa), (1 - b, pb), (1.0, p2) },
        };
        var cs = new LoadCaseBase(GpcIdentity.Case("q"), "q");
        foreach (var schema in expected)
        {
            var pieces = GpcBeamLoads.Distributed(cs, new G.Point3d(0, 0, 0), new G.Point3d(1000, 0, 0), schema.Key, pa, pb, p1, p2, a, b, new G.Vector3d(0, 0, 1), false, null, null, null, 1, "test");
            Assert(pieces.Count == schema.Value.Length - 1, "schema pieces " + schema.Key);
            for (int k = 0; k < pieces.Count; k++)
                Assert(pieces[k].Start == schema.Value[k].Item1 && pieces[k].End == schema.Value[k + 1].Item1
                    && pieces[k].StartIntensity.GetGlobalForces()[2] == schema.Value[k].Item2 && pieces[k].EndIntensity.GetGlobalForces()[2] == schema.Value[k + 1].Item2, "schema law " + schema.Key + " piece " + k);
        }
    }
}
#endif
