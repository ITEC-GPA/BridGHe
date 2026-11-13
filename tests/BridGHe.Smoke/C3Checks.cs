using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using BridGHe.Common.Geometry;
using BridGHe.Common.Gpc;
using BridGHe.Common.Gpc.Builders;
using BridGHe.Common.Gpc.Legacy;
using BridGHe.Common.Models;
using GPC.Converter.Straus7;
using GPC.Model.ElementProperties;
using GPC.Model.Materials;
using Rhino.Geometry;

/// <summary>
/// C3 (11/11/2026): the two product gaps of lane R2a.
/// <list type="bullet">
/// <item>Gap 1, regression <c>gpc-c3</c> (<see cref="Materials"/>): a material with explicit G (Use Poisson off), damping only, thermal data only,
/// a table without α and ρ, a table with α and ρ, a concrete with a table and a concrete with all of them is a valid GPC material (the linear
/// elastic law without a table, elastoplastic with one), and the beam, truss, cable, plate and slab-grillage properties built on each are made,
/// with the data of the material (the reader of the FEM value gives the same; the table only on the types of 0.3.0). The write plan of the
/// Converter for a GPC-only model with every material (<see cref="WritePlan"/>, recorded, not native) and the native readback
/// (<c>--c3 --native</c>, <see cref="Native"/>): what Straus7 receives through the GPC-only path, against the numbers and calls of 0.3.0.</item>
/// <item>Gap 2, the same regression (<see cref="Display"/>): the section solids and bar lines a draft given by its GPC data alone draws,
/// against the FEM drawing of the same element (TRANSITIONAL: the FEM side goes in P2) and against explicit counts and boxes for one case of
/// each kind (beam, composite beam, plate; the homogenised composite property that draws nothing: the open point of C3). The T, C, angle
/// and double sections need the native Straus7 sections: they are compared under <c>--c3 --native</c>.</item>
/// </list>
/// </summary>
internal static class C3Checks
{
    private static readonly List<string> failures = new();
    private static int checks;
    private static void Check(bool ok, string what) { checks++; if (!ok) failures.Add(what); }
    private static string R(double v) => v.ToString("R", CultureInfo.InvariantCulture);

    internal static void Regression()
    {
        failures.Clear(); checks = 0;
        Materials();
        WritePlan();
        Display(false);
        foreach (var f in failures) Console.WriteLine("  C3 FAIL: " + f);
        Console.WriteLine($"C3 CHECKS: {checks} checks, {failures.Count} failures");
        if (failures.Count != 0) throw new Exception($"C3 CHECKS FAIL: {failures.Count} of {checks} checks");
    }

    // ---------------------------------------------------------------------------------------------------------------------------------------
    // Gap 1: materials with analysis data and no table

    internal static readonly (double stress, double strain)[] Table = { (0, 0), (20, 20.0 / 34000), (30, 0.0035) };

    /// <summary>A material case: its recipe (the numbers of a component or of a former value) and what its analysis data must hold.</summary>
    internal sealed record Case(string Tag, LegacyMaterialRecipe Recipe, double? G, double Viscous, double Damping, double Conductivity, double SpecificHeat, bool HasTable, bool Concrete);

    private static LegacyMaterialRecipe Concrete(string name) => new LegacyMaterialRecipe
    {
        Kind = "Concrete", Name = name, Modulus = 34000, PoissonRatio = .2, ShearModulus = 14166.666666666666, UsePoisson = true, ThermalExpansion = 1e-5, Density = 2.5e-9,
        SpecificCompressiveStrength = 30,
    };

    internal static Case[] Cases()
    {
        var damping = Concrete("C3 damping"); damping.ViscousDamping = 0.05; damping.DampingRatio = 0.02;
        var thermal = Concrete("C3 thermal"); thermal.Conductivity = 1.6; thermal.SpecificHeat = 880;
        var concreteTable = Concrete("C3 concrete table"); concreteTable.StressStrainTable = Table;
        var all = Concrete("C3 all"); all.UsePoisson = false; all.ShearModulus = 14000; all.ViscousDamping = 0.05; all.DampingRatio = 0.02; all.Conductivity = 1.6; all.SpecificHeat = 880; all.StressStrainTable = Table;
        return new[]
        {
            // The case of R2a: a Custom concrete E 34000, Use Poisson off, G 14000 (the recipe of the Custom component).
            new Case("explicit G", GpcMaterialBuilders.Custom(1, "C3 G", 34000, .2, 1e-5, 14000, false, 2.5e-9, 30, 0), 14000, 0, 0, 0, 0, false, true),
            new Case("damping only", damping, null, 0.05, 0.02, 0, 0, false, true),
            new Case("thermal only", thermal, null, 0, 0, 1.6, 880, false, true),
            // The Table Material component (no kind, E 0), without and with α and ρ.
            new Case("table without alpha and rho", GpcMaterialBuilders.Table("C3 table", true, .2, 14000, 0, 0, Table), null, 0, 0, 0, 0, true, false),
            new Case("table with alpha and rho", GpcMaterialBuilders.Table("C3 table ar", true, .2, 14000, 1e-5, 2.5e-9, Table), null, 0, 0, 0, 0, true, false),
            new Case("concrete with a table", concreteTable, null, 0, 0, 0, 0, true, true),
            new Case("explicit G, damping, thermal and table", all, 14000, 0.05, 0.02, 1.6, 880, true, true),
        };
    }

    private static string AnalysisText(Material m) => GpcMaterialKindChecks.Dump(m);

    private static bool Refused(Action a, out string message)
    {
        try { a(); message = null; return false; }
        catch (Exception x) when (x is ArgumentException || x is NotSupportedException) { message = x.Message; return true; }
    }

