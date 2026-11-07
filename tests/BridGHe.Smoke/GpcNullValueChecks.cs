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
        BridGHe.Common.Gpc.GpcModels.Conversion c => GpcDraftCapture.ConversionText(c),
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

    internal static void Regression()
    {
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
            Common(what, fem, type, Gpc, (IGH_Goo)FromGpc(type, Gpc(fem)), type != group);
            data++;
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
        Console.WriteLine($"GPC NULL VALUES PASS: {elements} elements, {data} properties, materials, sections, groups, cases, combinations and stages, {models} models " +
            $"of the examples 01-10 given by their GPC datum alone ({checks} checks: validity, datum, bounding box and casts, text, copies, persistence without FemmR6, state of the model)");
    }
}
#endif
