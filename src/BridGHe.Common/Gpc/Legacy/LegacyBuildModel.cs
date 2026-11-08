using System;
using System.Collections.Generic;
using System.Linq;
using GPC.Model.Attributes;
using GPC.Model.Core;
using GPC.Model.Elements;
using GPC.Model.LoadCases;
using GPC.Model.Loads;
using GPC.Model.Loads.Assignments;
using GPC.Model.Restraints;
using GPC.Model.Structure.Assignments;

namespace BridGHe.Common.Gpc.Legacy
{
    /// <summary>
    /// Chain E (e3, P2K): Build Model on the element drafts of the GH types, the steps of the former <c>ModelHelper.BuildModel</c> before the
    /// legacy reader. Here the merge by GH GUID (coordinator decision 2 of 5/11: the 0.3.0 rule exactly, at list level, in the order of
    /// <c>ElementModel.Merge</c>, before the per-case sums of the resolution): a datum whose GH GUID came earlier is merged into a copy of the
    /// first, which keeps its place. <c>ElementModel.Merge</c> appends the groups the first lacks (by full name), then every load and every
    /// attribute of the other that <c>Contains</c> does not find in the list (the results are ignored). <c>Contains</c> uses the equality of
    /// the former values, so here:
    /// - attributes and records compare by content (Element ID, super element, coordinate system, restraint, stiffness, rigid connection,
    ///   releases, support, taper, string group, output stations, load patch, plate support: the former types compare their data); a bridge
    ///   member or a section state has no equality of its own and is always appended (two members: the former error; two states: the warning);
    /// - a load compares its data and its case, and the former case compares its list of stages by reference: two GH data never share a
    ///   case instance (every component copies the value), so a load of the other datum is equal to one of the first only in a case without a
    ///   list of stages on both sides (<see cref="LegacyCaseData.HasStageList"/>: the copy of the default "Dead" the Load Case component gives,
    ///   <see cref="StageLessCase"/>), with equal definitions; within the other datum the loads of one case share it, so a load
    ///   equal to one appended before from the same datum is not appended again.
    /// Examples (L12 scope, scan of 5/11, P2K): no merge by GH GUID in any Build Model run. Never changes its inputs.
    /// </summary>
    public static class LegacyBuildModel
    {
        /// <summary>The case of the former data whose stage list is null (the copy of <c>LoadCaseModel.Dead</c> the Load Case component
        /// gives for that name): in practice the only case whose loads two GH data can share in the former equality (the rule reads
        /// <see cref="LegacyCaseData.HasStageList"/>).</summary>
        public const string StageLessCase = "Dead";

        private static string Fp(params object[] values) => ModelValues.Fingerprint(values);
        private static string CaseName(ILoadCase c) => ((LoadCaseBase)c).Name;

        /// <summary>
        /// Whether a former load of the other datum (its kind, data and case) is equal to one already in the list for <c>Contains</c>: same kind
        /// and data, and an equal case: the same instance (a load appended before from the same datum, whose loads of one case share it) or
        /// two equal cases without a stage list (the former case compares its list by reference: null only for the copy of the default "Dead").
        /// </summary>
        private static bool Contains(IEnumerable<(string Kind, string Key, LegacyCaseData Case, bool FromOther)> existing, string kind, string key, LegacyCaseData c) =>
            existing.Any(e => e.Kind == kind && e.Key == key && e.Case.Name == c.Name && (e.FromOther || !e.Case.HasStageList && !c.HasStageList && e.Case.Same(c)));

        /// <summary>The report of a merged draft: that of the first, with what the appended records carry (the former reader carries it once
        /// per record of the merged list: an Element ID without a value, a slab strain, a plate support with its former face).</summary>
        private static LegacyImportReport MergedReport(LegacyImportReport first) => first.CopyFor(first.RegisteredCases);

        /// <summary>The Element IDs without a value: <c>Contains</c> finds the first one appended, so the merged list has one at most more.</summary>
        private static void EmptyLabels(LegacyElementRecords records, LegacyElementRecords other, LegacyImportReport report)
        {
            if (other.EmptyLabels == 0 || records.EmptyLabels != 0) return;
            records.EmptyLabels = 1; report.Carry("Empty Element ID attribute");
        }

        private static string ForceKey(PointLoad l) => Fp(l.Name, l.F1, l.F2, l.F3, l.M1, l.M2, l.M3);
        private static string TemperatureKey(NodalTemperatureAssignment t) => Fp((int)t.Kind, t.Celsius);
        private static string RestraintKey(RestrainAssignment r) => Fp(r.SourceRecord, r.Restrain?.CoordinateSystem, r.Restrain?.Restrains);

        /// <summary>The former loads of a node draft in order: (kind, data, case) with the record of each.</summary>
        private static List<(string Kind, string Key, LegacyCaseData Case, object Record)> NodeLoads(LegacyNodeDraft draft)
        {
            var forces = new Queue<PointLoad>(draft.Node.Loads.Values.OfType<PointLoad>());
            var temperatures = new Queue<NodalTemperatureAssignment>(draft.Node.Assignments.Physical?.Temperatures ?? new List<NodalTemperatureAssignment>());
            var result = new List<(string, string, LegacyCaseData, object)>();
            for (int k = 0; k < draft.Records.Loads; k++)
            {
                string kind = draft.Records.LoadRecords[k].Kind;
                if (kind == LegacyModelImport.ForceRecord || kind == LegacyModelImport.MomentRecord) { var l = forces.Dequeue(); result.Add((kind, ForceKey(l), draft.Records.LoadCases[k], l)); }
                else if (kind == "NodeTemperatureLoadModel") { var t = temperatures.Dequeue(); result.Add((kind, TemperatureKey(t), draft.Records.LoadCases[k], t)); }
                else throw new ArgumentException("LegacyNodeLoadRecordUnknown: " + kind);
            }
            return result;
        }

        /// <summary>The merge of two node drafts with the same GH GUID (<paramref name="other"/> into a copy of <paramref name="first"/>).</summary>
        public static LegacyNodeDraft Merge(LegacyNodeDraft first, LegacyNodeDraft other) => MergeNode(first, other, false);

        /// <summary>
        /// The merge of two node drafts at the same position (the node merge by tolerance of <c>ModelHelper.BuildModel</c>: <paramref name="other"/>
        /// into a copy of <paramref name="kept"/>): groups absent by full name, every load (the former check was by reference, never true between
        /// two copies), attributes absent by content and no second Element ID on a node that has one (the first Element ID of the other only).
        /// </summary>
        public static LegacyNodeDraft MergeCoincident(LegacyNodeDraft kept, LegacyNodeDraft other) => MergeNode(kept, other, true);

