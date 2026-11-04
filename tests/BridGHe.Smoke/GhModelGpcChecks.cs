#if GPC_PILOT
using System;
using System.Linq;
using System.Reflection;
using BridGHe.Common.Models;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;

/// <summary>
/// Regression gh-model-gpc (F6 phase (c), C2): the FEM model of a GH model carries its GPC model. Converted once and cached; the
/// conversion follows the FEM model (a new FEM model gives a new conversion, the same content the same identities); an analysed GPC
/// model attached by an analysis component is shared by the copies of the datum and dropped when the FEM model changes.
/// </summary>
internal static class GhModelGpcChecks
{
    internal static void Run()
    {
        var assembly = Assembly.LoadFrom(BuildPaths.GrasshopperAssembly);
        var type = assembly.GetType("BridGHe.Grasshopper.DataTypes.Fem.GH_Model", true);
        object Make(ModelModel model) => type.GetMethod("FromModel", BindingFlags.Public | BindingFlags.Static).Invoke(null, new object[] { model, null });
        GPC.Model.Models.Model Gpc(object goo) => (GPC.Model.Models.Model)type.GetProperty("Gpc").GetValue(goo);
        bool Analysed(object goo) => (bool)type.GetProperty("HasAnalysedGpc").GetValue(goo);
        void Require(bool condition, string what) { if (!condition) throw new Exception("GH MODEL GPC FAIL: " + what); }

        var femm = Cantilever();
        var goo = Make(femm);
        var gpc = Gpc(goo);
        Require(gpc != null && gpc.NodesElements.Count == 2 && gpc.BeamElements.Count == 1, "converted model: " + type.GetProperty("GpcError").GetValue(goo));
        Require(ReferenceEquals(gpc, Gpc(goo)), "conversion cached");
        Require(gpc.BeamElements[1].Attributes.Values.OfType<GPC.Model.Attributes.ElementLabelAttribute>().Single().Label == "B1"
            && gpc.PhysicalMembers.ContainsKey("SuperElement:G1"), "C1 identities in the converted model");
        var copy = ((IGH_Goo)goo).Duplicate();
        var copied = Gpc(copy);
        Require(!ReferenceEquals(copied, gpc) && copied.Guid == gpc.Guid && copied.BeamElements[1].Guid == gpc.BeamElements[1].Guid, "a copy converts its own FEM model to the same identities");
        Require(!Analysed(goo) && !Analysed(copy), "no analysed model yet");

        var analysed = GPC.Model.Core.ModelValues.Copy(gpc);
        type.GetMethod("AttachGpc").Invoke(goo, new object[] { analysed });
        Require(Analysed(goo) && ReferenceEquals(Gpc(goo), analysed), "analysed model attached");
        var duplicate = ((IGH_Goo)goo).Duplicate();
        Require(Analysed(duplicate) && ReferenceEquals(Gpc(duplicate), analysed), "copies share the analysed model");

        type.GetProperty("Value").SetValue(goo, Cantilever(2));
        var changed = Gpc(goo);
        Require(!Analysed(goo) && changed != null && !ReferenceEquals(changed, analysed) && changed.Guid != gpc.Guid, "a new FEM model drops the analysed model and converts again");
        Require(Analysed(duplicate), "the copy keeps its analysed model");
        Console.WriteLine("GH MODEL GPC PASS: conversion, cache, identities, analysed model shared by copies and dropped when the FEM model changes.");
    }

    private static ModelModel Cantilever(double length = 1)
    {
        var steel = new MaterialModel("S355") { Name = "S355", Modulus = 210000, PoissonRatio = 0.3, UsePoisson = true, Density = 7.85e-9, ThermalExpansion = 1.2e-5 };
        var section = new SectionModel { Name = "R", SectionType = SectionModel.SectionTypes.SolidRectangle, B = 300, D = 500 };
        var property = new BeamPropertyModel("P") { Name = "P", Material = steel, Section = section, PropertyType = BeamPropertyType.Beam };
        var a = new NodeModel("1", new Point3d(0, 0, 0)); var b = new NodeModel("2", new Point3d(1000 * length, 0, 0));
        a.Attributes.Add(new NodeRestraintAttributeModel(true, true, true, true, true, true));
        var beam = new BeamModel(new Point3d(0, 0, 0), new Point3d(1000 * length, 0, 0), property, new GroupModel("Girder"), 0) { BeamId = "1" };
        beam.Attributes.Add(new ElementIdAttributeModel("B1")); beam.Attributes.Add(new SuperElementAttributeModel("G1"));
        var model = new ModelModel(ModelModel.ModelUnits.Nmm) { Name = "cantilever" };
        model.AddElements(new ElementModel[] { a, b, beam });
        return model;
    }
}
#endif
