using System;
using System.Collections.Generic;
using System.Linq;
using Rhino.Geometry;

namespace BridGHe.Common.Geometry;

/// <summary>
/// R6.2, lane B: Merge Geometry without the former FEM helpers (<c>ModelHelper</c> goes with FeMM at R6.5): the points, curves and meshes merged
/// by tolerance, the operations of <c>ModelHelper.MergeGeometry</c> one for one (regression <c>gpc-builders-b</c> compares the two).
/// </summary>
public static class BridgeGeometryMerge
{
    /// <summary>Every point is resolved to the nearest point already kept within the tolerance (ties: the first kept), otherwise kept; the
    /// ends of the curves and the vertices of the meshes (copies) are moved onto the resolved points, the meshes cleaned.</summary>
    public static void Merge(ref List<Point3d> inputNodes, ref List<Curve> inputBeams, ref List<Mesh> inputMeshes, double tolerance)
    {
        if (!double.IsFinite(tolerance) || tolerance < 0) throw new ArgumentOutOfRangeException(nameof(tolerance));
        var points = new List<Point3d>();
        using var tree = new RTree();
        Point3d Resolve(Point3d point)
        {
            int index = -1;
            double nearest = double.PositiveInfinity;
            tree.Search(new Sphere(point, tolerance), (sender, e) =>
            {
                double distance = points[e.Id].DistanceToSquared(point);
                if (distance < nearest || (distance == nearest && e.Id < index))
                { index = e.Id; nearest = distance; }
            });
            if (index >= 0) return points[index];
            tree.Insert(point, points.Count);
            points.Add(point);
            return point;
        }
        foreach (var point in inputNodes) Resolve(point);
        var beams = inputBeams.Select(c => c.DuplicateCurve()).ToList();
        foreach (var beam in beams)
        {
            beam.SetStartPoint(Resolve(beam.PointAtStart));
            beam.SetEndPoint(Resolve(beam.PointAtEnd));
        }
        var meshes = inputMeshes.Select(m => m.DuplicateMesh()).ToList();
        foreach (var mesh in meshes)
        {
            mesh.Vertices.UseDoublePrecisionVertices = true;
            // Remap all vertices before changing topology, preserving every face's winding.
            for (int i = 0; i < mesh.Vertices.Count; i++)
                mesh.Vertices.SetVertex(i, Resolve(mesh.Vertices.Point3dAt(i)));
            mesh.Vertices.CombineIdentical(true, true);
            mesh.Faces.CullDegenerateFaces();
            mesh.Vertices.CullUnused();
            mesh.Normals.ComputeNormals();
            mesh.Compact();
        }
        inputNodes = points;
        inputBeams = beams;
        inputMeshes = meshes;
    }
}
