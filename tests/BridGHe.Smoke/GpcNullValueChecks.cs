using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using GH_IO.Serialization;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;

/// <summary>
/// R6.5 P0 (coordinator, 7/11): every GH type with a GPC datum works with a null FEM value. For the elements, properties, materials, cases,
/// combinations, stages, groups and sections of the examples 01–10 (built in memory) and for their models: the GH type made from the GPC
/// datum of the FEM-backed one alone (<c>FromGpc</c>) is valid, has the same datum, the same bounding box and casts (elements), a text, copies
/// that keep the datum, and is written without the transitional <c>FemmR6</c> chunk and read back with the same datum and no FEM value; the
/// GPC-only model owns its state (input, analysed model) and gives it to its copies. The FEM-backed types are not changed (the gates).
/// Regression <c>gpc-null-values</c> (not native).
/// <para><b>R6.6 lane R1 (11/11): without the FEM model.</b> The data come from the GPC examples 01–10 of R0 (<see cref="GpcNativeExamples"/>:
/// drafts, properties, sections, materials, groups, cases, combinations, stages and the GPC input of Build Model) and from the producer components
/// solved with the switches of P2 on (the extended records of L2C: fillet radius, section moduli, Brep shapes, mirror and gap, material kind,
/// membrane bending; the composite properties). Every GH type made from its datum alone (<see cref="Contract"/>): valid, no FEM value, a text,
/// copies with the datum (and its extras), written without <c>FemmR6</c> and read back with the same datum but for GUIDs; the geometry against
/// the coordinates of the drafts (bounding box, casts, translation and morph of a plate against the plate on the moved corners and the morphed
/// vertices). <b>Damaged chunks</b> (<see cref="Damaged"/>): every byte array a GH type without FEM value writes, removed or truncated, is not
/// read as another valid datum (the rule of R65-C2-02, which C1 extended to the model, P30-01/02/04: the model, the records and the trailing data
/// are in <c>gpc-review-fixes</c> and not repeated here). P2CA (13/11/2026, U3): the TRANSITIONAL run on the FEM catalogue (each GPC-only
/// type against its FEM-backed one) is retired by name (<see cref="P2rRetired.Checks"/>).</para>
/// </summary>
internal static class GpcNullValueChecks
{
    private static void Require(bool ok, string what) { if (!ok) throw new Exception("GPC NULL VALUES FAIL: " + what); }

