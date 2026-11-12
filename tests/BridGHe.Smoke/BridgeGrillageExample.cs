using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using BridGHe.Common.Checks;
using BridGHe.Common.Helpers;
using BridGHe.Common.Models;
using BridGHe.Common.MovingLoads;
using BridGHe.Common.Straus;
using GPC.Checkers.Concrete.Attributes;
using GPC.Checkers.Concrete.SectionSolvers;
using GPC.Model.Materials;
using GPC.Model.Sections;
using GPC.Model.Standards;
using Rhino.Geometry;

/// <summary>Reproducible teaching example. N, mm, MPa. Not a bridge design certification.</summary>
internal static class BridgeGrillageExample
{
    internal sealed record StressRow(int Phase,double TopConcrete,double BottomConcrete,double TopSteel,double BottomSteel);
    internal sealed record SectionReport(string Location,string Combination,List<StressRow> Rows,double MaxTrafficTorqueNmm);
    internal sealed record Parameters
    {
        public double Span { get; init; } = 30000;
        public double Width { get; init; } = 10000;
        public double? EffectiveSlabWidth { get; init; }
        public double GirderSlabWidth => EffectiveSlabWidth ?? Width / 4;
        public int Panels { get; init; } = 12;
        public double SlabThickness { get; init; } = 250;
        public double SteelHeight { get; init; } = 1600;
        public double FlangeWidth { get; init; } = 600;
        public double FlangeThickness { get; init; } = 40;
        public double WebThickness { get; init; } = 16;
        public double PermanentPressure { get; init; } = .0025;
        public double TrafficPressure { get; init; } = .0025;
        public double VehicleWeight { get; init; } = 300000;
        public double AxleSpacing { get; init; } = 3000;
        public int MovingPositions { get; init; } = 21;
        public double PhiLong { get; init; } = 2;
        public double ShrinkageLong { get; init; } = -100e-6;
        public double ShrinkageInstant { get; init; } = -50e-6;
        public void Validate(bool refined = false)
        {
            double[] positive = {Span,Width,GirderSlabWidth,SlabThickness,SteelHeight,FlangeWidth,FlangeThickness,WebThickness,VehicleWeight,AxleSpacing};
            if (positive.Any(x => !double.IsFinite(x) || x <= 0) || SteelHeight <= 2*FlangeThickness || FlangeWidth <= WebThickness || AxleSpacing >= Span)
                throw new ArgumentException("Invalid bridge dimensions or vehicle weight.");
            if (Panels < 12 || Panels > (refined ? 96 : 24) || Panels % 12 != 0 || MovingPositions < 3 || MovingPositions > 201)
                throw new ArgumentException("Base Panels must be 12 or 24 (refinements up to 96); MovingPositions in [3,201].");
            if (new[]{PermanentPressure,TrafficPressure,PhiLong}.Any(x=>!double.IsFinite(x)||x<0) || !double.IsFinite(ShrinkageLong) || !double.IsFinite(ShrinkageInstant))
                throw new ArgumentException("Invalid pressure, creep coefficient or free strain.");
        }
    }
    internal const double Es = 210000, NuSteel = .3, NuConcrete = .2;
    internal static readonly ConcreteMaterialEN1992 Concrete = new("C30",30,ConcreteMaterial.CompressionStressStrainDiagrams.ParabolaRectangle);
    internal static double Ec => Concrete.ElasticModulusCompression;
    internal static readonly GroupModel Main = new("Longitudinali"), Cross = new("Traversi");
    internal static readonly string[] PhaseNames = {"G1_GETTO", "G2_PERMANENTI", "Q_UDL"};
    internal static readonly LoadCaseModel.Stage.BridgePhases[] Phases = {LoadCaseModel.Stage.BridgePhases.OnlySteel,LoadCaseModel.Stage.BridgePhases.NInfinite,LoadCaseModel.Stage.BridgePhases.NInstant};
    internal static readonly JsonSerializerOptions Json = new(){WriteIndented=true};
    internal static readonly string Output = ExamplePaths.Calculations("11-ponte-graticcio");
    internal static readonly string Delivery = ExamplePaths.Case("11-ponte-graticcio");
    internal static MaterialModel Steel() => new("S355"){Name="S355",Modulus=Es,PoissonRatio=NuSteel,MinimumYieldStress=355,MinimumTensileStress=510};
    internal static SectionModel H(Parameters p) => new(){SectionType=SectionModel.SectionTypes.I,D=p.SteelHeight,B1=p.FlangeWidth,B2=p.FlangeWidth,T1=p.FlangeThickness,T2=p.FlangeThickness,T3=p.WebThickness};
    internal static SectionH GpcH(Parameters p) => new(p.SteelHeight,p.WebThickness,p.FlangeWidth,p.FlangeThickness,p.FlangeWidth,p.FlangeThickness,"H principale");
    internal static CompositeBeamPropertyModel Composite(Parameters p) => new("Composite")
    {
        Name="H + soletta tributaria", SteelSection=H(p), SteelMaterial=Steel(),
        ConcreteSection=new(p.SlabThickness,p.GirderSlabWidth,0,40),
        ConcreteMaterial=new("C30"){Name="C30",Modulus=Ec,PoissonRatio=NuConcrete,SpecificCompressiveStrength=30},
        RebarMaterial=new("B450"){Name="B450",Modulus=Es,MinimumYieldStress=450,MinimumTensileStress=540},
        NInstant=Es/Ec,NInfinite=Es/Ec*(1+p.PhiLong),ConsiderConcrete=true,ConsiderRebar=false
    };
    internal static LoadCaseModel Case(string name,int phase) => new(name,name){Stages=new(){new(name){BridgePhase=Phases[phase]}}};
    internal static double GirderY(Parameters p,int j) => (j-1.5)*p.Width/4;
    private static SectionH CrossH() => new(800,12,300,20,300,20,"Traverso acciaio");
    internal static double G1(Parameters p) => GpcH(p).Area*78.5e-6 + p.Width/4*p.SlabThickness*25e-6
        + CrossH().Area*(3*p.Width/4)*7*78.5e-6/(4*p.Span);

