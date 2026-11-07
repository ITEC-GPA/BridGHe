using System;
using System.Collections.Generic;
using System.Linq;
using BridGHe.Common.Gpc.Legacy;
using GPC.Geometry;
using GPC.Model.Attributes;
using GPC.Model.Elements;
using GPC.Model.LoadCases;
using GPC.Model.Loads;
using GPC.Model.Loads.Assignments;
using GPC.Model.Structure.Assignments;

namespace BridGHe.Common.Gpc.Builders
{
    /// <summary>
    /// R6.2, lane B (beams): the GPC builders of the beam drafts of the producer components. Each builder gives the draft the reader would
    /// give the former value with the change (<see cref="LegacyModelImport.BeamDraft"/>): the same functions of the reader on plain data
    /// (<c>LegacyModelImport.Beams.cs</c>: <c>NewBeam</c>, <c>OutputStations</c>, <c>Releases</c>, <c>Support</c>, <c>Taper</c>,
    /// <c>StringGroup</c>, <c>Distributed</c>, <c>PointLoad</c>, <c>PreLoad</c>, <c>Gradient</c>, <c>BridgeMembers</c>), same collections, same
    /// order, same records. A builder never changes its input (it works on a <see cref="GpcDrafts.Copy(LegacyBeamDraft)"/>) and ends with the
    /// identity given (<see cref="GpcDrafts.Reidentify(LegacyBeamDraft, Guid)"/>; until R6.5 the identity of the conversion of the FEM output,
    /// <see cref="GpcBeamDrafts.Identity(Models.BeamModel)"/>). Units: those of the draft (the definition units). The property of a new beam is the
    /// datum of the input property (<c>GH_BeamProperty.Gpc</c>): its data, signature included (passo 53: until R6.5 the signature of the
    /// conversion of the FEM property).
    /// </summary>
    public static class GpcBeamBuilders
    {
        /// <summary>The names of the former <c>BeamDistributedLoadModel.LoadDirection</c> values 0..7 (the Direction input of the load
        /// components is the enum value): X, Y, Z (local: principal axes 1, 2, 3), their projected forms, gravity.</summary>
        public static readonly IReadOnlyList<string> LoadDirections = new[] { "X", "Y", "Z", "X_Projected", "Y_Projected", "Z_Projected", "Gravity", "Gravity_Projected" };

        /// <summary>The former <c>BeamTaperAttributeModel.AlignmentCodes</c> by value (Top 0, Center 1, Bottom 2).</summary>
        public static readonly IReadOnlyList<string> TaperAlignments = new[] { "Top", "Center", "Bottom" };

        /// <summary>The former name of an enum value by its integer (out of range: the integer as text, as the former archive wrote it).</summary>
        /// <summary>A number as the reader reads it from a former archive: the binary XML of the archive writes a zero without its sign, so a
        /// −0 of the component reads +0 (found by gpc-builders-b on the curved bridge: the section up of the diaphragms). Every raw number a
        /// builder takes goes through it.</summary>
        public static double Z(double v) => v == 0 ? 0.0 : v;
        public static Point3d Z(Point3d p) => new Point3d(Z(p.X), Z(p.Y), Z(p.Z));
        public static Vector3d Z(Vector3d v) => new Vector3d(Z(v.X), Z(v.Y), Z(v.Z));

        public static string Code(IReadOnlyList<string> names, int value) => value >= 0 && value < names.Count ? names[value] : value.ToString(System.Globalization.CultureInfo.InvariantCulture);

        // -------------------------------------------------------------------------------------------------------------------------------------
        // New beams

        /// <summary>
        /// Beam (and the bridge generators): a new beam on its ends, with the property datum given, its groups (each as the chain of names
        /// from the root: <see cref="GroupChains"/>), BridGHe angle (degrees) and offset (principal x/y), BridGHe ID (empty: none); the beam the
        /// reader gives a new former beam value: number 1, its property converted on its own, the first group with its parents, every group
        /// by full name in the records, the carries of the property.
        /// </summary>
        public static LegacyBeamDraft New(Point3d from, Point3d to, LegacyBeamProperty property, IReadOnlyList<IReadOnlyList<string>> groups, double angleDeg, double offsetX, double offsetY,
            Guid identity, string beamId = "")
        {
            var d = Start(from, to, property, groups, angleDeg, offsetX, offsetY, identity, beamId);
            return Finish(d);
        }

