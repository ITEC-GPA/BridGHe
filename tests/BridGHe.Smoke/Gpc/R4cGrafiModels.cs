#if GPC_PILOT
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using BridGHe.Common.Gpc;
using BridGHe.Common.Gpc.Builders;
using BridGHe.Common.Gpc.Legacy;
using BridGHe.Common.MovingLoads;
using GPC.Model.Models;
using Rhino.Geometry;

/// <summary>
/// R6.6 lane R4c-grafi: the grillage models of the native examples 11–18 (<see cref="BridgeGrillageExample"/>, <see cref="BridgeSuiteExample"/>,
/// <see cref="BridgeSpineExamples"/>) and of their Grasshopper graphs (<see cref="GrasshopperBridgeExample"/>, <see cref="GrasshopperBridgeSuite"/>)
/// as plain recipes, without any FEM model. A recipe holds what the former FEM model held, in the same order: the nodes (position, restraint,
/// the forces per case, the group «Longitudinali»), the beams (end nodes, property, group, super element, BridGHe ID, pre-strain and gradient
/// loads), the cases, the stages and the moving paths. <see cref="Build"/> gives the GPC input with the product builders (the GPC data the GH
/// components give: <see cref="GpcNodeBuilders"/>, <see cref="GpcBeamBuilders"/>, <see cref="GpcCaseBuilders"/>), assembled by Build Model on GPC
/// (<see cref="LegacyBuildModel.Build"/>, through the builder of lane R0, <see cref="GpcNativeExamples.Builder"/>) and, with paths, Attach Moving
/// Paths on GPC (<see cref="GpcModelTransforms.ReplacePaths"/>); the graphs are drawn from the same recipe. <see cref="Solve"/> analyses it with
/// the GPC-only analysis (<c>StrausAnalysis.Run(Conversion…)</c>) and <see cref="Results"/> reads the analysed model with the GPC result readers
/// (<see cref="GpcResultRows"/>, the rows of the Results components), elements by their index in the recipe (checked on the positions).
/// </summary>
internal sealed class R4cModel
{
    internal sealed class Node
    {
        internal Point3d Position;
        internal bool[] Restraint = new bool[6];
        internal readonly List<(string Case, Vector3d Force)> Loads = new List<(string Case, Vector3d Force)>();
        /// <summary>The sum of the forces of a case on the node (the former <c>Loads.OfType&lt;NodeForceLoadModel&gt;()</c> of the case).</summary>
        internal Vector3d Force(string loadCase) => Loads.Where(l => l.Case == loadCase).Aggregate(Vector3d.Zero, (v, l) => v + l.Force);
    }

    internal sealed class Beam
    {
        internal int From, To;
        internal LegacyBeamProperty Property;
        /// <summary>The group: «Longitudinali» (true) or «Traversi».</summary>
        internal bool Main;
        internal string SuperElement, BeamId;
        /// <summary>The pre-strain (Beam Pre Load, strain) and the gradient (Beam Gradient Load, axes 1 and 2) of a case, or none.</summary>
        internal (string Case, double PreStrain, double Axis1, double Axis2)? Shrinkage;
    }

    internal const string MainGroup = "Longitudinali", CrossGroup = "Traversi";
    internal string Name, Id;
    internal readonly List<Node> Nodes = new List<Node>();
    internal readonly List<Beam> Beams = new List<Beam>();
    /// <summary>The cases in the order of the former model (the analysis requests them by name).</summary>
    internal readonly List<string> Cases = new List<string>();
    /// <summary>Staged Construction (Morph, Rotate, groups «Longitudinali» and «Traversi»): one increment of one case with its factor.</summary>
    internal readonly List<(string Name, string Case, double Factor)> Stages = new List<(string Name, string Case, double Factor)>();
    internal readonly List<MovingPath> Paths = new List<MovingPath>();
    /// <summary>Whether the nodes are in the group «Longitudinali» (the nodes of the grillages; not those of the shrinkage benchmarks).</summary>
    internal bool NodesInMain = true;

    internal R4cModel(string id, string name) { Id = id; Name = name; }

    internal int AddNode(Point3d position, bool tx, bool ty, bool tz, bool rx, bool ry, bool rz)
    {
        Nodes.Add(new Node { Position = position, Restraint = new[] { tx, ty, tz, rx, ry, rz } });
        return Nodes.Count - 1;
    }

    internal void AddCase(string name) { if (!Cases.Contains(name)) Cases.Add(name); }

    internal Beam AddBeam(int from, int to, LegacyBeamProperty property, bool main, string beamId, string superElement = null)
    {
        var beam = new Beam { From = from, To = to, Property = property, Main = main, BeamId = beamId, SuperElement = superElement };
        Beams.Add(beam); return beam;
    }

