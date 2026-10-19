using System;
using BridGHe.Common.Models;
using BridGHe.Common.Straus;
using BridGHe.Grasshopper.DataTypes.Fem;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Parameters;
namespace BridGHe.Grasshopper.Components.Attributes;

public sealed class BeamSupportComponent:GH_Component
{
    public BeamSupportComponent():base("Beam Support","BSup","Distributed Straus elastic support along principal local axis ±1 or ±2, in freedom case 1.","BridGHe","Attributes") { }
    protected override void RegisterInputParams(GH_InputParamManager p)
    {
        p.AddGenericParameter("Beam","B","Beam receiving an additional distributed support",GH_ParamAccess.item);
        p.AddNumberParameter("Stiffness","K","Distributed stiffness [force/length²], positive",GH_ParamAccess.item);
        p.AddIntegerParameter("Direction","D","Native Straus support side in principal local axes",GH_ParamAccess.item,3);
        var direction=(Param_Integer)p[2];direction.AddNamedValue("−1",0);direction.AddNamedValue("+1",1);direction.AddNamedValue("−2",2);direction.AddNamedValue("+2",3);
        p.AddBooleanParameter("Compression only","C","Unilateral contact; requires nonlinear analysis to enforce contact status",GH_ParamAccess.item,false);p[3].Optional=true;
        p.AddNumberParameter("Gap","G","Initial gap [length]; positive clearance, negative precompression. Compression-only support required",GH_ParamAccess.item,0);p[4].Optional=true;
    }
    protected override void RegisterOutputParams(GH_OutputParamManager p)=>p.AddGenericParameter("Beam","B","Copy of the beam with support",GH_ParamAccess.item);
    protected override void SolveInstance(IGH_DataAccess da)
    {
        GH_Beam beam=null;double stiffness=0,gap=0;int direction=3;bool compression=false;
        if(!da.GetData(0,ref beam)||!da.GetData(1,ref stiffness)||!da.GetData(2,ref direction))return;
        da.GetData(3,ref compression);da.GetData(4,ref gap);
        try
        {
            var value=new BeamSupportAttributeModel.AttributeData
            {
                SpringType=BeamSupportAttributeModel.SpringType.Simple,
                SpringTensionOrientation=BeamSupportAttributeModel.SpringTensionOrientation.BeamLocalAxis,
                SimpleSpringType=compression?BeamSupportAttributeModel.SimpleSpringTypes.CompressionOnly:BeamSupportAttributeModel.SimpleSpringTypes.TensionCompression,
                LocalAxisDirection=direction switch{0=>BeamSupportAttributeModel.LocalAxisDirection.negative1Axis,1=>BeamSupportAttributeModel.LocalAxisDirection.positive1Axis,
                    2=>BeamSupportAttributeModel.LocalAxisDirection.negative2Axis,3=>BeamSupportAttributeModel.LocalAxisDirection.positive2Axis,_=>throw new ArgumentException("Direction must be 0, 1, 2 or 3.")},
                Stiffness=stiffness,Gap=gap
            };
            BeamSupportExport.Resolve(value);
            var output=new GH_Beam(beam);output.Value.Attributes.Add(new BeamSupportAttributeModel(value));da.SetData(0,output);
        }
        catch(Exception e){AddRuntimeMessage(GH_RuntimeMessageLevel.Error,e.Message);}
    }
    public override GH_Exposure Exposure=>GH_Exposure.secondary;
    protected override System.Drawing.Bitmap Icon=>Properties.ComponentIcons.For(GetType());
    public override Guid ComponentGuid=>new Guid("ed5438f5-aa16-4bcb-a5c9-a31bc27d4745");
}
