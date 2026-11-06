using System;
using System.Collections.Generic;
using System.Linq;
using GPC.Geometry;
using GPC.Model.Attributes;
using GPC.Model.Combinations;
using GPC.Model.Constraints;
using GPC.Model.Core.Identity;
using GPC.Model.ElementProperties;
using GPC.Model.Elements;
using GPC.Model.LoadCases;
using GPC.Model.Loads;
using GPC.Model.Materials;
using GPC.Model.Models;
using GPC.Model.Restraints;
using GPC.Model.Sections;
using GPC.Model.Structure.Assignments;

namespace BridGHe.Common.Gpc.Legacy
{
    /// <summary>Diagnostics of a legacy conversion. Nothing is dropped silently: every legacy datum that has no GPC
    /// destination yet is listed (with the element count), and data that changes the analysis is rejected.</summary>
    public sealed class LegacyImportReport
    {
        public List<string> Warnings { get; } = new List<string>();
        public Dictionary<string, int> NotExported { get; } = new Dictionary<string, int>(StringComparer.Ordinal);
        /// <summary>Resolved native Straus7 sections (mm) of the beams with a former native section, for the Converter write plan.</summary>
        public Dictionary<Guid, GPC.Converter.Straus7.Straus7SectionDefinition> NativeSections { get; } = new Dictionary<Guid, GPC.Converter.Straus7.Straus7SectionDefinition>();
        /// <summary>F6 chain C: the definition units of the converted model (BridGHe data; the GPC model is N-mm).</summary>
        public BridgeUnits Units { get; internal set; }
        /// <summary>F6 chain C: the BridGHe angle (degrees) of every converted beam by its GPC number, the principal frame of its actions
        /// (<see cref="GpcBeamAxes.Principal"/>), as the results catalog of an analysis keeps it.</summary>
        public Dictionary<int, double> BeamAngles { get; } = new Dictionary<int, double>();
        /// <summary>F6 chain D: how many of the first cases of the conversion were registered in the cases of the former model (the others
        /// come from element loads, combinations, stages, moving paths, or are the default "Dead" case).</summary>
        public int RegisteredCases { get; internal set; }
        /// <summary>F6 chain D: the owner of the element identities of the conversion (its model GUID; a transformed model keeps it with the
        /// element identities and derives from it the identities of its new moving paths, as the conversion of its FEM output would).</summary>
        public Guid Owner { get; internal set; }
        /// <summary>F6 chain D: where the former model referred to every case of the conversion (the legacy reader places a case at its first
        /// origin, in the order of <see cref="LegacyCaseOrigins"/>), by case name; what a model transformer needs to give its cases the order of
        /// the conversion of its FEM output.</summary>
        public Dictionary<string, LegacyCaseOrigins> CaseOrigins { get; } = new Dictionary<string, LegacyCaseOrigins>(StringComparer.Ordinal);
        /// <summary>F6 chain D: the data of every registered beam property by its name (the GPC property of that name in the model): formulation,
        /// principal angle of the section, resolved native section, digest of the former content.</summary>
        public Dictionary<string, LegacyBeamPropertyData> BeamProperties { get; } = new Dictionary<string, LegacyBeamPropertyData>(StringComparer.Ordinal);
        /// <summary>F6 chain D: the beams (GPC numbers) whose former property had the name of an earlier, different property: they got the earlier
        /// one, as with the former exporter ("The beam property ... already exists"); their own content is not in the conversion.</summary>
        public HashSet<int> ShadowedPropertyBeams { get; } = new HashSet<int>();
        /// <summary>F6 chain D (Construction Sequence): the load cases of the former loads of every element, by element, in the order of the
        /// elements of the former model (the order in which the legacy reader meets the cases of the element loads); a transformer that keeps
        /// a subset of the elements orders the cases of its output from it.</summary>
        public List<LegacyElementCases> ElementLoadCases { get; } = new List<LegacyElementCases>();
        /// <summary>F6 chain D (Construction Sequence): the warnings of <see cref="Warnings"/> that concern one element (also in
        /// <see cref="Warnings"/>), so that a transformer that removes or renumbers elements gives the warnings of the conversion of its output.</summary>
        public List<LegacyElementWarning> ElementWarnings { get; } = new List<LegacyElementWarning>();
        /// <summary>F6 (L12, user decision of 5/11): the bridge data of the converted model (member metadata, super element, local section
        /// state of every beam, by beam GUID), outside the generic GPC model: <c>new BridgeModel(model, Bridge)</c>. Never null.</summary>
        public GPC.Checkers.CompositeBridge.BridgeModelData Bridge { get; internal set; } = new GPC.Checkers.CompositeBridge.BridgeModelData();
        /// <summary>Chain E (e3): what Build Model on GPC did (merges, re-anchorings), when the model comes from <see cref="LegacyBuildModel.Build"/>; null otherwise.</summary>
        public LegacyBuildModel.BuildCounts BuildModel { get; internal set; }

