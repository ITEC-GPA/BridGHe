using System;
using System.Linq;
using BridGHe.Common.Helpers;
using BridGHe.Common.Models;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Parameters;
using Rhino.Geometry;
using static GetBeamPropertyTests;
#if GPC_PILOT
using BridGHe.Common.Gpc.Legacy;
using Grasshopper.Kernel.Types;
#endif

internal static class BeamPropertyOverrideTests
{
    internal static void Run()
    {
#if GPC_PILOT
        Gpc();
#else
        Fem();
#endif
    }

#if GPC_PILOT
    /// <summary>R6.6 R2a: Beam Property Override on beams and properties given by their GPC data alone (no FEM value; the GPC path of the
    /// component is the one that stays in P2), with the same cases and expected values as the FEM test: pairs by exact, case-sensitive original
    /// name (not by numbers), order and BridGHe IDs, unmatched property unchanged, the replacement applied whole (name, area, inertia, density
    /// as given, composite preview), geometry, angle, offset, groups and GH GUIDs kept, the analysed results (the link to the analysed GPC
    /// element and model, the GPC twin of the former FEM results) gone from every output when one beam matches and kept on the inputs and on a
    /// no-op, loads kept, no mutation through the outputs, the refusals (one original with two replacements, two definitions under one name,
    /// list mismatch, an invalid or unnamed property, an invalid beam), the wiring from Get Beam Property and the archive.</summary>
    private static void Gpc()
    {
        int checks = 0;
        void Check(bool valid, string message) { if (!valid) throw new Exception("PROPERTY OVERRIDE: " + message); checks++; }
        var original = R2aPropertyFixtures.Property("G1_steel"); var other = R2aPropertyFixtures.Property("CrossMember");
        var next = R2aPropertyFixtures.Homogenised("G1", 18, "_n_inf", 4e-9);
        var lc = BridGHe.Common.Gpc.Builders.GpcCaseBuilders.Case("TEST", false);
        var ids = new[] { "B2", "B1", "B3" }; var assigned = new[] { other, original, original };
        var drafts = ids.Select((id, i) =>
        {
            var d = R2aPropertyFixtures.Beam("override:" + id, id, assigned[i], new[] { BridGHe.Common.Gpc.Builders.GpcCaseBuilders.Group("Deck", null) }, 17, 30, 80);
            return BridGHe.Common.Gpc.Builders.GpcBeamBuilders.WithDistributedLoad(d, lc, BridGHe.Common.Gpc.BridgeLoadSchema.Uniform, false, "Y", -2, 0, 0, 0, 0, 0, null,
                R2aDesignFixtures.Id("override:" + id + ":load"));
        }).ToArray();
        var analysed = R2aPropertyFixtures.Analysed("Beam property override", ids);
        var beams = drafts.Select((d, i) => R2aPropertyFixtures.WithAnalysed(R2aDesignFixtures.Beam(d), analysed.Beams[i], analysed.Model)).ToArray();
        // The source property: the same name as G1_steel, other numbers (E 200000).
        var sourceCopy = R2aPropertyFixtures.Property("G1_steel", 200000);
        using var doc = new GH_Document();
        var c = BridgeSegmentExcelTests.Add(doc, "BeamPropertyOverrideComponent");
        (IGH_Goo[] Beams, bool[] Flags, string[] Info, int Errors) Apply(IGH_Goo[] inputs, LegacyBeamProperty[] originals, LegacyBeamProperty[] replacements, IGH_Goo extra = null)
        {
            R2aDesignFixtures.Input(c, 0, inputs);
            R2aDesignFixtures.Input(c, 1, originals.Select(p => p == null ? R2aPropertyFixtures.Empty("GH_BeamProperty") : R2aDesignFixtures.Property(p)).ToArray());
            R2aDesignFixtures.Input(c, 2, replacements.Select(p => p == null ? R2aPropertyFixtures.Empty("GH_BeamProperty") : R2aDesignFixtures.Property(p)).ToArray());
            c.ExpireSolution(false); BridgeSegmentExcelTests.Solve(doc, true);
            return (R2aDesignFixtures.Outputs(c), R2aDesignFixtures.Values<bool>(c, 1), R2aDesignFixtures.Values<string>(c, 2), c.RuntimeMessages(GH_RuntimeMessageLevel.Error).Count);
        }
        void Reject((IGH_Goo[] Beams, bool[] Flags, string[] Info, int Errors) r) { if (r.Errors == 0 || r.Beams.Length != 0) throw new Exception("Expected invalid property replacement rejection."); checks++; }
        LegacyBeamDraft Draft(IGH_Goo g) => R2aDesignFixtures.Datum<LegacyBeamDraft>(g);
        var result = Apply(beams, new[] { sourceCopy }, new[] { next });
        var outputs = result.Beams.Select(Draft).ToArray();
        Check(result.Errors == 0 && R2aDesignFixtures.GpcOnly(result.Beams) && result.Flags.SequenceEqual(new[] {false,true,true}), "match by original name, not ID, reference, numerical equality or beam position");
        Check(outputs.Select(d => d.Beam.Name).SequenceEqual(ids), "all beams and ordering retained");
        Check(outputs[0].Beam.BeamProperty.Name == other.Property.Name && R2aPropertyFixtures.Elastic(outputs[0]).Section.Area == R2aPropertyFixtures.Area(other), "unmatched property unchanged");
        Check(outputs[1].Beam.BeamProperty.Name == "G1_n_inf" && R2aPropertyFixtures.Elastic(outputs[1]).Section.Area == R2aPropertyFixtures.Area(next)
            && R2aPropertyFixtures.Elastic(outputs[1]).Section.J11 == R2aPropertyFixtures.I11(next), "new name and full transformed properties applied");
        Check(R2aPropertyFixtures.Elastic(outputs[1]).Material.Density == 4e-9, "no implicit density or mass adjustment");
        Check(outputs[1].Property.Section.CompositePreview && ((IGH_GeometricGoo)result.Beams[1]).Boundingbox.IsValid, "composite preview survives replacement");
        Check(outputs.Zip(drafts, (a, b) => a.Beam.StartPoint.Equals(b.Beam.StartPoint) && a.Beam.EndPoint.Equals(b.Beam.EndPoint) && a.AngleDeg == 17 && b.AngleDeg == 17
            && a.Beam.Assignments.OffsetI.Equals(b.Beam.Assignments.OffsetI) && b.Beam.Assignments.OffsetI.Equals(new GPC.Geometry.Vector3d(30, 80, 0)) && a.Beam.Name == b.Beam.Name
            && a.Records.Groups.SequenceEqual(new[] { "Deck" })).All(v => v), "geometry, rotation, offset, groups and element ID retained");
        Check(result.Beams.All(b => R2aPropertyFixtures.Analysed(b) == null && R2aPropertyFixtures.AnalysedModel(b) == null) && outputs.All(d => d.Records.Loads == 1)
            && beams.Select((b, i) => ReferenceEquals(R2aPropertyFixtures.Analysed(b), analysed.Beams[i])).All(v => v), "results invalidated on all output beams while source results and loads remain");
        Check(outputs.All(d => d.Records.LoadCases.Single() == lc && d.Records.LoadCases.Single().Name == "TEST"), "shared load case identity retained across the copied graph");
        R2aPropertyFixtures.Elastic(outputs[1]).Material.Density = 1; R2aPropertyFixtures.Elastic(outputs[0]).Material.ElasticModulusTension = 1;
        Check(R2aPropertyFixtures.Material(next).Density == 4e-9 && R2aPropertyFixtures.Material(other).ElasticModulusTension == 210000
            && R2aPropertyFixtures.Elastic(drafts[0]).Material.ElasticModulusTension == 210000 && original.Property.Name == "G1_steel", "no mutations leak into beams or replacement inputs");
        var unseen = R2aPropertyFixtures.Property("Unseen");
        var none = Apply(beams, new[] { unseen }, new[] { next });
        Check(none.Flags.All(v => !v) && none.Info.Contains("Original property names not found: Unseen")
            && none.Beams.Select((b, i) => ReferenceEquals(R2aPropertyFixtures.Analysed(b), analysed.Beams[i])).All(v => v), "unmatched original reported; no-op retains results");
        var empty = Apply(beams, Array.Empty<LegacyBeamProperty>(), Array.Empty<LegacyBeamProperty>());
        Check(empty.Errors == 0 && empty.Beams.Length == 3 && empty.Beams.Select((b, i) => ReferenceEquals(R2aPropertyFixtures.Analysed(b), analysed.Beams[i])).All(v => v), "both mapping lists empty is a no-op");
        var lower = R2aPropertyFixtures.Property("g1_steel"); Check(Apply(beams, new[] { lower }, new[] { next }).Flags.All(v => !v), "exact case-sensitive name matching");
        var nextCopy = R2aPropertyFixtures.Homogenised("G1", 18, "_n_inf", 4e-9);
        var repeated = Apply(beams, new[] { original, R2aPropertyFixtures.Property("G1_steel") }, new[] { next, nextCopy });
        Check(repeated.Errors == 0 && repeated.Flags.Count(v => v) == 2, "identical repeated pairs from extracted properties accepted");
        var alternative = R2aPropertyFixtures.Homogenised("AnotherPhase", 18, "", 4e-9);
        Reject(Apply(beams, new[] { original, original }, new[] { next, alternative }));
        var conflict = R2aPropertyFixtures.Homogenised("G1", 18, "_n_inf", 4e-9, modulus: 1);
        Reject(Apply(beams, new[] { original, other }, new[] { next, conflict }));
        Reject(Apply(beams, new[] { original }, Array.Empty<LegacyBeamProperty>()));
        Reject(Apply(beams, new[] { original }, new LegacyBeamProperty[] { null }));
        var unnamed = R2aPropertyFixtures.Property(" "); Reject(Apply(beams, new[] { unnamed }, new[] { next }));
        Reject(Apply(new[] { R2aPropertyFixtures.Empty("GH_Beam") }, new[] { original }, new[] { next }));
        // Pair order is independent of beam order; different source names may share a target.
        var both = Apply(beams, new[] { original, other }, new[] { next, nextCopy });
        Check(both.Errors == 0 && both.Flags.All(v => v), "multiple source properties may map to the same target definition");
        using var wired = new GH_Document();
        c = BridgeSegmentExcelTests.Add(wired, "BeamPropertyOverrideComponent");
        var getter = BridgeSegmentExcelTests.Add(wired, "GetBeamPropertyComponent");
        R2aDesignFixtures.Input(c, 0, beams); R2aDesignFixtures.Input(getter, 0, beams[1]);
        c.Params.Input[1].AddSource(getter.Params.Output[0]); R2aDesignFixtures.Input(c, 2, R2aDesignFixtures.Property(next));
        BridgeSegmentExcelTests.Solve(wired);
        Check(R2aDesignFixtures.Values<bool>(c, 1).SequenceEqual(new[] {false,true,true}) && Draft(R2aDesignFixtures.Outputs(c)[1]).Beam.BeamProperty.Name == "G1_n_inf", "actual property extraction-to-override wiring");
        var inputs = ((Param_GenericObject)c.Params.Input[0]).PersistentData.AllData(true).ToArray();
        var wiredOutputs = R2aDesignFixtures.Outputs(c);
        Check(inputs.Length == 3 && inputs.Zip(wiredOutputs, (a, b) => R2aPropertyFixtures.Guid(a) == R2aPropertyFixtures.Guid(b)).All(v => v), "GH beam identities preserved");
        var chunk = new GH_IO.Serialization.GH_LooseChunk("Component"); Check(c.Write(chunk), "override archive write");
        using var restored = new GH_Document(); var copy = BridgeSegmentExcelTests.Add(restored, "BeamPropertyOverrideComponent");
        Check(copy.Read(chunk), "override archive read"); copy.Params.Input[1].RemoveAllSources(); R2aDesignFixtures.Input(copy, 1, R2aDesignFixtures.Property(original)); BridgeSegmentExcelTests.Solve(restored);
        Check(R2aDesignFixtures.Values<bool>(copy, 1).SequenceEqual(new[] {false,true,true}) && R2aDesignFixtures.GpcOnly(R2aDesignFixtures.Outputs(copy)), "restored override keeps mapping semantics");
        R2aDesignFixtures.Input(c, 2); c.ExpireSolution(false); BridgeSegmentExcelTests.Solve(wired, true);
        Check(c.RuntimeMessages(GH_RuntimeMessageLevel.Error).Count > 0 && c.Params.Output[0].VolatileDataCount == 0, "mismatched lists clear stale output and report error");
        Console.WriteLine($"BEAM PROPERTY OVERRIDE PASS: {checks} checks; explicit name pairs, partial/no-op cases, isolation, results, geometry, preview and GH wiring/archive.");
    }
#endif

#if !GPC_PILOT
    private static void Fem()
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
#endif
}
