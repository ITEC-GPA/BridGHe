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
        var result = BeamPropertyReplacement.Apply(beams.Select(b => b?.Value).ToArray(), originals.Select(p => p?.Value).ToArray(), replacements.Select(p => p?.Value).ToArray());
        da.SetDataList(0, result.Beams.Select((b, i) => new GH_Beam(b, beams[i].Guid)));
        da.SetDataList(1, result.Replaced);
        int count = result.Replaced.Count(v => v);
        var info = new List<string> { $"{count}/{beams.Count} beams matched. Names are exact and case-sensitive. Unmatched properties are retained.",
            count > 0 ? "All returned FEM results cleared: recalculate the assembled model. Geometry, offsets, loads and attributes retained. Replacement material and density used exactly as supplied." : "No replacement matched; copied properties and results are unchanged." };
        if (result.UnusedOriginalNames.Length > 0)
        {
            string warning = "Original property names not found: " + string.Join(", ", result.UnusedOriginalNames);
            info.Add(warning); AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, warning);
        }
        da.SetDataList(2, info); Message = $"{count}/{beams.Count} replaced";
    }
}