        private static LegacyNodeDraft MergeNode(LegacyNodeDraft first, LegacyNodeDraft other, bool coincident)
        {
            var node = ModelValues.CopyValue(first.Node); var records = first.Records.Copy(); var source = other.Node; var report = MergedReport(first.Report);
            bool hadLabel = records.FirstLabel != null || node.Attributes.Values.OfType<ElementLabelAttribute>().Any() || records.EmptyLabels != 0;
            foreach (var g in source.Groups.Values) if (!node.Groups.ContainsKey(g.Name)) node.AddGroup(g);
            // Loads, in the order of the former loads of the other datum.
            var existing = NodeLoads(new LegacyNodeDraft(node, report) { Records = records }).Select(l => (l.Kind, l.Key, l.Case, false)).ToList();
            var others = NodeLoads(other);
            for (int index = 0; index < others.Count; index++)
            {
                var (kind, key, c, record) = others[index];
                if (!coincident && Contains(existing, kind, key, c)) continue;
                if (record is PointLoad l)
                    node.Loads.Add(new PointLoad(new GPC.Geometry.Vector3d(l.F1, l.F2, l.F3), new GPC.Geometry.Vector3d(l.M1, l.M2, l.M3), node.Position, (LoadCaseBase)l.LoadCase) { Name = l.Name });
                else
                {
                    var t = (NodalTemperatureAssignment)record;
                    (node.Assignments.Physical ??= new NodePhysicalData()).Temperatures.Add(new NodalTemperatureAssignment { Case = t.Case, Kind = t.Kind, Celsius = t.Celsius });
                }
                existing.Add((kind, key, c, true));
                records.LoadList.Add(other.Records.LoadRecords[index]); records.LoadCaseList.Add(c);
            }
            // Attributes: restraint records by content, then the Element IDs and the rigid connections in the order of the former reader
            // (own attributes, then the rigid connections), as new attributes numbered in that order.
            foreach (var r in source.Assignments.Restrains)
                if (!node.Assignments.Restrains.Any(x => RestraintKey(x) == RestraintKey(r)))
                    node.Assignments.Restrains.Add(new RestrainAssignment { Restrain = new NodeRestrain(node, r.Restrain.CoordinateSystem, r.Restrain.Restrains.ToList()), SourceRecord = r.SourceRecord });
            var labels = node.Attributes.Values.OfType<ElementLabelAttribute>().Select(a => a.Label).ToList();
            var targets = node.Attributes.Values.OfType<RigidConnectionAttribute>().Select(a => a.TargetMm).ToList();
            if (!coincident)
            {
                EmptyLabels(records, other.Records, report);
                foreach (var a in source.Attributes.Values.OfType<ElementLabelAttribute>()) if (!labels.Contains(a.Label)) labels.Add(a.Label);
            }
            else if (!hadLabel && other.Records.FirstLabel != null)
            {
                // The former check: no Element ID on a node that has one, so only the first Element ID of the other comes.
                if (other.Records.FirstLabel.Length == 0) { records.EmptyLabels = 1; report.Carry("Empty Element ID attribute"); }
                else labels.Add(other.Records.FirstLabel);
            }
            if (records.FirstLabel == null) records.FirstLabel = coincident ? (hadLabel ? null : other.Records.FirstLabel) : other.Records.FirstLabel;
            foreach (var a in source.Attributes.Values.OfType<RigidConnectionAttribute>()) if (!targets.Any(t => t.X == a.TargetMm.X && t.Y == a.TargetMm.Y && t.Z == a.TargetMm.Z)) targets.Add(a.TargetMm);
            if (node.Attributes.Values.Any(a => !(a is ElementLabelAttribute) && !(a is RigidConnectionAttribute))) throw new ArgumentException("LegacyNodeAttributeRecordUnknown");
            node.Attributes.Clear();
            foreach (var l in labels) node.Attributes.Add(new ElementLabelAttribute(l));
            foreach (var t in targets) node.Attributes.Add(new RigidConnectionAttribute(t));
            return new LegacyNodeDraft(node, report) { Records = records };
        }


        /// <summary>A beam load of a draft without its source record (which names the beam and the former load).</summary>
        private static string AssignmentKey(BeamLoadAssignment l) => Fp(l.Start, l.End, l.Concentrated, l.StartIntensity, l.EndIntensity, l.Eccentricity, l.EccentricityCoordinateSystem,
            (int)l.LengthConvention, l.ProjectionPlaneNormal, l.StationDomain);

        /// <summary>The source record of the former load <paramref name="k"/> (1-based) of beam <paramref name="number"/>.</summary>
        internal static string BeamLoadRecord(int number, int k) => "BeamLoad/" + number + "/" + k;

