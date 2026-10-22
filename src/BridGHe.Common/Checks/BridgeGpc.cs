using System;
using System.Collections.Generic;
using BridGHe.Common.Models;
using GPC.Checkers.CompositeBridge;
using GPC.Model.Materials;

namespace BridGHe.Common.Checks;

/// <summary>Conversions at the BridGHe/gPC boundary. No engineering formula is duplicated here.</summary>
public static class BridgeGpc
{
    public static BridgeMaterialSet Materials(MaterialModel concrete,MaterialModel steel,MaterialModel rebar)
    {
        if(concrete==null||steel==null)throw new ArgumentException("Concrete and structural steel materials are required.");
        SteelMaterialEN1992 Steel(MaterialModel m,bool reinforcing)=>new(m.Name,m.Modulus,m.MinimumYieldStress,m.MinimumTensileStress,reinforcing ? m.ReinforcementUltimateStrain ?? .1 : .1,
            SteelMaterial.StressStrainCurveType.ElasticPerfectPlastic,reinforcing?SteelMaterial.SteelTypes.Rebar:SteelMaterial.SteelTypes.Structural);
        return new BridgeMaterialSet(new ConcreteMaterialEN1992(concrete.Name,concrete.SpecificCompressiveStrength,
            ConcreteMaterial.CompressionStressStrainDiagrams.ParabolaRectangle),Steel(steel,false),Steel(rebar??steel,rebar!=null));
    }

    public static HBridgeInput WithDetails(HBridgeInput input,IEnumerable<object> definitions)
    {
        var types=new HashSet<Type>();
        foreach(var definition in definitions)
        {
            if(definition==null||!types.Add(definition.GetType()))throw new ArgumentException("Null or duplicate bridge detail definition.");
            input=definition switch
            {
                BridgeStudOptions s=>input with{Studs=s},
                BridgeIntermediateStiffener s=>input with{Intermediate=s},
                BridgeSupportStiffener s=>input with{Support=s},
                BridgeTransverseReinforcement s=>input with{Transverse=s},
                BridgeFatigueOptions s=>input with{Fatigue=s},
                BridgeBoxOptions s=>input with{Box=s},
                _=>throw new ArgumentException("Unsupported detail: "+definition.GetType().Name)
            };
        }
        return input;
    }

    public static IEnumerable<BridgeLocalCheck> Checks(BridgeStage stage)
    {
        foreach(var check in stage.Shear?.Checks??new List<BridgeLocalCheck>())yield return check;
        foreach(var check in stage.Studs?.Checks??new List<BridgeLocalCheck>())yield return check;
        foreach(var check in stage.Torsion?.Checks??new List<BridgeLocalCheck>())yield return check;
    }
}
