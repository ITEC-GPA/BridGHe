using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using BridGHe.Common.Gpc.Legacy;
using BridGHe.Common.Helpers;
using BridGHe.Common.Models;

namespace BridGHe.Common.Gpc
{
    /// <summary>
    /// F6 phase (c), C2: the GPC model (N-mm) of a former FEM model of the components, converted with the legacy reader (the semantics of
    /// the former exporter, verified on examples 50/99, 01–10 and 77 captured models) until the components build GPC models themselves.
    /// The identity is derived from the content and the tolerance: the same FEM model gives the same GPC model, element GUIDs included.
    /// Results of the former model are not converted (an analysis attaches its own GPC results).
    /// </summary>
    public static class GpcModels
    {
        /// <summary>C3: a conversion and what the analysis needs with it: the import report (warnings, resolved native sections by beam
        /// GUID) and the tolerance used. Immutable by convention: analysis and export copy <see cref="Model"/> before changing it.</summary>
        public sealed class Conversion
        {
            public GPC.Model.Models.Model Model { get; }
            public LegacyImportReport Report { get; }
            public double Tolerance { get; }
            internal Conversion(GPC.Model.Models.Model model, LegacyImportReport report, double tolerance) { Model = model; Report = report; Tolerance = tolerance; }
            /// <summary>L12: the bridge data of <see cref="Model"/> (members, super elements, local section states by beam GUID).</summary>
            public GPC.Checkers.CompositeBridge.BridgeModelData Bridge => Report.Bridge;
            /// <summary>L12: <see cref="Model"/> with its bridge data (the data object is shared, not copied).</summary>
            public GPC.Checkers.CompositeBridge.BridgeModel BridgeModel => new GPC.Checkers.CompositeBridge.BridgeModel(Model, Report.Bridge);

