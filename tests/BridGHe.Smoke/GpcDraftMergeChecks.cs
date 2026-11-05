#if GPC_PILOT
using System;
using System.Collections.Generic;
using System.Linq;
using BridGHe.Common.Gpc;
using BridGHe.Common.Gpc.Legacy;
using BridGHe.Common.Models;
using GPC.Model.Elements;
using R = Rhino.Geometry;

/// <summary>
/// Chain E (e3, P2K): regression <c>gpc-draft-merge</c> (not native). The merge by GH GUID of two element drafts
/// (<see cref="LegacyBuildModel.Merge(LegacyBeamDraft, LegacyBeamDraft)"/> and the node and plate overloads), resolved
/// (<see cref="LegacyAssembly.ResolveNode"/>, <see cref="LegacyAssembly.ResolveBeam"/>, <see cref="LegacyAssembly.ResolvePlate"/>), against the
/// draft of the former merge (<c>new XModel(first).Merge(other)</c>, the 0.3.0 <c>ModelHelper.BuildModel</c>) resolved the same way: the
/// element (loads with their records, attributes with their ids, assignments, groups), the list-level records, the bridge data and the
/// warnings, bit for bit, or the same error. Cases: the same datum merged with itself, two branches of one datum (each with its own loads
/// and attributes), loads in the stage-less "Dead" case, two loads equal within the other datum, Element IDs, super elements, bridge
/// members and section states (always appended), releases, supports, taper, string group, output stations, restraints, springs,
/// coordinate systems, rigid connections, temperatures, plate pressures, prestress, supports and load patch.
/// </summary>
internal static class GpcDraftMergeChecks
{
    private static string Fp(params object[] values) => GPC.Model.Core.ModelValues.Fingerprint(values);

    private static string Text(Element e, LegacyElementRecords records, GPC.Checkers.CompositeBridge.BridgeBeamData bridge, LegacyImportReport report)
    {
        var parts = new List<string> { "name " + e.Name, "groups " + string.Join(",", e.Groups.Values.Select(g => g.Name)), "loads " + Fp(e.Loads.Values.ToArray()), "attributes " + Fp(e.Attributes.Values.ToArray()),
            "attribute ids " + string.Join(",", e.Attributes.Keys), "attributes max " + e.Attributes.MaxId };
        if (e is NodeElement n) parts.AddRange(new[] { "position " + Fp(n.Position), "restraints " + Fp(n.Assignments.Restrains.Select(r => new object[] { r.SourceRecord, r.Restrain?.CoordinateSystem, r.Restrain?.Restrains }).ToArray()),
            "physical " + Fp(n.Assignments.Physical) });
        if (e is BeamElement b) parts.AddRange(new[] { "ends " + Fp(b.StartPoint, b.EndPoint), "beam loads " + Fp(b.Assignments.Loads.ToArray()),
            "beam load records " + string.Join(",", b.Assignments.Loads.Select(l => l.OriginalAssignmentId + "~" + l.SourceRecord)), "physical " + Fp(b.Assignments.Physical),
            "axes " + Fp(b.Assignments.SectionAxes, b.Assignments.OffsetAxes, b.Assignments.OffsetI, b.Assignments.OffsetJ), "property " + Fp(b.BeamProperty) });
        if (e is AreaElement p) parts.AddRange(new[] { "physical " + Fp(p.Assignments.Physical), "offset " + p.Assignments.Offset, "property " + Fp(p.PlateProperty) });
        if (records != null) parts.AddRange(new[] { "record groups " + string.Join(",", records.Groups), "record labels " + string.Join(",", records.Labels), "record super elements " + string.Join(",", records.SuperElements),
            $"record counts {records.Members} {records.SectionStates} {records.Tapers}", "record loads " + string.Join(",", records.LoadRecords.Select(l => l.Kind + ":" + l.Case)) });
        if (bridge != null) parts.Add("bridge " + (bridge.Member == null ? "-" : Fp(bridge.Member)) + " " + (bridge.SuperElement ?? "-") + " " + (bridge.SectionState == null ? "-" : bridge.SectionState.Behavior + " " + bridge.SectionState.PropertyFingerprint));
        parts.Add("warnings " + string.Join(" | ", report.Warnings)); parts.Add("element warnings " + string.Join(" | ", report.ElementWarnings.Select(w => w.Family + ":" + w.Number + ":" + w.Format)));
        parts.Add("carried " + string.Join(" | ", report.NotExported.Select(x => x.Key + "=" + x.Value)));
        return string.Join("\n", parts);
    }

