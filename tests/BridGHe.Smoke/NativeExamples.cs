using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using BridGHe.Common.Helpers;
using BridGHe.Common.Models;
using BridGHe.Common.MovingLoads;
using BridGHe.Common.Straus;
using Rhino.Geometry;

/// <summary>Numerical integration benchmarks, N-mm, linear elastic material, no gravity.</summary>
internal static class NativeExamples
{
    internal sealed record Example(string Id,string Title,int Solver,bool Moving,ModelModel Model,string[] Cases,Action<ModelModel,Recorder> Verify);
    internal sealed record Measurement(string Name,double Actual,double Expected,double AbsoluteTolerance,bool Passed);
    internal sealed class Recorder
    {
        public List<Measurement> Measurements { get; }=new();
        public void Near(string name,double actual,double expected,double relative=1e-5,double absolute=1e-8)
        {
            double tolerance=Math.Max(absolute,Math.Abs(expected)*relative);
            bool pass=double.IsFinite(actual)&&Math.Abs(actual-expected)<=tolerance;
            Measurements.Add(new(name,actual,expected,tolerance,pass));
            Console.WriteLine($"{(pass?"PASS":"FAIL")}: {name}: {actual:G12}, expected {expected:G12}, tolerance {tolerance:G3}");
        }
    }
    private const double E=210000,A=60000,I=450000000,L=3000,P=1000;
    private static readonly GroupModel Main=new("Permanent"),Added=new("Added");
    private static double Delta(double force=P,double length=L)=>-force*Math.Pow(length,3)/(3*E*I);
    private static MaterialModel Steel()=>new("S355"){Name="S355",Modulus=E,PoissonRatio=0.3};
    private static BeamPropertyModel Property()=>new("1"){Name="Elastic beam",Material=Steel(),PropertyType=BeamPropertyType.Beam,
        Section=new SectionModel{SectionType=SectionModel.SectionTypes.Generic,SectionArea=A,I11=I,I22=200000000,J=100000000,ShearA1=0,ShearA2=0}};
    private static LoadCaseModel Load(string name)=>new(name,name);
    private static LoadCombinationModel Combo(string name,params (LoadCaseModel Case,double Factor)[] terms)
    {
        var c=new LoadCombinationModel(name);foreach(var t in terms)c.Values.Add(t.Case,t.Factor);return c;
    }
    private static void Stage(ModelModel model,string name,GroupModel[] groups,params LoadCombinationModel[] increments)
    {
        var stage=new StagedConstructionModel(name){Marph=true,Rotate=true};stage.AddGroups(groups);stage.AddLoadCombinations(increments);model.AddStagedConstructions(new[]{stage});
    }
    private static ModelModel Beam(int count=1,double length=L,bool supported=false)
    {
        var model=new ModelModel(ModelModel.ModelUnits.Nmm);var property=Property();
        for(int i=0;i<=count;i++)
        {
            var n=new NodeModel((i+1).ToString(),new Point3d(length*i/count,0,0));n.Groups.Add(Main);
            if(!supported&&i==0)n.Attributes.Add(new NodeRestraintAttributeModel(true,true,true,true,true,true));
            if(supported)n.Attributes.Add(new NodeRestraintAttributeModel(i==0,true,i==0||i==count,true,false,true));
            model.AddElements(new[]{n});
        }
        for(int i=0;i<count;i++)model.AddElements(new[]{new BeamModel(model.Nodes[i].Position,model.Nodes[i+1].Position,property,Main){BeamId=(i+1).ToString()}});
        return model;
    }
    private static void TipLoad(ModelModel model,LoadCaseModel load,Vector3d force)
    { model.LoadCases.Add(load);model.Nodes.Last().Loads.Add(new NodeForceLoadModel(load,force)); }
    private static NodeDisplResultModel Displ(ModelModel m,int node,string name)=>m.Nodes[node].Results.OfType<NodeDisplResultModel>().Single(r=>r.LoadCase.Name==name);
    private static NodeReactResultModel Reaction(ModelModel m,int node,string name)=>m.Nodes[node].Results.OfType<NodeReactResultModel>().Single(r=>r.LoadCase.Name==name);
    private static BeamForceResultModel Force(ModelModel m,int beam,string name)=>m.Beams[beam].Results.OfType<BeamForceResultModel>().Where(r=>r.LoadCase.Name==name).OrderBy(r=>r.Station).First();
    private static void CheckCantilever(ModelModel m,Recorder r,string name,double factor)
    {
        r.Near(name+" / Uz [mm]",Displ(m,m.Nodes.Count-1,name).Displacement.Z,Delta()*factor);
        r.Near(name+" / Rz [N]",Reaction(m,0,name).Force.Z,P*factor);
        r.Near(name+" / My reaction [N mm]",Reaction(m,0,name).Moment.Y,-P*L*factor);
        r.Near(name+" / beam |My| [N mm]",Math.Abs(Force(m,0,name).My),P*L*Math.Abs(factor));
    }
    internal static IEnumerable<Example> Create()
    {
        {
            var m=Beam();var p=Load("P");TipLoad(m,p,new(0,0,-600));m.Nodes.Last().Loads.Add(new NodeForceLoadModel(p,new(0,0,-400)));
            m.AddLoadCombinations(new[]{Combo("1.5P",(p,1.5))});
            yield return new("01-cantilever","Mensola: somma carichi nodali e combinazione",0,false,m,new[]{"P","1.5P"},(x,r)=>{CheckCantilever(x,r,"P",1);CheckCantilever(x,r,"1.5P",1.5);});
        }
        {
            const double length=6000,q=1;var m=Beam(4,length,true);var p=Load("q");m.LoadCases.Add(p);
            foreach(var b in m.Beams)b.Loads.Add(new BeamDistributedLoadModel(p,new(){LoadSchema=BeamDistributedLoadModel.LoadSchema.Uniform,CoordinateSystem=CoordinateSystemModel.Global,
                LoadDirection=BeamDistributedLoadModel.LoadDirection.Z,P1=-q,P2=-q},BeamDistributedLoadModel.LoadType.Force));
            yield return new("02-simply-supported","Trave appoggiata con carico uniforme",0,false,m,new[]{"q"},(x,r)=>{
                r.Near("Uz midspan [mm]",Displ(x,2,"q").Displacement.Z,-5*q*Math.Pow(length,4)/(384*E*I));
                r.Near("Rz left [N]",Reaction(x,0,"q").Force.Z,q*length/2);r.Near("Rz right [N]",Reaction(x,4,"q").Force.Z,q*length/2);
                r.Near("Max |My| [N mm]",x.Beams.SelectMany(b=>b.Results.OfType<BeamForceResultModel>()).Max(f=>Math.Abs(f.My)),q*length*length/8);
            });
        }
        {
            const double length=1000,width=500,thickness=10,force=2000;
            var m=new ModelModel(ModelModel.ModelUnits.Nmm);var p=Load("Tension");m.LoadCases.Add(p);
            var points=new[]{new Point3d(0,0,0),new Point3d(length,0,0),new Point3d(length,width,0),new Point3d(0,width,0)};
            for(int i=0;i<4;i++){
                var n=new NodeModel((i+1).ToString(),points[i]);n.Attributes.Add(new NodeRestraintAttributeModel(i==0||i==3,i==0,true,true,true,true));
                if(i==1||i==2)n.Loads.Add(new NodeForceLoadModel(p,new(force/2,0,0)));m.AddElements(new[]{n});
            }
            var property=new PlatePropertyModel("1"){Name="Shell patch",Material=Steel(),PropertyType=PlatePropertyType.ShellThin,MembraneThickness=thickness,BendingThickness=thickness};
            m.AddElements(new[]{new PlateModel("1",points,property)});
            yield return new("03-plate-patch","Patch test membranale di una piastra shell",0,false,m,new[]{"Tension"},(x,r)=>{
                r.Near("Ux right [mm]",Displ(x,1,"Tension").Displacement.X,force*length/(E*width*thickness));
                r.Near("Poisson Uy [mm]",Displ(x,2,"Tension").Displacement.Y,-.3*force/(E*thickness));
                r.Near("Sum Rx [N]",x.Nodes.SelectMany(n=>n.Results.OfType<NodeReactResultModel>()).Sum(f=>f.Force.X),-force);
                foreach(var f in x.Plates[0].Results.OfType<PlateForceResultModel>())r.Near("Plate Nxx [N/mm]",f.Force.X,force/width);
                r.Near("Plate result nodes",x.Plates[0].Results.Count,4,0,0);
            });
        }
        {
            const double length=6000;var m=Beam(12,length,true);
            m.MovingPaths.Add(new MovingPath{LoadCaseName="Traffic",Divisions=12,Target=2,Coordinates=new double[]{0,-500,0,length,-500,0,0,500,0},ConvertToStaticCases=true,
                Template=new MovingTemplate{Name="Single axle",Vehicles=new[]{new MovingVehicle{Name="1000 N",Loads=new MovingObject[]{new MovingPointForce{Force=new double[]{0,0,-P}}}}}}});
            yield return new("04-moving-load","Asse mobile su trave appoggiata",0,true,m,Array.Empty<string>(),(x,r)=>{
                r.Near("Generated load positions",x.GeneratedMovingCases.Count,13,0,0);
                foreach(string name in x.GeneratedMovingCases.Values){
                    double left=Reaction(x,0,name).Force.Z,right=Reaction(x,12,name).Force.Z;
                    r.Near(name+" / equilibrium [N]",left+right,P);
                    double position=right*length/P;
                    double moment=x.Beams.SelectMany(b=>b.Results.OfType<BeamForceResultModel>()).Where(f=>f.LoadCase.Name==name).Max(f=>Math.Abs(f.My));
                    r.Near(name+" / max moment [N mm]",moment,P*position*(length-position)/length,1e-5,1e-5);
                }
                r.Near("Envelope max |My| [N mm]",x.Beams.SelectMany(b=>b.Results.OfType<BeamForceResultModel>()).Max(f=>Math.Abs(f.My)),P*length/4);
            });
        }
        {
            var m=Beam();var p=Load("P");TipLoad(m,p,new(0,0,-P));m.AddLoadCombinations(new[]{Combo("Half",(p,.5)),Combo("Full",(p,1)),Combo("Double",(p,2))});
            yield return new("05-nonlinear-increments","Incrementi del solutore non lineare in campo elastico",1,false,m,new[]{"Half","Double"},(x,r)=>{CheckCantilever(x,r,"Half",.5);CheckCantilever(x,r,"Double",2);});
        }
        {
            var m=Beam();var p=Load("G");TipLoad(m,p,new(0,0,-P));Stage(m,"Steel",new[]{Main},Combo("Load",(p,1)));Stage(m,"Deck",new[]{Main},Combo("Load",(p,1.5)));
            yield return new("06-stage-loading","Fasi: aumento del carico permanente",2,false,m,new[]{"Load [Steel]","Load [Deck]"},(x,r)=>{CheckCantilever(x,r,"Load [Steel]",1);CheckCantilever(x,r,"Load [Deck]",1.5);});
        }
        {
            var m=Beam();var p=Load("G");TipLoad(m,p,new(0,0,-P));Stage(m,"Loading",new[]{Main},Combo("Step",(p,1)));Stage(m,"Unloading",new[]{Main},Combo("Step",(p,0)));Stage(m,"Reloading",new[]{Main},Combo("Step",(p,.5)));
            yield return new("07-stage-unloading","Fasi: scarico e ricarico elastico",2,false,m,new[]{"Step [Loading]","Step [Unloading]","Step [Reloading]"},(x,r)=>{CheckCantilever(x,r,"Step [Loading]",1);CheckCantilever(x,r,"Step [Unloading]",0);CheckCantilever(x,r,"Step [Reloading]",.5);});
        }
        for(int index=8;index<=9;index++)
        {
            var m=Beam();var p=Load("Axial");TipLoad(m,p,new(P,0,0));
            m.AddElements(new[]{new BeamModel(m.Nodes[0].Position,m.Nodes[1].Position,Property(),Added){BeamId="2"}});
            Stage(m,"Original",new[]{Main},Combo("Step",(p,1)));
            Stage(m,"Reinforced",new[]{Main,Added},Combo("Step",(p,2)));
            if(index==9)Stage(m,"Removed",new[]{Main},Combo("Step",(p,2)));
            bool remove=index==9;
            yield return new(index==8?"08-stage-activation":"09-stage-removal",remove?"Fasi: rimozione di un elemento provvisorio":"Fasi: aggiunta di un elemento senza tensioni iniziali",2,false,m,
                StrausResultSelection.Catalog(m,2).Select(c=>c.Case.Name).ToArray(),(x,r)=>{
                    double d=P*L/(E*A);
                    r.Near("Original Ux [mm]",Displ(x,1,"Step [Original]").Displacement.X,d);
                    r.Near("Reinforced Ux [mm]",Displ(x,1,"Step [Reinforced]").Displacement.X,1.5*d,1e-5);
                    r.Near("Original member N after addition [N]",Force(x,0,"Step [Reinforced]").AxialForce,1.5*P);
                    r.Near("Added member N [N]",Force(x,1,"Step [Reinforced]").AxialForce,.5*P);
                    if(remove){r.Near("After removal Ux [mm]",Displ(x,1,"Step [Removed]").Displacement.X,2*d);r.Near("Remaining member N [N]",Force(x,0,"Step [Removed]").AxialForce,2*P);}
                });
        }
        {
            var m=Beam();var g=Load("G");var q=Load("Q");TipLoad(m,g,new(0,0,-P));TipLoad(m,q,new(0,0,-P));
            Stage(m,"Construction",new[]{Main},Combo("Half",(g,.5)),Combo("Full",(g,1)));
            Stage(m,"Service",new[]{Main},Combo("SLS",(g,1),(q,.5)),Combo("ULS",(g,1.35),(q,1.5)));
            yield return new("10-stage-selection","Fasi: piu incrementi, casi diversi e selezione parziale",2,false,m,new[]{"Full [Construction]","ULS [Service]"},(x,r)=>{
                CheckCantilever(x,r,"Full [Construction]",1);CheckCantilever(x,r,"ULS [Service]",2.85);
                r.Near("Only requested cases",x.Nodes[1].Results.OfType<NodeDisplResultModel>().Count(),2,0,0);
            });
        }
    }
    internal static int Run(string selected)
    {
        StrausRuntime.EnsureInitialized();
        string root=Path.GetFullPath(Path.Combine("artifacts","examples",DateTime.Now.ToString("yyyyMMdd-HHmmss")));Directory.CreateDirectory(root);
        var reports=new List<object>();int failures=0,count=0;
        foreach(var example in Create().Where(x=>selected=="all"||x.Id.StartsWith(selected,StringComparison.Ordinal)))
        {
            count++;Console.WriteLine("EXAMPLE "+example.Id);var record=new Recorder();string error=null;List<string> warnings=new();
            string folder=Path.Combine(root,example.Id);Directory.CreateDirectory(folder);example.Model.Name=example.Title;
            File.WriteAllBytes(Path.Combine(folder,"input.bgmodel"),ModelArchive.Write(example.Model));
            try{
                var result=StrausAnalysis.Run(example.Model,Path.Combine(folder,"model.st7"),example.Solver,example.Cases,1e-6,example.Moving,out warnings);
                File.WriteAllBytes(Path.Combine(folder,"results.bgmodel"),ModelArchive.Write(result));example.Verify(result,record);
                record.Near("Input remains without results",example.Model.Elements.Sum(e=>e.Results.Count),0,0,0);
            }catch(Exception ex){error=ex.ToString();Console.WriteLine("ERROR: "+error);}
            bool passed=error==null&&record.Measurements.All(m=>m.Passed);if(!passed)failures++;
            var report=new {example.Id,example.Title,example.Solver,Passed=passed,Warnings=warnings,Error=error,record.Measurements};reports.Add(report);
            File.WriteAllText(Path.Combine(folder,"report.json"),JsonSerializer.Serialize(report,new JsonSerializerOptions{WriteIndented=true}));
        }
        File.WriteAllText(Path.Combine(root,"report.json"),JsonSerializer.Serialize(new{Date=DateTimeOffset.Now,Count=count,Failures=failures,Examples=reports},new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine($"EXAMPLES {count}, FAILURES {failures}; report: {root}");return count>0&&failures==0?0:1;
    }
}
