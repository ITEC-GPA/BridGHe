using System;
using System.Collections.Generic;
using System.Linq;
using BridGHe.Common.Helpers;
using BridGHe.Grasshopper.Components.Utilities;
using BridGHe.Grasshopper.DataTypes.Fem;
using BridGHe.Grasshopper.Helpers;
using Grasshopper.Kernel;

namespace BridGHe.Grasshopper.Components;

public sealed class BeamPropertyOverrideComponent : BridgeUtilityComponent
{
    public BeamPropertyOverrideComponent() : base("Beam Property Override", "PropertyOverride",
        "Replace properties using explicit original/replacement pairs. Match the current property by exact, case-sensitive name; keep unmatched properties. Copies inputs and clears all output FEM results when any beam is matched.",
        CategoryNameConstants.SUBCATEGORY_TOOLS) { }
    public override Guid ComponentGuid => new Guid("d2a78ee9-b639-49e5-9478-15eb8da3ebd5");
    protected override void RegisterInputParams(GH_InputParamManager p)
    {
        p.AddGenericParameter("Beams", "B", "All beams to process, in output order. No property-to-beam list broadcasting.", GH_ParamAccess.list);
        p.AddGenericParameter("Original Properties", "P0", "Beam Property objects to find by their exact Name. Paired by index with Replacement Properties; may come from Get Beam Property. Names are case-sensitive.", GH_ParamAccess.list); p[1].Optional = true;
        p.AddGenericParameter("Replacement Properties", "P1", "One new Beam Property per original property. Its name may differ. Replaces the complete property, including material, density and formulation; no automatic mass correction. Both lists empty: unchanged copies.", GH_ParamAccess.list); p[2].Optional = true;
    }
    protected override void RegisterOutputParams(GH_OutputParamManager p)
    {
        p.AddGenericParameter("Beams", "B", "Copied beams, same order/IDs/geometry/loads/attributes. Unmatched properties are retained. All returned FEM results are cleared if any replacement matched.", GH_ParamAccess.list);
        p.AddBooleanParameter("Replaced", "R", "One flag per output beam: its original property name matched a pair.", GH_ParamAccess.list);
        p.AddTextParameter("Information", "I", "Replacement counts and original property names not found in the supplied beams.", GH_ParamAccess.list);
    }
    protected override void Solve(IGH_DataAccess da)
    {
        var beams = new List<GH_Beam>(); var originals = new List<GH_BeamProperty>(); var replacements = new List<GH_BeamProperty>();
        if (!da.GetDataList(0, beams)) return;
        da.GetDataList(1, originals); da.GetDataList(2, replacements);
        bool[] flags; string[] unused;
        // R6.5 P1 lane 2b: a beam given by its draft alone or a property given by its GPC datum alone gives the beams by their drafts alone
        // (rule of passo 75); also with the switch of the property producers (the path of P2).
        if (GpcOnlyElements.PropertiesWithoutFem || beams.Any(b => b?.Value == null) || GpcOnlyProperties.WithoutFem(originals) || GpcOnlyProperties.WithoutFem(replacements))
        {
            var gpc = GpcOnly(beams, originals, replacements);
            if (gpc.Beams == null) return;
            da.SetDataList(0, gpc.Beams); flags = gpc.Replaced; unused = gpc.Unused;
        }
        else
        {
            var result = BeamPropertyReplacement.Apply(beams.Select(b => b?.Value).ToArray(), originals.Select(p => p?.Value).ToArray(), replacements.Select(p => p?.Value).ToArray());
            da.SetDataList(0, result.Beams.Select((b, i) => new GH_Beam(b, beams[i].Guid)));
            flags = result.Replaced; unused = result.UnusedOriginalNames;
        }
        da.SetDataList(1, flags);
        int count = flags.Count(v => v);
        var info = new List<string> { $"{count}/{beams.Count} beams matched. Names are exact and case-sensitive. Unmatched properties are retained.",
            count > 0 ? "All returned FEM results cleared: recalculate the assembled model. Geometry, offsets, loads and attributes retained. Replacement material and density used exactly as supplied." : "No replacement matched; copied properties and results are unchanged." };
        if (unused.Length > 0)
        {
            string warning = "Original property names not found: " + string.Join(", ", unused);
            info.Add(warning); AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, warning);
        }
        da.SetDataList(2, info); Message = $"{count}/{beams.Count} replaced";
    }

    /// <summary>R6.5 P1 lane 2b: <see cref="BeamPropertyReplacement.Apply"/> on the GPC data (the same checks, order and messages): the pairs by
    /// the exact names of the GPC properties, two replacements with one name the same only when their GPC data are (the canonical text of the
    /// property: signature, formulation, angle and section recipe); every beam a new beam by its draft alone with the GH GUID of its input
    /// and no analysed element (the FEM path gives copies with their results cleared): a matched one with the replacement property (Beam
    /// Override of the property), the others a copy of their draft; the identity derived from the input draft. Null beams: refused.</summary>
    private (List<GH_Beam> Beams, bool[] Replaced, string[] Unused) GpcOnly(List<GH_Beam> beams, List<GH_BeamProperty> originals, List<GH_BeamProperty> replacements)
    {
        var drafts = beams.Select(b => b?.Gpc).ToArray();
        if (drafts.Any(d => d?.Beam?.BeamProperty == null)) throw new ArgumentException("Supply valid beams with an assigned Beam Property.");
        if (originals.Count != replacements.Count)
            throw new ArgumentException("Original Properties and Replacement Properties must have the same number of items; no list broadcasting.");
        static string Name(GH_BeamProperty p) => p.Value != null ? p.Value.Name : p.Gpc.Property.Name;
        var pairs = new Dictionary<string, Common.Gpc.Legacy.LegacyBeamProperty>(StringComparer.Ordinal);
        var definitions = new Dictionary<string, (Common.Gpc.Legacy.LegacyBeamProperty Property, string Data)>(StringComparer.Ordinal);
        for (int i = 0; i < originals.Count; i++)
        {
            var original = originals[i]; var replacement = replacements[i];
            if (original?.IsValid != true || string.IsNullOrWhiteSpace(Name(original)) || replacement?.IsValid != true || string.IsNullOrWhiteSpace(Name(replacement)))
                throw new ArgumentException($"Pair {i + 1}: supply valid, named original and replacement Beam Property objects.");
            var datum = replacement.Gpc; string name = Name(replacement);
            // Repeated identical properties from Get Beam Property are harmless. Conflicting names are not.
            if (!definitions.TryGetValue(name, out var definition)) { definition = (datum, GpcOnlyElements.Canonical(datum)); definitions.Add(name, definition); }
            else if (!ReferenceEquals(definition.Property, datum) && definition.Data != GpcOnlyElements.Canonical(datum))
                throw new ArgumentException($"Replacement name '{name}' has different definitions. Give different properties distinct names.");
            if (pairs.TryGetValue(Name(original), out var previous) && previous.Property.Name != definition.Property.Property.Name)
                throw new ArgumentException($"Original name '{Name(original)}' is paired with more than one replacement.");
            pairs[Name(original)] = definition.Property;
        }
        var replaced = drafts.Select(d => d.Beam.BeamProperty.Name != null && pairs.ContainsKey(d.Beam.BeamProperty.Name)).ToArray();
        var used = new HashSet<string>(drafts.Where((d, i) => replaced[i]).Select(d => d.Beam.BeamProperty.Name), StringComparer.Ordinal);
        var outputs = new List<GH_Beam>();
        for (int i = 0; i < drafts.Length; i++)
        {
            var draft = drafts[i]; var replacement = replaced[i] ? pairs[draft.Beam.BeamProperty.Name] : null;
            var identity = GpcOnlyElements.Identity(draft.Beam.Guid, ComponentGuid, 0, replacement);
            var built = GpcOnlyElements.Generated(this, () => replacement != null
                ? Common.Gpc.Builders.GpcBeamBuilders.Override(draft, replacement, null, null, null, identity)
                : Common.Gpc.GpcDrafts.Reidentify(Common.Gpc.GpcDrafts.Copy(draft), identity));
            if (built == null) return (null, null, null);
            outputs.Add(GH_Beam.FromGpc(built, beams[i].Guid));
        }
        return (outputs, replaced, pairs.Keys.Where(name => !used.Contains(name)).ToArray());
    }
}
