#if GPC_PILOT
using System;
using System.Linq;
using BridGHe.Common.Checks;
using BridGHe.Common.Gpc;
using GPC.Checkers.CompositeBridge;
using GPC.Model.Materials;
using GPC.Model.Sections;
using GPC.Model.Sections.Concrete;
using GPC.Model.Sections.Rebar;
using Numbers = GpcCompositeDefinitions.Numbers;
using CompositeShrinkageActions = BridGHe.Common.Checks.CompositeShrinkageActions;

/// <summary>
/// Chain E step 1 (coordinator decision of 5/11, option ii): the homogenised analysis properties of a composite girder from the Checker
/// definition (<see cref="BridgeCompositeSectionDefinition.AnalysisSection"/> with the rows kept by <see cref="GpcCompositeSections"/>;
/// <see cref="GpcCompositeSections.Homogenised"/>, <see cref="GpcCompositeSections.AnalysisProperties"/>,
/// <see cref="GpcCompositeSections.SteelTorsionConstant"/>, <see cref="CompositeShrinkageActions"/>) against the former values of the
/// composite property they replace, every value by its bits, the same exceptions (by type):
/// <list type="bullet">
/// <item>the bars of the analysis section (positions, diameters, modulus), the slab and the steel H;</item>
/// <item>the eleven homogenised values for every choice of concrete, bars and steel (Composite Section Geometric Property) at n0, n∞, n,sh
/// and two other ratios;</item>
/// <item>the generic homogenised property (area, I11, I22, J, shear areas, centroid, angle) with Consider Concrete as given and toggled
/// (Bridge State Override: uncracked and cracked) at the same ratios;</item>
/// <item>the stresses of Self Tension, the analysis steel and Composite Shrinkage Actions.</item>
/// </list>
/// <para>R6.6 (P2U): the former values come from the numbers of the composite property (<see cref="Numbers"/>) with the former rules of
/// <c>CompositeBeamPropertyModel</c> kept here as the reference (<c>GetReinforcedConcreteSection</c>, <c>GetHomogeneizedMechanicalProperties</c>,
/// <c>CreateHomogenizedProperty</c>, <c>GetOnlySteelMechanicalProperties</c>, <c>CompositeShrinkageActions.Calculate(property, strain)</c>, the
/// legacy conversion of the steel material), not from FEM properties. Not native: <c>gpc-composite-sections</c> (its properties and every
/// definition of the R6.0 capture). While the FEM properties exist, <c>CompositeAnalysisPropertyChecks.Fem.cs</c> keeps the former comparison
/// with them (<c>CompareFem</c>, the same values from the FEM property) and the native gate of 50 and 99 (<c>Require</c>).</para>
/// </summary>
internal static partial class CompositeAnalysisPropertyChecks
{
    private static string B(double v) => BitConverter.DoubleToInt64Bits(v).ToString("X16");

    /// <summary>Self-tension results whose echoed rebar material differs (bar ultimate strain of the property other than 0.1).</summary>
    internal static int EchoedRebarDifferences;

