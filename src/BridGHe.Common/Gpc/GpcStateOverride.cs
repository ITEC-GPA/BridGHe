using System;
using System.Collections.Generic;
using System.Linq;
using BridgeActionBehavior = BridGHe.Common.Checks.BridgeActionBehavior;
using BridGHe.Common.Geometry;
using BridGHe.Common.Gpc.Legacy;
using GPC.Checkers.CompositeBridge;
using GPC.Model.Elements;
using GPC.Model.Structure.Assignments;
using GPC.Model.ElementProperties;

namespace BridGHe.Common.Gpc
{
    /// <summary>The GPC drafts of Bridge State Override: the girders with their new property and local section state, the other members as given.</summary>
    public sealed record GpcStateOverrideResult(LegacyBeamDraft[] Girders, LegacyBeamDraft[] OtherMembers);

    /// <summary>
    /// Chain E (d): Bridge State Override (<see cref="BridgeStateOverride.Apply"/>) on the GPC drafts of the beams
    /// (<see cref="GpcBeamDrafts"/>), the definitions of the composite sections (<see cref="BridgeCompositeSectionDefinition"/>, one per
    /// fabrication segment) and the moment states classified from the analysed GPC beams (<see cref="BeamMomentState.Analysed"/>), with the same
    /// rules and messages:
    /// <list type="bullet">
    /// <item>the girders (bridge member of family Girder) in the order of their group and station, one segment ID "G&lt;group&gt;-C&lt;segment&gt;"
    /// per definition (<c>BridgeSegmentComposite.Map</c>);</item>
    /// <item>the reference check against the analysed beam of the state (ends within 1e-5, offset and BridgeHe angle equal, area, inertias,
    /// torsion and modulus within 1e-10 relative, density within 1e-20);</item>
    /// <item>cracked for a negative sign, for a mixed sign with policy 2, for a zero sign with policy 1;</item>
    /// <item>the homogenised property of the segment (<see cref="GpcCompositeSections.Homogenised"/> of the definition at n∞ or n0, with the slab
    /// when uncracked; <see cref="ElasticBeamProperty"/> with the explicit section of those values, principal angle 0) named
    /// "&lt;segment&gt;&lt;definition&gt;_&lt;behaviour&gt;_&lt;C|U&gt;", one per segment, state and mass; its material the analysis steel of the definition
    /// with the density that keeps the mass per length of the beam (Preserve Mass);</item>
    /// <item>the local section state of the beam (behaviour, cracked, n, case, D3 fingerprint of the new property) in its bridge data.</item>
    /// </list>
    /// Equal bit for bit to the conversion of the FEM output (<c>gpc-state-override</c>, native 50).
    /// </summary>
    public static class GpcStateOverride
    {
        public static GpcStateOverrideResult Apply(IReadOnlyList<LegacyBeamDraft> beams, IReadOnlyList<string> ids, IReadOnlyList<BridgeCompositeSectionDefinition> definitions,
            IReadOnlyList<BeamMomentState> states, BridgeActionBehavior behavior, int mixedPolicy = 0, int zeroPolicy = 0, bool preserveMass = true)
        {
            if (behavior is not (BridgeActionBehavior.LongTerm or BridgeActionBehavior.ShortTerm) || mixedPolicy is < 0 or > 2 || zeroPolicy is < 0 or > 1)
                throw new ArgumentException("Only mechanical n infinity/n0 states are supported; shrinkage remains a separate uncracked model.");
            if (beams == null || beams.Any(b => b?.Beam?.BeamProperty is not ElasticBeamProperty || !(b.Beam.Length > 0))) throw new ArgumentException("Provide valid steel reference beams and properties.");
            var (girderDrafts, mappedDefinitions, _) = Map(beams, ids, definitions);
            if (states == null || states.Any(s => s?.Source?.IsValid != true || !Enum.IsDefined(s.Sign) || string.IsNullOrWhiteSpace(s.Case)) ||
                states.Select(s => s.Case).Distinct(StringComparer.Ordinal).Count() != 1 || states.Select(s => s.Source.BeamId).Distinct(StringComparer.Ordinal).Count() != states.Count ||
                !girderDrafts.Select(b => b.Beam.Name).ToHashSet(StringComparer.Ordinal).SetEquals(states.Select(s => s.Source.BeamId)))
                throw new ArgumentException("One moment state is required for each girder FEM ID, without additional beams.");
            var map = states.ToDictionary(s => s.Source.BeamId, StringComparer.Ordinal);
            var definitionOf = girderDrafts.Select((b, i) => (b.Beam.Name, Definition: mappedDefinitions[i])).ToDictionary(p => p.Name, p => p.Definition, StringComparer.Ordinal);
            var girders = new List<LegacyBeamDraft>(); var others = new List<LegacyBeamDraft>();
            var variants = new Dictionary<(string Segment, bool Cracked, double Mass), ElasticBeamProperty>();
            foreach (var draft in beams)
            {
                var b = draft.Beam;
                if (!map.TryGetValue(b.Name, out var state)) { others.Add(GpcBeamDrafts.With(draft, GPC.Model.Core.ModelValues.CopyValue(b), draft.Bridge)); continue; }
                var property = (ElasticBeamProperty)b.BeamProperty;
                var source = state.Analysed;
                if (source.Beam == null || source.Catalog == null)
                    throw new ArgumentException($"Beam {b.Name}: the moment state has no analysed GPC beam (classify it from the beams of Decompose Model of an analysed model).");
                Reference(b, draft.AngleDeg, property, source);
                if (state.Sign == BeamMomentSign.Missing || state.Sign == BeamMomentSign.Mixed && mixedPolicy == 0)
                    throw new ArgumentException($"Beam {b.Name}: {state.Sign}. Obtain FEM results or explicitly choose a mixed-beam policy/refine the common mesh.");
                bool cracked = state.Sign == BeamMomentSign.Negative || state.Sign == BeamMomentSign.Mixed && mixedPolicy == 2 || state.Sign == BeamMomentSign.Zero && zeroPolicy == 1;
                var full = definitionOf[b.Name];
                if (!full.ConsiderConcrete || !full.ConsiderRebar) throw new ArgumentException("Supply full composite definitions with concrete and reinforcement enabled.");
                double n = behavior == BridgeActionBehavior.LongTerm ? full.NLongTerm : full.NShortTerm;
                // The mass per length of the FEM path, bit for bit (rule of 0.4.0: the self-weight is kept; GpcSectionMass).
                double mass = GpcSectionMass.MassPerLength(property);
                var meta = draft.Bridge.Member;
                string segment = $"G{meta.Group}-C{meta.Segment}";
                var key = (segment, cracked, preserveMass ? mass : 0d);
                if (!variants.TryGetValue(key, out var replacement))
                {
                    var values = GpcCompositeSections.Homogenised(full, n, !cracked);
                    var material = GPC.Model.Core.ModelValues.CopyValue(full.AnalysisSteel ?? throw new ArgumentException($"The composite definition '{full.Name}' has no analysis steel."));
                    var section = GpcSections.Explicit("", values.Area, values.I11, values.I22, values.J, values.CentroidX, values.CentroidY, 0, 0, 0);
                    replacement = new ElasticBeamProperty(segment + full.Name + $"_{behavior}_{(cracked ? "C" : "U")}", material, section, values.ShearArea1, values.ShearArea2);
                    if (preserveMass) material.Density = mass / values.Area;
                    if (!double.IsFinite(material.Density) || material.Density < 0) throw new ArgumentException("Invalid physical mass per length.");
                    variants.Add(key, replacement);
                }
                var beam = GPC.Model.Core.ModelValues.CopyValue(b);
                beam.BeamProperty = replacement;
                beam.Assignments.Formulation = BeamFormulation.StraightTwoNode;
                var axes = GpcBeamAxes.Section(beam.StartPoint, beam.EndPoint, draft.AngleDeg, 0);
                beam.Assignments.SectionAxes = axes;
                if (beam.Assignments.Physical != null)
                    foreach (var support in beam.Assignments.Physical.Supports)
                        support.Axes = new GPC.Geometry.CoordinateSystem(new GPC.Geometry.Point3d(0, 0, 0), axes.V1, axes.V2);
                var sectionState = new BridgeLocalSectionState((GPC.Checkers.CompositeBridge.BridgeActionBehavior)Enum.Parse(typeof(GPC.Checkers.CompositeBridge.BridgeActionBehavior), behavior.ToString()), cracked, n, string.IsNullOrEmpty(state.Case) ? null : state.Case, BridgeWorkflowDefinition.PropertyFingerprint(beam));
                var bridge = (draft.Bridge ?? new BridgeBeamData(beam.Guid)).WithSectionState(sectionState);
                // The data of the new property: formulation, principal angle 0, no native section, the GPC content signature (a FEM property had the digest of its former content).
                // R6.1 (found by GpcDraftCheck on the native 50): the former component replaces every section state record of the beam with one
                // (BridgeStateOverride.cs), so the records of the draft count one state, as the reader counts it on the former output.
                var records = draft.Records.Copy(); records.SectionStates = 1;
                girders.Add(GpcBeamDrafts.With(draft, beam, bridge, new LegacyBeamPropertyData(BeamFormulation.StraightTwoNode, 0, null, "GPC:" + GPC.Model.Core.ModelValues.Fingerprint(new object[] { replacement }))) with { Records = records });
            }
            return new GpcStateOverrideResult(girders.ToArray(), others.ToArray());
        }