        /// <summary>A new beam draft not finished yet (no state refresh): the start of <see cref="New"/>, for the generators that add their
        /// attributes in the former order (<see cref="Work"/>).</summary>
        internal static Work Start(Point3d from, Point3d to, LegacyBeamProperty property, IReadOnlyList<IReadOnlyList<string>> groups, double angleDeg, double offsetX, double offsetY,
            Guid identity, string beamId)
        {
            if (property == null) throw new ArgumentNullException(nameof(property));
            from = Z(from); to = Z(to); angleDeg = Z(angleDeg); offsetX = Z(offsetX); offsetY = Z(offsetY);
            var units = GpcBeamDrafts.DraftUnits;
            var report = new LegacyImportReport { Units = units, Owner = identity, Bridge = new GPC.Checkers.CompositeBridge.BridgeModelData() };
            var records = new LegacyElementRecords();
            records.PropertyCarryList.AddRange(PropertyCarries(property.Data));
            groups ??= Array.Empty<IReadOnlyList<string>>();
            var chains = groups.Select(c => c.ToArray()).ToArray();
            var (beam, _, _) = LegacyModelImport.NewBeam(identity, identity, 0, beamId ?? "", new Point3d(units.ToMm(from.X), units.ToMm(from.Y), units.ToMm(from.Z)),
                new Point3d(units.ToMm(to.X), units.ToMm(to.Y), units.ToMm(to.Z)), GPC.Model.Core.ModelValues.CopyValue(property.Property), property.Data.Formulation, property.Data.Angle,
                angleDeg, units.ToMm(offsetX), units.ToMm(offsetY), null, chains.Length > 0 ? () => GroupOf(chains[0]) : null, chains.Select(FullName), report, records);
            return new Work(new LegacyBeamDraft(beam, null, angleDeg, property.Data, report) { Records = records });
        }

        /// <summary>What the conversion of a beam property carries (<c>LegacyModelImport.SectionProperty</c>): the contours of a native section,
        /// the composite preview of an explicit section.</summary>
        internal static IEnumerable<KeyValuePair<string, int>> PropertyCarries(LegacyBeamPropertyData data)
        {
            if (data?.Native != null) yield return new KeyValuePair<string, int>("Native section preview contours (display only)", 1);
            else if (data?.Section != null && data.Section.NativeKey == null && data.Section.Kind == BridgeSectionKind.Generic && data.Section.CompositePreview)
                yield return new KeyValuePair<string, int>("Composite preview geometry (preview and checks input, not exported)", 1);
        }

        /// <summary>The full name of a group chain (names from the root), as the reader joins them.</summary>
        public static string FullName(IReadOnlyList<string> chain) => string.Join("\\", chain);

        /// <summary>The GPC group of a chain with its parents (identity by full name), as the reader builds the first group of a draft.</summary>
        private static Group GroupOf(IReadOnlyList<string> chain)
        {
            Group parent = null, group = null;
            for (int i = 0; i < chain.Count; i++)
            {
                string full = FullName(chain.Take(i + 1).ToArray());
                group = Group.Create(GpcIdentity.Group(full), full);
                parent?.AddChild(group);
                parent = group;
            }
            return group;
        }

        // -------------------------------------------------------------------------------------------------------------------------------------
        // Attributes

        /// <summary>Beam Output Stations.</summary>
        public static LegacyBeamDraft WithOutputStations(LegacyBeamDraft beam, int stations, Guid identity) =>
            Change(beam, identity, w => LegacyModelImport.OutputStations(w.Beam, stations));

        /// <summary>Beam Releases: per end, translations along 1, 2, 3 and rotations about 1, 2, 3 released (true) with their partial values.</summary>
        public static LegacyBeamDraft WithReleases(LegacyBeamDraft beam, bool[] transI, Vector3d transIPartial, bool[] rotI, Vector3d rotIPartial,
            bool[] transJ, Vector3d transJPartial, bool[] rotJ, Vector3d rotJPartial, Guid identity) =>
            Change(beam, identity, w => w.Beam.Attributes.Add(LegacyModelImport.Releases(transI, Z(transIPartial), rotI, Z(rotIPartial), transJ, Z(transJPartial), rotJ, Z(rotJPartial), w.Units)));

        /// <summary>Beam String Group.</summary>
        public static LegacyBeamDraft WithStringGroup(LegacyBeamDraft beam, int id, Guid identity) =>
            Change(beam, identity, w => LegacyModelImport.StringGroup(w.Beam, id));