    private static void Materials()
    {
        var units = GpcBeamDrafts.DraftUnits;
        var input = SimpleBridgeTests.Input() with { DeckBeamStations = new[] { 0d, 10000, 25000, 60000, 100000 }, DeckBeamSpacing = 0 };
        var grillage = BridgeDeckGrillage.Build(input, SimpleBridgeBuilder.Build(input));
        int beams = 0, plates = 0, slabs = 0;
        foreach (var c in Cases())
        {
            string label = "material «" + c.Tag + "»";
            var m = GpcMaterialBuilders.Material(c.Recipe);
            var a = m.AnalysisData;
            Check(a != null, label + ": analysis data");
            if (a == null) continue;
            Check(!Refused(a.Validate, out var invalid), label + ": the analysis data are valid (" + invalid + ")");
            Check(a.NonlinearResponse == (c.HasTable ? MaterialNonlinearResponse.Elastoplastic : MaterialNonlinearResponse.NonlinearElastic) && a.Hardening == MaterialHardening.Isotropic,
                label + $": law {a.NonlinearResponse} {a.Hardening}, expected {(c.HasTable ? "Elastoplastic" : "NonlinearElastic")} Isotropic");
            Check(a.BeamShearModulus == c.G && a.ViscousDampingDensity == c.Viscous && a.ModalDampingRatio == c.Damping && a.ThermalConductivity == c.Conductivity * 1000
                && a.SpecificHeat == c.SpecificHeat * 1000, label + $": G {a.BeamShearModulus} damping {R(a.ViscousDampingDensity)} {R(a.ModalDampingRatio)} thermal {R(a.ThermalConductivity)} {R(a.SpecificHeat)}");
            Check((a.StressStrainPairs != null) == c.HasTable && (!c.HasTable || a.StressStrainPairs.SequenceEqual(Table.SelectMany(t => new[] { t.strain, t.stress }))), label + ": the table as (strain, stress) pairs");
            // The FEM value of the same recipe read by the reader (the path with the FEM value present): the same GPC material.
            var fem = GpcMaterialBuilders.Femm(c.Recipe);
            Check(GpcDraftCapture.Text(GpcMaterials.FromFemm(fem)) == GpcDraftCapture.Text(m), label + ": the reader of the FEM value gives the same material");
            Check(AnalysisText(GpcMaterials.Analysis(m)) == AnalysisText(GpcModels.Material(fem)), label + ": the analysis material of the FEM value is the same");

            // Beam (type 5) on a solid rectangle 300 × 500: made, with the analysis data of the material; the FEM value gives the same property.
            var section = GpcSectionBuilders.Standard(BridgeSectionKind.SolidRectangle, 500, 300);
            LegacyBeamProperty Builder(int type) => GpcPropertyBuilders.Beam("C3 " + c.Tag + " " + type, type, section, m, GpcMaterials.KindOf(m));
            LegacyBeamProperty Reader(int type)
            {
                var s = new SectionModel { SectionType = SectionModel.SectionTypes.SolidRectangle, B = 300, D = 500 }; s.CalculateSection();
                return GpcModels.BeamProperty(new BeamPropertyModel("C3 " + c.Tag + " " + type) { Name = "C3 " + c.Tag + " " + type, Material = fem, Section = s, PropertyType = (BeamPropertyType)type }, units);
            }
            foreach (int type in new[] { 5, 2, 1 })
            {
                string what = label + (type == 5 ? ", Beam" : type == 2 ? ", Truss" : ", Cable");
                // A Table Material has E 0: a cable without the table has no stiffness (refused, ZeroElasticModulus, a rule of its own): not a case of C3.
                if (type == 1 && c.Recipe.Modulus == 0) continue;
                LegacyBeamProperty built = null, read = null;
                Check(!Refused(() => built = Builder(type), out var why), what + ": the GPC builder makes the property (" + why + ")");
                Check(!Refused(() => read = Reader(type), out var whyRead), what + ": the reader of the FEM value makes the property (" + whyRead + ")");
                if (built == null || read == null) continue;
                beams++;
                Check(built.Data.Signature == read.Data.Signature, what + ": builder and reader give the same property");
                var pm = ((ElasticBeamProperty)built.Property).Material;
                bool table = type != 1 && c.HasTable; // 0.3.0 wrote the table for beams and trusses only
                var expected = GpcMaterials.Analysis(m); if (!table) GpcMaterials.WithoutTable(expected);
                Check(Equals(pm.AnalysisData, expected.AnalysisData), what + ": the analysis data of the material on the property\n    " + AnalysisText(pm) + "\n    " + AnalysisText(expected));
                Check((pm.AnalysisData?.StressStrainPairs != null) == table, what + $": table {(table ? "kept" : "removed")}");
                if (!table && pm.AnalysisData != null) Check(pm.AnalysisData.NonlinearResponse == MaterialNonlinearResponse.NonlinearElastic, what + ": without the table, the linear elastic law");
                if (type == 1 && c.HasTable && c.G == null && c.Viscous == 0 && c.Conductivity == 0)
                    Check(pm.AnalysisData == null, what + ": a cable of a table material alone has no analysis data (the material without the table)");
            }

            // Plate (Shell Thin, 200 + 200) and the reader of the FEM plate property: made, G not on a plate (Use Poisson, as 0.3.0 wrote plates).
            ElasticPlateProperty plate = null; PlateProperty plateRead = null;
            Check(!Refused(() => plate = (ElasticPlateProperty)GpcPropertyBuilders.Plate("C3 plate " + c.Tag, 0, m, 200, 200), out var whyPlate), label + ", Shell: the GPC builder makes the plate property (" + whyPlate + ")");
            var femPlate = new PlatePropertyModel("1") { Name = "C3 plate " + c.Tag, Material = fem, PropertyType = PlatePropertyType.ShellThin, MembraneThickness = 200, BendingThickness = 200 };
            Check(!Refused(() => plateRead = LegacyModelImport.PlatePropertyDraft(LegacyArchive.Read(BridGHe.Common.Helpers.ModelArchive.Write(femPlate)), units), out var whyPlateRead),
                label + ", Shell: the reader of the FEM value makes the plate property (" + whyPlateRead + ")");
            if (plate != null && plateRead != null)
            {
                plates++;
                Check(GpcDraftCapture.Text(plate) == GpcDraftCapture.Text(plateRead), label + ", Shell: builder and reader give the same plate property");
                var pa = plate.Material.AnalysisData;
                bool any = c.Viscous != 0 || c.Damping != 0 || c.Conductivity != 0 || c.SpecificHeat != 0 || c.HasTable;
                Check(any ? pa != null && pa.BeamShearModulus == null && pa.ViscousDampingDensity == c.Viscous && pa.ModalDampingRatio == c.Damping && pa.ThermalConductivity == c.Conductivity * 1000
                    && pa.SpecificHeat == c.SpecificHeat * 1000 && (pa.StressStrainPairs != null) == c.HasTable : pa == null, label + ", Shell: the analysis data of a plate (no G): " + AnalysisText(plate.Material));
                if (pa != null) Check(pa.NonlinearResponse == (c.HasTable ? MaterialNonlinearResponse.Elastoplastic : MaterialNonlinearResponse.NonlinearElastic), label + ", Shell: the law");
            }
            // A membrane of a table material stays refused, with the message of 0.3.0 parity (A04).
            if (c.HasTable) Check(Refused(() => GpcPropertyBuilders.Plate("C3 membrane " + c.Tag, 4, m, 200, 200), out var membrane) && membrane == LegacyModelImport.PlateTableRefused, label + ", Membrane: the table refused as before");

            // Bridge Slab Grillage on the GPC path (a concrete): every strip on the analysis material of the concrete, with its data.
            if (c.Concrete)
            {
                BridgeSlabGrillageResult slab = null;
                Check(!Refused(() => slab = BridgeSlabGrillage.Create(grillage.Geometry, null, "C3 slab", GpcMaterials.Analysis(m), m), out var whySlab), label + ", slab grillage: made (" + whySlab + ")");
                if (slab != null)
                {
                    var strips = R2aGeneratorFixtures.Drafts(slab.Plans, "c3-slab-" + c.Tag);
                    slabs++;
                    Check(strips.Length == grillage.Strips.Length && strips.All(s => Equals(R2aGeneratorFixtures.Elastic(s).Material.AnalysisData, a)), label + ", slab grillage: every strip has the analysis data of the concrete");
                }
            }
        }
        Console.WriteLine($"C3 MATERIALS: {Cases().Length} materials valid, {beams} beam, truss and cable properties, {plates} plate properties, {slabs} slab grillages made by the builders and by the reader");
    }

