using System;
using System.IO;
using System.Linq;
using BridGHe.Common.Helpers;
using BridGHe.Common.Models;
using BridGHe.Common.Straus;
using Rhino.Geometry;
using St7API;

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
        if(!StrausR3Helper.CreateModelR3(model,file,1e-6,out _,out _,out var errors))throw new Exception(string.Join(";",errors));
        lock(StrausRuntime.Gate)
        {
            int uid=StrausRuntime.AcquireModelId();bool open=false;
            try
            {
                StrausRuntime.Check(St7.St7OpenFile(uid,file,Path.GetTempPath()),"Open support test");open=true;
                foreach(var support in beam.Attributes.OfType<BeamSupportAttributeModel>())
                {
                    var expected=BeamSupportExport.Resolve(support.Value);int status=0;var data=new double[2];
                    StrausRuntime.Check(St7.St7GetBeamSupport2(uid,1,expected.Direction,1,ref status,data),"Read beam support");
                    checks.Near($"Direction {expected.Direction} stiffness",data[0],expected.Stiffness,0,0);
                    checks.Near($"Direction {expected.Direction} gap",data[1],expected.Gap,0,1e-12);
                    checks.Near($"Direction {expected.Direction} unilateral",status,expected.CompressionOnly?St7.btTrue:St7.btFalse,0,0);
                    var clone=ModelArchive.Clone(support);if(!clone.Equals(support)||clone.GetHashCode()!=support.GetHashCode())throw new Exception("Support archive/copy lost its value or gap.");
                }
            }
            finally{if(open)St7.St7CloseFile(uid);StrausRuntime.ReleaseModelId(uid);}
        }
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

