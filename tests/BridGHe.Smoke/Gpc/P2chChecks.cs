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
}
