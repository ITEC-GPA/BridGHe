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
    ///   list of stages, the default "Dead" (<see cref="StageLessCase"/>); within the other datum the loads of one case share it, so a load
    ///   equal to one appended before from the same datum is not appended again.
    /// Examples (L12 scope, scan of 5/11, P2K): no merge by GH GUID in any Build Model run. Never changes its inputs.
    /// </summary>
    public static class LegacyBuildModel
    {
        /// <summary>The case of the former data whose stage list is null (the copy of <c>LoadCaseModel.Dead</c> the Load Case component
        /// gives for that name): the only case whose loads two GH data can share in the former equality.</summary>
        public const string StageLessCase = "Dead";

        private static string Fp(params object[] values) => ModelValues.Fingerprint(values);
        private static string CaseName(ILoadCase c) => ((LoadCaseBase)c).Name;

        /// <summary>Whether a load of the other datum is equal to one already in the list: same data, same case, and the case may be
        /// shared (the stage-less case, or a load appended from the same datum).</summary>
        private static bool Contains<T>(IEnumerable<(T Record, bool FromOther)> existing, T record, Func<T, string> key, Func<T, string> caseName) =>
            existing.Any(e => key(e.Record) == key(record) && caseName(e.Record) == caseName(record) && (e.FromOther || caseName(record) == StageLessCase));

        /// <summary>The report of a merged draft: that of the first, with what the appended records carry (the former reader carries it once
        /// per record of the merged list: an Element ID without a value, a slab strain, a plate support with its former face).</summary>
        private static LegacyImportReport MergedReport(LegacyImportReport first) => first.CopyFor(first.RegisteredCases);

        /// <summary>The Element IDs without a value: <c>Contains</c> finds the first one appended, so the merged list has one at most more.</summary>
        private static void EmptyLabels(LegacyElementRecords records, LegacyElementRecords other, LegacyImportReport report)
        {
            if (other.EmptyLabels == 0 || records.EmptyLabels != 0) return;
            records.EmptyLabels = 1; report.Carry("Empty Element ID attribute");
        }

        /// <summary>The merge of two node drafts with the same GH GUID (<paramref name="other"/> into a copy of <paramref name="first"/>).</summary>
        public static LegacyNodeDraft Merge(LegacyNodeDraft first, LegacyNodeDraft other)
        {
            var node = ModelValues.CopyValue(first.Node); var records = first.Records.Copy(); var source = other.Node; var report = MergedReport(first.Report);
            EmptyLabels(records, other.Records, report);
            foreach (var g in source.Groups.Values) if (!node.Groups.ContainsKey(g.Name)) node.AddGroup(g);
            // Loads, in the order of the former loads of the other datum.
            var forces = node.Loads.Values.OfType<PointLoad>().Select(l => (l, false)).ToList();
            var temperatures = (node.Assignments.Physical?.Temperatures ?? new List<NodalTemperatureAssignment>()).Select(t => (t, false)).ToList();
            var otherForces = new Queue<PointLoad>(source.Loads.Values.OfType<PointLoad>());
            var otherTemperatures = new Queue<NodalTemperatureAssignment>(source.Assignments.Physical?.Temperatures ?? new List<NodalTemperatureAssignment>());
            static string ForceKey(PointLoad l) => Fp(l.Name, l.F1, l.F2, l.F3, l.M1, l.M2, l.M3);
            static string TemperatureKey(NodalTemperatureAssignment t) => Fp((int)t.Kind, t.Celsius);
            foreach (var (kind, caseName) in other.Records.LoadRecords)
            {
                if (kind == LegacyModelImport.ForceRecord || kind == LegacyModelImport.MomentRecord)
                {
                    var l = otherForces.Dequeue();
                    if (Contains(forces, l, ForceKey, x => CaseName(x.LoadCase))) continue;
                    var copy = new PointLoad(new GPC.Geometry.Vector3d(l.F1, l.F2, l.F3), new GPC.Geometry.Vector3d(l.M1, l.M2, l.M3), node.Position, (LoadCaseBase)l.LoadCase) { Name = l.Name };
                    node.Loads.Add(copy); forces.Add((copy, true));
                }
                else if (kind == "NodeTemperatureLoadModel")
                {
                    var t = otherTemperatures.Dequeue();
                    if (Contains(temperatures, t, TemperatureKey, x => CaseName(x.Case))) continue;
                    var copy = new NodalTemperatureAssignment { Case = t.Case, Kind = t.Kind, Celsius = t.Celsius };
                    (node.Assignments.Physical ??= new NodePhysicalData()).Temperatures.Add(copy); temperatures.Add((copy, true));
                }
                else throw new ArgumentException("LegacyNodeLoadRecordUnknown: " + kind);
                records.LoadList.Add((kind, caseName));
            }
            // Attributes: restraint records by content, then the Element IDs and the rigid connections in the order of the former reader
            // (own attributes, then the rigid connections), as new attributes numbered in that order.
            static string RestraintKey(RestrainAssignment r) => Fp(r.SourceRecord, r.Restrain?.CoordinateSystem, r.Restrain?.Restrains);
            foreach (var r in source.Assignments.Restrains)
                if (!node.Assignments.Restrains.Any(x => RestraintKey(x) == RestraintKey(r)))
                    node.Assignments.Restrains.Add(new RestrainAssignment { Restrain = new NodeRestrain(node, r.Restrain.CoordinateSystem, r.Restrain.Restrains.ToList()), SourceRecord = r.SourceRecord });
            var labels = node.Attributes.Values.OfType<ElementLabelAttribute>().Select(a => a.Label).ToList();
            var targets = node.Attributes.Values.OfType<RigidConnectionAttribute>().Select(a => a.TargetMm).ToList();
            foreach (var a in source.Attributes.Values.OfType<ElementLabelAttribute>()) if (!labels.Contains(a.Label)) labels.Add(a.Label);
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
                    var known = existing.Select((e, i) => (e, i)).Any(x => x.e.Item1.Kind == load.Kind && x.e.Item1.Key == key && records.LoadRecords[x.i].Case == load.Case
                        && (x.e.FromOther || load.Case == StageLessCase));
                    if (known) continue;
                    records.LoadList.Add(load); existing.Add(((load.Kind, key), true));
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
            var pressures = plate.Loads.Values.OfType<ShellFacePressure>().Select(l => (l, false)).ToList();
            var physical = plate.Assignments.Physical; var otherPhysical = source.Assignments.Physical;
            var stresses = (physical?.InitialStresses ?? new List<ShellInitialStress>()).Select(s => (s, false)).ToList();
            var masses = (physical?.NonStructuralMasses ?? new List<ShellNonStructuralMass>()).Select(s => (s, false)).ToList();
            var otherPressures = new Queue<ShellFacePressure>(source.Loads.Values.OfType<ShellFacePressure>());
            var otherStresses = new Queue<ShellInitialStress>(otherPhysical?.InitialStresses ?? new List<ShellInitialStress>());
            var otherMasses = new Queue<ShellNonStructuralMass>(otherPhysical?.NonStructuralMasses ?? new List<ShellNonStructuralMass>());
            static string PressureKey(ShellFacePressure l) => Fp((int)l.Face, l.NormalPressure, l.GlobalPressure, (int)l.Projection);
            foreach (var (kind, caseName) in other.Records.LoadRecords)
            {
                switch (kind)
                {
                    case "PlatePressureModel":
                    {
                        var l = otherPressures.Dequeue();
                        if (Contains(pressures, l, PressureKey, x => CaseName(x.LoadCase))) continue;
                        var copy = l.NormalPressure.HasValue ? ShellFacePressure.Normal((LoadCaseBase)l.LoadCase, l.Face, l.NormalPressure.Value)
                            : ShellFacePressure.Global((LoadCaseBase)l.LoadCase, l.Face, l.GlobalPressure, l.Projection);
                        plate.Loads.Add(copy); pressures.Add((copy, true)); break;
                    }
                    case "PlatePreStressModel":
                    {
                        var s = otherStresses.Dequeue();
                        if (Contains(stresses, s, x => Fp(x.Components), x => CaseName(x.Case))) continue;
                        var copy = new ShellInitialStress { Case = s.Case, Components = s.Components };
                        (physical ??= plate.Assignments.Physical ??= new ShellPhysicalData()).InitialStresses.Add(copy); stresses.Add((copy, true)); break;
                    }
                    case "PlateNSMassModel":
                    {
                        // The former mass load has no equality of its own: always appended.
                        var m = otherMasses.Dequeue();
                        var copy = new ShellNonStructuralMass { Case = m.Case, MassPerArea = m.MassPerArea, DynamicFactor = m.DynamicFactor, GlobalOffset = m.GlobalOffset };
                        (physical ??= plate.Assignments.Physical ??= new ShellPhysicalData()).NonStructuralMasses.Add(copy); masses.Add((copy, true)); break;
                    }
                    default: throw new ArgumentException("LegacyPlateLoadRecordUnknown: " + kind);
                }
                records.LoadList.Add((kind, caseName));
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
            return new LegacyPlateDraft(plate, first.Corners, report) { Records = records };
        }
    }
}
