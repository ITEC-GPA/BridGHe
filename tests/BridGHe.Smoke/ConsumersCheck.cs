#if GPC_PILOT
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using BridGHe.Common.Diagnostics;
using BridGHe.Common.Gpc;

/// <summary>
/// R6.6 lane R2b (replaces the F6 (a)/(b) parity with the former FEM consumers): the result consumers ported to GPC give on an analysed GPC
/// model what their meaning requires on the rows of the Results components (<see cref="GpcResultRows"/>), evaluated here independently of the
/// consumers: the envelope row of every element and station is the stored case with the minimum, maximum or largest absolute value of the
/// component (cases in name order on ties), with its six components; Bridge Result Cases lists one record per stored row and result kind
/// (catalogue label, step 0, no step type); a fatigue history at a stored station is the component of the rows of its cases, in the order
/// given. Every selection without data is refused. Called by <see cref="ResultsComponentsCheck"/> on every analysed model of the native
/// suites (the kN-m native section, the analyses reopened from the BGHR2, the four phases of 99) and, with the expected result names and the
/// GPC input, by <c>--examples</c> on examples 01–10: the rows are the analytic results of each example (checked by the example), the
/// result names those it requests, and the model audit follows from the known structure (<see cref="Audit"/>).
/// </summary>
internal static class ConsumersCheck
{
    private sealed record Row(string Id, double Station, string Case, double[] Values);

