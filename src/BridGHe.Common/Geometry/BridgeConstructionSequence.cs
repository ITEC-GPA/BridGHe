using System;
using System.Collections.Generic;
using System.Linq;
using BridGHe.Common.Checks;
using BridGHe.Common.Helpers;
using BridGHe.Common.Models;
using Rhino.Geometry;

namespace BridGHe.Common.Geometry;

public sealed record BridgeConstructionSnapshot(ModelModel Model, StagedConstructionModel Stage, string[] ActivePhysicalIds, string[] Diagnostics);

public static class BridgeConstructionSequence
{
    public static BridgeConstructionSnapshot Create(ModelModel source, BridgePhaseDefinition phase,
        IReadOnlyList<string> activeGroups, IReadOnlyList<string> physicalIds, IReadOnlyList<string> bearingIds,
        BeamPropertyModel girderOverride, IReadOnlyList<LoadCombinationModel> increments, double tolerance = 1e-6)
    {
        ArgumentNullException.ThrowIfNull(source); ArgumentNullException.ThrowIfNull(phase);
        if (phase.Order < 0 || string.IsNullOrWhiteSpace(phase.Name) || phase.Day is double day && (!double.IsFinite(day) || day < 0))
            throw new ArgumentException("Phase needs a nonnegative number/day and a name.");
        if (!double.IsFinite(tolerance) || tolerance <= 0) throw new ArgumentException("Tolerance must be finite and positive.");
        activeGroups ??= Array.Empty<string>(); physicalIds ??= Array.Empty<string>(); bearingIds ??= new[] { "*" };
        increments ??= Array.Empty<LoadCombinationModel>();
        var allGroups = source.Elements.SelectMany(e => e.Groups).SelectMany(GroupModel.GetBranch).Select(g => g.FullName).ToHashSet(StringComparer.Ordinal);
        var allPhysical = source.Beams.SelectMany(b => b.Attributes.OfType<BridgeMemberAttributeModel>()).Select(a => a.Value.PhysicalId).ToHashSet(StringComparer.Ordinal);
        if (activeGroups.Any(g => !allGroups.Contains(g)) || physicalIds.Any(id => !allPhysical.Contains(id)))
            throw new ArgumentException("Unknown active group/physical member identifier.");
        var supported = source.Nodes.Where(n => n.Attributes.Any(a => a is NodeRestraintAttributeModel or NodeStiffnessAttributeModel)).Select(n => n.NodeId).ToHashSet(StringComparer.Ordinal);
        if (bearingIds.Any(id => id != "*" && !supported.Contains(id)) || bearingIds.Contains("*") && bearingIds.Count != 1)
            throw new ArgumentException("Active bearings require exact supported Node IDs, '*' for all, or an empty list for none.");
        if (girderOverride != null && !girderOverride.IsValid) throw new ArgumentException("Invalid girder override property.");
        var model = new ModelModel(source) { Name = $"{phase.Order:D2}-{phase.Name}" };
        bool all = activeGroups.Count == 0 && physicalIds.Count == 0;
        bool Active(ElementModel element) => all || element.Groups.SelectMany(GroupModel.GetBranch).Any(g => activeGroups.Contains(g.FullName)) ||
            element.Attributes.OfType<BridgeMemberAttributeModel>().Any(a => physicalIds.Contains(a.Value.PhysicalId));
        var structure = model.Elements.Where(e => e is BeamModel or PlateModel).Where(Active).ToList();
        if (structure.Count == 0) throw new ArgumentException("The selected construction phase has no structural elements.");
        var points = structure.SelectMany(e => e is BeamModel b ? new[] { b.PointFrom, b.PointTo } : ((PlateModel)e).Points.ToArray()).ToList();
        var retainedNodes = new HashSet<NodeModel>();
        using var tree = new RTree();
        int vertex = 0; foreach (var point in points) tree.Insert(point, vertex++);
        bool Touches(Point3d p) { bool found = false; tree.Search(new Sphere(p, tolerance), (_, _) => found = true); return found; }
        bool changed;
        do
        {
            changed = false;
            foreach (var node in model.Nodes)
                if (!retainedNodes.Contains(node) && (Touches(node.Position) ||
                    node.Attributes.OfType<RigidConnectionAttributeModel>().Any(c => Touches(c.Value))))
                {
                    retainedNodes.Add(node); tree.Insert(node.Position, vertex++);
                    foreach (var link in node.Attributes.OfType<RigidConnectionAttributeModel>()) tree.Insert(link.Value, vertex++);
                    changed = true;
                }
        } while (changed);
        int removed = model.Beams.Count + model.Plates.Count - structure.Count;
        model.Elements.Clear(); model.Elements.AddRange(structure); model.Elements.AddRange(retainedNodes); model.RebuildIndexes();
        foreach (var element in model.Elements) element.Results.Clear();
        foreach (var node in model.Nodes)
            if (!bearingIds.Contains("*") && !bearingIds.Contains(node.NodeId))
                foreach (var a in node.Attributes.Where(a => a is NodeRestraintAttributeModel or NodeStiffnessAttributeModel).ToArray()) node.Attributes.Remove(a);
        if (girderOverride != null)
        {
            var property = ModelArchive.Clone(girderOverride);
            var girders = model.Beams.Where(b => b.Attributes.OfType<BridgeMemberAttributeModel>().Any(a => a.Value.Family == BridgeMemberFamily.Girder)).ToArray();
            if (girders.Length == 0) throw new ArgumentException("Girder property override requires members produced by Bridge Member Assignment.");
            foreach (var beam in girders)
            {
                if (beam.Attributes.OfType<BeamTaperAttributeModel>().Any() && property.SectionType is SectionModel.SectionTypes.Generic or SectionModel.SectionTypes.GenericShapes)
                    throw new ArgumentException("Cannot retain a dimensional taper when overriding with generic/homogenized properties. Use matching per-segment properties.");
                beam.BeamProperty = property;
            }
        }
        model.StagedConstruction.Clear(); model.BridgeWorkflow = null;
        var clonedIncrements = ModelArchive.Clone(increments.ToArray());
        var caseNames = source.LoadCases.Select(c => c.Name).ToHashSet(StringComparer.Ordinal);
        if (clonedIncrements.Any(c => c == null || c.Values.Any(v => v.Key == null || !caseNames.Contains(v.Key.Name) || !double.IsFinite(v.Value))))
            throw new ArgumentException("Construction increments contain unknown cases or non-finite coefficients.");
        StagedConstructionModel stage = null;
        bool eligible = girderOverride == null && bearingIds.SequenceEqual(new[] { "*" }) &&
            !source.Nodes.Any(n => n.Attributes.OfType<RigidConnectionAttributeModel>().Any());
        if (eligible && clonedIncrements.Length > 0)
        {
            stage = new StagedConstructionModel(model.Name);
            stage.AddGroups(structure.SelectMany(e => e.Groups).SelectMany(GroupModel.GetBranch).Distinct());
            stage.AddLoadCombinations(clonedIncrements);
        }
        return new(model, stage, model.Beams.SelectMany(b => b.Attributes.OfType<BridgeMemberAttributeModel>()).Select(a => a.Value.PhysicalId).Distinct().ToArray(), new[]
        {
            $"Phase {phase.Order} {phase.Name}: {structure.Count} active elements, {removed} omitted; day={phase.Day?.ToString("G9") ?? "unset"}.",
            "Phase Model is an independent analysis snapshot. Previous forces/deformations, creep or activation history are not automatically transferred.",
            "Apply the intended incremental actions to each snapshot, solve it, then use Bridge Action/Bridge Shrinkage Action and the existing phase workflow.",
            stage != null ? "Native Stage available: only element group activation and supplied load increments; connect ordered stages to the original full model." :
                "Native Stage omitted: requires load increments, unchanged bearings/properties, and no rigid links. Use explicit phase snapshots for the selected configuration.",
            "Section State and Day are workflow metadata; section properties change only through the explicit Girder Property input."
        });
    }
}
