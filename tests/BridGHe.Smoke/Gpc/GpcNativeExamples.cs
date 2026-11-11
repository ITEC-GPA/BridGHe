#if GPC_PILOT
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using BridGHe.Common.Gpc;
using BridGHe.Common.Gpc.Builders;
using BridGHe.Common.Gpc.Legacy;
using BridGHe.Common.MovingLoads;
using GPC.Model.Elements;
using GPC.Model.Models;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Parameters;
using Grasshopper.Kernel.Types;

/// <summary>
/// R6.6 lane R0: the numerical integration benchmarks 01–10 of <see cref="NativeExamples"/> built directly as GPC models, without any FEM model:
/// the GPC data the GH components give (the product builders <see cref="GpcNodeBuilders"/>, <see cref="GpcBeamBuilders"/>, <see cref="GpcPlateBuilders"/>,
/// <see cref="GpcCaseBuilders"/>, <see cref="GpcPropertyBuilders"/>, <see cref="GpcSectionBuilders"/>, <see cref="GpcMaterialBuilders"/>), assembled by
/// Build Model on GPC (<see cref="LegacyBuildModel.Build"/>) and, for the moving example, Attach Moving Paths on GPC
/// (<see cref="GpcModelTransforms.ReplacePaths"/>). N-mm, linear elastic material, no gravity: the same geometry, properties, loads, groups, stages,
/// cases, combinations and moving data as the FEM catalogue, and the same analytic expected values and tolerances (<see cref="Delta"/>,
/// <see cref="NativeExamples.Recorder.Near"/>), read on the analysed GPC model with the GPC result readers (<see cref="GpcResultRows"/>).
/// <para>The fixtures of lanes R1–R4: every example gives the inputs of Build Model (<see cref="Inputs"/>: drafts, combinations, stages, moving paths)
/// and the GPC input they assemble (<see cref="Example.Input"/>). Identities are derived from the example and the step of each builder: a
/// rebuild gives the same GUIDs. <see cref="RequireSameAsFem"/> (transitional, retired by P2 with the FEM catalogue) proves that each GPC example
/// is the conversion of its FEM model, field by field.</para>
/// </summary>
internal static class GpcNativeExamples
{
    /// <summary>The inputs of Build Model (and of Attach Moving Paths) of an example, in the order of the elements of its FEM model.</summary>
    internal sealed record Inputs(IReadOnlyList<LegacyNodeDraft> Nodes, IReadOnlyList<LegacyBeamDraft> Beams, IReadOnlyList<LegacyPlateDraft> Plates,
        IReadOnlyList<LegacyCombinationDraft> Combinations, IReadOnlyList<LegacyStageDraft> Stages, IReadOnlyList<MovingPath> Paths);

    /// <summary>An example: its inputs, its GPC input (Build Model, then Attach Moving Paths when it has paths), the analysis and the checks on the
    /// analysed GPC model.</summary>
    internal sealed record Example(string Id, string Title, int Solver, bool Moving, Inputs Inputs, GpcModels.Conversion Input, string[] Cases, Action<Results, NativeExamples.Recorder> Verify);

    /// <summary>The model tolerance of the examples (definition units), as the FEM natives gave the analysis.</summary>
    internal const double Tolerance = 1e-6;

    private const double E = 210000, A = 60000, I = 450000000, L = 3000, P = 1000;
    private static readonly IReadOnlyList<string> Main = GpcCaseBuilders.Group("Permanent", null), Added = GpcCaseBuilders.Group("Added", null);
    private static double Delta(double force = P, double length = L) => -force * Math.Pow(length, 3) / (3 * E * I);

    /// <summary>The material of the examples (the former <c>MaterialModel("S355") { Modulus = E, PoissonRatio = 0.3 }</c>: no kind, Use Poisson).</summary>
    internal static GPC.Model.Materials.Material Steel() => GpcMaterialBuilders.Material(new LegacyMaterialRecipe { Name = "S355", Modulus = E, PoissonRatio = 0.3 });

    /// <summary>The elastic beam property of the examples (Beam Property: type Beam, generic section A, I11, I22 2E8, J 1E8, no shear areas).</summary>
    internal static LegacyBeamProperty Property() => GpcPropertyBuilders.Beam("Elastic beam", 5, GpcSectionBuilders.Generic(A, I, 200000000, 100000000, 0, 0, 0, 0, 0, 0, 0), Steel());

    /// <summary>The GPC data of one example, built in the order of its FEM model, with identities derived from the example and the step.</summary>
    internal sealed class Builder
    {
        private readonly string id; private int step;
        private readonly Dictionary<string, LegacyCaseData> cases = new Dictionary<string, LegacyCaseData>(StringComparer.Ordinal);
        internal readonly List<LegacyNodeDraft> Nodes = new List<LegacyNodeDraft>();
        internal readonly List<LegacyBeamDraft> Beams = new List<LegacyBeamDraft>();
        internal readonly List<LegacyPlateDraft> Plates = new List<LegacyPlateDraft>();
        internal readonly List<LegacyCombinationDraft> Combinations = new List<LegacyCombinationDraft>();
        internal readonly List<LegacyStageDraft> Stages = new List<LegacyStageDraft>();
        internal readonly List<MovingPath> Paths = new List<MovingPath>();

