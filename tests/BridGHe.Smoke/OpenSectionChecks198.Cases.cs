using System;
using System.Collections.Generic;
using System.Linq;
using BridGHe.Common.Gpc;
using BridGHe.Common.Gpc.Builders;
using BridGHe.Common.Gpc.Legacy;
using GPC.Geometry;
using GPC.Model.ElementProperties;

internal static partial class OpenSectionChecks190
{
    private sealed record OpenCase198(string Type,double B,double H,double Tf,double Tw,bool Mirror,double Gap)
    {
        internal string Label=>$"{Type}-{B}x{H}-m{Mirror}-g{Gap}";
        internal int Shape=>Type=="T"?8:Type=="C"?5:9;
    }
    private static OpenCase198[] Cases198()=>new[]
    {
        new OpenCase198("T",140,300,12,8,false,0),new("C",75,200,12,10,false,0),new("Angle",100,150,12,10,false,0),
        new("C",75,200,12,10,true,0),new("C",75,200,12,10,true,10),new("C",75,200,12,10,true,120),
        new("C",75,200,12,10,true,101.96),new("C",75,200,12,10,true,101.99),
        new("Angle",100,150,12,10,true,0),new("Angle",100,150,12,10,true,10),new("Angle",100,150,12,10,true,40),
        new("Angle",100,150,12,10,true,21.83),new("Angle",100,150,12,10,true,21.85),
        new("Angle",150,100,10,12,false,0),new("T",300,80,12,10,false,0),new("C",300,80,12,10,false,0),
        new("T",48,40,18,16,false,0),new("C",40,30,14,12,false,0),new("Angle",40,30,14,12,false,0)
    };
    private static readonly Point3d[] Ends198={new(3000,0,0),new(0,0,3000),new(2000,3000,4000)};
    private static GpcModels.Conversion Model198(OpenCase198 item,DefinitionUnits units,double rotation,int direction,bool loads=true)
    {
        double scale=units==DefinitionUnits.Nmm?1:1000;
        var recipe=Solve(item.Type,item.B/scale,item.Tf/scale,item.H/scale,item.Tw/scale,item.Mirror,item.Gap/scale);
        var property=GpcPropertyBuilders.Beam("198 "+item.Label,5,recipe,GpcFixtures.Steel());
        var end=Ends198[direction];
        var draft=GpcBeamBuilders.New(Point3d.Origin,new Point3d(end.X/scale,end.Y/scale,end.Z/scale),property,null,rotation,3/scale,7/scale,Guid.NewGuid(),"B198");
        if(loads)draft=GpcBeamBuilders.WithPointLoad(draft,GpcCaseBuilders.Case("Q198",false),false,.37,new Vector3d(2,3,5),Guid.NewGuid());
        return LegacyBuildModel.Build("198",new BridgeUnits(units),.001/scale,new List<(Guid,LegacyNodeDraft)>(),new List<(Guid,LegacyBeamDraft)>{(draft.Beam.Guid,draft)},new List<(Guid,LegacyPlateDraft)>(),new List<LegacyCombinationDraft>(),new List<LegacyStageDraft>());
    }
    // Frozen reference: BeamModel.GetLocalAxes at 5ebd9e9:577 (before 190).
    // Independent reference convention documented by the former exporter: horizontal normal Z x beam;
    // for a vertical beam use beam x Y as axis 2. Explicit signed vectors, never sorted inertias alone.
    private static (Vector3d V1,Vector3d V2,Vector3d V3) Frame198(Point3d end,double angle)
    {
        Vector3d Unit(Vector3d v)=>v/v.Length;
        var z=Unit(new Vector3d(end.X,end.Y,end.Z));
        Vector3d x,y;
        if(Math.Abs(z.Z)<1-1e-12){x=Unit(new Vector3d(-z.Y,z.X,0));y=z.CrossProduct(x);}
        else {y=Unit(new Vector3d(-z.Z,0,z.X));x=y.CrossProduct(z);}
        double c=Math.Cos(angle),s=Math.Sin(angle);
        return(x*c+y*s,y*c-x*s,z);
    }
    private static void Vector198(Vector3d expected,Vector3d actual,string label,double tolerance=1e-9)
    {
        Check(actual!=null&&double.IsFinite(actual.X)&&double.IsFinite(actual.Y)&&double.IsFinite(actual.Z)
            &&(expected-actual).Length<=tolerance,label+$": ({actual?.X:R},{actual?.Y:R},{actual?.Z:R}) expected ({expected.X:R},{expected.Y:R},{expected.Z:R})");
    }
    private static (double A,double X,double Y,double Ixx,double Iyy,double Ixy,double Angle) RectangleReference198(OpenCase198 item)
    {
        var r=new List<(double X,double Y,double B,double H)>();
        if(item.Type=="T"){r.Add((0,item.H-item.Tf,item.B,item.Tf));r.Add(((item.B-item.Tw)/2,0,item.Tw,item.H-item.Tf));}
        else {r.Add((0,0,item.B,item.Tf));r.Add((0,item.Tf,item.Tw,item.H-(item.Type=="C"?2:1)*item.Tf));if(item.Type=="C")r.Add((0,item.H-item.Tf,item.B,item.Tf));}
        if(item.Mirror)r.AddRange(r.ToArray().Select(v=>(-item.Gap-v.X-v.B,v.Y,v.B,v.H)));
        double a=r.Sum(v=>v.B*v.H),x=r.Sum(v=>v.B*v.H*(v.X+v.B/2))/a,y=r.Sum(v=>v.B*v.H*(v.Y+v.H/2))/a;
        double ixx=r.Sum(v=>v.B*v.H*(v.H*v.H/12+Math.Pow(v.Y+v.H/2-y,2))),iyy=r.Sum(v=>v.B*v.H*(v.B*v.B/12+Math.Pow(v.X+v.B/2-x,2))),ixy=r.Sum(v=>v.B*v.H*(v.X+v.B/2-x)*(v.Y+v.H/2-y));
        double angle=Math.Abs(ixy)<=1e-12*(ixx+iyy)?(ixx>=iyy?0:-Math.PI/2):-.5*Math.Atan2(2*ixy,ixx-iyy);
        return(a,x,y,ixx,iyy,ixy,angle);
    }
    // Values in N-mm, taken from the immutable GPC property BEFORE preparing or invoking the writer.
    private static double[] Expected198(ElasticBeamProperty p,OpenCase198 item)
    {
        var s=p.Section;double a=s.AngleX1,c=Math.Cos(a),sn=Math.Sin(a),dx=s.ShearCenter.X-s.Centroid.X,dy=s.ShearCenter.Y-s.Centroid.Y;
        double x=s.Centroid.X+(item.Mirror?item.B+item.Gap:0);
        return new[]{s.Area,s.J11,s.J22,s.Jt,c*dx+sn*dy,-sn*dx+c*dy,p.ShearArea1,p.ShearArea2,x,s.Centroid.Y,a};
    }
}
