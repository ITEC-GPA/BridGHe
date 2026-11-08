#if GPC_PILOT
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using BridGHe.Common.Models;
using GH_IO.Serialization;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;

/// <summary>
/// R6.5 P1 (coordinator, 7/11): the components of a lane replayed with their inputs given by the GPC data alone. In every definition of the
/// Build Model scope (the example scan, solved without analyses), each instance of a component of the lane is copied into an empty
/// document twice: once with the inputs it had (the FEM-backed GH types of today), once with the same inputs where every GH type of the
/// converted kinds is replaced by the type made from its GPC datum alone (<c>FromGpc</c>, no FEM value). The two copies must give the same
/// outputs: the same trees, the same messages, every GH type with the same GPC datum (the text of the datum, exact), every other value the same
/// text; the outputs of the GPC-only copy built from GPC-only inputs have no FEM value. Regression <c>gpc-only-&lt;lane&gt;</c> (not native).
/// </summary>
internal static class GpcOnlyReplay
{
    /// <summary>Lane 1, elements: the consumers and producers of nodes, beams and plates (attributes, loads, filters and tools).</summary>
    internal static readonly string[] Lane1Types =
    {
        // Attributes
        "BeamOutputStationsComponent", "BeamReleasesComponent", "BeamStringGroupComponent", "BeamSupportComponent", "BeamTaperComponent",
        "ElementIdComponent", "LoadPatchTypeComponent", "NodeRestraintsComponent", "NodeStiffnessComponent", "PlateSupportComponent", "SuperElementComponent",
        // Loads
        "BeamDistributedLoadComponent", "BeamDistributedMomentComponent", "BeamGradientLoadComponent", "BeamPointLoadComponent", "BeamPointMomentComponent",
        "BeamPreLoadComponent", "NodeDisplacementLoadComponent", "NodeLoadComponent", "NodeMomentComponent", "NodeTemperatureComponent",
        "PlateNSMassComponent", "PlatePreStressComponent", "PlatePressureComponent",
        // Tools
        "AlignBeamAxesComponent", "AlignPlateAxesComponent", "BeamFilterComponent", "BeamOverrideComponent", "ElementFilterComponent",
        "MergeGeometryComponent", "NodeFilterComponent", "PlateFilterComponent",
    };

    /// <summary>Lane 1b, bridge generators (passo 73): the components of <c>Components/Geometry</c> that build or take nodes, beams and plates.
    /// The generators without an input element (bearings, links, connection beams, deck mesh, slab grillage, member assignment) are replayed
    /// with <c>GpcOnlyElements.GeneratorsWithoutFem</c> on in the GPC-only copy: they give their elements by their drafts alone (the path of
    /// P2). The others (Beam Moment Dispatch, Beam Offsets, Segment Override, Connections in mode 2) take beams given by their drafts alone.</summary>
    internal static readonly string[] Lane1bTypes =
    {
        "BeamMomentDispatchComponent", "BridgeBeamOffsetsComponent", "BridgeBearingsComponent", "BridgeConnectionBeamsComponent", "BridgeConnectionsComponent",
        "BridgeDeckMeshComponent", "BridgeFemMembersComponent", "BridgeMemberAssignmentComponent", "BridgeRigidLinksComponent", "BridgeSegmentOverrideComponent",
        "BridgeSlabGrillageComponent",
    };

    /// <summary>The GH element types converted to their GPC data alone in lane 1.</summary>
    internal static readonly string[] ElementGoo = { "GH_Node", "GH_Beam", "GH_Plate" };

    /// <summary>Lane 2, properties and data (passo 75, P2Y): the producers of sections, properties, materials, cases, combinations, stages
    /// and groups (<see cref="Lane2Producers"/>, every instance with data, replayed with <c>GpcOnlyElements.PropertiesWithoutFem</c> on in
    /// the GPC-only copy: they give their data by the GPC datum alone, the path of P2), and the consumers of those kinds in the files of
    /// lanes 1a and 1b (their property, material, case and group inputs only: the element inputs stay FEM-backed). Lane 3 (Build Model,
    /// analyses, checks, Construction Sequence, Composite Shrinkage Actions, Moving) is not here.
    /// <para>P2Z (coordinator, 8/11): lane 2 in two sub-lanes, 2a (<see cref="Lane2aTypes"/>: definitions, materials, sections, beam and
    /// plate properties and their consumers) and 2b (<see cref="Lane2bTypes"/>: composite and bridge properties, State Override). L2B (8/11):
    /// the regression <c>gpc-only-properties</c> replays both (no type declared pending), with the composite properties, the coordinate
    /// systems of the loads and the bridge rules among the converted kinds (<see cref="PropertyGoo"/>).</para></summary>
    internal static readonly string[] Lane2Producers =
    {
        // Components/Definitions
        "GroupComponent", "LoadCaseComponent", "LoadCombinationComponent", "StagedConstructionComponent",
        // Components/Materials
        "MaterialAluminiumComponent", "MaterialConcreteComponent", "MaterialCustomComponent", "MaterialRebarComponent", "MaterialSteelComponent", "MaterialTableComponent",
        // Components/Sections
        "BeamSectionAngleComponent", "BeamSectionBrepComponent", "BeamSectionCComponent", "BeamSectionGenericComponent", "BeamSectionHollowCircleComponent",
        "BeamSectionHollowRectComponent", "BeamSectionIComponent", "BeamSectionSolidCircleComponent", "BeamSectionSolidRectComponent", "BeamSectionTComponent",
        "SectionGeometricPropertyComponent", "StrausBxsSectionComponent", "StrausLibrarySectionComponent",
        "StrausCircularSolidSectionComponent", "StrausCircularHollowSectionComponent", "StrausRectangleSolidSectionComponent", "StrausRectangleHollowSectionComponent",
        "StrausLipChannelSectionComponent", "StrausTopHatSectionComponent", "StrausISectionComponent", "StrausTSectionComponent", "StrausAngleSectionComponent",
        "StrausZSectionComponent", "StrausTrapezoidSolidSectionComponent", "StrausTrapezoidHollowSectionComponent", "StrausTriangleSolidSectionComponent",
        "StrausTriangleHollowSectionComponent", "StrausCruciformSectionComponent", "StrausBglRectangleSectionComponent", "StrausBglISectionComponent",
        "StrausBglChannelSectionComponent", "StrausBglTSectionComponent", "StrausBglAngleSectionComponent", "StrausBglBulbSectionComponent",
        // Components/ElementProperties
        "BeamGeometricPropertyComponent", "BeamPropertyComponent", "PlatePropertyComponent",
        // Components/Tools (properties)
        "BeamPropertyOverrideComponent", "GetBeamPropertyComponent",
        // Components/BridgeChecks (property components)
        "BeamPropertyCompositeComponent", "BeamSectionConcreteSlabComponent", "BeamSectionHPropertyComponent", "CompositeSectionGeometricPropertyComponent",
        "CreepAndShrinkageConcreteComponent",
        // Components/Geometry (sections, workbooks, composite sections, state overrides)
        "BridgeSegmentSectionsComponent", "BridgeSegmentExcelComponent", "BridgeSectionTemplateComponent", "BridgeSectionReadComponent",
        "BridgeSegmentCompositeComponent", "BridgeStateOverrideComponent",
        // L2B (coordinator, 8/11): the producers of the bridge rules of Bridge FEM Members.
        "BridgeFamilyPropertyComponent", "BridgeSectionScheduleComponent",
    };

    /// <summary>Lane 2: the consumers of the lane-2 kinds in the files of lanes 1a and 1b (only their property, material, case and group
    /// inputs are given by the GPC datum alone in the replay).</summary>
    internal static readonly string[] Lane2Consumers =
    {
        "BeamComponent", "PlateComponent", "BeamOverrideComponent", "BeamFilterComponent", "NodeFilterComponent", "PlateFilterComponent",
        "BeamDistributedLoadComponent", "BeamDistributedMomentComponent", "BeamGradientLoadComponent", "BeamPointLoadComponent", "BeamPointMomentComponent",
        "BeamPreLoadComponent", "NodeDisplacementLoadComponent", "NodeLoadComponent", "NodeMomentComponent", "NodeTemperatureComponent",
        "PlateNSMassComponent", "PlatePreStressComponent", "PlatePressureComponent",
        "BridgeConnectionBeamsComponent", "BridgeConnectionsComponent", "BridgeDeckMeshComponent", "BridgeSlabGrillageComponent",
        "BridgeMemberAssignmentComponent", "BridgeFemMembersComponent", "BridgeSegmentOverrideComponent",
    };

    internal static readonly string[] Lane2Types = Lane2Producers.Concat(Lane2Consumers).ToArray();

    /// <summary>P2Z: lane 2b: the composite and bridge property components and State Override (coordinator, 8/11), Get Beam Property and
    /// Beam Property Override (they read and change bridge properties), and Bridge FEM Members with the producers of its Bridge Design data
    /// (Bridge Family Property, Bridge Section Schedule). L2B (8/11): replayed by <c>gpc-only-properties</c> with lane 2a.</summary>
    internal static readonly string[] Lane2bTypes =
    {
        "BeamPropertyOverrideComponent", "GetBeamPropertyComponent",
        "BeamPropertyCompositeComponent", "BeamSectionConcreteSlabComponent", "BeamSectionHPropertyComponent", "CompositeSectionGeometricPropertyComponent",
        "CreepAndShrinkageConcreteComponent",
        "BridgeSegmentSectionsComponent", "BridgeSegmentExcelComponent", "BridgeSectionTemplateComponent", "BridgeSectionReadComponent",
        "BridgeSegmentCompositeComponent", "BridgeStateOverrideComponent",
        "BridgeFemMembersComponent", "BridgeFamilyPropertyComponent", "BridgeSectionScheduleComponent",
    };

    /// <summary>P2Z: lane 2a, the types of <c>gpc-only-properties</c>: lane 2 without <see cref="Lane2bTypes"/>.</summary>
    internal static readonly string[] Lane2aTypes = Lane2Types.Where(t => !Lane2bTypes.Contains(t)).ToArray();

    /// <summary>The GH types of properties and data converted to their GPC data alone in lane 2. L2B (lane 2b): also the coordinate system of
    /// the loads (coordinator, 8/11) and the bridge rules (<c>BridgeDesignGoo</c>: a section rule or a family property whose beam property
    /// is given by its GPC datum alone, <see cref="DesignGpcOnly"/>).</summary>
    internal static readonly string[] PropertyGoo =
    {
        "GH_BeamSection", "GH_BeamProperty", "GH_PlateProperty", "GH_Material", "GH_LoadCase", "GH_LoadCombination", "GH_StagedConstruction", "GH_Group",
        "GH_CompositeBeamProperty", "GH_CoordinateSystem", "BridgeDesignGoo",
    };

    /// <summary>P2Z: the kinds of lane 2a (<see cref="PropertyGoo"/> without the kinds of lane 2b).</summary>
    internal static readonly string[] PropertyGoo2a = PropertyGoo.Where(k => k is not ("GH_CompositeBeamProperty" or "GH_CoordinateSystem" or "BridgeDesignGoo")).ToArray();

