using System;
using System.Collections.Generic;
using System.Linq;
using BridGHe.Common.Geometry;
using BridGHe.Grasshopper.Components.Utilities;
using BridGHe.Grasshopper.DataTypes;
using BridGHe.Grasshopper.DataTypes.Fem;
using BridGHe.Grasshopper.Helpers;
using Grasshopper.Kernel;

namespace BridGHe.Grasshopper.Components.Geometry;

public sealed class BridgeSegmentCompositeComponent : BridgeUtilityComponent
{
    public BridgeSegmentCompositeComponent() : base("Bridge Segment Composite", "SegmentComposite",
        "Match composite sections to longitudinal FEM beams by physical concio ID. Retains results; aligned outputs feed Composite beam and Composite Shrinkage Actions.", CategoryNameConstants.SUBCATEGORY_MODEL) { }
    public override Guid ComponentGuid => new Guid("36a9ed77-8fb7-4348-a783-f1c057a921e4");
    protected override void RegisterInputParams(GH_InputParamManager p)
    {
        p.AddGenericParameter("Beams", "B", "FEM members carrying Bridge Member metadata. Other member families are filtered out.", GH_ParamAccess.list);
        p.AddTextParameter("Segment IDs", "ID", "Distinct G1-C1 etc., paired with Composite Properties, from Bridge Segment Sections.", GH_ParamAccess.list);
        p.AddGenericParameter("Composite Properties", "P", "One composite section per Segment ID. No list broadcasting.", GH_ParamAccess.list);
    }
    protected override void RegisterOutputParams(GH_OutputParamManager p)
    {
        p.AddGenericParameter("Girders", "B", "FEM girders ordered by girder and alignment station, retaining forces. Connect to Composite beam > Beam.", GH_ParamAccess.list);
        p.AddGenericParameter("Composite Properties", "P", "One matched section per output FEM beam. Connect to Section Start or shrinkage conversion.", GH_ParamAccess.list);
        p.AddTextParameter("Segment IDs", "ID", "Concio key per output beam.", GH_ParamAccess.list);
        p.AddTextParameter("Beam IDs", "FEM", "FEM IDs in output order.", GH_ParamAccess.list);
    }
    protected override void Solve(IGH_DataAccess da)
    {
        var beams = new List<GH_Beam>(); var ids = new List<string>(); var props = new List<GH_CompositeBeamProperty>();
        if (!da.GetDataList(0, beams) || !da.GetDataList(1, ids) || !da.GetDataList(2, props)) return;
        if (beams.Count == 0) { Message = "Awaiting beams"; return; }
        Message = "";
        // R6.5 P1 lane 2b: a beam given by its draft alone or a composite section given by its GPC definition alone gives the outputs by their
        // GPC data alone (rule of passo 75); also with the switch of the property producers (the path of P2).
        if (GpcOnlyElements.PropertiesWithoutFem || beams.Any(b => b?.Value == null) || props.Any(GpcOnlyProperties.WithoutFem)) { SolveGpcOnly(da, beams, ids, props); return; }
        var mapped = BridgeSegmentComposite.Map(beams.Select(b => b?.Value).ToArray(), ids, props.Select(p => p?.Value).ToArray());
        // F6 (a)/(b), chain C: the girders keep the link to their beam of the GPC bridge model (Decompose Model of Build BridGHe Model).
        var links = new Dictionary<Common.Models.BeamModel, GH_Beam>(ReferenceEqualityComparer.Instance);
        foreach (var b in beams) if (b?.Value != null) links.TryAdd(b.Value, b);
        // R6.2 (lane B): the girders keep the GPC draft of their input (the same value: a copy with the identity of the output), the composite
        // properties the datum of theirs.
        da.SetDataList(0, mapped.Girders.Select(b =>
        {
            if (!links.TryGetValue(b, out var source)) return new GH_Beam(b);
            var girder = new GH_Beam(b) { AnalysedGpc = source.AnalysedGpc, AnalysedGpcModel = source.AnalysedGpcModel };
            GpcBeamInputs.Attach(girder, id => Common.Gpc.GpcDrafts.Reidentify(Common.Gpc.GpcDrafts.Copy(source.Gpc), id));
            return girder;
        }));
        var composites = new Dictionary<Common.Models.CompositeBeamPropertyModel, GH_CompositeBeamProperty>(ReferenceEqualityComparer.Instance);
        foreach (var p in props) if (p?.Value != null) composites.TryAdd(p.Value, p);
        da.SetDataList(1, mapped.Properties.Select(p =>
        {
            var output = new GH_CompositeBeamProperty(p);
            if (composites.TryGetValue(p, out var source)) GpcSegmentSections.TryAttach(output, source);
            return output;
        }));
        da.SetDataList(2, mapped.SegmentIds); da.SetDataList(3, mapped.Girders.Select(b => b.BeamId));
    }

    /// <summary>R6.5 P1 lane 2b: the association on the GPC data alone (<see cref="Common.Gpc.GpcStateOverride.Map"/>: the girders of the drafts in
    /// the order of their group and station, the same messages): every girder the copy of its input with a copy of its draft (the identity
    /// derived from the input draft; the analysed element kept, as the FEM path keeps it), every composite section its GPC definition.</summary>
    private void SolveGpcOnly(IGH_DataAccess da, List<GH_Beam> beams, List<string> ids, List<GH_CompositeBeamProperty> props)
    {
        if (beams.Any(b => b == null)) throw new ArgumentException("Supply bridge FEM beams with Bridge Member metadata.");
        var drafts = beams.Select(b => b.Gpc ?? throw new ArgumentException(GpcOnlyElements.NoDraft)).ToArray();
        var (girders, definitions, segments) = Common.Gpc.GpcStateOverride.Map(drafts, ids, props.Select(p => p?.IsValid == true ? p.Definition : null).ToArray());
        var input = new Dictionary<Common.Gpc.Legacy.LegacyBeamDraft, GH_Beam>(ReferenceEqualityComparer.Instance);
        for (int i = 0; i < drafts.Length; i++) input.TryAdd(drafts[i], beams[i]);
        var outputs = girders.Select(d => GpcOnlyElements.Beam(this, 0, input[d], id => Common.Gpc.GpcDrafts.Reidentify(Common.Gpc.GpcDrafts.Copy(d), id))).ToArray();
        if (outputs.Any(o => o == null)) return;
        da.SetDataList(0, outputs);
        da.SetDataList(1, definitions.Select(GH_CompositeBeamProperty.FromGpc));
        da.SetDataList(2, segments); da.SetDataList(3, girders.Select(d => d.Beam.Name));
    }
}
