using System;
using System.Collections.Generic;
using BridGHe.Common.Models;
using Rhino.Geometry;

namespace BridGHe.Common.Straus
{
    /// <summary>Spatial lookup preserving the exporter's coordinate ordering and one-based numbering.</summary>
    public sealed class StrausNodeIndex : IDisposable
    {
        private readonly IReadOnlyList<NodeModel> _nodes;
        private readonly double _tolerance;
        private readonly RTree _tree = new RTree();

        public StrausNodeIndex(IReadOnlyList<NodeModel> nodes, double tolerance)
        {
            if (!double.IsFinite(tolerance) || tolerance <= 0) throw new ArgumentOutOfRangeException(nameof(tolerance));
            _nodes = nodes ?? throw new ArgumentNullException(nameof(nodes));
            _tolerance = tolerance;
            for (int i = 0; i < nodes.Count; i++)
            {
                if (!nodes[i].Position.IsValid) { _tree.Dispose(); throw new ArgumentException("Posizione nodale non valida."); }
                _tree.Insert(nodes[i].Position, i);
            }
        }

        public (int Number, int Matches) Find(Point3d point)
        {
            if (!point.IsValid) throw new ArgumentException("Posizione di connessione non valida.");
            int found = -1, matches = 0;
            _tree.Search(new Sphere(point, _tolerance), (sender, e) =>
            {
                if (_nodes[e.Id].Position.DistanceTo(point) >= _tolerance) return;
                matches++;
                int order = found < 0 ? -1 : NodeModel.Comparer(_nodes[e.Id], _nodes[found]);
                if (order < 0 || (order == 0 && e.Id < found)) found = e.Id;
            });
            if (found < 0) throw new InvalidOperationException($"Nessun nodo FEM entro tolleranza alle coordinate {point}.");
            return (found + 1, matches);
        }

        public void Dispose() => _tree.Dispose();
    }
}
