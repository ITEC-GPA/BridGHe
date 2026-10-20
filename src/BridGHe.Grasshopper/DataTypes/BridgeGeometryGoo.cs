using System;
using System.Runtime.Serialization;
using BridGHe.Common.Geometry;
using BridGHe.Common.Helpers;
using GH_IO.Serialization;
using Grasshopper.Kernel.Types;

namespace BridGHe.Grasshopper.DataTypes;

public sealed class BridgeGeometryGoo : GH_Goo<SimpleBridgeGeometry>
{
    public BridgeGeometryGoo() { }
    public BridgeGeometryGoo(SimpleBridgeGeometry value) { Value = value; }
    public override bool IsValid => Value != null && Value.Stations.Count >= 2;
    public override string TypeName => "Bridge Geometry";
    public override string TypeDescription => "Bridge members, physical segments, supports and station frames in mm.";
    public override string ToString() => IsValid ? $"Bridge Geometry: {Value.Members.Count} axes, {Value.Segments.Count} segments" : "Empty Bridge Geometry";
    public override IGH_Goo Duplicate() => new BridgeGeometryGoo(Value == null ? null : ModelArchive.Clone(Value));
    public override bool Write(GH_IWriter writer)
    {
        if (!IsValid) return false;
        writer.SetByteArray("BridgeGeometry", ModelArchive.Write(Value)); return true;
    }
    public override bool Read(GH_IReader reader)
    {
        Value = null;
        if (!reader.ItemExists("BridgeGeometry")) return false;
        try { Value = ModelArchive.Read<SimpleBridgeGeometry>(reader.GetByteArray("BridgeGeometry")); return IsValid; }
        catch (Exception e) when (e is SerializationException or ArgumentException) { return false; }
    }
}