        /// <summary>A warning about one element: in <see cref="Warnings"/> and <see cref="ElementWarnings"/>.</summary>
        internal void Warn(GPC.Model.Core.Identity.EntityFamily family, int number, string text, bool numberInText = false, bool restraint = false)
        {
            Warnings.Add(text);
            string format = text.Replace("{", "{{").Replace("}", "}}");
            if (numberInText)
            {
                string suffix = number.ToString(System.Globalization.CultureInfo.InvariantCulture);
                if (!text.EndsWith(suffix, StringComparison.Ordinal)) throw new InvalidOperationException("LegacyElementWarningWithoutNumber: " + text);
                format = text.Substring(0, text.Length - suffix.Length).Replace("{", "{{").Replace("}", "}}") + "{0}";
            }
            ElementWarnings.Add(new LegacyElementWarning(family, number, format, restraint));
        }

        /// <summary>A copy of this report for a model transformed from the converted one (same element identities).</summary>
        internal LegacyImportReport CopyFor(int registeredCases)
        {
            var copy = new LegacyImportReport { Units = Units, RegisteredCases = registeredCases, Owner = Owner, Bridge = Bridge.Copy() };
            copy.Warnings.AddRange(Warnings);
            copy.ElementLoadCases.AddRange(ElementLoadCases);
            copy.ElementWarnings.AddRange(ElementWarnings);
            foreach (var p in NotExported) copy.NotExported.Add(p.Key, p.Value);
            foreach (var p in NativeSections) copy.NativeSections.Add(p.Key, p.Value);
            foreach (var p in BeamAngles) copy.BeamAngles.Add(p.Key, p.Value);
            foreach (var p in CaseOrigins) copy.CaseOrigins.Add(p.Key, p.Value);
            foreach (var p in BeamProperties) copy.BeamProperties.Add(p.Key, p.Value);
            copy.ShadowedPropertyBeams.UnionWith(ShadowedPropertyBeams);
            return copy;
        }
        internal void Carry(string what) { NotExported.TryGetValue(what, out int n); NotExported[what] = n + 1; }
        internal const string ExistingPropertyWarning = "The beam property {0} already exists";
    }

    /// <summary>F6 chain D: the origins of a case in the former model, in the order the legacy reader takes them: registered in the cases of
    /// the model (without the cases generated by a former moving analysis), element loads, combinations, stage increments, moving paths, and
    /// the default "Dead" self-weight case.</summary>
    [Flags]
    public enum LegacyCaseOrigins { None = 0, Registered = 1, ElementLoad = 2, Combination = 4, Stage = 8, Moving = 16, DefaultDead = 32 }

    /// <summary>F6 chain D: what the conversion of a former beam property gives besides the GPC property: the formulation (a datum of the
    /// element in GPC), the principal angle of the section (as <see cref="GpcBeamAxes.Section"/> takes it), the resolved native Straus7 section (or
    /// null) and the SHA-256 digest of the former content (the former exporter kept the first property of a name).</summary>
    public sealed record LegacyBeamPropertyData(BeamFormulation Formulation, double Angle, GPC.Converter.Straus7.Straus7SectionDefinition Native, string Signature)
    {
        /// <summary>Chain E (e3 point 5): the section of the property in the definition units (null: a property built on GPC, without one),
        /// from which Build Model builds the section in N-mm (<see cref="LegacyAssembly.NormaliseBeamProperty"/>).</summary>
        public LegacySectionRecipe Section { get; init; }
    }

    /// <summary>
    /// Chain E (e3 point 5, coordinator decision 1 of 5/11): the section of a former beam property in the definition units, as the section
    /// components give it (former SectionModel), so that Build Model builds the section in N-mm with the operations of the reader
    /// (<see cref="LegacyModelImport.SectionProperty"/>, shared by both). The GPC section of a draft is in the definition units and some of its
    /// values cannot be scaled back bit for bit: the shear centre of an explicit section is the centroid plus rotated offsets, the shear areas
    /// of a standard shape are computed on the shape, a native section is resolved by Straus7 from its dimensions in a length unit, and the
    /// check of the length unit of a library or BXS section needs the units of the model. <see cref="NativeKey"/> null: a BridGHe section.
    /// </summary>
    public sealed class LegacySectionRecipe
    {
        public BridgeSectionKind Kind { get; init; }
        public string Name { get; init; }
        /// <summary>AngleX1 (rad): the principal angle of an explicit section.</summary>
        public double Angle { get; init; }
        public double D { get; init; }
        public double B { get; init; }
        public double B1 { get; init; }
        public double B2 { get; init; }
        public double T { get; init; }
        public double T1 { get; init; }
        public double T2 { get; init; }
        public double T3 { get; init; }
        public double Area { get; init; }
        public double I11 { get; init; }
        public double I22 { get; init; }
        public double J { get; init; }
        public double CentroidX { get; init; }
        public double CentroidY { get; init; }
        public double ShearL1 { get; init; }
        public double ShearL2 { get; init; }
        public double ShearA1 { get; init; }
        public double ShearA2 { get; init; }
        public bool CompositePreview { get; init; }
        public string NativeKey { get; init; }
        public IReadOnlyList<double> NativeDimensions { get; init; }
        public string BxsPath { get; init; }
        public string LibraryName { get; init; }
        public string LibraryItem { get; init; }
        /// <summary>The Straus7 length unit a library or BXS section was read in (only for those).</summary>
        public int NativeLengthUnit { get; init; }
    }

