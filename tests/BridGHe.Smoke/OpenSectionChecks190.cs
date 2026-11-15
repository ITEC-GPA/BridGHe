using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BridGHe.Common.Gpc;
using BridGHe.Common.Gpc.Builders;
using BridGHe.Common.Gpc.Legacy;
using GPC.Converter.Straus7;
using GPC.Geometry;
using GPC.Model.ElementProperties;
using GPC.Model.Sections;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Parameters;

internal static class OpenSectionChecks190
{
    private static int checks,failed;
    private static Assembly plugin;
    private static void Check(bool ok,string label){checks++;if(!ok)failed++;Console.WriteLine((ok?"PASS: ":"FAIL: ")+label);}
    private static void Near(double expected,double actual,string label)=>Check(double.IsFinite(actual)&&Math.Abs(expected-actual)<=1e-9*Math.Max(1,Math.Abs(expected)),label+$": {actual:R}, expected {expected:R}");
    private static LegacySectionRecipe Solve(string type,double b,double tf,double d,double tw,bool mirrored,double gap)
    {
        using var doc=new GH_Document();
        var c=(GH_Component)Activator.CreateInstance(plugin.GetType("BridGHe.Grasshopper.Components.Sections.BeamSection"+type+"Component",true));
        c.CreateAttributes();doc.AddObject(c,false);
        var values=new[]{b,tf,d,tw};
        for(int i=0;i<4;i++){var p=(Param_Number)c.Params.Input[i];p.PersistentData.Clear();p.SetPersistentData(values[i]);}
        if(c.Params.Input.Count>4){var p=(Param_Boolean)c.Params.Input[4];p.PersistentData.Clear();p.SetPersistentData(mirrored);var g=(Param_Number)c.Params.Input[5];g.PersistentData.Clear();g.SetPersistentData(gap);}
        GH_Document.EnableSolutions=true;doc.Enabled=true;doc.NewSolution(false,GH_SolutionMode.Silent);
        Check(c.RuntimeMessages(GH_RuntimeMessageLevel.Error).Count==0,type+": real GH component solves");
        var goo=c.Params.Output[0].VolatileData.AllData(true).Single(g=>g!=null&&g.IsValid);
        return (LegacySectionRecipe)goo.GetType().GetProperty("Gpc").GetValue(goo);
    }