    internal static BeamPropertyModel LongProperty(Parameters p,int phase,double torsionFactor=1)
    {
        var h=GpcH(p); double a=h.Area,ix=h.Jxx,iy=h.Jyy,j=h.Jt;
        if(phase>0)
        {
            var prop=Composite(p).GetHomogeneizedMechanicalProperties(phase==1?Es/Ec*(1+p.PhiLong):Es/Ec,true,false,true);
            double n=phase==1?Es/Ec*(1+p.PhiLong):Es/Ec;
            a=prop.areaH/n;ix=prop.JxxH/n;iy=prop.JyyH/n;
            double gc=Ec/(phase==1?1+p.PhiLong:1)/(2*(1+NuConcrete)),gs=Es/(2*(1+NuSteel));
            j+=gc/gs*p.GirderSlabWidth*Math.Pow(p.SlabThickness,3)/6;
        }
        return Property("Longitudinale fase "+phase,a,ix,iy,j*torsionFactor);
    }
#if GPC_PILOT
    /// <summary>R6.6 lane R4a: the GPC twin of <see cref="LongProperty"/> (no FEM model): a Beam on a Generic section of the same area,
    /// inertias and torsion, on the steel S355 E 210000 ν 0.3 fy 355 fu 510 given in GPC. Phase 0 the steel H (<see cref="GpcH"/>); phases 1
    /// and 2 the homogenised values of the composite definition built in GPC from the numbers of the example
    /// (<c>GpcCompositeDefinitions.Grillage</c>; R6.6 R4c-grafi: <see cref="LongNumbers"/>, the Ixx and Iyy of the former homogenised property), with
    /// the torsion of the steel plus the slab strip in steel units.</summary>
    internal static BridGHe.Common.Gpc.Legacy.LegacyBeamProperty LongPropertyGpc(Parameters p,int phase,double torsionFactor=1)
    {
        // R6.6 lane R4c-grafi: the numbers of LongNumbers (Ixx, Iyy of the homogenised section, as LongProperty), bit for bit those of the former
        // Homogenised principal values for the default parameters of the callers (r4c-inputs, «R4C NUMBERS»), not swapped for a wide slab.
        var x=LongNumbers(p,phase,torsionFactor);
        return BridGHe.Common.Gpc.Builders.GpcPropertyBuilders.Beam("Longitudinale fase "+phase,5,
            BridGHe.Common.Gpc.Builders.GpcSectionBuilders.Generic(x.A,x.I11,x.I22,x.J,0,0,0,0,0,0,0),R4aFixtures.Steel("S355",Es,355,510),"Steel");
    }
#endif
    internal static BeamPropertyModel CrossProperty(Parameters p,int phase,int row,double torsionFactor=1)
    {
        // Steel diaphragms at seven fixed stations, independent of mesh refinement.
        bool diaphragm=row%(p.Panels/6)==0;
        var h=CrossH(); double a=diaphragm?h.Area:0,ix=diaphragm?h.Jxx:0,iy=diaphragm?h.Jyy:0,j=diaphragm?h.Jt:0;
        if(phase>0)
        {
            double strip=p.Span/p.Panels*(row==0||row==p.Panels?.5:1),ec=Ec/(phase==1?1+p.PhiLong:1);
            a+=ec/Es*strip*p.SlabThickness;
            ix+=ec/Es*strip*Math.Pow(p.SlabThickness,3)/12;
            iy+=ec/Es*p.SlabThickness*Math.Pow(strip,3)/12;
            j+=ec/(2*(1+NuConcrete))/(Es/(2*(1+NuSteel)))*strip*Math.Pow(p.SlabThickness,3)/6;
        }
        // Keep identical topology in all phases. Between diaphragms the wet slab
        // has NO stiffness: omit those members in the steel-only model.
        return a>0?Property("Trasversale "+phase+" / "+row,a,ix,iy,j*torsionFactor):null;
    }
    private static BeamPropertyModel Property(string name,double a,double ix,double iy,double j) => new(name)
    {Name=name,Material=Steel(),PropertyType=BeamPropertyType.Beam,Section=new(){SectionType=SectionModel.SectionTypes.Generic,SectionArea=a,I11=ix,I22=iy,J=j,ShearA1=0,ShearA2=0}};

#if GPC_PILOT
    // -----------------------------------------------------------------------------------------------------------------------------------------
    // R6.6 lane R4c-grafi: --bridge-grillage without any FEM model. The same models as recipes (R4cModel: the same nodes, restraints, forces,
    // groups, beams, properties, super elements, BridGHe IDs, cases, stages and moving path as the former ModelModel), built as GPC data by the
    // product builders, analysed by the GPC-only analysis and read with the GPC result readers. Every check of the former run is kept with its
    // expected value and tolerance: equilibrium, analytic deflections, symmetry, Maxwell-Betti, moving positions and resultants, staged
    // deflections and reactions, mesh convergence, torsion sensitivity, section stresses against the independent integrals.

