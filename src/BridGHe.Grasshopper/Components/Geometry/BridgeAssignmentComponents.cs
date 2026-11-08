using System;
using System.Collections.Generic;
using System.Linq;
using BridGHe.Common.Geometry;
using BridGHe.Common.Models;
using BridGHe.Grasshopper.Components.Utilities;
using BridGHe.Grasshopper.DataTypes;
using BridGHe.Grasshopper.DataTypes.Fem;
using BridGHe.Grasshopper.Helpers;
using Grasshopper.Kernel;
using Rhino.Geometry;

namespace BridGHe.Grasshopper.Components.Geometry;

public sealed class BridgeSectionScheduleComponent : BridgeUtilityComponent
{
    public BridgeSectionScheduleComponent() : base("Bridge Section Schedule", "SectionSchedule",
        "Assign a property to a family/group/segment over an arc-length interval. Optional dimensional taper; overlapping rules are rejected.", CategoryNameConstants.SUBCATEGORY_MODEL) { }
    public override Guid ComponentGuid => new Guid("2cd3635e-8a72-4f8c-bb2f-bf4b76ff136c");
    protected override void RegisterInputParams(GH_InputParamManager p)
    {
        p.AddGenericParameter("Property", "P", "Beam property in N-mm.", GH_ParamAccess.item);
        p.AddNumberParameter("Start", "S", "Start station, mm, inclusive.", GH_ParamAccess.item);
        p.AddNumberParameter("End", "E", "End station, mm, exclusive except bridge end.", GH_ParamAccess.item);
        p.AddIntegerParameter("Family", "F", "0 girder, 1 solid diaphragm, 2 top chord, 3 bottom chord, 4 post, 5 diagonal, 6 longitudinal bracing, 7 bracing post, 8 transverse slab.", GH_ParamAccess.item, 0);
        p.AddIntegerParameter("Group", "G", "0 all; otherwise girder number or diaphragm line.", GH_ParamAccess.item, 0);
        p.AddIntegerParameter("Segment", "C", "0 all; otherwise fabrication segment or diaphragm bay.", GH_ParamAccess.item, 0);
        p.AddVectorParameter("Start Scale", "SF", "Section local x/y scale factors (X,Y); Z must be zero. Dimensional Straus taper, centred.", GH_ParamAccess.item, new Vector3d(1, 1, 0));
        p.AddVectorParameter("End Scale", "SE", "Section local x/y scale factors (X,Y), linearly varying along station interval.", GH_ParamAccess.item, new Vector3d(1, 1, 0));
    }
    protected override void RegisterOutputParams(GH_OutputParamManager p)
    { p.AddGenericParameter("Schedule", "S", "Connect one or more rules to Bridge Member Assignment.", GH_ParamAccess.item); }
    protected override void Solve(IGH_DataAccess da)
    {
        GH_BeamProperty property = null; double start = 0, end = 0; int family = 0, group = 0, segment = 0;
        var sf = new Vector3d(1, 1, 0); var se = sf;
        if (!da.GetData(0, ref property) || !da.GetData(1, ref start) || !da.GetData(2, ref end)) return;
        da.GetData(3, ref family); da.GetData(4, ref group); da.GetData(5, ref segment); da.GetData(6, ref sf); da.GetData(7, ref se);
        if (sf.Z != 0 || se.Z != 0) throw new ArgumentException("Section scales use X/Y only; Z must be zero.");
        // R6.2 (lane B): the GPC datum of the property goes with it to Bridge Member Assignment.
        TryAttach(property);
        var rule = new BridgeSectionRule { Property = property.Value, Start = start, End = end, Family = family, Group = group,
            Segment = segment, ScaleFrom = new(sf.X, sf.Y), ScaleTo = new(se.X, se.Y) };
        rule.Validate(); da.SetData(0, new BridgeDesignGoo(rule));
    }

    /// <summary>R6.2 (lane B): the GPC datum of a GH property for the bridge generators (<see cref="BridgeGpcProperties"/>); a datum the reader
    /// refuses is not attached (the generators convert the FEM property, refused the same way).</summary>
    internal static void TryAttach(GH_BeamProperty property)
    {
        if (property?.Value == null) return;
        try { BridgeGpcProperties.Attach(property.Value, property.Gpc); }
        catch (Exception x) when (x is ArgumentException || x is NotSupportedException || x is InvalidOperationException) { }
    }
}

