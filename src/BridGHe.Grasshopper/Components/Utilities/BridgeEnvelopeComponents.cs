using System;
using System.Collections.Generic;
using System.Linq;
using BridGHe.Common.Diagnostics;
using BridGHe.Grasshopper.DataTypes.Fem;
using BridGHe.Grasshopper.Helpers;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;

namespace BridGHe.Grasshopper.Components.Utilities;

public abstract class BridgeEnvelopeComponent : BridgeUtilityComponent
{
    protected abstract int Kind { get; }
    protected BridgeEnvelopeComponent(string name, string nick, string components) : base(name, nick,
        "Envelope stored results with governing case/step and all six concomitant values. " + components + " Uses model units, no unit conversion or interpolation. Absolute mode retains the governing sign.", CategoryNameConstants.SUBCATEGORY_RESULTS) { }
    protected override void RegisterInputParams(GH_InputParamManager p)
    {
        p.AddGenericParameter("Model", "M", "Analysed FEM model.", GH_ParamAccess.item);
        p.AddTextParameter("Element IDs", "IDs", "Optional selection; empty selects all elements of this type. Every selected element must have results.", GH_ParamAccess.list); p[1].Optional = true;
        p.AddTextParameter("Cases", "LC", "Exact case names; empty selects all stored cases of this result type. Does not generate combinations.", GH_ParamAccess.list); p[2].Optional = true;
        p.AddIntegerParameter("Component", "C", Kind == 0 ? "0 N, 1 V2, 2 V3, 3 T, 4 M2, 5 M3; local Straus axes." : Kind == 1 ? "0 Fx, 1 Fy, 2 Fz, 3 Mx, 4 My, 5 Mz; global axes." : "0 Ux, 1 Uy, 2 Uz, 3 Rx, 4 Ry, 5 Rz; global axes, rotations in rad.", GH_ParamAccess.item, Kind == 0 ? 5 : 2);
        p.AddIntegerParameter("Mode", "Mode", "0 minimum, 1 maximum, 2 maximum absolute value (signed).", GH_ParamAccess.item, 2);
    }
    protected override void RegisterOutputParams(GH_OutputParamManager p)
    {
        p.AddTextParameter("Element ID", "ID", "Element corresponding to each result row.", GH_ParamAccess.list);
        p.AddNumberParameter("Station", "X", "Beam distance from I in model length units; zero for nodes.", GH_ParamAccess.list);
        p.AddNumberParameter("Value", "V", "Signed governing component, model units.", GH_ParamAccess.list);
        p.AddTextParameter("Case", "LC", "Governing case name.", GH_ParamAccess.list);
        p.AddNumberParameter("Step", "Step", "Stored step identifier; may identify moving vehicle position, not a distance.", GH_ParamAccess.list);
        p.AddTextParameter("Step type", "Type", "Stored step type.", GH_ParamAccess.list);
        p.AddNumberParameter("Concomitant", "All", "Six components from the SAME result. One branch per output row.", GH_ParamAccess.tree);
        p.AddGenericParameter("Result", "R", "Original governing result object.", GH_ParamAccess.list);
        p.AddTextParameter("Coverage", "Coverage", "Case coverage at each sampled station; no assumption of identical step grids.", GH_ParamAccess.list);
        p.AddTextParameter("Units", "U", "Model unit system; moments use force times length.", GH_ParamAccess.item);
    }
    protected override void Solve(IGH_DataAccess da)
    {
        GH_Model model = null; var ids = new List<string>(); var cases = new List<string>(); int component = Kind == 0 ? 5 : 2, mode = 2;
        if (!da.GetData(0, ref model) || !da.GetData(3, ref component) || !da.GetData(4, ref mode)) return;
        da.GetDataList(1, ids); da.GetDataList(2, cases);
        var rows = BridgeResultEnvelope.Evaluate(model.Value, Kind, ids, cases, component, mode);
        da.SetDataList(0, rows.Select(r => r.Element)); da.SetDataList(1, rows.Select(r => r.Station)); da.SetDataList(2, rows.Select(r => r.Value));
        da.SetDataList(3, rows.Select(r => r.Case)); da.SetDataList(4, rows.Select(r => r.Step)); da.SetDataList(5, rows.Select(r => r.StepType));
        var tree = new GH_Structure<GH_Number>(); for (int i = 0; i < rows.Length; i++) tree.AppendRange(rows[i].Components.Select(v => new GH_Number(v)), new GH_Path(i));
        da.SetDataTree(6, tree); da.SetDataList(7, rows.Select(r => new GH_ObjectWrapper(r.Source))); da.SetDataList(8, rows.Select(r => r.Coverage)); da.SetData(9, model.Value.Units.ToString());
        if (rows.Any(r => r.Coverage != "Complete cases")) AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Some stations have incomplete case coverage: inspect Coverage.");
    }
}
public sealed class BridgeBeamEnvelopeComponent : BridgeEnvelopeComponent
{
    public BridgeBeamEnvelopeComponent() : base("Bridge Beam Envelope", "BeamEnv", "Local beam forces N,V2,V3,T,M2,M3.") { }
    protected override int Kind => 0;
    public override Guid ComponentGuid => new Guid("1ea9fccb-8a94-4b89-bc26-cd4fb7904248");
}
public sealed class BridgeBearingEnvelopeComponent : BridgeEnvelopeComponent
{
    public BridgeBearingEnvelopeComponent() : base("Bridge Bearing Envelope", "BearingEnv", "Global support reactions Fx,Fy,Fz,Mx,My,Mz. Select support Node IDs explicitly when the model also contains free nodes.") { }
    protected override int Kind => 1;
    public override Guid ComponentGuid => new Guid("8272699a-e096-49e0-b185-f31addd9e21d");
}
public sealed class BridgeDisplacementEnvelopeComponent : BridgeEnvelopeComponent
{
    public BridgeDisplacementEnvelopeComponent() : base("Bridge Displacement Envelope", "DispEnv", "Global node translations Ux,Uy,Uz and rotations Rx,Ry,Rz. Compare translations with project serviceability limits separately.") { }
    protected override int Kind => 2;
    public override Guid ComponentGuid => new Guid("7cdb2c25-a0b6-44d3-b64c-bd56232663fa");
}
