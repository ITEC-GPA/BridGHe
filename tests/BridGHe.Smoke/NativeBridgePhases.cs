using System;
using System.IO;
using System.Linq;
using BridGHe.Common.Checks;
using BridGHe.Common.Gpc;
using GPC.Checkers.Concrete.Attributes;
using GPC.Checkers.Concrete.SectionSolvers;
using GPC.Model.Materials;
using GPC.Model.Sections;
using GPC.Model.Standards;

/// <summary>Native end moments through the BridgeChecks adapter, checked against independent section integrals. R6.6 lane R4b: on GPC data alone
/// (<see cref="R4bInputs.PhasesCantilever"/>, the GPC-only analysis): the actions of the five phases at the end of the beam assembled from its GPC
/// result rows (<see cref="BridgePhaseAssembler.CreateIndex(string, System.Collections.Generic.IEnumerable{ValueTuple{string, BeamForceSample}}, double)"/>,
/// the path of Super Element Check on the GPC bridge model) with the phase of each case from the recipe; the imposed strains of the two strain
/// phases are those of the recipe (the GPC workflow executor takes them from it: the GPC index has no slab strain load).</summary>
internal static class NativeBridgePhases
{
    internal static int Run()
    {
        const double es=210000,asSteel=10600,isSteel=(200.0*300*300*300-190.0*260*260*260)/12;
        const double ac=1000*200,ic=1000.0*200*200*200/12,ys=-150,yc=100;
        // The current gPC composite solver integrates flange walls on their
        // centre lines. Keep its documented thin-wall idealization explicit.
        const double isComposite=isSteel-2*200.0*20*20*20/12;
        var concrete=new ConcreteMaterialEN1992("C30",30,ConcreteMaterial.CompressionStressStrainDiagrams.ParabolaRectangle);
        var steel=new SteelMaterialEN1992("S355",es,355,510,.1,SteelMaterial.StressStrainCurveType.ElasticPerfectPlastic,SteelMaterial.SteelTypes.Structural);
        var h=new SectionH(300,10,200,20,200,20,"H");
        var section=CompositeSectionFactory.Create(1000,200,concrete,null,100,50,null,100,h,steel,50,0,"Bridge phase test");
        var cs=new GPC.Geometry.CoordinateSystem(GPC.Geometry.Point3d.Origin,new GPC.Geometry.Vector3d(-1,0,0),new GPC.Geometry.Vector3d(0,-1,0));
        var options=new GPC.Checkers.CompositeBridge.BridgeAnalysisOptions { Standard=GPC.Checkers.CompositeBridge.BridgeStandard.Eurocode4, LimitState=GPC.Checkers.CompositeBridge.BridgeLimitState.Rare, WebBuckling=false,TopFlangeBuckling=false,BottomFlangeBuckling=false};
        var checker=new BridgeCheckAdapter(section,cs, options);
        var input=R4bInputs.PhasesCantilever(es,asSteel,isSteel,2*20*Math.Pow(200,3)/12+260*Math.Pow(10,3)/12);
        var phases=R4bInputs.PhaseCases.Select(c=>c.Name).ToArray();
        string folder=Path.Combine(ExamplePaths.Calculations("22-fasi-sezione-gpc"),"fasi-sezione");Directory.CreateDirectory(folder);
        var analysed=BridGHe.Common.Straus.StrausAnalysis.Run(input,Path.Combine(folder,"moments.st7"),0,phases.Take(3).ToArray(),false,out var warnings);
        var result=new GpcNativeExamples.Results(analysed);
        var recorder=new NativeExamples.Recorder();
        // F6A: read with the GPC Converter API (the model is written by the Converter; the former St7API binding cannot share the process).
        string scratch=Path.Combine(Path.GetTempPath(),"bridghe-axes-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(scratch);
        try{
            using var api=new GPC.Converter.Straus7.Straus7NativeApi();
            api.OpenModelReadOnly(Path.GetFullPath(Path.Combine(folder,"moments.st7")),scratch);
            var axes=api.ReadElement(GPC.Converter.Straus7.Straus7Entity.Beam,1).InitialAxes;
            recorder.Near("Native principal axis 1 along +Y",axes[1],1);
            recorder.Near("Native principal axis 2 along +Z",axes[5],1);
            recorder.Near("Native principal axis 3 along +X",axes[6],1);
            api.CloseModel();
        }finally{try{Directory.Delete(scratch,true);}catch(IOException){}catch(UnauthorizedAccessException){}}
        recorder.Near("Upward deflection for sagging tip couple [mm]",result.Displacement(1,"OnlySteel")[2],1e6*3000*3000/(2*es*isSteel));
        // The index of the beam from its GPC result rows (station in mm, the numbers of the Results components), the phase of each case from the recipe.
        var samples=phases.Take(3).SelectMany(p=>result.BeamRows(0,p).Select(r=>(p,new BeamForceSample(r.Station,GpcResultRows.Shown(r.Axial),GpcResultRows.Shown(r.Shear1),
            GpcResultRows.Shown(r.Shear2),GpcResultRows.Shown(r.Torque),GpcResultRows.Shown(r.Bending1),GpcResultRows.Shown(r.Bending2))))).ToArray();
        var index=BridgePhaseAssembler.CreateIndex(analysed.BeamElements.Values.Single().Name,samples,1e-6);
        var actions=index.Assemble(3000,phases.Select(p=>(p,1.0)),R4bInputs.PhaseCases.ToDictionary(c=>c.Name,c=>c.Phase,StringComparer.Ordinal),cs);
        recorder.Near("Native steel moment -> gPC M1 [N mm]",actions.Steel.ToGlobalCoordinateSystem().M1,1e6);
        recorder.Near("Native infinite moment -> gPC M1 [N mm]",actions.Infinite.ToGlobalCoordinateSystem().M1,2e6);
        recorder.Near("Native instant moment -> gPC M1 [N mm]",actions.Instant.ToGlobalCoordinateSystem().M1,3e6);
        // The imposed strains of the recipe (before: the slab strain loads of the analysed FEM beam, summed by the FEM index).
        const double strainInfinite=-100e-6,strainInstant=-50e-6;
        recorder.Near("No strain from the GPC index (the strains come from the recipe)",Math.Abs(actions.StrainInfinite)+Math.Abs(actions.StrainInstant),0,0,0);
        checker.GetLinearAnalysisResultByStages(out var results,out _,2,0,new[]{actions.Steel},new[]{actions.Infinite},new[]{actions.Instant},true,new[]{strainInfinite},new[]{strainInstant},false);
        var analysis=results[0].Analysis;
        double ec=concrete.ElasticModulusCompression;
        double Mechanical(double y,bool isConcrete,double moment,double phi,bool steelOnly)
        {
            if(steelOnly)return isConcrete?0:-moment*(y-ys)/isSteel;
            double n=es/(ec/(1+phi)),centroid=(asSteel*ys+ac/n*yc)/(asSteel+ac/n);
            double inertia=isComposite+asSteel*Math.Pow(ys-centroid,2)+ic/n+ac/n*Math.Pow(yc-centroid,2);
            return -moment*(y-centroid)/inertia/(isConcrete?n:1);
        }
        double Shrinkage(double y,bool isConcrete,double strain,double phi)
        {
            double e=ec/(1+phi),aa=es*asSteel+e*ac,ab=es*asSteel*ys+e*ac*yc,bb=es*(isComposite+asSteel*ys*ys)+e*(ic+ac*yc*yc);
            double b0=e*ac*strain,b1=b0*yc,det=aa*bb-ab*ab;
            double epsilon=(b0*bb-b1*ab)/det,kappa=(aa*b1-ab*b0)/det;
            return (isConcrete?e:es)*(epsilon+kappa*y-(isConcrete?strain:0));
        }
        var points=new[]{(Name:"Top concrete",Y:200.0,Concrete:true),(Name:"Bottom concrete",Y:0.0,Concrete:true),(Name:"Top steel",Y:0.0,Concrete:false),(Name:"Bottom steel",Y:-300.0,Concrete:false)};
        recorder.Near("Five accumulated phases",analysis.Stages.Count,5,0,0);
        for(int stage=0;stage<5;stage++)
        {
            var response=new BridgeStressResult(analysis,stage);
            var measured=new[]{response.Point1_ConcreteSigma,response.Point4_ConcreteSigma,response.Point5_SteelSigma,response.Point8_SteelSigma};
            for(int p=0;p<points.Length;p++)
            {
                var point=points[p];double expected=0;
                for(int phase=0;phase<=stage;phase++)expected+=phase<3?Mechanical(point.Y,point.Concrete,(phase+1)*1e6,phase==1?2:0,phase==0):Shrinkage(point.Y,point.Concrete,phase==3?-100e-6:-50e-6,phase==3?2:0);
                recorder.Near($"{phases[stage]} / {point.Name} [MPa]",measured[p],expected,1e-6,1e-7);
            }
        }
        // The input (BGHGPC1) and the analysed model (BGHR2) in the calculation folder (before: the FEM results archive results.bgmodel).
        var (data,records)=GpcDraftArchive.Write(input);
        File.WriteAllBytes(Path.Combine(folder,"input.gpc"),data);File.WriteAllBytes(Path.Combine(folder,"input.records"),records);
        GpcResultsFile.Write(Path.Combine(folder,"results.bghr"),analysed);
        File.WriteAllText(Path.Combine(folder,"report.json"),System.Text.Json.JsonSerializer.Serialize(new{Date=DateTimeOffset.Now,Warnings=warnings,recorder.Measurements},new System.Text.Json.JsonSerializerOptions{WriteIndented=true}));
        return recorder.Measurements.All(m=>m.Passed)?0:1;
    }
}
