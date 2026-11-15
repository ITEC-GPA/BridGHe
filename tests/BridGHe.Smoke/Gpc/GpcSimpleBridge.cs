using System;
using System.Linq;
using BridGHe.Common.Geometry;
using BridGHe.Common.Gpc;

/// <summary>
/// P2T (F6, Smoke tests on GPC): the bridge model of the simple-bridge tests (<see cref="BridgeConstructionTests.Model"/>) built directly in
/// GPC from the bridge geometry, as the GPC input of the model transformers (<see cref="GpcModels.Conversion.FromGpc"/>): the nodes of the
/// geometry (BridGHe IDs 1..n, translations restrained at the abutment X = 0), one beam per geometry member in member order (BridGHe ID =
/// its number, as Bridge Member Assignment numbers them without a section schedule; no deck transverse member, which the default property of
/// these tests does not cover), its group "Family\GroupN\SegmentN", its bridge member
/// (physical member with the metadata of the geometry member) and the generic property of these tests; case G1.
/// <para>Not reproduced (no consumer of these tests reads them): the super elements of the girders and the BridGHe beam angle of Bridge Member
/// Assignment (all beams at 0°).</para>
/// </summary>
internal static class GpcSimpleBridge
{
    internal static GpcModels.Conversion Input(SimpleBridgeGeometry geometry, string name = "Bridge design test")
    {
        var test = new GpcTestModel(name); test.Case("G1");
        var nodes = geometry.Nodes.Select((p, i) => test.Node((i + 1).ToString(), p.X, p.Y, p.Z)).ToArray();
        foreach (var node in nodes.Where((_, i) => Math.Abs(geometry.Nodes[i].X) < 1e-8)) test.Restrain(node, true, true, true, false, false, false);
        int k = 0;
        // Bridge Member Assignment with one default property (key −1) leaves the deck transverse members without property (no beam).
        foreach (var member in geometry.Members.Where(m => m.Family != BridgeMemberFamily.DeckTransverse))
        {
            var beam = test.Beam((++k).ToString(), nodes[member.NodeFrom - 1], nodes[member.NodeTo - 1]);
            test.Group(beam, $"{member.Family}\\Group{member.Group}\\Segment{member.Segment}");
            test.Member(beam, Enum.Parse<GPC.Checkers.CompositeBridge.Layout.BridgeMemberFamily>(member.Family.ToString()), member.Group, member.Segment, member.Span, member.StationFrom, member.StationTo);
        }
        var model = test.Structure();
        return GpcModels.Conversion.FromGpc(model, new BridgeUnits(DefinitionUnits.Nmm), test.BeamAngles, 1e-6, bridge: test.Bridge);
    }
}
