using System;
using System.Collections.Generic;
using System.Linq;
using BridGHe.Common.Straus;
using BridGHe.Grasshopper.Components.BridgeChecks;
using BridGHe.Grasshopper.Components.Utilities;
using BridGHe.Grasshopper.DataTypes.Fem;
using BridGHe.Grasshopper.Helpers;
using Grasshopper.Kernel;

namespace BridGHe.Grasshopper.Components.Analysis;

public sealed class BridgeAnalysisCasesComponent : BridgeUtilityComponent
{
    public BridgeAnalysisCasesComponent():base("Bridge Analysis Cases","AnalysisCases","Collect and validate the FEM cases required by downstream actions before analysis. Connect the same case references to this component and to the actions; do not connect action outputs here.",CategoryNameConstants.SUBCATEGORY_ANALYSIS){}
    public override Guid ComponentGuid=>new Guid("a8e240c5-5c1c-4df3-bdc3-077a8de5f029");
    protected override void RegisterInputParams(GH_InputParamManager p)
    {p.AddGenericParameter("Model","M","FEM model BEFORE analysis, used to validate available result names.",GH_ParamAccess.item);p.AddGenericParameter("Required Cases","C","Case objects or exact names, shared with Bridge Action / Shrinkage Action / Moment Dispatch. No computed results required.",GH_ParamAccess.list);p[1].DataMapping=GH_DataMapping.Flatten;p.AddIntegerParameter("Solver","S","Same solver as Straus R3 Analysis: 0 linear, 1 nonlinear, 2 staged.",GH_ParamAccess.item,0);}
    protected override void RegisterOutputParams(GH_OutputParamManager p)
    {p.AddTextParameter("Analysis Results","R","Connect to Analysis Results on Straus R3 Analysis. May be combined with additional user-selected results.",GH_ParamAccess.list);p.AddTextParameter("Information","I","Validated required case names.",GH_ParamAccess.item);}
    protected override void Solve(IGH_DataAccess da)
    {GH_Model m=null;var cases=new List<object>();int solver=0;if(!da.GetData(0,ref m)||!da.GetDataList(1,cases))return;da.GetData(2,ref solver);var names=cases.Select(WorkflowInputs.Case).Distinct(StringComparer.Ordinal).ToArray();var selected=StrausResultSelection.Select(m.Value,solver,names);da.SetDataList(0,selected.Select(s=>s.Case.Name));da.SetData(1,$"{selected.Count} required results validated before analysis.");}
}