    /// <summary>The numbers of <see cref="LongProperty"/> (area, I11, I22, J) from GPC alone: phase 0 the steel H, phases 1 and 2 the homogenised
    /// mechanical properties of the composite definition built in GPC from the numbers of the example (<c>GpcCompositeSections.AnalysisProperties</c>,
    /// concrete without bars: the former <c>GetHomogeneizedMechanicalProperties(n, true, false, true)</c> of <see cref="Composite"/>), area,
    /// Ixx and Iyy divided by n as <see cref="LongProperty"/> took them, with the torsion of the steel plus the slab strip in steel units.
    /// Not <c>GpcCompositeSections.Homogenised</c>: that gives the principal values (I1 ≥ I2), which swap Ixx and Iyy when the slab makes the
    /// section stiffer laterally (the spine examples 16–18: r4c-inputs).</summary>
    internal static (double A,double I11,double I22,double J) LongNumbers(Parameters p,int phase,double torsionFactor=1)
    {
        var h=GpcH(p); double a=h.Area,ix=h.Jxx,iy=h.Jyy,j=h.Jt;
        if(phase>0)
        {
            double n=phase==1?Es/Ec*(1+p.PhiLong):Es/Ec;
            var prop=BridGHe.Common.Gpc.GpcCompositeSections.AnalysisProperties(GpcCompositeDefinitions.Create(GpcCompositeDefinitions.Grillage(p)),n,true,false,true);
            a=prop.areaH/n;ix=prop.JxxH/n;iy=prop.JyyH/n;
            double gc=Ec/(phase==1?1+p.PhiLong:1)/(2*(1+NuConcrete)),gs=Es/(2*(1+NuSteel));
            j+=gc/gs*p.GirderSlabWidth*Math.Pow(p.SlabThickness,3)/6;
        }
        return (a,ix,iy,j*torsionFactor);
    }
    /// <summary>The GPC twin of <see cref="LongProperty"/> on the steel of the former examples (<see cref="R4cModel.Steel"/>).</summary>
    internal static BridGHe.Common.Gpc.Legacy.LegacyBeamProperty LongPropertyOf(Parameters p,int phase,double torsionFactor=1,double thermalExpansion=0,string name=null)
    {
        var x=LongNumbers(p,phase,torsionFactor);
        return R4cModel.Generic(name??"Longitudinale fase "+phase,x.A,x.I11,x.I22,x.J,R4cModel.Steel(thermalExpansion));
    }
    /// <summary>The GPC twin of <see cref="CrossProperty"/> (the same arithmetic on the steel diaphragm and the slab strip; null: no member).</summary>
    internal static BridGHe.Common.Gpc.Legacy.LegacyBeamProperty CrossPropertyGpc(Parameters p,int phase,int row,double torsionFactor=1)
    {
        bool diaphragm=row%(p.Panels/6)==0;
        var h=CrossH(); double a=diaphragm?h.Area:0,ix=diaphragm?h.Jxx:0,iy=diaphragm?h.Jyy:0,j=diaphragm?h.Jt:0;
        if(phase>0)
        {
            double strip=p.Span/p.Panels*(row==0||row==p.Panels?.5:1),ec=Ec/(phase==1?1+p.PhiLong:1);
            a+=ec/Es*strip*p.SlabThickness;
            ix+=ec/Es*strip*Math.Pow(p.SlabThickness,3)/12;
            iy+=ec/Es*p.SlabThickness*Math.Pow(strip,3)/12;
            j+=ec/(2*(1+NuConcrete))/(Es/(2*(1+NuSteel)))*strip*Math.Pow(p.SlabThickness,3)/6;
        }
        return a>0?R4cModel.Generic("Trasversale "+phase+" / "+row,a,ix,iy,j*torsionFactor,R4cModel.Steel()):null;
    }
    /// <summary>The recipe of the former <c>Create</c>: four girders on 12 to 96 panels (group «Longitudinali», super elements G1–G4, BridGHe IDs
    /// 1…4n), the transverse members of <see cref="CrossPropertyGpc"/> (group «Traversi», IDs from 4n + 1), Uz restrained at the eight bearings,
    /// Ux and Uy everywhere, Rz everywhere; the nodal line load of the phase; in phase 2 the eccentric and mirrored vehicle cases.</summary>
    internal static R4cModel CreateGpc(Parameters p,int phase,double torsionFactor=1,string tag="")
    {
        p.Validate(refined:true);
        var model=new R4cModel(FormattableString.Invariant($"11:{phase}:{p.Panels}:{torsionFactor}{tag}"),"Ponte graticcio / "+PhaseNames[phase]);
        int n=p.Panels;double dx=p.Span/n;
        string primary=PhaseNames[phase];model.AddCase(primary);
        double q=phase==0?G1(p):(phase==1?p.PermanentPressure:p.TrafficPressure)*p.Width/4;
        for(int j=0;j<4;j++)for(int i=0;i<=n;i++)
        {
            // Plane grillage: only Uz, Rx, Ry are active. All eight bearings restrain Uz.
            int k=model.AddNode(new(dx*i,GirderY(p,j),0),true,true,i==0||i==n,false,false,true);
            model.Nodes[k].Loads.Add((primary,new(0,0,-q*dx*(i==0||i==n?.5:1))));
        }
        var longitudinal=LongPropertyOf(p,phase,torsionFactor);
        for(int j=0;j<4;j++)for(int i=0;i<n;i++)model.AddBeam(j*(n+1)+i,j*(n+1)+i+1,longitudinal,true,(j*n+i+1).ToString(CultureInfo.InvariantCulture),"G"+(j+1));
        for(int i=0;i<=n;i++)
        {
            var prop=CrossPropertyGpc(p,phase,i,torsionFactor);if(prop==null)continue;
            for(int j=0;j<3;j++)model.AddBeam(j*(n+1)+i,(j+1)*(n+1)+i,prop,false,(4*n+3*i+j+1).ToString(CultureInfo.InvariantCulture));
        }
        if(phase==2)
        {
            foreach(var entry in new[]{("Q_ECC",2), ("Q_MIRROR",0)})
            {
                model.AddCase(entry.Item1);
                foreach(double x in new[]{p.Span/2-p.AxleSpacing/2,p.Span/2+p.AxleSpacing/2})
                    for(int j=entry.Item2;j<entry.Item2+2;j++)AddPointGpc(model,p,entry.Item1,x,j,p.VehicleWeight/4);
            }
        }
        return model;
    }
    /// <summary>The moving model of the former run: the recipe of phase 2 with the vehicle path <see cref="Traffic"/>.</summary>
    internal static R4cModel MovingGpc(Parameters p)
    {
        var model=CreateGpc(p,2,tag:":moving");model.Paths.Add(Traffic(p));return model;
    }
    /// <summary>The staged model of the former run: the recipe of phase 0 with the restraints of the NLA (one plan bearing, Uy and Uz at the ends)
    /// and five stages GETTO_1…GETTO_5 on both groups, each one increment of G1_GETTO with the factor 1/5…5/5.</summary>
    internal static R4cModel StagedGpc(Parameters p)
    {
        var staged=CreateGpc(p,0,tag:":staged");
        // NLA must allow longitudinal shortening; the planar LSA suppression of
        // all in-plane DOFs would introduce artificial membrane stiffening.
        foreach(var node in staged.Nodes)
        {
            bool end=node.Position.X==0||node.Position.X==p.Span;
            node.Restraint=new[]{node.Position.X==0&&node.Position.Y==GirderY(p,0),end,end,false,false,false};
        }
        string[] stageNames=Enumerable.Range(1,5).Select(i=>"GETTO_"+i).ToArray();
        for(int i=0;i<5;i++)staged.Stages.Add((stageNames[i],staged.Cases.Single(),(i+1)/5.0));
        return staged;
    }
    private static void AddPointGpc(R4cModel m,Parameters p,string loadCase,double x,int girder,double weight)
    {
        double f=x/p.Span*p.Panels;int i=(int)Math.Floor(f);double t=f-i;
        // Explicit linear tributary load distribution, preserving force and moment.
        m.Nodes[girder*(p.Panels+1)+i].Loads.Add((loadCase,new(0,0,-weight*(1-t))));
        m.Nodes[girder*(p.Panels+1)+i+1].Loads.Add((loadCase,new(0,0,-weight*t)));
    }
    /// <summary>The former <c>RunModel</c> on the GPC data: the GPC-only analysis in <c>calcoli/&lt;name&gt;</c>, the cases of the recipe unless given.</summary>
    internal static R4cModel.Results RunModel(R4cModel model,string name,int solver=0,bool moving=false,string[] cases=null) =>
        new(R4cModel.Solve(model.Build(),Path.Combine(Output,name),solver,cases??model.Cases.ToArray(),moving),model);
    internal static double Midspan(R4cModel.Results m,Parameters p,string name,int girder=0)=>m.Displacement(girder*(p.Panels+1)+p.Panels/2,name)[2];
    private static void Equilibrium(R4cModel input,R4cModel.Results result,string name,NativeExamples.Recorder checks)
    {
        double[] residual=new double[3],scale=new double[3];
        for(int k=0;k<input.Nodes.Count;k++)
        {
            var node=input.Nodes[k];var r=result.Reaction(k,name);var f=node.Loads.Where(l=>l.Case==name).Sum(l=>l.Force.Z);
            double[] actions={r[2]+f,r[2]*node.Position.X-r[4]+f*node.Position.X,r[2]*node.Position.Y+r[3]+f*node.Position.Y};
            double[] loads={f,f*node.Position.X,f*node.Position.Y};
            for(int i=0;i<3;i++){residual[i]+=actions[i];scale[i]+=Math.Abs(loads[i]);}
        }
        for(int i=0;i<3;i++)checks.Near(name+" equilibrio "+new[]{"Fz","My","Mx"}[i],residual[i],0,0,Math.Max(1e-5,scale[i]*1e-8));
    }
    private static double AnalyticMidspan(Parameters p,int phase)
    {
        double q=phase==0?G1(p):(phase==1?p.PermanentPressure:p.TrafficPressure)*p.Width/4;
        double ei=Es*LongNumbers(p,phase).I11,dx=p.Span/p.Panels,value=0;
        // Simply supported point-load Green function at L/2, no beam shape functions.
        for(int i=1;i<p.Panels;i++)
        {
            double a=Math.Min(i*dx,p.Span-i*dx);
            value-=q*dx*a*(3*p.Span*p.Span-4*a*a)/(48*ei);
        }
        return value;
    }
    internal static int Run(bool grasshopper)
    {
        CultureInfo.CurrentCulture=CultureInfo.InvariantCulture;
        var p=JsonSerializer.Deserialize<Parameters>(File.ReadAllText(Path.Combine(Delivery,"parameters.json")));p.Validate();
        Directory.CreateDirectory(Output);var checks=new NativeExamples.Recorder();
        var inputs=Enumerable.Range(0,3).Select(i=>CreateGpc(p,i)).ToArray();
        var results=new R4cModel.Results[3];
        for(int phase=0;phase<3;phase++)
        {
            results[phase]=RunModel(inputs[phase],"0"+(phase+1)+"-"+PhaseNames[phase]);
            foreach(var c in inputs[phase].Cases)Equilibrium(inputs[phase],results[phase],c,checks);
            for(int j=0;j<4;j++)checks.Near(PhaseNames[phase]+" G"+(j+1)+" freccia analitica [mm]",Midspan(results[phase],p,PhaseNames[phase],j),AnalyticMidspan(p,phase),1e-7,1e-6);
        }
        var instant=results[2];
        for(int j=0;j<4;j++)for(int i=0;i<=p.Panels;i++)checks.Near($"Specularita veicolo nodo {j}/{i}",instant.Displacement(j*(p.Panels+1)+i,"Q_ECC")[2],instant.Displacement((3-j)*(p.Panels+1)+i,"Q_MIRROR")[2],1e-6,1e-7);
        double Work(string a,string b)=>Enumerable.Range(0,inputs[2].Nodes.Count).Sum(k=>inputs[2].Nodes[k].Loads.Where(l=>l.Case==a).Sum(l=>l.Force.Z)*instant.Displacement(k,b)[2]);
        checks.Near("Reciprocita Maxwell-Betti [N mm]",Work("Q_ECC","Q_UDL"),Work("Q_UDL","Q_ECC"),1e-8);
        var movingInput=MovingGpc(p);
        var moving=RunModel(movingInput,"04-mobile",moving:true,cases:Array.Empty<string>());
        checks.Near("Posizioni veicolo generate (inclusi ingresso/uscita)",moving.GeneratedMovingCases.Count,MovingCaseCount(p),0,0);
        int pos=0;
        var nodes=Enumerable.Range(0,movingInput.Nodes.Count).ToArray();
        foreach(string name in moving.GeneratedMovingCases.OrderBy(k=>k.Number).Select(k=>k.Name))
        {
            // Straus starts with the front axle, then advances by a path division.
            // Axles outside the path do not contribute, even during entry/exit.
            double front=(++pos-.5)*p.Span/p.MovingPositions;
            var axles=new[]{front,front-p.AxleSpacing}.Where(x=>x>=0&&x<=p.Span).ToArray();
            Point3d At(int k)=>movingInput.Nodes[k].Position;
            checks.Near(name+" somma Rz [N]",nodes.Sum(k=>moving.Reaction(k,name)[2]),p.VehicleWeight/2*axles.Length,1e-7);
            checks.Near(name+" momento globale Y [N mm]",nodes.Sum(k=>moving.Reaction(k,name)[2]*At(k).X-moving.Reaction(k,name)[4]),p.VehicleWeight/2*axles.Sum(),1e-7);
            checks.Near(name+" momento globale X [N mm]",nodes.Sum(k=>moving.Reaction(k,name)[2]*At(k).Y+moving.Reaction(k,name)[3]),p.VehicleWeight/2*axles.Length*p.Width/4,1e-7);
        }
        var staged=StagedGpc(p);
        var stagedInput=staged.Build();
        var stageNames=BridGHe.Common.Gpc.GpcResultSelection.Catalog(stagedInput.Model,2).Select(e=>e.Name).ToArray();
        var stages=new R4cModel.Results(R4cModel.Solve(stagedInput,Path.Combine(Output,"05-getto-cinque-fasi"),2,stageNames),staged);
        for(int i=0;i<5;i++)
        {
            // NLA retains finite rotations; compare to the small-deflection reference
            // within 0.001%, consistent with this example's displacement/span ratio.
            checks.Near(stageNames[i]+" freccia progressiva [mm]",Midspan(stages,p,stageNames[i]),AnalyticMidspan(p,0)*(i+1)/5,1e-5,1e-5);
            checks.Near(stageNames[i]+" equilibrio verticale [N]",Enumerable.Range(0,staged.Nodes.Count).Sum(k=>stages.Reaction(k,stageNames[i])[2]),4*G1(p)*p.Span*(i+1)/5,1e-6);
        }
        var meshes=new List<object>();double previous=Midspan(instant,p,"Q_ECC",3);
        meshes.Add(new{Panels=p.Panels,Uz=previous});
        foreach(int factor in new[]{2,4})
        {
            var refined=p with {Panels=p.Panels*factor};var r=RunModel(CreateGpc(refined,2),"mesh-"+refined.Panels);
            double d=Midspan(r,refined,"Q_ECC",3);meshes.Add(new{Panels=refined.Panels,Uz=d});
            checks.Near("Convergenza mesh "+refined.Panels+" Uz [mm]",previous,d,.02,1e-6);previous=d;
        }
        var sensitivity=new List<object>();
        foreach(double factor in new[]{.5,2.0})
        {
            var torsion=CreateGpc(p,2,factor);var r=RunModel(torsion,"torsione-"+factor.ToString(CultureInfo.InvariantCulture));
            sensitivity.Add(new{GJFactor=factor,Uz=Midspan(r,p,"Q_ECC",3)});
            Equilibrium(torsion,r,"Q_ECC",checks);
        }
        var stresses=CheckSections(p,results,checks);
        WriteResults(p,results,moving);
        if(grasshopper)GrasshopperBridgeExample.Run(p,inputs,results,stresses,checks);
        var report=new{Date=DateTimeOffset.Now,Parameters=p,Ec,SteelArea=GpcH(p).Area,LineG1=G1(p),Nodes=inputs[2].Nodes.Count,Beams=inputs[2].Beams.Count,
            StageMethod="Independent linear stiffness states; native wet-concrete stages are a separate benchmark, not summed into BridgeChecks.",
            Scope="SLS normal/shear stresses only for symmetric combination. Eccentric traffic torsion preserved; full mixed-action design NOT verified.",
            MidspanSteel=Midspan(results[0],p,PhaseNames[0]),MidspanLong=Midspan(results[1],p,PhaseNames[1]),MidspanLive=Midspan(results[2],p,PhaseNames[2]),
            Mesh=meshes,TorsionSensitivity=sensitivity,Stresses=stresses,Checks=checks.Measurements};
        File.WriteAllText(Path.Combine(Delivery,"validation.json"),JsonSerializer.Serialize(report,Json));
        File.WriteAllText(Path.Combine(Output,"validation.json"),JsonSerializer.Serialize(report,Json));
        int failed=checks.Measurements.Count(c=>!c.Passed);Console.WriteLine($"BRIDGE GRILLAGE: {checks.Measurements.Count-failed} passed, {failed} failed (GPC data alone)");return failed==0?0:1;
    }
    /// <summary>The section stresses of the former <c>CheckSections</c> on the GPC data: the composite section of the example built in GPC
    /// (<see cref="GpcCompositeDefinitions.Grillage"/>); the actions of the five contributions at the start of the midspan beam of G1 from the GPC
    /// result rows of the three phase models (<c>BridgePhaseAssembler.CreateIndex(beamId, samples…)</c>, the path of Super Element Check on the
    /// GPC bridge model) with the phases of the cases as the former cases had them (G1_GETTO steel, G2_PERMANENTI long term, Q_UDL short term),
    /// and the two imposed strains of the recipe (<see cref="Parameters.ShrinkageLong"/>, <see cref="Parameters.ShrinkageInstant"/>, unit
    /// factors: the former slab strain loads of the cases RITIRO_INF and DEF_IST). The eccentric response: the beam row of Q_ECC with the largest
    /// torque on the girders.</summary>
    private static SectionReport CheckSections(Parameters p,R4cModel.Results[] results,NativeExamples.Recorder checks)
    {
        var section=GpcCompositeDefinitions.Create(GpcCompositeDefinitions.Grillage(p)).Section;
        var cs=new GPC.Geometry.CoordinateSystem(GPC.Geometry.Point3d.Origin,new GPC.Geometry.Vector3d(-1,0,0),new GPC.Geometry.Vector3d(0,-1,0));
        var options=new GPC.Checkers.CompositeBridge.BridgeAnalysisOptions { Standard=GPC.Checkers.CompositeBridge.BridgeStandard.Eurocode4, LimitState=GPC.Checkers.CompositeBridge.BridgeLimitState.Rare, WebBuckling=false,TopFlangeBuckling=false,BottomFlangeBuckling=false};
        var checker=new BridgeCheckAdapter(section,cs, options);
        int midspan=p.Panels/2;
        var samples=Enumerable.Range(0,3).SelectMany(k=>results[k].BeamRows(midspan,PhaseNames[k]).Select(r=>(PhaseNames[k],R4cModel.Sample(r)))).ToArray();
        var index=BridgePhaseAssembler.CreateIndex(results[0].Recipe.Beams[midspan].BeamId,samples,1e-6);
        var phases=new Dictionary<string,BridgeCasePhase>{{PhaseNames[0],BridgeCasePhase.OnlySteel},{PhaseNames[1],BridgeCasePhase.NInfinite},{PhaseNames[2],BridgeCasePhase.NInstant},
            {"RITIRO_INF",BridgeCasePhase.StrainInfinite},{"DEF_IST",BridgeCasePhase.StrainInstant}};
        var factors=new[]{(PhaseNames[0],1.0),(PhaseNames[1],1.0),(PhaseNames[2],1.0),("RITIRO_INF",1.0),("DEF_IST",1.0)};
        var actions=index.Assemble(0,factors,phases,cs) with {StrainInfinite=p.ShrinkageLong*1.0,StrainInstant=p.ShrinkageInstant*1.0};
        checker.GetLinearAnalysisResultByStages(out var response,out _,p.PhiLong,0,new[]{actions.Steel},new[]{actions.Infinite},new[]{actions.Instant},true,new[]{actions.StrainInfinite},new[]{actions.StrainInstant},false);
        checks.Near("BridgeChecks cinque fasi accumulate",response[0].Analysis.Stages.Count,5,0,0);
        var rows=new List<StressRow>();
        // Independent elastic section integrals, including the current gPC thin-wall flange convention.
        double a=GpcH(p).Area,ys=-p.SteelHeight/2,yc=p.SlabThickness/2,ac=p.Width/4*p.SlabThickness,ic=p.Width/4*Math.Pow(p.SlabThickness,3)/12;
        double isFull=(p.FlangeWidth*Math.Pow(p.SteelHeight,3)-(p.FlangeWidth-p.WebThickness)*Math.Pow(p.SteelHeight-2*p.FlangeThickness,3))/12;
        double isMixed=isFull-2*p.FlangeWidth*Math.Pow(p.FlangeThickness,3)/12;
        double[] moments={actions.Steel.ToGlobalCoordinateSystem().M1,actions.Infinite.ToGlobalCoordinateSystem().M1,actions.Instant.ToGlobalCoordinateSystem().M1};
        double Expected(double y,bool concrete,int last)
        {
            double stress=0;
            for(int phase=0;phase<=last;phase++)
            {
                if(phase==0){if(!concrete)stress-=moments[0]*(y-ys)/isFull;continue;}
                double ec=Ec/(phase==1||phase==3?1+p.PhiLong:1);
                if(phase<3)
                {
                    double n=Es/ec,yg=(a*ys+ac/n*yc)/(a+ac/n),ii=isMixed+a*Math.Pow(ys-yg,2)+ic/n+ac/n*Math.Pow(yc-yg,2);
                    stress-=moments[phase]*(y-yg)/ii/(concrete?n:1);
                }
                else
                {
                    double eps=phase==3?p.ShrinkageLong:p.ShrinkageInstant,aa=Es*a+ec*ac,ab=Es*a*ys+ec*ac*yc,bb=Es*(isMixed+a*ys*ys)+ec*(ic+ac*yc*yc),det=aa*bb-ab*ab;
                    double b0=ec*ac*eps,b1=b0*yc,e=(b0*bb-b1*ab)/det,k=(aa*b1-ab*b0)/det;
                    stress+=(concrete?ec:Es)*(e+k*y-(concrete?eps:0));
                }
            }
            return stress;
        }
        for(int phase=0;phase<5;phase++)
        {
            var stress=new BridgeStressResult(response[0].Analysis,phase);
            double[] actual={stress.Point1_ConcreteSigma,stress.Point4_ConcreteSigma,stress.Point5_SteelSigma,stress.Point8_SteelSigma};
            var points=new[]{(p.SlabThickness,true),(0.0,true),(0.0,false),(-p.SteelHeight,false)};
            for(int i=0;i<4;i++)checks.Near($"Fase {phase+1} sigma punto {i+1} [MPa]",actual[i],Expected(points[i].Item1,points[i].Item2,phase),1e-6,1e-6);
            rows.Add(new(phase+1,actual[0],actual[1],actual[2],actual[3]));
        }
        var eccentric=Enumerable.Range(0,4*p.Panels).SelectMany(b=>results[2].BeamRows(b,"Q_ECC")).MaxBy(r=>Math.Abs(r.Torque));
        var f=BridgeForceConvention.FromStraus(R4cModel.Sample(eccentric),"Q_ECC",cs);
        checker.GetLinearAnalysisResultByStages(out var planar,out _,p.PhiLong,0,new[]{actions.Steel},new[]{actions.Infinite},new[]{f},true,new[]{0.0},new[]{0.0},false);
        var projected=R4cModel.Sample(eccentric) with {Torque=0,Bending1=0,Shear1=0};
        checker.GetLinearAnalysisResultByStages(out var reference,out _,p.PhiLong,0,new[]{actions.Steel},new[]{actions.Infinite},new[]{BridgeForceConvention.FromStraus(projected,"Q_ECC",cs)},true,new[]{0.0},new[]{0.0},false);
        checks.Near("BridgeChecks proiezione N-Mx-Vy della risposta eccentrica",planar[0].Point8_SteelSigma,reference[0].Point8_SteelSigma,0,1e-10);
        checks.Near("BridgeChecks ipotesi piana esplicita",planar[0].Assumptions.Contains(BridgeCheckAdapter.PlanarAssumption)?1:0,1,0,0);
        return new("G1, midspan, right-hand element end 1","G1 + G2 + Q_UDL + shrinkage long + imposed strain instant (unit factors)",rows,Math.Abs(eccentric.Torque));
    }
    /// <summary>nodes.csv and forces.csv of the former <c>WriteResults</c> from the GPC result rows: node and beam IDs of the recipe, positions of
    /// the recipe, every displacement result of every node with its vertical reaction, every beam row.</summary>
    private static void WriteResults(Parameters p,R4cModel.Results[] models,R4cModel.Results moving)
    {
        using var nodes=new StreamWriter(Path.Combine(Delivery,"nodes.csv"));nodes.WriteLine("model,case,node,x_mm,y_mm,uz_mm,rz_N");
        using var forces=new StreamWriter(Path.Combine(Delivery,"forces.csv"));forces.WriteLine("model,case,beam,x_mm,y_mm,station_mm,N_N,V1_N,V2_N,BM1_Nmm,BM2_Nmm,T_Nmm");
        foreach(var (m,index) in models.Append(moving).Select((m,i)=>(m,i)))
        {
            for(int k=0;k<m.Recipe.Nodes.Count;k++)foreach(var r in m.NodeRows(k).Where(r=>r.Displacement!=null))
            {
                var n=m.Recipe.Nodes[k];string name=m.Label(r.Entry);
                nodes.WriteLine(FormattableString.Invariant($"{index},{name},{k+1},{n.Position.X},{n.Position.Y},{r.Displacement[2]},{m.Reaction(k,name)[2]}"));
            }
            for(int b=0;b<m.Recipe.Beams.Count;b++)foreach(var r in m.BeamRows(b))
            {
                var from=m.Recipe.Nodes[m.Recipe.Beams[b].From].Position;
                forces.WriteLine(FormattableString.Invariant($"{index},{m.Label(r.Entry)},{m.Recipe.Beams[b].BeamId},{from.X},{from.Y},{r.Station},{r.Axial},{r.Shear1},{r.Shear2},{r.Bending1},{r.Bending2},{r.Torque}"));
            }
        }
    }
#endif
    // The former FEM models (kept for the transitional comparison r4c-inputs only: R4cGrafiInputs; P2 removes them with the FEM types).
    internal static ModelModel Create(Parameters p,int phase,double torsionFactor=1)
    {
        p.Validate(refined:true);var model=new ModelModel(ModelModel.ModelUnits.Nmm){Name="Ponte graticcio / "+PhaseNames[phase]};
        int n=p.Panels;double dx=p.Span/n;
        var primary=Case(PhaseNames[phase],phase);model.LoadCases.Add(primary);
        double q=phase==0?G1(p):(phase==1?p.PermanentPressure:p.TrafficPressure)*p.Width/4;
        for(int j=0;j<4;j++)for(int i=0;i<=n;i++)
        {
            var node=new NodeModel((j*(n+1)+i+1).ToString(),new(dx*i,GirderY(p,j),0));node.Groups.Add(Main);
            // Plane grillage: only Uz, Rx, Ry are active. All eight bearings restrain Uz.
            node.Attributes.Add(new NodeRestraintAttributeModel(true,true,i==0||i==n,false,false,true));
            node.Loads.Add(new NodeForceLoadModel(primary,new(0,0,-q*dx*(i==0||i==n?.5:1))));
            model.AddElements(new[]{node});
        }
        var longitudinal=LongProperty(p,phase,torsionFactor);
        for(int j=0;j<4;j++)for(int i=0;i<n;i++)
        {
            var b=new BeamModel(model.Nodes[j*(n+1)+i].Position,model.Nodes[j*(n+1)+i+1].Position,longitudinal,Main){BeamId=(j*n+i+1).ToString()};
            b.Attributes.Add(new SuperElementAttributeModel("G"+(j+1)));
            model.AddElements(new[]{b});
        }
        for(int i=0;i<=n;i++)
        {
            var prop=CrossProperty(p,phase,i,torsionFactor);if(prop==null)continue;
            for(int j=0;j<3;j++)model.AddElements(new[]{new BeamModel(model.Nodes[j*(n+1)+i].Position,model.Nodes[(j+1)*(n+1)+i].Position,prop,Cross){BeamId=(4*n+3*i+j+1).ToString()}});
        }
        if(phase==2)
        {
            foreach(var entry in new[]{("Q_ECC",2), ("Q_MIRROR",0)})
            {
                var lc=Case(entry.Item1,2);model.LoadCases.Add(lc);
                foreach(double x in new[]{p.Span/2-p.AxleSpacing/2,p.Span/2+p.AxleSpacing/2})
                    for(int j=entry.Item2;j<entry.Item2+2;j++)AddPoint(model,p,lc,x,j,p.VehicleWeight/4);
            }
        }
        return model;
    }
    private static void AddPoint(ModelModel m,Parameters p,LoadCaseModel lc,double x,int girder,double weight)
    {
        double f=x/p.Span*p.Panels;int i=(int)Math.Floor(f);double t=f-i;
        // Explicit linear tributary load distribution, preserving force and moment.
        m.Nodes[girder*(p.Panels+1)+i].Loads.Add(new NodeForceLoadModel(lc,new(0,0,-weight*(1-t))));
        m.Nodes[girder*(p.Panels+1)+i+1].Loads.Add(new NodeForceLoadModel(lc,new(0,0,-weight*t)));
    }
    internal static MovingPath Traffic(Parameters p) => new()
    {
        LoadCaseName="VEICOLO_MOBILE",Target=2,Divisions=p.MovingPositions,ConvertToStaticCases=true,
        Coordinates=new[]{0.0,p.Width/4,0,p.Span,p.Width/4,0,0,p.Width/4+1000,0},
        Template=new(){Name="Veicolo dimostrativo 2 assi",Vehicles=new[]{new MovingVehicle{Name="300 kN (non normativo)",Loads=(from x in new[]{-p.AxleSpacing/2,p.AxleSpacing/2} from y in new[]{-p.Width/8,p.Width/8} select (MovingObject)new MovingPointForce{X=x,Y=y,Force=new[]{0.0,0,-p.VehicleWeight/4}}).ToArray()}}}
    };
#if !GPC_PILOT
    internal static ModelModel RunModel(ModelModel model,string name,int solver=0,bool moving=false,string[] cases=null)
    {
        string folder=Path.Combine(Output,name);Directory.CreateDirectory(folder);
        File.WriteAllBytes(Path.Combine(folder,"input.bgmodel"),ModelArchive.Write(model));
        var result=StrausAnalysis.Run(model,Path.Combine(folder,"model.st7"),solver,cases??model.LoadCases.Select(c=>c.Name).ToArray(),1e-6,moving,out var warnings);
        File.WriteAllBytes(Path.Combine(folder,"results.bgmodel"),ModelArchive.Write(result));
        File.WriteAllText(Path.Combine(folder,"warnings.json"),JsonSerializer.Serialize(warnings,Json));
        return result;
    }
#endif
    internal static NodeDisplResultModel Displ(NodeModel node,string name)=>node.Results.OfType<NodeDisplResultModel>().Single(r=>r.LoadCase.Name==name);
    internal static NodeReactResultModel Reaction(NodeModel node,string name)=>node.Results.OfType<NodeReactResultModel>().Single(r=>r.LoadCase.Name==name);
#if !GPC_PILOT
    internal static double Midspan(ModelModel m,Parameters p,string name,int girder=0)=>Displ(m.Nodes[girder*(p.Panels+1)+p.Panels/2],name).Displacement.Z;
    private static void Equilibrium(ModelModel input,ModelModel result,string name,NativeExamples.Recorder checks)
    {
        double[] residual=new double[3],scale=new double[3];
        foreach(var node in result.Nodes)
        {
            var r=Reaction(node,name);var f=input.Nodes.Single(n=>n.NodeId==node.NodeId).Loads.OfType<NodeForceLoadModel>().Where(l=>l.LoadCase.Name==name).Sum(l=>l.Value.Z);
            double[] actions={r.Force.Z+f,r.Force.Z*node.Position.X-r.Moment.Y+f*node.Position.X,r.Force.Z*node.Position.Y+r.Moment.X+f*node.Position.Y};
            double[] loads={f,f*node.Position.X,f*node.Position.Y};
            for(int i=0;i<3;i++){residual[i]+=actions[i];scale[i]+=Math.Abs(loads[i]);}
        }
        for(int i=0;i<3;i++)checks.Near(name+" equilibrio "+new[]{"Fz","My","Mx"}[i],residual[i],0,0,Math.Max(1e-5,scale[i]*1e-8));
    }
    private static double AnalyticMidspan(Parameters p,int phase)
    {
        double q=phase==0?G1(p):(phase==1?p.PermanentPressure:p.TrafficPressure)*p.Width/4;
        double ei=Es*LongProperty(p,phase).Section.I11,dx=p.Span/p.Panels,value=0;
        // Simply supported point-load Green function at L/2, no beam shape functions.
        for(int i=1;i<p.Panels;i++)
        {
            double a=Math.Min(i*dx,p.Span-i*dx);
            value-=q*dx*a*(3*p.Span*p.Span-4*a*a)/(48*ei);
        }
        return value;
    }
    internal static int Run(bool grasshopper)
    {
        CultureInfo.CurrentCulture=CultureInfo.InvariantCulture;
        var p=JsonSerializer.Deserialize<Parameters>(File.ReadAllText(Path.Combine(Delivery,"parameters.json")));p.Validate();
        Directory.CreateDirectory(Output);var checks=new NativeExamples.Recorder();
        var inputs=Enumerable.Range(0,3).Select(i=>Create(p,i)).ToArray();
        var results=new ModelModel[3];
        for(int phase=0;phase<3;phase++)
        {
            results[phase]=RunModel(inputs[phase],"0"+(phase+1)+"-"+PhaseNames[phase]);
            foreach(var c in inputs[phase].LoadCases)Equilibrium(inputs[phase],results[phase],c.Name,checks);
            for(int j=0;j<4;j++)checks.Near(PhaseNames[phase]+" G"+(j+1)+" freccia analitica [mm]",Midspan(results[phase],p,PhaseNames[phase],j),AnalyticMidspan(p,phase),1e-7,1e-6);
        }
        var instant=results[2];
        for(int j=0;j<4;j++)for(int i=0;i<=p.Panels;i++)checks.Near($"Specularita veicolo nodo {j}/{i}",Displ(instant.Nodes[j*(p.Panels+1)+i],"Q_ECC").Displacement.Z,Displ(instant.Nodes[(3-j)*(p.Panels+1)+i],"Q_MIRROR").Displacement.Z,1e-6,1e-7);
        double Work(string a,string b)=>instant.Nodes.Sum(node=>inputs[2].Nodes.Single(n=>n.NodeId==node.NodeId).Loads.OfType<NodeForceLoadModel>().Where(l=>l.LoadCase.Name==a).Sum(l=>l.Value.Z)*Displ(node,b).Displacement.Z);
        checks.Near("Reciprocita Maxwell-Betti [N mm]",Work("Q_ECC","Q_UDL"),Work("Q_UDL","Q_ECC"),1e-8);
        var movingInput=Create(p,2);movingInput.MovingPaths.Add(Traffic(p));
        var moving=RunModel(movingInput,"04-mobile",moving:true,cases:Array.Empty<string>());
        checks.Near("Posizioni veicolo generate (inclusi ingresso/uscita)",moving.GeneratedMovingCases.Count,MovingCaseCount(p),0,0);
        int pos=0;
        foreach(string name in moving.GeneratedMovingCases.OrderBy(k=>k.Key).Select(k=>k.Value))
        {
            // Straus starts with the front axle, then advances by a path division.
            // Axles outside the path do not contribute, even during entry/exit.
            double front=(++pos-.5)*p.Span/p.MovingPositions;
            var axles=new[]{front,front-p.AxleSpacing}.Where(x=>x>=0&&x<=p.Span).ToArray();
            checks.Near(name+" somma Rz [N]",moving.Nodes.Sum(n=>Reaction(n,name).Force.Z),p.VehicleWeight/2*axles.Length,1e-7);
            checks.Near(name+" momento globale Y [N mm]",moving.Nodes.Sum(n=>Reaction(n,name).Force.Z*n.Position.X-Reaction(n,name).Moment.Y),p.VehicleWeight/2*axles.Sum(),1e-7);
            checks.Near(name+" momento globale X [N mm]",moving.Nodes.Sum(n=>Reaction(n,name).Force.Z*n.Position.Y+Reaction(n,name).Moment.X),p.VehicleWeight/2*axles.Length*p.Width/4,1e-7);
        }
        var staged=Create(p,0);
        // NLA must allow longitudinal shortening; the planar LSA suppression of
        // all in-plane DOFs would introduce artificial membrane stiffening.
        foreach(var node in staged.Nodes)
        {
            bool end=node.Position.X==0||node.Position.X==p.Span;
            node.Attributes.Clear();
            node.Attributes.Add(new NodeRestraintAttributeModel(node.Position.X==0&&node.Position.Y==GirderY(p,0),end,end,false,false,false));
        }
        string[] stageNames=Enumerable.Range(1,5).Select(i=>"GETTO_"+i).ToArray();
        for(int i=0;i<5;i++)
        {
            var combo=new LoadCombinationModel(stageNames[i]);combo.Values.Add(staged.LoadCases.Single(),(i+1)/5.0);
            var stage=new StagedConstructionModel(stageNames[i]){Marph=true,Rotate=true};stage.AddGroups(new[]{Main,Cross});stage.AddLoadCombinations(new[]{combo});staged.AddStagedConstructions(new[]{stage});
        }
        stageNames=StrausResultSelection.Catalog(staged,2).Select(e=>e.Case.Name).ToArray();
        var stages=RunModel(staged,"05-getto-cinque-fasi",2,cases:stageNames);
        for(int i=0;i<5;i++)
        {
            // NLA retains finite rotations; compare to the small-deflection reference
            // within 0.001%, consistent with this example's displacement/span ratio.
            checks.Near(stageNames[i]+" freccia progressiva [mm]",Midspan(stages,p,stageNames[i]),AnalyticMidspan(p,0)*(i+1)/5,1e-5,1e-5);
            checks.Near(stageNames[i]+" equilibrio verticale [N]",stages.Nodes.Sum(n=>Reaction(n,stageNames[i]).Force.Z),4*G1(p)*p.Span*(i+1)/5,1e-6);
        }
        var meshes=new List<object>();double previous=Midspan(instant,p,"Q_ECC",3);
        meshes.Add(new{Panels=p.Panels,Uz=previous});
        foreach(int factor in new[]{2,4})
        {
            var refined=p with {Panels=p.Panels*factor};var r=RunModel(Create(refined,2),"mesh-"+refined.Panels);
            double d=Midspan(r,refined,"Q_ECC",3);meshes.Add(new{Panels=refined.Panels,Uz=d});
            checks.Near("Convergenza mesh "+refined.Panels+" Uz [mm]",previous,d,.02,1e-6);previous=d;
        }
        var sensitivity=new List<object>();
        foreach(double factor in new[]{.5,2.0})
        {
            var r=RunModel(Create(p,2,factor),"torsione-"+factor.ToString(CultureInfo.InvariantCulture));
            sensitivity.Add(new{GJFactor=factor,Uz=Midspan(r,p,"Q_ECC",3)});
            Equilibrium(Create(p,2,factor),r,"Q_ECC",checks);
        }
        var stresses=CheckSections(p,results,checks);
        WriteResults(p,results,moving);
        if(grasshopper)GrasshopperBridgeExample.Run(p,inputs,results,stresses,checks);
        var report=new{Date=DateTimeOffset.Now,Parameters=p,Ec,SteelArea=GpcH(p).Area,LineG1=G1(p),Nodes=inputs[2].Nodes.Count,Beams=inputs[2].Beams.Count,
            StageMethod="Independent linear stiffness states; native wet-concrete stages are a separate benchmark, not summed into BridgeChecks.",
            Scope="SLS normal/shear stresses only for symmetric combination. Eccentric traffic torsion preserved; full mixed-action design NOT verified.",
            MidspanSteel=Midspan(results[0],p,PhaseNames[0]),MidspanLong=Midspan(results[1],p,PhaseNames[1]),MidspanLive=Midspan(results[2],p,PhaseNames[2]),
            Mesh=meshes,TorsionSensitivity=sensitivity,Stresses=stresses,Checks=checks.Measurements};
        File.WriteAllText(Path.Combine(Delivery,"validation.json"),JsonSerializer.Serialize(report,Json));
        File.WriteAllText(Path.Combine(Output,"validation.json"),JsonSerializer.Serialize(report,Json));
        int failed=checks.Measurements.Count(c=>!c.Passed);Console.WriteLine($"BRIDGE GRILLAGE: {checks.Measurements.Count-failed} passed, {failed} failed");return failed==0?0:1;
    }
#endif

