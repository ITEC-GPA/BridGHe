using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using BridGHe.Common.Gpc;
using BridGHe.Common.Gpc.Legacy;
using BridGHe.Common.Models;
using GPC.Model.Models;

/// <summary>
/// Chain E (e2, P2J): the conversion of the legacy reader written to text, model by model, so that two builds can be compared file by file
/// (the conversion before and after the split of <see cref="LegacyModelImport.Import"/> into element drafts and the assembly): the canonical
/// XML document of the model (<see cref="GpcInputComparison.Document"/>, after the analysis fingerprint completes the lazy values), the
/// analysis fingerprint, and every field of the report (warnings in order, carried data, BridGHe angles, case origins, property data,
/// shadowed beams, element cases and warnings, native sections, bridge data). Corpus: the 77 captured FEM inputs of the former exporter
/// (<c>results/capture-1</c>, with their tolerance), the examples 01–10 captured with it (<c>results/examples-1</c>) and the examples 01–10
/// built in memory (<see cref="NativeExamples.Create"/>). Not native, writes only into the given folder.
/// Usage: <c>--import-dump &lt;output folder&gt;</c>.
/// <para>R6.6 lane R3 (11/11): every model is read as bytes (<see cref="R3LegacyCorpus"/>), with no FEM class: the captures and the captured
/// examples from their files, the node, beam and plate data and the refused duplicates from their frozen payloads (the bytes the FEM writer
/// gave for the former node, beam and plate data, same ids), the former section-state signatures from the fixture
/// (<see cref="R3LegacyCorpus.Signatures"/>). The FEM builders <see cref="BeamData"/> and <see cref="PlateData"/> are kept for their other
/// callers only. P2CA (13/11/2026, U3): the examples 01–10 built in memory (<c>memory-*</c>, the FEM catalogue written by the FEM writer, no
/// frozen payload) are retired by name (<see cref="P2rRetired.Checks"/>); the node builder went with the FEM writer of the fixtures.</para>
/// </summary>
internal static class ImportDump
{
    internal static int Run(string[] args)
    {
        string output = args.SkipWhile(a => a != "--import-dump").Skip(1).FirstOrDefault() ?? throw new ArgumentException("--import-dump <output folder>");
        string only = args.SkipWhile(a => a != "--import-dump").Skip(2).FirstOrDefault(a => !a.StartsWith("--"));
        Directory.CreateDirectory(output);
        int written = 0, failed = 0;
        void Dump(string id, byte[] archive, Func<int, string> signatures, double tolerance) { if (only == null || id.StartsWith(only, StringComparison.Ordinal)) DumpOne(id, archive, signatures, tolerance); }
        void DumpOne(string id, byte[] archive, Func<int, string> signatures, double tolerance)
        {
            var report = new LegacyImportReport();
            string text;
            try
            {
                var model = LegacyModelImport.Import(LegacyArchive.Read(archive), GpcIdentity.Derive(GpcIdentity.NameSpace, "import-dump:" + id), tolerance, report, signatures);
                string fingerprint = model.AnalysisFingerprint();
                text = "FINGERPRINT " + fingerprint + "\n" + Report(report) + "\nDOCUMENT\n" + (GpcInputComparison.Document(model) ?? "beyond the serializer limit");
            }
            catch (Exception e) when (e is ArgumentException || e is NotSupportedException || e is InvalidOperationException) { text = "ERROR " + e.GetType().Name + ": " + e.Message; failed++; }
            File.WriteAllText(Path.Combine(output, id + ".txt"), text);
            written++;
        }
        void Item(R3LegacyCorpus.Item item) => Dump(item.Id, item.Bytes, item.Signatures, item.Tolerance);
        foreach (var item in R3LegacyCorpus.Captures()) Item(item);
        foreach (var item in R3LegacyCorpus.Examples()) Item(item);
        // P2CA (13/11/2026, U3): the examples 01-10 built in memory by the FEM catalogue and written by the FEM writer (memory-*) are retired by
        // name (P2rRetired.Checks, scope import-dump); the same ten examples as 0.3.0 saved them stay in the corpus (examples-*).
        Console.WriteLine($"RETIRED BY NAME (P2CA): {P2rRetired.Checks.Count(c => c.Key.StartsWith("import-dump|", StringComparison.Ordinal))} conversions of FEM models written by the FEM writer (P2rRetired.Checks)");
        // e3 (P2J): node data the corpus above lacks (rigid links, a labelled node with a rigid link, two labels, restraints with springs
        // and a coordinate system, two stiffnesses, forces and moments in one case, temperature kinds), in every unit system; a labelled node
        // with a rigid link: the F6 reader refused it (Model.AddCostrain -> AddNode wants case names for node attributes).
        // e3 (P2K): beam and plate data at list level (two Element IDs before another attribute, groups, stations, releases, taper, string
        // group, supports, super elements, bridge members, section states, every beam load kind, plate supports, load patch, pressures,
        // prestress), in every unit system, and one model per refused duplicate. R3: their frozen payloads.
        foreach (var item in R3LegacyCorpus.Fixtures()) Item(item);
        Console.WriteLine($"IMPORT DUMP: {written} conversions written to {output} ({failed} with the same kind of error text)");
        return 0;
    }

