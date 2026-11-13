using System;
using System.Collections.Generic;
using System.Linq;
using BridGHe.Common.Gpc;
using BridGHe.Common.Gpc.Legacy;
using BridGHe.Common.Models;
using BridGHe.Grasshopper.DataTypes.Fem;

namespace BridGHe.Grasshopper.Helpers
{
    /// <summary>
    /// R6.2, lane B (beams): what the beam producer components give their GPC builders besides the GPC data of their GH inputs
    /// (<c>GH_Beam.Gpc</c>, <c>GH_BeamProperty.Gpc</c>, <c>GH_LoadCase.Gpc</c>), and how they attach the result.
    /// <para>Inputs without a GPC datum in the frozen contract of R6.2 (§1bis): <c>GH_Group</c> (the chain of names of its group) and
    /// <c>GH_CoordinateSystem</c> (whether it is the global one). Their FEM values are read only here, to replace when those GH types carry a
    /// GPC group and coordinate system (R6.5). R6.5 P1 lane 2b: the coordinate system is read from its GPC frame.</para>
    /// </summary>
    internal static class GpcBeamInputs
    {
        /// <summary>The names of a group from its root (the full name of the reader is their join with "\").</summary>
        internal static IReadOnlyList<string> GroupChain(GroupModel group)
        {
            var names = new List<string>();
            for (var g = group; g != null; g = g.Parent) names.Insert(0, g.Name);
            return names;
        }

        internal static IReadOnlyList<IReadOnlyList<string>> GroupChains(IEnumerable<GroupModel> groups) => groups.Select(GroupChain).ToArray();

        internal static IReadOnlyList<IReadOnlyList<string>> GroupChains(IEnumerable<GH_Group> groups) => groups.Select(GpcBuilderAttach.GroupChain).ToArray(); // R6.2 part C: the GPC datum of the GH group

        /// <summary>The coordinate system of a load: null without one (principal axes), otherwise whether it is the global one, by the one
        /// rule of the frame (<see cref="GpcLoadFrames.IsGlobal"/>: origin 0, axes 1 and 2 along X and Y; R65-C2-01, user decision U6 of 12/11:
        /// the same rule for every system and for the reader of the 0.3.0 files).</summary>
        internal static bool? GlobalSystem(GH_CoordinateSystem cs) => cs == null ? null : GpcLoadFrames.IsGlobal(cs.Gpc);

        internal static GPC.Geometry.Point3d Point(Rhino.Geometry.Point3d p) => new GPC.Geometry.Point3d(p.X, p.Y, p.Z);
        internal static GPC.Geometry.Vector3d Vector(Rhino.Geometry.Point3d p) => new GPC.Geometry.Vector3d(p.X, p.Y, p.Z);
        internal static GPC.Geometry.Vector3d Vector(Rhino.Geometry.Vector3d v) => new GPC.Geometry.Vector3d(v.X, v.Y, v.Z);



        /// <summary>Outputs whose draft the builder refused (P2D: every refusal, the draft check is removed).</summary>
        internal static int Refused;
    }
}
