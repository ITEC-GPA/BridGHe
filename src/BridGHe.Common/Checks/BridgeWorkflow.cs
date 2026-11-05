using System;
using System.Runtime.Serialization;
using BridGHe.Common.Models;

namespace BridGHe.Common.Checks;

public enum BridgeActionBehavior { Steel, LongTerm, ShortTerm, Shrinkage }
public enum BridgeSectionState { Steel, Composite, ConcreteExcluded }
public enum BridgeWorkflowMethod { Compatible, LinearHistory }

/// <summary>An increment, not a cumulative load state. Source is read-only and is not retained in the assembled model.</summary>
[DataContract]
public sealed record BridgeActionDefinition
{
    [DataMember] public string Key { get; init; }
    [DataMember] public ModelModel Source { get; init; }
    /// <summary>F6 (a)/(b): the analysed GPC model of <see cref="Source"/> with its results catalog (the analysis attached it to the GH
    /// model), from which Build BridGHe Model merges the action; null for a source analysed before F6 C3 (the FEM results are merged).
    /// Not persisted: the Bridge Action components set it again from their analysed model.</summary>
    [IgnoreDataMember] public GPC.Model.Models.Model GpcSource { get; init; }
    /// <summary>F6 (L11): the GPC model of <see cref="Source"/> (the analysed one when it was analysed, else its conversion), the beams of
    /// the local section states Build BridGHe Model reads. Not persisted, as <see cref="GpcSource"/>.</summary>
    [IgnoreDataMember] public GPC.Model.Models.Model GpcModel { get; init; }
    /// <summary>F6 (L12): the bridge data of the GPC model of <see cref="Source"/> (by beam GUID: the analysed model keeps the GUIDs of its
    /// conversion), with the local section states (Bridge State Override). Not persisted, as <see cref="GpcSource"/>.</summary>
    [IgnoreDataMember] public GPC.Checkers.CompositeBridge.BridgeModelData GpcBridgeData { get; init; }
    [DataMember] public string Case { get; init; }
    [DataMember] public BridgeActionBehavior Behavior { get; init; }
    /// <summary>Concrete eigenstrain increment [dimensionless], contraction negative. Null means absent, zero is an explicit value.</summary>
    [DataMember] public double? Strain { get; init; }
    [DataMember] public string[] BeamIds { get; init; } = Array.Empty<string>();
    [DataMember] public bool PrimaryOnly { get; init; }
    public override string ToString() => $"{Key}: {Behavior}, {Case ?? "no FEM"}, Δεcs={Strain:G6}";
}

[DataContract]
public sealed record BridgePhaseDefinition
{
    [DataMember] public Guid Id { get; init; } = Guid.NewGuid();
    [DataMember] public int Order { get; init; }
    [DataMember] public string Name { get; init; }
    [DataMember] public BridgeSectionState State { get; init; }
    /// <summary>Sequence metadata [days]; no automatic creep/time integration is implied.</summary>
    [DataMember] public double? Day { get; init; }
    [DataMember] public BridgeActionDefinition[] Actions { get; init; } = Array.Empty<BridgeActionDefinition>();
    public override string ToString() => $"{Order} · {Name} · {State} · {Actions.Length} actions";
}

/// <summary>Small persistent recipe: references merged cases, never recursively embeds source models.</summary>
[DataContract]
public sealed record BridgeWorkflow
{
    [DataMember] public BridgeWorkflowMethod Method { get; init; }
    [DataMember] public BridgeWorkflowPhase[] Phases { get; init; } = Array.Empty<BridgeWorkflowPhase>();
}

[DataContract]
public sealed record BridgeWorkflowPhase
{
    [DataMember] public Guid Id { get; init; }
    [DataMember] public int Order { get; init; }
    [DataMember] public string Name { get; init; }
    [DataMember] public BridgeSectionState State { get; init; }
    [DataMember] public double? Day { get; init; }
    [DataMember] public BridgeWorkflowAction[] Actions { get; init; } = Array.Empty<BridgeWorkflowAction>();
}

[DataContract]
public sealed record BridgeWorkflowAction
{
    /// <summary>Only populated by an explicit per-beam FEM state assignment. Null in older archives.</summary>
    [DataMember] public string[] ConcreteExcludedBeamIds { get; init; } = Array.Empty<string>();
    [DataMember] public bool HasLocalSectionStates { get; init; }
    [DataMember] public string Key { get; init; }
    [DataMember] public string SourceName { get; init; }
    [DataMember] public string SourceCase { get; init; }
    [DataMember] public BridgeActionBehavior Behavior { get; init; }
    [DataMember] public string ForceCase { get; init; }
    [DataMember] public string StrainCase { get; init; }
    [DataMember] public double? Strain { get; init; }
    [DataMember] public string[] BeamIds { get; init; } = Array.Empty<string>();
}
