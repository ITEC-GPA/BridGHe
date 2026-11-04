using System;
using System.Collections.Generic;
using System.Linq;
using BridGHe.Common.Gpc;
using BridGHe.Common.Models;
using GPC.Model.Models;

namespace BridGHe.Common.Checks;

/// <summary>
/// F6 (a)/(b), R5: the analysed GPC model of a bridge action source, read by Build BridGHe Model in place of the FEM result objects. The
/// beam forces of a case are the rows the Results components show (<see cref="GpcResultRows.Beam"/>, the numbers the analysis wrote into
/// the FEM results, zero without sign as the FEM copies of the GH data), the concentrated loads of the cases contributing to it come from the
/// GPC load assignments of the beam. A FEM beam of the source is its GPC beam by numbering (FEM beam k is GPC beam k + 1, the numbering of
/// the legacy reader the analysis used).
/// </summary>
internal sealed class GpcActionSource
{
    private readonly Model gpc;
    private readonly GpcResultCatalog catalog;
    private readonly Dictionary<BeamModel, int> numbers;

    private GpcActionSource(ModelModel fem, Model gpc)
    {
        this.gpc = gpc;
        catalog = GpcResultCatalog.Of(gpc) ?? throw new ArgumentException("GpcActionSourceWithoutResults: " + fem.Name);
        if (gpc.BeamElements.Count != fem.Beams.Count || fem.Beams.Select((b, k) => (b, k)).Any(p => gpc.BeamElements[p.k + 1].Name != (p.b.BeamId ?? "")))
            throw new ArgumentException("GpcActionSourceNotTheAnalysedModel: " + fem.Name);
        numbers = new Dictionary<BeamModel, int>(ReferenceEqualityComparer.Instance);
        for (int k = 0; k < fem.Beams.Count; k++) numbers.Add(fem.Beams[k], k + 1);
    }

    internal static GpcActionSource Of(ModelModel fem, Model gpc) => new GpcActionSource(fem ?? throw new ArgumentNullException(nameof(fem)), gpc ?? throw new ArgumentNullException(nameof(gpc)));

    private GPC.Model.Elements.BeamElement Beam(BeamModel beam) =>
        numbers.TryGetValue(beam, out int number) ? gpc.BeamElements[number] : throw new ArgumentException("GpcActionSourceBeamNotInModel: " + beam.BeamId);

    /// <summary>The stored forces of a case (catalog label) of a beam, station in the definition length.</summary>
    internal BeamForceSample[] Forces(BeamModel beam, string label) => GpcResultRows.Beam(catalog, gpc, Beam(beam))
        .Where(r => catalog.Entries[r.Entry].Label == label)
        .Select(r => new BeamForceSample(r.Station, GpcResultRows.Shown(r.Axial), GpcResultRows.Shown(r.Shear1), GpcResultRows.Shown(r.Shear2),
            GpcResultRows.Shown(r.Torque), GpcResultRows.Shown(r.Bending1), GpcResultRows.Shown(r.Bending2))).ToArray();

    /// <summary>The positions (definition length from the start) of the non-zero concentrated loads of the cases contributing to a case: those
    /// with a non-zero coefficient of the combination of that name, or the case itself.</summary>
    internal double[] ConcentratedLoads(BeamModel beam, string label)
    {
        var contributing = gpc.Combinations.TryGetValue(label, out var combination)
            ? combination.GetLoadCaseCoefficientsPair().Where(p => p.Value != 0).Select(p => p.Key.Name).ToHashSet(StringComparer.Ordinal)
            : new[] { label }.ToHashSet(StringComparer.Ordinal);
        var element = Beam(beam); double length = catalog.Units.FromMm(element.Length);
        return element.Assignments.Loads.Where(l => l?.Concentrated?.LoadCase != null && contributing.Contains(l.Concentrated.LoadCase.Name))
            .Where(l => { var p = l.Concentrated; return p.F1 != 0 || p.F2 != 0 || p.F3 != 0 || p.M1 != 0 || p.M2 != 0 || p.M3 != 0; })
            .Select(l => l.Start * length).ToArray();
    }
}
