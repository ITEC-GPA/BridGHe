#if GPC_PILOT
using System;
using System.Linq;
using BridGHe.Common.Gpc;
using BridGHe.Common.Gpc.Builders;
using BridGHe.Common.Gpc.Legacy;
using Grasshopper.Kernel.Types;

/// <summary>
/// R6.6 lane R4a: the GPC data of the Grasshopper graphs of the bridge examples, without any FEM model. The materials are the recipes of the
/// product builders with the numbers the FEM fixtures of the examples wrote (<c>new MaterialModel("C30") { SpecificCompressiveStrength = 30 }</c>,
/// S355 E 210000 fy 355 fu 510, B450 E 200000 fy 450 fu 540), given to the graph as GH materials by their GPC datum alone
/// (<c>GH_Material.FromGpc</c>, no FEM value). The checks read the name and fck of a concrete and E, fy, fu of a steel
/// (<see cref="GpcMaterials.Concrete"/>, <see cref="GpcMaterials.StructuralSteel"/>, <see cref="GpcMaterials.Reinforcement"/>): the same numbers as
/// the conversion of the former FEM materials (<c>BridgeGpc.Materials(MaterialModel…)</c>), checked explicitly once per material.
/// </summary>
internal static class R4aFixtures
{
    internal static Guid Id(string tag) => GpcIdentity.Derive(GpcIdentity.NameSpace, "r66-r4a:" + tag);

    /// <summary>A concrete of fck <paramref name="fck"/> (kind Concrete, E = Ecm of GPC from fck, ν 0.2).</summary>
    internal static GPC.Model.Materials.Material Concrete(string name = "C30", double fck = 30)
    {
        double ecm = new GPC.Model.Materials.ConcreteMaterialEN1992(name, fck, GPC.Model.Materials.ConcreteMaterial.CompressionStressStrainDiagrams.ParabolaRectangle).ElasticModulusCompression;
        var m = GpcMaterialBuilders.Material(new LegacyMaterialRecipe { Kind = "Concrete", Name = name, SpecificCompressiveStrength = fck, Modulus = ecm, PoissonRatio = .2, UsePoisson = true });
        var v = GpcMaterials.ConcreteValues(m);
        Require(v.Name == name && v.Fck == fck, $"the concrete {name} has fck {v.Fck}, expected {fck}");
        return m;
    }

    /// <summary>A steel with E, fy and fu (kind Steel, ν 0.3; εu 0.1, as the former FEM material without a reinforcement ultimate strain).</summary>
    internal static GPC.Model.Materials.Material Steel(string name, double modulus, double fy, double fu)
    {
        var m = GpcMaterialBuilders.Material(new LegacyMaterialRecipe { Kind = "Steel", Name = name, Modulus = modulus, MinimumYieldStress = fy, MinimumTensileStress = fu, PoissonRatio = .3, UsePoisson = true });
        var v = GpcMaterials.SteelValues(m);
        Require(v.Name == name && v.E == modulus && v.Fy == fy && v.Fu == fu && v.StrainU == .1, $"the steel {name} has E {v.E}, fy {v.Fy}, fu {v.Fu}, εu {v.StrainU}");
        return m;
    }

    internal static GPC.Model.Materials.Material S355() => Steel("S355", 210000, 355, 510);
    internal static GPC.Model.Materials.Material B450() => Steel("B450", 200000, 450, 540);

    /// <summary>The GH material given by its GPC datum alone (kind of the GPC type), checked to have no FEM value.</summary>
    internal static IGH_Goo Material(GPC.Model.Materials.Material material)
    {
        var goo = R2aDesignFixtures.MaterialGoo(material);
        Require(!R2aDesignFixtures.HasFem(goo) && ReferenceEquals(R2aDesignFixtures.Datum(goo), material), "the GH material is not given by its GPC datum alone");
        return goo;
    }

