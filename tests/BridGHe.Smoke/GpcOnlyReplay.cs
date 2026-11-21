using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
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
/// <para><b>R6.6 lane R1 (11/11): the oracles without the FEM model.</b> Every replay is first the GPC-only copy, solved twice: its outputs carry
/// no FEM value and a second copy gives the same GUIDs (independent of any FEM value). Then:</para>
/// <list type="bullet">
/// <item><b>Definitions</b>: the R6.0 capture <c>scan</c> mapped one to one (<see cref="R1ScanCapture"/>): every GPC-only output item that the
/// definition wires into a Build Model input (a single source, through relay parameters, the same tree) is the captured draft at that location
/// (file, Build Model, input, path, item); every Build Model output is the captured conversion; every material, section, beam and plate property,
/// case, combination and composite section is one of the captured contents of its file. Equal, equal but for GUIDs, or declared by the rules of
/// the capture.</item>
/// <item><b>In-memory cases</b>: their inputs built without any FEM value (<see cref="gpcInputs"/>: the GPC examples of R0
/// <see cref="GpcNativeExamples"/>, the GPC fixtures of R2a <see cref="R2aDesignFixtures"/>, the producer components solved with the switches of
/// P2 on, the product builders), and the explicit expectation each case declares by its label (<see cref="Expect"/>: an error and no output for
/// a refusal, a warning, otherwise no error and an output), with the explicit values of the checks (ReviewChecks, ParityChecks, ModelsChecks,
/// Lane3cChecks, Lane3eChecks).</item>
/// <item><b>TRANSITIONAL</b> (until P2, P2 decides; <c>BRIDGHE_R1_TRANSITIONAL=0</c> turns it off to see what stays): the copy with the
/// FEM-backed inputs of today, the same messages and outputs modulo GUIDs, as before R1 (<see cref="Transitional"/>).</item>
/// </list>
/// </summary>
internal static class GpcOnlyReplay
{
    /// <summary>R1: the transitional comparison with the copy of the FEM-backed inputs (on unless <c>BRIDGHE_R1_TRANSITIONAL=0</c>).</summary>
    /// <summary>P2R (T3, 12/11): the FEM-backed copy is retired; its oracle is the replay capture of the base of P2 (P2rCapture.Replays). Kept false for the
    /// native rehearsal only (its reported differences against the definition).</summary>
    private static bool Transitional => false;

    /// <summary>R1: the counts of the oracles of a regression (the PASS line).</summary>
    private sealed class Oracles
    {
        internal int Transitional, Expected, Mapped, Located, Contents, Converted, Unmapped, GpcBuilt;
        internal readonly SortedDictionary<string, int> Outcomes = new SortedDictionary<string, int>(StringComparer.Ordinal);
        internal void Count(string what) => Outcomes[what] = Outcomes.GetValueOrDefault(what) + 1;
        internal string Summary => $"{Expected} in-memory instances against their declared expectation ({GpcBuilt} with inputs built without FEM), {Mapped} definition instances "
            + $"mapped to the R6.0 capture scan ({Located} items at Build Model inputs, {Contents} data by content, {Converted} Build Model outputs; "
            + string.Join(", ", Outcomes.Select(p => p.Key + " " + p.Value)) + $"; {Unmapped} definition instances with no output in the capture), "
            + $"; {Transitional} instances against the output of a definition (native rehearsal)";
    }
    private static Oracles oracles = new Oracles();

    /// <summary>P2R step 0 (U3): the file of the replay capture of the regression running (<c>gpc-only-&lt;lane&gt;</c>), null outside a full
    /// regression (a subset of types or files, the dry chain, the native rehearsal).</summary>
    private static string captureLane;

    /// <summary>P2R step 0: the captured value of a GPC-only replay: the number of output lines, then the short SHA-256 of its messages, of its
    /// outputs modulo GUIDs (the texts the FEM-backed copy was compared with) and of its raw outputs (the GPC data with their identities).</summary>
    private static string CaptureValue(Run r) => r.Outputs.Count + " "
        // P2R: the warning of the plug-in build check is environmental. P2CG (13/11/2026, review of ChatGPT tasks 53 CF-02 and 64 RF-01): only that
        // warning is left out of the digest, marked «*» before it, and the other messages are compared (P2rCapture.Declared); before, any such
        // warning made the whole field «*», not compared.
        + (r.Messages.Any(P2rCapture.Environmental) ? "*" : "") + P2rCapture.Sha(string.Join("\n", r.Messages.Where(m => !P2rCapture.Environmental(m))))
        + " " + P2rCapture.Sha(string.Join("\n", r.Outputs.Select(CaptureOutput))) + " " + P2rCapture.Sha(string.Join("\n", r.Raw.Select(CaptureOutput)));

    // Only this lane redirects its Excel/report File outputs to the owned temporary directory. Keep messages, filenames,
    // output values and all other paths unchanged; the historical capture remains byte-for-byte unchanged.
    private static string CaptureOutput(string line) => modelsLane ? ReplayTemporaryPaths.Normalize(line, Path.GetTempPath()) : line;