    // ---------------------------------------------------------------------------------------------------------------------------------------
    // Gap 2: the preview of a GPC draft

    private static readonly MaterialModel SteelFem = new MaterialModel("S355") { Name = "S355", Kind = MaterialModel.MaterialKind.Steel, Modulus = 210000, PoissonRatio = .3, Density = 7.85e-9 };

    private static Type Preview => R2aDesignFixtures.Plugin.GetType("BridGHe.Grasshopper.Display.ModelPreview", true);
    private static object Call(string method, params object[] args) => Preview.GetMethods(BindingFlags.Public | BindingFlags.Static).First(x => x.Name == method && x.GetParameters().Length == args.Length).Invoke(null, args);

    private static string Box(BoundingBox b) => b.IsValid ? $"[{R(b.Min.X)} {R(b.Min.Y)} {R(b.Min.Z)}]-[{R(b.Max.X)} {R(b.Max.Y)} {R(b.Max.Z)}]" : "invalid";
    private static bool Near(double a, double b, double tol = 1e-7) => Math.Abs(a - b) <= tol * Math.Max(1, Math.Max(Math.Abs(a), Math.Abs(b)));
    private static bool Near(Point3d a, Point3d b, double tol = 1e-7) => Near(a.X, b.X, tol) && Near(a.Y, b.Y, tol) && Near(a.Z, b.Z, tol);
    private static bool SameBox(BoundingBox a, BoundingBox b) => a.IsValid && b.IsValid && Near(a.Min, b.Min) && Near(a.Max, b.Max);
    private static bool IsBox(BoundingBox b, double x0, double y0, double z0, double x1, double y1, double z1) => SameBox(b, new BoundingBox(x0, y0, z0, x1, y1, z1));
    private static double Volume(Brep b) { using var v = VolumeMassProperties.Compute(b); return v?.Volume ?? double.NaN; }

    /// <summary>Two drawings equal: the same count, and each solid with the same box, vertices (matched within 1e-7 relative), faces, edges,
    /// solidity and volume.</summary>
    private static string Compare(Brep[] fem, Brep[] gpc)
    {
        if (fem.Length != gpc.Length) return $"{gpc.Length} solids, FEM {fem.Length}";
        for (int i = 0; i < fem.Length; i++)
        {
            Brep f = fem[i], g = gpc[i];
            if (!SameBox(f.GetBoundingBox(true), g.GetBoundingBox(true))) return $"solid {i}: box {Box(g.GetBoundingBox(true))}, FEM {Box(f.GetBoundingBox(true))}";
            if (f.Faces.Count != g.Faces.Count || f.Edges.Count != g.Edges.Count || f.Vertices.Count != g.Vertices.Count || f.IsSolid != g.IsSolid)
                return $"solid {i}: {g.Faces.Count} faces {g.Edges.Count} edges {g.Vertices.Count} vertices solid {g.IsSolid}, FEM {f.Faces.Count} {f.Edges.Count} {f.Vertices.Count} {f.IsSolid}";
            var left = g.Vertices.Select(v => v.Location).ToList();
            foreach (var v in f.Vertices)
            {
                int k = left.FindIndex(p => Near(p, v.Location));
                if (k < 0) return $"solid {i}: FEM vertex {v.Location} not drawn";
                left.RemoveAt(k);
            }
            double vf = Volume(f), vg = Volume(g);
            if (!Near(vf, vg, 1e-6)) return $"solid {i}: volume {R(vg)}, FEM {R(vf)}";
        }
        return null;
    }

    private static string Compare(Line[] fem, Line[] gpc)
    {
        if (fem.Length != gpc.Length) return $"{gpc.Length} bar lines, FEM {fem.Length}";
        for (int i = 0; i < fem.Length; i++) if (!Near(fem[i].From, gpc[i].From) || !Near(fem[i].To, gpc[i].To)) return $"bar {i}: {gpc[i]}, FEM {fem[i]}";
        return null;
    }

    private static BeamModel FemBeam(string id, SectionModel s, Point3d a, Point3d b, double angle = 0, double ox = 0, double oy = 0, (double fx, double tx, double fy, double ty)? taper = null, bool calculate = true)
    {
        if (calculate) s.CalculateSection();
        var property = new BeamPropertyModel(id) { Name = id, Material = SteelFem, Section = s, PropertyType = BeamPropertyType.Beam };
        var beam = new BeamModel(id, a, b, property) { AngleDeg = angle, Offset = new Vector2d(ox, oy) };
        if (taper is { } t) { beam.Attributes.Add(new BeamTaperAttributeModel(new BeamTaperAttributeModel.AttributeData { NFromX = t.fx, NToX = t.tx, NFromY = t.fy, NToY = t.ty })); beam.CreateShapes(); }
        return beam;
    }