    /// <summary>A FEM model with the beam data of the e3 corpus (see <see cref="Run"/>), coordinates in the given units; with
    /// <paramref name="duplicate"/>: the second beam also has a second record of that kind (a conversion error). Transitional (R3): the
    /// regressions of lane R3 read its frozen payloads (<c>beams-*</c>, <c>beams-duplicate-*</c>); kept for the other callers until P2.</summary>
    internal static ModelModel BeamData(ModelModel.ModelUnits units, string duplicate)
    {
        var steel = new MaterialModel("S355") { Name = "S355", Modulus = 210000, PoissonRatio = 0.3, UsePoisson = true, Density = 7.85e-9, ThermalExpansion = 1.2e-5 };
        var i = new SectionModel { Name = "I", SectionType = SectionModel.SectionTypes.I, D = 0.5, B1 = 0.3, B2 = 0.1, T1 = 0.02, T2 = 0.01, T3 = 0.005 };
        var property = new BeamPropertyModel("PI") { Name = "PI", Material = steel, Section = i, PropertyType = BeamPropertyType.Beam };
        var q = new LoadCaseModel("2", "Q"); var t = new LoadCaseModel("3", "T"); var dead = new LoadCaseModel(LoadCaseModel.Dead);
        var p = new[] { new Rhino.Geometry.Point3d(0, 0, 0), new Rhino.Geometry.Point3d(10, 0, 0), new Rhino.Geometry.Point3d(20, 5, 1), new Rhino.Geometry.Point3d(20, 5, 9) };
        var deck = new GroupModel("Deck"); var span = new GroupModel("Span") { Parent = deck }; var other = new GroupModel("Other");
        var beams = new[] { new BeamModel(p[0], p[1], property, deck, 0) { BeamId = "1", Offset = new Rhino.Geometry.Vector2d(0.05, -0.02) },
            new BeamModel(p[1], p[2], property, span, 15) { BeamId = "2" }, new BeamModel(p[2], p[3], property, deck, 30) { BeamId = "3" } };
        // Beam 1: two groups, two Element IDs before a release set, stations, taper, string group, supports, a super element twice, a bridge member.
        beams[0].Groups.Add(other);
        beams[0].Attributes.Add(new ElementIdAttributeModel("B-A")); beams[0].Attributes.Add(new ElementIdAttributeModel("B-B"));
        var releases = new BeamReleasesAttributeModel.AttributeData { TransNode1 = new[] { false, true, false }, TransNode2 = new[] { false, false, false }, RotNode1 = new[] { false, false, true },
            RotNode2 = new[] { true, false, false }, TransN1Partial = new Rhino.Geometry.Vector3d(0, 50, 0), TransN2Partial = Rhino.Geometry.Vector3d.Zero, RotN1Partial = Rhino.Geometry.Vector3d.Zero, RotN2Partial = new Rhino.Geometry.Vector3d(7, 0, 0) };
        beams[0].Attributes.Add(new BeamReleasesAttributeModel(releases));
        beams[0].Attributes.Add(new BeamOutputStationsModel(new BeamOutputStationsModel.AttributeData { MinimumNumberOfStations = 9 }));
        beams[0].Attributes.Add(new BeamOutputStationsModel(new BeamOutputStationsModel.AttributeData { MinimumNumberOfStations = 11 }));
        beams[0].Attributes.Add(new BeamTaperAttributeModel(new BeamTaperAttributeModel.AttributeData { NFromX = 2, NToX = 1.5 }));
        beams[0].Attributes.Add(new BeamStringGroupAttributeModel(new BeamStringGroupAttributeModel.AttributeData { ID = 7 }));
        beams[0].Attributes.Add(new BeamStringGroupAttributeModel(new BeamStringGroupAttributeModel.AttributeData { ID = 8 }));
        var support = new BeamSupportAttributeModel.AttributeData { SpringType = BeamSupportAttributeModel.SpringType.Simple, Stiffness = 3, Gap = 0.001, SimpleSpringType = BeamSupportAttributeModel.SimpleSpringTypes.CompressionOnly,
            LocalAxisDirection = BeamSupportAttributeModel.LocalAxisDirection.positive2Axis, SpringTensionOrientation = BeamSupportAttributeModel.SpringTensionOrientation.BeamLocalAxis };
        beams[0].Attributes.Add(new BeamSupportAttributeModel(support));
        beams[0].Attributes.Add(new BeamSupportAttributeModel(new BeamSupportAttributeModel.AttributeData { SpringType = BeamSupportAttributeModel.SpringType.Simple, Stiffness = 5, Gap = 0, SimpleSpringType = BeamSupportAttributeModel.SimpleSpringTypes.TensionCompression, LocalAxisDirection = BeamSupportAttributeModel.LocalAxisDirection.negative1Axis, SpringTensionOrientation = BeamSupportAttributeModel.SpringTensionOrientation.BeamLocalAxis }));
        beams[0].Attributes.Add(new SuperElementAttributeModel("SE-1")); beams[0].Attributes.Add(new SuperElementAttributeModel("SE-1"));
        var up = new Rhino.Geometry.Vector3d(0, 0, 1);
        beams[0].Attributes.Add(new BridgeMemberAttributeModel(new BridgeMemberMetadata("Girder:1:1", BridGHe.Common.Geometry.BridgeMemberFamily.Girder, 1, 1, 1, 0, 10, up, "G-1")));
        // Beam 2: two section states (the first current), a label after them, every load kind.
        beams[1].Attributes.Add(new BridgeSectionStateAttributeModel(new BridgeSectionAssignment(BridGHe.Common.Checks.BridgeActionBehavior.LongTerm, true, 18.5, "Q", BridgeSectionAssignment.Signature(property))));
        beams[1].Attributes.Add(new BridgeSectionStateAttributeModel(new BridgeSectionAssignment(BridGHe.Common.Checks.BridgeActionBehavior.ShortTerm, false, 6, "", "old")));
        beams[1].Attributes.Add(new ElementIdAttributeModel("B-C"));
        beams[1].Attributes.Add(new BridgeMemberAttributeModel(new BridgeMemberMetadata("Girder:1:1", BridGHe.Common.Geometry.BridgeMemberFamily.Girder, 1, 1, 1, 10, 21, up, "G-2")));
        beams[1].Attributes.Add(new SuperElementAttributeModel("SE-1"));
        beams[1].Loads.Add(new BeamDistributedLoadModel(q, new() { LoadSchema = BeamDistributedLoadModel.LoadSchema.Uniform, PA = -2, LoadDirection = BeamDistributedLoadModel.LoadDirection.Y }, BeamDistributedLoadModel.LoadType.Force));
        beams[1].Loads.Add(new BeamDistributedLoadModel(q, new() { LoadSchema = BeamDistributedLoadModel.LoadSchema.Keystone, PA = 1, PB = 3, A = 0.1, B = 0.2, LoadDirection = BeamDistributedLoadModel.LoadDirection.Gravity_Projected,
            CoordinateSystem = new CoordinateSystemModel("G") }, BeamDistributedLoadModel.LoadType.Force));
        beams[1].Loads.Add(new BeamDistributedLoadModel(t, new() { LoadSchema = BeamDistributedLoadModel.LoadSchema.Triangular, P1 = 1, PA = 4, P2 = 2, A = 0.3, LoadDirection = BeamDistributedLoadModel.LoadDirection.Z }, BeamDistributedLoadModel.LoadType.Moment));
        beams[1].Loads.Add(new BeamPreLoadModel(t, -2e-4, BeamPreLoadModel.LoadType.Strain));
        beams[1].Loads.Add(new BeamPointLoadModel(q, new() { P = new Rhino.Geometry.Vector3d(0, -5, 1), A = 0.4 }, BeamPointLoadModel.LoadType.Force));
        beams[1].Loads.Add(new BeamPointLoadModel(dead, new() { P = new Rhino.Geometry.Vector3d(2, 0, 0), A = 0.7 }, BeamPointLoadModel.LoadType.Moment));
        beams[1].Loads.Add(new BeamPreLoadModel(q, 12, BeamPreLoadModel.LoadType.Tension));
        beams[1].Loads.Add(new BeamGradientLoadModel(t, new() { Axis1 = 0.5, Axis2 = -0.25 }));
        beams[1].Loads.Add(new CompositeBeamSlabStrainModel(t, new()));
        beams[1].Loads.Add(new BeamDistributedLoadModel(dead, new() { LoadSchema = BeamDistributedLoadModel.LoadSchema.TriangularEnds_Keystone, P1 = 1, PA = 2, PB = 3, P2 = 4, A = 0.2, B = 0.3,
            LoadDirection = BeamDistributedLoadModel.LoadDirection.X, CoordinateSystem = new CoordinateSystemModel("G") }, BeamDistributedLoadModel.LoadType.Force));
        // Beam 3: a stale section state and a super element of its own.
        beams[2].Attributes.Add(new BridgeSectionStateAttributeModel(new BridgeSectionAssignment(BridGHe.Common.Checks.BridgeActionBehavior.Shrinkage, false, 9, "", "old")));
        beams[2].Attributes.Add(new SuperElementAttributeModel("SE-2"));
        switch (duplicate)
        {
            case "releases": beams[1].Attributes.Add(new BeamReleasesAttributeModel(releases)); beams[1].Attributes.Add(new BeamReleasesAttributeModel(new BeamReleasesAttributeModel.AttributeData { TransNode1 = new[] { true, false, false }, TransNode2 = new[] { false, false, false }, RotNode1 = new[] { false, false, false }, RotNode2 = new[] { false, true, false }, TransN1Partial = Rhino.Geometry.Vector3d.Zero, TransN2Partial = Rhino.Geometry.Vector3d.Zero, RotN1Partial = Rhino.Geometry.Vector3d.Zero, RotN2Partial = Rhino.Geometry.Vector3d.Zero })); break;
            case "taper": beams[1].Attributes.Add(new BeamTaperAttributeModel(new BeamTaperAttributeModel.AttributeData { NFromX = 2 })); beams[1].Attributes.Add(new BeamTaperAttributeModel(new BeamTaperAttributeModel.AttributeData { NFromY = 2 })); break;
            case "super-elements": beams[1].Attributes.Add(new SuperElementAttributeModel("SE-3")); break;
            case "members": beams[1].Attributes.Add(new BridgeMemberAttributeModel(new BridgeMemberMetadata("Girder:1:2", BridGHe.Common.Geometry.BridgeMemberFamily.Girder, 1, 2, 1, 0, 10, up, "G-3"))); break;
            case "prestrain": beams[1].Loads.Add(new BeamPreLoadModel(t, 3e-4, BeamPreLoadModel.LoadType.Strain)); break;
        }
        var nodes = p.Select((x, k) => new NodeModel((k + 1).ToString(), x)).ToArray();
        nodes[0].Attributes.Add(new NodeRestraintAttributeModel(true, true, true, true, true, true));
        var model = new ModelModel(units);
        model.AddElements(nodes.Cast<ElementModel>().Concat(beams));
        return model;
    }