    /// <summary>
    /// The document as P2 reads it, in memory only (never saved): every internalised GH model with a FEM value (a persistent datum of a parameter
    /// or of a component input) is replaced by the GH model given by its GPC input alone (<c>GH_Model.FromGpc</c>: the former payload read by
    /// value, <c>GpcLegacyPayloads.Model</c>, or the attached GPC input), with the analysed GPC model it carries (its saved results, chunk
    /// BridGHeResults) attached; every other internalised GH datum with a FEM value (a composite property, a material…) is replaced by the GH
    /// datum of its GPC datum alone (<c>FromGpc</c> of its type, as the GPC-only copies of the R6.5 replays). The objects, their instance GUIDs
    /// and the wires are kept. Returns the analysed models of the models replaced.
    /// </summary>
    internal static GPC.Model.Models.Model[] GpcOnly(Grasshopper.Kernel.GH_Document doc)
    {
        var ghModel = R2bFixtures.GhModel; var analysed = new System.Collections.Generic.List<GPC.Model.Models.Model>();
        var parameters = doc.Objects.OfType<Grasshopper.Kernel.IGH_Param>().Concat(doc.Objects.OfType<Grasshopper.Kernel.GH_Component>().SelectMany(c => c.Params.Input));
        foreach (var param in parameters)
        {
            if (!(param.GetType().GetProperty("PersistentData")?.GetValue(param) is Grasshopper.Kernel.Data.IGH_Structure data) || data.DataCount == 0) continue;
            bool changed = false;
            for (int p = 0; p < data.PathCount; p++)
            {
                var branch = data.get_Branch(p);
                for (int i = 0; i < branch.Count; i++)
                {
                    if (!(branch[i] is IGH_Goo goo) || goo.GetType().GetProperty("Value")?.GetValue(goo) == null) continue;
                    if (goo.GetType() != ghModel)
                    {
                        // Another GH datum with a FEM value (a composite property, a material...): its GPC datum alone (FromGpc of the GH type).
                        var fromGpc = goo.GetType().GetMethod("FromGpc", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                        var datum = goo.GetType().GetProperty("Gpc")?.GetValue(goo);
                        if (fromGpc == null || datum == null) continue;
                        var args = new object[fromGpc.GetParameters().Length]; args[0] = datum;
                        var twin = (IGH_Goo)fromGpc.Invoke(null, args);
                        Require(twin.GetType().GetProperty("Value").GetValue(twin) == null && ReferenceEquals(twin.GetType().GetProperty("Gpc").GetValue(twin), datum), "the GPC-only twin of a " + goo.GetType().Name);
                        branch[i] = twin; changed = true; continue;
                    }
                    var input = R2bFixtures.Get<GpcModels.Conversion>(goo, "GpcInput") ?? throw new InvalidOperationException("R4A: an internalised model without its GPC input (" + param.NickName + ")");
                    // The drafts of its elements carried apart (the former payload read by value), as the model transformers read them
                    // (GpcOnlyModels.InputOf): Build BridGHe Model and the filters need them.
                    var drafts = R2bFixtures.Get<GpcModelDrafts>(goo, "GpcDrafts");
                    if (input.Drafts == null && drafts != null) input = input.WithDrafts(drafts);
                    var results = R2bFixtures.Get<GPC.Model.Models.Model>(goo, "GpcResults");
                    var created = R2bFixtures.Model(input, results);
                    Require(R2bFixtures.Get<GpcModels.Conversion>(created, "GpcInput") == input && R2bFixtures.Get<GPC.Model.Models.Model>(created, "GpcResults") == results,
                        "the GPC-only model does not keep the input and results of " + param.NickName);
                    branch[i] = created; changed = true; if (results != null) analysed.Add(results);
                }
            }
            if (changed) param.ExpireSolution(false);
        }
        return analysed.ToArray();
    }

    /// <summary>
    /// The internalised GH models of a document that carry no drafts of their elements (neither in their GPC input nor apart). The five
    /// documents remade on 7/11 (<c>--remake-examples-04 write</c>) saved their models as GPC input (BGHGPC1, the conversion of the FEM value,
    /// which carries no drafts) plus the FEM value, with no drafts chunk: on GPC data alone (P2) the components that give the elements of such a
    /// model back (Super Element Filter, Decompose Model: <c>GpcModelDraftsAttach.OfGpcOnly</c>) refuse it. Listed as a gap for P2.
    /// </summary>
    internal static int WithoutDrafts(Grasshopper.Kernel.GH_Document doc) =>
        Models(doc).Count(g => R2bFixtures.Get<GpcModels.Conversion>(g, "GpcInput")?.Drafts == null && R2bFixtures.Get<GpcModelDrafts>(g, "GpcDrafts") == null);

    /// <summary>The GH models internalised in a document (persistent data of parameters and component inputs).</summary>
    internal static IGH_Goo[] Models(Grasshopper.Kernel.GH_Document doc) =>
        doc.Objects.OfType<Grasshopper.Kernel.IGH_Param>().Concat(doc.Objects.OfType<Grasshopper.Kernel.GH_Component>().SelectMany(c => c.Params.Input))
            .SelectMany(p => (p.GetType().GetProperty("PersistentData")?.GetValue(p) as Grasshopper.Kernel.Data.IGH_Structure)?.AllData(true) ?? Enumerable.Empty<IGH_Goo>())
            .Where(g => g != null && g.GetType() == R2bFixtures.GhModel).ToArray();

    internal static void Require(bool ok, string message) { if (!ok) throw new Exception("R4A: " + message); }
}
#endif
