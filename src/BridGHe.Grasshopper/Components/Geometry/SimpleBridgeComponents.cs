using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using BridGHe.Common.Geometry;
using BridGHe.Grasshopper.Components.Utilities;
using BridGHe.Grasshopper.DataTypes;
using BridGHe.Grasshopper.Helpers;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;
using Rhino.Display;
using Rhino.Geometry;

namespace BridGHe.Grasshopper.Components.Geometry;

public abstract class SimpleBridgeComponent : BridgeUtilityComponent
{
    private readonly bool truss;
    private readonly List<(SimpleBridgeGeometry Geometry, bool Labels)> preview = new();
    private readonly List<Line> deckPreview = new(), linkPreview = new();
    private static readonly Color[] Colors = { Color.Teal, Color.DarkOrange, Color.SteelBlue, Color.MediumPurple, Color.SlateGray, Color.IndianRed };
    private static readonly DisplayMaterial Concrete = new(Color.LightSlateGray, .78);
    protected SimpleBridgeComponent(bool truss, string name, string nickname) : base(name, nickname,
        "Multispan bridge geometry in mm, from a 3D top-of-deck reference alignment. Generates axes, slab, joints and topology; no materials, sections, loads, supports or releases are assigned.",
        CategoryNameConstants.SUBCATEGORY_MODEL) { this.truss = truss; }

    protected override void RegisterInputParams(GH_InputParamManager p)
    {
        p.AddCurveParameter("Alignment", "A", "Open tangent-continuous curve at deck top reference, in mm. Direction sets stations and local left.", GH_ParamAccess.item);
        p.AddNumberParameter("Support Stations", "S", "Ordered abutment/pier distances along alignment in mm, minimum two. Points only; no restraints.", GH_ParamAccess.list);
        p.AddNumberParameter("Girder Offsets", "Y", "Ordered signed distances: negative right, positive left looking along alignment. mm.", GH_ParamAccess.list);
        p.AddNumberParameter("Girder Levels", "Z", "One or one per girder. Normal offsets below deck top reference, negative in mm. Reference axes, not inferred centroids.", GH_ParamAccess.list, -1500);
        p.AddNumberParameter("Deck Widths", "W", "Left and right distances from alignment; one value makes them equal. mm.", GH_ParamAccess.list, 6000);
        p.AddNumberParameter("Deck Thickness", "H", "Geometric slab thickness along local normal. mm.", GH_ParamAccess.item, 250);
        p.AddNumberParameter("Crossfall", "%", "Optional tree: one value for constant slope %, or one {station mm, slope %} pair per branch covering the alignment. Positive raises left edge.", GH_ParamAccess.tree); p[6].Optional = true;
        p.AddGenericParameter("Segments", "C", "Optional Bridge Segments. Default: maximum 12000 mm, independent of spans.", GH_ParamAccess.item); p[7].Optional = true;
        p.AddGenericParameter("Diaphragms", "D", "Optional Bridge Diaphragms. Default: maximum 5000 mm and support lines; truss X, height 1500 mm, two panels/bay.", GH_ParamAccess.item); p[8].Optional = true;
        p.AddNumberParameter("Maximum Element Length", "L", "Maximum chord length, in mm, on all longitudinal axes and slab edges.", GH_ParamAccess.item, 2500);
        p.AddNumberParameter("Chord Tolerance", "T", "Adaptive deviation tolerance in mm, checked at quarter/mid/three-quarter points of each interval.", GH_ParamAccess.item, 5);
        p.AddNumberParameter("Support Skew", "K", "One angle or one per support, degrees from the local transverse line towards the tangent. Skew ends need sufficient alignment beyond supports.", GH_ParamAccess.list, 0);
        p.AddBooleanParameter("Labels", "ID", "Show girder/segment joints and support IDs in preview.", GH_ParamAccess.item, false);
        p.AddNumberParameter("Deck Beam Spacing", "DS", "Maximum transverse slab spacing along alignment, mm. 0 uses the refined longitudinal grid. Explicit Deck Beam Stations override spacing.", GH_ParamAccess.item, 0);
        p.AddNumberParameter("Deck Beam Stations", "DX", "Optional ordered distances along alignment, mm. Bridge ends are always included. Girders are split at each station.", GH_ParamAccess.list); p[14].Optional = true;
        p.AddNumberParameter("Deck Beam Level", "DZ", "Normal offset from deck top, mm, between -Deck Thickness and 0. Default: -Deck Thickness / 2 (slab mid-plane).", GH_ParamAccess.item); p[15].Optional = true;
    }

