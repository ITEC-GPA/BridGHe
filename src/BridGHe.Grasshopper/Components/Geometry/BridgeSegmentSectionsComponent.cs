using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using BridGHe.Common.Geometry;
using BridGHe.Common.Models;
using BridGHe.Grasshopper.Components.Utilities;
using BridGHe.Grasshopper.DataTypes;
using BridGHe.Grasshopper.DataTypes.Fem;
using BridGHe.Grasshopper.Helpers;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Special;
using Grasshopper.Kernel.Types;

namespace BridGHe.Grasshopper.Components.Geometry;

public sealed class BridgeSegmentSectionsComponent : BridgeUtilityComponent
{
    private string[] tableChanges = Array.Empty<string>();
    public BridgeSegmentSectionsComponent() : base("Bridge Segment Sections", "SegmentSections",
        "Create a steel I section for every girder fabrication segment. An external panel holds editable dimensions; '*' inherits the base section. Press Tabella conci to populate/refresh it.",
        CategoryNameConstants.SUBCATEGORY_SECTIONS) { }
    public override Guid ComponentGuid => new Guid("8b33beac-6c81-4285-94ad-4e3dc2351a21");
    protected override void RegisterInputParams(GH_InputParamManager p)
    {
        p.AddGenericParameter("Fabrication Plan", "C", "Physical segments from Bridge Fabrication Segments, before meshing. Existing Bridge Geometry is also accepted.", GH_ParamAccess.item);
        p.AddGenericParameter("Base Property", "P", "Steel I Section + Beam property. Material and formulation are retained; dimensions marked '*' follow this section.", GH_ParamAccess.item);
        p.AddTextParameter("Section Table", "T", "One row per concio: ID; start; end; b_bottom; b_top; height; t_bottom; t_top; t_web; radius. All dimensions mm. '*' inherits base dimension. Disconnected or header-only = all base; a connected empty source waits for data. Press Tabella conci to fill an attached editable Panel.", GH_ParamAccess.list);
        p[2].Optional = true;
    }
    protected override void RegisterOutputParams(GH_OutputParamManager p)
    {
        p.AddGenericParameter("Schedule", "S", "One section assignment per girder/concio, ready for Bridge Member Assignment > Schedule.", GH_ParamAccess.list);
        p.AddGenericParameter("Properties", "P", "One FEM beam property per girder/concio, same order as IDs.", GH_ParamAccess.list);
        p.AddGenericParameter("Sections", "Sec", "Steel sections for composite sections and phase overrides, same order as IDs.", GH_ParamAccess.list);
        p.AddTextParameter("Segment IDs", "ID", "G1-C1 = first girder, first fabrication segment. Ordered by girder, then concio.", GH_ParamAccess.list);
        p.AddTextParameter("Resolved Table", "T", "Actual section dimensions after resolving '*'. Header and one row per concio; semicolon separated.", GH_ParamAccess.list);
        p.AddNumberParameter("Top Offsets", "Ztop", "Distance from section centroid to top flange, mm. Reference information: this component does not move beam axes or change eccentric links.", GH_ParamAccess.list);
        p.AddTextParameter("Information", "I", "Counts, table-refresh changes and geometric reference warnings.", GH_ParamAccess.list);
    }
    public override void CreateAttributes()
    {
        var a = new ComponentAttributes.ComponentOneButtonAttributes(this, "Tabella conci");
        a.ButtonPressed += () => RefreshFromUi(); m_attributes = a;
    }
    protected override void AppendAdditionalComponentMenuItems(ToolStripDropDown menu)
    {
        base.AppendAdditionalComponentMenuItems(menu);
        Menu_AppendItem(menu, "Aggiorna tabella conci", (_, _) => RefreshFromUi());
    }
    private void RefreshFromUi()
    {
        try { UpdateTablePanel(); ExpireSolution(true); }
        catch (ArgumentException e) { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, e.Message); }
        catch (InvalidOperationException e) { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, e.Message); }
    }

    /// <summary>Explicit user action; never writes a panel during Solve or document loading.</summary>
    public void UpdateTablePanel()
    {
        var document = OnPingDocument() ?? throw new InvalidOperationException("Add this component to a Grasshopper document first.");
        var geometries = Params.Input[0].VolatileData.AllData(true).Select(g => g?.ScriptVariable()).OfType<IBridgeSegments>().ToArray();
        if (geometries.Length != 1) throw new ArgumentException("Connect one solved Bridge Geometry before updating the section table.");
        var input = Params.Input[2];
        if (input.SourceCount > 1 || input.SourceCount == 1 && input.Sources[0] is not GH_Panel)
            throw new ArgumentException("Section Table: connect a single editable Panel or disconnect the input to create one. Other sources are never overwritten.");
        var panel = input.Sources.OfType<GH_Panel>().SingleOrDefault();
        if (panel?.SourceCount > 0) throw new ArgumentException("The section Panel must contain editable text, not data connected from another component.");
        // Parse before changing the document; malformed existing rows are never discarded silently.
        var update = BridgeSegmentSections.Synchronize(geometries[0], panel == null ? null : new[] { panel.UserText });
        if (panel == null)
        {
            panel = new GH_Panel { Name = "Sezioni per concio [mm]", NickName = "Sezioni per concio [mm]" };
            panel.CreateAttributes(); panel.Attributes.Pivot = new PointF(Attributes.Pivot.X - 950, Attributes.Pivot.Y);
            panel.Attributes.Bounds = new RectangleF(panel.Attributes.Pivot, new SizeF(840, 420));
            document.AddObject(panel, false); input.AddSource(panel);
        }
        else panel.RecordUndoEvent("Aggiorna tabella sezioni conci");
        panel.Properties.Multiline = false;
        panel.UserText = string.Join(Environment.NewLine, update.Table);
        tableChanges = update.Changes;
        panel.ExpireSolution(false); ExpireSolution(false);
    }

    protected override void Solve(IGH_DataAccess da)
    {
        Message = "";
        IGH_Goo geometry = null; GH_BeamProperty property = null; var table = new List<string>();
        if (!da.GetData(0, ref geometry) || !da.GetData(1, ref property)) return;
        if (Params.Input[0].VolatileDataCount != 1 || Params.Input[1].VolatileDataCount != 1)
            throw new ArgumentException("Connect exactly one Bridge Geometry and one Base Property; use Section Table for multiple segments.");
        da.GetDataList(2, table);
        if (table.Count == 0 && Params.Input[2].SourceCount > 0)
        {
            Message = "Waiting for table";
            AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "The connected Section Table has no data. Read the workbook or supply a valid table before assigning sections. Disconnect the input to use only the base section.");
            return;
        }
        // R6.5 P1 lane 2b: a base property given by its GPC datum alone gives the rules, properties and sections by their GPC data alone (rule of
        // passo 75); also with the switch of the property producers (the path of P2).
        if (GpcOnlyElements.PropertiesWithoutFem || GpcOnlyProperties.WithoutFem(property)) { SolveGpcOnly(da, geometry, property, table); return; }
        var result = BridgeSegmentSections.Create(BridgeDefinitionGoo.Segments(geometry), property.Value, table);
        var properties = result.Rules.Select(r => r.Property).ToArray();
        // R6.2 (lane B): the section and the property of every concio also built on GPC (the dimensions of the table on the material and
        // formulation of the GPC datum of the base property); the rules carry the property datum to Bridge Member Assignment.
        var gpc = GpcSegmentSections.Build(result, property);
        da.SetDataList(0, result.Rules.Select(r => new BridgeDesignGoo(r)));
        da.SetDataList(1, properties.Select((p, i) => { var goo = new GH_BeamProperty(p); if (gpc != null) goo.AttachGpc(gpc[i].Property); return goo; }));
        da.SetDataList(2, properties.Select((p, i) => { var goo = new GH_BeamSection(p.Section); if (gpc != null) goo.AttachGpc(gpc[i].Section); return goo; }));
        da.SetDataList(3, result.Rules.Select(r => $"G{r.Group}-C{r.Segment}"));
        da.SetDataList(4, result.ResolvedTable);
        da.SetDataList(5, properties.Select(p => p.D - p.Centroid.Y));
        double top = property.Value.D - property.Value.Centroid.Y;
        Inform(da, properties.Length, properties.Any(p => Math.Abs(p.D - p.Centroid.Y - top) > BridgeSegmentSections.StationTolerance));
    }

    /// <summary>R6.5 P1 lane 2b: the outputs on the GPC datum of the base property alone (<see cref="BridgeSegmentSections.CreateGpc"/>: the
    /// same checks, tables and messages); the top of the base section from its recipe (<c>GpcSectionGeometry.Former</c>).</summary>
    private void SolveGpcOnly(IGH_DataAccess da, IGH_Goo geometry, GH_BeamProperty property, List<string> table)
    {
        var basis = GpcOnlyProperties.Datum(this, () => property.Gpc);
        if (basis == null) return;
        var result = BridgeSegmentSections.CreateGpc(BridgeDefinitionGoo.Segments(geometry), basis, table);
        da.SetDataList(0, result.Rules.Select(r => new BridgeDesignGoo(r)));
        da.SetDataList(1, result.Rules.Select(r => GH_BeamProperty.FromGpc(r.GpcProperty)));
        da.SetDataList(2, result.Recipes.Select(GH_BeamSection.FromGpc));
        da.SetDataList(3, result.Rules.Select(r => $"G{r.Group}-C{r.Segment}"));
        da.SetDataList(4, result.ResolvedTable);
        da.SetDataList(5, result.Tops);
        var former = GpcOnlyProperties.Datum(this, () => Common.Gpc.GpcSectionGeometry.Former(basis.Data.Section));
        if (former == null) return;
        double top = basis.Data.Section.D - former.YG;
        Inform(da, result.Rules.Length, result.Tops.Any(t => Math.Abs(t - top) > BridgeSegmentSections.StationTolerance));
    }

    private void Inform(IGH_DataAccess da, int count, bool topsDiffer)
    {
        var info = new List<string> { $"{count} sections; one property per concio. Units N, mm, MPa. '*' follows the base section sliders.",
            "Table: ID; start; end; bottom width; top width; height; bottom thickness; top thickness; web thickness; radius.",
            "Tabella conci preserves rows with the same ID and range; new/changed segments reset to '*'. Undo restores an edited panel.",
            "Beam axes and eccentric links remain defined by geometry. Use Top Offsets when changing section height/centroid; this is not an automatic top-flange alignment." };
        if (topsDiffer)
            AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Section top offsets differ from the base section. Check girder reference axes and slab eccentricities; the section table does not realign them.");
        info.AddRange(tableChanges); da.SetDataList(6, info);
        Message = count + " conci";
    }
}