    internal static int MovingCaseCount(Parameters p)=>(int)Math.Floor(p.MovingPositions+.5+p.AxleSpacing*p.MovingPositions/p.Span);

#if !GPC_PILOT
    private static SectionReport CheckSections(Parameters p,ModelModel[] results,NativeExamples.Recorder checks)
    {
        var property=Composite(p);var section=property.GetReinforcedConcreteSection();
        var cs=new GPC.Geometry.CoordinateSystem(GPC.Geometry.Point3d.Origin,new GPC.Geometry.Vector3d(-1,0,0),new GPC.Geometry.Vector3d(0,-1,0));
        var options=new GPC.Checkers.CompositeBridge.BridgeAnalysisOptions { Standard=GPC.Checkers.CompositeBridge.BridgeStandard.Eurocode4, LimitState=GPC.Checkers.CompositeBridge.BridgeLimitState.Rare, WebBuckling=false,TopFlangeBuckling=false,BottomFlangeBuckling=false};
        var checker=new BridgeCheckAdapter(section,cs, options);
        var beam=new BeamModel(results[0].Beams[p.Panels/2]);beam.Results.Clear();beam.Loads.Clear();
        for(int k=0;k<3;k++)foreach(var force in results[k].Beams[p.Panels/2].Results.OfType<BeamForceResultModel>().Where(r=>r.LoadCase.Name==PhaseNames[k]))beam.Results.Add(ModelArchive.Clone(force));
        var combo=new LoadCombinationModel("SLS_SIMMETRICO");for(int k=0;k<3;k++)combo.Values.Add(results[k].LoadCases.Single(c=>c.Name==PhaseNames[k]),1);
        foreach(var entry in new[]{("RITIRO_INF",p.ShrinkageLong,LoadCaseModel.Stage.BridgePhases.StrainInfinite),("DEF_IST",p.ShrinkageInstant,LoadCaseModel.Stage.BridgePhases.StrainInstant)})
        {
            var lc=new LoadCaseModel(entry.Item1,entry.Item1){Stages=new(){new(entry.Item1){BridgePhase=entry.Item3}}};
            beam.Loads.Add(new CompositeBeamSlabStrainModel(lc,new(){Strain=entry.Item2}));combo.Values.Add(lc,1);
        }
        var actions=BridgePhaseAssembler.Assemble(beam,beam.Length,0,combo,cs,1e-6);
        checker.GetLinearAnalysisResultByStages(out var response,out _,p.PhiLong,0,new[]{actions.Steel},new[]{actions.Infinite},new[]{actions.Instant},true,new[]{actions.StrainInfinite},new[]{actions.StrainInstant},false);
        checks.Near("BridgeChecks cinque fasi accumulate",response[0].Analysis.Stages.Count,5,0,0);
        var rows=new List<StressRow>();
        // Independent elastic section integrals, including the current gPC thin-wall flange convention.
        double a=GpcH(p).Area,ys=-p.SteelHeight/2,yc=p.SlabThickness/2,ac=p.Width/4*p.SlabThickness,ic=p.Width/4*Math.Pow(p.SlabThickness,3)/12;
        double isFull=(p.FlangeWidth*Math.Pow(p.SteelHeight,3)-(p.FlangeWidth-p.WebThickness)*Math.Pow(p.SteelHeight-2*p.FlangeThickness,3))/12;
        double isMixed=isFull-2*p.FlangeWidth*Math.Pow(p.FlangeThickness,3)/12;
        double[] moments={actions.Steel.ToGlobalCoordinateSystem().M1,actions.Infinite.ToGlobalCoordinateSystem().M1,actions.Instant.ToGlobalCoordinateSystem().M1};
        double Expected(double y,bool concrete,int last)
        {
            double stress=0;
            for(int phase=0;phase<=last;phase++)
            {
                if(phase==0){if(!concrete)stress-=moments[0]*(y-ys)/isFull;continue;}
                double ec=Ec/(phase==1||phase==3?1+p.PhiLong:1);
                if(phase<3)
                {
                    double n=Es/ec,yg=(a*ys+ac/n*yc)/(a+ac/n),ii=isMixed+a*Math.Pow(ys-yg,2)+ic/n+ac/n*Math.Pow(yc-yg,2);
                    stress-=moments[phase]*(y-yg)/ii/(concrete?n:1);
                }
                else
                {
                    double eps=phase==3?p.ShrinkageLong:p.ShrinkageInstant,aa=Es*a+ec*ac,ab=Es*a*ys+ec*ac*yc,bb=Es*(isMixed+a*ys*ys)+ec*(ic+ac*yc*yc),det=aa*bb-ab*ab;
                    double b0=ec*ac*eps,b1=b0*yc,e=(b0*bb-b1*ab)/det,k=(aa*b1-ab*b0)/det;
                    stress+=(concrete?ec:Es)*(e+k*y-(concrete?eps:0));
                }
            }
            return stress;
        }
        for(int phase=0;phase<5;phase++)
        {
            var stress=new BridgeStressResult(response[0].Analysis,phase);
            double[] actual={stress.Point1_ConcreteSigma,stress.Point4_ConcreteSigma,stress.Point5_SteelSigma,stress.Point8_SteelSigma};
            var points=new[]{(p.SlabThickness,true),(0.0,true),(0.0,false),(-p.SteelHeight,false)};
            for(int i=0;i<4;i++)checks.Near($"Fase {phase+1} sigma punto {i+1} [MPa]",actual[i],Expected(points[i].Item1,points[i].Item2,phase),1e-6,1e-6);
            rows.Add(new(phase+1,actual[0],actual[1],actual[2],actual[3]));
        }
        var eccentric=results[2].Beams.Take(4*p.Panels).SelectMany(b=>b.Results.OfType<BeamForceResultModel>()).Where(r=>r.LoadCase.Name=="Q_ECC").MaxBy(r=>Math.Abs(r.Torque));
        var f=BridgeForceConvention.FromStraus(eccentric,cs);
        checker.GetLinearAnalysisResultByStages(out var planar,out _,p.PhiLong,0,new[]{actions.Steel},new[]{actions.Infinite},new[]{f},true,new[]{0.0},new[]{0.0},false);
        var projected=ModelArchive.Clone(eccentric);projected.Torque=projected.Mx=projected.Vx=0;
        checker.GetLinearAnalysisResultByStages(out var reference,out _,p.PhiLong,0,new[]{actions.Steel},new[]{actions.Infinite},new[]{BridgeForceConvention.FromStraus(projected,cs)},true,new[]{0.0},new[]{0.0},false);
        checks.Near("BridgeChecks proiezione N-Mx-Vy della risposta eccentrica",planar[0].Point8_SteelSigma,reference[0].Point8_SteelSigma,0,1e-10);
        checks.Near("BridgeChecks ipotesi piana esplicita",planar[0].Assumptions.Contains(BridgeCheckAdapter.PlanarAssumption)?1:0,1,0,0);
        return new("G1, midspan, right-hand element end 1","G1 + G2 + Q_UDL + shrinkage long + imposed strain instant (unit factors)",rows,Math.Abs(eccentric.Torque));
    }
    private static void WriteResults(Parameters p,ModelModel[] models,ModelModel moving)
    {
        using var nodes=new StreamWriter(Path.Combine(Delivery,"nodes.csv"));nodes.WriteLine("model,case,node,x_mm,y_mm,uz_mm,rz_N");
        using var forces=new StreamWriter(Path.Combine(Delivery,"forces.csv"));forces.WriteLine("model,case,beam,x_mm,y_mm,station_mm,N_N,V1_N,V2_N,BM1_Nmm,BM2_Nmm,T_Nmm");
        foreach(var (m,index) in models.Append(moving).Select((m,i)=>(m,i)))
        {
            foreach(var n in m.Nodes)foreach(var r in n.Results.OfType<NodeDisplResultModel>())nodes.WriteLine(FormattableString.Invariant($"{index},{r.LoadCase.Name},{n.NodeId},{n.Position.X},{n.Position.Y},{r.Displacement.Z},{Reaction(n,r.LoadCase.Name).Force.Z}"));
            foreach(var b in m.Beams)foreach(var r in b.Results.OfType<BeamForceResultModel>())forces.WriteLine(FormattableString.Invariant($"{index},{r.LoadCase.Name},{b.BeamId},{b.PointFrom.X},{b.PointFrom.Y},{r.Station},{r.AxialForce},{r.Vx},{r.Vy},{r.Mx},{r.My},{r.Torque}"));
        }
    }
#endif
}
