#if GPC_PILOT
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using BridGHe.Common.Gpc;
using BridGHe.Common.Gpc.Builders;
using BridGHe.Common.Gpc.Legacy;
using BridGHe.Common.Models;
using GH_IO.Serialization;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Parameters;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;

/// <summary>
/// P2D (lane P2-dati, 12/11): the checks of the data types, the persistence and the format without the FEM path, in memory (regression
/// <c>p2d</c>):
/// <list type="bullet">
/// <item>P4 (user decision U1): a member of a newer writer of schema 1 that this reader does not know survives a read and a write (root
/// records and nested records, through the API and through a GH type); a datum changed by a component writes its own records; records of
/// schema 2 and archives of a later version are refused with the message for the user;</item>
/// <item>P5 (U6): the one frame rule of a global load system, on the frame and on the points of the 0.3.0 reader;</item>
/// <item>P11 (U5): a beam on a homogenised composite property draws the steel, the slab and the six bar lines as the FEM beam drew them, from
/// the reader and from the GPC builder, and keeps them through the records;</item>
/// <item>P2: the FEM chunk of a development file (<c>FemmR6</c>) read by value: alone, beside a datum (ignored), beside a model datum (the
/// drafts and GUIDs of its elements); a 0.3.0 payload read without a FEM value; nothing writes <c>FemmR6</c>;</item>
/// <item>P9 (U4): a composite slab strain keeps the kind and the case, never a value; no case is refused.</item>
/// </list>
/// </summary>
internal static class P2dChecks
{
    private static int checks;
    private static readonly List<string> failures = new();
    private static void Check(bool ok, string what) { checks++; if (!ok) failures.Add(what); }
    private static Assembly plugin;
    private static Type T(string name) => plugin.GetType("BridGHe.Grasshopper.DataTypes." + name, true);
    private static object Prop(object o, string name) => o.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance).GetValue(o);
    private static IGH_Goo FromGpc(Type type, params object[] args)
    {
        var m = type.GetMethods(BindingFlags.Public | BindingFlags.Static).Single(x => x.Name == "FromGpc");
        return (IGH_Goo)m.Invoke(null, args.Concat(Enumerable.Repeat((object)null, m.GetParameters().Length - args.Length)).ToArray());
    }

    internal static void Regression()
    {
        checks = 0; failures.Clear();
        plugin = BridgeMergeTests.Initialize();
        int format = Counted(Format), frames = Counted(Frames), preview = Counted(Preview), development = Counted(Development), strain = Counted(SlabStrain);
        if (failures.Count > 0) throw new Exception($"P2D FAIL ({failures.Count} of {checks}):\n" + string.Join("\n", failures.Take(30)));
        Console.WriteLine($"P2D PASS: {checks} checks; format BGHGPC1 schema 1 {format} (unknown members kept through read and write, schema 2 and later versions refused with the message), "
            + $"frame rule {frames}, preview of the homogenised composite property {preview}, FemmR6 by value and no FemmR6 written {development}, slab strain {strain}");
    }

    private static int Counted(Action a) { int before = checks; try { a(); } catch (Exception x) { failures.Add(a.Method.Name + ": " + x); } return checks - before; }

    // ----------------------------------------------------------------------------------------------------------------------------------------
    // P4: the format

    private static string Utf8(byte[] b) => Encoding.UTF8.GetString(b);
    private static byte[] Bytes(string s) => Encoding.UTF8.GetBytes(s);

    /// <summary>The records with an unknown member before the end of the first element named <paramref name="end"/> (a closing tag).</summary>
    private static byte[] WithUnknown(byte[] records, string end, string member, string value)
    {
        string text = Utf8(records); int at = text.IndexOf(end, StringComparison.Ordinal);
        if (at < 0) throw new Exception("no " + end + " in " + text);
        return Bytes(text.Insert(at, "<" + member + ">" + value + "</" + member + ">"));
    }

    private static void Format()
    {
        var (nodes, _) = GpcFixtures.CantileverInputs("p2d format");
        var node = nodes[1];
        var (data, records) = GpcDraftArchive.Write(node);
        Check(!Utf8(records).Contains("P2dFuture"), "the records written today hold no unknown member");
        // A root member of a newer writer: kept by a read and a write of the same datum.
        var newer = WithUnknown(records, "</DraftRecords>", "P2dFuture", "p2d-root");
        var read = GpcDraftArchive.ReadNode(data, newer);
        Check(GpcDraftArchiveChecks.Text(read) == GpcDraftArchiveChecks.Text(node), "a node with an unknown member reads as the node written");
        var again = Utf8(GpcDraftArchive.Write(read).Records);
        Check(again.Contains("<P2dFuture>p2d-root</P2dFuture>"), "the unknown root member survives a read and a write: " + again);
        Check(Utf8(GpcDraftArchive.Write(GpcDraftArchive.ReadNode(data, Bytes(again))).Records) == again, "a second read and write gives the same records");
        // A datum changed by a component is another object: its own records.
        Check(!Utf8(GpcDraftArchive.Write(GpcDrafts.Copy(read)).Records).Contains("P2dFuture"), "a changed copy writes its own records (no unknown member)");
        // A nested member (the section of the property of a beam) and a member of a newer writer in a list (a load case of the records).
        var beam = GpcFixtures.SingleBeam("p2d format beam");
        var (bd, br) = GpcDraftArchive.Write(beam);
        string bt = Utf8(br);
        Check(bt.Contains("</Section>"), "the beam records hold the section of its property");
        var nested = WithUnknown(br, "</Section>", "P2dFutureSection", "7.5");
        var beamRead = GpcDraftArchive.ReadBeam(bd, nested);
        Check(GpcDraftArchiveChecks.Text(beamRead) == GpcDraftArchiveChecks.Text(beam), "a beam with an unknown nested member reads as the beam written");
        string beamAgain = Utf8(GpcDraftArchive.Write(beamRead).Records);
        int s = beamAgain.IndexOf("<Section>", StringComparison.Ordinal), e = beamAgain.IndexOf("</Section>", StringComparison.Ordinal), f = beamAgain.IndexOf("<P2dFutureSection>7.5</P2dFutureSection>", StringComparison.Ordinal);
        Check(s >= 0 && f > s && f < e, "the unknown member of the section survives in the section record: " + beamAgain);
        // The record kinds with a GPC object or a record of their own: case, combination, stage, section, beam property, material with its kind.
        var dead = GpcCaseBuilders.Case("Dead", false);
        var caseRead = GpcDraftArchive.ReadCase(WithUnknown(GpcDraftArchive.Write(dead), "</DraftRecords>", "P2dFuture", "case"));
        Check(Utf8(GpcDraftArchive.Write(caseRead)).Contains("<P2dFuture>case</P2dFuture>") && caseRead == dead, "a case keeps the unknown member");
        var section = GpcSectionBuilders.Standard(BridgeSectionKind.SolidRectangle, 500, 300);
        var sectionRead = GpcDraftArchive.ReadSection(WithUnknown(GpcDraftArchive.Write(section), "</Section>", "P2dFutureSection", "s"));
        Check(Utf8(GpcDraftArchive.Write(sectionRead)).Contains("<P2dFutureSection>s</P2dFutureSection>"), "a section recipe keeps the unknown member of its record");
        var steel = GpcFixtures.Steel();
        var (md, _) = GpcDraftArchive.Write(steel, "Steel");
        var withKind = Bytes("<DraftRecords xmlns=\"urn:bridghe:gpc-draft-records\" xmlns:i=\"http://www.w3.org/2001/XMLSchema-instance\"><Schema>1</Schema><Kind>material</Kind><P2dFuture>m</P2dFuture></DraftRecords>");
        var materialRead = GpcDraftArchive.ReadMaterial(md, withKind, out string kind);
        var materialAgain = GpcDraftArchive.Write(materialRead, kind ?? GpcMaterials.KindOf(materialRead));
        Check(kind == null && materialAgain.Records != null && Utf8(materialAgain.Records).Contains("<P2dFuture>m</P2dFuture>"),
            "a material read with records of a newer writer keeps them, also with the kind of its GPC type");
        Check(GpcDraftArchive.Write(steel, GpcMaterials.KindOf(steel)).Records == null, "a material of today with the kind of its GPC type writes no records, as before");
        // Through a GH type: the chunk of a node with an unknown member read and written again.
        var nodeType = T("Fem.GH_Node");
        var archive = new GH_Archive(); Check(archive.AppendObject(FromGpc(nodeType, node), "datum"), "a GPC-only node writes");
        var chunk = (GH_Chunk)archive.GetRootNode.FindChunk("datum");
        Check(!chunk.ItemExists("FemmR6") && chunk.ItemExists("Data") && chunk.ItemExists("BridGHeRecords"), "a node writes its datum alone");
        chunk.RemoveItem("BridGHeRecords"); chunk.SetByteArray("BridGHeRecords", newer);
        var goo = (IGH_Goo)Activator.CreateInstance(nodeType);
        Check(archive.ExtractObject(goo, "datum") && goo.ScriptVariable() == null && goo.IsValid, "the GH node reads the records of a newer writer, without FEM value");
        var back = new GH_Archive(); back.AppendObject(goo, "datum");
        Check(Utf8(((GH_Chunk)back.GetRootNode.FindChunk("datum")).GetByteArray("BridGHeRecords")).Contains("<P2dFuture>p2d-root</P2dFuture>"), "the GH node writes the unknown member back");
        // Schema 2 (a member that must be read): refused with the message for the user, as a later version of the archive and of the drafts.
        var schema2 = Bytes(Utf8(newer).Replace("<Schema>1</Schema>", "<Schema>2</Schema>"));
        Check(Refused(() => GpcDraftArchive.ReadNode(data, schema2), "schema dei record BGHGPC1 2"), "records of schema 2 are refused with the message");
        var v2 = (byte[])data.Clone(); v2[7] = 2;
        Check(Refused(() => GpcDraftArchive.ReadNode(v2, records), "archivio BGHGPC1 versione 2"), "a BGHGPC1 archive of version 2 is refused with the message");
        var drafts = GpcModelDrafts.Write(new GpcModelDrafts(nodes, new LegacyBeamDraft[0], new LegacyPlateDraft[0])); drafts[7] = 2;
        Check(Refused(() => GpcModelDrafts.Read(drafts), "bozze BGHDRF2 versione 2"), "drafts BGHDRF2 of version 2 are refused with the message");
        chunk.RemoveItem("BridGHeRecords"); chunk.SetByteArray("BridGHeRecords", schema2);
        var refusedGoo = (IGH_Goo)Activator.CreateInstance(nodeType);
        Check(!archive.ExtractObject(refusedGoo, "datum"), "a GH node with records of schema 2 is not read");
        Check(GpcDraftArchive.UserMessage(new GpcNewerFormatException("schema dei record BGHGPC1 2")).Contains("versione più recente di BridGHe"), "the message for the user");
    }

    private static bool Refused(Action read, string what)
    {
        try { read(); return false; }
        catch (GpcNewerFormatException x) { return x.Message.Contains(what, StringComparison.Ordinal) && x.Message.Contains("versione più recente di BridGHe", StringComparison.Ordinal); }
    }

    // ----------------------------------------------------------------------------------------------------------------------------------------
    // P5: the frame rule

    private static GPC.Geometry.Point3d P(double x, double y, double z) => new GPC.Geometry.Point3d(x, y, z);

    private static void Frames()
    {
        Check(GpcLoadFrames.IsGlobal(P(0, 0, 0), P(1, 0, 0), P(0, 1, 0)), "the global points: global");
        Check(GpcLoadFrames.IsGlobal(P(0, 0, 0), P(2, 0, 0), P(0, 3, 0)), "(2, 0, 0) and (0, 3, 0): the global frame (R65-C2-01, U6)");
        Check(GpcLoadFrames.IsGlobal(P(0, 0, 0), P(5, 0, 0), P(4, 2, 0)), "a point in the plane 1-2 off the axis 2: the global frame");
        Check(!GpcLoadFrames.IsGlobal(P(1, 0, 0), P(2, 0, 0), P(1, 1, 0)), "another origin: not global");
        Check(!GpcLoadFrames.IsGlobal(P(0, 0, 0), P(0, 1, 0), P(-1, 0, 0)), "turned axes: not global");
        Check(!GpcLoadFrames.IsGlobal(P(0, 0, 0), P(1, 0, 0), P(2, 0, 0)), "aligned points (no frame): not global");
        Check(GpcLoadFrames.IsGlobal(GpcLoadFrames.Of(CoordinateSystemModel.Global)), "the global system of the parameter: global");
        // The 0.3.0 reader: the distributed load of a former beam on a stretched system is global (before P2D it was refused as not global).
        string Read(CoordinateSystemModel cs)
        {
            // A former model with a distributed load on its first beam, converted by the reader (the import reads the system of the load).
            var model = ExampleRemake04.WithEndNodes(BridgeUtilityTests.Source(), out _);
            model.Beams[0].Loads.Add(new BeamDistributedLoadModel(new LoadCaseModel("P2D", "P2D"), new() { LoadSchema = BeamDistributedLoadModel.LoadSchema.Uniform, CoordinateSystem = cs,
                LoadDirection = BeamDistributedLoadModel.LoadDirection.Z, PA = -2, PB = -2 }, BeamDistributedLoadModel.LoadType.Force));
            try { GpcModels.Convert(model, 0.001); return null; }
            catch (Exception x) when (x is ArgumentException || x is NotSupportedException || x is InvalidOperationException) { return "refused: " + x.Message; }
        }
        Check(Read(CoordinateSystemModel.Global) == null, "the 0.3.0 reader: a load on the global system");
        string stretched = Read(new CoordinateSystemModel("P2D", "Stretched") { Origin = new Point3d(0, 0, 0), Point1 = new Point3d(2, 0, 0), Point2 = new Point3d(0, 3, 0) });
        Check(stretched == null, "the 0.3.0 reader: a load on (2, 0, 0), (0, 3, 0) is on the global system (U6): " + stretched);
        string turned = Read(new CoordinateSystemModel("P2D", "Turned") { Origin = new Point3d(0, 0, 0), Point1 = new Point3d(0, 1, 0), Point2 = new Point3d(-1, 0, 0) });
        Check(turned != null && turned.StartsWith("refused", StringComparison.Ordinal), "the 0.3.0 reader: a load on a turned system is refused as before: " + turned);
    }

    // ----------------------------------------------------------------------------------------------------------------------------------------
    // P11: the preview of the homogenised composite property

    private static Type PreviewType => plugin.GetType("BridGHe.Grasshopper.Display.ModelPreview", true);
    private static object Call(string method, params object[] args) => PreviewType.GetMethods(BindingFlags.Public | BindingFlags.Static).First(x => x.Name == method && x.GetParameters().Length == args.Length).Invoke(null, args);
    private static bool Near(double a, double b, double tol = 1e-7) => Math.Abs(a - b) <= tol * Math.Max(1, Math.Max(Math.Abs(a), Math.Abs(b)));
    private static bool Near(Point3d a, Point3d b) => Near(a.X, b.X) && Near(a.Y, b.Y) && Near(a.Z, b.Z);
    private static bool Same(Point3d a, Point3d b) => a.X == b.X && a.Y == b.Y && a.Z == b.Z;

    /// <summary>Two drawings: null when equal (same count, each solid with the same faces, edges and vertices, bit for bit when <paramref name="exact"/>,
    /// otherwise within 1e-7 relative as C3; the same bar lines).</summary>
    private static string Compare(Brep[] fem, Brep[] gpc, bool exact)
    {
        if (fem.Length != gpc.Length) return $"{gpc.Length} solids, FEM {fem.Length}";
        for (int i = 0; i < fem.Length; i++)
        {
            if (fem[i].Faces.Count != gpc[i].Faces.Count || fem[i].Edges.Count != gpc[i].Edges.Count || fem[i].Vertices.Count != gpc[i].Vertices.Count) return $"solid {i}: topology";
            var left = gpc[i].Vertices.Select(v => v.Location).ToList();
            foreach (var v in fem[i].Vertices)
            {
                int k = left.FindIndex(p => exact ? Same(p, v.Location) : Near(p, v.Location));
                if (k < 0) return $"solid {i}: FEM vertex {v.Location} not drawn";
                left.RemoveAt(k);
            }
        }
        return null;
    }

    private static string Compare(Line[] fem, Line[] gpc, bool exact)
    {
        if (fem.Length != gpc.Length) return $"{gpc.Length} bar lines, FEM {fem.Length}";
        for (int i = 0; i < fem.Length; i++)
            if (exact ? !Same(fem[i].From, gpc[i].From) || !Same(fem[i].To, gpc[i].To) : !Near(fem[i].From, gpc[i].From) || !Near(fem[i].To, gpc[i].To)) return $"bar {i}: {gpc[i]}, FEM {fem[i]}";
        return null;
    }

    private static void Preview()
    {
        foreach (var (rows, angle, ox) in new[] { ((250.0, 3000.0, 2000.0, 40.0, 1500.0, 55.0), 0.0, 0.0), ((250.0, 2500.0, 2000.0, 40.0, 0.0, 0.0), 30.0, 120.0) })
        {
            string what = $"homogenised property (bottom row {rows.Item5}, angle {angle}, offset {ox})";
            var p0 = BridgeGrillageExample.Composite(new()); p0.ConcreteSection = new(rows.Item1, rows.Item2, rows.Item3, rows.Item4, rows.Item5, rows.Item6); p0.ConsiderRebar = true;
            var fem = new BeamModel("B1", new Point3d(0, 0, 0), new Point3d(12000, 3000, 500), p0.CreateHomogenizedProperty(12, "Shrink")) { AngleDeg = angle, Offset = new Vector2d(ox, -40) };
            var femShapes = fem.Shapes.ToArray(); var femBars = (Line[])Call("ReinforcementLines", fem);
            Check(femShapes.Length == 2 && femBars.Length == (rows.Item5 > 0 ? 6 : 3), what + ": the FEM beam draws steel, slab and the bars of its rows");
            // The reader: the draft of the former beam holds the outline of its preview geometry.
            var draft = GpcBeamDrafts.FromFemm(fem);
            Check(GpcDraftShapes.HasCompositePreview(draft) && draft.Property.Section.CompositeOutline != null, what + ": the reader keeps the outline");
            string shapes = Compare(femShapes, GpcDraftShapes.Beam(draft), false), bars = Compare(femBars, GpcDraftShapes.Bars(draft), true);
            Check(shapes == null, what + ": the GPC solids are the FEM ones: " + shapes);
            Check(bars == null, what + ": the GPC bar lines are the FEM ones, bit for bit: " + bars);
            Check(Compare(femShapes, (Brep[])Call("DraftShapes", draft), false) == null && Compare(femBars, (Line[])Call("DraftReinforcementLines", draft), true) == null,
                what + ": the plug-in preview of the draft draws them");
            // Through the records (P11: an optional member of schema 1).
            var (d, r) = GpcDraftArchive.Write(draft);
            var read = GpcDraftArchive.ReadBeam(d, r);
            Check(read.Property.Section.CompositeOutline == draft.Property.Section.CompositeOutline && Compare(femBars, GpcDraftShapes.Bars(read), true) == null, what + ": the outline is kept by the records");
            // The GPC builder of Composite Beam Property (the homogenised output from the definition): the same drawing.
            if (angle != 0) continue;
            var definition = GpcCompositeSections.Definition(p0, p0.Name);
            var shrink = GpcPropertyBuilders.Homogenised(definition, 12, "Shrink", GpcFixtures.Steel(), "Steel");
            var built = GpcBeamBuilders.New(new GPC.Geometry.Point3d(0, 0, 0), new GPC.Geometry.Point3d(12000, 3000, 500), shrink, null, 0, ox, -40, R2aDesignFixtures.Id("p2d:homogenised"), "H1");
            string builtShapes = Compare(femShapes, GpcDraftShapes.Beam(built), false), builtBars = Compare(femBars, GpcDraftShapes.Bars(built), false);
            Check(builtShapes == null && builtBars == null, what + ": the property built on GPC draws as the FEM one: " + builtShapes + " " + builtBars);
        }
        // A beam that is not on a homogenised composite property has no bar lines.
        Check(GpcDraftShapes.Bars(GpcFixtures.SingleBeam("p2d plain")).Length == 0, "a plain beam: no bar lines");
    }

    // ----------------------------------------------------------------------------------------------------------------------------------------
    // P2: the FEM chunk read by value

    private static GH_Archive Chunk(Action<GH_Chunk> fill)
    {
        var archive = new GH_Archive(); archive.CreateNewRoot(true); var chunk = (GH_Chunk)archive.GetRootNode.CreateChunk("datum"); fill(chunk); return archive;
    }

    private static IGH_Goo ReadGoo(Type type, GH_Archive archive)
    {
        var goo = (IGH_Goo)Activator.CreateInstance(type);
        return archive.ExtractObject(goo, "datum") ? goo : null;
    }

    private static Dictionary<string, byte[]> Items(IGH_Goo goo)
    {
        var archive = new GH_Archive(); archive.AppendObject(goo, "datum");
        return ((GH_Chunk)archive.GetRootNode.FindChunk("datum")).Items.Where(i => i.Type == GH_IO.Types.GH_Types.gh_bytearray).ToDictionary(i => i.Name, i => i._bytearray);
    }

    private static void Development()
    {
        var model = ExampleRemake04.WithEndNodes(BridgeUtilityTests.Source(), out _);
        var node = model.Nodes[0]; var beam = model.Beams[0];
        var nodeType = T("Fem.GH_Node"); var beamType = T("Fem.GH_Beam"); var modelType = T("Fem.GH_Model"); var materialType = T("Fem.GH_Material");
        // An element: the FEM chunk alone (a datum not written by the development version), and beside a datum (ignored).
        byte[] femmNode = BridGHe.Common.Helpers.ModelArchive.Write(node);
        var alone = ReadGoo(nodeType, Chunk(c => { c.SetGuid("Guid", Guid.NewGuid()); c.SetByteArray("FemmR6", femmNode); }));
        Check(alone != null && alone.ScriptVariable() == null && GpcDraftArchiveChecks.Text((LegacyNodeDraft)Prop(alone, "Gpc")) == GpcDraftArchiveChecks.Text(GpcLegacyPayloads.Node(femmNode)),
            "a node with the FEM chunk alone is read by value, without FEM value");
        var datum = GpcBeamDrafts.FromFemm(beam); var (bd, br) = GpcDraftArchive.Write(datum);
        var beside = ReadGoo(beamType, Chunk(c => { c.SetGuid("Guid", Guid.NewGuid()); c.SetByteArray("Data", bd); c.SetByteArray("BridGHeRecords", br); c.SetByteArray("FemmR6", BridGHe.Common.Helpers.ModelArchive.Write(model.Beams[1])); }));
        Check(beside != null && beside.ScriptVariable() == null && GpcDraftArchiveChecks.Text((LegacyBeamDraft)Prop(beside, "Gpc")) == GpcDraftArchiveChecks.Text(datum),
            "a beam with a datum and a FEM chunk is its datum (the FEM chunk is not read)");
        Check(!Items(beside).ContainsKey("FemmR6"), "written again, no FEM chunk");
        // A FEM-backed GH type of a component not yet on GPC writes its datum alone.
        var femBacked = (IGH_Goo)Activator.CreateInstance(beamType, beam, null);
        Check(femBacked.ScriptVariable() != null && !Items(femBacked).ContainsKey("FemmR6") && Items(femBacked).ContainsKey("Data"), "a FEM-backed beam writes its datum alone (no FemmR6)");
        // A material of 0.3.0 with its declared kind.
        var material = new MaterialModel("S355") { Name = "S355", Kind = MaterialModel.MaterialKind.Steel, Modulus = 210000, PoissonRatio = .3, Density = 7.85e-9 };
        var m = ReadGoo(materialType, Chunk(c => c.SetByteArray("Data", BridGHe.Common.Helpers.ModelArchive.Write(material))));
        Check(m != null && m.ScriptVariable() == null && (string)Prop(m, "GpcKind") == "Steel", "a 0.3.0 material is read by value with its kind");
        // A model of a development file: its datum, the FEM chunk of its elements and the GUIDs of its FEM elements, no drafts chunk.
        var conversion = GpcModels.Convert(model, 0.001);
        var (md, mr) = GpcDraftArchive.Write(conversion);
        var femGuids = model.Elements.Select(_ => Guid.NewGuid()).ToArray();
        var femm = BridGHe.Common.Helpers.ModelArchive.Write(model);
        var dev = ReadGoo(modelType, Chunk(c => { c.SetByteArray("Data", md); c.SetByteArray("BridGHeRecords", mr); c.SetByteArray("FemmR6", femm); c.SetString("ElementGuids", JsonSerializer.Serialize(femGuids)); }));
        Check(dev != null && dev.ScriptVariable() == null && dev.IsValid, "a model of a development file is read without FEM value");
        var drafts = (GpcModelDrafts)Prop(dev, "GpcDrafts");
        Check(drafts != null && drafts.Nodes.Count == model.Nodes.Count && drafts.Beams.Count == model.Beams.Count && drafts.Plates.Count == model.Plates.Count, "its elements have their drafts, read by value from the FEM chunk");
        var expectedGuids = model.Elements.Select((e, i) => (e, i)).OrderBy(x => x.e is NodeModel ? 0 : x.e is BeamModel ? 1 : 2).ThenBy(x => x.i).Select(x => femGuids[x.i]).ToArray();
        var guids = (Guid[])modelType.GetMethod("ElementGuidsOf").Invoke(dev, new object[] { drafts });
        Check(guids.SequenceEqual(expectedGuids), "the GH GUIDs of its FEM elements in the order of the drafts");
        var byValue = GpcLegacyPayloads.Model(femm, 0.001, out _).Drafts;
        Check(GpcDraftCapture.WithoutGuids(GpcDraftArchiveChecks.DraftsText(drafts)) == GpcDraftCapture.WithoutGuids(GpcDraftArchiveChecks.DraftsText(byValue)),
            "the drafts of the 0.3.0 reader of the same FEM chunk, but for GUIDs");
        var written = Items(dev);
        Check(!written.ContainsKey("FemmR6") && written.ContainsKey(GpcModelDrafts.Chunk), "written again: no FEM chunk, the drafts chunk");
        var archive = new GH_Archive(); archive.AppendObject(dev, "datum");
        var reread = ReadGoo(modelType, archive);
        var rereadDrafts = (GpcModelDrafts)Prop(reread, "GpcDrafts");
        Check(reread != null && GpcDraftArchiveChecks.DraftsText(rereadDrafts) == GpcDraftArchiveChecks.DraftsText(drafts)
            && ((Guid[])modelType.GetMethod("ElementGuidsOf").Invoke(reread, new object[] { rereadDrafts })).SequenceEqual(expectedGuids), "read again: the same drafts and GUIDs");
        // A model of 0.3.0 (Data = BGHA1) with the GUIDs of its elements: by value, no FEM value, the GUIDs in the order of the drafts.
        var legacy = ReadGoo(modelType, Chunk(c => { c.SetByteArray("Data", femm); c.SetString("ElementGuids", JsonSerializer.Serialize(femGuids)); }));
        Check(legacy != null && legacy.ScriptVariable() == null && legacy.IsValid && (bool)Prop(legacy, "HasAttachedInput"), "a 0.3.0 model is its GPC input read by value");
        var legacyDrafts = (GpcModelDrafts)Prop(legacy, "GpcDrafts");
        Check(legacyDrafts != null && ((Guid[])modelType.GetMethod("ElementGuidsOf").Invoke(legacy, new object[] { legacyDrafts })).SequenceEqual(expectedGuids), "its GH GUIDs in the order of the drafts");
        Check(GpcInputComparison.Document(((GpcModels.Conversion)Prop(legacy, "GpcInput")).Model) == GpcInputComparison.Document(GpcLegacyPayloads.Model(femm, 0.001, out _).Model),
            "its GPC input is the reader's");
        var legacyItems = Items(legacy);
        Check(!legacyItems.ContainsKey("FemmR6") && !GpcGooArchive.IsLegacy(legacyItems["Data"]), "written again in 0.4.0 form (BGHGPC1, no FEM chunk)");
    }

    // ----------------------------------------------------------------------------------------------------------------------------------------
    // P9: the slab strain

    private static void SlabStrain()
    {
        var beam = GpcFixtures.SingleBeam("p2d strain");
        bool refused;
        try { GpcDraftTransforms.WithSlabStrain(beam, null); refused = false; } catch (ArgumentException) { refused = true; }
        Check(refused, "a slab strain without its case is refused");
        var strain = GpcDraftTransforms.WithSlabStrain(beam, GpcCaseBuilders.Case("Shrink", false) with { Stages = 1 });
        Check(strain.Records.LoadRecords.Count(l => l.Kind == "CompositeBeamSlabStrainModel" && l.Case == "Shrink") == 1, "the kind of the load and its case are recorded");
        var (d, r) = GpcDraftArchive.Write(strain);
        Check(GpcDraftArchiveChecks.Text(GpcDraftArchive.ReadBeam(d, r)) == GpcDraftArchiveChecks.Text(strain) && !Utf8(r).Contains("Strain>"), "the records keep no strain value (U4)");
    }
}

