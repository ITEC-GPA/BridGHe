#if GPC_PILOT
using System;
using System.Collections.Generic;
using System.Linq;
using BridGHe.Common.Checks;
using BridGHe.Common.Geometry;
using BridGHe.Common.Gpc;
using BridGHe.Common.Gpc.Builders;
using BridGHe.Common.Gpc.Legacy;
using BridGHe.Common.Helpers;
using BridGHe.Common.Models;
using GPC.Checkers.CompositeBridge;
using Grasshopper.Kernel;
using BridgeActionBehavior = BridGHe.Common.Checks.BridgeActionBehavior;

/// <summary>
/// Chain E (d): Bridge State Override on GPC (<see cref="GpcStateOverride"/>: the drafts of the beams, the definitions of the composite
/// sections, the states classified from the analysed GPC beams) against the conversion of the FEM output (<see cref="BridgeStateOverride"/> on
/// the FEM values, then <see cref="GpcBeamDrafts.FromFemm(BeamModel, Guid?)"/> with the identity of the GPC draft): every part of the beam
/// (identity, ends, loads, attributes, physical data, axes, offsets, formulation, groups, property with its material and section) by its
/// fingerprint, the bridge data (member, super element, local section state with its D3 property fingerprint), the angle and the data of
/// the property (formulation, principal angle, native section): native, every Bridge State Override of 50 (<see cref="RequireComponents"/>),
/// TRANSITIONAL (R6.6 R2a: it reads the FEM outputs of the dual path; P2 decides). Not native, <c>gpc-state-override</c> (<see cref="Regression"/>):
/// R6.6 R2a, the override on GPC data alone with explicit expected values.
/// </summary>
internal static class GpcStateOverrideChecks
{
    private static string Fp(params object[] values) => GPC.Model.Core.ModelValues.Fingerprint(values);
    private static string B(double v) => BitConverter.DoubleToInt64Bits(v).ToString("X16");

    /// <summary>Areas measured (FEM property against its GPC conversion) and those whose GPC section area differs in some bit from the FEM
    /// area (the FEM area and the mass per length of <see cref="GpcSectionMass"/> must be equal bit for bit), with the largest relative
    /// difference per kind of GPC section.</summary>
    internal static int AreasMeasured, AreaDifferences;
    internal static readonly SortedDictionary<string, (int Count, double MaxRelative)> AreaDifferencesByKind = new(StringComparer.Ordinal);

    /// <summary>Rule of 0.4.0 (coordinator, 5/11): the self-weight stays bit for bit. The area of the FEM property equals
    /// <see cref="GpcSectionMass.LegacyArea"/> of its GPC section and the mass per length of the two paths is the same, bit for bit; the GPC
    /// section area is only measured (counted when it differs).</summary>
    internal static void MeasureArea(string label, BeamPropertyModel fem, GPC.Model.ElementProperties.BeamProperty gpcProperty)
    {
        if (fem?.Section == null || gpcProperty is not GPC.Model.ElementProperties.ElasticBeamProperty gpc) return;
        double f = fem.SectionArea, l = GpcSectionMass.LegacyArea(gpc.Section), g = gpc.Section.Area;
        if (B(f) != B(l)) throw new Exception($"GPC SECTION MASS FAIL ({label}, {fem.Name}, {gpc.Section.GetType().Name}): FEM area {f:R} / FEM-formula area of the GPC section {l:R}");
        if (B(fem.Material.Density * f) != B(GpcSectionMass.MassPerLength(gpc))) throw new Exception($"GPC SECTION MASS FAIL ({label}, {fem.Name}): mass per length differs");
        AreasMeasured++;
        if (B(f) != B(g))
        {
            AreaDifferences++; string kind = gpc.Section.GetType().Name;
            AreaDifferencesByKind.TryGetValue(kind, out var known);
            AreaDifferencesByKind[kind] = (known.Count + 1, Math.Max(known.MaxRelative, Math.Abs(g - f) / Math.Abs(f)));
        }
    }

