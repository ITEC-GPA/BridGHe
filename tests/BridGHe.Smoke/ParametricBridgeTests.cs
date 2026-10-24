using System;
using System.Linq;
using BridGHe.Common.Geometry;
using BridGHe.Common.Models;
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
        var material=new MaterialModel{Kind=MaterialModel.MaterialKind.Concrete,Name="Test C",Modulus=34000,ShearModulus=14000};
        var slab=BridgeSlabGrillage.Create(grillage.Geometry,material,"Test slab");
        Require(slab.Beams.Length==grillage.Strips.Length && slab.Widths.SequenceEqual(grillage.Strips.Select(s=>s.Width)),"Nonuniform tributary lengths exactly match geometry output");
        Require(slab.Beams.All(b=>!b.Attributes.OfType<ElementIdAttributeModel>().Any()),"Global slab FEM numbering is left to assembly, avoiding steel ID collisions");
        for(int i=0;i<slab.Beams.Length;i++)
        {
            var b=slab.Beams[i]; b.GetLocalAxes(false,out _,out var up,out _,out _);
            var meta=b.Attributes.OfType<BridgeMemberAttributeModel>().Single().Value;
            Require(Math.Abs(b.BeamProperty.Section.SectionArea-slab.Widths[i]*input.DeckThickness)<1e-6,"Analytical strip area b*h");
            Require(up*meta.SectionUp>1-1e-10 && b.PointFrom==grillage.Strips[i].Member.Axis.From,"Strip axes and section normal");
        }
        Require(grillage.Geometry.Members.Count>slab.Beams.Length && steel.Members.Count+slab.Beams.Length==grillage.Geometry.Members.Count,"Input geometry preserved");
        Reject(()=>BridgeSlabGrillage.Create(steel,material,"no slab")); Reject(()=>BridgeSlabGrillage.Create(grillage.Geometry,null,"invalid"));
        Console.WriteLine("PARAMETRIC BRIDGE CORE PASS: spans, girders, signed arcs, custom alignment, invalid inputs, nonuniform strips and analytical areas.");
    }
}
