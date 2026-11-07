using System;
using System.Collections;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using BridGHe.Common.Gpc;
using BridGHe.Common.Gpc.Legacy;
using BridGHe.Grasshopper.DataTypes.Fem;
using Grasshopper.Kernel;

namespace BridGHe.Grasshopper.Helpers
{
    /// <summary>
    /// R6.5 P1, lane 1a (coordinator, 7/11): the outputs of an element component (attributes, loads, tools) whose element input is given by
    /// its GPC draft alone (no FEM value, <c>FromGpc</c>). The output is the copy of the input (its GUID, its analysed element and the GH
    /// data of a plate kept, as the copy constructor keeps them) with the draft built on GPC from the draft of the input, the same builder as
    /// the FEM-backed path. With a FEM-backed input the component works as before (the dual rule of R6.2, until P2).
    /// <para><b>Identity of an output draft without a FEM value</b> (coordinator decision, 7/11): <see cref="GpcIdentity.Derive"/> of the GUID of
    /// the input draft and <c>"&lt;component&gt;:&lt;output index&gt;:" + canonical parameters</c>, where the component is its
    /// <see cref="GH_DocumentObject.ComponentGuid"/> and the parameters are every value the builder takes besides the input draft, in a
    /// canonical invariant text (<see cref="Canonical"/>): deterministic, independent of the plug-in and assembly versions. The FEM-backed path
    /// keeps the identity of the conversion of its FEM output until P2, so the two paths give the same data with other GUIDs (the lane test
    /// compares them modulo GUIDs).</para>
    /// <para>A builder that refuses its data (the refusals of the reader) gives an error and no output: without a FEM value there is no
    /// conversion to fall back on.</para>
    /// </summary>
    internal static class GpcOnlyElements
    {
        /// <summary>The identity of an output draft without a FEM value (see the class).</summary>
        internal static Guid Identity(Guid inputDraft, Guid component, int output, params object[] parameters) =>
            GpcIdentity.Derive(inputDraft, component.ToString("D") + ":" + output.ToString(CultureInfo.InvariantCulture) + ":" + Canonical(parameters));

        /// <summary>R6.5 P1 lane 1b (coordinator decision 1, 7/11): the identity of an element built without an input element (a bridge
        /// generator from curves, numbers and properties; in P2 also Node, Beam and Plate from geometry), the one rule for all of them:
        /// <see cref="GpcIdentity.Derive"/> of <see cref="GpcIdentity.NameSpace"/> and <c>"&lt;component&gt;:&lt;output index&gt;:" +</c> the
        /// canonical parameters of the builder of the element (<see cref="Canonical"/>). By content, as the identity of the conversion of a FEM
        /// value is: two equal elements of the same component share a GUID.</summary>
        internal static Guid ContentIdentity(Guid component, int output, params object[] parameters) =>
            GpcIdentity.Derive(GpcIdentity.NameSpace, component.ToString("D") + ":" + output.ToString(CultureInfo.InvariantCulture) + ":" + Canonical(parameters));

        /// <summary>R6.5 P1 lane 1b: the bridge generators that build elements without an input element (Bridge Bearings, Rigid Links,
        /// Connection Beams, Connections, Deck Mesh, Slab Grillage, Member Assignment, FEM Members) give them by their GPC drafts alone (no FEM
        /// value, <see cref="ContentIdentity"/>): the path that stays in P2. Off (always, outside the lane test <c>gpc-only-generators</c>) they
        /// give the FEM value with its draft, the dual rule of R6.2, so nothing changes with the inputs of today. Removed in P2 with the FEM
        /// path (the generators then always take the path it selects).</summary>
        public static bool GeneratorsWithoutFem { get; set; }

        /// <summary>The draft of a generator without a FEM value, or null after an error on the component (a refusal of the reader: without a
        /// FEM value there is no conversion to fall back on).</summary>
        internal static T Generated<T>(GH_Component component, Func<T> build) where T : class
        {
            try { return build(); }
            catch (Exception x) when (x is ArgumentException || x is NotSupportedException || x is InvalidOperationException)
            {
                component.AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "The GPC datum of the generated element is refused: " + x.Message);
                return null;
            }
        }

        /// <summary>The canonical text of the parameters of a builder: invariant, the exact doubles (round trip, −0 as 0), strings quoted,
        /// lists in brackets, a case record by the SHA-256 of its record archive (data contract XML, uncompressed), a beam property by its
        /// signature (the GPC fingerprint of the property), formulation, angle and the record archive of its section recipe.</summary>
        internal static string Canonical(params object[] parameters) => string.Join("|", (parameters ?? Array.Empty<object>()).Select(One));

        /// <summary>A parameter read when the identity is computed, inside the refusal rule of the builder (the datum of a FEM-backed input,
        /// a load case or a property, is a conversion that may refuse, as inside the builder).</summary>
        internal static Func<object> Lazy(Func<object> parameter) => parameter;

