using System;
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
    public override IGH_Goo Duplicate() => new BridgeWorkflowGoo(IsValid ? ModelArchive.Clone(Value) : null);
    public override bool Write(GH_IWriter writer)
    {
        if (!IsValid) return false;
        writer.SetByteArray("Workflow", ModelArchive.Write(Value)); return true;
    }
    public override bool Read(GH_IReader reader)
    {
        Value = null;
        if (!reader.ItemExists("Workflow")) return false;
        Value = ModelArchive.Read<object>(reader.GetByteArray("Workflow")); return IsValid;
    }
}
