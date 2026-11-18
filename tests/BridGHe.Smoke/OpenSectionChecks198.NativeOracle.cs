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
        var rows=new List<object>();int declared=0;
        Console.WriteLine("R3 OPTION B: singles with the original thresholds, without declarations; the 15 differences of the pairs against the Straus7 pair declared.");
        foreach(var item in Cases198())
        try
        {
            var model=Model198(item,DefinitionUnits.Nmm,0,0,false);
            var p=(ElasticBeamProperty)model.Model.BeamElements.Values.Single().BeamProperty;
            var gpc=Expected198(p,item);
            var native=GpcNativeSections.Resolve(item.Type=="C"?"LipChannel":item.Type,item.Type=="Angle"?new[]{item.B,item.H,item.Tf,item.Tw}:new[]{item.B,item.H,0,item.Tf,item.Tw,0},null,null,null,1,item.Mirror?3:0,new[]{item.Gap,0d}).Section.ReferenceProperties;
            double da=gpc[10]-native[10];var aligned=Align209(gpc,native);
            var metrics=new List<object>();
            for(int i=0;i<10;i++)
            {
                if(i==6||i==7)continue; // Areas themselves are never averaged under a general rotation.
                var comparison=Evaluate209(item,i,gpc[i],aligned[i]);
                if(comparison.Reference!=null)declared++;
                Check(comparison.Pass,$"R3 physical comparison {item.Label}/{PropertyNames198[i]} GPC={gpc[i]:R} Straus={aligned[i]:R} originalTolerance={comparison.Tolerance:R} classification={comparison.Classification}");
                metrics.Add(new{name=PropertyNames198[i],expected=gpc[i],actual=aligned[i],tolerance=comparison.Tolerance,pass=comparison.Pass,
                    originalPass=comparison.OriginalPass,classification=comparison.Classification,declared=comparison.Reference!=null,
                    recordedGpc=comparison.Reference?.Gpc,recordedStraus=comparison.Reference?.Straus,
                    delta=gpc[i]-aligned[i],ratio=aligned[i]==0?(double?)null:gpc[i]/aligned[i]});
            }
            double? singleStrausJ=null;double[] singleStraus=null,singleGpc=null,partPairCentre=null;
            if(item.Mirror)
            {
                // D2: the single part, calculated by Straus7 (the same resolver, no mirror) and by GPC (the same component, no mirror).
                singleStraus=GpcNativeSections.Resolve(item.Type=="C"?"LipChannel":item.Type,item.Type=="Angle"?new[]{item.B,item.H,item.Tf,item.Tw}:new[]{item.B,item.H,0,item.Tf,item.Tw,0},null,null,null,1,0,new[]{0d,0d}).Section.ReferenceProperties;
                var part=item with {Mirror=false,Gap=0};
                singleGpc=Expected198((ElasticBeamProperty)Model198(part,DefinitionUnits.Nmm,0,0,false).Model.BeamElements.Values.Single().BeamProperty,part);
                singleStrausJ=singleStraus[3];
                PairChecks209(item,gpc,singleGpc,singleStraus);partPairCentre=PartCentre209(singleStraus,gpc[10]);
            }
            var cn=ShearCompliance198(native[6],native[7],da);var cg=ShearCompliance198(gpc[6],gpc[7],0);
            double shearScale=Math.Max(Math.Max(Math.Abs(cg.C11),Math.Abs(cg.C22)),Math.Max(Math.Abs(cn.C11),Math.Abs(cn.C22)));
            foreach(var entry in new[]{("C11",cg.C11,cn.C11),("C22",cg.C22,cn.C22),("C12",cg.C12,cn.C12)})
            {
                double tolerance=.05*shearScale;bool pass=double.IsFinite(entry.Item3)&&Math.Abs(entry.Item2-entry.Item3)<=tolerance;
                Check(pass,$"R3 full shear compliance {item.Label}/{entry.Item1} GPC={entry.Item2:R} Straus={entry.Item3:R} tolerance={tolerance:R}");
                metrics.Add(new{name=entry.Item1,expected=entry.Item2,actual=entry.Item3,tolerance,pass});
            }
            rows.Add(new{item.Type,item.B,item.H,item.Tf,item.Tw,item.Mirror,item.Gap,compatibleTwist=false,gpc,native,relativeAngle=da,
                singleStrausJ,twiceSingleStrausJ=2*singleStrausJ,singleStraus,singleGpc,partPairCentre,
                complianceGpc=new[]{cg.C11,cg.C22,cg.C12},complianceNativeInGpcAxes=new[]{cn.C11,cn.C22,cn.C12},metrics});
        }
        catch(Exception ex){Check(false,"R3 native calculation "+item.Label+": "+ex);rows.Add(new{item.Label,error=ex.ToString()});}
        Check(declared==Declared209Count,$"R3 option B contains exactly the {Declared209Count} declared differences of the pairs");
        File.WriteAllText(Path.Combine(root,"physical-comparison.json"),JsonSerializer.Serialize(rows,new JsonSerializerOptions{WriteIndented=true}));
        File.WriteAllText(Path.Combine(root,"r3-option-b-summary.json"),JsonSerializer.Serialize(new{option="B (decisions D2 and D7 of 17/11/2026)",declared,
            reference="Measurements209 and Declarations209 (OpenSectionChecks209.Reference.cs)",measurementGuard="exact finite doubles; signed zero equivalent",
            physicalTolerances="unchanged; no declaration for a single section",pairedTorsion="D2: against twice the J of the single part calculated by Straus7, 5 %",
            pairShearCentre="D2: against the shear centre of the Straus7 single part, in the axes of the pair, 1 % of the largest dimension",
            strausPairConvention="J and centre of the Straus7 pair recorded as the 15 declared differences"},new JsonSerializerOptions{WriteIndented=true}));
    }
}