        /// <summary>The merge of two beam drafts with the same GH GUID (<paramref name="other"/> into a copy of <paramref name="first"/>).</summary>
        public static LegacyBeamDraft Merge(LegacyBeamDraft first, LegacyBeamDraft other)
        {
            var beam = ModelValues.CopyValue(first.Beam); var records = first.Records.Copy(); var source = other.Beam; var report = MergedReport(first.Report);
            EmptyLabels(records, other.Records, report);
            var bridge = first.Bridge;
            // Groups: the absent ones by full name; the beam keeps the first group of the merged list.
            foreach (var g in other.Records.Groups) if (!records.GroupList.Contains(g, StringComparer.Ordinal)) records.GroupList.Add(g);
            if (beam.Groups.Count == 0 && source.Groups.Count != 0) beam.AddGroup(source.Groups.Values.First());
            // Loads, in the order of the former loads of the other datum.
            var physical = beam.Assignments.Physical; var otherPhysical = source.Assignments.Physical;
            var existing = new List<((string Kind, string Key), bool FromOther)>();
            string LoadKey(BeamElement b, BeamPhysicalData p, (string Kind, string Case) load, int k, ref int prestrain, ref int gradient)
            {
                switch (load.Kind)
                {
                    case "BeamDistributedLoadModel": case "BeamPointLoadModel":
                        string record = BeamLoadRecord(b.Id, k);
                        return string.Join("|", b.Assignments.Loads.Where(a => a.OriginalAssignmentId == record).Select(AssignmentKey));
                    case "BeamPreLoadModel": { var s = p.InitialStates[prestrain++]; return Fp((int)s.Kind, s.Value); }
                    case "BeamGradientLoadModel": { var g = p.TemperatureGradients[gradient++]; return Fp(g.Axis1, g.Axis2); }
                    case "CompositeBeamSlabStrainModel": return "slab strain";
                    default: throw new ArgumentException("LegacyBeamLoadRecordUnknown: " + load.Kind);
                }
            }
            {
                int prestrain = 0, gradient = 0;
                for (int k = 0; k < records.Loads; k++) existing.Add(((records.LoadRecords[k].Kind, LoadKey(beam, physical, records.LoadRecords[k], k + 1, ref prestrain, ref gradient)), false));
            }
            {
                int prestrain = 0, gradient = 0;
                for (int k = 0; k < other.Records.Loads; k++)
                {
                    var load = other.Records.LoadRecords[k]; int prestrainAt = prestrain, gradientAt = gradient;
                    string key = LoadKey(source, otherPhysical, load, k + 1, ref prestrain, ref gradient);
                    var c = other.Records.LoadCases[k];
                    var known = existing.Select((e, i) => (e, i)).Any(x => x.e.Item1.Kind == load.Kind && x.e.Item1.Key == key && records.LoadRecords[x.i].Case == load.Case
                        && (x.e.FromOther || !records.LoadCases[x.i].HasStageList && !c.HasStageList && records.LoadCases[x.i].Same(c)));
                    if (known) continue;
                    records.LoadList.Add(load); records.LoadCaseList.Add(other.Records.LoadCases[k]); existing.Add(((load.Kind, key), true));
                    if (load.Kind == "CompositeBeamSlabStrainModel") report.Carry("Composite slab strain (checks input: the strain of the workflow shrinkage action)");
                    int position = records.Loads; string from = BeamLoadRecord(source.Id, k + 1), to = BeamLoadRecord(beam.Id, position);
                    switch (load.Kind)
                    {
                        case "BeamDistributedLoadModel": case "BeamPointLoadModel":
                            foreach (var a in source.Assignments.Loads.Where(a => a.OriginalAssignmentId == from))
                            {
                                var copy = ModelValues.CopyValue(a);
                                copy.OriginalAssignmentId = to; copy.SourceRecord = to + a.SourceRecord.Substring(from.Length);
                                beam.Assignments.Loads.Add(copy);
                            }
                            break;
                        case "BeamPreLoadModel": { var s = otherPhysical.InitialStates[prestrainAt]; (physical ??= beam.Assignments.Physical ??= new BeamPhysicalData()).InitialStates.Add(new BeamInitialState { Case = s.Case, Kind = s.Kind, Value = s.Value }); break; }
                        case "BeamGradientLoadModel": { var g = otherPhysical.TemperatureGradients[gradientAt]; (physical ??= beam.Assignments.Physical ??= new BeamPhysicalData()).TemperatureGradients.Add(new BeamTemperatureGradient { Case = g.Case, Axis1 = g.Axis1, Axis2 = g.Axis2 }); break; }
                    }
                }
            }
            // Attributes, in the order of the other datum: Element IDs (the beam has the first as its label), releases by content.
            static string ReleasesKey(BeamReleasesAttribute r) => Fp(r.I, r.J, r.CoordinateSystem, r.Phase, r.SourceRecord);
            int label = 0;
            foreach (var a in source.Attributes.Values)
            {
                if (a is ElementLabelAttribute)
                {
                    // The Element IDs of the other datum, in order (its first is this attribute, the others are records only).
                    for (; label < other.Records.Labels.Count; label++)
                    {
                        string value = other.Records.Labels[label];
                        if (records.LabelList.Contains(value, StringComparer.Ordinal)) continue;
                        records.LabelList.Add(value);
                        if (records.LabelList.Count == 1) beam.Attributes.Add(new ElementLabelAttribute(value));
                    }
                }
                else if (a is BeamReleasesAttribute r)
                {
                    if (beam.Attributes.Values.OfType<BeamReleasesAttribute>().Any(x => ReleasesKey(x) == ReleasesKey(r))) continue;
                    var copy = new BeamReleasesAttribute { CoordinateSystem = r.CoordinateSystem, Phase = r.Phase, SourceRecord = r.SourceRecord };
                    for (int k = 0; k < r.I.Length; k++) copy.I[k] = r.I[k];
                    for (int k = 0; k < r.J.Length; k++) copy.J[k] = r.J[k];
                    beam.Attributes.Add(copy);
                }
                else throw new ArgumentException("LegacyBeamAttributeRecordUnknown: " + a.GetType().Name);
            }
            // Physical records: supports by content, output stations, taper and string group (the last different one wins, as the former
            // reader on the merged list), bridge data (super element names by content, members and states always appended).
            if (otherPhysical != null)
            {
                var p = physical ??= beam.Assignments.Physical ??= new BeamPhysicalData();
                foreach (var s in otherPhysical.Supports) if (!p.Supports.Any(x => Fp(x) == Fp(s))) p.Supports.Add(ModelValues.CopyValue(s));
                if (otherPhysical.MinimumOutputStations.HasValue && otherPhysical.MinimumOutputStations != p.MinimumOutputStations) p.MinimumOutputStations = otherPhysical.MinimumOutputStations;
                if (otherPhysical.StringGroup.HasValue && otherPhysical.StringGroup != p.StringGroup) p.StringGroup = otherPhysical.StringGroup;
                if (otherPhysical.Taper != null)
                {
                    if (p.Taper == null) { p.Taper = ModelValues.CopyValue(otherPhysical.Taper); records.Tapers = other.Records.Tapers; }
                    else records.Tapers += Fp(p.Taper) == Fp(otherPhysical.Taper) ? other.Records.Tapers - 1 : other.Records.Tapers;
                }
            }
            var o = other.Bridge;
            foreach (var name in other.Records.SuperElements) if (!records.SuperElementList.Contains(name, StringComparer.Ordinal)) records.SuperElementList.Add(name);
            records.Members += other.Records.Members; records.SectionStates += other.Records.SectionStates;
            if (o != null)
            {
                var member = bridge?.Member ?? o.Member; var superElement = bridge?.SuperElement ?? o.SuperElement;
                var state = bridge?.SectionState ?? o.SectionState; var composite = bridge?.CompositeSection ?? o.CompositeSection;
                bridge = new GPC.Checkers.CompositeBridge.BridgeBeamData(beam.Guid, member, superElement, state, composite);
            }
            return new LegacyBeamDraft(beam, bridge, first.AngleDeg, first.Property, report) { Records = records };
        }