    /// <summary>The GPC input of the recipe: Build Model on GPC of the drafts made in recipe order (identities derived from <see cref="Id"/> and
    /// the step: a rebuild gives the same GUIDs), N-mm, model tolerance 1E-6, then Attach Moving Paths with the paths.</summary>
    internal GpcModels.Conversion Build()
    {
        var b = new GpcNativeExamples.Builder("r66-r4cg:" + Id);
        var main = GpcCaseBuilders.Group(MainGroup, null); var cross = GpcCaseBuilders.Group(CrossGroup, null);
        foreach (var c in Cases) b.Case(c);
        for (int i = 0; i < Nodes.Count; i++)
        {
            var n = Nodes[i]; var r = n.Restraint;
            int k = b.Node(n.Position.X, n.Position.Y, n.Position.Z, (i + 1).ToString(CultureInfo.InvariantCulture), NodesInMain ? main : null);
            b.Restrain(k, r[0], r[1], r[2], r[3], r[4], r[5]);
            foreach (var (c, f) in n.Loads) b.Force(k, c, f.X, f.Y, f.Z);
        }
        foreach (var beam in Beams)
        {
            int k = b.Beam(beam.From, beam.To, beam.Property, beam.Main ? main : cross, beam.BeamId);
            if (beam.SuperElement != null) b.Beams[k] = GpcBeamBuilders.WithSuperElement(b.Beams[k], beam.SuperElement, b.Next("super-element"));
            if (beam.Shrinkage is { } s)
            {
                b.Beams[k] = GpcBeamBuilders.WithPreLoad(b.Beams[k], b.Case(s.Case), true, s.PreStrain, b.Next("pre-load"));
                b.Beams[k] = GpcBeamBuilders.WithGradient(b.Beams[k], b.Case(s.Case), s.Axis1, s.Axis2, b.Next("gradient"));
            }
        }
        foreach (var (name, c, factor) in Stages) b.Stage(name, new[] { main, cross }, b.Combination(name, (c, factor)));
        b.Paths.AddRange(Paths);
        return b.Build(Name).Input;
    }

    /// <summary>
    /// The GPC-only analysis of <paramref name="input"/> in <paramref name="folder"/> (created): <c>input.gpc</c> + <c>input.records</c> (BGHGPC1)
    /// and <c>results.bghr</c> (BGHR2) in place of the former <c>input.bgmodel</c> and <c>results.bgmodel</c>, <c>model.st7</c> and
    /// <c>warnings.json</c>. The input never takes results.
    /// </summary>
    internal static Model Solve(GpcModels.Conversion input, string folder, int solver, IReadOnlyCollection<string> cases, bool moving = false)
    {
        Directory.CreateDirectory(folder);
        var (data, records) = GpcDraftArchive.Write(input);
        File.WriteAllBytes(Path.Combine(folder, "input.gpc"), data); File.WriteAllBytes(Path.Combine(folder, "input.records"), records);
        var analysed = BridGHe.Common.Straus.StrausAnalysis.Run(input, Path.Combine(folder, "model.st7"), solver, cases, moving, out var warnings);
        File.WriteAllBytes(Path.Combine(folder, "results.bghr"), GpcResultsFile.ToBytes(analysed));
        File.WriteAllText(Path.Combine(folder, "warnings.json"), JsonSerializer.Serialize(warnings, BridgeGrillageExample.Json));
        if (input.Model.AllElements.Sum(e => e.Results.Count) + input.Model.Datasets.Count != 0) throw new Exception("R4C: the input took results: " + input.Model.Name);
        return analysed;
    }

    /// <summary>The input and results archived by <see cref="Solve"/> in <paramref name="folder"/> (the replay of the graphs).</summary>
    internal static (GpcModels.Conversion Input, Model Analysed) Read(string folder) =>
        (GpcDraftArchive.ReadConversion(File.ReadAllBytes(Path.Combine(folder, "input.gpc")), File.ReadAllBytes(Path.Combine(folder, "input.records"))),
         GpcResultsFile.FromBytes(File.ReadAllBytes(Path.Combine(folder, "results.bghr"))));

    /// <summary>
    /// The results of an analysed GPC model of a recipe through the GPC result readers (<see cref="GpcResultRows"/>): nodes and beams by their
    /// index in the recipe (GPC number − 1, checked: every node and beam of the analysed model at the position of the recipe, 1E-6 mm), results by
    /// their label in the catalog. Translations and reactions in the global axes, beam actions at the native stations (mm from the start).
    /// </summary>
    internal sealed class Results
    {
        internal Model Model { get; }
        internal GpcResultCatalog Catalog { get; }
        internal R4cModel Recipe { get; }
        private readonly Dictionary<int, GpcResultRows.NodeRow[]> nodes = new Dictionary<int, GpcResultRows.NodeRow[]>();
        private readonly Dictionary<int, GpcResultRows.BeamRow[]> beams = new Dictionary<int, GpcResultRows.BeamRow[]>();

