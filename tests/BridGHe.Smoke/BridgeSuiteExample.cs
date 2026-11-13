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
    internal static readonly string Root=ExamplePaths.Root;
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
    // -----------------------------------------------------------------------------------------------------------------------------------------
    // R6.6 lane R4c-grafi: --bridge-suite without any FEM model. The same four contribution models (and the shrinkage benchmarks) as recipes
    // (R4cModel), built as GPC data by the product builders, analysed by the GPC-only analysis and read with the GPC result readers. Every check is
    // kept with its expected value and tolerance: global equilibrium, three-moment equation and closed-form deflections, results at every beam,
    // mesh convergence, the shrinkage benchmarks, the section stresses against the independent integrals.

    /// <summary>The numbers of <see cref="SuiteProperty"/> from GPC alone (<see cref="LongNumbers"/>, the thin-wall flange convention of gPC
    /// subtracted from I11 in the composite phases).</summary>
    internal static (double A,double I11,double I22,double J) SuiteNumbers(Parameters p,int phase)
    {
        var x=LongNumbers(p,phase);
        // Match the current gPC composite thin-wall flange convention exactly;
        // the steel-only stage retains the full rectangular-plate inertia.
        if(phase>0)x.I11-=2*p.FlangeWidth*Math.Pow(p.FlangeThickness,3)/12;
        return x;
    }
    /// <summary>The GPC twin of <see cref="SuiteProperty"/> («Longitudinale fase n», or the name given), with the thermal expansion given.</summary>
    internal static BridGHe.Common.Gpc.Legacy.LegacyBeamProperty SuitePropertyOf(Parameters p,int phase,double thermalExpansion=0,string name=null)
    {
        var x=SuiteNumbers(p,phase);
        return R4cModel.Generic(name??"Longitudinale fase "+phase,x.A,x.I11,x.I22,x.J,R4cModel.Steel(thermalExpansion));
    }
    /// <summary>The GPC twin of <see cref="Shrinkage"/> (the bending stiffness of <see cref="SuiteNumbers"/>).</summary>
    internal static ShrinkageData ShrinkageGpc(Parameters p)
    {
        double a=GpcH(p).Area,ac=p.GirderSlabWidth*p.SlabThickness,ys=-p.SteelHeight/2,yc=p.SlabThickness/2,ec=Ec/(1+p.PhiLong);
        double ea=Es*a+ec*ac,yg=(Es*a*ys+ec*ac*yc)/ea,ei=Es*SuiteNumbers(p,1).I11;
        double n=ec*ac*p.ShrinkageLong,m=n*(yc-yg);
        return new(yg,ea,ei,n/ea,m/ei,n,m);
    }
    /// <summary>The GPC twin of <see cref="CrossProperty"/> («Traverso equivalente», the same arithmetic; null: no member).</summary>
    internal static BridGHe.Common.Gpc.Legacy.LegacyBeamProperty CrossPropertyGpc(Parameters p,double strip,bool diaphragm,int phase)
    {
        var h=new GPC.Model.Sections.SectionH(800,12,300,20,300,20,"Diaframma");
        double a=diaphragm?h.Area:0,ix=diaphragm?h.Jxx:0,iy=diaphragm?h.Jyy:0,j=diaphragm?h.Jt:0;
        if(phase>0)
        {
            double ec=Ec/(phase==2?1:1+p.PhiLong),t=p.SlabThickness;
            a+=ec/Es*strip*t;ix+=ec/Es*strip*t*t*t/12;iy+=ec/Es*t*Math.Pow(strip,3)/12;
            j+=ec/(2*(1+NuConcrete))/(Es/(2*(1+NuSteel)))*strip*t*t*t/6;
        }
        return a==0?null:R4cModel.Generic("Traverso equivalente",a,ix,iy,j,R4cModel.Steel());
    }
    /// <summary>The recipe of the former <c>Create</c>: four girders along the (curved) axis, super elements G1–G4, one fixed bearing in plan and
    /// one Y guide, Uz at every abutment and pier; the tributary nodal load of the contribution; in phase 3 (RITIRO) the pre-strain and the
    /// gradient of the longitudinal shrinkage on the girders (steel α 1E-5: the equivalent strain device), the long-term stiffness.</summary>
    internal static R4cModel CreateGpc(Definition d,int phase)
    {
        d.Validate(refined:true);if(phase<0||phase>3)throw new ArgumentOutOfRangeException(nameof(phase));
        var p=SectionParameters(d);var s=Stations(d);int n=d.Panels;
        var m=new R4cModel(d.Id+":"+d.PanelsPerSpan+":"+phase,d.Title+" / "+Cases[phase]);m.AddCase(Cases[phase]);
        var sh=ShrinkageGpc(p);var prop=SuitePropertyOf(p,phase==3?1:phase,phase==3?1e-5:0); // Equivalent strain device, not a physical temperature action.
        double ad=new GPC.Model.Sections.SectionH(800,12,300,20,300,20,"D").Area;
        double q=phase==0?GpcH(p).Area*78.5e-6+p.Width/4*p.SlabThickness*25e-6+ad*(3*p.Width/4)*(6*d.Spans.Length+1)*78.5e-6/(4*d.Length)
            :phase==1?p.PermanentPressure*p.Width/4:phase==2?p.TrafficPressure*p.Width/4:0;
        for(int j=0;j<4;j++)for(int i=0;i<=n;i++)
        {
            // One fixed bearing in plan and one Y guide remove exactly three planar rigid motions.
            // Every girder is supported vertically at every abutment/pier; rotations remain free.
            var position=Position(d,s[i],GirderY(p,j));
            int k=m.AddNode(position,j==0&&i==0,j==0&&(i==0||i==n),i%d.PanelsPerSpan==0,false,false,false);
            double tributary=0;
            if(i>0)tributary+=position.DistanceTo(Position(d,s[i-1],GirderY(p,j)))/2;
            if(i<n)tributary+=position.DistanceTo(Position(d,s[i+1],GirderY(p,j)))/2;
            m.Nodes[k].Loads.Add((Cases[phase],new(0,0,-q*tributary)));
        }
        for(int j=0;j<4;j++)for(int i=0;i<n;i++)
        {
            var b=m.AddBeam(j*(n+1)+i,j*(n+1)+i+1,prop,true,(j*n+i+1).ToString(CultureInfo.InvariantCulture),"G"+(j+1));
            // Straus positive pre-strain contracts a free beam (opposite to free eigenstrain).
            if(phase==3)b.Shrinkage=(Cases[phase],-sh.Epsilon,0,sh.Gradient/1e-5);
        }
        for(int i=0;i<=n;i++)
        {
            double strip=((i>0?s[i]-s[i-1]:0)+(i<n?s[i+1]-s[i]:0))/2;
            var cross=CrossPropertyGpc(p,strip,i%(d.PanelsPerSpan/6)==0,phase);if(cross==null)continue;
            for(int j=0;j<3;j++)m.AddBeam(j*(n+1)+i,(j+1)*(n+1)+i,cross,false,(4*n+3*i+j+1).ToString(CultureInfo.InvariantCulture));
        }
        return m;
    }
    /// <summary>The former <c>Solve</c> on the GPC data: linear static, the cases of the recipe, in the calculation folder of the suite.</summary>
    internal static R4cModel.Results Solve(R4cModel input,string name)=>new(R4cModel.Solve(input.Build(),ExamplePaths.SuiteCalculation(name),0,input.Cases.ToArray()),input);
    internal static void Equilibrium(R4cModel input,R4cModel.Results result,string name,NativeExamples.Recorder checks)
    {
        var force=Vector3d.Zero;var moment=Vector3d.Zero;double scaleF=0,scaleM=0;
        for(int k=0;k<input.Nodes.Count;k++)
        {
            var f=input.Nodes[k].Force(name);
            var r=result.ReactionForce(k,name);var rm=result.ReactionMoment(k,name);var x=new Vector3d(input.Nodes[k].Position);
            force+=r+f;moment+=Vector3d.CrossProduct(x,r+f)+rm;
            scaleF+=r.Length+f.Length;scaleM+=Vector3d.CrossProduct(x,r).Length+Vector3d.CrossProduct(x,f).Length+rm.Length;
        }
        for(int k=0;k<3;k++)
        {
            checks.Near(name+" equilibrio F"+k+" [N]",force[k],0,0,Math.Max(1e-5,scaleF*1e-8));
            checks.Near(name+" equilibrio M"+k+" [Nmm]",moment[k],0,0,Math.Max(1e-2,scaleM*1e-8));
        }
    }
    internal const double BenchmarkLength=20000;
    /// <summary>The shrinkage benchmark of the former run: two beams of 10 m on the long-term property (α 1E-5), the pre-strain and gradient of the
    /// shrinkage in RITIRO, free (pinned and roller) or restrained (both ends fixed in Ux and Ry); nodes without group.</summary>
    internal static R4cModel BenchmarkGpc(bool restrained)
    {
        var p=new Parameters();var sh=ShrinkageGpc(p);double l=BenchmarkLength;
        var m=new R4cModel("benchmark:"+restrained,""){NodesInMain=false};m.AddCase("RITIRO");
        var prop=SuitePropertyOf(p,1,1e-5);
        for(int i=0;i<3;i++)m.AddNode(new(l*i/2,0,0),i==0||restrained&&i==2,true,i==0||i==2,true,restrained&&(i==0||i==2),true);
        for(int i=0;i<2;i++)m.AddBeam(i,i+1,prop,true,(i+1).ToString(CultureInfo.InvariantCulture)).Shrinkage=("RITIRO",-sh.Epsilon,0,sh.Gradient/1e-5);
        return m;
    }
    private static void ShrinkageBenchmarks(NativeExamples.Recorder checks)
    {
        var p=new Parameters();var sh=ShrinkageGpc(p);double l=BenchmarkLength;
        foreach(bool restrained in new[]{false,true})
        {
            var m=BenchmarkGpc(restrained);
            var r=Solve(m,restrained?"benchmark-shrink-fixed":"benchmark-shrink-free");
            checks.Near("Ritiro "+restrained+" Uz L/2 [mm]",r.Displacement(1,"RITIRO")[2],restrained?0:sh.Gradient*l*l/8,1e-8,1e-8);
            checks.Near("Ritiro "+restrained+" Ux L [mm]",r.Displacement(2,"RITIRO")[0],restrained?0:sh.Epsilon*l,1e-8,1e-8);
            checks.Near("Ritiro "+restrained+" N [N]",r.Force(0,"RITIRO").Axial,restrained?-sh.PrimaryN:0,1e-8,1e-6);
            checks.Near("Ritiro "+restrained+" My [Nmm]",r.Force(0,"RITIRO").Bending2,restrained?sh.PrimaryM:0,1e-8,1e-3);
            Equilibrium(m,r,"RITIRO",checks);
        }
    }
    // The former FEM models (kept for the transitional comparison r4c-inputs only: R4cGrafiInputs; P2 removes them with the FEM types).
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
    private static void CheckStraight(Definition d,R4cModel input,R4cModel.Results result,int phase,NativeExamples.Recorder checks)
    {
        var p=SectionParameters(d);string name=Cases[phase];double ei=Es*SuiteNumbers(p,phase==3?1:phase).I11;
        double q=-input.Nodes.Sum(n=>n.Loads.Sum(l=>l.Force.Z))/(4*d.Length),k=phase==3?ShrinkageGpc(p).Gradient:0;
        var moments=SupportMoments(d,q,ei,k);var s=Stations(d);
        for(int i=1;i<d.Spans.Length;i++)checks.Near(name+" tre momenti appoggio "+i,result.Force(i*d.PanelsPerSpan,name).Bending2,moments[i],1e-7,1e-2);
        double maxError=0,scale=0;
        for(int j=0;j<4;j++)for(int i=0;i<=d.Panels;i++)
        {
            int span=Math.Min(i/d.PanelsPerSpan,d.Spans.Length-1);double x=s[i]-d.Spans.Take(span).Sum();
            double expected=Deflection(x,d.Spans[span],d.PanelsPerSpan,q,ei,moments[span],moments[span+1],k);
            maxError=Math.Max(maxError,Math.Abs(result.Displacement(j*(d.Panels+1)+i,name)[2]-expected));scale=Math.Max(scale,Math.Abs(expected));
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
    /// <summary>The GPC twin of <see cref="Checker"/>: the composite section of the example built in GPC (<see cref="GpcCompositeDefinitions.Grillage"/>).</summary>
    internal static BridgeCheckAdapter CheckerGpc(Parameters p)
    {
        var options=new GPC.Checkers.CompositeBridge.BridgeAnalysisOptions { Standard=GPC.Checkers.CompositeBridge.BridgeStandard.Eurocode4, LimitState=GPC.Checkers.CompositeBridge.BridgeLimitState.Rare, WebBuckling=false,TopFlangeBuckling=false,BottomFlangeBuckling=false};
        return new(GpcCompositeDefinitions.Create(GpcCompositeDefinitions.Grillage(p)).Section,Reference, options);
    }
    /// <summary>The GPC twin of <see cref="ExpectedStress"/> on the beam rows of the four contributions (axial force and BM2 of each).</summary>
    internal static double[] ExpectedStressGpc(Parameters p,BridGHe.Common.Gpc.GpcResultRows.BeamRow[] f)
    {
        double a=GpcH(p).Area,ys=-p.SteelHeight/2,ac=p.GirderSlabWidth*p.SlabThickness,yc=p.SlabThickness/2;
        double fullI=(p.FlangeWidth*Math.Pow(p.SteelHeight,3)-(p.FlangeWidth-p.WebThickness)*Math.Pow(p.SteelHeight-2*p.FlangeThickness,3))/12;
        double mixedI=fullI-2*p.FlangeWidth*Math.Pow(p.FlangeThickness,3)/12,ic=p.GirderSlabWidth*Math.Pow(p.SlabThickness,3)/12;
        var sh=ShrinkageGpc(p);
        return new[]{(p.SlabThickness,true),(0.0,true),(0.0,false),(-p.SteelHeight,false)}.Select(point=>{
            double z=point.Item1;bool concrete=point.Item2;double stress=concrete?0:f[0].Axial/a-f[0].Bending2*(z-ys)/fullI;
            for(int phase=1;phase<4;phase++)
            {
                double ec=Ec/(phase==2?1:1+p.PhiLong),n=Es/ec,ah=a+ac/n,zg=(a*ys+ac/n*yc)/ah;
                double ii=mixedI+a*Math.Pow(ys-zg,2)+(ic+ac*Math.Pow(yc-zg,2))/n;
                stress+=(f[phase].Axial/ah-f[phase].Bending2*(z-zg)/ii)/(concrete?n:1);
            }
            // Self-equilibrated primary stress; native resultants supply only secondary effects.
            stress+=(concrete?Ec/(1+p.PhiLong):Es)*(sh.Epsilon+sh.Gradient*(z-sh.Centroid)-(concrete?p.ShrinkageLong:0));
            return stress;
        }).ToArray();
    }
    /// <summary>The actions of the second contribution plus those of the shrinkage (the former sum on a copy of the G2 result), as a sample.</summary>
    internal static BeamForceSample Combined(BridGHe.Common.Gpc.GpcResultRows.BeamRow g2,BridGHe.Common.Gpc.GpcResultRows.BeamRow shrinkage) =>
        R4cModel.Sample(g2) with {Axial=g2.Axial+shrinkage.Axial,Shear1=g2.Shear1+shrinkage.Shear1,Shear2=g2.Shear2+shrinkage.Shear2,
            Bending1=g2.Bending1+shrinkage.Bending1,Bending2=g2.Bending2+shrinkage.Bending2,Torque=g2.Torque+shrinkage.Torque};
    private static List<SectionSample> CheckSections(Definition d,R4cModel.Results[] results,NativeExamples.Recorder checks)
    {
        var p=SectionParameters(d);var checker=CheckerGpc(p);var samples=new List<SectionSample>();
        foreach(int beam in new[]{d.PanelsPerSpan/2,Math.Min(d.PanelsPerSpan,d.Panels-1)})
        {
            var f=Enumerable.Range(0,4).Select(phase=>results[phase].Force(beam,Cases[phase])).ToArray();
            var combined=Combined(f[1],f[3]);
            checker.GetLinearAnalysisResultByStages(out var stress,out _,p.PhiLong,0,new[]{BridgeForceConvention.FromStraus(R4cModel.Sample(f[0]),Cases[0],Reference)},
                new[]{BridgeForceConvention.FromStraus(combined,Cases[1],Reference)},new[]{BridgeForceConvention.FromStraus(R4cModel.Sample(f[2]),Cases[2],Reference)},true,new[]{p.ShrinkageLong},new[]{0.0},false);
            var actual=StressValues(stress[0]);var expected=ExpectedStressGpc(p,f);
            checks.Near("BridgeChecks quattro contributi "+beam,stress[0].Analysis.Stages.Count,4,0,0);
            for(int i=0;i<4;i++)checks.Near("BridgeChecks primario + secondario, beam "+beam+", sigma "+i,actual[i],expected[i],1e-6,1e-6);
            samples.Add(new(beam+1,0,actual,true,"Tensioni elastiche N-Mx-Vy; T, M2 e V1 esclusi; sezione lorda non fessurata."));
        }
        return samples;
    }
    private static object Summary(R4cModel.Results m,string name,int n)
    {
        var rows=Enumerable.Range(0,4*n).SelectMany(b=>m.BeamRows(b)).ToArray();var nodes=Enumerable.Range(0,m.Recipe.Nodes.Count).ToArray();
        return new{Case=name,MinUz=nodes.Min(k=>m.Displacement(k,name)[2]),MaxUz=nodes.Max(k=>m.Displacement(k,name)[2]),
            MaxAbsN=rows.Max(r=>Math.Abs(r.Axial)),MinMy=rows.Min(r=>r.Bending2),MaxMy=rows.Max(r=>r.Bending2),MaxAbsT=rows.Max(r=>Math.Abs(r.Torque))};
    }
    internal static void WriteResults(string folder,Definition d,R4cModel.Results[] models)
    {
        using var nodes=new StreamWriter(Path.Combine(folder,"nodes.csv"));nodes.WriteLine("phase,case,node,s_mm,girder,x_mm,y_mm,ux_mm,uy_mm,uz_mm,rx_N,ry_N,rz_N");
        using var forces=new StreamWriter(Path.Combine(folder,"forces.csv"));forces.WriteLine("phase,case,beam,station_mm,N_N,V1_N,V2_N,BM1_Nmm,BM2_Nmm,T_Nmm");
        var s=Stations(d);
        for(int phase=0;phase<4;phase++)
        {
            var m=models[phase];string name=Cases[phase];
            for(int index=0;index<m.Recipe.Nodes.Count;index++)
            {
                var node=m.Recipe.Nodes[index];var u=m.Translation(index,name);var r=m.ReactionForce(index,name);
                nodes.WriteLine(FormattableString.Invariant($"{phase+1},{name},{index+1},{s[index%(d.Panels+1)]},{index/(d.Panels+1)+1},{node.Position.X},{node.Position.Y},{u.X},{u.Y},{u.Z},{r.X},{r.Y},{r.Z}"));
            }
            for(int b=0;b<m.Recipe.Beams.Count;b++)foreach(var f in m.BeamRows(b))forces.WriteLine(FormattableString.Invariant($"{phase+1},{name},{m.Recipe.Beams[b].BeamId},{f.Station},{f.Axial},{f.Shear1},{f.Shear2},{f.Bending1},{f.Bending2},{f.Torque}"));
        }
        var g=models[2].Recipe;
        File.WriteAllText(Path.Combine(folder,"geometry.json"),JsonSerializer.Serialize(new{Nodes=g.Nodes.Select((n,i)=>new{NodeId=(i+1).ToString(CultureInfo.InvariantCulture),X=n.Position.X,Y=n.Position.Y,Z=n.Position.Z,Support=n.Restraint[2]}),
            Beams=g.Beams.Select(b=>new{b.BeamId,From=new[]{g.Nodes[b.From].Position.X,g.Nodes[b.From].Position.Y},To=new[]{g.Nodes[b.To].Position.X,g.Nodes[b.To].Position.Y},Main=b.SuperElement!=null})},Json));
    }
    internal static int Run(bool grasshopper,bool probe=false,string selected="all")
    {
        CultureInfo.CurrentCulture=CultureInfo.InvariantCulture;
        Directory.CreateDirectory(Root);Directory.CreateDirectory(ExamplePaths.Documentation);
        var benchmarks=new NativeExamples.Recorder();ShrinkageBenchmarks(benchmarks);
        File.WriteAllText(Path.Combine(ExamplePaths.Documentation,"benchmarks.json"),JsonSerializer.Serialize(benchmarks.Measurements,Json));
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
            var checks=new NativeExamples.Recorder();var inputs=Enumerable.Range(0,4).Select(i=>CreateGpc(d,i)).ToArray();var results=new R4cModel.Results[4];
            for(int phase=0;phase<4;phase++)
            {
                Console.WriteLine("SOLVE "+d.Id+" "+Cases[phase]);results[phase]=Solve(inputs[phase],d.Id+"/"+Cases[phase]);
                Equilibrium(inputs[phase],results[phase],Cases[phase],checks);
                if(d.Radius==0)CheckStraight(d,inputs[phase],results[phase],phase,checks);
                checks.Near(Cases[phase]+" risultati per ogni beam",Enumerable.Range(0,inputs[phase].Beams.Count).Count(b=>results[phase].BeamRows(b).Length==5),inputs[phase].Beams.Count,0,0);
            }
            var mesh=new List<object>();
            for(int phase=0;phase<4;phase++)
            {
                var refined=d with{PanelsPerSpan=d.PanelsPerSpan*2};var r=Solve(CreateGpc(refined,phase),d.Id+"/mesh-"+refined.PanelsPerSpan+"-"+Cases[phase]);
                double error=0,scale=0;
                for(int j=0;j<4;j++)for(int i=0;i<=d.Panels;i++)
                {
                    double u=results[phase].Displacement(j*(d.Panels+1)+i,Cases[phase])[2],v=r.Displacement(j*(refined.Panels+1)+2*i,Cases[phase])[2];
                    error=Math.Max(error,Math.Abs(u-v));scale=Math.Max(scale,Math.Abs(v));
                }
                checks.Near(Cases[phase]+" convergenza mesh Uz [mm]",error,0,0,Math.Max(1e-6,.02*scale));
                mesh.Add(new{Phase=Cases[phase],Base=d.PanelsPerSpan,Refined=refined.PanelsPerSpan,MaxUzDifference=error,ReferencePeakUz=scale,RelativeError=error/Math.Max(scale,1e-12)});
            }
            var sections=CheckSections(d,results,checks);WriteResults(folder,d,results);
            // GH graphs use the same component-built geometry, supports, loads and eigenstrains.
            if(grasshopper)GrasshopperBridgeSuite.Run(d,inputs,results,sections,checks);
            var report=new{Date=DateTimeOffset.Now,Definition=d,Section=SectionParameters(d),Ec,Shrinkage=ShrinkageGpc(SectionParameters(d)),
                Method="3 independent incremental linear stiffness states + longitudinal differential shrinkage; primary section stress and native secondary resultants added once.",
                Scope="Gross uncracked sections, full interaction, equivalent planar grillage. Torsion preserved; curved sections exceed current BridgeChecks action domain. No normative complete bridge verification.",
                Results=results.Select((m,i)=>Summary(m,Cases[i],d.Panels)),Mesh=mesh,Sections=sections,Checks=checks.Measurements};
            File.WriteAllText(Path.Combine(folder,"validation.json"),JsonSerializer.Serialize(report,Json));
            int failed=checks.Measurements.Count(c=>!c.Passed);total+=checks.Measurements.Count;failures+=failed;
            Console.WriteLine($"{d.Id}: {checks.Measurements.Count-failed} passed, {failed} failed");
        }
        Console.WriteLine($"BRIDGE SUITE: {total-failures} passed, {failures} failed (GPC data alone)");return failures==0?0:1;
    }
}
