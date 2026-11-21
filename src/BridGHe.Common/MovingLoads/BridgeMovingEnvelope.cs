using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using BridGHe.Common.Checks;

namespace BridGHe.Common.MovingLoads;

/// <summary>How the positions of a moving load enter the checks (BridGHe 0.7.0, user decisions of 21/11/2026).</summary>
public enum BridgeMovingMode
{
    /// <summary>Every position its own combination, with its concomitant forces: the most precise, the slowest (P combinations).</summary>
    Positions = 0,
    /// <summary>At every station the maximum and minimum of N, V and M among the positions, combined in the 8 corners (N±, V±, M±): covers
    /// every position, so it is always on the safe side; 8 combinations instead of P.</summary>
    Envelope = 1,
    /// <summary>At every station the position of the maximum and of the minimum of N, of V and of M, each with its concomitant forces:
    /// 6 combinations instead of P; precise for the governing quantity, not guaranteed on the safe side for N–M interaction.</summary>
    Concomitant = 2,
}

/// <summary>The moving positions of a combination table (the actions «prefix_0001», «prefix_0002», ... of Bridge Moving Combinations)
/// replaced by envelope actions «prefix@rule»: the table of Bridge Moving Envelope, and the samples Build BridGHe Model gives those actions.
/// The forces of the checks are N (Axial), V (Shear2) and M (Bending2) of the merged samples, the plane N–Mx–Vy of the checks; the other
/// components (Shear1, Torque, Bending1) are those of the position of the governing moment.</summary>
public static class BridgeMovingEnvelope
{
    public const char Separator = '@';
    /// <summary>The 8 corners of the envelope (N, V, M each at its maximum or minimum).</summary>
    public static readonly string[] EnvelopeRules = (from n in new[] { '+', '-' } from v in new[] { '+', '-' } from m in new[] { '+', '-' } select $"N{n}V{v}M{m}").ToArray();
    /// <summary>The 6 concomitant cases (the position of the extreme of one quantity, with its other forces).</summary>
    public static readonly string[] ConcomitantRules = { "Nmax", "Nmin", "Vmax", "Vmin", "Mmax", "Mmin" };

    public static IReadOnlyList<string> Rules(BridgeMovingMode mode) => mode switch
    {
        BridgeMovingMode.Envelope => EnvelopeRules,
        BridgeMovingMode.Concomitant => ConcomitantRules,
        _ => Array.Empty<string>(),
    };

    /// <summary>True for an envelope action key «prefix@rule» with a known rule.</summary>
    public static bool IsEnvelope(string key) => TryParse(key, out _, out _);

    public static bool TryParse(string key, out string prefix, out string rule)
    {
        prefix = rule = null;
        int at = key?.LastIndexOf(Separator) ?? -1;
        if (at <= 0 || at == key.Length - 1) return false;
        prefix = key.Substring(0, at); rule = key.Substring(at + 1);
        return EnvelopeRules.Contains(rule, StringComparer.Ordinal) || ConcomitantRules.Contains(rule, StringComparer.Ordinal);
    }

    /// <summary>True for a position action of Bridge Moving Combinations: «prefix_» and digits.</summary>
    public static bool IsPosition(string key, string prefix) =>
        key != null && prefix != null && key.Length > prefix.Length + 1 && key.StartsWith(prefix + "_", StringComparison.Ordinal) && key.Substring(prefix.Length + 1).All(char.IsDigit);

    public static string Describe(string rule, string prefix, int positions) => EnvelopeRules.Contains(rule, StringComparer.Ordinal)
        ? $"inviluppo {rule} di {positions} posizioni {prefix}"
        : $"concomitanti di {rule} fra {positions} posizioni {prefix}";

    /// <summary>The result of <see cref="Table"/>: the rows, and a summary for a Panel.</summary>
    public sealed record TableResult(string[][] Rows, string[] Summary);