        /// <summary>Beam Support: a simple spring along a principal local axis (former direction code and spring type names).</summary>
        public static LegacyBeamDraft WithSupport(LegacyBeamDraft beam, double stiffness, double gap, string localAxisDirection, string springType, Guid identity) =>
            Change(beam, identity, w => (w.Beam.Assignments.Physical ??= new GPC.Model.Structure.Assignments.BeamPhysicalData()).Supports.Add(
                LegacyModelImport.Support(Z(stiffness), Z(gap), () => localAxisDirection, () => springType, w.Beam.Assignments.SectionAxes, w.Units)));

        /// <summary>Beam Taper (former alignment names: Top, Center, Bottom).</summary>
        public static LegacyBeamDraft WithTaper(LegacyBeamDraft beam, double fromX, double fromY, double toX, double toY, string alignmentX, string alignmentY, Guid identity) =>
            Change(beam, identity, w => LegacyModelImport.Taper(w.Beam, w.Records, Z(fromX), Z(toX), Z(fromY), Z(toY), () => LegacyModelImport.TaperAlignmentOf(alignmentX), () => LegacyModelImport.TaperAlignmentOf(alignmentY)));

        /// <summary>Super Element on a beam.</summary>
        public static LegacyBeamDraft WithSuperElement(LegacyBeamDraft beam, string name, Guid identity) =>
            Change(beam, identity, w => w.SuperElement(name));

        // -------------------------------------------------------------------------------------------------------------------------------------
        // Loads

        /// <summary>Beam Distributed Load / Moment: the schema, the former direction name (<see cref="LoadDirections"/>), the intensities and the
        /// positions; <paramref name="globalSystem"/> null: in the principal axes, otherwise whether the coordinate system given is the global
        /// one.</summary>
        public static LegacyBeamDraft WithDistributedLoad(LegacyBeamDraft beam, LegacyCaseData loadCase, BridgeLoadSchema schema, bool moment, string direction,
            double pa, double pb, double p1, double p2, double a, double b, bool? globalSystem, Guid identity) =>
            Change(beam, identity, w => w.Load("BeamDistributedLoadModel", loadCase, (c, source) => w.Beam.Assignments.Loads.AddRange(LegacyModelImport.Distributed(schema, moment, direction,
                Z(pa), Z(pb), Z(p1), Z(p2), Z(a), Z(b), globalSystem.HasValue ? () => globalSystem.Value : (Func<bool>)null, w.Beam, w.Principal, w.Offset, c, w.Units, source))));

        /// <summary>Beam Point Load / Moment: the components at the normalised station <paramref name="a"/>, in the principal axes.</summary>
        public static LegacyBeamDraft WithPointLoad(LegacyBeamDraft beam, LegacyCaseData loadCase, bool moment, double a, Vector3d value, Guid identity) =>
            Change(beam, identity, w => w.Load("BeamPointLoadModel", loadCase, (c, source) => w.Beam.Assignments.Loads.Add(LegacyModelImport.PointLoad(moment, Z(a), Z(value), w.Beam, w.Principal, w.Offset, c, w.Units, source))));

        /// <summary>Beam Pre Load: an initial tension (force) or strain.</summary>
        public static LegacyBeamDraft WithPreLoad(LegacyBeamDraft beam, LegacyCaseData loadCase, bool strain, double value, Guid identity) =>
            Change(beam, identity, w => w.Load("BeamPreLoadModel", loadCase, (c, _) => LegacyModelImport.PreLoad(w.Beam, c, strain, Z(value), w.Units)));

        /// <summary>Beam Gradient Load.</summary>
        public static LegacyBeamDraft WithGradient(LegacyBeamDraft beam, LegacyCaseData loadCase, double axis1, double axis2, Guid identity) =>
            Change(beam, identity, w => w.Load("BeamGradientLoadModel", loadCase, (c, _) => LegacyModelImport.Gradient(w.Beam, c, Z(axis1), Z(axis2), w.Units)));

        // -------------------------------------------------------------------------------------------------------------------------------------
        // Overrides

