using System;
using System.Collections.Generic;
using System.Linq;
using BridGHe.Common.Gpc.Legacy;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;

/// <summary>
/// P2CH (14/11/2026, review of ChatGPT task 83). Run by <c>p2ce-reader</c>.
/// <para>83-02: a standalone Load Combinations parameter whose combinations are in several branches (and an empty one): a case removed and added
/// updates every branch and keeps the tree (paths, empty branches, order), and Undo gives back the combinations as they were; a case added alone
/// updates every branch and leaves the persistent data as it is (as before); the form refuses data of several branches. Before (and in 0.3.0):
/// only the first branch was updated, a removal kept the first branch alone, and the undo record held an empty list.</para>
/// </summary>
internal static class P2chChecks
{
    internal static int Branches(List<string> failures)
    {
        int n = 0;
        void Check(bool ok, string what) { n++; if (!ok) { Console.WriteLine("P2CH BRANCHES check " + n + " FAILED: " + what); failures.Add("P2CH 83-02: " + what); } }
        var plugin = R2aDesignFixtures.Plugin;
        var g = P2ceReader.One("LoadCaseComponent", (0, "G"), (1, false));
        var q = P2ceReader.One("LoadCaseComponent", (0, "Q"), (1, false));
        var w = P2ceReader.One("LoadCaseComponent", (0, "W"), (1, false));
        LegacyCaseData Case(IGH_Goo c) => R2aDesignFixtures.Datum<LegacyCaseData>(c);
        var inc = P2ceReader.One("LoadCombinationComponent", (0, "INC"), (1, new object[] { g, q }), (2, new object[] { 1.35, 0.7 }));
        var odd = P2ceReader.One("LoadCombinationComponent", (0, "ODD"), (1, new object[] { g, q }), (2, new object[] { 0.0, -0.5 }));
        var paramType = plugin.GetTypes().Single(t => t.Name == "LoadCombinationsParam");
        var update = paramType.GetMethod("UpdateLoadCases");

        // A standalone parameter with the tree {0}: INC, {1}: ODD, {3}: empty, solved once.
        (GH_Document Doc, IGH_Param Param) Standalone()
        {
            var doc = new GH_Document();
            var p = (IGH_Param)Activator.CreateInstance(paramType);
            p.CreateAttributes(); doc.AddObject(p, false);
            var data = p.GetType().GetProperty("PersistentData").GetValue(p);
            var append = data.GetType().GetMethods().First(m => m.Name == "Append" && m.GetParameters().Length == 2 && m.GetParameters()[1].ParameterType == typeof(GH_Path));
            var ensure = data.GetType().GetMethods().First(m => m.Name == "EnsurePath" && m.GetParameters().Length == 1 && m.GetParameters()[0].ParameterType == typeof(GH_Path));
            append.Invoke(data, new object[] { inc.Duplicate(), new GH_Path(0) });
            append.Invoke(data, new object[] { odd.Duplicate(), new GH_Path(1) });
            ensure.Invoke(data, new object[] { new GH_Path(3) });
            BridgeSegmentExcelTests.Solve(doc, true);
            return (doc, p);
        }
        string Tree(IGH_Structure t, bool empties = true) => string.Join(" | ", t.Paths.Where(path => empties || t.get_Branch(path).Count > 0)
            .Select(path => path + ": " + (t.get_Branch(path).Count == 0 ? "(empty)"
                : string.Join("; ", t.get_Branch(path).Cast<IGH_Goo>().Select(x => P2ceReader.Factors(R2aDesignFixtures.Datum<LegacyCombinationDraft>(x)))))));
        IGH_Structure Persistent(IGH_Param p) => (IGH_Structure)p.GetType().GetProperty("PersistentData").GetValue(p);
        const string Original = "{0}: INC: G 1.35, Q 0.7 | {1}: ODD: G 0, Q -0.5 | {3}: (empty)";

        // Q removed and W added (G/Q -> G/W): every branch, the tree kept; one undo record that gives back the original combinations.
        var (doc, param) = Standalone();
        Check(Tree(Persistent(param)) == Original && Tree(param.VolatileData, false) == Tree(Persistent(param), false), "the input tree: " + Tree(Persistent(param)));
        int undo = doc.UndoServer.UndoCount;
        bool changed = (bool)update.Invoke(param, new object[] { new[] { Case(g), Case(w) } });
        const string Removed = "{0}: INC: G 1.35, W 1 | {1}: ODD: G 0, W 1 | {3}: (empty)";
        Check(changed && Tree(Persistent(param)) == Removed && Tree(param.VolatileData, false) == Tree(Persistent(param), false),
            "a removal updates every branch and keeps the tree: " + Tree(Persistent(param)) + " (collected: " + Tree(param.VolatileData) + ")");
        Check(doc.UndoServer.UndoCount == undo + 1, "the removal makes one undo record: " + (doc.UndoServer.UndoCount - undo));
        doc.UndoServer.PerformUndo();
        Check(Tree(Persistent(param)) == Original, "Undo gives back the combinations as they were: " + Tree(Persistent(param)));
        doc.Dispose();

        // W added alone (G/Q -> G/Q/W): every collected branch has W with 1; the persistent data is not rewritten (as before), no undo record.
        (doc, param) = Standalone();
        undo = doc.UndoServer.UndoCount;
        changed = (bool)update.Invoke(param, new object[] { new[] { Case(g), Case(q), Case(w) } });
        Check(changed && Tree(Persistent(param)) == Original && Tree(param.VolatileData, false) == "{0}: INC: G 1.35, Q 0.7, W 1 | {1}: ODD: G 0, Q -0.5, W 1"
            && doc.UndoServer.UndoCount == undo, "an addition updates every collected branch: " + Tree(param.VolatileData) + "; persistent " + Tree(Persistent(param)));
        doc.Dispose();

        // The form edits one list: data of several branches refused (its OK kept the first branch alone).
        var rows = plugin.GetType("BridGHe.Grasshopper.Forms.LoadCombinationRows", true);
        var refusal = rows.GetMethod("TreeRefusal", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
        string Refused(int branches) => (string)refusal.Invoke(null, new object[] { branches });
        Check(Refused(2) is string m && m.Contains("more than one branch") && Refused(1) == null && Refused(0) == null, "the form refuses several branches only: «" + Refused(2) + "»");
        return n;
    }

    /// <summary>P2CH (83-01, design of task 102): one standalone Load Combinations wired to two Staged Construction. Different catalogues (G, Q
    /// against G, R; or the same name G with another definition): the conflict, on the parameter and on both stages; no factor changed, no new
    /// solution asked for, in two rounds and in both orders of the stages; the form refused; when one stage is unwired, the conflict goes.
    /// The same catalogue on both (G, W): the combination follows it as before, one deferred expiry, and the next solve asks for nothing.</summary>
    internal static int Catalogues(List<string> failures)
    {
        int n = 0;
        void Check(bool ok, string what) { n++; if (!ok) { Console.WriteLine("P2CH CATALOGUES check " + n + " FAILED: " + what); failures.Add("P2CH 83-01: " + what); } }
        var plugin = R2aDesignFixtures.Plugin;
        var flags = System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic;
        var deferred = plugin.GetType("BridGHe.Grasshopper.Helpers.DeferredExpiry", true);
        int Schedules() => (int)deferred.GetProperty("Schedules", flags).GetValue(null);
        int Waiting(GH_Document d) => (int)deferred.GetMethod("Waiting", flags).Invoke(null, new object[] { d });
        LegacyCaseData Case(IGH_Goo c) => R2aDesignFixtures.Datum<LegacyCaseData>(c);
        IGH_Goo LoadCase(string name) => P2ceReader.One("LoadCaseComponent", (0, name), (1, false));
        var g = LoadCase("G"); var q = LoadCase("Q"); var r = LoadCase("R"); var w = LoadCase("W");
        var inc = P2ceReader.One("LoadCombinationComponent", (0, "INC"), (1, new object[] { g, q }), (2, new object[] { 1.35, 0.7 }));
        var group = P2ceReader.One("GroupComponent", (0, "Deck"));
        var paramType = plugin.GetTypes().Single(t => t.Name == "LoadCombinationsParam");
        var setLoadCases = plugin.GetTypes().Single(t => t.Name == "StagedConstructionComponent").GetMethod("SetLoadCases");
        const string Inc = "INC: G 1.35, Q 0.7";

        // The parameter (INC = 1.35 G + 0.7 Q) and two stages S1 and S2 that take it, with the catalogues a and b (b's stage first when asked).
        (GH_Document Doc, IGH_Param Param, GH_Component A, GH_Component B) Two(LegacyCaseData[] a, LegacyCaseData[] b, bool bFirst)
        {
            var doc = new GH_Document();
            var p = (IGH_Param)Activator.CreateInstance(paramType); p.CreateAttributes(); doc.AddObject(p, false);
            var data = p.GetType().GetProperty("PersistentData").GetValue(p);
            data.GetType().GetMethods().First(m => m.Name == "Append" && m.GetParameters().Length == 1).Invoke(data, new object[] { inc.Duplicate() });
            GH_Component Stage(string name)
            {
                var s = BridgeSegmentExcelTests.Add(doc, "StagedConstructionComponent");
                P2ceReader.Set(s.Params.Input[0], name); P2ceReader.Set(s.Params.Input[1], group); P2ceReader.Set(s.Params.Input[3], false); P2ceReader.Set(s.Params.Input[4], false);
                s.Params.Input[2].RemoveAllSources(); s.Params.Input[2].AddSource(p);
                return s;
            }
            GH_Component sb = bFirst ? Stage("S2") : null, sa = Stage("S1"); sb ??= Stage("S2");
            setLoadCases.Invoke(sa, new object[] { a }); setLoadCases.Invoke(sb, new object[] { b });
            return (doc, p, sa, sb);
        }
        string Persistent(IGH_Param p) => string.Join(" / ", ((IGH_Structure)p.GetType().GetProperty("PersistentData").GetValue(p)).AllData(true)
            .Select(x => P2ceReader.Factors(R2aDesignFixtures.Datum<LegacyCombinationDraft>(x))));
        string Output(GH_Component s) => string.Join(" / ", R2aDesignFixtures.Datum<LegacyStageDraft>(s.Params.Output[0].VolatileData.AllData(true).FirstOrDefault())?.Increments
            .Select(P2ceReader.Factors) ?? new[] { "no stage" });
        bool Conflicted(GH_Component s) => s.RuntimeMessages(GH_RuntimeMessageLevel.Error).Any(e => e.Contains("incompatible load-case catalogues"));
        string Conflict(IGH_Param p) => (string)paramType.GetMethod("CurrentConflict", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Invoke(p, null);
        void Solve(GH_Document d, params GH_Component[] expire) { foreach (var c in expire) c.ExpireSolution(false); BridgeSegmentExcelTests.Solve(d, true); }

        LegacyCaseData[] gq = { Case(g), Case(q) }, gr = { Case(g), Case(r) }, gw = { Case(g), Case(w) };
        foreach (bool bFirst in new[] { false, true })
        {
            string order = bFirst ? " (S2 solved first)" : " (S1 solved first)";
            var (doc, param, a, b) = Two(gq, gr, bFirst);
            int starts = 0; doc.SolutionStart += (s, e) => starts++;
            int s0 = Schedules();
            string conflict = Conflict(param);
            Check(conflict != null && conflict.Contains("Cases: Q, R.") && conflict.Contains("No combinations were changed"), "G, Q against G, R: the conflict before the solve, the form refused" + order + ": " + conflict);
            for (int round = 1; round <= 2; round++)
            {
                if (round == 1) Solve(doc); else Solve(doc, a, b);
                Check(Persistent(param) == Inc && Output(a) == Inc && Output(b) == Inc && Conflicted(a) && Conflicted(b)
                    && param.RuntimeMessages(GH_RuntimeMessageLevel.Error).Contains(Conflict(param)) && Waiting(doc) == 0 && Schedules() == s0 && doc.ScheduleDelay < 0 && starts == round,
                    $"G, Q against G, R, round {round}{order}: no factor changed, the error on the parameter and on both stages, no new solution asked for: "
                    + Persistent(param) + "; " + Output(a) + "; " + Output(b) + $"; waiting {Waiting(doc)}, schedules {Schedules() - s0}, solutions {starts}");
            }
            if (!bFirst)
            {
                // S2 unwired: the conflict goes with the next solve of S1 (and the form opens), the factors as they were.
                b.Params.Input[2].RemoveAllSources();
                Solve(doc, a);
                Check(Conflict(param) == null && !param.RuntimeMessages(GH_RuntimeMessageLevel.Error).Any() && !Conflicted(a) && Persistent(param) == Inc && Output(a) == Inc && doc.ScheduleDelay < 0,
                    "S2 unwired: the conflict gone, 1.35/0.7 kept: " + Persistent(param) + "; " + string.Join(" / ", a.RuntimeMessages(GH_RuntimeMessageLevel.Error)));
            }
            doc.Dispose();
        }

        // The same name with another definition is a conflict too.
        var gOther = Case(g) with { GravityValue = Case(g).GravityValue + 1 };
        var (doc2, param2, _, _) = Two(gq, new[] { gOther, Case(q) }, false);
        Solve(doc2);
        Check(Conflict(param2) is string named && named.Contains("Cases: G.") && Persistent(param2) == Inc, "G with another gravity: the conflict names G: " + Conflict(param2));
        doc2.Dispose();

        // The same catalogue G, W on both stages: as before, Q removed and W added once, one deferred expiry, then nothing more.
        var (doc3, param3, a3, b3) = Two(gw, gw, false);
        int s3 = Schedules();
        Solve(doc3);
        Check(Conflict(param3) == null && Persistent(param3) == "INC: G 1.35, W 1" && Output(a3) == "INC: G 1.35, W 1" && Output(b3) == "INC: G 1.35, W 1"
            && Waiting(doc3) == 1 && Schedules() == s3 + 1, "the same catalogue on both stages: the combination follows it, one request: " + Persistent(param3) + "; " + Output(a3) + "; " + Output(b3));
        Solve(doc3);
        Check(Waiting(doc3) == 0 && Schedules() == s3 + 1 && Output(a3) == "INC: G 1.35, W 1" && Output(b3) == "INC: G 1.35, W 1" && !Conflicted(a3) && !Conflicted(b3),
            "the same catalogue: the deferred expiry solved, nothing asked again");
        Solve(doc3);
        Check(Waiting(doc3) == 0 && Schedules() == s3 + 1 && doc3.ScheduleDelay < 0, "the same catalogue: converged (a third solve asks for nothing)");
        doc3.Dispose();
        return n;
    }
}
