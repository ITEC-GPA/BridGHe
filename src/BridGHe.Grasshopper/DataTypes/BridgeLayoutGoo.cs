using System;
using System.Text.Json;
using BridGHe.Common.Geometry;
using GH_IO.Serialization;
using Grasshopper.Kernel.Types;

namespace BridGHe.Grasshopper.DataTypes;

public sealed class BridgeLayoutGoo : GH_Goo<object>
{
    public BridgeLayoutGoo() { }
    public BridgeLayoutGoo(object value) { Value = value; }
    public override bool IsValid => Value is BridgeSegments or BridgeDiaphragms;
    public override string TypeName => Value is BridgeSegments ? "Bridge Segments" : "Bridge Diaphragms";
    public override string TypeDescription => "Geometry configuration in mm. No FE properties or activation phases.";
    public override string ToString() => Value?.ToString() ?? "Empty bridge layout";
    public override IGH_Goo Duplicate() => new BridgeLayoutGoo(IsValid
        ? JsonSerializer.Deserialize(JsonSerializer.Serialize(Value, Value.GetType()), Value.GetType()) : null);
    public override bool Write(GH_IWriter writer)
    {
        if (!IsValid) return false;
        writer.SetString("Kind", Value.GetType().Name);
        writer.SetString("Layout", JsonSerializer.Serialize(Value, Value.GetType())); return true;
    }
    public override bool Read(GH_IReader reader)
    {
        Value = null; string kind = null, json = null;
        if (!reader.TryGetString("Kind", ref kind) || !reader.TryGetString("Layout", ref json)) return false;
        Type type = kind == nameof(BridgeSegments) ? typeof(BridgeSegments) : kind == nameof(BridgeDiaphragms) ? typeof(BridgeDiaphragms) : null;
        // P2CI (MU-01, ChatGPT task 74): an unknown kind and damaged JSON are said to the file reader (before: false alone); the same result.
        if (type == null) return GpcGooPersistence.NotRead(reader, GpcGooPersistence.Unreadable("bridge layout", "unknown kind '" + kind + "'"));
        try { Value = JsonSerializer.Deserialize(json, type); return IsValid; }
        catch (JsonException x) { return GpcGooPersistence.NotRead(reader, GpcGooPersistence.Unreadable(kind == nameof(BridgeSegments) ? "Bridge Segments" : "Bridge Diaphragms", x.Message)); }
    }
}
