using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using BridGHe.Common.Checks;
using GPC.Geometry;
using GPC.Model.Results;
using GPC.Model.Materials;
using GPC.Model.Sections;

internal static class BridgeNumericalBaseline
{
    private const string Path = "tests/BridGHe.Smoke/Fixtures/bridge-numerical-baseline.json";
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true,
        NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals };

    internal static Dictionary<string, double[]> Evaluate()
    {
        var concrete = new ConcreteMaterialEN1992("C30",30,ConcreteMaterial.CompressionStressStrainDiagrams.ParabolaRectangle);
        var steel = new SteelMaterialEN1992("S355",210000,355,510,.1,SteelMaterial.StressStrainCurveType.ElasticPerfectPlastic,SteelMaterial.SteelTypes.Structural);
        var section = CompositeSectionFactory.Create(2200,250,concrete,null,100,40,null,100,
            new SectionH(1400,16,450,30,600,40,"H"),steel,40,0,"Numerical baseline");
        var reference = CoordinateSystem.Global;
        var data = new Dictionary<string,double[]>();
        foreach (bool buckling in new[] { false, true })
        {
            var options = new GPC.Checkers.CompositeBridge.BridgeAnalysisOptions { Standard=GPC.Checkers.CompositeBridge.BridgeStandard.Eurocode4, LimitState=GPC.Checkers.CompositeBridge.BridgeLimitState.Rare,  WebBuckling=buckling,TopFlangeBuckling=buckling,BottomFlangeBuckling=buckling };
            var checker = new BridgeCheckAdapter(section,
                reference, options);
            foreach (double sign in new[] { -1.0, 0, 1 })
            foreach (bool slab in new[] { false, true })
            {
                ResultBeamForces Force(double scale) => new(sign*120000*scale,0,sign*350000*scale,0,sign*8e8*scale,0,reference);
                checker.GetLinearAnalysisResultByStages(out var results,out _,2,0,new[]{Force(1)},new[]{Force(.4)},
                    new[]{Force(.8)},slab,new[]{slab?-0.0002:0},new[]{0.0},true);
                data.Add($"buckling={buckling};sign={sign};slab={slab}", Values(results[0]));
            }
            data.Add($"shrinkage;buckling={buckling}",Values(checker.GetSelfTension(-0.0002,2)));
        }
        return data;
    }

    private static double[] Values(BridgeStressResult r) => new[] {
        r.Point1_ConcreteSigma,r.Point1_ConcreteEpsilon,r.Point2_RebarSigma,r.Point3_RebarSigma,
        r.Point4_ConcreteSigma,r.Point5_SteelSigma,r.Point6_SteelSigma,r.Point7_SteelSigma,
        r.Point8_SteelSigma,r.Point8_SteelEpsilon,r.Point6_SteelTau,r.Point7_SteelTau,
        r.Point6_SteelSigmaId,r.Point7_SteelSigmaId,r.ShearAt(-200),r.ShearAt(-700),r.ShearAt(-1200) };

    internal static void Capture() => File.WriteAllText(Path,JsonSerializer.Serialize(Evaluate(),Json));
    internal static void Verify()
    {
        var expected=JsonSerializer.Deserialize<Dictionary<string,double[]>>(File.ReadAllText(Path),Json);
        var actual=Evaluate();int exact=0,count=0;
        foreach(var pair in expected)
        {
            if(!actual.TryGetValue(pair.Key,out var values)||values.Length!=pair.Value.Length)
                throw new InvalidOperationException("Missing numerical baseline case: "+pair.Key);
            for(int i=0;i<values.Length;i++)
            {
                double a=values[i],b=pair.Value[i];count++;
                if(a.Equals(b)){exact++;continue;}
                if(!double.IsFinite(a)||!double.IsFinite(b)||Math.Abs(a-b)>1e-12*Math.Max(1,Math.Abs(b)))
                    throw new InvalidOperationException($"Numerical baseline changed: {pair.Key}[{i}] expected {b:R}, actual {a:R}");
            }
        }
        if(actual.Count!=expected.Count)throw new InvalidOperationException("Unexpected numerical baseline cases.");
        Console.WriteLine($"NUMERICAL BASELINE: {count} values, {exact} exactly equal; relative/absolute tolerance 1e-12.");
    }
}
