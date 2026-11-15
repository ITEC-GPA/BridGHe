using System;
using System.Collections.Generic;
using System.Linq;
using GPC.Model.Models;
using D3 = GPC.Checkers.CompositeBridge;

namespace BridGHe.Common.Gpc.Legacy
{
    /// <summary>
    /// C1: the bridge workflow of a former merged model (BridgeWorkflow: phases, actions on the merged cases) as the GPC recipe
    /// <see cref="D3.BridgeWorkflowDefinition"/> of the model converted from the same archive by <see cref="LegacyModelImport"/>.
    /// Cases by name (the legacy import keeps the former names and derives the case GUIDs from them), beams by their former BeamId
    /// (the source identity of the converted beams). The former merged model does not keep its source phase models: the source of every
    /// FEM action is the model itself and its force case. ψL is not in the former recipe (BridGHe used 1): null.
    /// Per-beam local section states (HasLocalSectionStates): the former merged recipe keeps only the beams whose concrete is excluded
    /// (ConcreteExcludedBeamIds, mapped); their modular ratios stayed on the source phase models, and BridGHe's checks homogenised every
    /// increment by the φ of its behaviour, never by the ratio of the state. No <see cref="D3.BridgeBeamSectionState"/> is created, so the
    /// GPC executor homogenises by φ as well.
    /// </summary>
    public static class LegacyWorkflowImport
    {
        public static D3.BridgeWorkflowDefinition Import(LegacyValue legacy, Model gpc)
        {
            if (legacy == null || !legacy.Is("ModelModel")) throw new ArgumentException("LegacyModelModelExpected");
            if (gpc == null) throw new ArgumentNullException(nameof(gpc));
            var workflow = legacy["BridgeWorkflow"];
            if (workflow.IsNull) throw new ArgumentException("LegacyBridgeWorkflowMissing");
            string fingerprint = gpc.AnalysisFingerprint();
            var beams = gpc.BeamElements.Values.ToDictionary(b => b.Source.OriginalId, b => b.Guid, StringComparer.Ordinal);
            Guid? Case(string name)
            {
                if (name == null) return null;
                return gpc.LoadCases.TryGetValue(name, out var c) ? c.Guid : throw new ArgumentException("LegacyWorkflowCaseNotInModel: " + name);
            }
            Guid[] Beams(LegacyValue ids) => ids.IsNull ? Array.Empty<Guid>() : ids.Items.Select(i =>
                beams.TryGetValue(i.Text ?? "", out var id) ? id : throw new ArgumentException("LegacyWorkflowBeamNotInModel: " + i.Text)).ToArray();
            D3.BridgeWorkflowActionDefinition Action(LegacyValue a)
            {
                string key = a.String("Key");
                var force = Case(a.String("ForceCase"));
                return new D3.BridgeWorkflowActionDefinition
                {
                    Key = key, SourceModelGuid = force.HasValue ? gpc.Guid : Guid.Empty, SourceAnalysisFingerprint = force.HasValue ? fingerprint : "",
                    SourceCaseGuid = force, SourceName = a.String("SourceName") ?? "", Behavior = Parse<D3.BridgeActionBehavior>(a.Enum("Behavior")),
                    ForceCaseGuid = force, StrainCaseGuid = Case(a.String("StrainCase")), Strain = a.Has("Strain") ? a.Double("Strain") : (double?)null,
                    BeamGuids = Beams(a["BeamIds"]), PrimaryOnly = !force.HasValue, ConcreteExcludedBeamGuids = Beams(a["ConcreteExcludedBeamIds"]),
                };
            }
            return new D3.BridgeWorkflowDefinition
            {
                ModelGuid = gpc.Guid, AnalysisFingerprint = fingerprint, Method = Parse<D3.BridgeWorkflowMethod>(workflow.Enum("Method")),
                Phases = workflow.Field("Phases").Items.Select(p => new D3.BridgeWorkflowPhaseDefinition
                {
                    Id = Guid.Parse(p.Field("Id").Text), Order = p.Int("Order"), Name = p.String("Name") ?? "", State = Parse<D3.BridgeWorkflowSectionState>(p.Enum("State")),
                    Day = p.Has("Day") ? p.Double("Day") : (double?)null, Actions = p.Field("Actions").Items.Select(Action).ToArray(),
                }).ToArray(),
            };
        }

        /// <summary>The former enumerations (Steel, LongTerm, ShortTerm, Shrinkage; Steel, Composite, ConcreteExcluded; Compatible, LinearHistory)
        /// have the names of the GPC ones.</summary>
        private static T Parse<T>(string name) where T : struct, Enum =>
            Enum.TryParse<T>(name, false, out var value) && Enum.IsDefined(typeof(T), value) ? value : throw new ArgumentException("LegacyWorkflowEnumValue: " + typeof(T).Name + " " + name);
    }
}