        internal Builder(string id) { this.id = id; }

        internal Guid Next(string what) => GpcIdentity.Derive(GpcIdentity.NameSpace, "r66-r0-example:" + id + ":" + what + ":" + (++step).ToString(CultureInfo.InvariantCulture));

        /// <summary>The case of a name (Load Case component, no non-structural mass), one record per name.</summary>
        internal LegacyCaseData Case(string name) => cases.TryGetValue(name, out var c) ? c : cases[name] = GpcCaseBuilders.Case(name, false);

        internal LegacyCombinationDraft Combination(string name, params (string Case, double Factor)[] terms) =>
            GpcCaseBuilders.Combination(name, terms.Select(t => Case(t.Case)).ToArray(), terms.Select(t => t.Factor).ToArray());

        /// <summary>Staged Construction: Morph and Rotate, the groups given, one increment per combination.</summary>
        internal void Stage(string name, IReadOnlyList<string>[] groups, params LegacyCombinationDraft[] increments) =>
            Stages.Add(GpcCaseBuilders.Stage(name, groups, increments, true, true));

        internal int Node(double x, double y, double z, string nodeId, IReadOnlyList<string> group)
        {
            Nodes.Add(GpcNodeBuilders.Create(x, y, z, nodeId, group == null ? null : new[] { group }, Next("node")));
            return Nodes.Count - 1;
        }

        internal void Restrain(int node, bool tx, bool ty, bool tz, bool rx, bool ry, bool rz) =>
            Nodes[node] = GpcNodeBuilders.WithRestraint(Nodes[node], tx, ty, tz, rx, ry, rz, Next("restraint"));

        internal void Force(int node, string loadCase, double fx, double fy, double fz) =>
            Nodes[node] = GpcNodeBuilders.WithForce(Nodes[node], Case(loadCase), fx, fy, fz, Next("force"));

        internal int Beam(int from, int to, LegacyBeamProperty property, IReadOnlyList<string> group, string beamId)
        {
            Beams.Add(GpcBeamBuilders.New(Nodes[from].Node.Position, Nodes[to].Node.Position, property, new[] { group }, 0, 0, 0, Next("beam"), beamId));
            return Beams.Count - 1;
        }

        /// <summary>Beam Distributed Load: uniform force along global Z, intensity <paramref name="q"/> at both ends.</summary>
        internal void UniformZ(int beam, string loadCase, double q) =>
            Beams[beam] = GpcBeamBuilders.WithDistributedLoad(Beams[beam], Case(loadCase), BridgeLoadSchema.Uniform, false, "Z", q, q, 0, 0, 0, 0, true, Next("distributed"));

        /// <summary>Build Model on GPC (N-mm, the model tolerance of the examples), then Attach Moving Paths when the example has paths.</summary>
        internal (Inputs Inputs, GpcModels.Conversion Input) Build(string name)
        {
            var built = LegacyBuildModel.Build(name, new BridgeUnits(DefinitionUnits.Nmm), Tolerance, Nodes.Select(d => (d.Node.Guid, d)).ToList(), Beams.Select(d => (d.Beam.Guid, d)).ToList(),
                Plates.Select(d => (d.Plate.Guid, d)).ToList(), Combinations.ToList(), Stages.ToList());
            if (Paths.Count != 0) built = GpcModelTransforms.ReplacePaths(built, Paths, GpcIdentity.Derive(GpcIdentity.NameSpace, "r66-r0-example:" + id + ":paths"));
            return (new Inputs(Nodes.ToArray(), Beams.ToArray(), Plates.ToArray(), Combinations.ToArray(), Stages.ToArray(), Paths.ToArray()), built);
        }
    }

    /// <summary>The cantilever or the beam of the FEM catalogue (<c>NativeExamples.Beam</c>): <paramref name="count"/> beams along X in the group
    /// «Permanent», fixed at the first node, or simply supported (the first node pinned, the last on a roller, out of plane fixed).</summary>
    internal static Builder Beam(string id, int count = 1, double length = L, bool supported = false)
    {
        var b = new Builder(id); var property = Property();
        for (int i = 0; i <= count; i++)
        {
            int n = b.Node(length * i / count, 0, 0, (i + 1).ToString(CultureInfo.InvariantCulture), Main);
            if (!supported && i == 0) b.Restrain(n, true, true, true, true, true, true);
            if (supported) b.Restrain(n, i == 0, true, i == 0 || i == count, true, false, true);
        }
        for (int i = 0; i < count; i++) b.Beam(i, i + 1, property, Main, (i + 1).ToString(CultureInfo.InvariantCulture));
        return b;
    }

    /// <summary>The moving path of example 04 (one axle of 1000 N on the beam of 6000 mm, 13 positions, static cases), or with the library item given.</summary>
    internal static MovingPath Path04(double length, MovingTemplate template = null) => new MovingPath
    {
        LoadCaseName = "Traffic", Divisions = 13, Target = 2, Coordinates = new double[] { 0, 0, 0, length, 0, 0, 0, 1000, 0 }, ConvertToStaticCases = true,
        Template = template ?? new MovingTemplate { Name = "Single axle", Vehicles = new[] { new MovingVehicle { Name = "1000 N", Loads = new MovingObject[] { new MovingPointForce { Force = new double[] { 0, 0, -P } } } } } },
    };

