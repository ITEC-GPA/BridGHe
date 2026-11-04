using System;
using System.Collections.Generic;
using System.Linq;
using BridGHe.Common.Models;

namespace BridGHe.Common.Straus;

/// <summary>Former St7SetBeamSupport2 semantics (native direction adMinus1 0, adPlus1 1, adMinus2 2, adPlus2 3; compression flag; stiffness, gap):
/// the validation of the beam support component and the reference of the conversion tests.</summary>
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
            BeamSupportAttributeModel.LocalAxisDirection.negative1Axis=>0,
            BeamSupportAttributeModel.LocalAxisDirection.positive1Axis=>1,
            BeamSupportAttributeModel.LocalAxisDirection.negative2Axis=>2,
            BeamSupportAttributeModel.LocalAxisDirection.positive2Axis=>3,
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
}
