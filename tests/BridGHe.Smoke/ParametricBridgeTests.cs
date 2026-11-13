using System;
using System.Linq;
using BridGHe.Common.Geometry;
using Rhino.Geometry;

internal static class ParametricBridgeTests
{
    internal static void Require(bool condition,string text) { if(!condition) throw new Exception("PARAMETRIC BRIDGE: "+text); }
    private static void Reject(Action action)
    { try { action(); } catch(ArgumentException) { return; } throw new Exception("Expected invalid layout rejection."); }
    internal static void Run()
    {
        BridgeParametricLayoutResult Make(double radius=0,double[] spans=null,double[] offsets=null,Curve curve=null) =>
            BridgeParametricLayout.Create(3,30000,spans,3,3500,offsets,1500,1500,0,0,radius,Point3d.Origin,0,curve);
        var regular=Make();
        Require(regular.Supports.SequenceEqual(new[]{0d,30000,60000,90000}) && regular.Offsets.SequenceEqual(new[]{-3500d,0,3500}),"Regular span/girder stations");
        Require(regular.DeckWidths.SequenceEqual(new[]{5000d,5000}) && regular.BearingTypes.Count(t=>t==0)==1 && regular.BearingTypes.Count(t=>t==1)==3,"Widths and editable bearing proposal");
        foreach(double r in new[]{250000d,-250000})
        {
            var arc=Make(r).Alignment;
            Require(Math.Abs(arc.GetLength()-90000)<1e-6 && Math.Sign(arc.PointAtEnd.Y)==Math.Sign(r),"Arc length and curve handedness");
        }
        var explicitLayout=Make(spans:new[]{20000d,40000},offsets:new[]{-4000d,-1000,2000,5000});
        Require(explicitLayout.Supports.SequenceEqual(new[]{0d,20000,60000}) && explicitLayout.Offsets.Length==4,"Explicit lists override counts");
        using var line=new LineCurve(new Point3d(1,2,3),new Point3d(1,2,90003));
        Require(Make(curve:line).Alignment.PointAtStart==line.PointAtStart,"Custom 3D alignment is copied");
        Reject(()=>Make(spans:new[]{1d,-2})); Reject(()=>Make(offsets:new[]{1d,1})); Reject(()=>Make(1000));
        using var shortCurve=new LineCurve(Point3d.Origin,new Point3d(100,0,0)); Reject(()=>Make(curve:shortCurve));
        var input=SimpleBridgeTests.Input() with { DeckBeamStations=new[]{0d,10000,25000,60000,100000},DeckBeamSpacing=0 };
        var steel=SimpleBridgeBuilder.Build(input); var grillage=BridgeDeckGrillage.Build(input,steel);
        // R6.6 R2a: the strips on a concrete given by its GPC datum alone (the path of P2): the plans of the strips and their drafts as the
        // component builds them; the same assertions on the GPC data of the drafts. C3 (11/11): the real case of R2a, a Custom concrete E 34000
        // with Use Poisson off and G 14000 (R2a turned Use Poisson on to get past the refusal of GpcMaterials.Elastic, fixed by C3).
        var concrete=BridGHe.Common.Gpc.Builders.GpcMaterialBuilders.Material(BridGHe.Common.Gpc.Builders.GpcMaterialBuilders.Custom(1,"Test C",34000,0,0,14000,false,0,0,0));
        var analysis=BridGHe.Common.Gpc.GpcMaterials.Analysis(concrete);
        var slab=BridgeSlabGrillage.Create(grillage.Geometry,null,"Test slab",analysis,concrete);
        var strips=R2aGeneratorFixtures.Drafts(slab.Plans,"slab");
        Require(slab.Beams.Length==0 && strips.Length==grillage.Strips.Length && slab.Widths.SequenceEqual(grillage.Strips.Select(s=>s.Width)),"Nonuniform tributary lengths exactly match geometry output");
        Require(strips.All(b=>b.Records.Labels.Count==0 && b.Records.FirstLabel==null),"Global slab FEM numbering is left to assembly, avoiding steel ID collisions");
        for(int i=0;i<strips.Length;i++)
        {
            var b=strips[i]; var meta=R2aGeneratorFixtures.Member(b);
            Require(Math.Abs(R2aGeneratorFixtures.Elastic(b).Section.Area-slab.Widths[i]*input.DeckThickness)<1e-6,"Analytical strip area b*h");
            var stripMaterial=R2aGeneratorFixtures.Elastic(b).Material; // C3: the explicit G of the concrete on the strip, with the linear elastic law
            Require(stripMaterial.AnalysisData?.BeamShearModulus==14000 && stripMaterial.AnalysisData.StressStrainPairs==null && stripMaterial.AnalysisData.NonlinearResponse==GPC.Model.Materials.MaterialNonlinearResponse.NonlinearElastic,"Strip material keeps the explicit G 14000");
            Require(R2aGeneratorFixtures.Dot(R2aGeneratorFixtures.Up(b),meta.SectionUp)>1-1e-10 && R2aGeneratorFixtures.SamePoint(b.Beam.StartPoint,grillage.Strips[i].Member.Axis.From),"Strip axes and section normal");
        }
        Require(grillage.Geometry.Members.Count>strips.Length && steel.Members.Count+strips.Length==grillage.Geometry.Members.Count,"Input geometry preserved");
        Reject(()=>BridgeSlabGrillage.Create(steel,null,"no slab",analysis,concrete)); Reject(()=>BridgeSlabGrillage.Create(grillage.Geometry,null,"invalid",null,null));
        Reject(()=>BridgeSlabGrillage.Create(grillage.Geometry,null,"invalid",BridGHe.Common.Gpc.GpcMaterials.Analysis(R2aDesignFixtures.Steel()),R2aDesignFixtures.Steel()));
        Console.WriteLine("PARAMETRIC BRIDGE CORE PASS: spans, girders, signed arcs, custom alignment, invalid inputs, nonuniform strips and analytical areas.");
    }
}