    /// <summary>F6 chain D: the load cases of the former loads of one element (GPC family and number), in load order.</summary>
    public sealed record LegacyElementCases(GPC.Model.Core.Identity.EntityFamily Family, int Number, IReadOnlyList<string> Cases);

    /// <summary>F6 chain D: a warning of the conversion about one element (GPC family and number): its text is
    /// <c>string.Format(Format, number)</c> (the number in the text when the warning names it); <see cref="Restraint"/>: a warning about the
    /// restraints of a node (it goes with them).</summary>
    public sealed record LegacyElementWarning(GPC.Model.Core.Identity.EntityFamily Family, int Number, string Format, bool Restraint)
    {
        public string Text(int number) => string.Format(System.Globalization.CultureInfo.InvariantCulture, Format, number);
    }

    /// <summary>F6 chain D: a former beam property converted on its own (<see cref="LegacyModelImport.BeamProperty"/>).</summary>
    public sealed record LegacyBeamProperty(BeamProperty Property, LegacyBeamPropertyData Data);

    /// <summary>Chain E (d): the GPC draft of one former beam value (<see cref="LegacyModelImport.BeamDraft"/>): the beam (no nodes, definition
    /// units), its bridge data (null: none), its BridGHe angle (degrees), the data of its property (formulation, principal angle, native section,
    /// signature) and the report of the conversion (warnings, data carried but not converted).</summary>
    public sealed record LegacyBeamDraft(BeamElement Beam, GPC.Checkers.CompositeBridge.BridgeBeamData Bridge, double AngleDeg, LegacyBeamPropertyData Property, LegacyImportReport Report)
    {
        /// <summary>Chain E (e3): the data of the beam at list level that the GPC beam keeps one of (<see cref="LegacyElementRecords"/>).</summary>
        public LegacyElementRecords Records { get; init; } = new LegacyElementRecords();
    }

    /// <summary>
    /// Chain E (e3, P2K, coordinator decision 2 of 5/11): what a beam or plate draft holds at list level besides its GPC element, which keeps
    /// one of each: every group (full names, in order; the element has the first), every Element ID value (the element has the first as its
    /// label), the super element names (distinct, in order; the bridge data have the first), and how many bridge member, section state and
    /// taper records and former loads the value had. The merge by GH GUID appends to these lists (<see cref="LegacyAssembly.Merge(LegacyBeamDraft, LegacyBeamDraft)"/>),
    /// the resolution (<see cref="LegacyAssembly.ResolveBeam"/>, <see cref="LegacyAssembly.ResolvePlate"/>) gives the warnings and errors of
    /// the former reader with the number and the name the element has in the model. Immutable by convention (a merge copies them).
    /// </summary>
    public sealed class LegacyElementRecords
    {
        internal readonly List<string> GroupList = new List<string>(), LabelList = new List<string>(), SuperElementList = new List<string>();
        public IReadOnlyList<string> Groups => GroupList;
        public IReadOnlyList<string> Labels => LabelList;
        public IReadOnlyList<string> SuperElements => SuperElementList;
        public int Members { get; internal set; }
        public int SectionStates { get; internal set; }
        public int Tapers { get; internal set; }
        /// <summary>Element IDs without a value (carried, not converted).</summary>
        public int EmptyLabels { get; internal set; }
        /// <summary>The carried text of every plate support, in order (the former face is not in the GPC support).</summary>
        internal readonly List<string> SupportCarryList = new List<string>();
        public IReadOnlyList<string> SupportCarries => SupportCarryList;
        /// <summary>The value of the first Element ID attribute of the value ("" when it has no value), null when it has none: the BridGHe ID
        /// Build Model gives the element (former ModelHelper.BuildModel).</summary>
        public string FirstLabel { get; internal set; }
        /// <summary>The definition of the case of every former load of the value, parallel to <see cref="LoadRecords"/> (the GPC cases of the
        /// drafts do not carry gravity, temperature, stage list; coordinator decision 1 of 5/11 evening).</summary>
        internal readonly List<LegacyCaseData> LoadCaseList = new List<LegacyCaseData>();
        public IReadOnlyList<LegacyCaseData> LoadCases => LoadCaseList;
        /// <summary>What the conversion of the beam property of the value carries (the model carries it once per registered property).</summary>
        internal readonly List<KeyValuePair<string, int>> PropertyCarryList = new List<KeyValuePair<string, int>>();
        public IReadOnlyList<KeyValuePair<string, int>> PropertyCarries => PropertyCarryList;
        /// <summary>The former loads of the value in order, as (former load type, case name): the k of the beam load records
        /// "BeamLoad/&lt;number&gt;/&lt;k&gt;" is the position here (1-based); the merge by GH GUID walks them in this order.</summary>
        internal readonly List<(string Kind, string Case)> LoadList = new List<(string, string)>();
        public IReadOnlyList<(string Kind, string Case)> LoadRecords => LoadList;
        public int Loads => LoadList.Count;
        internal LegacyElementRecords Copy()
        {
            var copy = new LegacyElementRecords { Members = Members, SectionStates = SectionStates, Tapers = Tapers, EmptyLabels = EmptyLabels };
            copy.SupportCarryList.AddRange(SupportCarryList); copy.LoadCaseList.AddRange(LoadCaseList); copy.PropertyCarryList.AddRange(PropertyCarryList); copy.FirstLabel = FirstLabel;
            copy.LoadList.AddRange(LoadList);
            copy.GroupList.AddRange(GroupList); copy.LabelList.AddRange(LabelList); copy.SuperElementList.AddRange(SuperElementList);
            return copy;
        }
    }

