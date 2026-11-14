using System;
using System.Collections.Generic;
using System.Linq;
using BridGHe.Common.Gpc;
using BridGHe.Common.Gpc.Builders;
using BridGHe.Common.Gpc.Legacy;
using GPC.Converter;
using GPC.Geometry;
using GPC.Model.Analysis;
using GPC.Model.Core.Units;
using GPC.Model.LoadCases;
using GPC.Model.Models;
using GPC.Model.Results.Locations;
using GPC.Model.Results.State;

/// <summary>
/// U2 (14/11/2026, coordinator decision (b) on the open point of the first hand-back): the models of 23 and 46 made by Build Model
/// (<see cref="LegacyBuildModel.Build"/>, GPC-only input) from the drafts of the GPC builders, so that they carry the drafts of their elements
/// (Decompose Model and Super Element Filter work on them, as on any model of Build Model). The inputs are those of the 0.3.0 models the
/// files held (read by value from their payloads, «dump-0.3.0»): 23 the pinned node A (Tx, Ty, Tz) and the beams B0 (0,0,0)–(10000,0,0) and
/// B1 (0,2000,0)–(10000,2000,0); 46 the same two beams, no node, no rigid link; every beam on «Longitudinale fase 0» (the steel H of the
/// grillage, <see cref="BridgeGrillageExample.LongPropertyGpc"/>), Element ID and beam ID B0/B1, angle 0. Build Model adds the nodes at the
/// beam ends. The analysed copy (an analysis component's output) is the Build Model model with the same samples as the generators
/// (<see cref="BridgeUtilityTests.SourceGpc"/>, <see cref="GpcWorkflowSources.Source"/>) imported by the GPC result mapper by BridGHe ID,
/// with the results catalog (cases in the order of their first result, label = case) and the cases of the samples.
/// </summary>
internal static class U2BuiltModels
{
    private static readonly BridgeUnits Units = new BridgeUnits(DefinitionUnits.Nmm);

    /// <summary>The tolerance of Build Model without a Rhino document (the plug-in default), as the fixtures of Build Model.</summary>
    internal const double Tolerance = 0.001;

    private static Guid Id(string tag) => GpcIdentity.Derive(GpcIdentity.NameSpace, "u2-remake-20261114:" + tag);

    private static LegacyBeamDraft Beam(string model, string id, double y)
    {
        var beam = GpcBeamBuilders.New(new Point3d(0, y, 0), new Point3d(10000, y, 0), BridgeGrillageExample.LongPropertyGpc(new BridgeGrillageExample.Parameters(), 0),
            null, 0, 0, 0, Id(model + ":beam " + id), id);
        return GpcElementBuilders.WithLabel(beam, id, Id(model + ":beam label " + id));
    }

    private static GpcModels.Conversion Build(string name, IReadOnlyList<LegacyNodeDraft> nodes, IReadOnlyList<LegacyBeamDraft> beams) =>
        LegacyBuildModel.Build(name, Units, Tolerance, nodes.Select(d => (d.Node.Guid, d)).ToList(), beams.Select(d => (d.Beam.Guid, d)).ToList(),
            new List<(Guid, LegacyPlateDraft)>(), new List<LegacyCombinationDraft>(), new List<LegacyStageDraft>(), gpcOnly: true);

    /// <summary>23: the model of the audit (two parallel girders, disconnected on purpose; the bearing node A pinned, as in 0.3.0) and its
    /// analysed copy with the samples of G and Q (the beam actions of both girders, the reaction and displacement of A).</summary>
    internal static (GpcModels.Conversion Input, Model Analysed) Audit23()
    {
        const string m = "23";
        // The node ID A of 0.3.0 is the Element ID of the node (the label Build Model numbers by), as the reader gives it.
        var a = GpcNodeBuilders.WithRestraint(GpcNodeBuilders.Create(0, 0, 0, "A", null, Id(m + ":node A")), true, true, true, false, false, false, Id(m + ":node A restraint"));
        a = GpcNodeBuilders.WithLabel(a, "A", Id(m + ":node A label"));
        var input = Build("Controlled result samples N mm", new[] { a }, new[] { Beam(m, "B0", 0), Beam(m, "B1", 2000) });
        var s = new Samples(input.Model, "u2-23");
        s.Case("G"); s.Case("Q");
        for (int i = 0; i < 2; i++) foreach (double x in new[] { 0d, .5, 1d }) s.Forces("B" + i, "G", x * 10000, 100 + i, bm2: 100 * 1000 * (1 + x));
        for (int i = 0; i < 2; i++) foreach (double x in new[] { 0d, .5, 1d }) s.Forces("B" + i, "Q", x * 10000, -50, 13, -17, 19, -23, -2 * (100 * 1000 * (1 + x)));
        s.Reaction("A", "G", 10, 20, 100, 1, 2, 3).Reaction("A", "Q", 50, 60, -120, 4, 5, 6);
        s.Displacement("A", "G", 1, 2, -10, .1, .2, .3).Displacement("A", "Q", 3, 4, 8, .4, .5, .6);
        return (input, s.Analyse());
    }