    protected override void RegisterOutputParams(GH_OutputParamManager p)
    {
        p.AddCurveParameter("Members", "M", "All member axes. Paths {family;group;segment}. Families: 0 girders, 1 solid diaphragms, 2 top, 3 bottom, 4 posts, 5 diagonals. Connect to Beam with explicit properties.", GH_ParamAccess.tree);
        p.AddTextParameter("Member IDs", "ID", "Same paths/order as Members; deterministic for identical inputs. Connect to Element ID after Beam.", GH_ParamAccess.tree);
        p.AddCurveParameter("Longitudinals", "G", "Girder axes, paths {girder;fabrication segment}, one-based. Joints do not imply releases.", GH_ParamAccess.tree);
        p.AddCurveParameter("Diaphragm Members", "D", "Paths {family;diaphragm line;bay/girder}. Select families before assigning properties.", GH_ParamAccess.tree);
        p.AddPointParameter("Nodes", "N", "Unique member endpoints. Position in list + 1 is the topology node number.", GH_ParamAccess.list);
        p.AddPointParameter("Support Points", "S", "Paths {support;girder}; actual intersections including skew. Geometric points only.", GH_ParamAccess.tree);
        p.AddPointParameter("Joint Points", "J", "Paths {girder;joint}; fabrication joints, not FE hinges.", GH_ParamAccess.tree);
        p.AddMeshParameter("Deck", "DK", "Closed geometric slab mesh; not a plate FEM mesh.", GH_ParamAccess.item);
        p.AddCurveParameter("Deck Edges", "E", "Left and right top edges.", GH_ParamAccess.list);
        p.AddPlaneParameter("Frames", "F", "X towards local left, Y deck normal, Z alignment tangent, at Stations.", GH_ParamAccess.list);
        p.AddNumberParameter("Stations", "X", "Physical mm along reference alignment, common refined longitudinal grid.", GH_ParamAccess.list);
        p.AddTextParameter("Topology", "TB", "Semicolon table: ID, family, group, segment, span, node1, node2, station1, station2. Span=0 for diaphragm members or start overhang.", GH_ParamAccess.list);
        p.AddTextParameter("Segment Ranges", "C", "Girder;segment;start;end in mm. A segment can cross a support.", GH_ParamAccess.list);
        p.AddTextParameter("Information", "I", "Geometry counts, conventions and limitations.", GH_ParamAccess.list);
        p.AddGenericParameter("Bridge Geometry", "BG", "Typed bridge geometry for assignment, bracing, bearings and detailing. mm.", GH_ParamAccess.item);
        p.AddLineParameter("Deck Beams", "DB", "Slab axes at the selected elevation, split at girder axes and deck edges. Paths {station;strip}, one-based. Geometry only.", GH_ParamAccess.tree);
        p.AddLineParameter("Deck Connections", "DC", "Girder axis to slab axis. Paths {station;girder}. Connect to Bridge Connections: mode 0 rigid links or mode 1 explicit beam properties.", GH_ParamAccess.tree);
        p.AddNumberParameter("Deck Beam Stations", "DX", "Actual slab stations along the reference alignment, mm.", GH_ParamAccess.list);
        p.AddNumberParameter("Deck Strip Widths", "DW", "Tributary widths along alignment, mm, same paths as Deck Beams. Half width at ends. Curved/skew deck stiffness needs calibration.", GH_ParamAccess.tree);
        p.AddGenericParameter("Grillage Geometry", "GG", "Steel geometry plus slab family 8. Assign an explicit property to family 8 and add Deck Connections separately to the model.", GH_ParamAccess.item);
        p.AddTextParameter("Deck Member IDs", "DI", "Geometric IDs, same paths as Deck Beams.", GH_ParamAccess.tree);
    }