    /// <summary>The FEM beams of the comparison (one per section kind and option of the FEM preview); <paramref name="native"/>: the kinds whose
    /// GPC property is a native Straus7 section (T, C, angle, the double C and angle).</summary>
    private static IEnumerable<(string Kind, BeamModel Beam)> FemBeams(bool native)
    {
        var o = Point3d.Origin; var x = new Point3d(6000, 0, 0);
        SectionModel I(double d, double b1, double b2, double t1, double t2, double t3) => new SectionModel { SectionType = SectionModel.SectionTypes.I, D = d, B1 = b1, B2 = b2, T1 = t1, T2 = t2, T3 = t3 };
        if (!native)
        {
            yield return ("I", FemBeam("I", I(600, 300, 300, 20, 20, 10), o, x));
            yield return ("I unequal flanges, angle 30, offset (15, -25)", FemBeam("I2", I(800, 400, 250, 30, 20, 12), o, new Point3d(5000, 3000, 1000), 30, 15, -25));
            yield return ("I vertical", FemBeam("I3", I(600, 300, 300, 20, 20, 10), o, new Point3d(0, 0, 4000)));
            yield return ("I taper 1-1.5 / 1-2", FemBeam("I4", I(600, 300, 300, 20, 20, 10), o, x, taper: (1, 1.5, 1, 2)));
            yield return ("solid rectangle", FemBeam("R1", new SectionModel { SectionType = SectionModel.SectionTypes.SolidRectangle, B = 300, D = 500 }, o, x, 15));
            yield return ("hollow rectangle", FemBeam("R2", new SectionModel { SectionType = SectionModel.SectionTypes.HollowRectangle, B = 300, D = 500, T1 = 20, T2 = 15 }, o, x));
            yield return ("solid circle", FemBeam("C1", new SectionModel { SectionType = SectionModel.SectionTypes.SolidCircle, D = 400 }, o, x));
            yield return ("hollow circle", FemBeam("C2", new SectionModel { SectionType = SectionModel.SectionTypes.HollowCircle, D = 400, T = 20 }, o, x));
            yield return ("generic (equivalent circle, angle 0.3)", FemBeam("G1", new SectionModel { SectionType = SectionModel.SectionTypes.Generic, SectionArea = 40000, I11 = 4e8, I22 = 2e8, J = 1e8, Centroid = new Point2d(10, 20), AngleX1Rad = 0.3 }, o, x, 20, calculate: false));
            var fill = new GPC.Geometry.Polygon2d(); foreach (var (px, py) in new[] { (0d, 0d), (400d, 0d), (400d, 40d), (40d, 40d), (40d, 300d), (0d, 300d) }) fill.Add(px, py);
            yield return ("Brep L shape", FemBeam("S1", new SectionModel { SectionType = SectionModel.SectionTypes.GenericShapes, GenericShapes = new BridGHe.Common.Geometry.Shapes(new[] { new GPC.Geometry.Shape2d(fill, null) }) }, o, x, 10));
            var outer = new GPC.Geometry.Polygon2d(); foreach (var (px, py) in new[] { (100d, 50d), (300d, 50d), (300d, 450d), (100d, 450d) }) outer.Add(px, py);
            var hole = new GPC.Geometry.Polygon2d(); foreach (var (px, py) in new[] { (150d, 100d), (250d, 100d), (250d, 400d), (150d, 400d) }) hole.Add(px, py);
            yield return ("Brep with a hole", FemBeam("S2", new SectionModel { SectionType = SectionModel.SectionTypes.GenericShapes, GenericShapes = new BridGHe.Common.Geometry.Shapes(new[] { new GPC.Geometry.Shape2d(outer, new[] { hole }) }) }, o, x));
        }
        else
        {
            yield return ("T", FemBeam("T1", new SectionModel { SectionType = SectionModel.SectionTypes.T, B = 200, T1 = 20, D = 300, T2 = 10 }, o, x, 25));
            yield return ("C", FemBeam("U1", new SectionModel { SectionType = SectionModel.SectionTypes.C, B = 100, T1 = 10, D = 300, T2 = 8 }, o, x));
            yield return ("angle", FemBeam("L1", new SectionModel { SectionType = SectionModel.SectionTypes.Angle, B = 150, T1 = 10, D = 100, T2 = 12 }, o, x, 0, 5, 7));
            yield return ("double C, gap 12", FemBeam("U2", new SectionModel { SectionType = SectionModel.SectionTypes.C, B = 100, T1 = 10, D = 300, T2 = 8, Mirror = SectionModel.MirrorTypes.Left, MirrorGapA = 12 }, o, x));
            yield return ("double angle, gap 8, angle 30", FemBeam("L2", new SectionModel { SectionType = SectionModel.SectionTypes.Angle, B = 150, T1 = 10, D = 100, T2 = 12, Mirror = SectionModel.MirrorTypes.Left, MirrorGapA = 8 }, o, x, 30));
        }
    }

