using System;
using GH_IO.Serialization;
using Grasshopper.Kernel.Types;

using BridGHe.Common.Models;

namespace BridGHe.Grasshopper.DataTypes.Fem
{
    public class GH_LoadCaseStageOperation : GH_Goo<LoadCaseModel.Stage.Operation>
    {
        public override bool IsValid => Value == null ? false : Value.Name != string.Empty;

        public override string IsValidWhyNot => "Not propely defined";

        public override string TypeName => "LoadCase stage operation";

        public override string TypeDescription => "The load case stage operation definition";

        public GH_LoadCaseStageOperation()
        {
        }

        public GH_LoadCaseStageOperation(LoadCaseModel.Stage.Operation operation)
        {
            Value = operation;
        }

        public GH_LoadCaseStageOperation(GH_LoadCaseStageOperation op)
        {
            Value = new LoadCaseModel.Stage.Operation(op.Value);
        }

        public override IGH_Goo Duplicate()
        {
            return new GH_LoadCaseStageOperation(this);
        }

        public override string ToString()
        {
            if (Value == null)
                return "Null LoadCase stage operation";
            else
                return Value.Name;
        }
    }
}