    /// <summary>A FEM model with the plate data of the e3 corpus (see <see cref="Run"/>), coordinates in the given units.</summary>
    internal static ModelModel PlateData(ModelModel.ModelUnits units)
    {
        var material = new MaterialModel("C") { Name = "C30", Modulus = 33000, PoissonRatio = 0.2, Density = 2.5e-9 };
        var shell = new PlatePropertyModel("1") { Name = "Shell", Material = material, PropertyType = PlatePropertyType.ShellThin, MembraneThickness = 0.25, BendingThickness = 0.2 };
        var patch = new PlatePropertyModel("2") { Name = "Patch", PropertyType = PlatePropertyType.LoadPatch };
        var q = new LoadCaseModel("2", "Q"); var dead = new LoadCaseModel(LoadCaseModel.Dead);
        var p = new[] { new Rhino.Geometry.Point3d(0, 0, 0), new Rhino.Geometry.Point3d(4, 0, 0), new Rhino.Geometry.Point3d(4, 3, 0), new Rhino.Geometry.Point3d(0, 3, 0), new Rhino.Geometry.Point3d(8, 0, 0), new Rhino.Geometry.Point3d(8, 3, 0) };
        var slab = new GroupModel("Slab"); var bay = new GroupModel("Bay") { Parent = slab };
        var plates = new[] { new PlateModel("1", new[] { p[0], p[1], p[2], p[3] }, shell, new[] { slab, bay }), new PlateModel("2", new[] { p[1], p[4], p[5], p[2] }, patch, new[] { bay }), new PlateModel("3", new[] { p[0], p[1], p[3] }, shell, null) };
        plates[0].Attributes.Add(new ElementIdAttributeModel("P-A")); plates[0].Attributes.Add(new ElementIdAttributeModel("P-B"));
        plates[0].Attributes.Add(new PlateSupportAttributeModel(new PlateSupportAttributeModel.AttributeData { Stiffness = 2, CompressionOnly = true, Face = PlateSupportAttributeModel.SupportedFace.Top }));
        plates[0].Attributes.Add(new PlateSupportAttributeModel(new PlateSupportAttributeModel.AttributeData { Stiffness = 4, CompressionOnly = false, Face = PlateSupportAttributeModel.SupportedFace.Bottom }));
        plates[0].Loads.Add(new PlatePressureModel(q, new() { P = new Rhino.Geometry.Vector3d(0, 0, -5) }));
        plates[0].Loads.Add(new PlatePressureModel(dead, new() { P = new Rhino.Geometry.Vector3d(1, 0, -2), CoordinateSystem = new CoordinateSystemModel("G"), Face = PlatePressureModel.LoadFace.Top, Projected = true }));
        plates[1].Attributes.Add(new LoadPatchTypeAttributeModel(new LoadPatchTypeAttributeModel.AttributeData { PatchType = LoadPatchTypeAttributeModel.PatchType.Auto2, SelectedEdges = new[] { 1, 3 }, Weights = new[] { 0.5, 0.5, 0, 0 } }));
        plates[1].Attributes.Add(new ElementIdAttributeModel("P-C"));
        plates[1].Loads.Add(new PlatePressureModel(q, new() { P = new Rhino.Geometry.Vector3d(0, 0, -1) }));
        plates[2].Offset = 0.1;
        var nodes = p.Select((x, k) => new NodeModel((k + 1).ToString(), x)).ToArray();
        nodes[0].Attributes.Add(new NodeRestraintAttributeModel(true, true, true, true, true, true));
        var model = new ModelModel(units);
        model.AddElements(nodes.Cast<ElementModel>().Concat(plates));
        return model;
    }