        /// <summary>The merge of two plate drafts with the same GH GUID (<paramref name="other"/> into a copy of <paramref name="first"/>; the
        /// corners are those of the first).</summary>
        public static LegacyPlateDraft Merge(LegacyPlateDraft first, LegacyPlateDraft other)
        {
            var plate = ModelValues.CopyValue(first.Plate); var records = first.Records.Copy(); var source = other.Plate; var report = MergedReport(first.Report);
            EmptyLabels(records, other.Records, report);
            foreach (var g in other.Records.Groups) if (!records.GroupList.Contains(g, StringComparer.Ordinal)) records.GroupList.Add(g);
            if (plate.Groups.Count == 0 && source.Groups.Count != 0) plate.AddGroup(source.Groups.Values.First());
            var physical = plate.Assignments.Physical; var otherPhysical = source.Assignments.Physical;
            static string PressureKey(ShellFacePressure l) => Fp((int)l.Face, l.NormalPressure, l.GlobalPressure, (int)l.Projection);
            // The former loads of a plate draft in order: (kind, data, case) with the record of each (the mass load has no equality of its own).
            static List<(string Kind, string Key, LegacyCaseData Case, object Record)> Loads(AreaElement p, LegacyElementRecords r)
            {
                var pressures = new Queue<ShellFacePressure>(p.Loads.Values.OfType<ShellFacePressure>());
                var stresses = new Queue<ShellInitialStress>(p.Assignments.Physical?.InitialStresses ?? new List<ShellInitialStress>());
                var masses = new Queue<ShellNonStructuralMass>(p.Assignments.Physical?.NonStructuralMasses ?? new List<ShellNonStructuralMass>());
                var result = new List<(string, string, LegacyCaseData, object)>();
                for (int k = 0; k < r.Loads; k++)
                {
                    string kind = r.LoadRecords[k].Kind;
                    if (kind == "PlatePressureModel") { var l = pressures.Dequeue(); result.Add((kind, PressureKey(l), r.LoadCases[k], l)); }
                    else if (kind == "PlatePreStressModel") { var s = stresses.Dequeue(); result.Add((kind, Fp(s.Components), r.LoadCases[k], s)); }
                    else if (kind == "PlateNSMassModel") { var m = masses.Dequeue(); result.Add((kind, "mass " + k + " " + Guid.NewGuid(), r.LoadCases[k], m)); }
                    else throw new ArgumentException("LegacyPlateLoadRecordUnknown: " + kind);
                }
                return result;
            }
            var existing = Loads(plate, records).Select(l => (l.Kind, l.Key, l.Case, false)).ToList();
            var others = Loads(source, other.Records);
            for (int index = 0; index < others.Count; index++)
            {
                var (kind, key, c, record) = others[index];
                if (Contains(existing, kind, key, c)) continue;
                switch (record)
                {
                    case ShellFacePressure l:
                        plate.Loads.Add(l.NormalPressure.HasValue ? ShellFacePressure.Normal((LoadCaseBase)l.LoadCase, l.Face, l.NormalPressure.Value)
                            : ShellFacePressure.Global((LoadCaseBase)l.LoadCase, l.Face, l.GlobalPressure, l.Projection)); break;
                    case ShellInitialStress s:
                        (physical ??= plate.Assignments.Physical ??= new ShellPhysicalData()).InitialStresses.Add(new ShellInitialStress { Case = s.Case, Components = s.Components }); break;
                    case ShellNonStructuralMass m:
                        (physical ??= plate.Assignments.Physical ??= new ShellPhysicalData()).NonStructuralMasses.Add(new ShellNonStructuralMass { Case = m.Case, MassPerArea = m.MassPerArea, DynamicFactor = m.DynamicFactor, GlobalOffset = m.GlobalOffset }); break;
                }
                existing.Add((kind, key, c, true));
                records.LoadList.Add(other.Records.LoadRecords[index]); records.LoadCaseList.Add(c);
            }
            int label = 0;
            foreach (var a in source.Attributes.Values)
            {
                if (!(a is ElementLabelAttribute)) throw new ArgumentException("LegacyPlateAttributeRecordUnknown: " + a.GetType().Name);
                for (; label < other.Records.Labels.Count; label++)
                {
                    string value = other.Records.Labels[label];
                    if (records.LabelList.Contains(value, StringComparer.Ordinal)) continue;
                    records.LabelList.Add(value);
                    if (records.LabelList.Count == 1) plate.Attributes.Add(new ElementLabelAttribute(value));
                }
            }
            if (otherPhysical != null)
            {
                var p = physical ??= plate.Assignments.Physical ??= new ShellPhysicalData();
                // A support compares with its former face (in the carried text: the GPC support is always on the -Z face).
                for (int k = 0; k < otherPhysical.Supports.Count; k++)
                {
                    var s = otherPhysical.Supports[k]; string face = other.Records.SupportCarries[k];
                    if (Enumerable.Range(0, p.Supports.Count).Any(i => Fp(p.Supports[i]) == Fp(s) && records.SupportCarries[i] == face)) continue;
                    p.Supports.Add(ModelValues.CopyValue(s)); records.SupportCarryList.Add(face); report.Carry(face);
                }
                if (otherPhysical.LoadPatch != null && (p.LoadPatch == null || Fp(p.LoadPatch) != Fp(otherPhysical.LoadPatch))) p.LoadPatch = ModelValues.CopyValue(otherPhysical.LoadPatch);
            }
            return new LegacyPlateDraft(plate, first.Corners, report) { Records = records, AngleDeg = first.AngleDeg };
        }

        /// <summary>Chain E (e3, P2K): what Build Model on GPC did, for the runners (merges by GH GUID per family node/beam/plate, of them the
        /// same draft merged with itself; node merges by position; beam ends and plate corners moved onto a node or with a new node; zero-length
        /// beams removed; ends and corners moved again by the node lookup of the reader; beams and plates whose final ends or corners differ
        /// from those of their draft: re-anchored).</summary>
        public sealed class BuildCounts
        {
            public int[] Merges { get; } = new int[3];
            public int[] SelfMerges { get; } = new int[3];
            public int PositionMerges { get; internal set; }
            public int EndsMoved { get; internal set; }
            public int EndNodes { get; internal set; }
            public int ZeroLength { get; internal set; }
            public int CornersMoved { get; internal set; }
            public int CornerNodes { get; internal set; }
            public int ReaderMoved { get; internal set; }
            public int Reanchored { get; internal set; }
            /// <summary>R6.5 (coordinator, 7/11): the IDs of beams (family 1) and plates (family 2) given again by another GH datum with the same
            /// data, kept once (the copy dropped), in input order.</summary>
            public List<string>[] DroppedCopies { get; } = { new List<string>(), new List<string>(), new List<string>() };
            public override string ToString() => "merges by GH GUID nodes/beams/plates " + string.Join("/", Merges) + " (self-merges " + string.Join("/", SelfMerges) + "), position merges " + PositionMerges
                + ", ends moved " + EndsMoved + ", end nodes " + EndNodes + ", zero-length beams " + ZeroLength + ", corners moved " + CornersMoved + ", corner nodes " + CornerNodes
                + ", reader moved " + ReaderMoved + ", re-anchored " + Reanchored + ", copies of the same ID dropped beams/plates " + DroppedCopies[1].Count + "/" + DroppedCopies[2].Count;
        }