    /// <summary>Chain E (e1): the GPC draft of one former node value (<see cref="LegacyModelImport.NodeDraft"/>): the node (definition units),
    /// with its rigid links as <see cref="GPC.Model.Attributes.RigidConnectionAttribute"/> (target points; the assembly makes the links), and the
    /// report of the conversion.</summary>
    public sealed record LegacyNodeDraft(NodeElement Node, LegacyImportReport Report)
    {
        /// <summary>Chain E (e3): the former loads of the node in order (<see cref="LegacyElementRecords.LoadRecords"/>), for the merge by GH GUID.</summary>
        public LegacyElementRecords Records { get; init; } = new LegacyElementRecords();
    }

    /// <summary>Chain E (e1): the GPC draft of one former plate value (<see cref="LegacyModelImport.PlateDraft"/>): the plate without nodes
    /// (definition units), its corners in order (the assembly finds their nodes by tolerance) and the report of the conversion.</summary>

    /// <summary>
    /// Chain E (e3, coordinator decision 1 of 5/11 evening): a former load case as the reader reads it, the BridGHe record the element drafts,
    /// combinations and stages carry with their loads and factors (the GPC case keeps its name, self weight and masses, not the gravity, the
    /// temperature or the stage list): filled from the former case now, from the GH load case on GPC after the removal of FeMM.
    /// <see cref="HasStageList"/>: the former case had a list of stages (every case of the Load Case component but the copy of the former
    /// default "Dead"), which its equality compares by reference (<see cref="LegacyBuildModel"/>).
    /// </summary>
    public sealed record LegacyCaseData(string Name, double Temperature, bool StructuralMass, bool NonStructuralMass, string Inertia, string GravityDirection, double GravityValue,
        int Stages, bool HasStageList)
    {
        internal static LegacyCaseData From(LegacyValue c) => new LegacyCaseData(c.String("Name"), c.Double("Temperature"), c.Bool("StructuralMassAcceleration"),
            c.Bool("NonStructuralMassAcceleration"), c.Enum("GlobalInertiaLoad"), c.Enum("GravityDirection"), c.Double("GravityValue"),
            c.Has("Stages") && !c["Stages"].IsNull ? c.Field("Stages").Items.Count : 0, c.Has("HasStages") ? c.Bool("HasStages") : c.Has("Stages") && !c["Stages"].IsNull);
        /// <summary>The former reader: one definition per case name (temperature, masses, inertia, gravity).</summary>
        internal bool Same(LegacyCaseData o) => Temperature == o.Temperature && StructuralMass == o.StructuralMass && NonStructuralMass == o.NonStructuralMass
            && Inertia == o.Inertia && GravityDirection == o.GravityDirection && GravityValue == o.GravityValue;
    }

    /// <summary>Chain E (e3): a former load combination by value: name and factors (case definition, factor) in order.</summary>
    public sealed record LegacyCombinationDraft(string Name, IReadOnlyList<(LegacyCaseData Case, double Factor)> Factors);

    /// <summary>R6.2 part C: the GPC datum of a GH group as a capturable record (the draft check): the chain of names from the root.</summary>
    public sealed record LegacyGroupChain(IReadOnlyList<string> Names);

    /// <summary>Chain E (e3): a former staged construction by value: name, the full names of its groups, morph, rotate, increments.</summary>
    public sealed record LegacyStageDraft(string Name, IReadOnlyList<string> Groups, bool Morph, bool Rotate, IReadOnlyList<LegacyCombinationDraft> Increments);
    public sealed record LegacyPlateDraft(AreaElement Plate, IReadOnlyList<Point3d> Corners, LegacyImportReport Report)
    {
        /// <summary>Chain E (e3): the groups and Element IDs of the plate at list level (<see cref="LegacyElementRecords"/>).</summary>
        public LegacyElementRecords Records { get; init; } = new LegacyElementRecords();
    }

    /// <summary>Converts a former BridGHe FEM model (BGHA1 archive, read as data by <see cref="LegacyArchive"/>) into a GPC
    /// model in N-mm with the semantics of the former Straus7 exporter: node order and numbering, node merging by tolerance
    /// at the beam and link ends, load-case set with the default "Dead" self-weight case, one group per element.
    /// Analysis results and caches of the former model are not converted (they are recomputed).</summary>
    public static partial class LegacyModelImport
    {
        public const string Program = "BridGHe";