    /// <summary>The results of an analysed GPC model through the GPC result readers (<see cref="GpcResultRows"/>, the rows of the Results components):
    /// elements by their index in the FEM catalogue (GPC number − 1), results by their label in the catalog.</summary>
    internal sealed class Results
    {
        internal Model Model { get; }
        internal GpcResultCatalog Catalog { get; }
        internal Results(Model analysed)
        {
            Model = analysed ?? throw new ArgumentNullException(nameof(analysed));
            Catalog = GpcResultCatalog.Of(analysed) ?? throw new InvalidOperationException("GpcNativeExamples: the analysed model has no results catalog");
        }
        private string Label(int entry) => Catalog.Entries[entry].Label;
        internal int NodeCount => Model.NodesElements.Count;
        internal IEnumerable<GpcResultRows.NodeRow> NodeRows(int node) => GpcResultRows.Node(Catalog, Model, Model.NodesElements[node + 1]);
        internal IEnumerable<GpcResultRows.NodeRow> AllNodeRows() => Model.NodesElements.Values.OrderBy(n => n.Id).SelectMany(n => GpcResultRows.Node(Catalog, Model, n));
        internal double[] Displacement(int node, string label) => NodeRows(node).Single(r => r.Displacement != null && Label(r.Entry) == label).Displacement;
        internal double[] Reaction(int node, string label) => NodeRows(node).Single(r => r.Reaction != null && Label(r.Entry) == label).Reaction;
        internal IEnumerable<GpcResultRows.BeamRow> BeamRows(int beam, string label = null) =>
            GpcResultRows.Beam(Catalog, Model, Model.BeamElements[beam + 1]).Where(r => label == null || Label(r.Entry) == label);
        internal IEnumerable<GpcResultRows.BeamRow> AllBeamRows(string label = null) =>
            Model.BeamElements.Values.OrderBy(b => b.Id).SelectMany(b => GpcResultRows.Beam(Catalog, Model, b)).Where(r => label == null || Label(r.Entry) == label);
        /// <summary>The beam actions at the first station of a result (the former <c>OrderBy(Station).First()</c>).</summary>
        internal GpcResultRows.BeamRow Force(int beam, string label) => BeamRows(beam, label).OrderBy(r => r.Station).First();
        internal IEnumerable<GpcResultRows.PlateRow> PlateRows(int plate) => GpcResultRows.Plate(Catalog, Model, Model.AreaElements[plate + 1]);
        internal IReadOnlyList<(int Number, string Name)> GeneratedMovingCases => Catalog.GeneratedMovingCases();
    }

    private static void CheckCantilever(Results m, NativeExamples.Recorder r, string name, double factor)
    {
        r.Near(name + " / Uz [mm]", m.Displacement(m.NodeCount - 1, name)[2], Delta() * factor);
        r.Near(name + " / Rz [N]", m.Reaction(0, name)[2], P * factor);
        r.Near(name + " / My reaction [N mm]", m.Reaction(0, name)[4], -P * L * factor);
        r.Near(name + " / beam |My| [N mm]", Math.Abs(m.Force(0, name).Bending2), P * L * Math.Abs(factor));
    }