    internal static string AreaSummary() => $"{AreasMeasured} beam property areas: FEM area = FEM formula on the GPC section and mass per length bit for bit in all; GPC section area different in {AreaDifferences}"
        + (AreaDifferencesByKind.Count == 0 ? "" : " (" + string.Join(", ", AreaDifferencesByKind.Select(p => $"{p.Key} {p.Value.Count}, max relative {p.Value.MaxRelative:G3}")) + ")");

    /// <summary>Every beam of the FEM models of the outputs of the solved <paramref name="doc"/> (GH_Model, its GPC input conversion, beams by
    /// number; shadowed properties and models with other units skipped), once per FEM property.</summary>
    internal static void MeasureModelAreas(GH_Document doc, string label)
    {
        var seen = new HashSet<BeamPropertyModel>(ReferenceEqualityComparer.Instance); int models = 0;
        foreach (var component in doc.Objects.OfType<GH_Component>())
            foreach (var output in component.Params.Output)
                foreach (var goo in output.VolatileData.AllData(true))
                {
                    if (goo == null || goo.GetType().Name != "GH_Model" || goo.GetType().GetProperty("Value").GetValue(goo) is not ModelModel fem || fem.Beams.Count == 0) continue;
                    GpcModels.Conversion conversion;
                    try { conversion = (GpcModels.Conversion)goo.GetType().GetProperty("GpcInput").GetValue(goo); } catch (System.Reflection.TargetInvocationException) { continue; }
                    if (conversion == null || !conversion.Report.Units.IsIdentity) continue;
                    var beams = fem.Beams.ToArray(); models++;
                    for (int k = 0; k < beams.Length; k++)
                    {
                        if (conversion.Report.ShadowedPropertyBeams.Contains(k + 1) || !seen.Add(beams[k].BeamProperty)) continue;
                        MeasureArea(label + " " + component.NickName, beams[k].BeamProperty, conversion.Model.BeamElements[k + 1].BeamProperty);
                    }
                }
        Console.WriteLine($"GPC SECTION MASS PASS ({label}): {models} models; " + AreaSummary());
    }

    /// <summary>The GPC draft against the conversion of the FEM beam with its identity: the number of parts compared (an exception with the
    /// first difference otherwise).</summary>
    internal static int Compare(string label, LegacyBeamDraft gpc, BeamModel fem)
    {
        var e = GpcBeamDrafts.FromFemm(fem, gpc.Beam.Guid); var a = gpc.Beam; var b = e.Beam; int parts = 0;
        void Same(string what, object x, object y)
        {
            string fx = Fp(x), fy = Fp(y);
            if (fx != fy) throw new Exception($"GPC STATE OVERRIDE FAIL ({label}, beam {a.Name}): {what} differs from the conversion of the FEM output");
            parts++;
        }
        void Text(string what, string x, string y) { if (x != y) throw new Exception($"GPC STATE OVERRIDE FAIL ({label}, beam {a.Name}): {what}: GPC '{x}' / FEM '{y}'"); parts++; }
        Same("identity", new object[] { a.Guid, a.Source, a.Name, a.Id }, new object[] { b.Guid, b.Source, b.Name, b.Id });
        Same("ends", new object[] { a.StartPoint, a.EndPoint }, new object[] { b.StartPoint, b.EndPoint });
        Same("loads", new object[] { a.Loads.ToArray(), a.Assignments.Loads.ToArray() }, new object[] { b.Loads.ToArray(), b.Assignments.Loads.ToArray() });
        Same("attributes", a.Attributes.Values.ToArray(), b.Attributes.Values.ToArray());
        Same("physical data", new object[] { a.Assignments.Physical }, new object[] { b.Assignments.Physical });
        Same("axes and offsets", new object[] { a.Assignments.SectionAxes, a.Assignments.OffsetAxes, a.Assignments.OffsetI, a.Assignments.OffsetJ, (int)a.Assignments.Formulation },
            new object[] { b.Assignments.SectionAxes, b.Assignments.OffsetAxes, b.Assignments.OffsetI, b.Assignments.OffsetJ, (int)b.Assignments.Formulation });
        Same("groups", a.Groups.Values.Select(g => new object[] { g.Guid, g.Name, g.Parent?.Guid }).ToArray(), b.Groups.Values.Select(g => new object[] { g.Guid, g.Name, g.Parent?.Guid }).ToArray());
        var pa = (GPC.Model.ElementProperties.ElasticBeamProperty)a.BeamProperty; var pb = (GPC.Model.ElementProperties.ElasticBeamProperty)b.BeamProperty;
        Text("density", B(pa.Material.Density), B(pb.Material.Density));
        Same("property", new object[] { pa }, new object[] { pb });
        string Bridge(BridgeBeamData d) => d == null ? "none" : string.Join("|", d.BeamGuid, d.Member == null ? "-" : Fp(d.Member), d.SuperElement ?? "-",
            d.SectionState == null ? "-" : $"{d.SectionState.Behavior} {d.SectionState.ConcreteExcluded} {B(d.SectionState.ModularRatio)} {d.SectionState.ClassificationCase ?? "-"} {d.SectionState.PropertyFingerprint}",
            d.CompositeSection == null ? "-" : "composite");
        Text("bridge data", Bridge(gpc.Bridge), Bridge(e.Bridge));
        Text("angle", B(gpc.AngleDeg), B(e.AngleDeg));
        Text("property data", $"{gpc.Property.Formulation} {B(gpc.Property.Angle)} {gpc.Property.Native == null}", $"{e.Property.Formulation} {B(e.Property.Angle)} {e.Property.Native == null}");
        return parts;
    }

