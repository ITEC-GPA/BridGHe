using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using BridGHe.Common.Gpc;
using BridGHe.Common.Gpc.Builders;
using BridGHe.Common.Gpc.Legacy;
using GPC.Model.ElementProperties;

internal static partial class OpenSectionChecks190
{
    // Independent physical comparison, AFTER the real-file conservation checks. Its tolerances are not
    // the readback tolerances: preserving GPC values and agreement with Straus' calculation are distinct.
    private static void NativeOracle198(string root)
    {
        var rows=new List<object>();
        foreach(var item in Cases198())
        try
        {
            var model=Model198(item,DefinitionUnits.Nmm,0,0,false);
            var p=(ElasticBeamProperty)model.Model.BeamElements.Values.Single().BeamProperty;
            var gpc=Expected198(p,item);
            var native=GpcNativeSections.Resolve(item.Type=="C"?"LipChannel":item.Type,item.Type=="Angle"?new[]{item.B,item.H,item.Tf,item.Tw}:new[]{item.B,item.H,0,item.Tf,item.Tw,0},null,null,null,1,item.Mirror?3:0,new[]{item.Gap,0d}).Section.ReferenceProperties;
            double da=gpc[10]-native[10],c=Math.Cos(da),s=Math.Sin(da);
            var aligned=(double[])native.Clone();
            aligned[1]=native[1]*c*c+native[2]*s*s;aligned[2]=native[1]*s*s+native[2]*c*c;
            aligned[4]=c*native[4]+s*native[5];aligned[5]=-s*native[4]+c*native[5];
            var metrics=new List<object>();
            for(int i=0;i<10;i++)
            {
                if(i==6||i==7)continue; // Areas themselves are never averaged under a general rotation.
                double tolerance=i==4||i==5?.01*Math.Max(item.B,item.H):(i==3?.05:1e-8)*Math.Max(1,Math.Abs(aligned[i]));
                bool pass=double.IsFinite(aligned[i])&&Math.Abs(gpc[i]-aligned[i])<=tolerance;
                Check(pass,$"R3 physical comparison {item.Label}/{PropertyNames198[i]} GPC={gpc[i]:R} Straus={aligned[i]:R} tolerance={tolerance:R}");
                metrics.Add(new{name=PropertyNames198[i],expected=gpc[i],actual=aligned[i],tolerance,pass});
            }
            var cn=ShearCompliance198(native[6],native[7],da);var cg=ShearCompliance198(gpc[6],gpc[7],0);
            double shearScale=Math.Max(Math.Max(Math.Abs(cg.C11),Math.Abs(cg.C22)),Math.Max(Math.Abs(cn.C11),Math.Abs(cn.C22)));
            foreach(var entry in new[]{("C11",cg.C11,cn.C11),("C22",cg.C22,cn.C22),("C12",cg.C12,cn.C12)})
            {
                double tolerance=.05*shearScale;bool pass=double.IsFinite(entry.Item3)&&Math.Abs(entry.Item2-entry.Item3)<=tolerance;
                Check(pass,$"R3 full shear compliance {item.Label}/{entry.Item1} GPC={entry.Item2:R} Straus={entry.Item3:R} tolerance={tolerance:R}");
                metrics.Add(new{name=entry.Item1,expected=entry.Item2,actual=entry.Item3,tolerance,pass});
            }
            rows.Add(new{item.Type,item.Mirror,item.Gap,compatibleTwist=false,gpc,native,relativeAngle=da,complianceGpc=new[]{cg.C11,cg.C22,cg.C12},complianceNativeInGpcAxes=new[]{cn.C11,cn.C22,cn.C12},metrics});
        }
        catch(Exception ex){Check(false,"R3 native calculation "+item.Label+": "+ex);rows.Add(new{item.Label,error=ex.ToString()});}
        File.WriteAllText(Path.Combine(root,"physical-comparison.json"),JsonSerializer.Serialize(rows,new JsonSerializerOptions{WriteIndented=true}));
    }
}
