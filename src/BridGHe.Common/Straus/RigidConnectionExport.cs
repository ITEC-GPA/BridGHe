using System;
using System.Collections.Generic;
using System.Linq;
using BridGHe.Common.Models;
using St7API;

namespace BridGHe.Common.Straus;

public static class RigidConnectionExport
{
    public static (int From, int To)[] Resolve(ModelModel model, double tolerance)
    {
        if (!model.Nodes.Any(n => n.Attributes.OfType<RigidConnectionAttributeModel>().Any())) return Array.Empty<(int, int)>();
        using var index = new StrausNodeIndex(model.Nodes, tolerance);
        var links = new HashSet<(int, int)>(); var result = new List<(int, int)>();
        for (int i = 0; i < model.Nodes.Count; i++)
            foreach (var connection in model.Nodes[i].Attributes.OfType<RigidConnectionAttributeModel>())
            {
                var target = index.Find(connection.Value);
                if (target.Matches != 1) throw new ArgumentException("Rigid link target matches more than one FEM node. Merge nodes before export.");
                if (target.Number == i + 1) throw new ArgumentException("Rigid link collapses to the same node within model tolerance.");
                var pair = (Math.Min(i + 1, target.Number), Math.Max(i + 1, target.Number));
                if (!links.Add(pair)) throw new ArgumentException("Duplicate rigid link after node merging.");
                result.Add((i + 1, target.Number));
            }
        return result.ToArray();
    }
    public static void Export(int modelId, ModelModel model, double tolerance)
    {
        var links = Resolve(model, tolerance);
        for (int i = 0; i < links.Length; i++)
            StrausRuntime.Check(St7.St7SetRigidLink(modelId, i + 1, 1, St7.rlPlaneXYZ,
                new[] { 2, links[i].From, links[i].To }), "St7SetRigidLink");
    }
}
