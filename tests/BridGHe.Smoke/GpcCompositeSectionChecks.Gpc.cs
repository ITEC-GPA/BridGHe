using System;
using System.Linq;
using BridGHe.Common.Gpc;
using GPC.Checkers.CompositeBridge;
using GPC.Model.Sections;

internal static partial class GpcCompositeSectionChecks
{
    /// <summary>
    /// Chain E (c), native (50, 99): for every composite beam of a solved Super Element Check, the definitions the component builds (from the
    /// definitions the beam carries: <c>GH_CompositeBeam.StartDefinition</c>/<c>EndDefinition</c>) at the stations of the check (the merged
    /// samples of the GPC bridge model), with the options of the component: the canonical text of each (ratios, choices, rows, section and input
    /// of the H bridge engine with and without bars).
    /// <para><b>P2R (T2/R5, 12/11):</b> on GPC data alone, against the frozen native capture (keys <c>definitions|&lt;check&gt;|&lt;beam&gt;|…</c>), whose
    /// values are those of the former FEM path (from the FEM properties of the beam, bit for bit equal to these when it was written). The key
    /// <c>|fem-constant</c> (the constant-section rule on the FEM material instances, FEM only) is retired by name
    /// (<see cref="NativeParityCapture.Retired"/>, user decision U3 of 12/11).</para>
    /// </summary>
    internal static void RequireSuperElementCheck(Grasshopper.Kernel.GH_Component check, string label)
    {
        void Fail(string what) => throw new Exception("GPC COMPOSITE BEAM FAIL (" + label + " " + check.NickName + "): " + what);
        var goo = check.Params.Input[0].VolatileData.AllData(true).Single();
        var bridge = (GpcBridge)goo.GetType().GetProperty("GpcBridge").GetValue(goo) ?? throw new Exception("GPC COMPOSITE BEAM FAIL (" + label + "): no GPC bridge model");
        bool Flag(int input) => check.Params.Input[input].VolatileData.AllData(true).OfType<Grasshopper.Kernel.Types.GH_Boolean>().Single().Value;
        var options = new[] { new BridgeAnalysisOptions { Standard = BridgeStandard.Eurocode4, LimitState = BridgeLimitState.Rare, WebBuckling = Flag(2), TopFlangeBuckling = Flag(3), BottomFlangeBuckling = Flag(4) } };
        int beams = 0, stations = 0, constant = 0, shared = 0;
        foreach (var g in check.Params.Input[1].VolatileData.AllData(true))
        {
            string id = GpcSecForcesCheck.BeamId(g);
            var start = (BridgeCompositeSectionDefinition)g.GetType().GetProperty("StartDefinition").GetValue(g);
            var end = (BridgeCompositeSectionDefinition)g.GetType().GetProperty("EndDefinition").GetValue(g);
            if (start == null || end == null) Fail($"beam {id} without its definitions");
            var samples = bridge.SamplesOf(id) ?? throw new Exception($"GPC COMPOSITE BEAM FAIL ({label}): beam {id} not in the GPC bridge model");
            // The length as Super Element Check takes it: the distance of Rhino between the ends of the beam of the GPC bridge model (the FEM length before).
            var b = bridge.Beam(id);
            double length = new Rhino.Geometry.Point3d(b.StartPoint.X, b.StartPoint.Y, b.StartPoint.Z).DistanceTo(new Rhino.Geometry.Point3d(b.EndPoint.X, b.EndPoint.Y, b.EndPoint.Z));
            var at = samples.Select(s => Math.Clamp(s.Sample.Station / length, 0, 1)).ToHashSet().OrderBy(v => v).ToArray();
            if (GpcCompositeSections.IsConstant(start, end)) constant++;
            if (ReferenceEquals(start, end)) shared++;
            var now = BeamDefinitions(start, end, at);
            string beamKey = $"definitions|{check.NickName}|{id}";
            NativeParityCapture.Datum(label, beamKey + "|stations", null, string.Join(",", at.Select(NativeParityCapture.Bits)));
            for (int k = 0; k < at.Length; k++)
                NativeParityCapture.Datum(label, $"{beamKey}|{k}", null, Canonical(now[k], ((SectionH)now[k].Section.SteelSections[0].Section.SectionShape).Name, options));
            beams++; stations += at.Length;
        }
        if (beams == 0) Fail("no composite beams");
        Console.WriteLine($"GPC COMPOSITE BEAM PASS ({label} {check.NickName}): {beams} composite beams, {stations} stations; constant {constant} by value, {shared} with one definition at both ends; " +
            "the definitions of the beams at the stations of the check recorded against the native capture.");
    }
}
