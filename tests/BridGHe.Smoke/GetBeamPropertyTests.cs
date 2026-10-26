using System;
using System.Linq;
using BridGHe.Common.Models;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Parameters;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;

internal static class GetBeamPropertyTests
{
    internal static IGH_Goo Wrap(object value)
    {
        string name = value is BeamModel ? "GH_Beam" : "GH_BeamProperty";
        var type = BridgeMergeTests.Initialize().GetTypes().Single(t => t.Name == name);
        var goo = (IGH_Goo)Activator.CreateInstance(type); type.GetProperty("Value").SetValue(goo, value); return goo;
    }
    internal static void Input(GH_Component component, int index, params object[] values)
    {
        var p = (Param_GenericObject)component.Params.Input[index]; p.PersistentData.Clear();
        foreach (var value in values) p.PersistentData.Append(Wrap(value), new GH_Path(0));
        p.ExpireSolution(false);
    }
    internal static BeamPropertyModel Property(string name)
    {
        var p = BridgeSegmentSectionsTests.BaseProperty(); p.Name = name; p.Id = "ID_" + name;
        p.Material.Density = 7.85e-9; return p;
    }
    internal static BeamModel Beam(string id, BeamPropertyModel property) =>
        new BeamModel(id, Point3d.Origin, new Point3d(5000,0,0), property) { BeamId = id };
    internal static T[] Output<T>(GH_Component c, int index = 0) =>
        c.Params.Output[index].VolatileData.AllData(true).Select(g => (T)g.ScriptVariable()).ToArray();
    internal static void Run()
    {
        int checks = 0;
        void Check(bool valid, string message) { if (!valid) throw new Exception("GET BEAM PROPERTY: " + message); checks++; }
        using var doc = new GH_Document();
        var c = BridgeSegmentExcelTests.Add(doc, "GetBeamPropertyComponent");
        var steel = Property("P_steel");
        var composite = BridgeGrillageExample.Composite(new()).CreateHomogenizedProperty(12, "P_composite");
        var b0 = Beam("B0", steel); var b1 = Beam("B1", composite);
        var input = (Param_GenericObject)c.Params.Input[0];
        input.PersistentData.AppendRange(new[] { Wrap(b0), Wrap(b0) }, new GH_Path(2,3));
        input.PersistentData.Append(Wrap(b1), new GH_Path(5,7));
        BridgeSegmentExcelTests.Solve(doc);
        var result = Output<BeamPropertyModel>(c);
        Check(result.Length == 3 && c.Params.Output[0].VolatileData.Paths.Select(p => p.ToString()).SequenceEqual(new[] {"{2;3}","{5;7}"}), "item extraction keeps list lengths and tree branches");
        Check(result[0].Name == steel.Name && result[0].Id == steel.Id && result[0].PropertyType == steel.PropertyType, "identity and formulation");
        Check(result[0].SectionArea == steel.SectionArea && result[0].I11 == steel.I11 && result[0].Material.Density == steel.Material.Density, "mechanical properties copied exactly");
        Check(!ReferenceEquals(result[0],steel) && !ReferenceEquals(result[0],result[1]) && !ReferenceEquals(result[0].Material,steel.Material), "independent copies including repeated input beams");
        Check(result[2].Section.CompositePreview != null && result[2].SectionArea == composite.SectionArea && result[2].I11 == composite.I11, "homogenized section and composite preview retained");
        result[0].Section.D = 123; result[0].Material.Modulus = 1;
        Check(steel.D == 2500 && steel.Material.Modulus == 210000 && result[1].D == 2500, "editing extracted data cannot mutate the input or sibling output");
        var chunk = new GH_IO.Serialization.GH_LooseChunk("Component"); Check(c.Write(chunk), "component archive write");
        using var restored = new GH_Document(); var copy = BridgeSegmentExcelTests.Add(restored,"GetBeamPropertyComponent");
        Check(copy.Read(chunk), "component archive read"); BridgeSegmentExcelTests.Solve(restored);
        Check(Output<BeamPropertyModel>(copy).Length == 3, "properties recover after reopening");
        Input(c,0,Beam("Invalid",new BeamPropertyModel())); c.ExpireSolution(false); BridgeSegmentExcelTests.Solve(doc,true);
        Check(c.RuntimeMessages(GH_RuntimeMessageLevel.Error).Count > 0 && !c.Params.Output[0].VolatileData.AllData(true).Any(g => g?.ScriptVariable() is BeamPropertyModel),
            "missing property reports an error without stale output; errors=" + string.Join(";",c.RuntimeMessages(GH_RuntimeMessageLevel.Error)) + "; count=" + c.Params.Output[0].VolatileDataCount);
        Console.WriteLine($"GET BEAM PROPERTY PASS: {checks} checks; steel/composite data, isolated copies, trees, archive and invalid input.");
    }
}
