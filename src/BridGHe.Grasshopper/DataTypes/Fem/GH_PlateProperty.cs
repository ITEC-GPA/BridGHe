using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Grasshopper.Kernel.Types;
using GH_IO.Serialization;
using BridGHe.Common.Models;

namespace BridGHe.Grasshopper.DataTypes.Fem
{
    public class GH_PlateProperty : GH_Goo<PlatePropertyModel>
    {
        GH_Material _material;

        public GH_PlateProperty(PlatePropertyModel plateProperty)
        {
            Value = plateProperty;
            _material = new GH_Material(Value.Material);
        }

        public GH_PlateProperty(PlatePropertyModel plateProperty, GH_Material material)
        {
            Value = plateProperty;
            _material = material;
        }

        #region Properties

        public override bool IsValid => Value == null ? false : Value.IsValid;

        public override string TypeName => "PlateProperty";

        public override string TypeDescription => "Define a plate property";

        #endregion

        public override IGH_Goo Duplicate()
        {
            return new GH_PlateProperty(Common.Helpers.ModelArchive.Clone(Value));
        }

        public override bool Write(GH_IWriter writer)
        {
            try { writer.SetByteArray("Data", Common.Helpers.ModelArchive.Write(Value)); return true; }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex); return false; }
        }

        public override bool Read(GH_IReader reader)
        {
            try { Value = Common.Helpers.ModelArchive.Read<PlatePropertyModel>(reader.GetByteArray("Data")); return true; }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex); return false; }
        }

        public override string ToString()
        {
            if (Value == null)
                return "Null Plate property";
            else
                return Value.ToString();
        }
    }
}
