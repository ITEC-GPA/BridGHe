using System;
using System.Collections.Generic;
using System.Linq;
using BridGHe.Common.Models;
using Rhino.Geometry;

namespace BridGHe.Common.Straus
{
    /// <summary>Spatial lookup preserving the exporter's coordinate ordering and one-based numbering.</summary>
    public sealed class StrausNodeIndex : IDisposable
    {
        private readonly IReadOnlyList<Point3d> _positions;
        private readonly double _tolerance;
        private readonly RTree _tree = new RTree();

        public StrausNodeIndex(IReadOnlyList<NodeModel> nodes, double tolerance)
            : this((nodes ?? throw new ArgumentNullException(nameof(nodes))).Select(n => n.Position).ToArray(), tolerance) { }

        /// <summary>F6 (a)/(b): the same lookup on node positions (a GPC model: its nodes in numbering order).</summary>
        public StrausNodeIndex(IReadOnlyList<Point3d> positions, double tolerance)
        {
            if (!double.IsFinite(tolerance) || tolerance <= 0) throw new ArgumentOutOfRangeException(nameof(tolerance));
            _positions = positions ?? throw new ArgumentNullException(nameof(positions));
            _tolerance = tolerance;
            for (int i = 0; i < positions.Count; i++)
            {
                if (!positions[i].IsValid) { _tree.Dispose(); throw new ArgumentException("Posizione nodale non valida."); }
                _tree.Insert(positions[i], i);
            }
        }

        /// <summary>The coordinate order of the former exporter (NodeModel.Comparer): X, then Y, then Z.</summary>
        private static int Compare(Point3d a, Point3d b)
        {
            var rx = a.X.CompareTo(b.X);
            if (rx != 0) return rx;
            var ry = a.Y.CompareTo(b.Y);
            return ry != 0 ? ry : a.Z.CompareTo(b.Z);
        }

        public (int Number, int Matches) Find(Point3d point)
        {
            if (!point.IsValid) throw new ArgumentException("Posizione di connessione non valida.");
            int found = -1, matches = 0;
            _tree.Search(new Sphere(point, _tolerance), (sender, e) =>
            {
                if (_positions[e.Id].DistanceTo(point) >= _tolerance) return;
                matches++;
                int order = found < 0 ? -1 : Compare(_positions[e.Id], _positions[found]);
                if (order < 0 || (order == 0 && e.Id < found)) found = e.Id;
            });
            if (found < 0) throw new InvalidOperationException($"Nessun nodo FEM entro tolleranza alle coordinate {point}.");
            return (found + 1, matches);
        }

        public void Dispose() => _tree.Dispose();
    }
}
