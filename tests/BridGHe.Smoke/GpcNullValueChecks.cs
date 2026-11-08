#if GPC_PILOT
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BridGHe.Common.Models;
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
        Require(fem.ContainsKey("FemmR6") && !gpc.ContainsKey("FemmR6"), what + ": FemmR6 only with the FEM value");
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

    internal static void Regression()
    {
        moved = 0; copied = 0;
        plugin = BridgeMergeTests.Initialize();
        Type node = T("Fem.GH_Node"), beam = T("Fem.GH_Beam"), plate = T("Fem.GH_Plate"), model = T("Fem.GH_Model");
        Type material = T("Fem.GH_Material"), beamProperty = T("Fem.GH_BeamProperty"), plateProperty = T("Fem.GH_PlateProperty"), section = T("Fem.GH_BeamSection");
        Type loadCase = T("Fem.GH_LoadCase"), combination = T("Fem.GH_LoadCombination"), stage = T("Fem.GH_StagedConstruction"), group = T("Fem.GH_Group");
        object Gpc(object goo) => Prop(goo, "Gpc");
        int elements = 0, data = 0, models = 0;
        var seen = new HashSet<object>(ReferenceEqualityComparer.Instance);
        // The FEM-backed type as the components make it: the constructor from the FEM value (optional parameters at their defaults), else Value.
        IGH_Goo Make(Type type, object value)
        {
            var ctor = type.GetConstructors().Where(c => c.GetParameters().Length >= 1 && c.GetParameters()[0].ParameterType.IsInstanceOfType(value)
                && c.GetParameters().Skip(1).All(p => p.HasDefaultValue)).OrderBy(c => c.GetParameters().Length).FirstOrDefault();
            if (ctor != null) return (IGH_Goo)ctor.Invoke(new[] { value }.Concat(ctor.GetParameters().Skip(1).Select(p => p.DefaultValue)).ToArray());
            var goo = (IGH_Goo)Activator.CreateInstance(type); type.GetProperty("Value").SetValue(goo, value); return goo;
        }
        void Data(string what, Type type, object value)
        {
            if (value == null || !seen.Add(value)) return;
            var fem = Make(type, value);
            var gpcOnly = (IGH_Goo)FromGpc(type, Gpc(fem));
            // L2C: a plate property given by its datum alone with the bending thickness of a membrane beside it (written in its records).
            if (type.GetProperty("GpcMembraneBending") is { CanWrite: true } bending) bending.SetValue(gpcOnly, bending.GetValue(fem));
            Common(what, fem, type, Gpc, gpcOnly, type != group);
            data++;
            // P2Z (revisione ChatGPT R65-2): the public copy constructor of a group, case or stage keeps the GPC datum, of a datum alone too.
            if (type == group || type == loadCase || type == stage)
            {
                var only = (IGH_Goo)FromGpc(type, Gpc(fem));
                var copy = (IGH_Goo)Activator.CreateInstance(type, only);
                Require(copy.IsValid && Prop(copy, "Value") == null && TextOf(Gpc(copy)) == TextOf(Gpc(only)), what + ": the copy constructor keeps the datum alone (R65-2)");
                Require(TextOf(Gpc(Activator.CreateInstance(type, fem))) == TextOf(Gpc(fem)), what + ": the copy constructor of the FEM-backed type, the same datum (R65-2)");
                checks += 2; copied++;
            }
        }
        foreach (var example in NativeExamples.Create())
        {
            var fem = example.Model; string id = example.Id;
            foreach (var e in fem.Elements)
            {
                switch (e)
                {
                    case NodeModel n:
                    {
                        var f = (IGH_GeometricGoo)Make(node, n); var g = (IGH_GeometricGoo)FromGpc(node, Gpc(f), Prop(f, "Guid"));
                        var read = (IGH_GeometricGoo)Common($"{id} node {n.NodeId}", f, node, Gpc, g);
                        Box($"{id} node {n.NodeId}", f.Boundingbox, g.Boundingbox); Box($"{id} node {n.NodeId} read", f.Boundingbox, read.Boundingbox);
                        Require(g.CastTo(out GH_Point p) && p.Value.DistanceTo(n.Position) <= 1e-9 * Math.Max(1, n.Position.DistanceTo(Point3d.Origin)), id + ": node cast to a point");
                        Require((Guid)Prop(g, "Guid") == (Guid)Prop(f, "Guid"), id + ": node GUID");
                        break;
                    }
                    case BeamModel b:
                    {
                        var f = (IGH_GeometricGoo)Make(beam, b); var g = (IGH_GeometricGoo)FromGpc(beam, Gpc(f), Prop(f, "Guid"));
                        Common($"{id} beam {b.BeamId}", f, beam, Gpc, g);
                        var axis = new BoundingBox(new[] { b.PointFrom, b.PointTo }); Box($"{id} beam {b.BeamId}", axis, g.Boundingbox);
                        Require(g.CastTo(out GH_Curve c) && c.Value.PointAtStart.DistanceTo(b.PointFrom) <= 1e-9 * Math.Max(1, axis.Diagonal.Length)
                            && c.Value.PointAtEnd.DistanceTo(b.PointTo) <= 1e-9 * Math.Max(1, axis.Diagonal.Length), id + ": beam cast to its axis");
                        Data($"{id} beam property {b.BeamProperty?.Name}", beamProperty, b.BeamProperty);
                        Data($"{id} section of {b.BeamProperty?.Name}", section, b.BeamProperty?.Section);
                        Data($"{id} material of {b.BeamProperty?.Name}", material, b.BeamProperty?.Material);
                        foreach (var gr in b.Groups) Data($"{id} group {gr.FullName}", group, gr);
                        break;
                    }
                    case PlateModel p:
                    {
                        var f = (IGH_GeometricGoo)Make(plate, p); var g = (IGH_GeometricGoo)FromGpc(plate, Gpc(f), Prop(f, "Guid"));
                        Common($"{id} plate {p.PlateId}", f, plate, Gpc, g, false); // a GH plate is not persisted (as before)
                        Box($"{id} plate {p.PlateId}", new BoundingBox(p.Points), g.Boundingbox);
                        Require(g.CastTo(out GH_Curve outline) && outline.Value.IsClosed, id + ": plate cast to its outline");
                        Data($"{id} plate property {p.PlateProperty?.Name}", plateProperty, p.PlateProperty);
                        // P2Z (R65-1): the plate and the triangle on three of its corners, moved and morphed.
                        Moved($"{id} plate {p.PlateId}", f, g);
                        var tf = (IGH_GeometricGoo)Make(plate, new PlateModel(p.PlateId + "t", p.Points.Take(3).ToArray(), p.PlateProperty));
                        Moved($"{id} triangle on plate {p.PlateId}", tf, (IGH_GeometricGoo)FromGpc(plate, Gpc(tf), Prop(tf, "Guid")));
                        break;
                    }
                    default: continue;
                }
                elements++;
            }
            foreach (var lc in fem.Elements.SelectMany(e => e.Loads).Select(l => l.LoadCase).Where(c => c != null)) Data($"{id} case {lc.Name}", loadCase, lc);
            foreach (var c in fem.LoadCombinations) Data($"{id} combination {c.Name}", combination, c);
            foreach (var s in fem.StagedConstruction) Data($"{id} stage {s.Name}", stage, s);

            // The model: its GPC input alone owns the state.
            var femModel = (IGH_Goo)model.GetMethod("FromModel", BindingFlags.Public | BindingFlags.Static).Invoke(null, new object[] { fem, null });
            var input = Prop(femModel, "GpcInput");
            var gpcModel = (IGH_Goo)FromGpc(model, input);
            var readModel = Common($"{id} model", femModel, model, m => Prop(m, "GpcInput"), gpcModel, true, "ElementGuids");
            Require(ReferenceEquals(Prop(gpcModel, "Gpc"), Prop(input, "Model")) && Prop(gpcModel, "GpcBridgeData") != null, id + ": the GPC model and bridge data of the input");
            Require(Prop(readModel, "GpcInput") != null && (bool)Prop(readModel, "HasAttachedInput"), id + ": the model read back owns its input");
            var analysed = GPC.Model.Core.ModelValues.Copy((GPC.Model.Models.Model)Prop(input, "Model"));
            model.GetMethod("AttachGpc").Invoke(gpcModel, new object[] { analysed });
            Require((bool)Prop(gpcModel, "HasAnalysedGpc") && ReferenceEquals(Prop(gpcModel, "Gpc"), analysed) && ReferenceEquals(Prop(gpcModel, "GpcInput"), input),
                id + ": the analysed model belongs to the GPC input");
            var copy = gpcModel.Duplicate();
            Require((bool)Prop(copy, "HasAnalysedGpc") && ReferenceEquals(Prop(copy, "Gpc"), analysed) && ReferenceEquals(Prop(copy, "GpcInput"), input), id + ": a copy shares the state");
            Require(((Grasshopper.Kernel.IGH_PreviewData)gpcModel).ClippingBox.IsValid == (Prop(input, "Drafts") != null), id + ": the preview of the drafts (none without drafts)");
            checks += 5; models++;
        }
        Require(elements >= 60 && data >= 60 && models == 10, $"too few cases: {elements} elements, {data} data, {models} models");
        // P2Z (revisione ChatGPT R65-1/R65-2): the cases of the GPC-only plate geometry and of the copy constructors.
        var stageValue = new StagedConstructionModel { Name = "Stage R65", Marph = false, Rotate = true };
        stageValue.AddGroups(new[] { new GroupModel("Deck") });
        var comboValue = new LoadCombinationModel("ULS R65"); comboValue.Values.Add(new LoadCaseModel("R65", "R65"), 1.35);
        stageValue.AddLoadCombinations(new[] { comboValue });
        Data("in memory: stage", stage, stageValue);
        Data("in memory: group with a parent", group, new GroupModel("Pour1", new GroupModel("Deck")));
        Data("in memory: case", loadCase, new LoadCaseModel("R65 case", "R65 case"));
        // L2B (lane 2b): the coordinate system of the loads by its GPC frame alone (no persistence of its own, as before), and the bridge rules
        // whose beam property is given by its GPC datum alone: a copy keeps the datum, a read back gives it again (but for GUIDs), no FEM property.
        var system = T("Fem.GH_CoordinateSystem");
        var global = (IGH_Goo)Activator.CreateInstance(system);
        Common("in memory: global coordinate system", global, system, Gpc, (IGH_Goo)FromGpc(system, Gpc(global)), false);
        // R65-C2-01 (review of lane 2, 8/11): a system with a FEM value keeps the rule of the reader on its points: (2, 0, 0) and (0, 3, 0) is not
        // the global one (its normalised frame is: the rule of a system given by its frame alone, the only one in P2).
        var stretched = new CoordinateSystemModel("R65-C2-01", "Stretched") { Origin = new Point3d(0, 0, 0), Point1 = new Point3d(2, 0, 0), Point2 = new Point3d(0, 3, 0) };
        var globalSystem = plugin.GetType("BridGHe.Grasshopper.Helpers.GpcBeamInputs", true).GetMethod("GlobalSystem", BindingFlags.NonPublic | BindingFlags.Static);
        var stretchedGoo = (IGH_Goo)Activator.CreateInstance(system, stretched);
        Require((bool?)globalSystem.Invoke(null, new object[] { stretchedGoo }) == false, "a FEM system with the points (2, 0, 0) and (0, 3, 0): not global (R65-C2-01)");
        Require((bool?)globalSystem.Invoke(null, new object[] { global }) == true, "the FEM global system: global (R65-C2-01)");
        Require((bool?)globalSystem.Invoke(null, new object[] { FromGpc(system, Gpc(stretchedGoo)) }) == true, "its frame alone (normalised): global, the rule of P2 (R65-C2-01)");
        checks += 3;
        int rules = 0;
        var designType = plugin.GetTypes().Single(t => t.Name == "BridgeDesignGoo");
        var gpcProperty = BridGHe.Common.Geometry.BridgeGpcProperties.Of(BridgeAssignmentTests.Property("L2B"));
        string RuleText(IGH_Goo g) => Prop(g, "Value") switch
        {
            BridGHe.Common.Geometry.BridgeSectionRule { Property: null, GpcProperty: { } p } => "rule " + TextOf(p),
            BridGHe.Common.Geometry.BridgeFamilyProperty { Property: null, GpcProperty: { } p } => "family " + TextOf(p),
            _ => null,
        };
        foreach (var value in new object[] { new BridGHe.Common.Geometry.BridgeSectionRule { Start = 0, End = 1000, GpcProperty = gpcProperty },
            new BridGHe.Common.Geometry.BridgeFamilyProperty(0, null) { GpcProperty = gpcProperty } })
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
                ("truncated GPC records", c => { var b = c.GetByteArray("DesignGpcRecords"); c.RemoveItem("DesignGpcRecords"); c.SetByteArray("DesignGpcRecords", b.Take(b.Length / 2).ToArray()); }) })
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
        Require(moved >= 2 && copied >= 3, $"too few R65 cases: {moved} plates moved, {copied} copies");
        Console.WriteLine($"GPC NULL VALUES (R65): {moved} plates given by their drafts alone translated and morphed as the FEM plates, {copied} groups, cases and stages copied by their copy constructors");
        Console.WriteLine($"GPC NULL VALUES PASS: {elements} elements, {data} properties, materials, sections, groups, cases, combinations and stages, {models} models " +
            $"of the examples 01-10 given by their GPC datum alone ({checks} checks: validity, datum, bounding box and casts, text, copies, persistence without FemmR6, state of the model)");
    }
}
#endif
