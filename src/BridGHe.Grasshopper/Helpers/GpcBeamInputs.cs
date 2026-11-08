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

        /// <summary>The coordinate system of a load: null without one (principal axes), otherwise whether it is the global one. R6.5 P1 lane
        /// 2b: read from its GPC frame (<see cref="GpcLoadFrames.IsGlobal"/>: origin 0, axes 1 and 2 along X and Y), also for a system given by
        /// its frame alone; for the global system of the parameter the rule of the reader on the former points (origin 0, first point
        /// (1, 0, 0), second point (0, 1, 0)).</summary>
        internal static bool? GlobalSystem(GH_CoordinateSystem cs) => cs == null ? null : GpcLoadFrames.IsGlobal(cs.Gpc);

        internal static GPC.Geometry.Point3d Point(Rhino.Geometry.Point3d p) => new GPC.Geometry.Point3d(p.X, p.Y, p.Z);
        internal static GPC.Geometry.Vector3d Vector(Rhino.Geometry.Point3d p) => new GPC.Geometry.Vector3d(p.X, p.Y, p.Z);
        internal static GPC.Geometry.Vector3d Vector(Rhino.Geometry.Vector3d v) => new GPC.Geometry.Vector3d(v.X, v.Y, v.Z);

        /// <summary>
        /// Attaches to <paramref name="output"/> the beam draft built on GPC with the identity of the conversion of its FEM value (until R6.5).
        /// A builder that refuses its data (the reader refuses them: <see cref="ArgumentException"/>, <see cref="NotSupportedException"/>,
        /// <see cref="InvalidOperationException"/>) attaches nothing: the GH type converts its FEM value, which the reader refuses the same way,
        /// as before R6.2. When the draft check is on (the tests), a refusal of the builder where the conversion does not refuse fails the
        /// component.
        /// </summary>
        internal static void Attach(GH_Beam output, Func<Guid, LegacyBeamDraft> build)
        {
            if (output?.Value == null) return;
            Guid identity; LegacyBeamDraft draft;
            try { identity = GpcBeamDrafts.Identity(output.Value); draft = build(identity); }
            catch (Exception x) when (x is ArgumentException || x is NotSupportedException || x is InvalidOperationException)
            {
                if (GpcDraftCheck.Verify == null) return;
                try { GpcBeamDrafts.FromFemm(output.Value); }
                catch (Exception y) when (y is ArgumentException || y is NotSupportedException || y is InvalidOperationException) { Refused++; return; }
                throw new InvalidOperationException("GpcBuilderRefusedWhatTheReaderReads: " + x.Message, x);
            }
            output.AttachGpc(draft);
        }

        /// <summary>The same for a node draft (the rigid links of Bridge Rigid Links).</summary>
        internal static void Attach(GH_Node output, Func<Guid, LegacyNodeDraft> build)
        {
            if (output?.Value == null) return;
            Guid identity; LegacyNodeDraft draft;
            try { identity = GpcBeamDrafts.Identity(output.Value); draft = build(identity); }
            catch (Exception x) when (x is ArgumentException || x is NotSupportedException || x is InvalidOperationException)
            {
                if (GpcDraftCheck.Verify == null) return;
                try { GpcBeamDrafts.FromFemm(output.Value); }
                catch (Exception y) when (y is ArgumentException || y is NotSupportedException || y is InvalidOperationException) { Refused++; return; }
                throw new InvalidOperationException("GpcBuilderRefusedWhatTheReaderReads: " + x.Message, x);
            }
            output.AttachGpc(draft);
        }

        /// <summary>Outputs refused by the builder and by the reader alike (counted when the draft check is on).</summary>
        internal static int Refused;
    }
}