    /// <summary>Regression (not native) <c>gpc-state-override</c>, R6.6 R2a: Bridge State Override on GPC data alone (no FEM value). The
    /// girders of the simple bridge of the design tests given by their GPC drafts (Bridge Segment Sections and Bridge Member Assignment on the
    /// GPC base property, <see cref="GpcBridgeGeometryBuilders.Assigned"/>), the moment states classified from an analysed GPC reference with
    /// synthetic results (BM2 at the ends and at mid span, by girder: positive, negative, mixed, zero within the tolerance), one composite
    /// section per segment built in GPC; both behaviours, the mixed and zero policies, with and without Preserve Mass. The parity with the
    /// conversion of the FEM outputs is retired with the FEM model; each output is checked against its own expected values: the girders and the
    /// other members (unchanged) in their numbers, the state classified from the synthetic moments, cracked as the policies say, the property
    /// the homogenised values of its segment at n∞ or n0 without the concrete when cracked, the section state recorded, the mass per length of
    /// the steel reference kept (7.85E-9 × 75040, the analytical area of the I) or the density of the definition, geometry, IDs and formulation.
    /// The errors of the override (shrinkage, mixed sign with policy 0, a missing state, a changed reference, composite sections without
    /// concrete) with the messages of the former override. Rule of 0.4.0, the self-weight: the mass per length is the density times the area
    /// of the former formula on the GPC section, bit for bit, and that area is the analytical area of the standard shapes.</summary>
    internal static void Regression()
    {
        var steel = R2aDesignFixtures.BaseProperty("Base", 7.85e-9);
        var geometry = SimpleBridgeBuilder.Build(SimpleBridgeTests.Input());
        var schedule = BridgeSegmentSections.CreateGpc(geometry, steel, null);
        var plans = BridgeMemberAssignment.Assign(geometry, null, schedule.Rules, new Dictionary<int, LegacyBeamProperty> { [-1] = steel }).Plans;
        var all = plans.Select(p => GpcBridgeGeometryBuilders.Assigned(p, R2aDesignFixtures.Id("state override:" + p.SolverId))).ToArray();
        var girders = all.Where(b => b.Bridge?.Member?.Family == GPC.Checkers.CompositeBridge.Layout.BridgeMemberFamily.Girder).ToArray();
        // The moments of girder i (BM2 at the normalised stations 0, 0.5, 1): positive, negative, mixed, zero within the tolerance of 1000.
        double Moment(int i, double s) => (i % 4) switch { 0 => 1e8, 1 => -1e8, 2 => (s - .5) * 2e8, _ => 500 };
        var expected = new[] { BeamMomentSign.Positive, BeamMomentSign.Negative, BeamMomentSign.Mixed, BeamMomentSign.Zero };
        // The analysed GPC reference: a copy of every beam (ends, angle, property) with its own nodes, the results of the girders.
        var test = new GpcTestModel("State override reference"); test.Case("Dead");
        var reference = new Dictionary<string, GPC.Model.Elements.BeamElement>(StringComparer.Ordinal);
        for (int k = 0; k < all.Length; k++)
        {
            var b = all[k].Beam; var property = GPC.Model.Core.ModelValues.CopyValue((GPC.Model.ElementProperties.ElasticBeamProperty)b.BeamProperty);
            reference[b.Name] = test.Beam(b.Name, test.Node("a" + k, b.StartPoint.X, b.StartPoint.Y, b.StartPoint.Z), test.Node("b" + k, b.EndPoint.X, b.EndPoint.Y, b.EndPoint.Z), all[k].AngleDeg, property);
        }
        for (int i = 0; i < girders.Length; i++)
        {
            var a = reference[girders[i].Beam.Name];
            foreach (double s in new[] { 0d, .5, 1d }) test.Forces(a, "Dead", s * a.Length, 0, bm1: -1e8, bm2: Moment(i, s));
        }
        var analysed = test.Analyse();
        var catalog = GpcResultCatalog.Of(analysed) ?? throw new Exception("GPC STATE OVERRIDE FAIL: no results catalog");
        var states = BeamMomentClassification.Classify(girders.Select(g => g.Beam.Name).ToArray(), new BeamModel[girders.Length],
            girders.Select(g => (catalog, analysed, reference[g.Beam.Name])).ToArray(), "Dead");
        if (states.Any(s => s.Analysed.Beam == null) || states.Where((s, i) => s.Sign != expected[i % 4] || s.Source != null).Any())
            throw new Exception("GPC STATE OVERRIDE FAIL: the states are not those of the synthetic moments, with their analysed GPC beams and no FEM source");
        var ids = schedule.Rules.Select(r => $"G{r.Group}-C{r.Segment}").ToArray();
        // One composite section per segment: the grillage section on the I of the base (2500, flanges 600 × 30, web 16), slab 3000 × 250 with
        // the top row 1600 at cover 40, Consider Rebar (the former fixture: Composite with the base section and SectionConcreteSlabModel(250, 3000, 1600, 40)).
        GpcCompositeDefinitions.Numbers Numbers(string id) => GpcCompositeDefinitions.Grillage(new BridgeGrillageExample.Parameters()) with
        {
            Name = "CS " + id, Height = 2500, Web = 16, TopWidth = 600, TopThickness = 30, BottomWidth = 600, BottomThickness = 30,
            SlabWidth = 3000, SlabHeight = 250, TopArea = 1600, TopCover = 40, ConsiderRebar = true,
        };
        var definitions = ids.Select(id => GpcCompositeDefinitions.Create(Numbers(id))).ToArray();
        var definitionOf = ids.Select((id, i) => (id, definitions[i])).ToDictionary(p => p.id, p => p.Item2, StringComparer.Ordinal);
        double baseArea = 600 * 30 + 600 * 30 + 16 * (2500 - 30 - 30), baseMass = 7.85e-9 * baseArea;
        int checks = 0, variants = 0, errors = 0;
        var girderNames = girders.Select(g => g.Beam.Name).ToHashSet(StringComparer.Ordinal); var others = all.Where(b => !girderNames.Contains(b.Beam.Name)).ToArray();
        void Require(bool ok, string label, string what) { if (!ok) throw new Exception($"GPC STATE OVERRIDE FAIL ({label}): {what}"); checks++; }
        foreach (var behavior in new[] { BridgeActionBehavior.LongTerm, BridgeActionBehavior.ShortTerm })
            foreach (int mixed in new[] { 1, 2 })
                foreach (int zero in new[] { 0, 1 })
                    foreach (bool mass in new[] { true, false })
                    {
                        string label = $"{behavior} mixed {mixed} zero {zero} mass {mass}";
                        var result = GpcStateOverride.Apply(all, ids, definitions, states, behavior, mixed, zero, mass);
                        Require(result.Girders.Length == girders.Length && result.OtherMembers.Length == all.Length - girders.Length, label, "girders and other members retained");
                        Require(result.OtherMembers.Select((o, i) => o.Beam.Name == others[i].Beam.Name && Fp(o.Beam.BeamProperty) == Fp(others[i].Beam.BeamProperty)
                            && o.Bridge?.SectionState == null).All(v => v), label, "other members unchanged, in order");
                        foreach (var g in result.Girders)
                        {
                            int i = Array.FindIndex(girders, x => x.Beam.Name == g.Beam.Name); var input = girders[i]; var sign = expected[i % 4];
                            bool cracked = sign == BeamMomentSign.Negative || sign == BeamMomentSign.Mixed && mixed == 2 || sign == BeamMomentSign.Zero && zero == 1;
                            var meta = input.Bridge.Member; var definition = definitionOf[$"G{meta.Group}-C{meta.Segment}"];
                            double n = behavior == BridgeActionBehavior.LongTerm ? definition.NLongTerm : definition.NShortTerm;
                            var values = GpcCompositeSections.Homogenised(definition, n, !cracked);
                            var p = (GPC.Model.ElementProperties.ElasticBeamProperty)g.Beam.BeamProperty; var s = g.Property.Section; var state = g.Bridge?.SectionState;
                            Require(i >= 0 && g.Beam.StartPoint.Equals(input.Beam.StartPoint) && g.Beam.EndPoint.Equals(input.Beam.EndPoint) && g.AngleDeg == input.AngleDeg
                                && g.Beam.Assignments.Formulation == GPC.Model.Structure.Assignments.BeamFormulation.StraightTwoNode, label, $"beam {g.Beam.Name}: geometry, ID, angle and formulation");
                            Require(B(p.Section.Area) == B(values.Area) && s.CompositePreview && B(s.Area) == B(values.Area) && B(s.I11) == B(values.I11) && B(s.I22) == B(values.I22) && B(s.J) == B(values.J),
                                label, $"beam {g.Beam.Name}: the homogenised values of its segment at n = {n} ({(cracked ? "cracked" : "uncracked")})");
                            Require(state != null && state.Behavior.ToString() == behavior.ToString() && state.ConcreteExcluded == cracked && state.ModularRatio == n && state.ClassificationCase == "Dead"
                                && g.Records.SectionStates == 1, label, $"beam {g.Beam.Name}: the section state");
                            Require(mass ? Math.Abs(p.Material.Density * values.Area / baseMass - 1) < 1e-12 : p.Material.Density == definition.AnalysisSteel.Density,
                                label, $"beam {g.Beam.Name}: " + (mass ? "the mass per length of the steel reference kept" : "the density of the definition kept"));
                        }
                        Require(girders.All(b => b.Bridge.SectionState == null) && all.All(b => B(GpcSectionMass.LegacyArea(((GPC.Model.ElementProperties.ElasticBeamProperty)b.Beam.BeamProperty).Section)) == B(baseArea)),
                            label, "the inputs unchanged, the steel reference on the analytical area of the I");
                        variants++;
                    }
        void Error(string label, IReadOnlyList<LegacyBeamDraft> drafts, IReadOnlyList<BridgeCompositeSectionDefinition> defs, IReadOnlyList<BeamMomentState> s,
            BridgeActionBehavior behavior, int mixed, Func<string, bool> message)
        {
            string text = null;
            try { GpcStateOverride.Apply(drafts, ids, defs, s, behavior, mixed, 0, true); } catch (ArgumentException x) { text = x.Message; }
            if (text == null || !message(text)) throw new Exception($"GPC STATE OVERRIDE FAIL ({label}): {(text == null ? "accepted" : "message '" + text + "'")}");
            errors++;
        }
        Error("shrinkage", all, definitions, states, BridgeActionBehavior.Shrinkage, 2, m => m == "Only mechanical n infinity/n0 states are supported; shrinkage remains a separate uncracked model.");
        Error("mixed sign, policy 0", all, definitions, states, BridgeActionBehavior.LongTerm, 0, m => m == $"Beam {girders[2].Beam.Name}: Mixed. Obtain FEM results or explicitly choose a mixed-beam policy/refine the common mesh.");
        Error("missing state", all, definitions, states.Skip(1).ToArray(), BridgeActionBehavior.LongTerm, 2, m => m == "One moment state is required for each girder FEM ID, without additional beams.");
        var changed = all.Select(b => { if (!ReferenceEquals(b, girders[0])) return b; var c = GpcDrafts.Copy(b); ((GPC.Model.ElementProperties.ElasticBeamProperty)c.Beam.BeamProperty).Material.ElasticModulusTension = 1; return c; }).ToArray();
        Error("changed reference", changed, definitions, states, BridgeActionBehavior.LongTerm, 2, m => m == $"Beam {girders[0].Beam.Name}: reference geometry or steel stiffness changed. Recalculate model 0.");
        var noConcrete = ids.Select(id => GpcCompositeDefinitions.Create(Numbers(id) with { ConsiderConcrete = false })).ToArray();
        Error("concrete not considered", all, noConcrete, states, BridgeActionBehavior.LongTerm, 2, m => m == "Supply full composite definitions with concrete and reinforcement enabled.");
        // Rule of 0.4.0, the self-weight: the mass per length is the density times the area of the former formula on the GPC section, bit for
        // bit, and that area is the analytical area of the shape (the I of the simple bridge and the five standard shapes the reader maps with
        // several dimensions); the GPC section area is measured against it (counted when it differs in some bit).
        int shapes = 0, sectionDifferences = 0; double maxRelative = 0;
        void Shape(GPC.Model.ElementProperties.ElasticBeamProperty p, double area, double tolerance, string label)
        {
            double l = GpcSectionMass.LegacyArea(p.Section), g = p.Section.Area;
            if (Math.Abs(l - area) > tolerance * area) throw new Exception($"GPC SECTION MASS FAIL ({label}): former-formula area {l:R} of the GPC section / analytical area {area:R}");
            if (B(GpcSectionMass.MassPerLength(p)) != B(p.Material.Density * l)) throw new Exception($"GPC SECTION MASS FAIL ({label}): mass per length differs from density x area");
            shapes++; if (B(l) != B(g)) { sectionDifferences++; maxRelative = Math.Max(maxRelative, Math.Abs(g - l) / Math.Abs(l)); }
        }
        foreach (var b in all) Shape((GPC.Model.ElementProperties.ElasticBeamProperty)b.Beam.BeamProperty, baseArea, 0, "simple bridge " + b.Beam.Name);
        GPC.Model.ElementProperties.ElasticBeamProperty Of(LegacySectionRecipe r) => (GPC.Model.ElementProperties.ElasticBeamProperty)GpcPropertyBuilders.Beam("Shape " + r.Kind, 5, r, R2aDesignFixtures.Steel(7.85e-9), "Steel").Property;
        foreach (var (b1, b2, d, t1, t2, t3) in new[] { (600d, 600d, 2500d, 30d, 30d, 16d), (800d, 600d, 1800d, 40d, 30d, 15d), (450d, 900d, 2200d, 45d, 35d, 17d), (300.5, 250.25, 1234.5, 20.3, 18.7, 11.1) })
            Shape(Of(GpcSectionBuilders.Standard(BridgeSectionKind.I, d, b1: b1, b2: b2, t1: t1, t2: t2, t3: t3)), b1 * t1 + b2 * t2 + (d - t1 - t2) * t3, 1e-15, "I");
        foreach (var (d, b) in new[] { (500d, 300d), (1234.5, 678.9), (0.3, 0.2) }) Shape(Of(GpcSectionBuilders.Standard(BridgeSectionKind.SolidRectangle, d, b)), d * b, 1e-15, "solid rectangle");
        foreach (var (d, b, t1, t2) in new[] { (500d, 300d, 20d, 15d), (1234.5, 678.9, 12.3, 9.8) })
            Shape(Of(GpcSectionBuilders.Standard(BridgeSectionKind.HollowRectangle, d, b, t1: t1, t2: t2)), d * b - (b - 2 * t2) * (d - 2 * t1), 1e-14, "hollow rectangle");
        foreach (double d in new[] { 500d, 1234.5, 0.3 }) Shape(Of(GpcSectionBuilders.Standard(BridgeSectionKind.SolidCircle, d)), Math.PI * d * d / 4, 1e-15, "solid circle");
        foreach (var (d, t) in new[] { (500d, 20d), (1234.5, 12.3) }) Shape(Of(GpcSectionBuilders.Standard(BridgeSectionKind.HollowCircle, d, t: t)), Math.PI * (d * d - (d - 2 * t) * (d - 2 * t)) / 4, 1e-14, "hollow circle");
        // TRANSITIONAL (R6.6 R2a, P2 decides): the beams of the examples 01-10, FEM area = former formula on the GPC section of the conversion of
        // the FEM catalog (NativeExamples.Create, kept by R0 for the callers outside R0): their sections have no analytical oracle here.
        foreach (var example in NativeExamples.Create())
        {
            if (example.Model.Units != ModelModel.ModelUnits.Nmm || example.Model.Beams.Count == 0) continue;
            var conversion = GpcModels.Convert(example.Model, 1e-6); var femBeams2 = example.Model.Beams.ToArray();
            for (int k = 0; k < femBeams2.Length; k++)
                if (!conversion.Report.ShadowedPropertyBeams.Contains(k + 1)) MeasureArea(example.Id, femBeams2[k].BeamProperty, conversion.Model.BeamElements[k + 1].BeamProperty);
        }
        Console.WriteLine($"GPC STATE OVERRIDE PASS: {variants} overrides of {girders.Length} girders and {all.Length - girders.Length} other members on GPC data alone ({checks} checks: " +
            $"states, cracking, homogenised values, section states, masses, geometry); {errors} errors with the messages of the override; {shapes} beam property areas = analytical areas, " +
            $"mass per length = density x area bit for bit, GPC section area different in {sectionDifferences} (max relative {maxRelative:G3}); TRANSITIONAL examples 01-10: " + AreaSummary() + ".");
    }

