using System;
using System.Collections.Generic;
using System.Linq;
using BridGHe.Common.Geometry;
using BridGHe.Common.Gpc.Legacy;
using GPC.Model.Core.Identity;
using GPC.Model.ElementProperties;
using GPC.Model.Elements;
using GPC.Model.Materials;
using GPC.Model.Structure.Assignments;

namespace BridGHe.Common.Gpc.Builders
{
    /// <summary>
    /// R6.2, lane B (beams): the GPC builders of the bridge generators (Bridge Member Assignment / FEM Members, Bridge Slab Grillage, Bridge
    /// Connections, Bridge Segment Sections, Bridge Segment Override): the drafts and the property data the reader gives their FEM outputs,
    /// built from data without FEM values (<see cref="BridgeMemberPlan"/>, the GPC data of their GH inputs). Units: the definition units.
    /// <para>Signature of a property a generator builds (Bridge Slab Grillage, Bridge Segment Sections, Bridge Segment Override with Preserve
    /// Mass): until R6.3/R6.5 the one of the conversion of its FEM value (passo 53), given by the caller
    /// (<see cref="BridgeGpcProperties.FormerSignature"/>); everything else is built on GPC.</para>
    /// </summary>
    public static class GpcBridgeGeometryBuilders
    {
        private static GPC.Geometry.Point3d P(Rhino.Geometry.Point3d p) => new GPC.Geometry.Point3d(p.X, p.Y, p.Z);

        /// <summary>
        /// Bridge Member Assignment: the beam of a plan, as the reader gives the former beam (<c>BridgeMemberAssignment.Assign</c>): its ends,
        /// the property datum, the BridGHe angle that turns its section y onto the deck frame (<see cref="BridgeBeamFrames.Orientation"/> with
        /// the principal angle of the property), its export ID as name and first Element ID, its group (family\Group&lt;g&gt;\Segment&lt;s&gt;),
        /// the super element G&lt;g&gt; of a girder, its bridge member (stations, section up, geometric ID) and its taper (alignment Center, the
        /// default of the former attribute).
        /// </summary>
        public static LegacyBeamDraft Assigned(BridgeMemberPlan plan, Guid identity)
        {
            if (plan == null) throw new ArgumentNullException(nameof(plan));
            var property = plan.Property();
            var (angle, up) = BridgeBeamFrames.Orientation(plan.From, plan.To, property.Data.Angle, plan.DeckY, plan.DeckX);
            var chain = new[] { plan.Family.ToString(), $"Group{plan.Group}", $"Segment{plan.Segment}" };
            var w = GpcBeamBuilders.Start(P(plan.From), P(plan.To), property, new[] { chain }, angle, 0, 0, identity, plan.SolverId);
            if (plan.Label) w.Label(plan.SolverId);
            if (plan.Family == BridgeMemberFamily.Girder) w.SuperElement($"G{plan.Group}");
            w.Member(plan.PhysicalId, plan.Family.ToString(), plan.Group, plan.Segment, plan.Span, GpcBeamBuilders.Z(plan.StationFrom), GpcBeamBuilders.Z(plan.StationTo),
                GpcBeamBuilders.Z(new GPC.Geometry.Vector3d(up.X, up.Y, up.Z)), plan.GeometryId);
            if (plan.ScaleFrom.HasValue)
            {
                var sf = plan.ScaleFrom.Value; var st = plan.ScaleTo.Value;
                w.Taper(GpcBeamBuilders.Z(sf.X), GpcBeamBuilders.Z(sf.Y), GpcBeamBuilders.Z(st.X), GpcBeamBuilders.Z(st.Y), GpcBeamBuilders.TaperAlignments[1], GpcBeamBuilders.TaperAlignments[1]);
            }
            return GpcBeamBuilders.Finish(w);
        }

        /// <summary>Bridge Connection Beams: a deformable connection member (export ID "Connection&lt;i&gt;", group DeformableConnections).</summary>
        public static LegacyBeamDraft ConnectionBeam(Rhino.Geometry.Point3d from, Rhino.Geometry.Point3d to, LegacyBeamProperty property, string beamId, Guid identity) =>
            GpcBeamBuilders.New(P(from), P(to), property, new[] { new[] { "DeformableConnections" } }, 0, 0, 0, identity, beamId);

