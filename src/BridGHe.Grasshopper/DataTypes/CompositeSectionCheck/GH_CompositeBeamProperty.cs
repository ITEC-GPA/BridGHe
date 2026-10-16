using BridGHe.Common.Models;
using BridGHe.Grasshopper.DataTypes.Fem;
using GH_IO.Serialization;
using Grasshopper.Kernel.Types;
using System;

namespace BridGHe.Grasshopper.DataTypes
{
    public class GH_CompositeBeamProperty : GH_Goo<CompositeBeamPropertyModel>
    {
        public GH_CompositeBeamProperty() { }
        private readonly Fem.GH_Material _material;

        public GH_CompositeBeamProperty(CompositeBeamPropertyModel beamProperty)
        {
            Value = beamProperty;
            _material = new Fem.GH_Material(Value.Material);
        }

        public GH_CompositeBeamProperty(CompositeBeamPropertyModel beamProperty, Fem.GH_Material material)
        {
            Value = beamProperty;
            _material = material;
        }

        #region Properties

        public override bool IsValid => Value == null ? false : Value.IsValid;

        public override string TypeName => "BeamProperty";

        public override string TypeDescription => "Define a beam property";

        #endregion

        public override IGH_Goo Duplicate()
        {
            return new GH_CompositeBeamProperty(Common.Helpers.ModelArchive.Clone(Value));
        }

        public override bool Write(GH_IWriter writer)
        {
            try { writer.SetByteArray("Data", Common.Helpers.ModelArchive.Write(Value)); return true; }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex); return false; }
        }

        public override bool Read(GH_IReader reader)
        {
            try { Value = Common.Helpers.ModelArchive.Read<CompositeBeamPropertyModel>(reader.GetByteArray("Data")); return true; }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex); return false; }
        }

        public override string ToString()
        {
            if (Value == null)
                return "Null Beam property";
            else
                return Value.ToString();
        }
    }
}
