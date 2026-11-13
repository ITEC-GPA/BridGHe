using System;
using System.Collections.Generic;
using GPC.Checkers.CompositeBridge;
using GPC.Model.Materials;

namespace BridGHe.Common.Checks;

/// <summary>Conversions at the BridGHe/gPC boundary. No engineering formula is duplicated here.</summary>
public static class BridgeGpc
{

    /// <summary>R6.5 P1 lane 3a: the same set from the GPC materials (materials given by their GPC data alone): the rule of the former builders
    /// (<see cref="Gpc.GpcMaterials.Concrete"/>, <see cref="Gpc.GpcMaterials.StructuralSteel"/>, <see cref="Gpc.GpcMaterials.Reinforcement"/>);
    /// without a rebar material the structural steel stands for it, as on the FEM path.</summary>
    public static BridgeMaterialSet Materials(Material concrete,Material steel,Material rebar)
    {
        if(concrete==null||steel==null)throw new ArgumentException("Concrete and structural steel materials are required.");
        return new BridgeMaterialSet(Gpc.GpcMaterials.Concrete(concrete),Gpc.GpcMaterials.StructuralSteel(steel),rebar==null?Gpc.GpcMaterials.StructuralSteel(steel):Gpc.GpcMaterials.Reinforcement(rebar));
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