    private static string Resolved(Func<(Element Element, LegacyElementRecords Records, GPC.Checkers.CompositeBridge.BridgeBeamData Bridge, LegacyImportReport Report)> draft)
    {
        try
        {
            var d = draft(); var report = new LegacyImportReport();
            foreach (var x in d.Report.NotExported) report.NotExported[x.Key] = x.Value;
            if (d.Element is NodeElement n) LegacyAssembly.ResolveNode(n, report);
            else if (d.Element is BeamElement b) LegacyAssembly.ResolveBeam(b, d.Records, report);
            else LegacyAssembly.ResolvePlate((AreaElement)d.Element, d.Records, report);
            return Text(d.Element, d.Element is NodeElement ? null : d.Records, d.Bridge, report);
        }
        catch (Exception e) when (e is ArgumentException || e is NotSupportedException || e is InvalidOperationException) { return "ERROR " + e.Message; }
    }

    internal static void Regression()
    {
        int compared = 0, errors = 0;
        void Compare(string label, ElementModel first, ElementModel other)
        {
            string expected, actual;
            if (first is NodeModel n)
            {
                var guid = GpcBeamDrafts.FromFemm(n).Node.Guid; var merged = new NodeModel(n); merged.Merge(other);
                expected = Resolved(() => { var d = GpcBeamDrafts.FromFemm(merged, guid); return (d.Node, null, null, d.Report); });
                actual = Resolved(() => { var d = LegacyBuildModel.Merge(GpcBeamDrafts.FromFemm(n), GpcBeamDrafts.FromFemm((NodeModel)other)); return (d.Node, d.Records, null, d.Report); });
            }
            else if (first is BeamModel b)
            {
                var guid = GpcBeamDrafts.FromFemm(b).Beam.Guid; var merged = new BeamModel(b); merged.Merge(other);
                expected = Resolved(() => { var d = GpcBeamDrafts.FromFemm(merged, guid); return (d.Beam, d.Records, d.Bridge, d.Report); });
                actual = Resolved(() => { var d = LegacyBuildModel.Merge(GpcBeamDrafts.FromFemm(b), GpcBeamDrafts.FromFemm((BeamModel)other)); return (d.Beam, d.Records, d.Bridge, d.Report); });
            }
            else
            {
                var p = (PlateModel)first; var guid = GpcBeamDrafts.FromFemm(p).Plate.Guid; var merged = new PlateModel(p); merged.Merge(other);
                expected = Resolved(() => { var d = GpcBeamDrafts.FromFemm(merged, guid); return (d.Plate, d.Records, null, d.Report); });
                actual = Resolved(() => { var d = LegacyBuildModel.Merge(GpcBeamDrafts.FromFemm(p), GpcBeamDrafts.FromFemm((PlateModel)other)); return (d.Plate, d.Records, null, d.Report); });
            }
            if (expected != actual)
            {
                var x = expected.Split('\n'); var y = actual.Split('\n');
                int line = Enumerable.Range(0, Math.Min(x.Length, y.Length)).FirstOrDefault(i => x[i] != y[i], -1);
                throw new Exception($"GPC DRAFT MERGE FAIL ({label}): " + (line < 0 ? $"{x.Length} / {y.Length} lines" : $"former '{x[line]}' / GPC '{y[line]}'"));
            }
            compared++; if (expected.StartsWith("ERROR ")) errors++;
        }
        var q = new LoadCaseModel("2", "Q"); var t = new LoadCaseModel("3", "T"); var dead = new LoadCaseModel(LoadCaseModel.Dead);
        // Nodes.
        var node = new NodeModel("7", new R.Point3d(1, 2, 3));
        node.Attributes.Add(new NodeRestraintAttributeModel(true, true, true, false, false, false)); node.Attributes.Add(new ElementIdAttributeModel("N1"));
        node.Loads.Add(new NodeForceLoadModel(q, new R.Vector3d(0, 0, -10))); node.Loads.Add(new NodeForceLoadModel(dead, new R.Vector3d(1, 0, 0)));
        node.Groups.Add(new GroupModel("Bearings"));
        Compare("node with itself", node, node);
        NodeModel Branch(NodeModel from, Action<NodeModel> change) { var c = new NodeModel(from); change(c); return c; }
        var nodeA = Branch(node, c => { c.Loads.Add(new NodeMomentLoadModel(q, new R.Vector3d(0, 5, 0))); c.Attributes.Add(new RigidConnectionAttributeModel(new R.Point3d(1, 2, 4))); c.Groups.Add(new GroupModel("A")); });
        var nodeB = Branch(node, c =>
        {
            c.Loads.Add(new NodeForceLoadModel(q, new R.Vector3d(0, 0, -3))); c.Loads.Add(new NodeForceLoadModel(q, new R.Vector3d(0, 0, -3)));
            c.Loads.Add(new NodeTemperatureLoadModel(t, 15, NodeTemperatureLoadModel.LoadType.Fixed)); c.Attributes.Add(new NodeStiffnessAttributeModel(1, 2, 3, 0, 0, 0));
            c.Attributes.Add(new NodeCoordinateAttributeModel(new R.Plane(new R.Point3d(1, 2, 3), new R.Vector3d(1, 1, 0), new R.Vector3d(-1, 1, 0))));
            c.Attributes.Add(new ElementIdAttributeModel("N2")); c.Attributes.Add(new RigidConnectionAttributeModel(new R.Point3d(1, 2, 5))); c.Groups.Add(new GroupModel("B")); c.Groups.Add(new GroupModel("A"));
        });
        Compare("node branches", nodeA, nodeB); Compare("node branches reversed", nodeB, nodeA); Compare("node branch with its source", nodeA, node);
        // Beams.
        var steel = new MaterialModel("S355") { Name = "S355", Modulus = 210000, PoissonRatio = 0.3, UsePoisson = true, Density = 7.85e-9 };
        var property = new BeamPropertyModel("PI") { Name = "PI", Material = steel, Section = new SectionModel { Name = "I", SectionType = SectionModel.SectionTypes.I, D = 500, B1 = 300, B2 = 100, T1 = 20, T2 = 10, T3 = 5 }, PropertyType = BeamPropertyType.Beam };
        var beam = new BeamModel(new R.Point3d(0, 0, 0), new R.Point3d(10000, 0, 0), property, new GroupModel("Deck"), 10) { BeamId = "4" };
        beam.Attributes.Add(new ElementIdAttributeModel("B1")); beam.Attributes.Add(new SuperElementAttributeModel("SE"));
        beam.Loads.Add(new BeamDistributedLoadModel(q, new() { LoadSchema = BeamDistributedLoadModel.LoadSchema.Uniform, PA = -2, LoadDirection = BeamDistributedLoadModel.LoadDirection.Y }, BeamDistributedLoadModel.LoadType.Force));
        beam.Loads.Add(new BeamPointLoadModel(dead, new() { P = new R.Vector3d(0, -5, 0), A = 0.25 }, BeamPointLoadModel.LoadType.Force));
        Compare("beam with itself", beam, beam);
        BeamModel BeamBranch(Action<BeamModel> change) { var c = new BeamModel(beam); change(c); return c; }
        var releases = new BeamReleasesAttributeModel.AttributeData { TransNode1 = new[] { false, false, false }, TransNode2 = new[] { false, false, false }, RotNode1 = new[] { false, false, true }, RotNode2 = new[] { false, false, true },
            TransN1Partial = R.Vector3d.Zero, TransN2Partial = R.Vector3d.Zero, RotN1Partial = R.Vector3d.Zero, RotN2Partial = R.Vector3d.Zero };
        var beamA = BeamBranch(c =>
        {
            c.Loads.Add(new BeamPreLoadModel(t, -1e-4, BeamPreLoadModel.LoadType.Strain)); c.Attributes.Add(new BeamReleasesAttributeModel(releases));
            c.Attributes.Add(new BeamOutputStationsModel(new BeamOutputStationsModel.AttributeData { MinimumNumberOfStations = 9 }));
            c.Attributes.Add(new BeamTaperAttributeModel(new BeamTaperAttributeModel.AttributeData { NFromX = 2, NToX = 1 })); c.Groups.Add(new GroupModel("A"));
        });
        var beamB = BeamBranch(c =>
        {
            c.Loads.Add(new BeamGradientLoadModel(t, new() { Axis2 = 0.5 })); c.Loads.Add(new BeamDistributedLoadModel(q, new() { LoadSchema = BeamDistributedLoadModel.LoadSchema.Uniform, PA = -1, LoadDirection = BeamDistributedLoadModel.LoadDirection.Z }, BeamDistributedLoadModel.LoadType.Force));
            c.Attributes.Add(new BeamReleasesAttributeModel(releases)); c.Attributes.Add(new ElementIdAttributeModel("B2"));
            c.Attributes.Add(new BeamOutputStationsModel(new BeamOutputStationsModel.AttributeData { MinimumNumberOfStations = 13 }));
            c.Attributes.Add(new BeamTaperAttributeModel(new BeamTaperAttributeModel.AttributeData { NFromX = 2, NToX = 1 }));
            c.Attributes.Add(new BeamStringGroupAttributeModel(new BeamStringGroupAttributeModel.AttributeData { ID = 3 }));
            c.Attributes.Add(new BeamSupportAttributeModel(new BeamSupportAttributeModel.AttributeData { SpringType = BeamSupportAttributeModel.SpringType.Simple, Stiffness = 3, SimpleSpringType = BeamSupportAttributeModel.SimpleSpringTypes.TensionCompression,
                LocalAxisDirection = BeamSupportAttributeModel.LocalAxisDirection.positive2Axis, SpringTensionOrientation = BeamSupportAttributeModel.SpringTensionOrientation.BeamLocalAxis }));
            c.Groups.Add(new GroupModel("B"));
        });
        Compare("beam branches", beamA, beamB); Compare("beam branches reversed", beamB, beamA); Compare("beam branch with its source", beamA, beam);
        var up = new R.Vector3d(0, 0, 1);
        var member = BeamBranch(c => c.Attributes.Add(new BridgeMemberAttributeModel(new BridgeMemberMetadata("Girder:1:1", BridGHe.Common.Geometry.BridgeMemberFamily.Girder, 1, 1, 1, 0, 10000, up, "G"))));
        Compare("beam with a bridge member, with itself", member, member);
        var state = BeamBranch(c => c.Attributes.Add(new BridgeSectionStateAttributeModel(new BridgeSectionAssignment(BridGHe.Common.Checks.BridgeActionBehavior.LongTerm, false, 18, "Q", BridgeSectionAssignment.Signature(property)))));
        Compare("beam with a section state, with itself", state, state);
        Compare("beam in two super elements", beam, BeamBranch(c => { c.Attributes.Clear(); c.Attributes.Add(new SuperElementAttributeModel("SE-2")); }));
        Compare("beam with two release sets", beamA, BeamBranch(c => c.Attributes.Add(new BeamReleasesAttributeModel(new BeamReleasesAttributeModel.AttributeData { TransNode1 = new[] { true, false, false }, TransNode2 = new[] { false, false, false },
            RotNode1 = new[] { false, false, false }, RotNode2 = new[] { false, false, false }, TransN1Partial = R.Vector3d.Zero, TransN2Partial = R.Vector3d.Zero, RotN1Partial = R.Vector3d.Zero, RotN2Partial = R.Vector3d.Zero }))));
        Compare("beam with two tapers", beamA, BeamBranch(c => c.Attributes.Add(new BeamTaperAttributeModel(new BeamTaperAttributeModel.AttributeData { NFromY = 3 }))));
        Compare("beam with two prestrains in a case", beamA, BeamBranch(c => c.Loads.Add(new BeamPreLoadModel(t, -2e-4, BeamPreLoadModel.LoadType.Strain))));
        // Plates.
        var shell = new PlatePropertyModel("1") { Name = "Shell", Material = new MaterialModel("C") { Name = "C30", Modulus = 33000, PoissonRatio = 0.2 }, PropertyType = PlatePropertyType.ShellThin, MembraneThickness = 250, BendingThickness = 250 };
        var plate = new PlateModel("5", new[] { new R.Point3d(0, 0, 0), new R.Point3d(1000, 0, 0), new R.Point3d(1000, 1000, 0) }, shell, new[] { new GroupModel("Slab") });
        plate.Loads.Add(new PlatePressureModel(q, new() { P = new R.Vector3d(0, 0, -5) })); plate.Loads.Add(new PlatePressureModel(dead, new() { P = new R.Vector3d(0, 0, -1) }));
        plate.Attributes.Add(new ElementIdAttributeModel("P1"));
        Compare("plate with itself", plate, plate);
        PlateModel PlateBranch(Action<PlateModel> change) { var c = new PlateModel(plate); change(c); return c; }
        var plateA = PlateBranch(c => { c.Attributes.Add(new PlateSupportAttributeModel(new PlateSupportAttributeModel.AttributeData { Stiffness = 2 })); c.Groups.Add(new GroupModel("A")); });
        var plateB = PlateBranch(c =>
        {
            c.Attributes.Add(new PlateSupportAttributeModel(new PlateSupportAttributeModel.AttributeData { Stiffness = 2 })); c.Attributes.Add(new ElementIdAttributeModel("P2"));
            c.Attributes.Add(new LoadPatchTypeAttributeModel(new LoadPatchTypeAttributeModel.AttributeData { PatchType = LoadPatchTypeAttributeModel.PatchType.Auto1, SelectedEdges = new[] { 1, 3, 0, 0 }, Weights = new[] { 1.0, 0, 0, 0 } }));
            c.Loads.Add(new PlatePressureModel(q, new() { P = new R.Vector3d(0, 0, -2) })); c.Loads.Add(new PlatePreStressModel(t, new() { LocalPressure = new R.Vector3d(1, 2, 0) }));
        });
        Compare("plate branches", plateA, plateB); Compare("plate branches reversed", plateB, plateA);
        Console.WriteLine($"GPC DRAFT MERGE PASS: {compared} merges of node, beam and plate drafts equal to the drafts of the former merge after the resolution ({errors} with the same error)");
    }
}
#endif
