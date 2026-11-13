using System;
using System.Linq;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Parameters;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;

internal static class GetBeamPropertyTests
{
    internal static T[] Output<T>(GH_Component c, int index = 0) =>
        c.Params.Output[index].VolatileData.AllData(true).Select(g => (T)g.ScriptVariable()).ToArray();
    internal static void Run()
    {
        Gpc();
    }

    /// <summary>R6.6 R2a: Get Beam Property on beams given by their GPC drafts alone (no FEM value): the steel I property and the homogenised
    /// composite property of the grillage example (<see cref="R2aPropertyFixtures"/>). The same checks as the FEM test on the GPC data: list
    /// lengths and tree branches, name, signature and formulation (a GPC property has no BridGHe ID of its own: it is known by its name and
    /// signature), area, inertia and density copied exactly, independent copies (property and material), composite preview of the homogenised
    /// section, no mutation of the inputs through the output, archive and the error on a beam without a property.</summary>
    private static void Gpc()
    {
        int checks = 0;
        void Check(bool valid, string message) { if (!valid) throw new Exception("GET BEAM PROPERTY: " + message); checks++; }
        using var doc = new GH_Document();
        var c = BridgeSegmentExcelTests.Add(doc, "GetBeamPropertyComponent");
        var steel = R2aPropertyFixtures.Property("P_steel");
        var composite = R2aPropertyFixtures.Homogenised("P", 12, "_composite");
        var b0 = R2aPropertyFixtures.Beam("get:B0", "B0", steel); var b1 = R2aPropertyFixtures.Beam("get:B1", "B1", composite);
        var input = (Param_GenericObject)c.Params.Input[0];
        input.PersistentData.AppendRange(new[] { R2aDesignFixtures.Beam(b0), R2aDesignFixtures.Beam(b0) }, new GH_Path(2,3));
        input.PersistentData.Append(R2aDesignFixtures.Beam(b1), new GH_Path(5,7));
        BridgeSegmentExcelTests.Solve(doc);
        var outputs = R2aDesignFixtures.Outputs(c);
        var result = outputs.Select(R2aDesignFixtures.Datum<BridGHe.Common.Gpc.Legacy.LegacyBeamProperty>).ToArray();
        Check(result.Length == 3 && result.All(r => r != null) && R2aDesignFixtures.GpcOnly(outputs) && c.Params.Output[0].VolatileData.Paths.Select(p => p.ToString()).SequenceEqual(new[] {"{2;3}","{5;7}"}),
            "item extraction keeps list lengths and tree branches, by the GPC datum alone");
        Check(composite.Property.Name == "P_composite" && result[0].Property.Name == steel.Property.Name && result[0].Data.Signature == steel.Data.Signature
            && result[0].Data.Formulation == steel.Data.Formulation, "identity and formulation");
        var m0 = R2aPropertyFixtures.Material(result[0]);
        Check(R2aPropertyFixtures.Area(result[0]) == R2aPropertyFixtures.Area(steel) && R2aPropertyFixtures.I11(result[0]) == R2aPropertyFixtures.I11(steel)
            && m0.Density == R2aPropertyFixtures.Material(steel).Density && m0.Density == 7.85e-9, "mechanical properties copied exactly");
        Check(!ReferenceEquals(result[0].Property, steel.Property) && !ReferenceEquals(result[0].Property, b0.Beam.BeamProperty) && !ReferenceEquals(result[0].Property, result[1].Property)
            && !ReferenceEquals(m0, R2aPropertyFixtures.Material(steel)) && !ReferenceEquals(m0, R2aPropertyFixtures.Material(result[1])), "independent copies including repeated input beams");
        Check(result[2].Data.Section.CompositePreview && R2aPropertyFixtures.Area(result[2]) == R2aPropertyFixtures.Area(composite) && R2aPropertyFixtures.I11(result[2]) == R2aPropertyFixtures.I11(composite),
            "homogenized section and composite preview retained");
        m0.Density = 1; m0.ElasticModulusTension = 1; R2aPropertyFixtures.Elastic(result[0]).ShearArea1 = 1;
        Check(R2aPropertyFixtures.Material(steel).ElasticModulusTension == 210000 && R2aPropertyFixtures.Material(steel).Density == 7.85e-9 && R2aPropertyFixtures.Elastic(b0).Material.ElasticModulusTension == 210000
            && R2aPropertyFixtures.Elastic(b0).ShearArea1 == R2aPropertyFixtures.Elastic(steel).ShearArea1 && R2aPropertyFixtures.Material(result[1]).ElasticModulusTension == 210000
            && R2aPropertyFixtures.Elastic(steel).Section.Height == 2500, "editing extracted data cannot mutate the input or sibling output");
        var chunk = new GH_IO.Serialization.GH_LooseChunk("Component"); Check(c.Write(chunk), "component archive write");
        using var restored = new GH_Document(); var copy = BridgeSegmentExcelTests.Add(restored,"GetBeamPropertyComponent");
        Check(copy.Read(chunk), "component archive read"); BridgeSegmentExcelTests.Solve(restored);
        Check(R2aDesignFixtures.Outputs(copy).Length == 3 && R2aDesignFixtures.GpcOnly(R2aDesignFixtures.Outputs(copy)), "properties recover after reopening");
        // A beam whose draft has no property (the GPC twin of a beam on an empty Beam Property).
        var invalid = BridGHe.Common.Gpc.GpcDrafts.Copy(R2aPropertyFixtures.Beam("get:Invalid", "Invalid", steel)); invalid.Beam.BeamProperty = null;
        R2aDesignFixtures.Input(c, 0, R2aDesignFixtures.Beam(invalid)); c.ExpireSolution(false); BridgeSegmentExcelTests.Solve(doc,true);
        Check(c.RuntimeMessages(GH_RuntimeMessageLevel.Error).Count > 0 && !c.Params.Output[0].VolatileData.AllData(true).Any(g => R2aDesignFixtures.Datum(g) != null),
            "missing property reports an error without stale output; errors=" + string.Join(";",c.RuntimeMessages(GH_RuntimeMessageLevel.Error)) + "; count=" + c.Params.Output[0].VolatileDataCount);
        Console.WriteLine($"GET BEAM PROPERTY PASS: {checks} checks; steel/composite data, isolated copies, trees, archive and invalid input.");
    }

}
