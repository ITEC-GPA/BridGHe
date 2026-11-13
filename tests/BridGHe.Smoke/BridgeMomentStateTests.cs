using System;
using System.Collections.Generic;
using System.Linq;
using BridGHe.Common.Geometry;

/// <summary>
/// <c>--regression bridge-moment-state</c>: the sampled-sign classification of Beam Moment Dispatch on GPC data alone (<see cref="Gpc"/>).
/// P2CB (13/11/2026, rule U3): the girders are those of Bridge Member Assignment on GPC (the plans of a GPC base property: BridGHe IDs, ends and
/// angles of their drafts), the classification the GPC-only overload the component uses. The FEM part (the FEM classification, Bridge State
/// Override on the FEM girders, the merge of the FEM sources with local section states and the checker on the FEM section) used FEM-only APIs
/// and is retired by name (<see cref="P2rRetiredP2cb"/>).
/// </summary>
internal static class BridgeMomentStateTests
{
    internal static void Run()
    {
        int count=0;
        void Check(bool condition,string message){if(!condition)throw new Exception("MOMENT STATE: "+message);count++;}
        void Reject(Action action){try{action();}catch(ArgumentException){count++;return;}throw new Exception("Expected invalid state rejection.");}
        var steel=R2aDesignFixtures.BaseProperty(density:7.85e-9); // the GPC twin of BridgeSegmentSectionsTests.BaseProperty with its density
        var geometry=SimpleBridgeBuilder.Build(SimpleBridgeTests.Input());
        var schedule=BridgeSegmentSections.CreateGpc(geometry,steel,null);
        var plans=BridgeMemberAssignment.Assign(geometry,null,schedule.Rules,new Dictionary<int,BridGHe.Common.Gpc.Legacy.LegacyBeamProperty>{[-1]=steel}).Plans
            .Where(p=>p.Family==BridgeMemberFamily.Girder).ToArray();
        var girders=plans.Select(p=>(Plan:p,Draft:BridGHe.Common.Gpc.Builders.GpcBridgeGeometryBuilders.Assigned(p,R2aDesignFixtures.Id("moment-state:"+p.SolverId)))).ToArray();
        Gpc(girders.Select(g=>g.Plan.SolverId).ToArray(),girders.Select(g=>(g.Plan.From,g.Plan.To,g.Draft.AngleDeg)).ToArray(),Check,Reject);
        Console.WriteLine($"MOMENT STATE PASS: {count} assertions; sampled sign on GPC data alone (classes, extrema, BM1, orientation, rejections). No native FEM.");
    }
    /// <summary>R6.6 R2b: the sampled-sign classification of Beam Moment Dispatch on GPC data alone: the girders are given by their BridGHe IDs
    /// (no FEM beam: the GPC-only overload, the state has no FEM source), each with its analysed GPC beam built in GPC with the «Dead» samples
    /// of the fixture (BM1 −1e8; BM2 by girder i % 5: +1e8, −1e8, (s − 0.5)·2e8, 500, +1e8; girder 5 without results). Explicit expected
    /// classes and extrema (they replace the former parity with the FEM classification): Positive +1e8, Negative −1e8, Mixed −1e8..+1e8, Zero
    /// 500 within the tolerance 1000, Missing; BM1 Negative; the orientation sign; the rejections.</summary>
    private static void Gpc(string[] ids, (Rhino.Geometry.Point3d From, Rhino.Geometry.Point3d To, double Angle)[] girders, Action<bool, string> check, Action<Action> reject)
    {
        (BridGHe.Common.Gpc.GpcResultCatalog Catalog, GPC.Model.Models.Model Model, GPC.Model.Elements.BeamElement Beam)[] Analysed(string name,
            Action<GpcTestModel, GPC.Model.Elements.BeamElement[]> change = null, bool firstStart = true)
        {
            var test = new GpcTestModel(name); test.Case("Dead");
            var nodes = new Dictionary<Rhino.Geometry.Point3d, GPC.Model.Elements.NodeElement>();
            GPC.Model.Elements.NodeElement Node(Rhino.Geometry.Point3d p)
            {
                if (!nodes.TryGetValue(p, out var n)) nodes.Add(p, n = test.Node((nodes.Count + 1).ToString(), p.X, p.Y, p.Z));
                return n;
            }
            var gpcBeams = girders.Select((g, i) => test.Beam(ids[i], Node(g.From), Node(g.To), g.Angle)).ToArray();
            for (int i = 0; i < gpcBeams.Length; i++)
            {
                if (i == 4) continue; // the girder without results of the fixture
                foreach (double s in new[] { 0d, .5, 1d })
                {
                    if (i == 0 && s == 0 && !firstStart) continue;
                    test.Forces(gpcBeams[i], "Dead", s * gpcBeams[i].Length, 0, bm1: -1e8, bm2: Bm2(i, s));
                }
            }
            change?.Invoke(test, gpcBeams);
            var model = test.Analyse(); var catalog = BridGHe.Common.Gpc.GpcResultCatalog.Of(model);
            return gpcBeams.Select(g => (catalog, model, g)).ToArray();
        }
        static double Bm2(int i, double s) => (i % 5) switch { 0 => 1e8, 1 => -1e8, 2 => (s - .5) * 2e8, 3 => 500, _ => 1e8 };
        // The GPC-only overload of Beam Moment Dispatch (the beams by their IDs and analysed GPC beams; P2CB: before, the overload with a null FEM beam each).
        BeamMomentState[] Classify(string[] which, (BridGHe.Common.Gpc.GpcResultCatalog, GPC.Model.Models.Model, GPC.Model.Elements.BeamElement)[] analysed, int component = 1, int sign = 1, double tolerance = 1000) =>
            BeamMomentClassification.Classify(which, analysed, "Dead", tolerance, component, sign);
        var all = Analysed("Moment state girders");
        var states = Classify(ids, all);
        check(states.Take(5).Select(s => s.Sign).SequenceEqual(new[] { BeamMomentSign.Positive, BeamMomentSign.Negative, BeamMomentSign.Mixed, BeamMomentSign.Zero, BeamMomentSign.Missing }),
            "GPC: five classes, tolerance and BM2 default");
        // Explicit classes and extrema of every girder (within 1e-9: the GPC rows pass through the section axes of the beam angle).
        static bool Near(double a, double b) => double.IsNaN(b) ? double.IsNaN(a) : Math.Abs(a - b) <= 1e-9 * Math.Max(1, Math.Abs(b));
        for (int i = 0; i < states.Length; i++)
        {
            var s = states[i];
            var (sign, min, max) = i == 4 ? (BeamMomentSign.Missing, double.NaN, double.NaN) : (i % 5) switch
            {
                0 => (BeamMomentSign.Positive, 1e8, 1e8), 1 => (BeamMomentSign.Negative, -1e8, -1e8), 2 => (BeamMomentSign.Mixed, -1e8, 1e8),
                3 => (BeamMomentSign.Zero, 500d, 500d), _ => (BeamMomentSign.Positive, 1e8, 1e8)
            };
            check(s.Sign == sign && Near(s.Minimum, min) && Near(s.Maximum, max) && s.Case == "Dead" && s.BeamId == ids[i] && s.Source == null && (i == 4 || s.Analysed.Beam == all[i].Beam),
                $"GPC: girder {ids[i]}: {s}, expected {sign} {min} .. {max}");
        }
        check(Classify(ids, all, component: 0)[0].Sign == BeamMomentSign.Negative, "GPC: BM1 selection independent");
        check(Classify(ids, all, sign: -1)[0].Sign == BeamMomentSign.Negative, "GPC: orientation sign explicit");
        reject(() => Classify(ids, all, tolerance: -1));
        reject(() => Classify(new[] { ids[0], ids[0] }, new[] { all[0], all[0] }));
        reject(() => BeamMomentClassification.Classify(ids, all.Take(2).ToArray(), "Dead"));
        var withoutStart = Analysed("Moment state girders, no start sample", firstStart: false);
        reject(() => Classify(new[] { ids[0] }, new[] { withoutStart[0] }));
        // A duplicate sample never reaches the classification (rejected by the result mapper or by the classification).
        bool duplicate = false;
        try { var twice = Analysed("Moment state girders, duplicate", (t, g) => t.Forces(g[0], "Dead", 0, 0, bm1: -1e8, bm2: 1e8)); Classify(new[] { ids[0] }, new[] { twice[0] }); }
        catch (ArgumentException) { duplicate = true; }
        catch (InvalidOperationException e) when (e.Message.StartsWith("GpcTestModel results not imported")) { duplicate = true; }
        check(duplicate, "GPC: duplicate sample rejected");
    }
}
