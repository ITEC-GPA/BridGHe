#if GPC_PILOT
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BridGHe.Common.Gpc;
using BridGHe.Common.Gpc.Legacy;
using BridGHe.Common.Models;
using BridGHe.Common.MovingLoads;
using GPC.Model.Models;
using Grasshopper.Kernel;

/// <summary>
/// F6 chain D: the GPC input a model transformer builds from the GPC input of its source (<see cref="GpcModelTransforms"/>) against the
/// conversion of its FEM output by the legacy reader, with the identity the transformed copy keeps (the model GUID of the source): analysis
/// fingerprint, the content fingerprint of every part (<see cref="GpcBridgeComparison.PartsDiffer"/>), and the XML document of the model
/// with only the random GUIDs of the GPC geometry values masked. Everything must be equal bit for bit.
/// </summary>
internal static class GpcInputComparison
{
    private static readonly System.Text.RegularExpressions.Regex RandomGuid = new System.Text.RegularExpressions.Regex("[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}", System.Text.RegularExpressions.RegexOptions.Compiled);

    /// <summary>The conversion of a FEM model with an explicit identity (the results are never converted: archived without them).</summary>
    internal static Model Conversion(ModelModel fem, double tolerance, Guid owner)
    {
        if (fem.Elements.Any(e => e.Results.Count != 0)) { fem = new ModelModel(fem); foreach (var e in fem.Elements) e.Results.Clear(); }
        return LegacyModelImport.Import(LegacyArchive.Read(BridGHe.Common.Helpers.ModelArchive.Write(fem)), owner, tolerance, new LegacyImportReport());
    }

    /// <summary>The XML document of a model in a canonical form (null beyond the 2,000,000 objects of the serializer): random (version 4)
    /// GUIDs masked; a reference of the DataContract graph (z:Ref) to a value (string, number) replaced by the value, object references
    /// renumbered by the order of the objects, value ids dropped: two equal graphs compare equal whether or not equal values are shared.</summary>
    internal static string Document(Model m)
    {
        string text;
        try
        {
            using var stream = new MemoryStream(); GPC.Model.Persistence.ModelArchive.SaveDocument(m, stream);
            text = RandomGuid.Replace(System.Text.Encoding.UTF8.GetString(stream.ToArray()), "v4");
        }
        catch (System.Runtime.Serialization.SerializationException e) when (e.Message.Contains("Maximum number of items")) { return null; }
        System.Xml.Linq.XNamespace z = "http://schemas.microsoft.com/2003/10/Serialization/", xsi = "http://www.w3.org/2001/XMLSchema-instance";
        var document = System.Xml.Linq.XDocument.Parse(text.TrimStart((char)0xFEFF));
        // Linear: a reference to a value without child elements (a string, a number) is replaced by the value; a reference to an object
        // keeps the reference, renumbered by the order of the objects in the document (values do not take a number).
        var elements = document.Descendants().ToArray();
        var targets = new Dictionary<string, System.Xml.Linq.XElement>(); var ordinals = new Dictionary<string, string>();
        foreach (var e in elements)
        {
            var id = e.Attribute(z + "Id"); if (id == null) continue;
            targets[id.Value] = e; if (e.HasElements) ordinals[id.Value] = "o" + ordinals.Count;
        }
        foreach (var e in elements)
        {
            var reference = e.Attribute(z + "Ref");
            if (reference != null)
            {
                var target = targets[reference.Value];
                if (target.HasElements) reference.Value = ordinals[reference.Value];
                else
                {
                    e.RemoveAttributes();
                    foreach (var a in target.Attributes().Where(a => a.Name != z + "Id" && !a.IsNamespaceDeclaration)) e.SetAttributeValue(a.Name, a.Value);
                    e.Value = target.Value;
                }
            }
            var id = e.Attribute(z + "Id");
            if (id != null) { if (ordinals.TryGetValue(id.Value, out var ordinal)) id.Value = ordinal; else id.Remove(); }
            foreach (var d in e.Attributes().Where(a => a.IsNamespaceDeclaration).ToArray()) d.Remove();
        }
        return document.ToString();
    }

    /// <summary>The owner of the element identities of a converted (or copied) model: the model revision of its element sources.</summary>
    internal static Guid ElementOwner(Model m) => Guid.Parse(m.AllElements.First().Source.ModelRevision);

