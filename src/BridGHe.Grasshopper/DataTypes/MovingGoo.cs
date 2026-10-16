using System;
using System.Text.Json;
using BridGHe.Common.MovingLoads;
using GH_IO.Serialization;
using Grasshopper.Kernel.Types;

namespace BridGHe.Grasshopper.DataTypes
{
    public sealed class MovingGoo : GH_Goo<MovingObject>
    {
        public MovingGoo() { }
        public MovingGoo(MovingObject value) { Value = value; }
        public override bool IsValid { get { try { Value?.Validate(); return Value != null; } catch { return false; } } }
        public override string TypeName => "Straus moving load";
        public override string TypeDescription => "Carico mobile, veicolo, template o percorso Straus R3.";
        public override IGH_Goo Duplicate() => new MovingGoo(Value?.Clone());
        public override string ToString() => Value?.ToString() ?? "Empty moving load";
        public override bool Write(GH_IWriter writer) { if (Value != null) writer.SetString("Data", JsonSerializer.Serialize(Value)); return true; }
        public override bool Read(GH_IReader reader)
        {
            string json = null;
            if (!reader.TryGetString("Data", ref json)) return false;
            try { Value = JsonSerializer.Deserialize<MovingObject>(json); return IsValid; }
            catch (JsonException) { Value = null; return false; }
        }
    }
}
