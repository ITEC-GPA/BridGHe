using System;
using System.Collections.Generic;
using System.Linq;
using BridGHe.Common.Helpers;
using BridGHe.Common.Models;
using Rhino.Geometry;

namespace BridGHe.Common.Geometry;

public sealed record BridgeDeckResult(SimpleBridgeGeometry Geometry, Mesh Mesh, PlateModel[] Plates, Line[] Connections, double[] Stations, string[] Diagnostics)
{
    /// <summary>R6.2 lane A (dual rule until R6.5): the GPC draft of every plate (when the GPC property is given), built on GPC with the plate
    /// builders from the same data (corners, property, pour group under Deck, plate ID), with the identity of the conversion of its FEM value.</summary>
    public BridGHe.Common.Gpc.Legacy.LegacyPlateDraft[] Drafts { get; init; }

    /// <summary>R6.5 P1 lane 1b: what every plate is built from besides the GPC property, in the order of <see cref="Plates"/>: its corners,
    /// its group (from the root) and its plate ID (the parameters of its identity by content without a FEM value).</summary>
    public (Point3d[] Corners, string[] Group, string PlateId)[] Plans { get; init; }
}

public static class BridgeDeckMesh
{
    /// <param name="gpcProperty">R6.2 lane A: the GPC property of the Plate Property datum (its <c>Gpc</c>); when given the result has the GPC
    /// drafts of the plates.</param>
    /// <param name="gpcMembraneBending">L2C: the bending thickness of a membrane given by its datum alone (beside the GPC membrane, which has none).</param>
    public static BridgeDeckResult Create(SimpleBridgeGeometry source, PlatePropertyModel property,
        double maximumLongitudinal, double maximumTransverse, IReadOnlyList<double> pourBoundaries, bool triangles,
        GPC.Model.ElementProperties.PlateProperty gpcProperty = null, double gpcMembraneBending = 0)
    {
        BridgeGeometryTools.Validate(source);
        if (!double.IsFinite(source.DeckThickness) || source.DeckThickness <= 0 || source.LeftEdge.Count != source.Stations.Count ||
            source.RightEdge.Count != source.Stations.Count)
            throw new ArgumentException("Bridge Geometry needs current slab thickness and edge data; recompute Simple Bridge.");
        // R6.5 P1 lane 2a: a property given by its GPC datum alone (property null, gpcProperty given) is checked on the datum, with the same
        // messages: its formulation and its thicknesses (N-mm, as the draft units). L2C (lane 2c, as in 0.3.0): the bending thickness of a
        // membrane is the one recorded beside its datum (the GPC membrane has none). No FEM plates are made then (only the drafts and the plans).
        bool gpcOnly = property == null && gpcProperty != null, loadPatch;
        if (gpcOnly)
        {
            if (gpcProperty is not GPC.Model.ElementProperties.ElasticPlateProperty elastic)
                throw new ArgumentException("Provide a valid plate property in N-mm.");
            loadPatch = elastic.Formulation == GPC.Model.ElementProperties.ElasticPlateFormulation.LoadPatch;
            if (!loadPatch && (Math.Abs(elastic.MembraneThickness - source.DeckThickness) > 1e-6 ||
                Math.Abs((elastic.Formulation == GPC.Model.ElementProperties.ElasticPlateFormulation.Membrane ? gpcMembraneBending : elastic.BendingThickness) - source.DeckThickness) > 1e-6))
                throw new ArgumentException("Structural deck membrane/bending thickness must match the geometric slab. Use explicit properties consistent with the model.");
        }
        else
        {
            if (property?.IsValid != true || !Enum.IsDefined(property.PropertyType) || property.PropertyType == PlatePropertyType.Undefined)
                throw new ArgumentException("Provide a valid plate property in N-mm.");
            if (property.PropertyType != PlatePropertyType.LoadPatch &&
                (Math.Abs(property.MembraneThickness - source.DeckThickness) > 1e-6 ||
                Math.Abs(property.BendingThickness - source.DeckThickness) > 1e-6))
                throw new ArgumentException("Structural deck membrane/bending thickness must match the geometric slab. Use explicit properties consistent with the model.");
            loadPatch = property.PropertyType == PlatePropertyType.LoadPatch;
        }
        if (!double.IsFinite(maximumLongitudinal) || !double.IsFinite(maximumTransverse) || maximumLongitudinal <= 0 || maximumTransverse <= 0)
            throw new ArgumentException("Mesh lengths must be finite and positive, mm.");
        pourBoundaries ??= Array.Empty<double>();
        if (pourBoundaries.Any(s => !double.IsFinite(s) || s <= source.Stations[0] || s >= source.Stations[^1]) ||
            pourBoundaries.Zip(pourBoundaries.Skip(1)).Any(p => p.First >= p.Second))
            throw new ArgumentException("Pour boundaries must be distinct, ordered internal stations in mm.");
        var mandatory = source.Stations.Concat(pourBoundaries).Distinct().OrderBy(s => s).ToArray();
        var rowStations = new List<double> { mandatory[0] };
        for (int i = 0; i < mandatory.Length - 1; i++)
        {
            double count = Math.Ceiling((mandatory[i + 1] - mandatory[i]) / maximumLongitudinal);
            if (count + rowStations.Count > 20000) throw new ArgumentException("Deck mesh exceeds 20000 longitudinal stations.");
            for (int j = 1; j <= count; j++) rowStations.Add(mandatory[i] + (mandatory[i + 1] - mandatory[i]) * j / count);
        }
        double maxWidth = source.LeftEdge.Zip(source.RightEdge, (a, b) => a.DistanceTo(b)).Max();
        double acrossCount = Math.Ceiling(maxWidth / maximumTransverse);
        if (acrossCount > 1000) throw new ArgumentException("Deck mesh exceeds 1000 transverse divisions.");
        var across = source.RightEdge[0] - source.LeftEdge[0];
        int girderCount = source.Segments.Max(s => s.Girder);
        var girderFractions = Enumerable.Range(1, girderCount).Select(g =>
            (BridgeGeometryTools.GirderPoint(source, g, source.Stations[0]) - source.LeftEdge[0]) * across / across.SquareLength).ToArray();
        var fractionList = new List<double>(Enumerable.Range(0, (int)acrossCount + 1).Select(i => i / acrossCount));
        foreach (double f in girderFractions)
        {
            if (f < -1e-9 || f > 1 + 1e-9) throw new ArgumentException("Girder projection lies outside deck mesh.");
            fractionList.RemoveAll(v => Math.Abs(v - f) < 1e-9); fractionList.Add(Math.Clamp(f, 0, 1));
        }
        var fractions = fractionList.Distinct().OrderBy(x => x).ToArray();
        if ((long)(rowStations.Count - 1) * (fractions.Length - 1) * (triangles ? 2 : 1) > 100000)
            throw new ArgumentException("Deck mesh exceeds 100000 plates; increase mesh spacing.");
        using var editor = new BridgeGeometryEditor(source);
        var mesh = new Mesh(); mesh.Vertices.UseDoublePrecisionVertices = true;
        var connectors = new List<Line>(); var plates = new List<PlateModel>(); var pp = gpcOnly ? null : ModelArchive.Clone(property);
        var drafts = gpcProperty == null ? null : new List<BridGHe.Common.Gpc.Legacy.LegacyPlateDraft>();
        var plans = new List<(Point3d[], string[], string)>();
        Point3d Edge(Polyline edge, double station)
        {
            int index = source.Stations.BinarySearch(station);
            if (index >= 0) return edge[index];
            index = Math.Clamp(~index - 1, 0, source.Stations.Count - 2);
            double t = (station - source.Stations[index]) / (source.Stations[index + 1] - source.Stations[index]);
            return edge[index] + t * (edge[index + 1] - edge[index]);
        }
        for (int row = 0; row < rowStations.Count; row++)
        {
            double s = rowStations[row]; var left = Edge(source.LeftEdge, s); var right = Edge(source.RightEdge, s);
            var normal = BridgeGeometryTools.FrameAt(source, s).YAxis;
            foreach (double f in fractions) mesh.Vertices.Add(left + f * (right - left) - source.DeckThickness / 2 * normal);
            for (int g = 1; g <= girderCount; g++)
            {
                var beamPoint = BridgeGeometryTools.GirderPoint(source, g, s); editor.SplitAt(beamPoint);
                int column = Array.FindIndex(fractions, f => Math.Abs(f - girderFractions[g - 1]) < 1e-8);
                var deckPoint = mesh.Vertices.Point3dAt(row * fractions.Length + column);
                if (beamPoint.DistanceTo(deckPoint) > SimpleBridgeBuilder.JoinTolerance) connectors.Add(new(beamPoint, deckPoint));
            }
        }
        void Face(int[] indices, int pour)
        {
            var points = indices.Select(mesh.Vertices.Point3dAt).ToArray();
            var cross = Vector3d.CrossProduct(points[1] - points[0], points[2] - points[0]);
            if (cross.Length <= 1e-8) throw new ArgumentException("Degenerate deck face.");
            if (indices.Length == 3) mesh.Faces.AddFace(indices[0], indices[1], indices[2]);
            else mesh.Faces.AddFace(indices[0], indices[1], indices[2], indices[3]);
            // P2K (P1): the plate ID by its order; the FEM plate only on the transitional FEM path (property given), none on the GPC path.
            string plateId = $"Deck{plans.Count + 1}";
            var p = gpcOnly ? null : new PlateModel(points, pp, new GroupModel($"Pour{pour}", new GroupModel("Deck"))) { PlateId = plateId };
            // R6.2 lane A: the same plate on GPC (the reader of the former plate: corners, property, the group Deck\Pour<n> with its parent, plate ID).
            // R6.5 P1 lane 2a: without a FEM property the draft takes a provisional identity of its plate ID here (the component gives it the
            // identity by content of lane 1b).
            drafts?.Add(BridGHe.Common.Gpc.Builders.GpcPlateBuilders.Create(points.Select(q => (q.X, q.Y, q.Z)).ToArray(), gpcProperty,
                new[] { new[] { "Deck", $"Pour{pour}" } }, 0, 0, plateId, gpcOnly ? BridGHe.Common.Gpc.GpcIdentity.Derive(BridGHe.Common.Gpc.GpcIdentity.NameSpace, "BridgeDeckMesh:" + plateId) : BridGHe.Common.Gpc.GpcBeamDrafts.Identity(p)));
            plans.Add((points, new[] { "Deck", $"Pour{pour}" }, plateId));
            if (p != null) plates.Add(p);
        }
        for (int row = 0; row < rowStations.Count - 1; row++)
        {
            int pour = 1 + pourBoundaries.Count(s => s < (rowStations[row] + rowStations[row + 1]) / 2);
            for (int col = 0; col < fractions.Length - 1; col++)
            {
                int a = row * fractions.Length + col, b = a + 1, d = a + fractions.Length, c = d + 1;
                // Rightward transverse axis crossed with forward station gives upward face normal.
                if (triangles) { Face(new[] { a, b, c }, pour); Face(new[] { a, c, d }, pour); }
                else Face(new[] { a, b, c, d }, pour);
            }
        }
        mesh.Normals.ComputeNormals();
        return new(editor.Geometry, mesh, plates.ToArray(), connectors.ToArray(), rowStations.ToArray(), new[]
        {
            $"{plans.Count} plates at slab mid-surface, {pourBoundaries.Count + 1} pour groups, {connectors.Count} beam/deck connection lines.",
            "Connection lines are geometry only: feed them to Bridge Connections and deliberately choose rigid or deformable mechanics.",
            loadPatch ? "Load-patch deck property: no structural slab stiffness is intended." :
                "Structural deck plates: assign steel-only beam properties to avoid counting concrete stiffness twice.",
            "No automatic beam/plate connection is implied by visual overlap. Triangles are recommended for warped decks; mesh convergence remains a separate analysis."
        }) { Drafts = drafts?.ToArray(), Plans = plans.ToArray() };
    }
}
