using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
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

    /// <summary>P2K (P6): the JSON of <paramref name="value"/> written to <paramref name="stream"/>: the same UTF-8 bytes as
    /// <c>Encoding.UTF8.GetBytes(Serialize(value))</c>, without the .NET string (the data of a large report, example 50, is longer than the
    /// longest string: OutOfMemoryException).</summary>
    public static void Serialize(Stream stream, object value) => JsonSerializer.Serialize(stream, value, JsonOptions);

    /// <summary>P2K (P6): <c>Hash(Encoding.UTF8.GetBytes(Serialize(value)))</c>, computed on the stream of the JSON (no string, no array of
    /// the whole JSON).</summary>
    public static string HashOf(object value)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        using (var sink = new HashSink(hash)) Serialize(sink, value);
        return Convert.ToHexString(hash.GetHashAndReset());
    }

    /// <summary>P2K (P6): a JSON round trip through a buffer of chunks (no string and no single array, so no size limit of one .NET object).</summary>
    private static T RoundTrip<T>(T value)
    {
        using var buffer = new ChunkBuffer();
        Serialize(buffer, value);
        buffer.Rewind();
        return JsonSerializer.Deserialize<T>(buffer, JsonOptions);
    }

    /// <summary>A write-only stream that feeds a hash.</summary>
    private sealed class HashSink : Stream
    {
        private readonly IncrementalHash hash;
        internal HashSink(IncrementalHash hash) => this.hash = hash;
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => hash.AppendData(buffer, offset, count);
        public override void Write(ReadOnlySpan<byte> buffer) => hash.AppendData(buffer);
    }

    /// <summary>A stream written once, then read once from the start (<see cref="Rewind"/>), held in chunks of 16 MiB.</summary>
    private sealed class ChunkBuffer : Stream
    {
        private const int Size = 1 << 24;
        private readonly List<byte[]> chunks = new();
        private long length, position;
        private bool reading;
        internal void Rewind() { reading = true; position = 0; }
        public override bool CanRead => reading;
        public override bool CanSeek => false;
        public override bool CanWrite => !reading;
        public override long Length => length;
        public override long Position { get => position; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));
        public override void Write(ReadOnlySpan<byte> buffer)
        {
            if (reading) throw new NotSupportedException();
            while (buffer.Length > 0)
            {
                int at = (int)(length % Size);
                if (at == 0 && length / Size == chunks.Count) chunks.Add(new byte[Size]);
                int n = Math.Min(Size - at, buffer.Length);
                buffer.Slice(0, n).CopyTo(chunks[(int)(length / Size)].AsSpan(at));
                buffer = buffer.Slice(n); length += n;
            }
        }
        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));
        public override int Read(Span<byte> buffer)
        {
            if (!reading) throw new NotSupportedException();
            int total = 0;
            while (buffer.Length > 0 && position < length)
            {
                int at = (int)(position % Size);
                int n = (int)Math.Min(Math.Min(Size - at, buffer.Length), length - position);
                chunks[(int)(position / Size)].AsSpan(at, n).CopyTo(buffer);
                buffer = buffer.Slice(n); position += n; total += n;
            }
            return total;
        }
        protected override void Dispose(bool disposing) { chunks.Clear(); base.Dispose(disposing); }
    }
    public static string ResultIds(IEnumerable<string> values)
    {
        var ids = values.Distinct().OrderBy(x => x, StringComparer.Ordinal).ToArray();
        if (ids.Length >= 3 && int.Parse(ids[^1][1..], CultureInfo.InvariantCulture) - int.Parse(ids[0][1..], CultureInfo.InvariantCulture) + 1 == ids.Length)
            return ids[0] + "–" + ids[^1];
        return string.Join(", ", ids);
    }

    /// <summary>F6 R6.4 (6): the model of a report as <see cref="Create(IEnumerable{BridgeReportEntry}, BridgeReportMetadata, ReportModelSource)"/>
    /// reads it: whether it is N-mm, the length of a beam by its BridGHe ID (null: no such beam), the combination names, and the report model.</summary>
    public sealed record ReportModelSource(bool Nmm, Func<string, double?> BeamLength, IReadOnlyCollection<string> Combinations, BridgeReportModel Model);


    /// <summary>F6 R6.4 (6): the model of a report from the GPC bridge model of Build BridGHe Model (N-mm by construction): its name and counts,
    /// the records of its actions and its combination table with the zero coefficients (<see cref="Gpc.GpcBridge.Records"/>,
    /// <see cref="Gpc.GpcBridge.Combinations"/>, as the merged FEM model kept them), the length of a beam as the distance of Rhino between its ends.</summary>
    public static ReportModelSource Source(Gpc.GpcBridge bridge)
    {
        if (bridge == null) return null;
        var m = bridge.Model;
        var beams = m.BeamElements.Values.GroupBy(b => b.Source?.OriginalId ?? b.Name, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.ToArray(), StringComparer.Ordinal);
        static double Length(GPC.Model.Elements.BeamElement b) => new Rhino.Geometry.Point3d(b.StartPoint.X, b.StartPoint.Y, b.StartPoint.Z).DistanceTo(new Rhino.Geometry.Point3d(b.EndPoint.X, b.EndPoint.Y, b.EndPoint.Z));
        return new ReportModelSource(true, id => id != null && beams.TryGetValue(id, out var b) ? b.Length == 1 ? Length(b[0]) : throw new InvalidOperationException("Sequence contains more than one matching element") : null,
            bridge.Combinations.Select(c => c.Name).ToArray(),
            new BridgeReportModel(m.Name, "Nmm" /* P2CE phase 2a: the text of the former ModelModel.ModelUnits.Nmm */, m.NodesElements.Count, m.BeamElements.Count, m.AreaElements.Count, bridge.Records,
                bridge.Combinations.Select(c => new BridgeReportCombination(c.Name, c.Factors.Select(f => new BridgeReportFactor(f.Case, f.Factor)).ToArray())).ToArray()));
    }

    /// <summary>R6.5 P1 lane 3a (coordinator, 8/11): the model of a report from the GPC input of a model given by it alone, without the GPC bridge
    /// model (a model of Build Model or of an analysis): the units of the input, its name and counts, no bridge workflow (the FEM model has one
    /// only as the merged model of Build BridGHe Model), its combinations with their cases and factors in order, the length of a beam by its BridGHe ID (the
    /// original ID of its source) as the distance of Rhino between its ends (N-mm: the coordinates of the FEM model).</summary>
    public static ReportModelSource Source(Gpc.GpcModels.Conversion input)
    {
        if (input == null) return null;
        var m = input.Model; var units = input.Report.Units.Units;
        var beams = m.BeamElements.Values.GroupBy(b => b.Source?.OriginalId ?? b.Name, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.ToArray(), StringComparer.Ordinal);
        static double Length(GPC.Model.Elements.BeamElement b) => new Rhino.Geometry.Point3d(b.StartPoint.X, b.StartPoint.Y, b.StartPoint.Z).DistanceTo(new Rhino.Geometry.Point3d(b.EndPoint.X, b.EndPoint.Y, b.EndPoint.Z));
        var combinations = m.Combinations.Values.Select(c => new BridgeReportCombination(c.Name, c.GetLoadCaseCoefficientsPair().Select(f => new BridgeReportFactor(f.Key.Name, f.Value)).ToArray())).ToArray();
        return new ReportModelSource(units == Gpc.DefinitionUnits.Nmm, id => id != null && beams.TryGetValue(id, out var b) ? b.Length == 1 ? Length(b[0]) : throw new InvalidOperationException("Sequence contains more than one matching element") : null,
            combinations.Select(c => c.Name).ToArray(),
            new BridgeReportModel(m.Name, units.ToString(), m.NodesElements.Count, m.BeamElements.Count, m.AreaElements.Count, null, combinations));
    }

    /// <summary>R6.4: the canonical text of the model of a report (for the comparison of the GPC and the FEM sources until R6.5).</summary>
    public static string Text(ReportModelSource source, IEnumerable<string> beamIds) => source == null ? "null" : source.Nmm + "\n" + Serialize(source.Model) + "\n"
        + string.Join("|", source.Combinations) + "\n" + string.Join("|", beamIds.Select(id => id + "=" + (source.BeamLength(id) is double l ? BitConverter.DoubleToInt64Bits(l).ToString("X16") : "-")));

    /// <summary>P2CE (13/11/2026, phase 1): the overload on the merged FEM model removed; no source (null) is a report without a model, as before.</summary>
    public static BridgeReportData Create(IEnumerable<BridgeReportEntry> entries, BridgeReportMetadata metadata, ReportModelSource model = null)
    {
        if (metadata == null || string.IsNullOrWhiteSpace(metadata.Title)) throw new ArgumentException("Supply a report title.");
        var input = entries?.ToArray() ?? throw new ArgumentNullException(nameof(entries));
        if (input.Length == 0 || input.Any(e => e?.Result == null)) throw new ArgumentException("Supply at least one bridge verification result.");
        if (model != null && !model.Nmm) throw new ArgumentException("Report model must use N-mm units, as the bridge checker.");
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
                var length = model.BeamLength(r.Source.BeamId);
                if (length == null || !double.IsFinite(r.Source.StationMm) || r.Source.StationMm < -1e-6 || r.Source.StationMm > length.Value + 1e-6
                    || !model.Combinations.Any(c => c == r.Source.Combination))
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
        var reportModel = model?.Model;
        // Round-trip once to detach all nested arrays/records from the caller's mutable collections.
        var data = new BridgeReportData(Guid.NewGuid().ToString("N"), DateTimeOffset.Now, metadata, "",
            typeof(BridgeReportData).Assembly.GetName().Version.ToString(), typeof(HBridgeSection).Assembly.GetName().Version.ToString(), reportModel,
            sections.Values.ToArray(), samples.ToArray(), warnings.Distinct().ToArray());
        // P2K (P6): the round trip and the hash on streams, the same JSON bytes and values as through one string (OutOfMemoryException on 50).
        data = RoundTrip(data);
        return data with { DataHash = HashOf(new { data.Model, data.Sections, data.Samples }) };
    }
}
