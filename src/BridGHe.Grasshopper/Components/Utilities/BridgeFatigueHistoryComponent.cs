using System;
using System.Collections.Generic;
using System.Linq;
using BridGHe.Common.Diagnostics;
using BridGHe.Grasshopper.DataTypes.Fem;
using BridGHe.Grasshopper.Helpers;
using Grasshopper.Kernel;

namespace BridGHe.Grasshopper.Components.Utilities;

public sealed class BridgeFatigueHistoryComponent : BridgeUtilityComponent
{
    public BridgeFatigueHistoryComponent() : base("Bridge Fatigue History", "FatigueHistory",
        "Ordered response history and rainflow ranges. Accept direct stress/flow samples or extract FEM forces/explicit linear response. Does not infer an S-N curve or fatigue resistance.", CategoryNameConstants.SUBCATEGORY_RESULTS) { }
    public override Guid ComponentGuid => new Guid("25ce0914-c534-4d08-883d-607b74c65883");
    protected override void RegisterInputParams(GH_InputParamManager p)
    {
        p.AddNumberParameter("Values", "V", "Optional ordered stress/flow history. Mutually exclusive with Model.", GH_ParamAccess.list); p[0].Optional = true;
        p.AddGenericParameter("Model", "M", "Optional analysed N-mm model for FEM history extraction.", GH_ParamAccess.item); p[1].Optional = true;
        p.AddTextParameter("Beam ID", "B", "Required with Model.", GH_ParamAccess.item, "");
        p.AddNumberParameter("Station", "X", "Physical distance from beam start, mm.", GH_ParamAccess.item, 0);
        p.AddTextParameter("Ordered Cases", "LC", "Explicit transit order. May be omitted only when one case exists; its steps are ordered numerically.", GH_ParamAccess.list); p[4].Optional = true;
        p.AddIntegerParameter("Force Component", "F", "0=N,1=Vx,2=Vy,3=T,4=Mx,5=My; ignored when linear response factors are supplied.", GH_ParamAccess.item, 4);
        p.AddNumberParameter("Response Factors", "K", "Optional six factors: response=dot(N,Vx,Vy,T,Mx,My; factors). Supply coefficients for the actual section/phase and units.", GH_ParamAccess.list); p[6].Optional = true;
        p.AddTextParameter("Response Units", "U", "Units of direct values or linear response, e.g. MPa or N/mm. Raw force units are generated automatically.", GH_ParamAccess.item, "MPa");
        p.AddNumberParameter("Exponent", "m", "User-selected positive exponent for equivalent range; not an automatic material/detail classification.", GH_ParamAccess.item, 3);
        p.AddNumberParameter("Reference Cycles", "N", "Equivalent range = (sum(count*range^m)/N)^(1/m).", GH_ParamAccess.item, 1);
    }
    protected override void RegisterOutputParams(GH_OutputParamManager p)
    {
        p.AddNumberParameter("History", "V", "Ordered response samples.", GH_ParamAccess.list);
        p.AddTextParameter("Sources", "S", "Exact FEM case/step/station or direct sample index.", GH_ParamAccess.list);
        foreach (var name in new[] { "Ranges", "Means", "Cycle Counts" }) p.AddNumberParameter(name, name, "Paired rainflow cycle data; residual half cycles are retained.", GH_ParamAccess.list);
        p.AddNumberParameter("Equivalent Range", "Eq", "Equivalent response range using supplied exponent/reference count.", GH_ParamAccess.item);
        p.AddTextParameter("Units", "U", "Response units.", GH_ParamAccess.item);
        p.AddTextParameter("Information", "I", "Counting conventions and limits.", GH_ParamAccess.list);
    }
    protected override void Solve(IGH_DataAccess da)
    {
        var values = new List<double>(); GH_Model model = null; string id = "", units = "MPa"; double station = 0, exponent = 3, n = 1;
        var cases = new List<string>(); var factors = new List<double>(); int component = 4;
        da.GetDataList(0, values); da.GetData(1, ref model); da.GetData(2, ref id); da.GetData(3, ref station);
        da.GetDataList(4, cases); da.GetData(5, ref component); da.GetDataList(6, factors); da.GetData(7, ref units); da.GetData(8, ref exponent); da.GetData(9, ref n);
        if (model != null && values.Count > 0) throw new ArgumentException("Choose direct Values or Model extraction, not both.");
        if (string.IsNullOrWhiteSpace(units)) throw new ArgumentException("Provide response units.");
        var series = model != null ? BridgeFatigueHistory.Extract(model.Value, id, station, cases, component, factors) :
            new BridgeFatigueSeries(values.ToArray(), Enumerable.Range(0, values.Count).Select(i => $"Direct sample {i}").ToArray());
        if (model != null && factors.Count == 0) units = component <= 2 ? "N" : "N mm";
        var spectrum = BridgeFatigueHistory.Count(series.Values, exponent, n);
        da.SetDataList(0, series.Values); da.SetDataList(1, series.Sources); da.SetDataList(2, spectrum.Cycles.Select(c => c.Range));
        da.SetDataList(3, spectrum.Cycles.Select(c => c.Mean)); da.SetDataList(4, spectrum.Cycles.Select(c => c.Count));
        da.SetData(5, spectrum.EquivalentRange); da.SetData(6, units);
        da.SetDataList(7, new[] { $"Open history: {series.Values.Length} samples, {spectrum.Count:G9} counted cycles; min={spectrum.Minimum:G9}, max={spectrum.Maximum:G9}.",
            "Residual excursions count as half cycles; the component never closes or repeats a transit implicitly.",
            "No fatigue utilization or Miner damage is calculated without a separately defined resistance model and traffic repetition count." });
    }
}