        private static string One(object p) => p switch
        {
            null => "null",
            Func<object> lazy => One(lazy()),
            double d => (d == 0 ? 0.0 : d).ToString("R", CultureInfo.InvariantCulture),
            float f => ((double)f == 0 ? 0.0 : (double)f).ToString("R", CultureInfo.InvariantCulture),
            int i => i.ToString(CultureInfo.InvariantCulture),
            long l => l.ToString(CultureInfo.InvariantCulture),
            bool b => b ? "true" : "false",
            string s => "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"",
            Guid g => g.ToString("D"),
            Enum e => e.GetType().Name + "." + e,
            GPC.Geometry.Vector3d v => "(" + One(v.X) + "," + One(v.Y) + "," + One(v.Z) + ")",
            GPC.Geometry.Point3d v => "(" + One(v.X) + "," + One(v.Y) + "," + One(v.Z) + ")",
            Rhino.Geometry.Vector3d v => "(" + One(v.X) + "," + One(v.Y) + "," + One(v.Z) + ")",
            Rhino.Geometry.Point3d v => "(" + One(v.X) + "," + One(v.Y) + "," + One(v.Z) + ")",
            // R6.5 P1 lane 1b: the parameters of the bridge generators (taper scales, bearing frames, the GPC property of a deck plate).
            Rhino.Geometry.Vector2d v => "(" + One(v.X) + "," + One(v.Y) + ")",
            Rhino.Geometry.Plane f => "plane:" + One(f.Origin) + One(f.XAxis) + One(f.YAxis),
            GPC.Model.ElementProperties.PlateProperty pp => "plate-property:" + GPC.Model.Core.ModelValues.Fingerprint(new object[] { GPC.Model.Core.ModelValues.CopyValue(pp) }),
            LegacyCaseData c => "case:" + Digest(GpcDraftArchive.Write(c)),
            LegacyBeamProperty bp => "property:" + One(bp.Data?.Signature) + ":" + One(bp.Data?.Formulation.ToString()) + ":" + One(bp.Data?.Angle ?? 0)
                + ":" + (bp.Data?.Section == null ? "null" : Digest(GpcDraftArchive.Write(bp.Data.Section))),
            IEnumerable e => "[" + string.Join(",", e.Cast<object>().Select(One)) + "]",
            _ => throw new ArgumentException("GpcOnlyIdentityParameter: no canonical text for " + p.GetType().Name),
        };

        private static string Digest(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));

        /// <summary>The draft of the builder, or null after an error on the component (a refusal of the reader, or an input without draft).</summary>
        private static T Build<T>(GH_Component component, object inputDraft, Func<T> build) where T : class
        {
            if (inputDraft == null) { component.AddRuntimeMessage(GH_RuntimeMessageLevel.Error, NoDraft); return null; }
            try { return build(); }
            catch (Exception x) when (x is ArgumentException || x is NotSupportedException || x is InvalidOperationException)
            {
                component.AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "The GPC datum of the element refuses the change: " + x.Message);
                return null;
            }
        }

        internal const string NoDraft = "The element has neither a FEM value nor a GPC datum.";

        /// <summary>The beam output <paramref name="output"/> of <paramref name="component"/> for a beam given by its draft alone (null: refused,
        /// with the error on the component).</summary>
        internal static GH_Beam Beam(GH_Component component, int output, GH_Beam input, Func<Guid, LegacyBeamDraft> build, params object[] parameters)
        {
            var draft = Build(component, input.Gpc, () => build(Identity(input.Gpc.Beam.Guid, component.ComponentGuid, output, parameters)));
            if (draft == null) return null;
            var copy = new GH_Beam(input); copy.AttachGpc(draft);
            return copy;
        }

        /// <summary>R6.5 P1 lane 1b: the beam output of a component that gives a new beam from its input (Bridge Beam Offsets, Bridge Segment
        /// Override: the FEM path gives a new copy with its results cleared), for a beam given by its draft alone: the draft built on GPC with
        /// the identity derived from the input draft (<see cref="Identity"/>), a new GH GUID and no analysed element (null: refused, with the
        /// error on the component).</summary>
        internal static GH_Beam NewBeam(GH_Component component, int output, GH_Beam input, Func<Guid, LegacyBeamDraft> build, params object[] parameters)
        {
            var draft = Build(component, input.Gpc, () => build(Identity(input.Gpc.Beam.Guid, component.ComponentGuid, output, parameters)));
            return draft == null ? null : GH_Beam.FromGpc(draft);
        }

        /// <summary>The same for a node.</summary>
        internal static GH_Node Node(GH_Component component, int output, GH_Node input, Func<Guid, LegacyNodeDraft> build, params object[] parameters)
        {
            var draft = Build(component, input.Gpc, () => build(Identity(input.Gpc.Node.Guid, component.ComponentGuid, output, parameters)));
            if (draft == null) return null;
            var copy = new GH_Node(input); copy.AttachGpc(draft);
            return copy;
        }

        /// <summary>The same for a plate.</summary>
        internal static GH_Plate Plate(GH_Component component, int output, GH_Plate input, Func<Guid, LegacyPlateDraft> build, params object[] parameters)
        {
            var draft = Build(component, input.Gpc, () => build(Identity(input.Gpc.Plate.Guid, component.ComponentGuid, output, parameters)));
            if (draft == null) return null;
            var copy = new GH_Plate(input); copy.AttachGpc(draft);
            return copy;
        }

        /// <summary>Sets an output item when there is one (a refused output gives none).</summary>
        internal static void Set(IGH_DataAccess access, int output, object value)
        {
            if (value != null) access.SetData(output, value);
        }
    }
}