            /// <summary>F6 chain D (step 32, P2T): the input of the model transformers (<see cref="GpcModelTransforms"/>) for a model built in GPC
            /// (no FEM model, no conversion). The report: the definition <paramref name="units"/>; owner of the element identities = the model
            /// GUID; every case registered, in the order of the model (origins: registered, plus element loads, combinations, stage increments and
            /// moving paths where the model refers to the case); the BridGHe angle of every beam by its number (<paramref name="beamAngles"/>);
            /// the data of every beam property by name from its first beam (formulation of that beam, principal angle of the section, native
            /// section of that beam if any, GPC fingerprint of the property: <see cref="BeamProperty(GPC.Model.ElementProperties.BeamProperty, GPC.Model.Structure.Assignments.BeamFormulation, GPC.Converter.Straus7.Straus7SectionDefinition)"/>);
            /// the resolved native sections by beam GUID (<paramref name="nativeSections"/>, none if null); the bridge data of the model
            /// (<paramref name="bridge"/>, L12: none if null; copied); no warnings. The model is kept, not copied: the transformers copy it.</summary>
            public static Conversion FromGpc(GPC.Model.Models.Model model, BridgeUnits units, IReadOnlyDictionary<int, double> beamAngles, double toleranceDefinitionUnits,
                IReadOnlyDictionary<Guid, GPC.Converter.Straus7.Straus7SectionDefinition> nativeSections = null, GPC.Checkers.CompositeBridge.BridgeModelData bridge = null)
            {
                if (model == null) throw new ArgumentNullException(nameof(model));
                if (beamAngles == null) throw new ArgumentNullException(nameof(beamAngles));
                if (!double.IsFinite(toleranceDefinitionUnits) || toleranceDefinitionUnits <= 0) throw new ArgumentException("Tolerance must be finite and positive.");
                if (model.Datasets.Count != 0 || model.Analysis != null || model.AllElements.Any(e => e.Results.Count != 0))
                    throw new ArgumentException("GpcInputNotAnInputModel: " + model.Name);
                var beams = model.BeamElements.Values.OrderBy(b => b.Id).ToArray();
                var missing = beams.Where(b => !beamAngles.TryGetValue(b.Id, out var a) || !double.IsFinite(a)).Select(b => b.Id).ToArray();
                if (missing.Length != 0) throw new ArgumentException("GpcInputBeamAngleMissing: " + string.Join(", ", missing));
                var natives = nativeSections ?? new Dictionary<Guid, GPC.Converter.Straus7.Straus7SectionDefinition>();
                var beamGuids = beams.Select(b => b.Guid).ToHashSet();
                if (natives.Keys.Any(g => !beamGuids.Contains(g))) throw new ArgumentException("GpcInputNativeSectionOfUnknownBeam");

                var report = new LegacyImportReport { Units = units, Owner = model.Guid, RegisteredCases = model.LoadCases.Count, Bridge = bridge?.Copy() ?? new GPC.Checkers.CompositeBridge.BridgeModelData() };
                if (report.Bridge.Beams.Any(b => !beamGuids.Contains(b.BeamGuid))) throw new ArgumentException("GpcInputBridgeDataOfUnknownBeam");
                foreach (var c in model.LoadCases.Values) report.CaseOrigins[c.Name] = LegacyCaseOrigins.Registered;
                void Origin(GPC.Model.LoadCases.ILoadCase c, LegacyCaseOrigins origin)
                {
                    if (c is GPC.Model.LoadCases.LoadCaseBase b && report.CaseOrigins.TryGetValue(b.Name, out var o)) report.CaseOrigins[b.Name] = o | origin;
                }
                foreach (var c in GpcModelCases.ElementCases(model)) Origin(c, LegacyCaseOrigins.ElementLoad);
                // The cases of the loads of every element, nodes, beams and plates in number order (Construction Sequence orders its cases from them).
                foreach (var e in model.NodesElements.Values.Cast<GPC.Model.Elements.Element>().Concat(model.BeamElements.Values).Concat(model.AreaElements.Values))
                    report.ElementLoadCases.Add(new LegacyElementCases(GPC.Model.Models.Model.FamilyOf(e), e.Id,
                        GpcModelCases.ElementCases(e).OfType<GPC.Model.LoadCases.LoadCaseBase>().Select(c => c.Name).ToArray()));
                foreach (var combination in model.Combinations.Values) foreach (var c in combination.GetLoadCases()) Origin(c, LegacyCaseOrigins.Combination);
                var byGuid = model.LoadCases.Values.ToDictionary(c => c.Guid);
                foreach (var stage in model.Stages.Values)
                    foreach (var increment in stage.AnalysisState?.Increments ?? Array.Empty<GPC.Model.Stages.StageLoadIncrement>())
                        foreach (var factor in increment.LoadFactors ?? Array.Empty<GPC.Model.Stages.StageCaseFactor>())
                            if (byGuid.TryGetValue(factor.CaseGuid, out var c)) Origin(c, LegacyCaseOrigins.Stage);
                if (model.MovingLoads != null)
                    foreach (var path in model.MovingLoads.Paths) if (byGuid.TryGetValue(path.CaseGuid, out var c)) Origin(c, LegacyCaseOrigins.Moving);

                foreach (var beam in beams)
                {
                    report.BeamAngles[beam.Id] = beamAngles[beam.Id];
                    var property = beam.BeamProperty ?? throw new ArgumentException("GpcInputBeamWithoutProperty: " + beam.Name);
                    if (!model.BeamProperties.TryGetValue(property.Name, out var registered) || !ReferenceEquals(registered, property))
                        throw new ArgumentException("GpcInputBeamPropertyNotRegistered: " + property.Name);
                    natives.TryGetValue(beam.Guid, out var native);
                    if (report.BeamProperties.TryGetValue(property.Name, out var known))
                    {
                        // One datum per property (the transformers apply it to a beam that takes the property): its beams agree.
                        if (known.Formulation != beam.Assignments.Formulation || !ReferenceEquals(known.Native, native))
                            throw new ArgumentException("GpcInputBeamPropertyDataDiffer: " + property.Name);
                        continue;
                    }
                    report.BeamProperties.Add(property.Name, GpcModels.BeamProperty(property, beam.Assignments.Formulation, native).Data);
                }
                foreach (var p in natives) report.NativeSections.Add(p.Key, p.Value);
                return new Conversion(model, report, toleranceDefinitionUnits);
            }
        }

        /// <summary>F6 chain D (step 32): a beam property built in GPC as the datum of the model transformers (e.g. the replacement of Bridge
        /// Phase Model): the formulation a beam takes with it, the principal angle of its section (the angle of an explicit section, 0 for a
        /// standard shape, that of the native section if given) and its signature, the GPC fingerprint of the property ("GPC:" prefix, never
        /// equal to the digest of a former property).</summary>
        public static LegacyBeamProperty BeamProperty(GPC.Model.ElementProperties.BeamProperty property, GPC.Model.Structure.Assignments.BeamFormulation formulation,
            GPC.Converter.Straus7.Straus7SectionDefinition native = null)
        {
            if (property == null) throw new ArgumentNullException(nameof(property));
            if (string.IsNullOrWhiteSpace(property.Name)) throw new ArgumentException("GpcBeamPropertyNameRequired");
            double angle = native != null ? native.ReferenceProperties[10]
                : property is GPC.Model.ElementProperties.ElasticBeamProperty elastic && elastic.Section?.GetType() == typeof(GPC.Model.Sections.Section)
                    ? ((GPC.Model.Sections.Section)elastic.Section).AngleX1 : 0;
            string signature = "GPC:" + GPC.Model.Core.ModelValues.Fingerprint(new object[] { property });
            return new LegacyBeamProperty(property, new LegacyBeamPropertyData(formulation, angle, native, signature));
        }