    private static string Try(Func<string> value)
    {
        try { return value(); }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or InvalidOperationException) { return "throws " + ex.GetType().Name; }
    }

    private static string Values((double areaH, double SxH, double SyH, GPC.Geometry.Point2d centroidH, double JxxH, double JyyH, double JxyH, double JpH, double J11H, double J22H, double angleX) p) =>
        string.Join(" ", new[] { p.areaH, p.SxH, p.SyH, p.centroidH.X, p.centroidH.Y, p.JxxH, p.JyyH, p.JxyH, p.JpH, p.J11H, p.J22H, p.angleX }.Select(B));

    private static string Bars(ReinforcedConcreteSection s)
    {
        var h = (SectionH)s.SteelSections[0].Section.SectionShape; var at = s.SteelSections[0];
        return string.Join(";", s.Rebars.Select(b => B(b.Position.X) + "," + B(b.Position.Y) + "," + B(b.RebarSection.Diameter) + "," + B(b.RebarSection.RebarMaterial.ElasticModulusTension)))
            + "|slab " + B(s.Width) + "," + B(s.Height) + "," + B(s.ConcreteMaterial.ElasticModulusCompression) + "," + s.ConcreteMaterial.Name
            + "|steel " + string.Join(",", new[] { h.Height, h.ThicknessWeb, h.LenghtTopFlange, h.ThicknessTopFlange, h.LenghtBottomFlange, h.ThicknessBottomFlange, h.Jt,
                at.Section.SteelMaterial.ElasticModulusTension, at.Rotation }.Select(B)) + "|" + s.Name;
    }

    private static string Property(HomogenisedAnalysisProperty h) =>
        string.Join(" ", new[] { h.Area, h.I11, h.I22, h.J, h.ShearArea1, h.ShearArea2, h.CentroidX, h.CentroidY, 0 }.Select(B));

    private static string Shrinkage(CompositeShrinkageActions a) => string.Join(" ", new[] { a.FreeStrain, a.Curvature, a.PreStrain, a.ThermalGradient, a.EigenForce, a.EigenMoment, a.Centroid }.Select(B));

    // TESTS ONLY: the former rules of the composite property (CompositeBeamPropertyModel, BridGHe 0.3.0), on its numbers.

    /// <summary>The former rule of a valid slab (<c>SectionConcreteSlabModel.IsValid</c>): finite positive height and width, finite
    /// nonnegative areas and finite covers, an active row inside the slab, two active rows not overlapping.</summary>
    internal static bool SlabValid(Numbers x) => double.IsFinite(x.SlabHeight) && x.SlabHeight > 0 && double.IsFinite(x.SlabWidth) && x.SlabWidth > 0
        && double.IsFinite(x.TopArea) && x.TopArea >= 0 && double.IsFinite(x.BottomArea) && x.BottomArea >= 0
        && double.IsFinite(x.TopCover) && double.IsFinite(x.BottomCover)
        && (x.TopArea == 0 || (x.TopCover > 0 && x.TopCover < x.SlabHeight))
        && (x.BottomArea == 0 || (x.BottomCover > 0 && x.BottomCover < x.SlabHeight))
        && (x.TopArea == 0 || x.BottomArea == 0 || x.TopCover + x.BottomCover < x.SlabHeight);

    /// <summary>The former analysis section (<c>GetReinforcedConcreteSection</c>): steel H named by the property, EN 1992 materials (the bars
    /// with the ultimate strain of the rebar parameter, 0.1 when it has none), one bar per row of the whole area of the row at the pitch of
    /// the slab width.</summary>
    internal static ReinforcedConcreteSection FormerAnalysisSection(Numbers x)
    {
        if (!SlabValid(x)) throw new ArgumentException("Invalid concrete slab or reinforcement rows.");
        var sectionH = new SectionH(x.Height, x.Web, x.TopWidth, x.TopThickness, x.BottomWidth, x.BottomThickness, x.Name);
        var concrete = new ConcreteMaterialEN1992(x.Concrete, x.Fck, ConcreteMaterial.CompressionStressStrainDiagrams.ParabolaRectangle);
        var steel = new SteelMaterialEN1992(x.Steel, x.SteelE, x.SteelFy, x.SteelFu, 0.1, SteelMaterial.StressStrainCurveType.ElasticPerfectPlastic, SteelMaterial.SteelTypes.Structural);
        var rebar = new SteelMaterialEN1992(x.Rebar, x.RebarE, x.RebarFy, x.RebarFu, x.RebarUltimateStrain ?? 0.1, SteelMaterial.StressStrainCurveType.ElasticPerfectPlastic, SteelMaterial.SteelTypes.Rebar);
        double rebarDiameterTop = Math.Sqrt(x.TopArea * 4 / Math.PI);
        var top = rebarDiameterTop != 0 ? new RebarSectionCircular("", rebarDiameterTop, rebar) : null;
        double rebarDiameterBottom = Math.Sqrt(x.BottomArea * 4 / Math.PI);
        var bottom = rebarDiameterBottom != 0 ? new RebarSectionCircular("", rebarDiameterBottom, rebar) : null;
        return CompositeSectionFactory.Create(x.SlabWidth, x.SlabHeight, concrete, top, x.SlabWidth / 1.0, x.TopCover, bottom, x.SlabWidth / 1.0, sectionH, steel, x.BottomCover, x.VerticalOffset, x.Name);
    }

    /// <summary>The former homogenised values (<c>GetHomogeneizedMechanicalProperties</c>) of the analysis section.</summary>
    internal static (double areaH, double SxH, double SyH, GPC.Geometry.Point2d centroidH, double JxxH, double JyyH, double JxyH, double JpH, double J11H, double J22H, double angleX)
        FormerHomogenised(Numbers x, double n, bool considerConcrete, bool considerRebars, bool considerSteelSections)
    {
        var section = FormerAnalysisSection(x);
        double phi = ReinforcedConcreteSection.CalculateHomogenizedFactorPhi(n, section.SteelSections.First().Section.SteelMaterial, section.ConcreteMaterial);
        return BridGHe.Common.Helpers.CompositePropertyAdapter.Calculate(section, phi, considerConcrete, considerRebars, considerSteelSections);
    }

    /// <summary>The former torsion of the steel alone (<c>GetOnlySteelMechanicalProperties().Jt</c>): a new H of the dimensions of the steel.</summary>
    internal static double FormerSteelTorsion(Numbers x) => new SectionH(x.Height, x.Web, x.TopWidth, x.TopThickness, x.BottomWidth, x.BottomThickness, x.Name).Jt;

    /// <summary>The former generic homogenised property (<c>CreateHomogenizedProperty</c> with Consider Concrete =
    /// <paramref name="considerConcrete"/>): area and inertias of the analysis section divided by n, the torsion of the steel alone, the
    /// homogenised shear areas of the steel and the slab, the centroid, angle 0. Lane INR (12/11, user decision option B, a declared change of
    /// 0.4.0): the inertias on the section axes, I11 about the horizontal axis (<see cref="InrInertia.HorizontalFirst"/>, written in the test);
    /// the variants whose values differ from the 0.3.0 rule (the sorted principal values) are counted in <see cref="AxisOneHorizontal"/>.</summary>
    internal static HomogenisedAnalysisProperty FormerHomogenisedProperty(Numbers x, double n, bool considerConcrete)
    {
        if (!double.IsFinite(n) || n <= 0) throw new ArgumentOutOfRangeException(nameof(n));
        var p = FormerHomogenised(x, n, considerConcrete, x.ConsiderRebar, true);
        double j = FormerSteelTorsion(x);
        var shear = GpcCompositeSections.HomogenisedShearAreas(x.BottomWidth, x.TopWidth, x.Height, x.BottomThickness, x.TopThickness, x.Web, x.SlabWidth, x.SlabHeight, considerConcrete, n, x.Name);
        var axes = InrInertia.HorizontalFirst(p.J11H, p.J22H, p.JxxH, p.JyyH, p.angleX);
        if (axes.Changed) AxisOneHorizontal++;
        return new HomogenisedAnalysisProperty(p.areaH / n, axes.I11 / n, axes.I22 / n, j, shear.Area1, shear.Area2, p.centroidH.X, p.centroidH.Y);
    }

    /// <summary>Lane INR: the homogenised property variants of the former rule written on the section axes instead of the 0.3.0 sorted
    /// principal values (Iyy &gt; Ixx: a wide slab, or the ratios 1 and 15.3 of the checks).</summary>
    internal static int AxisOneHorizontal;

    /// <summary>The former Composite Shrinkage Actions of the composite property (<c>CompositeShrinkageActions.Calculate(property, strain)</c>):
    /// n,sh, E and α of the steel parameter, the homogenised values and the bars of the former analysis section.</summary>
    internal static CompositeShrinkageActions FormerShrinkage(Numbers x, double strain)
    {
        if (!SlabValid(x) || !x.ConsiderConcrete) throw new ArgumentException("Shrinkage requires a valid composite section with concrete enabled.");
        if (!double.IsFinite(strain)) throw new ArgumentOutOfRangeException(nameof(strain));
        double n = x.NShrinkage, ea = x.SteelE, alpha = x.SteelThermalExpansion;
        if (!double.IsFinite(n) || n <= 0 || !double.IsFinite(alpha) || alpha == 0) throw new ArgumentException("Positive n ritiro and a nonzero steel thermal expansion coefficient are required.");
        var p = FormerHomogenised(x, n, true, x.ConsiderRebar, true);
        if (Math.Abs(p.JxyH) > 1e-10 * (p.JxxH + p.JyyH) || Math.Abs(p.J11H - p.JxxH) > 1e-8 * p.JxxH)
            throw new NotSupportedException("Shrinkage actions require the major bending axis to coincide with the slab plane's horizontal axis.");
        double ac = x.SlabWidth * x.SlabHeight, sc = ac * x.SlabHeight / 2;
        if (x.ConsiderRebar) foreach (var bar in FormerAnalysisSection(x).Rebars) { ac -= bar.RebarSection.Area; sc -= bar.RebarSection.Area * bar.Position.Y; }
        double force = ea / n * ac * strain, moment = ea / n * (sc - ac * p.centroidH.Y) * strain;
        double epsilon = force / (ea * p.areaH / n), curvature = moment / (ea * p.JxxH / n);
        return new CompositeShrinkageActions(epsilon, curvature, -epsilon, curvature / alpha, force, moment, p.centroidH.Y);
    }

    /// <summary>The former analysis steel: the legacy conversion of a steel material parameter that uses Poisson's ratio and has no damping,
    /// thermal or stress-strain data (the materials of the checks): the elastic GPC material of E, ν, density and α, no analysis data.</summary>
    internal static Material FormerAnalysisSteel(Numbers x)
    {
        if (!x.SteelUsePoisson) throw new NotSupportedException("Former analysis steel of a material without Poisson's ratio: not a material of these checks.");
        return new Material(string.IsNullOrWhiteSpace(x.Steel) ? "Material" : x.Steel, x.SteelE, x.SteelPoisson, x.SteelDensity, x.SteelThermalExpansion);
    }

    /// <summary>Compares the analysis values of the composite property of the numbers <paramref name="x"/> (the former rules) with those of
    /// <paramref name="definition"/>; the number of variants compared (an exception with the first difference otherwise).</summary>
    internal static int Compare(Numbers x, BridgeCompositeSectionDefinition definition, string label)
    {
        int compared = 0;
        void Same(string what, string expected, string actual)
        {
            if (expected != actual) throw new Exception($"COMPOSITE ANALYSIS PROPERTY FAIL ({label}, {x.Name}, {what}): former '{expected}' / definition '{actual}'");
            compared++;
        }
        if (definition.RebarRows == null) throw new Exception($"COMPOSITE ANALYSIS PROPERTY FAIL ({label}, {x.Name}): definition without rows");
        Same("analysis section", Try(() => Bars(FormerAnalysisSection(x))), Try(() => Bars(definition.AnalysisSection())));
        Same("steel torsion", B(FormerSteelTorsion(x)), B(GpcCompositeSections.SteelTorsionConstant(definition)));
        var ratios = new[] { x.NShortTerm, x.NLongTerm, x.NShrinkage, 1, 15.3 }.Where(n => double.IsFinite(n) && n > 0).Distinct().ToArray();
        foreach (double n in ratios)
        {
            foreach (bool c in new[] { true, false })
                foreach (bool r in new[] { true, false })
                    foreach (bool s in new[] { true, false })
                        Same($"homogenised n={n:R} concrete={c} rebars={r} steel={s}", Try(() => Values(FormerHomogenised(x, n, c, r, s))),
                            Try(() => Values(GpcCompositeSections.AnalysisProperties(definition, n, c, r, s))));
            foreach (bool concrete in new[] { true, false })
                Same($"homogenised property n={n:R} concrete={concrete}", Try(() => Property(FormerHomogenisedProperty(x, n, concrete))),
                    Try(() => Property(GpcCompositeSections.Homogenised(definition, n, concrete))));
            // Composite Beam Self Tension: the stresses of the H bridge engine on the analysis section of the definition (bar ultimate strain
            // 0.1) and on the former section of the property (the strain of its rebar material, 0.075 for the B450C of BridGHe).
            // Every value of the result is compared except the rebar material it echoes (the input of the engine): ultimate strains and
            // hardening modulus Et follow the ultimate strain of the bars (listed difference, counted in EchoedRebarDifferences).
            foreach (double strain in new[] { -2e-4, -3.5e-4, 1e-4 })
            {
                string former = Try(() => GpcCompositeSectionChecks.Dump(CompositeSelfTension.Calculate(FormerAnalysisSection(x), n, strain)));
                string gpc = Try(() => GpcCompositeSectionChecks.Dump(CompositeSelfTension.Calculate(definition.AnalysisSection(), n, strain)));
                bool Echo(string line) => line.StartsWith(".Analysis.Input.Materials.Rebar.Et=", StringComparison.Ordinal) || line.StartsWith(".Analysis.Input.Materials.Rebar.StrainU", StringComparison.Ordinal);
                string Kept(string dump) => string.Join("\n", dump.Split('\n').Where(l => !Echo(l)));
                Same($"self tension n={n:R} strain={strain:R}", Kept(former), Kept(gpc));
                if (former != gpc) EchoedRebarDifferences++;
            }
        }
        // The analysis steel of the definition: the steel material of the property with the rule of the legacy reader; Composite Shrinkage
        // Actions from the definition (n,sh, E and α of that steel, analysis section) = from the property.
        if (definition.AnalysisSteel == null) throw new Exception($"COMPOSITE ANALYSIS PROPERTY FAIL ({label}, {x.Name}): definition without the analysis steel");
        Same("analysis steel", GPC.Model.Core.ModelValues.Fingerprint(new object[] { FormerAnalysisSteel(x) }), GPC.Model.Core.ModelValues.Fingerprint(new object[] { definition.AnalysisSteel }));
        foreach (double strain in new[] { -2e-4, -3.5e-4, 1e-4, 0 })
            Same($"shrinkage strain={strain:R}", Try(() => Shrinkage(FormerShrinkage(x, strain))), Try(() => Shrinkage(CompositeShrinkageActions.Calculate(definition, strain))));
        return compared;
    }
}
#endif