        /// <summary>
        /// Chain E (e3, P2K): Build Model on the drafts of the GH types, the steps of the former <c>ModelHelper.BuildModel</c> and of the
        /// conversion of its FEM output by the legacy reader:
        /// - per family, in input order: the first datum of a GH GUID takes its place and its BridGHe ID (its first Element ID, else the first
        ///   free integer among the first Element IDs of every datum of the family), the next ones are merged into it
        ///   (<see cref="Merge(LegacyNodeDraft, LegacyNodeDraft)"/>); beams and plates ordered by integer ID (stable; another ID last);
        /// - the RTree of Rhino on the definition units with the tolerance of the plug-in: a node within the tolerance of a kept node is merged
        ///   into the nearest (on a tie the first, <see cref="MergeCoincident"/>); a beam end or plate corner moves onto the last node found, or
        ///   creates a node (numbered after the IDs of the nodes); beams of zero length removed;
        /// - the assembly (<see cref="LegacyAssembly"/>): the case set from the loads of the elements, combinations and stages, the nodes and the
        ///   node lookup in mm, beams on the nodes found with the registered property (re-anchored: <see cref="LegacyAssembly.PlaceBeam"/>),
        ///   plates, stages, combinations, the carried data of the drafts and of the registered properties.
        /// Identity: the model GUID (owner of the element identities) from the drafts, the merges and the inputs, so the same inputs give the same
        /// GUIDs. Units: the drafts are in the definition units, normalised to N-mm by the assembly (e3 point 5, coordinator decision 1).
        /// </summary>
        /// <summary>A new string instance for an ID made from a number: .NET shares the strings of small numbers, and the archive of a model
        /// writes a shared string once (found by the 50: a node "255" and the colour "255" of a moving path); the former reader read every ID
        /// from the archive of the FEM model, a string of its own.</summary>
        private static string Fresh(string s) => new string(s.ToCharArray());

        /// <summary>
        /// R6.3: the load cases of Build Model from the case records of the drafts (the former <c>ModelHelper.GetLoadCases</c> on the FEM
        /// values): the case of every former load of the nodes, then of the beams, then of the plates, one per name (the first), «Dead» added
        /// when absent (a «Dead» with another definition is refused, «Dead load case error»), sorted as the former cases sorted («Dead» first,
        /// then by name with the comparison of the current culture). They check the cases of the combinations and fill the case menus of the
        /// Load Combinations parameter and of Staged Construction.
        /// </summary>
        public static IReadOnlyList<LegacyCaseData> Cases(IEnumerable<LegacyElementRecords> nodes, IEnumerable<LegacyElementRecords> beams, IEnumerable<LegacyElementRecords> plates)
        {
            var cases = new List<LegacyCaseData>();
            foreach (var records in nodes.Concat(beams).Concat(plates))
                foreach (var c in records.LoadCases)
                    if (!cases.Any(k => k.Name == c.Name)) cases.Add(c);
            var dead = Builders.GpcCaseBuilders.Case(Builders.GpcCaseBuilders.Dead, false);
            var given = cases.FirstOrDefault(k => k.Name == dead.Name);
            if (given == null) cases.Add(dead);
            else if (given.Inertia != dead.Inertia || given.GravityDirection != dead.GravityDirection || given.GravityValue != dead.GravityValue
                || given.StructuralMass != dead.StructuralMass || given.NonStructuralMass != dead.NonStructuralMass)
                throw new NotSupportedException("Dead load case error");
            cases.Sort((a, b) => a.Name == b.Name ? 0 : a.Name == dead.Name ? -1 : b.Name == dead.Name ? 1 : string.Compare(a.Name, b.Name));
            return cases;
        }

