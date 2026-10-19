using System;
using GPC.Checkers.CompositeBridge;
namespace BridGHe.Grasshopper.Components.BridgeDetails;

public sealed class BridgeGeometryComponent:DetailDefinition<HSectionDimensions>
{
    public BridgeGeometryComponent():base("Bridge Section Geometry","Geometry","H, inclined-web H or box section. Dimensions mm; box TopWidth is EACH top flange.") { }
    public override Guid ComponentGuid=>new Guid("422a7607-2759-4da2-819b-158d2c6f6cb3");
    protected override (string,object,string)[] Fields=>new (string,object,string)[]
    {
        ("SlabWidth",2200.0,"Slab effective width [mm]"),
        ("SlabHeight",250.0,"Slab thickness [mm]"),
        ("WebHeight",1330.0,"Vertical web height [mm]"),
        ("WebThickness",16.0,"Web thickness normal to plate [mm]"),
        ("TopWidth",450.0,"Top flange width, each flange for box [mm]"),
        ("TopThickness",30.0,"Top flange thickness [mm]"),
        ("BottomWidth",600.0,"Bottom flange total width [mm]"),
        ("BottomThickness",40.0,"Bottom flange thickness [mm]"),
        ("SecondBottomEnabled",false,"Enable second bottom plate, H only"),
        ("SecondBottomWidth",600.0,"Second bottom plate width [mm]"),
        ("SecondBottomThickness",20.0,"Second bottom plate thickness [mm]"),
        ("SectionType",0,"H, inclined-web H, or box"),
        ("WebOffset",0.0,"Horizontal web offset [mm]; inward for box"),
        ("WebSpacing",1800.0,"Box web centreline spacing at top [mm]"),
    };
}

public sealed class BridgeRebarRowComponent:DetailDefinition<BridgeRebarRow>
{
    public BridgeRebarRowComponent():base("Bridge Rebar Row","RebarRow","Longitudinal reinforcement row, native gPC. Connect to top or bottom row port.") { }
    public override Guid ComponentGuid=>new Guid("402a895f-b583-41ed-b2a0-b80c1d50732b");
    protected override (string,object,string)[] Fields=>new (string,object,string)[]
    {
        ("Enabled",true,"Enable this reinforcement row"),
        ("Diameter",16.0,"Bar diameter [mm]"),
        ("Pitch",150.0,"Transverse bar pitch [mm]"),
        ("AxisDistance",40.0,"Axis distance from the corresponding slab face [mm]"),
    };
}

public sealed class BridgePhaseComponent:DetailDefinition<BridgePhase>
{
    public BridgePhaseComponent():base("Bridge Section Increment","SectionStep","One native gPC section increment, not a model construction phase. Forces kN, moments kNm, shrinkage microstrain.") { }
    public override Guid ComponentGuid=>new Guid("4660cce7-4aa2-49f7-a929-b85327e0b8d2");
    protected override (string,object,string)[] Fields=>new (string,object,string)[]
    {
        ("Name","Phase","Phase name"),
        ("Active",true,"Include phase in calculation"),
        ("Kind",1,"Steel only, composite, concrete excluded, or shrinkage"),
        ("Reference",0,"Force reference: gross centroid, effective centroid, common elevation"),
        ("ForceKN",0.0,"Axial force increment [kN], native gPC sign"),
        ("MomentKNm",0.0,"Major bending increment [kNm], native gPC sign"),
        ("ShearKN",0.0,"Shear increment [kN]"),
        ("TorsionKNm",0.0,"Torque increment [kNm], only supported for box with Box Options"),
        ("AdditionalConnectionFlow",0.0,"Additional connection shear flow [N/mm]"),
        ("ShrinkageMicrostrain",0.0,"Shrinkage increment [microstrain], contraction negative"),
        ("HomogenizationSource",0,"Phi or modular ratio n"),
        ("Phi",0.0,"Creep coefficient"),
        ("PsiL",1.0,"Creep multiplier for this action"),
        ("N",6.0,"Steel/concrete modular ratio, used only with ModularRatio"),
    };
}

public sealed class BridgeAnalysisOptionsComponent:DetailDefinition<BridgeAnalysisOptions>
{
    public BridgeAnalysisOptionsComponent():base("Bridge Analysis Options","CheckOptions","Native gPC verification options. Supply already factored actions; this component does not generate load combinations.") { }
    public override Guid ComponentGuid=>new Guid("f86ac409-7bb4-4d8e-a62c-8893feebfe31");
    protected override (string,object,string)[] Fields=>new (string,object,string)[]
    {
        ("Standard",1,"gPC standard selector: Ntc2018 or Eurocode4"),
        ("LimitState",1,"Ultimate, rare SLS, quasi-permanent SLS"),
        ("Class4",true,"Effective-width analysis"),
        ("TopFlangeBuckling",true,"Local buckling of top flange"),
        ("BottomFlangeBuckling",true,"Local buckling of bottom flange"),
        ("WebBuckling",true,"Local buckling of web"),
        ("CommonLoadY",0.0,"Common load reference elevation [mm], zero at slab underside"),
        ("GammaM0",1.05,"Steel cross-section factor; user must select applicable value"),
        ("GammaM1",1.1,"Steel stability factor"),
        ("GammaM2",1.25,"Steel connection factor"),
        ("GammaC",1.5,"Concrete factor"),
        ("GammaS",1.15,"Reinforcement factor"),
        ("AlphaCC",0.85,"Concrete strength coefficient"),
        ("ShearEta",1.2,"Shear resistance multiplier"),
    };
}