        /// <param name="femPropertySignature">F6 (L11): the former section-state signature (<c>BridgeSectionAssignment.Signature</c>) of the
        /// property of the former beam k (0-based), when the former model is at hand (<see cref="GpcModels"/>); a section state whose
        /// signature differs is stale (the property changed after Bridge State Override). Null: every state is taken as current.</param>
        public static Model Import(LegacyValue legacy, Guid owner, double toleranceDefinitionUnits, LegacyImportReport report, Func<int, string> femPropertySignature = null)
        {
            if (legacy == null || !legacy.Is("ModelModel")) throw new ArgumentException("LegacyModelModelExpected");
            report ??= new LegacyImportReport();
            var units = new BridgeUnits(BridgeUnits.Parse(legacy.Enum("Units")));
            // Chain E (e2): the model part (cases, groups, node merge, rigid links, property registries, moving paths, stages, combinations) is
            // the assembly; the elements are the drafts of the former values, built here with the identities and numbers of the model and the
            // units of the model (N-mm), as Build Model builds them from the GH types (e3).
            var assembly = new LegacyAssembly(legacy.String("Name") ?? "", units, owner, toleranceDefinitionUnits, report);
            var model = assembly.Model;
            var elements = legacy.Field("Elements").Items;
            var nodes = elements.Where(e => e.Is("NodeModel")).ToList();
            var beams = elements.Where(e => e.Is("BeamModel")).ToList();
            var plates = elements.Where(e => e.Is("PlateModel")).ToList();
            var others = elements.Where(e => !e.Is("NodeModel") && !e.Is("BeamModel") && !e.Is("PlateModel")).ToList();
            if (others.Count != 0) throw new NotSupportedException("LegacyElementTypeNotMappedYet: " + string.Join(", ", others.Select(o => o.ShortType).Distinct()));
            // F6 chain D: the cases of the former loads of every element, in the order of the former elements (numbers as below).
            int nodeCount = 0, beamCount = 0, plateCount = 0;
            foreach (var e in elements)
            {
                var family = e.Is("NodeModel") ? GPC.Model.Core.Identity.EntityFamily.Node : e.Is("BeamModel") ? GPC.Model.Core.Identity.EntityFamily.Beam : GPC.Model.Core.Identity.EntityFamily.Shell;
                int number = family == GPC.Model.Core.Identity.EntityFamily.Node ? ++nodeCount : family == GPC.Model.Core.Identity.EntityFamily.Beam ? ++beamCount : ++plateCount;
                report.ElementLoadCases.Add(new LegacyElementCases(family, number, e.Field("Loads").Items.Select(l => l.Field("LoadCase").String("Name")).ToArray()));
            }
            string movingJson = legacy.Has("MovingPaths") ? legacy.String("MovingPaths") : "[]";
            if (legacy.Has("BridgeWorkflow")) report.Carry("BridgeWorkflow (checks recipe, not exported)");

            assembly.RegisterCases(Cases(legacy, MovingCaseNames(movingJson), report));
            var cases = assembly.Cases;

            // Nodes: former order and numbering; their groups are organizational metadata (Straus7 nodes have no group; the writer creates
            // the groups, as the former exporter); rigid links in node order, as the former RigidConnectionExport.
            assembly.RequireTolerance();
            assembly.AddNodes(nodes.Select((n, i) => LegacyAssembly.ResolveNode(Node(n, i, GpcIdentity.Derive(owner, "node:" + i), owner, assembly.GroupOf, cases, units, report), report)).ToList());

            // Beams: former order and numbering; nodes found by tolerance (StrausNodeIndex semantics).
            // The former exporter created one native property per property name: later properties with the same name used the first.
            var shearAreas = new Dictionary<string, (double, double)>(StringComparer.Ordinal);
            var members = new BridgeMembers(report.Bridge);
            for (int k = 0; k < beams.Count; k++)
            {
                var b = beams[k];
                var (i, j) = assembly.BeamEnds(Point(b["PointFrom"], units), Point(b["PointTo"], units), k + 1);
                string signature = Signature(b["BeamProperty"]);
                var p = assembly.BeamProperty(PropertyName(b["BeamProperty"]), signature, k + 1, name => BeamProperty(b["BeamProperty"], name, units, shearAreas, report, signature));
                var ni = assembly.Node(i); var nj = assembly.Node(j);
                var records = new LegacyElementRecords();
                var beam = Beam(b, k, GpcIdentity.Derive(owner, "beam:" + k), owner, ni.Position, nj.Position, p.Property, p.Formulation, p.Angle,
                    created => created.ConnectNodes(ni, nj), assembly.GroupOf, cases, units, report, members, femPropertySignature, records);
                LegacyAssembly.ResolveBeam(beam, records, report);
                assembly.AddBeam(beam, p.Native);
            }

            assembly.AddMovingPaths(movingJson);
            // Plates: former order and numbering, nodes by tolerance; one property per name as the former exporter.
            for (int k = 0; k < plates.Count; k++)
            {
                var pl = plates[k];
                int count = pl.Int("NPoints"); if (count != 3 && count != 4) throw new NotSupportedException("LegacyPlateWithMoreThanFourNodes");
                var connected = assembly.PlateNodes(Enumerable.Range(0, count).Select(i => Point(pl["Point" + i], units)), k + 1);
                var pp = pl.Field("PlateProperty"); string ppName = PlatePropertyName(pp);
                var property = assembly.PlateProperty(ppName, () => PlateProperty(pp, ppName, units));
                var records = new LegacyElementRecords();
                var plate = Plate(pl, k, GpcIdentity.Derive(owner, "plate:" + k), owner, property, created => created.ConnectNodes(connected), assembly.GroupOf, cases, units, report, records);
                LegacyAssembly.ResolvePlate(plate, records, report);
                assembly.AddPlate(plate);
            }

            var stages = legacy.Has("StagedConstruction") ? legacy.Field("StagedConstruction").Items : Array.Empty<LegacyValue>();
            if (stages.Count != 0) Stages(stages.Select(StageDraft).ToList(), model, cases, assembly.Groups, report);
            foreach (var c in legacy.Field("LoadCombinations").Items)
            {
                var combination = CombinationDraft(c);
                assembly.AddCombination(combination.Name, combination.Factors.Select(f => (f.Case.Name, f.Factor)));
            }
            return model;
        }