    /// <param name="expectedCases">The result names the analysis requested (examples 01–10): the result cases of the records are exactly these,
    /// plus the generated moving cases; null: not checked.</param>
    /// <param name="input">The GPC input of the analysis: the model audit on the analysed model is checked against its structure; null: not checked.</param>
    internal static long Verify(GPC.Model.Models.Model gpc, string label, IReadOnlyCollection<string> expectedCases = null, GpcModels.Conversion input = null, int generatedMovingCases = -1)
    {
        void Require(bool condition, string what) { if (!condition) throw new Exception("RESULT CONSUMERS GPC FAIL (" + label + "): " + what); }
        var catalog = GpcResultCatalog.Of(gpc);
        Require(catalog != null, "the analysed GPC model has its results catalog");
        string Label(int entry) => catalog.Entries[entry].Label;
        var beams = gpc.BeamElements.Values.OrderBy(b => b.Id).ToArray(); var nodes = gpc.NodesElements.Values.OrderBy(n => n.Id).ToArray();
        // The rows of the Results components, per kind: beams (station from the start, definition units), reactions, displacements.
        var rows = new List<Row>[3];
        rows[0] = beams.SelectMany(b => GpcResultRows.Beam(catalog, gpc, b).Select(r => new Row(b.Name, r.Station, Label(r.Entry),
            new[] { r.Axial, r.Shear1, r.Shear2, r.Torque, r.Bending1, r.Bending2 }.Select(GpcResultRows.Shown).ToArray()))).ToList();
        for (int kind = 1; kind < 3; kind++)
        {
            int k = kind;
            rows[k] = nodes.SelectMany(n => GpcResultRows.Node(catalog, gpc, n).Where(r => (k == 1 ? r.Reaction : r.Displacement) != null)
                .Select(r => new Row(n.Name, 0, Label(r.Entry), (k == 1 ? r.Reaction : r.Displacement).Select(GpcResultRows.Shown).ToArray()))).ToList();
        }
        long values = 0;
        // Envelopes: every kind, component and mode, on all elements and cases.
        for (int kind = 0; kind < 3; kind++)
        {
            var ids = (kind == 0 ? beams.Select(b => b.Name) : nodes.Select(n => n.Name)).ToArray();
            for (int component = 0; component < 6; component++)
                for (int mode = 0; mode < 3; mode++)
                {
                    string what = $"envelope kind {kind} component {component} mode {mode}";
                    EnvelopeRow[] actual = null; string error = null;
                    try { actual = BridgeResultEnvelope.Evaluate(gpc, kind, null, null, component, mode); } catch (ArgumentException e) { error = e.Message; }
                    // Every selected element without a stored row of this kind makes the envelope refuse the selection.
                    if (ids.Any(string.IsNullOrWhiteSpace) || ids.Distinct(StringComparer.Ordinal).Count() != ids.Length) { Require(error != null, $"{what}: empty or repeated element IDs not refused"); continue; }
                    var without = ids.Where(id => !rows[kind].Any(r => r.Id == id)).ToArray();
                    if (rows[kind].Count == 0 || without.Length > 0) { Require(error != null, $"{what}: elements without results ({string.Join(", ", without.Take(3))}) not refused"); continue; }
                    Require(error == null, $"{what}: refused: {error}");
                    int c = component, m = mode;
                    var expected = rows[kind].GroupBy(r => (r.Id, r.Station)).OrderBy(g => g.Key.Id, StringComparer.Ordinal).ThenBy(g => g.Key.Station).Select(g =>
                    {
                        var byName = g.OrderBy(r => r.Case, StringComparer.Ordinal).ToArray();
                        double target = m == 0 ? byName.Min(r => r.Values[c]) : m == 1 ? byName.Max(r => r.Values[c]) : byName.Max(r => Math.Abs(r.Values[c]));
                        return byName.First(r => (m == 2 ? Math.Abs(r.Values[c]) : r.Values[c]) == target);
                    }).ToArray();
                    Require(actual.Length == expected.Length, $"{what}: {actual.Length} rows, {expected.Length} element stations with results");
                    for (int i = 0; i < expected.Length; i++)
                    {
                        var a = actual[i]; var e = expected[i];
                        Require(a.Element == e.Id && Bits(a.Station) == Bits(e.Station) && a.Case == e.Case && Bits(a.Value) == Bits(e.Values[c]) && a.Step == 0 && a.StepType == ""
                            && a.Components.Select(Bits).SequenceEqual(e.Values.Select(Bits)) && (a.Coverage == "Complete cases") == (rows[kind].Where(r => r.Id == e.Id && r.Station == e.Station).Select(r => r.Case).Distinct().Count() == rows[kind].Select(r => r.Case).Distinct().Count()),
                            $"{what}, row {i}: {a.Element} {a.Station} {a.Case} {a.Value} [{a.Coverage}] / expected {e.Id} {e.Station} {e.Case} {e.Values[c]}");
                        values += 8;
                    }
                }
        }
        // Bridge Result Cases: one record per stored row and result kind (label, step 0, no step type).
        static IEnumerable<string> Groups(IEnumerable<(string Case, double Step, string Type, string Result)> all) => all.GroupBy(r => r)
            .OrderBy(g => g.Key.Case, StringComparer.Ordinal).ThenBy(g => g.Key.Step).ThenBy(g => g.Key.Type, StringComparer.Ordinal).ThenBy(g => g.Key.Result, StringComparer.Ordinal)
            .Select(g => g.Key.Case + "|" + Bits(g.Key.Step) + "|" + g.Key.Type + "|" + g.Key.Result + "|" + g.Count());
        var reference = rows[2].Select(r => (r.Case, 0d, "", BridgeResultCases.NodeDisplacement)).Concat(rows[1].Select(r => (r.Case, 0d, "", BridgeResultCases.NodeReaction)))
            .Concat(rows[0].Select(r => (r.Case, 0d, "", BridgeResultCases.BeamForce)))
            .Concat(gpc.AreaElements.Values.SelectMany(p => GpcResultRows.Plate(catalog, gpc, p).Select(r => (Label(r.Entry), 0d, "", BridgeResultCases.PlateForce))));
        var actualCases = Groups(BridgeResultCases.Records(gpc)).ToArray(); var expectedRecords = Groups(reference).ToArray();
        Require(actualCases.SequenceEqual(expectedRecords, StringComparer.Ordinal), $"result cases: {string.Join("; ", actualCases)} / expected {string.Join("; ", expectedRecords)}");
        values += actualCases.Length;
        var moving = catalog.GeneratedMovingCases();
        if (generatedMovingCases >= 0) Require(moving.Count == generatedMovingCases && moving.Select(c => c.Number).SequenceEqual(Enumerable.Range(1, moving.Count)), $"{moving.Count} generated moving cases, expected {generatedMovingCases}");
        if (expectedCases != null)
        {
            var labels = BridgeResultCases.Records(gpc).Select(r => r.Case).Distinct().OrderBy(x => x, StringComparer.Ordinal).ToArray();
            var want = expectedCases.Concat(moving.Select(c => c.Name)).Distinct().OrderBy(x => x, StringComparer.Ordinal).ToArray();
            Require(labels.SequenceEqual(want, StringComparer.Ordinal), $"result names {string.Join(", ", labels)}, requested {string.Join(", ", want)}");
        }
        // Fatigue history: every case of a beam in their stored order, at both ends and at mid length, per component: at a stored station the
        // rows of the cases; at mid length (resampled when not stored) finite values; the source of every value names its case.
        foreach (var beam in beams.Take(24))
        {
            var own = rows[0].Where(r => r.Id == beam.Name).ToArray();
            var cases = own.Select(r => r.Case).Distinct().ToArray();
            if (cases.Length == 0) continue;
            double length = catalog.Units.FromMm(beam.Length);
            foreach (double station in new[] { 0, length / 2, length })
                foreach (int component in new[] { 0, 4, 5 })
                {
                    string what = $"fatigue beam {beam.Name} station {station:R} component {component}";
                    var stored = cases.Select(c => own.Where(r => r.Case == c && Math.Abs(r.Station - station) <= 1e-9 * Math.Max(1, length)).ToArray()).ToArray();
                    BridgeFatigueSeries series = null; string error = null;
                    try { series = BridgeFatigueHistory.Extract(gpc, beam.Name, station, cases, component); } catch (ArgumentException e) { error = e.Message; }
                    if (stored.Any(s => s.Length > 1)) { Require(error != null, $"{what}: a two-sided result at the station not refused"); continue; }
                    // A case not stored at the station is resampled from its own samples, which must include both beam ends (no extrapolation).
                    bool End(string c, double at) => own.Any(r => r.Case == c && Math.Abs(r.Station - at) <= 1e-9 * Math.Max(1, length));
                    if (cases.Where((c, i) => stored[i].Length == 0).Any(c => !End(c, 0) || !End(c, length))) { Require(error != null, $"{what}: resampling without both beam ends not refused"); continue; }
                    // A value away from the stored stations of a case is resampled from its grid, which needs both beam ends.
                    bool Ends(string c) => own.Any(r => r.Case == c && Math.Abs(r.Station) <= 1e-9 * Math.Max(1, length)) && own.Any(r => r.Case == c && Math.Abs(r.Station - length) <= 1e-9 * Math.Max(1, length));
                    if (Enumerable.Range(0, cases.Length).Any(i => stored[i].Length == 0 && !Ends(cases[i]))) { Require(error != null, $"{what}: a resampling without both beam ends not refused"); continue; }
                    Require(error == null && series.Values.Length == cases.Length && series.Sources.Zip(cases).All(p => p.First.Contains("/" + p.Second + "/")), $"{what}: {error ?? "history of the cases"}");
                    for (int i = 0; i < cases.Length; i++)
                    {
                        if (stored[i].Length == 1) Require(Bits(series.Values[i]) == Bits(stored[i][0].Values[component]), $"{what}, case {cases[i]}: {series.Values[i]} / stored {stored[i][0].Values[component]}");
                        else Require(double.IsFinite(series.Values[i]), $"{what}, case {cases[i]}: resampled value not finite");
                    }
                    values += cases.Length;
                }
        }
        string audit = input == null ? "" : "; " + Audit(input, gpc, label);
        Console.WriteLine($"RESULT CONSUMERS GPC PASS ({label}): envelopes, result cases and fatigue histories equal to the rows of the Results components ({values} values){audit}.");
        return values;
    }

