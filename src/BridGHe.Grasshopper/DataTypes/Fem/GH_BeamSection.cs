using GH_IO.Serialization;
using Grasshopper.Kernel.Types;
using Grasshopper.Kernel;
using Rhino.Geometry;
using System;
using System.Collections.Generic;
using System.Linq;

namespace BridGHe.Grasshopper.DataTypes.Fem
{
    /// <summary>
    /// The Beam Section type. P2CE (13/11/2026, phase 2b): on its GPC recipe alone (<see cref="Gpc"/>), without the former FEM section: the casts
    /// of a text or a curve make the recipe on GPC (<see cref="Helpers.GpcSectionCasts"/>, the same results as the former FEM cast and its
    /// conversion, checked by p2ce-reader while the FEM section existed), and the text of a cast section is the former text of its FEM section.
    /// </summary>
    public class GH_BeamSection : GH_Goo<object>, IGH_PreviewData
    {
        // The native outlines of the former FEM section (its Straus native section): none on the recipe (as for every section without FEM value).
        public BoundingBox ClippingBox => BoundingBox.Empty;
        public void DrawViewportWires(GH_PreviewWireArgs args) { }
        public void DrawViewportMeshes(GH_PreviewMeshArgs args) { }

        public override bool IsValid => Gpc != null;

        public override string IsValidWhyNot => "The section type is not defined";

        public override string TypeName => "BeamSection";

        public override string TypeDescription => "Defines a beam section";

        public GH_BeamSection()
        {
            Value = null;
        }

        public GH_BeamSection(GH_BeamSection section)
        {
            Value = null;
            section?.CarryGpcTo(this);
        }

        /// <summary>The casts of a text and of a curve (the former rules, on GPC: <see cref="Helpers.GpcSectionCasts"/>).</summary>
        public override bool CastFrom(object source)
        {
            Common.Gpc.Legacy.LegacySectionRecipe recipe;
            bool cast = source switch
            {
                GH_String str => Helpers.GpcSectionCasts.Text(str.Value, out recipe),
                GH_Curve curve => Helpers.GpcSectionCasts.Curve(curve.Value, out recipe),
                _ => Refused(out recipe),
            };
            if (!cast) return false;
            Value = null; _gpc = recipe; HasAttachedGpc = false; _cast = recipe != null;
            return true;
        }

        private static bool Refused(out Common.Gpc.Legacy.LegacySectionRecipe recipe) { recipe = null; return false; }

        /// <summary>
        /// R6.1 (frozen API for R6.2, §1bis): the GPC datum of this GH_BeamSection, the section recipe in the definition units (the property builds the
        /// GPC section): the one a GPC builder attached (<see cref="AttachGpc"/>), the one read from a file (BGHGPC1), the former payload read by value
        /// (<see cref="Common.Gpc.GpcLegacyPayloads"/>), or the one of a cast. Immutable by convention.
        /// </summary>
        public Common.Gpc.Legacy.LegacySectionRecipe Gpc => _gpc;

        /// <summary>True when <see cref="Gpc"/> was attached by a GPC builder or read from a file (not made by a cast, as the former conversion of
        /// the FEM value of a cast).</summary>
        public bool HasAttachedGpc { get; private set; }

        /// <summary>Attaches the GPC datum built on GPC.</summary>
        public void AttachGpc(Common.Gpc.Legacy.LegacySectionRecipe datum)
        {
            _gpc = datum ?? throw new ArgumentNullException(nameof(datum)); HasAttachedGpc = true; _cast = false;
        }

        private Common.Gpc.Legacy.LegacySectionRecipe _gpc;
        // A section made by a cast (a text or a curve): its text is the former text of its FEM section (not kept by a file, as before).
        private bool _cast;

        /// <summary>R6.1: a copy (<c>Duplicate</c>) keeps the datum.</summary>
        private void CarryGpcTo(GH_BeamSection copy)
        {
            if (_gpc != null) { copy._gpc = _gpc; copy.HasAttachedGpc = HasAttachedGpc; copy._cast = _cast; }
        }

        /// <summary>R6.5 P0 (coordinator, 7/11): a section given by its GPC recipe alone: <see cref="Gpc"/> is the recipe and validity, copies,
        /// persistence and text read it.</summary>
        public static GH_BeamSection FromGpc(Common.Gpc.Legacy.LegacySectionRecipe recipe)
        {
            var goo = new GH_BeamSection();
            goo._gpc = recipe ?? throw new ArgumentNullException(nameof(recipe)); goo.HasAttachedGpc = true;
            return goo;
        }

        public override IGH_Goo Duplicate() => new GH_BeamSection(this);

        public override IGH_GooProxy EmitProxy()
        {
            return null;
        }

        public override bool Read(GH_IReader reader)
        {
            try
            {
                // P2D: the recipe (records), or a former payload / the FEM chunk alone of a development file read by value; never a FEM value.
                var datum = GpcGooPersistence.Read(reader, out _, out var records, out var femm) switch
                {
                    GpcGooPersistence.Kind.Gpc => Common.Gpc.GpcDraftArchive.ReadSection(records),
                    GpcGooPersistence.Kind.Legacy => Common.Gpc.GpcLegacyPayloads.Section(femm),
                    _ => null,
                };
                if (datum == null) return false;
                Value = null; _gpc = datum; HasAttachedGpc = true; _cast = false; return true;
            }
            catch (Exception ex) { return GpcGooPersistence.Refused(reader, TypeName, ex); }
        }

        public override object ScriptVariable()
        {
            return null;
        }

        public override bool Write(GH_IWriter writer)
        {
            try { GpcGooPersistence.Write(writer, ((byte[])null, Common.Gpc.GpcDraftArchive.Write(Gpc))); return true; } // P2D: the recipe alone
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex); return false; }
        }

        public override string ToString()
        {
            var g = Gpc;
            if (g == null) return "Null Section";
            // P2CE phase 2b: a cast section shows the former text of its FEM section (GpcSectionCasts.FormerText, from the numbers of its recipe).
            return _cast ? Helpers.GpcSectionCasts.FormerText(g) : $"{g.Kind}: {g.Name} (D {g.D}, B {g.B}, A {g.Area})\nGPC datum (no FEM value)";
        }
    }
}