        /// <summary>The GPC case of a former load case (null: the default "Dead" case): self weight with gravity, the masses of its inertia
        /// loads; the identity by name. Model loads (gravity) are added by the caller.</summary>
        /// <summary>R6.1 (frozen API for R6.2): the GPC case of a new load of a draft element, as the reader gives the cases of one element
        /// (<c>ElementCases</c>): the case of that name the element already refers to (one instance per name), after the same checks (a case
        /// with a name; one definition per name among the loads of the element), otherwise a new one (<see cref="Case"/>).</summary>
        internal static LoadCaseBase ElementCase(GPC.Model.Elements.Element element, LegacyElementRecords records, LegacyCaseData data)
        {
            if (data == null || string.IsNullOrWhiteSpace(data.Name)) throw new ArgumentException("Caso di carico nullo o senza nome.");
            var known = records.LoadCases.FirstOrDefault(c => c != null && c.Name == data.Name);
            if (known != null && !known.Same(data)) throw new ArgumentException("Definizioni incompatibili per il caso di carico " + data.Name);
            return GpcModelCases.ElementCases(element).OfType<LoadCaseBase>().FirstOrDefault(c => c.Name == data.Name) ?? Case(data.Name, data);
        }

        private static LoadCaseBase Case(string name, LegacyCaseData c)
        {
            bool dead = c == null && name == "Dead";
            bool gravity = dead || c != null && c.Inertia == "Gravity";
            if (c != null && c.Inertia != "Gravity" && c.Inertia != "None")
                throw new NotSupportedException("LegacyInertiaLoadNotMappedYet: " + c.Inertia);
            LoadCaseBase gpc = gravity ? new LoadCase(GpcIdentity.Case(name), name, LoadCase.LoadCaseTypes.SelfWeight) : new LoadCaseBase(GpcIdentity.Case(name), name);
            bool structural = dead || c != null && c.StructuralMass, nonStructural = c != null && c.NonStructuralMass;
            gpc.InertiaMasses = new InertiaMassSelection { Structural = structural, NonStructural = nonStructural };
            return gpc;
        }

        /// <summary>The former case set: model cases (not generated by moving loads), the cases of element loads and of the
        /// combinations, grouped by name; the default "Dead" case (self weight along −Z, 9806.65 mm/s², structural mass);
        /// numbering by name with Dead first (the writer numbers by ordinal name).</summary>
        private static IEnumerable<string> MovingCaseNames(string json)
        {
            using var document = System.Text.Json.JsonDocument.Parse(json);
            return document.RootElement.EnumerateArray().Select(p => p.GetProperty("LoadCaseName").GetString()).ToArray();
        }

        private static List<LegacyCase> Cases(LegacyValue legacy, IEnumerable<string> movingCases, LegacyImportReport report)
        {
            // Static cases generated by a former moving-load analysis are results (recomputed): their names are not input cases.
            var generated = new HashSet<string>(StringComparer.Ordinal);
            if (legacy.Has("GeneratedMovingCases") && legacy.String("GeneratedMovingCases") != "{}")
            {
                using var document = System.Text.Json.JsonDocument.Parse(legacy.String("GeneratedMovingCases"));
                foreach (var item in document.RootElement.EnumerateObject()) generated.Add(item.Value.GetString());
                report.Carry("Generated moving cases of a former analysis (cache, recomputed): " + generated.Count);
            }
            return CaseSet(legacy.Field("LoadCases").Items.Where(c => !generated.Contains(c.String("Name"))).Select(LegacyCaseData.From).ToList(),
                legacy.Field("Elements").Items.SelectMany(e => e.Field("Loads").Items).Select(l => LegacyCaseData.From(l.Field("LoadCase"))).ToList(),
                legacy.Field("LoadCombinations").Items.SelectMany(c => c.Field("Keys").Items).Select(LegacyCaseData.From).ToList(),
                (legacy.Has("StagedConstruction") ? legacy.Field("StagedConstruction").Items : Array.Empty<LegacyValue>())
                    .SelectMany(st => st.Field("_loadCombinations").Items).SelectMany(c => c.Field("Keys").Items).Select(LegacyCaseData.From).ToList(),
                movingCases, report);
        }

