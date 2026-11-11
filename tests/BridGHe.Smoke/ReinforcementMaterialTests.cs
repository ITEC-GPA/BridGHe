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
#if GPC_PILOT
        // R6.6 R2a: the GPC rebar of the B450C recipe (the material the component gives), no FEM material: the same expected values.
        var recipe = BridGHe.Common.Gpc.Builders.GpcMaterialBuilders.Rebar();
        var gpc = (GPC.Model.Materials.SteelMaterial)BridGHe.Common.Gpc.Builders.GpcMaterialBuilders.Material(recipe);
        bool Grade(GPC.Model.Materials.Material m) => m is GPC.Model.Materials.SteelMaterial s && s.SteelType == GPC.Model.Materials.SteelMaterial.SteelTypes.Rebar
            && s.ElasticModulusTension == 200000 && s.Fyk == 450 && s.Fu == 540 && s.StrainUTension == .075 && Math.Abs(s.Density - 7.85e-9) < 1e-20;
        Require(recipe.Kind == "Rebar" && gpc.SteelType == GPC.Model.Materials.SteelMaterial.SteelTypes.Rebar && gpc.ElasticModulusTension == 200000 && gpc.Fyk == 450 && gpc.Fu == 540, "Characteristic properties");
        Require(gpc.StrainUTension == .075 && Math.Abs(gpc.Density - 7.85e-9) < 1e-20, "GPC ductility and density units");
        var gooType = R2aDesignFixtures.Plugin.GetTypes().Single(t => t.Name == "GH_Material");
        var goo = R2aDesignFixtures.MaterialGoo(gpc, recipe.Kind);
        var archive = new GH_IO.Serialization.GH_Archive(); var restoredGoo = (Grasshopper.Kernel.Types.IGH_Goo)Activator.CreateInstance(gooType);
        Require(archive.AppendObject(goo, "material") && archive.ExtractObject(restoredGoo, "material"), "GH archive of the material");
        foreach (var copy in new[] { GPC.Model.Core.ModelValues.CopyValue((GPC.Model.Materials.Material)gpc), R2aDesignFixtures.Datum<GPC.Model.Materials.Material>(restoredGoo) })
            Require(Grade(copy) && !ReferenceEquals(copy, gpc), "Copy and serialization preserve grade");
        Require(R2aDesignFixtures.Get<string>(restoredGoo, "GpcKind") == "Rebar" && !R2aDesignFixtures.HasFem(restoredGoo), "Serialization preserves the kind, without a FEM value");
        var plugin = BridgeMergeTests.Initialize();
        using var only = R2aDesignFixtures.WithoutFem(generators: false); // the producer gives the material by its GPC datum alone (the path of P2)
#else
        var material = ReinforcementMaterials.B450C();
        Require(material.Kind == MaterialModel.MaterialKind.Rebar && material.Modulus == 200000 && material.MinimumYieldStress == 450 && material.MinimumTensileStress == 540, "Characteristic properties");
        Require(material.ReinforcementUltimateStrain == .075 && Math.Abs(material.Density - 7.85e-9) < 1e-20, "GPC ductility and density units");
        foreach (var copy in new[] { new MaterialModel(material), ModelArchive.Clone(material) })
            Require(copy.ReinforcementUltimateStrain == .075 && copy.Kind == MaterialModel.MaterialKind.Rebar, "Copy and serialization preserve grade");
        var plugin = BridgeMergeTests.Initialize();
#endif
        using var doc = new GH_Document();
        var component = (GH_Component)Activator.CreateInstance(plugin.GetTypes().Single(t => t.Name == "MaterialRebarComponent"));
        component.CreateAttributes(); doc.AddObject(component, false);
        GH_Document.EnableSolutions = true; doc.Enabled = true; doc.NewSolution(false, GH_SolutionMode.Silent);
        Require(component.Params.Output[0].VolatileDataCount == 1 && component.RuntimeMessages(GH_RuntimeMessageLevel.Error).Count == 0, "Component defaults solve");
#if GPC_PILOT
        var output = R2aDesignFixtures.Outputs(component);
        Require(R2aDesignFixtures.GpcOnly(output) && Grade(R2aDesignFixtures.Datum<GPC.Model.Materials.Material>(output[0])), "Component defaults give the B450C grade by its GPC datum alone");
#endif
        var name = (Param_String)component.Params.Input[0]; name.PersistentData.Clear(); name.SetPersistentData(" ");
        name.ExpireSolution(false); component.ExpireSolution(false); doc.NewSolution(false, GH_SolutionMode.Silent);
#if GPC_PILOT
        Require(!component.Params.Output[0].VolatileData.AllData(true).Any(g => R2aDesignFixtures.Datum(g) is GPC.Model.Materials.Material || g?.ScriptVariable() is MaterialModel) &&
#else
        Require(!component.Params.Output[0].VolatileData.AllData(true).Any(g => g?.ScriptVariable() is MaterialModel) &&
#endif
            component.RuntimeMessages(GH_RuntimeMessageLevel.Error).Count + component.RuntimeMessages(GH_RuntimeMessageLevel.Warning).Count > 0,
            $"Blank name rejected without stale output: output={component.Params.Output[0].VolatileDataCount}; input='{string.Join("|",name.VolatileData.AllData(true))}'; errors={string.Join("|",component.RuntimeMessages(GH_RuntimeMessageLevel.Error))}; warnings={string.Join("|",component.RuntimeMessages(GH_RuntimeMessageLevel.Warning))}");
        Console.WriteLine("B450C PASS: characteristic properties, density, ductility, copy, archive and GH defaults/validation.");
    }
}
