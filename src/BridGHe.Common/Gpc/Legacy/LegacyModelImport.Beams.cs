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
    }
}
