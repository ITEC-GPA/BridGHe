using System;
using System.Linq;
using BridGHe.Common.Checks;
using GPC.Checkers.CompositeBridge;
using GPC.Geometry;
using GPC.Model.Materials;
using GPC.Model.Results;
using GPC.Model.Sections;

internal static class BridgeLinearSmallLoadsTests
{
    internal static void Run()
    {
        var concrete=new ConcreteMaterialEN1992("C30",30,ConcreteMaterial.CompressionStressStrainDiagrams.ParabolaRectangle);
        var steel=new SteelMaterialEN1992("S355",210000,355,510,.1,SteelMaterial.StressStrainCurveType.ElasticPerfectPlastic,SteelMaterial.SteelTypes.Structural);
        var section=CompositeSectionFactory.Create(2200,250,concrete,null,100,40,null,100,new SectionH(1400,16,450,30,600,40,"H"),steel,40,0,"Small loads");
        var axes=CoordinateSystem.Global;
        var checker=new BridgeCheckAdapter(section,axes,new BridgeAnalysisOptions{Class4=false});
        ResultBeamForces Force(double n,double m)=>new(n,0,0,0,m,0,axes);
        int count=0;
        foreach(double sign in new[]{-1d,1d})
        foreach(var load in new[]{(N:0d,M:0d),(N:2e-5,M:0d),(N:0d,M:2e-5),(N:2e-4,M:1e-4),(N:2e-3,M:1d),(N:2d,M:1000d)})
        {
            double n=sign*load.N,m=sign*load.M;
            checker.GetLinearAnalysisResultByStages(out var rows,out _,2,0,new[]{Force(0,0)},new[]{Force(0,0)},new[]{Force(n,m)},true,new[]{0d},new[]{0d},false);
            var c=rows.Single().Stage.Contributions.Last();
            double integratedN=c.UniformStress*c.Area;
            double integratedM=-c.StressSlope*c.SolverInertia-integratedN*(c.Centroid-c.LoadY);
            if(!double.IsFinite(integratedN)||!double.IsFinite(integratedM)||Math.Abs(integratedN-n)>1e-10*Math.Max(1,Math.Abs(n))||Math.Abs(integratedM-m)>1e-9*Math.Max(1,Math.Abs(m)))
                throw new Exception($"Small load lost: target N={n:G17} N, M={m:G17} Nmm; integrated N={integratedN:G17}, M={integratedM:G17}.");
            count+=2;
        }
        Console.WriteLine($"SMALL LINEAR LOADS PASS: {count} independent force/moment integrations, both signs and exact zero.");
    }
}