        internal Results(Model analysed, R4cModel recipe)
        {
            Model = analysed ?? throw new ArgumentNullException(nameof(analysed)); Recipe = recipe ?? throw new ArgumentNullException(nameof(recipe));
            Catalog = GpcResultCatalog.Of(analysed) ?? throw new InvalidOperationException("R4C: the analysed model has no results catalog: " + analysed.Name);
            if (analysed.NodesElements.Count != recipe.Nodes.Count || analysed.BeamElements.Count != recipe.Beams.Count)
                throw new Exception($"R4C: {analysed.Name}: {analysed.NodesElements.Count} nodes and {analysed.BeamElements.Count} beams analysed, the recipe has {recipe.Nodes.Count} and {recipe.Beams.Count}");
            static bool Near(GPC.Geometry.Point3d a, Point3d b) => Math.Abs(a.X - b.X) <= 1e-6 && Math.Abs(a.Y - b.Y) <= 1e-6 && Math.Abs(a.Z - b.Z) <= 1e-6;
            for (int i = 0; i < recipe.Nodes.Count; i++)
                if (!Near(analysed.NodesElements[i + 1].Position, recipe.Nodes[i].Position)) throw new Exception($"R4C: {analysed.Name}: node {i + 1} is not at the position of the recipe");
            for (int i = 0; i < recipe.Beams.Count; i++)
            {
                var b = analysed.BeamElements[i + 1]; var r = recipe.Beams[i];
                if (!Near(b.StartPoint, recipe.Nodes[r.From].Position) || !Near(b.EndPoint, recipe.Nodes[r.To].Position)) throw new Exception($"R4C: {analysed.Name}: beam {i + 1} is not on the nodes of the recipe");
            }
        }

        internal string Label(int entry) => Catalog.Entries[entry].Label;
        internal GpcResultRows.NodeRow[] NodeRows(int node) =>
            nodes.TryGetValue(node, out var rows) ? rows : nodes[node] = GpcResultRows.Node(Catalog, Model, Model.NodesElements[node + 1]).ToArray();
        internal GpcResultRows.BeamRow[] BeamRows(int beam) =>
            beams.TryGetValue(beam, out var rows) ? rows : beams[beam] = GpcResultRows.Beam(Catalog, Model, Model.BeamElements[beam + 1]).ToArray();
        internal IEnumerable<GpcResultRows.BeamRow> BeamRows(int beam, string label) => BeamRows(beam).Where(r => Label(r.Entry) == label);

        /// <summary>Ux, Uy, Uz, Rx, Ry, Rz of a node in a result (the former <c>Displ(node, name)</c>).</summary>
        internal double[] Displacement(int node, string label) => NodeRows(node).Single(r => r.Displacement != null && Label(r.Entry) == label).Displacement;
        internal Vector3d Translation(int node, string label) { var d = Displacement(node, label); return new Vector3d(d[0], d[1], d[2]); }

        /// <summary>Fx, Fy, Fz, Mx, My, Mz of the reaction of a node in a result; zeros when the node has no reaction (a free node: the former
        /// reaction result of a free node was zero).</summary>
        internal double[] Reaction(int node, string label) => NodeRows(node).FirstOrDefault(r => r.Reaction != null && Label(r.Entry) == label)?.Reaction ?? new double[6];
        internal Vector3d ReactionForce(int node, string label) { var r = Reaction(node, label); return new Vector3d(r[0], r[1], r[2]); }
        internal Vector3d ReactionMoment(int node, string label) { var r = Reaction(node, label); return new Vector3d(r[3], r[4], r[5]); }

        /// <summary>The beam actions at the first station of a result (the former <c>OrderBy(Station).First()</c>), or at the fifth (the end).</summary>
        internal GpcResultRows.BeamRow Force(int beam, string label, bool end = false) => BeamRows(beam, label).OrderBy(r => r.Station).ElementAt(end ? 4 : 0);

        internal IReadOnlyList<(int Number, string Name)> GeneratedMovingCases => Catalog.GeneratedMovingCases();
    }

    /// <summary>The sample of a beam row in the BridGHe convention (SF1, SF2, BM1, BM2: the numbers of the former result objects).</summary>
    internal static BridGHe.Common.Checks.BeamForceSample Sample(GpcResultRows.BeamRow r) =>
        new BridGHe.Common.Checks.BeamForceSample(r.Station, r.Axial, r.Shear1, r.Shear2, r.Torque, r.Bending1, r.Bending2);

    /// <summary>A Generic beam property (type Beam) of the examples: area, inertias and torsion, no shear areas, on <paramref name="material"/>.</summary>
    internal static LegacyBeamProperty Generic(string name, double area, double i11, double i22, double j, GPC.Model.Materials.Material material) =>
        GpcPropertyBuilders.Beam(name, 5, GpcSectionBuilders.Generic(area, i11, i22, j, 0, 0, 0, 0, 0, 0, 0), material);

    /// <summary>The steel of the former examples (<c>BridgeGrillageExample.Steel()</c>: S355, E 210000, ν 0.3, fy 355, fu 510, no kind, Use
    /// Poisson), with the thermal expansion given (the equivalent strain device of the shrinkage models: 1E-5).</summary>
    internal static GPC.Model.Materials.Material Steel(double thermalExpansion = 0) => GpcMaterialBuilders.Material(new LegacyMaterialRecipe
    {
        Name = "S355", Modulus = BridgeGrillageExample.Es, PoissonRatio = BridgeGrillageExample.NuSteel, MinimumYieldStress = 355, MinimumTensileStress = 510,
        ThermalExpansion = thermalExpansion,
    });
}
#endif
