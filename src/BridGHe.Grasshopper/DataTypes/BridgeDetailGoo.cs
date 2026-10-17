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
        public BridgeDetailGoo() { }
        public BridgeDetailGoo(object value) { Value=value; }
        public override bool IsValid=>Value is BridgeStudOptions || Value is BridgeIntermediateStiffener || Value is BridgeSupportStiffener;
        public override string TypeName=>"gPC bridge detail";
        public override string TypeDescription=>"Pioli o irrigidimenti gPC, mm / MPa / kN.";
        public override string ToString()=>Value?.ToString()??"Empty bridge detail";
        public override IGH_Goo Duplicate()=>new BridgeDetailGoo(Value); // gPC records contain only scalar init properties.
        public override bool Write(GH_IWriter writer)
        {
            if(!IsValid)return false;
            writer.SetString("Kind",Value.GetType().Name);writer.SetString("Data",JsonSerializer.Serialize(Value,Value.GetType()));return true;
        }
        public override bool Read(GH_IReader reader)
        {
            Value=null;string kind=null,json=null;if(!reader.TryGetString("Kind",ref kind)||!reader.TryGetString("Data",ref json))return false;
            Type type=kind switch {nameof(BridgeStudOptions)=>typeof(BridgeStudOptions),nameof(BridgeIntermediateStiffener)=>typeof(BridgeIntermediateStiffener),nameof(BridgeSupportStiffener)=>typeof(BridgeSupportStiffener),_=>null};
            if(type==null)return false;
            try{Value=JsonSerializer.Deserialize(json,type);return IsValid;}catch(JsonException){return false;}
        }
    }
}