        public static GpcModels.Conversion Build(string name, BridgeUnits units, double toleranceDefinitionUnits, IReadOnlyList<(Guid Gh, LegacyNodeDraft Draft)> nodes,
            IReadOnlyList<(Guid Gh, LegacyBeamDraft Draft)> beams, IReadOnlyList<(Guid Gh, LegacyPlateDraft Draft)> plates, IReadOnlyList<LegacyCombinationDraft> combinations,
            IReadOnlyList<LegacyStageDraft> stages, BuildCounts counts = null)
        {
            counts ??= new BuildCounts();
            var identity = new System.Text.StringBuilder();
            identity.Append(name).Append('|').Append(units.Units).Append('|').Append(toleranceDefinitionUnits.ToString("R", System.Globalization.CultureInfo.InvariantCulture));
            int nextNode = 1;
            // IDs and merges by GH GUID, per family (former ModelHelper.BuildModel).
            List<(T Draft, string Id)> Family<T>(IReadOnlyList<(Guid Gh, T Draft)> data, int family, Func<T, LegacyElementRecords> recordsOf, Func<T, T, T> merge, Func<T, string> key) where T : class
            {
                var result = new List<(T Draft, string Id)>(); var first = new Dictionary<Guid, (int Index, T Draft)>();
                var byId = new Dictionary<string, int>(StringComparer.Ordinal);
                var used = data.Select(d => recordsOf(d.Draft).FirstLabel).Where(l => l != null).ToList();
                int next = 1;
                for (int i = 0; i < data.Count; i++)
                {
                    var (gh, draft) = data[i];
                    if (first.TryGetValue(gh, out var known))
                    {
                        // A self-merge: the same datum (the same draft, or a copy of it: the GUID of a draft comes from its content).
                        counts.Merges[family]++; if (ReferenceEquals(known.Draft, draft) || key(known.Draft) == key(draft)) counts.SelfMerges[family]++;
                        result[known.Index] = (merge(result[known.Index].Draft, draft), result[known.Index].Id);
                        identity.Append("|m").Append(family).Append(':').Append(known.Index).Append(':').Append(key(draft));
                        continue;
                    }
                    string id = recordsOf(draft).FirstLabel;
                    if (id == null) { while (used.Contains(next.ToString())) next++; id = Fresh(next.ToString()); next++; }
                    // R6.5 (coordinator, 7/11): a beam or plate whose ID another GH datum already gave (the FEM model merged them by ID): the same
                    // data (the same draft content) is kept once and named in a remark; other data with the same ID is an error naming the ID.
                    if (family != 0 && byId.TryGetValue(id, out int other))
                    {
                        if (key(result[other].Draft) != key(draft))
                            throw new ArgumentException($"{(family == 1 ? "Beam" : "Plate")} ID {id} is given by two elements with different data (LegacyElementIdGivenTwice): give each element its own Element ID, or connect it once.");
                        counts.DroppedCopies[family].Add(id);
                        identity.Append("|d").Append(family).Append(':').Append(id);
                        continue;
                    }
                    if (family != 0) byId[id] = result.Count;
                    first.Add(gh, (result.Count, draft)); result.Add((draft, id));
                    identity.Append("|e").Append(family).Append(':').Append(key(draft));
                }
                if (family == 0) nextNode = next;
                return result;
            }
            static string BridgeKey(GPC.Checkers.CompositeBridge.BridgeBeamData b) => b == null ? "-" : Fp(new object[] { b.Member, b.SuperElement, b.SectionState?.Behavior.ToString(), b.SectionState?.ModularRatio,
                b.SectionState?.ConcreteExcluded, b.SectionState?.ClassificationCase, b.SectionState?.PropertyFingerprint });
            var nodeList = Family(nodes, 0, d => d.Records, Merge, d => d.Node.Guid.ToString("N"));
            var beamList = Family(beams, 1, d => d.Records, Merge, d => d.Beam.Guid.ToString("N") + ":" + d.Property.Signature + ":" + BridgeKey(d.Bridge));
            var plateList = Family(plates, 2, d => d.Records, Merge, d => d.Plate.Guid.ToString("N"));
            static int Order(string id) => int.TryParse(id, out int n) ? n : int.MaxValue;
            beamList = beamList.Select((b, i) => (b, i)).OrderBy(x => Order(x.b.Id)).ThenBy(x => x.i).Select(x => x.b).ToList();
            plateList = plateList.Select((p, i) => (p, i)).OrderBy(x => Order(x.p.Id)).ThenBy(x => x.i).Select(x => x.p).ToList();
            foreach (var c in combinations) identity.Append("|c").Append(c.Name).Append(string.Concat(c.Factors.Select(f => ";" + f.Case + "=" + f.Factor.ToString("R", System.Globalization.CultureInfo.InvariantCulture))));
            foreach (var s in stages)
                identity.Append("|s").Append(s.Name).Append(';').Append(string.Join(",", s.Groups)).Append(';').Append(s.Morph).Append(s.Rotate)
                    .Append(string.Concat(s.Increments.Select(c => ";" + c.Name + string.Concat(c.Factors.Select(f => ":" + f.Case + "=" + f.Factor.ToString("R", System.Globalization.CultureInfo.InvariantCulture))))));

            // Node merge by position, beam ends, plate corners (former ModelHelper.BuildModel, RTree of Rhino).
            static Rhino.Geometry.Point3d R(GPC.Geometry.Point3d p) => new Rhino.Geometry.Point3d(p.X, p.Y, p.Z);
            var kept = new List<(LegacyNodeDraft Draft, string Id, Rhino.Geometry.Point3d Position)>();
            using var tree = new Rhino.Geometry.RTree();
            foreach (var (draft, id) in nodeList)
            {
                var pt = R(draft.Node.Position); bool found = false; int foundId = -1; double nearest = double.PositiveInfinity;
                tree.Search(new Rhino.Geometry.Sphere(pt, toleranceDefinitionUnits), (s, e) =>
                {
                    double distance = kept[e.Id].Position.DistanceToSquared(pt);
                    if (distance < nearest || distance == nearest && e.Id < foundId) { found = true; foundId = e.Id; nearest = distance; }
                });
                if (!found) { kept.Add((draft, id, pt)); tree.Insert(pt, kept.Count - 1); }
                else { kept[foundId] = (MergeCoincident(kept[foundId].Draft, draft), kept[foundId].Id, kept[foundId].Position); counts.PositionMerges++; }
            }
            int newNode = nextNode;
            var createdNodes = new HashSet<int>();
            Rhino.Geometry.Point3d Anchor(Rhino.Geometry.Point3d p, bool end)
            {
                bool found = false; int foundId = -1;
                tree.Search(new Rhino.Geometry.Sphere(p, toleranceDefinitionUnits), (s, e) => { found = true; foundId = e.Id; });
                if (found && foundId != -1) { if (kept[foundId].Position != p) { if (end) counts.EndsMoved++; else counts.CornersMoved++; } return kept[foundId].Position; }
                string nodeName = Fresh((newNode++).ToString());
                var created = new GPC.Model.Elements.NodeElement(GpcIdentity.Derive(GpcIdentity.NameSpace, "gpc-build-model-node:" + nodeName), new GPC.Geometry.Point3d(p.X, p.Y, p.Z), null, nodeName, 1);
                kept.Add((new LegacyNodeDraft(created, new LegacyImportReport { Units = units }), nodeName, p)); tree.Insert(p, kept.Count - 1); createdNodes.Add(kept.Count - 1);
                if (end) counts.EndNodes++; else counts.CornerNodes++;
                return p;
            }
            var beamEnds = new List<(LegacyBeamDraft Draft, string Id, Rhino.Geometry.Point3d Start, Rhino.Geometry.Point3d End)>();
            foreach (var (draft, id) in beamList) { var a = Anchor(R(draft.Beam.StartPoint), true); var b = Anchor(R(draft.Beam.EndPoint), true); beamEnds.Add((draft, id, a, b)); }
            int before = beamEnds.Count; beamEnds = beamEnds.Where(b => b.Start != b.End).ToList(); counts.ZeroLength = before - beamEnds.Count;
            var plateCorners = new List<(LegacyPlateDraft Draft, string Id, Rhino.Geometry.Point3d[] Corners)>();
            foreach (var (draft, id) in plateList) plateCorners.Add((draft, id, draft.Corners.Select(c => Anchor(R(c), false)).ToArray()));

            // The assembly, as the legacy reader converts the FEM output of Build Model.
            var owner = GpcIdentity.Derive(GpcIdentity.NameSpace, "gpc-build-model:" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(identity.ToString()))));
            var report = new LegacyImportReport();
            var assembly = new LegacyAssembly(name ?? "", units, owner, toleranceDefinitionUnits, report);
            int nodeCount = 0, beamCount = 0, plateCount = 0;
            foreach (var n in kept) report.ElementLoadCases.Add(new LegacyElementCases(GPC.Model.Core.Identity.EntityFamily.Node, ++nodeCount, n.Draft.Records.LoadRecords.Select(l => l.Case).ToArray()));
            foreach (var b in beamEnds) report.ElementLoadCases.Add(new LegacyElementCases(GPC.Model.Core.Identity.EntityFamily.Beam, ++beamCount, b.Draft.Records.LoadRecords.Select(l => l.Case).ToArray()));
            foreach (var p in plateCorners) report.ElementLoadCases.Add(new LegacyElementCases(GPC.Model.Core.Identity.EntityFamily.Shell, ++plateCount, p.Draft.Records.LoadRecords.Select(l => l.Case).ToArray()));
            var elementCases = kept.SelectMany(n => n.Draft.Records.LoadCases).Concat(beamEnds.SelectMany(b => b.Draft.Records.LoadCases)).Concat(plateCorners.SelectMany(p => p.Draft.Records.LoadCases)).ToList();
            assembly.RegisterCases(LegacyModelImport.CaseSet(Array.Empty<LegacyCaseData>(), elementCases, combinations.SelectMany(c => c.Factors).Select(f => f.Case).ToList(),
                stages.SelectMany(s => s.Increments).SelectMany(c => c.Factors).Select(f => f.Case).ToList(), Array.Empty<string>(), report));
            void Carry(LegacyImportReport draft) { foreach (var p in draft.NotExported) for (int q = 0; q < p.Value; q++) report.Carry(p.Key); }
            assembly.RequireTolerance();
            bool Moved(GPC.Geometry.Point3d n, double x, double y, double z) => n.X != units.ToMm(x) || n.Y != units.ToMm(y) || n.Z != units.ToMm(z);
            var placedNodes = new List<GPC.Model.Elements.NodeElement>();
            for (int i = 0; i < kept.Count; i++) { Carry(kept[i].Draft.Report); placedNodes.Add(assembly.PlaceNode(kept[i].Draft, i, kept[i].Id)); }
            assembly.AddNodes(placedNodes);
            for (int k = 0; k < beamEnds.Count; k++)
            {
                var (draft, id, start, end) = beamEnds[k];
                var (i, j) = assembly.BeamEnds(new GPC.Geometry.Point3d(units.ToMm(start.X), units.ToMm(start.Y), units.ToMm(start.Z)), new GPC.Geometry.Point3d(units.ToMm(end.X), units.ToMm(end.Y), units.ToMm(end.Z)), k + 1);
                var ni = assembly.Node(i); var nj = assembly.Node(j);
                // The counters compare in mm: the nodes of the model are normalised (e3 point 5), the RTree points and the drafts are not.
                if (Moved(ni.Position, start.X, start.Y, start.Z)) counts.ReaderMoved++;
                if (Moved(nj.Position, end.X, end.Y, end.Z)) counts.ReaderMoved++;
                var d = draft.Beam;
                if (Moved(ni.Position, d.StartPoint.X, d.StartPoint.Y, d.StartPoint.Z) || Moved(nj.Position, d.EndPoint.X, d.EndPoint.Y, d.EndPoint.Z)) counts.Reanchored++;
                var property = d.BeamProperty ?? throw new ArgumentException("GpcBuildModelBeamWithoutProperty: beam " + id);
                var p = assembly.BeamProperty(property.Name, draft.Property.Signature, k + 1, unique =>
                {
                    if (unique != property.Name) throw new NotSupportedException("GpcBuildModelPropertyNameTaken: the beam property '" + property.Name + "' would be renamed '" + unique + "'");
                    foreach (var c in draft.Records.PropertyCarries) for (int q = 0; q < c.Value; q++) report.Carry(c.Key);
                    return assembly.NormaliseBeamProperty(property, draft.Property);
                });
                Carry(draft.Report);
                assembly.AddBeam(assembly.PlaceBeam(draft, k, id, ni, nj, p), p.Native);
            }
            for (int k = 0; k < plateCorners.Count; k++)
            {
                var (draft, id, corners) = plateCorners[k];
                var connected = assembly.PlateNodes(corners.Select(c => new GPC.Geometry.Point3d(units.ToMm(c.X), units.ToMm(c.Y), units.ToMm(c.Z))), k + 1);
                for (int q = 0; q < corners.Length; q++) if (Moved(connected[q].Position, corners[q].X, corners[q].Y, corners[q].Z)) counts.ReaderMoved++;
                if (connected.Where((n, q) => Moved(n.Position, draft.Corners[q].X, draft.Corners[q].Y, draft.Corners[q].Z)).Any()) counts.Reanchored++;
                var property = assembly.PlateProperty(draft.Plate.PlateProperty.Name, () => assembly.NormalisePlateProperty(draft.Plate.PlateProperty));
                Carry(draft.Report);
                assembly.AddPlate(assembly.PlacePlate(draft, k, id, property, connected));
            }
            if (stages.Count != 0) LegacyModelImport.Stages(stages, assembly.Model, assembly.Cases, assembly.Groups, report);
            foreach (var c in combinations) assembly.AddCombination(c.Name, c.Factors.Select(f => (f.Case.Name, f.Factor)));
            report.BuildModel = counts;
            // R6.4 (2), extension (α) of §1bis (coordinator, 7/11): the drafts of the elements of the output, in the order of the former output
            // (the kept nodes, then those created at the beam ends and plate corners; the beams by ID without those of zero length; the plates
            // by ID): the drafts the reader gives the former values of the output (<see cref="OutputDrafts"/>).
            var drafts = OutputDrafts(kept, createdNodes, beamEnds, plateCorners);
            return new GpcModels.Conversion(assembly.Model, report, toleranceDefinitionUnits, drafts);
        }

        /// <summary>
        /// R6.4 (2), extension (α): the drafts of the elements of the output of Build Model, as the reader gives the former values of its FEM
        /// output (former <c>ModelHelper.BuildModel</c>): a kept node, its draft after the merges with the BridGHe ID as name (source: the ID, or
        /// the number 1 when the ID is empty); a node created at a beam end or plate corner, the draft of a former node with that ID and position
        /// and nothing else (<see cref="Builders.GpcNodeBuilders.Create"/>); a beam, its draft after the merges with the ID and the ends on the
        /// nodes found (<see cref="Builders.GpcBeamBuilders.Placed"/>, re-anchored); a plate, its draft after the merges with the ID and the
        /// corners on the nodes found. Definition units; the identity of the first datum of each (a created node: its own).
        /// </summary>
        private static GpcModelDrafts OutputDrafts(List<(LegacyNodeDraft Draft, string Id, Rhino.Geometry.Point3d Position)> kept, HashSet<int> createdNodes,
            List<(LegacyBeamDraft Draft, string Id, Rhino.Geometry.Point3d Start, Rhino.Geometry.Point3d End)> beamEnds,
            List<(LegacyPlateDraft Draft, string Id, Rhino.Geometry.Point3d[] Corners)> plateCorners)
        {
            static GPC.Geometry.Point3d G(Rhino.Geometry.Point3d p) => new GPC.Geometry.Point3d(p.X, p.Y, p.Z);
            // The source of a renamed element (the reader: the ID, the same string as the name, or the number 1 when the ID is empty). The name
            // keeps its string when it is equal already: the source takes that one (the archive of the draft writes a shared string once).
            static GPC.Model.Core.Identity.SourceIdentity Source(GPC.Model.Core.Identity.SourceIdentity s, LegacyImportReport report, GPC.Model.Core.Identity.EntityFamily family, string name) =>
                new GPC.Model.Core.Identity.SourceIdentity(s?.Program ?? LegacyModelImport.Program, s?.ModelRevision ?? report.Owner.ToString("D"), family, name.Length != 0 ? name : 1.ToString());
            var nodes = kept.Select((n, i) =>
            {
                if (createdNodes.Contains(i)) return Builders.GpcNodeBuilders.Create(n.Position.X, n.Position.Y, n.Position.Z, n.Id, null, n.Draft.Node.Guid);
                var d = GpcDrafts.Copy(n.Draft);
                d.Node.Name = n.Id; d.Node.Source = Source(d.Node.Source, d.Report, GPC.Model.Core.Identity.EntityFamily.Node, d.Node.Name);
                ShareCases(d.Node);
                return d;
            }).ToList();
            // The property of a beam or plate of the former output is a copy of its own (the former copy of a beam or plate value clones its
            // property apart from the rest of the value): no string of it is shared with the element (the archive of the draft shows sharing:
            // found by the captures, a group and a property both named "Beam").
            var beams = beamEnds.Select(b =>
            {
                var d = Builders.GpcBeamBuilders.Placed(b.Draft, b.Id, G(b.Start), G(b.End));
                ShareCases(d.Beam); d.Beam.BeamProperty = d.Beam.BeamProperty is GPC.Model.ElementProperties.ElasticBeamProperty e ? Isolated(e) : Isolated(d.Beam.BeamProperty);
                return d;
            }).ToList();
            var plates = plateCorners.Select(p =>
            {
                var d = GpcDrafts.Copy(p.Draft);
                d.Plate.Name = p.Id; d.Plate.Source = Source(d.Plate.Source, d.Report, GPC.Model.Core.Identity.EntityFamily.Shell, d.Plate.Name);
                ShareCases(d.Plate); d.Plate.PlateProperty = d.Plate.PlateProperty switch { GPC.Model.ElementProperties.ElasticPlateProperty e => Isolated(e), GPC.Model.ElementProperties.MembranePlateProperty m => Isolated(m), var other => Isolated(other) };
                return d with { Corners = p.Corners.Select(G).ToArray() };
            }).ToList();
            return new GpcModelDrafts(nodes, beams, plates);
        }

        /// <summary>R6.4 (2): a copy of a value through its XML archive (<see cref="GPC.Model.Persistence.ModelValueArchive"/>): its strings are new
        /// (the contract copy shares them with the source), shared inside the value as in the source, as the former copy of a value clones its
        /// property (<c>ModelArchive.Clone</c>).</summary>
        private static T Isolated<T>(T value) where T : class
        {
            if (value == null) return null;
            using var stream = new System.IO.MemoryStream();
            GPC.Model.Persistence.ModelValueArchive.Save(value, stream);
            stream.Position = 0;
            return GPC.Model.Persistence.ModelValueArchive.Load<T>(stream);
        }

        /// <summary>R6.4 (2): one case instance per case name in an element (the first in the order of <see cref="GpcModelCases.ElementCases(GPC.Model.Elements.Element)"/>),
        /// as the reader gives one former value (one case per name for all its loads): a merge takes the loads of the other datum with the case
        /// instances of that datum (the assembly takes the cases of the model by name: the model does not see it; the draft does).</summary>
        private static void ShareCases(GPC.Model.Elements.Element element)
        {
            var first = new Dictionary<string, LoadCaseBase>(StringComparer.Ordinal);
            foreach (var c in GpcModelCases.ElementCases(element).OfType<LoadCaseBase>()) first.TryAdd(c.Name, c);
            LoadCaseBase One(ILoadCase c) => c is LoadCaseBase b && first.TryGetValue(b.Name, out var f) ? f : (LoadCaseBase)c;
            foreach (var l in element.Loads.Values) l.LoadCase = One(l.LoadCase);
            switch (element)
            {
                case GPC.Model.Elements.NodeElement n:
                    foreach (var t in n.Assignments.Physical?.Temperatures ?? new List<NodalTemperatureAssignment>()) t.Case = One(t.Case);
                    break;
                case GPC.Model.Elements.BeamElement b:
                    foreach (var a in b.Assignments.Loads)
                        foreach (var v in new Load[] { a.Concentrated, a.StartIntensity, a.EndIntensity }) if (v != null) v.LoadCase = One(v.LoadCase);
                    if (b.Assignments.Physical != null)
                    {
                        foreach (var g in b.Assignments.Physical.TemperatureGradients) g.Case = One(g.Case);
                        foreach (var s in b.Assignments.Physical.InitialStates) s.Case = One(s.Case);
                    }
                    break;
                case GPC.Model.Elements.AreaElement p:
                    if (p.Assignments.Physical != null)
                    {
                        foreach (var s in p.Assignments.Physical.InitialStresses) s.Case = One(s.Case);
                        foreach (var m in p.Assignments.Physical.NonStructuralMasses) m.Case = One(m.Case);
                    }
                    break;
            }
        }
    }
}
