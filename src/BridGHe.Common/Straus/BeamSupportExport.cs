using System;
using System.Collections.Generic;
using System.Linq;
using BridGHe.Common.Models;
using St7API;

namespace BridGHe.Common.Straus;

/// <summary>Straus R3 St7SetBeamSupport2: native direction, freedom case, compression flag, [stiffness,gap].</summary>
public static class BeamSupportExport
{
    public static (int Direction,bool CompressionOnly,double Stiffness,double Gap) Resolve(BeamSupportAttributeModel.AttributeData value)
    {
        if(value==null)throw new ArgumentNullException(nameof(value));
        if(value.SpringType!=BeamSupportAttributeModel.SpringType.Simple||value.SpringTensionOrientation!=BeamSupportAttributeModel.SpringTensionOrientation.BeamLocalAxis)
            throw new NotSupportedException("Straus beam supports require simple springs along principal local axes 1 or 2.");
        if(!double.IsFinite(value.Stiffness)||value.Stiffness<=0||!double.IsFinite(value.Gap))
            throw new ArgumentException("Beam support requires finite positive stiffness [F/L²] and finite gap [L].");
        int direction=value.LocalAxisDirection switch
        {
            BeamSupportAttributeModel.LocalAxisDirection.negative1Axis=>St7.adMinus1,
            BeamSupportAttributeModel.LocalAxisDirection.positive1Axis=>St7.adPlus1,
            BeamSupportAttributeModel.LocalAxisDirection.negative2Axis=>St7.adMinus2,
            BeamSupportAttributeModel.LocalAxisDirection.positive2Axis=>St7.adPlus2,
            _=>throw new NotSupportedException("Straus beam supports accept only ±1 and ±2, not longitudinal axis 3.")
        };
        bool compressionOnly=value.SimpleSpringType switch
        {
            BeamSupportAttributeModel.SimpleSpringTypes.TensionCompression=>false,
            BeamSupportAttributeModel.SimpleSpringTypes.CompressionOnly=>true,
            BeamSupportAttributeModel.SimpleSpringTypes.TensionOnly=>true,
            _=>throw new ArgumentException("Unknown spring behaviour.")
        };
        if(value.SimpleSpringType==BeamSupportAttributeModel.SimpleSpringTypes.TensionOnly)direction^=1;
        if(!compressionOnly&&value.Gap!=0)throw new ArgumentException("A gap requires a compression-only spring.");
        return(direction,compressionOnly,value.Stiffness,value.Gap);
    }

    public static void Write(int modelId,int beamNumber,IEnumerable<BeamSupportAttributeModel> supports)
    {
        var values=supports.Select(s=>Resolve(s.Value)).ToArray();
        if(values.Select(s=>s.Direction).Distinct().Count()!=values.Length)throw new ArgumentException($"Beam {beamNumber}: duplicate support direction. Combine stiffnesses explicitly.");
        foreach(var value in values)
            StrausRuntime.Check(St7.St7SetBeamSupport2(modelId,beamNumber,value.Direction,1,
                value.CompressionOnly?St7.btTrue:St7.btFalse,new[]{value.Stiffness,value.Gap}),"St7SetBeamSupport2");
    }
}
