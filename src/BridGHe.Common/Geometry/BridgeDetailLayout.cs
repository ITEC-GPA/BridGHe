using System;
using System.Collections.Generic;
using System.Linq;
using GPC.Checkers.CompositeBridge;
using Rhino.Geometry;

namespace BridGHe.Common.Geometry;

public sealed record BridgeDetailPlacement(string Id, int Girder, double Station, object Definition, string Kind);
public sealed record BridgeDetailLayoutResult(Line[] StudAxes, Circle[] StudHeads, Mesh[] Stiffeners, BridgeDetailPlacement[] Details, string[] Diagnostics);

public static class BridgeDetailLayout
{
    public static BridgeDetailLayoutResult Create(SimpleBridgeGeometry geometry, IReadOnlyList<int> selectedGirders,
        double start, double end, HSectionDimensions section, double steelTopOffset, BridgeStudOptions studs,
        BridgeIntermediateStiffener intermediate, BridgeSupportStiffener support, double stiffenerPitch,
        IReadOnlyList<double> stiffenerStations)
    {
        BridgeGeometryTools.Validate(geometry);
        if (!double.IsFinite(start) || !double.IsFinite(end) || start < 0 || end <= start || end > geometry.Stations[^1] ||
            !double.IsFinite(steelTopOffset) || section == null || !Enum.IsDefined(section.SectionType) ||
            new[] { section.WebHeight, section.WebThickness, section.TopWidth, section.TopThickness }.Any(v => !double.IsFinite(v) || v <= 0) ||
            !double.IsFinite(section.WebOffset))
            throw new ArgumentException("Provide a valid layout interval, steel top offset and gPC steel section geometry, mm.");
        bool box = section.SectionType == BridgeSteelSectionType.Box;
        if (box && (!double.IsFinite(section.WebSpacing) || section.WebSpacing <= 0 || section.WebSpacing - 2 * section.WebOffset <= 0))
            throw new ArgumentException("Invalid box web spacing/offset.");
        var available = geometry.Segments.Select(s => s.Girder).Distinct().ToArray();
        selectedGirders = selectedGirders == null || selectedGirders.Count == 0 ? available : selectedGirders;
        if (selectedGirders.Any(g => !available.Contains(g)) || selectedGirders.Distinct().Count() != selectedGirders.Count)
            throw new ArgumentException("Invalid or duplicate girder selector.");
        var axes = new List<Line>(); var heads = new List<Circle>(); var plates = new List<Mesh>(); var details = new List<BridgeDetailPlacement>();
        if (studs?.Enabled == true)
        {
            if (!double.IsFinite(studs.CountPerRow) || studs.CountPerRow < 1 || studs.CountPerRow > 20 ||
                studs.CountPerRow != Math.Truncate(studs.CountPerRow) || box && studs.CountPerRow % 2 != 0 ||
                new[] { studs.Diameter, studs.Height, studs.HeadDiameter, studs.HeadThickness, studs.LongitudinalPitch }.Any(v => !double.IsFinite(v) || v <= 0) ||
                !double.IsFinite(studs.TransversePitch) || studs.TransversePitch < 0 || !double.IsFinite(studs.RequiredCover) || studs.RequiredCover < 0 ||
                studs.HeadDiameter < studs.Diameter || studs.Height <= studs.HeadThickness || studs.LongitudinalPitch < studs.HeadDiameter)
                throw new ArgumentException("Invalid stud dimensions/pitches. Box layouts require an even total CountPerRow.");
            int perFlange = (int)(box ? studs.CountPerRow / 2 : studs.CountPerRow);
            if (perFlange > 1 && studs.TransversePitch < studs.HeadDiameter ||
                (perFlange - 1) * studs.TransversePitch + studs.HeadDiameter > section.TopWidth)
                throw new ArgumentException("Stud heads overlap or exceed the top flange width.");
            double rowsValue = Math.Ceiling((end - start) / studs.LongitudinalPitch);
            if (rowsValue * studs.CountPerRow * selectedGirders.Count > 100000) throw new ArgumentException("Detail layout exceeds 100000 studs.");
            int rows = (int)rowsValue;
            foreach (int g in selectedGirders)
                for (int row = 0; row < rows; row++)
                {
                    double s = start + row * studs.LongitudinalPitch;
                    var frame = BridgeGeometryTools.FrameAt(geometry, s); var basePoint = BridgeGeometryTools.GirderPoint(geometry, g, s) + steelTopOffset * frame.YAxis;
                    if ((basePoint - frame.Origin) * frame.YAxis + studs.Height + studs.RequiredCover > 1e-5)
                        throw new ArgumentException($"Stud row on G{g} at {s:G9} mm exceeds deck top/cover; check Steel Top Offset and stud height.");
                    foreach (double centre in box ? new[] { -section.WebSpacing / 2, section.WebSpacing / 2 } : new[] { 0d })
                        for (int j = 0; j < perFlange; j++)
                        {
                            double x = centre + (j - (perFlange - 1) / 2d) * studs.TransversePitch;
                            var p = basePoint + x * frame.XAxis; var q = p + studs.Height * frame.YAxis;
                            axes.Add(new(p, q)); heads.Add(new Circle(new Plane(q, frame.YAxis), studs.HeadDiameter / 2));
                        }
                    details.Add(new($"Stud/G{g}/{s:G17}", g, s, studs, "Stud row"));
                }
        }
        // Stiffener placement is independent of stud pitch.
        AddStiffeners(geometry, selectedGirders, start, end, section, steelTopOffset, intermediate, support,
            stiffenerPitch, stiffenerStations, plates, details);
        return new(axes.ToArray(), heads.ToArray(), plates.ToArray(), details.ToArray(), new[]
        {
            $"{axes.Count} studs, {plates.Count} stiffener plates; intervals are [Start,End), avoiding duplicate details across adjacent zones.",
            "Uses original gPC detail records; stiffener adjacent-panel lengths derive from the generated layout.",
            "Geometry and quantities only. No automatic FEM stiffness, welding design or collision clearance is inferred."
        });
    }
    private static void AddStiffeners(SimpleBridgeGeometry geometry, IReadOnlyList<int> girders, double start, double end,
        HSectionDimensions section, double topOffset, BridgeIntermediateStiffener intermediate, BridgeSupportStiffener support,
        double pitch, IReadOnlyList<double> requested, List<Mesh> plates, List<BridgeDetailPlacement> details)
    {
        requested ??= Array.Empty<double>();
        if (!double.IsFinite(pitch) || pitch <= 0 || requested.Any(s => !double.IsFinite(s) || s < start || s >= end) ||
            requested.Zip(requested.Skip(1)).Any(p => p.First >= p.Second))
            throw new ArgumentException("Provide positive stiffener pitch and ordered explicit stations in [Start,End).");
        double count = Math.Ceiling((end - start) / pitch);
        if (requested.Count == 0 && count > 10000) throw new ArgumentException("Too many stiffener stations.");
        var automatic = requested.Count > 0 ? requested.ToArray() : Enumerable.Range(0, (int)count).Select(i => start + i * pitch).ToArray();
        bool box = section.SectionType == BridgeSteelSectionType.Box;
        foreach (int g in girders)
        {
            var supports = support?.Enabled == true ? geometry.Supports.Where(p => p.Girder == g && p.Station >= start && p.Station < end)
                .Select(p => p.Station).ToArray() : Array.Empty<double>();
            var locations = (intermediate?.Enabled == true ? automatic : Array.Empty<double>()).Concat(supports).Distinct().OrderBy(s => s).ToArray();
            for (int i = 0; i < locations.Length; i++)
            {
                double s = locations[i], left = s - (i == 0 ? start : locations[i - 1]), right = (i == locations.Length - 1 ? end : locations[i + 1]) - s;
                bool atSupport = supports.Any(p => Math.Abs(p - s) <= 1e-5);
                BridgeStiffener definition = atSupport ? support with { LeftPanel = left, RightPanel = right } :
                    intermediate with { LeftPanel = left, RightPanel = right, EqualPanels = false };
                if (!Enum.IsDefined(definition.Layout) || new[] { definition.Width, definition.Thickness, definition.RightWidth, definition.RightThickness }.Any(v => !double.IsFinite(v) || v <= 0))
                    throw new ArgumentException("Invalid gPC stiffener layout or dimensions.");
                void Pair(double station, string suffix)
                {
                    var frame = BridgeGeometryTools.FrameAt(geometry, station);
                    var top = BridgeGeometryTools.GirderPoint(geometry, g, station) + (topOffset - section.TopThickness) * frame.YAxis;
                    foreach (double x in box ? new[] { -section.WebSpacing / 2, section.WebSpacing / 2 } : new[] { 0d })
                    {
                        double bottomX = box ? x - Math.Sign(x) * section.WebOffset :
                            section.SectionType == BridgeSteelSectionType.InclinedWebH ? -section.WebOffset : 0;
                        var a = top + x * frame.XAxis; var b = top + bottomX * frame.XAxis - section.WebHeight * frame.YAxis;
                        var normal = Vector3d.CrossProduct(frame.ZAxis, b - a); normal.Unitize();
                        if (normal * frame.XAxis < 0) normal.Reverse();
                        if (definition.Layout != BridgeStiffenerLayout.RightOnly)
                            plates.Add(Prism(a, b, normal, definition.Width, definition.Thickness, section.WebThickness, frame.ZAxis));
                        if (definition.Layout != BridgeStiffenerLayout.LeftOnly)
                            plates.Add(Prism(a, b, -normal, definition.Layout == BridgeStiffenerLayout.Unequal ? definition.RightWidth : definition.Width,
                                definition.Layout == BridgeStiffenerLayout.Unequal ? definition.RightThickness : definition.Thickness, section.WebThickness, frame.ZAxis));
                    }
                    details.Add(new($"{(atSupport ? "SupportStiffener" : "Stiffener")}/G{g}/{station:G17}{suffix}", g, station, definition,
                        atSupport ? "Support stiffener" : "Intermediate stiffener"));
                }
                Pair(s, "");
                if (atSupport && support.RigidEndPost)
                {
                    double spacing = support.EndPostSpacing;
                    if (!double.IsFinite(spacing) || spacing <= 0) throw new ArgumentException("Rigid end post spacing must be positive.");
                    double next = s + (support.Location == BridgeSupportLocation.RightEnd ? -spacing : spacing);
                    if (support.Location == BridgeSupportLocation.Internal || next < start || next >= end)
                        throw new ArgumentException("Rigid end post needs an end support and a second pair inside the layout interval.");
                    Pair(next, "/EndPost");
                }
            }
        }
    }
    private static Mesh Prism(Point3d top, Point3d bottom, Vector3d normal, double width, double thickness, double webThickness, Vector3d tangent)
    {
        var a = top + webThickness / 2 * normal; var b = bottom + webThickness / 2 * normal;
        var face = new[] { a, b, b + width * normal, a + width * normal }; var mesh = new Mesh(); mesh.Vertices.UseDoublePrecisionVertices = true;
        foreach (double sign in new[] { -1d, 1d }) foreach (var p in face) mesh.Vertices.Add(p + sign * thickness / 2 * tangent);
        mesh.Faces.AddFace(0, 3, 2, 1); mesh.Faces.AddFace(4, 5, 6, 7);
        for (int i = 0; i < 4; i++) mesh.Faces.AddFace(i, (i + 1) % 4, (i + 1) % 4 + 4, i + 4);
        mesh.Normals.ComputeNormals(); return mesh;
    }
}
