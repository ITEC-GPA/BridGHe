using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using BridGHe.Common.Gpc.Legacy;
using Rhino.Geometry;

namespace BridGHe.Grasshopper.DataTypes
{
    /// <summary>
    /// R6.5 P0 (coordinator, 7/11): what a GH element type reads from its GPC draft when it has no FEM value (<c>Value</c> null): the
    /// geometry (the drafts are in definition units, as the Rhino document), the bounding box, the label and the text of <c>ToString</c>
    /// (the layout of the FEM text, with the data the draft has). While the components still build the FEM value beside the draft
    /// (until P1), the types read the FEM value as before.
    /// </summary>
    internal static class GpcDraftGeometry
    {
        internal static Point3d Point(GPC.Geometry.Point3d p) => new Point3d(p.X, p.Y, p.Z);

        internal static Point3d Position(LegacyNodeDraft d) => d?.Node == null ? Point3d.Unset : Point(d.Node.Position);

        internal static Line Axis(LegacyBeamDraft d) => d?.Beam == null ? Line.Unset : new Line(Point(d.Beam.StartPoint), Point(d.Beam.EndPoint));

        internal static Point3d[] Corners(LegacyPlateDraft d) => d?.Corners == null ? Array.Empty<Point3d>() : d.Corners.Select(Point).ToArray();

        /// <summary>The bounding box of a draft (node, beam or plate); empty for anything else.</summary>
        internal static BoundingBox Bounds(object draft)
        {
            var box = BoundingBox.Empty;
            switch (draft)
            {
                case LegacyNodeDraft n: { var p = Position(n); if (p.IsValid) box.Union(p); break; }
                case LegacyBeamDraft b: { var l = Axis(b); if (l.IsValid) { box.Union(l.From); box.Union(l.To); } break; }
                case LegacyPlateDraft p: foreach (var c in Corners(p)) if (c.IsValid) box.Union(c); break;
            }
            return box;
        }

        /// <summary>The BridGHe ID of a draft (the first Element ID, the name of the element otherwise), "" without one.</summary>
        internal static string Label(object draft) => draft switch
        {
            LegacyNodeDraft n => n.Node?.Name ?? "",
            LegacyBeamDraft b => b.Records?.FirstLabel ?? b.Beam?.Name ?? "",
            LegacyPlateDraft p => p.Records?.FirstLabel ?? p.Plate?.Name ?? "",
            _ => "",
        };

        private static string Groups(LegacyElementRecords records) => records == null ? "" : string.Join(", ", records.Groups);

        private const string NoFem = "\nGPC datum (no FEM value)";

        internal static string Text(LegacyNodeDraft d)
        {
            if (d?.Node == null) return "Null Node";
            string text = "BridGHe Node";
            string label = Label(d); if (label.Length != 0) text += $" #{label}";
            text += "\nGroups: " + Groups(d.Records);
            text += $"\nPosition {Position(d)}";
            return text + NoFem;
        }

        internal static string Text(LegacyBeamDraft d)
        {
            if (d?.Beam == null) return "Null Beam";
            var axis = Axis(d);
            string text = "BridGHe Beam";
            string label = Label(d); if (label.Length != 0) text += $" #{label}";
            text += $"\nProperty: {d.Beam.BeamProperty?.Name}";
            text += "\nAngle: " + d.AngleDeg.ToString(CultureInfo.CurrentCulture);
            text += "\nGroups: " + Groups(d.Records);
            text += $"\nFrom point: {axis.From}";
            text += $"\nTo point: {axis.To}";
            text += "\nLenght: " + axis.Length.ToString("F2");
            return text + NoFem;
        }

        internal static string Text(LegacyPlateDraft d)
        {
            if (d?.Plate == null) return "Null Plate";
            string text = "BridGHe Plate";
            string label = Label(d); if (label.Length != 0) text += $" #{label}";
            text += $"\nProperty: {d.Plate.PlateProperty?.Name}";
            text += "\nGroups: " + Groups(d.Records);
            text += "\nPoints: " + string.Join("; ", Corners(d));
            text += "\nAngle: " + d.AngleDeg.ToString(CultureInfo.CurrentCulture); // P2X: the local axis angle, as the FEM text
            return text + NoFem;
        }

        /// <summary>The closed outline of a plate draft (corners and the first again), null with fewer than three corners.</summary>
        internal static PolylineCurve Outline(LegacyPlateDraft d)
        {
            var corners = Corners(d);
            if (corners.Length < 3) return null;
            return new PolylineCurve(corners.Concat(new[] { corners[0] }));
        }

        /// <summary>The flat mesh of a plate draft (a fan from the first corner, as the FEM plate), null with fewer than three corners.</summary>
        internal static Mesh FlatMesh(LegacyPlateDraft d)
        {
            var corners = Corners(d);
            if (corners.Length < 3) return null;
            var mesh = new Mesh();
            mesh.Vertices.AddVertices(corners);
            for (int i = 1; i < corners.Length - 1; i++) mesh.Faces.AddFace(0, i, i + 1);
            mesh.Normals.ComputeNormals();
            return mesh;
        }
    }
}
