using System;
using System.Runtime.Serialization;
using BridGHe.Common.Geometry;
using Rhino.Geometry;

namespace BridGHe.Common.Models;

[Serializable]
public sealed record BridgeMemberMetadata(string PhysicalId, BridgeMemberFamily Family, int Group, int Segment,
    int Span, double StationFrom, double StationTo, Vector3d SectionUp, string GeometryId = "");

/// <summary>Traceable geometry identity; does not change stiffness or export numerical values.</summary>
[Serializable]
public sealed class BridgeMemberAttributeModel : AttributeModel<BridgeMemberMetadata>
{
    public BridgeMemberAttributeModel(BridgeMemberMetadata value) : base(value) { }
    private BridgeMemberAttributeModel(SerializationInfo info, StreamingContext context) : base(info, context)
    { Value = (BridgeMemberMetadata)info.GetValue("BridgeMetadata", typeof(BridgeMemberMetadata)); }
    public override void GetObjectData(SerializationInfo info, StreamingContext context)
    { base.GetObjectData(info, context); info.AddValue("BridgeMetadata", Value); }
    public override string Description => "Bridge physical member";
}