        public static Conversion Convert(ModelModel model, double toleranceDefinitionUnits)
        {
            var gpc = FromFemm(model, toleranceDefinitionUnits, out var report);
            return new Conversion(gpc, report, toleranceDefinitionUnits);
        }

        public static GPC.Model.Models.Model FromFemm(ModelModel model, double toleranceDefinitionUnits, out LegacyImportReport report)
        {
            if (model == null) throw new ArgumentNullException(nameof(model));
            // F6 chain C: the results of an analysed model are never converted, so they are not archived either: the conversion reads a copy
            // without them. The identity does not depend on the results, and the size limit of the legacy reader concerns the input only (the
            // analysed phase models of example 50, the references of Build BridGHe Model, exceed 256 MB with their results).
            if (model.Elements.Any(e => e.Results.Count != 0))
            {
                model = new ModelModel(model);
                foreach (var e in model.Elements) e.Results.Clear();
            }
            byte[] archive = ModelArchive.Write(model);
            var owner = GpcIdentity.Derive(GpcIdentity.NameSpace, "femm-model:" + System.Convert.ToHexString(SHA256.HashData(archive)) + ":"
                + toleranceDefinitionUnits.ToString("R", CultureInfo.InvariantCulture));
            report = new LegacyImportReport();
            return LegacyModelImport.Import(LegacyArchive.Read(archive), owner, toleranceDefinitionUnits, report, SectionStateSignatures(model));
        }

        /// <summary>F6 (L11): the former section-state signature of the property of every former beam (by index), for the legacy reader to
        /// tell a current local section state from a stale one (the former checks compared them).</summary>
        public static Func<int, string> SectionStateSignatures(ModelModel model)
        {
            if (model == null) throw new ArgumentNullException(nameof(model));
            var beams = model.Beams.ToArray();
            return k => BridgeSectionAssignment.Signature(beams[k].BeamProperty);
        }

        /// <summary>F6 chain D: a former beam property (the datum of a Beam Property parameter) as GPC, with the rules of the conversion of a
        /// whole model; dimensions in the definition <paramref name="units"/> (those of the model it goes into).</summary>
        public static LegacyBeamProperty BeamProperty(BeamPropertyModel property, BridgeUnits units)
        {
            if (property == null) throw new ArgumentNullException(nameof(property));
            return LegacyModelImport.BeamProperty(LegacyArchive.Read(ModelArchive.Write(property)), units);
        }
    }

    /// <summary>F6 chain D: the load cases the elements of a GPC model refer to (the element loads of the former model, as the legacy reader
    /// converts them): element loads, beam load assignments, case-dependent physical data (node temperatures, beam gradients and initial
    /// states, plate initial stresses and non-structural masses) and prescribed restraints.</summary>
    internal static class GpcModelCases
    {
        internal static IEnumerable<GPC.Model.LoadCases.ILoadCase> ElementCases(GPC.Model.Models.Model model) => model.AllElements.SelectMany(ElementCases);

        internal static IEnumerable<GPC.Model.LoadCases.ILoadCase> ElementCases(GPC.Model.Elements.Element element)
        {
            foreach (var load in element.Loads.Values) yield return load.LoadCase;
            if (element is GPC.Model.Elements.NodeElement node)
            {
                foreach (var t in node.Assignments.Physical?.Temperatures ?? new List<GPC.Model.Structure.Assignments.NodalTemperatureAssignment>()) yield return t.Case;
                foreach (var r in node.Assignments.Restrains) if (r.Case != null) yield return r.Case;
            }
            else if (element is GPC.Model.Elements.BeamElement beam)
            {
                foreach (var a in beam.Assignments.Loads)
                    foreach (var value in new GPC.Model.Loads.Load[] { a.Concentrated, a.StartIntensity, a.EndIntensity })
                        if (value != null) yield return value.LoadCase;
                var physical = beam.Assignments.Physical;
                if (physical != null)
                {
                    foreach (var g in physical.TemperatureGradients) yield return g.Case;
                    foreach (var s in physical.InitialStates) yield return s.Case;
                }
            }
            else if (element is GPC.Model.Elements.AreaElement plate)
            {
                var physical = plate.Assignments.Physical;
                if (physical != null)
                {
                    foreach (var s in physical.InitialStresses) yield return s.Case;
                    foreach (var m in physical.NonStructuralMasses) yield return m.Case;
                }
            }
        }
    }
}