    private static Assembly plugin;
    private static Type T(string name) => plugin.GetType("BridGHe.Grasshopper.DataTypes." + name, true);
    private static object Prop(object o, string name) => o.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance).GetValue(o);
    private static object FromGpc(Type type, params object[] args) =>
        type.GetMethods(BindingFlags.Public | BindingFlags.Static).Single(m => m.Name == "FromGpc").Invoke(null, args.Concat(Enumerable.Repeat((object)null,
            type.GetMethods(BindingFlags.Public | BindingFlags.Static).Single(m => m.Name == "FromGpc").GetParameters().Length - args.Length)).ToArray());

    /// <summary>The items of the chunk a goo writes: name and bytes (byte arrays) or text.</summary>
    private static Dictionary<string, string> Written(IGH_Goo goo, out GH_Archive archive)
    {
        archive = new GH_Archive();
        Require(archive.AppendObject(goo, "datum"), goo.GetType().Name + " writes");
        var chunk = archive.GetRootNode.FindChunk("datum");
        var items = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var item in chunk.Items)
            items[item.Name] = item.Type == GH_IO.Types.GH_Types.gh_bytearray ? Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(item._bytearray)) : item.InternalData?.ToString();
        return items;
    }

    private static int checks;

    internal static string TextOf(object datum) => datum switch
    {
        BridGHe.Common.Gpc.Legacy.LegacyNodeDraft n => GpcDraftArchiveChecks.Text(n),
        BridGHe.Common.Gpc.Legacy.LegacyBeamDraft b => GpcDraftArchiveChecks.Text(b),
        BridGHe.Common.Gpc.Legacy.LegacyPlateDraft p => GpcDraftArchiveChecks.Text(p),
        GPC.Model.Materials.Material m => GpcDraftArchiveChecks.Text(m),
        BridGHe.Common.Gpc.Legacy.LegacyCaseData c => GpcDraftArchiveChecks.Text(c),
        BridGHe.Common.Gpc.Legacy.LegacyCombinationDraft c => GpcDraftArchiveChecks.Text(c),
        BridGHe.Common.Gpc.Legacy.LegacyStageDraft st => GpcDraftArchiveChecks.Text(st),
        BridGHe.Common.Gpc.Legacy.LegacySectionRecipe r => GpcDraftArchiveChecks.Text(r),
        BridGHe.Common.Gpc.Legacy.LegacyBeamProperty bp => GpcDraftArchiveChecks.Text(bp),
        GPC.Model.ElementProperties.PlateProperty pp => GpcDraftArchiveChecks.Text(pp),
        // P2Y (lane 2 replay): the composite section of Composite Beam Property, Segment Composite and State Override.
        GPC.Checkers.CompositeBridge.BridgeCompositeSectionDefinition d => GpcDraftArchiveChecks.Text(d),
        BridGHe.Common.Gpc.GpcModels.Conversion c => GpcDraftCapture.ConversionText(c),
        // L2B (lane 2b): the coordinate system of the loads, its GPC frame (name, origin, axes; exact doubles).
        GPC.Geometry.CoordinateSystem f => "frame \"" + f.Name + "\" " + string.Join(" ", new[] { f.Origin.X, f.Origin.Y, f.Origin.Z, f.V1.X, f.V1.Y, f.V1.Z, f.V2.X, f.V2.Y, f.V2.Z, f.V3.X, f.V3.Y, f.V3.Z }
            .Select(v => v.ToString("R", System.Globalization.CultureInfo.InvariantCulture))),
        IEnumerable<string> g => string.Join("\\", g),
        _ => throw new Exception("GPC NULL VALUES FAIL: no text for " + datum?.GetType().Name),
    };

    /// <summary>The common contract of a GH type without FEM value: valid, the datum, a text, copies with the datum, persistence without FemmR6.</summary>
    private static bool Same(object a, object b) => ReferenceEquals(a, b) || a != null && a.GetType().IsDefined(typeof(System.Runtime.CompilerServices.CompilerGeneratedAttribute), false) == false && a.GetType().GetMethod("<Clone>$") != null && a.Equals(b) || a is IEnumerable<string> x && b is IEnumerable<string> y && x.SequenceEqual(y);

    private static IGH_Goo Common(string what, IGH_Goo femBacked, Type type, Func<object, object> datumOf, IGH_Goo gpcOnly, bool persistent = true, params string[] ignored)
    {
        object datum = datumOf(femBacked);
        Require(datum != null, what + ": the FEM-backed type has a GPC datum");
        Require(gpcOnly.ScriptVariable() == null, what + ": no FEM value");
        Require(Same(datumOf(gpcOnly), datum), what + ": the datum of the GPC-only type is the one given");
        Require(gpcOnly.IsValid, what + ": valid without FEM value (" + gpcOnly.IsValidWhyNot + ")");
        string text = gpcOnly.ToString();
        Require(!string.IsNullOrEmpty(text) && !text.StartsWith("Null ", StringComparison.Ordinal), what + ": text " + text);
        var copy = gpcOnly is IGH_GeometricGoo g ? (IGH_Goo)g.DuplicateGeometry() : gpcOnly.Duplicate();
        Require(copy.ScriptVariable() == null && Same(datumOf(copy), datum), what + ": a copy keeps the datum, without FEM value");
        if (!persistent) { checks += 5; return gpcOnly; }
        var fem = Written(femBacked, out _); var gpc = Written(gpcOnly, out var archive);
        // P2D (P2): the FEM value is never written (no FemmR6), the FEM-backed type writes its datum alone, as the GPC-only type.
        Require(!fem.ContainsKey("FemmR6") && !gpc.ContainsKey("FemmR6"), what + ": FemmR6 never written (P2D)");
        foreach (var name in fem.Keys.Where(k => k != "FemmR6" && !ignored.Contains(k)))
            Require(gpc.TryGetValue(name, out var v) && v == fem[name], what + ": item " + name + " as the FEM-backed type writes it");
        var read = (IGH_Goo)Activator.CreateInstance(type);
        Require(archive.ExtractObject(read, "datum") && read.ScriptVariable() == null && read.IsValid, what + ": read back without FEM value");
        // Read back, the datum is the one written but for the GUIDs a read gives anew (the criterion of the captures, «equal but for GUIDs»).
        Require(Written(read, out _).Keys.OrderBy(k => k, StringComparer.Ordinal).SequenceEqual(gpc.Keys.OrderBy(k => k, StringComparer.Ordinal)), what + ": read back, the same items written");
        Require(GpcDraftCapture.WithoutGuids(TextOf(datumOf(read))) == GpcDraftCapture.WithoutGuids(TextOf(datum)), what + ": read back, the same datum but for GUIDs");
        checks += 8;
        return read;
    }

    private static void Box(string what, BoundingBox expected, BoundingBox actual)
    {
        double size = Math.Max(1, expected.Diagonal.Length);
        Require(expected.IsValid && actual.IsValid && expected.Min.DistanceTo(actual.Min) <= 1e-9 * size && expected.Max.DistanceTo(actual.Max) <= 1e-9 * size,
            $"{what}: bounding box {actual} vs {expected}");
        checks++;
    }

    /// <summary>P2Z (revisione ChatGPT R65-1): a morph that bends and shears (a plate keeps no plane under it).</summary>
    private sealed class Bend : SpaceMorph
    {
        public override Point3d MorphPoint(Point3d p) => new Point3d(p.X + 0.25 * p.Z, p.Y + 1e-4 * p.X * p.X, p.Z + 0.5 * p.Y);
    }

    /// <summary>P2Z (R65-1): two solids with the same vertices, edges and faces, bit for bit.</summary>
    private static bool SameBrep(Brep a, Brep b) => a != null && b != null && a.Faces.Count == b.Faces.Count && a.Edges.Count == b.Edges.Count
        && a.Vertices.Select(v => v.Location).SequenceEqual(b.Vertices.Select(v => v.Location));

    /// <summary>P2Z (R65-1): a translation and a morph of a plate given by its draft alone give the solid of the FEM plate.</summary>
    private static void Moved(string what, IGH_GeometricGoo fem, IGH_GeometricGoo gpc)
    {
        var move = Transform.Translation(120, -45, 30);
        Require(fem.Transform(move) is GH_Brep f && gpc.Transform(move) is GH_Brep g && SameBrep(f.Value, g.Value), what + ": translated, the solid of the FEM plate (R65-1)");
        Require(fem.Morph(new Bend()) is GH_Brep fm && gpc.Morph(new Bend()) is GH_Brep gm && SameBrep(fm.Value, gm.Value), what + ": morphed, the solid of the FEM plate (R65-1)");
        checks += 2; moved++;
    }

    private static int moved, copied;

    /// <summary>R1: the regression: the GPC data without FEM (examples, extended records, composite properties, in-memory data), the damaged chunks,
    /// the coordinate systems and bridge rules, then (TRANSITIONAL) the FEM catalogue.</summary>
    internal static void Regression()
    {
        moved = 0; copied = 0; checks = 0; damagedRefused = 0; damagedSame = 0; lost.Clear(); unexpected.Clear();
        plugin = BridgeMergeTests.Initialize();
        var (elements, data, models, extended) = WithoutFem();
        int independent = checks;
        Rules();
        independent = checks;
        int damaged = damagedRefused + damagedSame;
        Require(lost.Count == 0, "a damaged chunk read as another valid datum: " + string.Join("; ", lost.Take(5)));
        Require(unexpected.Count == 0, $"a damaged chunk raised an exception that is not a refusal of the readers ({unexpected.Count}): " + string.Join("; ", unexpected.Take(5)));
        Require(elements >= 60 && data >= 40 && models == 10 && extended >= 8 && damaged >= 100, $"too few cases without FEM: {elements} elements, {data} data, {models} models, {extended} extended, {damaged} damaged chunks");
        Console.WriteLine($"GPC NULL VALUES (R1, without FEM): {elements} elements, {data} properties, materials, sections, groups, cases, combinations and stages, {models} models of the GPC examples 01-10 (R0), "
            + $"{extended} extended records and composite properties of the producers (switches of P2 on), {moved} plates translated and morphed against their moved corners, {copied} copy constructors; "
            + $"{damaged} damaged chunks (a byte array removed or truncated): {damagedRefused} not read, {damagedSame} read as the same datum, 0 read as another datum");
        // P2CA (13/11/2026, U3): the TRANSITIONAL run on the FEM catalogue (each GPC-only type against its FEM-backed one) is retired by name with
        // the FEM types (P2rRetired.Checks); the checks without FEM above are its GPC coverage.
        Console.WriteLine($"GPC NULL VALUES PASS: {independent} checks without FEM (validity, datum, bounding box and casts, text, copies, persistence without FemmR6, damaged chunks, state of the model)"
            + "; RETIRED BY NAME (P2CA): the checks of the FEM catalogue against the FEM-backed types");
    }

    private static int damagedRefused, damagedSame;
    private static readonly List<string> lost = new List<string>();
    private static readonly List<string> unexpected = new List<string>();

    /// <summary>Task 39 (T12, P2D): the exception itself (not the reflection wrapper).</summary>
    private static Exception Unwrapped(Exception x) { while (x is TargetInvocationException t && t.InnerException != null) x = t.InnerException; return x; }
    private static string Inner(Exception x) { x = Unwrapped(x); return x.GetType().Name + ": " + x.Message; }

    /// <summary>Task 39 (T12, P2D): the refusals of the readers of a damaged datum (the rule of the product, GpcGooPersistence.IsRefusal):
    /// serialization, XML, compressed-stream and JSON errors, and the refusals of the readers.</summary>
    private static bool Refusal(Exception x)
    {
        x = Unwrapped(x);
        return x is System.Runtime.Serialization.SerializationException || x is System.Xml.XmlException || x is System.IO.InvalidDataException || x is System.IO.EndOfStreamException
            || x is ArgumentException || x is NotSupportedException || x is InvalidOperationException || x is FormatException || x is System.Text.Json.JsonException
            || x is KeyNotFoundException || x is OverflowException;
    }

    /// <summary>R1: the contract of a GH type given by its datum alone, with no FEM-backed twin: the datum, no FEM value, valid, a text, copies with the
    /// datum, written without <c>FemmR6</c> (its GPC chunks) and read back with the same datum but for GUIDs; then its damaged chunks
    /// (<see cref="Damaged"/>). <paramref name="textOf"/>: the text of the datum compared (default: <see cref="TextOf"/>; the extended records add
    /// their extras). Returns the GH type read back (or the type itself when not persisted).</summary>
    private static IGH_Goo Contract(string what, IGH_Goo gpcOnly, Type type, Func<object, object> datumOf, bool persistent = true, Func<IGH_Goo, string> textOf = null)
    {
        textOf ??= g => TextOf(datumOf(g));
        object datum = datumOf(gpcOnly);
        Require(datum != null, what + ": a GPC datum");
        Require(gpcOnly.ScriptVariable() == null, what + ": no FEM value");
        Require(gpcOnly.IsValid, what + ": valid without FEM value (" + gpcOnly.IsValidWhyNot + ")");
        string text = gpcOnly.ToString();
        Require(!string.IsNullOrEmpty(text) && !text.StartsWith("Null ", StringComparison.Ordinal), what + ": text " + text);
        var copy = gpcOnly is IGH_GeometricGoo g ? (IGH_Goo)g.DuplicateGeometry() : gpcOnly.Duplicate();
        Require(copy.ScriptVariable() == null && Same(datumOf(copy), datum) && textOf(copy) == textOf(gpcOnly), what + ": a copy keeps the datum, without FEM value");
        if (!persistent) { checks += 5; return gpcOnly; }
        var gpc = Written(gpcOnly, out var archive);
        Require(!gpc.ContainsKey("FemmR6") && (gpc.ContainsKey("Data") || gpc.ContainsKey("BridGHeRecords") || gpc.ContainsKey("GpcData") || gpc.Keys.Any(k => k.StartsWith("Design", StringComparison.Ordinal))),
            what + ": written by its GPC chunks, without FemmR6 (" + string.Join(", ", gpc.Keys) + ")");
        var read = (IGH_Goo)Activator.CreateInstance(type);
        Require(archive.ExtractObject(read, "datum") && read.ScriptVariable() == null && read.IsValid, what + ": read back without FEM value");
        Require(Written(read, out _).Keys.OrderBy(k => k, StringComparer.Ordinal).SequenceEqual(gpc.Keys.OrderBy(k => k, StringComparer.Ordinal)), what + ": read back, the same items written");
        Require(GpcDraftCapture.WithoutGuids(textOf(read)) == GpcDraftCapture.WithoutGuids(textOf(gpcOnly)), what + ": read back, the same datum but for GUIDs");
        checks += 8;
        Damaged(what, gpcOnly, type, textOf);
        return read;
    }

    /// <summary>R1 (task 4 of R6.6): every byte array of the chunk of a GH type without FEM value removed, then truncated to half: the GH type read
    /// from it is not read, or is not valid, or has no datum, or (counted apart) has the same datum but for GUIDs (the item carried nothing for it);
    /// a valid GH type with another datum is a loss (listed, the run fails). The rule of R65-C2-02 (bridge rules), for every kind; the model, the
    /// records without data and the trailing data are C1's (<c>gpc-review-fixes</c>, P30-01/02/04).</summary>
    private static void Damaged(string what, IGH_Goo goo, Type type, Func<IGH_Goo, string> textOf)
    {
        string expected = GpcDraftCapture.WithoutGuids(textOf(goo));
        var items = Written(goo, out var archive);
        // The drafts and the results of a model: C1's (P30-01, gpc-review-fixes), not repeated.
        var arrays = ((GH_Chunk)archive.GetRootNode.FindChunk("datum")).Items.Where(i => i.Type == GH_IO.Types.GH_Types.gh_bytearray).Select(i => i.Name)
            .Where(n => n != BridGHe.Common.Gpc.GpcModelDrafts.Chunk && n != BridGHe.Common.Gpc.GpcResultsFile.Chunk).ToList();
        foreach (var name in arrays)
            foreach (var truncate in new[] { false, true })
            {
                Written(goo, out var broken);
                var chunk = (GH_Chunk)broken.GetRootNode.FindChunk("datum");
                var bytes = chunk.GetByteArray(name);
                chunk.RemoveItem(name);
                if (truncate) chunk.SetByteArray(name, bytes.Take(bytes.Length / 2).ToArray());
                var read = (IGH_Goo)Activator.CreateInstance(type);
                bool ok;
                string label = $"{what}: {name} {(truncate ? "truncated" : "missing")}";
                // Task 39 (T12, P2D): only the refusals of the readers count as a refused damaged datum; any other exception (a null reference, an
                // assembly that does not load...) is a defect and fails the run, also when the GH type caught it and reported it as an internal error.
                try { ok = broken.ExtractObject(read, "datum"); } catch (Exception x) when (Refusal(x)) { ok = false; } catch (Exception x) { unexpected.Add(label + ": " + Inner(x)); ok = false; }
                if (broken.Messages != null) foreach (var m in broken.Messages) if (m.Message.Contains("errore interno di BridGHe", StringComparison.Ordinal)) unexpected.Add(label + ": " + m.Message);
                object datum = null; string now = null;
                if (ok && read.IsValid)
                {
                    try { datum = read.GetType().GetProperty("Gpc")?.GetValue(read); now = datum == null ? null : GpcDraftCapture.WithoutGuids(textOf(read)); }
                    catch (Exception x) when (Refusal(x)) { datum = null; }
                    catch (Exception x) { unexpected.Add(label + ", reading the datum: " + Inner(x)); datum = null; }
                }
                if (!ok || !read.IsValid || datum == null) damagedRefused++;
                else if (now == expected) damagedSame++;
                else lost.Add(label + ": read as another valid datum");
                checks++;
            }
    }

    /// <summary>A producer component solved with the switches of P2 on (its outputs by their GPC data alone); the first item of an output.</summary>
    private static IGH_Goo Produced(string type, int output, params (int Input, object Value)[] inputs)
    {
        using var doc = new Grasshopper.Kernel.GH_Document();
        var c = BridgeSegmentExcelTests.Add(doc, type);
        foreach (var (k, v) in inputs)
        {
            var p = c.Params.Input[k];
            var persistent = p.GetType().GetProperty("PersistentData").GetValue(p);
            persistent.GetType().GetMethod("Clear").Invoke(persistent, null);
            var append = persistent.GetType().GetMethods().First(m => m.Name == "Append" && m.GetParameters().Length == 2 && m.GetParameters()[1].ParameterType == typeof(Grasshopper.Kernel.Data.GH_Path));
            foreach (var x in v is object[] list ? list : new[] { v })
                append.Invoke(persistent, new object[] { x is IGH_Goo goo ? goo : x is Brep brep ? new GH_Brep(brep) : x is double d ? new GH_Number(d) : x is int n ? new GH_Integer(n) : x is bool b ? new GH_Boolean(b) : x is string s ? new GH_String(s) : Grasshopper.Kernel.GH_Convert.ToGoo(x), new Grasshopper.Kernel.Data.GH_Path(0) });
        }
        using (R2aDesignFixtures.WithoutFem()) BridgeSegmentExcelTests.Solve(doc, true);
        var item = c.Params.Output[output].VolatileData.AllData(true).FirstOrDefault();
        Require(item != null, type + ": no output by the GPC data alone (" + string.Join(" / ", c.RuntimeMessages(Grasshopper.Kernel.GH_RuntimeMessageLevel.Error)) + ")");
        return item.Duplicate();
    }

    /// <summary>R1: the data of the GPC examples 01–10 (R0), the extended records and composite properties of the producers, the in-memory data:
    /// every GH type made from its datum alone, no FEM value anywhere.</summary>
    private static (int Elements, int Data, int Models, int Extended) WithoutFem()
    {
        Type node = T("Fem.GH_Node"), beam = T("Fem.GH_Beam"), plate = T("Fem.GH_Plate"), model = T("Fem.GH_Model");
        Type material = T("Fem.GH_Material"), beamProperty = T("Fem.GH_BeamProperty"), plateProperty = T("Fem.GH_PlateProperty"), section = T("Fem.GH_BeamSection");
        Type loadCase = T("Fem.GH_LoadCase"), combination = T("Fem.GH_LoadCombination"), stage = T("Fem.GH_StagedConstruction"), group = T("Fem.GH_Group");
        object Gpc(object goo) => Prop(goo, "Gpc");
        int elements = 0, data = 0, models = 0, extended = 0;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        void Data(string what, Type type, object datum, params object[] more)
        {
            if (datum == null || !seen.Add(type.Name + "\n" + TextOf(datum))) return;
            var only = (IGH_Goo)FromGpc(type, new[] { datum }.Concat(more).ToArray());
            Contract(what, only, type, Gpc, type != group);
            data++;
            // R65-2: the public copy constructor of a group, case or stage keeps the GPC datum of a datum alone.
            if (type == group || type == loadCase || type == stage)
            {
                var copy = (IGH_Goo)Activator.CreateInstance(type, only);
                Require(copy.IsValid && Prop(copy, "Value") == null && TextOf(Gpc(copy)) == TextOf(Gpc(only)), what + ": the copy constructor keeps the datum alone (R65-2)");
                checks++; copied++;
            }
        }
        static Point3d P(GPC.Geometry.Point3d p) => new Point3d(p.X, p.Y, p.Z);
        foreach (var example in GpcNativeExamples.Create())
        {
            string id = example.Id; var inputs = example.Inputs; seen.Clear();
            foreach (var d in inputs.Nodes)
            {
                var guid = d.Node.Guid; var g = (IGH_GeometricGoo)FromGpc(node, d, guid);
                var read = (IGH_GeometricGoo)Contract($"{id} node {d.Node.Name}", g, node, Gpc);
                var at = P(d.Node.Position); var box = new BoundingBox(at, at);
                Box($"{id} node {d.Node.Name}", box, g.Boundingbox); Box($"{id} node {d.Node.Name} read", box, read.Boundingbox);
                Require(g.CastTo(out GH_Point p) && p.Value.DistanceTo(at) <= 1e-9 * Math.Max(1, at.DistanceTo(Point3d.Origin)), id + ": node cast to its position");
                Require((Guid)Prop(g, "Guid") == guid, id + ": node GUID, the one given"); checks += 2; elements++;
            }
            foreach (var d in inputs.Beams)
            {
                var guid = d.Beam.Guid; var g = (IGH_GeometricGoo)FromGpc(beam, d, guid);
                Contract($"{id} beam {d.Beam.Name}", g, beam, Gpc);
                Point3d a = P(d.Beam.StartPoint), b = P(d.Beam.EndPoint); var axis = new BoundingBox(new[] { a, b });
                Box($"{id} beam {d.Beam.Name}", axis, g.Boundingbox);
                Require(g.CastTo(out GH_Curve c) && c.Value.PointAtStart.DistanceTo(a) <= 1e-9 * Math.Max(1, axis.Diagonal.Length) && c.Value.PointAtEnd.DistanceTo(b) <= 1e-9 * Math.Max(1, axis.Diagonal.Length),
                    id + ": beam cast to its axis");
                Require((Guid)Prop(g, "Guid") == guid, id + ": beam GUID, the one given"); checks += 2; elements++;
            }
            foreach (var d in inputs.Plates)
            {
                var g = (IGH_GeometricGoo)FromGpc(plate, d, d.Plate.Guid);
                Contract($"{id} plate {d.Plate.Name}", g, plate, Gpc, false); // a GH plate is not persisted (as before)
                Box($"{id} plate {d.Plate.Name}", new BoundingBox(d.Corners.Select(P)), g.Boundingbox);
                Require(g.CastTo(out GH_Curve outline) && outline.Value.IsClosed, id + ": plate cast to its outline"); checks++;
                // R65-1 without FEM: the plate translated is the plate on the translated corners; morphed, its vertices are the morphed vertices.
                Moved($"{id} plate {d.Plate.Name}", d.Corners.Select(c => (c.X, c.Y, c.Z)).ToArray(), g);
                Moved($"{id} triangle on plate {d.Plate.Name}", d.Corners.Take(3).Select(c => (c.X, c.Y, c.Z)).ToArray(),
                    (IGH_GeometricGoo)FromGpc(plate, BridGHe.Common.Gpc.Builders.GpcPlateBuilders.Create(d.Corners.Take(3).Select(c => (c.X, c.Y, c.Z)).ToArray(), Patch(), null, 0, 0, "1t",
                        BridGHe.Common.Gpc.GpcIdentity.Derive(BridGHe.Common.Gpc.GpcIdentity.NameSpace, "r66-r1-null:" + id + ":triangle")), null));
                Data($"{id} plate property", plateProperty, Patch());
                elements++;
            }
            if (inputs.Beams.Count > 0)
            {
                var property = GpcNativeExamples.Property();
                Data($"{id} beam property {property.Property.Name}", beamProperty, property);
                Data($"{id} section of {property.Property.Name}", section, property.Data.Section);
                Data($"{id} material of {property.Property.Name}", material, GpcNativeExamples.Steel());
            }
            // The groups of the examples (R0: «Permanent» for every element, «Added» for the beam of 08 and 09) and of their stages.
            foreach (var gr in new[] { "Permanent", "Added" }.Where(n => n == "Permanent" || inputs.Stages.Any(s => s.Groups.Contains(n)))
                .Select(n => BridGHe.Common.Gpc.Builders.GpcCaseBuilders.Group(n, null)).Concat(inputs.Stages.SelectMany(s => s.Groups.Select(x => (IReadOnlyList<string>)new[] { x }))))
                Data($"{id} group {string.Join("\\", gr)}", group, gr);
            foreach (var c in inputs.Combinations.Concat(inputs.Stages.SelectMany(s => s.Increments)))
            {
                foreach (var (lc, _) in c.Factors) Data($"{id} case {lc.Name}", loadCase, lc);
                Data($"{id} combination {c.Name}", combination, c);
            }
            foreach (var s in inputs.Stages) Data($"{id} stage {s.Name}", stage, s);
            foreach (var lc in example.Input.Model.LoadCases.Keys) Data($"{id} case {lc}", loadCase, BridGHe.Common.Gpc.Builders.GpcCaseBuilders.Case(lc, false));

            // The model: its GPC input alone owns the state.
            var input = example.Input;
            var gpcModel = (IGH_Goo)FromGpc(model, input);
            var readModel = Contract($"{id} model", gpcModel, model, m => Prop(m, "GpcInput"));
            Require(ReferenceEquals(Prop(gpcModel, "Gpc"), Prop(input, "Model")) && Prop(gpcModel, "GpcBridgeData") != null, id + ": the GPC model and bridge data of the input");
            Require(Prop(readModel, "GpcInput") != null && (bool)Prop(readModel, "HasAttachedInput"), id + ": the model read back owns its input");
            var analysed = GPC.Model.Core.ModelValues.Copy((GPC.Model.Models.Model)Prop(input, "Model"));
            model.GetMethod("AttachGpc").Invoke(gpcModel, new object[] { analysed });
            Require((bool)Prop(gpcModel, "HasAnalysedGpc") && ReferenceEquals(Prop(gpcModel, "Gpc"), analysed) && ReferenceEquals(Prop(gpcModel, "GpcInput"), input), id + ": the analysed model belongs to the GPC input");
            var copy = gpcModel.Duplicate();
            Require((bool)Prop(copy, "HasAnalysedGpc") && ReferenceEquals(Prop(copy, "Gpc"), analysed) && ReferenceEquals(Prop(copy, "GpcInput"), input), id + ": a copy shares the state");
            Require(((Grasshopper.Kernel.IGH_PreviewData)gpcModel).ClippingBox.IsValid == (Prop(input, "Drafts") != null), id + ": the preview of the drafts (none without drafts)");
            checks += 5; models++;
        }

        // The extended records of L2C and the composite properties, as the producers give them on the path of P2: copies and saves keep the
        // datum with its extras (the material kind of a beam property, the bending thickness of a membrane).
        string Extended(IGH_Goo g) => TextOf(Gpc(g)) + GpcDraftArchiveChecks.Extras(Gpc(g))
            + (g.GetType().GetProperty("GpcMembraneBending")?.GetValue(g) is double bending && bending != 0 ? "\nmembrane bending " + bending.ToString("R", System.Globalization.CultureInfo.InvariantCulture) : "");
        void Ext(string what, IGH_Goo goo)
        {
            Require(goo.ScriptVariable() == null, what + ": the producer gives it by its GPC datum alone");
            Contract("extended: " + what, goo, goo.GetType(), Gpc, true, Extended); extended++;
        }
        var dims = new (int, object)[] { (0, 300.0), (1, 250.0), (2, 800.0), (3, 20.0), (4, 16.0), (5, 12.0) };
        var steel = Produced("MaterialSteelComponent", 0, (0, "S355"), (1, 2));
        var concrete = Produced("MaterialConcreteComponent", 0, (0, "C35/45"), (1, 7));
        var rebar = Produced("MaterialRebarComponent", 0, (0, "B450C"));
        var filleted = Produced("BeamSectionIComponent", 0, dims.Append((6, (object)15.0)).ToArray());
        Ext("I section with fillet radius 15 (R)", filleted);
        Ext("Generic section with section moduli (W11/W22) and offsets", Produced("BeamSectionGenericComponent", 0, (0, 9000.0), (1, 1.2e8), (2, 4e7), (3, 5e5), (6, 4000.0), (7, 3000.0),
            (8, 8e5), (9, 4e5), (10, 20.0), (11, 10.0), (12, 0.1)));
        Ext("Brep section with a hole (shapes)", Produced("BeamSectionBrepComponent", 0, (0, Brep.CreatePlanarBreps(new[] { new Rectangle3d(Plane.WorldXY, 200, 400).ToNurbsCurve(),
            new Rectangle3d(new Plane(new Point3d(50, 50, 0), Vector3d.ZAxis), 100, 300).ToNurbsCurve() }, 1e-6).Single())));
        var doubleC = Produced("BeamSectionCComponent", 0, (0, 90.0), (1, 12.0), (2, 260.0), (3, 9.0), (4, true), (5, 10.0));
        Ext("double channel (mirror and gap 10)", doubleC);
        Ext("double angle (mirror and gap 12)", Produced("BeamSectionAngleComponent", 0, (0, 100.0), (1, 10.0), (2, 150.0), (3, 8.0), (4, true), (5, 12.0)));
        var plain = Produced("BeamSectionIComponent", 0, dims);
        Ext("beam property of a steel (material kind Steel)", Produced("BeamPropertyComponent", 0, (0, "Girder"), (1, 5), (2, plain), (3, steel)));
        Ext("beam property of a fillet I section", Produced("BeamPropertyComponent", 0, (0, "Girder R"), (1, 5), (2, filleted), (3, steel)));
        Ext("beam property of a double channel", Produced("BeamPropertyComponent", 0, (0, "2C"), (1, 5), (2, doubleC), (3, steel)));
        Ext("membrane plate property, bending thickness 100 of 250", Produced("PlatePropertyComponent", 0, (0, "Deck"), (1, 4), (2, 250.0), (3, 100.0), (4, concrete)));
        Ext("concrete C35/45", concrete); Ext("rebar B450C", rebar);
        // The composite properties (Composite Beam Property: the composite definition and the four homogenised beam properties).
        var slab = Produced("BeamSectionConcreteSlabComponent", 0, (0, 250.0), (1, 2500.0), (2, 1000.0), (3, 50.0), (4, 800.0), (5, 50.0));
        for (int output = 0; output <= 4; output++)
        {
            var p = Produced("BeamPropertyCompositeComponent", output, (0, "CB"), (1, slab), (2, plain), (3, concrete), (4, steel), (5, rebar));
            Ext("Composite Beam Property, output " + output, p);
        }

        // In memory (R65): a stage, a group with a parent, a case, built on GPC.
        var deck = new[] { "Deck" };
        var r65 = BridGHe.Common.Gpc.Builders.GpcCaseBuilders.Case("R65", false);
        Data("in memory: stage", stage, BridGHe.Common.Gpc.Builders.GpcCaseBuilders.Stage("Stage R65", new IReadOnlyList<string>[] { deck },
            new[] { BridGHe.Common.Gpc.Builders.GpcCaseBuilders.Combination("ULS R65", new[] { r65 }, new[] { 1.35 }) }, false, true));
        Data("in memory: group with a parent", group, (IReadOnlyList<string>)new[] { "Deck", "Pour1" });
        Data("in memory: case", loadCase, BridGHe.Common.Gpc.Builders.GpcCaseBuilders.Case("R65 case", false));
        Require(moved >= 2 && copied >= 3, $"too few R65 cases without FEM: {moved} plates moved, {copied} copies");
        return (elements, data, models, extended);
    }


    /// <summary>The plate property of the example 03 built on GPC (Shell Thin «Shell patch», 10 mm, the steel of the examples), as R0 builds it.</summary>
    private static GPC.Model.ElementProperties.PlateProperty Patch() => BridGHe.Common.Gpc.Builders.GpcPropertyBuilders.Plate("Shell patch", 0, GpcNativeExamples.Steel(), 10, 10);

    /// <summary>R1 (R65-1 without FEM): a plate given by its draft alone translated is the solid of the plate on the translated corners; morphed (it
    /// bends and shears), its solid has the morphed vertices of its solid, in order.</summary>
    private static void Moved(string what, (double X, double Y, double Z)[] corners, IGH_GeometricGoo gpc)
    {
        var move = Transform.Translation(120, -45, 30);
        var shifted = corners.Select(c => (c.X + 120, c.Y - 45, c.Z + 30)).ToArray();
        var plate = T("Fem.GH_Plate");
        var draft = (BridGHe.Common.Gpc.Legacy.LegacyPlateDraft)Prop(gpc, "Gpc");
        var target = (IGH_GeometricGoo)FromGpc(plate, BridGHe.Common.Gpc.Builders.GpcPlateBuilders.Create(shifted, Patch(), null, 0, 0, draft.Plate.Name,
            BridGHe.Common.Gpc.GpcIdentity.Derive(BridGHe.Common.Gpc.GpcIdentity.NameSpace, "r66-r1-null:moved:" + what)), null);
        Require(gpc.Transform(move) is GH_Brep f && target.Transform(Transform.Identity) is GH_Brep g && SameBrep(f.Value, g.Value), what + ": translated, the solid of the plate on the translated corners (R65-1)");
        var bend = new Bend();
        Require(gpc.Transform(Transform.Identity) is GH_Brep solid && gpc.Morph(bend) is GH_Brep morphed && morphed.Value.Faces.Count == solid.Value.Faces.Count
            && morphed.Value.Vertices.Select(v => v.Location).SequenceEqual(solid.Value.Vertices.Select(v => bend.MorphPoint(v.Location))), what + ": morphed, the morphed vertices of its solid (R65-1)");
        checks += 2; moved++;
    }

    /// <summary>R1: the coordinate systems of the loads and the bridge rules (their GPC data alone; R65-C2-01: the rule of a FEM system, transitional).</summary>
    private static void Rules()
    {
        var system = T("Fem.GH_CoordinateSystem");
        var global = (IGH_Goo)Activator.CreateInstance(system);
        Contract("in memory: global coordinate system", (IGH_Goo)FromGpc(system, Prop(global, "Gpc")), system, g => Prop(g, "Gpc"), false);
        var globalSystem = plugin.GetType("BridGHe.Grasshopper.Helpers.GpcBeamInputs", true).GetMethod("GlobalSystem", BindingFlags.NonPublic | BindingFlags.Static);
        Require((bool?)globalSystem.Invoke(null, new object[] { FromGpc(system, Prop(global, "Gpc")) }) == true, "the global system by its frame alone: global"); checks++;
        // P2CA (13/11/2026, U3): the three checks of R65-C2-01 on systems with a FEM value are retired by name (P2rRetired.Checks); the frame rule
        // on the points is checked on GPC in p2d (GpcLoadFrames.IsGlobal of (2, 0, 0) and (0, 3, 0)), the global frame alone just above.
        int rules = 0;
        var designType = plugin.GetTypes().Single(t => t.Name == "BridgeDesignGoo");
        var gpcProperty = R2aDesignFixtures.RectangleProperty("L2B"); // R1: the GPC twin of BridgeAssignmentTests.Property (R2a)
        string RuleText(IGH_Goo g) => Prop(g, "Value") switch
        {
            BridGHe.Common.Geometry.BridgeSectionRule { GpcProperty: { } p } => "rule " + TextOf(p), // P2CA: by the GPC property (the FEM member is not named)
            BridGHe.Common.Geometry.BridgeFamilyProperty { GpcProperty: { } p } => "family " + TextOf(p),
            _ => null,
        };
        foreach (var value in new object[] { new BridGHe.Common.Geometry.BridgeSectionRule { Start = 0, End = 1000, GpcProperty = gpcProperty },
            new BridGHe.Common.Geometry.BridgeFamilyProperty(0) { GpcProperty = gpcProperty } })
        {
            var goo = (IGH_Goo)Activator.CreateInstance(designType, value);
            Require(goo.IsValid && RuleText(goo) != null, "bridge rule by its GPC property alone: valid");
            Require(RuleText(goo.Duplicate()) == RuleText(goo), "bridge rule by its GPC property alone: a copy keeps the datum");
            Written(goo, out var archive);
            var read = (IGH_Goo)Activator.CreateInstance(designType);
            Require(archive.ExtractObject(read, "datum") && read.IsValid && RuleText(read) is { } text && GpcDraftCapture.WithoutGuids(text) == GpcDraftCapture.WithoutGuids(RuleText(goo)),
                "bridge rule by its GPC property alone: read back with the datum, without FEM property");
            checks += 3; rules++;
            // R65-C2-02 (review of lane 2, 8/11): a rule without its FEM property and with a GPC chunk missing or truncated is not read (before:
            // valid with no property, or the partial rule kept after the exception).
            foreach (var (label, change) in new (string, Action<GH_Chunk>)[] {
                ("missing GPC records", c => c.RemoveItem("DesignGpcRecords")),
                ("missing GPC data", c => c.RemoveItem("DesignGpcData")),
                ("truncated GPC records", c => { var b = c.GetByteArray("DesignGpcRecords"); c.RemoveItem("DesignGpcRecords"); c.SetByteArray("DesignGpcRecords", b.Take(b.Length / 2).ToArray()); }),
                ("truncated GPC data", c => { var b = c.GetByteArray("DesignGpcData"); c.RemoveItem("DesignGpcData"); c.SetByteArray("DesignGpcData", b.Take(b.Length / 2).ToArray()); }) }) // R1: and the data truncated
            {
                Written(goo, out var broken);
                change((GH_Chunk)broken.GetRootNode.FindChunk("datum"));
                var bad = (IGH_Goo)Activator.CreateInstance(designType);
                _ = broken.ExtractObject(bad, "datum");
                Require(!bad.IsValid && Prop(bad, "Value") == null, "bridge rule by its GPC property alone, " + label + ": not read, no value (R65-C2-02)");
                checks++;
            }
        }
        Console.WriteLine($"GPC NULL VALUES (L2B): the global coordinate system by its GPC frame alone, {rules} bridge rules by their GPC property alone copied and read back");
    }

}