    /// <summary>The ten examples, in the order and with the checks of the FEM catalogue.</summary>
    internal static IEnumerable<Example> Create()
    {
        Example Make(Builder b, string id, string title, int solver, bool moving, string[] cases, Action<Results, NativeExamples.Recorder> verify)
        {
            var (inputs, input) = b.Build(title);
            return new Example(id, title, solver, moving, inputs, input, cases, verify);
        }
        {
            var b = Beam("01-cantilever"); int tip = b.Nodes.Count - 1; b.Force(tip, "P", 0, 0, -600); b.Force(tip, "P", 0, 0, -400);
            b.Combinations.Add(b.Combination("1.5P", ("P", 1.5)));
            yield return Make(b, "01-cantilever", "Mensola: somma carichi nodali e combinazione", 0, false, new[] { "P", "1.5P" }, (x, r) => { CheckCantilever(x, r, "P", 1); CheckCantilever(x, r, "1.5P", 1.5); });
        }
        {
            const double length = 6000, q = 1; var b = Beam("02-simply-supported", 4, length, true);
            for (int k = 0; k < b.Beams.Count; k++) b.UniformZ(k, "q", -q);
            yield return Make(b, "02-simply-supported", "Trave appoggiata con carico uniforme", 0, false, new[] { "q" }, (x, r) =>
            {
                r.Near("Uz midspan [mm]", x.Displacement(2, "q")[2], -5 * q * Math.Pow(length, 4) / (384 * E * I));
                r.Near("Rz left [N]", x.Reaction(0, "q")[2], q * length / 2); r.Near("Rz right [N]", x.Reaction(4, "q")[2], q * length / 2);
                r.Near("Max |My| [N mm]", x.AllBeamRows().Max(f => Math.Abs(f.Bending2)), q * length * length / 8);
            });
        }
        {
            const double length = 1000, width = 500, thickness = 10, force = 2000;
            var b = new Builder("03-plate-patch");
            var points = new[] { (0.0, 0.0, 0.0), (length, 0.0, 0.0), (length, width, 0.0), (0.0, width, 0.0) };
            for (int i = 0; i < 4; i++)
            {
                int n = b.Node(points[i].Item1, points[i].Item2, points[i].Item3, (i + 1).ToString(CultureInfo.InvariantCulture), null);
                b.Restrain(n, i == 0 || i == 3, i == 0, true, true, true, true);
                if (i == 1 || i == 2) b.Force(n, "Tension", force / 2, 0, 0);
            }
            var property = GpcPropertyBuilders.Plate("Shell patch", 0, Steel(), thickness, thickness);
            b.Plates.Add(GpcPlateBuilders.Create(points, property, null, 0, 0, "1", b.Next("plate")));
            yield return Make(b, "03-plate-patch", "Patch test membranale di una piastra shell", 0, false, new[] { "Tension" }, (x, r) =>
            {
                r.Near("Ux right [mm]", x.Displacement(1, "Tension")[0], force * length / (E * width * thickness));
                r.Near("Poisson Uy [mm]", x.Displacement(2, "Tension")[1], -.3 * force / (E * thickness));
                r.Near("Sum Rx [N]", x.AllNodeRows().Where(n => n.Reaction != null).Sum(f => f.Reaction[0]), -force);
                foreach (var f in x.PlateRows(0)) r.Near("Plate Nxx [N/mm]", f.Values[0], force / width);
                r.Near("Plate result nodes", x.PlateRows(0).Count(), 4, 0, 0);
            });
        }
        {
            const double length = 6000; var b = Beam("04-moving-load", 12, length, true);
            b.Paths.Add(Path04(length));
            yield return Make(b, "04-moving-load", "Asse mobile su trave appoggiata", 0, true, Array.Empty<string>(), (x, r) =>
            {
                r.Near("Generated load positions", x.GeneratedMovingCases.Count, 13, 0, 0);
                int step = 0;
                foreach (string name in x.GeneratedMovingCases.Select(c => c.Name))
                {
                    double left = x.Reaction(0, name)[2], right = x.Reaction(12, name)[2];
                    r.Near(name + " / equilibrium [N]", left + right, P);
                    double position = (++step - .5) * length / 13;
                    r.Near(name + " / right reaction [N]", right, P * position / length);
                    double moment = x.AllBeamRows(name).Max(f => Math.Abs(f.Bending2));
                    r.Near(name + " / max moment [N mm]", moment, P * position * (length - position) / length, 1e-5, 1e-5);
                }
                r.Near("Envelope max |My| [N mm]", x.AllBeamRows().Max(f => Math.Abs(f.Bending2)), P * length / 4);
            });
        }
        {
            var b = Beam("05-nonlinear-increments"); b.Force(b.Nodes.Count - 1, "P", 0, 0, -P);
            b.Combinations.Add(b.Combination("Half", ("P", .5))); b.Combinations.Add(b.Combination("Full", ("P", 1))); b.Combinations.Add(b.Combination("Double", ("P", 2)));
            yield return Make(b, "05-nonlinear-increments", "Incrementi del solutore non lineare in campo elastico", 1, false, new[] { "Half", "Double" }, (x, r) => { CheckCantilever(x, r, "Half", .5); CheckCantilever(x, r, "Double", 2); });
        }
        {
            var b = Beam("06-stage-loading"); b.Force(b.Nodes.Count - 1, "G", 0, 0, -P);
            b.Stage("Steel", new[] { Main }, b.Combination("Load", ("G", 1))); b.Stage("Deck", new[] { Main }, b.Combination("Load", ("G", 1.5)));
            yield return Make(b, "06-stage-loading", "Fasi: aumento del carico permanente", 2, false, new[] { "Load [Steel]", "Load [Deck]" }, (x, r) => { CheckCantilever(x, r, "Load [Steel]", 1); CheckCantilever(x, r, "Load [Deck]", 1.5); });
        }
        {
            var b = Beam("07-stage-unloading"); b.Force(b.Nodes.Count - 1, "G", 0, 0, -P);
            b.Stage("Loading", new[] { Main }, b.Combination("Step", ("G", 1))); b.Stage("Unloading", new[] { Main }, b.Combination("Step", ("G", 0))); b.Stage("Reloading", new[] { Main }, b.Combination("Step", ("G", .5)));
            yield return Make(b, "07-stage-unloading", "Fasi: scarico e ricarico elastico", 2, false, new[] { "Step [Loading]", "Step [Unloading]", "Step [Reloading]" },
                (x, r) => { CheckCantilever(x, r, "Step [Loading]", 1); CheckCantilever(x, r, "Step [Unloading]", 0); CheckCantilever(x, r, "Step [Reloading]", .5); });
        }
        for (int index = 8; index <= 9; index++)
        {
            string id = index == 8 ? "08-stage-activation" : "09-stage-removal";
            var b = Beam(id); b.Force(b.Nodes.Count - 1, "Axial", P, 0, 0);
            b.Beam(0, 1, Property(), Added, "2");
            b.Stage("Original", new[] { Main }, b.Combination("Step", ("Axial", 1)));
            b.Stage("Reinforced", new[] { Main, Added }, b.Combination("Step", ("Axial", 2)));
            if (index == 9) b.Stage("Removed", new[] { Main }, b.Combination("Step", ("Axial", 2)));
            bool remove = index == 9;
            string title = remove ? "Fasi: rimozione di un elemento provvisorio" : "Fasi: aggiunta di un elemento senza tensioni iniziali";
            var (inputs, input) = b.Build(title);
            yield return new Example(id, title, 2, false, inputs, input, GpcResultSelection.Catalog(input.Model, 2).Select(c => c.Name).ToArray(), (x, r) =>
            {
                double d = P * L / (E * A);
                r.Near("Original Ux [mm]", x.Displacement(1, "Step [Original]")[0], d);
                r.Near("Reinforced Ux [mm]", x.Displacement(1, "Step [Reinforced]")[0], 1.5 * d, 1e-5);
                r.Near("Original member N after addition [N]", x.Force(0, "Step [Reinforced]").Axial, 1.5 * P);
                r.Near("Added member N [N]", x.Force(1, "Step [Reinforced]").Axial, .5 * P);
                r.Near("No results before activation", x.BeamRows(1, "Step [Original]").Count(), 0, 0, 0);
                if (remove)
                {
                    r.Near("After removal Ux [mm]", x.Displacement(1, "Step [Removed]")[0], 2 * d); r.Near("Remaining member N [N]", x.Force(0, "Step [Removed]").Axial, 2 * P);
                    r.Near("No results after removal", x.BeamRows(1, "Step [Removed]").Count(), 0, 0, 0);
                }
            });
        }
        {
            var b = Beam("10-stage-selection"); int tip = b.Nodes.Count - 1; b.Force(tip, "G", 0, 0, -P); b.Force(tip, "Q", 0, 0, -P);
            b.Stage("Construction", new[] { Main }, b.Combination("Half", ("G", .5)), b.Combination("Full", ("G", 1)));
            b.Stage("Service", new[] { Main }, b.Combination("SLS", ("G", 1), ("Q", .5)), b.Combination("ULS", ("G", 1.35), ("Q", 1.5)));
            yield return Make(b, "10-stage-selection", "Fasi: piu incrementi, casi diversi e selezione parziale", 2, false, new[] { "Full [Construction]", "ULS [Service]" }, (x, r) =>
            {
                CheckCantilever(x, r, "Full [Construction]", 1); CheckCantilever(x, r, "ULS [Service]", 2.85);
                r.Near("Only requested cases", x.NodeRows(1).Count(n => n.Displacement != null), 2, 0, 0);
            });
        }
    }