    /// <summary>R1: the label of the definition being replayed (the file key of the capture), null for the in-memory cases.</summary>
    private static string definition;
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
        "BeamPropertyCompositeComponent", "BeamSectionConcreteSlabComponent", "BeamSectionConcreteSlabBarsComponent", "BeamSectionHPropertyComponent",
        "CompositeSectionGeometricPropertyComponent",
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
        "BeamPropertyCompositeComponent", "BeamSectionConcreteSlabComponent", "BeamSectionConcreteSlabBarsComponent", "BeamSectionHPropertyComponent",
        "CompositeSectionGeometricPropertyComponent",
        "CreepAndShrinkageConcreteComponent",
        "BridgeSegmentSectionsComponent", "BridgeSegmentExcelComponent", "BridgeSectionTemplateComponent", "BridgeSectionReadComponent",
        "BridgeSegmentCompositeComponent", "BridgeStateOverrideComponent",
        "BridgeFemMembersComponent", "BridgeFamilyPropertyComponent", "BridgeSectionScheduleComponent",
    };

    /// <summary>P2Z: lane 2a, the types of <c>gpc-only-properties</c>: lane 2 without <see cref="Lane2bTypes"/>.</summary>
    internal static readonly string[] Lane2aTypes = Lane2Types.Where(t => !Lane2bTypes.Contains(t)).ToArray();

    /// <summary>The GH types of properties and data converted to their GPC data alone in lane 2. L2B (lane 2b): also the coordinate system of
    /// the loads (coordinator, 8/11) and the bridge rules (<c>BridgeDesignGoo</c>: a section rule or a family property whose beam property
    /// is given by its GPC datum alone).</summary>
    internal static readonly string[] PropertyGoo =
    {
        "GH_BeamSection", "GH_BeamProperty", "GH_PlateProperty", "GH_Material", "GH_LoadCase", "GH_LoadCombination", "GH_StagedConstruction", "GH_Group",
        "GH_CompositeBeamProperty", "GH_CoordinateSystem", "BridgeDesignGoo",
    };

    /// <summary>P2Z: the kinds of lane 2a (<see cref="PropertyGoo"/> without the kinds of lane 2b).</summary>
    internal static readonly string[] PropertyGoo2a = PropertyGoo.Where(k => k is not ("GH_CompositeBeamProperty" or "GH_CoordinateSystem" or "BridgeDesignGoo")).ToArray();

    private static object Prop(object o, string name) => o?.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance)?.GetValue(o);

    /// <summary>A GH type with a FEM value. P2Y: <c>GH_BeamSection.ScriptVariable</c> is always null, so a section is read by its value.
    /// P2CC (13/11/2026): a bridge rule and a workflow object carry no FEM property or source on the path of 0.4.0 (their script variable is the
    /// rule or the definition itself): never FEM-backed.</summary>
    private static bool HasFem(IGH_Goo goo) => goo.GetType().Name is "BridgeDesignGoo" or "BridgeWorkflowGoo" ? false
        : goo.ScriptVariable() != null || (goo.GetType().Name == "GH_BeamSection" && Prop(goo, "Value") != null);

    /// <summary>L2B: the beam property of a bridge rule (a section rule or a family property) by its GPC datum (P2CC: the FEM property, never given on
    /// the path of 0.4.0, is not read).</summary>
    private static BridGHe.Common.Gpc.Legacy.LegacyBeamProperty DesignGpcProperty(IGH_Goo goo) => Prop(goo, "Value") switch
    {
        BridGHe.Common.Geometry.BridgeSectionRule r => r.GpcProperty,
        BridGHe.Common.Geometry.BridgeFamilyProperty f => f.GpcProperty,
        _ => null,
    };

    /// <summary>The GH type made from the GPC datum alone of a FEM-backed one, or the same goo (another kind, or no datum).</summary>
    private static IGH_Goo GpcOnly(IGH_Goo goo, ISet<string> kinds)
    {
        // P2CC (13/11/2026): no moment state has a FEM source and no bridge rule or workflow object a FEM value on the path of 0.4.0: their former
        // mappings (DesignGpcOnly, WorkflowOnly) are gone.
        if (goo == null || !kinds.Contains(goo.GetType().Name) || !HasFem(goo)) return goo;
        // L3a (section «models»): a model by its GPC input and state.
        if (goo.GetType().Name == "GH_Model") return ModelOnly(goo);
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
    internal static string Text(IGH_Goo goo)
    {
        // PAR (A08): the texts of the replay compare a stale section state as stale (see GpcDraftArchiveChecks.CompareStaleAsStale).
        bool before = GpcDraftArchiveChecks.CompareStaleAsStale;
        GpcDraftArchiveChecks.CompareStaleAsStale = true;
        try { return TextOf(goo); }
        finally { GpcDraftArchiveChecks.CompareStaleAsStale = before; }
    }

    private static string TextOf(IGH_Goo goo)
    {
        if (goo == null) return "null";
        if (ModelsText(goo, false) is { } models) return models; // L3a: the model kinds and the section results
        // L2B: a bridge rule by its data and the GPC datum of its beam property (of its FEM value, or given alone).
        if (goo.GetType().Name == "BridgeDesignGoo" && DesignGpcProperty(goo) is { } designProperty)
        {
            string rule = Prop(goo, "Value") switch
            {
                BridGHe.Common.Geometry.BridgeSectionRule r => FormattableString.Invariant($"rule {r.Family} {r.Group} {r.Segment} {r.Start:R} {r.End:R} {r.ScaleFrom.X:R} {r.ScaleFrom.Y:R} {r.ScaleTo.X:R} {r.ScaleTo.Y:R}"),
                BridGHe.Common.Geometry.BridgeFamilyProperty f => "family " + f.Family,
                _ => "",
            };
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
        public readonly List<IGH_Goo> Goos = new List<IGH_Goo>();
        internal List<string> Outputs = new List<string>();
        /// <summary>The same texts with their GUIDs (the determinism of the identities derived without a FEM value).</summary>
        internal List<string> Raw = new List<string>();
        internal List<string> Messages = new List<string>();
        internal int FemOutputs;
        internal bool Unconverted; // L3b
        /// <summary>R1: every output item by output index, path and index in its branch (the locations of the capture).</summary>
        internal readonly List<(int Output, string Path, int Index, IGH_Goo Goo)> Items = new List<(int, string, int, IGH_Goo)>();
        /// <summary>R1: the number of items and of branches of every output (the structure of the outputs).</summary>
        internal readonly List<(int Branches, int Items)> Shape = new List<(int, int)>();
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
        bool on = gpcOnly && (generatorsLane || propertiesLane || modelsLane && ModelsProducers.Contains(original.GetType().Name)); // L3c: Node
        try
        {
            var copy = Copy(original, doc);
            int unconvertedBefore = unconverted; // L3b
            // L3a: the inputs converted in the GPC-only copies of the section «models», by kind (reported).
            Func<IGH_Goo, IGH_Goo> mapped = modelsLane && gpcOnly ? g => { var r = map(g); if (g != null && !ReferenceEquals(r, g)) convertedKinds[g.GetType().Name] = convertedKinds.GetValueOrDefault(g.GetType().Name) + 1; return r; } : map;
            for (int i = 0; i < original.Params.Input.Count; i++)
                SetInput(copy.Params.Input[i], original.Params.Input[i].VolatileData, mapped);
            if (modelsLane) PrepareModels(copy, original); // L3a: buttons pressed, output paths in TEMP
            if (on) generators.SetValue(null, true);
            try { doc.Enabled = true; doc.NewSolution(false, GH_SolutionMode.Silent); }
            finally { if (on) generators.SetValue(null, false); }
            var run = new Run { Unconverted = unconverted != unconvertedBefore };
            Collect(copy, run, kinds);
            return run;
        }
        finally { doc.Dispose(); }
    }

    /// <summary>The messages and the outputs of a solved component (L3e: also of the component of the definition, for the rehearsal).</summary>
    private static void Collect(GH_Component copy, Run run, ISet<string> kinds)
    {
        foreach (var level in new[] { GH_RuntimeMessageLevel.Error, GH_RuntimeMessageLevel.Warning, GH_RuntimeMessageLevel.Remark })
            foreach (var m in copy.RuntimeMessages(level)) run.Messages.Add(level + ": " + m);
        for (int o = 0; o < copy.Params.Output.Count; o++)
        {
            var p = copy.Params.Output[o];
            var data = p.VolatileData;
            run.Shape.Add((data.PathCount, data.DataCount));
            for (int b = 0; b < data.PathCount; b++)
            {
                var branch = data.get_Branch(b);
                run.Outputs.Add($"{p.Name} {data.get_Path(b)} {branch.Count}"); run.Raw.Add(run.Outputs[^1]);
                int index = 0;
                foreach (var item in branch)
                {
                    var goo = item as IGH_Goo;
                    run.Items.Add((o, data.get_Path(b).ToString(), index++, goo));
                    // L3a (declared rule): the ID of a report is new at every generation (BridgeReportData.Create): masked.
                    if (modelsLane && p.Name == "Report ID" && copy.GetType().Name == "BridgeReportComponent") { run.Outputs.Add("Report ID <new per generation>"); run.Raw.Add(run.Outputs[^1]); continue; }
                    run.Outputs.Add(Text(goo));
                    var datum = goo == null ? null : Prop(goo, "Gpc");
                    run.Raw.Add(ModelsText(goo, true) ?? (datum == null ? run.Outputs[^1] : goo.GetType().Name + " " + GpcNullValueChecks.TextOf(datum)));
                    if (goo != null && kinds.Contains(goo.GetType().Name) && HasFem(goo)) run.FemOutputs++;
                    if (goo != null) run.Goos.Add(goo);
                }
            }
        }
    }

    /// <summary>One replay of a component instance (R1): the GPC-only copy, twice (no FEM value in its outputs, the same GUIDs again), against the
    /// independent references (the declared expectation of an in-memory case, the R6.0 capture for a definition) and, TRANSITIONAL until P2,
    /// against the copy with the FEM-backed inputs of today (the same outputs modulo GUIDs and the same messages).</summary>
    /// <param name="reference">L3e: the outputs to compare with, instead of a FEM-backed copy (the rehearsal, Build BridGHe Model: the copies of the FEM models are not the
    /// models of the definition, so the FEM build of the copy reports members «in the FEM source only» and the host check on its summary fails).</param>
    /// <param name="expect">R1: the expectation an in-memory case declares (null for a definition).</param>
    /// <param name="gpcSource">R1: the same component with the inputs built without any FEM value (the in-memory cases); null: the GPC-only copy
    /// takes the inputs of <paramref name="c"/> by their GPC data alone (<see cref="GpcOnly"/>).</param>
    private static void Replay(GH_Component c, string where, ISet<string> kinds, IDictionary<string, (int Instances, int Items)> byType, List<string> failures, Run reference = null,
        Expect expect = null, GH_Component gpcSource = null)
    {
        Run fem = null, gpc, again;
        // R1: inputs built without FEM are checked for FEM values of every converted kind, not only those of the lane.
        var gpcKinds = gpcSource == null ? kinds : new HashSet<string>(ElementGoo.Concat(PropertyGoo).Concat(ModelGoo), StringComparer.Ordinal);
        try { gpc = Solve(gpcSource ?? c, g => GpcOnly(g, gpcKinds), gpcKinds, true); again = Solve(gpcSource ?? c, g => GpcOnly(g, gpcKinds), gpcKinds, true); }
        catch (Exception ex) { failures.Add(where + ": the copy with GPC-only inputs fails: " + ex.GetBaseException().Message + Frame(ex)); return; }
        var (n, items) = byType.TryGetValue(c.GetType().Name, out var v) ? v : (0, 0);
        byType[c.GetType().Name] = (n + 1, items + gpc.Outputs.Count);
        if (modelsLane && c.GetType().Name == "BridgeModelAuditComponent") // L3b: the coverage of the audit, reported
            foreach (var o in gpc.Outputs.SkipWhile(o => !o.StartsWith("Code ", StringComparison.Ordinal)).Skip(1).TakeWhile(o => o.StartsWith("GH_String ", StringComparison.Ordinal)))
                auditCodes[o.Substring(10)] = auditCodes.GetValueOrDefault(o.Substring(10)) + 1;
        // L3a: BRIDGHE_GPC_ONLY_TRACE=1 prints the messages of every instance (diagnosis of a lane in progress only); R1: and the shape of its outputs.
        if (Environment.GetEnvironmentVariable("BRIDGHE_GPC_ONLY_TRACE") == "1")
            Console.WriteLine("TRACE " + where + ": " + gpc.Outputs.Count + " lines; shape " + string.Join(" ", gpc.Shape.Select(s => s.Branches + "/" + s.Items)) + "; " + string.Join(" / ", gpc.Messages));
        // Independent of any FEM value: no FEM value in the outputs, deterministic identities.
        if (gpc.FemOutputs != 0 && !gpc.Unconverted) failures.Add($"{where}: {gpc.FemOutputs} outputs with a FEM value from GPC-only inputs");
        if (!gpc.Raw.SequenceEqual(again.Raw))
        {
            var first = gpc.Raw.Zip(again.Raw).FirstOrDefault(p => p.First != p.Second);
            // P2R (T3): the GH GUID of a GPC-only composite beam comes from its draft since P2K (P7, CompositeBeamComponent.cs:88,100-109): the former
            // exception of R1 (a non-deterministic composite beam GUID reported, not failing) is gone, every difference fails.
            failures.Add($"{where}: two GPC-only copies give other GUIDs (identities not deterministic); first:" + Environment.NewLine + Differences(first.First ?? "<none>", first.Second ?? "<none>"));
        }
        // P2R step 0 (U3): the GPC-only replay against the capture of the base of P2 (written once, P2rCapture.Replays).
        if (captureLane != null && reference == null) P2rCapture.Replays.Datum(captureLane, where, CaptureValue(gpc));
        // R1: the references without the FEM model.
        if (expect != null) { expect.Check(where, gpc, failures); oracles.Expected++; }
        else if (definition != null) Capture(c, where, gpc, failures);
        // TRANSITIONAL (until P2, P2 decides): the copy with the FEM-backed inputs of today.
        if (reference == null) return; // P2R (T3): the FEM-backed copy retired, the capture above is the oracle
        try { fem = reference ?? Solve(c, g => g.Duplicate(), kinds); }
        catch (Exception ex) { failures.Add(where + ": the copy with the inputs of today fails: " + ex.GetBaseException().Message + Frame(ex)); return; }
        oracles.Transitional++;
        // L3e (declared rule, identity only): against the output of the definition the bridge data of the model come in the order of the GUIDs of its beams (those of the
        // first reference model on the GPC-only path, those of the conversion of the merged FEM model in the definition): compared as a set, without the ordinals.
        var gpcOutputs = gpc.Outputs;
        if (reference != null) { fem.Outputs.Replace(BridgeLinesAsSet); gpcOutputs = gpc.Outputs.ToList(); gpcOutputs.Replace(BridgeLinesAsSet); }
        if (!fem.Messages.SequenceEqual(gpc.Messages)) failures.Add(where + ": messages " + string.Join(" / ", fem.Messages) + " -> " + string.Join(" / ", gpc.Messages));
        int diff = fem.Outputs.Zip(gpcOutputs).Count(p => p.First != p.Second);
        if (modelsLane && c.GetType().Name == "BridgeModelAuditComponent" && !fem.Outputs.SequenceEqual(gpcOutputs)) failures.Add(where + ": GPC-only outputs: " + string.Join(" | ", gpcOutputs)); // L3b: the diagnostics that differ
        if (fem.Outputs.Count != gpcOutputs.Count || diff != 0)
        {
            var first = fem.Outputs.Zip(gpcOutputs).FirstOrDefault(p => p.First != p.Second);
            failures.Add($"{where}: outputs {fem.Outputs.Count} -> {gpcOutputs.Count}, {diff} different; first (FEM-backed -> GPC-only):" + Environment.NewLine
                + Differences(first.First ?? "<none>", first.Second ?? "<none>"));
        }
    }

    // -----------------------------------------------------------------------------------------------------------------------------------------
    // R1: the R6.0 capture as the oracle of the definitions, and the expectations of the in-memory cases.

    private static R1ScanCapture scan;
    private static HashSet<string> scanFiles;

    private static bool Plain(IGH_Param p) => p.DataMapping == GH_DataMapping.None && !p.Reverse && !p.Simplify;

    /// <summary>R1: the GH types the R6.0 scan records by content.</summary>
    private static readonly HashSet<string> ContentTypes = new HashSet<string>(StringComparer.Ordinal)
    {
        "GH_Material", "GH_BeamSection", "GH_BeamProperty", "GH_PlateProperty", "GH_LoadCase", "GH_LoadCombination", "GH_StagedConstruction", "GH_CompositeBeamProperty",
    };

    /// <summary>The objects the material scan solved when it wrote the capture (the rule of <see cref="GpcMaterialKindChecks.Scan"/>): Build Model, the
    /// components with an input of materials, composite, beam or plate properties, and everything upstream of them. Cached by document.</summary>
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<GH_Document, HashSet<IGH_DocumentObject>> scanSolved = new();
    private static HashSet<IGH_DocumentObject> ScanSolved(GH_Document doc)
    {
        if (doc == null) return new HashSet<IGH_DocumentObject>();
        return scanSolved.GetValue(doc, d =>
        {
            static bool Consumer(IGH_Component c) => c.GetType().Name == "BuildModelComponent"
                || c.Params.Input.Any(p => p.Type.Name == "GH_Material" || p.Type.Name == "GH_CompositeBeamProperty" || p.Type.Name == "GH_BeamProperty" || p.Type.Name == "GH_PlateProperty");
            var upstream = new HashSet<IGH_DocumentObject>();
            void Up(IGH_DocumentObject o)
            {
                if (!upstream.Add(o)) return;
                var parameters = o is IGH_Component c ? c.Params.Input : o is IGH_Param single ? new List<IGH_Param> { single } : new List<IGH_Param>();
                foreach (var p in parameters) foreach (var s in p.Sources) Up(s.Attributes.GetTopLevel.DocObject);
            }
            foreach (var c in d.Objects.OfType<IGH_Component>().Where(Consumer).ToArray()) Up(c);
            return upstream;
        });
    }

    /// <summary>The output parameter a source of a Build Model input carries one to one: the output of a component, through relay parameters with one
    /// source and no data mapping (null: a relay with more sources or a mapping).</summary>
    private static IGH_Param Origin(IGH_Param source)
    {
        for (int hops = 0; hops < 20; hops++)
        {
            if (source.Attributes?.GetTopLevel?.DocObject is IGH_Component) return source; // an output of a component
            if (source.Sources.Count != 1 || !Plain(source)) return null; // a relay parameter: one source, no mapping
            source = source.Sources[0];
        }
        return null;
    }

    /// <summary>The locations of the capture an instance gives one to one: its output index, the key prefix of the Build Model input
    /// (<c>file|Build Model|in&lt;i&gt;</c>) and, by path, the index in the input of its first item. A Build Model input without data mapping
    /// collects its sources in order, branch by branch (Grasshopper appends the items of each source to the branch of the same path): the items of a
    /// source follow those of the sources before it on the same path. Kept only when the collected tree is the tree of the input in the definition.</summary>
    private static List<(int Output, string Prefix, Dictionary<string, int> Offsets)> Landings(GH_Component c)
    {
        var found = new List<(int, string, Dictionary<string, int>)>();
        var doc = c.OnPingDocument(); if (doc == null) return found;
        static string Tree(IEnumerable<(string Path, int Count)> s) => string.Join(";", s.Select(b => b.Path + ":" + b.Count));
        static IEnumerable<(string, int)> Branches(IGH_Structure s) => Enumerable.Range(0, s.PathCount).Select(b => (s.get_Path(b).ToString(), s.get_Branch(b).Count));
        foreach (var build in doc.Objects.OfType<GH_Component>().Where(b => b.GetType().Name == "BuildModelComponent"))
            for (int pi = 0; pi < build.Params.Input.Count; pi++)
            {
                var input = build.Params.Input[pi];
                if (input.Sources.Count == 0 || !Plain(input)) continue;
                var running = new Dictionary<string, int>(StringComparer.Ordinal); var order = new List<string>();
                foreach (var source in input.Sources)
                {
                    var offsets = new Dictionary<string, int>(StringComparer.Ordinal);
                    foreach (var (path, count) in Branches(source.VolatileData))
                    {
                        if (!running.ContainsKey(path)) { running[path] = 0; order.Add(path); }
                        offsets[path] = running[path]; running[path] += count;
                    }
                    var origin = Origin(source);
                    int output = origin == null ? -1 : c.Params.Output.IndexOf(origin);
                    if (output >= 0 && Tree(Branches(origin.VolatileData)) == Tree(Branches(source.VolatileData))) found.Add((output, $"{definition}|{build.InstanceGuid}|in{pi}", offsets));
                }
                // The tree collected so must be the tree of the input (else no landing on this input).
                var collected = order.Select(p => (p, running[p])).OrderBy(b => b.p, StringComparer.Ordinal);
                if (Tree(collected) != Tree(Branches(input.VolatileData).OrderBy(b => b.Item1, StringComparer.Ordinal)))
                    found.RemoveAll(f => f.Item2 == $"{definition}|{build.InstanceGuid}|in{pi}");
            }
        return found;
    }

    /// <summary>R1: the GPC-only outputs of an instance of a definition against the R6.0 capture <c>scan</c>, one to one: the items wired into a Build
    /// Model input at their location, the Build Model output by its instance, the data the capture records by content within the file.</summary>
    private static void Capture(GH_Component c, string where, Run gpc, List<string> failures)
    {
        if (scan == null || !scanFiles.Contains(definition)) return;
        int before = oracles.Located + oracles.Contents + oracles.Converted;
        void Outcome(R1ScanCapture.Outcome outcome, string what, string detail)
        {
            oracles.Count(outcome.ToString());
            if (outcome == R1ScanCapture.Outcome.Different || outcome == R1ScanCapture.Outcome.Missing)
                failures.Add($"{where}: {what}: {(outcome == R1ScanCapture.Outcome.Missing ? "not in the R6.0 capture" : "differs from the R6.0 capture (GUIDs replaced by their ordinals)")}"
                    + (detail == null ? "" : Environment.NewLine + detail));
        }
        foreach (var (output, prefix, offsets) in Landings(c))
            foreach (var item in gpc.Items.Where(i => i.Output == output))
            {
                var datum = item.Goo == null ? null : Prop(item.Goo, "Gpc");
                if (!(datum is BridGHe.Common.Gpc.Legacy.LegacyNodeDraft || datum is BridGHe.Common.Gpc.Legacy.LegacyBeamDraft || datum is BridGHe.Common.Gpc.Legacy.LegacyPlateDraft)) continue;
                if (!offsets.TryGetValue(item.Path, out int offset)) { failures.Add($"{where}: output {output} path {item.Path} not in the Build Model input it feeds"); continue; }
                string key = prefix + "|" + item.Path + "|" + (offset + item.Index);
                Outcome(scan.Compare(key, datum, out var detail), key, detail); oracles.Located++;
            }
        if (c.GetType().Name == "BuildModelComponent")
            foreach (var item in gpc.Items.Where(i => i.Output == 0 && i.Goo != null))
                if (Prop(item.Goo, "GpcInput") is BridGHe.Common.Gpc.GpcModels.Conversion conversion)
                {
                    string key = $"{definition}|{c.InstanceGuid}|model";
                    Outcome(scan.Compare(key, conversion, out var detail), key, detail); oracles.Converted++;
                }
        // The data by content: only for an instance the scan solved when it wrote the capture (upstream of a consumer of materials and properties or of
        // Build Model, GpcMaterialKindChecks.Scan: the other objects were locked, their data never recorded); elsewhere a datum not recorded is counted apart.
        bool solvedByScan = ScanSolved(c.OnPingDocument()).Contains(c);
        // The GH types whose data the scan records by content (the GPC datum of a GH_Material; the FEM values of cases, combinations, stages, beam, plate
        // and composite properties and sections): not the bridge rules (BridgeDesignGoo, a section rule or a family property, never recorded).
        foreach (var item in gpc.Items.Where(i => i.Goo != null && ContentTypes.Contains(i.Goo.GetType().Name)))
        {
            var datum = Prop(item.Goo, "Gpc");
            if (datum == null || R1ScanCapture.ContentKind(datum) == null) continue;
            var outcome = scan.CompareContent(definition, datum, out var detail);
            // A datum not among the contents of its file is not a failure: the scan records by content only what its walk meets (the objects it solved,
            // the FEM values of the GH types), not every output of the replay (e.g. the conci of Bridge Segment Sections in 50 REV001). The content oracle
            // confirms (equal, equal but for GUIDs, declared); the locations (Build Model inputs and outputs) are the strict part.
            if (outcome == R1ScanCapture.Outcome.Missing) { oracles.Count(solvedByScan ? "content not recorded (object solved by the scan)" : "content not recorded (object locked in the scan)"); continue; }
            Outcome(outcome, "output " + item.Output + " " + item.Path + " " + item.Index + " " + R1ScanCapture.ContentKind(datum) + " by content", detail);
            oracles.Contents++;
        }
        if (oracles.Located + oracles.Contents + oracles.Converted > before) oracles.Mapped++; else oracles.Unmapped++;
    }

    /// <summary>R1: the explicit expectation of an in-memory case, declared by its label (the convention of the cases since lane 1a): «(error …)»,
    /// «(refused)», «refused (…)» or «the same error»: at least one error and no output item; «(warning)»: a warning and no error; otherwise no error
    /// and at least one output item. <see cref="Outputs"/>: the number of items of each output, when the case states it.</summary>
    private sealed class Expect
    {
        internal enum Kind { Output, Error, Warning, Nothing }
        internal Kind What; internal int[] Outputs; internal string Message;

        /// <summary>The cases whose label does not say what they give (written from the inputs of the case): Beam Distributed Moment «uniform, axis 2»
        /// is given PB and not PA (since lane 1a): the warning of the component and no output (its twin with PA and PB, R1, gives the load); Merge
        /// Geometry «elements (no geometry)» is given no point, curve or mesh to merge: no output and no message.</summary>
        private static readonly Dictionary<string, Expect> Declared = new Dictionary<string, Expect>(StringComparer.Ordinal)
        {
            ["uniform, axis 2"] = new Expect { What = Kind.Warning, Message = "Input parameter 'PA' failed to collect data" },
            ["elements (no geometry)"] = new Expect { What = Kind.Nothing },
            // Bridge Bearings «mixed types, guide angle» (lane 1b): three types for the points of support 2 that are not one per point: the error of
            // the component, no output (on both paths since lane 1b).
            ["mixed types, guide angle"] = new Expect { What = Kind.Error, Message = "Provide one bearing type or one per selected point" },
            // Node Filter and Plate Filter «by group» (lane 2a) are given no input U: the warning of the component and no output, on both paths since lane 2a.
            ["by group"] = new Expect { What = Kind.Warning, Message = "Input parameter U failed to collect data" },
            // Composite Shrinkage Actions «composite property, shrinkage strain» (lane 3a): the error of the component on both paths until lane INR
            // (gpc-only-models r1-t24). Lane INR (user decision 12/11, «togli il rifiuto»): the section CB (I 800 + slab 2500 × 250) has X and Y
            // principal with Iyy > Ixx at n,sh: its four actions on both paths, no message (curvature checked by hand in --regression inr-inertia).
            ["composite property, shrinkage strain"] = new Expect { What = Kind.Output, Outputs = new[] { 1, 1, 1, 1 } },
        };

        internal static Expect Of(string label)
        {
            if (Declared.TryGetValue(label, out var declared)) return declared;
            string l = label.ToLowerInvariant();
            if (l.Contains("(error") || l.Contains("(refused") || l.Contains("refused (") || l.Contains("same error") || l.Contains(" error,") || l.Contains("(the error")) return new Expect { What = Kind.Error };
            if (l.Contains("(warning")) return new Expect { What = Kind.Warning };
            return new Expect { What = Kind.Output };
        }

        internal void Check(string where, Run run, List<string> failures)
        {
            bool error = run.Messages.Any(m => m.StartsWith("Error: ", StringComparison.Ordinal)), warning = run.Messages.Any(m => m.StartsWith("Warning: ", StringComparison.Ordinal));
            int outputs = run.Items.Count(i => i.Goo != null);
            switch (What)
            {
                case Kind.Error:
                    if (!error || outputs != 0) failures.Add($"{where}: expected an error and no output (R1), got {(error ? "an error" : "no error")} and {outputs} output items: {string.Join(" / ", run.Messages)}");
                    break;
                case Kind.Warning:
                    if (!warning || error) failures.Add($"{where}: expected a warning and no error (R1): {string.Join(" / ", run.Messages)}");
                    break;
                case Kind.Nothing:
                    if (run.Messages.Count != 0 || outputs != 0) failures.Add($"{where}: expected no output and no message (R1), got {outputs} output items: {string.Join(" / ", run.Messages)}");
                    break;
                default:
                    if (error || outputs == 0) failures.Add($"{where}: expected no error and an output (R1), got {outputs} output items: {string.Join(" / ", run.Messages)}");
                    break;
            }
            if (Message != null && !run.Messages.Any(m => m.Contains(Message, StringComparison.Ordinal))) failures.Add($"{where}: expected the message «{Message}» (R1): {string.Join(" / ", run.Messages)}");
            if (Outputs != null && !run.Shape.Select(s => s.Items).Take(Outputs.Length).SequenceEqual(Outputs))
                failures.Add($"{where}: expected {string.Join("/", Outputs)} output items (R1), got {string.Join("/", run.Shape.Select(s => s.Items))}");
        }
    }

    // -----------------------------------------------------------------------------------------------------------------------------------------
    // In-memory cases (coordinator, 7/11): the component types of the lane that no example of the scope uses.

    private static Assembly plugin;
    private static Type T(string name) => plugin.GetType("BridGHe.Grasshopper.DataTypes." + name, true);

    /// <summary>R1: the second build of every in-memory case list, with its inputs built without any FEM value: the GPC examples 01–10 of R0
    /// (<see cref="GpcNativeExamples"/>), the GPC twins of R2a (<see cref="R2aDesignFixtures"/>), the product builders, and the producer components
    /// solved with the switches of P2 on (<see cref="Produce"/>). Every <see cref="Twin"/> gives the FEM-backed input of today (the transitional copy)
    /// or its GPC twin; P2 keeps the second.</summary>
    private static bool gpcInputs;
    private static Dictionary<string, GpcNativeExamples.Example> gpcExamples;
    private static GpcNativeExamples.Inputs Ex(string id) => (gpcExamples ??= GpcNativeExamples.Create().ToDictionary(e => e.Id, StringComparer.Ordinal))[id].Inputs;
    private static BridGHe.Common.Gpc.GpcModels.Conversion ExInput(string id) => (gpcExamples ??= GpcNativeExamples.Create().ToDictionary(e => e.Id, StringComparer.Ordinal))[id].Input;
    private static T Twin<T>(Func<T> fem, Func<T> gpc) => gpcInputs ? gpc() : fem();
    private static Guid R1Id(string what) => BridGHe.Common.Gpc.GpcIdentity.Derive(BridGHe.Common.Gpc.GpcIdentity.NameSpace, "r66-r1-replay:" + what);
    private static Rhino.Geometry.Point3d R(GPC.Geometry.Point3d p) => new Rhino.Geometry.Point3d(p.X, p.Y, p.Z);
    /// <summary>The plate property of the example 03 (Shell Thin «Shell patch», 10 mm, the steel of the examples), built on GPC as R0 builds it.</summary>
    /// <summary>P2CC (13/11/2026, user decision «Congela»): a model of the frozen fixture of Lane 3d (<see cref="Lane3dPayloads"/>, the 0.3.0 payload of the
    /// former in-memory FEM model) read by value at the tolerance of the former conversion (1E-3), as the GH type of 0.4.0 reads it.</summary>
    private static IGH_Goo FrozenModel(string name) => GpcModelGoo(BridGHe.Common.Gpc.GpcLegacyPayloads.Model(Lane3dPayloads.Bytes(name), 1e-3, out _));
    /// <summary>P2CC: the kN-m beam data of the former tests (<c>ImportDump.BeamData</c>) by its frozen payload <c>beams-Knm</c> (legacy-payloads), read by value.</summary>
    private static IGH_Goo FrozenKnm() => GpcModelGoo(BridGHe.Common.Gpc.GpcLegacyPayloads.Model(LegacyPayloadFixtures.Bytes("beams-Knm"), 1e-3, out _));
    private static IGH_Goo FrozenProperty(string name) => R2aDesignFixtures.Property(BridGHe.Common.Gpc.GpcLegacyPayloads.BeamProperty(Lane3dPayloads.Bytes(name)));
    private static IGH_Goo FrozenBeam(string name) => R2aDesignFixtures.Beam(BridGHe.Common.Gpc.GpcLegacyPayloads.Beam(Lane3dPayloads.Bytes(name)));
    private static GPC.Model.ElementProperties.PlateProperty Patch() => BridGHe.Common.Gpc.Builders.GpcPropertyBuilders.Plate("Shell patch", 0, GpcNativeExamples.Steel(), 10, 10);
    /// <summary>A plate on corners of the example 03 with its property, built on GPC (the former <c>new PlateModel(id, corners, property) { AngleDeg }</c>).</summary>
    private static IGH_Goo GpcPlate(string id, double angle, params (double X, double Y, double Z)[] corners) =>
        R2aDesignFixtures.Plate(BridGHe.Common.Gpc.Builders.GpcPlateBuilders.Create(corners, Patch(), null, angle, 0, id, R1Id("plate:" + id + ":" + angle)));
    /// <summary>The corners of the plate of the example 03 (its GPC draft).</summary>
    private static (double X, double Y, double Z)[] PatchCorners() => Ex("03-plate-patch").Plates.Single().Corners.Select(p => (p.X, p.Y, p.Z)).ToArray();
    /// <summary>The GPC twin of <c>BridgeAssignmentTests.Property</c> as its conversion reads it: Beam «P» on a solid rectangle D 1500 × B 300, the material
    /// without a name (the former <c>new MaterialModel("S")</c>: its id is «S», its name the default «Material»), E 210000, ν 0.3, Use Poisson, kind
    /// Undefined. (R2a's twin <c>RectangleProperty</c> names the material «S»: another signature.)</summary>
    private static BridGHe.Common.Gpc.Legacy.LegacyBeamProperty AssignmentProperty(string name = "P") =>
        BridGHe.Common.Gpc.Builders.GpcPropertyBuilders.Beam(name, 5, BridGHe.Common.Gpc.Builders.GpcSectionBuilders.Standard(BridGHe.Common.Gpc.BridgeSectionKind.SolidRectangle, 1500, 300),
            R2aDesignFixtures.Material("Undefined", "Material", 210000, .3));

    /// <summary>A model given by its GPC input alone (<c>GH_Model.FromGpc</c>).</summary>
    private static IGH_Goo GpcModelGoo(BridGHe.Common.Gpc.GpcModels.Conversion input) => (IGH_Goo)T("Fem.GH_Model").GetMethod("FromGpc", BindingFlags.Public | BindingFlags.Static).Invoke(null, new object[] { input });

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
        // R1: the FEM catalogue only for the transitional copy; the GPC twins from the GPC examples of R0 (the same elements, in the same order).
        var g1 = Ex("01-cantilever"); var g2 = Ex("02-simply-supported");
        var beam = R2aDesignFixtures.Beam(g2.Beams[1]);
        var beam2 = R2aDesignFixtures.Beam(g2.Beams[2]);
        var loaded = R2aDesignFixtures.Beam(g2.Beams[0]);
        var node = R2aDesignFixtures.Node(g1.Nodes.Last());
        var node2 = R2aDesignFixtures.Node(g1.Nodes.First());
        var corners = PatchCorners();
        var plate = R2aDesignFixtures.Plate(Ex("03-plate-patch").Plates.Single());
        var tri = GpcPlate("2", 0, corners.Take(3).ToArray());
        // P2X: the plate of 03 with a local axis angle (the datum of an internalized 0.3.0 plate: its draft is the conversion of the value).
        var angled = GpcPlate("3", 30, corners);
        var lc = R2aDesignFixtures.Case(BridGHe.Common.Gpc.Builders.GpcCaseBuilders.Case("Replay", false));
        var along = R(g2.Beams[1].Beam.EndPoint) - R(g2.Beams[1].Beam.StartPoint);
        var tip = R(g1.Nodes.Last().Node.Position);
        var corner3 = new Rhino.Geometry.Point3d(corners[3].X, corners[3].Y, corners[3].Z);
        var end3 = R(g2.Beams[2].Beam.EndPoint);
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
            // R1 (added): the twin of the case above with PA and PB, so that the component gives its load (the case above gives PB only: its warning).
            ("BeamDistributedMomentComponent", "uniform, axis 2, PA and PB", new Dictionary<int, object> { [0] = loaded, [1] = lc, [2] = 0, [3] = 1, [5] = 2.5, [6] = 2.5 }),
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
            ("NodeFilterComponent", "by position", new Dictionary<int, object> { [0] = new object[] { node, node2 }, [2] = new object[] { tip }, [3] = 0 }),
            ("PlateFilterComponent", "by position and property", new Dictionary<int, object> { [0] = new object[] { plate, tri }, [2] = new object[] { corner3 }, [3] = 0, [4] = new object[] { "none" } }),
            ("BeamFilterComponent", "by end point", new Dictionary<int, object> { [0] = new object[] { beam, beam2, loaded }, [3] = new object[] { end3 }, [4] = 0 }),
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
        var beam = R2aDesignFixtures.Beam(Ex("02-simply-supported").Beams[1]);
        var loaded = R2aDesignFixtures.Beam(Ex("02-simply-supported").Beams[0]);
        var geometry = Make("BridgeGeometryGoo", BridGHe.Common.Geometry.SimpleBridgeBuilder.Build(SimpleBridgeTests.Input()));
        var steel = R2aDesignFixtures.Property(AssignmentProperty());
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
        // R1: for the inputs built without FEM, the producer solved with the switches of P2 on (its output by its GPC datum alone).
        using (gpcInputs ? R2aDesignFixtures.WithoutFem() : null) BridgeSegmentExcelTests.Solve(doc, true);
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
        var node = R2aDesignFixtures.Node(Ex("01-cantilever").Nodes.Last());
        var node2 = R2aDesignFixtures.Node(Ex("01-cantilever").Nodes.First());
        var plate = R2aDesignFixtures.Plate(Ex("03-plate-patch").Plates.Single());
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
        var q = PatchCorners().Select(c => new Rhino.Geometry.Point3d(c.X, c.Y, c.Z)).ToArray();
        var mesh = new Rhino.Geometry.Mesh(); foreach (var p in q) mesh.Vertices.Add(p); mesh.Faces.AddFace(0, 1, 2, 3);
        var brep = Rhino.Geometry.Brep.CreatePlanarBreps(new Rhino.Geometry.Rectangle3d(Rhino.Geometry.Plane.WorldXY, 200, 400).ToNurbsCurve(), 1e-6).Single();
        var patchProperty = R2aDesignFixtures.PlateProperty(Patch());
        var beam02 = R2aDesignFixtures.Beam(Ex("02-simply-supported").Beams[1]);
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
            ("PlateComponent", "quadrilateral with property and groups", new Dictionary<int, object> { [0] = mesh, [1] = patchProperty,
                [2] = new object[] { deck, pour }, [3] = 15.0, [4] = 5.0 }),
            ("BeamComponent", "line with property and group", new Dictionary<int, object> { [0] = new Rhino.Geometry.LineCurve(new Rhino.Geometry.Point3d(0, 0, 0), new Rhino.Geometry.Point3d(5000, 0, 0)), [1] = property, [2] = new object[] { pour } }),
            ("NodeFilterComponent", "by group", new Dictionary<int, object> { [0] = new object[] { node, node2 }, [4] = new object[] { deck } }),
            ("PlateFilterComponent", "by group", new Dictionary<int, object> { [0] = new object[] { plate }, [5] = new object[] { deck } }),
            ("BeamOverrideComponent", "property and group", new Dictionary<int, object> { [0] = beam02, [1] = tube, [2] = new object[] { deck } }),
            ("StrausBxsSectionComponent", "missing file (the same error on both paths)", new Dictionary<int, object> { [0] = Path.Combine(Path.GetTempPath(), "p2z-missing.bxs"), [1] = 2 }),
        };
    }

    /// <summary>L2B (lane 2b): the in-memory cases of the lane-2b types that no example of the scope uses (and of the paths the examples do
    /// not reach). Their lane-2 inputs come from the producer components solved in memory, as in <see cref="Lane2aCases"/>.</summary>
    private static List<(string Type, string Label, Dictionary<int, object> Inputs)> Lane2bCases()
    {
        var beam = R2aDesignFixtures.Beam(Ex("02-simply-supported").Beams[1]);
        var loaded = R2aDesignFixtures.Beam(Ex("02-simply-supported").Beams[0]);
        var original = R2aDesignFixtures.Property(GpcNativeExamples.Property());
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
        var beam = R2aDesignFixtures.Beam(Ex("02-simply-supported").Beams[1]);
        var original = R2aDesignFixtures.Property(GpcNativeExamples.Property());
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
        var q = PatchCorners().Select(c => new Rhino.Geometry.Point3d(c.X, c.Y, c.Z)).ToArray();
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
        var records = new[] { lc, lq }.Select(c => (BridGHe.Common.Gpc.Legacy.LegacyCaseData)Prop(c, "Gpc")).ToList();
        var comboType = combo.GetType(); var listType = typeof(List<>).MakeGenericType(comboType);
        object ListOf(params IGH_Goo[] goo) { var l = (System.Collections.IList)Activator.CreateInstance(listType); foreach (var g in goo) l.Add(g); return l; }
        string RowsText(object rows) => string.Join(" | ", ((System.Collections.IEnumerable)rows).Cast<object>().Select(r => Prop(r, "Name") + ":" + string.Join(",",
            ((IEnumerable<KeyValuePair<string, double>>)Prop(r, "Factors")).Select(f => f.Key + "=" + f.Value.ToString("R", System.Globalization.CultureInfo.InvariantCulture)))));
        var of = rowsType.GetMethod("Of", BindingFlags.NonPublic | BindingFlags.Static);
        string femRows = RowsText(of.Invoke(null, new[] { ListOf(combo) })), gpcRows = RowsText(of.Invoke(null, new[] { ListOf(Only(combo)) }));
        Check(femRows == gpcRows && femRows.Contains("ULS:G=1.35,Q=1.5"), $"R65-C2-05: rows {femRows} -> {gpcRows}");
        bool gpcOnly = (bool)rowsType.GetMethod("GpcOnly", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, new object[] { ListOf(Only(combo)), records }); // P2CE phase 2b: no FEM cases parameter
        bool femOnly = (bool)rowsType.GetMethod("GpcOnly", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, new object[] { ListOf(combo), records });
        // P2R (T3): the combination of Load Combination has no FEM value since P2K/P2D, so both lists are GPC-only; before, the FEM-backed list was not.
        Check(gpcOnly && femOnly == (Prop(combo, "Value") == null), "R65-C2-05: the form makes GPC-only combinations for GPC-only inputs only");
        var columns = (string[])rowsType.GetMethod("Columns", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, new object[] { true, records });
        var cells = new List<(string, IReadOnlyList<object>)> { ("SLS", new object[] { 1.0, 0.6 }) };
        var make = rowsType.GetMethod("Combinations", BindingFlags.NonPublic | BindingFlags.Static);
        object Made(bool only) { var r = make.Invoke(null, new object[] { cells, columns, records, only }); return r.GetType().GetField("Item1").GetValue(r); }
        var madeGpc = ((System.Collections.IEnumerable)Made(true)).Cast<IGH_Goo>().Single(); var madeFem = ((System.Collections.IEnumerable)Made(false)).Cast<IGH_Goo>().Single();
        Check(Prop(madeGpc, "Value") == null && GpcNullValueChecks.TextOf(Prop(madeGpc, "Gpc")) == GpcNullValueChecks.TextOf(Prop(madeFem, "Gpc")),
            "R65-C2-05: the combination of a row by its GPC datum alone, the datum of the FEM one");
        var clash = make.Invoke(null, new object[] { new List<(string, IReadOnlyList<object>)> { ("G", new object[] { 1.0, 1.0 }) }, columns, records, true });
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
            stage.GetType().GetMethod("SetLoadCases").Invoke(stage, new object[] { records }); // P2CE phase 2b: no FEM cases parameter
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
        // P2CC (13/11/2026): the beam and its property by their GPC twins (the example 02 of R0).
        var beam = R2aDesignFixtures.Beam(Ex("02-simply-supported").Beams[1]);
        beam.GetType().GetProperty("AnalysedGpc").SetValue(beam, ((BridGHe.Common.Gpc.Legacy.LegacyBeamDraft)Prop(beam, "Gpc")).Beam);
        var own = R2aDesignFixtures.Property(GpcNativeExamples.Property());
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
        // R65-C2-08: P2CC (13/11/2026): retired by name (P2rRetired.Checks): the refused property was a FEM value (a 0.3.0-like property on an Omega section);
        // no GPC producer makes a property the reader refuses.
        Console.WriteLine("GPC-ONLY properties (review R65-C2): 4 in-memory checks (State Override identities, Load Combinations form rows, Staged Construction with an added case, Beam Property Override analysed links; Member Assignment refusal)");
    }

    /// <summary>L2B (note (1) of passo 73): Bridge State Override on the reference of <c>gpc-state-override</c> (the girders of the simple bridge
    /// of the design tests on the steel reference, moment states classified from its GPC conversion with synthetic results, all signs; one
    /// composite section per segment): an override and two refusals (a missing state, a mixed sign with policy 0). The replay gives the
    /// composite sections by their definitions and the states without their FEM source (their BridGHe ID and analysed GPC beam).</summary>
    /// <param name="caseName">L2C (R65-C2-03): the classification case given to every state (null: that of the classification).</param>
    private static IEnumerable<(string Type, string Label, Dictionary<int, object> Inputs)> StateOverrideCases(string caseName = null)
    {
        // P2CC (13/11/2026): the GPC twin alone (the FEM reference is gone), built with the switches of P2 on, as in memory.
        bool keep = gpcInputs; gpcInputs = true;
        try { return StateOverrideCasesGpc(caseName).ToList(); }
        finally { gpcInputs = keep; }
    }

    /// <summary>R1: the GPC twin of the reference of <see cref="StateOverrideCasesFem"/>, built as R2a builds it in <c>gpc-state-override</c>: the beams of
    /// Bridge Member Assignment on the GPC base property (I 600/600/2500/30/30/16, S355 with density), the analysed GPC reference of
    /// <see cref="GpcTestModel"/> with the same synthetic moments (BM1 −1E8; BM2 1E8, −1E8, mixed, 500 by girder), the states classified from it (no FEM
    /// source), one composite definition per segment from the numbers of the former fixture (<see cref="GpcCompositeDefinitions"/>).</summary>
    private static IEnumerable<(string Type, string Label, Dictionary<int, object> Inputs)> StateOverrideCasesGpc(string caseName)
    {
        // The twin of BridgeSegmentSectionsTests.BaseProperty with the density as its conversion reads it: the property «Base», the material without a name
        // (the former new MaterialModel("S355"): id «S355», the default name «Material»), kind Steel. (R2a's BaseProperty names it «S355».)
        var steel = BridGHe.Common.Gpc.Builders.GpcPropertyBuilders.Beam("Base", 5, R2aDesignFixtures.BaseSection(), R2aDesignFixtures.Material("Steel", "Material", 210000, .3, 7.85e-9), "Steel");
        var geometry = BridGHe.Common.Geometry.SimpleBridgeBuilder.Build(SimpleBridgeTests.Input());
        var schedule = BridGHe.Common.Geometry.BridgeSegmentSections.CreateGpc(geometry, steel, null);
        var plans = BridGHe.Common.Geometry.BridgeMemberAssignment.Assign(geometry, schedule.Rules, new Dictionary<int, BridGHe.Common.Gpc.Legacy.LegacyBeamProperty> { [-1] = steel }).Plans;
        var all = plans.Select(p => BridGHe.Common.Gpc.Builders.GpcBridgeGeometryBuilders.Assigned(p, R1Id("state override:" + p.SolverId))).ToArray();
        var girders = all.Where(b => b.Bridge?.Member?.Family == GPC.Checkers.CompositeBridge.Layout.BridgeMemberFamily.Girder).ToArray();
        double Moment(int i, double s) => (i % 4) switch { 0 => 1e8, 1 => -1e8, 2 => (s - .5) * 2e8, _ => 500 };
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
        var catalog = BridGHe.Common.Gpc.GpcResultCatalog.Of(analysed) ?? throw new Exception("GPC-ONLY REPLAY: no results catalog");
        // P2CC: the overload of the ids alone (the same classification: no FEM beam is passed).
        var states = BridGHe.Common.Geometry.BeamMomentClassification.Classify(girders.Select(g => g.Beam.Name).ToArray(),
            girders.Select(g => (catalog, analysed, reference[g.Beam.Name])).ToArray(), "Dead");
        if (caseName != null) states = states.Select(s => s with { Case = caseName }).ToArray();
        var ids = schedule.Rules.Select(r => $"G{r.Group}-C{r.Segment}").ToArray();
        GpcCompositeDefinitions.Numbers Numbers(string id) => GpcCompositeDefinitions.Grillage(new BridgeGrillageExample.Parameters()) with
        {
            Name = "CS " + id, Height = 2500, Web = 16, TopWidth = 600, TopThickness = 30, BottomWidth = 600, BottomThickness = 30,
            SlabWidth = 3000, SlabHeight = 250, TopArea = 1600, TopCover = 40, ConsiderRebar = true,
        };
        var composites = ids.Select(id => (object)R2aDesignFixtures.Composite(GpcCompositeDefinitions.Create(Numbers(id)))).ToArray();
        var beams = all.Select(b => (object)R2aDesignFixtures.Beam(b)).ToArray();
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
        // A04: a material with E and a stress-strain table (a former value).
        var table = TableMaterial();
        // A04 (LIB, with GPC.Model 5.51): a material of the Table Material component (E = 0 and the table), here without Thermal Expansion and
        // Density. After C1 (11/11, MaterialTableComponent: α and ρ optional reads) the component reads the table with and without α and ρ; the
        // case with both is the lane-2a case «stress-strain table» (Lane2aCases).
        var stresses = new object[] { 0.0, 235.0, 360.0 }; var strains = new object[] { 0.0, 0.00112, 0.15 };
        var tableInputs = new Dictionary<int, object> { [0] = "Table E0", [1] = true, [2] = 0.3, [3] = 80000.0, [6] = stresses, [7] = strains };
        var tableOnly = Produce("MaterialTableComponent", tableInputs);
        var pentagon = new Rhino.Geometry.Mesh();
        foreach (var (x, y) in new[] { (0.0, 0.0), (1000.0, 0.0), (1500.0, 500.0), (1000.0, 1000.0), (0.0, 1000.0) }) pentagon.Vertices.Add(x, y, 0);
        pentagon.Faces.AddFace(0, 1, 3, 4); pentagon.Faces.AddFace(1, 2, 3);
        var shell = Produce("PlatePropertyComponent", new Dictionary<int, object> { [0] = "Deck", [1] = 0, [2] = 250.0, [3] = 250.0, [4] = steel });
        var cases = new List<(string, string, Dictionary<int, object>)>
        {
            ("BeamPropertyComponent", "A01: cutoff bar, the cutoff inputs not shown (zeros, brittle)", new Dictionary<int, object> { [0] = "Bar", [1] = 3, [2] = section, [3] = steel }),
            ("BeamPropertyComponent", "A02c: Brep section with a hole", new Dictionary<int, object> { [0] = "Brep", [1] = 5, [2] = brep, [3] = steel }),
            ("PlateComponent", "A07: a mesh of five vertices (error, no output)", new Dictionary<int, object> { [0] = pentagon, [1] = shell }),
            ("MaterialTableComponent", "A04: the table without Thermal Expansion and Density (E = 0)", tableInputs),
            ("BeamPropertyComponent", "A04: a Beam of a Table Material material (E = 0)", new Dictionary<int, object> { [0] = "Table beam", [1] = 5, [2] = section, [3] = tableOnly }),
            ("BeamPropertyComponent", "A04: a Truss of a Table Material material (E = 0)", new Dictionary<int, object> { [0] = "Table truss", [1] = 2, [2] = section, [3] = tableOnly }),
            ("PlatePropertyComponent", "A04: a ShellThin of a Table Material material (E = 0)", new Dictionary<int, object> { [0] = "Table shell", [1] = 0, [2] = 20.0, [3] = 20.0, [4] = tableOnly }),
        };
        foreach (var (type, name) in new[] { (0, "ShellThin"), (1, "ShellThick"), (2, "PlateThin"), (3, "PlateThick") })
            cases.Add(("PlatePropertyComponent", "A04: " + name + " of a material with a stress-strain table", new Dictionary<int, object> { [0] = "Table plate", [1] = type, [2] = 20.0, [3] = 20.0, [4] = table }));
        // A08: a girder of Bridge State Override (a current section state) given another property. P2CC: the state reference on its GPC twin.
        IGH_Goo stated, former;
        {
            var (stateType, _, stateInputs) = StateOverrideCases().First();
            stated = ProduceAll(stateType, stateInputs).First(g => ((BridGHe.Common.Gpc.Legacy.LegacyBeamDraft)Prop(g, "Gpc")).Bridge?.SectionState != null);
            // P2R (T3): the property of the stated beam from Get Beam Property on its GPC datum (the GH types have no FEM value since P2K/P2D;
            // before: the FEM BeamProperty of the same beam).
            former = Produce("GetBeamPropertyComponent", new Dictionary<int, object> { [0] = stated });
        }
        var other = Produce("BeamPropertyComponent", new Dictionary<int, object> { [0] = "Other", [1] = 5, [2] = section, [3] = steel });
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
        // LIB (A02b): the double C and double angle (the native section with the mirror of 0.3.0), a beam of the double angle (the axes of 0.3.0,
        // turned by 90°; angle and offset given) and the taper refused on it (the former message, both paths).
        var doubleL = Produce("BeamSectionAngleComponent", new Dictionary<int, object> { [0] = 150.0, [1] = 10.0, [2] = 100.0, [3] = 12.0, [4] = true, [5] = 8.0 });
        var doubleC = Produce("BeamSectionCComponent", new Dictionary<int, object> { [0] = 100.0, [1] = 10.0, [2] = 300.0, [3] = 8.0, [4] = true, [5] = 12.0 });
        var propertyL = Produce("BeamPropertyComponent", new Dictionary<int, object> { [0] = "PAR 2L", [1] = 5, [2] = doubleL, [3] = steel });
        var line = new Rhino.Geometry.LineCurve(new Rhino.Geometry.Point3d(0, 0, 0), new Rhino.Geometry.Point3d(3000, 0, 0));
        var beamInputs = new Dictionary<int, object> { [0] = line, [1] = propertyL, [3] = 30.0, [4] = new Rhino.Geometry.Vector3d(5, 7, 0) };
        var beam = Produce("BeamComponent", beamInputs);
        cases.Add(("BeamPropertyComponent", "A02b: double C (the native section with its mirror)", new Dictionary<int, object> { [0] = "PAR 2C", [1] = 5, [2] = doubleC, [3] = steel }));
        cases.Add(("BeamPropertyComponent", "A02b: double angle (the native section with its mirror)", new Dictionary<int, object> { [0] = "PAR 2L", [1] = 5, [2] = doubleL, [3] = steel }));
        cases.Add(("BeamComponent", "A02b: a beam of a double angle (the axes turned by 90°)", beamInputs));
        cases.Add(("BeamTaperComponent", "A02b: taper on a double angle (error, no output)", new Dictionary<int, object> { [0] = beam, [1] = 1.0, [2] = 0.8, [3] = 0.5, [4] = 0.6, [5] = 0, [6] = 2 }));
        return cases;
    }

    /// <summary>PAR (A04): a former material with E and a stress-strain table (strain, stress pairs of 0.3.0), FEM-backed.</summary>
    // R1: the GPC twin of the former table material, the recipe of the same values (Custom kind Undefined, Use Poisson); P2CC: alone.
    private static IGH_Goo TableMaterial() => R2aDesignFixtures.MaterialGoo(BridGHe.Common.Gpc.Builders.GpcMaterialBuilders.Material(new BridGHe.Common.Gpc.Legacy.LegacyMaterialRecipe { Name = "Table", Modulus = 210000,
            PoissonRatio = 0.3, Density = 7.85e-9, StressStrainTable = new[] { (stress: 0.0, strain: 0.0), (stress: 235.0, strain: 0.00112), (stress: 360.0, strain: 0.15) } }));

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
        using (gpcInputs ? R2aDesignFixtures.WithoutFem() : null) BridgeSegmentExcelTests.Solve(doc, true); // R1: as Produce
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
                Replay(c, "in memory: BeamPropertyComponent (A01: cutoff bar with its inputs)", kinds, new SortedDictionary<string, (int, int)>(), failures, expect: Expect.Of("cutoff bar with its inputs"));
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
        var records = cases.Select(c => (BridGHe.Common.Gpc.Legacy.LegacyCaseData)Prop(c, "Gpc")).ToList();
        var parameters = new (string Type, string Label, IGH_Goo Stored, string Text, Action<IGH_Param> Act)[]
        {
            ("MaterialParam", "stored material", steel, null, null),
            ("GroupParam", "names cast from a text", null, "Deck::Pour1", null),
            ("BeamSectionParam", "units menu (Nmm to Knm)", section, null, p => p.GetType().GetMethod("OnUnitsClick", BindingFlags.NonPublic | BindingFlags.Instance)
                .Invoke(p, new object[] { new System.Windows.Forms.ToolStripMenuItem("Knm"), EventArgs.Empty })),
            ("LoadCombinationsParam", "cases updated (Traffic removed, Wind added)", combo, null, p => p.GetType().GetMethod("UpdateLoadCases").Invoke(p, new object[] { records })),
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
            Run fem = null, gpc, again;
            try { gpc = Once(true); again = Once(true); } // P2R (T3): the FEM-backed parameter retired (oracle: the replay capture)
            catch (Exception ex) { failures.Add(where + ": " + ex.GetBaseException().Message); continue; }
            var (n, items) = byType.TryGetValue(type, out var v) ? v : (0, 0);
            byType[type] = (n + 1, items + gpc.Outputs.Count);
            // R1: independent of any FEM value: data, no error, no FEM value, deterministic identities (the explicit values of these menus: L2C, P2Z).
            if (gpc.Outputs.Count == 0) failures.Add(where + ": no data by the GPC data alone");
            if (gpc.Messages.Any(m => m.StartsWith("Error: ", StringComparison.Ordinal))) failures.Add(where + ": an error by the GPC data alone: " + string.Join(" / ", gpc.Messages));
            if (gpc.FemOutputs != 0) failures.Add($"{where}: {gpc.FemOutputs} data with a FEM value from GPC-only data");
            if (!gpc.Raw.SequenceEqual(again.Raw)) failures.Add($"{where}: two GPC-only copies give other GUIDs");
            if (captureLane != null) P2rCapture.Replays.Datum(captureLane, where, CaptureValue(gpc)); // P2R step 0
            oracles.Expected++;
            count++;
            if (fem == null) continue;
            // TRANSITIONAL (until P2): the parameter with its FEM-backed data of today.
            oracles.Transitional++;
            if (fem.Outputs.Count == 0) failures.Add(where + ": no data with the inputs of today");
            if (!fem.Messages.SequenceEqual(gpc.Messages)) failures.Add(where + ": messages " + string.Join(" / ", fem.Messages) + " -> " + string.Join(" / ", gpc.Messages));
            if (!fem.Outputs.SequenceEqual(gpc.Outputs))
            {
                var first = fem.Outputs.Zip(gpc.Outputs).FirstOrDefault(p => p.First != p.Second);
                failures.Add($"{where}: data {fem.Outputs.Count} -> {gpc.Outputs.Count}; first (FEM-backed -> GPC-only):" + Environment.NewLine + Differences(first.First ?? "<none>", first.Second ?? "<none>"));
            }
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

    // -----------------------------------------------------------------------------------------------------------------------------------------
    // L3a (R6.5 P1 lane 3, coordinator decisions 2-3 of 8/11): the section «models» (--gpc-only models, regression gpc-only-models, a gate step
    // of its own with its limit of 5,400 s, outside regression all). Every kind is converted: the elements, the properties and data, and the
    // model kinds (GH_Model, GH_CompositeBeam, BridgeWorkflowGoo). The button components are pressed by reflection (as ExampleRemake04.Replay).
    // The scope is a list of its own (ModelsScopeFiles: the scope of Build Model, the examples 11-18 and the remade examples), so the counts of
    // the scan and of the import do not move. Later sub-lanes of lane 3 add their types and cases here.

    /// <summary>L3a: the components of sub-lane 3a (checks, results, report, preview, Composite Beam). L3a part 1: Bridge Model Audit
    /// (<c>Inspect</c> on GPC) is declared pending (part 2 of 3a): not replayed yet.</summary>
    internal static readonly string[] Lane3aTypes =
    {
        "SuperElementCheckComponent", "BridgeShearPlotComponent", "BridgeStudPlotComponent", "BridgeForcePlotComponent", "BridgeResultPlotComponent",
        "BridgeIntermediateStiffenerPlotComponent", "BridgeSupportStiffenerPlotComponent", "CompositeBeamComponent", "CompositeShrinkageActionsComponent",
        "BridgeSectionCheckComponent", "BridgeGirderResultsComponent", "BridgeDetailStationsComponent", "BridgeReportComponent", "BridgeCheckMapComponent",
        "PreviewModelComponent",
    };

    /// <summary>L3b: the components of sub-lane 3b (phases, actions, the bridge model) and Bridge Model Audit (part 2 of 3a).</summary>
    internal static readonly string[] Lane3bTypes =
    {
        "BridgeAnalysisCasesComponent", "BridgeActionComponent", "BridgeShrinkageActionComponent", "BuildBridgeModel", "BridgeCombinationExcelComponent",
        "BridgePhaseTableComponent", "BridgeModelAuditComponent",
    };

    /// <summary>L3c: the components of sub-lane 3c: Build Model, Decompose Model, Merge Model, Node, Super Element Filter, Composite Beam Strain.</summary>
    internal static readonly string[] Lane3cTypes =
    {
        "BuildModelComponent", "DecomposeModelComponent", "MergeModelComponent", "NodeComponent", "SuperElementFilterComponent", "CompositeBeamSlabStrainComponent",
    };

    /// <summary>L3d: the components of sub-lane 3d: the model transformers. Bridge Phase Model is only in the example 23, outside the scope of the section (its model has
    /// a beam end without a node, no GPC input): in-memory cases.</summary>
    internal static readonly string[] Lane3dTypes = { "AttachMovingPathsComponent", "BridgePhaseModelComponent", "BridgeConstructionSequenceComponent" };

    /// <summary>L3c: the producers of the section without an input of the converted kinds (every instance with data; their GPC-only copy runs with
    /// <c>GpcOnlyElements.GeneratorsWithoutFem</c> on, as the generators of lane 1b).</summary>
    internal static readonly string[] ModelsProducers = { "NodeComponent" };

    /// <summary>L3a: the consumers of the converted kinds that read no FEM value (list 1.3 of corsia3-elenco.md), replayed with them.</summary>
    internal static readonly string[] Lane3Consumers =
    {
        "BeamResultsComponent", "NodeResultsComponent", "PlateResultsComponent", "BridgeBeamEnvelopeComponent", "BridgeBearingEnvelopeComponent",
        "BridgeDisplacementEnvelopeComponent", "BridgeResultCasesComponent", "BridgeDeflectionCamberComponent", "BridgeFatigueHistoryComponent",
        "CompositeBeamSelfTensionComponent", "CompositeBeamLinearTensionAnalysisComponent_OBSOLETE", "BridgeConstructionPhaseComponent",
    };

    /// <summary>L3a: the types of the section whose inputs exist only after a native analysis (no remade example has plates): replayed by the
    /// natives of lane 3e (the analyses), not required here.</summary>
    internal static readonly string[] ModelsNativeOnly = { "PlateResultsComponent" };

    /// <summary>L3a: the model kinds converted by the section «models» (with <see cref="ElementGoo"/> and <see cref="PropertyGoo"/>).</summary>
    internal static readonly string[] ModelGoo = { "GH_Model", "GH_CompositeBeam", "BridgeWorkflowGoo" };

    /// <summary>L3c (declared, coordinator 9/11): the definitions of the section with a model that has no GPC input, and why. They stay FEM-backed in the replay, like
    /// the actions on them; any other definition with such a model is a failure of the section. Example 46 has no Build Model: its model is internalised and a
    /// rigid link has a target without a node, which the GPC assembly refuses (Build Model gives the same error: in-memory case of <see cref="Lane3cCases"/>). P2 remakes it.</summary>
    internal static readonly (string File, string Reason)[] ModelsWithoutGpcInput =
    {
        ("46-combinazioni-excel", "its internalised model has a rigid link whose target has no node (the GPC assembly refuses it, and so did the Straus7 export of the FEM model); P2 remakes the example"),
    };

    private static bool modelsLane;
    /// <summary>L3b: inputs of the section without any GPC datum (a model whose GPC input cannot be made and its actions): they stay FEM-backed, and so may their outputs.</summary>
    private static int unconverted;
    private static readonly SortedDictionary<string, int> auditCodes = new SortedDictionary<string, int>(StringComparer.Ordinal);
    private static readonly SortedDictionary<string, int> convertedKinds = new SortedDictionary<string, int>(StringComparer.Ordinal);

    /// <summary>L3a: the definitions of the section: the scope of Build Model (<see cref="BuildModelScan.ScopeFiles"/>), the examples 11-18 and
    /// the remade examples (<see cref="ExampleRemake04.Files"/>, internalised models with GPC results).</summary>
    internal static List<string> ModelsScopeFiles()
    {
        var more = Directory.GetDirectories(ExamplePaths.Root).Where(d => Enumerable.Range(11, 8).Any(i => Path.GetFileName(d).StartsWith(i.ToString("00") + "-", StringComparison.Ordinal)))
            .SelectMany(d => Directory.GetFiles(d, "*.gh*", SearchOption.AllDirectories)).Where(f => (f.EndsWith(".gh") || f.EndsWith(".ghx")) && !f.Contains(Path.DirectorySeparatorChar + "calcoli" + Path.DirectorySeparatorChar));
        return BuildModelScan.ScopeFiles().Concat(more).Concat(ExampleRemake04.Files.Select(f => Path.GetFullPath(Path.Combine(ExamplePaths.Root, f))))
            .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(f => f, StringComparer.Ordinal).ToList();
    }

    /// <summary>L3a: a model given by its GPC input alone, as the producers give it on the path of P2 (<c>GH_Model.FromGpc</c>): the GPC input of
    /// the FEM-backed model with its state (the GPC bridge model of Build BridGHe Model, the analysed model, the drafts, the linked results).
    /// A model whose GPC input cannot be made has no GPC datum: the same goo.</summary>
    internal static IGH_Goo ModelOnly(IGH_Goo goo)
    {
        if (goo.ScriptVariable() == null) return goo;
        var t = goo.GetType();
        BridGHe.Common.Gpc.GpcModels.Conversion input;
        try { input = Prop(goo, "GpcInput") as BridGHe.Common.Gpc.GpcModels.Conversion; }
        catch (TargetInvocationException) { unconverted++; return goo; }
        if (input == null) { unconverted++; return goo; }
        var only = (IGH_Goo)t.GetMethod("FromGpc", BindingFlags.Public | BindingFlags.Static).Invoke(null, new object[] { input });
        if (Prop(goo, "GpcBridge") is { } bridge) t.GetMethod("AttachBridge").Invoke(only, new[] { bridge });
        else if (Prop(goo, "HasAnalysedGpc") is true) t.GetMethod("AttachGpc").Invoke(only, new[] { Prop(goo, "Gpc") });
        if (input.Drafts == null && Prop(goo, "GpcDrafts") is { } drafts) t.GetMethod("AttachDrafts").Invoke(only, new[] { drafts });
        if (Prop(goo, "LinkedGpcResults") is { } linked) t.GetMethod("AttachLinkedResults").Invoke(only, new[] { linked });
        return only;
    }


    /// <summary>L3a: the text of an output of the model kinds and of a section result (null: another kind). A model by the text of its GPC input
    /// and its state; a composite beam by its datum (the draft of its beam, its two definitions, its groups, its analysed beam) and, raw, its GH
    /// GUID (the derived identity); a section result by its provenance, stage, stress points and local checks (exact doubles).</summary>
    private static string ModelsText(IGH_Goo goo, bool raw)
    {
        static string N(double? v) => v is double d ? d.ToString("R", System.Globalization.CultureInfo.InvariantCulture) : "-";
        string text;
        switch (goo?.GetType().Name)
        {
            case "GH_Model":
            {
                BridGHe.Common.Gpc.GpcModels.Conversion input;
                try { input = Prop(goo, "GpcInput") as BridGHe.Common.Gpc.GpcModels.Conversion; }
                catch (TargetInvocationException ex) { return "GH_Model without GPC input: " + ex.GetBaseException().Message; }
                text = "GH_Model " + (input == null ? "null" : GpcDraftCapture.ConversionText(input)) + "\nanalysed " + Prop(goo, "HasAnalysedGpc") + " bridge " + (Prop(goo, "GpcBridge") != null)
                    + " results " + (Prop(goo, "GpcResults") != null) + " linked " + (Prop(goo, "LinkedGpcResults") != null);
                // L3b (declared rule, identity only, as for the drafts): the bridge data of the input by their values (their fingerprint is keyed by the GUIDs of the beams).
                if (!raw && input?.Report.Bridge is { } bridgeData)
                    text = text.Replace("bridge " + bridgeData.Count + " " + bridgeData.Fingerprint(), "bridge " + bridgeData.Count + " values " + string.Join(" ; ", GpcDraftCapture.BridgeValues(bridgeData)));
                break;
            }
            case "GH_CompositeBeam":
            {
                if (Prop(goo, "Gpc") is not { } datumObject) return null;
                var beam = (BridGHe.Common.Gpc.Legacy.LegacyBeamDraft)Prop(datumObject, "Beam");
                var start = (GPC.Checkers.CompositeBridge.BridgeCompositeSectionDefinition)Prop(datumObject, "Start");
                var end = (GPC.Checkers.CompositeBridge.BridgeCompositeSectionDefinition)Prop(datumObject, "End");
                var groups = (IReadOnlyList<string>)Prop(datumObject, "Groups");
                text = "GH_CompositeBeam " + GpcNullValueChecks.TextOf(beam) + "\nstart " + (start == null ? "null" : GpcNullValueChecks.TextOf(start))
                    + "\nend " + (end == null ? "null" : GpcNullValueChecks.TextOf(end)) + "\ngroups " + string.Join("|", groups ?? Array.Empty<string>())
                    + "\nanalysed " + (Prop(goo, "AnalysedGpc") is GPC.Model.Elements.Element e ? e.Id + " " + e.Name : "none")
                    + (raw ? "\nguid " + Prop(goo, "Guid") : "");
                break;
            }
            case "GH_ObjectWrapper" when modelsLane && Prop(goo, "Value") is BridGHe.Common.Checks.BridgeStressResult r:
                return "GH_ObjectWrapper BridgeStressResult " + r.Source + " " + r.Stage.Name + " " + r.Analysis.Geometry.SectionType
                    + string.Concat(r.Stage.Points.Select(p => "\n  point " + p.Name + " " + N(p.Stress) + " " + N(p.Limit) + " " + N(p.Utilization) + " " + p.Active))
                    + string.Concat(BridGHe.Common.Checks.BridgeGpc.Checks(r.Stage).Select(c => "\n  check " + c.Name + " " + N(c.Demand) + " " + N(c.Resistance) + " " + N(c.Ratio) + " " + c.Status));
            default: return null;
        }
        if (raw) return text;
        // L3b (declared rule, identity only): the model revision of the element sources of a GPC-only Build BridGHe Model output is that of its
        // first reference model (the bridge model keeps the element identities of the reference), not the GUID of the conversion of the merged
        // FEM model: compared as a mark, so that the ordinals of the other GUIDs stay comparable.
        return GpcDraftCapture.WithoutGuids(System.Text.RegularExpressions.Regex.Replace(text, "(ModelRevision_x003E_k__BackingField[^>]*>)[0-9a-fA-F]{8}-[0-9a-fA-F-]{27}<", "$1revision<"));
    }

    /// <summary>L3a: the copy of a component of the section, before it is solved: the buttons pressed (Super Element Check, the plots, Bridge
    /// Report), the output paths redirected into TEMP (a plot writes no file; the report writes one file there, overwritten; its template
    /// resolved against the definition the component comes from).</summary>
    private static void PrepareModels(GH_Component copy, GH_Component original)
    {
        static FieldInfo Field(Type t, string name)
        {
            for (; t != null; t = t.BaseType) if (t.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic) is { } f) return f;
            return null;
        }
        void Set(string name, IGH_Goo value)
        {
            var param = copy.Params.Input.FirstOrDefault(p => p.Name == name) ?? throw new Exception("GPC-ONLY REPLAY: no input " + name + " on " + copy.Name);
            var tree = new GH_Structure<IGH_Goo>();
            if (value != null) tree.Append(value, new GH_Path(0));
            SetInput(param, tree, g => g);
        }
        switch (copy.GetType().Name)
        {
            case "SuperElementCheckComponent": case "BuildBridgeModel": Field(copy.GetType(), "_run").SetValue(copy, true); break; // L3b: Build BridGHe Model
            case "StrausR3AnalysisComponent": case "MovingStaticAnalysisComponent":
                // L3e: pressed only in the rehearsal with Straus7, the output in a folder of its own (same file name: the name of the analysed model comes from it).
                if (rehearsalFolder != null)
                {
                    var path = copy.Params.Input.First(p => p.Name.Equals("Output Path", StringComparison.OrdinalIgnoreCase));
                    string own = Path.Combine(rehearsalFolder, Guid.NewGuid().ToString("N")); Directory.CreateDirectory(own);
                    string given = original.Params.Input.First(p => p.Name.Equals("Output Path", StringComparison.OrdinalIgnoreCase)).VolatileData.AllData(true).Single().ScriptVariable() as string;
                    Field(copy.GetType(), "_run").SetValue(copy, true); Set(path.Name, new GH_String(Path.Combine(own, Path.GetFileName(given))));
                }
                break;
            case "BridgeCombinationExcelComponent":
            {
                // L3b: Create writes a workbook in TEMP only (overwritten); Read reads the workbook of the example, resolved against the definition.
                Field(copy.GetType(), "_run").SetValue(copy, true);
                int mode = original.Params.Input[3].VolatileData.AllData(true).FirstOrDefault()?.ScriptVariable() is int m ? m : 0;
                string file = original.Params.Input[2].VolatileData.AllData(true).FirstOrDefault()?.ScriptVariable() as string, source = original.OnPingDocument()?.FilePath;
                if (mode == 0)
                {
                    string dir = Path.Combine(Path.GetTempPath(), "bridghe-gpc-only-models"); Directory.CreateDirectory(dir);
                    Set("File", new GH_String(Path.Combine(dir, "combinations.xlsx"))); Set("Overwrite", new GH_Boolean(true));
                }
                else if (file != null && !Path.IsPathRooted(file) && !string.IsNullOrEmpty(source))
                {
                    // P2CG (14/11/2026): Read reads a copy of the workbook of the example in TEMP, in a folder named as the example folder (Create writes
                    // in TEMP too), so the File output does not hold the folder of the worktree that runs the replay (the capture of 46 held that of
                    // the worktree that recorded it). A missing workbook is resolved as before.
                    string workbook = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(source), file));
                    if (File.Exists(workbook))
                    {
                        string dir = Path.Combine(Path.GetTempPath(), "bridghe-gpc-only-models", Path.GetFileName(Path.GetDirectoryName(source))); Directory.CreateDirectory(dir);
                        string copied = Path.Combine(dir, Path.GetFileName(workbook));
                        File.Copy(workbook, copied, true);
                        Set("File", new GH_String(copied));
                    }
                    else Set("File", new GH_String(workbook));
                }
                break;
            }
            case "BridgeReportComponent":
            {
                Field(copy.GetType(), "_run").SetValue(copy, true);
                string folder = Path.Combine(Path.GetTempPath(), "bridghe-gpc-only-models"); Directory.CreateDirectory(folder);
                Set("File", new GH_String(Path.Combine(folder, "report.docx"))); Set("Overwrite", new GH_Boolean(true));
                var template = original.Params.Input[1].VolatileData.AllData(true).FirstOrDefault()?.ScriptVariable() as string;
                string definition = original.OnPingDocument()?.FilePath;
                if (template != null && !Path.IsPathRooted(template) && !string.IsNullOrEmpty(definition))
                    Set("Template", new GH_String(Path.GetFullPath(Path.Combine(Path.GetDirectoryName(definition), template))));
                break;
            }
            default:
                if (Field(copy.GetType(), "run") is { } run && copy.GetType().IsSubclassOf(plugin.GetType("BridGHe.Grasshopper.Components.BridgeChecks.BridgePlotComponent", true)))
                { run.SetValue(copy, true); Set("File Path", null); }
                break;
        }
    }

    /// <summary>L3a: the button components of a definition of the section pressed once, upstream first (Build BridGHe Model, then Super Element
    /// Check), as <see cref="ExampleRemake04.Replay"/> does: their outputs feed the components of the section. No other button (plots, Report,
    /// Combination Excel write files) and no analysis.</summary>
    private static void PressModels(GH_Document doc)
    {
        foreach (var name in new[] { "BuildBridgeModel", "SuperElementCheckComponent" })
            foreach (var c in doc.Objects.OfType<GH_Component>().Where(c => c.GetType().Name == name))
            {
                c.GetType().GetField("_run", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(c, true);
                c.ExpireSolution(false); BridgeSegmentExcelTests.Solve(doc, true);
            }
    }

    /// <summary>L3a: derived instances on a solved definition whose Super Element Check has results on the GPC bridge model (the remade 47 and
    /// 48): Bridge Girder Results (girder 1), Bridge Detail Stations (two result points, without and with the phases of Build BridGHe Model)
    /// and Bridge Check Map (one row per result, at most 40), wired to the outputs of the definition in memory (never saved). Outside the natives
    /// no example of the scope reaches these components with data (their examples 50 and 99 analyse first). Returns the instances added.</summary>
    private static int DeriveModels(GH_Document doc)
    {
        int added = 0;
        foreach (var check in doc.Objects.OfType<GH_Component>().Where(c => c.GetType().Name == "SuperElementCheckComponent").ToArray())
        {
            var output = check.Params.Output[0];
            var results = output.VolatileData.AllData(true).Select(g => (g as GH_ObjectWrapper)?.Value as BridGHe.Common.Checks.BridgeStressResult).Where(r => r?.Source != null).ToArray();
            var models = check.Params.Input[0].Sources; var beams = check.Params.Input[1].Sources;
            if (results.Length == 0 || models.Count == 0 || beams.Count == 0) continue;
            var bridge = Prop(check.Params.Input[0].VolatileData.AllData(true).FirstOrDefault(), "GpcBridge") as BridGHe.Common.Gpc.GpcBridge;
            if (bridge == null) continue;
            void Persistent(IGH_Param param, IEnumerable<IGH_Goo> values)
            {
                var tree = new GH_Structure<IGH_Goo>(); foreach (var v in values) tree.Append(v, new GH_Path(0));
                SetInput(param, tree, g => g);
            }
            // Bridge Girder Results on the beams and results of the check.
            var girder = BridgeSegmentExcelTests.Add(doc, "BridgeGirderResultsComponent");
            foreach (var s in beams) girder.Params.Input[0].AddSource(s);
            girder.Params.Input[1].AddSource(output);
            // Bridge Detail Stations at two result points (the points of the stations on the axes of the beams of the bridge model, as the
            // component computes them), selection only and with the phases of Build BridGHe Model.
            var axes = bridge.Model.BeamElements.Values.GroupBy(b => b.Source.OriginalId, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
            var points = results.Select(r => r.Source).Where(s => axes.ContainsKey(s.BeamId)).Select(s =>
            {
                var b = axes[s.BeamId];
                var from = new Rhino.Geometry.Point3d(b.StartPoint.X, b.StartPoint.Y, b.StartPoint.Z); var to = new Rhino.Geometry.Point3d(b.EndPoint.X, b.EndPoint.Y, b.EndPoint.Z);
                return from + (to - from) * (s.StationMm / from.DistanceTo(to));
            }).Distinct().Take(2).ToArray();
            var build = models[0].Attributes?.GetTopLevel?.DocObject as GH_Component;
            foreach (bool phases in new[] { false, true })
            {
                if (phases && build?.GetType().Name != "BuildBridgeModel") continue;
                var stations = BridgeSegmentExcelTests.Add(doc, "BridgeDetailStationsComponent");
                foreach (var s in models) stations.Params.Input[0].AddSource(s);
                stations.Params.Input[1].AddSource(output);
                Persistent(stations.Params.Input[2], points.Select(p => (IGH_Goo)new GH_Point(p)));
                if (phases) foreach (var s in build.Params.Input[1].Sources) stations.Params.Input[3].AddSource(s);
                added++;
            }
            // The stress, force and shear plots on the results of the check (its forces output for the force plot).
            // 0.7.0: Tension Forces is the second output (Tau b removed), found by name
            foreach (var (type, from) in new[] { ("BridgeResultPlotComponent", "Tension Check"), ("BridgeForcePlotComponent", "Tension Forces"), ("BridgeShearPlotComponent", "Tension Check") })
            {
                var plot = BridgeSegmentExcelTests.Add(doc, type);
                foreach (var s in models) plot.Params.Input[0].AddSource(s);
                foreach (var s in beams) plot.Params.Input[1].AddSource(s);
                plot.Params.Input[2].AddSource(check.Params.Output.First(o => o.Name == from));
                added++;
            }
            // L3b (the cases 3a left uncovered): on the reference model of Build BridGHe Model, without the bridge: Bridge Detail Stations (the beams of
            // the GPC input, BridgeDetailStations of 3a) and, where the definition has a report, Bridge Report (BridgeReportData.Source(Conversion)).
            if (build?.GetType().Name == "BuildBridgeModel")
            {
                var plain = BridgeSegmentExcelTests.Add(doc, "BridgeDetailStationsComponent");
                foreach (var s in build.Params.Input[0].Sources) plain.Params.Input[0].AddSource(s);
                plain.Params.Input[1].AddSource(output);
                Persistent(plain.Params.Input[2], points.Select(p => (IGH_Goo)new GH_Point(p)));
                added++;
                if (doc.Objects.OfType<GH_Component>().FirstOrDefault(c => c.GetType().Name == "BridgeReportComponent") is { } report)
                {
                    var extra = BridgeSegmentExcelTests.Add(doc, "BridgeReportComponent");
                    extra.Params.Input[0].AddSource(output);
                    SetInput(extra.Params.Input[1], report.Params.Input[1].VolatileData, g => g); // the template
                    SetInput(extra.Params.Input[2], report.Params.Input[2].VolatileData, g => g); // replaced in the copies (PrepareModels)
                    foreach (var s in build.Params.Input[0].Sources) extra.Params.Input[4].AddSource(s);
                    added++;
                }
                // The refusal of the reactions without the bridge (ModelsChecks): its inputs, once.
                detailRefusal ??= (build.Params.Input[0].VolatileData.AllData(true).First(), results, points, build.Params.Input[1].VolatileData.AllData(true).ToArray());
            }
            // Bridge Check Map, one row per result.
            var map = BridgeSegmentExcelTests.Add(doc, "BridgeCheckMapComponent");
            foreach (var s in models) map.Params.Input[0].AddSource(s);
            var rows = results.Take(40).ToArray();
            Persistent(map.Params.Input[1], rows.Select(r => (IGH_Goo)new GH_ObjectWrapper(r)));
            Persistent(map.Params.Input[2], rows.Select(r => (IGH_Goo)new GH_String(r.Source.BeamId)));
            Persistent(map.Params.Input[3], rows.Select(r => (IGH_Goo)new GH_Number(r.Source.StationMm)));
            Persistent(map.Params.Input[4], rows.Select(r => (IGH_Goo)new GH_String(r.Source.Combination)));
            added += 2;
        }
        // Beam, Node and Plate Results on the first three elements of each kind of the first internalised model with GPC results (the remade
        // examples), through Decompose Model (its FEM path: elements with their analysed GPC elements).
        var analysed = doc.Objects.OfType<IGH_Param>().Concat(doc.Objects.OfType<GH_Component>().SelectMany(c => c.Params.Input))
            .FirstOrDefault(p => (p.GetType().GetProperty("PersistentData")?.GetValue(p) as IGH_Structure)?.AllData(true).Any(g => g?.GetType().Name == "GH_Model" && Prop(g, "GpcResults") != null) == true);
        if (analysed != null)
        {
            var decompose = BridgeSegmentExcelTests.Add(doc, "DecomposeModelComponent");
            decompose.Params.Input[0].AddSource(analysed);
            BridgeSegmentExcelTests.Solve(doc, true);
            foreach (var (type, output) in new[] { ("NodeResultsComponent", 0), ("BeamResultsComponent", 1), ("PlateResultsComponent", 2) })
            {
                var items = decompose.Params.Output[output].VolatileData.AllData(true).Take(3).ToArray();
                if (items.Length == 0) continue;
                var results = BridgeSegmentExcelTests.Add(doc, type);
                var tree = new GH_Structure<IGH_Goo>(); foreach (var g in items) tree.Append(g, new GH_Path(0));
                SetInput(results.Params.Input[0], tree, g => g);
                added++;
            }
        }
        if (added > 0) BridgeSegmentExcelTests.Solve(doc, true);
        return added;
    }

    /// <summary>L3b: the inputs of the refusal of Bridge Detail Stations (reactions without the bridge model), from the first remade example.</summary>
    private static (IGH_Goo Model, BridGHe.Common.Checks.BridgeStressResult[] Results, Rhino.Geometry.Point3d[] Points, IGH_Goo[] Phases)? detailRefusal;

    /// <summary>L3b (the cases 3a left uncovered): what the GPC-only path refuses where the FEM path reads FEM data (an error with the prefix of
    /// <c>GpcOnlyModels.Refused</c> and no output; the FEM-backed copies keep reading the FEM data, so these are not replays), and Bridge Check Map outside N-mm
    /// (the GPC beams go through <c>BridgeUnits.FromMm</c>: equal to the FEM reading within 1e-6, the tolerance of the station check of <c>BridgeCheckMap.Evaluate</c>).</summary>
    private static void ModelsChecks(ISet<string> kinds, List<string> failures, ref int cases)
    {
        var count = 0;
        void Refused(string type, string label, Dictionary<int, object> inputs)
        {
            if (!Selected(type)) return;
            using var doc = new GH_Document();
            var c = BridgeSegmentExcelTests.Add(doc, type);
            foreach (var input in inputs)
            {
                var tree = new GH_Structure<IGH_Goo>();
                foreach (var v in input.Value is object[] list ? list : new[] { input.Value }) tree.Append(Goo(v), new GH_Path(0));
                SetInput(input.Key < 0 ? c.Params.Input.OfType<Grasshopper.Kernel.Parameters.Param_Boolean>().Last() : c.Params.Input[input.Key], tree, g => g); // -1: the last boolean input (Live)
            }
            if (c.Params.Input.FirstOrDefault(p => p.Name == "Stations") is { } stations) // the stiffener plots: a required input read after the refusal
            {
                var one = new GH_Structure<IGH_Goo>(); one.Append(new GH_Number(0), new GH_Path(0));
                SetInput(stations, one, g => g);
            }
            BridgeSegmentExcelTests.Solve(doc, true);
            var errors = c.RuntimeMessages(GH_RuntimeMessageLevel.Error).ToArray();
            if (!errors.Any(e => e.StartsWith("GPC-only input: ", StringComparison.Ordinal))) failures.Add($"refusal: {c.Name} ({label}): no GPC-only error: {string.Join(" / ", errors)}");
            if (c.Params.Output.Any(p => p.VolatileData.AllData(true).Any())) failures.Add($"refusal: {c.Name} ({label}): an output on a refusal");
            count++;
        }
        // P2CC (13/11/2026): the model and the beam by their GPC twins (the example 02 of R0).
        var model = GpcModelGoo(ExInput("02-simply-supported"));
        var steel = Produce("MaterialSteelComponent", new Dictionary<int, object> { [0] = "S355", [1] = 2 });
        var concrete = Produce("MaterialConcreteComponent", new Dictionary<int, object> { [0] = "C35/45", [1] = 7 });
        var rebar = Produce("MaterialRebarComponent", new Dictionary<int, object> { [0] = "B450C" });
        var section = Produce("BeamSectionIComponent", new Dictionary<int, object> { [0] = 300.0, [1] = 250.0, [2] = 800.0, [3] = 20.0, [4] = 16.0, [5] = 12.0 });
        var slab = Produce("BeamSectionConcreteSlabComponent", new Dictionary<int, object> { [0] = 250.0, [1] = 2500.0, [2] = 1000.0, [3] = 50.0, [4] = 800.0, [5] = 50.0 });
        var composite = Produce("BeamPropertyCompositeComponent", new Dictionary<int, object> { [0] = "CB", [1] = slab, [2] = section, [3] = concrete, [4] = steel, [5] = rebar });
        var beam = GpcOnly(Produce("CompositeBeamComponent", new Dictionary<int, object> { [0] = R2aDesignFixtures.Beam(Ex("02-simply-supported").Beams[1]), [1] = composite }), kinds);
        foreach (var plot in new[] { "BridgeShearPlotComponent", "BridgeStudPlotComponent", "BridgeForcePlotComponent", "BridgeResultPlotComponent", "BridgeIntermediateStiffenerPlotComponent", "BridgeSupportStiffenerPlotComponent" })
            Refused(plot, "without the GPC bridge model", new Dictionary<int, object> { [0] = model, [1] = beam, [2] = 1.0, [3] = 0, [-1] = true });
        Refused("BridgeGirderResultsComponent", "without the GPC bridge model", new Dictionary<int, object> { [0] = beam, [1] = 1.0, [2] = 1 });
        if (detailRefusal is var (reference, results, points, phases))
            Refused("BridgeDetailStationsComponent", "reactions without the GPC bridge model", new Dictionary<int, object>
            {
                [0] = ModelOnly(reference), [1] = results.Select(r => (object)new GH_ObjectWrapper(r)).ToArray(), [2] = points.Select(p => (object)p).ToArray(), [3] = phases.Select(p => (object)GpcOnly(p, kinds)).ToArray(),
            });
        else if (Selected("BridgeDetailStationsComponent") && string.IsNullOrEmpty(Environment.GetEnvironmentVariable("BRIDGHE_GPC_ONLY_FILES"))) failures.Add("refusal: no remade example gave the inputs of Bridge Detail Stations");
        // P2CC (13/11/2026): Bridge Check Map in kN-m against the FEM reading is retired by name (P2rRetired.Checks).
        Lane3cChecks(kinds, failures, ref count);
        // R1 (the evidence of the reported defect of Composite Beam): the digest of the GPC archive of one composite definition, written twice and written
        // from a copy of its GH type (as a solve copies the persistent data).
        if (Selected("CompositeBeamComponent"))
        {
            var definition = (GPC.Checkers.CompositeBridge.BridgeCompositeSectionDefinition)Prop(GpcOnly(composite, kinds), "Gpc");
            string D(GPC.Checkers.CompositeBridge.BridgeCompositeSectionDefinition d) => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(BridGHe.Common.Gpc.GpcDraftArchive.Write(d)));
            var copied = (GPC.Checkers.CompositeBridge.BridgeCompositeSectionDefinition)Prop(GpcOnly(composite, kinds).Duplicate(), "Gpc");
            Console.WriteLine($"GPC-ONLY models: the GPC archive of a composite definition: written twice {(D(definition) == D(definition) ? "the same bytes" : "OTHER bytes")}; "
                + $"from a copy of its GH type {(ReferenceEquals(copied, definition) ? "the same definition" : D(copied) == D(definition) ? "another definition, the same bytes" : "another definition, OTHER bytes")}; "
                + $"canonical text of the copy {(GpcNullValueChecks.TextOf(copied) == GpcNullValueChecks.TextOf(definition) ? "equal" : "different")}, but for GUIDs {(GpcDraftCapture.WithoutGuids(GpcNullValueChecks.TextOf(copied)) == GpcDraftCapture.WithoutGuids(GpcNullValueChecks.TextOf(definition)) ? "equal" : "different")}");
        }
        cases += count;
    }

    /// <summary>L3c: what the replay of two copies does not see: the GH GUIDs of a GPC-only model (Build Model gives them, Decompose Model gives them back, written
    /// and read together), the notice of a former payload without a FEM value, a check that meets a beam with a stale section state (A08), the declared
    /// material kind in Composite Beam Property.</summary>
    private static void Lane3cChecks(ISet<string> kinds, List<string> failures, ref int count)
    {
        void Check(bool ok, string what) { if (!ok) failures.Add("L3c: " + what); }
        if (Selected("BuildModelComponent") || Selected("DecomposeModelComponent"))
        {
            // P2CC (13/11/2026): the node and the beam by their GPC twins (the example 02 of R0).
            var node = R2aDesignFixtures.Node(Ex("02-simply-supported").Nodes.First()); var beam = R2aDesignFixtures.Beam(Ex("02-simply-supported").Beams[1]);
            var built = Produce("BuildModelComponent", new Dictionary<int, object> { [0] = new object[] { node, beam }, [1] = "Guids", [2] = 2 });
            dynamic model = built;
            Check(Prop(built, "Value") == null, "Build Model on GPC-only elements gives no FEM value");
            BridGHe.Common.Gpc.GpcModelDrafts drafts = model.GpcDrafts;
            Guid[] guids = model.ElementGuidsOf(drafts);
            int nodeIndex = drafts.Nodes.ToList().FindIndex(d => d.Node.Guid == ((BridGHe.Common.Gpc.Legacy.LegacyNodeDraft)Prop(node, "Gpc")).Node.Guid);
            Check(nodeIndex >= 0 && guids[nodeIndex] == (Guid)Prop(node, "Guid"), "the GH GUID of a node of Build Model is the one it was given");
            Check(guids[drafts.Nodes.Count] == (Guid)Prop(beam, "Guid"), "the GH GUID of a beam of Build Model is the one it was given");
            var created = Enumerable.Range(0, drafts.Nodes.Count).Where(i => i != nodeIndex).ToArray();
            Check(created.Length > 0 && created.All(i => guids[i] == drafts.Nodes[i].Node.Guid), "a node created at a beam end has the GUID of its draft");
            var chunk = new GH_IO.Serialization.GH_LooseChunk("model");
            Check(model.Write(chunk), "a GPC-only model is written with its element GUIDs");
            var binary = chunk.Serialize_Binary();
            GH_IO.Serialization.GH_LooseChunk Reader() { var c = new GH_IO.Serialization.GH_LooseChunk("read"); c.Deserialize_Binary(binary); return c; }
            dynamic read = Activator.CreateInstance(built.GetType());
            Check(read.Read(Reader()) && read.Value == null, "a GPC-only model read back has no FEM value");
            Guid[] again = read.ElementGuidsOf((BridGHe.Common.Gpc.GpcModelDrafts)read.GpcDrafts);
            Check(again.SequenceEqual(guids), "the element GUIDs of a GPC-only model are read back with it");
            var beams = ProduceAll("DecomposeModelComponent", new Dictionary<int, object> { [0] = read }, 1);
            Check(beams.Count == 1 && (Guid)Prop(beams[0], "Guid") == (Guid)Prop(beam, "Guid"), "Decompose Model gives the GH GUIDs of the model back");
            var damaged = Reader(); damaged.RemoveItem("ElementGuids"); damaged.SetString("ElementGuids", "[\"" + Guid.NewGuid() + "\"]");
            dynamic refused = Activator.CreateInstance(built.GetType());
            Check(!refused.Read(damaged), "a saved list of element GUIDs of another length is refused");
            count++;
            // A model given by its input alone without the drafts of its elements (no producer gives one): the error of the components and no output.
            // P2CC: the GPC input of example 02 (R0) without its drafts (before: the conversion of the FEM model).
            var bare = (IGH_Goo)built.GetType().GetMethod("FromGpc").Invoke(null, new object[] { ExInput("02-simply-supported").WithDrafts(null) });
            foreach (var type in new[] { "DecomposeModelComponent", "SuperElementFilterComponent" })
            {
                using var doc = new GH_Document();
                var c = BridgeSegmentExcelTests.Add(doc, type);
                var tree = new GH_Structure<IGH_Goo>(); tree.Append(bare, new GH_Path(0));
                SetInput(c.Params.Input[0], tree, g => g);
                if (c.Params.Input.Count > 1) { var name = new GH_Structure<IGH_Goo>(); name.Append(new GH_String("x"), new GH_Path(0)); SetInput(c.Params.Input[1], name, g => g); }
                BridgeSegmentExcelTests.Solve(doc, true);
                Check(c.RuntimeMessages(GH_RuntimeMessageLevel.Error).Any(e => e.StartsWith("GPC-only input: ", StringComparison.Ordinal)), type + ": a model without drafts is an error");
                Check(!c.Params.Output.Any(p => p.VolatileData.AllData(true).Any()), type + ": no output for a model without drafts");
                count++;
            }
        }
        // The notice of a former payload, read from the payload when the model has no FEM value (P2 removes the FEM value read with it).
        if (selected == null)
        {
            // P2CC (13/11/2026): the former payloads by their frozen fixtures: a model without results (units-Nmm of legacy-payloads) and a model with
            // beam results (p2d-model of p2d-payloads: the source of the bridge utilities with its results).
            var type = T("Fem.GH_Model");
            string Notice(byte[] payload)
            {
                var input = BridGHe.Common.Gpc.GpcLegacyPayloads.Model(payload, 1e-6, out _);
                var goo = type.GetMethod("FromGpc").Invoke(null, new object[] { input });
                type.GetField("_legacy", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(goo, payload);
                type.GetField("_legacyOf", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(goo, input);
                return (string)Prop(goo, "LegacyNotice");
            }
            Check(Notice(LegacyPayloadFixtures.Bytes("units-Nmm")) == null, "a former payload without results has no notice");
            Check(Notice(P2dPayloads.Bytes("p2d-model.bgha1")) == BridGHe.Common.Gpc.GpcLegacyPayloads.ResultsDroppedMessage, "a former payload with results has the notice, without a FEM value");
            count++;
        }
        var steel = Produce("MaterialSteelComponent", new Dictionary<int, object> { [0] = "S355", [1] = 2 });
        var section = Produce("BeamSectionIComponent", new Dictionary<int, object> { [0] = 300.0, [1] = 250.0, [2] = 800.0, [3] = 20.0, [4] = 16.0, [5] = 12.0 });
        // A08 (PAR did not run it): a beam whose property changed after Bridge State Override meets the checks: the same refusal as on the FEM path, from the marker of the builder.
        if (Selected("BuildBridgeModel"))
        {
            var (stateType, _, stateInputs) = StateOverrideCases().First();
            var stated = ProduceAll(stateType, stateInputs).First(g => ((BridGHe.Common.Gpc.Legacy.LegacyBeamDraft)Prop(g, "Gpc")).Bridge?.SectionState != null);
            var other = Produce("BeamPropertyComponent", new Dictionary<int, object> { [0] = "Other", [1] = 5, [2] = section, [3] = steel });
            var stale = ProduceAll("BeamOverrideComponent", new Dictionary<int, object> { [0] = GpcOnly(stated, kinds), [1] = GpcOnly(other, kinds) }).Single();
            var state = ((BridGHe.Common.Gpc.Legacy.LegacyBeamDraft)Prop(stale, "Gpc")).Bridge.SectionState;
            var model = Produce("BuildModelComponent", new Dictionary<int, object> { [0] = new object[] { stale }, [1] = "Stale", [2] = 2 });
            var action = Produce("BridgeActionComponent", new Dictionary<int, object> { [0] = "Q", [1] = model, [2] = "Dead", [3] = (int)state.Behavior });
            var phase = Produce("BridgeConstructionPhaseComponent", new Dictionary<int, object> { [0] = 10, [1] = "Composite", [2] = 1, [3] = new object[] { action } });
            using var doc = new GH_Document();
            var c = BridgeSegmentExcelTests.Add(doc, "BuildBridgeModel");
            void Input(int k, params IGH_Goo[] values) { var tree = new GH_Structure<IGH_Goo>(); foreach (var v in values) tree.Append(v, new GH_Path(0)); SetInput(c.Params.Input[k], tree, g => g); }
            Input(0, model); Input(1, phase); Input(2, new GH_String("x"), new GH_String("Q"));
            c.GetType().GetField("_run", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(c, true);
            BridgeSegmentExcelTests.Solve(doc, true);
            string expected = "Action Q: FEM properties changed after the local section-state assignment. Reapply Bridge State Override and recalculate.";
            var errors = c.RuntimeMessages(GH_RuntimeMessageLevel.Error).ToArray();
            Check(errors.Contains(expected), "A08: a stale section state met by the checks gives the message of the FEM path (" + string.Join(" / ", errors) + ")");
            Check(!c.Params.Output[0].VolatileData.AllData(true).Any(), "A08: no model on the refusal of a stale section state");
            count++;
        }
        // PAR (A10): Composite Beam Property records the kind the steel material declares (as Beam Property).
        if (Selected("BeamPropertyCompositeComponent"))
        {
            var concrete = Produce("MaterialConcreteComponent", new Dictionary<int, object> { [0] = "C35/45", [1] = 7 });
            var rebar = Produce("MaterialRebarComponent", new Dictionary<int, object> { [0] = "B450C" });
            var slab = Produce("BeamSectionConcreteSlabComponent", new Dictionary<int, object> { [0] = 250.0, [1] = 2500.0, [2] = 1000.0, [3] = 50.0, [4] = 800.0, [5] = 50.0 });
            var declared = (IGH_Goo)T("Fem.GH_Material").GetMethod("FromGpc").Invoke(null, new object[] { Prop(steel, "Gpc"), "Undefined" });
            for (int output = 1; output <= 4; output++)
            {
                var p = Produce("BeamPropertyCompositeComponent", new Dictionary<int, object> { [0] = "CB", [1] = slab, [2] = section, [3] = concrete, [4] = declared, [5] = rebar }, output);
                var kind = p == null ? null : ((BridGHe.Common.Gpc.Legacy.LegacyBeamProperty)Prop(p, "Gpc")).Data.MaterialKind;
                Check(kind == "Undefined", $"Composite Beam Property, output {output}: the declared kind of the steel material ({kind ?? "no output"})");
            }
            count++;
        }
    }

    /// <summary>L3a: the regression of the section «models» (<c>gpc-only-models</c>).</summary>
    internal static void Models() => Regression("models");

    /// <summary>L3e (A05): a model of plates with a non-structural mass, and (A06) a moving path with a heat source, in the units of the definition.</summary>
    private static BridGHe.Common.MovingLoads.MovingPath HeatPath() => new BridGHe.Common.MovingLoads.MovingPath
    {
        LoadCaseName = "Traffic", Divisions = 5, Target = 2, Coordinates = new double[] { 0, 0, 0, 1, 0, 0, 0, 1, 0 },
        Template = new BridGHe.Common.MovingLoads.MovingTemplate { Name = "Heat", Vehicles = new[] { new BridGHe.Common.MovingLoads.MovingVehicle { Name = "H", Loads = new BridGHe.Common.MovingLoads.MovingObject[]
            { new BridGHe.Common.MovingLoads.MovingPointForce { Force = new double[] { 0, 0, -1 } }, new BridGHe.Common.MovingLoads.MovingHeatSource { X = 0.1, Y = 0.2, SizeX = 0.3, SizeY = 0.4, Heat = 5 } } } } },
    };

    /// <summary>P2CA (13/11/2026, coordinator decision 1): the plate of the example 03 given by its GPC draft (R0) with the non-structural mass of
    /// Plate NS Mass in the stage-less «Dead» case (mass 0.2, dynamic factor 1.1, offset 0.01, -0.02, 0.03), its GPC twin of the former FEM plate.</summary>
    private static IGH_Goo MassPlate() => Produce("PlateNSMassComponent", new Dictionary<int, object> { [0] = R2aDesignFixtures.Plate(Ex("03-plate-patch").Plates.Single()),
        [1] = R2aDesignFixtures.Case(BridGHe.Common.Gpc.Builders.GpcCaseBuilders.Case(BridGHe.Common.Gpc.Builders.GpcCaseBuilders.Dead, false)), [2] = 0.2, [3] = 1.1, [4] = new Rhino.Geometry.Vector3d(0.01, -0.02, 0.03) });

    /// <summary>L3e (A05): Build Model of plates with a non-structural mass outside N-mm: the GPC-only path. P2CA: the plate by its GPC twin (<see cref="MassPlate"/>).</summary>
    private static List<(string Type, string Label, Dictionary<int, object> Inputs)> Lane3eCases()
    {
        var cases = new List<(string, string, Dictionary<int, object>)>();
        foreach (var (units, index) in new[] { ("Knm", 0), ("SI", 1), ("IPS", 3) })
            cases.Add(("BuildModelComponent", "A05 " + units, new Dictionary<int, object> { [0] = new object[] { MassPlate() }, [1] = "A05", [2] = index }));
        return cases;
    }

    /// <summary>L3e (A05, A06): the values in N, mm, t of the plate non-structural mass and of the moving heat source, as 0.3.0 wrote them in the units of its files.
    /// P2CA (13/11/2026, coordinator decision 1): on GPC alone, Build Model on the plate given by its draft (<see cref="MassPlate"/>) in the units of the
    /// definition, then Attach Moving Paths (GpcModelTransforms.ReplacePaths); the same check on the conversion of the FEM value is retired by name.</summary>
    private static void Lane3eChecks(List<string> failures, ref int count)
    {
        bool keep = gpcInputs; gpcInputs = true; // the producers with the switches of P2 on, as the in-memory cases
        try
        {
            foreach (var (units, index, length, mass, power) in new[] { ("Nmm", 2, 1.0, 1.0, 1000.0), ("Knm", 0, 1000.0, 1e-6, 1000.0), ("SI", 1, 1000.0, 1e-9, 1000.0),
                ("IPS", 3, 25.4, 4.5359237e-4 / (25.4 * 25.4), 1055.056 * 1000) })
            {
                var model = Produce("BuildModelComponent", new Dictionary<int, object> { [0] = new object[] { MassPlate() }, [1] = "L3e " + units, [2] = index });
                if (Prop(model, "GpcInput") is not BridGHe.Common.Gpc.GpcModels.Conversion built) { failures.Add($"A05 {units}: Build Model gives no GPC input"); continue; }
                var input = BridGHe.Common.Gpc.GpcModelTransforms.ReplacePaths(built, new[] { HeatPath() }, BridGHe.Common.Gpc.GpcIdentity.Derive(BridGHe.Common.Gpc.GpcIdentity.NameSpace, "test-l3e:" + units));
                var m = input.Model.AreaElements.Values.Single().Assignments.Physical.NonStructuralMasses.Single();
                var h = input.Model.MovingLoads.Paths.Single().Template.Vehicles.Single().HeatSources.Single();
                bool Near(double a, double b) => Math.Abs(a - b) <= 1e-12 * Math.Abs(b);
                if (!Near(m.MassPerArea, 0.2 * mass) || !Near(m.GlobalOffset.X, 0.01 * length) || !Near(m.GlobalOffset.Y, -0.02 * length) || !Near(m.GlobalOffset.Z, 0.03 * length) || m.DynamicFactor != 1.1)
                    failures.Add($"A05 {units} (Attach Moving Paths): {m.MassPerArea} {m.DynamicFactor} {m.GlobalOffset}");
                if (!Near(h.X, 0.1 * length) || !Near(h.Y, 0.2 * length) || !Near(h.SizeX, 0.3 * length) || !Near(h.SizeY, 0.4 * length) || !Near(h.Power, 5 * power))
                    failures.Add($"A06 {units} (Attach Moving Paths): {h.X} {h.Y} {h.SizeX} {h.SizeY} {h.Power}");
                count++;
            }
        }
        finally { gpcInputs = keep; }
    }

    private static void Replace(this List<string> list, Func<string, string> map) { for (int i = 0; i < list.Count; i++) list[i] = map(list[i]); }

    private static string BridgeLinesAsSet(string text)
    {
        var lines = text.Split('\n'); int first = Array.FindIndex(lines, l => l.StartsWith("BRIDGE BEAM ", StringComparison.Ordinal));
        if (first < 0) return text;
        int count = lines.Skip(first).TakeWhile(l => l.StartsWith("BRIDGE BEAM ", StringComparison.Ordinal)).Count();
        var sorted = lines.Skip(first).Take(count).Select(l => System.Text.RegularExpressions.Regex.Replace(l, "^BRIDGE BEAM G[0-9]+ ", "BRIDGE BEAM ")).OrderBy(l => l, StringComparer.Ordinal);
        return string.Join("\n", lines.Take(first).Concat(sorted).Concat(lines.Skip(first + count)));
    }

    /// <summary>L3e: where the analyses of the rehearsal write (null outside the rehearsal: the analyses are never pressed).</summary>
    private static string rehearsalFolder;

    /// <summary>L3e: the analysed GPC models of two outputs compared: fingerprint, name, datasets, catalog and every result row the components read, in exact
    /// text (the serialized files differ in the dataset GUIDs of each import). Null when equal, otherwise the first difference.</summary>
    internal static string ResultsDifference(object fem, object gpc) => ResultsDifference(fem, gpc, true, out _);

    /// <summary>R1chain (12/11): <see cref="ResultsDifference(object, object)"/>; with <paramref name="identities"/> false, without the analysis fingerprint.
    /// The fingerprint holds the GUID of the model and of every element (<c>ModelRevisions.AnalysisFingerprint</c>), so an analysis whose input is built
    /// inside the chain of the rehearsal, downstream of another analysis on the path of P2 (identities derived from the drafts), has another fingerprint by
    /// construction; its name, dataset keys, catalog and every result row the components read, elements by number, are compared all the same (R4c-moving,
    /// task 2: <c>R4cChainDiagnosis.RowsDifference</c>). <paramref name="rows"/>: the result rows compared.</summary>
    internal static string ResultsDifference(object fem, object gpc, bool identities, out long rows)
    {
        rows = 0;
        var a = Prop(fem, "GpcResults") as GPC.Model.Models.Model; var b = Prop(gpc, "GpcResults") as GPC.Model.Models.Model;
        if (a == null || b == null) return "an output without results catalog: FEM-backed " + (a != null) + ", GPC-only " + (b != null);
        if (identities && GPC.Model.Models.ModelRevisions.AnalysisFingerprint(a) != GPC.Model.Models.ModelRevisions.AnalysisFingerprint(b)) return "the analysis fingerprint";
        if (a.Name != b.Name) return "the name: '" + a.Name + "' -> '" + b.Name + "'";
        if (!a.Datasets.Keys.SequenceEqual(b.Datasets.Keys)) return "the dataset keys";
        var p = BridGHe.Common.Gpc.GpcResultCatalog.Of(a); var q = BridGHe.Common.Gpc.GpcResultCatalog.Of(b);
        static string E(BridGHe.Common.Gpc.GpcResultCatalog c) => c.Units.Units + " " + string.Join(";", c.Entries.Select(e => e.Label + "|" + e.Case + "|" + e.Step + "|" + e.Phase));
        if (E(p) != E(q)) return "the catalog: " + E(p) + " -> " + E(q);
        var first = ResultRows(p, a).GetEnumerator(); var second = ResultRows(q, b).GetEnumerator();
        while (true)
        {
            bool one = first.MoveNext(), two = second.MoveNext();
            if (one != two) return "the number of result rows, after " + rows;
            if (!one) return rows == 0 ? "no result rows" : null;
            rows++;
            if (first.Current != second.Current) return "result row " + rows + ": " + first.Current + " -> " + second.Current;
        }
    }

    /// <summary>L3e: every result row the components read, in exact text (the angles of the beams come from the catalog).</summary>
    private static IEnumerable<string> ResultRows(BridGHe.Common.Gpc.GpcResultCatalog c, GPC.Model.Models.Model m)
    {
        static string V(double v) => v.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
        static string Vs(IEnumerable<double> v) => v == null ? "-" : string.Join(" ", v.Select(V));
        foreach (var n in m.NodesElements.Values.OrderBy(e => e.Id)) foreach (var r in BridGHe.Common.Gpc.GpcResultRows.Node(c, m, n))
            yield return "N " + n.Id + " " + r.Entry + " " + Vs(r.Displacement) + " / " + Vs(r.Reaction);
        foreach (var b in m.BeamElements.Values.OrderBy(e => e.Id)) foreach (var r in BridGHe.Common.Gpc.GpcResultRows.Beam(c, m, b))
            yield return "B " + b.Id + " " + r.Entry + " " + V(r.Station) + " " + Vs(new[] { r.Axial, r.Shear1, r.Shear2, r.Torque, r.Bending1, r.Bending2 });
        foreach (var s in m.AreaElements.Values.OrderBy(e => e.Id)) foreach (var r in BridGHe.Common.Gpc.GpcResultRows.Plate(c, m, s))
            yield return "P " + s.Id + " " + r.Entry + " " + r.Node + " " + Vs(r.Values) + " " + Vs(new[] { r.ForceMax, r.ForceMin, r.ForceAngle, r.ForceVonMises, r.MomentMax, r.MomentMin, r.MomentAngle });
    }



    /// <summary>L3e: the rehearsal of P2 with real results on a definition analysed by Straus7 (native 99 or 50, with BRIDGHE_GPC_ONLY_REHEARSAL=1; never in
    /// the gate). <b>R6.6 lane R1 (task 2): the real chain.</b> The analyses of the definition and every object downstream of them (Decompose Model, Super
    /// Element Filter, Composite Beam, Build BridGHe Model, Super Element Check, the plots, the report, the consumers, the relay parameters) are copied into
    /// one document wired as in the definition; every datum that enters it from outside is given by its GPC datum alone (<see cref="GpcOnly"/>: the models
    /// by <c>FromGpc(input)</c>), the analyses are pressed again on their GPC-only input (Output Path in TEMP), and the document is solved once with the
    /// switches of P2 on: every consumer takes the real GPC-only output of the analysis upstream of it, not a conversion of the FEM output of the definition.
    /// Checks: every analysis of the chain against the analysis of the definition (the analysed model and every result row, bit for bit); every object of the
    /// section without a FEM value in its outputs, without an error the definition has not, with outputs where the definition has them; TRANSITIONAL (until
    /// P2) the same messages and outputs modulo GUIDs as the definition. Then the chain written and read back (the saved results re-attached after reopening:
    /// the same outputs, no analysis pressed) and with the results missing (an empty Output Path: no output with a FEM value, no result read). The two
    /// questions L3E left open are printed: the case origin of WIND in Build BridGHe Model and <c>Shape2d</c> in the output of the analyses.</summary>
    internal static void Rehearsal(GH_Document doc, string label)
    {
        if (Environment.GetEnvironmentVariable("BRIDGHE_GPC_ONLY_REHEARSAL") != "1") return;
        Chain(doc, label, press: true);
    }

    /// <summary>R1: an analysis that does not solve again: its Keep Running input off; with <paramref name="folder"/>, its Output Path moved into it (a folder
    /// without results).</summary>
    private static void Unsolved(GH_Component c, string folder)
    {
        foreach (var p in c.Params.Input.Where(p => p.Name.Replace(" ", "").Equals("KeepRunning", StringComparison.OrdinalIgnoreCase) || p.Name.Equals("Run", StringComparison.OrdinalIgnoreCase)))
        { var off = new GH_Structure<IGH_Goo>(); off.Append(new GH_Boolean(false), new GH_Path(0)); SetInput(p, off, g => g); }
        if (folder == null) return;
        Directory.CreateDirectory(folder);
        var path = c.Params.Input.First(p => p.Name.Equals("Output Path", StringComparison.OrdinalIgnoreCase));
        var tree = new GH_Structure<IGH_Goo>(); tree.Append(new GH_String(Path.Combine(folder, (c.NickName ?? "analysis").Replace(' ', '-') + ".st7")), new GH_Path(0)); SetInput(path, tree, g => g);
    }

    /// <summary>R1: the chain of <see cref="Rehearsal"/>. <paramref name="press"/> false (the non-native diagnosis <c>BRIDGHE_R1_CHAIN_DRY</c>): the analyses
    /// are not pressed (their Output Path in an empty TEMP folder: no result, no Straus7), the chain is wired and solved, only the checks without results run.</summary>
    private static void Chain(GH_Document doc, string label, bool press)
    {
        Init("models"); modelsLane = true;
        selected = Environment.GetEnvironmentVariable("BRIDGHE_GPC_ONLY_TYPES") is { Length: > 0 } named ? new HashSet<string>(named.Split(','), StringComparer.Ordinal) : null;
        var kinds = new HashSet<string>(ElementGoo.Concat(PropertyGoo).Concat(ModelGoo), StringComparer.Ordinal);
        var types = Lane3aTypes.Concat(Lane3bTypes).Concat(Lane3cTypes).Concat(Lane3dTypes).Concat(Lane3Consumers).Where(Selected).ToHashSet(StringComparer.Ordinal);
        var analysisTypes = new[] { "StrausR3AnalysisComponent", "MovingStaticAnalysisComponent" };
        var byType = new SortedDictionary<string, (int Instances, int Items)>(StringComparer.Ordinal); var failures = new List<string>(); var transitionalDiffs = new List<string>();
        var exclusions = new List<string>(); // R1chain: the declared exclusions applied (not failures)
        var analyses = doc.Objects.OfType<GH_Component>().Where(c => analysisTypes.Contains(c.GetType().Name) && Selected(c.GetType().Name)).ToList();
        // Downstream of the analyses: every object that reads an output of an analysis, through any number of objects.
        var down = new List<IGH_DocumentObject>(); var seen = new HashSet<IGH_DocumentObject>(analyses);
        var queue = new Queue<IGH_DocumentObject>(analyses);
        static IEnumerable<IGH_Param> Outputs(IGH_DocumentObject o) => o is IGH_Component c ? c.Params.Output : o is IGH_Param p ? new[] { p } : Enumerable.Empty<IGH_Param>();
        static IEnumerable<IGH_Param> Inputs(IGH_DocumentObject o) => o is IGH_Component c ? c.Params.Input : o is IGH_Param p ? new[] { p } : Enumerable.Empty<IGH_Param>();
        while (queue.Count > 0)
            foreach (var output in Outputs(queue.Dequeue()))
                foreach (var r in output.Recipients)
                {
                    var top = r.Attributes?.GetTopLevel?.DocObject;
                    if (top != null && seen.Add(top)) { down.Add(top); queue.Enqueue(top); }
                }
        var objects = analyses.Cast<IGH_DocumentObject>().Concat(down).ToList();
        string folder = Path.Combine(Path.GetTempPath(), "bridghe-r1-chain-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder);
        rehearsalFolder = press ? folder : null;
        // The copies, then the wires: a source inside the chain is wired to its copy; a source outside gives its data by their GPC data alone (a relay holding them).
        var chain = new GH_Document();
        var copies = new Dictionary<IGH_DocumentObject, IGH_DocumentObject>();
        IGH_Param Mapped(IGH_Param source)
        {
            var top = source.Attributes.GetTopLevel.DocObject;
            if (!copies.TryGetValue(top, out var copy)) return null;
            return copy is IGH_Component c ? c.Params.Output[((IGH_Component)top).Params.Output.IndexOf(source)] : (IGH_Param)copy;
        }
        int external = 0;
        try
        {
            foreach (var o in objects)
            {
                var archive = new GH_Archive();
                if (!archive.AppendObject((GH_DocumentObject)o, "object")) throw new Exception("GPC-ONLY REHEARSAL: " + o.Name + " not written");
                var copy = Grasshopper.Instances.ComponentServer.EmitObject(o.ComponentGuid);
                copy.CreateAttributes();
                if (!archive.ExtractObject((GH_IO.GH_ISerializable)copy, "object")) throw new Exception("GPC-ONLY REHEARSAL: " + o.Name + " not read");
                chain.AddObject(copy, false);
                copies[o] = copy;
            }
            foreach (var o in objects)
            {
                var originalInputs = Inputs(o).ToList(); var copyInputs = Inputs(copies[o]).ToList();
                for (int i = 0; i < originalInputs.Count; i++)
                {
                    var input = originalInputs[i]; var target = copyInputs[i];
                    target.RemoveAllSources();
                    if (input.Sources.Count == 0)
                    {
                        if (o is IGH_Param) continue; // a relay parameter of the definition without a source keeps its own data
                        SetInput(target, input.VolatileData, g => GpcOnly(g, kinds)); continue;
                    }
                    foreach (var source in input.Sources)
                    {
                        var inside = Mapped(source);
                        if (inside != null) { target.AddSource(inside); continue; }
                        var relay = new Grasshopper.Kernel.Parameters.Param_GenericObject(); relay.CreateAttributes(); chain.AddObject(relay, false);
                        SetInput(relay, source.VolatileData, g => GpcOnly(g, kinds)); target.AddSource(relay); external++;
                    }
                }
                if (copies[o] is GH_Component component && o is GH_Component original) PrepareModels(component, original);
            }
            // The dry chain: no analysis solved (locked: no Straus7, whatever its Keep Running), its Output Path in the empty folder.
            if (!press) foreach (var a in copies.Values.OfType<GH_Component>().Where(c => analysisTypes.Contains(c.GetType().Name))) { Unsolved(a, Path.Combine(folder, "dry")); a.Locked = true; }
            using (R2aDesignFixtures.WithoutFem()) { chain.Enabled = true; chain.NewSolution(false, GH_SolutionMode.Silent); }
        }
        finally { rehearsalFolder = null; }
        Console.WriteLine($"GPC-ONLY rehearsal {label}: chain of {analyses.Count} analyses and {down.Count} objects downstream, {external} inputs from outside by their GPC data alone" + (press ? "" : " (dry: no analysis pressed)"));
        // The analyses of the chain against the analyses of the definition.
        foreach (var a in analyses)
        {
            string where = label + ": " + a.Name + " [" + a.NickName + "]";
            var fem = new Run(); Collect((GH_Component)a, fem, kinds);
            var gpc = new Run(); Collect((GH_Component)copies[a], gpc, kinds);
            var (n, items) = byType.TryGetValue(a.GetType().Name, out var v) ? v : (0, 0); byType[a.GetType().Name] = (n + 1, items + gpc.Outputs.Count);
            if (gpc.FemOutputs != 0) failures.Add(where + ": a FEM value in the output of the chain");
            if (!press) continue;
            if (gpc.Messages.Any(m => m.StartsWith("Error", StringComparison.Ordinal))) failures.Add(where + ": " + string.Join(" / ", gpc.Messages));
            if (fem.Goos.Count == 0 || gpc.Goos.Count == 0) { failures.Add(where + ": no output"); continue; }
            // R1chain (12/11): an analysis whose Model comes from a copy of the chain (on the 50: M02_NINF, M03_N0 and the moving analysis on M03_N0, whose
            // input Decompose Model of M01 → … → Build Model builds downstream of M01) takes the identities derived on the path of P2, not those of the
            // conversion of the FEM value of the definition: its analysis fingerprint (GUIDs of the model and of every element) differs by construction
            // (R4c-moving, task 2: 986 GUIDs of 986; without them 9142 result rows of 9142 equal). It is compared without the fingerprint: name, dataset keys,
            // catalog and every result row by element number. An analysis whose Model is the definition's own (from outside the chain, by FromGpc of the
            // same input: the same GUIDs; the 4 of the 99, M01 and M04 of the 50) keeps the comparison with the fingerprint.
            var model = a.Params.Input.First(p => p.Name == "Model");
            bool chainInput = model.Sources.Any(s => s.Attributes?.GetTopLevel?.DocObject is { } top && copies.ContainsKey(top));
            string difference = ResultsDifference(fem.Goos[0], gpc.Goos[0], !chainInput, out long rows);
            if (difference != null) failures.Add(where + ": analysed models differ" + (chainInput ? " (input built in the chain, compared without the identities)" : "") + ": " + difference);
            string mode = "with the analysis fingerprint";
            if (chainInput)
            {
                bool sameFingerprint = GPC.Model.Models.ModelRevisions.AnalysisFingerprint((GPC.Model.Models.Model)Prop(fem.Goos[0], "GpcResults"))
                    == GPC.Model.Models.ModelRevisions.AnalysisFingerprint((GPC.Model.Models.Model)Prop(gpc.Goos[0], "GpcResults"));
                mode = $"without the identities (input built in the chain; analysis fingerprint {(sameFingerprint ? "equal" : "different")})";
                // Reported, not failing (it compares with the conversion of the FEM value of the definition, as the TRANSITIONAL texts): the input of the analysis
                // against the input in the definition modulo GUIDs, under the declared identity rules of the replays (Text, BridgeLinesAsSet): only identities differ.
                Console.WriteLine($"GPC-ONLY rehearsal {where}: its input against the input of the definition modulo GUIDs (bridge data by value, reported): {InputDifference(a, (GH_Component)copies[a])}");
            }
            // L3E open item 2: Shape2d in the GPC input of the analysis output (the chain: the real output; the definition: the conversion of its FEM output).
            static bool Shape2d(IGH_Goo g) { try { return Prop(g, "GpcInput") is BridGHe.Common.Gpc.GpcModels.Conversion c && GpcInputComparison.Document(c.Model)?.Contains("Shape2d", StringComparison.Ordinal) == true; } catch (Exception) { return false; } }
            Console.WriteLine($"GPC-ONLY rehearsal {where}: analysed, {((GPC.Model.Models.Model)Prop(gpc.Goos[0], "Gpc"))?.Datasets.Count} datasets{(difference == null ? ", equal" : ", DIFFERENT")} {mode}, {rows} result rows; Shape2d in the GPC input: chain {Shape2d(gpc.Goos[0])}, definition {Shape2d(fem.Goos[0])}");
        }
        // Every object of the section in the chain against its instance of the definition.
        var outputsOf = new Dictionary<IGH_DocumentObject, Run>();
        foreach (var o in down.OfType<GH_Component>().Where(c => types.Contains(c.GetType().Name)))
        {
            string where = label + ": " + o.Name + " [" + o.NickName + "] (chain)";
            var fem = new Run(); Collect(o, fem, kinds);
            var gpc = new Run(); Collect((GH_Component)copies[o], gpc, kinds); outputsOf[o] = gpc;
            var (n, items) = byType.TryGetValue(o.GetType().Name, out var v) ? v : (0, 0); byType[o.GetType().Name] = (n + 1, items + gpc.Outputs.Count);
            if (gpc.FemOutputs != 0) failures.Add($"{where}: {gpc.FemOutputs} outputs with a FEM value");
            if (!press) continue;
            var errors = gpc.Messages.Where(m => m.StartsWith("Error", StringComparison.Ordinal)).ToList();
            // R1chain (12/11): a declared exclusion (a known product defect left to P2) replaces the checks of errors and outputs of that object only, and only
            // while every error it gives is the declared one; its TRANSITIONAL differences are still reported below.
            var exclusion = RehearsalExclusions.FirstOrDefault(x => x.Label == label && x.Type == o.GetType().Name);
            if (exclusion.Reason != null && errors.Count > 0 && errors.All(e => e.Contains(exclusion.Error, StringComparison.Ordinal)))
                exclusions.Add(where + ": " + exclusion.Reason);
            else
            {
                if (exclusion.Reason != null) Console.WriteLine($"GPC-ONLY rehearsal {where}: the declared exclusion is not applied (no {exclusion.Error} in its errors: {(errors.Count == 0 ? "none" : string.Join(" / ", errors))}); checked as any other object");
                if (errors.Except(fem.Messages).Any()) failures.Add(where + ": errors the definition has not: " + string.Join(" / ", errors));
                if (fem.Goos.Count > 0 && gpc.Goos.Count == 0) failures.Add(where + $": no output ({fem.Goos.Count} in the definition): " + string.Join(" / ", gpc.Messages));
            }
            // L3E open item 1: the case origin of WIND in the report of Build BridGHe Model.
            if (o.GetType().Name == "BuildBridgeModel")
                foreach (var (name, run) in new[] { ("chain", gpc), ("definition", fem) })
                    foreach (var line in run.Raw.SelectMany(t => t.Split('\n')).Where(l => l.StartsWith("ORIGIN WIND", StringComparison.Ordinal)).Distinct())
                        Console.WriteLine($"GPC-ONLY rehearsal {where}: {name}: {line}");
            if (!Transitional) continue;
            // TRANSITIONAL (until P2), reported and not failing: the same messages and outputs modulo GUIDs as the definition (the bridge lines of a model as
            // a set, L3e). The chain is the path of P2 from the analysis on; what differs from the FEM path of the definition is listed for P2 to decide.
            var a = fem.Outputs.ToList(); var b = gpc.Outputs.ToList(); a.Replace(BridgeLinesAsSet); b.Replace(BridgeLinesAsSet);
            if (!fem.Messages.SequenceEqual(gpc.Messages)) transitionalDiffs.Add(where + ": messages " + string.Join(" / ", fem.Messages) + " -> " + string.Join(" / ", gpc.Messages));
            if (!a.SequenceEqual(b))
            {
                var first = a.Zip(b).FirstOrDefault(p => p.First != p.Second);
                transitionalDiffs.Add($"{where}: outputs {a.Count} -> {b.Count}, {a.Zip(b).Count(p => p.First != p.Second)} different; first (definition -> chain):" + Environment.NewLine + Differences(first.First ?? "<none>", first.Second ?? "<none>"));
            }
        }
        foreach (var d in transitionalDiffs.Take(40)) Console.WriteLine("GPC-ONLY rehearsal TRANSITIONAL difference from the FEM path of the definition: " + d);
        Console.WriteLine($"GPC-ONLY rehearsal {label}: {transitionalDiffs.Count} TRANSITIONAL differences from the FEM path of the definition (reported, P2 decides)");
        // The chain written and read back: the analyses re-attach their saved results (no analysis pressed), the consumers give the same outputs; then the
        // results missing (an Output Path without files): no output with a FEM value.
        foreach (var variant in press ? new[] { "reopened", "results missing" } : new[] { "reopened" })
        {
            var archive = new GH_Archive(); archive.AppendObject(chain, "Definition");
            var reopened = new GH_Document();
            if (!archive.ExtractObject(reopened, "Definition")) { failures.Add(label + ": the chain is not read back"); continue; }
            // Reopened: no analysis solves again (Keep Running off; dry: locked), they re-attach the results saved beside their Output Path; the buttons
            // downstream (Build BridGHe Model, Super Element Check, the plots, the report) pressed again, as after opening a file. Results missing: the
            // Output Path of every analysis in a folder without files.
            var originalOf = copies.ToDictionary(p => p.Value.InstanceGuid, p => p.Key);
            foreach (var c in reopened.Objects.OfType<GH_Component>())
            {
                if (analysisTypes.Contains(c.GetType().Name)) { Unsolved(c, variant == "results missing" ? Path.Combine(folder, "missing") : null); if (!press) c.Locked = true; }
                else if (originalOf.TryGetValue(c.InstanceGuid, out var o) && o is GH_Component original) PrepareModels(c, original);
            }
            using (R2aDesignFixtures.WithoutFem()) { reopened.Enabled = true; reopened.NewSolution(false, GH_SolutionMode.Silent); }
            int same = 0, fem = 0, compared = 0;
            // Not compared after reopening: Preview BridGHe Model takes Display Settings, a display object and not a datum: the relay that holds it in the
            // chain is not saved, so the reopened preview has none («Connect Display Settings.», native 99, r1-t27); its data are compared in the chain.
            foreach (var (original, run) in outputsOf.Where(p => p.Key.GetType().Name != "PreviewModelComponent"))
            {
                var copy = reopened.Objects.OfType<GH_Component>().FirstOrDefault(c => c.InstanceGuid == copies[original].InstanceGuid);
                if (copy == null) { failures.Add($"{label} ({variant}): {original.Name} not in the chain read back"); continue; }
                var now = new Run(); Collect(copy, now, kinds); compared++;
                fem += now.FemOutputs;
                if (variant == "reopened" && now.Outputs.SequenceEqual(run.Outputs) && now.Messages.SequenceEqual(run.Messages)) same++;
                else if (variant == "reopened" && press) failures.Add($"{label} (reopened): {original.Name} [{original.NickName}]: other outputs or messages after reopening: {string.Join(" / ", now.Messages)}");
            }
            if (fem != 0) failures.Add($"{label} ({variant}): {fem} outputs with a FEM value");
            Console.WriteLine($"GPC-ONLY rehearsal {label} ({variant}): {compared} objects of the section, {same} with the same outputs and messages as the chain, {fem} outputs with a FEM value");
            reopened.Dispose();
        }
        chain.Dispose();
        foreach (var p in byType) Console.WriteLine($"GPC-ONLY rehearsal: {p.Key} {p.Value.Instances} instances, {p.Value.Items} output lines");
        foreach (var e in exclusions) Console.WriteLine("GPC-ONLY rehearsal DECLARED EXCLUSION (known, not a failure): " + e);
        foreach (var f in failures.Take(60)) Console.WriteLine("  " + f);
        if (Environment.GetEnvironmentVariable("BRIDGHE_GPC_ONLY_REPORT") is { Length: > 0 } report) File.WriteAllLines(report, failures);
        modelsLane = false;
        if (failures.Count != 0) throw new Exception($"GPC-ONLY rehearsal {label} FAIL: {failures.Count} differences or failures (first: {failures[0]})");
        Console.WriteLine($"GPC-ONLY rehearsal {label} PASS: the real chain of {analyses.Count} analyses and {byType.Where(p => !analysisTypes.Contains(p.Key)).Sum(p => p.Value.Instances)} objects of the section downstream"
            + (press ? " on real Straus7 results" : " (dry, no analysis)") + ", by their GPC data alone" + (exclusions.Count == 0 ? "" : $", {exclusions.Count} declared exclusion{(exclusions.Count == 1 ? "" : "s")}"));
    }

    /// <summary>R1chain (12/11): the declared exclusions of the rehearsal, known product defects left to P2, by rehearsal label and component type: the
    /// error that identifies the defect and the reason in one line. The 50 only: its Bridge Report throws an OutOfMemoryException also for the definition
    /// alone, without the chain (R4c-moving, task 2: 13.4 GB private of 63.5 GB available to the GC, so not the memory of the machine).</summary>
    private static readonly (string Label, string Type, string Error, string Reason)[] RehearsalExclusions =
    {
        ("50", "BridgeReportComponent", "System.OutOfMemoryException",
            "Bridge Report of the 50 REV003: OutOfMemoryException in BridgeReportData.Create (BridgeReportData.cs:173, the report data serialized into one .NET string beyond its maximum length, also for the definition alone): product defect left to P2"),
    };

    /// <summary>R1chain (12/11): the Model input of an analysis of the definition against that of its copy in the chain, by <see cref="Text"/> (modulo
    /// GUIDs, the bridge data by their values) and, as the TRANSITIONAL comparison of the chain does (declared rule L3e, identity only), with the
    /// «BRIDGE BEAM» lines as a set: they come in the order of the GUIDs of the beams (native 50, r1chain-b: 279111 lines of 279695 equal in text
    /// order, then the block of the 498 beams in another order). The outcome in words, with the first different line (cut).</summary>
    private static string InputDifference(GH_Component definition, GH_Component chain)
    {
        static string Cut(string v) => v.Length > 240 ? v[..240] + "…" : v;
        static string Lines(string s, string t)
        {
            var l = s.Split('\n'); var m = t.Split('\n');
            int at = Enumerable.Range(0, Math.Min(l.Length, m.Length)).FirstOrDefault(i => l[i] != m[i], -1);
            return $"{l.Length} -> {m.Length} lines, {l.Zip(m).Count(p => p.First != p.Second)} different" + (at < 0 ? "" : $"; first at line {at + 1}: '{Cut(l[at])}' -> '{Cut(m[at])}'");
        }
        try
        {
            static IGH_Goo One(GH_Component c) => c.Params.Input.First(p => p.Name == "Model").VolatileData.AllData(true).OfType<IGH_Goo>().SingleOrDefault();
            IGH_Goo x = One(definition), y = One(chain);
            if (x == null || y == null) return "not compared: an input missing (definition " + (x != null) + ", chain " + (y != null) + ")";
            string s = Text(x), t = Text(y);
            if (s == t) return "equal";
            string u = BridgeLinesAsSet(s), w = BridgeLinesAsSet(t);
            if (u == w) return "equal with the BRIDGE BEAM lines as a set (in the order of the GUIDs of the beams: in text order " + Lines(s, t) + ")";
            return "DIFFERENT, with the BRIDGE BEAM lines as a set: " + Lines(u, w);
        }
        catch (Exception ex) { return "not compared: " + ex.GetBaseException().GetType().Name + ": " + Cut(ex.GetBaseException().Message); }
    }

    /// <summary>L3a: the in-memory cases of the section: the types no example of the scope reaches with data (Composite Beam Self Tension and
    /// Composite Beam Linear Tension Analysis are in no example) and the paths the examples do not reach (Composite Beam with an end section and
    /// a group, the preview of elements). Their inputs come from the producer components solved in memory (FEM-backed with the datum attached,
    /// as in the examples): a beam of the example 02, the composite property of lane 2b.</summary>
    private static List<(string Type, string Label, Dictionary<int, object> Inputs)> ModelsCases()
    {
        // R1: the GPC twins of the elements of the examples (R0). P2CC (13/11/2026): alone; the models of the audits by their frozen payloads.
        var beam = R2aDesignFixtures.Beam(Ex("02-simply-supported").Beams[1]);
        var node = R2aDesignFixtures.Node(Ex("02-simply-supported").Nodes.First());
        var plate = R2aDesignFixtures.Plate(Ex("03-plate-patch").Plates.Single());
        var steel = Produce("MaterialSteelComponent", new Dictionary<int, object> { [0] = "S355", [1] = 2 });
        var concrete = Produce("MaterialConcreteComponent", new Dictionary<int, object> { [0] = "C35/45", [1] = 7 });
        var rebar = Produce("MaterialRebarComponent", new Dictionary<int, object> { [0] = "B450C" });
        var section = Produce("BeamSectionIComponent", new Dictionary<int, object> { [0] = 300.0, [1] = 250.0, [2] = 800.0, [3] = 20.0, [4] = 16.0, [5] = 12.0 });
        var deeper = Produce("BeamSectionIComponent", new Dictionary<int, object> { [0] = 300.0, [1] = 250.0, [2] = 1000.0, [3] = 20.0, [4] = 16.0, [5] = 12.0 });
        var slab = Produce("BeamSectionConcreteSlabComponent", new Dictionary<int, object> { [0] = 250.0, [1] = 2500.0, [2] = 1000.0, [3] = 50.0, [4] = 800.0, [5] = 50.0 });
        var composite = Produce("BeamPropertyCompositeComponent", new Dictionary<int, object> { [0] = "CB", [1] = slab, [2] = section, [3] = concrete, [4] = steel, [5] = rebar });
        var end = Produce("BeamPropertyCompositeComponent", new Dictionary<int, object> { [0] = "CB2", [1] = slab, [2] = deeper, [3] = concrete, [4] = steel, [5] = rebar });
        var group = Produce("GroupComponent", new Dictionary<int, object> { [0] = "Deck" });
        var compositeBeam = Produce("CompositeBeamComponent", new Dictionary<int, object> { [0] = beam, [1] = composite });
        // The forces of the Force component (N, mm): axial, shear 2, bending 3 at mid-span.
        IGH_Goo Force(string name, double n, double v, double m) => Produce("ForceComponent", new Dictionary<int, object> { [0] = name, [1] = n, [2] = v, [6] = m, [7] = 0.5 });
        var steelForces = Force("G1", -1.2e5, 8.0e4, 3.5e8); var longForces = Force("G2", -2.0e5, 6.0e4, 2.1e8); var shortForces = Force("Q", -0.5e5, 9.0e4, 4.4e8);
        // L3b: Bridge Model Audit outside N-mm (kN-m, the numbers through FromMm), with itself as a phase model; an isolated node and a second part (a beam far away) in N-mm.
        // R1: FEM-backed in both builds (TRANSITIONAL input). Its GPC twins (the frozen payload «beams-Knm» read by value, R3; the GPC inputs of the examples
        // 02 and 03, R0) give the audit another warning or line (gpc-only-models r1-t24: SECTION_AXIS_CHANGED on the payload read by value): listed for P2.
        var knm = FrozenKnm(); // P2CC: the frozen beam data in kN-m read by value (its replay key retired by name: the stale state of G14 differs)
        // L3b: the diagnostics of the audit, on both paths: a homogenised beam beside a structural plate (POSSIBLE_DOUBLE_CONCRETE, the mark from the recipe), a beam far from
        // the plate (DISCONNECTED, NO_SUPPORT), a tolerance above the lengths (INVALID_BEAM), phases of other units and geometry (PHASE_UNITS, PHASE_GEOMETRY_MISMATCH).
        // P2CC (13/11/2026): the examples 02 and 03 of the former FEM catalogue by their frozen payloads (p2cc-m2, p2cc-m3), read by value.
        IGH_Goo Example(string id) => FrozenModel(id == "02-simply-supported" ? "p2cc-m2" : "p2cc-m3");
        // P2F (12/11): with its nodes (a node at every plate corner and beam end): the GPC path builds no element without its end nodes (before,
        // the case stopped at «Nessun nodo FEM entro tolleranza»). Its former key of the P2R capture is retired by name (P2rRetired.ReplayKeys).
        // P2CC (13/11/2026): on its GPC twin: the nodes N1..N6 at the corners of the plate of the example 03 and at the far beam ends, the plate P1 on the
        // property of the example (R0), the beam labelled B1 on the homogenised property n 12 «P_composite» of the composite section of the grillage
        // (the GPC builder of Composite Beam Property), Build Model in N-mm with the tolerance of the former conversion (1e-3), no case.
        var corners = PatchCorners(); var far = new[] { (1e6, 0.0, 0.0), (1e6, 5000.0, 0.0) };
        var numbers = GpcCompositeDefinitions.Grillage(new BridgeGrillageExample.Parameters());
        var homogenised = BridGHe.Common.Gpc.Builders.GpcPropertyBuilders.Homogenised(GpcCompositeDefinitions.Create(numbers), 12, "P_composite", GpcCompositeDefinitions.AnalysisSteel(numbers), "Steel");
        var mixedNodes = corners.Concat(far).Select((c, i) => BridGHe.Common.Gpc.Builders.GpcNodeBuilders.Create(c.Item1, c.Item2, c.Item3, "N" + (i + 1), null, R1Id("3b-mixed-node:" + i))).ToList();
        var mixedPlate = BridGHe.Common.Gpc.Builders.GpcPlateBuilders.Create(corners, Patch(), null, 0, 0, "P1", R1Id("3b-mixed-plate"));
        var mixedBeam = BridGHe.Common.Gpc.Builders.GpcElementBuilders.WithLabel(BridGHe.Common.Gpc.Builders.GpcBeamBuilders.New(new GPC.Geometry.Point3d(far[0].Item1, far[0].Item2, far[0].Item3),
            new GPC.Geometry.Point3d(far[1].Item1, far[1].Item2, far[1].Item3), homogenised, null, 0, 0, 0, R1Id("3b-mixed-beam")), "B1", R1Id("3b-mixed-label"));
        var mixed = BridGHe.Common.Gpc.Legacy.LegacyBuildModel.Build("", new BridGHe.Common.Gpc.BridgeUnits(BridGHe.Common.Gpc.DefinitionUnits.Nmm), 1e-3,
            mixedNodes.Select(d => (d.Node.Guid, d)).ToList(), new List<(Guid, BridGHe.Common.Gpc.Legacy.LegacyBeamDraft)> { (mixedBeam.Beam.Guid, mixedBeam) },
            new List<(Guid, BridGHe.Common.Gpc.Legacy.LegacyPlateDraft)> { (mixedPlate.Plate.Guid, mixedPlate) }, new List<BridGHe.Common.Gpc.Legacy.LegacyCombinationDraft>(), new List<BridGHe.Common.Gpc.Legacy.LegacyStageDraft>());
        return new List<(string, string, Dictionary<int, object>)>
        {
            ("BridgeModelAuditComponent", "kN-m with itself as a phase model", new Dictionary<int, object> { [0] = knm, [1] = 1e-6, [3] = new object[] { knm } }),
            ("BridgeModelAuditComponent", "a homogenised beam beside a plate, far away", new Dictionary<int, object> { [0] = GpcModelGoo(mixed), [1] = 1e-6 }),
            ("BridgeModelAuditComponent", "tolerance above the beam lengths", new Dictionary<int, object> { [0] = Example("02-simply-supported"), [1] = 1e9 }),
            ("BridgeModelAuditComponent", "phases of other units and geometry", new Dictionary<int, object> { [0] = Example("02-simply-supported"), [1] = 1e-6,
                [3] = new object[] { knm, Example("03-plate-patch") } }),
            ("CompositeBeamComponent", "a beam of the example 02 and a composite property", new Dictionary<int, object> { [0] = beam, [1] = composite }),
            ("CompositeBeamComponent", "start and end sections and a group", new Dictionary<int, object> { [0] = beam, [1] = composite, [2] = end, [3] = new object[] { group } }),
            ("CompositeBeamSelfTensionComponent", "composite property, n 15, slab strain", new Dictionary<int, object> { [0] = composite, [1] = 15.0, [2] = -0.0002 }),
            ("CompositeShrinkageActionsComponent", "composite property, shrinkage strain", new Dictionary<int, object> { [0] = composite, [1] = -0.0003 }),
            ("CompositeBeamLinearTensionAnalysisComponent_OBSOLETE", "a composite beam and three phases", new Dictionary<int, object> { [0] = compositeBeam, [1] = steelForces, [2] = longForces,
                [3] = shortForces, [4] = 18.0, [5] = 7.0 }),
            ("PreviewModelComponent", "a node, a beam, a plate and a composite beam", new Dictionary<int, object> { [0] = new object[] { node, beam, plate, compositeBeam } }),
        };
    }

    /// <summary>L3c: the in-memory cases of sub-lane 3c: Merge Model (in no example: node merges with loads and Element IDs, ends and corners moved onto
    /// nodes or creating them, a beam of zero length, a plate with coincident corners and one with too few, a plate with an angle), Composite Beam Strain,
    /// Build Model (the same node given twice, created nodes, a combination and a stage, an unknown case).</summary>
    private static List<(string Type, string Label, Dictionary<int, object> Inputs)> Lane3cCases()
    {
        // P2CC (13/11/2026): built with the GPC twins of R1 alone (the elements by the product builders on the property of the examples, R0; the cases by
        // the case builder). The former BeamModel(id, ...) gave its id as an Element ID label (BridGHe ID empty): the twin is the beam with that label
        // (GpcElementBuilders.WithLabel), so the outputs equal the replay capture modulo GUIDs; the node with a rigid link by GpcBridgeGeometryBuilders.RigidNode.
        var lc = R2aDesignFixtures.Case(BridGHe.Common.Gpc.Builders.GpcCaseBuilders.Case("Merge", false));
        var lc2 = R2aDesignFixtures.Case(BridGHe.Common.Gpc.Builders.GpcCaseBuilders.Case("Other", false));
        static Rhino.Geometry.Point3d P(double x, double y) => new Rhino.Geometry.Point3d(x, y, 0);
        static GPC.Geometry.Point3d G(Rhino.Geometry.Point3d p) => new GPC.Geometry.Point3d(p.X, p.Y, p.Z);
        IGH_Goo N(double x, double y) => R2aDesignFixtures.Node(BridGHe.Common.Gpc.Builders.GpcNodeBuilders.Create(x, y, 0, "", null, R1Id("3c-node:" + x + ":" + y)));
        IGH_Goo B(string id, Rhino.Geometry.Point3d a, Rhino.Geometry.Point3d b) => R2aDesignFixtures.Beam(BridGHe.Common.Gpc.Builders.GpcElementBuilders.WithLabel(
            BridGHe.Common.Gpc.Builders.GpcBeamBuilders.New(G(a), G(b), GpcNativeExamples.Property(), null, 0, 0, 0, R1Id("3c-beam:" + id)), id, R1Id("3c-beam-label:" + id)));
        IGH_Goo Q(string id, params Rhino.Geometry.Point3d[] c) =>
            R2aDesignFixtures.Plate(BridGHe.Common.Gpc.Builders.GpcPlateBuilders.Create(c.Select(p => (p.X, p.Y, p.Z)).ToArray(), Patch(), null, 0, 0, id, R1Id("3c-plate:" + id)));
        var loaded = Produce("ElementIdComponent", new Dictionary<int, object> { [0] = Produce("NodeLoadComponent", new Dictionary<int, object> { [0] = N(0, 0), [1] = lc, [2] = new Rhino.Geometry.Vector3d(1, 0, -5) }), [1] = "N1" });
        var near = Produce("ElementIdComponent", new Dictionary<int, object> { [0] = Produce("NodeMomentComponent", new Dictionary<int, object> { [0] = N(0.4, 0), [1] = lc, [2] = new Rhino.Geometry.Vector3d(0, 2, 0) }), [1] = "N2" });
        var other = N(5000, 0);
        var beam = Produce("BeamPointLoadComponent", new Dictionary<int, object> { [0] = B("B1", P(0.5, 0), P(5000, 0.3)), [1] = lc, [3] = 1.5, [5] = -2.0, [6] = 0.25 });
        var zero = B("B2", P(0, 0), P(0.4, 0)); var far = B("B3", P(5000, 4000), P(10000, 0));
        var quad = Q("P1", P(0, 0), P(5000, 0), P(5000, 4000), P(0, 4000));
        var triangle = Q("P2", P(5000.2, 0), P(10000, 0), P(5000, 4000), P(5000.1, 4000));
        var removed = Q("P3", P(0, 0), P(0.4, 0), P(0.2, 0.3));
        var angled = R2aDesignFixtures.Plate(BridGHe.Common.Gpc.Builders.GpcPlateBuilders.Create(new[] { (0.3, 0.0, 0.0), (5000.0, 0.2, 0.0), (5000.0, 4000.0, 0.0), (0.0, 4000.0, 0.0) }, Patch(), null, 30, 0, "P4", R1Id("3c-plate:P4")));
        var all = new object[] { loaded, near, other, beam, zero, far, quad, triangle, removed, angled };
        // The reason of the example 46 (ModelsWithoutGpcInput): a rigid link whose target has no node (P2CA: the node by the builder of the reader's form).
        var linked = R2aDesignFixtures.Node(BridGHe.Common.Gpc.Builders.GpcBridgeGeometryBuilders.RigidNode(P(1000, 0), "", P(7777, 0), R1Id("3c-linked")));
        var strained = Produce("CompositeBeamSlabStrainComponent", new Dictionary<int, object> { [0] = beam, [1] = lc, [2] = -2e-4 });
        var combination = Produce("LoadCombinationComponent", new Dictionary<int, object> { [0] = "ULS", [1] = new object[] { lc }, [2] = new object[] { 1.35 } });
        var unknown = Produce("LoadCombinationComponent", new Dictionary<int, object> { [0] = "ULS", [1] = new object[] { lc2 }, [2] = new object[] { 1.0 } });
        var group = Produce("GroupComponent", new Dictionary<int, object> { [0] = "Deck" });
        var stage = Produce("StagedConstructionComponent", new Dictionary<int, object> { [0] = "Stage 1", [1] = new object[] { group }, [2] = new object[] { combination }, [3] = false, [4] = true });
        return new List<(string, string, Dictionary<int, object>)>
        {
            ("MergeModelComponent", "merges, ends and corners moved, new nodes, zero length, coincident corners", new Dictionary<int, object> { [0] = all, [1] = 1.0 }),
            ("MergeModelComponent", "a small tolerance: new nodes only", new Dictionary<int, object> { [0] = all, [1] = 1e-6 }),
            ("MergeModelComponent", "a negative tolerance (error)", new Dictionary<int, object> { [0] = all, [1] = -1.0 }),
            ("MergeModelComponent", "beams only", new Dictionary<int, object> { [0] = new object[] { beam, far, zero }, [1] = 1.0 }),
            ("CompositeBeamSlabStrainComponent", "a loaded beam, a case and a strain", new Dictionary<int, object> { [0] = beam, [1] = lc, [2] = -2e-4 }),
            ("CompositeBeamSlabStrainComponent", "a second strain, in another case", new Dictionary<int, object> { [0] = strained, [1] = lc2, [2] = -1e-4 }),
            ("BuildModelComponent", "the same node twice, created nodes, a combination", new Dictionary<int, object> { [0] = new object[] { loaded, loaded, beam, far, angled }, [1] = "Merged", [2] = 2, [3] = new object[] { combination } }),
            ("BuildModelComponent", "a rigid link whose target has no node (error, as example 46)", new Dictionary<int, object> { [0] = new object[] { linked, beam }, [1] = "Link", [2] = 2 }),
            ("BuildModelComponent", "a stage", new Dictionary<int, object> { [0] = new object[] { loaded, beam }, [1] = "Staged", [2] = 2, [3] = new object[] { combination }, [4] = new object[] { stage } }),
            ("BuildModelComponent", "a combination of a case the elements do not have (error)", new Dictionary<int, object> { [0] = new object[] { loaded, beam }, [1] = "Unknown", [2] = 2, [3] = new object[] { unknown } }),
        };
    }

    /// <summary>L3d: the in-memory cases of sub-lane 3d: Bridge Phase Model and Bridge Construction Sequence (N-mm and kN-m, the refusals, native stages and the
    /// reasons they are omitted, a beam in two groups, rigid links, the girders of Bridge Member Assignment) and A09 (properties with the same name and other content,
    /// through Build Model, whose model carries the drafts).</summary>
    private static List<(string Type, string Label, Dictionary<int, object> Inputs)> Lane3dCases()
    {
        const string phaseModel = "BridgePhaseModelComponent", sequence = "BridgeConstructionSequenceComponent";
        // P2CC (13/11/2026, user decision «Congela»): the former in-memory FEM models, properties and beams by their 0.3.0 payloads frozen once
        // (Fixtures/lane3d-payloads, Lane3dPayloads), each read back by value equal to its FEM value when it was written: the example 02 with the
        // BridGHe IDs B1..B4 (named), the example 02 (m2) and with a rigid link (linked), three beams and a plate in the groups A, A\Child and B with
        // a registered case Q (mixed, two groups, ungrouped), the simple bridge of Bridge Member Assignment (bridge), the beam properties Stiff and
        // Stiff kN-m (E x 1.25), the composite one and Same (E x 1.25), the A09 beams; the kN-m beam data by beams-Knm of legacy-payloads.
        var phase = Produce("BridgeConstructionPhaseComponent", new Dictionary<int, object> { [0] = 10, [1] = "Cast", [2] = 0, [4] = 7.0 });
        var noDay = Produce("BridgeConstructionPhaseComponent", new Dictionary<int, object> { [0] = 20, [1] = "Open", [2] = 0 });
        IGH_Goo Named() => FrozenModel("p2cc-named");
        var namedIds = new object[] { "B1", "B2", "B3", "B4" }; // the BridGHe IDs of the frozen model (checked when it was written)
        var stiff = FrozenProperty("p2cc-stiff"); var knmStiff = FrozenProperty("p2cc-stiff-knm");
        var mixed = FrozenModel("p2cc-mixed"); var twoGroups = FrozenModel("p2cc-two-groups"); var ungrouped = FrozenModel("p2cc-ungrouped");
        // The cases Q and Other by their GPC twins (the case builder, as Lane 3c).
        var q = R2aDesignFixtures.Case(BridGHe.Common.Gpc.Builders.GpcCaseBuilders.Case("Q", false)); var other = R2aDesignFixtures.Case(BridGHe.Common.Gpc.Builders.GpcCaseBuilders.Case("Other", false));
        var increment = Produce("LoadCombinationComponent", new Dictionary<int, object> { [0] = "Cast", [1] = new object[] { q }, [2] = new object[] { 1.35 } });
        var unknownIncrement = Produce("LoadCombinationComponent", new Dictionary<int, object> { [0] = "Cast", [1] = new object[] { other }, [2] = new object[] { 1.0 } });
        IGH_Goo M2() => FrozenModel("p2cc-m2");
        const string supported = "1"; // the first supported node of the example 02 (checked when the fixture was written)
        var bridge = FrozenModel("p2cc-bridge"); var composite = FrozenProperty("p2cc-composite");
        // A09: the first beam of a name and two beams with the name and other content; through Build Model (the model carries the drafts).
        var shadowed = Produce("BuildModelComponent", new Dictionary<int, object> { [0] = new object[] { FrozenBeam("p2cc-a09-B1"), FrozenBeam("p2cc-a09-B2"), FrozenBeam("p2cc-a09-B3") }, [1] = "Shadowed", [2] = 2 });
        var same = FrozenProperty("p2cc-same");

        return new List<(string, string, Dictionary<int, object>)>
        {
            (phaseModel, "a beam on a replacement, loads cleared", new Dictionary<int, object> { [0] = Named(), [1] = "Phase", [2] = new object[] { "B1" }, [3] = stiff, [4] = true }),
            (phaseModel, "every beam, loads kept", new Dictionary<int, object> { [0] = Named(), [1] = "Phase", [2] = namedIds, [3] = stiff, [4] = false }),
            (phaseModel, "kN-m", new Dictionary<int, object> { [0] = FrozenKnm(), [1] = "Phase kN-m", [2] = new object[] { "2" }, [3] = knmStiff, [4] = false }),
            (phaseModel, "no name (error)", new Dictionary<int, object> { [0] = Named(), [1] = " ", [2] = new object[] { "B1" }, [3] = stiff, [4] = true }),
            (phaseModel, "a beam twice (error)", new Dictionary<int, object> { [0] = Named(), [1] = "Phase", [2] = new object[] { "B1", "B1" }, [3] = stiff, [4] = true }),
            (phaseModel, "an unknown beam (error)", new Dictionary<int, object> { [0] = Named(), [1] = "Phase", [2] = new object[] { "B99" }, [3] = stiff, [4] = true }),
            (sequence, "everything, no increments", new Dictionary<int, object> { [0] = mixed, [1] = phase }),
            (sequence, "everything with an increment: native stage", new Dictionary<int, object> { [0] = mixed, [1] = phase, [7] = new object[] { increment } }),
            (sequence, "a parent group with an increment: native stage", new Dictionary<int, object> { [0] = mixed, [1] = noDay, [2] = new object[] { "A" }, [7] = new object[] { increment } }),
            (sequence, "a group of a beam and a plate", new Dictionary<int, object> { [0] = mixed, [1] = phase, [2] = new object[] { "B" }, [7] = new object[] { increment } }),
            (sequence, "a child group: part of a parent", new Dictionary<int, object> { [0] = mixed, [1] = phase, [2] = new object[] { "A\\Child" }, [7] = new object[] { increment } }),
            (sequence, "a beam in two groups: no native stage", new Dictionary<int, object> { [0] = twoGroups, [1] = phase, [7] = new object[] { increment } }),
            (sequence, "a beam without a group: no native stage", new Dictionary<int, object> { [0] = ungrouped, [1] = phase, [7] = new object[] { increment } }),
            (sequence, "an increment of an unknown case (error)", new Dictionary<int, object> { [0] = mixed, [1] = phase, [7] = new object[] { unknownIncrement } }),
            (sequence, "an unknown group (error)", new Dictionary<int, object> { [0] = mixed, [1] = phase, [2] = new object[] { "Z" } }),
            (sequence, "an unknown physical member (error)", new Dictionary<int, object> { [0] = mixed, [1] = phase, [3] = new object[] { "Girder:9:9" } }),
            (sequence, "an override without members (error)", new Dictionary<int, object> { [0] = mixed, [1] = phase, [6] = stiff }),
            (sequence, "bearings kept: every one", new Dictionary<int, object> { [0] = M2(), [1] = phase }),
            (sequence, "bearings: one", new Dictionary<int, object> { [0] = M2(), [1] = phase, [4] = new object[] { supported } }),
            (sequence, "bearings: disabled", new Dictionary<int, object> { [0] = M2(), [1] = phase, [5] = false }),
            (sequence, "an unknown bearing (error)", new Dictionary<int, object> { [0] = M2(), [1] = phase, [4] = new object[] { "Nope" } }),
            (sequence, "a rigid link: no native stage", new Dictionary<int, object> { [0] = FrozenModel("p2cc-linked"), [1] = phase }),
            (sequence, "girders of Bridge Member Assignment: a group, override, no bearings", new Dictionary<int, object> { [0] = bridge, [1] = phase, [2] = new object[] { "Girder\\Group1" }, [5] = false, [6] = composite }),
            (sequence, "girders of Bridge Member Assignment: a physical member", new Dictionary<int, object> { [0] = bridge, [1] = phase, [3] = new object[] { "Girder:2:1" } }),
            (phaseModel, "A09: the first beam of a name replaced: the next registers its own property", new Dictionary<int, object> { [0] = shadowed, [1] = "A09", [2] = new object[] { "B1" }, [3] = stiff, [4] = false }),
            (phaseModel, "A09: the replacement takes the name", new Dictionary<int, object> { [0] = shadowed, [1] = "A09", [2] = new object[] { "B1" }, [3] = same, [4] = false }),
            (sequence, "A09: the first beam of a name inactive", new Dictionary<int, object> { [0] = shadowed, [1] = phase, [2] = new object[] { "B" } }),
        };
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
        // R1: the lists without GPC twins yet (Lane3c, Lane3d, Lane3e: FEM models built in memory) are built with their FEM-backed inputs in both builds
        // (no producer solved with the switches of P2 between FEM-backed data): listed as TRANSITIONAL inputs.
        List<(string Type, string Label, Dictionary<int, object> Inputs)> Off(Func<List<(string, string, Dictionary<int, object>)>> list)
        { bool keep = gpcInputs; gpcInputs = false; try { return list(); } finally { gpcInputs = keep; } }
        // A list whose build without FEM fails (a producer refuses on the path of P2): listed, built with its FEM-backed inputs (TRANSITIONAL).
        List<(string Type, string Label, Dictionary<int, object> Inputs)> Safe(string name, Func<List<(string, string, Dictionary<int, object>)>> list)
        {
            if (!gpcInputs) return list();
            try { return list(); }
            catch (Exception ex) { femInputs.Add(name + ": the build without FEM fails (" + ex.GetBaseException().Message + Frame(ex) + ")"); return Off(list); }
        }
        List<(string Type, string Label, Dictionary<int, object> Inputs)> Cases() => lane == "native" ? Safe("native sections", NativeSectionCases).Concat(Safe("parity native", ParityNativeCases)).ToList()
            : lane == "properties" ? Safe("lane 1", Lane1Cases).Concat(Safe("lane 1b", Lane1bCases)).Where(OfLane2).Concat(Safe("lane 2a", Lane2aCases)).Concat(Safe("lane 2b", Lane2bCases))
                .Concat(Safe("lane 2c", Lane2cCases)).Concat(Safe("parity", ParityCases)).ToList()
            : lane == "generators" ? Safe("lane 1b", Lane1bCases) : lane == "models" ? Safe("lane 3a", ModelsCases).Concat(Safe("lane 3c", Lane3cCases)).Concat(Off(Lane3dCases)).Concat(Safe("lane 3e", Lane3eCases)).ToList()
            : Safe("lane 1", Lane1Cases);
        // P2CA (13/11/2026, coordinator decision 1): every case list built once, with the inputs built without any FEM value (the double build with
        // the FEM-backed inputs of today is gone: since P2R the FEM-backed copy was solved only for its type name, the replay solves the GPC copy).
        List<(string Type, string Label, Dictionary<int, object> Inputs)> built;
        gpcInputs = true;
        try { built = Cases().Where(c => Selected(c.Type)).ToList(); }
        finally { gpcInputs = false; }
        var all = built;
        var allKinds = new HashSet<string>(ElementGoo.Concat(PropertyGoo).Concat(ModelGoo), StringComparer.Ordinal);
        GH_Component Solved(GH_Document doc, string type, Dictionary<int, object> inputs)
        {
            var c = BridgeSegmentExcelTests.Add(doc, type);
            foreach (var input in inputs)
            {
                var tree = new GH_Structure<IGH_Goo>();
                var path = new GH_Path(0);
                foreach (var v in input.Value is object[] list ? list : new[] { input.Value }) tree.Append(Goo(v), path);
                SetInput(c.Params.Input[input.Key], tree, g => g);
            }
            BridgeSegmentExcelTests.Solve(doc, true);
            return c;
        }
        for (int k = 0; k < all.Count; k++)
        {
            var (type, label, inputs) = all[k];
            using var gpcDoc = new GH_Document();
            var g = Solved(gpcDoc, type, inputs); var c = g;
            // The inputs of the GPC-only copy: built without FEM, or (listed) a datum still taken from a FEM value (its GPC-only form by GpcOnly).
            var fem = g.Params.Input.SelectMany(p => p.VolatileData.AllData(true)).Where(x => x != null && allKinds.Contains(x.GetType().Name) && HasFem(x)).Select(x => x.GetType().Name).Distinct().ToList();
            if (fem.Count == 0) oracles.GpcBuilt++; else femInputs.Add(c.Name + " (" + label + "): " + string.Join(", ", fem));
            // The element attributes, loads and axes (lane 1): one output element for each input element (explicit, from the inputs of the case).
            var expect = Expect.Of(label);
            if (expect.What == Expect.Kind.Output && OneToOneTypes.Contains(type) && inputs.TryGetValue(0, out var first))
                expect = new Expect { What = expect.What, Message = expect.Message, Outputs = new[] { first is object[] list ? list.Length : 1 } };
            Replay(c, "in memory: " + c.Name + " (" + label + ")", kinds, byType, failures, expect: expect, gpcSource: g);
            count++;
        }
    }

    /// <summary>R1: the instances where the GH GUID of a GPC-only composite beam changed from one solve to the next (a product defect, reported).</summary>
    private static readonly List<string> compositeGuidDefects = new List<string>();

    /// <summary>R1: the components that give one output element for each input element (attributes, loads, axes of lane 1).</summary>
    private static readonly HashSet<string> OneToOneTypes = new HashSet<string>(Lane1Types.Take(24).Concat(new[] { "AlignBeamAxesComponent", "AlignPlateAxesComponent" }), StringComparer.Ordinal);

    /// <summary>R1: the in-memory cases whose inputs still carry a FEM value in the build without FEM (no GPC twin yet: the GPC-only copy takes
    /// their datum by <see cref="GpcOnly"/>), listed in the report.</summary>
    private static readonly List<string> femInputs = new List<string>();

    /// <summary>L2B: the component types named by <c>BRIDGHE_GPC_ONLY_TYPES</c> (a comma list; diagnosis of a lane in progress only: the
    /// regression replays every type), or all.</summary>
    private static HashSet<string> selected;
    private static bool Selected(string type) => selected == null || selected.Contains(type);

    internal static int Regression(string lane)
    {
        // L2B: lane 2 replays 2a and 2b together (no type declared pending).
        // L3a: the section «models» (lane 3) converts every kind.
        var types = lane switch { "elements" => Lane1Types, "generators" => Lane1bTypes, "properties" => Lane2Types, "models" => Lane3aTypes.Concat(Lane3bTypes).Concat(Lane3cTypes).Concat(Lane3dTypes).Concat(Lane3Consumers).ToArray(),
            _ => throw new ArgumentException("Unknown lane: " + lane) };
        var kinds = new HashSet<string>(lane switch { "elements" or "generators" => ElementGoo, "properties" => PropertyGoo, "models" => ElementGoo.Concat(PropertyGoo).Concat(ModelGoo).ToArray(),
            _ => Array.Empty<string>() }, StringComparer.Ordinal);
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
        modelsLane = lane == "models";
        // P2R step 0: the replay capture only for a full regression (no subset of types or files, not the dry chain).
        captureLane = selected == null && string.IsNullOrEmpty(Environment.GetEnvironmentVariable("BRIDGHE_GPC_ONLY_FILES"))
            && string.IsNullOrEmpty(Environment.GetEnvironmentVariable("BRIDGHE_R1_CHAIN_DRY")) ? "gpc-only-" + lane : null;
        // R1 (diagnosis only, not in the gate): BRIDGHE_R1_CHAIN_DRY=<definition relative to example> wires and solves the chain of the rehearsal on that
        // definition without any analysis solved (the analyses locked in the definition and in the chain: no Straus7), to check the chain without natives.
        if (modelsLane && Environment.GetEnvironmentVariable("BRIDGHE_R1_CHAIN_DRY") is { Length: > 0 } dry)
        {
            string file = Path.GetFullPath(Path.Combine(ExamplePaths.Root, dry));
            using var definitionDoc = GrasshopperBridgeExample.Read(file); definitionDoc.FilePath = file;
            foreach (var o in definitionDoc.Objects.OfType<IGH_ActiveObject>()) if (o.GetType().Name is "StrausR3AnalysisComponent" or "MovingStaticAnalysisComponent") o.Locked = true;
            BridgeSegmentExcelTests.Solve(definitionDoc, true);
            Chain(definitionDoc, Path.GetRelativePath(ExamplePaths.Root, file), press: false);
            return 0;
        }
        // R1: the R6.0 capture «scan» as the oracle of the definitions (with BRIDGHE_CAPTURE=compare:<folder>, as the gates run).
        oracles = new Oracles(); femInputs.Clear(); compositeGuidDefects.Clear();
        scan = R1ScanCapture.Open();
        scanFiles = scan == null ? new HashSet<string>() : scan.Files;
        Console.WriteLine(scan == null ? "GPC-ONLY " + lane + ": no R6.0 capture (BRIDGHE_CAPTURE=compare:<folder> not set): the definitions are not mapped to it"
            : $"GPC-ONLY {lane}: the R6.0 capture scan of {scan.Folder} ({scan.Count} entries, {scanFiles.Count} definitions) as the oracle of the definitions");
        var producers = new HashSet<string>(propertiesLane ? Lane2Producers : modelsLane ? ModelsProducers : Array.Empty<string>(), StringComparer.Ordinal); // L3c: Node
        var byType = new SortedDictionary<string, (int Instances, int Items)>(StringComparer.Ordinal);
        var failures = new List<string>();
        int files = 0, derived = 0;
        // A substring of the definitions to replay (diagnosis only; the regression replays them all).
        string only = Environment.GetEnvironmentVariable("BRIDGHE_GPC_ONLY_FILES");
        foreach (var file in (modelsLane ? ModelsScopeFiles() : BuildModelScan.ScopeFiles()).Where(f => string.IsNullOrEmpty(only) || f.Contains(only, StringComparison.OrdinalIgnoreCase)))
        {
            string label = Path.GetRelativePath(ExamplePaths.Root, file);
            using var doc = GrasshopperBridgeExample.Read(file); doc.FilePath = file;
            // No analysis runs (the Run button is never pressed: the definitions may name outputs in the main checkout).
            foreach (var o in doc.Objects.OfType<IGH_ActiveObject>())
                if (o.GetType().Name is "StrausR3AnalysisComponent" or "MovingStaticAnalysisComponent") o.Locked = true;
            BridgeSegmentExcelTests.Solve(doc, true);
            if (modelsLane) { PressModels(doc); derived += DeriveModels(doc); } // L3a
            files++;
            int unconvertedBefore = unconverted; // L3c
            definition = label; // R1: the file key of the capture
            try
            {
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
            finally { definition = null; }
            // L3c: a model without GPC input must be declared.
            if (modelsLane && unconverted != unconvertedBefore)
            {
                var declared = ModelsWithoutGpcInput.FirstOrDefault(d => label.Contains(d.File, StringComparison.OrdinalIgnoreCase));
                if (declared.File == null) failures.Add(label + ": a model without GPC input, not declared in ModelsWithoutGpcInput");
                else Console.WriteLine("GPC-ONLY models: declared without GPC input: " + label + ": " + declared.Reason);
            }
        }
        var fromExamples = new HashSet<string>(byType.Keys, StringComparer.Ordinal);
        if (modelsLane) Console.WriteLine($"GPC-ONLY models: {derived} derived instances (on the results of Super Element Check and the analysed models of the remade examples)");
        int cases = 0;
        if (lane is "elements" or "generators" or "properties" or "models") InMemory(lane, kinds, byType, failures, out cases);
        if (lane == "models") ModelsChecks(kinds, failures, ref cases); // L3b
        if (lane == "models" && Selected("Lane3e")) Lane3eChecks(failures, ref cases); // L3e
        if (lane == "properties") ParameterCases(kinds, byType, failures, ref cases);
        // L2C: the checks of the review of lane 2 (R65-C2).
        if (lane == "properties" && (selected == null || selected.Contains("Review"))) ReviewChecks(kinds, failures, ref cases);
        // PAR: the checks of the 0.3.0 parity that need more than a list of inputs.
        if (lane == "properties" && (selected == null || selected.Contains("Parity"))) ParityChecks(kinds, failures, ref cases);
        foreach (var t in types.Where(t => !fromExamples.Contains(t))) Console.WriteLine("GPC-ONLY " + lane + ": not met in the examples (in-memory cases only): " + t);
        foreach (var t in types.Where(t => !byType.ContainsKey(t) && !(lane == "properties" && NativeTypes.Contains(t)) && !(lane == "models" && ModelsNativeOnly.Contains(t)))) failures.Add("no case for " + t);
        if (lane == "models") foreach (var t in ModelsNativeOnly.Where(t => Selected(t) && !byType.ContainsKey(t))) Console.WriteLine("GPC-ONLY models: inputs only after a native analysis (lane 3e): " + t);
        if (lane == "properties") foreach (var t in Lane2aParameters.Where(t => Selected(t) && !byType.ContainsKey(t))) failures.Add("no case for " + t);
        if (lane == "properties") foreach (var t in NativeTypes.Where(Selected)) Console.WriteLine("GPC-ONLY properties: Straus7 section, in-memory cases under the native host only (core-native): " + t);
        foreach (var p in byType) Console.WriteLine($"GPC-ONLY {lane}: {p.Key} {p.Value.Instances} instances, {p.Value.Items} output lines");
        if (modelsLane) Console.WriteLine("GPC-ONLY models: Bridge Model Audit codes met: " + string.Join(", ", auditCodes.Select(k => k.Key + " " + k.Value)));
        if (modelsLane) Console.WriteLine("GPC-ONLY models: inputs given by their GPC data alone in the GPC-only copies: " + string.Join(", ", convertedKinds.Select(k => k.Key + " " + k.Value)));
        // R1: the oracles, and the in-memory cases with an input still taken from a FEM value (no GPC twin yet, P2 decides).
        Console.WriteLine($"GPC-ONLY {lane} ORACLES (R1): {oracles.Summary}");
        foreach (var f in femInputs) Console.WriteLine($"GPC-ONLY {lane} TRANSITIONAL input (a datum from a FEM value, no GPC twin yet): {f}");
        // P2R step 0: the replay capture closed (compare: every captured replay seen with the same value).
        if (captureLane != null) { failures.AddRange(P2rCapture.Replays.Close(captureLane)); captureLane = null; }
        foreach (var f in failures.Take(60)) Console.WriteLine("  " + f);
        // P2Y: every failure in a file named by BRIDGHE_GPC_ONLY_REPORT (diagnosis of a lane in progress; the console keeps the first 60).
        if (Environment.GetEnvironmentVariable("BRIDGHE_GPC_ONLY_REPORT") is { Length: > 0 } report) File.WriteAllLines(report, failures);
        if (failures.Count != 0) throw new Exception($"GPC-ONLY {lane} FAIL: {failures.Count} differences or failures (first: {failures[0]})");
        Console.WriteLine($"GPC-ONLY {lane} PASS: {files} definitions and {cases} in-memory cases, {byType.Sum(p => p.Value.Instances)} instances of {byType.Count} components " +
            "replayed with GPC-only inputs: no FEM value, deterministic identities, the R6.0 capture (definitions) and the declared expectations (in memory)"
            + "; the replay capture of the base of P2 (P2R, U3: messages, outputs modulo GUIDs, raw outputs), the FEM-backed copy retired");
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