        /// <summary>
        /// The case set of the former reader from the definitions of the cases (e3: shared by the conversion of a whole model and by Build
        /// Model on the drafts): the registered cases, the cases of the element loads in the order of the elements and of their loads, of the
        /// combination factors, of the stage increments, then the moving cases and the default "Dead"; the first definition of a name wins and a
        /// different one is refused; the origins, the registered count, the carried temperatures and stage lists, the gravity loads.
        /// </summary>
        internal static List<LegacyCase> CaseSet(IReadOnlyList<LegacyCaseData> registered, IReadOnlyList<LegacyCaseData> elementLoads, IReadOnlyList<LegacyCaseData> combinations,
            IReadOnlyList<LegacyCaseData> stages, IEnumerable<string> movingCases, LegacyImportReport report)
        {
            var result = new List<LegacyCase>();
            var first = new Dictionary<string, LegacyCaseData>(StringComparer.Ordinal);
            foreach (var c in registered.Concat(elementLoads).Concat(combinations).Concat(stages))
            {
                if (string.IsNullOrWhiteSpace(c.Name)) throw new ArgumentException("Caso di carico nullo o senza nome.");
                if (!first.TryGetValue(c.Name, out var known)) first.Add(c.Name, c);
                else if (!known.Same(c)) throw new ArgumentException("Definizioni incompatibili per il caso di carico " + c.Name);
            }
            // F6 chain D: the cases registered in the former model come first (its LoadCases, without generated moving cases).
            report.RegisteredCases = registered.Select(c => c.Name).Distinct(StringComparer.Ordinal).Count();
            // F6 chain D: every origin of every case, for the model transformers.
            var origins = report.CaseOrigins; origins.Clear();
            void Origin(string name, LegacyCaseOrigins origin) { origins.TryGetValue(name, out var o); origins[name] = o | origin; }
            foreach (var c in registered) Origin(c.Name, LegacyCaseOrigins.Registered);
            foreach (var c in elementLoads) Origin(c.Name, LegacyCaseOrigins.ElementLoad);
            foreach (var c in combinations) Origin(c.Name, LegacyCaseOrigins.Combination);
            foreach (var c in stages) Origin(c.Name, LegacyCaseOrigins.Stage);
            foreach (var name in movingCases) Origin(name, LegacyCaseOrigins.Moving);
            foreach (var name in movingCases) if (!first.ContainsKey(name)) first.Add(name, null);
            if (!first.ContainsKey("Dead")) { first.Add("Dead", null); Origin("Dead", LegacyCaseOrigins.DefaultDead); }
            foreach (var pair in first)
            {
                var c = pair.Value;
                bool dead = c == null && pair.Key == "Dead";
                bool gravity = dead || c != null && c.Inertia == "Gravity";
                var gpc = Case(pair.Key, c);
                if (c != null && (c.Temperature != 0 || c.Stages != 0)) report.Carry("Load case temperature or stage list (not exported by the former writer)");
                ModelGravityLoad gravityLoad = null;
                if (gravity)
                {
                    // Gravity value in mm/s², already N-mm in the former model (Dead: −9806.65 along Z).
                    double value = dead ? -9806.65 : c.GravityValue;
                    string direction = dead ? "Z" : c.GravityDirection;
                    var axis = direction == "X" ? Vector3d.XAxis : direction == "Y" ? Vector3d.YAxis : direction == "Z" ? Vector3d.ZAxis : throw new NotSupportedException("LegacyGravityDirection: " + direction);
                    if (value != 0) gravityLoad = new ModelGravityLoad(axis, value, gpc, CoordinateSystem.Global, "Gravity " + pair.Key);
                }
                result.Add(new LegacyCase(pair.Key, gpc, gravityLoad));
            }
            return result;
        }

        /// <summary>Chain E (e3): a former load combination by value (the datum of a Load Combinations parameter, or one of a former model):
        /// its name and its factors with the definitions of their cases, in order.</summary>
        public static LegacyCombinationDraft CombinationDraft(LegacyValue c)
        {
            string name = c.String("Name");
            var keys = c.Field("Keys").Items; var values = c.Field("Values").Items;
            if (keys.Count != values.Count) throw new ArgumentException("InvalidLegacyCombination: " + name);
            return new LegacyCombinationDraft(name, keys.Select((key, q) => (LegacyCaseData.From(key), values[q].AsDouble())).ToArray());
        }

