using System;
using System.Collections.Generic;
using System.Linq;

internal static partial class OpenSectionChecks190
{
    private sealed record Physical209(bool Pass,bool OriginalPass,double Tolerance,string Classification,Declared209 Reference);
    private static double Tolerance209(OpenCase198 item,int index,double straus)=>index==4||index==5?.01*Math.Max(item.B,item.H):(index==3?.05:1e-8)*Math.Max(1,Math.Abs(straus));
    private static double[] Align209(double[] gpc,double[] native)
    {
        double angle=gpc[10]-native[10],c=Math.Cos(angle),s=Math.Sin(angle);var aligned=(double[])native.Clone();
        aligned[1]=native[1]*c*c+native[2]*s*s;aligned[2]=native[1]*s*s+native[2]*c*c;
        aligned[4]=c*native[4]+s*native[5];aligned[5]=-s*native[4]+c*native[5];
        return aligned;
    }
    private static Physical209 Evaluate209(OpenCase198 item,int index,double gpc,double straus)
    {
        double tolerance=Tolerance209(item,index,straus);
        bool original=double.IsFinite(gpc)&&double.IsFinite(straus)&&Math.Abs(gpc-straus)<=tolerance;
        var reference=Declarations209.SingleOrDefault(d=>d.Case==item&&d.Index==index);
        if(reference==null)return new(original,original,tolerance,"strict-original-tolerance",null);
        // Both coordinator runs gave identical doubles. A declaration never authorizes drift,
        // even when changed numbers would fall inside the original physical tolerance.
        bool unchanged=double.IsFinite(gpc)&&double.IsFinite(straus)&&gpc.Equals(reference.Gpc)&&straus.Equals(reference.Straus);
        return new(unchanged,original,tolerance,reference.Category,reference);
    }
    private static double[] PairCentre209(OpenCase198 item)
    {
        var r=RectangleReference198(item);
        double dy=item.Type=="Angle"?item.Tf/2-r.Y:0;
        return new[]{Math.Sin(r.Angle)*dy,Math.Cos(r.Angle)*dy};
    }
    private static void PairChecks209(OpenCase198 item,double[] gpc,double singleStrausJ)
    {
        double sum=2*singleStrausJ;
        Check(PairTorsionMatches209(item,gpc[3],singleStrausJ),
            $"R3 paired torsion versus two independent Straus singles {item.Label}: GPC={gpc[3]:R}, 2*single={sum:R}");
        var centre=PairCentre209(item);
        Near(centre[0],gpc[4],"R3 analytical mirrored shear-centre SL1 "+item.Label);
        Near(centre[1],gpc[5],"R3 analytical mirrored shear-centre SL2 "+item.Label);
    }
    private static bool PairTorsionMatches209(OpenCase198 item,double gpcJ,double singleStrausJ)
    {
        double measured=Measurements209.Single(v=>v.Case==(item with {Mirror=false,Gap=0})).Straus[3];
        double sum=2*singleStrausJ;
        return double.IsFinite(gpcJ)&&singleStrausJ.Equals(measured)&&Math.Abs(gpcJ-sum)<=.05*Math.Max(1,Math.Abs(sum));
    }
    internal static int Replay209()
    {
        checks=failed=0;int declared=0,outside=0;
        foreach(var row in Measurements209)
        {
            var aligned=Align209(row.Gpc,row.Straus);
            foreach(int i in new[]{0,1,2,3,4,5,8,9})
            {
                var result=Evaluate209(row.Case,i,row.Gpc[i],aligned[i]);
                if(!result.OriginalPass)outside++;
                if(result.Reference!=null)declared++;
                Check(result.Pass,$"209 replay measured {row.Case.Label}/{PropertyNames198[i]}: {result.Classification}");
            }
            if(row.Case.Mirror)
            {
                var single=Measurements209.Single(v=>v.Case==(row.Case with {Mirror=false,Gap=0}));
                PairChecks209(row.Case,row.Gpc,single.Straus[3]);
                Check(!PairTorsionMatches209(row.Case,row.Gpc[3],Math.BitIncrement(single.Straus[3])),"209 changed single-section Straus torsion fails "+row.Case.Label);
            }
        }
        Check(outside==23&&declared==23,"209 exactly 23 measured differences declared; original thresholds retained");
        foreach(var d in Declarations209)
        {
            double gpcChange=Math.Max(1,Math.Abs(d.Gpc))*.001,strausChange=Math.Max(1,Math.Abs(d.Straus))*.001;
            Check(!Evaluate209(d.Case,d.Index,d.Gpc+gpcChange,d.Straus).Pass,"209 changed GPC measurement fails "+d.Case.Label+"/"+PropertyNames198[d.Index]);
            Check(!Evaluate209(d.Case,d.Index,d.Gpc,d.Straus+strausChange).Pass,"209 changed Straus measurement fails "+d.Case.Label+"/"+PropertyNames198[d.Index]);
            Check(!Evaluate209(d.Case,d.Index,Math.BitIncrement(d.Gpc),d.Straus).Pass,"209 one-double-step GPC drift fails "+d.Case.Label+"/"+PropertyNames198[d.Index]);
            Check(!Evaluate209(d.Case,d.Index,d.Gpc,Math.BitIncrement(d.Straus)).Pass,"209 one-double-step Straus drift fails "+d.Case.Label+"/"+PropertyNames198[d.Index]);
        }
        var first=Declarations209[0];
        Check(!Evaluate209(first.Case,first.Index,double.NaN,first.Straus).Pass&&!Evaluate209(first.Case,first.Index,first.Gpc,double.PositiveInfinity).Pass,"209 non-finite declared values fail");
        Check(!Evaluate209(first.Case,first.Index,first.Straus,first.Straus).Pass,"209 changed values fail even when their physical difference disappears");
        Check(Declarations209.Select(d=>(d.Case,d.Index)).Distinct().Count()==23&&Measurements209.Select(m=>m.Case).Distinct().Count()==19,"209 reference keys are unique and explicit");
        var normal=Measurements209[0];
        Check(!Evaluate209(normal.Case,3,normal.Gpc[3]*2,normal.Straus[3]).Pass,"209 normal torsion keeps the original five-percent limit");
        Check(!Evaluate209(normal.Case,0,normal.Gpc[0]*1.01,normal.Straus[0]).Pass,"209 normal area keeps the original tolerance");
        Check(!Evaluate209(Declarations209[0].Case with {Gap=777},3,Declarations209[0].Gpc,Declarations209[0].Straus).Pass,"209 an unmeasured pair is not silently declared");
        Console.WriteLine($"R3 OPTION A REPLAY 209: {checks-failed}/{checks} PASS, {failed} FAIL; {declared} declared, {outside} outside original thresholds; recorded data only, no native run");
        return failed==0?0:1;
    }
}
