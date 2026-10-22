using System;
using System.Linq;
using BridGHe.Common.Helpers;
using BridGHe.Common.Models;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Parameters;

internal static class ReinforcementMaterialTests
{
    internal static void Run()
    {
        void Require(bool ok, string label) { if (!ok) throw new Exception("B450C: " + label); }
        var material = ReinforcementMaterials.B450C();
        Require(material.Kind == MaterialModel.MaterialKind.Rebar && material.Modulus == 200000 && material.MinimumYieldStress == 450 && material.MinimumTensileStress == 540, "Characteristic properties");
        Require(material.ReinforcementUltimateStrain == .075 && Math.Abs(material.Density - 7.85e-9) < 1e-20, "GPC ductility and density units");
        foreach (var copy in new[] { new MaterialModel(material), ModelArchive.Clone(material) })
            Require(copy.ReinforcementUltimateStrain == .075 && copy.Kind == MaterialModel.MaterialKind.Rebar, "Copy and serialization preserve grade");
        var plugin = BridgeMergeTests.Initialize();
        using var doc = new GH_Document();
        var component = (GH_Component)Activator.CreateInstance(plugin.GetTypes().Single(t => t.Name == "MaterialRebarComponent"));
        component.CreateAttributes(); doc.AddObject(component, false);
        GH_Document.EnableSolutions = true; doc.Enabled = true; doc.NewSolution(false, GH_SolutionMode.Silent);
        Require(component.Params.Output[0].VolatileDataCount == 1 && component.RuntimeMessages(GH_RuntimeMessageLevel.Error).Count == 0, "Component defaults solve");
        var name = (Param_String)component.Params.Input[0]; name.PersistentData.Clear(); name.SetPersistentData(" ");
        name.ExpireSolution(false); component.ExpireSolution(false); doc.NewSolution(false, GH_SolutionMode.Silent);
        Require(!component.Params.Output[0].VolatileData.AllData(true).Any(g => g?.ScriptVariable() is MaterialModel) &&
            component.RuntimeMessages(GH_RuntimeMessageLevel.Error).Count + component.RuntimeMessages(GH_RuntimeMessageLevel.Warning).Count > 0,
            $"Blank name rejected without stale output: output={component.Params.Output[0].VolatileDataCount}; input='{string.Join("|",name.VolatileData.AllData(true))}'; errors={string.Join("|",component.RuntimeMessages(GH_RuntimeMessageLevel.Error))}; warnings={string.Join("|",component.RuntimeMessages(GH_RuntimeMessageLevel.Warning))}");
        Console.WriteLine("B450C PASS: characteristic properties, density, ductility, copy, archive and GH defaults/validation.");
    }
}
