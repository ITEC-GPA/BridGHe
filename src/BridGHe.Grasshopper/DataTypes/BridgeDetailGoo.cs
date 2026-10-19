using System.Linq;
using System;
using System.Text.Json;
using GH_IO.Serialization;
using Grasshopper.Kernel.Types;
using GPC.Checkers.CompositeBridge;

namespace BridGHe.Grasshopper.DataTypes
{
    /// <summary>Closed set of immutable gPC detail records; safe GH archive roundtrip.</summary>
    public sealed class BridgeDetailGoo : GH_Goo<object>
    {
        private static readonly System.Collections.Generic.Dictionary<string,Type> Types = new[] { typeof(BridgeStudOptions),typeof(BridgeIntermediateStiffener),typeof(BridgeSupportStiffener),typeof(HSectionDimensions),typeof(BridgeRebarRow),typeof(BridgePhase),typeof(BridgeAnalysisOptions),typeof(BridgeTransverseReinforcement),typeof(BridgeFatigueOptions),typeof(BridgeBoxOptions) }.ToDictionary(t=>t.Name,StringComparer.Ordinal);
        private static readonly JsonSerializerOptions Json = new() { NumberHandling=System.Text.Json.Serialization.JsonNumberHandling.AllowNamedFloatingPointLiterals };
        public BridgeDetailGoo() { }
        public BridgeDetailGoo(object value) { Value=value; }
        public override bool IsValid=>Value != null && Types.Values.Contains(Value.GetType());
        public override string TypeName=>"gPC bridge detail";
        public override string TypeDescription=>"Parametri di sezione, fasi e dettagli gPC: mm, MPa, kN, kNm.";
        public override string ToString()=>Value?.ToString()??"Empty bridge detail";
        public override IGH_Goo Duplicate()=>new BridgeDetailGoo(Value); // gPC records contain only scalar init properties.
        public override bool Write(GH_IWriter writer)
        {
            if(!IsValid)return false;
            writer.SetString("Kind",Value.GetType().Name);writer.SetString("Data",JsonSerializer.Serialize(Value,Value.GetType(),Json));return true;
        }
        public override bool Read(GH_IReader reader)
        {
            Value=null;string kind=null,json=null;if(!reader.TryGetString("Kind",ref kind)||!reader.TryGetString("Data",ref json))return false;
            Types.TryGetValue(kind,out Type type);
            if(type==null)return false;
            try{Value=JsonSerializer.Deserialize(json,type,Json);return IsValid;}catch(JsonException){return false;}
        }
    }
}