        /// <summary>Chain E (e3): a former staged construction by value: name, the full names of its groups, morph and rotate, its increments
        /// (one per combination, with the definitions of their cases).</summary>
        public static LegacyStageDraft StageDraft(LegacyValue st)
        {
            // StagedConstructionModel is [Serializable] without ISerializable: field names.
            return new LegacyStageDraft(st.String("_name"), st.Field("_groups").Items.Select(FullName).ToArray(), st.Bool("_morph"), st.Bool("_rotate"),
                st.Field("_loadCombinations").Items.Select(c =>
                {
                    var keys = c.Field("Keys").Items; var values = c.Field("Values").Items;
                    return new LegacyCombinationDraft(c.String("Name"), keys.Select((key, q) => (LegacyCaseData.From(key), values[q].AsDouble())).ToArray());
                }).ToArray());
        }

        /// <summary>Former staged construction: one native stage per definition, in order; the listed groups are the complete set of
        /// active groups (the others are disabled); Morph = birth on deformed coordinates, Rotate = rotate incoming clusters;
        /// each stage combination is one load increment. Legacy stage groups without elements did not exist natively and are ignored.</summary>
        internal static void Stages(IReadOnlyList<LegacyStageDraft> stages, Model model, Dictionary<string, LoadCaseBase> cases, Dictionary<string, Group> groups, LegacyImportReport report)
        {
            var freedom = new GPC.Model.FreedomCases.FreedomCase(GpcIdentity.FreedomCase("Default restraints"), "Default restraints");
            model.FreedomCases.Add(freedom.Name, freedom);
            for (int k = 0; k < stages.Count; k++)
            {
                var st = stages[k]; string name = st.Name;
                var stage = new GPC.Model.Stages.Stage(GpcIdentity.Stage(name), k + 1, name) { Definition = new GPC.Model.Stages.StageDefinition(k + 1, 0) };
                var active = new List<Guid>();
                foreach (var g in st.Groups)
                {
                    if (groups.TryGetValue(g, out var group)) { if (!active.Contains(group.Guid)) active.Add(group.Guid); }
                    else report.Carry("Stage group without elements (not created natively by the former exporter)");
                }
                stage.AnalysisState = new GPC.Model.Stages.StageAnalysisState
                {
                    BirthGeometry = st.Morph ? GPC.Model.Stages.StageBirthGeometry.DeformedCoordinates : GPC.Model.Stages.StageBirthGeometry.OriginalCoordinates,
                    MoveRestrainedNodes = false, RotateIncomingClusters = st.Rotate,
                    ActiveGroupGuids = active.ToArray(), ActiveFreedomCaseGuids = new[] { freedom.Guid },
                    Increments = st.Increments.Select(c => new GPC.Model.Stages.StageLoadIncrement
                    {
                        Name = c.Name,
                        LoadFactors = c.Factors.Select(f => new GPC.Model.Stages.StageCaseFactor { CaseGuid = cases[f.Case.Name].Guid, Factor = f.Factor }).ToArray(),
                    }).ToArray(),
                };
                model.Stages.Add(stage);
            }
        }
        internal static readonly Vector3d Zero = new Vector3d(0, 0, 0);

        /// <summary>BGHGPC1 (ii): a former load case by value (the datum of a Load Case parameter), the record the drafts carry.</summary>
        public static LegacyCaseData CaseDraft(LegacyValue c)
        {
            if (c == null || c.IsNull || !c.Is("LoadCaseModel")) throw new ArgumentException("LegacyLoadCaseModelExpected");
            return LegacyCaseData.From(c);
        }

        internal static string FullName(LegacyValue group)
        {
            var names = new List<string>();
            for (var g = group; !g.IsNull; g = g["Parent"]) names.Insert(0, g.String("Name"));
            return string.Join("\\", names);
        }

        internal static Point3d Point(LegacyValue p, BridgeUnits u) => new Point3d(u.ToMm(p.Double("X")), u.ToMm(p.Double("Y")), u.ToMm(p.Double("Z")));
        internal static Vector3d Vector(LegacyValue p) => new Vector3d(p.Double("X"), p.Double("Y"), p.Double("Z"));
        private static bool SamePlane(LegacyValue a, LegacyValue b) =>
            new[] { "m_origin", "m_xaxis", "m_yaxis", "m_zaxis" }.All(f => Vector(a[f]).Equals(Vector(b[f])));
        private static CoordinateSystem Frame(LegacyValue plane, BridgeUnits u)
        {
            var o = Point(plane["m_origin"], u);
            return new CoordinateSystem(o, Vector(plane["m_xaxis"]), Vector(plane["m_yaxis"]));
        }
        private static bool IsGlobal(LegacyValue cs) =>
            Vector(cs["Origin"]).Equals(new Vector3d(0, 0, 0)) && Vector(cs["Point1"]).Equals(new Vector3d(1, 0, 0)) && Vector(cs["Point2"]).Equals(new Vector3d(0, 1, 0));
    }
}
