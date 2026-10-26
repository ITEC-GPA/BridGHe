using System;
using System.Runtime.Serialization;
using System.Security.Cryptography;
using BridGHe.Common.Checks;
using BridGHe.Common.Helpers;

namespace BridGHe.Common.Models;

[Serializable]
public sealed record BridgeSectionAssignment(BridgeActionBehavior Behavior, bool ConcreteExcluded, double ModularRatio, string ClassificationCase, string PropertySignature)
{
    /// <summary>Reject later property edits that would leave a stale section-state tag on the beam.</summary>
    public static string Signature(BeamPropertyModel p) => Convert.ToHexString(SHA256.HashData(ModelArchive.Write(new[] {
        p.SectionArea,p.I11,p.I22,p.J,p.Section.Centroid.X,p.Section.Centroid.Y,p.Material.Modulus,p.Material.PoissonRatio,p.Material.Density,(double)p.PropertyType })));
}

/// <summary>Per-beam section state, retained by the solver and copied to each merged action.</summary>
[Serializable]
public sealed class BridgeSectionStateAttributeModel : AttributeModel<BridgeSectionAssignment>
{
    public BridgeSectionStateAttributeModel(BridgeSectionAssignment value) : base(value) { }
    private BridgeSectionStateAttributeModel(SerializationInfo info, StreamingContext context) : base(info, context)
    { Value = (BridgeSectionAssignment)info.GetValue("SectionAssignment", typeof(BridgeSectionAssignment)); }
    public override void GetObjectData(SerializationInfo info, StreamingContext context)
    { base.GetObjectData(info, context); info.AddValue("SectionAssignment", Value); }
    public override string Description => "Bridge section state";
}
