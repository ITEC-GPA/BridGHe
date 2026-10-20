using System;
using System.Collections.Generic;
using System.Linq;
using BridGHe.Common.Checks;
using BridGHe.Common.Geometry;
using BridGHe.Grasshopper.Components.Utilities;
using BridGHe.Grasshopper.DataTypes;
using BridGHe.Grasshopper.DataTypes.Fem;
using BridGHe.Grasshopper.Helpers;
using Grasshopper.Kernel;

namespace BridGHe.Grasshopper.Components.Geometry;

public sealed class BridgeConstructionSequenceComponent : BridgeUtilityComponent
{
    public BridgeConstructionSequenceComponent() : base("Bridge Construction Sequence", "Construction",
        "Prepare one explicit construction phase snapshot and, where representable, a native activation stage. Repeat per phase; no implicit force-history transfer.", CategoryNameConstants.SUBCATEGORY_MODEL) { }
    public override Guid ComponentGuid => new Guid("b723e528-d958-4c30-a258-a9ae1b5fc2d3");
    protected override void RegisterInputParams(GH_InputParamManager p)
    {
        p.AddGenericParameter("Model", "M", "Full assembled model, unchanged.", GH_ParamAccess.item);
        p.AddGenericParameter("Phase", "P", "Numbered Bridge Phase; number/name/day identify the snapshot.", GH_ParamAccess.item);
        p.AddTextParameter("Active Groups", "G", "Exact full group paths; descendants included. Union with Physical IDs. Both lists empty selects all.", GH_ParamAccess.list); p[2].Optional = true;
        p.AddTextParameter("Physical IDs", "ID", "Physical member IDs from Member Assignment, mesh independent.", GH_ParamAccess.list); p[3].Optional = true;
        p.AddTextParameter("Active Bearing IDs", "B", "Exact FEM Node IDs. '*' all; leave empty with Enable Bearings=false to remove all.", GH_ParamAccess.list, "*");
        p.AddBooleanParameter("Enable Bearings", "E", "False removes all nodal restraints/springs in the phase snapshot.", GH_ParamAccess.item, true);
        p.AddGenericParameter("Girder Property", "S", "Optional explicit override on active longitudinal girders, e.g. steel/n0/n-infinity. No automatic change from phase label.", GH_ParamAccess.item); p[6].Optional = true;
        p.AddGenericParameter("Load Increments", "LC", "Optional load combinations for a native group activation stage; do not infer incremental loads from cumulative states.", GH_ParamAccess.list); p[7].Optional = true;
    }
    protected override void RegisterOutputParams(GH_OutputParamManager p)
    {
        p.AddGenericParameter("Phase Model", "M", "Independent model snapshot; results cleared, existing loads retained on active elements.", GH_ParamAccess.item);
        p.AddGenericParameter("Native Stage", "S", "Available only for group activation with unchanged bearings/properties and supplied increments. Connect ordered stages to the original full model.", GH_ParamAccess.item);
        p.AddGenericParameter("Phase", "P", "Unchanged workflow phase; attach actions from subsequently solved models.", GH_ParamAccess.item);
        p.AddTextParameter("Active Physical IDs", "ID", "Selected physical member identities.", GH_ParamAccess.list);
        p.AddTextParameter("Available Groups", "G", "Full source group paths, including parents, for selection.", GH_ParamAccess.list);
        p.AddTextParameter("Information", "I", "Snapshot/history distinction and native stage eligibility.", GH_ParamAccess.list);
    }
    protected override void Solve(IGH_DataAccess da)
    {
        GH_Model model = null; BridgeWorkflowGoo phase = null; var groups = new List<string>(); var ids = new List<string>();
        var bearings = new List<string>(); bool enabled = true; GH_BeamProperty property = null; var increments = new List<GH_LoadCombination>();
        if (!da.GetData(0, ref model) || !da.GetData(1, ref phase)) return;
        if (phase.Value is not BridgePhaseDefinition definition) throw new ArgumentException("Phase requires a Bridge Phase object.");
        da.GetDataList(2, groups); da.GetDataList(3, ids); da.GetDataList(4, bearings); da.GetData(5, ref enabled);
        da.GetData(6, ref property); da.GetDataList(7, increments);
        var result = BridgeConstructionSequence.Create(model.Value, definition, groups, ids, enabled ? bearings : Array.Empty<string>(),
            property?.Value, increments.Select(c => c.Value).ToArray());
        da.SetData(0, new GH_Model { Value = result.Model }); if (result.Stage != null) da.SetData(1, new GH_StagedConstruction(result.Stage));
        da.SetData(2, phase); da.SetDataList(3, result.ActivePhysicalIds);
        da.SetDataList(4, model.Value.Elements.SelectMany(e => e.Groups).SelectMany(BridGHe.Common.Models.GroupModel.GetBranch).Select(g => g.FullName).Distinct());
        da.SetDataList(5, result.Diagnostics);
    }
}
