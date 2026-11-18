using System;
using System.Collections.Generic;
using System.Linq;

internal static partial class OpenSectionChecks190
{
    // Option B (D7 of 17/11/2026): J and shear centre of T, C and Angle are numerical, so every single section is compared with the
    // original thresholds of 198, without declarations; only the 15 differences of the pairs against the Straus7 pair stay declared.
    private const int Declared209Count=15;
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
    // D2 (17/11/2026): the two parts of a pair are joined only at points, so J of the pair is twice J of the part and its shear centre is on
    // the axis of symmetry at the height of the shear centre of the part (2C: the centroid). Straus7 gives the pair 5-6 % less J and its
    // centre in the centroid: those stay declared differences. The physical check of a pair is against the single part: J against twice
    // the J of the single section calculated by Straus7, the centre against the shear centre of that Straus7 part moved into the
    // principal axes of the pair, with the thresholds of 198 (5 %, 1 % of the largest dimension). No thin-wall tf/2 - Ybar any more.
    // part: the eleven values of a single section (A, I11, I22, J, SL1, SL2, SA1, SA2, Xbar, Ybar, angle), GPC or Straus7, in its own
    // principal axes; the pair is mirrored about the vertical axis, so only the vertical offset of the centre is carried over.
    private static double[] PartCentre209(double[] part,double pairAngle)
    {
        double a=part[10],dy=Math.Sin(a)*part[4]+Math.Cos(a)*part[5];
        return new[]{Math.Sin(pairAngle)*dy,Math.Cos(pairAngle)*dy};
    }
    private static bool PairCentreMatches209(OpenCase198 item,double[] gpc,double[] strausPart)
    {
        var centre=PartCentre209(strausPart,gpc[10]);double tolerance=Tolerance209(item,4,0);
        return double.IsFinite(gpc[4])&&double.IsFinite(gpc[5])&&double.IsFinite(centre[0])&&double.IsFinite(centre[1])
            &&Math.Abs(gpc[4]-centre[0])<=tolerance&&Math.Abs(gpc[5]-centre[1])<=tolerance;
    }
    private static void PairChecks209(OpenCase198 item,double[] gpc,double[] gpcPart,double[] strausPart)
    {
        double sum=2*strausPart[3];
        Check(PairTorsionMatches209(item,gpc[3],strausPart[3]),
            $"R3 D2 paired torsion versus two independent Straus singles {item.Label}: GPC={gpc[3]:R}, 2*single={sum:R}");
        var centre=PartCentre209(strausPart,gpc[10]);
        Check(PairCentreMatches209(item,gpc,strausPart),
            $"R3 D2 pair shear centre versus the Straus single part {item.Label}: GPC=({gpc[4]:R},{gpc[5]:R}), part=({centre[0]:R},{centre[1]:R}), tolerance={Tolerance209(item,4,0):R}");
        // The GPC pair itself follows D2: J the sum of its two parts, centre at the height of the centre of the part.
        Near(2*gpcPart[3],gpc[3],"D2 GPC pair J twice the J of its part "+item.Label);
        var own=PartCentre209(gpcPart,gpc[10]);
        Near(own[0],gpc[4],"D2 GPC pair shear-centre SL1 at the centre of its part "+item.Label);
        Near(own[1],gpc[5],"D2 GPC pair shear-centre SL2 at the centre of its part "+item.Label);
    }
    private static bool PairTorsionMatches209(OpenCase198 item,double gpcJ,double singleStrausJ)
    {
        double measured=Measurements209.Single(v=>v.Case==(item with {Mirror=false,Gap=0})).Straus[3];
        return singleStrausJ.Equals(measured)&&PairTorsionWithin209(gpcJ,singleStrausJ);
    }
    private static bool PairTorsionWithin209(double gpcJ,double singleStrausJ)
    {
        double sum=2*singleStrausJ;
        return double.IsFinite(gpcJ)&&double.IsFinite(sum)&&Math.Abs(gpcJ-sum)<=.05*Math.Max(1,Math.Abs(sum));
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
                PairChecks209(row.Case,row.Gpc,single.Gpc,single.Straus);
                Check(!PairTorsionMatches209(row.Case,row.Gpc[3],Math.BitIncrement(single.Straus[3])),"209 changed single-section Straus torsion fails "+row.Case.Label);
                // D2: a part centre moved vertically by twice the tolerance fails; so does twice J of the pair against the Straus pair rule.
                double shift=2*Tolerance209(row.Case,4,0),a=single.Straus[10];
                var moved=(double[])single.Straus.Clone();moved[4]+=Math.Sin(a)*shift;moved[5]+=Math.Cos(a)*shift;
                Check(!PairCentreMatches209(row.Case,row.Gpc,moved),"209 D2 pair centre fails with the part centre moved by twice the tolerance "+row.Case.Label);
                double twice=2*single.Straus[3];
                Check(PairTorsionWithin209(twice*1.049,single.Straus[3])&&PairTorsionWithin209(twice*.951,single.Straus[3])
                    &&!PairTorsionWithin209(twice*1.051,single.Straus[3])&&!PairTorsionWithin209(twice*.949,single.Straus[3]),
                    "209 D2 pair torsion keeps the five-percent limit against twice the part "+row.Case.Label);
            }
        }
        Check(outside==Declared209Count&&declared==Declared209Count,$"209 option B: exactly {Declared209Count} measured differences declared, all of the pairs; original thresholds retained");
        Check(Declarations209.All(d=>d.Case.Mirror&&(d.Index==3&&d.Category=="paired-torsion"||(d.Index==4||d.Index==5)&&d.Category=="mirror-shear-centre")),
            "209 option B: no declared difference for a single section, only J and shear centre of the pairs");
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
        Check(Declarations209.Select(d=>(d.Case,d.Index)).Distinct().Count()==Declared209Count&&Measurements209.Select(m=>m.Case).Distinct().Count()==19,"209 reference keys are unique and explicit");
        var normal=Measurements209[0];
        Check(!Evaluate209(normal.Case,3,normal.Gpc[3]*2,normal.Straus[3]).Pass,"209 normal torsion keeps the original five-percent limit");
        Check(!Evaluate209(normal.Case,0,normal.Gpc[0]*1.01,normal.Straus[0]).Pass,"209 normal area keeps the original tolerance");
        // The former declarations of option A (the thick profiles and the C 300x80) are gone: the same limits as any single section.
        foreach(var former in new[]{("T",48d,40d),("C",40d,30d),("Angle",40d,30d),("C",300d,80d)})
        {
            var single=Measurements209.Single(m=>m.Case.Type==former.Item1&&m.Case.B==former.Item2&&m.Case.H==former.Item3&&!m.Case.Mirror);
            var aligned=Align209(single.Gpc,single.Straus);
            Check(!Evaluate209(single.Case,3,aligned[3]*1.06,aligned[3]).Pass&&!Evaluate209(single.Case,5,aligned[5]+.011*Math.Max(single.Case.B,single.Case.H),aligned[5]).Pass,
                "209 option B: the single "+single.Case.Label+" keeps the original thresholds, without a declaration");
        }
        Check(!Evaluate209(Declarations209[0].Case with {Gap=777},3,Declarations209[0].Gpc,Declarations209[0].Straus).Pass,"209 an unmeasured pair is not silently declared");
        Console.WriteLine($"R3 OPTION B REPLAY 209: {checks-failed}/{checks} PASS, {failed} FAIL; {declared} declared (pairs), {outside} outside original thresholds; recorded data only, no native run");
        return failed==0?0:1;
    }
}
