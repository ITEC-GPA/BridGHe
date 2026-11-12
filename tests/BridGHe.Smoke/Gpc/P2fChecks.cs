#if GPC_PILOT
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BridGHe.Common.Gpc;
using BridGHe.Common.Gpc.Builders;
using BridGHe.Common.Gpc.Legacy;
using GH_IO.Serialization;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;

/// <summary>
/// P2F (12/11/2026): the checks of the reds of P2 moved to the GPC path before the cut of the FEM types.
/// <list type="bullet">
/// <item><see cref="GooArchives"/> (core-only, former <c>Program.CheckGooArchives</c>): the GH archive round trip of a section, a beam property
/// and a composite property given by their GPC data alone. The FEM fixture of the former check is no longer written: since P2D a GH type
/// writes its GPC datum only, and the datum of that fixture is refused; read back, a GH type has no FEM value to compare.</item>
/// <item><see cref="ModelGuids"/> (regression <c>model-guids</c>, its round-trip part): since P2D a model is read back as its GPC input
/// alone, so the GH GUIDs of its elements are checked on the GPC path (the GUIDs Build Model was given, in the order of the drafts).</item>
/// </list>
/// </summary>
internal static class P2fChecks
{
    private static IGH_Goo Goo(Assembly plugin, string type, params object[] args)
    {
        var t = plugin.GetTypes().Single(x => x.Name == type);
        var method = t.GetMethods(BindingFlags.Public | BindingFlags.Static).Single(m => m.Name == "FromGpc");
        int n = method.GetParameters().Length;
        return (IGH_Goo)method.Invoke(null, args.Concat(Enumerable.Repeat((object)null, n - args.Length)).ToArray());
    }

    private static object Datum(object goo) => goo?.GetType().GetProperty("Gpc")?.GetValue(goo);
    private static bool HasFem(object goo) => goo?.GetType().GetProperty("Value")?.GetValue(goo) != null;

    /// <summary>The binary GH archive round trip of a goo (write, serialize, deserialize, read into a new instance); null when the read fails.</summary>
    private static IGH_Goo RoundTrip(IGH_Goo goo, out bool written)
    {
        var chunk = new GH_LooseChunk("Test");
        written = goo.Write(chunk);
        if (!written) return null;
        var loaded = new GH_LooseChunk("Test"); loaded.Deserialize_Binary(chunk.Serialize_Binary());
        var copy = (IGH_Goo)Activator.CreateInstance(goo.GetType());
        return copy.Read(loaded) ? copy : null;
    }

    /// <summary>
    /// Core-only (P2F, item 3): a section with native metadata and explicit values, a beam property on it and a composite property with its
    /// modular ratios and flags, each given by its GPC datum alone, written to a GH archive and read back: the same canonical text (the texts
    /// of the R6.0 capture), no FEM value, and the values the former check named (native key and dimensions, moments, nested section, ratios
    /// and flags). Nine checks, as the former one.
    /// </summary>
    internal static void GooArchives(Assembly plugin, Action<bool, string> assert)
    {
        var native = new LegacySectionRecipe
        {
            Kind = BridgeSectionKind.Generic, Name = "Native metadata", Area = 100, I11 = 234, I22 = 123, J = 50, Angle = 0.25,
            NativeKey = "TriangleSolid", NativeDimensions = new[] { 20.0, 30.0 },
        };
        var steel = GpcMaterialBuilders.Material(GpcMaterialBuilders.Steel("S355", 2));
        var property = GpcPropertyBuilders.Beam("P", 5, native, steel);
        var composite = GpcCompositeDefinitions.Create(GpcCompositeDefinitions.Grillage(new BridgeGrillageExample.Parameters()) with
        {
            Name = "C", NLongTerm = 18, NShortTerm = 6, NShrinkage = 18, ConsiderConcrete = true, ConsiderRebar = true, TopArea = 1000, BottomArea = 800,
        });
        foreach (var (type, datum) in new (string, object)[] { ("GH_BeamSection", native), ("GH_BeamProperty", property), ("GH_CompositeBeamProperty", composite) })
        {
            var goo = Goo(plugin, type, datum);
            var copy = RoundTrip(goo, out bool written);
            assert(written, "GH archive write " + type + " (GPC datum alone)");
            assert(copy != null && !HasFem(copy) && Datum(copy) != null, "GH archive read " + type + " (GPC datum, no FEM value)");
            var back = Datum(copy);
            bool same = GpcDraftCapture.Text(back) == GpcDraftCapture.Text(datum);
            string why = same ? "" : " | " + GpcDraftArchiveChecks.FirstDifference(GpcDraftCapture.Text(datum), GpcDraftCapture.Text(back)).Replace('\n', ' ');
            switch (back)
            {
                case LegacySectionRecipe s:
                    assert(same && s.NativeKey == "TriangleSolid" && s.NativeDimensions.SequenceEqual(new[] { 20.0, 30.0 }) && s.Area == 100 && s.I11 == 234 && s.I22 == 123 && s.Angle == 0.25,
                        "GH native section and moments preserved (GPC recipe: the same text)" + why); break;
                case LegacyBeamProperty b:
                    // The property keeps as its recipe the values computed from the native section (GpcPropertyBuilders.PropertySection, the
                    // former StrausSection.Calculate); the text holds the recipe, the GPC property and its Straus7 definition, if any.
                    assert(same && b.Data.Section != null && b.Data.Section.Area > 0 && b.Data.Section.I11 > 0, "GH nested beam section preserved (GPC beam property: the same text, its section recipe)" + why); break;
                case GPC.Checkers.CompositeBridge.BridgeCompositeSectionDefinition c:
                    assert(same && copy.IsValid && c.NLongTerm == 18 && c.NShortTerm == 6 && c.ConsiderConcrete && c.ConsiderRebar,
                        "GH composite validity, modular ratios and flags preserved (GPC definition: the same text)" + why); break;
                default: assert(false, "GH archive " + type + ": the datum read back is a " + back?.GetType().Name); break;
            }
        }
    }

