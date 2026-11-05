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
    public static class LegacyModelImport
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

        /// <summary>
        /// One former node as a GPC node (the body of the node loop of <see cref="Import"/>, chain E (e1): also the conversion of a single node
        /// value, <see cref="NodeDraft"/>): position, name (BridGHe node ID), source, groups (all, by full name), restraints, springs, Element ID,
        /// loads and temperatures (<see cref="NodeData"/>), and the rigid links of the former node as <see cref="RigidConnectionAttribute"/> with
        /// the target point, after its own attributes (L12 §5.1; the assembly makes the <see cref="RigidLink"/> and removes them).
        /// </summary>
        private static NodeElement Node(LegacyValue n, int i, Guid guid, Guid owner, Func<LegacyValue, Group> groupOf, Dictionary<string, LoadCaseBase> cases,
            BridgeUnits units, LegacyImportReport report, LegacyElementRecords records = null)
        {
            string nodeId = n.String("NodeId") ?? "";
            var node = new NodeElement(guid, Point(n["Position"], units), null, nodeId, i + 1)
            { Source = new SourceIdentity(Program, owner.ToString("D"), EntityFamily.Node, nodeId.Length != 0 ? nodeId : (i + 1).ToString()) };
            foreach (var g in n.Field("Groups").Items) node.AddGroup(groupOf(g));
            NodeData(n, node, cases, units, report);
            if (records != null) records.EmptyLabels = n.Field("Attributes").Items.Count(a => a.Is("ElementIdAttributeModel") && string.IsNullOrEmpty(a.String("Value")));
            foreach (var a in n.Field("Attributes").Items.Where(a => a.Is("RigidConnectionAttributeModel"))) node.Attributes.Add(new RigidConnectionAttribute(Point(a["Target"], units)));
            return node;
        }

        /// <summary>
        /// One former plate as a GPC plate (the body of the plate loop of <see cref="Import"/>, chain E (e1): also the conversion of a single
        /// plate value, <see cref="PlateDraft"/>): on the given property, connected by <paramref name="connect"/> right after its creation; offset,
        /// the first group, Element ID, load patch, supports, prestress, non-structural masses and pressures.
        /// </summary>
        private static AreaElement Plate(LegacyValue pl, int k, Guid guid, Guid owner, PlateProperty property, Action<AreaElement> connect, Func<LegacyValue, Group> groupOf,
            Dictionary<string, LoadCaseBase> cases, BridgeUnits units, LegacyImportReport report, LegacyElementRecords records)
        {
            string plateId = pl.String("PlateId") ?? "";
            var plate = new AreaElement(guid, null, property, null, plateId, k + 1)
            { Source = new SourceIdentity(Program, owner.ToString("D"), EntityFamily.Shell, plateId.Length != 0 ? plateId : (k + 1).ToString()) };
            connect?.Invoke(plate);
            if (pl.Double("Angle") != 0) throw new NotSupportedException("LegacyPlateAngleNotMappedYet");
            if (pl.Double("Offset") != 0) plate.Assignments.Offset = units.ToMm(pl.Double("Offset"));
            // e3: every group at list level (the former exporter wrote the first; the warning comes with the resolution, LegacyAssembly.ResolvePlate).
            var pg = pl.Field("Groups").Items; if (pg.Count > 0) plate.AddGroup(groupOf(pg[0]));
            foreach (var g in pg) records.GroupList.Add(FullName(g));
            foreach (var a in pl.Field("Attributes").Items)
            {
                if (a.Is("ElementIdAttributeModel")) Label(plate, a, report, records);
                else if (a.Is("LoadPatchTypeAttributeModel"))
                {
                    var edges = new List<int>(); for (int e = 0; a.Has("SelectedEdges" + e); e++) edges.Add(a.Int("SelectedEdges" + e));
                    var weights = new double[4]; for (int e = 0; e < 4 && a.Has("Weights" + e); e++) weights[e] = a.Double("Weights" + e);
                    (plate.Assignments.Physical ??= new ShellPhysicalData()).LoadPatch = new LoadPatchDistribution
                    { Kind = (LoadPatchDistributionKind)(int)Enum.Parse(typeof(PatchKinds), a.Enum("PatchType")), SelectedEdges = edges.ToArray(), EdgeWeights = weights };
                }
                else if (a.Is("PlateSupportAttributeModel"))
                {
                    // Former exporter: the -Z face, in every load-case number used as freedom case (one freedom case exists).
                    (plate.Assignments.Physical ??= new ShellPhysicalData()).Supports.Add(new ShellDistributedSupport
                    {
                        Stiffness = a.Double("Stiffness") * units.Force / (units.Length * units.Length * units.Length), Gap = 0, Face = ShellFace.NegativeNormal,
                        Behavior = a.Bool("CompressionOnly") ? DistributedSpringBehavior.CompressionOnly : DistributedSpringBehavior.Bilateral
                    });
                    report.Carry("Plate support face " + a.Enum("Face") + " (the former exporter wrote the -Z face)");
                    records.SupportCarryList.Add("Plate support face " + a.Enum("Face") + " (the former exporter wrote the -Z face)");
                }
                else throw new NotSupportedException("LegacyPlateAttributeNotMappedYet: " + a.ShortType);
            }
            foreach (var l in pl.Field("Loads").Items)
            {
                var loadCase = cases[l.Field("LoadCase").String("Name")];
                if (l.Is("PlatePreStressModel"))
                {
                    var stress = Vector(l["LocalPressure"]);
                    (plate.Assignments.Physical ??= new ShellPhysicalData()).InitialStresses.Add(new ShellInitialStress { Case = loadCase, Components = new Vector3d(units.ToMPa(stress.X), units.ToMPa(stress.Y), units.ToMPa(stress.Z)) });
                    continue;
                }
                if (l.Is("PlateNSMassModel"))
                {
                    if (!units.IsIdentity) throw new NotSupportedException("LegacyPlateNonStructuralMassUnitsRequireNmm");
                    (plate.Assignments.Physical ??= new ShellPhysicalData()).NonStructuralMasses.Add(new ShellNonStructuralMass { Case = loadCase, MassPerArea = l.Double("Mass"), DynamicFactor = l.Double("DynFactor"), GlobalOffset = Vector(l["Offset"]) });
                    continue;
                }
                if (!l.Is("PlatePressureModel")) throw new NotSupportedException("LegacyPlateLoadNotMappedYet: " + l.ShortType);
                var pressure = Vector(l["Pressure"]);
                // Review L1 B2: local pressure = St7SetPlateNormalPressure2 with one value (the -Z face); global = St7SetPlateGlobalPressure3S.
                plate.Loads.Add(l["CoordinateSystem"].IsNull
                    ? ShellFacePressure.Normal(loadCase, ShellFace.NegativeNormal, units.ToMPa(pressure.Z))
                    : ShellFacePressure.Global(loadCase, l.Enum("Face") == "Top" ? ShellFace.PositiveNormal : ShellFace.NegativeNormal,
                        new Vector3d(units.ToMPa(pressure.X), units.ToMPa(pressure.Y), units.ToMPa(pressure.Z)),
                        l.Bool("Projected") ? SurfacePressureProjection.Resultant : SurfacePressureProjection.None));
            }
            return plate;
        }

        private static string PlatePropertyName(LegacyValue pp) => pp.Any("Name", "_name").Text ?? pp.Any("Id", "_id").Text ?? "Plate property";

        /// <summary>
        /// One former beam as a GPC beam (the body of the beam loop of <see cref="Import"/>, chain E (d): also the conversion of a single beam
        /// value, <see cref="BeamDraft"/>): on the given ends and property, connected by <paramref name="connect"/> right after its creation;
        /// formulation, section axes from the BridGHe angle and the principal angle of the property, offsets in the principal axes, the first
        /// group, attributes, loads in its assignments, physical data, at least 5 output stations, the local section state.
        /// </summary>
        private static BeamElement Beam(LegacyValue b, int k, Guid guid, Guid owner, Point3d start, Point3d end, BeamProperty property, BeamFormulation formulation,
            double principalAngle, Action<BeamElement> connect, Func<LegacyValue, Group> groupOf, Dictionary<string, LoadCaseBase> cases, BridgeUnits units,
            LegacyImportReport report, BridgeMembers members, Func<int, string> femPropertySignature, LegacyElementRecords records)
        {
            string beamId = b.String("BeamId") ?? "";
            var beam = new BeamElement(guid, start, end, property, null, beamId, k + 1)
            { Source = new SourceIdentity(Program, owner.ToString("D"), EntityFamily.Beam, beamId.Length != 0 ? beamId : (k + 1).ToString()) };
            connect?.Invoke(beam);
            double angleDeg = b.Double("Angle");
            report.BeamAngles[k + 1] = angleDeg;
            var section = GpcBeamAxes.Section(start, end, angleDeg, principalAngle);
            var principal = GpcBeamAxes.Principal(start, end, angleDeg);
            beam.Assignments.Formulation = formulation;
            beam.Assignments.SectionAxes = section;
            var offset = b["Offset"]; double ox = units.ToMm(offset.Double("X")), oy = units.ToMm(offset.Double("Y"));
            var globalOffset = principal.V1 * ox + principal.V2 * oy;
            if (ox != 0 || oy != 0)
            {
                beam.Assignments.OffsetAxes = principal;
                beam.Assignments.OffsetI = new Vector3d(ox, oy, 0); beam.Assignments.OffsetJ = new Vector3d(ox, oy, 0);
            }
            // e3: every group at list level (the former exporter wrote the first; the warning comes with the resolution, LegacyAssembly.ResolveBeam).
            var bg = b.Field("Groups").Items;
            if (bg.Count > 0) beam.AddGroup(groupOf(bg[0]));
            foreach (var g in bg) records.GroupList.Add(FullName(g));
            BeamData(b, beam, principal, globalOffset, cases, units, report, members, records);
            SectionState(b, k, beam, femPropertySignature, report, records);
            return beam;
        }

        /// <summary>
        /// Chain E (d): the draft of one former beam value (a <c>GH_Beam</c> datum upstream of Build Model): the GPC beam with the rules of the
        /// conversion of a whole model (<see cref="Beam"/>), without nodes (its own ends), number 1, its property converted on its own
        /// (<see cref="BeamProperty(LegacyValue, BridgeUnits)"/>: name as given), its groups by full name (<see cref="GpcIdentity.Group"/>, with
        /// their parents), the cases of its loads by name (<see cref="GpcIdentity.Case"/>, as the cases of a whole model), and its bridge data
        /// (member, super element, local section state; L12) in <see cref="LegacyBeamDraft.Bridge"/>. Dimensions in the definition
        /// <paramref name="units"/> (identity for a draft: Build Model normalises). <paramref name="femPropertySignature"/>: the former section-state
        /// signature of its property (a state whose signature differs is stale); null: a state is current.
        /// </summary>
        public static LegacyBeamDraft BeamDraft(LegacyValue b, BridgeUnits units, Guid guid, string femPropertySignature = null)
        {
            if (b == null || b.IsNull || !b.Is("BeamModel")) throw new ArgumentException("LegacyBeamModelExpected");
            var report = new LegacyImportReport { Units = units, Owner = guid, Bridge = new GPC.Checkers.CompositeBridge.BridgeModelData() };
            var records = new LegacyElementRecords();
            var p = b["BeamProperty"];
            if (p == null || p.IsNull || !p.Is("BeamPropertyModel")) throw new ArgumentException("LegacyBeamPropertyModelExpected");
            // e3: what the conversion of the property carries goes with the draft (the model carries it when the property is registered).
            var propertyReport = new LegacyImportReport();
            // e3 point 5: the length unit of a library or BXS section is checked by the assembly, against the units of the model.
            var property = BeamProperty(p, PropertyName(p), units, new Dictionary<string, (double, double)>(StringComparer.Ordinal), propertyReport, Signature(p), checkLengthUnit: false);
            records.PropertyCarryList.AddRange(propertyReport.NotExported);
            var cases = ElementCases(b, out var groupOf, records);
            var beam = Beam(b, 0, guid, guid, Point(b["PointFrom"], units), Point(b["PointTo"], units), property.Property, property.Data.Formulation, property.Data.Angle,
                null, groupOf, cases, units, report, new BridgeMembers(report.Bridge), femPropertySignature == null ? null : _ => femPropertySignature, records);
            return new LegacyBeamDraft(beam, report.Bridge.Beam(beam.Guid), b.Double("Angle"), property.Data, report) { Records = records };
        }

        /// <summary>
        /// Chain E (e1): the draft of one former node value (a <c>GH_Node</c> datum upstream of Build Model): the GPC node with the rules of the
        /// conversion of a whole model (<see cref="Node"/>), number 1, its groups by full name (with their parents), the cases of its loads by
        /// name, its rigid links as <see cref="RigidConnectionAttribute"/> (target point in the draft units). Dimensions in the definition
        /// <paramref name="units"/> (identity for a draft).
        /// </summary>
        public static LegacyNodeDraft NodeDraft(LegacyValue n, BridgeUnits units, Guid guid)
        {
            if (n == null || n.IsNull || !n.Is("NodeModel")) throw new ArgumentException("LegacyNodeModelExpected");
            var report = new LegacyImportReport { Units = units, Owner = guid, Bridge = new GPC.Checkers.CompositeBridge.BridgeModelData() };
            var records = new LegacyElementRecords();
            var cases = ElementCases(n, out var groupOf, records);
            return new LegacyNodeDraft(Node(n, 0, guid, guid, groupOf, cases, units, report, records), report) { Records = records };
        }

        /// <summary>
        /// Chain E (e1): the draft of one former plate value (a <c>GH_Plate</c> datum upstream of Build Model): the GPC plate with the rules of
        /// the conversion of a whole model (<see cref="Plate"/>) without nodes, number 1, its corners (<see cref="LegacyPlateDraft.Corners"/>, in
        /// order), its property converted on its own (name as given), its first group by full name (with its parents), the cases of its loads by
        /// name. Dimensions in the definition <paramref name="units"/> (identity for a draft).
        /// </summary>
        public static LegacyPlateDraft PlateDraft(LegacyValue pl, BridgeUnits units, Guid guid)
        {
            if (pl == null || pl.IsNull || !pl.Is("PlateModel")) throw new ArgumentException("LegacyPlateModelExpected");
            var report = new LegacyImportReport { Units = units, Owner = guid, Bridge = new GPC.Checkers.CompositeBridge.BridgeModelData() };
            int count = pl.Int("NPoints"); if (count != 3 && count != 4) throw new NotSupportedException("LegacyPlateWithMoreThanFourNodes");
            var corners = Enumerable.Range(0, count).Select(i => Point(pl["Point" + i], units)).ToArray();
            var pp = pl.Field("PlateProperty"); string ppName = PlatePropertyName(pp);
            var property = PlateProperty(pp, ppName, units);
            var records = new LegacyElementRecords();
            var cases = ElementCases(pl, out var groupOf, records);
            return new LegacyPlateDraft(Plate(pl, 0, guid, guid, property, null, groupOf, cases, units, report, records), corners, report) { Records = records };
        }

        /// <summary>The cases of the loads of one former element by name (the rules of the case set of a whole model: a name, one definition
        /// per name), and the groups of a draft by full name with their parents (no model: the draft keeps them). e3: the definitions of the
        /// cases (<see cref="LegacyElementRecords.Cases"/>) and the first Element ID (<see cref="LegacyElementRecords.FirstLabel"/>) go to the
        /// records of the draft.</summary>
        private static Dictionary<string, LoadCaseBase> ElementCases(LegacyValue element, out Func<LegacyValue, Group> groupOf, LegacyElementRecords records)
        {
            var groups = new Dictionary<string, Group>(StringComparer.Ordinal);
            Group GroupOf(LegacyValue g)
            {
                string full = FullName(g);
                if (groups.TryGetValue(full, out var known)) return known;
                var parent = g["Parent"].IsNull ? null : GroupOf(g["Parent"]);
                var group = Group.Create(GpcIdentity.Group(full), full);
                parent?.AddChild(group);
                groups.Add(full, group);
                return group;
            }
            groupOf = GroupOf;
            var first = new Dictionary<string, LegacyCaseData>(StringComparer.Ordinal);
            foreach (var c in element.Field("Loads").Items.Select(l => LegacyCaseData.From(l.Field("LoadCase"))))
            {
                if (string.IsNullOrWhiteSpace(c.Name)) throw new ArgumentException("Caso di carico nullo o senza nome.");
                if (!first.TryGetValue(c.Name, out var known)) first.Add(c.Name, c);
                else if (!known.Same(c)) throw new ArgumentException("Definizioni incompatibili per il caso di carico " + c.Name);
            }
            foreach (var l in element.Field("Loads").Items) { records.LoadList.Add((l.ShortType, l.Field("LoadCase").String("Name"))); records.LoadCaseList.Add(LegacyCaseData.From(l.Field("LoadCase"))); }
            var label = element.Field("Attributes").Items.FirstOrDefault(a => a.Is("ElementIdAttributeModel"));
            records.FirstLabel = label == null ? null : label.String("Value") ?? "";
            return first.ToDictionary(pair => pair.Key, pair => Case(pair.Key, pair.Value), StringComparer.Ordinal);
        }

        /// <summary>The GPC case of a former load case (null: the default "Dead" case): self weight with gravity, the masses of its inertia
        /// loads; the identity by name. Model loads (gravity) are added by the caller.</summary>
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
        /// <summary>
        /// Chain E (e3, coordinator decision 2 of 5/11): the data of a former node as records of its draft, one per former attribute and
        /// load, in their order, never resolved: what a component appends to a node and what Build Model merges by GH GUID before the
        /// resolution (<see cref="LegacyAssembly.ResolveNode"/>): coordinate systems, restraints and stiffnesses as
        /// <see cref="RestrainAssignment"/> records (<see cref="FrameRecord"/>, <see cref="RestraintRecord"/>, <see cref="StiffnessRecord"/>),
        /// every Element ID as <see cref="ElementLabelAttribute"/>, forces and moments as one <see cref="PointLoad"/> each
        /// (<see cref="ForceRecord"/>, <see cref="MomentRecord"/>), temperatures with their own kind. The checks of one value stay here
        /// (coordinate systems of the value, prescribed displacements, data not mapped yet).
        /// </summary>
        private static void NodeData(LegacyValue n, NodeElement node, Dictionary<string, LoadCaseBase> cases, BridgeUnits units, LegacyImportReport report)
        {
            var attributes = n.Field("Attributes").Items;
            var frames = attributes.Where(a => a.Is("NodeCoordinateAttributeModel")).Select(a => a["Frame"]).ToArray();
            if (frames.Length > 1 && frames.Skip(1).Any(f => !SamePlane(f, frames[0]))) throw new ArgumentException($"Node {node.Name}: conflicting/invalid restraint coordinate systems.");
            bool restrained = attributes.Any(a => a.Is("NodeRestraintAttributeModel") || a.Is("NodeStiffnessAttributeModel"));
            if (restrained && n.Field("Loads").Items.Any(l => l.Is("NodeDisplacementLoadModel"))) throw new NotSupportedException("LegacyPrescribedDisplacementNotMappedYet");
            var names = new[] { "Tx", "Ty", "Tz", "Rx", "Ry", "Rz" };
            foreach (var a in attributes)
            {
                if (a.Is("RigidConnectionAttributeModel")) continue; // the rigid connections after the attributes (Node)
                if (a.Is("NodeCoordinateAttributeModel"))
                    node.Assignments.Restrains.Add(new RestrainAssignment { Restrain = new NodeRestrain(node, Frame(a["Frame"], units), Enumerable.Range(0, 6).Select(k => new DofRestrain((GeometryRestrain.DOF)k, false)).ToList()), SourceRecord = FrameRecord });
                else if (a.Is("NodeRestraintAttributeModel"))
                    node.Assignments.Restrains.Add(new RestrainAssignment { Restrain = new NodeRestrain(node, CoordinateSystem.Global, Enumerable.Range(0, 6).Select(k => new DofRestrain((GeometryRestrain.DOF)k, a.Bool(names[k]))).ToList()), SourceRecord = RestraintRecord });
                else if (a.Is("NodeStiffnessAttributeModel"))
                    node.Assignments.Restrains.Add(new RestrainAssignment
                    {
                        Restrain = new NodeRestrain(node, CoordinateSystem.Global, Enumerable.Range(0, 6).Select(k =>
                        {
                            double spring = a.Double(names[k]) * (k < 3 ? units.Force / units.Length : units.Force * units.Length);
                            return spring == 0 ? new DofRestrain((GeometryRestrain.DOF)k, false) : new DofRestrain((GeometryRestrain.DOF)k, spring);
                        }).ToList()),
                        SourceRecord = StiffnessRecord
                    });
                else if (a.Is("ElementIdAttributeModel"))
                {
                    string value = a.String("Value");
                    if (string.IsNullOrEmpty(value)) report.Carry("Empty Element ID attribute"); else node.Attributes.Add(new ElementLabelAttribute(value));
                }
                else throw new NotSupportedException("LegacyNodeAttributeNotMappedYet: " + a.ShortType);
            }
            foreach (var l in n.Field("Loads").Items)
            {
                var c = cases[l.Field("LoadCase").String("Name")];
                if (l.Is("NodeForceLoadModel")) node.Loads.Add(new PointLoad(Vector(l) * units.Force, Zero, node.Position, c) { Name = ForceRecord });
                else if (l.Is("NodeMomentLoadModel")) node.Loads.Add(new PointLoad(Zero, Vector(l) * (units.Force * units.Length), node.Position, c) { Name = MomentRecord });
                else if (l.Is("NodeTemperatureLoadModel"))
                {
                    string type = l.Enum("LoadType");
                    var kind = type == "Reference" ? NodalTemperatureKind.Reference : type == "Fixed" ? NodalTemperatureKind.Prescribed : type == "Initial" ? NodalTemperatureKind.Initial
                        : throw new NotSupportedException("LegacyNodeTemperatureType: " + type);
                    (node.Assignments.Physical ??= new NodePhysicalData()).Temperatures.Add(new NodalTemperatureAssignment { Case = c, Kind = kind, Celsius = units.ToCelsius(l.Double("Value")) });
                }
                else throw new NotSupportedException("LegacyNodeLoadNotMappedYet: " + l.ShortType);
            }
        }

        /// <summary>The kinds of the records of a node draft (<see cref="NodeData"/>): the <see cref="RestrainAssignment.SourceRecord"/> of its
        /// coordinate systems, restraints and stiffnesses, and the name of its force and moment loads.</summary>
        internal const string FrameRecord = "NodeCoordinateAttributeModel", RestraintRecord = "NodeRestraintAttributeModel", StiffnessRecord = "NodeStiffnessAttributeModel",
            ForceRecord = "NodeForceLoadModel", MomentRecord = "NodeMomentLoadModel";
        internal static readonly Vector3d Zero = new Vector3d(0, 0, 0);

        /// <summary>
        /// The attributes and loads of a former beam at list level (chain E, e3): every Element ID in <see cref="LegacyElementRecords.Labels"/>
        /// (the first as the label of the beam), super element names and bridge members counted in the records (the bridge data have the
        /// first), the output stations of the last record (without the default of 5), every release set and prestrain kept, tapers counted
        /// (the beam has the first); the resolution (<see cref="LegacyAssembly.ResolveBeam"/>) gives the former warnings and refuses the
        /// duplicates with the former messages, with the number and name of the beam in the model.
        /// </summary>
        private static void BeamData(LegacyValue b, BeamElement beam, CoordinateSystem principal, Vector3d offset, Dictionary<string, LoadCaseBase> cases, BridgeUnits units, LegacyImportReport report,
            BridgeMembers members, LegacyElementRecords records)
        {
            foreach (var a in b.Field("Attributes").Items)
            {
                switch (a.ShortType)
                {
                    case "ElementIdAttributeModel": Label(beam, a, report, records); break;
                    case "SuperElementAttributeModel": members.SuperElement(a.String("Value"), beam, records); break;
                    case "BridgeMemberAttributeModel": members.Member(a.Field("BridgeMetadata"), beam, units, records); break;
                    case "BridgeSectionStateAttributeModel": break; // after the beam is complete (SectionState)
                    case "BeamOutputStationsModel":
                        (beam.Assignments.Physical ??= new BeamPhysicalData()).MinimumOutputStations = a.Int("MinimumNumberOfStations"); break;
                    case "BeamReleasesAttributeModel":
                        beam.Attributes.Add(Releases(a, units)); break;
                    case "BeamSupportAttributeModel":
                        (beam.Assignments.Physical ??= new BeamPhysicalData()).Supports.Add(Support(a, beam.Assignments.SectionAxes, units)); break;
                    case "BeamTaperAttributeModel":
                        if (++records.Tapers > 1) break;
                        (beam.Assignments.Physical ??= new BeamPhysicalData()).Taper = new BeamSectionTaper
                        {
                            ScaleI1 = a.Double("NFromX"), ScaleJ1 = a.Double("NToX"), ScaleI2 = a.Double("NFromY"), ScaleJ2 = a.Double("NToY"),
                            Alignment1 = TaperAlignmentOf(a.Enum("AlignmentX")), Alignment2 = TaperAlignmentOf(a.Enum("AlignmentY")),
                        };
                        break;
                    case "BeamStringGroupAttributeModel":
                        (beam.Assignments.Physical ??= new BeamPhysicalData()).StringGroup = GpcIdentity.Derive(GpcIdentity.NameSpace, "string-group:" + a.Int("ID")); break;
                    default: throw new NotSupportedException("LegacyBeamAttributeNotMappedYet: " + a.ShortType);
                }
            }
            int loadIndex = 0;
            foreach (var l in b.Field("Loads").Items)
            {
                var loadCase = cases[l.Field("LoadCase").String("Name")]; string source = "BeamLoad/" + beam.Id + "/" + (++loadIndex);
                switch (l.ShortType)
                {
                    case "BeamDistributedLoadModel": beam.Assignments.Loads.AddRange(Distributed(l, beam, principal, offset, loadCase, units, source)); break;
                    case "BeamPointLoadModel": beam.Assignments.Loads.Add(PointLoad(l, beam, principal, offset, loadCase, units, source)); break;
                    case "BeamPreLoadModel":
                        // Two prestrains in one case: refused by the resolution (LegacyAssembly.ResolveBeam), with the number of the beam.
                        bool strain = l.Enum("LoadType") == "Strain";
                        (beam.Assignments.Physical ??= new BeamPhysicalData()).InitialStates.Add(new BeamInitialState
                        { Case = loadCase, Kind = strain ? BeamInitialStateKind.AxialStrain : BeamInitialStateKind.AxialForce, Value = strain ? l.Double("Value") : units.ToN(l.Double("Value")) });
                        break;
                    case "BeamGradientLoadModel":
                        (beam.Assignments.Physical ??= new BeamPhysicalData()).TemperatureGradients.Add(new BeamTemperatureGradient
                        { Case = loadCase, Axis1 = units.ToCelsiusPerMm(l.Double("Axis1")), Axis2 = units.ToCelsiusPerMm(l.Double("Axis2")) });
                        break;
                    // The slab eigenstrain of a composite beam: a checks input (the strain of the workflow shrinkage action), not an FE load.
                    case "CompositeBeamSlabStrainModel": report.Carry("Composite slab strain (checks input: the strain of the workflow shrinkage action)"); break;
                    default: throw new NotSupportedException("LegacyBeamLoadNotMappedYet: " + l.ShortType);
                }
            }

        }

        private static IEnumerable<GPC.Model.Loads.Assignments.BeamLoadAssignment> Distributed(LegacyValue l, BeamElement beam, CoordinateSystem principal, Vector3d offset,
            LoadCaseBase loadCase, BridgeUnits units, string source)
        {
            var schema = (BridgeLoadSchema)Enum.Parse(typeof(BridgeLoadSchema), l.Enum("LoadSchema"));
            bool moment = l.Enum("LoadType") == "Moment"; string direction = l.Enum("LoadDirection");
            double pa = l.Double("PA"), pb = l.Double("PB"), p1 = l.Double("P1"), p2 = l.Double("P2"), a = l.Double("A"), b = l.Double("B");
            double scale = moment ? units.ToNmmPerMm(1) : units.ToNPerMm(1);
            if (l["CoordinateSystem"].IsNull)
            {
                // Principal (local) axes: X, Y, Z are the beam principal axes 1, 2, 3.
                var axis = direction == "X" ? Vector3d.XAxis : direction == "Y" ? Vector3d.YAxis : direction == "Z" ? Vector3d.ZAxis : throw new NotSupportedException("LegacyLocalLoadDirection: " + direction);
                return GpcBeamLoads.Distributed(loadCase, beam.StartPoint, beam.EndPoint, schema, pa, pb, p1, p2, a, b, axis, moment, principal, null, offset, scale, source);
            }
            // The former exporter wrote every global distributed action as a global force (also LoadType = Moment).
            if (moment) throw new NotSupportedException("LegacyGlobalDistributedMomentWasExportedAsForce");
            if (!IsGlobal(l["CoordinateSystem"])) throw new NotSupportedException("LegacyNonGlobalLoadCoordinateSystemWasIgnored");
            bool gravity = direction.StartsWith("Gravity", StringComparison.Ordinal);
            bool projected = direction.EndsWith("_Projected", StringComparison.Ordinal);
            string d = gravity ? "Z" : direction.Substring(0, 1);
            var global = d == "X" ? Vector3d.XAxis : d == "Y" ? Vector3d.YAxis : Vector3d.ZAxis;
            // Gravity loads point down: positive values act along −Z.
            var vector = gravity ? new Vector3d(0, 0, -1) : global;
            return GpcBeamLoads.Distributed(loadCase, beam.StartPoint, beam.EndPoint, schema, pa, pb, p1, p2, a, b, vector, false, null,
                projected ? global : null, offset, scale, source);
        }

        private static GPC.Model.Loads.Assignments.BeamLoadAssignment PointLoad(LegacyValue l, BeamElement beam, CoordinateSystem principal, Vector3d offset,
            LoadCaseBase loadCase, BridgeUnits units, string source)
        {
            // The former archive did not store the coordinate system of a beam point load ("TODO" in BeamPointLoadModel), so every
            // analysis (which cloned the model first) exported it in the principal axes: read it as such.
            bool moment = l.Enum("LoadType") == "Moment";
            return GpcBeamLoads.Concentrated(loadCase, beam.StartPoint, beam.EndPoint, l.Double("A"), Vector(l["Force"]), moment,
                principal, offset, moment ? units.Force * units.Length : units.Force, source);
        }

        private static string PropertyName(LegacyValue p) => p.String("Name") ?? p.String("Id") ?? "Property";
        /// <summary>Content of a legacy property as text (to detect different properties with the same name).</summary>
        private static string Signature(LegacyValue v)
        {
            if (v.IsNull) return "null";
            if (v.Text != null) return v.Text;
            if (v.IsCollection) return "[" + string.Join(",", v.Items.Select(Signature)) + "]";
            return "{" + string.Join(";", v.Fields.Where(f => f.Key != "CompositePreview").OrderBy(f => f.Key, StringComparer.Ordinal).Select(f => f.Key + "=" + Signature(f.Value))) + "}";
        }

        /// <summary>F6 chain D: a former beam property converted on its own, with the rules of the conversion of a whole model (the model
        /// transformers that replace the property of beams, e.g. Bridge Phase Model); dimensions in the definition <paramref name="units"/>.</summary>
        public static LegacyBeamProperty BeamProperty(LegacyValue property, BridgeUnits units)
        {
            if (property == null || property.IsNull || !property.Is("BeamPropertyModel")) throw new ArgumentException("LegacyBeamPropertyModelExpected");
            return BeamProperty(property, PropertyName(property), units, new Dictionary<string, (double, double)>(StringComparer.Ordinal), new LegacyImportReport(), Signature(property));
        }

        private static string Digest(string signature) => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(signature)));

        /// <summary>The GPC material of a beam property of the former exporter (<see cref="GpcMaterials.Elastic"/>: E, ν, G with Use Poisson,
        /// density, thermal expansion, damping, conductivity, specific heat, stress-strain table), the one rule of the reader for every beam
        /// property; chain E: the steel of the analysis properties of a composite section (<c>GpcModels.Material</c>).</summary>
        public static Material ElasticMaterial(LegacyValue m)
        {
            if (m == null || m.IsNull) throw new ArgumentException("LegacyMaterialModelExpected");
            return GpcMaterials.Elastic(m.String("Name"), m.Double("Modulus"), m.Double("PoissonRatio"), m.Double("ShearModulus"), m.Bool("UsePoisson"),
                m.Double("Density"), m.Double("ThermalExpansion"), m.Double("ViscousDamping"), m.Double("DampingRatio"), m.Double("Conductivity"), m.Double("SpecificHeat"),
                StressStrain(m["StressStrainTable"]));
        }

        /// <param name="checkLengthUnit">False for a draft (definition units, identity factors): the length unit of a library or BXS section
        /// is checked against the units of the model by the assembly (<see cref="LegacyAssembly.NormaliseBeamProperty"/>).</param>
        private static LegacyBeamProperty BeamProperty(LegacyValue p, string propertyName, BridgeUnits units, Dictionary<string, (double, double)> shearAreas, LegacyImportReport report, string signature,
            bool checkLengthUnit = true)
        {
            var (property, formulation, angle, native, recipe) = Property(p, propertyName, units, shearAreas, report, checkLengthUnit);
            return new LegacyBeamProperty(property, new LegacyBeamPropertyData(formulation, angle, native, Digest(signature)) { Section = recipe });
        }

        private static (BeamProperty, BeamFormulation, double, GPC.Converter.Straus7.Straus7SectionDefinition, LegacySectionRecipe) Property(LegacyValue p, string propertyName, BridgeUnits units,
            Dictionary<string, (double, double)> shearAreas, LegacyImportReport report, bool checkLengthUnit)
        {
            string type = p.Enum("BeamPropertyType");
            var formulation = type == "Beam" ? BeamFormulation.StraightTwoNode : type == "Truss" ? BeamFormulation.Truss : type == "Cable" ? BeamFormulation.Cable
                : type == "CutoffBar" ? BeamFormulation.CutoffBar : type == "PointContact" ? BeamFormulation.PointContact : type == "Pipe" ? BeamFormulation.Pipe
                : type == "Connection" ? BeamFormulation.Connection : throw new NotSupportedException("LegacyBeamPropertyType: " + type);
            if (formulation == BeamFormulation.CutoffBar) throw new NotSupportedException("LegacyCutoffBarNotMappedYet");
            var m = p.Field("Material"); var s = p.Field("SectionModel");
            if (s.Enum("MirrorType") != "None") throw new NotSupportedException("LegacyMirroredSectionNotMappedYet");
            var material = ElasticMaterial(m);
            if (!m["StressStrainTable"].IsNull && type != "Beam" && type != "Truss") material.AnalysisData.StressStrainPairs = null; // former exporter: tables only for beams and trusses
            var recipe = SectionRecipe(s);
            var (property, angle, native) = SectionProperty(recipe, propertyName, material, units, shearAreas, report, checkLengthUnit);
            return (property, formulation, angle, native, recipe);
        }

        /// <summary>Chain E (e3 point 5): the section of a former SectionModel in the definition units (the numbers as written, nothing derived).</summary>
        private static LegacySectionRecipe SectionRecipe(LegacyValue s)
        {
            if (!s["NativeSection"].IsNull)
            {
                var n = s["NativeSection"]; string key = n.String("Key");
                return new LegacySectionRecipe
                {
                    NativeKey = key, NativeLengthUnit = GpcNativeSections.IsLibraryOrBxs(key) ? n.Int("LengthUnit") : 0,
                    NativeDimensions = n["Dimensions"].IsNull ? Array.Empty<double>() : n["Dimensions"].Items.Select(v => v.AsDouble()).ToArray(),
                    BxsPath = n.String("BxsPath"), LibraryName = n.String("LibraryName"), LibraryItem = n.String("LibraryItem"),
                };
            }
            var kind = (BridgeSectionKind)Enum.Parse(typeof(BridgeSectionKind), s.Enum("SectionType"));
            string name = s.String("Name") ?? ""; double angle = s.Double("AngleX1");
            if (kind == BridgeSectionKind.Composite) return new LegacySectionRecipe { Kind = kind, Name = name, Angle = angle };
            if (kind == BridgeSectionKind.Generic)
            {
                var c = s["Centroid"];
                return new LegacySectionRecipe
                {
                    Kind = kind, Name = name, Angle = angle, Area = s.Double("Area"), I11 = s.Double("I11"), I22 = s.Double("I22"), J = s.Double("J"), CentroidX = c.Double("X"), CentroidY = c.Double("Y"),
                    ShearL1 = s.Double("ShearL1"), ShearL2 = s.Double("ShearL2"), ShearA1 = s.Double("ShearA1"), ShearA2 = s.Double("ShearA2"), CompositePreview = !s["CompositePreview"].IsNull,
                };
            }
            return new LegacySectionRecipe
            {
                Kind = kind, Name = name, Angle = angle, D = s.Double("D"), B = s.Double("B"), B1 = s.Double("B1"), B2 = s.Double("B2"), T = s.Double("T"), T1 = s.Double("T1"), T2 = s.Double("T2"), T3 = s.Double("T3"),
            };
        }

        /// <summary>The message of a library or BXS section read in a length unit other than that of the model (former exporter).</summary>
        internal const string LibraryUnitsMessage = "Le unità del modello differiscono da quelle usate per leggere la sezione di libreria/BXS. Ricalcolare la sezione nelle unità del modello.";

        /// <summary>
        /// Chain E (e3 point 5, coordinator decision 1 of 5/11): the beam property of a section in the definition <paramref name="units"/>, with
        /// the operations of the former exporter. The reader converts a former property with it, Build Model normalises the property of a draft
        /// with it (<see cref="LegacyAssembly.NormaliseBeamProperty"/>): the same numbers in, the same operations, the same section.
        /// </summary>
        internal static (BeamProperty Property, double Angle, GPC.Converter.Straus7.Straus7SectionDefinition Native) SectionProperty(LegacySectionRecipe r, string propertyName, Material material,
            BridgeUnits units, Dictionary<string, (double, double)> shearAreas, LegacyImportReport report, bool checkLengthUnit = true)
        {
            if (r.NativeKey != null)
            {
                // Former exporter: the native section was assigned again in the model units (standard and BGL dimensions as numbers in
                // the model length unit; BXS and library sections only when read in the model length unit); Straus7 computed the properties.
                if (checkLengthUnit && GpcNativeSections.IsLibraryOrBxs(r.NativeKey) && r.NativeLengthUnit != GpcNativeSections.Straus7LengthUnit(units.Length))
                    throw new ArgumentException(LibraryUnitsMessage);
                var resolved = GpcNativeSections.Resolve(r.NativeKey, r.NativeDimensions.ToArray(), r.BxsPath, r.LibraryName, r.LibraryItem, units.Length).Section;
                var nativeProperty = resolved.ToBeamProperty(propertyName, material);
                report.Carry("Native section preview contours (display only)");
                return (nativeProperty, resolved.ReferenceProperties[10], resolved);
            }
            var kind = r.Kind;
            string sectionName = r.Name; double angle = r.Angle;
            double L(double v) => units.ToMm(v);
            ISectionShape section; double shear1, shear2;
            if (kind == BridgeSectionKind.Generic || kind == BridgeSectionKind.Composite)
            {
                if (kind == BridgeSectionKind.Composite) throw new NotSupportedException("LegacyCompositeSectionTypeNotMappedYet");
                section = GpcSections.Explicit(sectionName, units.ToMm2(r.Area), units.ToMm4(r.I11), units.ToMm4(r.I22), units.ToMm4(r.J),
                    units.ToMm(r.CentroidX), units.ToMm(r.CentroidY), units.ToMm(r.ShearL1), units.ToMm(r.ShearL2), angle);
                shear1 = units.ToMm2(r.ShearA1); shear2 = units.ToMm2(r.ShearA2);
                if (r.CompositePreview) report.Carry("Composite preview geometry (preview and checks input, not exported)");
            }
            else
            {
                if (angle != 0) throw new NotSupportedException("LegacyStandardSectionWithPrincipalAngleNotMappedYet");
                section = GpcSections.Standard(kind, sectionName, L(r.D), L(r.B), L(r.B1), L(r.B2), L(r.T), L(r.T1), L(r.T2), L(r.T3));
                // Shear areas from the GPC section geometry (user decision C2; Straus7 no longer derives them): De Saint Venant at nu = 0
                // (user decision 4/11, public GPCModel API of L6), so they depend on the geometry only.
                string key = kind + "/" + string.Join(",", new[] { r.D, r.B, r.B1, r.B2, r.T, r.T1, r.T2, r.T3 }.Select(v => v.ToString("R")));
                if (!shearAreas.TryGetValue(key, out var areas))
                {
                    var computed = GPC.Model.Sections.SectionShearAreas.Compute(section);
                    shearAreas.Add(key, areas = (computed.Area1, computed.Area2));
                }
                (shear1, shear2) = areas;
            }
            var property = new ElasticBeamProperty(propertyName, material, section, shear1, shear2);
            return (property, angle, null);
        }

        private enum PatchKinds { Auto1, Auto2, Auto3, Auto4, AngleSplit, Manual }

        /// <summary>The fingerprint of a section state the former property no longer matches (stale: never equal to a property fingerprint).</summary>
        public const string StaleSectionStatePrefix = "BridGHe stale section state: ";

        /// <summary>F6 (L11): the former local section state of a beam (Bridge State Override, a checks input) as the descriptive GPC
        /// <see cref="GPC.Model.Structure.Bridges.BridgeSectionStateAttribute"/> (skipped by the Straus7 writer, outside the analysis fingerprint):
        /// behaviour, concrete excluded, modular ratio, classification case (empty: none) and the fingerprint of the GPC property of the beam
        /// (<see cref="GPC.Checkers.CompositeBridge.BridgeWorkflowDefinition.PropertyFingerprint"/>, the one D3 uses) when the former state was
        /// current for its property, else a stale marker (the checks refuse it, as the former ones refused the stale signature). Only the first
        /// state of a beam is kept (Bridge State Override writes one).</summary>
        private static void SectionState(LegacyValue b, int k, BeamElement beam, Func<int, string> femPropertySignature, LegacyImportReport report, LegacyElementRecords records)
        {
            var states = b.Field("Attributes").Items.Where(a => a.Is("BridgeSectionStateAttributeModel")).ToArray();
            if (states.Length == 0) return;
            // e3: the states counted at list level; the warning for the others comes with the resolution (LegacyAssembly.ResolveBeam).
            records.SectionStates = states.Length;
            var s = states[0].Field("SectionAssignment");
            if (!Enum.TryParse<GPC.Checkers.CompositeBridge.BridgeActionBehavior>(s.Enum("Behavior"), false, out var behavior) || behavior == GPC.Checkers.CompositeBridge.BridgeActionBehavior.Steel)
                throw new ArgumentException("LegacyBridgeSectionStateBehavior: " + s.Enum("Behavior"));
            string signature = s.String("PropertySignature") ?? "";
            bool current = femPropertySignature == null || femPropertySignature(k) == signature;
            string classification = s.String("ClassificationCase");
            // L12: the state is bridge data of the beam (by GUID), outside the generic model.
            report.Bridge.SetSectionState(beam.Guid, new GPC.Checkers.CompositeBridge.BridgeLocalSectionState(behavior, s.Bool("ConcreteExcluded"), s.Double("ModularRatio"),
                string.IsNullOrEmpty(classification) ? null : classification,
                current ? GPC.Checkers.CompositeBridge.BridgeWorkflowDefinition.PropertyFingerprint(beam) : StaleSectionStatePrefix + signature));
        }

        /// <summary>Former releases: per end and kind, a released DOF with zero partial value is released, with a value it is a partial
        /// (absolute) stiffness; the others stay continuous. Beam principal axes, as St7SetBeamTRelease3/RRelease3.</summary>
        /// <summary>C1: the former Element ID (ElementIdComponent: the user's identifier of a node, beam or plate) as the GPC element label,
        /// descriptive (the Straus7 writer does not write it; the former exporter numbered the native elements itself).</summary>
        /// <remarks>e3: every value at list level (<see cref="LegacyElementRecords.Labels"/>); the element takes the first as its label, the
        /// warning for each other comes with the resolution.</remarks>
        private static void Label(Element element, LegacyValue a, LegacyImportReport report, LegacyElementRecords records)
        {
            string value = a.String("Value");
            if (string.IsNullOrEmpty(value)) { report.Carry("Empty Element ID attribute"); records.EmptyLabels++; return; }
            records.LabelList.Add(value);
            if (records.LabelList.Count == 1) element.Attributes.Add(new ElementLabelAttribute(value));
        }

        /// <summary>
        /// C1 (L12, user decision of 5/11): the bridge identities of the former beams as bridge data of the beam (by GUID, <see cref="LegacyImportReport.Bridge"/>),
        /// never in the generic model:
        /// - bridge member metadata (BridgeMemberAttributeModel: the physical member of the FE part, its stations in mm, section up and geometry
        ///   id) → <c>SetMember</c>; one member per beam (<c>LegacyDuplicateBridgeMemberAttribute</c>); the parts of a member are its beams in
        ///   numbering order (<c>BridgeModel.PhysicalMember</c>);
        /// - super elements (SuperElementAttributeModel: the beams checked together by Super Element Check) → <c>SetSuperElement</c>; two
        ///   attributes with the same name are one super element, two names are an error (<c>LegacyBeamInTwoSuperElements</c>, lead decision of 5/11).
        /// </summary>
        private sealed class BridgeMembers
        {
            private readonly GPC.Checkers.CompositeBridge.BridgeModelData data;
            public BridgeMembers(GPC.Checkers.CompositeBridge.BridgeModelData data) { this.data = data; }
            /// <remarks>e3: the distinct names at list level; two names are refused by the resolution (<see cref="LegacyAssembly.ResolveBeam"/>).</remarks>
            public void SuperElement(string name, BeamElement beam, LegacyElementRecords records)
            {
                if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("LegacyEmptySuperElementName");
                if (records.SuperElementList.Contains(name, StringComparer.Ordinal)) return;
                records.SuperElementList.Add(name);
                if (records.SuperElementList.Count == 1) data.SetSuperElement(beam.Guid, name);
            }
            /// <remarks>e3: counted at list level (the bridge data keep the first); a second is refused by the resolution.</remarks>
            public void Member(LegacyValue m, BeamElement beam, BridgeUnits units, LegacyElementRecords records)
            {
                if (++records.Members > 1) return;
                string id = m.String("PhysicalId");
                if (!Enum.TryParse<GPC.Checkers.CompositeBridge.Layout.BridgeMemberFamily>(m.Enum("Family"), false, out var family)) throw new ArgumentException("LegacyBridgeMemberFamily: " + m.Enum("Family"));
                var up = m.Field("SectionUp");
                var metadata = new GPC.Checkers.CompositeBridge.Layout.BridgeMemberMetadata(id, family, m.Int("Group"), m.Int("Segment"), m.Int("Span"),
                    units.ToMm(m.Double("StationFrom")), units.ToMm(m.Double("StationTo")), new Vector3d(up.Double("X"), up.Double("Y"), up.Double("Z")), m.String("GeometryId") ?? "");
                if (data.Member(beam.Guid) != null) throw new ArgumentException("LegacyDuplicateBridgeMemberAttribute: beam " + beam.Name);
                data.SetMember(beam.Guid, metadata);
            }
        }

        private static GPC.Model.Attributes.BeamReleasesAttribute Releases(LegacyValue a, BridgeUnits units)
        {
            var release = new GPC.Model.Attributes.BeamReleasesAttribute { SourceRecord = "BeamReleasesAttributeModel" };
            void End(GPC.Model.Attributes.BeamDofConnection[] target, string trans, string rot, string transPartial, string rotPartial)
            {
                var tp = Vector(a[transPartial]); var rp = Vector(a[rotPartial]);
                var t = new[] { tp.X, tp.Y, tp.Z }; var r = new[] { rp.X, rp.Y, rp.Z };
                for (int k = 0; k < 3; k++)
                {
                    if (a.Bool(trans + k)) target[k] = t[k] == 0 ? new GPC.Model.Attributes.BeamDofConnection(GPC.Model.Attributes.BeamConnectionKind.Released)
                        : new GPC.Model.Attributes.BeamDofConnection(GPC.Model.Attributes.BeamConnectionKind.AbsoluteStiffness, t[k] * units.Force / units.Length);
                    if (a.Bool(rot + k)) target[3 + k] = r[k] == 0 ? new GPC.Model.Attributes.BeamDofConnection(GPC.Model.Attributes.BeamConnectionKind.Released)
                        : new GPC.Model.Attributes.BeamDofConnection(GPC.Model.Attributes.BeamConnectionKind.AbsoluteStiffness, r[k] * units.Force * units.Length);
                }
            }
            End(release.I, "TransNode1", "RotNode1", "TransN1Partial", "RotN1Partial");
            End(release.J, "TransNode2", "RotNode2", "TransN2Partial", "RotN2Partial");
            return release;
        }

        /// <summary>Former BeamSupportExport: simple springs along +-1/+-2 of the beam axes; tension-only flips the direction.</summary>
        private static BeamDistributedSupport Support(LegacyValue a, CoordinateSystem axes, BridgeUnits units)
        {
            if (a.Enum("SpringType") != "Simple" || a.Enum("SpringTensionOrientation") != "BeamLocalAxis")
                throw new NotSupportedException("Straus beam supports require simple springs along principal local axes 1 or 2.");
            double stiffness = a.Double("Stiffness"), gap = a.Double("Gap");
            if (!(stiffness > 0) || double.IsInfinity(stiffness) || double.IsNaN(gap) || double.IsInfinity(gap)) throw new ArgumentException("Beam support requires finite positive stiffness [F/L2] and finite gap [L].");
            Vector3d direction;
            switch (a.Enum("LocalAxisDirection"))
            {
                case "negative1Axis": direction = new Vector3d(-1, 0, 0); break;
                case "positive1Axis": direction = new Vector3d(1, 0, 0); break;
                case "negative2Axis": direction = new Vector3d(0, -1, 0); break;
                case "positive2Axis": direction = new Vector3d(0, 1, 0); break;
                default: throw new NotSupportedException("Straus beam supports accept only +-1 and +-2, not longitudinal axis 3.");
            }
            DistributedSpringBehavior behavior;
            switch (a.Enum("SimpleSpringTypes"))
            {
                case "TensionCompression": behavior = DistributedSpringBehavior.Bilateral; break;
                case "CompressionOnly": behavior = DistributedSpringBehavior.CompressionOnly; break;
                case "TensionOnly": behavior = DistributedSpringBehavior.TensionOnly; break;
                default: throw new ArgumentException("Unknown spring behaviour.");
            }
            if (behavior == DistributedSpringBehavior.Bilateral && gap != 0) throw new ArgumentException("A gap requires a compression-only spring.");
            return new BeamDistributedSupport
            {
                Stiffness = stiffness * units.Force / (units.Length * units.Length), Gap = units.ToMm(gap), Behavior = behavior,
                Direction = direction, Axes = new CoordinateSystem(new Point3d(0, 0, 0), axes.V1, axes.V2)
            };
        }

        private static TaperAlignment TaperAlignmentOf(string code)
        {
            switch (code)
            {
                case "Bottom": return TaperAlignment.NegativeSide;
                case "Center": return TaperAlignment.Symmetric;
                case "Top": return TaperAlignment.PositiveSide;
                default: throw new NotSupportedException("LegacyTaperAlignment: " + code);
            }
        }

        private static PlateProperty PlateProperty(LegacyValue p, string name, BridgeUnits units)
        {
            string type = p.Any("PropertyType", "_platePropertyType").Text?.Trim();
            if (type == "LoadPatch") return new ElasticPlateProperty(name, null, 0, 0, ElasticPlateFormulation.LoadPatch);
            var m = p.Any("Material", "_material");
            var material = GpcMaterials.Elastic(m.String("Name"), m.Double("Modulus"), m.Double("PoissonRatio"), m.Double("ShearModulus"), true,
                m.Double("Density"), m.Double("ThermalExpansion"), m.Double("ViscousDamping"), m.Double("DampingRatio"), m.Double("Conductivity"), m.Double("SpecificHeat"), null);
            if (!m["StressStrainTable"].IsNull) throw new NotSupportedException("LegacyPlateStressStrainTableNotMappedYet");
            double membrane = units.ToMm(p.Any("MembraneThickness", "_membraneThickness").AsDouble()), bending = units.ToMm(p.Any("BendingThickness", "_bendingThickness").AsDouble());
            switch (type)
            {
                case "ShellThin": case "ShellThick": case "PlateThin": case "PlateThick":
                    // The former exporter wrote all four as the native ptPlateShell with the two thicknesses.
                    return new ElasticPlateProperty(name, material, bending, membrane, ElasticPlateFormulation.Shell);
                case "Membrane": return new ElasticPlateProperty(name, material, 0, membrane, ElasticPlateFormulation.Membrane);
                case "ShearPanel": return new ElasticPlateProperty(name, material, bending, membrane, ElasticPlateFormulation.ShearPanel);
                default: throw new NotSupportedException("LegacyPlatePropertyType: " + type);
            }
        }

        private static (double stress, double strain)[] StressStrain(LegacyValue table)
        {
            if (table.IsNull) return null;
            return table.Items.Select(t => (t.Double("Item1"), t.Double("Item2"))).ToArray();
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