        /// <summary>The girders of the drafts (bridge member of family Girder) in the order of their group and station, the definition of the
        /// segment of each and the segment IDs (<c>BridgeSegmentComposite.Map</c> on the drafts, same messages).</summary>
        public static (LegacyBeamDraft[] Girders, BridgeCompositeSectionDefinition[] Definitions, string[] Segments) Map(IReadOnlyList<LegacyBeamDraft> beams, IReadOnlyList<string> ids,
            IReadOnlyList<BridgeCompositeSectionDefinition> definitions)
        {
            if (beams == null || beams.Count == 0 || beams.Any(b => b == null)) throw new ArgumentException("Supply bridge FEM beams with Bridge Member metadata.");
            if (ids == null || definitions == null || ids.Count == 0 || ids.Count != definitions.Count || ids.Any(string.IsNullOrWhiteSpace) ||
                ids.Distinct(StringComparer.Ordinal).Count() != ids.Count || definitions.Any(p => p == null))
                throw new ArgumentException("Supply one valid composite property per distinct Segment ID; list lengths must match.");
            if (beams.Any(b => b.Bridge?.Member == null)) throw new ArgumentException("Every input beam requires exactly one Bridge Member identity.");
            var girders = beams.Where(b => b.Bridge.Member.Family == GPC.Checkers.CompositeBridge.Layout.BridgeMemberFamily.Girder)
                .OrderBy(b => b.Bridge.Member.Group).ThenBy(b => b.Bridge.Member.StationFromMm).ToArray();
            string Key(LegacyBeamDraft b) { var m = b.Bridge.Member; return $"G{m.Group}-C{m.Segment}"; }
            if (girders.Length == 0 || girders.Any(b => string.IsNullOrWhiteSpace(b.Beam.Name)) || girders.Select(b => b.Beam.Name).Distinct().Count() != girders.Length)
                throw new ArgumentException("Longitudinal FEM beam IDs must be nonempty and unique.");
            var keys = girders.Select(Key).ToArray();
            if (!keys.Distinct().ToHashSet(StringComparer.Ordinal).SetEquals(ids)) throw new ArgumentException("Segment IDs must cover exactly the fabrication segments of the supplied girders.");
            var lookup = ids.Select((key, i) => (key, definition: definitions[i])).ToDictionary(r => r.key, r => r.definition, StringComparer.Ordinal);
            return (girders, keys.Select(k => lookup[k]).ToArray(), keys);
        }