    internal static string Require(string label, GpcModels.Conversion attached, ModelModel fem, double tolerance)
    {
        void Fail(string what) => throw new Exception("GPC INPUT FAIL (" + label + "): " + what);
        var model = attached.Model; var owner = ElementOwner(model);
        if (owner == model.Guid) Fail("the transformed model has the model GUID of its source");
        var expected = GPC.Model.Core.ModelValues.Copy(Conversion(fem, tolerance, owner), GPC.Model.Core.ModelCopyOptions.None, model.Guid);
        var parts = GpcBridgeComparison.PartsDiffer(model, expected);
        if (model.AnalysisFingerprint() != expected.AnalysisFingerprint()) Fail("analysis fingerprint differs: " + string.Join("; ", parts.Take(12)));
        if (parts.Count != 0) Fail("parts differ: " + string.Join("; ", parts.Take(12)) + (parts.Count > 12 ? $"; ... {parts.Count} in all" : ""));
        string a = Document(model), b = Document(expected);
        if (a != null && a != b)
        {
            string folder = Path.Combine(Path.GetTempPath(), "bridghe-gpc-input-diff"); Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, label.Replace(' ', '-') + "-gpc.xml"), a); File.WriteAllText(Path.Combine(folder, label.Replace(' ', '-') + "-conversion.xml"), b ?? "");
            var x = a.Split('\n'); var y = (b ?? "").Split('\n');
            int line = Enumerable.Range(0, Math.Min(x.Length, y.Length)).FirstOrDefault(i => x[i] != y[i], -1);
            Fail($"XML document differs, both in {folder}: " + (line < 0 ? $"{x.Length} / {y.Length} lines" : $"line {line + 1}: GPC '{x[line].Trim()}' / conversion '{y[line].Trim()}'"));
        }
        return $"{model.NodesElements.Count} nodes, {model.BeamElements.Count} beams, {model.AreaElements.Count} plates, {model.LoadCases.Count} cases, {model.MovingLoads?.Paths.Count ?? 0} moving paths, parts equal, "
            + (a == null ? "XML document beyond the serializer limit" : $"XML document equal ({a.Length} characters)");
    }

    /// <summary>Native runners: every model transformer of the definition attached its GPC input, equal to the conversion of its FEM output.</summary>
    internal static void RequireComponents(GH_Document doc, string label)
    {
        double tolerance = global::Rhino.RhinoDoc.ActiveDoc?.ModelAbsoluteTolerance ?? 0.001; // Settings.Tolerance of the plug-in
        int count = 0;
        foreach (var component in doc.Objects.OfType<GH_Component>().Where(c => c.GetType().Name is "AttachMovingPathsComponent"))
            foreach (var goo in component.Params.Output[0].VolatileData.AllData(true))
            {
                string name = label + " " + component.GetType().Name.Replace("Component", "") + " '" + component.NickName + "'";
                if (!(bool)goo.GetType().GetProperty("HasAttachedInput").GetValue(goo)) throw new Exception("GPC INPUT FAIL (" + name + "): no GPC input attached by the transformer");
                var attached = (GpcModels.Conversion)goo.GetType().GetProperty("GpcInput").GetValue(goo);
                var fem = (ModelModel)goo.GetType().GetProperty("Value").GetValue(goo);
                Console.WriteLine($"GPC INPUT PASS ({name}): " + Require(name, attached, fem, tolerance)); count++;
            }
        Console.WriteLine($"GPC INPUT ({label}): {count} transformer outputs on GPC equal to the conversion of their FEM output.");
    }

    /// <summary>Regression (not native) <c>gpc-model-transforms</c>: Attach Moving Paths on the examples 01–10 (node, beam and plate loads,
    /// combinations, moving loads, stages): a new path on a new case, a path on an existing case, two paths, no path (removal); GPC = the
    /// conversion of the FEM output bit for bit, or the same error.</summary>
    internal static void Regression()
    {
        const double tolerance = 1e-6; int compared = 0;
        MovingPath Path(string caseName, double y) => new MovingPath
        {
            LoadCaseName = caseName, Divisions = 7, Target = 0, Coordinates = new double[] { 0, y, 0, 3000, y, 0, 0, y + 1000, 0 }, ConvertToStaticCases = true,
            Template = new MovingTemplate { Name = "Axle " + caseName, Vehicles = new[] { new MovingVehicle { Name = "1000 N", Loads = new MovingObject[] { new MovingPointForce { Force = new double[] { 0, 0, -1000 } } } } } },
        };
        foreach (var example in NativeExamples.Create())
        {
            var fem = example.Model;
            string existing = fem.LoadCases.FirstOrDefault()?.Name ?? "Q";
            foreach (var (variant, paths) in new (string, MovingPath[])[] { ("new case", new[] { Path("Traffic GPC", 0) }), ("existing case", new[] { Path(existing, 100) }),
                ("two paths", new[] { Path("Lane 1", 0), Path("Lane 2", 200) }), ("no path", Array.Empty<MovingPath>()) })
            {
                string label = example.Id + " " + variant; string femError = null, gpcError = null; ModelModel output = null; GpcModels.Conversion gpc = null;
                try { output = MovingModelBuilder.ReplacePaths(fem, paths); } catch (Exception e) when (e is ArgumentException || e is InvalidOperationException) { femError = e.Message; }
                try { gpc = GpcModelTransforms.ReplacePaths(GpcModels.Convert(fem, tolerance), paths, GpcIdentity.Derive(GpcIdentity.NameSpace, "test-paths:" + label)); } catch (Exception e) when (e is ArgumentException || e is InvalidOperationException) { gpcError = e.Message; }
                if (femError != null || gpcError != null)
                {
                    if (femError != gpcError) throw new Exception($"GPC INPUT FAIL ({label}): FEM error '{femError}', GPC error '{gpcError}'");
                    Console.WriteLine($"GPC INPUT ({label}): same error: {femError}"); continue;
                }
                Console.WriteLine($"GPC INPUT ({label}): " + Require(label, gpc, output, tolerance)); compared++;
            }
        }
        if (compared < 36) throw new Exception($"GPC INPUT FAIL: only {compared} transformations compared");
        Console.WriteLine($"GPC MODEL TRANSFORMS PASS: {compared} Attach Moving Paths outputs on GPC equal to the conversion of their FEM output.");
    }
}
#endif
