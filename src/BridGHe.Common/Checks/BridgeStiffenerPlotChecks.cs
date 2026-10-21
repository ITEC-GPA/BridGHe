using System;
using System.Collections.Generic;
using System.Linq;
using GPC.Checkers.CompositeBridge;

namespace BridGHe.Common.Checks;

public enum StiffenerPlotRole { Intermediate, Support }
public enum CheckPlotDisplay { Utilization, Demand, Resistance, DemandAndResistance }
public sealed record StiffenerPlotReading(double Value, double Limit, string Unit, string Governing, string Unavailable);

/// <summary>Presentation of native gPC checks only. No resistance or utilization is recalculated.</summary>
public static class BridgeStiffenerPlotChecks
{
    private static readonly string[] MemberChecks = {
        "stabilità e pressoflessione II ordine", "freccia fuori piano", "rigidezza torsionale",
        "piatti entro classe 3", "ingombro tra piattabande", "saldature verticali all’anima",
        "saldature alle flange", "gola minima", "lunghezza efficace cordone", "collegamenti"
    };

    public static string[] Choices(StiffenerPlotRole role)
    {
        var names = new List<string> { "Inviluppo WR di tutte le verifiche" };
        if (role == StiffenerPlotRole.Intermediate)
        {
            names.Add("Intermedio · rigidezza pannelli");
            names.AddRange(MemberChecks.Select(s => "Intermedio · " + s));
        }
        else
        {
            names.AddRange(MemberChecks.Select(s => "Appoggio · " + s));
            names.AddRange(new[] { "impronta trasversale sotto i piatti", "impronta longitudinale", "impronta entro l’estremità", "contatto piatti/flangia" }.Select(s => "Appoggio · " + s));
            names.AddRange(new[] { "due coppie bilaterali simmetriche", "area di ciascuna coppia", "distanza e > 0,1 hw", "e entro il pannello", "reazione e ancoraggio combinati" }.Select(s => "Terminale · " + s));
            names.AddRange(MemberChecks.Select(s => "Seconda coppia terminale · " + s));
        }
        return names.ToArray();
    }

    public static BridgeLocalCheck[] Checks(BridgeStressResult result, StiffenerPlotRole role) =>
        (result.Stage.Shear?.Checks ?? new List<BridgeLocalCheck>()).Where(c => role == StiffenerPlotRole.Intermediate
            ? c.Name.StartsWith("Intermedio · ", StringComparison.Ordinal)
            : c.Name.StartsWith("Appoggio · ", StringComparison.Ordinal) || c.Name.StartsWith("Terminale · ", StringComparison.Ordinal)
                || c.Name.StartsWith("Seconda coppia terminale · ", StringComparison.Ordinal)).ToArray();

    public static StiffenerPlotReading Read(BridgeStressResult result, StiffenerPlotRole role, string check, CheckPlotDisplay display)
    {
        if (result == null) throw new ArgumentNullException(nameof(result));
        if (!Enum.IsDefined(display)) throw new ArgumentOutOfRangeException(nameof(display));
        var checks = Checks(result, role);
        bool envelope = string.IsNullOrEmpty(check);
        if (!envelope) checks = checks.Where(c => c.Name == check).ToArray();
        if (!envelope && checks.Length > 1) throw new ArgumentException("Duplicate gPC check: " + check);
        string unit = envelope || display == CheckPlotDisplay.Utilization ? "ratio" : checks.FirstOrDefault()?.Unit ?? "—";
        if (checks.Length == 0)
            return new(double.NaN, double.NaN, unit, "", (check ?? role.ToString()) + ": verifica non disponibile o dettaglio non attivato.");
        var missing = checks.Where(c => !c.Ratio.HasValue || !double.IsFinite(c.Ratio.Value)).ToArray();
        if (missing.Length > 0)
            return new(double.NaN, double.NaN, unit, "", string.Join("; ", missing.Select(c => c.Name + ": " + c.Status + ". " + c.Note)));
        if (envelope)
        {
            double maximum = checks.Max(c => c.Ratio.Value);
            return new(maximum, double.NaN, "ratio", string.Join("; ", checks.Where(c => c.Ratio.Value == maximum).Select(c => c.Name)), "");
        }
        var selected = checks[0];
        double value = display switch { CheckPlotDisplay.Utilization => selected.Ratio.Value, CheckPlotDisplay.Resistance => selected.Resistance, _ => selected.Demand };
        double limit = display == CheckPlotDisplay.DemandAndResistance ? selected.Resistance : double.NaN;
        if (!double.IsFinite(value) || display == CheckPlotDisplay.DemandAndResistance && !double.IsFinite(limit))
            return new(double.NaN, double.NaN, unit, "", selected.Name + ": valore numerico non finito.");
        return new(value, limit, unit, selected.Name, "");
    }
}