        /// <summary>The check of the reference model: the draft beam has the geometry and the steel stiffness of the analysed beam its state
        /// was classified from (in the definition units of the analysis: its catalog).</summary>
        private static void Reference(BeamElement beam, double angleDeg, ElasticBeamProperty property, (GpcResultCatalog Catalog, GPC.Model.Models.Model Model, BeamElement Beam) source)
        {
            var u = source.Catalog.Units; var s = source.Beam;
            static double Distance(GPC.Geometry.Point3d a, GPC.Geometry.Point3d b) => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y) + (a.Z - b.Z) * (a.Z - b.Z));
            GPC.Geometry.Point3d Def(GPC.Geometry.Point3d p) => new GPC.Geometry.Point3d(u.FromMm(p.X), u.FromMm(p.Y), u.FromMm(p.Z));
            bool Equal(double a, double c) => Math.Abs(a - c) <= Math.Max(1e-9, Math.Max(Math.Abs(a), Math.Abs(c)) * 1e-10);
            var sp = s.BeamProperty as ElasticBeamProperty;
            double sourceAngle = source.Catalog.BeamAngle(s);
            GPC.Geometry.Vector3d Offset(BeamElement e) => e.Assignments.OffsetI ?? new GPC.Geometry.Vector3d(0, 0, 0);
            var so = Offset(s); var bo = Offset(beam);
            if (sp == null || Distance(Def(s.StartPoint), beam.StartPoint) > 1e-5 || Distance(Def(s.EndPoint), beam.EndPoint) > 1e-5
                || u.FromMm(so.X) != bo.X || u.FromMm(so.Y) != bo.Y || sourceAngle != angleDeg
                || !Equal(sp.Section.Area, u.ToMm2(property.Section.Area)) || !Equal(sp.Section.J11, u.ToMm4(property.Section.J11)) || !Equal(sp.Section.J22, u.ToMm4(property.Section.J22))
                || !Equal(sp.Material.ElasticModulusTension, property.Material.ElasticModulusTension) || !Equal(sp.Section.Jt, u.ToMm4(property.Section.Jt))
                || Math.Abs(sp.Material.Density - property.Material.Density) > 1e-20)
                throw new ArgumentException($"Beam {beam.Name}: reference geometry or steel stiffness changed. Recalculate model 0.");
        }
    }
}
