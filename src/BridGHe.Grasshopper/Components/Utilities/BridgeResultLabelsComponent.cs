using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using BridGHe.Common.Checks;
using BridGHe.Grasshopper.Helpers;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Special;
using Grasshopper.Kernel.Types;

namespace BridGHe.Grasshopper.Components.Utilities;

public sealed class BridgeResultLabelsComponent : BridgeUtilityComponent
{
    private static readonly string[] FormatNames = { "Completa", "Trave e stazione", "Combinazione", "Fase" };
    private bool updatingLists;

    public BridgeResultLabelsComponent() : base("Bridge Result Labels", "ResultLabels",
        "Create one label per bridge result, preserving order and tree branches. Connect a Value List to Format to populate its choices automatically.",
        CategoryNameConstants.SUBCATEGORY_RESULTS)
    {
        PrincipalParameterIndex = 0;
        Params.ParameterSourcesChanged += (_, _) => ConfigureValueLists();
    }
    public override Guid ComponentGuid => new Guid("d507b37a-ca9f-4c25-890c-2cd354c22ebc");
    protected override void RegisterInputParams(GH_InputParamManager p)
    {
        p.AddGenericParameter("Results", "R", "The same BridgeStressResult tree connected to Bridge Check Summary.", GH_ParamAccess.list);
        p.AddIntegerParameter("Format", "F", "0 Completa; 1 Trave e stazione; 2 Combinazione; 3 Fase. Connect a Value List for automatic choices. One format for the entire tree.", GH_ParamAccess.item, 0);
        p[1].Optional = true;
    }
    protected override void RegisterOutputParams(GH_OutputParamManager p)
    {
        p.AddTextParameter("Labels", "L", "One label per input result, with identical order and branches. Connect to Labels of Bridge Check Summary. Station s is measured from the FEM beam start in mm.", GH_ParamAccess.list);
    }
    public override void AddedToDocument(GH_Document document)
    {
        base.AddedToDocument(document);
        PrincipalParameterIndex = 0;
        ConfigureValueLists();
    }
    private void ConfigureValueLists()
    {
        if (updatingLists || Params.Input.Count < 2) return;
        updatingLists = true;
        try
        {
            foreach (var list in Params.Input[1].Sources.OfType<GH_ValueList>())
            {
                bool matches = list.ListMode == GH_ValueListMode.DropDown && list.SelectedItems.Count == 1 && list.ListItems.Count == FormatNames.Length
                    && list.ListItems.Select((item, i) => item.Name == FormatNames[i] && item.Expression == i.ToString(CultureInfo.InvariantCulture)).All(x => x);
                if (matches) continue;
                // Preserve a known format on reconnect/read; an unrelated new Value List defaults to Complete.
                int selected = Array.FindIndex(FormatNames, name => list.ListItems.Any(item => item.Name == name && item.Selected));
                if (selected < 0) selected = 0;
                list.ListMode = GH_ValueListMode.DropDown;
                list.ListItems.Clear();
                for (int i = 0; i < FormatNames.Length; i++)
                    list.ListItems.Add(new GH_ValueListItem(FormatNames[i], i.ToString(CultureInfo.InvariantCulture)) { Selected = i == selected });
                // The connection/document event drives the next solution. Never start a nested solution here.
                list.ExpireSolution(false);
                Params.Input[1].ExpireSolution(false);
            }
        }
        finally { updatingLists = false; }
    }
    protected override void Solve(IGH_DataAccess da)
    {
        Message = "";
        var values = new List<GH_ObjectWrapper>(); int format = 0;
        if (!da.GetDataList(0, values)) return;
        da.GetData(1, ref format);
        if (Params.Input[1].VolatileDataCount > 1) throw new ArgumentException("Choose a single Format for all results.");
        if (format < 0 || format >= FormatNames.Length) throw new ArgumentException("Format must be 0 Completa, 1 Trave e stazione, 2 Combinazione or 3 Fase.");
        if (values.Any(v => v?.Value is not BridgeStressResult)) throw new ArgumentException("Results must contain BridgeStressResult objects from a bridge checker.");
        // SetDataList can graft an iteration index when Format uses item access.
        // Explicit branches keep this output aligned with the original Results tree.
        var path = da.ParameterTargetPath(0);
        var labels = new GH_Structure<GH_String>();
        labels.AppendRange(values.Select((v, i) => new GH_String(BridgeResultLabels.Format((BridgeStressResult)v.Value,
            (BridgeResultLabelFormat)format, $"{path}[{i}]"))), path);
        da.SetDataTree(0, labels);
        Message = FormatNames[format];
    }
}