    /// <summary>The table of Bridge Moving Envelope. Positions: the table unchanged. Envelope and Concomitant: every group of rows that differ
    /// only in the one position with a non-zero coefficient (same coefficients elsewhere, same coefficient of the position) is replaced by one
    /// row per rule, named «base + prefix@rule» (base: the name without « + prefix_NNNN»), with that coefficient on the column «prefix@rule»;
    /// the position columns stay in the header with zeros (their actions stay in the phases), the rule columns are appended. Rows without
    /// positions stay as they are. A row with two positions is refused.</summary>
    public static TableResult Table(IReadOnlyList<string[]> rows, BridgeMovingMode mode, string prefix)
    {
        if (rows == null || rows.Count < 2 || rows[0] == null || rows[0].Length < 2) throw new ArgumentException("Collegare una tabella di combinazioni: intestazione e almeno una riga.");
        if (!Enum.IsDefined(mode)) throw new ArgumentException("Mode: 0 Posizioni, 1 Inviluppo, 2 Concomitanti.");
        if (string.IsNullOrWhiteSpace(prefix) || prefix != prefix.Trim() || prefix.Contains(Separator)) throw new ArgumentException("Action prefix: il prefisso delle posizioni di Bridge Moving Combinations, senza spazi agli estremi e senza «@».");
        int width = rows[0].Length;
        if (rows.Any(r => r == null || r.Length != width)) throw new ArgumentException("Tutte le righe della tabella devono avere le colonne dell'intestazione.");
        var header = rows[0];
        var positions = Enumerable.Range(1, width - 1).Where(c => IsPosition(header[c], prefix)).ToArray();
        if (mode == BridgeMovingMode.Positions)
            return new TableResult(rows.Select(r => r.ToArray()).ToArray(), new[] { $"Posizioni: {positions.Length} posizioni {prefix}, tabella invariata ({rows.Count - 1} combinazioni)." });
        if (positions.Length == 0) throw new ArgumentException($"Nessuna colonna «{prefix}_NNNN» nella tabella: collegare la tabella di Bridge Moving Combinations o correggere Action prefix.");
        if (header.Skip(1).Any(h => TryParse(h, out var p, out _) && p == prefix)) throw new ArgumentException("La tabella contiene già le colonne d'inviluppo di " + prefix + ".");
        var rules = Rules(mode);
        double Value(string s) => double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double v) && double.IsFinite(v) ? v
            : throw new ArgumentException($"Coefficiente non valido «{s}».");
        var isPosition = new bool[width]; foreach (int c in positions) isPosition[c] = true;
        var output = new List<string[]> { header.Concat(rules.Select(r => prefix + Separator + r)).ToArray() };
        var groups = new List<(string Key, string Name, string Factor, string[] Row, int Count)>();
        int kept = 0;
        foreach (var row in rows.Skip(1))
        {
            var used = positions.Where(c => Value(row[c]) != 0).ToArray();
            if (used.Length == 0) { output.Add(row.Concat(rules.Select(_ => "0")).ToArray()); kept++; continue; }
            if (used.Length > 1) throw new ArgumentException($"La combinazione «{row[0]}» ha più posizioni del mobile ({string.Join(", ", used.Select(c => header[c]))}): l'inviluppo richiede una posizione per combinazione.");
            var rest = Enumerable.Range(1, width - 1).Select(c => isPosition[c] ? "0" : Value(row[c]).ToString("R", CultureInfo.InvariantCulture));
            string factor = Value(row[used[0]]).ToString("R", CultureInfo.InvariantCulture);
            string key = string.Join("|", rest) + "|" + factor;
            string suffix = " + " + header[used[0]];
            string name = row[0].EndsWith(suffix, StringComparison.Ordinal) ? row[0].Substring(0, row[0].Length - suffix.Length)
                : row[0].EndsWith(header[used[0]], StringComparison.Ordinal) ? row[0].Substring(0, row[0].Length - header[used[0]].Length).TrimEnd() : row[0];
            int g = groups.FindIndex(x => x.Key == key);
            if (g < 0) groups.Add((key, name, factor, row, 1));
            else groups[g] = (groups[g].Key, groups[g].Name == name ? name : groups[g].Name, factor, groups[g].Row, groups[g].Count + 1);
        }
        var names = new HashSet<string>(output.Skip(1).Select(r => r[0]), StringComparer.Ordinal);
        foreach (var group in groups)
            foreach (var rule in rules)
            {
                var row = group.Row.Select((v, c) => c == 0 ? null : isPosition[c] ? "0" : v).Concat(rules.Select(r => r == rule ? group.Factor : "0")).ToArray();
                string glue = group.Name.Length == 0 || group.Name.EndsWith("_", StringComparison.Ordinal) ? "" : " + ";
                row[0] = group.Name + glue + prefix + Separator + rule;
                if (!names.Add(row[0])) throw new ArgumentException("Nome di combinazione già presente: " + row[0]);
                output.Add(row);
            }
        var summary = new[]
        {
            $"{(mode == BridgeMovingMode.Envelope ? "Inviluppo (8 angoli N±, V±, M±)" : "Concomitanti (Nmax, Nmin, Vmax, Vmin, Mmax, Mmin)")}: {positions.Length} posizioni {prefix}.",
            $"{groups.Sum(g => g.Count)} combinazioni con una posizione → {groups.Count * rules.Count} ({groups.Count} gruppi × {rules.Count}); {kept} combinazioni senza posizioni invariate.",
        };
        return new TableResult(output.ToArray(), summary);
    }

    /// <summary>The samples of an envelope action of a beam from the samples of its positions resampled on the same stations (one array per
    /// position, the same stations in the same order).</summary>
    public static BeamForceSample[] Samples(IReadOnlyList<BeamForceSample[]> positions, string rule)
    {
        if (positions == null || positions.Count == 0) throw new ArgumentException("Nessuna posizione per l'inviluppo.");
        int n = positions[0].Length;
        if (positions.Any(p => p.Length != n)) throw new ArgumentException("Le posizioni dell'inviluppo devono avere le stesse stazioni.");
        var output = new BeamForceSample[n];
        for (int s = 0; s < n; s++)
        {
            int Arg(Func<BeamForceSample, double> f, bool max)
            {
                int best = 0;
                for (int k = 1; k < positions.Count; k++)
                    if (max ? f(positions[k][s]) > f(positions[best][s]) : f(positions[k][s]) < f(positions[best][s])) best = k;
                return best;
            }
            double station = positions[0][s].Station;
            if (ConcomitantRules.Contains(rule, StringComparer.Ordinal))
            {
                Func<BeamForceSample, double> quantity = rule[0] switch { 'N' => x => x.Axial, 'V' => x => x.Shear2, _ => x => x.Bending2 };
                output[s] = positions[Arg(quantity, rule.EndsWith("max", StringComparison.Ordinal))][s] with { Station = station };
                continue;
            }
            if (!EnvelopeRules.Contains(rule, StringComparer.Ordinal)) throw new ArgumentException("Regola d'inviluppo sconosciuta: " + rule);
            bool nMax = rule[1] == '+', vMax = rule[3] == '+', mMax = rule[5] == '+';
            var governing = positions[Arg(x => x.Bending2, mMax)][s];
            output[s] = governing with
            {
                Station = station,
                Axial = positions[Arg(x => x.Axial, nMax)][s].Axial,
                Shear2 = positions[Arg(x => x.Shear2, vMax)][s].Shear2,
            };
        }
        return output;
    }
}
