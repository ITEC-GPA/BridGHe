using System;
using System.IO;
using System.Linq;
using BridGHe.Common.Geometry;
using BridGHe.Common.Helpers;
using BridGHe.Common.Straus;
using Rhino.Geometry;

internal static class BridgeBearingTests
{
    internal static void Run()
    {
        Gpc();
    }

    /// <summary>R6.6 R2a: the bearings by their GPC drafts (<see cref="BridgeBearingLayout.Drafts"/>, what the component gives on the path of
    /// P2): masks, frames and springs read on the restraint records of the drafts, the frame through the archive of the draft; a second
    /// conflicting frame is refused by the node builder (the reader accepts a second frame only when it is the same plane).</summary>
    private static void Gpc()
    {
        var geometry = SimpleBridgeBuilder.Build(SimpleBridgeTests.Input(curved: true));
        var bearings = BridgeBearings.Create(geometry, new[] { 2 }, null, new[] { 0, 1, 2 }, 23, Vector3d.Zero, Vector3d.Zero);
        Require(bearings.Drafts.Length == 3, "Selection count");
        var masks = bearings.Drafts.Select(R2aGeneratorFixtures.Restraint).ToArray();
        Require(masks[0][0] && masks[0][1] && masks[0][2] && !masks[0][3], "Fixed translations, free rotations");
        Require(!masks[1][0] && masks[1][1] && masks[1][2] && !masks[1][4], "Longitudinal guide");
        Require(!masks[2][0] && !masks[2][1] && masks[2][2], "Multidirectional");
        foreach (var node in bearings.Drafts)
        {
            var frame = R2aGeneratorFixtures.Frame(node); var z = R2aGeneratorFixtures.Cross(frame.V1, frame.V2);
            Require(z.Z / Math.Sqrt(R2aGeneratorFixtures.Dot(z, z)) > 1 - 1e-12, "Default bearing frames horizontal");
            var copy = R2aGeneratorFixtures.Frame(R2aGeneratorFixtures.Clone(node));
            Require(copy.Origin.X == frame.Origin.X && copy.Origin.Y == frame.Origin.Y && copy.Origin.Z == frame.Origin.Z
                && copy.V1.X == frame.V1.X && copy.V1.Y == frame.V1.Y && copy.V1.Z == frame.V1.Z && copy.V2.X == frame.V2.X && copy.V2.Y == frame.V2.Y && copy.V2.Z == frame.V2.Z, "Nodal frame persists in archive");
        }
        var elastic = BridgeBearings.Create(geometry, new[] { 1 }, new[] { 1 }, new[] { 3 }, 0, new Vector3d(10, 20, 30), new Vector3d(40, 50, 60));
        var stiffness = R2aGeneratorFixtures.Stiffness(elastic.Drafts[0]);
        Require(stiffness[0] == 10 && stiffness[1] == 20 && stiffness[5] == 60, "Local elastic stiffnesses");
        R2aGeneratorFixtures.Reject(() => BridGHe.Common.Gpc.Builders.GpcNodeBuilders.WithFrame(bearings.Drafts[0], 0, 0, 0, 1, 0, 0, 0, 1, 0, R2aGeneratorFixtures.Id("bearing conflicting frame")),
            "a second, conflicting bearing frame", "GpcNodeBuilderSecondCoordinateSystem");
        Reject(() => BridgeBearings.Create(geometry, new[] { 999 }, null, new[] { 2 }, 0, Vector3d.Zero, Vector3d.Zero));
        Reject(() => BridgeBearings.Create(geometry, null, null, new[] { 3 }, 0, Vector3d.Zero, Vector3d.Zero));
        Console.WriteLine("BRIDGE BEARINGS PASS: masks, curved guide frames, elastic stiffnesses, archives and conflicting frames.");
    }
    /// <summary>F6A: the bearing frame, restraint and springs written by the GPC Converter (export component path) and read back with
    /// the Converter API: UCS orientation, local restraint flags and spring values. No solver run. R6.6 R2a (GPC_PILOT): the model is Build
    /// Model on GPC of the node and beam drafts (no FEM model), written by Export of the GPC-only input.</summary>
    internal static void Native()
    {
        var frame = new Plane(new Point3d(100, 200, 300), new Vector3d(1, 2, 0), new Vector3d(-2, 1, 0));
        string Id(string s) => "bearing native " + s;
        var n = BridGHe.Common.Gpc.Builders.GpcNodeBuilders.Create(100, 200, 300, "N", null, R2aGeneratorFixtures.Id(Id("N")));
        n = BridGHe.Common.Gpc.Builders.GpcNodeBuilders.WithFrame(n, frame.Origin.X, frame.Origin.Y, frame.Origin.Z, frame.XAxis.X, frame.XAxis.Y, frame.XAxis.Z,
            frame.YAxis.X, frame.YAxis.Y, frame.YAxis.Z, R2aGeneratorFixtures.Id(Id("N frame")));
        n = BridGHe.Common.Gpc.Builders.GpcNodeBuilders.WithRestraint(n, false, false, true, false, false, false, R2aGeneratorFixtures.Id(Id("N restraint")));
        // Springs on the free DOFs (on a restrained DOF the restraint governs, as the former exporter).
        n = BridGHe.Common.Gpc.Builders.GpcNodeBuilders.WithStiffness(n, 10, 20, 0, 0, 0, 0, R2aGeneratorFixtures.Id(Id("N springs")));
        var m = BridGHe.Common.Gpc.Builders.GpcNodeBuilders.WithRestraint(BridGHe.Common.Gpc.Builders.GpcNodeBuilders.Create(1100, 200, 300, "M", null, R2aGeneratorFixtures.Id(Id("M"))),
            true, true, true, true, true, true, R2aGeneratorFixtures.Id(Id("M restraint")));
        var steel = BridGHe.Common.Gpc.Builders.GpcMaterialBuilders.Material(new BridGHe.Common.Gpc.Legacy.LegacyMaterialRecipe { Kind = "Undefined", Name = "S355", Modulus = 210000, PoissonRatio = .3 });
        var property = BridGHe.Common.Gpc.Builders.GpcPropertyBuilders.Beam("P", 5, BridGHe.Common.Gpc.Builders.GpcSectionBuilders.Generic(10000, 1e8, 1e8, 1e7, 0, 0, 0, 0, 0, 0, 0, 0, 0), steel);
        var beam = BridGHe.Common.Gpc.Builders.GpcBeamBuilders.New(n.Node.Position, m.Node.Position, property, null, 0, 0, 0, R2aGeneratorFixtures.Id(Id("beam")), "1");
        var input = R2aGeneratorFixtures.Build("bearing native", new[] { n, m }, new[] { beam }, 1e-6);
        string folder = Path.GetFullPath(Path.Combine("artifacts", "bearings-native", Guid.NewGuid().ToString("N"))); Directory.CreateDirectory(folder);
        string file = Path.Combine(folder, "bearing.st7");
        BridGHe.Common.Gpc.Legacy.FemmAnalysisSeam.Export(null, input, file, 1e-6, new System.Collections.Generic.List<string>());
        using var api = new GPC.Converter.Straus7.Straus7NativeApi(); api.OpenModelReadOnly(file, folder);
        var restraint = api.ReadRestraint(1, 1); var ucs = api.ReadUcs(restraint.Ucs).Data;
        var axis = new Vector3d(ucs[3] - ucs[0], ucs[4] - ucs[1], ucs[5] - ucs[2]); axis.Unitize();
        Require(restraint.Ucs > 1 && axis * frame.XAxis > 1 - 1e-12, "Vendor UCS X orientation");
        Require(restraint.Status.SequenceEqual(new[] { 0, 0, 1, 0, 0, 0 }), "Vendor restraint roundtrip");
        var k = api.ReadNodeStiffness(1, 1, false);
        Require(k.Ucs == restraint.Ucs && k.Values.SequenceEqual(new[] { 10d, 20, 0 }), $"Vendor spring roundtrip: UCS {k.Ucs} (restraint {restraint.Ucs}), {string.Join(", ", k.Values)}");
        api.CloseModel();
        Console.WriteLine("BRIDGE BEARINGS NATIVE PASS: UCS, restraints and springs written by the GPC Converter and read back; no solver run.");
    }
    private static void Require(bool value, string text) { if (!value) throw new InvalidOperationException("BRIDGE BEARINGS: " + text); }
    private static void Reject(Action action)
    { try { action(); } catch (ArgumentException) { return; } throw new InvalidOperationException("Invalid bearing was accepted"); }
}