/// <summary>
/// P2D (P2, user decision U2 of 12/11): the tool that remakes 24, 42, 43, 47 and 48 without their FEM chunks (<c>FemmR6</c>), headless:
/// each file is read as P2 reads it (every internalised BridGHe datum by value, without FEM value; an internalised model of a development
/// file takes the drafts and GUIDs of its elements from its FEM chunk) and written back as it is (every object and instance GUID kept,
/// only the internalised data in 0.4.0 form). Nothing is solved, no analysis is run.
/// <para><c>--p2d-remake-examples</c> writes the remade files into <c>%TEMP%\bridghe-p2d-remake</c> and reports (sizes, FEM chunks before
/// and after, internalised data and models with drafts and GPC results, read again); <c>--p2d-remake-examples write</c> writes the five
/// files of the worktree (never the main checkout). The coordinator runs it once after the merge.</para>
/// </summary>
internal static class P2dExampleRemake
{
    private static IEnumerable<IGH_Param> Params(GH_Document doc) => doc.Objects.OfType<IGH_Param>().Concat(doc.Objects.OfType<GH_Component>().SelectMany(c => c.Params.Input));

    private static int Count(string text, string what) { int n = 0; for (int i = text.IndexOf(what, StringComparison.Ordinal); i >= 0; i = text.IndexOf(what, i + what.Length, StringComparison.Ordinal)) n++; return n; }