        /// <summary>
        /// Bridge Rigid Links: a node with its BridGHe ID and, for the first node of a link, its rigid connection to the target point (a
        /// <see cref="GPC.Model.Attributes.RigidConnectionAttribute"/>; the assembly makes the link), as the reader gives the former node.
        /// <para>R6.2 (P2N, after the merge of R62A): the node of lane A (<see cref="GpcNodeBuilders.Create"/>: position, ID, no groups) with the
        /// rigid connection the reader adds after the attributes; every number as the reader reads it (<see cref="GpcBeamBuilders.Z"/>).</para>
        /// </summary>
        public static LegacyNodeDraft RigidNode(Rhino.Geometry.Point3d position, string nodeId, Rhino.Geometry.Point3d? target, Guid identity)
        {
            var draft = GpcNodeBuilders.Create(GpcBeamBuilders.Z(position.X), GpcBeamBuilders.Z(position.Y), GpcBeamBuilders.Z(position.Z), nodeId ?? "", null, identity);
            if (target.HasValue)
            {
                var units = GpcBeamDrafts.DraftUnits; var t = target.Value;
                draft.Node.Attributes.Add(new GPC.Model.Attributes.RigidConnectionAttribute(new GPC.Geometry.Point3d(units.ToMm(GpcBeamBuilders.Z(t.X)), units.ToMm(GpcBeamBuilders.Z(t.Y)), units.ToMm(GpcBeamBuilders.Z(t.Z)))));
            }
            return draft;
        }

        /// <summary>
        /// Bridge Slab Grillage and Bridge Segment Sections: a beam property on a standard section of the definition units (its recipe, the
        /// numbers as written: <see cref="LegacySectionRecipe"/>), with the analysis material given (N-mm) and the formulation; the section and
        /// its shear areas as the reader builds them from the recipe (<c>LegacyModelImport.SectionProperty</c>); R6.3: the signature is the GPC
        /// fingerprint of the property (<see cref="LegacyModelImport.PropertySignature"/>).
        /// </summary>
        /// <param name="materialKind">L2C (limit (b) of passo 77): the kind of the former material of the property (Segment Sections: that of the
        /// base property; Slab Grillage: the concrete), recorded beside the GPC property.</param>
        public static LegacyBeamProperty StandardProperty(string propertyName, LegacySectionRecipe recipe, Material analysisMaterial, BeamFormulation formulation, string materialKind = null)
        {
            if (recipe == null) throw new ArgumentNullException(nameof(recipe));
            var (property, angle, native) = LegacyModelImport.SectionProperty(recipe, propertyName, GPC.Model.Core.ModelValues.CopyValue(analysisMaterial ?? throw new ArgumentNullException(nameof(analysisMaterial))),
                GpcBeamDrafts.DraftUnits, new Dictionary<string, (double, double)>(StringComparer.Ordinal), new LegacyImportReport(), checkLengthUnit: false);
            return new LegacyBeamProperty(property, new LegacyBeamPropertyData(formulation, angle, native, LegacyModelImport.PropertySignature(property)) { Section = recipe, MaterialKind = materialKind });
        }

        /// <summary>The recipe of a standard section given by its dimensions (the others 0), as the reader reads a former SectionModel; L2C:
        /// with the fillet radius <paramref name="r"/> (the conci of Bridge Segment Sections).</summary>
        public static LegacySectionRecipe StandardSection(BridgeSectionKind kind, string name, double d = 0, double b = 0, double b1 = 0, double b2 = 0, double t = 0, double t1 = 0, double t2 = 0, double t3 = 0,
            double r = 0) =>
            new LegacySectionRecipe { Kind = kind, Name = name ?? "", Angle = 0, D = GpcBeamBuilders.Z(d), B = GpcBeamBuilders.Z(b), B1 = GpcBeamBuilders.Z(b1), B2 = GpcBeamBuilders.Z(b2),
                T = GpcBeamBuilders.Z(t), T1 = GpcBeamBuilders.Z(t1), T2 = GpcBeamBuilders.Z(t2), T3 = GpcBeamBuilders.Z(t3), R = GpcBeamBuilders.Z(r) };

        /// <summary>
        /// Bridge Segment Override: the replacement property of a concio, the datum of the input property with, when Preserve Mass is on, the
        /// density of its material set to the mass per length of the source girder over the area of the replacement (the areas of the former
        /// sections, <see cref="GpcSectionMass.LegacyArea"/>); R6.3: the signature is the GPC fingerprint of the property (it follows the density).
        /// </summary>
        public static LegacyBeamProperty Replacement(LegacyBeamProperty replacement, BeamProperty source, bool preserveMass)
        {
            if (replacement == null) throw new ArgumentNullException(nameof(replacement));
            if (!preserveMass) return new LegacyBeamProperty(replacement.Property, replacement.Data with { Signature = LegacyModelImport.PropertySignature(replacement.Property) });
            var property = GPC.Model.Core.ModelValues.CopyValue((ElasticBeamProperty)replacement.Property);
            var from = (ElasticBeamProperty)source;
            double mass = from.Material.Density * GpcSectionMass.LegacyArea(from.Section);
            property.Material.Density = GpcBeamBuilders.Z(mass / GpcSectionMass.LegacyArea(property.Section));
            return new LegacyBeamProperty(property, replacement.Data with { Signature = LegacyModelImport.PropertySignature(property) });
        }
    }
}