    /// <summary>
    /// Regression <c>model-guids</c> (P2F, item 7), the part after the FEM map (P2D reads a model back as its GPC input alone, no FEM value): a
    /// model as Build Model gives it on GPC (the cantilever of <see cref="GpcFixtures"/>, GH GUIDs of its own for every element) keeps those GUIDs
    /// in a copy and through a binary GH archive round trip, in the order of the drafts; a 0.3.0 payload (<paramref name="legacy"/>, the former
    /// value of two nodes) reads back with a GUID for each element.
    /// </summary>
    internal static void ModelGuids(Assembly plugin, Action<bool, string> require, byte[] legacy)
    {
        var (nodes, beams) = GpcFixtures.CantileverInputs("p2f model guids");
        var ghNodes = nodes.Select(_ => Guid.NewGuid()).ToArray(); var ghBeams = beams.Select(_ => Guid.NewGuid()).ToArray();
        var counts = new LegacyBuildModel.BuildCounts();
        var input = LegacyBuildModel.Build("p2f model guids", new BridgeUnits(DefinitionUnits.Nmm), 0.001, nodes.Select((d, i) => (ghNodes[i], d)).ToList(),
            beams.Select((d, i) => (ghBeams[i], d)).ToList(), new List<(Guid, LegacyPlateDraft)>(), new List<LegacyCombinationDraft>(), new List<LegacyStageDraft>(), counts, gpcOnly: true);
        var type = plugin.GetType("BridGHe.Grasshopper.DataTypes.Fem.GH_Model", true);
        var goo = (IGH_Goo)type.GetMethod("FromGpc").Invoke(null, new object[] { input });
        type.GetMethod("AttachElementGuids").Invoke(goo, new object[] { counts.ElementGuids });
        Guid[] Of(object m) => (Guid[])type.GetMethod("ElementGuidsOf").Invoke(m, new[] { type.GetProperty("GpcDrafts").GetValue(m) });
        var given = Of(goo);
        require(given.Length == nodes.Count + beams.Count && ghNodes.Concat(ghBeams).All(given.Contains), "a GPC model keeps the GH GUIDs Build Model was given, one per element");
        require(Of(goo.Duplicate()).SequenceEqual(given), "result models duplicate with complete GUID maps (GPC model)");
        var copy = RoundTrip(goo, out bool written);
        require(written, "write model and GUID map (GPC model)");
        require(copy != null && !HasFem(copy) && Of(copy).SequenceEqual(given), "GUIDs survive GH archive round trip (GPC model, in the order of the drafts)");
        var chunk = new GH_LooseChunk("legacy"); chunk.SetByteArray("Data", legacy);
        var former = (IGH_Goo)Activator.CreateInstance(type);
        bool read = former.Read(chunk);
        var guids = read ? Of(former) : Array.Empty<Guid>();
        require(read && guids.Length == 2 && guids.Distinct().Count() == 2 && !guids.Contains(Guid.Empty), "legacy archives regenerate complete maps (GPC model read by value)");
    }
}
#endif