    // -----------------------------------------------------------------------------------------------------------------------------------------
    // The native run of --examples on the GPC path.

    /// <summary><c>--examples</c>: examples 01–10 on their GPC input through the GPC-only analysis (<see cref="BridGHe.Common.Straus.StrausAnalysis.Run(GpcModels.Conversion, string, int, IReadOnlyCollection{string}, bool, out List{string})"/>),
    /// checked on the analysed GPC model by the GPC result readers with the analytic values; then Node, Beam and Plate Results on the GPC-only
    /// model, every output equal to the reader rows bit for bit; the input never takes results. The input (BGHGPC1), the results (BGHR2) and the
    /// report of each example go into its calculation folder.</summary>
    internal static int Run(string selected)
    {
        string runId = DateTime.Now.ToString("yyyyMMdd-HHmmssfff");
        string root = Path.Combine(ExamplePaths.Validation, "esecuzioni", runId); Directory.CreateDirectory(root);
        var reports = new List<object>(); int failures = 0, count = 0;
        foreach (var example in Create().Where(x => selected == "all" || x.Id.StartsWith(selected, StringComparison.Ordinal)))
        {
            count++; Console.WriteLine("EXAMPLE " + example.Id); var record = new NativeExamples.Recorder(); string error = null; List<string> warnings = new List<string>();
            string folder = Path.Combine(ExamplePaths.Calculations(example.Id), "native", runId); Directory.CreateDirectory(folder);
            var (data, records) = GpcDraftArchive.Write(example.Input);
            File.WriteAllBytes(Path.Combine(folder, "input.gpc"), data); File.WriteAllBytes(Path.Combine(folder, "input.records"), records);
            try
            {
                // Exercise relative paths with '/' against the real native solver.
                // Before canonicalisation St7NewFile succeeded but St7RunSolver
                // failed with 1029 (cannot write result file).
                string solverPath = example.Id == "01-cantilever" ? Path.GetRelativePath(Environment.CurrentDirectory, Path.Combine(folder, "model.st7")).Replace('\\', '/') : Path.Combine(folder, "model.st7");
                var analysed = BridGHe.Common.Straus.StrausAnalysis.Run(example.Input, solverPath, example.Solver, example.Cases, example.Moving, out warnings);
                GpcResultsFile.Write(Path.Combine(folder, "results.bghr"), analysed);
                example.Verify(new Results(analysed), record);
                // R6.6 R0 (replaces the C4 check against the FEM results): Node, Beam and Plate Results on the GPC-only model show the reader rows.
                long values = ComponentsCheck(example.Input, analysed, example.Id);
                record.Near("Results components on the GPC-only model: outputs equal to the GPC reader rows bit for bit", values > 0 ? 1 : 0, 1, 0, 0);
                record.Near("Input remains without results", example.Input.Model.AllElements.Sum(e => e.Results.Count) + example.Input.Model.Datasets.Count, 0, 0, 0);
            }
            catch (Exception ex) { error = ex.ToString(); Console.WriteLine("ERROR: " + error); }
            bool passed = error == null && record.Measurements.All(m => m.Passed); if (!passed) failures++;
            var report = new { example.Id, example.Title, example.Solver, Passed = passed, Warnings = warnings, Error = error, record.Measurements };
            reports.Add(report);
            File.WriteAllText(Path.Combine(folder, "report.json"), System.Text.Json.JsonSerializer.Serialize(report, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        }
        File.WriteAllText(Path.Combine(root, "report.json"), System.Text.Json.JsonSerializer.Serialize(new { Date = DateTimeOffset.Now, Count = count, Failures = failures, Examples = reports }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        if (selected == "all" && count == 10 && failures == 0) File.WriteAllText(Path.Combine(ExamplePaths.Validation, "latest-passed.txt"), runId);
        Console.WriteLine($"EXAMPLES {count}, FAILURES {failures}; report: {root}"); return count > 0 && failures == 0 ? 0 : 1;
    }

    /// <summary>
    /// Node, Beam and Plate Results on the GPC-only model of an analysis (<c>GH_Model.FromGpc</c> of the input, the analysed model attached, as the
    /// analysis components give it): Decompose Model, then each element through its Results component; every output (texts, numbers, vectors; a 2D
    /// vector as the 3D vector of the parameter, z = 0) equal bit for bit to the rows of the GPC readers in the layout of the component
    /// (<see cref="GpcResultRows"/>, zeros shown without sign). The number of values compared (each element type present must give values).
    /// </summary>
    internal static long ComponentsCheck(GpcModels.Conversion input, Model analysed, string label)
    {
        void Require(bool condition, string what) { if (!condition) throw new Exception("GPC RESULTS COMPONENTS FAIL (" + label + "): " + what); }
        var assembly = Assembly.LoadFrom(BuildPaths.GrasshopperAssembly);
        var modelType = assembly.GetType("BridGHe.Grasshopper.DataTypes.Fem.GH_Model", true);
        var goo = (IGH_Goo)modelType.GetMethod("FromGpc", BindingFlags.Public | BindingFlags.Static).Invoke(null, new object[] { input });
        modelType.GetMethod("AttachGpc").Invoke(goo, new object[] { analysed });
        Require(modelType.GetProperty("Value").GetValue(goo) == null, "the GH model has no FEM value");
        using var document = new GH_Document { Enabled = true };
        GH_Document.EnableSolutions = true;
        GH_Component Make(string name)
        {
            var c = (GH_Component)Activator.CreateInstance(assembly.GetTypes().Single(t => t.Name == name));
            c.CreateAttributes(); document.AddObject(c, false); return c;
        }
        var decompose = Make("DecomposeModelComponent");
        ((Param_GenericObject)decompose.Params.Input[0]).PersistentData.Append(goo);
        document.NewSolution(false, GH_SolutionMode.Silent);
        Require(decompose.RuntimeMessages(GH_RuntimeMessageLevel.Error).Count == 0, "Decompose Model: " + string.Join("; ", decompose.RuntimeMessages(GH_RuntimeMessageLevel.Error)));
        var elements = Enumerable.Range(0, 3).Select(k => decompose.Params.Output[k].VolatileData.AllData(true).ToArray()).ToArray();
        Require(elements[0].Length == analysed.NodesElements.Count && elements[1].Length == analysed.BeamElements.Count && elements[2].Length == analysed.AreaElements.Count,
            $"Decompose Model gives {elements[0].Length}/{elements[1].Length}/{elements[2].Length} elements, the analysed model has {analysed.NodesElements.Count}/{analysed.BeamElements.Count}/{analysed.AreaElements.Count}");
        var catalog = GpcResultCatalog.Of(analysed);
        string[] components = { "NodeResultsComponent", "BeamResultsComponent", "PlateResultsComponent" };
        var present = Enumerable.Range(0, 3).Where(k => elements[k].Length > 0).ToArray();
        var made = new GH_Component[3]; var expected = new Dictionary<int, List<string>[]>[3];
        foreach (int k in present)
        {
            made[k] = Make(components[k]); expected[k] = new Dictionary<int, List<string>[]>();
            var parameter = (Param_GenericObject)made[k].Params.Input[0];
            for (int i = 0; i < elements[k].Length; i++)
            {
                var link = elements[k][i].GetType().GetProperty("AnalysedGpc").GetValue(elements[k][i]) as Element;
                Require(link != null && analysed.AllElements.Contains(link), components[k] + " " + i + ": linked to its analysed GPC element");
                parameter.PersistentData.Append(elements[k][i], new GH_Path(i));
                expected[k][i] = Rows(k, catalog, analysed, link);
            }
            parameter.ExpireSolution(false);
        }
        document.NewSolution(false, GH_SolutionMode.Silent);
        long values = 0;
        foreach (int k in present)
        {
            var c = made[k]; long elementValues = 0;
            Require(c.RuntimeMessages(GH_RuntimeMessageLevel.Error).Count == 0 && c.RuntimeMessages(GH_RuntimeMessageLevel.Warning).Count == 0,
                components[k] + " without messages: " + string.Join("; ", c.RuntimeMessages(GH_RuntimeMessageLevel.Error).Concat(c.RuntimeMessages(GH_RuntimeMessageLevel.Warning))));
            for (int o = 0; o < c.Params.Output.Count; o++)
            {
                var data = c.Params.Output[o].VolatileData; var byElement = new Dictionary<int, List<string>>();
                for (int p = 0; p < data.PathCount; p++)
                {
                    int element = data.get_Path(p).Indices[0];
                    if (!byElement.TryGetValue(element, out var list)) byElement.Add(element, list = new List<string>());
                    foreach (var item in data.get_Branch(p)) list.Add(Token(item));
                }
                Require(byElement.Keys.All(i => i < elements[k].Length), components[k] + " / " + c.Params.Output[o].Name + ": no branch beyond the elements");
                for (int i = 0; i < elements[k].Length; i++)
                {
                    var x = byElement.TryGetValue(i, out var xa) ? xa : new List<string>(); var y = expected[k][i][o];
                    string output = components[k] + " / " + c.Params.Output[o].Name + ", element " + i;
                    Require(x.Count == y.Count, $"{output}: {x.Count} values from the component, {y.Count} reader rows");
                    for (int j = 0; j < x.Count; j++) Require(x[j] == y[j], $"{output}, value {j}: component {x[j]} vs reader {y[j]}");
                    values += x.Count; elementValues += x.Count;
                }
            }
            Require(elementValues > 0, components[k] + ": values from the readers");
        }
        Console.WriteLine($"GPC RESULTS COMPONENTS PASS ({label}): {elements[0].Length} nodes, {elements[1].Length} beams, {elements[2].Length} plates of the GPC-only model, {values} output values equal to the GPC reader rows bit for bit.");
        return values;
    }

    /// <summary>The outputs of Node (k = 0), Beam (1) and Plate (2) Results for an analysed element, from the GPC reader rows, in output order.</summary>
    private static List<string>[] Rows(int k, GpcResultCatalog catalog, Model analysed, Element element)
    {
        static double Z(double v) => GpcResultRows.Shown(v);
        string S(string v) => Token(new GH_String(v)); string N(double v) => Token(new GH_Number(v));
        string V(double x, double y, double z) => Token(new GH_Vector(new Rhino.Geometry.Vector3d(x, y, z)));
        var o = Enumerable.Range(0, k == 0 ? 8 : k == 1 ? 7 : 15).Select(_ => new List<string>()).ToArray();
        string Label(int entry) => catalog.Entries[entry].Label;
        if (k == 0)
            foreach (var r in GpcResultRows.Node(catalog, analysed, (NodeElement)element))
            {
                var d = r.Displacement; var f = r.Reaction;
                if (d != null) { o[0].Add(S(Label(r.Entry))); o[1].Add(N(0)); o[2].Add(V(Z(d[0]), Z(d[1]), Z(d[2]))); o[3].Add(V(Z(d[3]), Z(d[4]), Z(d[5]))); }
                if (f != null) { o[4].Add(S(Label(r.Entry))); o[5].Add(N(0)); o[6].Add(V(Z(f[0]), Z(f[1]), Z(f[2]))); o[7].Add(V(Z(f[3]), Z(f[4]), Z(f[5]))); }
            }
        else if (k == 1)
            foreach (var r in GpcResultRows.Beam(catalog, analysed, (BeamElement)element))
            {
                o[0].Add(S(Label(r.Entry))); o[1].Add(N(Z(r.Station))); o[2].Add(N(0)); o[3].Add(V(Z(r.Shear1), Z(r.Shear2), 0)); o[4].Add(V(Z(r.Bending1), Z(r.Bending2), 0));
                o[5].Add(N(Z(r.Axial))); o[6].Add(N(Z(r.Torque)));
            }
        else
            foreach (var r in GpcResultRows.Plate(catalog, analysed, (AreaElement)element))
            {
                var v = r.Values;
                o[0].Add(S(Label(r.Entry))); o[1].Add(S(r.Node)); o[2].Add(N(0)); o[3].Add(V(Z(v[0]), Z(v[1]), Z(v[2])));
                o[4].Add(N(Z(r.ForceMax))); o[5].Add(N(Z(r.ForceMin))); o[6].Add(N(Z(r.ForceAngle))); o[7].Add(N(Z(r.ForceVonMises)));
                o[8].Add(V(Z(v[5]), Z(v[6]), Z(v[7]))); o[9].Add(N(Z(r.MomentMax))); o[10].Add(N(Z(r.MomentMin))); o[11].Add(N(Z(r.MomentAngle)));
                o[12].Add(V(Z(v[3]), Z(v[4]), 0)); o[13].Add(N(double.NaN)); o[14].Add(N(double.NaN));
            }
        return o;
    }

    private static string Token(object item)
    {
        static string Bits(double v) => BitConverter.DoubleToInt64Bits(v).ToString("X16", CultureInfo.InvariantCulture);
        return item switch
        {
            GH_String s => "s:" + s.Value,
            GH_Number n => "n:" + Bits(n.Value),
            GH_Vector v => "v:" + Bits(v.Value.X) + "," + Bits(v.Value.Y) + "," + Bits(v.Value.Z),
            null => "null",
            _ => item.GetType().Name + ":" + item,
        };
    }

    // -----------------------------------------------------------------------------------------------------------------------------------------
    // Transitional: the GPC catalogue against the FEM catalogue (retired by P2 with NativeExamples.Create).

    /// <summary>
    /// <b>Transitional (P2 retires it with the FEM catalogue).</b> Each GPC example against the conversion of its FEM model by the legacy reader with the
    /// same owner of the element identities (<see cref="GpcInputComparison.Require"/>: report, bridge data, analysis fingerprint, parts and the canonical
    /// XML document of the model, bit for bit, N-mm; the drafts of the elements against the conversion of every FEM value): content, field by field, not
    /// identity. Declared form of the FEM side: the FEM model as Build Model (and Attach Moving Paths) give it, i.e. without the registered case records
    /// the hand-built FEM models add (Build Model registers none; Attach Moving Paths registers the case of its paths): checked apart, the conversion of
    /// the FEM model as built has the same XML document, and its report differs only in the registered count and the «Registered» origin.
    /// The same solver, moving flag and result names. Returns one line per example.
    /// </summary>
    internal static IReadOnlyList<string> RequireSameAsFem()
    {
        var fem = NativeExamples.Create().ToArray(); var gpc = Create().ToArray();
        if (fem.Length != gpc.Length) throw new Exception($"GPC NATIVE EXAMPLES FAIL: {gpc.Length} GPC examples, {fem.Length} FEM");
        var lines = new List<string>();
        for (int i = 0; i < fem.Length; i++)
        {
            var f = fem[i]; var g = gpc[i]; string label = "native example " + g.Id;
            void Fail(string what) => throw new Exception("GPC NATIVE EXAMPLES FAIL (" + label + "): " + what);
            if (f.Id != g.Id || f.Title != g.Title || f.Solver != g.Solver || f.Moving != g.Moving) Fail($"identity of the example: FEM {f.Id} '{f.Title}' {f.Solver} {f.Moving}, GPC {g.Id} '{g.Title}' {g.Solver} {g.Moving}");
            if (!f.Cases.SequenceEqual(g.Cases, StringComparer.Ordinal)) Fail($"result names: FEM {string.Join(",", f.Cases)}, GPC {string.Join(",", g.Cases)}");
            var model = BridGHe.Common.Helpers.ModelArchive.Clone(f.Model); model.Name = f.Title;
            // The FEM model as Build Model and Attach Moving Paths give it: no registered case records; the cases of the moving paths registered.
            var built = BridGHe.Common.Helpers.ModelArchive.Clone(model); built.LoadCases.Clear();
            if (built.MovingPaths.Count != 0) { var paths = built.MovingPaths.ToList(); built.MovingPaths.Clear(); built = MovingModelBuilder.ReplacePaths(built, paths); built.Name = f.Title; }
            string result = g.Inputs.Paths.Count == 0 ? GpcInputComparison.Require(label, g.Input, built, Tolerance, buildModel: true) : GpcInputComparison.Require(label, g.Input, built, Tolerance);
            string drafts = g.Inputs.Paths.Count == 0 ? "; " + GpcInputComparison.RequireDrafts(label, g.Input.Drafts, built) : "";
            // The FEM model as built (registered case records): the same model document; the report differs only by the registration.
            var owner = GpcInputComparison.ElementOwner(g.Input.Model);
            var asBuilt = GpcInputComparison.Conversion(model, Tolerance, owner, out var asBuiltReport);
            var asGiven = GpcInputComparison.Conversion(built, Tolerance, owner, out var givenReport);
            if (GpcInputComparison.Document(asBuilt) != GpcInputComparison.Document(asGiven)) Fail("the registered case records change the model document");
            string registration = $"registered cases: FEM model as built {asBuiltReport.RegisteredCases}, as Build Model{(built.MovingPaths.Count != 0 ? " and Attach Moving Paths" : "")} give it {givenReport.RegisteredCases}";
            var origins = asBuiltReport.CaseOrigins.Where(p => !givenReport.CaseOrigins.TryGetValue(p.Key, out var o) || o != p.Value).Select(p => p.Key + ":" + p.Value).ToArray();
            if (asBuiltReport.CaseOrigins.Count != givenReport.CaseOrigins.Count
                || asBuiltReport.CaseOrigins.Any(p => (p.Value & ~LegacyCaseOrigins.Registered) != (givenReport.CaseOrigins[p.Key] & ~LegacyCaseOrigins.Registered)))
                Fail("the case origins differ beyond the registration: " + string.Join(", ", origins));
            lines.Add($"{g.Id}: {result}{drafts}; {registration}, origins differing only by Registered: {string.Join(", ", origins)}");
        }
        return lines;
    }
}
#endif