    /// <summary>Native (50): every Bridge State Override of the solved definition attaches to its outputs the GPC drafts of the override on GPC,
    /// each equal to the conversion of its FEM output.</summary>
    internal static void RequireComponents(GH_Document doc, string label)
    {
        int components = 0, beams = 0, parts = 0;
        foreach (var c in doc.Objects.OfType<GH_Component>().Where(o => o.GetType().Name == "BridgeStateOverrideComponent"))
        {
            var goos = c.Params.Output[0].VolatileData.AllData(true).Concat(c.Params.Output[1].VolatileData.AllData(true)).ToArray();
            if (goos.Length == 0) continue;
            // The mass per length of the inputs: the FEM area = the FEM formula on the GPC section of their drafts (rule of 0.4.0).
            foreach (var input in c.Params.Input[0].VolatileData.AllData(true))
                MeasureArea($"{label} {c.NickName} input", ((BeamModel)((Grasshopper.Kernel.Types.IGH_Goo)input).ScriptVariable()).BeamProperty,
                    ((LegacyBeamDraft)input.GetType().GetProperty("Gpc").GetValue(input)).Beam.BeamProperty);
            foreach (var goo in goos)
            {
                if (!(bool)goo.GetType().GetProperty("HasAttachedGpc").GetValue(goo)) throw new Exception($"GPC STATE OVERRIDE FAIL ({label} {c.NickName}): an output without its GPC draft");
                var draft = (LegacyBeamDraft)goo.GetType().GetProperty("Gpc").GetValue(goo);
                parts += Compare($"{label} {c.NickName}", draft, (BeamModel)((Grasshopper.Kernel.Types.IGH_Goo)goo).ScriptVariable()); beams++;
            }
            components++;
        }
        if (components == 0) throw new Exception($"GPC STATE OVERRIDE FAIL ({label}): no Bridge State Override with data");
        Console.WriteLine($"GPC STATE OVERRIDE PASS ({label}): {components} components, {beams} output beams on GPC equal to the conversion of their FEM outputs ({parts} parts); " + AreaSummary() + ".");
    }
}
#endif