    /// <summary>The internalised BridGHe data of a document: count, with a FEM value, models, models with drafts, models with GPC results.</summary>
    private static (int Data, int Fem, int Models, int Drafts, int Results) Data(GH_Document doc)
    {
        int data = 0, fem = 0, models = 0, drafts = 0, results = 0;
        foreach (var param in Params(doc))
        {
            if (!(param.GetType().GetProperty("PersistentData")?.GetValue(param) is IGH_Structure tree)) continue;
            foreach (var goo in tree.AllData(true).OfType<IGH_Goo>().Where(g => g.GetType().Namespace?.StartsWith("BridGHe.", StringComparison.Ordinal) == true))
            {
                data++; if (goo.GetType().GetProperty("Value")?.GetValue(goo) != null && goo.GetType().Namespace.EndsWith(".Fem", StringComparison.Ordinal)) fem++;
                if (goo.GetType().Name != "GH_Model") continue;
                models++;
                if (goo.GetType().GetProperty("GpcDrafts").GetValue(goo) != null) drafts++;
                if (goo.GetType().GetProperty("GpcResults").GetValue(goo) != null) results++;
            }
        }
        return (data, fem, models, drafts, results);
    }

    internal static int Run(bool write)
    {
        BridgeMergeTests.Initialize();
        string root = Path.GetFullPath(ExamplePaths.Root);
        if (write && root.StartsWith(@"C:\Users\g.pacini\Documents\Repository\BridGHe\", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Never writes into the main checkout.");
        string output = write ? root : Path.Combine(Path.GetTempPath(), "bridghe-p2d-remake");
        int failures = 0;
        foreach (var relative in ExampleRemake04.Files)
        {
            string path = Path.Combine(root, relative), target = Path.Combine(output, relative);
            long before = new FileInfo(path).Length; int femmBefore = Count(File.ReadAllText(path), "FemmR6");
            (int Data, int Fem, int Models, int Drafts, int Results) read, again;
            using (var doc = GrasshopperBridgeExample.Read(path))
            {
                read = Data(doc);
                Directory.CreateDirectory(Path.GetDirectoryName(target));
                GrasshopperBridgeExample.Write(doc, target);
            }
            int femmAfter = Count(File.ReadAllText(target), "FemmR6");
            using (var doc = GrasshopperBridgeExample.Read(target)) again = Data(doc);
            bool ok = femmAfter == 0 && read.Fem == 0 && again == read && read.Drafts == read.Models;
            if (!ok) failures++;
            Console.WriteLine($"P2D REMAKE {(ok ? "OK" : "FAIL")} {relative}: {before} -> {new FileInfo(target).Length} bytes, FemmR6 {femmBefore} -> {femmAfter}; {read.Data} internalised data "
                + $"({read.Fem} with a FEM value), {read.Models} models ({read.Drafts} with the drafts of their elements, {read.Results} with GPC results); read again: {(again == read ? "the same" : again.ToString())}"
                + (write ? "" : "; written to " + target));
        }
        Console.WriteLine(failures == 0 ? $"P2D REMAKE PASS: {ExampleRemake04.Files.Length} examples without FemmR6{(write ? " (written in the worktree)" : " (dry run, " + output + ")")}" : $"P2D REMAKE FAIL: {failures} examples");
        return failures == 0 ? 0 : 1;
    }
}

/// <summary>
/// P2D (P2, the measures of the former lane 4): a definition solved, saved as <c>.gh</c> and <c>.ghx</c> and reopened, with the same results.
/// <c>--p2d-save-reopen &lt;definition&gt; &lt;label&gt;</c> (native, under the lock): the Output Paths of the analyses go into
/// <c>%TEMP%\bridghe-p2d-save\&lt;label&gt;</c> (never the definition's), the analyses, Bridge Section Read, Build BridGHe Model and Super
/// Element Check are pressed (Bridge Report is not), the document is solved and its section results fingerprinted
/// (<see cref="ResultFingerprint"/>, in <c>BRIDGHE_RESULT_DUMP</c> or the folder of the label); the solved document is saved as .gh and .ghx
/// (sizes, FEM chunks), the .gh is reopened and solved again with the Straus R3 analyses on their saved results (not pressed: their cache and
/// the results file next to the .st7; the moving analysis is pressed again, its results are not saved), and the fingerprints are compared row
/// by row. The definition file is never written.
/// </summary>
internal static class P2dSaveReopen
{
    private static readonly string[] Pressed = { "StrausR3AnalysisComponent", "MovingStaticAnalysisComponent", "BuildBridgeModel", "SuperElementCheckComponent" };

