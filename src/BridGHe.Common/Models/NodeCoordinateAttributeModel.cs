using System;
using System.Runtime.Serialization;
using Rhino.Geometry;

namespace BridGHe.Common.Models;

/// <summary>Cartesian frame for nodal restraints, imposed displacements and grounded springs.</summary>
[Serializable]
public sealed class NodeCoordinateAttributeModel : AttributeModel<Plane>
{
    public NodeCoordinateAttributeModel(Plane frame) : base(frame)
    { if (!frame.IsValid) throw new ArgumentException("Invalid nodal coordinate frame."); }
    private NodeCoordinateAttributeModel(SerializationInfo info, StreamingContext context) : base(info, context)
    { Value = (Plane)info.GetValue("Frame", typeof(Plane)); }
    public override void GetObjectData(SerializationInfo info, StreamingContext context)
    { base.GetObjectData(info, context); info.AddValue("Frame", Value); }
    public override string Description => "Nodal restraint coordinate system";
    public override bool Equals(object obj) => obj is NodeCoordinateAttributeModel other && Value == other.Value;
    public override int GetHashCode() => Value.GetHashCode();
}