public class BridgeMemberAssignmentComponent : BridgeUtilityComponent
{
    public BridgeMemberAssignmentComponent() : base("Bridge Member Assignment", "AssignBridge",
        "Convert Bridge Geometry into beams with physical IDs, groups and local section axes. No loads, restraints or analysis.", CategoryNameConstants.SUBCATEGORY_MODEL) { }
    public override Guid ComponentGuid => new Guid("92ef4133-2716-4a81-9b70-ff01db7fd15d");
    public override GH_Exposure Exposure => GetType() == typeof(BridgeMemberAssignmentComponent) ? GH_Exposure.hidden : GH_Exposure.primary;
    protected override void RegisterInputParams(GH_InputParamManager p)
    {
        p.AddGenericParameter("Bridge Geometry", "BG", "Typed output of Simple Bridge or Bridge Longitudinal Bracing.", GH_ParamAccess.item);
        p.AddGenericParameter("Default Properties", "P", "Optional beam properties, paired with Families. Rules override these defaults.", GH_ParamAccess.list); p[1].Optional = true;
        p.AddIntegerParameter("Families", "F", "One code per default property. Codes 0..8 select families; -1 applies to steel families only. Family 8 (slab) requires an explicit property.", GH_ParamAccess.list, -1);
        p.AddGenericParameter("Schedule", "S", "Optional Bridge Section Schedule entries; overlapping matching entries are rejected.", GH_ParamAccess.list); p[3].Optional = true;
    }
    protected override void RegisterOutputParams(GH_OutputParamManager p)
    {
        p.AddGenericParameter("Beams", "B", "Beam elements for Build Model, N-mm. Preview available through beam output/Preview BridGHe Model.", GH_ParamAccess.list);
        p.AddTextParameter("Physical IDs", "ID", "Mesh-independent family/group/segment identity paired with each beam.", GH_ParamAccess.list);
        p.AddTextParameter("Unassigned", "U", "Geometric member IDs without a property; an incomplete assignment must be resolved before export.", GH_ParamAccess.list);
        p.AddTextParameter("Information", "I", "Counts, unused rules and assignment conventions.", GH_ParamAccess.list);
    }
    protected override void Solve(IGH_DataAccess da)
    {
        BridgeGeometryGoo geometry = null; var properties = new List<GH_BeamProperty>(); var families = new List<int>(); var rules = new List<BridgeDesignGoo>();
        if (!da.GetData(0, ref geometry) || geometry?.Value == null) return;
        da.GetDataList(1, properties); da.GetDataList(2, families); da.GetDataList(3, rules);
        if (properties.Count > 0 && (families.Count != properties.Count || families.Distinct().Count() != families.Count))
            throw new ArgumentException("Provide exactly one distinct family code per default property.");
        if (rules.Any(r => r?.Value is not BridgeSectionRule)) throw new ArgumentException("Schedule accepts Bridge Section Schedule entries.");
        // R6.5 P1 lane 2a: a default property given by its GPC datum alone is assigned by its datum (rule of passo 75): the beams then are
        // given by their plans alone.
        var defaults = properties.Select((p, i) => (families[i], p)).Where(p => !GpcOnlyProperties.WithoutFem(p.p)).ToDictionary(p => p.Item1, p => p.p.Value);
        var gpcDefaults = properties.Select((p, i) => (families[i], p)).Where(p => GpcOnlyProperties.WithoutFem(p.p)).ToDictionary(p => p.Item1, p => p.p.Gpc);
        foreach (var p in properties) BridgeSectionScheduleComponent.TryAttach(p);
        Publish(da, geometry.Value, defaults, rules.Select(r => (BridgeSectionRule)r.Value).ToArray(), gpcDefaults);
    }
    protected void Publish(IGH_DataAccess da, SimpleBridgeGeometry geometry, Dictionary<int,BeamPropertyModel> defaults, BridgeSectionRule[] rules,
        Dictionary<int, Common.Gpc.Legacy.LegacyBeamProperty> gpcDefaults = null)
    {
        var result = BridgeMemberAssignment.Assign(geometry, defaults, rules, gpcDefaults);
        if (result.Unassigned.Length > 0) AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, $"{result.Unassigned.Length} members have no property and were not created. Inspect Unassigned.");
        // R6.5 P1 lane 1b: the beams by their GPC drafts alone (identity by content of the plan), the physical IDs from the plans.
        // R6.5 P1 lane 2a: the same when a property is given by its GPC datum alone (an assignment on GPC only has no FEM beams).
        if (GpcOnlyElements.GeneratorsWithoutFem || gpcDefaults is { Count: > 0 } || rules.Any(r => r.Property == null && r.GpcProperty != null))
        {
            da.SetDataList(0, GpcOnly(this, 0, result.Plans));
            da.SetDataList(1, result.Plans.Select(p => p.PhysicalId));
            da.SetDataList(2, result.Unassigned); da.SetDataList(3, result.Diagnostics);
            return;
        }
        // R6.2 (lane B): every beam with its GPC draft built on GPC from its plan (identity of the conversion of the FEM output until R6.5).
        var outputs = result.Beams.Select(b => new GH_Beam(b)).ToArray();
        for (int i = 0; i < outputs.Length; i++) { var plan = result.Plans[i]; GpcBeamInputs.Attach(outputs[i], id => Common.Gpc.Builders.GpcBridgeGeometryBuilders.Assigned(plan, id)); }
        da.SetDataList(0, outputs);
        da.SetDataList(1, result.Beams.Select(b => b.Attributes.OfType<BridgeMemberAttributeModel>().Single().Value.PhysicalId));
        da.SetDataList(2, result.Unassigned); da.SetDataList(3, result.Diagnostics);
    }

    /// <summary>R6.5 P1 lane 1b: the beams of plans by their GPC drafts alone (<c>GpcBridgeGeometryBuilders.Assigned</c>), each with the
    /// identity by content of its plan (every field: ends, the GPC property, export ID and label, family, group, segment, span, physical
    /// and geometric IDs, stations, deck frame, taper scales); a refused one gives an error and is left out. Also Bridge Slab Grillage.</summary>
    internal static List<GH_Beam> GpcOnly(GH_Component component, int output, IEnumerable<BridgeMemberPlan> plans)
    {
        var beams = new List<GH_Beam>();
        foreach (var plan in plans)
        {
            var draft = GpcOnlyElements.Generated(component, () => Common.Gpc.Builders.GpcBridgeGeometryBuilders.Assigned(plan, GpcOnlyElements.ContentIdentity(component.ComponentGuid, output,
                plan.From, plan.To, GpcOnlyElements.Lazy(() => plan.Property()), plan.SolverId, plan.Label, plan.Family, plan.Group, plan.Segment, plan.Span, plan.PhysicalId,
                plan.StationFrom, plan.StationTo, plan.GeometryId, plan.DeckY, plan.DeckX, plan.ScaleFrom, plan.ScaleTo)));
            if (draft != null) beams.Add(GH_Beam.FromGpc(draft));
        }
        return beams;
    }
}