        /// <summary>
        /// Beam Override and Bridge Beam Offsets: another property, groups, BridGHe angle or offset (null: unchanged). The beam the reader gives
        /// the changed value: the axes of the new angle and property, the offset in the new principal axes, and what depends on them re-anchored
        /// with the reader's formulas (as Build Model re-anchors a moved beam: section and offset axes, the global eccentricity of every load,
        /// the frame of the local loads, the axes of the supports); attributes, physical data, loads and bridge data kept in order. A beam with a
        /// current section state whose property changes content is refused (<see cref="NotSupportedException"/>): the stale marker of the
        /// reader holds the former signature of the state, which the draft does not carry (until R6.3, when the states take the GPC fingerprint).
        /// </summary>
        public static LegacyBeamDraft Override(LegacyBeamDraft beam, LegacyBeamProperty property, IReadOnlyList<IReadOnlyList<string>> groups, double? angleDeg, (double X, double Y)? offset,
            Guid identity) => GpcDrafts.Reidentify(Rebuild(beam, property, groups, angleDeg, offset, null, null, null), identity);

        /// <summary>
        /// R6.4 (2), extension (α): Build Model: the beam of its FEM output, with the BridGHe ID <paramref name="beamId"/> and its ends on the
        /// nodes found (<paramref name="start"/>, <paramref name="end"/>, definition units): the beam the reader gives that value, the ends and
        /// what depends on them re-anchored with the reader's formulas (<see cref="Override"/>: section and offset axes, the global eccentricity of
        /// every load, the points and lines of the loads, the axes of the supports, the fingerprint of a current section state); property,
        /// groups, angle, offset, attributes, physical data, loads and bridge data kept in order; the identity of the draft kept.
        /// </summary>
        public static LegacyBeamDraft Placed(LegacyBeamDraft beam, string beamId, Point3d start, Point3d end) =>
            Rebuild(beam, null, null, null, null, beamId ?? throw new ArgumentNullException(nameof(beamId)), start ?? throw new ArgumentNullException(nameof(start)),
                end ?? throw new ArgumentNullException(nameof(end)));

        /// <summary>
        /// R6.5 P1 lane 1a: Align Beam Axes on axis 3 against the direction: the beam with its ends swapped, the beam the reader gives the former
        /// value with <c>PointFrom</c> and <c>PointTo</c> exchanged: the ends and what depends on them re-anchored with the reader's formulas
        /// (<see cref="Override"/>), the stations of the loads and the attributes kept as the former value kept them (from the new start);
        /// property, groups, angle, offset, physical data and bridge data kept; the identity given.
        /// </summary>
        public static LegacyBeamDraft Reversed(LegacyBeamDraft beam, Guid identity)
        {
            if (beam == null) throw new ArgumentNullException(nameof(beam));
            return GpcDrafts.Reidentify(Rebuild(beam, null, null, null, null, null, beam.Beam.EndPoint, beam.Beam.StartPoint), identity);
        }

