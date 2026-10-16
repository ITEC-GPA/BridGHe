using BridGHe.Common.Models;
using GH_IO.Serialization;
using Grasshopper.Kernel.Types;
using System;
using System.IO;

using System.Text.Json;

namespace BridGHe.Grasshopper.DataTypes.Fem
{
    public class GH_Material : GH_Goo<MaterialModel>
    {
        public GH_Material()
        {
            Value = new MaterialModel();
        }

        public GH_Material(MaterialModel material)
        {
            Value = material;
        }

        public override bool IsValid => Value == null ? false : Value.IsValid;

        public override string TypeName => "Material";

        public override string TypeDescription => "Material definition";

        public override IGH_Goo Duplicate()
        {
            return new GH_Material(Value);
        }

        public override bool Write(GH_IWriter writer)
        {
            try { writer.SetByteArray("Data", Common.Helpers.ModelArchive.Write(Value)); return true; }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex); return false; }
        }

        public override bool Read(GH_IReader reader)
        {
            try { Value = Common.Helpers.ModelArchive.Read<MaterialModel>(reader.GetByteArray("Data")); return true; }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex); return false; }
        }

        public override string ToString()
        {
            if (Value == null)
                return "Null Material";
            else
                return Value.ToString();
        }
    }
}
