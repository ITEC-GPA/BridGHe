#if GPC_PILOT
using System;
using System.Collections.Generic;
using System.Linq;
using BridGHe.Common.Geometry;
using BridGHe.Common.Gpc;
using BridGHe.Common.Gpc.Builders;
using BridGHe.Common.Gpc.Legacy;
using GPC.Checkers.CompositeBridge.Layout;

/// <summary>
/// R6.6 lane R2a (bridge generators): the GPC side of the generator tests, without any FEM model. The beams of Bridge Member Assignment are
/// the drafts of its plans (<see cref="GpcBridgeGeometryBuilders.Assigned"/>, the builder of the component on the path of P2); the nodes and
/// beams are read on their GPC data (bridge member of the draft, section axes, restraint records); a model is Build Model on GPC
/// (<see cref="LegacyBuildModel.Build"/>, N-mm, tolerance 0.001 as the plug-in without a Rhino document). Identities derived from a tag: a
/// rebuild gives the same GUIDs.
/// </summary>
internal static class R2aGeneratorFixtures
{
    internal static Guid Id(string tag) => R2aDesignFixtures.Id("generators:" + tag);

    /// <summary>Bridge Member Assignment on GPC data alone: default properties by family (-1: steel families) and rules given by their GPC
    /// property (no FEM defaults): the result has the plans only.</summary>
    internal static BridgeAssignment Assign(SimpleBridgeGeometry geometry, IReadOnlyDictionary<int, LegacyBeamProperty> defaults, IReadOnlyList<BridgeSectionRule> rules = null) =>
        BridgeMemberAssignment.Assign(geometry, null, rules ?? Array.Empty<BridgeSectionRule>(), defaults);

    /// <summary>The beam drafts of plans, in order, as the component builds them (identity from the tag and the position).</summary>
    internal static LegacyBeamDraft[] Drafts(IEnumerable<BridgeMemberPlan> plans, string tag) =>
        plans.Select((p, i) => GpcBridgeGeometryBuilders.Assigned(p, Id(tag + ":" + i))).ToArray();

