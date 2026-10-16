using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using GH_IO.Serialization;
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
    public class GH_Group : GH_Goo<GroupModel>
    {
        public override bool IsValid => Value == null ? false : Value.Name != string.Empty;

        public override string IsValidWhyNot => "Not propely defined";

        public override string TypeName => "Group";

        public override string TypeDescription => "The group definition";

        public GH_Group()
        {
        }

        public GH_Group(GroupModel group)
        {
            Value = group;
        }

        public GH_Group(GH_Group grp)
        {
            Value = new GroupModel(grp.Value);
        }

        public override IGH_Goo Duplicate()
        {
            return new GH_Group(this);
        }

        public override string ToString()
        {
            if (Value == null)
                return "Null Group property";
            else
                return Value.ToString();
        }
    }
}
