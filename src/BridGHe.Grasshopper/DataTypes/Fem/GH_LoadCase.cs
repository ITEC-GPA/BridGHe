using System;
using GH_IO.Serialization;
using Grasshopper.Kernel.Types;

using BridGHe.Common.Models;

namespace BridGHe.Grasshopper.DataTypes.Fem
{
    public class GH_LoadCase : GH_Goo<LoadCaseModel>
    {
        public override bool IsValid => Value == null ? false : Value.Name != string.Empty;

        public override string IsValidWhyNot => "Not propely defined";

        public override string TypeName => "LoadCase";

        public override string TypeDescription => "The load case definition";

        public GH_LoadCase()
        {
        }

        public GH_LoadCase(LoadCaseModel lc)
        {
            Value = lc;
        }

        public GH_LoadCase(GH_LoadCase lc)
        {
            Value = new LoadCaseModel(lc.Value);
        }

        public override IGH_Goo Duplicate()
        {
            return new GH_LoadCase(this);
        }

        public override bool Read(GH_IReader reader)
        {
            try { Value = Common.Helpers.ModelArchive.Read<LoadCaseModel>(reader.GetByteArray("Data")); return true; }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex); return false; }
        }

        public override bool Write(GH_IWriter writer)
        {
            try { writer.SetByteArray("Data", Common.Helpers.ModelArchive.Write(Value)); return true; }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex); return false; }
        }

        public override string ToString()
        {
            if (Value == null)
                return "Null LoadCase property";
            else
                return Value.ToString();
        }
    }
}
