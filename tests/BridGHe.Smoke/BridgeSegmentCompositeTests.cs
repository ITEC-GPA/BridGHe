using System;
using System.Linq;
using System.Collections.Generic;
using BridGHe.Common.Geometry;
using BridGHe.Common.Models;

internal static class BridgeSegmentCompositeTests
{
    internal static void Run()
    {
        Gpc();
    }

    /// <summary>R6.6 R2a: the association on GPC data alone (<see cref="BridGHe.Common.Gpc.GpcStateOverride.Map"/>, the path of P2 of Bridge
    /// Segment Composite): the girders of the assignment on GPC data, the composite sections by their GPC definitions (named by concio); the
    /// results of the girders are their link to the analysed GPC beam, kept by the component (a copy of the input with its analysed element).</summary>
    private static void Gpc()
    {
        var geometry = SimpleBridgeBuilder.Build(SimpleBridgeTests.Input()); var steel = R2aDesignFixtures.BaseProperty();
        var schedule = BridgeSegmentSections.CreateGpc(geometry, steel, null);
        var beams = R2aGeneratorFixtures.Drafts(R2aGeneratorFixtures.Assign(geometry, new Dictionary<int, BridGHe.Common.Gpc.Legacy.LegacyBeamProperty> { [-1] = steel }, schedule.Rules).Plans, "segment composite");
        var ids = schedule.Rules.Select(r => $"G{r.Group}-C{r.Segment}").ToArray();
        var properties = ids.Select(id => GpcCompositeDefinitions.Create(GpcCompositeDefinitions.Grillage(new BridgeGrillageExample.Parameters()) with { Name = id })).ToArray();
        var mapped = BridGHe.Common.Gpc.GpcStateOverride.Map(beams.Reverse().ToArray(), ids.Reverse().ToArray(), properties.Reverse().ToArray());
        if (mapped.Girders.Length <= properties.Length) throw new Exception("Fixture must have more FEM beams than fabrication segments.");
        for (int i = 0; i < mapped.Girders.Length; i++)
        {
            var beam = mapped.Girders[i]; var m = R2aGeneratorFixtures.Member(beam);
            if (mapped.Definitions[i].Name != $"G{m.Group}-C{m.Segment}" || mapped.Segments[i] != mapped.Definitions[i].Name ||
                !ReferenceEquals(beam, beams.Single(b => b.Beam.Name == beam.Beam.Name)))
                throw new Exception("Composite mapping lost physical identity or results.");
        }
        // The results: the component keeps the link of every girder to its analysed GPC beam (the inputs given by their drafts alone).
        var analysed = R2aGeneratorFixtures.Analysed("segment composite");
        var inputs = beams.Select(b => { var g = R2aDesignFixtures.Beam(b); g.GetType().GetProperty("AnalysedGpc").SetValue(g, analysed.Beam); g.GetType().GetProperty("AnalysedGpcModel").SetValue(g, analysed.Model); return g; }).ToArray();
        using (var doc = new Grasshopper.Kernel.GH_Document())
        {
            var c = BridgeSegmentExcelTests.Add(doc, "BridgeSegmentCompositeComponent");
            R2aDesignFixtures.Input(c, 0, inputs.Reverse().ToArray());
            var idParam = (Grasshopper.Kernel.Parameters.Param_String)c.Params.Input[1]; idParam.PersistentData.Clear(); idParam.SetPersistentData(ids.Reverse().Cast<object>().ToArray());
            R2aDesignFixtures.Input(c, 2, properties.Reverse().Select(R2aDesignFixtures.Composite).ToArray());
            BridgeSegmentExcelTests.Solve(doc);
            var girders = R2aDesignFixtures.Outputs(c, 0); var segments = R2aDesignFixtures.Values<string>(c, 2);
            if (girders.Length != mapped.Girders.Length || !R2aDesignFixtures.GpcOnly(girders) || !segments.SequenceEqual(mapped.Segments)
                || girders.Any(g => !ReferenceEquals(R2aDesignFixtures.Get<object>(g, "AnalysedGpc"), analysed.Beam) || !ReferenceEquals(R2aDesignFixtures.Get<object>(g, "AnalysedGpcModel"), analysed.Model))
                || R2aDesignFixtures.Values<string>(c, 3).Zip(mapped.Girders, (id, b) => id == b.Beam.Name).Any(v => !v))
                throw new Exception("Composite mapping lost physical identity or results.");
        }
        void Reject(Action action) { try { action(); } catch (ArgumentException) { return; } throw new Exception("Invalid mapping accepted."); }
        Reject(() => BridGHe.Common.Gpc.GpcStateOverride.Map(beams, ids, properties.Take(1).ToArray()));
        Reject(() => BridGHe.Common.Gpc.GpcStateOverride.Map(beams, ids.Select(_ => ids[0]).ToArray(), properties));
        Reject(() => BridGHe.Common.Gpc.GpcStateOverride.Map(beams, ids.Select(id => id + "wrong").ToArray(), properties));
        Reject(() => BridGHe.Common.Gpc.GpcStateOverride.Map(new[] { GpcFixtures.SingleBeam("segment composite without member") }, ids, properties));
        Console.WriteLine("SEGMENT COMPOSITE PASS: reordered keys, multiple FEM members per concio, retained results and invalid mappings rejected.");
    }
}