    internal static void Display(bool native)
    {
        int compared = 0;
        // TRANSITIONAL (P2 removes the FEM side): every kind drawn from its GPC draft (the draft of the FEM value, the datum of the GPC-only path
        // after P2) equals the FEM drawing of the same element (BeamModel.CreateShapes).
        foreach (var (kind, fem) in FemBeams(native))
        {
            var draft = GpcBeamDrafts.FromFemm(fem);
            var drawn = GpcDraftShapes.Beam(draft);
            string diff = Compare(fem.Shapes, drawn);
            Check(diff == null, "TRANSITIONAL beam «" + kind + "»: the GPC drawing differs from the FEM drawing: " + diff);
            // The plug-in draws the same solids for the draft (the cache of ModelPreview), and a GH beam given by its draft alone clips to them.
            var shown = (Brep[])Call("DraftShapes", draft);
            Check(shown.Length == drawn.Length && Compare(drawn, shown) == null, "beam «" + kind + "»: the preview draws the solids of the draft");
            var goo = R2aDesignFixtures.Beam(draft);
            var clip = (BoundingBox)goo.GetType().GetProperty("ClippingBox").GetValue(goo);
            Check(SameBox(clip, (BoundingBox)Call("DraftBounds", draft)) && SameBox(clip, ModelPreviewBounds(fem)), "beam «" + kind + "»: the clipping box of the GPC beam is the FEM one: " + Box(clip));
            compared++;
            Console.WriteLine($"C3 DISPLAY {kind}: {drawn.Length} solids, box {Box(Union(drawn))}{(diff == null ? ", equal to the FEM drawing" : "")}");
        }
        if (!native)
        {
            // With the FEM value present nothing changes: the FEM beam draws from its value (Shapes of the FEM preview, its own box).
            var femOnly = FemBeams(false).First().Beam;
            var withFem = Make("Fem.GH_Beam", femOnly);
            Check(SameBox((BoundingBox)withFem.GetType().GetProperty("ClippingBox").GetValue(withFem), ModelPreviewBounds(femOnly)), "a beam with its FEM value clips to the FEM box");

            // Composite beam (TRANSITIONAL): the FEM composite beam and the solids and bars of its two definitions (Composite Beam).
            var p0 = BridgeGrillageExample.Composite(new()); p0.ConcreteSection = new(250, 3000, 2000, 40, 1500, 55); p0.ConsiderRebar = true;
            var p1 = BridgeGrillageExample.Composite(new()); p1.ConcreteSection = new(250, 2500, 2000, 40, 0, 0); p1.ConsiderRebar = true;
            var cb = new CompositeBeamModel(Point3d.Origin, new Point3d(12000, 3000, 500), p0, null, p1);
            var beamDraft = GpcBeamBuilders.New(new GPC.Geometry.Point3d(0, 0, 0), new GPC.Geometry.Point3d(12000, 3000, 500), R2aDesignFixtures.BaseProperty(), null, 0, 0, 0, R2aDesignFixtures.Id("c3:cb"), "CB1");
            var d0 = GpcCompositeSections.Definition(p0, p0.Name); var d1 = GpcCompositeSections.Definition(p1, p1.Name);
            string cdiff = Compare(cb.Shapes, GpcDraftShapes.Composite(beamDraft, d0, d1));
            Check(cdiff == null, "TRANSITIONAL composite beam: the GPC solids differ from the FEM drawing: " + cdiff);
            string bdiff = Compare((Line[])Call("ReinforcementLines", cb), GpcDraftShapes.CompositeBars(beamDraft, d0, d1));
            Check(bdiff == null, "TRANSITIONAL composite beam: the GPC bar lines differ from the FEM drawing: " + bdiff);
            var datum = Activator.CreateInstance(R2aDesignFixtures.Plugin.GetType("BridGHe.Grasshopper.DataTypes.GpcCompositeBeam", true), beamDraft, d0, d1, (IReadOnlyList<string>)Array.Empty<string>());
            Check(((Brep[])Call("DraftShapes", datum)).Length == 2 && ((Line[])Call("DraftReinforcementLines", datum)).Length == 6, "composite beam: the preview draws its two solids and six bar lines");
            compared++;
            Console.WriteLine($"C3 DISPLAY composite beam: {cb.Shapes.Length} solids, {((Line[])Call("ReinforcementLines", cb)).Length} bar lines{(cdiff == null && bdiff == null ? ", equal to the FEM drawing" : "")}");

            // Plate (TRANSITIONAL): the solid of a shell given by its draft alone, as the FEM plate.
            var corners = new[] { new Point3d(0, 0, 0), new Point3d(2000, 0, 0), new Point3d(2000, 1000, 300), new Point3d(0, 1000, 300) };
            var femPlate = new PlateModel("P1", corners, new PlatePropertyModel("1") { Name = "C3 shell", Material = SteelFem, PropertyType = PlatePropertyType.ShellThin, MembraneThickness = 200, BendingThickness = 200 });
            string pdiff = Compare(new[] { femPlate.Shape }, (Brep[])Call("DraftShapes", GpcBeamDrafts.FromFemm(femPlate)));
            Check(pdiff == null, "TRANSITIONAL plate: the GPC solid differs from the FEM plate: " + pdiff);
            compared++;

            // The homogenised composite property (TRANSITIONAL): the FEM beam draws the steel and the slab and six bar lines from the preview geometry
            // of its FEM property. P2R (T3, 12/11): the open point of C3 is closed by P2D (P11, user decision U5: the optional CompositeOutline of the
            // recipe): the GPC draft draws the same, the solids within the C3 criterion and the bar lines bit for bit (as the regression p2d).
            var homogenised = new BeamModel("B1", Point3d.Origin, new Point3d(12000, 0, 0), p0.CreateHomogenizedProperty(12, "Shrink"));
            var hd = GpcBeamDrafts.FromFemm(homogenised);
            var femBars = (Line[])Call("ReinforcementLines", homogenised); var gpcBars = (Line[])Call("DraftReinforcementLines", hd);
            string hdiff = Compare(homogenised.Shapes, (Brep[])Call("DraftShapes", hd));
            Check(homogenised.Shapes.Length == 2 && femBars.Length == 6 && GpcDraftShapes.HasCompositePreview(hd) && hdiff == null && gpcBars.Length == 6 && femBars.SequenceEqual(gpcBars),
                "TRANSITIONAL homogenised composite property: FEM 2 solids and 6 bar lines, the GPC draft the same (" + (hdiff ?? "solids equal") + $", {gpcBars.Length} bar lines)");
            Console.WriteLine("C3 DISPLAY homogenised composite property: FEM 2 solids and 6 bar lines, the GPC draft the same (P11 of P2D)");

            Explicit();
        }
        Console.WriteLine($"C3 DISPLAY: {compared} kinds compared with the FEM drawing{(native ? " (native sections)" : "")}");
    }

    private static BoundingBox Union(IEnumerable<Brep> shapes) { var box = BoundingBox.Empty; foreach (var s in shapes) box.Union(s.GetBoundingBox(true)); return box; }
    private static BoundingBox ModelPreviewBounds(ElementModel e) => (BoundingBox)Call("Bounds", e);

    /// <summary>The FEM-backed GH type as the components make it (the constructor from the FEM value, optional parameters at their defaults).</summary>
    private static object Make(string type, object value)
    {
        var t = R2aDesignFixtures.Plugin.GetType("BridGHe.Grasshopper.DataTypes." + type, true);
        var ctor = t.GetConstructors().Where(c => c.GetParameters().Length >= 1 && c.GetParameters()[0].ParameterType.IsInstanceOfType(value) && c.GetParameters().Skip(1).All(p => p.HasDefaultValue))
            .OrderBy(c => c.GetParameters().Length).First();
        return ctor.Invoke(new[] { value }.Concat(ctor.GetParameters().Skip(1).Select(p => p.DefaultValue)).ToArray());
    }

