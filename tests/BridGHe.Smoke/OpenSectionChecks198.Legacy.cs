using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using BridGHe.Common.Gpc;
using BridGHe.Common.Gpc.Builders;
using BridGHe.Common.Gpc.Legacy;
using GH_IO.Serialization;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using GPC.Model.ElementProperties;

internal static partial class OpenSectionChecks190
{
    private sealed record HistoricalInput198(string Path,string Sha256,string Units,string Role);
    private static IEnumerable<object> Persistent198(GH_Document doc)
    {
        foreach(var p in doc.Objects.OfType<IGH_Param>().Concat(doc.Objects.OfType<GH_Component>().SelectMany(c=>c.Params.Input)))
        {
            var data=p.GetType().GetProperty("PersistentData")?.GetValue(p);
            if(data==null)continue;
            var all=data.GetType().GetMethod("AllData",new[]{typeof(bool)});
            if(all?.Invoke(data,new object[]{true}) is not IEnumerable values)continue;
            foreach(var goo in values)
            {
                var datum=goo?.GetType().GetProperty("Gpc")?.GetValue(goo);
                if(datum is LegacyBeamProperty or LegacyBeamDraft or GpcModels.Conversion)yield return datum;
            }
        }
    }
    private static IEnumerable<LegacyBeamProperty> Properties198(object datum)
    {
        if(datum is LegacyBeamProperty p)yield return p;
        if(datum is LegacyBeamDraft b)yield return new LegacyBeamProperty(b.Beam.BeamProperty,b.Property);
        if(datum is GpcModels.Conversion c)
            foreach(var pair in c.Report.BeamProperties)
                if(c.Model.BeamProperties.TryGetValue(pair.Key,out var property))yield return new LegacyBeamProperty(property,pair.Value);
    }
    private static IGH_Goo FromGpc198(string type,object value,string kind=null)
    {
        var method=plugin.GetTypes().Single(t=>t.Name==type).GetMethod("FromGpc",BindingFlags.Public|BindingFlags.Static);
        var parameters=method.GetParameters();var args=new object[parameters.Length];args[0]=value;
        for(int i=1;i<args.Length;i++)args[i]=parameters[i].Name=="kind"?kind:null;
        return (IGH_Goo)method.Invoke(null,args);
    }
    private static LegacyBeamProperty Recompute198(LegacyBeamProperty old)
    {
        var r=old.Data.Section;
        using var h=new GpcBuilderChecks("198 old recipe recompute");
        IGH_Goo section;
        if(r.NativeKey!=null)
        {
            // Solve the original native component as well; this is coordinator-only, like loading this payload.
            section=GpcBuilderChecks.Output(h.Run("Straus"+r.NativeKey+"SectionComponent",new[]{0},r.NativeDimensions.Select((v,i)=>(i,(object)v)).ToArray()),0);
        }
        else
        {
            string type=r.Kind==BridgeSectionKind.T?"T":r.Kind==BridgeSectionKind.C?"C":"Angle";
            var input=new List<(int Input,object Value)>{(0,r.B),(1,r.T1),(2,r.D),(3,r.T2)};
            if(type!="T"){input.Add((4,r.Mirror!=LegacySectionRecipe.NoMirror));input.Add((5,r.MirrorGapA));}
            section=GpcBuilderChecks.Output(h.Run("BeamSection"+type+"Component",new[]{0},input.ToArray()),0);
        }
        var material=FromGpc198("GH_Material",((ElasticBeamProperty)old.Property).Material,old.Data.MaterialKind);
        var output=GpcBuilderChecks.Output(h.Run("BeamPropertyComponent",new[]{0},(0,old.Property.Name),(1,5),(2,section),(3,material)),0);
        Check(h.Failures.Count==0,"R4 explicit section/property component solution: "+string.Join(";",h.Failures));
        var result=(LegacyBeamProperty)output.GetType().GetProperty("Gpc").GetValue(output);
        if(r.NativeKey!=null)Check(result.Data.Native!=null&&result.Data.Section.NativeKey==r.NativeKey,"R4 NativeKey remains on its explicit native path");
        return result;
    }
    private static object MigrationNumbers198(LegacyBeamProperty old,LegacyBeamProperty current)
    {
        var r=current.Data.Section;var p=(ElasticBeamProperty)current.Property;
        var item=new OpenCase198(r.Kind.ToString(),r.B,r.D,r.T1,r.T2,r.Mirror!=LegacySectionRecipe.NoMirror,r.MirrorGapA);
        var before=old.Data.Native?.ReferenceProperties;
        var after=current.Data.Native?.ReferenceProperties??Expected198(p,item);
        Check(before?.Length==11&&after.Length==11,"R4 eleven historical and recomputed properties available");
        if(before?.Length!=11)return new{error="Missing historical properties"};
        double deltaAngle=after[10]-before[10],c=Math.Cos(deltaAngle),s=Math.Sin(deltaAngle);
        var aligned=(double[])before.Clone();
        aligned[1]=c*c*before[1]+s*s*before[2];aligned[2]=s*s*before[1]+c*c*before[2];
        aligned[4]=c*before[4]+s*before[5];aligned[5]=-s*before[4]+c*before[5];
        foreach(int i in new[]{0,1,2,8,9})Near(aligned[i],after[i],"R4 geometry invariant after recompute "+PropertyNames198[i]);
        if(r.NativeKey!=null)for(int i=0;i<11;i++)Near(before[i],after[i],"R4 explicit NativeKey preserves "+PropertyNames198[i]);
        var metrics=Enumerable.Range(0,11).Select(i=>new{name=PropertyNames198[i],before=before[i],after=after[i],delta=after[i]-before[i],
            classification=Close198(before[i],after[i],i)?"unchanged":"migration difference; retain original expected value for review"}).ToArray();
        // Derived numerical consequences are reported, not silently accepted as equivalent native results.
        object Response(double[] v)
        {
            var compliance=ShearCompliance198(v[6],v[7],0);
            return new{torsionalCompliance=1/v[3],shearCompliance=new[]{compliance.C11,compliance.C22,compliance.C12},bendingCompliance1=1/v[2],bendingCompliance2=1/v[1]};
        }
        return new{before,after,metrics,deltaAngle,beforeResponse=Response(before),afterResponse=Response(after),responseSource="analytical compliance factors; native responses are qualified separately"};
    }
    private static void Historical198(bool native,string root)
    {
        string manifest=Environment.GetEnvironmentVariable("BRIDGHE_SECTIONS198_LEGACY_MANIFEST")??Path.Combine("tests","BridGHe.Smoke","Fixtures","open-sections-198","inputs.json");
        Check(File.Exists(manifest),"R4 historical input manifest exists");if(!File.Exists(manifest))return;
        var inputs=JsonSerializer.Deserialize<HistoricalInput198[]>(File.ReadAllText(manifest));
        var coverage=new HashSet<string>();var rows=new List<object>();bool hasNativeKey=false;
        foreach(var input in inputs.Where(i=>native||i.Role=="component-case"))
        try
        {
            string path=System.IO.Path.GetFullPath(input.Path);var bytes=File.ReadAllBytes(path);
            Check(Convert.ToHexString(SHA256.HashData(bytes)).Equals(input.Sha256,StringComparison.OrdinalIgnoreCase),"R4 original hash "+path);
            if(!Convert.ToHexString(SHA256.HashData(bytes)).Equals(input.Sha256,StringComparison.OrdinalIgnoreCase))continue;
            using var doc=new GH_Document{Enabled=false};GH_Document.EnableSolutions=false;
            var archive=new GH_Archive();Check(archive.ReadFromFile(path)&&archive.ExtractObject(doc,"Definition"),"R4 open historical document without solution "+path);
            var before=Persistent198(doc).ToArray();
            var text=before.Select(GpcDraftCapture.Text).OrderBy(s=>s,StringComparer.Ordinal).ToArray();
            // Round-trip outputs are test artifacts; source GH/GHX and all frozen expectations remain untouched.
            foreach(string extension in new[]{"gh","ghx"})
            {
                string saved=System.IO.Path.Combine(root,"roundtrip-"+input.Sha256.Substring(0,12)+"-"+System.IO.Path.GetFileNameWithoutExtension(path)+"."+extension);
                var written=new GH_Archive();Check(written.AppendObject(doc,"Definition")&&written.WriteToFile(saved,false,false),"R4 save test copy "+extension);
                using var again=new GH_Document{Enabled=false};var read=new GH_Archive();
                Check(read.ReadFromFile(saved)&&read.ExtractObject(again,"Definition"),"R4 reopen test copy "+extension);
                Check(text.SequenceEqual(Persistent198(again).Select(GpcDraftCapture.Text).OrderBy(s=>s,StringComparer.Ordinal)),"R4 persistent data/model/signatures unchanged before recompute "+extension);
            }
            if(input.Role=="component-case")
            {
                foreach(var c in doc.Objects.OfType<GH_Component>())c.Locked=true;
                var sections=doc.Objects.OfType<GH_Component>().Where(c=>c.GetType().Name=="BeamSectionAngleComponent").ToArray();
                foreach(var c in sections)c.Locked=false;
                // Only the section component runs: all analysis/export/report components stay locked.
                GH_Document.EnableSolutions=true;doc.Enabled=true;doc.NewSolution(false,GH_SolutionMode.Silent);
                Check(sections.Length==1&&sections.All(c=>c.RuntimeMessages(GH_RuntimeMessageLevel.Error).Count==0&&c.Params.Output[0].VolatileData.DataCount==1),"R4 F07 saved section inputs recompute "+path);
                rows.Add(new{path,input.Units,input.Role,persistentValues=before.Length,countsAsInternalizedCoverage=false});
                continue;
            }
            foreach(var old in before.SelectMany(Properties198))
            {
                var r=old.Data.Section;if(r==null||old.Property is not ElasticBeamProperty)continue;
                if(r.NativeKey==null&&r.Kind is not (BridgeSectionKind.T or BridgeSectionKind.C or BridgeSectionKind.Angle))continue;
                if(r.NativeKey!=null&&r.NativeKey is not ("T" or "LipChannel" or "Angle"))continue;
                Check(old.Data.Native!=null,"R4 historical resolved native section present");
                if(r.NativeKey!=null)hasNativeKey=true;
                else coverage.Add(r.Kind+(r.Mirror!=LegacySectionRecipe.NoMirror?"2":"1")+"/"+input.Units);
                var current=Recompute198(old);var repeat=Recompute198(old);
                Check(current.Data.Signature==repeat.Data.Signature,"R4 recompute signature and dedup stable");
                if(r.NativeKey==null)Check(current.Data.Native==null,"R4 standard recipe becomes pure GPC only on recompute");
                var previous=((ElasticBeamProperty)old.Property);var now=(ElasticBeamProperty)current.Property;
                Near(previous.Section.Area,now.Section.Area,"R4 area preserved on recompute");
                var data=GpcDraftArchive.Write(current);var reopened=GpcDraftArchive.ReadBeamProperty(data.Data,data.Records);
                Check(GpcDraftCapture.Text(current)==GpcDraftCapture.Text(reopened),"R4 recomputed property roundtrip retains every value");
                var migration=MigrationNumbers198(old,current);
                rows.Add(new{path,input.Units,input.Role,kind=r.Kind.ToString(),r.NativeKey,r.Mirror,oldSignature=old.Data.Signature,newSignature=current.Data.Signature,oldNative=old.Data.Native?.ReferenceProperties,
                    migration});
            }
            Check(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).Equals(input.Sha256,StringComparison.OrdinalIgnoreCase),"R4 historical source remains byte-identical");
        }
        catch(Exception ex){Check(false,"R4 historical input "+input.Path+": "+ex);rows.Add(new{input.Path,error=ex.ToString()});}
        if(native)
        {
            foreach(string family in new[]{"T1","C1","C2","Angle1","Angle2"})foreach(string unit in new[]{"Nmm","Knm"})
                Check(coverage.Contains(family+"/"+unit),"R4 internalized historical coverage REQUIRED "+family+"/"+unit);
            Check(hasNativeKey,"R4 internalized historical NativeKey coverage REQUIRED");
        }
        File.WriteAllText(System.IO.Path.Combine(root,"legacy-migration.json"),JsonSerializer.Serialize(new{native,coverage,hasNativeKey,rows},new JsonSerializerOptions{WriteIndented=true}));
        if(!native)Console.WriteLine("R4 full internalized Native/NativeKey coverage is required in --sections-190-native; F07 input recompute alone does not qualify it.");
    }
}