    /// <summary>The model audit on the analysed model against the known structure of its GPC input: no error; DISCONNECTED exactly when the
    /// elements form more than one connected part (shared end points and plate vertices within the tolerance); ISOLATED_NODE exactly for the
    /// nodes on no element vertex; NO_SUPPORT exactly when no node has a restraint; no INVALID_RESULT (every stored result has its case); and
    /// a result without case identity reported as INVALID_RESULT.</summary>
    internal static string Audit(GpcModels.Conversion input, GPC.Model.Models.Model analysed, string label)
    {
        void Require(bool condition, string what) { if (!condition) throw new Exception("MODEL AUDIT GPC FAIL (" + label + "): " + what); }
        double tolerance = 1e-6;
        var issues = BridgeModelAudit.Inspect(input, tolerance, analysed: analysed);
        string Text() => string.Join("\n", issues.Select(i => i.Severity + "|" + i.Code + "|" + i.Element + "|" + i.Description));
        var model = input.Model;
        var links = model.GetCostrains().OfType<GPC.Model.Constraints.RigidLink>().SelectMany(l => l.EndNodes.Select(e => new[] { l.StartNode.Position, e.Position })).ToArray();
        var elements = model.BeamElements.Values.Select(b => new[] { b.StartPoint, b.EndPoint }).Concat(model.AreaElements.Values.Select(p => p.Nodes.Select(n => n.Position).ToArray())).ToArray();
        var vertices = elements.Concat(links).ToArray();
        // Connected parts of the elements by shared vertices (union-find).
        var parent = Enumerable.Range(0, vertices.Length).ToArray();
        int Find(int i) => parent[i] == i ? i : parent[i] = Find(parent[i]);
        static bool Near(GPC.Geometry.Point3d a, GPC.Geometry.Point3d b, double t) => Math.Abs(a.X - b.X) <= t && Math.Abs(a.Y - b.Y) <= t && Math.Abs(a.Z - b.Z) <= t;
        for (int i = 0; i < vertices.Length; i++)
            for (int j = i + 1; j < vertices.Length; j++)
                if (vertices[i].Any(p => vertices[j].Any(q => Near(p, q, tolerance)))) parent[Find(i)] = Find(j);
        int parts = Enumerable.Range(0, elements.Length).Select(Find).Distinct().Count();
        var isolated = model.NodesElements.Values.Where(n => !elements.Any(v => v.Any(p => Near(p, n.Position, tolerance)))).Select(n => n.Name).OrderBy(x => x, StringComparer.Ordinal).ToArray();
        // A restraint record of a node frame (NodeCoordinateAttributeModel, the reader record of a frame) is not a support.
        bool supported = model.NodesElements.Values.Any(n => n.Assignments.Restrains.Any(r => r.SourceRecord != "NodeCoordinateAttributeModel" && r.Restrain.Restrains.Any(d => d.IsRestrained || d.HasStiffness)))
            || model.BeamElements.Values.Any(b => b.Assignments.Physical?.Supports.Count > 0) || model.AreaElements.Values.Any(p => p.Assignments.Physical?.Supports.Count > 0);
        Require(!issues.Any(i => i.Severity == "Error"), "errors on a valid example:\n" + Text());
        Require(issues.Any(i => i.Code == "DISCONNECTED") == (parts > 1), $"{parts} connected parts:\n" + Text());
        Require(issues.Where(i => i.Code == "ISOLATED_NODE").Select(i => i.Element).OrderBy(x => x, StringComparer.Ordinal).SequenceEqual(isolated), $"isolated nodes {string.Join(",", isolated)}:\n" + Text());
        Require(issues.Any(i => i.Code == "NO_SUPPORT") == !supported, $"supported {supported}:\n" + Text());
        Require(!issues.Any(i => i.Code == "INVALID_RESULT"), "a stored result without case identity:\n" + Text());
        return $"model audit as the structure ({parts} part(s), {isolated.Length} isolated nodes, {(supported ? "supported" : "no support")}; {issues.Length} issues: {string.Join(",", issues.Select(i => i.Code).Distinct())})";
    }

    private static string Bits(double v) => BitConverter.DoubleToInt64Bits(v == 0 ? 0.0 : v).ToString("X16", CultureInfo.InvariantCulture);
}
#endif
