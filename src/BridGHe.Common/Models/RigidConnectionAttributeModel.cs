using System;
using System.Drawing;
using System.Runtime.Serialization;
using Rhino.Display;
using Rhino.Geometry;

namespace BridGHe.Common.Models;

/// <summary>Full spatial kinematic rigid link from the owning node to Target; no penalty stiffness.</summary>
[Serializable]
public sealed class RigidConnectionAttributeModel : AttributeModel<Point3d>
{
    public RigidConnectionAttributeModel(Point3d target) : base(target)
    { if (!target.IsValid) throw new ArgumentException("Invalid rigid connection target."); }
    private RigidConnectionAttributeModel(SerializationInfo info, StreamingContext context) : base(info, context)
    { Value = (Point3d)info.GetValue("Target", typeof(Point3d)); }
    public override void GetObjectData(SerializationInfo info, StreamingContext context)
    { base.GetObjectData(info, context); info.AddValue("Target", Value); }
    public override string Description => "Spatial rigid link";
    public override bool Equals(object obj) => obj is RigidConnectionAttributeModel other && Value == other.Value;
    public override int GetHashCode() => Value.GetHashCode();
    public override void Draw(DisplayPipeline display, RhinoViewport viewport, Color color, ElementModel element)
    { if (element is NodeModel n) display.DrawLine(n.Position, Value, Color.MediumPurple, 3); }
}