        /// <summary>The body of <see cref="Override"/> and <see cref="Placed"/>: the changed beam the reader gives (null: unchanged), not re-identified.</summary>
        private static LegacyBeamDraft Rebuild(LegacyBeamDraft beam, LegacyBeamProperty property, IReadOnlyList<IReadOnlyList<string>> groups, double? angleDeg, (double X, double Y)? offset,
            string newName, Point3d newStart, Point3d newEnd)
        {
            if (beam == null) throw new ArgumentNullException(nameof(beam));
            var c = GpcDrafts.Copy(beam);
            var old = c.Beam; var units = c.Report.Units;
            var state = c.Bridge?.SectionState;
            if (property != null && state != null && !state.PropertyFingerprint.StartsWith(LegacyModelImport.StaleSectionStatePrefix, StringComparison.Ordinal)
                && property.Data.Signature != c.Property?.Signature)
                throw new NotSupportedException("GpcBeamOverrideOfASectionState: the property of a beam with a section state changes; the stale marker needs the former signature of the state.");
            double angle = angleDeg.HasValue ? Z(angleDeg.Value) : c.AngleDeg;
            double ox = offset.HasValue ? units.ToMm(Z(offset.Value.X)) : old.Assignments.OffsetI?.X ?? 0, oy = offset.HasValue ? units.ToMm(Z(offset.Value.Y)) : old.Assignments.OffsetI?.Y ?? 0;
            var records = c.Records;
            Func<Group> firstGroup; IEnumerable<string> names;
            if (groups != null)
            {
                records.GroupList.Clear();
                var chains = groups.Select(g => g.ToArray()).ToArray();
                firstGroup = chains.Length > 0 ? () => GroupOf(chains[0]) : null; names = chains.Select(FullName);
            }
            else
            {
                var kept = old.Groups.Values.FirstOrDefault();
                firstGroup = kept == null ? null : () => kept; names = Array.Empty<string>();
            }
            var gpcProperty = property == null ? old.BeamProperty : GPC.Model.Core.ModelValues.CopyValue(property.Property);
            var formulation = property == null ? old.Assignments.Formulation : property.Data.Formulation;
            var data = property?.Data ?? c.Property;
            if (property != null) { records.PropertyCarryList.Clear(); records.PropertyCarryList.AddRange(PropertyCarries(property.Data)); }
            var (placed, principal, globalOffset) = LegacyModelImport.NewBeam(old.Guid, c.Report.Owner, old.Id - 1, newName ?? old.Name ?? "", newStart ?? old.StartPoint, newEnd ?? old.EndPoint, gpcProperty, formulation,
                data.Angle, angle, ox, oy, null, firstGroup, names, c.Report, records);
            foreach (var a in old.Attributes.Values) placed.Attributes.Add(a);
            var physical = old.Assignments.Physical;
            if (physical != null)
            {
                var section = placed.Assignments.SectionAxes;
                foreach (var s in physical.Supports) s.Axes = new CoordinateSystem(new Point3d(0, 0, 0), section.V1, section.V2);
                placed.Assignments.Physical = physical;
            }
            var start = placed.StartPoint; var end = placed.EndPoint;
            // One line per former distributed load, shared by its pieces (GpcBeamLoads.Distributed), as the reader and LegacyAssembly.PlaceBeam.
            var lines = new Dictionary<string, Line3d>(StringComparer.Ordinal);
            foreach (var a in old.Assignments.Loads)
            {
                var load = new BeamLoadAssignment
                {
                    Start = a.Start, End = a.End, Eccentricity = globalOffset, EccentricityCoordinateSystem = CoordinateSystem.Global, LengthConvention = a.LengthConvention,
                    ProjectionPlaneNormal = a.ProjectionPlaneNormal, StationDomain = a.StationDomain, OriginalAssignmentId = a.OriginalAssignmentId, SourceRecord = a.SourceRecord,
                };
                if (a.Concentrated is PointLoad p)
                {
                    double station = a.Start;
                    var point = new Point3d(start.X + (end.X - start.X) * station, start.Y + (end.Y - start.Y) * station, start.Z + (end.Z - start.Z) * station);
                    var axes = Local(p) ? GpcBeamLoads.ComponentFrame(principal) : null;
                    var f = new Vector3d(p.F1, p.F2, p.F3); var m = new Vector3d(p.M1, p.M2, p.M3);
                    load.Concentrated = axes == null ? new PointLoad(f, m, point, (LoadCaseBase)p.LoadCase) : new PointLoad(f, m, point, (LoadCaseBase)p.LoadCase, axes);
                }
                else
                {
                    if (!lines.TryGetValue(a.OriginalAssignmentId, out var line)) lines.Add(a.OriginalAssignmentId, line = new Line3d(start, end));
                    LineLoad Line(LineLoad l)
                    {
                        var axes = Local(l) ? GpcBeamLoads.ComponentFrame(principal) : null;
                        var f = new Vector3d(l.F1, l.F2, l.F3); var m = new Vector3d(l.M1, l.M2, l.M3);
                        return axes == null ? new LineLoad(f, m, line, (LoadCaseBase)l.LoadCase) : new LineLoad(f, m, line, (LoadCaseBase)l.LoadCase, axes);
                    }
                    load.StartIntensity = Line((LineLoad)a.StartIntensity); load.EndIntensity = Line((LineLoad)a.EndIntensity);
                }
                placed.Assignments.Loads.Add(load);
            }
            var w = new Work(new LegacyBeamDraft(placed, c.Bridge, angle, data, c.Report) { Records = records });
            return Finish(w);
        }

        /// <summary>A load of a draft in the beam axes (the frame <see cref="GpcBeamLoads.ComponentFrame"/>) or global (a load built without a
        /// frame takes the global coordinate system), as <c>LegacyAssembly</c> tells them.</summary>
        private static bool Local(Load l) => l.CoordinateSystem != null && l.CoordinateSystem.Name != CoordinateSystem.Global.Name;

        // -------------------------------------------------------------------------------------------------------------------------------------
        // The working draft

        /// <summary>A copy of the draft changed by <paramref name="change"/> (on the working draft), finished and re-identified.</summary>
        private static LegacyBeamDraft Change(LegacyBeamDraft beam, Guid identity, Action<Work> change)
        {
            var w = new Work(GpcDrafts.Copy(beam ?? throw new ArgumentNullException(nameof(beam))));
            change(w);
            return GpcDrafts.Reidentify(Finish(w), identity);
        }

