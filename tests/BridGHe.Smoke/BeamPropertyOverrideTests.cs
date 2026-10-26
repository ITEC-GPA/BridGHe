using System;
using System.Linq;
using BridGHe.Common.Helpers;
using BridGHe.Common.Models;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Parameters;
using Rhino.Geometry;
using static GetBeamPropertyTests;

internal static class BeamPropertyOverrideTests
{
    internal static void Run()
    {
        int checks = 0;
        void Check(bool valid, string message) { if (!valid) throw new Exception("PROPERTY OVERRIDE: " + message); checks++; }
        void Reject(Action action) { try { action(); } catch (ArgumentException) { checks++; return; } throw new Exception("Expected invalid property replacement rejection."); }
        var original = Property("G1_steel"); var other = Property("CrossMember");
        var next = BridgeGrillageExample.Composite(new()).CreateHomogenizedProperty(18, "G1_n_inf");
        next.Material.Density = 4e-9;
        var beams = new[] { Beam("B2", other), Beam("B1", original), Beam("B3", original) };
        var lc = new LoadCaseModel("TEST", "TEST");
        foreach (var b in beams)
        {
            b.AngleDeg = 17; b.Offset = new Vector2d(30, 80); b.Groups.Add(new GroupModel("Deck"));
            b.Loads.Add(new BeamDistributedLoadModel(lc, new() { LoadSchema=BeamDistributedLoadModel.LoadSchema.Uniform, PA=-2, LoadDirection=BeamDistributedLoadModel.LoadDirection.Y }, BeamDistributedLoadModel.LoadType.Force));
            b.Results.Add(new BeamForceResultModel(lc, 0, 0, "") { AxialForce = 123 });
        }
        var sourceCopy = ModelArchive.Clone(original); sourceCopy.Id = "OtherId"; sourceCopy.Material.Modulus = 200000;
        var result = BeamPropertyReplacement.Apply(beams, new[] { sourceCopy }, new[] { next });
        Check(result.Replaced.SequenceEqual(new[] {false,true,true}), "match by original name, not ID, reference, numerical equality or beam position");
        Check(result.Beams.Select(b=>b.BeamId).SequenceEqual(beams.Select(b=>b.BeamId)), "all beams and ordering retained");
        Check(result.Beams[0].BeamProperty.Name == other.Name && result.Beams[0].BeamProperty.SectionArea == other.SectionArea, "unmatched property unchanged");
        Check(result.Beams[1].BeamProperty.Name == next.Name && result.Beams[1].BeamProperty.SectionArea == next.SectionArea && result.Beams[1].BeamProperty.I11 == next.I11, "new name and full transformed properties applied");
        Check(result.Beams[1].BeamProperty.Material.Density == 4e-9, "no implicit density or mass adjustment");
        Check(result.Beams[1].BeamProperty.Section.CompositePreview != null && result.Beams[1].Shapes.Length > 0, "composite preview survives replacement");
        Check(result.Beams.Zip(beams,(a,b)=>a.PointFrom==b.PointFrom && a.PointTo==b.PointTo && a.AngleDeg==b.AngleDeg && a.Offset==b.Offset && a.HasId(b.BeamId) && a.Groups.Single().Name=="Deck").All(v=>v), "geometry, rotation, offset, groups and element ID retained");
        Check(result.Beams.All(b=>b.Results.Count==0 && b.Loads.Count==1) && beams.All(b=>b.Results.Count==1), "results invalidated on all output beams while source results and loads remain");
        Check(ReferenceEquals(((BeamDistributedLoadModel)result.Beams[0].Loads[0]).LoadCase,((BeamDistributedLoadModel)result.Beams[1].Loads[0]).LoadCase), "shared load case identity retained across the copied graph");
        result.Beams[1].BeamProperty.Material.Density = 1; result.Beams[0].BeamProperty.Section.D = 100;
        Check(next.Material.Density == 4e-9 && other.D == 2500 && original.Name == "G1_steel", "no mutations leak into beams or replacement inputs");
        var unseen = Property("Unseen");
        var none = BeamPropertyReplacement.Apply(beams,new[]{unseen},new[]{next});
        Check(none.Replaced.All(v=>!v) && none.UnusedOriginalNames.SequenceEqual(new[]{"Unseen"}) && none.Beams.All(b=>b.Results.Count==1), "unmatched original reported; no-op retains results");
        var empty = BeamPropertyReplacement.Apply(beams,Array.Empty<BeamPropertyModel>(),Array.Empty<BeamPropertyModel>());
        Check(empty.Beams.Length==3 && empty.Beams.All(b=>b.Results.Count==1), "both mapping lists empty is a no-op");
        var lower = Property("g1_steel"); Check(BeamPropertyReplacement.Apply(beams,new[]{lower},new[]{next}).Replaced.All(v=>!v), "exact case-sensitive name matching");
        var repeated = BeamPropertyReplacement.Apply(beams,new[]{original,ModelArchive.Clone(original)},new[]{next,ModelArchive.Clone(next)});
        Check(repeated.Replaced.Count(v=>v)==2, "identical repeated pairs from extracted properties accepted");
        var alternative = ModelArchive.Clone(next); alternative.Name = "AnotherPhase";
        Reject(()=>BeamPropertyReplacement.Apply(beams,new[]{original,original},new[]{next,alternative}));
        var conflict = ModelArchive.Clone(next); conflict.Material.Modulus = 1;
        Reject(()=>BeamPropertyReplacement.Apply(beams,new[]{original,other},new[]{next,conflict}));
        Reject(()=>BeamPropertyReplacement.Apply(beams,new[]{original},Array.Empty<BeamPropertyModel>()));
        Reject(()=>BeamPropertyReplacement.Apply(beams,new[]{original},new BeamPropertyModel[]{null}));
        var unnamed = Property(" "); Reject(()=>BeamPropertyReplacement.Apply(beams,new[]{unnamed},new[]{next}));
        Reject(()=>BeamPropertyReplacement.Apply(new BeamModel[]{null},new[]{original},new[]{next}));
        // Pair order is independent of beam order; different source names may share a target.
        var both = BeamPropertyReplacement.Apply(beams,new[]{original,other},new[]{next,ModelArchive.Clone(next)});
        Check(both.Replaced.All(v=>v), "multiple source properties may map to the same target definition");
        using var doc = new GH_Document();
        var c = BridgeSegmentExcelTests.Add(doc,"BeamPropertyOverrideComponent");
        var getter = BridgeSegmentExcelTests.Add(doc,"GetBeamPropertyComponent");
        Input(c,0,beams.Cast<object>().ToArray()); Input(getter,0,beams[1]);
        c.Params.Input[1].AddSource(getter.Params.Output[0]); Input(c,2,next);
        BridgeSegmentExcelTests.Solve(doc);
        Check(Output<bool>(c,1).SequenceEqual(new[]{false,true,true}) && Output<BeamModel>(c)[1].BeamProperty.Name==next.Name,"actual property extraction-to-override wiring");
        var inputs=((Param_GenericObject)c.Params.Input[0]).PersistentData.AllData(true).ToArray();
        var outputs=c.Params.Output[0].VolatileData.AllData(true).ToArray();
        Check(inputs.Zip(outputs,(a,b)=>a.GetType().GetProperty("Guid").GetValue(a).Equals(b.GetType().GetProperty("Guid").GetValue(b))).All(v=>v),"GH beam identities preserved");
        var chunk = new GH_IO.Serialization.GH_LooseChunk("Component"); Check(c.Write(chunk),"override archive write");
        using var restored=new GH_Document();var copy=BridgeSegmentExcelTests.Add(restored,"BeamPropertyOverrideComponent");
        Check(copy.Read(chunk),"override archive read");copy.Params.Input[1].RemoveAllSources();Input(copy,1,original);BridgeSegmentExcelTests.Solve(restored);
        Check(Output<bool>(copy,1).SequenceEqual(new[]{false,true,true}),"restored override keeps mapping semantics");
        Input(c,2);c.ExpireSolution(false);BridgeSegmentExcelTests.Solve(doc,true);
        Check(c.RuntimeMessages(GH_RuntimeMessageLevel.Error).Count>0 && c.Params.Output[0].VolatileDataCount==0,"mismatched lists clear stale output and report error");
        Console.WriteLine($"BEAM PROPERTY OVERRIDE PASS: {checks} checks; explicit name pairs, partial/no-op cases, isolation, results, geometry, preview and GH wiring/archive.");
    }
}