    /// <summary>Explicit counts and boxes, GPC data alone (one case of each kind).</summary>
    private static void Explicit()
    {
        // A beam: I 600 (flanges 300 × 20, web 10), 5000 along X, BridGHe offset (10, 20): one solid, section x along +Y, y along +Z.
        var steel = R2aDesignFixtures.Steel();
        var iProperty = GpcPropertyBuilders.Beam("C3 I", 5, GpcSectionBuilders.Standard(BridgeSectionKind.I, 600, b1: 300, b2: 300, t1: 20, t2: 20, t3: 10), steel, "Steel");
        var i = GpcBeamBuilders.New(new GPC.Geometry.Point3d(0, 0, 0), new GPC.Geometry.Point3d(5000, 0, 0), iProperty, null, 0, 10, 20, R2aDesignFixtures.Id("c3:i"), "I1");
        var iShapes = GpcDraftShapes.Beam(i);
        Check(iShapes.Length == 1 && iShapes[0].IsSolid && IsBox(iShapes[0].GetBoundingBox(true), 0, -140, -280, 5000, 160, 320) && Near(Volume(iShapes[0]), (300 * 20 * 2 + 10 * 560) * 5000.0, 1e-9),
            "explicit beam: one solid, box [0 -140 -280]-[5000 160 320], volume 88e6: " + (iShapes.Length == 1 ? Box(iShapes[0].GetBoundingBox(true)) + " " + R(Volume(iShapes[0])) : iShapes.Length + " solids"));

        // A composite beam: steel I 1000 (bottom flange 600 × 40, top 400 × 30, web 20), slab 3000 × 250, rows 2000 at 40 and 1500 at 55, 12 m
        // along X: the steel and the slab, six bar lines; the centroid of the steel from the bottom by its three rectangles.
        var numbers = GpcCompositeDefinitions.Grillage(new()) with
        {
            Name = "C3 composite", Height = 1000, Web = 20, TopWidth = 400, TopThickness = 30, BottomWidth = 600, BottomThickness = 40, SlabWidth = 3000, SlabHeight = 250,
            TopArea = 2000, TopCover = 40, BottomArea = 1500, BottomCover = 55,
        };
        var definition = GpcCompositeDefinitions.Create(numbers);
        var beam = GpcBeamBuilders.New(new GPC.Geometry.Point3d(0, 0, 0), new GPC.Geometry.Point3d(12000, 0, 0), R2aDesignFixtures.BaseProperty(), null, 0, 0, 0, R2aDesignFixtures.Id("c3:composite"), "CB2");
        double cy = (600 * 40 * 20 + 20 * 930 * (40 + 465) + 400 * 30 * 985.0) / (600 * 40 + 20 * 930 + 400 * 30.0), top = 1000 - cy;
        var cs = GpcDraftShapes.Composite(beam, definition, definition);
        Check(cs.Length == 2 && IsBox(cs[0].GetBoundingBox(true), 0, -300, -cy, 12000, 300, top) && IsBox(cs[1].GetBoundingBox(true), 0, -1500, top, 12000, 1500, top + 250),
            $"explicit composite beam: steel [0 -300 {R(-cy)}]-[12000 300 {R(top)}] and slab [0 -1500 {R(top)}]-[12000 1500 {R(top + 250)}]: " + string.Join(" ", cs.Select(s => Box(s.GetBoundingBox(true)))));
        var bars = GpcDraftShapes.CompositeBars(beam, definition, definition);
        var expected = new[] { top + 250 - 40, top + 55 }.SelectMany(z => new[] { -1000d, 0, 1000 }.Select(y => new Line(new Point3d(0, y, z), new Point3d(12000, y, z)))).ToArray();
        Check(Compare(expected, bars) == null, "explicit composite beam: six bar lines at y -1000, 0, 1000 and z top - cover: " + Compare(expected, bars));

        // A plate: shell 2000 × 1000 in the XY plane, membrane 200: one solid [0 0 -100]-[2000 1000 100], volume 4e8.
        var concrete = R2aDesignFixtures.Material("Concrete", "C3 C", 34000, .2);
        var plate = GpcPlateBuilders.Create(new[] { (0d, 0d, 0d), (2000d, 0d, 0d), (2000d, 1000d, 0d), (0d, 1000d, 0d) }, GpcPropertyBuilders.Plate("C3 shell", 0, concrete, 200, 200), null, 0, 0, "P1", R2aDesignFixtures.Id("c3:plate"));
        var ps = (Brep[])Call("DraftShapes", plate);
        // (The volume of the FEM plate solid is signed by the orientation of its extrusion: its magnitude.)
        Check(ps.Length == 1 && IsBox(ps[0].GetBoundingBox(true), 0, 0, -100, 2000, 1000, 100) && Near(Math.Abs(Volume(ps[0])), 4e8, 1e-9),
            "explicit plate: one solid [0 0 -100]-[2000 1000 100], volume 4e8: " + (ps.Length == 1 ? Box(ps[0].GetBoundingBox(true)) + " " + R(Volume(ps[0])) : ps.Length + " solids"));

        // The homogenised composite property built on GPC (Composite Beam Property, n 12). P2R (T3, 12/11): since P2D (P11, U5) it draws the steel
        // and the slab (two solids) and the six bar lines of its outline, as the composite beam above (before: nothing, the open point of C3).
        var shrink = GpcPropertyBuilders.Homogenised(definition, 12, "Shrink", GpcCompositeDefinitions.AnalysisSteel(numbers), "Steel");
        var h = GpcBeamBuilders.New(new GPC.Geometry.Point3d(0, 0, 0), new GPC.Geometry.Point3d(12000, 0, 0), shrink, null, 0, 0, 0, R2aDesignFixtures.Id("c3:homogenised"), "H1");
        Check(GpcDraftShapes.HasCompositePreview(h) && ((Brep[])Call("DraftShapes", h)).Length == 2 && ((Line[])Call("DraftReinforcementLines", h)).Length == 6,
            "explicit homogenised property: two solids and six bar lines (" + ((Brep[])Call("DraftShapes", h)).Length + " solids, " + ((Line[])Call("DraftReinforcementLines", h)).Length + " bar lines)");
        Console.WriteLine($"C3 DISPLAY explicit: beam 1 solid {Box(iShapes.FirstOrDefault()?.GetBoundingBox(true) ?? BoundingBox.Empty)}; composite beam {cs.Length} solids, {bars.Length} bar lines; plate {ps.Length} solid; homogenised property 2 solids");
    }

    // ---------------------------------------------------------------------------------------------------------------------------------------
    // Native: what Straus7 receives (gap 1) and the native sections of the preview (gap 2)

