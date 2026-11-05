#if GPC_PILOT
using System;
using System.Collections.Generic;
using System.Linq;
using BridGHe.Common.Checks;
using BridGHe.Common.Geometry;
using BridGHe.Common.Gpc;
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
/// the property (formulation, principal angle, native section). Not native: <c>gpc-state-override</c>; native: every Bridge State Override
/// of 50 (<see cref="RequireComponents"/>).
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

    /// <summary>The FEM and GPC overrides of the same inputs: the same girders and other members in the same order, each GPC draft equal to the
    /// conversion of its FEM output, or the same kind of error on both paths. The number of parts compared (-1: the same error).</summary>
    private static int Both(string label, IReadOnlyList<BeamModel> fem, IReadOnlyList<LegacyBeamDraft> drafts, string[] ids, CompositeBeamPropertyModel[] properties,
        BridgeCompositeSectionDefinition[] definitions, IReadOnlyList<BeamMomentState> states, BridgeActionBehavior behavior, int mixed, int zero, bool mass)
    {
        BridgeSegmentOverrideResult f = null; GpcStateOverrideResult g = null; Exception fe = null, ge = null;
        try { f = BridgeStateOverride.Apply(fem, ids, properties, states, behavior, mixed, zero, mass); } catch (ArgumentException ex) { fe = ex; }
        try { g = GpcStateOverride.Apply(drafts, ids, definitions, states, behavior, mixed, zero, mass); } catch (ArgumentException ex) { ge = ex; }
        if (fe != null || ge != null)
        {
            if (fe == null || ge == null || fe.Message != ge.Message) throw new Exception($"GPC STATE OVERRIDE FAIL ({label}): FEM '{fe?.Message ?? "no error"}' / GPC '{ge?.Message ?? "no error"}'");
            return -1;
        }
        if (f.Girders.Length != g.Girders.Length || f.OtherMembers.Length != g.OtherMembers.Length) throw new Exception($"GPC STATE OVERRIDE FAIL ({label}): counts differ");
        int parts = 0;
        for (int i = 0; i < f.Girders.Length; i++) parts += Compare(label, g.Girders[i], f.Girders[i]);
        for (int i = 0; i < f.OtherMembers.Length; i++) parts += Compare(label, g.OtherMembers[i], f.OtherMembers[i]);
        return parts;
    }

    /// <summary>Regression (not native) <c>gpc-state-override</c>: the girders of the simple bridge of the design tests on the steel reference,
    /// moment states classified from the GPC conversion of the reference with synthetic results (all signs), one composite section per
    /// segment; both behaviours, the mixed and zero policies, with and without Preserve Mass; the errors of the FEM override (shrinkage, mixed
    /// sign with policy 0, a missing state, a changed reference, composite sections without concrete) on both paths.</summary>
    internal static void Regression()
    {
        var steel = BridgeSegmentSectionsTests.BaseProperty(); steel.Material.Density = 7.85e-9;
        var geometry = SimpleBridgeBuilder.Build(SimpleBridgeTests.Input());
        var schedule = BridgeSegmentSections.Create(geometry, steel, null);
        var all = BridgeMemberAssignment.Assign(geometry, new Dictionary<int, BeamPropertyModel> { [-1] = steel }, schedule.Rules).Beams;
        var lc = new LoadCaseModel("Dead", "Dead");
        var girders = all.Where(b => b.Attributes.OfType<BridgeMemberAttributeModel>().Single().Value.Family == BridgeMemberFamily.Girder).ToArray();
        for (int i = 0; i < girders.Length; i++) foreach (double s in new[] { 0d, .5, 1d })
            girders[i].Results.Add(new BeamForceResultModel(lc, s, 0, "", true) { My = (i % 4) switch { 0 => 1e8, 1 => -1e8, 2 => (s - .5) * 2e8, _ => 500 }, Mx = -1e8 });
        // The reference as a FEM model with nodes, converted to GPC with its results (the analysed GPC beams of the states).
        var reference = new ModelModel(ModelModel.ModelUnits.Nmm) { Name = "State override reference" };
        reference.AddElements(all); reference.AddElements(geometry.Nodes.Select((p, i) => new NodeModel((i + 1).ToString(), p)).ToArray()); reference.LoadCases.Add(lc);
        var analysed = GpcSyntheticResults.Analysed(reference);
        var catalog = GpcResultCatalog.Of(analysed) ?? throw new Exception("GPC STATE OVERRIDE FAIL: no results catalog");
        var femBeams = reference.Beams.ToList();
        var states = BeamMomentClassification.Classify(girders, girders.Select(b => (catalog, analysed, analysed.BeamElements[femBeams.IndexOf(b) + 1])).ToArray(), "Dead");
        if (states.Any(s => s.Analysed.Beam == null)) throw new Exception("GPC STATE OVERRIDE FAIL: states without their analysed GPC beam");
        var ids = schedule.Rules.Select(r => $"G{r.Group}-C{r.Segment}").ToArray();
        var properties = ids.Select(id => { var p = BridgeGrillageExample.Composite(new()); p.Name = "CS " + id; p.SteelSection = ModelArchive.Clone(steel.Section); p.ConsiderRebar = true; p.ConcreteSection = new(250, 3000, 1600, 40); return p; }).ToArray();
        var definitions = properties.Select(p => GpcCompositeSections.Definition(p, p.Name)).ToArray();
        var drafts = all.Select(b => GpcBeamDrafts.FromFemm(b)).ToArray();
        int compared = 0, variants = 0, errors = 0;
        foreach (var behavior in new[] { BridgeActionBehavior.LongTerm, BridgeActionBehavior.ShortTerm })
            foreach (int mixed in new[] { 1, 2 })
                foreach (int zero in new[] { 0, 1 })
                    foreach (bool mass in new[] { true, false })
                    {
                        int parts = Both($"{behavior} mixed {mixed} zero {zero} mass {mass}", all, drafts, ids, properties, definitions, states, behavior, mixed, zero, mass);
                        if (parts < 0) throw new Exception($"GPC STATE OVERRIDE FAIL: valid inputs rejected ({behavior} {mixed} {zero} {mass})");
                        compared += parts; variants++;
                    }
        int Error(string label, IReadOnlyList<BeamModel> fem, IReadOnlyList<LegacyBeamDraft> d, CompositeBeamPropertyModel[] p, BridgeCompositeSectionDefinition[] defs,
            IReadOnlyList<BeamMomentState> s, BridgeActionBehavior behavior, int mixed)
        {
            if (Both(label, fem, d, ids, p, defs, s, behavior, mixed, 0, true) != -1) throw new Exception($"GPC STATE OVERRIDE FAIL ({label}): accepted on both paths");
            return 1;
        }
        errors += Error("shrinkage", all, drafts, properties, definitions, states, BridgeActionBehavior.Shrinkage, 2);
        errors += Error("mixed sign, policy 0", all, drafts, properties, definitions, states, BridgeActionBehavior.LongTerm, 0);
        errors += Error("missing state", all, drafts, properties, definitions, states.Skip(1).ToArray(), BridgeActionBehavior.LongTerm, 2);
        var changed = all.Select(b => { var c = ModelArchive.Clone(b); if (ReferenceEquals(b, girders[0])) c.BeamProperty.Material.Modulus = 1; return c; }).ToArray();
        errors += Error("changed reference", changed, changed.Select(b => GpcBeamDrafts.FromFemm(b)).ToArray(), properties, definitions, states, BridgeActionBehavior.LongTerm, 2);
        var noConcrete = properties.Select(p => { var c = ModelArchive.Clone(p); c.ConsiderConcrete = false; return c; }).ToArray();
        errors += Error("concrete not considered", all, drafts, noConcrete, noConcrete.Select(p => GpcCompositeSections.Definition(p, p.Name)).ToArray(), states, BridgeActionBehavior.LongTerm, 2);
        // Rule of 0.4.0, the self-weight bit for bit: the area of every FEM property (the drafts of the reference, the five standard shapes
        // the reader maps with several dimensions, every beam of the examples 01-10) = the FEM formula on its GPC section.
        for (int i = 0; i < all.Length; i++) MeasureArea("simple bridge", all[i].BeamProperty, drafts[i].Beam.BeamProperty);
        var shapes = new List<SectionModel>();
        foreach (var (b1, b2, d, t1, t2, t3) in new[] { (600d, 600d, 2500d, 30d, 30d, 16d), (800d, 600d, 1800d, 40d, 30d, 15d), (450d, 900d, 2200d, 45d, 35d, 17d), (300.5, 250.25, 1234.5, 20.3, 18.7, 11.1) })
            shapes.Add(new SectionModel { SectionType = SectionModel.SectionTypes.I, B1 = b1, B2 = b2, D = d, T1 = t1, T2 = t2, T3 = t3 });
        foreach (var (d, b) in new[] { (500d, 300d), (1234.5, 678.9), (0.3, 0.2) }) shapes.Add(new SectionModel { SectionType = SectionModel.SectionTypes.SolidRectangle, D = d, B = b });
        foreach (var (d, b, t1, t2) in new[] { (500d, 300d, 20d, 15d), (1234.5, 678.9, 12.3, 9.8) }) shapes.Add(new SectionModel { SectionType = SectionModel.SectionTypes.HollowRectangle, D = d, B = b, T1 = t1, T2 = t2 });
        foreach (double d in new[] { 500d, 1234.5, 0.3 }) shapes.Add(new SectionModel { SectionType = SectionModel.SectionTypes.SolidCircle, D = d });
        foreach (var (d, t) in new[] { (500d, 20d), (1234.5, 12.3) }) shapes.Add(new SectionModel { SectionType = SectionModel.SectionTypes.HollowCircle, D = d, T = t });
        foreach (var s in shapes)
        {
            s.CalculateSection();
            var p = ModelArchive.Clone(steel); p.Name = "Shape " + s.SectionType; p.Section = s;
            MeasureArea("shapes", p, GpcModels.BeamProperty(p, GpcBeamDrafts.DraftUnits).Property);
        }
        foreach (var example in NativeExamples.Create())
        {
            if (example.Model.Units != ModelModel.ModelUnits.Nmm || example.Model.Beams.Count == 0) continue;
            var conversion = GpcModels.Convert(example.Model, 1e-6); var femBeams2 = example.Model.Beams.ToArray();
            for (int k = 0; k < femBeams2.Length; k++)
                if (!conversion.Report.ShadowedPropertyBeams.Contains(k + 1)) MeasureArea(example.Id, femBeams2[k].BeamProperty, conversion.Model.BeamElements[k + 1].BeamProperty);
        }
        Console.WriteLine($"GPC STATE OVERRIDE PASS: {variants} overrides of {girders.Length} girders and {all.Length - girders.Length} other members equal to the conversion of the FEM outputs ({compared} parts); {errors} errors equal on both paths; " + AreaSummary() + ".");
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