    protected override void BeforeSolveInstance() { base.BeforeSolveInstance(); preview.Clear(); deckPreview.Clear(); linkPreview.Clear(); }
    protected virtual bool TrussMode(IGH_DataAccess da) => truss;
    protected override void Solve(IGH_DataAccess da)
    {
        Curve alignment = null; var supports = new List<double>(); var offsets = new List<double>(); var levels = new List<double>();
        var widths = new List<double>(); var skew = new List<double>(); double thickness = 250, maxLength = 2500, tolerance = 5; bool labels = false;
        if (!da.GetData(0, ref alignment) || !da.GetDataList(1, supports) || !da.GetDataList(2, offsets)) return;
        da.GetDataList(3, levels); da.GetDataList(4, widths); da.GetData(5, ref thickness);
        da.GetData(9, ref maxLength); da.GetData(10, ref tolerance); da.GetDataList(11, skew); da.GetData(12, ref labels);
        double deckSpacing = 0, deckLevel = 0; var deckStations = new List<double>();
        da.GetData(13, ref deckSpacing); da.GetDataList(14, deckStations); bool hasDeckLevel = da.GetData(15, ref deckLevel);
        double[] slopeStations = Array.Empty<double>(), slopes = new[] { 0d };
        if (da.GetDataTree(6, out GH_Structure<GH_Number> crossfall) && crossfall.DataCount > 0)
        {
            if (crossfall.PathCount == 1 && crossfall.Branches[0].Count == 1) slopes = new[] { crossfall.Branches[0][0].Value };
            else
            {
                if (crossfall.Branches.Any(b => b.Count != 2)) throw new ArgumentException("Crossfall requires one constant value or one station/percent pair per branch.");
                slopeStations = crossfall.Branches.Select(b => b[0].Value).ToArray(); slopes = crossfall.Branches.Select(b => b[1].Value).ToArray();
            }
        }
        BridgeLayoutGoo segments = null, diaphragms = null;
        bool hasSegments = da.GetData(7, ref segments), hasDiaphragms = da.GetData(8, ref diaphragms);
        if (hasSegments && segments?.Value is not BridgeSegments) throw new ArgumentException("Segments requires Bridge Segments.");
        if (hasDiaphragms && diaphragms?.Value is not BridgeDiaphragms) throw new ArgumentException("Diaphragms requires Bridge Diaphragms.");
        var input = new SimpleBridgeInput
        {
            Alignment = alignment, Supports = supports.ToArray(), GirderOffsets = offsets.ToArray(), GirderLevels = levels.ToArray(),
            DeckWidths = widths.ToArray(), DeckThickness = thickness, SupportSkewDegrees = skew.ToArray(),
            CrossfallStations = slopeStations, CrossfallPercent = slopes, Segments = segments?.Value as BridgeSegments ?? new(),
            Diaphragms = diaphragms?.Value as BridgeDiaphragms ?? new(), MaximumElementLength = maxLength, ChordTolerance = tolerance, Truss = TrussMode(da),
            DeckBeamSpacing = deckSpacing, DeckBeamStations = deckStations.ToArray(), DeckBeamLevel = hasDeckLevel ? deckLevel : null
        };
        var result = SimpleBridgeBuilder.Build(input);
        var grillage = BridgeDeckGrillage.Build(input, result);
        preview.Add((result, labels));
        deckPreview.AddRange(grillage.Strips.Select(s => s.Member.Axis)); linkPreview.AddRange(grillage.Connections.Select(c => c.Axis));
        var members = new GH_Structure<GH_Curve>(); var ids = new GH_Structure<GH_String>();
        var girders = new GH_Structure<GH_Curve>(); var cross = new GH_Structure<GH_Curve>();
        foreach (var member in result.Members)
        {
            var path = new GH_Path((int)member.Family, member.Group, member.Segment); var line = new GH_Curve(new LineCurve(member.Axis));
            members.Append(line, path); ids.Append(new GH_String(member.Id), path);
            if (member.Family == BridgeMemberFamily.Girder) girders.Append(line, new GH_Path(member.Group, member.Segment));
            else cross.Append(line, path);
        }
        var supportPoints = new GH_Structure<GH_Point>(); var jointPoints = new GH_Structure<GH_Point>();
        foreach (var s in result.Supports) supportPoints.Append(new GH_Point(s.Point), new GH_Path(s.Support, s.Girder));
        foreach (var j in result.Joints) jointPoints.Append(new GH_Point(j.Point), new GH_Path(j.Girder, j.Joint));
        da.SetDataTree(0, members); da.SetDataTree(1, ids); da.SetDataTree(2, girders); da.SetDataTree(3, cross);
        da.SetDataList(4, result.Nodes); da.SetDataTree(5, supportPoints); da.SetDataTree(6, jointPoints); da.SetData(7, result.Deck);
        da.SetDataList(8, new Curve[] { new PolylineCurve(result.LeftEdge), new PolylineCurve(result.RightEdge) });
        da.SetDataList(9, result.Frames); da.SetDataList(10, result.Stations);
        da.SetDataList(11, new[] { "ID;Family;Group;Segment;Span;Node1;Node2;Station1_mm;Station2_mm" }.Concat(result.Members.Select(m =>
            FormattableString.Invariant($"{m.Id};{m.Family};{m.Group};{m.Segment};{m.Span};{m.NodeFrom};{m.NodeTo};{m.StationFrom:G17};{m.StationTo:G17}"))));
        da.SetDataList(12, new[] { "Girder;Segment;Start_mm;End_mm" }.Concat(result.Segments.Select(s =>
            FormattableString.Invariant($"{s.Girder};{s.Segment};{s.Start:G17};{s.End:G17}"))));
        da.SetDataList(13, grillage.Geometry.Diagnostics); Message = $"{result.Members.Count} + {grillage.Strips.Length} axes";
        da.SetData(14, new BridgeGeometryGoo(result));
        var deckLines = new GH_Structure<GH_Line>(); var links = new GH_Structure<GH_Line>();
        var stripWidths = new GH_Structure<GH_Number>(); var deckIds = new GH_Structure<GH_String>();
        foreach (var strip in grillage.Strips)
        {
            var path = new GH_Path(strip.Member.Group, strip.Member.Segment);
            deckLines.Append(new GH_Line(strip.Member.Axis), path); stripWidths.Append(new GH_Number(strip.Width), path);
            deckIds.Append(new GH_String(strip.Member.Id), path);
        }
        foreach (var link in grillage.Connections) links.Append(new GH_Line(link.Axis), new GH_Path(link.Station, link.Girder));
        da.SetDataTree(15, deckLines); da.SetDataTree(16, links); da.SetDataList(17, grillage.Stations);
        da.SetDataTree(18, stripWidths); da.SetData(19, new BridgeGeometryGoo(grillage.Geometry)); da.SetDataTree(20, deckIds);
    }

