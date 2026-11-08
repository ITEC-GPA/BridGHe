using System;
using Grasshopper.Kernel.Types;

using BridGHe.Common.Models;

namespace BridGHe.Grasshopper.DataTypes.Fem
{
    public class GH_CoordinateSystem : GH_Goo<CoordinateSystemModel>
    {
        // R6.5 P1 lane 2b: without a FEM value the coordinate system is its GPC frame.
        public override bool IsValid => Value == null ? Gpc != null && Gpc.Name != string.Empty : Value.Name != string.Empty;

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
            // R6.5 P1 lane 2b: a coordinate system given by its GPC frame alone is copied with it (the frame is immutable by convention).
            if (cs.Value == null) { Value = null; _gpc = cs.Gpc; _gpcOf = null; return; }
            Value = new CoordinateSystemModel(cs.Value);
        }

        /// <summary>
        /// R6.5 P1 lane 2b (coordinator, 8/11: <c>GH_CoordinateSystem</c> is data, a GPC datum with its parameter): the GPC datum of this
        /// coordinate system, the GPC form of the load frame (<see cref="GPC.Geometry.CoordinateSystem"/>, the frame the GPC loads take): the one
        /// given (<see cref="FromGpc"/>), otherwise the conversion of the FEM value, once per value (<see cref="Common.Gpc.GpcLoadFrames.Of"/>).
        /// The load components read from it whether the load is in the global system (<see cref="Common.Gpc.GpcLoadFrames.IsGlobal"/>).
        /// Immutable by convention.
        /// </summary>
        public GPC.Geometry.CoordinateSystem Gpc
        {
            get
            {
                if (_gpc != null && ReferenceEquals(_gpcOf, Value)) return _gpc;
                if (Value == null) return null;
                _gpc = Common.Gpc.GpcLoadFrames.Of(Value); _gpcOf = Value;
                return _gpc;
            }
        }

        private GPC.Geometry.CoordinateSystem _gpc;
        private CoordinateSystemModel _gpcOf;

        /// <summary>R6.5 P1 lane 2b: a coordinate system given by its GPC frame alone, with no FEM value: <see cref="Gpc"/> is the frame and
        /// validity, copies and text read it. (No persistence of its own, as before: a stored coordinate system is read back as the global one.)</summary>
        public static GH_CoordinateSystem FromGpc(GPC.Geometry.CoordinateSystem frame)
        {
            var goo = new GH_CoordinateSystem(null as CoordinateSystemModel);
            goo._gpc = frame ?? throw new ArgumentNullException(nameof(frame)); goo._gpcOf = null;
            return goo;
        }

        /// <summary>R6.5 P1 lane 2b: the global coordinate system of the parameter (its «Global» menu): the FEM-backed one of today, or with
        /// <see cref="Helpers.GpcOnlyElements.PropertiesWithoutFem"/> the one given by its GPC frame alone (the path of P2).</summary>
        internal static GH_CoordinateSystem Global() =>
            Helpers.GpcOnlyElements.PropertiesWithoutFem ? FromGpc(Common.Gpc.GpcLoadFrames.Of(CoordinateSystemModel.Global)) : new GH_CoordinateSystem();

        /// <summary>The name of the coordinate system (the message of the load components).</summary>
        internal string Name => Value != null ? Value.Name : Gpc?.Name;

        public override IGH_Goo Duplicate()
        {
            return new GH_CoordinateSystem(this);
        }

        public override string ToString()
        {
            if (Value == null)
                return Gpc == null ? "Null Coordinate System property" : Gpc.Name;
            else
                return Value.Name;
        }
    }
}
