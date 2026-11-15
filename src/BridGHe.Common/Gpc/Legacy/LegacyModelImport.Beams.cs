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
    /// <summary>R6.1 (lane B of R6.2): the reader of the former beam values (beam, attributes, loads, bridge data, section states).</summary>
    public static partial class LegacyModelImport
    {
        /// <summary>
        /// One former beam as a GPC beam (the body of the beam loop of <see cref="Import"/>, chain E (d): also the conversion of a single beam
        /// value, <see cref="BeamDraft"/>): on the given ends and property, connected by <paramref name="connect"/> right after its creation;
        /// formulation, section axes from the BridGHe angle and the principal angle of the property, offsets in the principal axes, the first
        /// group, attributes, loads in its assignments, physical data, at least 5 output stations, the local section state.
        /// </summary>
        private static BeamElement Beam(LegacyValue b, int k, Guid guid, Guid owner, Point3d start, Point3d end, BeamProperty property, BeamFormulation formulation,
            double principalAngle, Action<BeamElement> connect, Func<LegacyValue, Group> groupOf, Dictionary<string, LoadCaseBase> cases, BridgeUnits units,
            LegacyImportReport report, BridgeMembers members, Func<int, string> femPropertySignature, LegacyElementRecords records, double axesRotationDeg = 0)
        {
            var offset = b["Offset"];
            // e3: every group at list level (the former exporter wrote the first; the warning comes with the resolution, LegacyAssembly.ResolveBeam).
            var bg = b.Field("Groups").Items;
            var (beam, principal, globalOffset) = NewBeam(guid, owner, k, b.String("BeamId") ?? "", start, end, property, formulation, principalAngle, b.Double("Angle"),
                units.ToMm(offset.Double("X")), units.ToMm(offset.Double("Y")), connect, bg.Count > 0 ? () => groupOf(bg[0]) : null, bg.Select(FullName), report, records, axesRotationDeg);
            BeamData(b, beam, principal, globalOffset, cases, units, report, members, records);
            SectionState(b, k, beam, femPropertySignature, report, records);
            return beam;
        }

        /// <summary>
        /// R6.2 (lane B): the start of <see cref="Beam"/> on plain data (also the start of a beam built on GPC, <c>GpcBeamBuilders.New</c>): the
        /// beam <paramref name="k"/> (0-based) on its ends and property, connected right after its creation, its BridGHe angle in the report,
        /// formulation, section axes, the offset in the principal axes, the first group (<paramref name="firstGroup"/>, null: none) and the full
        /// names of every group in the records. Returns the beam, its principal axes and the global eccentricity of its loads (the offset).
        /// <paramref name="axesRotationDeg"/>: LIB (A02b, <see cref="AxesRotationDeg"/>): the turn of the axes of the beam, 0 but for a double angle.
        /// </summary>
        internal static (BeamElement Beam, CoordinateSystem Principal, Vector3d GlobalOffset) NewBeam(Guid guid, Guid owner, int k, string beamId, Point3d start, Point3d end,
            BeamProperty property, BeamFormulation formulation, double principalAngle, double angleDeg, double ox, double oy, Action<BeamElement> connect,
            Func<Group> firstGroup, IEnumerable<string> groupFullNames, LegacyImportReport report, LegacyElementRecords records, double axesRotationDeg = 0)
        {
            var beam = new BeamElement(guid, start, end, property, null, beamId, k + 1)
            { Source = new SourceIdentity(Program, owner.ToString("D"), EntityFamily.Beam, beamId.Length != 0 ? beamId : (k + 1).ToString()) };
            connect?.Invoke(beam);
            report.BeamAngles[k + 1] = angleDeg;
            var section = GpcBeamAxes.Section(start, end, angleDeg + axesRotationDeg, principalAngle);
            var principal = GpcBeamAxes.Principal(start, end, angleDeg + axesRotationDeg);
            beam.Assignments.Formulation = formulation;
            beam.Assignments.SectionAxes = section;
            var globalOffset = principal.V1 * ox + principal.V2 * oy;
            if (ox != 0 || oy != 0)
            {
                beam.Assignments.OffsetAxes = principal;
                beam.Assignments.OffsetI = new Vector3d(ox, oy, 0); beam.Assignments.OffsetJ = new Vector3d(ox, oy, 0);
            }
            if (firstGroup != null) beam.AddGroup(firstGroup());
            foreach (var g in groupFullNames) records.GroupList.Add(g);
            return (beam, principal, globalOffset);
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
            var property = BeamProperty(p, PropertyName(p), units, new Dictionary<string, (double, double)>(StringComparer.Ordinal), propertyReport, checkLengthUnit: false);
            records.PropertyCarryList.AddRange(propertyReport.NotExported);
            var cases = ElementCases(b, out var groupOf, records);
            var beam = Beam(b, 0, guid, guid, Point(b["PointFrom"], units), Point(b["PointTo"], units), property.Property, property.Data.Formulation, property.Data.Angle,
                null, groupOf, cases, units, report, new BridgeMembers(report.Bridge), femPropertySignature == null ? null : _ => femPropertySignature, records, AxesRotationDeg(property.Data.Native));
            return new LegacyBeamDraft(beam, report.Bridge.Beam(beam.Guid), b.Double("Angle"), property.Data, report) { Records = records };
        }

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
                    case "BeamOutputStationsModel": OutputStations(beam, a.Int("MinimumNumberOfStations")); break;
                    case "BeamReleasesAttributeModel":
                        beam.Attributes.Add(Releases(a, units)); break;
                    case "BeamSupportAttributeModel":
                        (beam.Assignments.Physical ??= new BeamPhysicalData()).Supports.Add(Support(a, beam.Assignments.SectionAxes, units)); break;
                    case "BeamTaperAttributeModel":
                        Taper(beam, records, a.Double("NFromX"), a.Double("NToX"), a.Double("NFromY"), a.Double("NToY"), () => TaperAlignmentOf(a.Enum("AlignmentX")), () => TaperAlignmentOf(a.Enum("AlignmentY")));
                        break;
                    case "BeamStringGroupAttributeModel": StringGroup(beam, a.Int("ID")); break;
                    default: throw new NotSupportedException("LegacyBeamAttributeNotMappedYet: " + a.ShortType);
                }
            }
            int loadIndex = 0;
            foreach (var l in b.Field("Loads").Items)
            {
                var loadCase = cases[l.Field("LoadCase").String("Name")]; string source = BeamLoadSource(beam, ++loadIndex);
                switch (l.ShortType)
                {
                    case "BeamDistributedLoadModel": beam.Assignments.Loads.AddRange(Distributed(l, beam, principal, offset, loadCase, units, source)); break;
                    case "BeamPointLoadModel": beam.Assignments.Loads.Add(PointLoad(l, beam, principal, offset, loadCase, units, source)); break;
                    case "BeamPreLoadModel": PreLoad(beam, loadCase, l.Enum("LoadType") == "Strain", l.Double("Value"), units); break;
                    case "BeamGradientLoadModel": Gradient(beam, loadCase, l.Double("Axis1"), l.Double("Axis2"), units); break;
                    // The slab eigenstrain of a composite beam: a checks input (the strain of the workflow shrinkage action), not an FE load.
                    case "CompositeBeamSlabStrainModel": report.Carry("Composite slab strain (checks input: the strain of the workflow shrinkage action)"); break;
                    default: throw new NotSupportedException("LegacyBeamLoadNotMappedYet: " + l.ShortType);
                }
            }

        }

        // R6.2 (lane B): every former beam attribute and load on plain data, the one rule of the reader (BeamData) and of the GPC builders of
        // the producer components (GpcBeamBuilders): same collections, same order, same values.

        /// <summary>R6.2 (lane B): an Element ID of a beam built on GPC by a generator, with the rule of <c>Label</c> (Elements.cs) on its value:
        /// an empty value is carried, the first value is the label of the beam, every value is in the records; the first value (empty: "")
        /// is the BridGHe ID Build Model gives the beam (<see cref="LegacyElementRecords.FirstLabel"/>).</summary>
        internal static void BeamLabel(BeamElement beam, string value, LegacyImportReport report, LegacyElementRecords records)
        {
            if (records.FirstLabel == null && records.LabelList.Count == 0 && records.EmptyLabels == 0) records.FirstLabel = value ?? "";
            if (string.IsNullOrEmpty(value)) { report.Carry("Empty Element ID attribute"); records.EmptyLabels++; return; }
            records.LabelList.Add(value);
            if (records.LabelList.Count == 1) beam.Attributes.Add(new ElementLabelAttribute(value));
        }

        /// <summary>The record of the k-th former load (1-based) of a beam: "BeamLoad/&lt;number&gt;/&lt;k&gt;".</summary>
        internal static string BeamLoadSource(BeamElement beam, int k) => "BeamLoad/" + beam.Id + "/" + k;

        /// <summary>Beam Output Stations: the minimum number of stations of the last record (no default here: the resolution adds it).</summary>
        internal static void OutputStations(BeamElement beam, int stations) => (beam.Assignments.Physical ??= new BeamPhysicalData()).MinimumOutputStations = stations;

        /// <summary>Beam Taper: the first taper of a beam (the others are counted and refused by the resolution); the alignments are read only
        /// for the first.</summary>
        internal static void Taper(BeamElement beam, LegacyElementRecords records, double fromX, double toX, double fromY, double toY, Func<TaperAlignment> alignmentX, Func<TaperAlignment> alignmentY)
        {
            if (++records.Tapers > 1) return;
            (beam.Assignments.Physical ??= new BeamPhysicalData()).Taper = new BeamSectionTaper
            {
                ScaleI1 = fromX, ScaleJ1 = toX, ScaleI2 = fromY, ScaleJ2 = toY,
                Alignment1 = alignmentX(), Alignment2 = alignmentY(),
            };
        }

        /// <summary>Beam String Group: the string group of its ID (the last record).</summary>
        internal static void StringGroup(BeamElement beam, int id) => (beam.Assignments.Physical ??= new BeamPhysicalData()).StringGroup = GpcIdentity.Derive(GpcIdentity.NameSpace, "string-group:" + id);

        /// <summary>Beam Pre Load: an initial axial force (force units) or strain in a case; two in one case are refused by the resolution
        /// (<see cref="LegacyAssembly.ResolveBeam"/>), with the number of the beam.</summary>
        internal static void PreLoad(BeamElement beam, LoadCaseBase loadCase, bool strain, double value, BridgeUnits units) =>
            (beam.Assignments.Physical ??= new BeamPhysicalData()).InitialStates.Add(new BeamInitialState
            { Case = loadCase, Kind = strain ? BeamInitialStateKind.AxialStrain : BeamInitialStateKind.AxialForce, Value = strain ? value : units.ToN(value) });

        /// <summary>Beam Gradient Load: the temperature gradients along the principal axes 1 and 2 in a case.</summary>
        internal static void Gradient(BeamElement beam, LoadCaseBase loadCase, double axis1, double axis2, BridgeUnits units) =>
            (beam.Assignments.Physical ??= new BeamPhysicalData()).TemperatureGradients.Add(new BeamTemperatureGradient
            { Case = loadCase, Axis1 = units.ToCelsiusPerMm(axis1), Axis2 = units.ToCelsiusPerMm(axis2) });

        private static IEnumerable<GPC.Model.Loads.Assignments.BeamLoadAssignment> Distributed(LegacyValue l, BeamElement beam, CoordinateSystem principal, Vector3d offset,
            LoadCaseBase loadCase, BridgeUnits units, string source)
        {
            var schema = (BridgeLoadSchema)Enum.Parse(typeof(BridgeLoadSchema), l.Enum("LoadSchema"));
            var cs = l["CoordinateSystem"];
            return Distributed(schema, l.Enum("LoadType") == "Moment", l.Enum("LoadDirection"), l.Double("PA"), l.Double("PB"), l.Double("P1"), l.Double("P2"), l.Double("A"), l.Double("B"),
                cs.IsNull ? (Func<bool>)null : () => IsGlobal(cs), beam, principal, offset, loadCase, units, source);
        }

        /// <summary>
        /// Beam Distributed Load / Moment on plain data: the schema, a moment or a force, the former direction name (local X, Y, Z: the principal
        /// axes 1, 2, 3; global X, Y, Z, their projected forms and gravity), the intensities and the positions; <paramref name="globalSystem"/>
        /// null: no coordinate system (local), otherwise whether the coordinate system of the load is the global one (any other is refused, as
        /// the former exporter ignored it).
        /// </summary>
        internal static IEnumerable<GPC.Model.Loads.Assignments.BeamLoadAssignment> Distributed(BridgeLoadSchema schema, bool moment, string direction, double pa, double pb, double p1, double p2,
            double a, double b, Func<bool> globalSystem, BeamElement beam, CoordinateSystem principal, Vector3d offset, LoadCaseBase loadCase, BridgeUnits units, string source)
        {
            double scale = moment ? units.ToNmmPerMm(1) : units.ToNPerMm(1);
            if (globalSystem == null)
            {
                // Principal (local) axes: X, Y, Z are the beam principal axes 1, 2, 3.
                var axis = direction == "X" ? Vector3d.XAxis : direction == "Y" ? Vector3d.YAxis : direction == "Z" ? Vector3d.ZAxis : throw new NotSupportedException("LegacyLocalLoadDirection: " + direction);
                return GpcBeamLoads.Distributed(loadCase, beam.StartPoint, beam.EndPoint, schema, pa, pb, p1, p2, a, b, axis, moment, principal, null, offset, scale, source);
            }
            // The former exporter wrote every global distributed action as a global force (also LoadType = Moment).
            if (moment) throw new NotSupportedException("LegacyGlobalDistributedMomentWasExportedAsForce");
            if (!globalSystem()) throw new NotSupportedException("LegacyNonGlobalLoadCoordinateSystemWasIgnored");
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
            return PointLoad(l.Enum("LoadType") == "Moment", l.Double("A"), Vector(l["Force"]), beam, principal, offset, loadCase, units, source);
        }

        /// <summary>Beam Point Load / Moment on plain data: a force or a moment at the normalised station <paramref name="a"/>, in the principal
        /// axes whatever coordinate system the component was given (the former archive did not store it).</summary>
        internal static GPC.Model.Loads.Assignments.BeamLoadAssignment PointLoad(bool moment, double a, Vector3d value, BeamElement beam, CoordinateSystem principal, Vector3d offset,
            LoadCaseBase loadCase, BridgeUnits units, string source) =>
            GpcBeamLoads.Concentrated(loadCase, beam.StartPoint, beam.EndPoint, a, value, moment, principal, offset, moment ? units.Force * units.Length : units.Force, source);

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

        /// <summary>
        /// C1 (L12, user decision of 5/11): the bridge identities of the former beams as bridge data of the beam (by GUID, <see cref="LegacyImportReport.Bridge"/>),
        /// never in the generic model:
        /// - bridge member metadata (BridgeMemberAttributeModel: the physical member of the FE part, its stations in mm, section up and geometry
        ///   id) → <c>SetMember</c>; one member per beam (<c>LegacyDuplicateBridgeMemberAttribute</c>); the parts of a member are its beams in
        ///   numbering order (<c>BridgeModel.PhysicalMember</c>);
        /// - super elements (SuperElementAttributeModel: the beams checked together by Super Element Check) → <c>SetSuperElement</c>; two
        ///   attributes with the same name are one super element, two names are an error (<c>LegacyBeamInTwoSuperElements</c>, lead decision of 5/11).
        /// </summary>
        internal sealed class BridgeMembers
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
                var up = m.Field("SectionUp");
                Member(m.String("PhysicalId"), m.Enum("Family"), m.Int("Group"), m.Int("Segment"), m.Int("Span"), m.Double("StationFrom"), m.Double("StationTo"),
                    () => new Vector3d(up.Double("X"), up.Double("Y"), up.Double("Z")), () => m.String("GeometryId") ?? "", beam, units);
            }
            /// <summary>R6.2 (lane B): the first bridge member of a beam on plain data (the former family name, stations in the definition units).</summary>
            internal void Member(string id, string familyName, int group, int segment, int span, double stationFrom, double stationTo, Func<Vector3d> sectionUp,
                Func<string> geometryId, BeamElement beam, BridgeUnits units)
            {
                if (!Enum.TryParse<GPC.Checkers.CompositeBridge.Layout.BridgeMemberFamily>(familyName, false, out var family)) throw new ArgumentException("LegacyBridgeMemberFamily: " + familyName);
                var metadata = new GPC.Checkers.CompositeBridge.Layout.BridgeMemberMetadata(id, family, group, segment, span,
                    units.ToMm(stationFrom), units.ToMm(stationTo), sectionUp(), geometryId());
                if (data.Member(beam.Guid) != null) throw new ArgumentException("LegacyDuplicateBridgeMemberAttribute: beam " + beam.Name);
                data.SetMember(beam.Guid, metadata);
            }
        }

        private static GPC.Model.Attributes.BeamReleasesAttribute Releases(LegacyValue a, BridgeUnits units)
        {
            bool[] Flags(string name) => new[] { a.Bool(name + 0), a.Bool(name + 1), a.Bool(name + 2) };
            return Releases(Flags("TransNode1"), Vector(a["TransN1Partial"]), Flags("RotNode1"), Vector(a["RotN1Partial"]),
                Flags("TransNode2"), Vector(a["TransN2Partial"]), Flags("RotNode2"), Vector(a["RotN2Partial"]), units);
        }

        /// <summary>Beam Releases on plain data: per end and kind (translations along 1, 2, 3, rotations about 1, 2, 3) a released DOF with a zero
        /// partial value is released, with a value it is a partial (absolute) stiffness; the others stay continuous.</summary>
        internal static GPC.Model.Attributes.BeamReleasesAttribute Releases(bool[] transI, Vector3d transIPartial, bool[] rotI, Vector3d rotIPartial,
            bool[] transJ, Vector3d transJPartial, bool[] rotJ, Vector3d rotJPartial, BridgeUnits units)
        {
            var release = new GPC.Model.Attributes.BeamReleasesAttribute { SourceRecord = "BeamReleasesAttributeModel" };
            void End(GPC.Model.Attributes.BeamDofConnection[] target, bool[] trans, bool[] rot, Vector3d tp, Vector3d rp)
            {
                var t = new[] { tp.X, tp.Y, tp.Z }; var r = new[] { rp.X, rp.Y, rp.Z };
                for (int k = 0; k < 3; k++)
                {
                    if (trans[k]) target[k] = t[k] == 0 ? new GPC.Model.Attributes.BeamDofConnection(GPC.Model.Attributes.BeamConnectionKind.Released)
                        : new GPC.Model.Attributes.BeamDofConnection(GPC.Model.Attributes.BeamConnectionKind.AbsoluteStiffness, t[k] * units.Force / units.Length);
                    if (rot[k]) target[3 + k] = r[k] == 0 ? new GPC.Model.Attributes.BeamDofConnection(GPC.Model.Attributes.BeamConnectionKind.Released)
                        : new GPC.Model.Attributes.BeamDofConnection(GPC.Model.Attributes.BeamConnectionKind.AbsoluteStiffness, r[k] * units.Force * units.Length);
                }
            }
            End(release.I, transI, rotI, transIPartial, rotIPartial);
            End(release.J, transJ, rotJ, transJPartial, rotJPartial);
            return release;
        }

        /// <summary>Former BeamSupportExport: simple springs along +-1/+-2 of the beam axes; tension-only flips the direction.</summary>
        private static BeamDistributedSupport Support(LegacyValue a, CoordinateSystem axes, BridgeUnits units)
        {
            if (a.Enum("SpringType") != "Simple" || a.Enum("SpringTensionOrientation") != "BeamLocalAxis")
                throw new NotSupportedException("Straus beam supports require simple springs along principal local axes 1 or 2.");
            return Support(a.Double("Stiffness"), a.Double("Gap"), () => a.Enum("LocalAxisDirection"), () => a.Enum("SimpleSpringTypes"), axes, units);
        }

        /// <summary>Beam Support on plain data (a simple spring along a principal local axis): stiffness [F/L²], gap [L], the former direction
        /// code (negative1Axis, positive1Axis, negative2Axis, positive2Axis) and spring type (TensionCompression, CompressionOnly, TensionOnly),
        /// the section axes of the beam; the checks and messages of the former exporter, in its order.</summary>
        internal static BeamDistributedSupport Support(double stiffness, double gap, Func<string> localAxisDirection, Func<string> springType, CoordinateSystem axes, BridgeUnits units)
        {
            if (!(stiffness > 0) || double.IsInfinity(stiffness) || double.IsNaN(gap) || double.IsInfinity(gap)) throw new ArgumentException("Beam support requires finite positive stiffness [F/L2] and finite gap [L].");
            Vector3d direction;
            switch (localAxisDirection())
            {
                case "negative1Axis": direction = new Vector3d(-1, 0, 0); break;
                case "positive1Axis": direction = new Vector3d(1, 0, 0); break;
                case "negative2Axis": direction = new Vector3d(0, -1, 0); break;
                case "positive2Axis": direction = new Vector3d(0, 1, 0); break;
                default: throw new NotSupportedException("Straus beam supports accept only +-1 and +-2, not longitudinal axis 3.");
            }
            DistributedSpringBehavior behavior;
            switch (springType())
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

        internal static TaperAlignment TaperAlignmentOf(string code)
        {
            switch (code)
            {
                case "Bottom": return TaperAlignment.NegativeSide;
                case "Center": return TaperAlignment.Symmetric;
                case "Top": return TaperAlignment.PositiveSide;
                default: throw new NotSupportedException("LegacyTaperAlignment: " + code);
            }
        }
    }
}
