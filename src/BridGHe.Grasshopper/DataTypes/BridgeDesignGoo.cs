using System;
using System.Runtime.Serialization;
using BridGHe.Common.Geometry;
using BridGHe.Common.Helpers;
using GH_IO.Serialization;
using Grasshopper.Kernel.Types;

namespace BridGHe.Grasshopper.DataTypes;

public sealed class BridgeDesignGoo : GH_Goo<object>
{
    public BridgeDesignGoo() { }
    public BridgeDesignGoo(object value) { Value = value; }
    public override bool IsValid => Value is BridgeSectionRule;
    public override string TypeName => "Bridge Design Definition";
    public override string TypeDescription => "Typed bridge assignment and detailing definitions.";
    public override string ToString() => Value?.ToString() ?? "Empty bridge design definition";
    public override IGH_Goo Duplicate() => new BridgeDesignGoo(IsValid ? ModelArchive.Clone(Value) : null);
    public override bool Write(GH_IWriter writer)
    { if (!IsValid) return false; writer.SetByteArray("Design", ModelArchive.Write(Value)); return true; }
    public override bool Read(GH_IReader reader)
    {
        Value = null;
        if (!reader.ItemExists("Design")) return false;
        try { Value = ModelArchive.Read<object>(reader.GetByteArray("Design")); if (IsValid) return true; Value = null; return false; }
        catch (Exception e) when (e is SerializationException or ArgumentException) { return false; }
    }
}