    private static string B(double v) => BitConverter.DoubleToInt64Bits(v).ToString("X16");

    internal static string Report(LegacyImportReport r)
    {
        var s = new StringBuilder();
        s.Append("UNITS ").Append(r.Units.Units.ToString()).Append('\n');
        s.Append("OWNER ").Append(r.Owner).Append('\n');
        s.Append("REGISTERED ").Append(r.RegisteredCases).Append('\n');
        foreach (var w in r.Warnings) s.Append("WARNING ").Append(w).Append('\n');
        foreach (var p in r.NotExported) s.Append("CARRY ").Append(p.Key).Append(" = ").Append(p.Value).Append('\n');
        foreach (var p in r.NativeSections) s.Append("NATIVE ").Append(p.Key).Append(' ').Append(p.Value?.GetType().Name ?? "null").Append('\n');
        foreach (var p in r.BeamAngles) s.Append("ANGLE ").Append(p.Key).Append(' ').Append(B(p.Value)).Append('\n');
        foreach (var p in r.CaseOrigins) s.Append("ORIGIN ").Append(p.Key).Append(' ').Append(p.Value).Append('\n');
        foreach (var p in r.BeamProperties) s.Append("PROPERTY ").Append(p.Key).Append(' ').Append(p.Value.Formulation).Append(' ').Append(B(p.Value.Angle)).Append(' ')
            .Append(p.Value.Native == null ? "-" : "native").Append(' ').Append(p.Value.Signature).Append('\n');
        foreach (var k in r.ShadowedPropertyBeams.OrderBy(k => k)) s.Append("SHADOWED ").Append(k).Append('\n');
        foreach (var c in r.ElementLoadCases) s.Append("CASES ").Append(c.Family).Append(':').Append(c.Number).Append(' ').Append(string.Join(",", c.Cases)).Append('\n');
        foreach (var w in r.ElementWarnings) s.Append("ELEMENT WARNING ").Append(w.Family).Append(':').Append(w.Number).Append(' ').Append(w.Restraint).Append(' ').Append(w.Format).Append('\n');
        // P2K: the archive of the bridge data differs between two runs, so every record is written instead of its digest.
        s.Append("BRIDGE ").Append(r.Bridge.Beams.Count()).Append('\n');
        foreach (var b in r.Bridge.Beams) s.Append("BRIDGE BEAM ").Append(b.BeamGuid).Append(" member ").Append(b.Member == null ? "-" : GPC.Model.Core.ModelValues.Fingerprint(new object[] { b.Member })).Append(" super ").Append(b.SuperElement ?? "-")
            .Append(" state ").Append(b.SectionState == null ? "-" : $"{b.SectionState.Behavior} {b.SectionState.ConcreteExcluded} {B(b.SectionState.ModularRatio)} {b.SectionState.ClassificationCase ?? "-"} {b.SectionState.PropertyFingerprint}").Append(" composite ").Append(b.CompositeSection == null ? "-" : "yes").Append('\n');
        return s.ToString();
    }
}
