#if GPC_PILOT
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BridGHe.Common.Gpc;
using BridGHe.Common.Gpc.Builders;
using BridGHe.Common.Gpc.Legacy;
using BridGHe.Common.Straus;

/// <summary>
/// --beam-support (native; R6.6 lane R4b: on GPC data alone, <see cref="R4bInputs.BeamSupport"/>): the distributed beam supports of Beam Support
/// on a beam given by its GPC draft, through Build Model on GPC and the GPC-only analysis:
/// <list type="bullet">
/// <item>two bilateral springs (+1 axis k 10, −2 axis k 20) under uniform loads: the uniform displacement q/k of each case (analytic);</item>
/// <item>the four directions written by the Converter and read back with its API: the Straus7 direction code of the former exporter (−1 → 0, +1 → 1,
/// −2 → 2, +2 → 3), the stiffness, the gap and the compression-only flag as given;</item>
/// <item>the supports of the draft through the BGHGPC1 archive of the draft and its copy unchanged; the longitudinal axis refused by the builder;</item>
/// <item>the two contacts with their gaps under the combinations «Positive» and «Negative» (nonlinear solver): q = k·u + k_c·(u − gap) on the
/// engaged side (analytic); a negative gap kept as given.</item>
/// </list>
/// </summary>
internal static class NativeBeamSupportTests
{
    internal static void Run()
    {
        var checks = new NativeExamples.Recorder(); string folder = Path.GetFullPath("artifacts/beam-support"); Directory.CreateDirectory(folder);
        var bilateral = R4bInputs.BeamSupport(2, false);
        var solved = new GpcNativeExamples.Results(StrausAnalysis.Run(bilateral, Path.Combine(folder, "bilateral.st7"), 0, new[] { "Q", "R" }, false, out var warnings));
        for (int n = 0; n < solved.NodeCount; n++)
            foreach (var result in solved.NodeRows(n).Where(r => r.Displacement != null))
            {
                string name = solved.Catalog.Entries[result.Entry].Label, node = solved.Model.NodesElements[n + 1].Name;
                double sign = name == "Q" ? 1 : -.5;
                checks.Near(name + " node " + node + " Uy [mm]", result.Displacement[1], .2 * sign, 1e-8, 1e-10);
                checks.Near(name + " node " + node + " Uz [mm]", result.Displacement[2], .2 * sign, 1e-8, 1e-10);
            }
        var four = R4bInputs.BeamSupport(4, false);
        string file = Path.Combine(folder, "four-directions.st7");
        // F6A: export by the GPC Converter writer (export component path, here the GPC input alone) and readback with the Converter API.
        FemmAnalysisSeam.Export(null, four, file, R4bInputs.Tolerance, new List<string>());
        string scratch = Path.Combine(Path.GetTempPath(), "bridghe-support-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(scratch);
        try
        {
            using var api = new GPC.Converter.Straus7.Straus7NativeApi();
            api.OpenModelReadOnly(file, scratch);
            foreach (var support in R4bInputs.Supports)
            {
                // The Straus7 direction of the former exporter (BeamSupportExport.Resolve): −1 → 0, +1 → 1, −2 → 2, +2 → 3; compression only as given.
                int direction = Array.IndexOf(new[] { "negative1Axis", "positive1Axis", "negative2Axis", "positive2Axis" }, support.Direction);
                bool compression = support.Spring == "CompressionOnly";
                var data = ((GPC.Converter.Straus7.IStraus7PhysicalReadApi)api).ReadBeamSupport(1, direction, 1, out bool compressionOnly);
                checks.Near($"Direction {direction} stiffness", data[0], support.Stiffness, 0, 0);
                checks.Near($"Direction {direction} gap", data[1], support.Gap, 0, 1e-12);
                checks.Near($"Direction {direction} unilateral", compressionOnly ? 1 : 0, compression ? 1 : 0, 0, 0);
            }
            api.CloseModel();
        }
        finally { try { Directory.Delete(scratch, true); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
        // The supports of the draft: the BGHGPC1 archive of the draft and its copy keep every value and gap (before: the archive and copy of the FEM attribute).
        var draft = four.Drafts.Beams.Single();
        string Supports(LegacyBeamDraft d) => string.Join(";", d.Beam.Assignments.Physical.Supports.Select(s => $"{s.Stiffness:R} {s.Gap:R} {s.Behavior} {s.Direction.X:R},{s.Direction.Y:R},{s.Direction.Z:R}"));
        var (data2, records) = GpcDraftArchive.Write(draft);
        var read = GpcDraftArchive.ReadBeam(data2, records); var copy = GpcDrafts.Copy(draft);
        if (draft.Beam.Assignments.Physical.Supports.Count != 4 || Supports(read) != Supports(draft) || Supports(copy) != Supports(draft) || ReferenceEquals(copy.Beam, draft.Beam))
            throw new Exception("Support archive/copy lost its value or gap: " + Supports(draft) + " / archive " + Supports(read) + " / copy " + Supports(copy));
        bool rejected = false;
        try { GpcBeamBuilders.WithSupport(draft, 10, 0, "positive3Axis", "TensionCompression", R4bInputs.Id("beam-support:longitudinal")); } catch (NotSupportedException) { rejected = true; }
        if (!rejected) throw new Exception("Longitudinal distributed support must be rejected.");
        var contact = R4bInputs.BeamSupport(4, true);
        var nonlinear = new GpcNativeExamples.Results(StrausAnalysis.Run(contact, Path.Combine(folder, "contact.st7"), 1, new[] { "Positive", "Negative" }, false, out var nonlinearWarnings));
        for (int n = 0; n < nonlinear.NodeCount; n++)
            foreach (var result in nonlinear.NodeRows(n).Where(r => r.Displacement != null))
            {
                string name = nonlinear.Catalog.Entries[result.Entry].Label;
                bool plus = name == "Positive";
                // Uniform displacement: q = k_bilateral*u + k_contact*(u-gap) on the engaged side.
                double uy = plus ? .2 : -(1 + 30 * .04) / (10 + 30), uz = plus ? (4 + 40 * .05) / (20 + 40) : -.1;
                checks.Near(name + " contact Uy [mm]", result.Displacement[1], uy, 1e-6, 1e-8);
                checks.Near(name + " contact Uz [mm]", result.Displacement[2], uz, 1e-6, 1e-8);
            }
        var precompressed = GpcBeamBuilders.WithSupport(draft, 10, -.05, "positive2Axis", "CompressionOnly", R4bInputs.Id("beam-support:precompressed"));
        checks.Near("Negative gap retained", precompressed.Beam.Assignments.Physical.Supports.Last().Gap, -.05, 0, 0);
        File.WriteAllText(Path.Combine(folder, "report.json"), System.Text.Json.JsonSerializer.Serialize(new { Warnings = warnings, NonlinearWarnings = nonlinearWarnings, checks.Measurements }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        if (checks.Measurements.Any(c => !c.Passed)) throw new Exception("Native beam support checks failed.");
        Console.WriteLine($"BEAM SUPPORT: {checks.Measurements.Count} native checks passed (GPC data alone).");
    }
}
#else
using System;
using System.IO;
using System.Linq;
using BridGHe.Common.Helpers;
using BridGHe.Common.Models;
using BridGHe.Common.Straus;
using Rhino.Geometry;

internal static class NativeBeamSupportTests
{
    private static BeamSupportAttributeModel Support(BeamSupportAttributeModel.LocalAxisDirection direction,double stiffness,bool compression=false,double gap=0)=>new(new BeamSupportAttributeModel.AttributeData()
    {SpringType=BeamSupportAttributeModel.SpringType.Simple,SpringTensionOrientation=BeamSupportAttributeModel.SpringTensionOrientation.BeamLocalAxis,
        LocalAxisDirection=direction,Stiffness=stiffness,Gap=gap,SimpleSpringType=compression?BeamSupportAttributeModel.SimpleSpringTypes.CompressionOnly:BeamSupportAttributeModel.SimpleSpringTypes.TensionCompression});
    internal static void Run()
    {
        var checks=new NativeExamples.Recorder();string folder=Path.GetFullPath("artifacts/beam-support");Directory.CreateDirectory(folder);
        var group=new GroupModel("Beam");var model=new ModelModel(ModelModel.ModelUnits.Nmm);
        var property=new BeamPropertyModel("P"){Name="Beam",PropertyType=BeamPropertyType.Beam,Material=new MaterialModel("S"){Name="S355",Modulus=210000,PoissonRatio=.3},
            Section=new SectionModel{SectionType=SectionModel.SectionTypes.Generic,SectionArea=10000,I11=1e8,I22=1e8,J=1e7}};
        var q=new LoadCaseModel("Q","Q");var r=new LoadCaseModel("R","R");model.LoadCases.Add(q);model.LoadCases.Add(r);
        for(int i=0;i<2;i++)
        {
            var node=new NodeModel((i+1).ToString(),new Point3d(i*1000,0,0));node.Groups.Add(group);
            node.Attributes.Add(new NodeRestraintAttributeModel(true,false,false,true,true,true));
            model.AddElements(new[]{node});
        }
        var beam=new BeamModel(model.Nodes[0].Position,model.Nodes[1].Position,property,group){BeamId="1"};model.AddElements(new[]{beam});
        foreach(var entry in new[]{(Case:q,Factor:1.0),(Case:r,Factor:-.5)})
            foreach(var load in new[]{(Direction:BeamDistributedLoadModel.LoadDirection.Y,Value:2.0),(Direction:BeamDistributedLoadModel.LoadDirection.Z,Value:4.0)})
                beam.Loads.Add(new BeamDistributedLoadModel(entry.Case,new(){LoadSchema=BeamDistributedLoadModel.LoadSchema.Uniform,CoordinateSystem=CoordinateSystemModel.Global,
                    LoadDirection=load.Direction,PA=load.Value*entry.Factor,PB=load.Value*entry.Factor},BeamDistributedLoadModel.LoadType.Force));
        beam.Attributes.Add(Support(BeamSupportAttributeModel.LocalAxisDirection.positive1Axis,10));
        beam.Attributes.Add(Support(BeamSupportAttributeModel.LocalAxisDirection.negative2Axis,20));
        var solved=StrausAnalysis.Run(model,Path.Combine(folder,"bilateral.st7"),0,new[]{"Q","R"},1e-6,false,out var warnings);
        foreach(var node in solved.Nodes)
            foreach(var result in node.Results.OfType<NodeDisplResultModel>())
            {
                double sign=result.LoadCase.Name=="Q"?1:-.5;
                checks.Near(result.LoadCase.Name+" node "+node.NodeId+" Uy [mm]",result.Displacement.Y,.2*sign,1e-8,1e-10);
                checks.Near(result.LoadCase.Name+" node "+node.NodeId+" Uz [mm]",result.Displacement.Z,.2*sign,1e-8,1e-10);
            }
        beam.Attributes.Add(Support(BeamSupportAttributeModel.LocalAxisDirection.negative1Axis,30,true,.04));
        beam.Attributes.Add(Support(BeamSupportAttributeModel.LocalAxisDirection.positive2Axis,40,true,.05));
        string file=Path.Combine(folder,"four-directions.st7");
        // F6A: export by the GPC Converter writer (export component path) and readback with the Converter API.
        BridGHe.Common.Gpc.Legacy.FemmAnalysisSeam.Export(model,file,1e-6,new System.Collections.Generic.List<string>());
        string scratch=Path.Combine(Path.GetTempPath(),"bridghe-support-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(scratch);
        try
        {
            using var api=new GPC.Converter.Straus7.Straus7NativeApi();
            api.OpenModelReadOnly(file,scratch);
            foreach(var support in beam.Attributes.OfType<BeamSupportAttributeModel>())
            {
                var expected=BeamSupportExport.Resolve(support.Value);
                var data=((GPC.Converter.Straus7.IStraus7PhysicalReadApi)api).ReadBeamSupport(1,expected.Direction,1,out bool compressionOnly);
                checks.Near($"Direction {expected.Direction} stiffness",data[0],expected.Stiffness,0,0);
                checks.Near($"Direction {expected.Direction} gap",data[1],expected.Gap,0,1e-12);
                checks.Near($"Direction {expected.Direction} unilateral",compressionOnly?1:0,expected.CompressionOnly?1:0,0,0);
                var clone=ModelArchive.Clone(support);if(!clone.Equals(support)||clone.GetHashCode()!=support.GetHashCode())throw new Exception("Support archive/copy lost its value or gap.");
            }
            api.CloseModel();
        }
        finally{try{Directory.Delete(scratch,true);}catch(IOException){}catch(UnauthorizedAccessException){}}
        var invalid=Support(BeamSupportAttributeModel.LocalAxisDirection.positive3Axis,10);bool rejected=false;
        try{BeamSupportExport.Resolve(invalid.Value);}catch(NotSupportedException){rejected=true;}
        if(!rejected)throw new Exception("Longitudinal distributed support must be rejected.");
        var positive=new LoadCombinationModel("Positive");positive.Values.Add(q,1);
        var negative=new LoadCombinationModel("Negative");negative.Values.Add(r,1);model.AddLoadCombinations(new[]{positive,negative});
        var nonlinear=StrausAnalysis.Run(model,Path.Combine(folder,"contact.st7"),1,new[]{"Positive","Negative"},1e-6,false,out var nonlinearWarnings);
        foreach(var node in nonlinear.Nodes)
            foreach(var result in node.Results.OfType<NodeDisplResultModel>())
            {
                bool plus=result.LoadCase.Name=="Positive";
                // Uniform displacement: q = k_bilateral*u + k_contact*(u-gap) on the engaged side.
                double uy=plus?.2:-(1+30*.04)/(10+30),uz=plus?(4+40*.05)/(20+40):-.1;
                checks.Near(result.LoadCase.Name+" contact Uy [mm]",result.Displacement.Y,uy,1e-6,1e-8);
                checks.Near(result.LoadCase.Name+" contact Uz [mm]",result.Displacement.Z,uz,1e-6,1e-8);
            }
        var precompressed=Support(BeamSupportAttributeModel.LocalAxisDirection.positive2Axis,10,true,-.05);
        checks.Near("Negative gap retained",BeamSupportExport.Resolve(precompressed.Value).Gap,-.05,0,0);
        File.WriteAllText(Path.Combine(folder,"report.json"),System.Text.Json.JsonSerializer.Serialize(new{Warnings=warnings,checks.Measurements},new System.Text.Json.JsonSerializerOptions{WriteIndented=true}));
        if(checks.Measurements.Any(c=>!c.Passed))throw new Exception("Native beam support checks failed.");
        Console.WriteLine($"BEAM SUPPORT: {checks.Measurements.Count} native checks passed.");
    }
}

#endif
