using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using GPC.Checkers.CompositeBridge;

/// <summary>
/// P2F (12/11/2026, item 5): the content of the verification recipe of a GPC bridge model (<see cref="BridgeWorkflowDefinition"/>), phase by
/// phase and action by action, with its identities written by what they name: an element of the bridge model by its family and ID, a load
/// case of the model by its name, the bridge model itself as <c>model</c>, its analysis fingerprint as <c>=model</c>; any other GUID by its
/// ordinal of first appearance. With <c>BRIDGHE_P2F_RECIPE=&lt;folder&gt;</c> the native runner writes it (<c>&lt;label&gt;-&lt;which&gt;.txt</c>),
/// to compare the recipe of two runs whose identities differ (diagnosis, not a check).
/// </summary>
internal static class P2fRecipe
{
    internal static string Text(BridgeWorkflowDefinition w, GPC.Model.Models.Model model)
    {
        var names = new Dictionary<Guid, string>();
        foreach (var e in model.AllElements) names[e.Guid] = GPC.Model.Models.Model.FamilyOf(e) + ":" + e.Id;
        foreach (var c in model.LoadCases) names[c.Value.Guid] = "case:" + c.Key;
        names[model.Guid] = "model";
        var other = new Dictionary<Guid, int>();
        string G(Guid g) => names.TryGetValue(g, out var n) ? n : "G" + (other.TryGetValue(g, out int k) ? k : other[g] = other.Count + 1);
        string Gn(Guid? g) => g == null ? "-" : G(g.Value);
        string R(double? v) => v == null ? "-" : v.Value.ToString("R", CultureInfo.InvariantCulture);
        string analysis = GPC.Model.Models.ModelRevisions.AnalysisFingerprint(model);
        string A(string f) => f == analysis ? "=model" : f;
        var text = new StringBuilder();
        text.Append("workflow ").Append(G(w.ModelGuid)).Append(" analysis ").Append(A(w.AnalysisFingerprint)).Append(" method ").Append(w.Method).Append(" phases ").Append(w.Phases.Length).Append('\n');
        foreach (var p in w.Phases)
        {
            text.Append("phase ").Append(G(p.Id)).Append(" order ").Append(p.Order).Append(" name ").Append(p.Name).Append(" state ").Append(p.State).Append(" day ").Append(R(p.Day)).Append(" actions ").Append(p.Actions.Length).Append('\n');
            foreach (var a in p.Actions)
            {
                text.Append("  action ").Append(a.Key).Append(" source ").Append(G(a.SourceModelGuid)).Append(' ').Append(A(a.SourceAnalysisFingerprint)).Append(" case ").Append(Gn(a.SourceCaseGuid))
                    .Append(" name ").Append(a.SourceName).Append(" behavior ").Append(a.Behavior).Append(" force ").Append(Gn(a.ForceCaseGuid)).Append(" strain case ").Append(Gn(a.StrainCaseGuid))
                    .Append(" strain ").Append(R(a.Strain)).Append(" psiL ").Append(R(a.PsiL)).Append(" primary ").Append(a.PrimaryOnly).Append('\n');
                text.Append("    beams ").Append(string.Join(",", a.BeamGuids.Select(G).OrderBy(x => x, StringComparer.Ordinal))).Append('\n');
                text.Append("    excluded ").Append(string.Join(",", a.ConcreteExcludedBeamGuids.Select(G).OrderBy(x => x, StringComparer.Ordinal))).Append('\n');
                foreach (var s in a.SectionStates.Select(s => $"    state {G(s.BeamGuid)} {s.Behavior} {s.ConcreteExcluded} {s.ModularRatio.ToString("R", CultureInfo.InvariantCulture)} {Gn(s.ClassificationCaseGuid)} {s.PhysicalPropertyFingerprint}").OrderBy(x => x, StringComparer.Ordinal))
                    text.Append(s).Append('\n');
            }
        }
        return text.ToString();
    }

    internal static void Dump(string label, string which, BridgeWorkflowDefinition w, GPC.Model.Models.Model model)
    {
        string folder = Environment.GetEnvironmentVariable("BRIDGHE_P2F_RECIPE");
        if (string.IsNullOrWhiteSpace(folder) || w == null) return;
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, label + "-" + which + ".txt"), Text(w, model), new UTF8Encoding(false));
        Console.WriteLine($"P2F RECIPE {label} {which}: {w.Phases.Length} phases, {w.Phases.Sum(p => p.Actions.Length)} actions, fingerprint {w.Fingerprint()}, written in {folder}");
    }
}