    private static void Press(GH_Component c, bool on)
    {
        var f = c.GetType().GetField("_run", BindingFlags.Instance | BindingFlags.NonPublic) ?? c.GetType().GetField("run", BindingFlags.Instance | BindingFlags.NonPublic);
        f?.SetValue(c, on); if (on) c.ExpireSolution(false);
    }

    private static IGH_Param Input(GH_Component c, string name) => c.Params.Input.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));

    private static string Errors(GH_Document doc) => string.Join("\n", doc.Objects.OfType<GH_ActiveObject>().SelectMany(o => o.RuntimeMessages(GH_RuntimeMessageLevel.Error).Select(m => o.NickName + ": " + m)).Take(20));

    private static void Solve(GH_Document doc, bool reopened)
    {
        foreach (var c in doc.Objects.OfType<GH_Component>())
        {
            string type = c.GetType().Name;
            if (type == "BridgeReportComponent") { Press(c, false); continue; }
            if (type == "BridgeSectionReadComponent") { BridgeSegmentExcelTests.Press(c); c.ExpireSolution(false); continue; }
            if (!Pressed.Contains(type)) continue;
            // Reopened: the Straus R3 analyses keep their saved results (not pressed); everything else is pressed as on the first run.
            Press(c, !(reopened && type == "StrausR3AnalysisComponent"));
        }
        BridgeSegmentExcelTests.Solve(doc, true);
    }

    internal static int Run(string definition, string label)
    {
        BridgeMergeTests.Initialize();
        string source = Path.GetFullPath(Path.Combine(ExamplePaths.Root, definition));
        byte[] original = File.ReadAllBytes(source);
        string folder = Path.Combine(Path.GetTempPath(), "bridghe-p2d-save", label); Directory.CreateDirectory(folder);
        string dump = ResultFingerprint.Folder ?? throw new InvalidOperationException("Set BRIDGHE_RESULT_DUMP to the folder of the fingerprints.");
        string gh = Path.Combine(folder, label + "-solved.gh"), ghx = Path.Combine(folder, label + "-solved.ghx");
        var clock = System.Diagnostics.Stopwatch.StartNew();
        using (var doc = GrasshopperBridgeExample.Read(source))
        {
            doc.FilePath = Path.Combine(folder, Path.GetFileName(source));
            var analyses = doc.Objects.OfType<GH_Component>().Where(c => c.GetType().Name is "StrausR3AnalysisComponent" or "MovingStaticAnalysisComponent").OrderBy(c => c.NickName, StringComparer.Ordinal).ThenBy(c => c.InstanceGuid).ToArray();
            for (int i = 0; i < analyses.Length; i++)
            {
                var a = analyses[i];
                var model = a.GetType().GetField("_model", BindingFlags.Instance | BindingFlags.NonPublic); model.SetValue(a, Activator.CreateInstance(model.FieldType));
                var path = (Param_String)Input(a, "Output Path"); path.RemoveAllSources(); path.PersistentData.Clear(); path.SetPersistentData(Path.Combine(folder, $"{i + 1:00}-{string.Concat(a.NickName.Split(Path.GetInvalidFileNameChars())).Replace(' ', '_')}.st7"));
                if (Input(a, "Keep Running") is Param_Boolean keep) { keep.RemoveAllSources(); keep.PersistentData.Clear(); keep.SetPersistentData(false); }
                if (Input(a, "Run") is Param_Boolean runInput) { runInput.RemoveAllSources(); runInput.PersistentData.Clear(); runInput.SetPersistentData(false); }
            }
            Solve(doc, false);
            string errors = Errors(doc);
            Console.WriteLine($"P2D SAVE {label}: solved in {clock.Elapsed.TotalSeconds:F0} s, {analyses.Length} analyses; errors: {(errors.Length == 0 ? "none" : errors)}");
            ResultFingerprint.Write(label + "-run", doc);
            var archive = new GH_Archive(); if (!archive.AppendObject(doc, "Definition") || !archive.WriteToFile(gh, true, false) || !archive.WriteToFile(ghx, true, false)) throw new IOException("Cannot save " + gh);
        }
        long ghBytes = new FileInfo(gh).Length, ghxBytes = new FileInfo(ghx).Length; int femm = 0;
        { string text = File.ReadAllText(ghx); for (int i = text.IndexOf("FemmR6", StringComparison.Ordinal); i >= 0; i = text.IndexOf("FemmR6", i + 6, StringComparison.Ordinal)) femm++; }
        Console.WriteLine($"P2D SAVE SIZE {label}: .gh {ghBytes} bytes ({ghBytes / 1048576.0:F2} MB), .ghx {ghxBytes} bytes ({ghxBytes / 1048576.0:F2} MB), FemmR6 items {femm}");
        clock.Restart();
        using (var reopened = GrasshopperBridgeExample.Read(gh))
        {
            Solve(reopened, true);
            string errors = Errors(reopened);
            Console.WriteLine($"P2D REOPEN {label}: solved in {clock.Elapsed.TotalSeconds:F0} s; errors: {(errors.Length == 0 ? "none" : errors)}");
            ResultFingerprint.Write(label + "-reopened", reopened);
        }
        if (!File.ReadAllBytes(source).SequenceEqual(original)) throw new Exception("The definition was modified.");
        var run = File.ReadAllLines(Path.Combine(dump, label + "-run.rows.tsv")); var again = File.ReadAllLines(Path.Combine(dump, label + "-reopened.rows.tsv"));
        int differ = run.Zip(again, (a, b) => a == b ? 0 : 1).Sum() + Math.Abs(run.Length - again.Length);
        bool same = differ == 0 && run.Length > 1 && femm == 0;
        Console.WriteLine($"P2D SAVE REOPEN {(same ? "PASS" : "FAIL")} {label}: {run.Length - 1} section result rows, {differ} different after reopening; FemmR6 items {femm}");
        return same ? 0 : 1;
    }
}
#endif