    private static object Prop(object o, string name) => o?.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance)?.GetValue(o);

    /// <summary>A GH type with a FEM value. P2Y: <c>GH_BeamSection.ScriptVariable</c> is always null, so a section is read by its value.</summary>
    private static bool HasFem(IGH_Goo goo) => goo.GetType().Name == "BridgeDesignGoo" ? DesignProperty(goo).Fem != null
        : goo.ScriptVariable() != null || (goo.GetType().Name == "GH_BeamSection" && Prop(goo, "Value") != null);

    /// <summary>L2B: the beam property of a bridge rule (a section rule or a family property): its FEM value and its GPC datum alone.</summary>
    private static (BeamPropertyModel Fem, BridGHe.Common.Gpc.Legacy.LegacyBeamProperty Gpc) DesignProperty(IGH_Goo goo) => Prop(goo, "Value") switch
    {
        BridGHe.Common.Geometry.BridgeSectionRule r => (r.Property, r.GpcProperty),
        BridGHe.Common.Geometry.BridgeFamilyProperty f => (f.Property, f.GpcProperty),
        _ => (null, null),
    };

    /// <summary>L2B: a bridge rule with its beam property given by its GPC datum alone (the datum the generators read from the FEM property,
    /// <see cref="BridGHe.Common.Geometry.BridgeGpcProperties.Of"/>), the form the producers give on the path of P2.</summary>
    private static IGH_Goo DesignGpcOnly(IGH_Goo goo)
    {
        object value = Prop(goo, "Value") switch
        {
            BridGHe.Common.Geometry.BridgeSectionRule r when r.Property != null => r with { Property = null, GpcProperty = BridGHe.Common.Geometry.BridgeGpcProperties.Of(r.Property) },
            BridGHe.Common.Geometry.BridgeFamilyProperty f when f.Property != null => f with { Property = null, GpcProperty = BridGHe.Common.Geometry.BridgeGpcProperties.Of(f.Property) },
            _ => null,
        };
        return value == null ? goo : (IGH_Goo)Activator.CreateInstance(goo.GetType(), value);
    }

    /// <summary>The GH type made from the GPC datum alone of a FEM-backed one, or the same goo (another kind, or no datum).</summary>
    private static IGH_Goo GpcOnly(IGH_Goo goo, ISet<string> kinds)
    {
        // L2B (note (1) of passo 73): in lane 2 a moment state with a FEM source is given as the state of a beam given by its draft alone (its
        // BridGHe ID and analysed GPC beam, no FEM source), the state Bridge State Override takes on the path of P2.
        if (goo is GH_ObjectWrapper { Value: BridGHe.Common.Geometry.BeamMomentState { Source: not null } state } && kinds.Contains("GH_CompositeBeamProperty"))
            return new GH_ObjectWrapper(state with { Source = null, BeamId = state.BeamId ?? state.Source.BeamId });
        if (goo == null || !kinds.Contains(goo.GetType().Name) || !HasFem(goo)) return goo;
        if (goo.GetType().Name == "BridgeDesignGoo") return DesignGpcOnly(goo);
        var datum = Prop(goo, "Gpc");
        if (datum == null) return goo;
        var from = goo.GetType().GetMethod("FromGpc", BindingFlags.Public | BindingFlags.Static);
        // PAR (A10): a material by its datum with the kind it declares.
        var args = goo.GetType().Name == "GH_Material" ? new[] { datum, Prop(goo, "GpcKind") } : from.GetParameters().Length == 2 ? new[] { datum, Prop(goo, "Guid") } : new[] { datum };
        var only = (IGH_Goo)from.Invoke(null, args);
        foreach (var name in new[] { "AnalysedGpc", "AnalysedGpcModel", "GpcMembraneBending" }) // L2C: the bending thickness of a membrane
            if (goo.GetType().GetProperty(name) is { CanWrite: true } p) p.SetValue(only, p.GetValue(goo));
        return only;
    }

    /// <summary>A copy of a component (its settings and persistent data) in a new document.</summary>
    private static GH_Component Copy(GH_Component c, GH_Document into)
    {
        var archive = new GH_Archive();
        if (!archive.AppendObject(c, "component")) throw new Exception("GPC-ONLY REPLAY: " + c.Name + " not written");
        var copy = (GH_Component)Grasshopper.Instances.ComponentServer.EmitObject(c.ComponentGuid);
        copy.CreateAttributes();
        if (!archive.ExtractObject(copy, "component")) throw new Exception("GPC-ONLY REPLAY: " + c.Name + " not read");
        into.AddObject(copy, false);
        return copy;
    }

    private static void SetInput(IGH_Param param, IGH_Structure data, Func<IGH_Goo, IGH_Goo> map)
    {
        param.RemoveAllSources();
        var persistent = param.GetType().GetProperty("PersistentData")?.GetValue(param) ?? throw new Exception("GPC-ONLY REPLAY: input " + param.Name + " is not persistent");
        persistent.GetType().GetMethod("Clear").Invoke(persistent, null);
        var append = persistent.GetType().GetMethods().First(m => m.Name == "Append" && m.GetParameters().Length == 2 && m.GetParameters()[1].ParameterType == typeof(GH_Path));
        var ensure = persistent.GetType().GetMethod("EnsurePath", new[] { typeof(GH_Path) });
        for (int b = 0; b < data.PathCount; b++)
        {
            var path = data.get_Path(b);
            var branch = data.get_Branch(b);
            ensure?.Invoke(persistent, new object[] { path });
            foreach (var item in branch) append.Invoke(persistent, new object[] { item == null ? null : map((IGH_Goo)item), path });
        }
    }

    /// <summary>The text of an output: a GH type with a GPC datum by the text of its datum with every GUID replaced by its ordinal in the text
    /// (coordinator, 7/11: the outputs without a FEM value take identities derived from the input draft, the FEM-backed ones the identity of
    /// the conversion of their FEM value until P2, so the lane test compares the data modulo GUIDs, the criterion of the captures).
    /// <para>Declared rule (identity only): the bridge data of a beam draft are keyed by the GUID of the beam, so their fingerprint (the line
    /// «bridge &lt;GUID&gt; &lt;fingerprint&gt;» of the text) changes with the identity; the line is compared by the bridge values of the beam
    /// instead (<see cref="GpcDraftCapture.BridgeValues"/>: member, super element, section state, composite sections; no GUID), as the captures
    /// compare a renamed Build Model output (R6.3).</para></summary>
    private static string Text(IGH_Goo goo)
    {
        if (goo == null) return "null";
        // L2B: a bridge rule by its data and the GPC datum of its beam property (of its FEM value, or given alone).
        if (goo.GetType().Name == "BridgeDesignGoo" && DesignProperty(goo) is var (fem, gpcProperty) && (fem != null || gpcProperty != null))
        {
            string rule = Prop(goo, "Value") switch
            {
                BridGHe.Common.Geometry.BridgeSectionRule r => FormattableString.Invariant($"rule {r.Family} {r.Group} {r.Segment} {r.Start:R} {r.End:R} {r.ScaleFrom.X:R} {r.ScaleFrom.Y:R} {r.ScaleTo.X:R} {r.ScaleTo.Y:R}"),
                BridGHe.Common.Geometry.BridgeFamilyProperty f => "family " + f.Family,
                _ => "",
            };
            var designProperty = gpcProperty ?? BridGHe.Common.Geometry.BridgeGpcProperties.Of(fem);
            return goo.GetType().Name + " " + rule + Environment.NewLine + GpcDraftCapture.WithoutGuids(GpcNullValueChecks.TextOf(designProperty) + GpcDraftArchiveChecks.Extras(designProperty));
        }
        var datum = Prop(goo, "Gpc");
        if (datum == null) return goo.GetType().Name + " " + goo;
        // L2C: with the BridGHe data beside the datum (the material kind of a beam property, the bending thickness of a membrane plate property).
        string text = GpcNullValueChecks.TextOf(datum) + GpcDraftArchiveChecks.Extras(datum)
            + (Prop(goo, "GpcMembraneBending") is double bending && bending != 0 ? "\nmembrane bending " + bending.ToString("R", System.Globalization.CultureInfo.InvariantCulture) : "");
        if (datum is BridGHe.Common.Gpc.Legacy.LegacyBeamDraft { Bridge: { } bridge })
        {
            var one = new GPC.Checkers.CompositeBridge.BridgeModelData(); one.Set(GpcDraftArchiveChecks.StaleAsStale(bridge)); // PAR: a stale state as stale
            string line = "bridge " + bridge.BeamGuid + " " + one.Fingerprint();
            if (text.Contains(line, StringComparison.Ordinal))
                text = text.Replace(line, "bridge " + bridge.BeamGuid + " values " + string.Join(" ; ", GpcDraftCapture.BridgeValues(one)));
        }
        // The same for the bridge data of the report of a draft (the line «bridge <count> <fingerprint>»).
        var report = datum switch
        {
            BridGHe.Common.Gpc.Legacy.LegacyBeamDraft b => b.Report, BridGHe.Common.Gpc.Legacy.LegacyNodeDraft n => n.Report,
            BridGHe.Common.Gpc.Legacy.LegacyPlateDraft p => p.Report, _ => null,
        };
        if (report?.Bridge is { } reportBridge)
        {
            var data = GpcDraftArchiveChecks.StaleAsStale(reportBridge); // PAR: a stale state as stale
            string line = "bridge " + data.Count + " " + data.Fingerprint();
            if (text.Contains(line, StringComparison.Ordinal))
                text = text.Replace(line, "bridge " + data.Count + " values " + string.Join(" ; ", GpcDraftCapture.BridgeValues(data)));
        }
        // PAR (A08): a stale section state as stale (its marker holds the signature it was computed for: of the FEM value or of the GPC datum).
        text = System.Text.RegularExpressions.Regex.Replace(text, System.Text.RegularExpressions.Regex.Escape(BridGHe.Common.Gpc.Legacy.LegacyModelImport.StaleSectionStatePrefix) + "[0-9A-Za-z:]+",
            BridGHe.Common.Gpc.Legacy.LegacyModelImport.StaleSectionStatePrefix + "(stale)");
        return goo.GetType().Name + " " + GpcDraftCapture.WithoutGuids(text);
    }


    private sealed class Run
    {
        internal List<string> Outputs = new List<string>();
        /// <summary>The same texts with their GUIDs (the determinism of the identities derived without a FEM value).</summary>
        internal List<string> Raw = new List<string>();
        internal List<string> Messages = new List<string>();
        internal int FemOutputs;
    }

    /// <summary>Lane 1b: the switch of the generators without FEM values (<c>GpcOnlyElements.GeneratorsWithoutFem</c>), on for the GPC-only
    /// copies of the generators lane only.</summary>
    private static PropertyInfo generators;
    private static bool generatorsLane;

    /// <summary>Lane 2: the switch of the producers of properties and data without FEM values (<c>GpcOnlyElements.PropertiesWithoutFem</c>),
    /// on for the GPC-only copies of lane 2 only (the producers of lane 2 replayed with every instance with data).</summary>
    private static bool propertiesLane;

    private static Run Solve(GH_Component original, Func<IGH_Goo, IGH_Goo> map, ISet<string> kinds, bool gpcOnly = false)
    {
        var doc = new GH_Document();
        bool on = gpcOnly && (generatorsLane || propertiesLane);
        try
        {
            var copy = Copy(original, doc);
            for (int i = 0; i < original.Params.Input.Count; i++)
                SetInput(copy.Params.Input[i], original.Params.Input[i].VolatileData, map);
            if (on) generators.SetValue(null, true);
            try { doc.Enabled = true; doc.NewSolution(false, GH_SolutionMode.Silent); }
            finally { if (on) generators.SetValue(null, false); }
            var run = new Run();
            foreach (var level in new[] { GH_RuntimeMessageLevel.Error, GH_RuntimeMessageLevel.Warning, GH_RuntimeMessageLevel.Remark })
                foreach (var m in copy.RuntimeMessages(level)) run.Messages.Add(level + ": " + m);
            foreach (var p in copy.Params.Output)
            {
                var data = p.VolatileData;
                for (int b = 0; b < data.PathCount; b++)
                {
                    var branch = data.get_Branch(b);
                    run.Outputs.Add($"{p.Name} {data.get_Path(b)} {branch.Count}"); run.Raw.Add(run.Outputs[^1]);
                    foreach (var item in branch)
                    {
                        var goo = item as IGH_Goo;
                        run.Outputs.Add(Text(goo));
                        var datum = goo == null ? null : Prop(goo, "Gpc");
                        run.Raw.Add(datum == null ? run.Outputs[^1] : goo.GetType().Name + " " + GpcNullValueChecks.TextOf(datum));
                        if (goo != null && kinds.Contains(goo.GetType().Name) && HasFem(goo)) run.FemOutputs++;
                    }
                }
            }
            return run;
        }
        finally { doc.Dispose(); }
    }

    /// <summary>One replay of a component instance: the copy with the inputs of today and the copy with GPC-only inputs give the same outputs
    /// modulo GUIDs and the same messages; the GPC-only outputs have no FEM value; a second GPC-only copy gives the same GUIDs.</summary>
    private static void Replay(GH_Component c, string where, ISet<string> kinds, IDictionary<string, (int Instances, int Items)> byType, List<string> failures)
    {
        Run fem, gpc, again;
        try { fem = Solve(c, g => g.Duplicate(), kinds); }
        catch (Exception ex) { failures.Add(where + ": the copy with the inputs of today fails: " + ex.GetBaseException().Message + Frame(ex)); return; }
        try { gpc = Solve(c, g => GpcOnly(g, kinds), kinds, true); again = Solve(c, g => GpcOnly(g, kinds), kinds, true); }
        catch (Exception ex) { failures.Add(where + ": the copy with GPC-only inputs fails: " + ex.GetBaseException().Message + Frame(ex)); return; }
        var (n, items) = byType.TryGetValue(c.GetType().Name, out var v) ? v : (0, 0);
        byType[c.GetType().Name] = (n + 1, items + fem.Outputs.Count);
        if (!fem.Messages.SequenceEqual(gpc.Messages)) failures.Add(where + ": messages " + string.Join(" / ", fem.Messages) + " -> " + string.Join(" / ", gpc.Messages));
        int diff = fem.Outputs.Zip(gpc.Outputs).Count(p => p.First != p.Second);
        if (fem.Outputs.Count != gpc.Outputs.Count || diff != 0)
        {
            var first = fem.Outputs.Zip(gpc.Outputs).FirstOrDefault(p => p.First != p.Second);
            failures.Add($"{where}: outputs {fem.Outputs.Count} -> {gpc.Outputs.Count}, {diff} different; first (FEM-backed -> GPC-only):" + Environment.NewLine
                + Differences(first.First ?? "<none>", first.Second ?? "<none>"));
        }
        if (gpc.FemOutputs != 0) failures.Add($"{where}: {gpc.FemOutputs} outputs with a FEM value from GPC-only inputs");
        if (!gpc.Raw.SequenceEqual(again.Raw)) failures.Add($"{where}: two GPC-only copies give other GUIDs (identities not deterministic)");
    }

    // -----------------------------------------------------------------------------------------------------------------------------------------
    // In-memory cases (coordinator, 7/11): the component types of the lane that no example of the scope uses.

    private static Assembly plugin;
    private static Type T(string name) => plugin.GetType("BridGHe.Grasshopper.DataTypes." + name, true);

    /// <summary>The FEM-backed GH type as the components make it: the constructor from the FEM value (optional parameters at their defaults).</summary>
    private static IGH_Goo Make(string type, object value)
    {
        var t = T(type);
        var ctor = t.GetConstructors().Where(c => c.GetParameters().Length >= 1 && c.GetParameters()[0].ParameterType.IsInstanceOfType(value)
            && c.GetParameters().Skip(1).All(p => p.HasDefaultValue)).OrderBy(c => c.GetParameters().Length).First();
        return (IGH_Goo)ctor.Invoke(new[] { value }.Concat(ctor.GetParameters().Skip(1).Select(p => p.DefaultValue)).ToArray());
    }

    private static IGH_Goo Goo(object v) => v switch
    {
        IGH_Goo g => g,
        double d => new GH_Number(d),
        int i => new GH_Integer(i),
        bool b => new GH_Boolean(b),
        string s => new GH_String(s),
        Rhino.Geometry.Vector3d x => new GH_Vector(x),
        Rhino.Geometry.Point3d p => new GH_Point(p),
        Rhino.Geometry.Line l => new GH_Line(l),
        Rhino.Geometry.Mesh m => new GH_Mesh(m), // P2Z
        Rhino.Geometry.Curve c => new GH_Curve(c), // P2Z
        Rhino.Geometry.Brep b => new GH_Brep(b), // P2Z
        BridGHe.Common.Geometry.BeamMomentState s => new GH_ObjectWrapper(s), // L2B
        _ => throw new ArgumentException("GPC-ONLY REPLAY: no goo for " + v?.GetType().Name),
    };

    /// <summary>The in-memory cases of lane 1: component type, label, inputs by index (one value, or an array for a list; the others keep their
    /// defaults). Elements of the examples 01 (beams, nodes), 02 (a beam with a distributed load) and 03 (a quadrilateral plate, and a triangle on
    /// three of its corners) with a load case of their own.</summary>
    private static List<(string Type, string Label, Dictionary<int, object> Inputs)> Lane1Cases()
    {
        var examples = NativeExamples.Create().ToDictionary(e => e.Id, e => e.Model);
        var m1 = examples["01-cantilever"]; var m2 = examples["02-simply-supported"]; var m3 = examples["03-plate-patch"];
        var beam = Make("Fem.GH_Beam", m2.Beams[1]); var beam2 = Make("Fem.GH_Beam", m2.Beams[2]); var loaded = Make("Fem.GH_Beam", m2.Beams[0]);
        var node = Make("Fem.GH_Node", m1.Nodes.Last()); var node2 = Make("Fem.GH_Node", m1.Nodes.First());
        var quad = m3.Plates.Single();
        var plate = Make("Fem.GH_Plate", quad);
        var tri = Make("Fem.GH_Plate", new PlateModel("2", quad.Points.Take(3).ToArray(), quad.PlateProperty));
        // P2X: the plate of 03 with a local axis angle (the datum of an internalized 0.3.0 plate: its draft is the conversion of the value).
        var angled = Make("Fem.GH_Plate", new PlateModel("3", quad.Points.ToArray(), quad.PlateProperty) { AngleDeg = 30 });
        var lc = Make("Fem.GH_LoadCase", new LoadCaseModel("Replay", "Replay"));
        var along = m2.Beams[1].PointTo - m2.Beams[1].PointFrom;
        var cases = new List<(string, string, Dictionary<int, object>)>
        {
            ("BeamReleasesComponent", "partial releases", new Dictionary<int, object> { [0] = loaded, [3] = 0.0, [9] = 5.0, [12] = 0.0 }),
            ("BeamStringGroupComponent", "two beams", new Dictionary<int, object> { [0] = new object[] { beam, loaded }, [1] = 3 }),
            ("BeamSupportComponent", "compression only", new Dictionary<int, object> { [0] = beam, [1] = 1000.0, [2] = 1, [3] = true, [4] = 0.5 }),
            ("BeamTaperComponent", "taper", new Dictionary<int, object> { [0] = loaded, [1] = 1.0, [2] = 0.8, [3] = 0.5, [4] = 0.6, [5] = 0, [6] = 2 }),
            ("LoadPatchTypeComponent", "Auto1 on a quadrilateral", new Dictionary<int, object> { [0] = plate, [1] = 0, [2] = new object[] { 1 } }),
            ("LoadPatchTypeComponent", "Angle Split on a triangle (refused)", new Dictionary<int, object> { [0] = tri, [1] = 4, [2] = new object[] { 1, 2 } }),
            ("LoadPatchTypeComponent", "too many edges (refused)", new Dictionary<int, object> { [0] = tri, [1] = 5, [2] = new object[] { 0, 1, 2, 3 }, [3] = new object[] { 1.0, 1.0, 1.0, 1.0 } }),
            ("NodeStiffnessComponent", "springs", new Dictionary<int, object> { [0] = node, [1] = new Rhino.Geometry.Vector3d(1, 2, 3), [2] = new Rhino.Geometry.Vector3d(4, 5, 6) }),
            ("PlateSupportComponent", "bottom face", new Dictionary<int, object> { [0] = plate, [1] = 500.0, [2] = true, [3] = 1 }),
            ("BeamDistributedMomentComponent", "uniform, axis 2", new Dictionary<int, object> { [0] = loaded, [1] = lc, [2] = 0, [3] = 1, [6] = 2.5 }),
            ("BeamPointLoadComponent", "local", new Dictionary<int, object> { [0] = loaded, [1] = lc, [3] = 1.5, [5] = -2.0, [6] = 0.25 }),
            ("BeamPointMomentComponent", "local", new Dictionary<int, object> { [0] = beam, [1] = lc, [4] = 3.0, [6] = 0.5 }),
            ("NodeDisplacementLoadComponent", "refused (0.4.0)", new Dictionary<int, object> { [0] = node, [1] = lc, [2] = new Rhino.Geometry.Vector3d(0, 0, 1) }),
            ("NodeLoadComponent", "force", new Dictionary<int, object> { [0] = node, [1] = lc, [2] = new Rhino.Geometry.Vector3d(1, 0, -5) }),
            ("NodeMomentComponent", "moment", new Dictionary<int, object> { [0] = node, [1] = lc, [2] = new Rhino.Geometry.Vector3d(0, 2, 0) }),
            ("NodeTemperatureComponent", "temperature", new Dictionary<int, object> { [0] = node, [1] = lc, [2] = 0, [3] = 15.0 }),
            ("PlateNSMassComponent", "mass", new Dictionary<int, object> { [0] = plate, [1] = lc, [2] = 1.2, [3] = 1.0, [4] = new Rhino.Geometry.Vector3d(0, 0, 0.1) }),
            ("PlatePreStressComponent", "prestress", new Dictionary<int, object> { [0] = plate, [1] = lc, [2] = 1.0, [3] = 2.0, [4] = 0.0 }),
            ("PlatePressureComponent", "local pressure", new Dictionary<int, object> { [0] = plate, [1] = lc, [3] = 0, [4] = new GH_Number(3.0), [5] = false }),
            ("AlignBeamAxesComponent", "axis 3 against the beam (reversed)", new Dictionary<int, object> { [0] = loaded, [1] = 2, [2] = -along }),
            ("AlignBeamAxesComponent", "axis 3 along the beam", new Dictionary<int, object> { [0] = loaded, [1] = 2, [2] = along }),
            ("AlignBeamAxesComponent", "axis 1 on a direction", new Dictionary<int, object> { [0] = loaded, [1] = 0, [2] = new Rhino.Geometry.Vector3d(0, 1, 1) }),
            ("AlignBeamAxesComponent", "axis 2 on a direction", new Dictionary<int, object> { [0] = beam, [1] = 1, [2] = new Rhino.Geometry.Vector3d(0, -1, 2) }),
            ("AlignBeamAxesComponent", "local x on a direction", new Dictionary<int, object> { [0] = beam, [1] = 3, [2] = new Rhino.Geometry.Vector3d(0.3, 1, -1) }),
            ("AlignBeamAxesComponent", "local y on a direction", new Dictionary<int, object> { [0] = beam, [1] = 4, [2] = new Rhino.Geometry.Vector3d(0, 1, 0) }),
            ("AlignBeamAxesComponent", "axis 1 along the beam (warning)", new Dictionary<int, object> { [0] = beam, [1] = 0, [2] = along }),
            ("AlignPlateAxesComponent", "axis 3 against the normal (reversed)", new Dictionary<int, object> { [0] = plate, [1] = 2, [2] = new Rhino.Geometry.Vector3d(0, 0, -1) }),
            ("AlignPlateAxesComponent", "axis 3 along the normal", new Dictionary<int, object> { [0] = plate, [1] = 2, [2] = new Rhino.Geometry.Vector3d(0, 0, 1) }),
            ("AlignPlateAxesComponent", "axis 1 along the normal (warning)", new Dictionary<int, object> { [0] = plate, [1] = 0, [2] = new Rhino.Geometry.Vector3d(0, 0, 1) }),
            ("AlignPlateAxesComponent", "axis 1 already aligned", new Dictionary<int, object> { [0] = plate, [1] = 0, [2] = new Rhino.Geometry.Vector3d(1, 0, 0) }),
            // P2X (user decision of 8/11, «Mappalo come nella 0.3.0»): the local axis angle on GPC.
            ("AlignPlateAxesComponent", "axis 1 turned by an increment", new Dictionary<int, object> { [0] = plate, [1] = 0, [2] = new Rhino.Geometry.Vector3d(1, 1, 0) }),
            ("AlignPlateAxesComponent", "axis 2 turned by an increment", new Dictionary<int, object> { [0] = plate, [1] = 1, [2] = new Rhino.Geometry.Vector3d(1, -2, 0.5) }),
            ("AlignPlateAxesComponent", "axis 1 on a plate with an angle", new Dictionary<int, object> { [0] = angled, [1] = 0, [2] = new Rhino.Geometry.Vector3d(0, 1, 0) }),
            ("AlignPlateAxesComponent", "axis 3 on a plate with an angle (reversed)", new Dictionary<int, object> { [0] = angled, [1] = 2, [2] = new Rhino.Geometry.Vector3d(0, 0, -1) }),
            ("PlatePreStressComponent", "prestress on a plate with an angle", new Dictionary<int, object> { [0] = angled, [1] = lc, [2] = 1.0, [3] = 2.0, [4] = 0.0 }),
            ("ElementIdComponent", "a plate with an angle", new Dictionary<int, object> { [0] = angled, [1] = "P8" }),
            ("ElementFilterComponent", "find a beam and a node", new Dictionary<int, object> { [0] = new object[] { node, beam, plate, node2 }, [1] = new object[] { beam, node2 } }),
            ("MergeGeometryComponent", "elements (no geometry)", new Dictionary<int, object> { [0] = new object[] { beam, node } }),
            ("NodeFilterComponent", "by position", new Dictionary<int, object> { [0] = new object[] { node, node2 }, [2] = new object[] { m1.Nodes.Last().Position }, [3] = 0 }),
            ("PlateFilterComponent", "by position and property", new Dictionary<int, object> { [0] = new object[] { plate, tri }, [2] = new object[] { quad.Points[3] }, [3] = 0, [4] = new object[] { "none" } }),
            ("BeamFilterComponent", "by end point", new Dictionary<int, object> { [0] = new object[] { beam, beam2, loaded }, [3] = new object[] { m2.Beams[2].PointTo }, [4] = 0 }),
            ("ElementIdComponent", "a node", new Dictionary<int, object> { [0] = node, [1] = "N9" }),
            ("ElementIdComponent", "a plate", new Dictionary<int, object> { [0] = plate, [1] = "P9" }),
        };
        return cases;
    }

    /// <summary>The in-memory cases of lane 1b: the hidden components no example uses (Bridge Member Assignment, Bridge Connections in its
    /// three modes) and the cases the examples do not reach (elastic and mixed bearings, beams given by their drafts alone through Beam
    /// Offsets and Moment Dispatch), on the Simple Bridge of the Smoke (<c>SimpleBridgeTests.Input</c>) and the beams of the example 02.</summary>
    private static List<(string Type, string Label, Dictionary<int, object> Inputs)> Lane1bCases()
    {
        var examples = NativeExamples.Create().ToDictionary(e => e.Id, e => e.Model);
        var m2 = examples["02-simply-supported"];
        var beam = Make("Fem.GH_Beam", m2.Beams[1]); var loaded = Make("Fem.GH_Beam", m2.Beams[0]);
        var geometry = Make("BridgeGeometryGoo", BridGHe.Common.Geometry.SimpleBridgeBuilder.Build(SimpleBridgeTests.Input()));
        var steel = Make("Fem.GH_BeamProperty", BridgeAssignmentTests.Property());
        var lines = new object[] { new Rhino.Geometry.Line(0, 0, 0, 0, 0, 400), new Rhino.Geometry.Line(1000, 0, 0, 1000, 0, 400) };
        return new List<(string, string, Dictionary<int, object>)>
        {
            ("BridgeMemberAssignmentComponent", "one default property", new Dictionary<int, object> { [0] = geometry, [1] = new object[] { steel }, [2] = new object[] { -1 } }),
            ("BridgeMemberAssignmentComponent", "girders only (unassigned members)", new Dictionary<int, object> { [0] = geometry, [1] = new object[] { steel }, [2] = new object[] { 0 } }),
            ("BridgeConnectionsComponent", "mode 0, rigid links", new Dictionary<int, object> { [0] = 0, [1] = lines }),
            ("BridgeConnectionsComponent", "mode 1, deformable members", new Dictionary<int, object> { [0] = 1, [1] = lines, [2] = steel }),
            ("BridgeConnectionsComponent", "mode 2, beam offsets", new Dictionary<int, object> { [0] = 2, [3] = new object[] { beam, loaded }, [4] = new Rhino.Geometry.Vector3d(12, -20, 0) }),
            ("BridgeBeamOffsetsComponent", "two beams", new Dictionary<int, object> { [0] = new object[] { beam, loaded }, [1] = new Rhino.Geometry.Vector3d(0, 35, 0) }),
            ("BridgeRigidLinksComponent", "two links", new Dictionary<int, object> { [0] = lines }),
            ("BridgeConnectionBeamsComponent", "two members", new Dictionary<int, object> { [0] = lines, [1] = steel }),
            ("BridgeBearingsComponent", "elastic bearings on support 1", new Dictionary<int, object> { [0] = geometry, [1] = new object[] { 1 }, [3] = new object[] { 3 },
                [5] = new Rhino.Geometry.Vector3d(10, 20, 30), [6] = new Rhino.Geometry.Vector3d(0, 4, 0) }),
            ("BridgeBearingsComponent", "mixed types, guide angle", new Dictionary<int, object> { [0] = geometry, [2] = new object[] { 2 }, [3] = new object[] { 0, 1, 2 }, [4] = 23.0 }),
            ("BeamMomentDispatchComponent", "beams without results (Missing)", new Dictionary<int, object> { [0] = new object[] { beam, loaded }, [1] = "Dead" }),
        };
    }

    /// <summary>P2Z: the first item of output <paramref name="output"/> of a component solved in memory (the data of today: FEM-backed with
    /// its GPC datum attached, as the examples have them).</summary>
    private static IGH_Goo Produce(string type, Dictionary<int, object> inputs, int output = 0)
    {
        using var doc = new GH_Document();
        var c = BridgeSegmentExcelTests.Add(doc, type);
        foreach (var input in inputs)
        {
            var tree = new GH_Structure<IGH_Goo>();
            foreach (var v in input.Value is object[] list ? list : new[] { input.Value }) tree.Append(Goo(v), new GH_Path(0));
            SetInput(c.Params.Input[input.Key], tree, g => g);
        }
        BridgeSegmentExcelTests.Solve(doc, true);
        var item = c.Params.Output[output].VolatileData.AllData(true).FirstOrDefault()
            ?? throw new Exception("GPC-ONLY REPLAY: " + type + " gives no output (" + string.Join(" / ", c.RuntimeMessages(GH_RuntimeMessageLevel.Error)) + ")");
        return item.Duplicate();
    }

    /// <summary>P2Z (lane 2a): the in-memory cases of the lane-2a types that no example of the scope uses (and of the paths the examples do
    /// not reach): sections, materials, groups, stages, plate properties, the geometric property components, Plate from geometry, the node
    /// and plate filters by group. Their lane-2a inputs come from the producer components solved in memory (FEM-backed with the attached
    /// datum, as in the examples); the replay gives them by their GPC data alone.</summary>
    private static List<(string Type, string Label, Dictionary<int, object> Inputs)> Lane2aCases()
    {
        var examples = NativeExamples.Create().ToDictionary(e => e.Id, e => e.Model);
        var m1 = examples["01-cantilever"]; var m3 = examples["03-plate-patch"];
        var node = Make("Fem.GH_Node", m1.Nodes.Last()); var node2 = Make("Fem.GH_Node", m1.Nodes.First());
        var plate = Make("Fem.GH_Plate", m3.Plates.Single());
        var steel = Produce("MaterialSteelComponent", new Dictionary<int, object> { [0] = "S355", [1] = 2 });
        var concrete = Produce("MaterialConcreteComponent", new Dictionary<int, object> { [0] = "C35/45", [1] = 7 });
        var section = Produce("BeamSectionIComponent", new Dictionary<int, object> { [0] = 300.0, [1] = 250.0, [2] = 800.0, [3] = 20.0, [4] = 16.0, [5] = 12.0 });
        var circle = Produce("BeamSectionHollowCircleComponent", new Dictionary<int, object> { [0] = 323.9, [1] = 10.0 });
        var deck = Produce("GroupComponent", new Dictionary<int, object> { [0] = "Deck" });
        var pour = Produce("GroupComponent", new Dictionary<int, object> { [0] = "Pour1", [1] = deck });
        var lc = Produce("LoadCaseComponent", new Dictionary<int, object> { [0] = "Replay", [1] = false });
        var lc2 = Produce("LoadCaseComponent", new Dictionary<int, object> { [0] = "Traffic", [1] = false });
        var combo = Produce("LoadCombinationComponent", new Dictionary<int, object> { [0] = "ULS", [1] = new object[] { lc, lc2 }, [2] = new object[] { 1.35, 1.5 } });
        var property = Produce("BeamPropertyComponent", new Dictionary<int, object> { [0] = "Girder", [1] = 5, [2] = section, [3] = steel });
        var tube = Produce("BeamPropertyComponent", new Dictionary<int, object> { [0] = "Tube", [1] = 5, [2] = circle, [3] = steel });
        var shell = Produce("PlatePropertyComponent", new Dictionary<int, object> { [0] = "Deck", [1] = 0, [2] = 250.0, [3] = 250.0, [4] = concrete });
        var q = m3.Plates.Single().Points;
        var mesh = new Rhino.Geometry.Mesh(); foreach (var p in q) mesh.Vertices.Add(p); mesh.Faces.AddFace(0, 1, 2, 3);
        var brep = Rhino.Geometry.Brep.CreatePlanarBreps(new Rhino.Geometry.Rectangle3d(Rhino.Geometry.Plane.WorldXY, 200, 400).ToNurbsCurve(), 1e-6).Single();
        return new List<(string, string, Dictionary<int, object>)>
        {
            ("GroupComponent", "root group", new Dictionary<int, object> { [0] = "Deck" }),
            ("GroupComponent", "with a parent", new Dictionary<int, object> { [0] = "Pour2", [1] = pour }),
            ("StagedConstructionComponent", "one stage", new Dictionary<int, object> { [0] = "Stage 1", [1] = new object[] { deck, pour }, [2] = new object[] { combo }, [3] = false, [4] = true }),
            ("MaterialAluminiumComponent", "wrought alloy", new Dictionary<int, object> { [0] = "Al", [1] = 0, [2] = "EN AW-6082 T6" }),
            ("MaterialTableComponent", "stress-strain table", new Dictionary<int, object> { [0] = "Table", [1] = true, [2] = 0.3, [3] = 80000.0, [4] = 1.2e-5, [5] = 7.85e-9,
                [6] = new object[] { -355.0, 0.0, 355.0 }, [7] = new object[] { -0.002, 0.0, 0.002 } }),
            ("BeamSectionAngleComponent", "angle", new Dictionary<int, object> { [0] = 100.0, [1] = 10.0, [2] = 150.0, [3] = 8.0 }),
            ("BeamSectionCComponent", "channel", new Dictionary<int, object> { [0] = 90.0, [1] = 12.0, [2] = 260.0, [3] = 9.0 }),
            ("BeamSectionGenericComponent", "explicit values", new Dictionary<int, object> { [0] = 9000.0, [1] = 1.2e8, [2] = 4e7, [3] = 5e5, [4] = 0.0, [5] = 0.0, [6] = 4000.0,
                [7] = 3000.0, [8] = 8e5, [9] = 4e5, [10] = 0.0, [11] = 10.0, [12] = 0.1 }),
            ("BeamSectionHollowCircleComponent", "tube", new Dictionary<int, object> { [0] = 323.9, [1] = 10.0 }),
            ("BeamSectionSolidCircleComponent", "bar", new Dictionary<int, object> { [0] = 60.0 }),
            ("BeamSectionTComponent", "tee", new Dictionary<int, object> { [0] = 200.0, [1] = 15.0, [2] = 300.0, [3] = 10.0 }),
            ("BeamSectionBrepComponent", "planar shape", new Dictionary<int, object> { [0] = brep }),
            ("SectionGeometricPropertyComponent", "tube", new Dictionary<int, object> { [0] = circle }),
            ("SectionGeometricPropertyComponent", "I section", new Dictionary<int, object> { [0] = section }),
            ("BeamGeometricPropertyComponent", "I property", new Dictionary<int, object> { [0] = property }),
            ("BeamGeometricPropertyComponent", "tube property", new Dictionary<int, object> { [0] = tube }),
            ("PlatePropertyComponent", "thin shell", new Dictionary<int, object> { [0] = "Deck", [1] = 0, [2] = 250.0, [3] = 250.0, [4] = concrete }),
            ("PlatePropertyComponent", "membrane", new Dictionary<int, object> { [0] = "Web", [1] = 4, [2] = 12.0, [3] = 12.0, [4] = steel }),
            ("PlatePropertyComponent", "load patch (no material)", new Dictionary<int, object> { [0] = "Patch", [1] = 5 }),
            // The plate property of the example 03 (its datum the conversion of its value): with the property of Plate Property (its datum
            // attached) the FEM path of Plate has a DRAFT CHECK difference of its own, the XML form of the property name inside the plate
            // draft (passo 76).
            ("PlateComponent", "quadrilateral with property and groups", new Dictionary<int, object> { [0] = mesh, [1] = Make("Fem.GH_PlateProperty", m3.Plates.Single().PlateProperty),
                [2] = new object[] { deck, pour }, [3] = 15.0, [4] = 5.0 }),
            ("BeamComponent", "line with property and group", new Dictionary<int, object> { [0] = new Rhino.Geometry.LineCurve(new Rhino.Geometry.Point3d(0, 0, 0), new Rhino.Geometry.Point3d(5000, 0, 0)), [1] = property, [2] = new object[] { pour } }),
            ("NodeFilterComponent", "by group", new Dictionary<int, object> { [0] = new object[] { node, node2 }, [4] = new object[] { deck } }),
            ("PlateFilterComponent", "by group", new Dictionary<int, object> { [0] = new object[] { plate }, [5] = new object[] { deck } }),
            ("BeamOverrideComponent", "property and group", new Dictionary<int, object> { [0] = Make("Fem.GH_Beam", examples["02-simply-supported"].Beams[1]), [1] = tube, [2] = new object[] { deck } }),
            ("StrausBxsSectionComponent", "missing file (the same error on both paths)", new Dictionary<int, object> { [0] = Path.Combine(Path.GetTempPath(), "p2z-missing.bxs"), [1] = 2 }),
        };
    }

    /// <summary>L2B (lane 2b): the in-memory cases of the lane-2b types that no example of the scope uses (and of the paths the examples do
    /// not reach). Their lane-2 inputs come from the producer components solved in memory, as in <see cref="Lane2aCases"/>.</summary>
    private static List<(string Type, string Label, Dictionary<int, object> Inputs)> Lane2bCases()
    {
        var examples = NativeExamples.Create().ToDictionary(e => e.Id, e => e.Model);
        var m2 = examples["02-simply-supported"];
        var beam = Make("Fem.GH_Beam", m2.Beams[1]); var loaded = Make("Fem.GH_Beam", m2.Beams[0]);
        var original = Make("Fem.GH_BeamProperty", m2.Beams[1].BeamProperty);
        var steel = Produce("MaterialSteelComponent", new Dictionary<int, object> { [0] = "S355", [1] = 2 });
        var concrete = Produce("MaterialConcreteComponent", new Dictionary<int, object> { [0] = "C35/45", [1] = 7 });
        var rebar = Produce("MaterialRebarComponent", new Dictionary<int, object> { [0] = "B450C" });
        var section = Produce("BeamSectionIComponent", new Dictionary<int, object> { [0] = 300.0, [1] = 250.0, [2] = 800.0, [3] = 20.0, [4] = 16.0, [5] = 12.0 });
        var circle = Produce("BeamSectionHollowCircleComponent", new Dictionary<int, object> { [0] = 323.9, [1] = 10.0 });
        var girder = Produce("BeamPropertyComponent", new Dictionary<int, object> { [0] = "Girder", [1] = 5, [2] = section, [3] = steel });
        var slab = Produce("BeamSectionConcreteSlabComponent", new Dictionary<int, object> { [0] = 250.0, [1] = 2500.0, [2] = 1000.0, [3] = 50.0, [4] = 800.0, [5] = 50.0 });
        var compositeInputs = new Dictionary<int, object> { [0] = "CB", [1] = slab, [2] = section, [3] = concrete, [4] = steel, [5] = rebar };
        var composite = Produce("BeamPropertyCompositeComponent", compositeInputs);
        var geometry = Make("BridgeGeometryGoo", BridGHe.Common.Geometry.SimpleBridgeBuilder.Build(SimpleBridgeTests.Input() with
            { Segments = new BridGHe.Common.Geometry.BridgeSegments { MaximumLength = 12000 } }));
        var family = Produce("BridgeFamilyPropertyComponent", new Dictionary<int, object> { [0] = girder, [1] = 0 });
        var schedule = Produce("BridgeSectionScheduleComponent", new Dictionary<int, object> { [0] = girder, [1] = 0.0, [2] = 10000.0, [3] = 0, [4] = 1, [5] = 0 });
        return new List<(string, string, Dictionary<int, object>)>
        {
            ("GetBeamPropertyComponent", "a beam of the example 02", new Dictionary<int, object> { [0] = beam }),
            ("BeamPropertyOverrideComponent", "one pair, the beams of its property matched", new Dictionary<int, object> { [0] = new object[] { beam, loaded }, [1] = new object[] { original }, [2] = new object[] { girder } }),
            ("BeamPropertyOverrideComponent", "an original name not found (warning)", new Dictionary<int, object> { [0] = new object[] { beam }, [1] = new object[] { girder }, [2] = new object[] { girder } }),
            ("BeamPropertyOverrideComponent", "two names, conflicting replacements (error)", new Dictionary<int, object> { [0] = new object[] { beam }, [1] = new object[] { original, girder },
                [2] = new object[] { girder, Produce("BeamPropertyComponent", new Dictionary<int, object> { [0] = "Girder", [1] = 5, [2] = circle, [3] = steel }) } }),
            ("BeamSectionHPropertyComponent", "I section", new Dictionary<int, object> { [0] = section }),
            ("BeamSectionHPropertyComponent", "I property", new Dictionary<int, object> { [0] = girder }),
            ("BeamSectionHPropertyComponent", "not an I section (error)", new Dictionary<int, object> { [0] = circle }),
            ("BeamPropertyCompositeComponent", "slab, I steel and three materials", compositeInputs),
            ("BeamPropertyCompositeComponent", "steel only (no concrete, no rebars)", new Dictionary<int, object>(compositeInputs) { [9] = false, [10] = false }),
            ("CompositeSectionGeometricPropertyComponent", "n0 with concrete and rebars", new Dictionary<int, object> { [0] = composite, [1] = 6.0 }),
            ("BridgeSectionTemplateComponent", "template on a base property", new Dictionary<int, object> { [0] = geometry, [2] = girder }),
            ("BridgeSegmentExcelComponent", "template mode on a base property", new Dictionary<int, object> { [0] = geometry, [2] = 0, [3] = girder }),
            ("BridgeSegmentSectionsComponent", "base property, no table", new Dictionary<int, object> { [0] = geometry, [1] = girder }),
            ("BridgeSegmentSectionsComponent", "base property not an I section (error)", new Dictionary<int, object> { [0] = geometry, [1] = original }),
            ("BridgeFamilyPropertyComponent", "girders", new Dictionary<int, object> { [0] = girder, [1] = 0 }),
            ("BridgeSectionScheduleComponent", "girder 1, 0..10 m", new Dictionary<int, object> { [0] = girder, [1] = 0.0, [2] = 10000.0, [3] = 0, [4] = 1, [5] = 0 }),
            ("BridgeFemMembersComponent", "a family property and a section rule", new Dictionary<int, object> { [0] = geometry, [1] = new object[] { family }, [2] = new object[] { schedule } }),
            ("BridgeFemMembersComponent", "a family property only", new Dictionary<int, object> { [0] = geometry, [1] = new object[] { family } }),
        }.Concat(StateOverrideCases()).ToList();
    }

    /// <summary>L2C (lane 2c, coordinator answers of 8/11: the GPC-only path behaves as in 0.3.0): the in-memory cases of the data lane 2c
    /// adds to the recipe and to the records (the fillet radius R, the section moduli of a Generic section, the shapes of a Brep section, the
    /// mirror of the double sections, the material kind of a beam property, the bending thickness of a membrane) and of the rejections of the
    /// GPC-only path it removes (Section Geometric Property on Generic, Brep and mirrored sections, Bridge Deck Mesh with a membrane, Bridge
    /// Segment Sections on a base that is not steel, Beam Property Override with replacements that differ only in R), and the Plate and Beam
    /// with a property named as their group (one XML form of the record, passo 76).</summary>
    private static List<(string Type, string Label, Dictionary<int, object> Inputs)> Lane2cCases()
    {
        var examples = NativeExamples.Create().ToDictionary(e => e.Id, e => e.Model);
        var m2 = examples["02-simply-supported"]; var m3 = examples["03-plate-patch"];
        var beam = Make("Fem.GH_Beam", m2.Beams[1]);
        var original = Make("Fem.GH_BeamProperty", m2.Beams[1].BeamProperty);
        var steel = Produce("MaterialSteelComponent", new Dictionary<int, object> { [0] = "S355", [1] = 2 });
        var concrete = Produce("MaterialConcreteComponent", new Dictionary<int, object> { [0] = "C35/45", [1] = 7 });
        var dims = new Dictionary<int, object> { [0] = 300.0, [1] = 250.0, [2] = 800.0, [3] = 20.0, [4] = 16.0, [5] = 12.0 };
        var section = Produce("BeamSectionIComponent", dims);
        var filleted = Produce("BeamSectionIComponent", new Dictionary<int, object>(dims) { [6] = 15.0 });
        var girder = Produce("BeamPropertyComponent", new Dictionary<int, object> { [0] = "Girder", [1] = 5, [2] = section, [3] = steel });
        var concreteGirder = Produce("BeamPropertyComponent", new Dictionary<int, object> { [0] = "Girder C", [1] = 5, [2] = section, [3] = concrete });
        var bridge = BridGHe.Common.Geometry.SimpleBridgeBuilder.Build(SimpleBridgeTests.Input() with { Segments = new BridGHe.Common.Geometry.BridgeSegments { MaximumLength = 12000 } });
        var geometry = Make("BridgeGeometryGoo", bridge);
        // The table of the conci with every dimension of the base ('*') and a radius of 12 mm.
        var table = BridGHe.Common.Geometry.BridgeSegmentSections.Synchronize(bridge, null).Table
            .Select(l => l.StartsWith("#", StringComparison.Ordinal) ? l : l.Substring(0, l.LastIndexOf('*')) + "12").ToArray();
        var concio = Produce("BridgeSegmentSectionsComponent", new Dictionary<int, object> { [0] = geometry, [1] = girder }, 1);
        var concioR = Produce("BridgeSegmentSectionsComponent", new Dictionary<int, object> { [0] = geometry, [1] = girder, [2] = table.Cast<object>().ToArray() }, 1);
        var generic = Produce("BeamSectionGenericComponent", new Dictionary<int, object> { [0] = 9000.0, [1] = 1.2e8, [2] = 4e7, [3] = 5e5, [6] = 4000.0, [7] = 3000.0, [8] = 8e5, [9] = 4e5,
            [10] = 20.0, [11] = 10.0, [12] = 0.1 });
        var brep = Produce("BeamSectionBrepComponent", new Dictionary<int, object> { [0] = Rhino.Geometry.Brep.CreatePlanarBreps(new[] {
            new Rhino.Geometry.Rectangle3d(Rhino.Geometry.Plane.WorldXY, 200, 400).ToNurbsCurve(),
            new Rhino.Geometry.Rectangle3d(new Rhino.Geometry.Plane(new Rhino.Geometry.Point3d(50, 50, 0), Rhino.Geometry.Vector3d.ZAxis), 100, 300).ToNurbsCurve() }, 1e-6).Single() });
        var doubleC = Produce("BeamSectionCComponent", new Dictionary<int, object> { [0] = 90.0, [1] = 12.0, [2] = 260.0, [3] = 9.0, [4] = true, [5] = 10.0 });
        var doubleL = Produce("BeamSectionAngleComponent", new Dictionary<int, object> { [0] = 100.0, [1] = 10.0, [2] = 150.0, [3] = 8.0, [4] = true, [5] = 12.0 });
        var membrane = Produce("PlatePropertyComponent", new Dictionary<int, object> { [0] = "Deck", [1] = 4, [2] = 250.0, [3] = 100.0, [4] = concrete });
        var membraneSlab = Produce("PlatePropertyComponent", new Dictionary<int, object> { [0] = "Deck", [1] = 4, [2] = 250.0, [3] = 250.0, [4] = concrete });
        var deckGeometry = Make("BridgeGeometryGoo", BridGHe.Common.Geometry.SimpleBridgeBuilder.Build(SimpleBridgeTests.Input()));
        // The plate property and the beam property named as the group of the element (the same text given to both).
        var shell = Produce("PlatePropertyComponent", new Dictionary<int, object> { [0] = "Deck", [1] = 0, [2] = 250.0, [3] = 250.0, [4] = concrete });
        var deckProperty = Produce("BeamPropertyComponent", new Dictionary<int, object> { [0] = "Deck", [1] = 5, [2] = section, [3] = steel });
        var deck = Produce("GroupComponent", new Dictionary<int, object> { [0] = "Deck" });
        var pour = Produce("GroupComponent", new Dictionary<int, object> { [0] = "Pour1", [1] = deck });
        var q = m3.Plates.Single().Points;
        var mesh = new Rhino.Geometry.Mesh(); foreach (var p in q) mesh.Vertices.Add(p); mesh.Faces.AddFace(0, 1, 2, 3);
        return new List<(string, string, Dictionary<int, object>)>
        {
            // (a), (d): the fillet radius R.
            ("BeamSectionIComponent", "fillet radius 15", new Dictionary<int, object>(dims) { [6] = 15.0 }),
            ("SectionGeometricPropertyComponent", "I section with fillet radius 15", new Dictionary<int, object> { [0] = filleted }),
            ("BeamSectionHPropertyComponent", "I section with fillet radius 15", new Dictionary<int, object> { [0] = filleted }),
            ("BridgeSegmentSectionsComponent", "table with radius 12", new Dictionary<int, object> { [0] = geometry, [1] = girder, [2] = table.Cast<object>().ToArray() }),
            ("BridgeSegmentSectionsComponent", "base property with radius 12, radius '*'", new Dictionary<int, object> { [0] = geometry, [1] = concioR }),
            ("BridgeSectionTemplateComponent", "base property with radius 12", new Dictionary<int, object> { [0] = geometry, [2] = concioR }),
            ("BeamSectionHPropertyComponent", "concio property with radius 12", new Dictionary<int, object> { [0] = concioR }),
            // (b): the material kind.
            ("BridgeSegmentSectionsComponent", "base property of concrete (error)", new Dictionary<int, object> { [0] = geometry, [1] = concreteGirder }),
            // (c): two replacements with one name that differ only in R.
            ("BeamPropertyOverrideComponent", "two replacements differing only in R (error)", new Dictionary<int, object> { [0] = new object[] { beam },
                [1] = new object[] { original, girder }, [2] = new object[] { concio, concioR } }),
            ("BeamPropertyOverrideComponent", "the same replacement twice (same R)", new Dictionary<int, object> { [0] = new object[] { beam },
                [1] = new object[] { original, girder }, [2] = new object[] { concioR, concioR.Duplicate() } }),
            // Section Geometric Property as in 0.3.0: Generic (section moduli), Brep (zeros: the component did not compute), double sections.
            ("BeamSectionGenericComponent", "section moduli and offsets", new Dictionary<int, object> { [0] = 9000.0, [1] = 1.2e8, [2] = 4e7, [3] = 5e5, [6] = 4000.0, [7] = 3000.0,
                [8] = 8e5, [9] = 4e5, [10] = 20.0, [11] = 10.0, [12] = 0.1 }),
            ("SectionGeometricPropertyComponent", "Generic section with S11/S22", new Dictionary<int, object> { [0] = generic }),
            ("BeamSectionBrepComponent", "planar shape with a hole", new Dictionary<int, object> { [0] = Rhino.Geometry.Brep.CreatePlanarBreps(new[] {
                new Rhino.Geometry.Rectangle3d(Rhino.Geometry.Plane.WorldXY, 200, 400).ToNurbsCurve(),
                new Rhino.Geometry.Rectangle3d(new Rhino.Geometry.Plane(new Rhino.Geometry.Point3d(50, 50, 0), Rhino.Geometry.Vector3d.ZAxis), 100, 300).ToNurbsCurve() }, 1e-6).Single() }),
            ("SectionGeometricPropertyComponent", "Brep section", new Dictionary<int, object> { [0] = brep }),
            ("BeamSectionCComponent", "double channel", new Dictionary<int, object> { [0] = 90.0, [1] = 12.0, [2] = 260.0, [3] = 9.0, [4] = true, [5] = 10.0 }),
            ("BeamSectionAngleComponent", "double angle", new Dictionary<int, object> { [0] = 100.0, [1] = 10.0, [2] = 150.0, [3] = 8.0, [4] = true, [5] = 12.0 }),
            ("SectionGeometricPropertyComponent", "double channel", new Dictionary<int, object> { [0] = doubleC }),
            ("SectionGeometricPropertyComponent", "double angle", new Dictionary<int, object> { [0] = doubleL }),
            // Bridge Deck Mesh with a membrane: its bending thickness checked against the slab, as before.
            ("PlatePropertyComponent", "membrane with a bending thickness", new Dictionary<int, object> { [0] = "Deck", [1] = 4, [2] = 250.0, [3] = 100.0, [4] = concrete }),
            ("BridgeDeckMeshComponent", "membrane, bending thickness not the slab (error)", new Dictionary<int, object> { [0] = deckGeometry, [1] = membrane }),
            ("BridgeDeckMeshComponent", "membrane, bending thickness of the slab", new Dictionary<int, object> { [0] = deckGeometry, [1] = membraneSlab }),
            // The record of a plate and of a beam with a property named as their group (passo 76: one XML form).
            ("PlateComponent", "quadrilateral with the property of Plate Property named as its group", new Dictionary<int, object> { [0] = mesh, [1] = shell,
                [2] = new object[] { deck, pour }, [3] = 15.0, [4] = 5.0 }),
            ("BeamComponent", "line with a property named as its group", new Dictionary<int, object> { [0] = new Rhino.Geometry.LineCurve(new Rhino.Geometry.Point3d(0, 0, 0),
                new Rhino.Geometry.Point3d(5000, 0, 0)), [1] = deckProperty, [2] = new object[] { deck } }),
        };
    }

    /// <summary>L2C (coordinator, 8/11): the checks of the review of lane 2 (R65-C2, ChatGPT task 19) that a replay cannot express (two
    /// GPC-only runs compared with each other, a helper of a form, a component wired to a parameter): each adds its failures and counts one
    /// in-memory case.</summary>
    private static void ReviewChecks(ISet<string> kinds, List<string> failures, ref int count)
    {
        void Check(bool ok, string what) { if (!ok) failures.Add("in memory: review " + what); }
        IGH_Goo Only(IGH_Goo g) => GpcOnly(g, kinds);
        // R65-C2-03: two GPC-only State Override runs with the states of two cases of the same sign give other identities.
        Guid[] Girders(string caseName)
        {
            var (type, _, inputs) = StateOverrideCases(caseName).First();
            using var doc = new GH_Document();
            var c = BridgeSegmentExcelTests.Add(doc, type);
            foreach (var input in inputs)
            {
                var tree = new GH_Structure<IGH_Goo>();
                foreach (var v in input.Value is object[] list ? list : new[] { input.Value }) tree.Append(Goo(v), new GH_Path(0));
                SetInput(c.Params.Input[input.Key], tree, Only);
            }
            BridgeSegmentExcelTests.Solve(doc, true);
            return c.Params.Output[0].VolatileData.AllData(true).Select(g => ((BridGHe.Common.Gpc.Legacy.LegacyBeamDraft)Prop(g, "Gpc")).Beam.Guid).ToArray();
        }
        var q1 = Girders("Q1"); var q2 = Girders("Q2");
        Check(q1.Length > 0 && q1.Length == q2.Length && !q1.Intersect(q2).Any(), $"R65-C2-03: State Override, states of Q1 and Q2: {q1.Length} and {q2.Length} girders, {q1.Intersect(q2).Count()} identities in common");
        Check(Girders("Q1").SequenceEqual(q1), "R65-C2-03: State Override, the same states twice: the same identities");
        count++;
        // R65-C2-05: the rows of the Load Combinations form and the combinations made of them, FEM-backed and by the GPC datum alone.
        var rowsType = plugin.GetType("BridGHe.Grasshopper.Forms.LoadCombinationRows", true);
        var lc = Produce("LoadCaseComponent", new Dictionary<int, object> { [0] = "G", [1] = false });
        var lq = Produce("LoadCaseComponent", new Dictionary<int, object> { [0] = "Q", [1] = false });
        var combo = Produce("LoadCombinationComponent", new Dictionary<int, object> { [0] = "ULS", [1] = new object[] { lc, lq }, [2] = new object[] { 1.35, 1.5 } });
        var femCases = new[] { lc, lq }.Select(c => (LoadCaseModel)Prop(c, "Value")).ToList();
        var records = new[] { lc, lq }.Select(c => (BridGHe.Common.Gpc.Legacy.LegacyCaseData)Prop(c, "Gpc")).ToList();
        var comboType = combo.GetType(); var listType = typeof(List<>).MakeGenericType(comboType);
        object ListOf(params IGH_Goo[] goo) { var l = (System.Collections.IList)Activator.CreateInstance(listType); foreach (var g in goo) l.Add(g); return l; }
        string RowsText(object rows) => string.Join(" | ", ((System.Collections.IEnumerable)rows).Cast<object>().Select(r => Prop(r, "Name") + ":" + string.Join(",",
            ((IEnumerable<KeyValuePair<string, double>>)Prop(r, "Factors")).Select(f => f.Key + "=" + f.Value.ToString("R", System.Globalization.CultureInfo.InvariantCulture)))));
        var of = rowsType.GetMethod("Of", BindingFlags.NonPublic | BindingFlags.Static);
        string femRows = RowsText(of.Invoke(null, new[] { ListOf(combo) })), gpcRows = RowsText(of.Invoke(null, new[] { ListOf(Only(combo)) }));
        Check(femRows == gpcRows && femRows.Contains("ULS:G=1.35,Q=1.5"), $"R65-C2-05: rows {femRows} -> {gpcRows}");
        bool gpcOnly = (bool)rowsType.GetMethod("GpcOnly", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, new object[] { ListOf(Only(combo)), femCases, records });
        bool femOnly = (bool)rowsType.GetMethod("GpcOnly", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, new object[] { ListOf(combo), femCases, records });
        Check(gpcOnly && !femOnly, "R65-C2-05: the form makes GPC-only combinations for GPC-only inputs only");
        var columns = (string[])rowsType.GetMethod("Columns", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, new object[] { true, femCases, records });
        var cells = new List<(string, IReadOnlyList<object>)> { ("SLS", new object[] { 1.0, 0.6 }) };
        var make = rowsType.GetMethod("Combinations", BindingFlags.NonPublic | BindingFlags.Static);
        object Made(bool only) { var r = make.Invoke(null, new object[] { cells, columns, femCases, records, only }); return r.GetType().GetField("Item1").GetValue(r); }
        var madeGpc = ((System.Collections.IEnumerable)Made(true)).Cast<IGH_Goo>().Single(); var madeFem = ((System.Collections.IEnumerable)Made(false)).Cast<IGH_Goo>().Single();
        Check(Prop(madeGpc, "Value") == null && GpcNullValueChecks.TextOf(Prop(madeGpc, "Gpc")) == GpcNullValueChecks.TextOf(Prop(madeFem, "Gpc")),
            "R65-C2-05: the combination of a row by its GPC datum alone, the datum of the FEM one");
        var clash = make.Invoke(null, new object[] { new List<(string, IReadOnlyList<object>)> { ("G", new object[] { 1.0, 1.0 }) }, columns, femCases, records, true });
        Check(clash.GetType().GetField("Item2").GetValue(clash) is string m && m.Contains("must be different to the load cases names"), "R65-C2-05: a combination named as a case refused, with the message of the form");
        count++;
        // R65-C2-06: a combination of the Load Combinations parameter followed by Staged Construction with an added case only (G -> G, Q).
        string Stage(bool only)
        {
            using var doc = new GH_Document();
            var param = (IGH_Param)Activator.CreateInstance(plugin.GetTypes().Single(t => t.Name == "LoadCombinationsParam")); param.CreateAttributes(); doc.AddObject(param, false);
            var single = Produce("LoadCombinationComponent", new Dictionary<int, object> { [0] = "ULS", [1] = new object[] { lc }, [2] = new object[] { 1.35 } });
            var tree = new GH_Structure<IGH_Goo>(); tree.Append(only ? Only(single) : single.Duplicate(), new GH_Path(0)); SetInput(param, tree, g => g);
            var stage = BridgeSegmentExcelTests.Add(doc, "StagedConstructionComponent");
            var deck = Produce("GroupComponent", new Dictionary<int, object> { [0] = "Deck" });
            foreach (var (k, v) in new (int, object)[] { (0, "Stage 1"), (1, deck), (3, false), (4, true) })
            { var t = new GH_Structure<IGH_Goo>(); t.Append(Goo(v), new GH_Path(0)); SetInput(stage.Params.Input[k], t, g => g); }
            stage.Params.Input[2].AddSource(param);
            stage.GetType().GetMethod("SetLoadCases").Invoke(stage, new object[] { femCases, records });
            BridgeSegmentExcelTests.Solve(doc, true);
            var output = stage.Params.Output[0].VolatileData.AllData(true).Single();
            var persistent = ((System.Collections.IEnumerable)param.GetType().GetProperty("PersistentData").GetValue(param)).Cast<object>().ToList();
            return GpcNullValueChecks.TextOf(Prop(output, "Gpc")) + " / persistent " + string.Join(";", ((IGH_Structure)param.GetType().GetProperty("PersistentData").GetValue(param))
                .AllData(true).Select(g => GpcNullValueChecks.TextOf(Prop(g, "Gpc"))));
        }
        string femStage = Stage(false), gpcStage = Stage(true);
        Check(femStage == gpcStage && femStage.Contains("Q~"), "R65-C2-06: Staged Construction with an added case: " + Short(femStage) + " -> " + Short(gpcStage));
        count++;
        // R65-C2-07: Beam Property Override by the GPC data: the links to the analysed element stay when no beam is matched, go when one is.
        var examples = NativeExamples.Create().ToDictionary(e => e.Id, e => e.Model);
        var femBeam = Make("Fem.GH_Beam", examples["02-simply-supported"].Beams[1]); // the beam by its draft alone (GH_Beam is not a kind of lane 2)
        var beam = (IGH_Goo)T("Fem.GH_Beam").GetMethod("FromGpc", BindingFlags.Public | BindingFlags.Static).Invoke(null, new[] { Prop(femBeam, "Gpc"), Prop(femBeam, "Guid") });
        beam.GetType().GetProperty("AnalysedGpc").SetValue(beam, ((BridGHe.Common.Gpc.Legacy.LegacyBeamDraft)Prop(beam, "Gpc")).Beam);
        var own = Make("Fem.GH_BeamProperty", examples["02-simply-supported"].Beams[1].BeamProperty);
        var steel = Produce("MaterialSteelComponent", new Dictionary<int, object> { [0] = "S355", [1] = 2 });
        var section = Produce("BeamSectionIComponent", new Dictionary<int, object> { [0] = 300.0, [1] = 250.0, [2] = 800.0, [3] = 20.0, [4] = 16.0, [5] = 12.0 });
        var girder = Produce("BeamPropertyComponent", new Dictionary<int, object> { [0] = "Girder", [1] = 5, [2] = section, [3] = steel });
        string lastInfo = "";
        object Analysed(IGH_Goo original)
        {
            using var doc = new GH_Document();
            var c = BridgeSegmentExcelTests.Add(doc, "BeamPropertyOverrideComponent");
            foreach (var (k, v) in new (int, IGH_Goo)[] { (0, beam), (1, original), (2, girder) }) { var t = new GH_Structure<IGH_Goo>(); t.Append(v, new GH_Path(0)); SetInput(c.Params.Input[k], t, g => g); }
            BridgeSegmentExcelTests.Solve(doc, true);
            var outputs = c.Params.Output[0].VolatileData.AllData(true).ToList(); lastInfo = string.Join(" / ", c.Params.Output[2].VolatileData.AllData(true).Select(g => g.ToString())) + "; " + outputs.Count + " outputs; input analysed " + (Prop(beam, "AnalysedGpc") != null) + "; duplicate analysed " + (Prop(beam.Duplicate(), "AnalysedGpc") != null) + "; input volatile analysed " + (Prop(c.Params.Input[0].VolatileData.AllData(true).First(), "AnalysedGpc") != null) + "; output datum " + (Prop(outputs[0], "Gpc") != null);
            return outputs.Count == 1 ? Prop(outputs[0], "AnalysedGpc") : null;
        }
        Check(Analysed(girder) != null, "R65-C2-07: no beam matched: the analysed element kept (" + lastInfo + ")");
        Check(Analysed(own) == null, "R65-C2-07: a beam matched: the analysed element gone");
        count++;
        // R65-C2-08: Bridge Member Assignment with a family given by a GPC-only property and the others by a property the reader refuses: the
        // error and no outputs. PAR: a cutoff bar is no longer refused (A01); a property on an Omega section (no 0.3.0 component makes one) is.
        {
            using var doc = new GH_Document();
            var c = BridgeSegmentExcelTests.Add(doc, "BridgeMemberAssignmentComponent");
            var cutoff = Make("Fem.GH_BeamProperty", new BeamPropertyModel("Bar") { Name = "Bar", Material = (MaterialModel)Prop(steel, "Value"), PropertyType = BeamPropertyType.Beam,
                Section = new SectionModel { SectionType = SectionModel.SectionTypes.Omega, B = 100, D = 200, T1 = 8, T2 = 8 } });
            var geometry = Make("BridgeGeometryGoo", BridGHe.Common.Geometry.SimpleBridgeBuilder.Build(SimpleBridgeTests.Input()));
            foreach (var (k, v) in new (int, object[])[] { (0, new object[] { geometry }), (1, new object[] { Only(girder), cutoff }), (2, new object[] { 0, -1 }) })
            { var t = new GH_Structure<IGH_Goo>(); foreach (var x in v) t.Append(Goo(x), new GH_Path(0)); SetInput(c.Params.Input[k], t, g => g); }
            BridgeSegmentExcelTests.Solve(doc, true);
            var errors = c.RuntimeMessages(GH_RuntimeMessageLevel.Error);
            Check(errors.Any(e => e.Contains("BridgeSectionKindNotMappedYet")) && c.Params.Output[0].VolatileDataCount == 0 && c.Params.Output[1].VolatileDataCount == 0,
                "R65-C2-08: a refused plan, the error and no outputs: " + string.Join(" / ", errors) + $"; {c.Params.Output[0].VolatileDataCount} beams, {c.Params.Output[1].VolatileDataCount} IDs");
        }
        count++;
        Console.WriteLine("GPC-ONLY properties (review R65-C2): 5 in-memory checks (State Override identities, Load Combinations form rows, Staged Construction with an added case, Beam Property Override analysed links, Member Assignment refusal)");
    }

    /// <summary>L2B (note (1) of passo 73): Bridge State Override on the reference of <c>gpc-state-override</c> (the girders of the simple bridge
    /// of the design tests on the steel reference, moment states classified from its GPC conversion with synthetic results, all signs; one
    /// composite section per segment): an override and two refusals (a missing state, a mixed sign with policy 0). The replay gives the
    /// composite sections by their definitions and the states without their FEM source (their BridGHe ID and analysed GPC beam).</summary>
    /// <param name="caseName">L2C (R65-C2-03): the classification case given to every state (null: that of the classification).</param>
    private static IEnumerable<(string Type, string Label, Dictionary<int, object> Inputs)> StateOverrideCases(string caseName = null)
    {
        var steel = BridgeSegmentSectionsTests.BaseProperty(); steel.Material.Density = 7.85e-9;
        var geometry = BridGHe.Common.Geometry.SimpleBridgeBuilder.Build(SimpleBridgeTests.Input());
        var schedule = BridGHe.Common.Geometry.BridgeSegmentSections.Create(geometry, steel, null);
        var all = BridGHe.Common.Geometry.BridgeMemberAssignment.Assign(geometry, new Dictionary<int, BeamPropertyModel> { [-1] = steel }, schedule.Rules).Beams;
        var lc = new LoadCaseModel("Dead", "Dead");
        var girders = all.Where(b => b.Attributes.OfType<BridgeMemberAttributeModel>().Single().Value.Family == BridGHe.Common.Geometry.BridgeMemberFamily.Girder).ToArray();
        for (int i = 0; i < girders.Length; i++) foreach (double s in new[] { 0d, .5, 1d })
            girders[i].Results.Add(new BeamForceResultModel(lc, s, 0, "", true) { My = (i % 4) switch { 0 => 1e8, 1 => -1e8, 2 => (s - .5) * 2e8, _ => 500 }, Mx = -1e8 });
        var reference = new ModelModel(ModelModel.ModelUnits.Nmm) { Name = "State override reference" };
        reference.AddElements(all); reference.AddElements(geometry.Nodes.Select((p, i) => new NodeModel((i + 1).ToString(), p)).ToArray()); reference.LoadCases.Add(lc);
        var analysed = GpcSyntheticResults.Analysed(reference);
        var catalog = BridGHe.Common.Gpc.GpcResultCatalog.Of(analysed) ?? throw new Exception("GPC-ONLY REPLAY: no results catalog");
        var femBeams = reference.Beams.ToList();
        var states = BridGHe.Common.Geometry.BeamMomentClassification.Classify(girders, girders.Select(b => (catalog, analysed, analysed.BeamElements[femBeams.IndexOf(b) + 1])).ToArray(), "Dead");
        if (caseName != null) states = states.Select(s => s with { Case = caseName }).ToArray();
        var ids = schedule.Rules.Select(r => $"G{r.Group}-C{r.Segment}").ToArray();
        var composites = ids.Select(id => { var p = BridgeGrillageExample.Composite(new()); p.Name = "CS " + id; p.SteelSection = BridGHe.Common.Helpers.ModelArchive.Clone(steel.Section);
            p.ConsiderRebar = true; p.ConcreteSection = new(250, 3000, 1600, 40); return (object)Make("GH_CompositeBeamProperty", p); }).ToArray();
        var beams = all.Select(b => (object)Make("Fem.GH_Beam", b)).ToArray();
        Dictionary<int, object> Inputs(IEnumerable<BridGHe.Common.Geometry.BeamMomentState> s, int mixed) =>
            new() { [0] = beams, [1] = ids.Cast<object>().ToArray(), [2] = composites, [3] = s.Cast<object>().ToArray(), [4] = 1, [5] = mixed, [6] = 0, [7] = true };
        yield return ("BridgeStateOverrideComponent", "long term, mixed uncracked, all signs", Inputs(states, 1));
        yield return ("BridgeStateOverrideComponent", "a missing state (error)", Inputs(states.Skip(1), 1));
        yield return ("BridgeStateOverrideComponent", "a mixed sign with policy 0 (error)", Inputs(states, 0));
    }

    /// <summary>P2Z (lane 2a, native): the Straus7 section components (21 shapes and the library section), replayed in memory under the
    /// native host (<c>--core-only --native</c>): Straus7 computes the section on both paths, the GPC-only copy gives the recipe alone. The
    /// dimensions of <c>CheckNativeSections</c>.</summary>
    private static List<(string Type, string Label, Dictionary<int, object> Inputs)> NativeSectionCases()
    {
        var cases = new List<(string, string, Dictionary<int, object>)>();
        foreach (var shape in BridGHe.Common.Straus.StrausSectionCatalog.All)
        {
            double Dimension(string n) => n.StartsWith("R") || n.StartsWith("alpha") ? 0 : n == "D" ? 400 : n == "B2" ? 100 : n.StartsWith("B") ? 200 : n == "L" ? 20 : 10;
            var dimensions = shape.Key == "BglBulb" ? new double[] { 200, 50, 10, 10, 0, 5, 0, 0, 30 } : shape.Dimensions.Select(Dimension).ToArray();
            var type = plugin.GetTypes().Single(t => !t.IsAbstract && t.Name.StartsWith("Straus") && t.Name.EndsWith("SectionComponent") && t.BaseType?.Name == "StrausSectionComponent"
                && (string)t.GetProperty("ShapeKey", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(Activator.CreateInstance(t)) == shape.Key).Name;
            cases.Add((type, shape.Key, dimensions.Select((d, i) => (d, i)).ToDictionary(p => p.i, p => (object)p.d)));
        }
        cases.Add(("StrausLibrarySectionComponent", "an item not in the library (the same error on both paths)", new Dictionary<int, object> { [0] = "P2Z-none", [1] = "none", [2] = false, [3] = 2 }));
        // L2C (lane 2c, as in 0.3.0): Section Geometric Property on a native section gives the values Straus7 computes (no longer an error).
        var first = cases[0];
        cases.Add(("SectionGeometricPropertyComponent", "native section " + first.Item2 + " (the values of Straus7)", new Dictionary<int, object> { [0] = Produce(first.Item1, first.Item3) }));
        return cases;
    }

    #region PAR (R6.5 P1): the 0.3.0 parity on the GPC path (A01, A02, A02c, A04, A07, A08, A10)

    /// <summary>PAR (user decision of 8/11, «Sì, come proponi»: what 0.3.0 exported from the UI comes back on the GPC path): the in-memory
    /// cases of the data the GPC path refused (inventory of ChatGPT task 20): a cutoff bar (A01, with the inputs not shown: zeros, brittle), a
    /// Brep section on Beam Property (A02c), a material with a stress-strain table on the four shell and plate types and the refusal on a
    /// membrane and a shear panel (A04), a plate from a mesh of five vertices (A07: an error and no output on both paths), Beam Override and
    /// Beam Property Override of a beam with a current section state (A08: the new property, the state stale).</summary>
    private static List<(string Type, string Label, Dictionary<int, object> Inputs)> ParityCases()
    {
        var steel = Produce("MaterialSteelComponent", new Dictionary<int, object> { [0] = "S355", [1] = 2 });
        var section = Produce("BeamSectionIComponent", new Dictionary<int, object> { [0] = 300.0, [1] = 250.0, [2] = 800.0, [3] = 20.0, [4] = 16.0, [5] = 12.0 });
        var brep = Produce("BeamSectionBrepComponent", new Dictionary<int, object> { [0] = Rhino.Geometry.Brep.CreatePlanarBreps(new[] {
            new Rhino.Geometry.Rectangle3d(Rhino.Geometry.Plane.WorldXY, 200, 400).ToNurbsCurve(),
            new Rhino.Geometry.Rectangle3d(new Rhino.Geometry.Plane(new Rhino.Geometry.Point3d(50, 50, 0), Rhino.Geometry.Vector3d.ZAxis), 100, 300).ToNurbsCurve() }, 1e-6).Single() });
        // A04: a material with E and a stress-strain table (a former value; the Table Material component gives E = 0, which the GPC elastic
        // material refuses: library question of PAR).
        var table = TableMaterial();
        var pentagon = new Rhino.Geometry.Mesh();
        foreach (var (x, y) in new[] { (0.0, 0.0), (1000.0, 0.0), (1500.0, 500.0), (1000.0, 1000.0), (0.0, 1000.0) }) pentagon.Vertices.Add(x, y, 0);
        pentagon.Faces.AddFace(0, 1, 3, 4); pentagon.Faces.AddFace(1, 2, 3);
        var shell = Produce("PlatePropertyComponent", new Dictionary<int, object> { [0] = "Deck", [1] = 0, [2] = 250.0, [3] = 250.0, [4] = steel });
        var cases = new List<(string, string, Dictionary<int, object>)>
        {
            ("BeamPropertyComponent", "A01: cutoff bar, the cutoff inputs not shown (zeros, brittle)", new Dictionary<int, object> { [0] = "Bar", [1] = 3, [2] = section, [3] = steel }),
            ("BeamPropertyComponent", "A02c: Brep section with a hole", new Dictionary<int, object> { [0] = "Brep", [1] = 5, [2] = brep, [3] = steel }),
            ("PlateComponent", "A07: a mesh of five vertices (error, no output)", new Dictionary<int, object> { [0] = pentagon, [1] = shell }),
        };
        foreach (var (type, name) in new[] { (0, "ShellThin"), (1, "ShellThick"), (2, "PlateThin"), (3, "PlateThick") })
            cases.Add(("PlatePropertyComponent", "A04: " + name + " of a material with a stress-strain table", new Dictionary<int, object> { [0] = "Table plate", [1] = type, [2] = 20.0, [3] = 20.0, [4] = table }));
        // A08: a girder of Bridge State Override (a current section state) given another property.
        var (stateType, _, stateInputs) = StateOverrideCases().First();
        var stated = ProduceAll(stateType, stateInputs).First(g => ((BridGHe.Common.Gpc.Legacy.LegacyBeamDraft)Prop(g, "Gpc")).Bridge?.SectionState != null);
        var other = Produce("BeamPropertyComponent", new Dictionary<int, object> { [0] = "Other", [1] = 5, [2] = section, [3] = steel });
        var former = Make("Fem.GH_BeamProperty", ((BeamModel)Prop(stated, "Value")).BeamProperty);
        cases.Add(("BeamOverrideComponent", "A08: a beam with a current section state, another property (the state stale)", new Dictionary<int, object> { [0] = stated, [1] = other }));
        cases.Add(("BeamPropertyOverrideComponent", "A08: a beam with a current section state, another property (the state stale)",
            new Dictionary<int, object> { [0] = new object[] { stated }, [1] = new object[] { former }, [2] = new object[] { other } }));
        return cases;
    }

    /// <summary>PAR (A02, A03): Beam Property on the T, C (single) and angle sections of the components of 0.3.0, under the native host: the
    /// property is the native Straus7 section 0.3.0 wrote, resolved by Straus7 (its values and principal angle).</summary>
    private static List<(string Type, string Label, Dictionary<int, object> Inputs)> ParityNativeCases()
    {
        var steel = Produce("MaterialSteelComponent", new Dictionary<int, object> { [0] = "S355", [1] = 2 });
        var cases = new List<(string, string, Dictionary<int, object>)>();
        foreach (var (type, label) in new[] { ("BeamSectionTComponent", "T"), ("BeamSectionCComponent", "C"), ("BeamSectionAngleComponent", "angle") })
        {
            var s = Produce(type, new Dictionary<int, object> { [0] = 150.0, [1] = 10.0, [2] = 300.0, [3] = 12.0 });
            cases.Add(("BeamPropertyComponent", "A02: " + label + " section (the native section of 0.3.0)", new Dictionary<int, object> { [0] = "PAR " + label, [1] = 5, [2] = s, [3] = steel }));
        }
        return cases;
    }

    /// <summary>PAR (A04): a former material with E and a stress-strain table (strain, stress pairs of 0.3.0), FEM-backed.</summary>
    private static IGH_Goo TableMaterial() => Make("Fem.GH_Material", new MaterialModel("Table") { Name = "Table", Modulus = 210000, PoissonRatio = 0.3, Density = 7.85e-9,
        StressStrainTable = new[] { (stress: 0.0, strain: 0.0), (stress: 235.0, strain: 0.00112), (stress: 360.0, strain: 0.15) } });

    /// <summary>PAR: every item of output <paramref name="output"/> of a component solved in memory (as <see cref="Produce"/>).</summary>
    private static List<IGH_Goo> ProduceAll(string type, Dictionary<int, object> inputs, int output = 0)
    {
        using var doc = new GH_Document();
        var c = BridgeSegmentExcelTests.Add(doc, type);
        foreach (var input in inputs)
        {
            var tree = new GH_Structure<IGH_Goo>();
            foreach (var v in input.Value is object[] list ? list : new[] { input.Value }) tree.Append(Goo(v), new GH_Path(0));
            SetInput(c.Params.Input[input.Key], tree, g => g);
        }
        BridgeSegmentExcelTests.Solve(doc, true);
        return c.Params.Output[output].VolatileData.AllData(true).Select(g => g.Duplicate()).ToList();
    }

    /// <summary>PAR: the checks of the parity beyond a replay: a cutoff bar with its four inputs (A01: the law of the inputs, replayed); the
    /// stale state of A08 (its marker, refused by the checks as before); the kind a material declares (A10: a generic GPC material declared
    /// Steel is Steel for Segment Sections, and it is saved with the material).</summary>
    private static void ParityChecks(ISet<string> kinds, List<string> failures, ref int count)
    {
        void Check(bool ok, string what) { if (!ok) failures.Add("in memory: parity " + what); }
        var steel = Produce("MaterialSteelComponent", new Dictionary<int, object> { [0] = "S355", [1] = 2 });
        var section = Produce("BeamSectionIComponent", new Dictionary<int, object> { [0] = 300.0, [1] = 250.0, [2] = 800.0, [3] = 20.0, [4] = 16.0, [5] = 12.0 });
        // A01: Beam Property of type Cutoff Bar shows the four inputs (its menu); with them the property carries their law, replayed.
        {
            using var doc = new GH_Document();
            var c = BridgeSegmentExcelTests.Add(doc, "BeamPropertyComponent");
            foreach (var (k, v) in new (int, object)[] { (0, "Bar"), (1, 3), (2, section), (3, steel) }) { var t = new GH_Structure<IGH_Goo>(); t.Append(Goo(v), new GH_Path(0)); SetInput(c.Params.Input[k], t, g => g); }
            c.ExpireSolution(false);
            Check(c.Params.Input.Count == 8, "A01: Beam Property of type Cutoff Bar shows the cutoff inputs (" + c.Params.Input.Count + " inputs)");
            if (c.Params.Input.Count == 8)
            {
                foreach (var (k, v) in new (int, object)[] { (4, 1.5e5), (5, 2.5e5), (6, 1), (7, true) }) { var t = new GH_Structure<IGH_Goo>(); t.Append(Goo(v), new GH_Path(0)); SetInput(c.Params.Input[k], t, g => g); }
                BridgeSegmentExcelTests.Solve(doc, true);
                var p = c.Params.Output[0].VolatileData.AllData(true).FirstOrDefault();
                var law = (p == null ? null : (GPC.Model.ElementProperties.ElasticBeamProperty)((BridGHe.Common.Gpc.Legacy.LegacyBeamProperty)Prop(p, "Gpc")).Property)?.CutoffLaw;
                Check(law != null && law.Failure == GPC.Model.ElementProperties.BeamCutoffFailure.Ductile && law.KeepMass && law.TensionLimit == 1.5e5 && law.CompressionLimit == 2.5e5,
                    "A01: the cutoff law of the inputs (" + (law == null ? "none" : $"{law.Failure} {law.KeepMass} {law.TensionLimit} {law.CompressionLimit}") + ")");
                Replay(c, "in memory: BeamPropertyComponent (A01: cutoff bar with its inputs)", kinds, new SortedDictionary<string, (int, int)>(), failures);
            }
        }
        count++;
        // A08: the stale marker, as the reader marks a former state whose property changed; the checks refuse it with the former message.
        {
            var (stateType, _, stateInputs) = StateOverrideCases().First();
            var stated = ProduceAll(stateType, stateInputs).First(g => ((BridGHe.Common.Gpc.Legacy.LegacyBeamDraft)Prop(g, "Gpc")).Bridge?.SectionState != null);
            var other = Produce("BeamPropertyComponent", new Dictionary<int, object> { [0] = "Other", [1] = 5, [2] = section, [3] = steel });
            var before = ((BridGHe.Common.Gpc.Legacy.LegacyBeamDraft)Prop(stated, "Gpc")).Bridge.SectionState.PropertyFingerprint;
            var overridden = ProduceAll("BeamOverrideComponent", new Dictionary<int, object> { [0] = GpcOnly(stated, kinds), [1] = GpcOnly(other, kinds) }).SingleOrDefault();
            var draft = overridden == null ? null : (BridGHe.Common.Gpc.Legacy.LegacyBeamDraft)Prop(overridden, "Gpc");
            var after = draft?.Bridge?.SectionState?.PropertyFingerprint;
            Check(after == BridGHe.Common.Gpc.Legacy.LegacyModelImport.StaleSectionStatePrefix + before && draft.Beam.BeamProperty.Name == "Other"
                && after != GPC.Checkers.CompositeBridge.BridgeWorkflowDefinition.PropertyFingerprint(draft.Beam),
                "A08: Beam Override (GPC-only) gives the new property and the state stale with its former fingerprint (" + (after ?? "no state") + ")");
        }
        count++;
        // A10: a material given by its GPC datum with a declared kind: Steel for Beam Property and Segment Sections even when its GPC type is
        // the generic material (a kind whose strengths the GPC type refused), Undefined without the declaration; saved with the material.
        {
            var generic = new GPC.Model.Materials.Material("Custom S", 210000, 0.3, 7.85e-9, 1.2e-5);
            var declared = T("Fem.GH_Material").GetMethod("FromGpc").Invoke(null, new object[] { generic, "Steel" });
            var undeclared = T("Fem.GH_Material").GetMethod("FromGpc").Invoke(null, new object[] { generic, null });
            Check((string)Prop(declared, "GpcKind") == "Steel" && (string)Prop(undeclared, "GpcKind") == "Undefined", "A10: the declared kind of the material");
            var (data, records) = BridGHe.Common.Gpc.GpcDraftArchive.Write(generic, "Steel");
            BridGHe.Common.Gpc.GpcDraftArchive.ReadMaterial(data, records, out var readKind);
            Check(records != null && readKind == "Steel" && BridGHe.Common.Gpc.GpcDraftArchive.Write(generic, "Undefined").Records == null, "A10: the declared kind saved with the material (only when not the kind of its GPC type)");
            string KindOf(object material)
            {
                var p = ProduceAll("BeamPropertyComponent", new Dictionary<int, object> { [0] = "Girder", [1] = 5, [2] = GpcOnly(section, kinds), [3] = material }).SingleOrDefault();
                return p == null ? null : ((BridGHe.Common.Gpc.Legacy.LegacyBeamProperty)Prop(p, "Gpc")).Data.MaterialKind;
            }
            Check(KindOf(declared) == "Steel" && KindOf(undeclared) == "Undefined", $"A10: Beam Property records the declared kind ({KindOf(declared)}, {KindOf(undeclared)})");
            var geometry = Make("BridgeGeometryGoo", BridGHe.Common.Geometry.SimpleBridgeBuilder.Build(SimpleBridgeTests.Input() with { Segments = new BridGHe.Common.Geometry.BridgeSegments { MaximumLength = 12000 } }));
            bool Accepted(object material)
            {
                var p = ProduceAll("BeamPropertyComponent", new Dictionary<int, object> { [0] = "Girder", [1] = 5, [2] = GpcOnly(section, kinds), [3] = material }).Single();
                using var doc = new GH_Document();
                var c = BridgeSegmentExcelTests.Add(doc, "BridgeSegmentSectionsComponent");
                foreach (var (k, v) in new (int, object)[] { (0, geometry), (1, p) }) { var t = new GH_Structure<IGH_Goo>(); t.Append(Goo(v), new GH_Path(0)); SetInput(c.Params.Input[k], t, g => g); }
                BridgeSegmentExcelTests.Solve(doc, true);
                return c.RuntimeMessages(GH_RuntimeMessageLevel.Error).Count == 0 && c.Params.Output[1].VolatileDataCount > 0;
            }
            Check(Accepted(declared) && !Accepted(undeclared), "A10: Segment Sections accepts the generic material declared Steel, refuses it undeclared (as 0.3.0 by the kind of the value)");
        }
        count++;
        // A04 (B01): a table on a membrane or a shear panel: by the GPC datum alone, the error with the reason and no output.
        foreach (var type in new[] { 4, 6 })
        {
            using var doc = new GH_Document();
            var c = BridgeSegmentExcelTests.Add(doc, "PlatePropertyComponent");
            foreach (var (k, v) in new (int, object)[] { (0, "Table plate"), (1, type), (2, 20.0), (3, 20.0), (4, GpcOnly(TableMaterial(), kinds)) }) { var t = new GH_Structure<IGH_Goo>(); t.Append(Goo(v), new GH_Path(0)); SetInput(c.Params.Input[k], t, g => g); }
            BridgeSegmentExcelTests.Solve(doc, true);
            var errors = c.RuntimeMessages(GH_RuntimeMessageLevel.Error);
            Check(errors.Any(e => e.Contains(BridGHe.Common.Gpc.Legacy.LegacyModelImport.PlateTableRefused)) && !c.Params.Output[0].VolatileData.AllData(true).Any(g => g != null),
                $"A04: a table on plate type {type} by the GPC datum alone: the error with the reason, no output ({string.Join(" / ", errors)})");
        }
        count++;
        Console.WriteLine("GPC-ONLY properties (parity PAR): 4 in-memory checks (cutoff bar with its inputs, stale state after Beam Override, declared material kind, table on a membrane and a shear panel)");
    }

    #endregion

    /// <summary>P2Z: the Straus7 section components, replayed only by <see cref="NativeSections"/>.</summary>
    internal static readonly string[] NativeTypes =
    {
        "StrausCircularSolidSectionComponent", "StrausCircularHollowSectionComponent", "StrausRectangleSolidSectionComponent", "StrausRectangleHollowSectionComponent",
        "StrausLipChannelSectionComponent", "StrausTopHatSectionComponent", "StrausISectionComponent", "StrausTSectionComponent", "StrausAngleSectionComponent",
        "StrausZSectionComponent", "StrausTrapezoidSolidSectionComponent", "StrausTrapezoidHollowSectionComponent", "StrausTriangleSolidSectionComponent",
        "StrausTriangleHollowSectionComponent", "StrausCruciformSectionComponent", "StrausBglRectangleSectionComponent", "StrausBglISectionComponent",
        "StrausBglChannelSectionComponent", "StrausBglTSectionComponent", "StrausBglAngleSectionComponent", "StrausBglBulbSectionComponent",
        "StrausLibrarySectionComponent",
    };

    /// <summary>P2Z (coordinator, 8/11: «in-memory cases ... so they repeat in every gate»): the four parameters with modules, in memory. Each
    /// parameter is solved twice, with its stored data of today (FEM-backed) and with the same data by its GPC datum alone (the switch of the
    /// property producers on), and must give the same data modulo GUIDs and no FEM value: Material (stored), Group (the text cast by names),
    /// Beam Section (the units menu), Load Combinations (the update of its cases by Staged Construction and Build Model).</summary>
    private static void ParameterCases(ISet<string> kinds, IDictionary<string, (int Instances, int Items)> byType, List<string> failures, ref int count)
    {
        var steel = Produce("MaterialSteelComponent", new Dictionary<int, object> { [0] = "S355", [1] = 2 });
        var section = Produce("BeamSectionIComponent", new Dictionary<int, object> { [0] = 300.0, [1] = 250.0, [2] = 800.0, [3] = 20.0, [4] = 16.0, [5] = 12.0 });
        var lc = Produce("LoadCaseComponent", new Dictionary<int, object> { [0] = "Replay", [1] = false });
        var lc2 = Produce("LoadCaseComponent", new Dictionary<int, object> { [0] = "Traffic", [1] = false });
        var lc3 = Produce("LoadCaseComponent", new Dictionary<int, object> { [0] = "Wind", [1] = false });
        var combo = Produce("LoadCombinationComponent", new Dictionary<int, object> { [0] = "ULS", [1] = new object[] { lc, lc2 }, [2] = new object[] { 1.35, 1.5 } });
        var cases = new[] { lc, lc3 };
        // L2C: the sections of the menus of lane 2c.
        var channel = Produce("BeamSectionCComponent", new Dictionary<int, object> { [0] = 90.0, [1] = 12.0, [2] = 260.0, [3] = 9.0 });
        var doubleC = Produce("BeamSectionCComponent", new Dictionary<int, object> { [0] = 90.0, [1] = 12.0, [2] = 260.0, [3] = 9.0, [4] = true, [5] = 10.0 });
        var generic = Produce("BeamSectionGenericComponent", new Dictionary<int, object> { [0] = 9000.0, [1] = 1.2e8, [2] = 4e7, [3] = 5e5, [8] = 8e5, [9] = 4e5, [10] = 20.0 });
        var brep = Produce("BeamSectionBrepComponent", new Dictionary<int, object> { [0] = Rhino.Geometry.Brep.CreatePlanarBreps(
            new Rhino.Geometry.Rectangle3d(Rhino.Geometry.Plane.WorldXY, 200, 400).ToNurbsCurve(), 1e-6).Single() });
        var filleted = Produce("BeamSectionIComponent", new Dictionary<int, object> { [0] = 300.0, [1] = 250.0, [2] = 800.0, [3] = 20.0, [4] = 16.0, [5] = 12.0, [6] = 15.0 });
        static void Units(IGH_Param p) => p.GetType().GetMethod("OnUnitsClick", BindingFlags.NonPublic | BindingFlags.Instance)
            .Invoke(p, new object[] { new System.Windows.Forms.ToolStripMenuItem("Knm"), EventArgs.Empty });
        var femCases = cases.Select(c => (LoadCaseModel)Prop(c, "Value")).ToList();
        var records = cases.Select(c => (BridGHe.Common.Gpc.Legacy.LegacyCaseData)Prop(c, "Gpc")).ToList();
        var parameters = new (string Type, string Label, IGH_Goo Stored, string Text, Action<IGH_Param> Act)[]
        {
            ("MaterialParam", "stored material", steel, null, null),
            ("GroupParam", "names cast from a text", null, "Deck::Pour1", null),
            ("BeamSectionParam", "units menu (Nmm to Knm)", section, null, p => p.GetType().GetMethod("OnUnitsClick", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(p, new object[] { new System.Windows.Forms.ToolStripMenuItem("Knm"), EventArgs.Empty })),
            ("LoadCombinationsParam", "cases updated (Traffic removed, Wind added)", combo, null, p => p.GetType().GetMethod("UpdateLoadCases").Invoke(p, new object[] { femCases, records })),
            // L2B: the coordinate system of the loads, stored (the global one of today) and set by the «Global» menu.
            ("CoordinateSystemParam", "stored global system", (IGH_Goo)Activator.CreateInstance(T("Fem.GH_CoordinateSystem")), null, null),
            ("CoordinateSystemParam", "the Global menu", null, null, p => p.GetType().GetMethod("SetGlobal", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(p, null)),
            // L2C (lane 2c, as in 0.3.0): the mirror and gap menus of Beam Section on a section given by its recipe alone, and the units menu on
            // the data lane 2c adds to the recipe (the section moduli, the shapes of a Brep section, the gap of a double channel).
            ("BeamSectionParam", "mirror menu on a channel", channel, null, p => p.GetType().GetMethod("OnSetMirror", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(p, new object[] { null, EventArgs.Empty })),
            ("BeamSectionParam", "mirror menu on a double channel", doubleC, null, p => p.GetType().GetMethod("OnSetMirror", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(p, new object[] { null, EventArgs.Empty })),
            ("BeamSectionParam", "gap menu on a double channel", doubleC, null, p => p.GetType().GetMethod("OnGapKeyDown", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(p,
                new object[] { new Grasshopper.GUI.GH_MenuTextBox(new System.Windows.Forms.ToolStripDropDown(), "25", false), new System.Windows.Forms.KeyEventArgs(System.Windows.Forms.Keys.Enter) })),
            ("BeamSectionParam", "units menu on a double channel (Nmm to Knm)", doubleC, null, Units),
            ("BeamSectionParam", "units menu on a Generic section (Nmm to Knm)", generic, null, Units),
            ("BeamSectionParam", "units menu on a Brep section (Nmm to Knm)", brep, null, Units),
            ("BeamSectionParam", "units menu on an I section with fillet radius (Nmm to Knm)", filleted, null, Units),
        };
        foreach (var (type, label, stored, text, act) in parameters.Where(p => Selected(p.Type)))
        {
            string where = "in memory: parameter " + type + " (" + label + ")";
            Run Once(bool gpcOnly)
            {
                using var doc = new GH_Document();
                var param = (IGH_Param)Activator.CreateInstance(plugin.GetTypes().Single(t => t.Name == type));
                param.CreateAttributes(); doc.AddObject(param, false);
                if (text != null)
                {
                    var source = new Grasshopper.Kernel.Parameters.Param_String(); source.CreateAttributes(); doc.AddObject(source, false);
                    source.PersistentData.Append(new GH_String(text)); param.AddSource(source);
                }
                if (stored != null)
                {
                    var tree = new GH_Structure<IGH_Goo>(); tree.Append(gpcOnly ? GpcOnly(stored, kinds) : stored.Duplicate(), new GH_Path(0));
                    SetInput(param, tree, g => g);
                }
                if (gpcOnly) generators.SetValue(null, true);
                try
                {
                    doc.Enabled = true; doc.NewSolution(false, GH_SolutionMode.Silent);
                    if (act != null) { act(param); doc.NewSolution(false, GH_SolutionMode.Silent); }
                }
                finally { if (gpcOnly) generators.SetValue(null, false); }
                var run = new Run();
                foreach (var level in new[] { GH_RuntimeMessageLevel.Error, GH_RuntimeMessageLevel.Warning, GH_RuntimeMessageLevel.Remark })
                    foreach (var m in param.RuntimeMessages(level)) run.Messages.Add(level + ": " + m);
                foreach (var item in param.VolatileData.AllData(true))
                {
                    run.Outputs.Add(Text(item));
                    var datum = item == null ? null : Prop(item, "Gpc");
                    run.Raw.Add(datum == null ? run.Outputs[^1] : item.GetType().Name + " " + GpcNullValueChecks.TextOf(datum));
                    if (item != null && kinds.Contains(item.GetType().Name) && HasFem(item)) run.FemOutputs++;
                }
                return run;
            }
            Run fem, gpc, again;
            try { fem = Once(false); gpc = Once(true); again = Once(true); }
            catch (Exception ex) { failures.Add(where + ": " + ex.GetBaseException().Message); continue; }
            var (n, items) = byType.TryGetValue(type, out var v) ? v : (0, 0);
            byType[type] = (n + 1, items + fem.Outputs.Count);
            if (fem.Outputs.Count == 0) failures.Add(where + ": no data with the inputs of today");
            if (!fem.Messages.SequenceEqual(gpc.Messages)) failures.Add(where + ": messages " + string.Join(" / ", fem.Messages) + " -> " + string.Join(" / ", gpc.Messages));
            if (!fem.Outputs.SequenceEqual(gpc.Outputs))
            {
                var first = fem.Outputs.Zip(gpc.Outputs).FirstOrDefault(p => p.First != p.Second);
                failures.Add($"{where}: data {fem.Outputs.Count} -> {gpc.Outputs.Count}; first (FEM-backed -> GPC-only):" + Environment.NewLine + Differences(first.First ?? "<none>", first.Second ?? "<none>"));
            }
            if (gpc.FemOutputs != 0) failures.Add($"{where}: {gpc.FemOutputs} data with a FEM value from GPC-only data");
            if (!gpc.Raw.SequenceEqual(again.Raw)) failures.Add($"{where}: two GPC-only copies give other GUIDs");
            count++;
        }
    }

    /// <summary>P2Z (lane 2a): the native part of <c>gpc-only-properties</c>, run by the native host (<c>--core-only --native</c>): the
    /// Straus7 section components replayed in memory (<see cref="NativeSectionCases"/>), Straus7 computing the sections on both paths. The
    /// number of differences (0: the same outputs modulo GUIDs and messages, no FEM value, deterministic).</summary>
    internal static int NativeSections()
    {
        Init("properties");
        var kinds = new HashSet<string>(PropertyGoo2a, StringComparer.Ordinal);
        var byType = new SortedDictionary<string, (int Instances, int Items)>(StringComparer.Ordinal);
        var failures = new List<string>();
        InMemory("native", kinds, byType, failures, out int cases);
        foreach (var t in NativeTypes.Where(t => !byType.ContainsKey(t))) failures.Add("no case for " + t);
        foreach (var f in failures.Take(60)) Console.WriteLine("  " + f);
        Console.WriteLine($"GPC-ONLY properties (native): {cases} in-memory cases of {byType.Count} Straus7 section components replayed with the switch of the property producers, {failures.Count} differences");
        return failures.Count;
    }

    /// <summary>The plug-in and the switch of the lane (<c>GpcOnlyElements.GeneratorsWithoutFem</c> or <c>PropertiesWithoutFem</c>).</summary>
    private static void Init(string lane)
    {
        plugin = BridgeMergeTests.Initialize();
        generatorsLane = lane == "generators"; propertiesLane = lane == "properties";
        string switchName = propertiesLane ? "PropertiesWithoutFem" : "GeneratorsWithoutFem";
        generators = plugin.GetType("BridGHe.Grasshopper.Helpers.GpcOnlyElements", true).GetProperty(switchName, BindingFlags.Public | BindingFlags.Static)
            ?? throw new Exception("GPC-ONLY REPLAY: GpcOnlyElements." + switchName + " not found");
    }

    /// <summary>P2Z: the parameters of lane 2 replayed by <see cref="ParameterCases"/> (in memory only; L2B: and the coordinate system of
    /// the loads).</summary>
    internal static readonly string[] Lane2aParameters = { "MaterialParam", "GroupParam", "BeamSectionParam", "LoadCombinationsParam", "CoordinateSystemParam" };

    private static void InMemory(string lane, ISet<string> kinds, IDictionary<string, (int Instances, int Items)> byType, List<string> failures, out int count)
    {
        count = 0;
        // P2Z (lane 2a): the cases of lanes 1a and 1b of the lane-2a types with an input of the lane-2a kinds (a load case, a property), and
        // the cases of lane 2a; the Straus7 sections only under the native host (NativeSections).
        // L2B: the same for the types of lane 2b, and the cases of lane 2b.
        bool OfLane2((string Type, string Label, Dictionary<int, object> Inputs) c) => Lane2Types.Contains(c.Type) && !NativeTypes.Contains(c.Type)
            && c.Inputs.Values.SelectMany(v => v is object[] l ? l : new[] { v }).Any(v => v is IGH_Goo g && kinds.Contains(g.GetType().Name));
        // L2C: and the cases of lane 2c.
        // PAR: and the cases of the 0.3.0 parity (ParityCases; the T, C and angle properties under the native host, ParityNativeCases).
        var all = lane == "native" ? NativeSectionCases().Concat(ParityNativeCases()).ToList() : lane == "properties" ? Lane1Cases().Concat(Lane1bCases()).Where(OfLane2).Concat(Lane2aCases()).Concat(Lane2bCases()).Concat(Lane2cCases()).Concat(ParityCases()).ToList()
            : lane == "generators" ? Lane1bCases() : Lane1Cases();
        foreach (var (type, label, inputs) in all.Where(c => Selected(c.Type)))
        {
            using var doc = new GH_Document();
            var c = BridgeSegmentExcelTests.Add(doc, type);
            foreach (var input in inputs)
            {
                var tree = new GH_Structure<IGH_Goo>();
                var path = new GH_Path(0);
                foreach (var v in input.Value is object[] list ? list : new[] { input.Value }) tree.Append(Goo(v), path);
                SetInput(c.Params.Input[input.Key], tree, g => g);
            }
            BridgeSegmentExcelTests.Solve(doc, true);
            Replay(c, "in memory: " + c.Name + " (" + label + ")", kinds, byType, failures);
            count++;
        }
    }

    /// <summary>L2B: the component types named by <c>BRIDGHE_GPC_ONLY_TYPES</c> (a comma list; diagnosis of a lane in progress only: the
    /// regression replays every type), or all.</summary>
    private static HashSet<string> selected;
    private static bool Selected(string type) => selected == null || selected.Contains(type);

    internal static int Regression(string lane)
    {
        // L2B: lane 2 replays 2a and 2b together (no type declared pending).
        var types = lane switch { "elements" => Lane1Types, "generators" => Lane1bTypes, "properties" => Lane2Types, _ => throw new ArgumentException("Unknown lane: " + lane) };
        var kinds = new HashSet<string>(lane switch { "elements" or "generators" => ElementGoo, "properties" => PropertyGoo, _ => Array.Empty<string>() }, StringComparer.Ordinal);
        selected = Environment.GetEnvironmentVariable("BRIDGHE_GPC_ONLY_TYPES") is { Length: > 0 } named ? new HashSet<string>(named.Split(','), StringComparer.Ordinal) : null;
        types = types.Where(Selected).ToArray();
        // The Grasshopper libraries are parsed only when they are not registered yet (P2W, 8/11): in regression all (p2w1a2) the parse again of
        // BridgeExampleMigration.Initialize, in gpc-only-generators right after gpc-only-elements, threw from Grasshopper's own upgrader
        // Upgrade_PolygonCenterComponent2.Version of CurveComponents.gha (new DateTime(2013, 23, 9, 10, 45, 0)). Registered (an earlier group
        // parsed them in this process): the solutions are only disabled again before reading the definitions, as Initialize does.
        // P2X: the server is read from its field, never through Instances.ComponentServer: in a process of its own (--gpc-only) that property
        // creates Grasshopper's default server, which loads the installed plug-ins (BridGHe of the main checkout among them) before the one under
        // test, and the load of the plug-in under test then failed («Assembly with same name is already loaded»).
        var registered = typeof(Grasshopper.Instances).GetField("m_comServer", BindingFlags.NonPublic | BindingFlags.Static)?.GetValue(null) as GH_ComponentServer;
        if (registered != null && registered.ObjectProxies.Any(p => p.Location?.EndsWith("CurveComponents.gha", StringComparison.OrdinalIgnoreCase) == true))
            GH_Document.EnableSolutions = false;
        else BridgeExampleMigration.Initialize();
        Init(lane);
        var producers = new HashSet<string>(propertiesLane ? Lane2Producers : Array.Empty<string>(), StringComparer.Ordinal);
        var byType = new SortedDictionary<string, (int Instances, int Items)>(StringComparer.Ordinal);
        var failures = new List<string>();
        int files = 0;
        // A substring of the definitions to replay (diagnosis only; the regression replays them all).
        string only = Environment.GetEnvironmentVariable("BRIDGHE_GPC_ONLY_FILES");
        foreach (var file in BuildModelScan.ScopeFiles().Where(f => string.IsNullOrEmpty(only) || f.Contains(only, StringComparison.OrdinalIgnoreCase)))
        {
            string label = Path.GetRelativePath(ExamplePaths.Root, file);
            using var doc = GrasshopperBridgeExample.Read(file); doc.FilePath = file;
            // No analysis runs (the Run button is never pressed: the definitions may name outputs in the main checkout).
            foreach (var o in doc.Objects.OfType<IGH_ActiveObject>())
                if (o.GetType().Name is "StrausR3AnalysisComponent" or "MovingStaticAnalysisComponent") o.Locked = true;
            BridgeSegmentExcelTests.Solve(doc, true);
            files++;
            foreach (var c in doc.Objects.OfType<GH_Component>().Where(c => types.Contains(c.GetType().Name)).ToArray())
            {
                bool converted = c.Params.Input.Any(p => p.VolatileData.AllData(true).Any(g => g != null && (kinds.Contains(g.GetType().Name) || !ReferenceEquals(GpcOnly(g, kinds), g))));
                // Lane 1b: every instance with data (a generator has no element input; its GPC-only copy runs with the switch on).
                // Lane 2: the same for the producers of lane 2 (a material or a section has no input of the lane-2 kinds).
                bool producer = generatorsLane || producers.Contains(c.GetType().Name);
                if (!converted && !(producer && c.Params.Input.Any(p => !p.VolatileData.IsEmpty))) continue;
                Replay(c, $"{label}: {c.Name} [{c.NickName}]", kinds, byType, failures);
            }
        }
        var fromExamples = new HashSet<string>(byType.Keys, StringComparer.Ordinal);
        int cases = 0;
        if (lane is "elements" or "generators" or "properties") InMemory(lane, kinds, byType, failures, out cases);
        if (lane == "properties") ParameterCases(kinds, byType, failures, ref cases);
        // L2C: the checks of the review of lane 2 (R65-C2).
        if (lane == "properties" && (selected == null || selected.Contains("Review"))) ReviewChecks(kinds, failures, ref cases);
        // PAR: the checks of the 0.3.0 parity that need more than a list of inputs.
        if (lane == "properties" && (selected == null || selected.Contains("Parity"))) ParityChecks(kinds, failures, ref cases);
        foreach (var t in types.Where(t => !fromExamples.Contains(t))) Console.WriteLine("GPC-ONLY " + lane + ": not met in the examples (in-memory cases only): " + t);
        foreach (var t in types.Where(t => !byType.ContainsKey(t) && !(lane == "properties" && NativeTypes.Contains(t)))) failures.Add("no case for " + t);
        if (lane == "properties") foreach (var t in Lane2aParameters.Where(t => Selected(t) && !byType.ContainsKey(t))) failures.Add("no case for " + t);
        if (lane == "properties") foreach (var t in NativeTypes.Where(Selected)) Console.WriteLine("GPC-ONLY properties: Straus7 section, in-memory cases under the native host only (core-native): " + t);
        foreach (var p in byType) Console.WriteLine($"GPC-ONLY {lane}: {p.Key} {p.Value.Instances} instances, {p.Value.Items} output lines");
        foreach (var f in failures.Take(60)) Console.WriteLine("  " + f);
        // P2Y: every failure in a file named by BRIDGHE_GPC_ONLY_REPORT (diagnosis of a lane in progress; the console keeps the first 60).
        if (Environment.GetEnvironmentVariable("BRIDGHE_GPC_ONLY_REPORT") is { Length: > 0 } report) File.WriteAllLines(report, failures);
        if (failures.Count != 0) throw new Exception($"GPC-ONLY {lane} FAIL: {failures.Count} differences or failures (first: {failures[0]})");
        Console.WriteLine($"GPC-ONLY {lane} PASS: {files} definitions and {cases} in-memory cases, {byType.Sum(p => p.Value.Instances)} instances of {byType.Count} components " +
            "replayed with GPC-only inputs: the same outputs modulo GUIDs and the same messages, no FEM value, deterministic identities");
        return 0;
    }

    /// <summary>The regression of lane 1 (<c>gpc-only-elements</c>).</summary>
    internal static void Elements() => Regression("elements");

    /// <summary>The regression of lane 1b (<c>gpc-only-generators</c>).</summary>
    internal static void Generators() => Regression("generators");

    /// <summary>The regression of lane 2 (<c>gpc-only-properties</c>; P2Z: lane 2a, in <c>regression all</c>; lane 2b declared pending).</summary>
    internal static void Properties() => Regression("properties");

    /// <summary>P2Z: where an exception of a replay comes from (the first frames of its base exception), for the report.</summary>
    private static string Frame(Exception ex) => " [" + string.Join(" < ", (ex.GetBaseException().StackTrace ?? "").Split('\n').Take(4).Select(l => l.Trim())) + "]";

    /// <summary>The first differing lines of two texts (at most six), with their numbers.</summary>
    private static string Differences(string a, string b)
    {
        var x = a.Split('\n'); var y = b.Split('\n');
        var lines = new List<string>();
        for (int i = 0; i < Math.Max(x.Length, y.Length) && lines.Count < 6; i++)
        {
            string p = i < x.Length ? x[i] : "<end>", q = i < y.Length ? y[i] : "<end>";
            if (p != q) lines.Add($"  line {i + 1}" + Environment.NewLine + "  before " + Short(p) + Environment.NewLine + "  after  " + Short(q));
        }
        return lines.Count == 0 ? "  (same lines)" : string.Join(Environment.NewLine, lines);
    }

    private static string Short(string s) => s == null ? "null" : s.Length <= 240 ? s : s.Substring(0, 240) + "…";
}
#endif
