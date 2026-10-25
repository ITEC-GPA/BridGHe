using System;
using System.Linq;
using System.Collections.Generic;
using BridGHe.Common.Geometry;
using BridGHe.Common.Models;

internal static class BridgeSegmentCompositeTests
{
    internal static void Run()
    {
        var geometry = SimpleBridgeBuilder.Build(SimpleBridgeTests.Input()); var steel = BridgeSegmentSectionsTests.BaseProperty();
        var schedule = BridgeSegmentSections.Create(geometry, steel, null);
        var beams = BridgeMemberAssignment.Assign(geometry, new Dictionary<int, BeamPropertyModel> { [-1] = steel }, schedule.Rules).Beams;
        var ids = schedule.Rules.Select(r => $"G{r.Group}-C{r.Segment}").ToArray();
        var properties = ids.Select(id => new CompositeBeamPropertyModel(id) { Name = id, SteelSection = steel.Section,
            ConcreteSection = new SectionConcreteSlabModel(250, 3000, 1500, 40), SteelMaterial = steel.Material,
            ConcreteMaterial = steel.Material, RebarMaterial = steel.Material }).ToArray();
        foreach (var b in beams) b.Results.Add(new BeamForceResultModel(new LoadCaseModel("Q", "Q"), 0, 0, "") { AxialForce = 123 });
        var mapped = BridgeSegmentComposite.Map(beams.Reverse().ToArray(), ids.Reverse().ToArray(), properties.Reverse().ToArray());
        if (mapped.Girders.Length <= properties.Length) throw new Exception("Fixture must have more FEM beams than fabrication segments.");
        for (int i = 0; i < mapped.Girders.Length; i++)
        {
            var beam = mapped.Girders[i]; var m = beam.Attributes.OfType<BridgeMemberAttributeModel>().Single().Value;
            if (mapped.Properties[i].Name != $"G{m.Group}-C{m.Segment}" || mapped.SegmentIds[i] != mapped.Properties[i].Name ||
                !ReferenceEquals(beam, beams.Single(b => b.BeamId == beam.BeamId)) || ((BeamForceResultModel)beam.Results.Single()).AxialForce != 123)
                throw new Exception("Composite mapping lost physical identity or results.");
        }
        void Reject(Action action) { try { action(); } catch (ArgumentException) { return; } throw new Exception("Invalid mapping accepted."); }
        Reject(() => BridgeSegmentComposite.Map(beams, ids, properties.Take(1).ToArray()));
        Reject(() => BridgeSegmentComposite.Map(beams, ids.Select(_ => ids[0]).ToArray(), properties));
        Reject(() => BridgeSegmentComposite.Map(beams, ids.Select(id => id + "wrong").ToArray(), properties));
        Reject(() => BridgeSegmentComposite.Map(new[] { new BeamModel() }, ids, properties));
        Console.WriteLine("SEGMENT COMPOSITE PASS: reordered keys, multiple FEM members per concio, retained results and invalid mappings rejected.");
    }
}
