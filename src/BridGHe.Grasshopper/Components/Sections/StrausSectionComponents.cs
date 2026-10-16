using System;
namespace BridGHe.Grasshopper.Components.Sections
{
    public sealed class StrausCircularSolidSectionComponent : StrausSectionComponent
    {
        protected override string ShapeKey => "CircularSolid";
        public StrausCircularSolidSectionComponent() : base("Straus Circular Solid Section", "S7CircularSolid") { }
        public override Guid ComponentGuid => new Guid("91092dc4-a478-5bae-b104-6d9b79acfa48");
    }
    public sealed class StrausCircularHollowSectionComponent : StrausSectionComponent
    {
        protected override string ShapeKey => "CircularHollow";
        public StrausCircularHollowSectionComponent() : base("Straus Circular Hollow Section", "S7CircularHollow") { }
        public override Guid ComponentGuid => new Guid("52dfbc6c-40ee-5ecc-8de7-a0252a1aa3ca");
    }
    public sealed class StrausRectangleSolidSectionComponent : StrausSectionComponent
    {
        protected override string ShapeKey => "RectangleSolid";
        public StrausRectangleSolidSectionComponent() : base("Straus Rectangle Solid Section", "S7RectangleSolid") { }
        public override Guid ComponentGuid => new Guid("0d17a80e-109b-5a2b-a68c-f0cbacf6f453");
    }
    public sealed class StrausRectangleHollowSectionComponent : StrausSectionComponent
    {
        protected override string ShapeKey => "RectangleHollow";
        public StrausRectangleHollowSectionComponent() : base("Straus Rectangle Hollow Section", "S7RectangleHollow") { }
        public override Guid ComponentGuid => new Guid("5fba2a07-4ad4-54ca-9fe2-8a8350e7e912");
    }
    public sealed class StrausLipChannelSectionComponent : StrausSectionComponent
    {
        protected override string ShapeKey => "LipChannel";
        public StrausLipChannelSectionComponent() : base("Straus Lipped Channel Section", "S7LipChannel") { }
        public override Guid ComponentGuid => new Guid("0fda128e-507c-5de0-81c5-b10ff3c4d34e");
    }
    public sealed class StrausTopHatSectionComponent : StrausSectionComponent
    {
        protected override string ShapeKey => "TopHat";
        public StrausTopHatSectionComponent() : base("Straus Top Hat Section", "S7TopHat") { }
        public override Guid ComponentGuid => new Guid("6c788ba2-2787-5147-a865-e74c3d4ed5cc");
    }
    public sealed class StrausISectionComponent : StrausSectionComponent
    {
        protected override string ShapeKey => "I";
        public StrausISectionComponent() : base("Straus I Section", "S7I") { }
        public override Guid ComponentGuid => new Guid("6ce96dfc-2dda-53ea-9740-b0e2535cd7ad");
    }
    public sealed class StrausTSectionComponent : StrausSectionComponent
    {
        protected override string ShapeKey => "T";
        public StrausTSectionComponent() : base("Straus T Section", "S7T") { }
        public override Guid ComponentGuid => new Guid("5c8a2a20-382c-5006-b890-b14c3082f7bc");
    }
    public sealed class StrausAngleSectionComponent : StrausSectionComponent
    {
        protected override string ShapeKey => "Angle";
        public StrausAngleSectionComponent() : base("Straus Angle Section", "S7Angle") { }
        public override Guid ComponentGuid => new Guid("e970edd5-6cd6-5658-9411-4702026137b2");
    }
    public sealed class StrausZSectionComponent : StrausSectionComponent
    {
        protected override string ShapeKey => "Z";
        public StrausZSectionComponent() : base("Straus Z Section", "S7Z") { }
        public override Guid ComponentGuid => new Guid("8c774db6-43d1-5fab-ac8c-549cf8210962");
    }
    public sealed class StrausTrapezoidSolidSectionComponent : StrausSectionComponent
    {
        protected override string ShapeKey => "TrapezoidSolid";
        public StrausTrapezoidSolidSectionComponent() : base("Straus Trapezoid Solid Section", "S7TrapezoidSolid") { }
        public override Guid ComponentGuid => new Guid("5b2d5d96-8222-56fd-adfe-826b63b78311");
    }
    public sealed class StrausTrapezoidHollowSectionComponent : StrausSectionComponent
    {
        protected override string ShapeKey => "TrapezoidHollow";
        public StrausTrapezoidHollowSectionComponent() : base("Straus Trapezoid Hollow Section", "S7TrapezoidHollow") { }
        public override Guid ComponentGuid => new Guid("4feecffd-8cbb-5053-b52f-34da648bd1ad");
    }
    public sealed class StrausTriangleSolidSectionComponent : StrausSectionComponent
    {
        protected override string ShapeKey => "TriangleSolid";
        public StrausTriangleSolidSectionComponent() : base("Straus Triangle Solid Section", "S7TriangleSolid") { }
        public override Guid ComponentGuid => new Guid("8f7b51fa-7018-5d1c-82f6-b5332a639cb6");
    }
    public sealed class StrausTriangleHollowSectionComponent : StrausSectionComponent
    {
        protected override string ShapeKey => "TriangleHollow";
        public StrausTriangleHollowSectionComponent() : base("Straus Triangle Hollow Section", "S7TriangleHollow") { }
        public override Guid ComponentGuid => new Guid("48a915d4-85f6-5c47-bc49-4c81adf4f1e3");
    }
    public sealed class StrausCruciformSectionComponent : StrausSectionComponent
    {
        protected override string ShapeKey => "Cruciform";
        public StrausCruciformSectionComponent() : base("Straus Cruciform Section", "S7Cruciform") { }
        public override Guid ComponentGuid => new Guid("20f6dc79-24d0-5c9e-a68d-1eed14630ac1");
    }
    public sealed class StrausBglRectangleSectionComponent : StrausSectionComponent
    {
        protected override string ShapeKey => "BglRectangle";
        public StrausBglRectangleSectionComponent() : base("Straus BGL Hollow Rectangle Section", "S7BglRectangle") { }
        public override Guid ComponentGuid => new Guid("8910273b-e5fb-50bd-942c-ece70d0d06dc");
    }
    public sealed class StrausBglISectionComponent : StrausSectionComponent
    {
        protected override string ShapeKey => "BglI";
        public StrausBglISectionComponent() : base("Straus BGL I Section", "S7BglI") { }
        public override Guid ComponentGuid => new Guid("19dd18cb-893d-550f-9e2d-65666acb157c");
    }
    public sealed class StrausBglChannelSectionComponent : StrausSectionComponent
    {
        protected override string ShapeKey => "BglChannel";
        public StrausBglChannelSectionComponent() : base("Straus BGL Channel Section", "S7BglChannel") { }
        public override Guid ComponentGuid => new Guid("79a61eb7-de96-518c-bee7-61fb5ddb2c94");
    }
    public sealed class StrausBglTSectionComponent : StrausSectionComponent
    {
        protected override string ShapeKey => "BglT";
        public StrausBglTSectionComponent() : base("Straus BGL T Section", "S7BglT") { }
        public override Guid ComponentGuid => new Guid("2d9d3954-4d07-5ebb-bbbb-1848890b83ee");
    }
    public sealed class StrausBglAngleSectionComponent : StrausSectionComponent
    {
        protected override string ShapeKey => "BglAngle";
        public StrausBglAngleSectionComponent() : base("Straus BGL Angle Section", "S7BglAngle") { }
        public override Guid ComponentGuid => new Guid("5e172bd8-5f4a-500c-a71b-80c01f48ed2a");
    }
    public sealed class StrausBglBulbSectionComponent : StrausSectionComponent
    {
        protected override string ShapeKey => "BglBulb";
        public StrausBglBulbSectionComponent() : base("Straus BGL Bulb Flat Section", "S7BglBulb") { }
        public override Guid ComponentGuid => new Guid("7b1bdf19-31c2-5b53-9b1b-43ede1385c70");
    }
}
