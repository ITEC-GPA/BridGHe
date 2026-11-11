using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Windows.Forms;
using BridGHe.Common.Helpers;
using BridGHe.Common.Models;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Parameters;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;

/// <summary>
/// Regression shared-properties (F6, decision of 4/11: copies of GH data share the immutable properties; L9 in GPCModel): the components
/// that receive a material, a section, a beam property or a beam never change it in place. Upstream data (the very instances, as a
/// shared copy would hand them over) go through the downstream components that calculate or edit sections and properties, the menus of
/// the section parameter, a composite beam and edits of duplicated beams; afterwards the fingerprint (archive SHA-256) of every upstream
/// datum must be the one it had before. The cases are those of the audit in correzioni-P2B.md (step 10).
/// </summary>
internal static class SharedPropertiesChecks
{
    internal static void Run()
    {
#if GPC_PILOT
        Gpc();
#else
        Fem();
#endif
    }

#if GPC_PILOT
    /// <summary>
    /// R6.6 R2a: the same audit on the GPC data alone (no FEM value): the upstream data are a steel, an I section recipe, a Brep section recipe
    /// of one region away from the origin, a composite section definition, the two beam properties Beam Property makes of them and the beam Beam
    /// makes of the first; they go through Beam Property, Beam, Section H Beam Property (section, property, beam), the Mirror and Units menus of
    /// the section parameter, Composite Beam and the edits of a duplicated beam (Beam Override and Super Element, the builders of the edit
    /// components); afterwards the fingerprint (SHA-256 of the GPC archive of the datum, <c>GpcDraftArchive</c>) of every upstream datum must be
    /// the one it had before. The two shapes of the FEM fixture are one L region here (GPC refuses a Brep section of separate regions); the FEM
    /// check «the property keeps its shapes moved to the minimum» (the former <c>CalculateSection</c>) is replaced by the analytical area of the
    /// region in the GPC section of the property (300 × 600 + 300 × 40 = 192000).
    /// </summary>
    private static void Gpc()
    {
        var assembly = R2aDesignFixtures.Plugin;
        static string Hash(params byte[][] parts) => Convert.ToHexString(SHA256.HashData(parts.SelectMany(p => p ?? Array.Empty<byte>()).ToArray()));
        static string Fingerprint(object value) => value switch
        {
            GPC.Model.Materials.Material m => Hash(BridGHe.Common.Gpc.GpcDraftArchive.Write(m)),
            BridGHe.Common.Gpc.Legacy.LegacySectionRecipe s => Hash(BridGHe.Common.Gpc.GpcDraftArchive.Write(s)),
            BridGHe.Common.Gpc.Legacy.LegacyBeamProperty p => Hash(BridGHe.Common.Gpc.GpcDraftArchive.Write(p).Data, BridGHe.Common.Gpc.GpcDraftArchive.Write(p).Records),
            BridGHe.Common.Gpc.Legacy.LegacyBeamDraft b => Hash(BridGHe.Common.Gpc.GpcDraftArchive.Write(b).Data, BridGHe.Common.Gpc.GpcDraftArchive.Write(b).Records),
            GPC.Checkers.CompositeBridge.BridgeCompositeSectionDefinition d => Hash(BridGHe.Common.Gpc.GpcDraftArchive.Write(d)),
            _ => throw new ArgumentException("No GPC fingerprint for " + value?.GetType().Name),
        };

        var steel = R2aDesignFixtures.Material("Steel", "S355", 210000, .3, 7.85e-9);
        var section = BridGHe.Common.Gpc.Builders.GpcSectionBuilders.Standard(BridGHe.Common.Gpc.BridgeSectionKind.I, 600, b1: 300, b2: 300, t1: 30, t2: 30, t3: 15);
        var region = new GPC.Geometry.Shape2d(new GPC.Geometry.Polygon2d(new[] { new GPC.Geometry.Point2d(100, 50), new GPC.Geometry.Point2d(700, 50), new GPC.Geometry.Point2d(700, 90),
            new GPC.Geometry.Point2d(400, 90), new GPC.Geometry.Point2d(400, 650), new GPC.Geometry.Point2d(100, 650) }));
        var generic = BridGHe.Common.Gpc.Builders.GpcSectionBuilders.Shapes(new[] { region });
        var composite = GpcCompositeDefinitions.Create(GpcCompositeDefinitions.Grillage(new BridgeGrillageExample.Parameters()));
        var upstream = new Dictionary<string, object> { ["material"] = steel, ["I section"] = section, ["generic section"] = generic, ["composite property"] = composite };
        var before = upstream.ToDictionary(p => p.Key, p => Fingerprint(p.Value));

        using var document = new GH_Document { Enabled = true }; GH_Document.EnableSolutions = true;
        GH_Component Add(string type)
        {
            var component = (GH_Component)Activator.CreateInstance(assembly.GetType("BridGHe.Grasshopper.Components." + type, true));
            component.CreateAttributes(); document.AddObject(component, false); return component;
        }
        void Set(IGH_Param param, params IGH_Goo[] values)
        {
            ((Param_GenericObject)param).PersistentData.Clear(); foreach (var v in values) ((Param_GenericObject)param).PersistentData.Append(v);
            param.ExpireSolution(false);
        }
        IGH_Goo Solve(GH_Component component, int output = 0)
        {
            component.ExpireSolution(false); document.NewSolution(false, GH_SolutionMode.Silent);
            var errors = component.RuntimeMessages(GH_RuntimeMessageLevel.Error);
            if (errors.Count > 0) throw new Exception(component.Name + ": " + string.Join("; ", errors));
            var value = component.Params.Output[output].VolatileData.AllData(true).Single();
            if (value.GetType().GetProperty("Gpc") != null && R2aDesignFixtures.HasFem(value)) throw new Exception(component.Name + ": the output of GPC-only inputs must have no FEM value.");
            return value;
        }

        // Beam Property on the I section and on the Brep section (the input goo instances themselves).
        var sectionGoo = R2aDesignFixtures.Goo("GH_BeamSection", section); var genericGoo = R2aDesignFixtures.Goo("GH_BeamSection", generic);
        var steelGoo = R2aDesignFixtures.MaterialGoo(steel, "Steel");
        var property = Add("ElementProperties.BeamPropertyComponent");
        ((Param_String)property.Params.Input[0]).PersistentData.Append(new GH_String("P1"));
        ((Param_Integer)property.Params.Input[1]).PersistentData.Append(new GH_Integer((int)BeamPropertyType.Beam));
        Set(property.Params.Input[2], sectionGoo); Set(property.Params.Input[3], steelGoo);
        var p1 = R2aDesignFixtures.Datum<BridGHe.Common.Gpc.Legacy.LegacyBeamProperty>(Solve(property));
        Set(property.Params.Input[2], genericGoo); var p2 = R2aDesignFixtures.Datum<BridGHe.Common.Gpc.Legacy.LegacyBeamProperty>(Solve(property));
        if (generic.Shapes.Single().Fill.Where((_, i) => i % 2 == 0).Min() != 100) throw new Exception("Beam Property moved the shapes of its input section.");
        if (Math.Abs(R2aPropertyFixtures.Area(p2) - 192000) > 1e-6) throw new Exception("The GPC section of the property must be the region as given (area 192000), not " + R2aPropertyFixtures.Area(p2) + ".");
        upstream["property P1"] = p1; upstream["property P2"] = p2; before["property P1"] = Fingerprint(p1); before["property P2"] = Fingerprint(p2);

        // A beam from P1, then the downstream components that read or calculate its section.
        var beamComponent = Add("Models.BeamComponent");
        ((Param_Curve)beamComponent.Params.Input[0]).PersistentData.Append(new GH_Curve(new LineCurve(new Point3d(0, 0, 0), new Point3d(5000, 0, 0))));
        Set(beamComponent.Params.Input[1], R2aDesignFixtures.Property(p1));
        var beamGoo = Solve(beamComponent); var beam = R2aDesignFixtures.Datum<BridGHe.Common.Gpc.Legacy.LegacyBeamDraft>(beamGoo);
        upstream["beam"] = beam; before["beam"] = Fingerprint(beam);
        var hProperty = Add("BridgeChecks.BeamSectionHPropertyComponent");
        foreach (var input in new[] { sectionGoo, R2aDesignFixtures.Property(p1), beamGoo })
        { Set(hProperty.Params.Input[0], input); Solve(hProperty, 1); }

        // The menus of the section parameter on a stored section that is the upstream instance.
        var sectionParam = (IGH_Param)Activator.CreateInstance(assembly.GetType("BridGHe.Grasshopper.Components.Parameters.BeamSectionParam", true));
        sectionParam.CreateAttributes(); document.AddObject(sectionParam, false);
        var persistent = sectionParam.GetType().GetProperty("PersistentData").GetValue(sectionParam);
        persistent.GetType().GetMethod("Append", new[] { sectionGoo.GetType() }).Invoke(persistent, new object[] { sectionGoo });
        void Menu(string name, object sender) => sectionParam.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(sectionParam, new[] { sender, EventArgs.Empty });
        Menu("OnSetMirror", null); Menu("OnUnitsClick", new ToolStripMenuItem(nameof(ModelModel.ModelUnits.Knm)));
        // The parameter shows one stored section recipe, converted once (mm -> m) and mirrored (Left), a new recipe.
        var shown = sectionParam.VolatileData.AllData(true).Select(R2aDesignFixtures.Datum<BridGHe.Common.Gpc.Legacy.LegacySectionRecipe>).ToArray();
        if (shown.Length != 1 || shown[0] == null || Math.Abs(shown[0].D - .6) > 1e-12 || Math.Abs(shown[0].T3 - .015) > 1e-12 || shown[0].Mirror != 2 || ReferenceEquals(shown[0], section))
            throw new Exception("Section parameter after the Mirror and Units menus: " + string.Join(", ", shown.Select(x => x?.D + " " + x?.Mirror)) + " (expected 0.6 m, mirrored, a new section).");

        // A composite beam on the composite property, and edits of a duplicated beam (the builders of Beam Override and Super Element).
        var compositeBeam = Add("BridgeChecks.CompositeBeamComponent");
        Set(compositeBeam.Params.Input[0], beamGoo); Set(compositeBeam.Params.Input[1], R2aDesignFixtures.Composite(composite)); Solve(compositeBeam);
        var duplicate = beamGoo.Duplicate(); var copy = R2aDesignFixtures.Datum<BridGHe.Common.Gpc.Legacy.LegacyBeamDraft>(duplicate);
        var edited = BridGHe.Common.Gpc.Builders.GpcBeamBuilders.Override(copy, null, null, 30, (10, 20), R2aDesignFixtures.Id("shared:edit"));
        edited = BridGHe.Common.Gpc.Builders.GpcBeamBuilders.WithSuperElement(edited, "Copy", R2aDesignFixtures.Id("shared:super element"));
        if (edited.AngleDeg != 30 || edited.Records.SuperElements.SingleOrDefault() != "Copy") throw new Exception("The edits of the duplicated beam were not applied.");

        var changed = upstream.Where(p => Fingerprint(p.Value) != before[p.Key]).Select(p => p.Key).ToArray();
        if (changed.Length != 0) throw new Exception("Downstream components changed upstream data in place: " + string.Join(", ", changed) + ".");
        Console.WriteLine($"SHARED PROPERTIES PASS: {upstream.Count} upstream data unchanged after Beam Property, Beam, Section H Beam Property (section, property, beam), " +
            "the mirror and units menus of the section parameter, a composite beam and edits of a duplicated beam.");
    }
#endif

#if !GPC_PILOT
    private static void Fem()
    {
        var assembly = Assembly.LoadFrom(BuildPaths.GrasshopperAssembly);
        IGH_Goo Goo(string name, object value)
        {
            var type = assembly.GetType("BridGHe.Grasshopper.DataTypes." + name, true);
            var goo = (IGH_Goo)Activator.CreateInstance(type); type.GetProperty("Value").SetValue(goo, value); return goo;
        }
        static string Fingerprint(object value) => Convert.ToHexString(SHA256.HashData(ModelArchive.Write(value)));

        // Upstream data: a steel, an I section with tabulated stiffness (as a library section: CalculateSection would overwrite it),
        // a section of generic shapes away from the origin (CalculateSection moves shapes to the minimum), a composite property.
        var steel = new MaterialModel("S355") { Modulus = 210000, PoissonRatio = .3, Density = 7.85e-9 };
        var section = new SectionModel("HEB-like") { SectionType = SectionModel.SectionTypes.I, D = 600, B1 = 300, B2 = 300, T1 = 30, T2 = 30, T3 = 15 };
        section.CalculateSection(); section.I11 *= 1.01; section.SectionArea *= 1.005;
        GPC.Geometry.Shape2d Rectangle(double x0, double y0, double x1, double y1) => new(new GPC.Geometry.Polygon2d(new[]
            { new GPC.Geometry.Point2d(x0, y0), new GPC.Geometry.Point2d(x1, y0), new GPC.Geometry.Point2d(x1, y1), new GPC.Geometry.Point2d(x0, y1) }));
        var generic = new SectionModel("Shapes") { SectionType = SectionModel.SectionTypes.GenericShapes,
            GenericShapes = new BridGHe.Common.Geometry.Shapes(new[] { Rectangle(100, 50, 400, 650), Rectangle(400, 50, 700, 90) }) };
        var composite = BridgeGrillageExample.Composite(new BridgeGrillageExample.Parameters());
        var upstream = new Dictionary<string, object> { ["material"] = steel, ["I section"] = section, ["generic section"] = generic, ["composite property"] = composite };
        var before = upstream.ToDictionary(p => p.Key, p => Fingerprint(p.Value));

        using var document = new GH_Document { Enabled = true }; GH_Document.EnableSolutions = true;
        GH_Component Add(string type)
        {
            var component = (GH_Component)Activator.CreateInstance(assembly.GetType("BridGHe.Grasshopper.Components." + type, true));
            component.CreateAttributes(); document.AddObject(component, false); return component;
        }
        void Set(IGH_Param param, params IGH_Goo[] values)
        {
            switch (param)
            {
                case Param_GenericObject generic: generic.PersistentData.Clear(); foreach (var v in values) generic.PersistentData.Append(v); break;
                default: throw new InvalidOperationException("Unexpected parameter " + param.GetType().Name);
            }
            param.ExpireSolution(false);
        }
        object Solve(GH_Component component, int output = 0)
        {
            component.ExpireSolution(false); document.NewSolution(false, GH_SolutionMode.Silent);
            var errors = component.RuntimeMessages(GH_RuntimeMessageLevel.Error);
            if (errors.Count > 0) throw new Exception(component.Name + ": " + string.Join("; ", errors));
            return component.Params.Output[output].VolatileData.AllData(true).Single().ScriptVariable();
        }

        // Beam Property on the I section and on the generic shapes (the input goo instances themselves).
        var sectionGoo = Goo("Fem.GH_BeamSection", section); var genericGoo = Goo("Fem.GH_BeamSection", generic); var steelGoo = Goo("Fem.GH_Material", steel);
        var property = Add("ElementProperties.BeamPropertyComponent");
        ((Param_String)property.Params.Input[0]).PersistentData.Append(new GH_String("P1"));
        ((Param_Integer)property.Params.Input[1]).PersistentData.Append(new GH_Integer((int)BeamPropertyType.Beam));
        Set(property.Params.Input[2], sectionGoo); Set(property.Params.Input[3], steelGoo);
        var p1 = (BeamPropertyModel)Solve(property);
        Set(property.Params.Input[2], genericGoo); var p2 = (BeamPropertyModel)Solve(property);
        if (generic.GenericShapes.GetBBox().Min.X != 100) throw new Exception("Beam Property moved the shapes of its input section.");
        if (p2.Section.GenericShapes.GetBBox().Min.X != 0) throw new Exception("The section of the property must keep its shapes moved to the minimum.");
        upstream["property P1"] = p1; upstream["property P2"] = p2; before["property P1"] = Fingerprint(p1); before["property P2"] = Fingerprint(p2);

        // A beam from P1, then the downstream components that read or calculate its section.
        var beamComponent = Add("Models.BeamComponent");
        ((Param_Curve)beamComponent.Params.Input[0]).PersistentData.Append(new GH_Curve(new LineCurve(new Point3d(0, 0, 0), new Point3d(5000, 0, 0))));
        Set(beamComponent.Params.Input[1], Goo("Fem.GH_BeamProperty", p1));
        var beam = (BeamModel)Solve(beamComponent); upstream["beam"] = beam; before["beam"] = Fingerprint(beam);
        var hProperty = Add("BridgeChecks.BeamSectionHPropertyComponent");
        foreach (var input in new[] { sectionGoo, Goo("Fem.GH_BeamProperty", p1), Goo("Fem.GH_Beam", beam) })
        { Set(hProperty.Params.Input[0], input); Solve(hProperty, 1); }

        // The menus of the section parameter on a stored section that is the upstream instance.
        var sectionParam = (IGH_Param)Activator.CreateInstance(assembly.GetType("BridGHe.Grasshopper.Components.Parameters.BeamSectionParam", true));
        sectionParam.CreateAttributes(); document.AddObject(sectionParam, false);
        var persistent = sectionParam.GetType().GetProperty("PersistentData").GetValue(sectionParam);
        persistent.GetType().GetMethod("Append", new[] { sectionGoo.GetType() }).Invoke(persistent, new object[] { sectionGoo });
        void Menu(string name, object sender) => sectionParam.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(sectionParam, new[] { sender, EventArgs.Empty });
        Menu("OnSetMirror", null); Menu("OnUnitsClick", new ToolStripMenuItem(nameof(ModelModel.ModelUnits.Knm)));
        // The parameter shows one stored section, converted once (mm -> m) and mirrored; the upstream section is checked below.
        // Before: Units appended the section again and converted the shared instance three times (two items, 6e-7 m).
        var shown = sectionParam.VolatileData.AllData(true).Select(g => (SectionModel)g.GetType().GetProperty("Value").GetValue(g)).ToArray();
        if (shown.Length != 1 || Math.Abs(shown[0].D - .6) > 1e-12 || Math.Abs(shown[0].T3 - .015) > 1e-12 || shown[0].Mirror != SectionModel.MirrorTypes.Left || ReferenceEquals(shown[0], section))
            throw new Exception("Section parameter after the Mirror and Units menus: " + string.Join(", ", shown.Select(x => x.D + " " + x.Mirror)) + " (expected 0.6 m, mirrored, a new section).");

        // A composite beam on the composite property, and edits of duplicated beams.
        _ = new CompositeBeamModel(Point3d.Origin, new Point3d(8000, 0, 0), composite);
        var duplicate = (IGH_Goo)Goo("Fem.GH_Beam", beam).Duplicate(); var copy = (BeamModel)duplicate.ScriptVariable();
        copy.AngleDeg = 30; copy.Offset = new Vector2d(10, 20); copy.Attributes.Add(new SuperElementAttributeModel("Copy"));

        var changed = upstream.Where(p => Fingerprint(p.Value) != before[p.Key]).Select(p => p.Key).ToArray();
        if (changed.Length != 0) throw new Exception("Downstream components changed upstream data in place: " + string.Join(", ", changed) + ".");
        Console.WriteLine($"SHARED PROPERTIES PASS: {upstream.Count} upstream data unchanged after Beam Property, Beam, Section H Beam Property (section, property, beam), " +
            "the mirror and units menus of the section parameter, a composite beam and edits of a duplicated beam.");
    }
#endif
}
