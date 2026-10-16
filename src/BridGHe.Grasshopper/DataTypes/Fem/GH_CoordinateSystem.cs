using System;
using Grasshopper.Kernel.Types;

/* Unmerged change from project 'BridGHe.Grasshopper (net7.0-windows)'
Before:
using BridGHe.Common.Models;
After:
using BridGHe.Common.Models;
using FeMM;
using BridGHe.Grasshopper;
using BridGHe.Grasshopper.DataTypes;
using BridGHe.Grasshopper.DataTypes.Fem;
*/
using BridGHe.Common.Models;

namespace BridGHe.Grasshopper.DataTypes.Fem
{
    public class GH_CoordinateSystem : GH_Goo<CoordinateSystemModel>
    {
        public override bool IsValid => Value == null ? false : Value.Name != string.Empty;

        public override string IsValidWhyNot => "Not propely defined";

        public override string TypeName => "CoordinateSystem";

        public override string TypeDescription => "The coordinate system definition";

        public GH_CoordinateSystem()
        {
            Value = CoordinateSystemModel.Global;
        }

        public GH_CoordinateSystem(CoordinateSystemModel cs)
        {
            Value = cs;
        }

        public GH_CoordinateSystem(GH_CoordinateSystem cs)
        {
            Value = new CoordinateSystemModel(cs.Value);
        }

        public override IGH_Goo Duplicate()
        {
            return new GH_CoordinateSystem(this);
        }

        public override string ToString()
        {
            if (Value == null)
                return "Null Coordinate System property";
            else
                return Value.Name;
        }
    }
}