    public override BoundingBox ClippingBox
    {
        get { var box = BoundingBox.Empty; foreach (var item in preview) { box.Union(item.Geometry.Deck.GetBoundingBox(false)); foreach (var p in item.Geometry.Nodes) box.Union(p); } return box; }
    }
    public override void DrawViewportWires(IGH_PreviewArgs args)
    {
        foreach (var line in deckPreview) args.Display.DrawLine(line, Attributes.Selected ? args.WireColour_Selected : Color.SeaGreen, 2);
        foreach (var line in linkPreview) args.Display.DrawLine(line, Attributes.Selected ? args.WireColour_Selected : Color.DarkViolet, 1);
        foreach (var item in preview)
        {
            foreach (var m in item.Geometry.Members)
                args.Display.DrawLine(m.Axis, Attributes.Selected ? args.WireColour_Selected :
                    m.Family == BridgeMemberFamily.Girder ? Colors[(m.Segment - 1) % 3] : Colors[(int)m.Family], 2);
            if (item.Labels)
            {
                foreach (var s in item.Geometry.Supports) args.Display.DrawDot(s.Point, $"S{s.Support}/G{s.Girder}", Color.DarkRed, Color.White);
                foreach (var j in item.Geometry.Joints) args.Display.DrawDot(j.Point, $"G{j.Girder}/J{j.Joint}", Color.DarkSlateGray, Color.White);
            }
        }
    }
    public override void DrawViewportMeshes(IGH_PreviewArgs args)
    { foreach (var item in preview) args.Display.DrawMeshShaded(item.Geometry.Deck, Concrete); }
}

public sealed class SimpleBridgeSolidComponent : SimpleBridgeComponent
{
    public SimpleBridgeSolidComponent() : base(false, "Simple Bridge — Solid Diaphragms", "BridgeSolid") { }
    public override Guid ComponentGuid => new Guid("e3eeef52-dba7-4bb0-8131-57d6956edf58");
}

public sealed class SimpleBridgeTrussComponent : SimpleBridgeComponent
{
    public SimpleBridgeTrussComponent() : base(true, "Simple Bridge — Truss Diaphragms", "BridgeTruss") { }
    public override Guid ComponentGuid => new Guid("dc7cfadf-84f8-4ebd-8a49-17782694754e");
}
