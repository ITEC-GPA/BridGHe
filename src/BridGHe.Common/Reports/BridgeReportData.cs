using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using BridGHe.Common.Checks;
using BridGHe.Common.Models;
using GPC.Checkers.CompositeBridge;

namespace BridGHe.Common.Reports;

public enum BridgeReportTopic { Model, Sections, Phases, Checks, Critical, Plots, Appendix }
public sealed record BridgeReportMetadata(string Title, string Project, string Author, string Revision);
public sealed record BridgeReportEntry(BridgeStressResult Result, string TreePosition);
public sealed record BridgeReportCheck(string Family, string Name, double Demand, double Resistance, string Unit,
    double? Ratio, bool Active, string NativeStatus, string Note)
{
    public bool HasRatio => Active && Ratio.HasValue && double.IsFinite(Ratio.Value);
    public string Status => !Active ? "Non attivo" : !HasRatio ? "Non disponibile" : Ratio <= 1 ? "Entro limite" : "Non verificato";
}
public sealed record BridgeReportSection(string Id, BridgeGeometry Geometry, BridgeMaterialValues Materials, BridgeAnalysisOptions Options, JsonElement Details);
public sealed record BridgeReportContribution(string Name, double N, double Mx, double V, double HomogenizationN, double ShrinkageStrain, JsonElement Native);
public sealed record BridgeReportSample(string Id, string TreePosition, BridgeResultSource Source, string SectionId,
    string Phase, string LimitState, string Method, string Scope, int Iterations, double Residual,
    BridgeReportContribution[] Contributions, BridgePhase[] InputPhases, BridgeReportCheck[] Checks, string[] Warnings, string[] Assumptions);
public sealed record BridgeReportModel(string Name, string Units, int Nodes, int Beams, int Plates,
    BridgeWorkflow Workflow, BridgeReportCombination[] Combinations);
public sealed record BridgeReportCombination(string Name, BridgeReportFactor[] Factors);
public sealed record BridgeReportFactor(string Case, double Factor);
public sealed record BridgeReportRow(BridgeReportSample Sample, BridgeReportCheck Check);
public sealed record BridgeReportFigure(string Title, byte[] Png, int Width, int Height, string Svg);