    /// <summary>46: the four sources of the recipe (Steel 100, Composite long term 30, Composite short term 50, Shrinkage secondary 20: case G,
    /// N = force + i and BM2 = force·1000·(1 + x/L) at x = 0, L/2, L on the beams B0 and B1), in the order of the Action IDs G1, G2, Q, RITIRO.</summary>
    internal static (GpcModels.Conversion Input, Model Analysed)[] Recipe46() =>
        new[] { ("Steel", 100d), ("Composite long term", 30d), ("Composite short term", 50d), ("Shrinkage secondary", 20d) }.Select(p =>
        {
            string m = "46:" + p.Item1;
            var input = Build(p.Item1, Array.Empty<LegacyNodeDraft>(), new[] { Beam(m, "B0", 0), Beam(m, "B1", 2000) });
            var s = new Samples(input.Model, "u2-46-" + p.Item1);
            s.Case("G");
            for (int i = 0; i < 2; i++) foreach (double x in new[] { 0d, 5000, 10000 }) s.Forces("B" + i, "G", x, p.Item2 + i, bm2: p.Item2 * 1000 * (1 + x / 10000));
            return (input, s.Analyse());
        }).ToArray();

    /// <summary>The analysed copy of a model of Build Model with synthetic samples, as <see cref="GpcTestModel"/> writes them (values as the
    /// Results components show them, in N-mm: node values global, beam actions in the former principal frame of the beam angle, converted once
    /// with the inverse of the <see cref="GpcResults"/> conventions), by BridGHe ID.</summary>
    private sealed class Samples
    {
        private readonly Model model;
        private readonly ResultImportBatch batch;
        private readonly AnalysisSource source;
        private readonly List<string> entries = new();
        private readonly Dictionary<int, double> angles;

        internal Samples(Model input, string name)
        {
            model = GPC.Model.Core.ModelValues.Copy(input, GPC.Model.Core.ModelCopyOptions.None, input.Guid);
            angles = model.BeamElements.Values.ToDictionary(b => b.Id, _ => 0d);
            source = new AnalysisSource { Program = GpcTestModel.Program, SolverVersion = "U2 synthetic results", ModelRevision = model.Guid.ToString("D"), AnalysisId = GpcTestModel.Dataset };
            batch = new ResultImportBatch
            {
                Source = source, DatasetId = GpcTestModel.Dataset, SourceHash = "u2/" + name,
                Units = new ResultUnits(Units.Force, Units.Length, Units.Force * Units.Length), ShellDenominatorLengthToMm = Units.Length,
                ResolvedConvention = "BridGHe Results components: nodal values global; beam actions in the former principal frame, SF1 = -V1, SF2 = -V2, BM1 = M2, BM2 = -M1.",
                ReaderVersion = "BridGHe.Smoke.U2BuiltModels/1", Semantics = AnalysisSemantics.LinearStatic, IsSynthetic = true,
            };
        }

        /// <summary>A case of the samples (GUID by name, as every BridGHe case); the model of Build Model has only «Dead» (no load).</summary>
        internal void Case(string name)
        {
            if (!model.LoadCases.ContainsKey(name)) model.LoadCases.Add(name, new LoadCaseBase(GpcIdentity.Case(name), name));
        }

        private void Entry(string c) { if (!model.LoadCases.ContainsKey(c)) throw new ArgumentException("U2 samples: unknown case " + c); if (!entries.Contains(c)) entries.Add(c); }

        private GPC.Model.Elements.NodeElement Node(string id) => model.NodesElements.Values.SingleOrDefault(n => n.Source?.OriginalId == id)
            ?? throw new ArgumentException("U2 samples: no node " + id + " among " + string.Join(", ", model.NodesElements.Values.Select(n => n.Id + ":" + n.Name + "/" + n.Source?.OriginalId)));
        private GPC.Model.Elements.BeamElement Beam(string id) => model.BeamElements.Values.SingleOrDefault(b => b.Source?.OriginalId == id)
            ?? throw new ArgumentException("U2 samples: no beam " + id + " among " + string.Join(", ", model.BeamElements.Values.Select(n => n.Id + ":" + n.Name + "/" + n.Source?.OriginalId)));