        /// <summary>The draft of a working copy: the bridge data of its beam (in its report) and, when its section state is current, the
        /// fingerprint of the complete beam (the reader computes the state after every attribute and load of the value).</summary>
        internal static LegacyBeamDraft Finish(Work w)
        {
            var beam = w.Beam; var bridge = w.Report.Bridge;
            var state = bridge.SectionState(beam.Guid);
            if (state != null && !state.PropertyFingerprint.StartsWith(LegacyModelImport.StaleSectionStatePrefix, StringComparison.Ordinal))
                bridge.SetSectionState(beam.Guid, new GPC.Checkers.CompositeBridge.BridgeLocalSectionState(state.Behavior, state.ConcreteExcluded, state.ModularRatio, state.ClassificationCase,
                    GPC.Checkers.CompositeBridge.BridgeWorkflowDefinition.PropertyFingerprint(beam)));
            return w.Draft with { Bridge = bridge.Beam(beam.Guid) };
        }

        /// <summary>A beam draft being built: its beam, report and records (one copy, changed in place), the principal axes and the global
        /// eccentricity of its loads as the reader computes them (from the ends, the BridGHe angle and the offset of the beam).</summary>
        internal sealed class Work
        {
            internal Work(LegacyBeamDraft draft) { Draft = draft; }
            internal LegacyBeamDraft Draft { get; }
            internal BeamElement Beam => Draft.Beam;
            internal LegacyImportReport Report => Draft.Report;
            internal LegacyElementRecords Records => Draft.Records;
            internal BridgeUnits Units => Draft.Report.Units;
            internal CoordinateSystem Principal => GpcBeamAxes.Principal(Beam.StartPoint, Beam.EndPoint, Draft.AngleDeg);
            /// <summary>The global eccentricity of the loads: the offset in the principal axes (zero without one), as the reader computes it,
            /// once per beam: every load of the beam shares that instance (the graph of the reader), so a beam with loads gives theirs.</summary>
            internal Vector3d Offset
            {
                get
                {
                    var shared = Beam.Assignments.Loads.FirstOrDefault()?.Eccentricity;
                    if (shared != null) return shared;
                    var principal = Principal; double ox = Beam.Assignments.OffsetI?.X ?? 0, oy = Beam.Assignments.OffsetI?.Y ?? 0;
                    return principal.V1 * ox + principal.V2 * oy;
                }
            }

            /// <summary>A new former load of the beam in a case: the case of that name of the element (<see cref="LegacyModelImport.ElementCase"/>),
            /// its record "BeamLoad/1/&lt;k&gt;" (k: its position among the former loads), then the records of the load and of its case.</summary>
            internal void Load(string kind, LegacyCaseData loadCase, Action<LoadCaseBase, string> add)
            {
                var c = LegacyModelImport.ElementCase(Beam, Records, loadCase);
                add(c, LegacyModelImport.BeamLoadSource(Beam, Records.Loads + 1));
                Records.LoadList.Add((kind, loadCase.Name)); Records.LoadCaseList.Add(loadCase);
            }

            internal void SuperElement(string name) => new LegacyModelImport.BridgeMembers(Report.Bridge).SuperElement(name, Beam, Records);

            /// <summary>An Element ID of the beam (the reader: the first value is the label; an empty one is carried).</summary>
            internal void Label(string value) => LegacyModelImport.BeamLabel(Beam, value, Report, Records);

            /// <summary>The first bridge member of the beam (stations in the definition units; the others counted and refused by the resolution).</summary>
            internal void Member(string id, string family, int group, int segment, int span, double stationFrom, double stationTo, Vector3d sectionUp, string geometryId)
            {
                if (++Records.Members > 1) return;
                new LegacyModelImport.BridgeMembers(Report.Bridge).Member(id, family, group, segment, span, stationFrom, stationTo, () => sectionUp, () => geometryId ?? "", Beam, Units);
            }

            internal void Taper(double fromX, double fromY, double toX, double toY, string alignmentX, string alignmentY) =>
                LegacyModelImport.Taper(Beam, Records, fromX, toX, fromY, toY, () => LegacyModelImport.TaperAlignmentOf(alignmentX), () => LegacyModelImport.TaperAlignmentOf(alignmentY));
        }
    }
}
