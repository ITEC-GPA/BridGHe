using System;

namespace BridGHe.Grasshopper.Components.Geometry;

public sealed class BridgeSectionTemplateComponent : BridgeSegmentExcelComponent
{
    public BridgeSectionTemplateComponent() : base("Bridge Section Excel Template", "SectionTemplate") { }
    protected override int? FixedMode => 0;
    public override Guid ComponentGuid => new Guid("a8e240c5-5c1c-4df3-bdc3-077a8de5f012");
}

public sealed class BridgeSectionReadComponent : BridgeSegmentExcelComponent
{
    public BridgeSectionReadComponent() : base("Bridge Read Section Excel", "ReadSections") { }
    protected override int? FixedMode => 1;
    public override Guid ComponentGuid => new Guid("a8e240c5-5c1c-4df3-bdc3-077a8de5f013");
}