        private static ResultState State(string c) => new ResultState
        {
            Semantics = AnalysisSemantics.LinearStatic, ConcomitantStateId = "test case " + c, IsCombined = false, IsCumulative = true,
            Components = Enumerable.Repeat(ComponentAvailability.Available, 6).ToArray(), Coverage = "Synthetic results of a remade example.",
        };

        private static CoordinateSystem At(Point3d mm) => new CoordinateSystem(new Point3d(mm.X / Units.Length, mm.Y / Units.Length, mm.Z / Units.Length),
            new Vector3d(1, 0, 0), new Vector3d(0, 1, 0), new Vector3d(0, 0, 1));

        internal Samples Displacement(string node, string c, double ux, double uy, double uz, double rx, double ry, double rz)
        {
            Entry(c); var n = Node(node);
            batch.NodeDisplacements.Add(new NodeDisplacementRecord { ElementId = n.Source.OriginalId, Case = c, State = State(c), Axes = At(n.Position),
                Record = "u2 node " + n.Name + " displacement " + c, Values = new double?[] { ux, uy, uz, rx, ry, rz } });
            return this;
        }

        internal Samples Reaction(string node, string c, double fx, double fy, double fz, double mx, double my, double mz)
        {
            Entry(c); var n = Node(node);
            batch.NodeForces.Add(new NodeForceRecord { ElementId = n.Source.OriginalId, Case = c, State = State(c), Axes = At(n.Position),
                Record = "u2 node " + n.Name + " reaction " + c, Kind = NodalForceKind.SupportReaction, Body = ActionBody.OnNode,
                Values = new double?[] { fx, fy, fz, mx, my, mz } });
            return this;
        }

        internal Samples Forces(string beamId, string c, double station, double axial, double sf1 = 0, double sf2 = 0, double torque = 0, double bm1 = 0, double bm2 = 0)
        {
            Entry(c); var beam = Beam(beamId);
            double length = beam.Length / Units.Length, t = station / length;
            var axes = beam.Assignments.SectionAxes; var principal = GpcBeamAxes.Principal(beam.StartPoint, beam.EndPoint, angles[beam.Id]);
            // Inverse of GpcResults.BeamForces (the former principal frame back to the section axes), as GpcTestModel.Forces.
            var shear = principal.V1 * -sf1 + principal.V2 * -sf2;
            var bending = principal.V2 * bm1 + principal.V1 * -bm2;
            static double Dot(Vector3d a, Vector3d b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;
            var point = beam.StartPoint + (beam.EndPoint - beam.StartPoint) * t;
            var at = new CoordinateSystem(new Point3d(point.X / Units.Length, point.Y / Units.Length, point.Z / Units.Length),
                new Vector3d(axes.V1.X, axes.V1.Y, axes.V1.Z), new Vector3d(axes.V2.X, axes.V2.Y, axes.V2.Z), new Vector3d(axes.V3.X, axes.V3.Y, axes.V3.Z));
            batch.Beams.Add(new BeamForceRecord { ElementId = beam.Source.OriginalId, Case = c, State = State(c), Axes = at, Station = t, PhysicalDistance = station,
                Body = ActionBody.PositiveSectionFace, StationDomain = "NodeToNode", Record = "u2 beam " + beam.Name + " station " + station.ToString("R") + " " + c,
                Values = new double?[] { axial, Dot(shear, axes.V1), Dot(shear, axes.V2), torque, Dot(bending, axes.V1), Dot(bending, axes.V2) } });
            return this;
        }

        internal Model Analyse()
        {
            model.AnalysisSource = source;
            batch.ExpectedInputFingerprint = ModelRevisions.AnalysisFingerprint(model);
            var report = ResultMapper.Import(model, batch);
            if (report.Status != ImportStatus.Completed)
                throw new InvalidOperationException("U2 samples not imported: " + report.Status + " " + string.Join("; ", report.Diagnostics.Select(d => d.Code + " " + d.Message)));
            GpcResultCatalog.Attach(model, Units.Units, entries.Select(c => new GpcResultCatalog.Entry { Label = c, Case = c }).ToArray(), angles);
            return model;
        }
    }
}