public sealed class BridgeTransverseReinforcementComponent:DetailDefinition<BridgeTransverseReinforcement>
{
    public BridgeTransverseReinforcementComponent():base("Bridge Transverse Reinforcement","SlabShear","Native gPC longitudinal slab shear, transverse reinforcement and anchorage checks.") { }
    public override Guid ComponentGuid=>new Guid("01f06ff8-ab56-4fb1-b291-983e7635aab8");
    protected override (string,object,string)[] Fields=>new (string,object,string)[]
    {
        ("Enabled",true,"Enable transverse slab reinforcement checks"),
        ("TopDiameter",16.0,"Top transverse bar diameter [mm]"),
        ("TopPitch",150.0,"Top transverse bar longitudinal spacing [mm]"),
        ("BottomDiameter",16.0,"Bottom transverse bar diameter [mm]"),
        ("BottomPitch",150.0,"Bottom transverse bar longitudinal spacing [mm]"),
        ("CotTheta",1.0,"Cotangent of compression strut angle, gPC slab domain 1 to 1.25"),
        ("LeftFlowFraction",0.5,"Fraction of connection flow transferred to left slab side"),
        ("BendingSteelPerMetre",0.0,"Reinforcement required by transverse bending [mm2/m]"),
        ("AnchorageLength",800.0,"Available anchorage length [mm]"),
        ("GoodBond",true,"Good bond conditions"),
        ("LeftConcreteEdge",800.0,"Distance to left concrete edge [mm]"),
        ("RightConcreteEdge",800.0,"Distance to right concrete edge [mm]"),
        ("EdgeUBar",false,"Edge U-bar detailing"),
        ("UBarDiameter",16.0,"Edge U-bar diameter [mm]"),
    };
}

public sealed class BridgeFatigueComponent:DetailDefinition<BridgeFatigueOptions>
{
    public BridgeFatigueComponent():base("Bridge Connection Fatigue","Fatigue","Native gPC stud/flange fatigue parameters. Flow envelope and stress range must come from the fatigue load model.") { }
    public override Guid ComponentGuid=>new Guid("7d5be63e-a554-44f6-a042-2ca037f73f7d");
    protected override (string,object,string)[] Fields=>new (string,object,string)[]
    {
        ("Enabled",true,"Enable fatigue checks"),
        ("MinimumFlow",0.0,"Minimum connection flow [N/mm]"),
        ("MaximumFlow",0.0,"Maximum connection flow [N/mm]"),
        ("EquivalenceFactor",1.0,"Damage equivalence factor, supplied by user"),
        ("DynamicFactor",1.0,"Dynamic amplification, supplied by user"),
        ("TensileFlange",false,"Steel flange in tension"),
        ("FlangeStressRange",0.0,"Flange normal stress range [MPa]"),
        ("GammaFf",1.0,"Fatigue action factor"),
        ("GammaMfStud",1.0,"Stud fatigue resistance factor"),
        ("GammaMfFlange",1.0,"Flange fatigue resistance factor"),
    };
}

public sealed class BridgeBoxOptionsComponent:DetailDefinition<BridgeBoxOptions>
{
    public BridgeBoxOptionsComponent():base("Bridge Box Options","BoxOptions","Native gPC box torsion, distortion and diaphragm checks. No torsion verification is implied for an H girder.") { }
    public override Guid ComponentGuid=>new Guid("2dfc68e3-becc-4b23-948b-35b7b80521a7");
    protected override (string,object,string)[] Fields=>new (string,object,string)[]
    {
        ("Enabled",true,"Enable box torsion and distortion"),
        ("BracingThickness",0.0,"Equivalent upper bracing thickness [mm]; zero means open steel box"),
        ("SpanLength",0.0,"Distortion span [mm]; zero disables distortion analysis"),
        ("DiaphragmSpacing",0.0,"Intermediate diaphragm spacing [mm]"),
        ("DiaphragmKind",0,"Plate or cross bracing"),
        ("DiaphragmThickness",0.0,"Plate diaphragm thickness [mm]"),
        ("BracingArea",0.0,"Area of EACH diagonal [mm2]"),
        ("BracingRadius",0.0,"Minimum diagonal radius of gyration [mm]"),
        ("BracingBucklingFactor",1.0,"Diagonal buckling length factor"),
        ("DistributedTorque",0.0,"Distributed distortion torque [kNm/m]"),
        ("ConcentratedTorque",0.0,"Concentrated distortion torque [kNm]"),
        ("SupportDiaphragmThickness",0.0,"Support diaphragm thickness [mm], zero disables check"),
        ("SupportTorqueKNm",0.0,"Torque transferred to bearings [kNm]"),
        ("BearingSpacing",1800.0,"Transverse bearing spacing [mm]"),
    };
}