    internal static BridgeMemberMetadata Member(LegacyBeamDraft d) => d.Bridge?.Member;
    internal static double Dot(GPC.Geometry.Vector3d a, GPC.Geometry.Vector3d b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;

    /// <summary>The section y axis of the beam (its section axes, as the FEM local axes without the principal rotation).</summary>
    internal static GPC.Geometry.Vector3d Up(LegacyBeamDraft d) => d.Beam.Assignments.SectionAxes.V2;

    internal static GPC.Model.ElementProperties.ElasticBeamProperty Elastic(LegacyBeamDraft d) => (GPC.Model.ElementProperties.ElasticBeamProperty)d.Beam.BeamProperty;

    /// <summary>The bottom flange thickness of the section recipe of the property of a beam draft.</summary>
    internal static double T1(LegacyBeamDraft d) => d.Property.Section.T1;

    internal static double Distance(GPC.Geometry.Point3d a, GPC.Geometry.Point3d b) => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y) + (a.Z - b.Z) * (a.Z - b.Z));
    internal static bool SamePoint(GPC.Geometry.Point3d a, Rhino.Geometry.Point3d b) => a.X == b.X && a.Y == b.Y && a.Z == b.Z;

    // Archives of the drafts (the two chunks of the GH archive of a draft).
    internal static LegacyBeamDraft Clone(LegacyBeamDraft d) { var (data, records) = GpcDraftArchive.Write(d); return GpcDraftArchive.ReadBeam(data, records); }
    internal static LegacyNodeDraft Clone(LegacyNodeDraft d) { var (data, records) = GpcDraftArchive.Write(d); return GpcDraftArchive.ReadNode(data, records); }
    internal static LegacyPlateDraft Clone(LegacyPlateDraft d) { var (data, records) = GpcDraftArchive.Write(d); return GpcDraftArchive.ReadPlate(data, records); }

    internal static bool Same(BridgeMemberMetadata a, BridgeMemberMetadata b) => a != null && b != null && a.PhysicalId == b.PhysicalId && a.Family == b.Family && a.Group == b.Group
        && a.Segment == b.Segment && a.Span == b.Span && a.StationFromMm == b.StationFromMm && a.StationToMm == b.StationToMm && a.GeometryId == b.GeometryId
        && a.SectionUp.X == b.SectionUp.X && a.SectionUp.Y == b.SectionUp.Y && a.SectionUp.Z == b.SectionUp.Z;

    // Node restraint records (the former attributes by their record names).
    internal static bool[] Restraint(LegacyNodeDraft d) => d.Node.Assignments.Restrains.Single(r => r.SourceRecord == "NodeRestraintAttributeModel").Restrain.Restrains
        .OrderBy(x => (int)x.Dof).Select(x => x.IsRestrained).ToArray();
    internal static double[] Stiffness(LegacyNodeDraft d) => d.Node.Assignments.Restrains.Single(r => r.SourceRecord == "NodeStiffnessAttributeModel").Restrain.Restrains
        .OrderBy(x => (int)x.Dof).Select(x => x.HasStiffness ? x.Stiffness : 0).ToArray();
    internal static GPC.Geometry.CoordinateSystem Frame(LegacyNodeDraft d) => d.Node.Assignments.Restrains.Single(r => r.SourceRecord == "NodeCoordinateAttributeModel").Restrain.CoordinateSystem;
    internal static GPC.Geometry.Vector3d Cross(GPC.Geometry.Vector3d a, GPC.Geometry.Vector3d b) => new(a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X);

    /// <summary>Build Model on GPC of nodes and beams (N-mm, no plates, combinations or stages).</summary>
    internal static GpcModels.Conversion Build(string name, IEnumerable<LegacyNodeDraft> nodes, IEnumerable<LegacyBeamDraft> beams, double tolerance = 0.001) =>
        LegacyBuildModel.Build(name, new BridgeUnits(DefinitionUnits.Nmm), tolerance, (nodes ?? Array.Empty<LegacyNodeDraft>()).Select(d => (d.Node.Guid, d)).ToList(),
            (beams ?? Array.Empty<LegacyBeamDraft>()).Select(d => (d.Beam.Guid, d)).ToList(), new List<(Guid, LegacyPlateDraft)>(), new List<LegacyCombinationDraft>(), new List<LegacyStageDraft>());

    /// <summary>An analysed GPC beam and its model (one case «Q», forces at both ends): the analysed element a GH beam links to (its results).</summary>
    internal static (GPC.Model.Elements.BeamElement Beam, GPC.Model.Models.Model Model) Analysed(string name)
    {
        var test = new GpcTestModel(name); test.Case("Q");
        var beam = test.Beam("B1", test.Node("1", 0, 0, 0), test.Node("2", 1000, 0, 0));
        foreach (double s in new[] { 0d, 1000 }) test.Forces(beam, "Q", s, 123);
        return (beam, test.Analyse());
    }

    /// <summary>P2F (T09 of task 39): with <paramref name="code"/>, only a rejection whose message carries it counts (another error of the
    /// block, a section or unit refusal, no longer passes the case); the message of every rejection is printed.</summary>
    internal static void Reject(Action action, string text, string code = null)
    {
        try { action(); }
        catch (Exception x) when (x is ArgumentException || x is NotSupportedException)
        {
            Console.WriteLine("R2A GENERATORS rejection (" + text + "): " + x.GetType().Name + ": " + x.Message);
            if (code != null && !x.Message.Contains(code, StringComparison.Ordinal)) throw new InvalidOperationException("R2A GENERATORS: " + text + " rejected for another reason: " + x.Message + " (expected " + code + ")");
            return;
        }
        throw new InvalidOperationException("R2A GENERATORS: expected rejection: " + text);
    }
}
#endif
