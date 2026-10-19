using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using BridGHe.Common.Helpers;
using BridGHe.Common.Models;
using BridGHe.Common.Straus;
using BridGHe.Common.Checks;
using GPC.Checkers.Concrete.Attributes;
using GPC.Checkers.Concrete.SectionSolvers;
using GPC.Model.Standards;
using Rhino.Geometry;
using static BridgeGrillageExample;

/// <summary>Four continuous/curved grillages. Incremental stiffness states, N-mm-MPa.</summary>
internal static class BridgeSuiteExample
{
    internal sealed record Definition(string Id,string Title,double[] Spans,double Radius,int PanelsPerSpan=12)
    {
        public double Length=>Spans.Sum();
        public int Panels=>Spans.Length*PanelsPerSpan;
        public void Validate(bool refined=false)
        {
            if(Spans==null||Spans.Length<1||Spans.Length>5||Spans.Any(l=>!double.IsFinite(l)||l<=0)
                || !double.IsFinite(Radius)||Radius<0||Radius>0&&(Radius<=5000||Length/Radius>=Math.PI)
                ||PanelsPerSpan<12||PanelsPerSpan>(refined?192:96)||PanelsPerSpan%12!=0)
                throw new ArgumentException("Invalid spans, radius or mesh.");
        }
    }
    internal sealed record ShrinkageData(double Centroid,double EA,double EI,double Epsilon,double Gradient,double PrimaryN,double PrimaryM);
    internal sealed record SectionSample(int Beam,double Station,double[] FinalStress,bool Supported,string Reason);
    internal static readonly string Root=Path.GetFullPath("examples/bridge-suite");
    internal static readonly string Artifacts=Path.GetFullPath("artifacts/bridge-suite");
    internal static readonly string[] Cases={"G1_GETTO","G2_PERMANENTI","Q_TRAFFICO","RITIRO"};
    internal static readonly Definition[] Defaults={
        new("12-continuo-2campate","Rettilineo continuo 20 + 20 m",new[]{20000.0,20000},0),
        new("13-continuo-3campate","Rettilineo continuo 20 + 30 + 20 m",new[]{20000.0,30000,20000},0),
        new("14-curvo-1campata","Curvo 30 m / R 80 m",new[]{30000.0},80000,48),
        new("15-curvo-2campate","Curvo continuo 25 + 25 m / R 120 m",new[]{25000.0,25000},120000,24)
    };
    internal static Parameters SectionParameters(Definition d)=>new(){Span=d.Length,Panels=12,ShrinkageInstant=0};
    internal static BeamPropertyModel SuiteProperty(Parameters p,int phase)
    {
        var property=LongProperty(p,phase);
        // Match the current gPC composite thin-wall flange convention exactly;
        // the steel-only stage retains the full rectangular-plate inertia.
        if(phase>0)property.Section.I11-=2*p.FlangeWidth*Math.Pow(p.FlangeThickness,3)/12;
        return property;
    }
    internal static double[] Stations(Definition d)=>Enumerable.Range(0,d.Panels+1).Select(i=>{
        int span=Math.Min(i/d.PanelsPerSpan,d.Spans.Length-1);
        return d.Spans.Take(span).Sum()+d.Spans[span]*(i-span*d.PanelsPerSpan)/d.PanelsPerSpan;
    }).ToArray();
    internal static Point3d Position(Definition d,double s,double y)=>d.Radius==0?new(s,y,0):new((d.Radius-y)*Math.Sin(s/d.Radius),d.Radius-(d.Radius-y)*Math.Cos(s/d.Radius),0);
    internal static ShrinkageData Shrinkage(Parameters p)
    {
        double a=GpcH(p).Area,ac=p.GirderSlabWidth*p.SlabThickness,ys=-p.SteelHeight/2,yc=p.SlabThickness/2,ec=Ec/(1+p.PhiLong);
        double ea=Es*a+ec*ac,yg=(Es*a*ys+ec*ac*yc)/ea,ei=Es*SuiteProperty(p,1).Section.I11;
        double n=ec*ac*p.ShrinkageLong,m=n*(yc-yg);
        return new(yg,ea,ei,n/ea,m/ei,n,m);
    }
    internal static BeamPropertyModel CrossProperty(Parameters p,double strip,bool diaphragm,int phase)
    {
        var h=new GPC.Model.Sections.SectionH(800,12,300,20,300,20,"Diaframma");
        double a=diaphragm?h.Area:0,ix=diaphragm?h.Jxx:0,iy=diaphragm?h.Jyy:0,j=diaphragm?h.Jt:0;
        if(phase>0)
        {
            double ec=Ec/(phase==2?1:1+p.PhiLong),t=p.SlabThickness;
            a+=ec/Es*strip*t;ix+=ec/Es*strip*t*t*t/12;iy+=ec/Es*t*Math.Pow(strip,3)/12;
            j+=ec/(2*(1+NuConcrete))/(Es/(2*(1+NuSteel)))*strip*t*t*t/6;
        }
        return a==0?null:new("Trasverso "+phase+" "+strip+" "+diaphragm){Name="Traverso equivalente",Material=Steel(),
            PropertyType=BeamPropertyType.Beam,Section=new(){SectionType=SectionModel.SectionTypes.Generic,SectionArea=a,I11=ix,I22=iy,J=j,ShearA1=0,ShearA2=0}};
    }
    internal static ModelModel Create(Definition d,int phase)
    {
        d.Validate(refined:true);if(phase<0||phase>3)throw new ArgumentOutOfRangeException(nameof(phase));
        var p=SectionParameters(d);var s=Stations(d);int n=d.Panels;
        var m=new ModelModel(ModelModel.ModelUnits.Nmm){Name=d.Title+" / "+Cases[phase]};
        var lc=Case(Cases[phase],phase==3?1:phase);m.LoadCases.Add(lc);
        var sh=Shrinkage(p);var prop=SuiteProperty(p,phase==3?1:phase);
        if(phase==3)prop.Material.ThermalExpansion=1e-5; // Equivalent strain device, not a physical temperature action.
        double ad=new GPC.Model.Sections.SectionH(800,12,300,20,300,20,"D").Area;
        double q=phase==0?GpcH(p).Area*78.5e-6+p.Width/4*p.SlabThickness*25e-6+ad*(3*p.Width/4)*(6*d.Spans.Length+1)*78.5e-6/(4*d.Length)
            :phase==1?p.PermanentPressure*p.Width/4:phase==2?p.TrafficPressure*p.Width/4:0;
        for(int j=0;j<4;j++)for(int i=0;i<=n;i++)
        {
            var node=new NodeModel((j*(n+1)+i+1).ToString(),Position(d,s[i],GirderY(p,j)));node.Groups.Add(Main);
            // One fixed bearing in plan and one Y guide remove exactly three planar rigid motions.
            // Every girder is supported vertically at every abutment/pier; rotations remain free.
            node.Attributes.Add(new NodeRestraintAttributeModel(j==0&&i==0,j==0&&(i==0||i==n),i%d.PanelsPerSpan==0,false,false,false));
            double tributary=0;
            if(i>0)tributary+=node.Position.DistanceTo(Position(d,s[i-1],GirderY(p,j)))/2;
            if(i<n)tributary+=node.Position.DistanceTo(Position(d,s[i+1],GirderY(p,j)))/2;
            node.Loads.Add(new NodeForceLoadModel(lc,new(0,0,-q*tributary)));
            m.AddElements(new[]{node});
        }
        for(int j=0;j<4;j++)for(int i=0;i<n;i++)
        {
            var b=new BeamModel(m.Nodes[j*(n+1)+i].Position,m.Nodes[j*(n+1)+i+1].Position,prop,Main){BeamId=(j*n+i+1).ToString()};
            b.Attributes.Add(new SuperElementAttributeModel("G"+(j+1)));
            if(phase==3)
            {
                // Straus positive pre-strain contracts a free beam (opposite to free eigenstrain).
                b.Loads.Add(new BeamPreLoadModel(lc,-sh.Epsilon,BeamPreLoadModel.LoadType.Strain));
                b.Loads.Add(new BeamGradientLoadModel(lc,new(){Axis1=0,Axis2=sh.Gradient/prop.Material.ThermalExpansion}));
            }
            m.AddElements(new[]{b});
        }
        for(int i=0;i<=n;i++)
        {
            double strip=((i>0?s[i]-s[i-1]:0)+(i<n?s[i+1]-s[i]:0))/2;
            var cross=CrossProperty(p,strip,i%(d.PanelsPerSpan/6)==0,phase);if(cross==null)continue;
            for(int j=0;j<3;j++)m.AddElements(new[]{new BeamModel(m.Nodes[j*(n+1)+i].Position,m.Nodes[(j+1)*(n+1)+i].Position,cross,Cross){BeamId=(4*n+3*i+j+1).ToString()}});
        }
        return m;
    }
    internal static ModelModel Solve(ModelModel input,string name)
    {
        string folder=Path.Combine(Artifacts,name);Directory.CreateDirectory(folder);
        File.WriteAllBytes(Path.Combine(folder,"input.bgmodel"),ModelArchive.Write(input));
        var result=StrausAnalysis.Run(input,Path.Combine(folder,"model.st7"),0,input.LoadCases.Select(c=>c.Name).ToArray(),1e-6,false,out var warnings);
        File.WriteAllBytes(Path.Combine(folder,"results.bgmodel"),ModelArchive.Write(result));
        File.WriteAllText(Path.Combine(folder,"warnings.json"),JsonSerializer.Serialize(warnings,Json));
        return result;
    }
    internal static void Equilibrium(ModelModel input,ModelModel result,string name,NativeExamples.Recorder checks)
    {
        var force=Vector3d.Zero;var moment=Vector3d.Zero;double scaleF=0,scaleM=0;
        foreach(var node in result.Nodes)
        {
            var f=input.Nodes.Single(n=>n.NodeId==node.NodeId).Loads.OfType<NodeForceLoadModel>().Where(l=>l.LoadCase.Name==name).Aggregate(Vector3d.Zero,(v,l)=>v+l.Value);
            var r=Reaction(node,name);var x=new Vector3d(node.Position);
            force+=r.Force+f;moment+=Vector3d.CrossProduct(x,r.Force+f)+r.Moment;
            scaleF+=r.Force.Length+f.Length;scaleM+=Vector3d.CrossProduct(x,r.Force).Length+Vector3d.CrossProduct(x,f).Length+r.Moment.Length;
        }
        for(int k=0;k<3;k++)
        {
            checks.Near(name+" equilibrio F"+k+" [N]",force[k],0,0,Math.Max(1e-5,scaleF*1e-8));
            checks.Near(name+" equilibrio M"+k+" [Nmm]",moment[k],0,0,Math.Max(1e-2,scaleM*1e-8));
        }
    }
    private static BeamForceResultModel Force(ModelModel m,int beam,string name,bool end=false)=>m.Beams[beam].Results.OfType<BeamForceResultModel>().Where(f=>f.LoadCase.Name==name).OrderBy(f=>f.Station).ElementAt(end?4:0);
    private static void ShrinkageBenchmarks(NativeExamples.Recorder checks)
    {
        var p=new Parameters();var sh=Shrinkage(p);double l=20000;
        foreach(bool restrained in new[]{false,true})
        {
            var m=new ModelModel(ModelModel.ModelUnits.Nmm);var lc=Case("RITIRO",1);m.LoadCases.Add(lc);
            var prop=SuiteProperty(p,1);prop.Material.ThermalExpansion=1e-5;
            for(int i=0;i<3;i++)
            {
                var node=new NodeModel((i+1).ToString(),new(l*i/2,0,0));
                node.Attributes.Add(new NodeRestraintAttributeModel(i==0||restrained&&i==2,true,i==0||i==2,true,restrained&&(i==0||i==2),true));m.AddElements(new[]{node});
            }
            for(int i=0;i<2;i++)
            {
                var b=new BeamModel(m.Nodes[i].Position,m.Nodes[i+1].Position,prop,Main){BeamId=(i+1).ToString()};
                b.Loads.Add(new BeamPreLoadModel(lc,-sh.Epsilon,BeamPreLoadModel.LoadType.Strain));
                b.Loads.Add(new BeamGradientLoadModel(lc,new(){Axis2=sh.Gradient/prop.Material.ThermalExpansion}));m.AddElements(new[]{b});
            }
            var r=Solve(m,restrained?"benchmark-shrink-fixed":"benchmark-shrink-free");
            checks.Near("Ritiro "+restrained+" Uz L/2 [mm]",Displ(r.Nodes[1],"RITIRO").Displacement.Z,restrained?0:sh.Gradient*l*l/8,1e-8,1e-8);
            checks.Near("Ritiro "+restrained+" Ux L [mm]",Displ(r.Nodes[2],"RITIRO").Displacement.X,restrained?0:sh.Epsilon*l,1e-8,1e-8);
            checks.Near("Ritiro "+restrained+" N [N]",Force(r,0,"RITIRO").AxialForce,restrained?-sh.PrimaryN:0,1e-8,1e-6);
            checks.Near("Ritiro "+restrained+" My [Nmm]",Force(r,0,"RITIRO").My,restrained?sh.PrimaryM:0,1e-8,1e-3);
            Equilibrium(m,r,"RITIRO",checks);
        }
    }
    // Clapeyron's three-moment equation, independent of the 3D frame solver.
    // Uniform nodal tributary loads: the exact load term is reduced by (1-1/n²).
    private static double[] SupportMoments(Definition d,double q,double ei,double gradient)
    {
        int count=d.Spans.Length-1;var moments=new double[count+2];if(count==0)return moments;
        var a=new double[count,count];var rhs=new double[count];
        for(int i=0;i<count;i++)
        {
            double l=d.Spans[i],r=d.Spans[i+1];a[i,i]=2*(l+r);
            if(i>0)a[i,i-1]=l;if(i<count-1)a[i,i+1]=r;
            rhs[i]=-q*(Math.Pow(l,3)+Math.Pow(r,3))/4*(1-1.0/(d.PanelsPerSpan*d.PanelsPerSpan))+3*ei*gradient*(l+r);
        }
        for(int i=1;i<count;i++){double f=a[i,i-1]/a[i-1,i-1];a[i,i]-=f*a[i-1,i];rhs[i]-=f*rhs[i-1];}
        for(int i=count-1;i>=0;i--)moments[i+1]=(rhs[i]-(i<count-1?a[i,i+1]*moments[i+2]:0))/a[i,i];
        return moments;
    }
    private static double Deflection(double x,double l,int panels,double q,double ei,double ml,double mr,double k)
    {
        double w=(ml*(x*x/2-x*x*x/(6*l)-l*x/3)+mr*(x*x*x/(6*l)-l*x/6))/ei+k*x*(l-x)/2;
        for(int i=1;i<panels;i++)
        {
            double a=l*i/panels,b=l-a,p=q*l/panels;
            w-=x<=a?p*b*x*(l*l-b*b-x*x)/(6*l*ei):p*a*(l-x)*(l*l-a*a-(l-x)*(l-x))/(6*l*ei);
        }
        return w;
    }
    private static void CheckStraight(Definition d,ModelModel input,ModelModel result,int phase,NativeExamples.Recorder checks)
    {
        var p=SectionParameters(d);string name=Cases[phase];double ei=Es*SuiteProperty(p,phase==3?1:phase).Section.I11;
        double q=-input.Nodes.Sum(n=>n.Loads.OfType<NodeForceLoadModel>().Sum(l=>l.Value.Z))/(4*d.Length),k=phase==3?Shrinkage(p).Gradient:0;
        var moments=SupportMoments(d,q,ei,k);var s=Stations(d);
        for(int i=1;i<d.Spans.Length;i++)checks.Near(name+" tre momenti appoggio "+i,Force(result,i*d.PanelsPerSpan,name).My,moments[i],1e-7,1e-2);
        double maxError=0,scale=0;
        for(int j=0;j<4;j++)for(int i=0;i<=d.Panels;i++)
        {
            int span=Math.Min(i/d.PanelsPerSpan,d.Spans.Length-1);double x=s[i]-d.Spans.Take(span).Sum();
            double expected=Deflection(x,d.Spans[span],d.PanelsPerSpan,q,ei,moments[span],moments[span+1],k);
            maxError=Math.Max(maxError,Math.Abs(Displ(result.Nodes[j*(d.Panels+1)+i],name).Displacement.Z-expected));scale=Math.Max(scale,Math.Abs(expected));
        }
        checks.Near(name+" tutti i nodi / soluzione analitica [mm]",maxError,0,0,Math.Max(1e-6,scale*1e-7));
    }
    internal static BridgeCheckAdapter Checker(Parameters p)
    {
        var options=new GPC.Checkers.CompositeBridge.BridgeAnalysisOptions { Standard=GPC.Checkers.CompositeBridge.BridgeStandard.Eurocode4, LimitState=GPC.Checkers.CompositeBridge.BridgeLimitState.Rare, WebBuckling=false,TopFlangeBuckling=false,BottomFlangeBuckling=false};
        return new(Composite(p).GetReinforcedConcreteSection(),Reference, options);
    }
    internal static GPC.Geometry.CoordinateSystem Reference=>new(GPC.Geometry.Point3d.Origin,new GPC.Geometry.Vector3d(-1,0,0),new GPC.Geometry.Vector3d(0,-1,0));
    internal static double[] StressValues(BridgeStressResult r)=>new[]{r.Point1_ConcreteSigma,r.Point4_ConcreteSigma,r.Point5_SteelSigma,r.Point8_SteelSigma};
    internal static double[] ExpectedStress(Parameters p,BeamForceResultModel[] f)
    {
        double a=GpcH(p).Area,ys=-p.SteelHeight/2,ac=p.GirderSlabWidth*p.SlabThickness,yc=p.SlabThickness/2;
        double fullI=(p.FlangeWidth*Math.Pow(p.SteelHeight,3)-(p.FlangeWidth-p.WebThickness)*Math.Pow(p.SteelHeight-2*p.FlangeThickness,3))/12;
        double mixedI=fullI-2*p.FlangeWidth*Math.Pow(p.FlangeThickness,3)/12,ic=p.GirderSlabWidth*Math.Pow(p.SlabThickness,3)/12;
        var sh=Shrinkage(p);
        return new[]{(p.SlabThickness,true),(0.0,true),(0.0,false),(-p.SteelHeight,false)}.Select(point=>{
            double z=point.Item1;bool concrete=point.Item2;double stress=concrete?0:f[0].AxialForce/a-f[0].My*(z-ys)/fullI;
            for(int phase=1;phase<4;phase++)
            {
                double ec=Ec/(phase==2?1:1+p.PhiLong),n=Es/ec,ah=a+ac/n,zg=(a*ys+ac/n*yc)/ah;
                double ii=mixedI+a*Math.Pow(ys-zg,2)+(ic+ac*Math.Pow(yc-zg,2))/n;
                stress+=(f[phase].AxialForce/ah-f[phase].My*(z-zg)/ii)/(concrete?n:1);
            }
            // Self-equilibrated primary stress; native resultants supply only secondary effects.
            stress+=(concrete?Ec/(1+p.PhiLong):Es)*(sh.Epsilon+sh.Gradient*(z-sh.Centroid)-(concrete?p.ShrinkageLong:0));
            return stress;
        }).ToArray();
    }
    private static List<SectionSample> CheckSections(Definition d,ModelModel[] results,NativeExamples.Recorder checks)
    {
        var p=SectionParameters(d);var checker=Checker(p);var samples=new List<SectionSample>();
        foreach(int beam in new[]{d.PanelsPerSpan/2,Math.Min(d.PanelsPerSpan,d.Panels-1)})
        {
            var f=Enumerable.Range(0,4).Select(phase=>Force(results[phase],beam,Cases[phase])).ToArray();
            var combined=ModelArchive.Clone(f[1]);combined.AxialForce+=f[3].AxialForce;combined.Vx+=f[3].Vx;combined.Vy+=f[3].Vy;combined.Mx+=f[3].Mx;combined.My+=f[3].My;combined.Torque+=f[3].Torque;
            try
            {
                checker.GetLinearAnalysisResultByStages(out var stress,out _,p.PhiLong,0,new[]{BridgeForceConvention.FromStraus(f[0],Reference)},
                    new[]{BridgeForceConvention.FromStraus(combined,Reference)},new[]{BridgeForceConvention.FromStraus(f[2],Reference)},true,new[]{p.ShrinkageLong},new[]{0.0},false);
                var actual=StressValues(stress[0]);var expected=ExpectedStress(p,f);
                checks.Near("BridgeChecks quattro contributi "+beam,stress[0].Analysis.Stages.Count,4,0,0);
                for(int i=0;i<4;i++)checks.Near("BridgeChecks primario + secondario, beam "+beam+", sigma "+i,actual[i],expected[i],1e-6,1e-6);
                samples.Add(new(beam+1,0,actual,true,"Tensioni elastiche; sezione lorda non fessurata."));
            }
            catch(NotSupportedException ex)
            {
                checks.Near("BridgeChecks segnala azioni fuori campo beam "+beam,d.Radius>0?1:0,1,0,0);
                samples.Add(new(beam+1,0,Array.Empty<double>(),false,ex.Message));
            }
        }
        return samples;
    }
    private static object Summary(ModelModel m,string name,int n)=>new{
        Case=name,MinUz=m.Nodes.Min(node=>Displ(node,name).Displacement.Z),MaxUz=m.Nodes.Max(node=>Displ(node,name).Displacement.Z),
        MaxAbsN=m.Beams.Take(4*n).SelectMany(b=>b.Results.OfType<BeamForceResultModel>()).Max(r=>Math.Abs(r.AxialForce)),
        MinMy=m.Beams.Take(4*n).SelectMany(b=>b.Results.OfType<BeamForceResultModel>()).Min(r=>r.My),
        MaxMy=m.Beams.Take(4*n).SelectMany(b=>b.Results.OfType<BeamForceResultModel>()).Max(r=>r.My),
        MaxAbsT=m.Beams.Take(4*n).SelectMany(b=>b.Results.OfType<BeamForceResultModel>()).Max(r=>Math.Abs(r.Torque))};
    internal static void WriteResults(string folder,Definition d,ModelModel[] models)
    {
        using var nodes=new StreamWriter(Path.Combine(folder,"nodes.csv"));nodes.WriteLine("phase,case,node,s_mm,girder,x_mm,y_mm,ux_mm,uy_mm,uz_mm,rx_N,ry_N,rz_N");
        using var forces=new StreamWriter(Path.Combine(folder,"forces.csv"));forces.WriteLine("phase,case,beam,station_mm,N_N,V1_N,V2_N,BM1_Nmm,BM2_Nmm,T_Nmm");
        var s=Stations(d);
        for(int phase=0;phase<4;phase++)
        {
            var m=models[phase];string name=Cases[phase];
            foreach(var (node,index) in m.Nodes.Select((node,i)=>(node,i)))
            {
                var u=Displ(node,name).Displacement;var r=Reaction(node,name).Force;
                nodes.WriteLine(FormattableString.Invariant($"{phase+1},{name},{node.NodeId},{s[index%(d.Panels+1)]},{index/(d.Panels+1)+1},{node.Position.X},{node.Position.Y},{u.X},{u.Y},{u.Z},{r.X},{r.Y},{r.Z}"));
            }
            foreach(var b in m.Beams)foreach(var f in b.Results.OfType<BeamForceResultModel>())forces.WriteLine(FormattableString.Invariant($"{phase+1},{name},{b.BeamId},{f.Station},{f.AxialForce},{f.Vx},{f.Vy},{f.Mx},{f.My},{f.Torque}"));
        }
        File.WriteAllText(Path.Combine(folder,"geometry.json"),JsonSerializer.Serialize(new{Nodes=models[2].Nodes.Select(n=>new{n.NodeId,X=n.Position.X,Y=n.Position.Y,Z=n.Position.Z,Support=n.Attributes.OfType<NodeRestraintAttributeModel>().Single().Value.Tz}),
            Beams=models[2].Beams.Select(b=>new{b.BeamId,From=new[]{b.PointFrom.X,b.PointFrom.Y},To=new[]{b.PointTo.X,b.PointTo.Y},Main=b.HasAttribute(typeof(SuperElementAttributeModel))})},Json));
    }
    internal static int Run(bool grasshopper,bool probe=false,string selected="all")
    {
        CultureInfo.CurrentCulture=CultureInfo.InvariantCulture;
        Directory.CreateDirectory(Root);Directory.CreateDirectory(Artifacts);
        var benchmarks=new NativeExamples.Recorder();ShrinkageBenchmarks(benchmarks);
        File.WriteAllText(Path.Combine(Root,"benchmarks.json"),JsonSerializer.Serialize(benchmarks.Measurements,Json));
        if(probe||benchmarks.Measurements.Any(c=>!c.Passed))return benchmarks.Measurements.All(c=>c.Passed)?0:1;
        int failures=0,total=benchmarks.Measurements.Count;
        var selectedDefinitions=Defaults.Where(d=>selected=="all"||d.Id.StartsWith(selected,StringComparison.Ordinal)).ToArray();
        if(selectedDefinitions.Length==0)throw new ArgumentException("Unknown bridge example: "+selected);
        foreach(var initial in selectedDefinitions)
        {
            string folder=Path.Combine(Root,initial.Id);Directory.CreateDirectory(folder);
            string parameterFile=Path.Combine(folder,"parameters.json");
            var d=File.Exists(parameterFile)?JsonSerializer.Deserialize<Definition>(File.ReadAllText(parameterFile)):initial;d.Validate();
            if(d.Id!=initial.Id)throw new ArgumentException("Keep example Id unchanged.");
            File.WriteAllText(parameterFile,JsonSerializer.Serialize(d,Json));
            var checks=new NativeExamples.Recorder();var inputs=Enumerable.Range(0,4).Select(i=>Create(d,i)).ToArray();var results=new ModelModel[4];
            for(int phase=0;phase<4;phase++)
            {
                Console.WriteLine("SOLVE "+d.Id+" "+Cases[phase]);results[phase]=Solve(inputs[phase],d.Id+"/"+Cases[phase]);
                Equilibrium(inputs[phase],results[phase],Cases[phase],checks);
                if(d.Radius==0)CheckStraight(d,inputs[phase],results[phase],phase,checks);
                checks.Near(Cases[phase]+" risultati per ogni beam",results[phase].Beams.Count(b=>b.Results.OfType<BeamForceResultModel>().Count()==5),inputs[phase].Beams.Count,0,0);
            }
            var mesh=new List<object>();
            for(int phase=0;phase<4;phase++)
            {
                var refined=d with{PanelsPerSpan=d.PanelsPerSpan*2};var r=Solve(Create(refined,phase),d.Id+"/mesh-"+refined.PanelsPerSpan+"-"+Cases[phase]);
                double error=0,scale=0;
                for(int j=0;j<4;j++)for(int i=0;i<=d.Panels;i++)
                {
                    double u=Displ(results[phase].Nodes[j*(d.Panels+1)+i],Cases[phase]).Displacement.Z,v=Displ(r.Nodes[j*(refined.Panels+1)+2*i],Cases[phase]).Displacement.Z;
                    error=Math.Max(error,Math.Abs(u-v));scale=Math.Max(scale,Math.Abs(v));
                }
                checks.Near(Cases[phase]+" convergenza mesh Uz [mm]",error,0,0,Math.Max(1e-6,.02*scale));
                mesh.Add(new{Phase=Cases[phase],Base=d.PanelsPerSpan,Refined=refined.PanelsPerSpan,MaxUzDifference=error,ReferencePeakUz=scale,RelativeError=error/Math.Max(scale,1e-12)});
            }
            var sections=CheckSections(d,results,checks);WriteResults(folder,d,results);
            // GH graphs use the same component-built geometry, supports, loads and eigenstrains.
            if(grasshopper)GrasshopperBridgeSuite.Run(d,inputs,results,sections,checks);
            var report=new{Date=DateTimeOffset.Now,Definition=d,Section=SectionParameters(d),Ec,Shrinkage=Shrinkage(SectionParameters(d)),
                Method="3 independent incremental linear stiffness states + longitudinal differential shrinkage; primary section stress and native secondary resultants added once.",
                Scope="Gross uncracked sections, full interaction, equivalent planar grillage. Torsion preserved; curved sections exceed current BridgeChecks action domain. No normative complete bridge verification.",
                Results=results.Select((m,i)=>Summary(m,Cases[i],d.Panels)),Mesh=mesh,Sections=sections,Checks=checks.Measurements};
            File.WriteAllText(Path.Combine(folder,"validation.json"),JsonSerializer.Serialize(report,Json));
            int failed=checks.Measurements.Count(c=>!c.Passed);total+=checks.Measurements.Count;failures+=failed;
            Console.WriteLine($"{d.Id}: {checks.Measurements.Count-failed} passed, {failed} failed");
        }
        Console.WriteLine($"BRIDGE SUITE: {total-failures} passed, {failures} failed");return failures==0?0:1;
    }
}