    internal static int Run()
    {
        checks=failed=0;
        // A safe negative test on AW: no resolver is called if the pure mappings do not exist yet.
        foreach(var kind in new[]{BridgeSectionKind.T,BridgeSectionKind.C,BridgeSectionKind.Angle})
        {
            try{Check(GpcSections.Standard(kind,"190",300,140,0,0,0,12,8,0)!=null,"pure mapping "+kind);}
            catch(NotSupportedException){Check(false,"pure mapping "+kind+" absent; stop before any native resolver");}
        }
        if(failed!=0)return Finish();
        plugin=BridgeMergeTests.Initialize();
        foreach(var item in new[]{("T",false,0d),("C",false,0d),("C",true,0d),("C",true,10d),("Angle",false,0d),("Angle",true,0d),("Angle",true,10d)})
        foreach(var units in new[]{DefinitionUnits.Nmm,DefinitionUnits.Knm})
        {
            string type=item.Item1;bool mirror=item.Item2;double gap=item.Item3;
            double b=type=="T"?140:type=="C"?75:100,tf=12,d=type=="T"?300:type=="C"?200:150,tw=type=="T"?8:10;
            double scale=units==DefinitionUnits.Nmm?1:1000;
            string label=$"{type}, mirror={mirror}, gap={gap}, {units}";
            try
            {
                var recipe=Solve(type,b/scale,tf/scale,d/scale,tw/scale,mirror,gap/scale);
                var property=GpcPropertyBuilders.Beam("190 "+label,5,recipe,GpcFixtures.Steel());
                Check(property.Data.Native==null,label+": properties computed without native resolver/override");
                Check(((ElasticBeamProperty)property.Property).Section is SectionT or SectionC or SectionL or SectionBuiltUp,label+": typed Model section");
                var values=GpcSectionGeometry.Former(recipe);
                Near(((ElasticBeamProperty)property.Property).Section.Area,values.Area,label+": displayed area equals Model");
                Near(((ElasticBeamProperty)property.Property).Section.Jt,values.J,label+": displayed torsion equals Model");
                var bytes=GpcDraftArchive.Write(property);var archived=GpcDraftArchive.ReadBeamProperty(bytes.Data,bytes.Records);
                Check(archived.Data.Native==null&&archived.Data.Signature==property.Data.Signature,label+": property archive round trip");
                foreach(double rotation in new[]{0d,37d})
                {
                    var draft=GpcBeamBuilders.New(new Point3d(0,0,0),new Point3d(3000/scale,0,0),property,null,rotation,3/scale,7/scale,Guid.NewGuid(),"beam190");
                    if(units==DefinitionUnits.Nmm)
                    {
                        var solids=GpcDraftShapes.Beam(draft);
                        Check(solids.Length==(mirror?2:1)&&solids.All(v=>v.IsSolid),label+": solid preview without native resolver");
                        double volume=solids.Sum(v=>{using var mass=Rhino.Geometry.VolumeMassProperties.Compute(v);return mass.Volume;});
                        Near(((ElasticBeamProperty)property.Property).Section.Area*3000,volume,label+": preview volume A*L");
                        foreach(var solid in solids)solid.Dispose();
                    }
                    var model=LegacyBuildModel.Build("190",new BridgeUnits(units),.001/scale,new List<(Guid,LegacyNodeDraft)>(),new List<(Guid,LegacyBeamDraft)>{(draft.Beam.Guid,draft)},new List<(Guid,LegacyPlateDraft)>(),new List<LegacyCombinationDraft>(),new List<LegacyStageDraft>());
                    var beam=model.Model.BeamElements.Values.Single();var section=((ElasticBeamProperty)beam.BeamProperty).Section;
                    Check(model.Report.NativeSections.Count==0,label+": normalisation retains pure GPC path");
                    Analytical(type,b,tf,d,tw,mirror,gap,section,label);
                    Axes(beam,section,rotation+(type=="Angle"&&mirror?90:0),label);
                    var log=Record(model);var offsets=(double[])log.Calls.Single(v=>v.Name=="SetBeamOffset").Args[1];
                    Near(3,offsets[0],label+": native offset1");Near(7,offsets[1],label+": native offset2");
                    var geometry=log.Calls.Where(c=>c.Name==(mirror?"SetNativeSection":"SetBeamSectionGeometry")).ToArray();
                    Check(geometry.Length==1&&!log.Calls.Any(c=>c.Name=="SetBeamSectionMesh"),label+": direct native shape, no BXS");
                    if(geometry.Length==1)
                    {
                        int shape=type=="T"?8:type=="C"?5:9;
                        if(mirror){var native=(Straus7SectionDefinition)geometry[0].Args[1];Check(native.Shape==shape&&native.MirrorType==3&&native.MirrorGaps.SequenceEqual(new[]{gap,0d})&&!native.CompatibleTwist,label+": native mirror and gap");}
                        else Check((int)geometry[0].Args[1]==shape&&((double[])geometry[0].Args[2]).SequenceEqual(new[]{b,d,0,tf,tw,0}),label+": native dimensions");
                    }
                    if(type=="Angle"&&mirror&&units==DefinitionUnits.Nmm&&rotation==37)
                    {
                        var single=GpcPropertyBuilders.Beam("single",5,GpcSectionBuilders.Standard(BridgeSectionKind.C,200,75,t1:12,t2:10),GpcFixtures.Steel());
                        var changed=GpcModelTransforms.PhaseModel(model,"remove angle turn",new[]{beam.Name},single,false,Guid.NewGuid());
                        var newBeam=changed.Model.BeamElements.Values.Single();Axes(newBeam,((ElasticBeamProperty)newBeam.BeamProperty).Section,37,"property replacement removes legacy turn");
                    }
                }
            }
            catch(Exception e){Check(false,label+": "+e);}
        }
        foreach(var invalid in new[]{(0d,8d,0d),(12d,0d,0d),(12d,8d,-1d)})
        {
            try{GpcPropertyBuilders.Beam("invalid",5,GpcSectionBuilders.Standard(BridgeSectionKind.C,200,75,t1:invalid.Item1,t2:invalid.Item2,mirror:2,gapA:invalid.Item3),GpcFixtures.Steel());Check(false,"invalid dimensions/gap refused");}
            catch(ArgumentException){Check(true,"invalid dimensions/gap refused");}catch(NotSupportedException){Check(true,"invalid dimensions/gap refused");}
        }
        return Finish();
    }
    // Coordinator only, under the native lock. Never called by Run or any default regression.
    internal static int Native()
    {
        if(Environment.GetEnvironmentVariable("BRIDGHE_SECTIONS190_NATIVE")!="1")
        {Console.WriteLine("Set BRIDGHE_SECTIONS190_NATIVE=1 under the coordinator native lock.");return 2;}
        checks=failed=0;plugin=BridgeMergeTests.Initialize();
        var rows=new List<object>();
        foreach(var item in new[]{("T",false,0d),("C",false,0d),("C",true,0d),("C",true,10d),("Angle",false,0d),("Angle",true,0d),("Angle",true,10d)})
        {
            string type=item.Item1;bool mirror=item.Item2;double gap=item.Item3;
            double b=type=="T"?140:type=="C"?75:100,tf=12,d=type=="T"?300:type=="C"?200:150,tw=type=="T"?8:10;
            var recipe=Solve(type,b,tf,d,tw,mirror,gap);
            var property=GpcPropertyBuilders.Beam("native190",5,recipe,GpcFixtures.Steel());
            var draft=GpcBeamBuilders.New(new Point3d(0,0,0),new Point3d(3000,0,0),property,null,0,0,0,Guid.NewGuid(),"native190");
            var model=LegacyBuildModel.Build("native190",new BridgeUnits(DefinitionUnits.Nmm),.001,new List<(Guid,LegacyNodeDraft)>(),new List<(Guid,LegacyBeamDraft)>{(draft.Beam.Guid,draft)},new List<(Guid,LegacyPlateDraft)>(),new List<LegacyCombinationDraft>(),new List<LegacyStageDraft>());
            var call=Record(model).Calls.Single(v=>v.Name==(mirror?"SetNativeSection":"SetBeamSectionGeometry"));
            var gpc=(double[])call.Args[mirror?2:3];
            var native=GpcNativeSections.Resolve(type=="C"?"LipChannel":type,type=="Angle"?new[]{b,d,tf,tw}:new[]{b,d,0,tf,tw,0},null,null,null,1,mirror?3:0,new[]{gap,0d}).Section.ReferenceProperties;
            double da=gpc[10]-native[10],c=Math.Cos(da),sn=Math.Sin(da);
            var aligned=(double[])native.Clone();
            aligned[1]=native[1]*c*c+native[2]*sn*sn;aligned[2]=native[1]*sn*sn+native[2]*c*c;
            aligned[4]=c*native[4]+sn*native[5];aligned[5]=-sn*native[4]+c*native[5];
            aligned[6]=native[6]*c*c+native[7]*sn*sn;aligned[7]=native[6]*sn*sn+native[7]*c*c;
            var metrics=new List<object>();
            string[] names={"A","I11","I22","J","SL1","SL2","SA1","SA2","Xbar","Ybar"};
            for(int i=0;i<10;i++)
            {
                double tolerance=i==4||i==5?.01*Math.Max(b,d):(i==3||i==6||i==7?.05:1e-8)*Math.Max(1,Math.Abs(aligned[i]));
                bool pass=Math.Abs(gpc[i]-aligned[i])<=tolerance;
                Check(pass,$"native {type} mirror={mirror} gap={gap}: {names[i]}, GPC={gpc[i]:R}, Straus={aligned[i]:R}, tolerance={tolerance:R}");
                metrics.Add(new {name=names[i],gpc=gpc[i],straus=aligned[i],delta=gpc[i]-aligned[i],absoluteTolerance=tolerance,pass});
            }
            rows.Add(new {type,mirror,gap,compatibleTwist=false,gpc,native,metrics});
        }
        string output=System.IO.Path.Combine("artifacts","190","native-open-comparison.json");System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(output));
        System.IO.File.WriteAllText(output,System.Text.Json.JsonSerializer.Serialize(rows,new System.Text.Json.JsonSerializerOptions{WriteIndented=true}));
        return Finish();
    }

    private static int Finish(){Console.WriteLine($"OPEN SECTIONS 190: {checks-failed}/{checks} PASS, {failed} FAIL");return failed==0?0:1;}
    private static void Analytical(string type,double b,double tf,double d,double tw,bool mirror,double gap,ISectionShape s,string label)
    {
        var r=new List<(double X,double Y,double B,double H)>();
        if(type=="T"){r.Add((0,d-tf,b,tf));r.Add(((b-tw)/2,0,tw,d-tf));}
        else {r.Add((0,0,b,tf));r.Add((0,tf,tw,d-(type=="C"?2:1)*tf));if(type=="C")r.Add((0,d-tf,b,tf));}
        if(mirror)r.AddRange(r.ToArray().Select(v=>(-gap-v.X-v.B,v.Y,v.B,v.H)));
        double a=r.Sum(v=>v.B*v.H),x=r.Sum(v=>v.B*v.H*(v.X+v.B/2))/a,y=r.Sum(v=>v.B*v.H*(v.Y+v.H/2))/a;
        Near(a,s.Area,label+": analytical A");Near(x,s.Centroid.X,label+": Xbar");Near(y,s.Centroid.Y,label+": Ybar");
        Near(r.Sum(v=>v.B*v.H*(v.H*v.H/12+Math.Pow(v.Y+v.H/2-y,2))),s.Jxx,label+": Ixx");
        Near(r.Sum(v=>v.B*v.H*(v.B*v.B/12+Math.Pow(v.X+v.B/2-x,2))),s.Jyy,label+": Iyy");
        Check(Math.Abs(r.Sum(v=>v.B*v.H*(v.X+v.B/2-x)*(v.Y+v.H/2-y))-s.Jxy)<=1e-9*Math.Max(s.Jxx,s.Jyy),label+": Ixy (inertia scale)");
    }
    private static void Axes(GPC.Model.Elements.BeamElement beam,ISectionShape section,double degrees,string label)
    {
        var frame=beam.Assignments.SectionAxes;double c=Math.Cos(section.AngleX1),s=Math.Sin(section.AngleX1);
        var v1=frame.V1*c+frame.V2*s;var v2=frame.V2*c-frame.V1*s;double angle=degrees*Math.PI/180;
        Near(0,v1.X,label+": principal x");Near(Math.Cos(angle),v1.Y,label+": principal y");Near(Math.Sin(angle),v1.Z,label+": principal z");
        Near(-Math.Sin(angle),v2.Y,label+": axis2 y");Near(Math.Cos(angle),v2.Z,label+": axis2 z");
    }
    public class Recorder:DispatchProxy
    {
        public readonly List<(string Name,object[] Args)> Calls=new();
        protected override object Invoke(MethodInfo method,object[] args){Calls.Add((method.Name,args));return method.ReturnType==typeof(void)?null:method.ReturnType.IsValueType?Activator.CreateInstance(method.ReturnType):null;}
    }
    private static Recorder Record(GpcModels.Conversion model)
    {
        var writer=DispatchProxy.Create<IC1Straus7Recording,Recorder>();
        Straus7ModelWritePlan.Prepare(model.Model,null,null,model.Report.NativeSections).Write(writer,model.Model);
        return (Recorder)(object)writer;
    }
}