    [DllImport("kernel32", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string name);
    [DllImport("kernel32", CharSet = CharSet.Ansi)] private static extern IntPtr GetProcAddress(IntPtr module, string name);
    [UnmanagedFunctionPointer(CallingConvention.Winapi, CharSet = CharSet.Ansi)] private delegate int NewFile(int uid, string path, string scratch);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int OneInt(int uid);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int Units(int uid, int[] units);
    [UnmanagedFunctionPointer(CallingConvention.Winapi, CharSet = CharSet.Ansi)] private delegate int NewProperty(int uid, int prop, int type, string name);
    [UnmanagedFunctionPointer(CallingConvention.Winapi, CharSet = CharSet.Ansi)] private delegate int NewPlateProperty(int uid, int prop, int type, int material, string name);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int Doubles(int uid, int prop, double[] doubles);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int ThreeInts(int uid, int a, int b);
    [UnmanagedFunctionPointer(CallingConvention.Winapi)] private delegate int RefInt(int uid, int prop, ref int value);

    private static T F<T>(string name) where T : Delegate
    {
        var address = GetProcAddress(GetModuleHandle("St7api.dll"), name);
        if (address == IntPtr.Zero) throw new InvalidOperationException("C3 native: no St7API function " + name);
        return Marshal.GetDelegateForFunctionPointer<T>(address);
    }
    private static void C(int code, string what) { if (code != 0) throw new InvalidOperationException($"C3 native: {what} returned {code}"); }
    private static string Rs(IEnumerable<double> v) => string.Join(" ", v.Select(R));

    /// <summary>
    /// <c>--c3 --native</c>: (a) a model given by its GPC data alone (Build Model on GPC drafts, the GPC-only path of Straus R3 Analysis without
    /// the solution) with a beam and a thin shell on every material of <see cref="Cases"/>, written by the Converter and read back from the
    /// .st7, against the calls of 0.3.0 replayed on a scratch model (St7SetBeamMaterialData and St7SetBeamShearModulusMode, St7SetPlateIsotropicMaterial
    /// with the numbers of the former material): E, G (or Use Poisson: the effective G, listed difference E7), ν, density, α, damping and
    /// thermal data; (b) the preview of the T, C, angle and double sections (native Straus7 sections) against the FEM drawing.
    /// </summary>
    /// <summary>The model of the native readback: Build Model on GPC drafts alone (the GPC-only path), a beam (solid rectangle 300 × 500,
    /// Beam) and a thin shell (200 + 200) on every material of <see cref="Cases"/>, named «C3 beam k» and «C3 shell k».</summary>
    private static GpcModels.Conversion MaterialsModel()
    {
        var cases = Cases();
        var units = new BridgeUnits(DefinitionUnits.Nmm);
        var beams = new List<LegacyBeamDraft>(); var plates = new List<LegacyPlateDraft>();
        for (int k = 0; k < cases.Length; k++)
        {
            var m = GpcMaterialBuilders.Material(cases[k].Recipe);
            var bp = GpcPropertyBuilders.Beam("C3 beam " + k, 5, GpcSectionBuilders.Standard(BridgeSectionKind.SolidRectangle, 500, 300), m, GpcMaterials.KindOf(m));
            beams.Add(GpcBeamBuilders.New(new GPC.Geometry.Point3d(0, 2000 * k, 0), new GPC.Geometry.Point3d(3000, 2000 * k, 0), bp, null, 0, 0, 0, R2aDesignFixtures.Id("c3:native-beam:" + k), "B" + k));
            var pp = GpcPropertyBuilders.Plate("C3 shell " + k, 0, m, 200, 200);
            double y = -3000 - 2000 * k;
            plates.Add(GpcPlateBuilders.Create(new[] { (0d, y, 0d), (1000d, y, 0d), (1000d, y + 1000, 0d), (0d, y + 1000, 0d) }, pp, null, 0, 0, "P" + k, R2aDesignFixtures.Id("c3:native-plate:" + k)));
        }
        var conversion = LegacyBuildModel.Build("C3 materials", units, 0.001, new List<(Guid, LegacyNodeDraft)>(), beams.Select(b => (b.Beam.Guid, b)).ToList(),
            plates.Select(p => (p.Plate.Guid, p)).ToList(), new List<LegacyCombinationDraft>(), new List<LegacyStageDraft>(), gpcOnly: true);
        GpcPreflight.Require(conversion.Model);
        return conversion;
    }

    /// <summary>The material numbers 0.3.0 wrote for a recipe (StrausR3Helper, N-mm: factors 1): St7SetBeamMaterialData {E, G, ν, ρ, α,
    /// viscous damping, damping ratio, conductivity, specific heat} with the shear modulus mode, St7SetPlateIsotropicMaterial {E, ν, ρ, α,
    /// viscous damping, damping ratio, conductivity, specific heat}.</summary>
    private static (double[] Beam, int Mode, double[] Plate) Former(LegacyMaterialRecipe r) =>
        (new[] { r.Modulus, r.ShearModulus, r.PoissonRatio, r.Density, r.ThermalExpansion, r.ViscousDamping, r.DampingRatio, r.Conductivity, r.SpecificHeat }, r.UsePoisson ? 0 : 1,
         new[] { r.Modulus, r.PoissonRatio, r.Density, r.ThermalExpansion, r.ViscousDamping, r.DampingRatio, r.Conductivity, r.SpecificHeat });

    /// <summary>Not native: the write plan of the Converter for <see cref="MaterialsModel"/> (recorded, <see cref="Straus7Recorder"/>): the
    /// material arrays of every beam and shell property against the numbers of 0.3.0 (<see cref="Former"/>; with Use Poisson the G of the
    /// beam is E/2(1+ν), listed difference E7), and the table of the beams and shells of a table material.</summary>
    private static void WritePlan()
    {
        var cases = Cases();
        var calls = Straus7Recorder.Record(MaterialsModel());
        static double[] Numbers(string call)
        {
            // CreateBeamProperty(id, 'name', formulation, [section], [material]) / CreatePlateProperty(id, 'name', formulation, bending, membrane, [material], orthotropic)
            int open = call.LastIndexOf('['), close = call.LastIndexOf(']');
            return call.Substring(open + 1, close - open - 1).Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(s => double.Parse(s, CultureInfo.InvariantCulture)).ToArray();
        }
        int laws = calls.Count(c => c.StartsWith("SetMaterialLaw(", StringComparison.Ordinal));
        // The Converter names a plate property after its plate (a property per plate, in the order of the plates): the k-th shell.
        var plateCalls = calls.Where(c => c.StartsWith("CreatePlateProperty(", StringComparison.Ordinal)).ToList();
        for (int k = 0; k < cases.Length; k++)
        {
            var r = cases[k].Recipe; var former = Former(r); string label = "write plan «" + cases[k].Tag + "»";
            string beam = calls.FirstOrDefault(c => c.StartsWith("CreateBeamProperty(", StringComparison.Ordinal) && c.Contains("'C3 beam " + k + "'"));
            string plate = plateCalls.Count == cases.Length ? plateCalls[k] : null;
            Check(beam != null && plate != null, label + ": the beam and shell properties are written (" + string.Join(" / ", calls.Where(c => c.StartsWith("Create", StringComparison.Ordinal) && c.Contains("Property(")).Select(c => c.Substring(0, Math.Min(60, c.Length)))) + ")");
            if (beam == null || plate == null) continue;
            var b = Numbers(beam); var p = Numbers(plate);
            double g = r.UsePoisson ? r.Modulus / (2 * (1 + r.PoissonRatio)) : r.ShearModulus;
            Check(b.Length == 9 && b[0] == former.Beam[0] && b[1] == g && Enumerable.Range(2, 7).All(j => b[j] == former.Beam[j]), label + $": beam material {Rs(b)}, 0.3.0 {Rs(former.Beam)} (G {R(g)})");
            Check(p.Length == 8 && Enumerable.Range(0, 8).All(j => p[j] == former.Plate[j]), label + $": shell material {Rs(p)}, 0.3.0 {Rs(former.Plate)}");
        }
        Check(laws == 2 * cases.Count(c => c.HasTable), $"write plan: {laws} material laws, one on the beam and one on the shell of each table material");
        Console.WriteLine($"C3 WRITE PLAN: {cases.Length} beam and shell properties with the material numbers of 0.3.0, {laws} tables");
    }

    internal static int Native()
    {
        failures.Clear(); checks = 0;
        var cases = Cases();
        var conversion = MaterialsModel();
        string folder = Path.GetFullPath(Path.Combine("artifacts", "native", Guid.NewGuid().ToString("N"))); Directory.CreateDirectory(folder);
        string st7 = Path.Combine(folder, "c3-materials.st7");
        var plan = Straus7ModelWritePlan.Prepare(conversion.Model, null, null, conversion.Report.NativeSections);
        using (var writer = new Straus7NativeWriter(st7, folder)) { plan.Write(writer, conversion.Model); writer.Save(); }
        using (var api = new Straus7NativeApi())
        {
            api.OpenModelReadOnly(st7, folder);
            try
            {
                var beamByName = new Dictionary<string, Straus7Property>(StringComparer.Ordinal); var plateByName = new Dictionary<string, Straus7Property>(StringComparer.Ordinal);
                var beamMode = new Dictionary<string, int>(StringComparer.Ordinal);
                for (int b = 1; b <= api.Count(Straus7Entity.Beam); b++)
                {
                    var p = api.ReadProperty(Straus7Entity.Beam, api.ReadElement(Straus7Entity.Beam, b).Property); beamByName[p.Name] = p;
                    int mode = -1; C(F<RefInt>("St7GetBeamShearModulusMode")(1, p.Number, ref mode), "St7GetBeamShearModulusMode"); beamMode[p.Name] = mode;
                }
                // The Converter names a plate property after its plate (one per plate, in the order of the plates): the k-th shell by its plate.
                for (int q = 1; q <= api.Count(Straus7Entity.Plate); q++) { var p = api.ReadProperty(Straus7Entity.Plate, api.ReadElement(Straus7Entity.Plate, q).Property); plateByName["C3 shell " + (q - 1)] = p; }
                Console.WriteLine($"C3 NATIVE .st7: {api.Count(Straus7Entity.Beam)} beams, properties {string.Join(" | ", beamByName.Keys)}; {api.Count(Straus7Entity.Plate)} plates, properties {string.Join(" | ", plateByName.Keys)}");
                Console.WriteLine($"C3 NATIVE model: beam properties {string.Join(" | ", conversion.Model.BeamProperties.Keys)}");
                // 0.3.0 replayed (file 7): the material calls of StrausR3Helper for these materials, in N-mm (the factors of the units are 1).
                string scratch = Path.Combine(folder, "c3-030.st7");
                C(F<NewFile>("St7NewFile")(7, scratch, folder), "St7NewFile");
                try
                {
                    C(F<Units>("St7SetUnits")(7, new[] { 2, 0, 2, 1, 0, 0 }), "St7SetUnits");
                    for (int k = 0; k < cases.Length; k++)
                    {
                        var r = cases[k].Recipe; int id = k + 1;
                        C(F<NewProperty>("St7NewBeamProperty")(7, id, 6, "C3 0.3.0 beam " + k), "St7NewBeamProperty");
                        var m9 = new[] { r.Modulus, r.ShearModulus, r.PoissonRatio, r.Density, r.ThermalExpansion, r.ViscousDamping, r.DampingRatio, r.Conductivity, r.SpecificHeat };
                        C(F<Doubles>("St7SetBeamMaterialData")(7, id, m9), "St7SetBeamMaterialData");
                        C(F<ThreeInts>("St7SetBeamShearModulusMode")(7, id, r.UsePoisson ? 0 : 1), "St7SetBeamShearModulusMode");
                        C(F<ThreeInts>("St7UpdateElementPropertyData")(7, 1, id), "St7UpdateElementPropertyData");
                        var former = new double[9]; C(F<Doubles>("St7GetBeamMaterialData")(7, id, former), "St7GetBeamMaterialData");
                        int formerMode = -1; C(F<RefInt>("St7GetBeamShearModulusMode")(7, id, ref formerMode), "St7GetBeamShearModulusMode");
                        C(F<NewPlateProperty>("St7NewPlateProperty")(7, id, 4, 1, "C3 0.3.0 shell " + k), "St7NewPlateProperty");
                        C(F<Doubles>("St7SetPlateThickness")(7, id, new[] { 200d, 200d }), "St7SetPlateThickness");
                        var m8 = new[] { r.Modulus, r.PoissonRatio, r.Density, r.ThermalExpansion, r.ViscousDamping, r.DampingRatio, r.Conductivity, r.SpecificHeat };
                        C(F<Doubles>("St7SetPlateIsotropicMaterial")(7, id, m8), "St7SetPlateIsotropicMaterial");
                        var formerPlate = new double[8]; C(F<Doubles>("St7GetPlateIsotropicMaterial")(7, id, formerPlate), "St7GetPlateIsotropicMaterial");

                        string label = "«" + cases[k].Tag + "»";
                        var gp = beamByName.TryGetValue("C3 beam " + k, out var fb) ? fb : null; var gs = plateByName.TryGetValue("C3 shell " + k, out var fs) ? fs : null;
                        Check(gp != null && gs != null, label + ": the beam and shell properties are in the .st7");
                        if (gp == null || gs == null) continue;
                        var mat = gp.Material; int mode = beamMode[gp.Name];
                        static double G(double[] m, int mode) => mode == 1 ? m[1] : m[0] / (2 * (1 + m[2]));
                        Console.WriteLine($"C3 NATIVE {label} beam: GPC {Rs(mat)} mode {mode}; 0.3.0 {Rs(former)} mode {formerMode}");
                        Console.WriteLine($"C3 NATIVE {label} shell: GPC {Rs(gs.Material)}; 0.3.0 {Rs(formerPlate)}");
                        // E, ν, density, α, damping and thermal data as 0.3.0 wrote them; G: the explicit G with its mode, as 0.3.0; with Use Poisson the
                        // effective G (E7: 0.3.0 wrote the mode «from ν», GPC writes G = E/2(1+ν) explicitly).
                        // A Table Material has E 0: Straus7 reads ν and G of such a beam back as it keeps them (open question 3 of C1), so only
                        // E, density, α, damping and thermal data are compared there (printed above in full).
                        bool table = r.Modulus == 0;
                        // E7 (listed difference): with Use Poisson GPC writes G = E/2(1+ν) in the mode «G given», and Straus7 reads ν back from E
                        // and G, within one rounding of ν (0.19999999999999996 for 0.2); 0.3.0 wrote the mode «from ν». Every other value exact.
                        Check(mat.Length >= 9 && new[] { 0, 3, 4, 5, 6, 7, 8 }.All(j => mat[j] == former[j]) && (table || (r.UsePoisson ? Near(mat[2], former[2], 1e-15) : mat[2] == former[2])),
                            label + ": beam material data (but G) as 0.3.0");
                        if (!table)
                            Check(r.UsePoisson ? mode == 1 && formerMode == 0 && Near(G(mat, mode), G(former, formerMode), 1e-12) : mode == 1 && formerMode == 1 && mat[1] == former[1] && mat[1] == r.ShearModulus,
                                label + $": G {R(G(mat, mode))} (mode {mode}), 0.3.0 {R(G(former, formerMode))} (mode {formerMode})");
                        Check(gs.Material.Length >= 8 && Enumerable.Range(0, 8).All(j => gs.Material[j] == formerPlate[j]), label + ": shell material data as 0.3.0");
                    }
                }
                finally { F<OneInt>("St7CloseFile")(7); }
            }
            finally { api.CloseModel(); }
        }
        Display(true);
        foreach (var f in failures) Console.WriteLine("  C3 NATIVE FAIL: " + f);
        Console.WriteLine($"C3 NATIVE: {cases.Length} materials on a beam and a shell read back from Straus7 against 0.3.0, native sections of the preview; {checks} checks, {failures.Count} failures");
        return failures.Count;
    }
}
