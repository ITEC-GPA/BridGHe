using System;
using System.Runtime.Serialization;
using BridGHe.Common.Geometry;
using BridGHe.Common.Helpers;
using GH_IO.Serialization;
using Grasshopper.Kernel.Types;

namespace BridGHe.Grasshopper.DataTypes;

public sealed class BridgeDefinitionGoo : GH_Goo<object>
{
    public BridgeDefinitionGoo() { }
    public BridgeDefinitionGoo(object value) { Value = value; }
    public override bool IsValid => Value is BridgeSupportLayout or BridgeGirderLayout or BridgeDeckLayout or BridgeMeshSettings
        or BridgeDiaphragmLayout or BridgeJointSet or BridgeFabricationPlan or BridgeDefinition;
    public override string TypeName => Value?.GetType().Name ?? "Bridge Definition";
    public override string TypeDescription => "Typed bridge geometry definition, in mm. No FEM properties or results.";
    public override string ToString() => Value is BridgeFabricationPlan p ? $"Fabrication: {p.GirderCount} girders, {p.Ranges.Length} segments" : TypeName;
    public override IGH_Goo Duplicate() => new BridgeDefinitionGoo(IsValid ? ModelArchive.Clone(Value) : null);
    public override bool Write(GH_IWriter writer)
    { if (!IsValid) return false; writer.SetByteArray("Definition", ModelArchive.Write(Value)); return true; }
    public override bool Read(GH_IReader reader)
    {
        Value = null; if (!reader.ItemExists("Definition")) return false;
        try { Value = ModelArchive.Read<object>(reader.GetByteArray("Definition")); return IsValid; }
        catch (Exception e) when (e is SerializationException or ArgumentException) { return false; }
    }
    public static IBridgeSegments Segments(IGH_Goo value) => value?.ScriptVariable() as IBridgeSegments
        ?? throw new ArgumentException("Connect Bridge Fabrication Segments (or existing Bridge Geometry).");
}
