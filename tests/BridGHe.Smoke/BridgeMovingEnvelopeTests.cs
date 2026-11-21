using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using BridGHe.Common.Checks;
using BridGHe.Common.MovingLoads;

/// <summary>0.7.0 (user decisions of 21/11/2026): Bridge Moving Envelope. The table of the three modes (positions unchanged; 8 envelope
/// corners; 6 concomitant cases), the refusals, and the samples of the rules on known positions: every corner bounds every position, every
/// concomitant case is the sample of one position.</summary>
internal static class BridgeMovingEnvelopeTests
{
    public static void Run()
    {
        int checks = 0;
        void Require(bool ok, string what) { if (!ok) throw new Exception("MOVING ENVELOPE: " + what); checks++; }
        static bool Throws(Action a) { try { a(); return false; } catch (ArgumentException) { return true; } }
        var table = new[]
        {
            new[] { "Combinazione", "G1", "G2", "Q_MOV_0001", "Q_MOV_0002", "Q_MOV_0003" },
            new[] { "SLU", "1.35", "1.5", "0", "0", "0" },
            new[] { "SLU + Q_MOV_0001", "1.35", "1.5", "1.35", "0", "0" },
            new[] { "SLU + Q_MOV_0002", "1.35", "1.5", "0", "1.35", "0" },
            new[] { "SLU + Q_MOV_0003", "1.35", "1.5", "0", "0", "1.35" },
            new[] { "TEST_Q_MOV_0001", "0", "0", "1", "0", "0" },
            new[] { "TEST_Q_MOV_0002", "0", "0", "0", "1", "0" },
            new[] { "TEST_Q_MOV_0003", "0", "0", "0", "0", "1" },
        };
        var same = BridgeMovingEnvelope.Table(table, BridgeMovingMode.Positions, "Q_MOV");
        Require(same.Rows.Length == table.Length && same.Rows.Zip(table, (a, b) => a.SequenceEqual(b)).All(x => x), "Positions: the table unchanged");
        var envelope = BridgeMovingEnvelope.Table(table, BridgeMovingMode.Envelope, "Q_MOV");
        Require(envelope.Rows[0].Length == 6 + 8 && envelope.Rows[0].Skip(6).SequenceEqual(BridgeMovingEnvelope.EnvelopeRules.Select(r => "Q_MOV@" + r)), "Envelope: the 8 corner columns appended");
        Require(envelope.Rows.Length == 1 + 1 + 8 + 8, "Envelope: the base row, then 8 rows for each of the 2 groups");
        var corner = envelope.Rows.Single(r => r[0] == "SLU + Q_MOV@N+V-M+");
        Require(corner.Skip(1).Take(5).SequenceEqual(new[] { "1.35", "1.5", "0", "0", "0" }) && corner[6 + Array.IndexOf(BridgeMovingEnvelope.EnvelopeRules, "N+V-M+")] == "1.35"
            && corner.Skip(6).Count(v => v != "0") == 1, "Envelope: the base coefficients, the positions at zero, the factor on its corner");
        Require(envelope.Rows.Any(r => r[0] == "TEST_Q_MOV@N-V-M-") && envelope.Rows.Count(r => r[0].StartsWith("TEST_", StringComparison.Ordinal)) == 8, "Envelope: the isolated positions are a group too");
        Require(envelope.Rows.Single(r => r[0] == "SLU").Skip(6).All(v => v == "0"), "Envelope: a row without positions unchanged, zero on the corners");
        var concomitant = BridgeMovingEnvelope.Table(table, BridgeMovingMode.Concomitant, "Q_MOV");
        Require(concomitant.Rows.Length == 1 + 1 + 6 + 6 && concomitant.Rows.Any(r => r[0] == "SLU + Q_MOV@Mmax"), "Concomitant: 6 rows per group");
        Require(Throws(() => BridgeMovingEnvelope.Table(table.Append(new[] { "DUE", "0", "0", "1", "1", "0" }).ToArray(), BridgeMovingMode.Envelope, "Q_MOV")), "a row with two positions is refused");
        Require(Throws(() => BridgeMovingEnvelope.Table(table, BridgeMovingMode.Envelope, "Q_X")), "no position of the prefix: refused");
        Require(Throws(() => BridgeMovingEnvelope.Table(envelope.Rows, BridgeMovingMode.Envelope, "Q_MOV")), "an envelope of an envelope is refused");
        Require(BridgeMovingEnvelope.IsEnvelope("Q_MOV@N+V+M+") && BridgeMovingEnvelope.IsEnvelope("Q_MOV@Vmin") && !BridgeMovingEnvelope.IsEnvelope("Q_MOV@X")
            && !BridgeMovingEnvelope.IsEnvelope("Q_MOV_0001") && BridgeMovingEnvelope.IsPosition("Q_MOV_0012", "Q_MOV") && !BridgeMovingEnvelope.IsPosition("Q_MOV@Mmax", "Q_MOV"), "envelope and position keys");

        // Samples: three positions at two stations (Axial, Shear2, Bending2 the forces of the checks; Bending1 marks the position).
        BeamForceSample S(double station, double n, double v, double m, double mark) => new(station, n, 0, v, 0, mark, m);
        var positions = new[]
        {
            new[] { S(0, 10, -5, 100, 1), S(1000, -2, 3, -50, 1) },
            new[] { S(0, -4, 7, 300, 2), S(1000, 6, -1, 20, 2) },
            new[] { S(0, 1, 2, -200, 3), S(1000, 0, 9, 80, 3) },
        };
        var top = BridgeMovingEnvelope.Samples(positions, "N+V+M+");
        Require(top[0].Axial == 10 && top[0].Shear2 == 7 && top[0].Bending2 == 300 && top[0].Bending1 == 2 && top[1].Axial == 6 && top[1].Shear2 == 9 && top[1].Bending2 == 80,
            "N+V+M+: the maxima of N, V, M at every station, the other forces of the position of the maximum moment");
        var low = BridgeMovingEnvelope.Samples(positions, "N-V-M-");
        Require(low[0].Axial == -4 && low[0].Shear2 == -5 && low[0].Bending2 == -200 && low[1].Bending2 == -50 && low[1].Bending1 == 1, "N-V-M-: the minima");
        foreach (var rule in BridgeMovingEnvelope.EnvelopeRules)
        {
            var c = BridgeMovingEnvelope.Samples(positions, rule);
            for (int s = 0; s < 2; s++)
                Require(positions.All(p => (rule[1] == '+' ? p[s].Axial <= c[s].Axial : p[s].Axial >= c[s].Axial) && (rule[3] == '+' ? p[s].Shear2 <= c[s].Shear2 : p[s].Shear2 >= c[s].Shear2)
                    && (rule[5] == '+' ? p[s].Bending2 <= c[s].Bending2 : p[s].Bending2 >= c[s].Bending2)), $"{rule}, station {s}: every position inside the corner");
        }
        var mmax = BridgeMovingEnvelope.Samples(positions, "Mmax"); var vmin = BridgeMovingEnvelope.Samples(positions, "Vmin");
        Require(mmax[0] == positions[1][0] && mmax[1] == positions[2][1] && vmin[0] == positions[0][0] && vmin[1] == positions[1][1], "Mmax, Vmin: the sample of one position, all its forces");
        Require(Throws(() => BridgeMovingEnvelope.Samples(new[] { positions[0], positions[1].Take(1).ToArray() }, "Mmax")), "positions on different stations are refused");
        Console.WriteLine($"BRIDGE MOVING ENVELOPE PASS: {checks} checks (tables of the three modes, refusals, corners bounding every position, concomitant samples).");
    }
}