/// <summary>A detached, presentation-only snapshot. No analysis is invoked and no input is mutated.</summary>
public sealed record BridgeReportData(string Id, DateTimeOffset Created, BridgeReportMetadata Metadata, string DataHash,
    string BridGHeVersion, string GpcVersion, BridgeReportModel Model, BridgeReportSection[] Sections,
    BridgeReportSample[] Samples, string[] Warnings)
{
    public static readonly JsonSerializerOptions JsonOptions = new()
    { WriteIndented = true, NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals };
    public static readonly string[] TopicNames = { "Modello", "Sezioni e materiali", "Fasi e combinazioni", "Verifiche", "Casi governanti", "Grafici", "Tabulato completo" };
    public static readonly BridgeReportTopic[] DefaultTopics = { BridgeReportTopic.Model, BridgeReportTopic.Sections,
        BridgeReportTopic.Phases, BridgeReportTopic.Checks, BridgeReportTopic.Critical, BridgeReportTopic.Plots };
    [JsonIgnore] public IEnumerable<BridgeReportRow> Rows => Samples.SelectMany(s => s.Checks.Select(c => new BridgeReportRow(s, c)));
    public IEnumerable<BridgeReportRow> Governing(bool byCheck) => Rows.Where(r => r.Check.HasRatio)
        .GroupBy(r => (r.Sample.SectionId, r.Sample.Phase, r.Sample.LimitState, r.Check.Family,
            Check: byCheck ? r.Check.Name : "", Unit: byCheck ? r.Check.Unit : ""))
        .SelectMany(g => { double max = g.Max(r => r.Check.Ratio.Value); return g.Where(r => r.Check.Ratio.Value == max); });
    public static string Number(double? value) => value.HasValue && double.IsFinite(value.Value) ? value.Value.ToString("G6", CultureInfo.InvariantCulture) : "n.d.";
    public static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    public static string Serialize(object value) => JsonSerializer.Serialize(value, JsonOptions);
    public static string ResultIds(IEnumerable<string> values)
    {
        var ids = values.Distinct().OrderBy(x => x, StringComparer.Ordinal).ToArray();
        if (ids.Length >= 3 && int.Parse(ids[^1][1..], CultureInfo.InvariantCulture) - int.Parse(ids[0][1..], CultureInfo.InvariantCulture) + 1 == ids.Length)
            return ids[0] + "–" + ids[^1];
        return string.Join(", ", ids);
    }

    public static BridgeReportData Create(IEnumerable<BridgeReportEntry> entries, BridgeReportMetadata metadata, ModelModel model = null)
    {
        if (metadata == null || string.IsNullOrWhiteSpace(metadata.Title)) throw new ArgumentException("Supply a report title.");
        var input = entries?.ToArray() ?? throw new ArgumentNullException(nameof(entries));
        if (input.Length == 0 || input.Any(e => e?.Result == null)) throw new ArgumentException("Supply at least one bridge verification result.");
        if (model != null && model.Units != ModelModel.ModelUnits.Nmm) throw new ArgumentException("Report model must use N-mm units, as the bridge checker.");
        var sections = new Dictionary<string, BridgeReportSection>();
        var samples = new List<BridgeReportSample>(); var warnings = new List<string>();
        foreach (var entry in input)
        {
            var r = entry.Result; var a = r.Analysis; var i = a.Input;
            var details = JsonSerializer.SerializeToElement(new { i.Studs, i.Intermediate, i.Support, i.Transverse, i.Fatigue, i.Box }, JsonOptions);
            string key = Hash(Encoding.UTF8.GetBytes(Serialize(new { a.Geometry, a.Materials, i.Options, Details = details })));
            if (!sections.TryGetValue(key, out var section))
            {
                section = new("S" + (sections.Count + 1).ToString("D3"), a.Geometry, a.Materials, i.Options, details);
                sections.Add(key, section);
            }
            var checks = new List<BridgeReportCheck>();
            checks.AddRange(r.Stage.Points.Select(p => new BridgeReportCheck("Tensioni", p.Name, p.Stress, p.Limit, "MPa",
                p.Utilization, p.Active, p.Active ? "Punto attivo" : "Punto non attivo", "Materiale " + p.Material + "; y=" + Number(p.Y) + " mm")));
            void Add(IEnumerable<BridgeLocalCheck> values, string family)
            {
                foreach (var c in values ?? Array.Empty<BridgeLocalCheck>())
                {
                    string group = c.Name.StartsWith("Intermedio · ", StringComparison.Ordinal) ? "Irrigiditori intermedi"
                        : c.Name.StartsWith("Appoggio · ", StringComparison.Ordinal) || c.Name.StartsWith("Terminale · ", StringComparison.Ordinal)
                            || c.Name.StartsWith("Seconda coppia terminale · ", StringComparison.Ordinal) ? "Irrigiditori di appoggio" : family;
                    checks.Add(new(group, c.Name, c.Demand, c.Resistance, c.Unit, c.Ratio, true, c.Status, c.Note));
                }
            }
            Add(r.Stage.Shear?.Checks, "Taglio e soletta"); Add(r.Stage.Studs?.Checks, "Pioli"); Add(r.Stage.Torsion?.Checks, "Torsione e diaframmi");
            string id = "R" + (samples.Count + 1).ToString("D5");
            if (r.Source == null) warnings.Add(id + ": provenienza FEM assente; identificazione tramite percorso GH " + entry.TreePosition + ".");
            else if (model != null)
            {
                var beam = model.Beams.SingleOrDefault(b => b.BeamId == r.Source.BeamId);
                if (beam == null || !double.IsFinite(r.Source.StationMm) || r.Source.StationMm < -1e-6 || r.Source.StationMm > beam.Length + 1e-6
                    || !model.LoadCombinations.Any(c => c.Name == r.Source.Combination))
                    throw new ArgumentException(id + ": beam/station/combination does not belong to the supplied report model.");
            }
            if (!checks.Any(c => c.Active)) warnings.Add(id + ": nessuna verifica attiva disponibile.");
            if (!i.Studs.Enabled || !i.Intermediate.Enabled || !i.Support.Enabled)
                warnings.Add(id + ": dettagli disattivati: " + string.Join(", ", new[] { !i.Studs.Enabled ? "pioli" : null, !i.Intermediate.Enabled ? "irrigiditori intermedi" : null, !i.Support.Enabled ? "irrigiditori di appoggio" : null }.Where(s => s != null)) + ". Non inclusi nel massimo WR.");
            samples.Add(new(id, entry.TreePosition, r.Source, section.Id, r.Stage.Name, i.Options.LimitStateName, a.Method, a.Scope,
                r.Stage.Iterations, r.Stage.Residual, r.Stage.Contributions.Select(c => new BridgeReportContribution(c.Name, c.N, c.Mx, c.V, c.HomogenizationN, c.ShrinkageStrain,
                    JsonSerializer.SerializeToElement(c, JsonOptions))).ToArray(), i.Phases.ToArray(), checks.ToArray(), r.Stage.Warnings.ToArray(), r.Assumptions.ToArray()));
        }
        foreach (var g in samples.Where(s => s.Source != null).GroupBy(s => (s.Source, s.Phase, s.LimitState)).Where(g => g.Count() > 1))
            warnings.Add("Provenienza ripetuta: " + string.Join(", ", g.Select(s => s.Id)) + ". I risultati sono conservati separatamente.");
        warnings.AddRange(samples.SelectMany(s => s.Warnings.Select(w => (s.Id, Warning: w))).GroupBy(w => w.Warning)
            .Select(g => ResultIds(g.Select(w => w.Id)) + ": " + g.Key));
        if (model == null) warnings.Add("Modello non fornito: nessuna ricostruzione di geometria globale, casi e coefficienti delle combinazioni.");
        else warnings.Add("Associazione al modello controllata per ID trave, stazione e combinazione; i risultati non contengono un'impronta FEM sorgente per certificare la revisione del modello.");
        var reportModel = model == null ? null : new BridgeReportModel(model.Name, model.Units.ToString(), model.Nodes.Count, model.Beams.Count, model.Plates.Count,
            model.BridgeWorkflow, model.LoadCombinations.Select(c => new BridgeReportCombination(c.Name,
                c.Values.Select(v => new BridgeReportFactor(v.Key.Name, v.Value)).ToArray())).ToArray());
        // Round-trip once to detach all nested arrays/records from the caller's mutable collections.
        var data = new BridgeReportData(Guid.NewGuid().ToString("N"), DateTimeOffset.Now, metadata, "",
            typeof(BridgeReportData).Assembly.GetName().Version.ToString(), typeof(HBridgeSection).Assembly.GetName().Version.ToString(), reportModel,
            sections.Values.ToArray(), samples.ToArray(), warnings.Distinct().ToArray());
        data = JsonSerializer.Deserialize<BridgeReportData>(Serialize(data), JsonOptions);
        return data with { DataHash = Hash(Encoding.UTF8.GetBytes(Serialize(new { data.Model, data.Sections, data.Samples }))) };
    }
}
