using System;
using System.Collections.Generic;
using BridGHe.Common.Models;
using Rhino.Geometry;

namespace BridGHe.Common.Straus
{
    public static class NodalLoadAccumulator
    {
        /// <summary>Resultants in the model's global axes and force/moment units, one per load case.</summary>
        public static IReadOnlyDictionary<string, (Vector3d Force, Vector3d Moment)> Sum(IEnumerable<LoadModel> loads)
        {
            var result = new Dictionary<string, (Vector3d Force, Vector3d Moment)>(StringComparer.Ordinal);
            foreach (var load in loads)
            {
                if (load is not NodeForceLoadModel && load is not NodeMomentLoadModel) continue;
                string name = load.LoadCase?.Name;
                if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Caso mancante per un'azione nodale.");
                var value = load is NodeForceLoadModel force ? force.Value : ((NodeMomentLoadModel)load).Value;
                if (!value.IsValid) throw new ArgumentException("Azione nodale non finita nel caso " + name);
                result.TryGetValue(name, out var current);
                if (load is NodeForceLoadModel) current.Force += value;
                else current.Moment += value;
                result[name] = current;
            }
            return result;
        }
    }
}
