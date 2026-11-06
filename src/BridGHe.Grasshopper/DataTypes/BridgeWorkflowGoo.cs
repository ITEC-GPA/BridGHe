using System;
using System.Linq;
using BridGHe.Common.Checks;
using BridGHe.Common.Helpers;
using GH_IO.Serialization;
using Grasshopper.Kernel.Types;

namespace BridGHe.Grasshopper.DataTypes;

public sealed class BridgeWorkflowGoo : GH_Goo<object>
{
    public BridgeWorkflowGoo() { }
    public BridgeWorkflowGoo(object value) { Value = value; }
    public override bool IsValid => Value is BridgeActionDefinition or BridgePhaseDefinition;
    public override string TypeName => Value is BridgePhaseDefinition ? "BridGHe Phase" : "BridGHe Action";
    public override string TypeDescription => "Numbered construction phase or incremental action, N-mm-MPa; eigenstrain is dimensionless.";
    public override string ToString() => Value?.ToString() ?? "Empty BridGHe workflow object";
    /// <summary>A copy by archive round trip, with the GPC links of the actions carried over (they are not data contracts: the analysed GPC model
    /// of the source and its GPC model, shared by reference as the other GPC links of the GH types; finding 1 of P2T, 5/11: a copied or
    /// internalised action lost them and the consumers fell back to the FEM results silently).</summary>
    public override IGH_Goo Duplicate() => new BridgeWorkflowGoo(IsValid ? WithGpcLinks(ModelArchive.Clone(Value), Value) : null);

    /// <summary>The GPC links of <paramref name="source"/> (an action, or the actions of a phase in their order) on its copy <paramref name="copy"/>.</summary>
    internal static object WithGpcLinks(object copy, object source)
    {
        static BridgeActionDefinition Link(BridgeActionDefinition c, BridgeActionDefinition s) => c with { GpcSource = s.GpcSource, GpcModel = s.GpcModel, GpcBridgeData = s.GpcBridgeData };
        return (copy, source) switch
        {
            (BridgeActionDefinition c, BridgeActionDefinition s) => Link(c, s),
            (BridgePhaseDefinition c, BridgePhaseDefinition s) when c.Actions.Length == s.Actions.Length => c with { Actions = c.Actions.Select((a, i) => Link(a, s.Actions[i])).ToArray() },
            _ => throw new InvalidOperationException("BridgeWorkflowCopyShapeChanged"),
        };
    }
    // R6.1 (§1bis): the workflow objects are references to analysed models (a cache), not persisted; a former payload ("Workflow", BGHA1
    // with the FEM source model) is dropped (Common.Gpc.GpcGooArchive.LegacyCacheMessage): the Bridge Action and Phase components rebuild them.
    public override bool Write(GH_IWriter writer) => true;
    public override bool Read(GH_IReader reader)
    {
        Value = null;
        if (reader.ItemExists("Workflow")) System.Diagnostics.Debug.WriteLine(Common.Gpc.GpcGooArchive.LegacyCacheMessage);
        return false;
    }
}
