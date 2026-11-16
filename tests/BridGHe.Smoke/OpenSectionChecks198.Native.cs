using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using BridGHe.Common.Gpc;
using BridGHe.Common.Gpc.Builders;
using BridGHe.Common.Gpc.Legacy;
using BridGHe.Common.Straus;
using GPC.Converter.Straus7;
using GPC.Geometry;
using GPC.Model.ElementProperties;

internal static partial class OpenSectionChecks190
{
    private static readonly string[] PropertyNames198={"A","I11","I22","J","SL1","SL2","SA1","SA2","Xbar","Ybar","angle"};
    // Build Model also adds Dead; use the writer's numbering for the loaded case.
    private static int PointLoadCase198(Straus7ModelWritePlan plan,GpcModels.Conversion model)=>plan.LoadCaseIds[model.Model.LoadCases["Q198"].Guid];
    private static bool Close198(double expected,double actual,int index)=>double.IsFinite(actual)&&Math.Abs(expected-actual)<=
        (index==10?1e-7:1e-8*Math.Max(1,Math.Abs(expected)));
    private static bool ReadbackMatches198(OpenCase198 item,double[] expected,int shape,double[] dimensions,int mirror,double[] gaps,bool twist,double[] actual)
    {
        if(shape!=item.Shape||dimensions==null||dimensions.Length!=6||mirror!=(item.Mirror?3:0)||twist||gaps==null||gaps.Length!=2||actual==null||actual.Length!=11)return false;
        double[] dims={item.B,item.H,0,item.Tf,item.Tw,0};
        return dimensions.Zip(dims,(a,b)=>Close198(b,a,0)).All(x=>x)&&Close198(item.Mirror?item.Gap:0,gaps[0],0)&&Close198(0,gaps[1],0)
            &&actual.Select((v,i)=>Close198(expected[i],v,i)).All(x=>x);
    }
    private static void ReadbackNegative198()
    {
        var item=new OpenCase198("Angle",100,150,12,10,true,40);
        double[] expected={4980,15508000,11629850.233,205440,13,17,2300,1700,140,47,-Math.PI/2};
        double[] dims={100,150,0,12,10,0},gaps={40,0};
        bool Accept(double[] values,int shape=9,int mirror=3,double[] geometry=null,double[] separation=null,bool twist=false)=>ReadbackMatches198(item,expected,shape,geometry??dims,mirror,separation??gaps,twist,values);
        Check(Accept(expected),"R1 readback comparator accepts the complete reference");
        for(int i=0;i<11;i++)
        {
            var corrupt=(double[])expected.Clone();corrupt[i]+=i==10?Math.PI:Math.Max(1,Math.Abs(corrupt[i])*.1);
            Check(!Accept(corrupt),"R1 negative control rejects overwritten/lost "+PropertyNames198[i]);
        }
        Check(!Accept(expected,shape:5)&&!Accept(expected,mirror:0)&&!Accept(expected,separation:new[]{10d,0})&&!Accept(expected,twist:true),"R1 negative controls reject shape/mirror/gap/flag changes");
        var wrong=(double[])dims.Clone();wrong[3]+=1;
        Check(!Accept(expected,geometry:wrong),"R1 negative control rejects altered geometry");
    }
    private static int Native198()
    {
        if(Environment.GetEnvironmentVariable("BRIDGHE_SECTIONS190_NATIVE")!="1")
        {Console.WriteLine("Set BRIDGHE_SECTIONS190_NATIVE=1 under the coordinator Straus7 and heavy locks.");return 2;}
        checks=failed=0;plugin=BridgeMergeTests.Initialize();OracleChecks198();ReadbackNegative198();
        string root=Path.GetFullPath(Path.Combine("artifacts","198","native-"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff")));Directory.CreateDirectory(root);
        var rows=new List<object>();int number=0;
        foreach(var item in Cases198())foreach(var units in new[]{DefinitionUnits.Nmm,DefinitionUnits.Knm})
        foreach(double rotation in new[]{0d,37d})foreach(int direction in new[]{0,1,2})
        {
            string label=$"{item.Label}/{units}/{rotation}/axis{direction}";
            try
            {
                var model=Model198(item,units,rotation,direction);var beam=model.Model.BeamElements.Values.Single();
                var expected=Expected198((ElasticBeamProperty)beam.BeamProperty,item);
                var reference=Frame198(Ends198[direction],(rotation+(item.Type=="Angle"&&item.Mirror?90:0))*Math.PI/180);
                string path=Path.Combine(root,(++number).ToString("D3")+".st7");
                var plan=Straus7ModelWritePlan.Prepare(model.Model,null,null,model.Report.NativeSections);
                using(var writer=new Straus7NativeWriter(path,root)){plan.Write(writer,model.Model);writer.Save();}
                string fileHash=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));
                // A new runtime owner and a newly opened file. Never a resolver or a recorder in place of readback.
                using var api=new Straus7NativeApi();api.OpenModelReadOnly(path,root);
                try
                {
                    var element=api.ReadElement(Straus7Entity.Beam,1);var section=api.ReadSectionDefinition(element.Property,root);
                    var actual=section.ReferenceProperties;
                    Check(ReadbackMatches198(item,expected,section.Shape,section.Dimensions,section.MirrorType,section.MirrorGaps,section.CompatibleTwist,actual),"R1 saved/closed/reopened geometry and all GPC properties "+label);
                    for(int i=0;i<11;i++)Check(Close198(expected[i],actual[i],i),$"R1 {label}/{PropertyNames198[i]} actual={actual[i]:R} expected={expected[i]:R}");
                    var axes=element.InitialAxes;
                    Check(axes?.Length==9,"R2 native axes available "+label);
                    if(axes?.Length==9)
                    {
                        Vector198(reference.V1,new Vector3d(axes[0],axes[1],axes[2]),"R2 native signed axis1 "+label,1e-6);
                        Vector198(reference.V2,new Vector3d(axes[3],axes[4],axes[5]),"R2 native signed axis2 "+label,1e-6);
                        Vector198(reference.V3,new Vector3d(axes[6],axes[7],axes[8]),"R2 native signed axis3 "+label,1e-6);
                    }
                    var offset=api.ReadBeamOffset(1);Check(Close198(3,offset[0],0)&&Close198(7,offset[1],0),"R2 native signed offsets "+label);
                    int loadCase=PointLoadCase198(plan,model);
                    var load=api.ReadBeamPointLoad(1,loadCase,1,false,true);
                    double forceScale=units==DefinitionUnits.Nmm?1:1000;
                    var expectedForce=(reference.V1*2+reference.V2*3+reference.V3*5)*forceScale;
                    Vector198(expectedForce,new Vector3d(load[0],load[1],load[2]),"R2 native global force from local components "+label,1e-3);
                    Check(Math.Abs(load[3]-.37)<1e-6,"R2 native point load station "+label);
                    rows.Add(new{label,path,fileSha256=fileHash,expected,actual,shape=section.Shape,dimensions=section.Dimensions,mirror=section.MirrorType,gaps=section.MirrorGaps,compatibleTwist=section.CompatibleTwist,axes,offset,loadCase,globalPointLoad=load});
                }
                finally{api.CloseModel();}
            }
            catch(Exception ex){Check(false,"R1 native "+label+": "+ex);rows.Add(new{label,error=ex.ToString()});}
        }
        NativeNegative198(root);
        NativeResponse198(root);
        NativeOracle198(root);
        Historical198(true,root);
        File.WriteAllText(Path.Combine(root,"readback.json"),JsonSerializer.Serialize(new{source="actual Straus7ModelWritePlan export, saved, closed and reopened",units="N-mm after definition-unit normalization",rows},new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine("198 native readback evidence: "+root);
        return Finish();
    }
    private static void NativeResponse198(string root)
    {
        foreach(var item in Cases198().Where(c=>!c.Mirror||c.Gap==10||c.Gap==40||c.Gap==120))foreach(bool torsion in new[]{false,true})
        try
        {
            var recipe=Solve(item.Type,item.B,item.Tf,item.H,item.Tw,item.Mirror,item.Gap);
            var property=GpcPropertyBuilders.Beam("response "+item.Label,5,recipe,GpcFixtures.Steel());var elastic=(ElasticBeamProperty)property.Property;
            var b=new GpcNativeExamples.Builder("198 response "+item.Label+torsion);
            int a=b.Node(0,0,0,"1",null),z=b.Node(3000,0,0,"2",null);b.Restrain(a,true,true,true,true,true,true);
            if(!torsion)b.Restrain(z,false,false,false,true,false,false); // prevent torsion from transverse load eccentricity
            b.Beam(a,z,property,null,"B198");
            var frame=Frame198(Ends198[0],item.Type=="Angle"&&item.Mirror?Math.PI/2:0);
            if(torsion)b.Nodes[z]=GpcNodeBuilders.WithMoment(b.Nodes[z],b.Case("T"),1000000,0,0,b.Next("torque"));
            else {b.Force(z,"Q1",1000*frame.V1.X,1000*frame.V1.Y,1000*frame.V1.Z);b.Force(z,"Q2",1000*frame.V2.X,1000*frame.V2.Y,1000*frame.V2.Z);}
            var model=b.Build("response").Input;string path=Path.Combine(root,"response-"+item.Label+"-"+torsion+".st7");
            var result=new GpcNativeExamples.Results(StrausAnalysis.Run(model,path,0,torsion?new[]{"T"}:new[]{"Q1","Q2"},false,out var warnings));
            double e=elastic.Material.E,g=elastic.Material.GetShearModule();
            void Response(double expected,double actual,string name)=>Check(double.IsFinite(actual)&&Math.Abs(expected-actual)<=1e-5*Math.Max(1e-9,Math.Abs(expected)),"R1 response "+item.Label+" "+name+$" actual={actual:R} expected={expected:R}");
            if(torsion)Response(1000000d*3000/(g*elastic.Section.Jt),result.Displacement(1,"T")[3],"twist T L/GJ [rad]");
            else foreach(var q in new[]{("Q1",frame.V1,elastic.Section.J22,elastic.ShearArea1),("Q2",frame.V2,elastic.Section.J11,elastic.ShearArea2)})
            {
                var d=result.Displacement(1,q.Item1);double deflection=d[0]*q.Item2.X+d[1]*q.Item2.Y+d[2]*q.Item2.Z;
                Response(1000*Math.Pow(3000,3)/(3*e*q.Item3)+1000*3000/(g*q.Item4),deflection,q.Item1+" bending + shear [mm]");
                var reaction=result.Reaction(0,q.Item1);Response(-1000,reaction[0]*q.Item2.X+reaction[1]*q.Item2.Y+reaction[2]*q.Item2.Z,q.Item1+" reaction [N]");
            }
        }
        catch(Exception ex){Check(false,"R1 beam response "+item.Label+": "+ex);}
    }
}
