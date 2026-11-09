using BridGHe.Common.Models;
using BridGHe.Grasshopper.DataTypes;
using BridGHe.Grasshopper.DataTypes.Fem;
using BridGHe.Grasshopper.Helpers;
using Grasshopper.Kernel;
using System;
using System.Collections.Generic;
using System.Linq;

namespace BridGHe.Grasshopper.Components.BridgeChecks
{
    public class CompositeBeamComponent : GH_Component
    {
        /// <summary>
        /// Initializes a new instance of the CompositeBeamComponent class.
        /// </summary>
        public CompositeBeamComponent()
          : base("Composite beam", "CB", "Assign the composite sections to the beam", CategoryNameConstants.CATEGORY_CHECKS, CategoryNameConstants.SUBCATEGORY_MODEL)
        {
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("Beam", "B", "The beam to define the mixed section", GH_ParamAccess.item);
            pManager.AddGenericParameter("Section Start", "SS", "Section on the starting node", GH_ParamAccess.item);
            pManager.AddGenericParameter("Section End", "SE", "Section at the end; omitted uses Section Start", GH_ParamAccess.item);
            pManager.AddGenericParameter("Groups", "G", "The groups to assign to the beam", GH_ParamAccess.list);
            pManager[2].Optional = true;
            pManager[3].Optional = true;
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("Beam", "B", "The composite section beam", GH_ParamAccess.item);
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object is used to retrieve from inputs and store in outputs.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            GH_Beam gH_Beam = null;
            GH_CompositeBeamProperty startSection = null;
            GH_CompositeBeamProperty endSection = null;
            List<GH_Group> groups = new List<GH_Group>();

            if (!DA.GetData(0, ref gH_Beam))
                return;
            if (!DA.GetData(1, ref startSection))
                return;
            DA.GetData(2, ref endSection);
            endSection ??= startSection;
            DA.GetDataList(3, groups);

            // R6.5 P1 lane 3a (coordinator, 8/11): an input given by its GPC datum alone (the beam, a composite property or a group without FEM
            // value) gives the composite beam by its GPC datum alone (GpcOnlyModels); the FEM path is the same as before.
            if (GpcOnlyModels.Any(gH_Beam, startSection, endSection, groups))
            {
                SolveGpc(DA, gH_Beam, startSection, endSection, groups);
                return;
            }

            if (gH_Beam?.Value == null || startSection?.Value == null || endSection?.Value == null)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "A beam and a valid start section are required.");
                return;
            }

            BeamModel bb = gH_Beam.Value;
            CompositeBeamModel compositeBeamModel = new CompositeBeamModel()
            {
                PointFrom = bb.PointFrom,
                PointTo = bb.PointTo,
                BeamId = bb.BeamId,
                StartBeamProperty = startSection.Value,
                EndBeamProperty = endSection.Value,
            };

            for (int i = 0; i < bb.Attributes.Count; i++)
                compositeBeamModel.Attributes.Add(bb.Attributes[i]);
            for (int i = 0; i < bb.Loads.Count; i++)
                compositeBeamModel.Loads.Add(bb.Loads[i]);
            for (int i = 0; i < bb.Groups.Count; i++)
                compositeBeamModel.Groups.Add(bb.Groups[i]);
            foreach (GH_Group group in groups)
                if (group?.Value is not null && !compositeBeamModel.Groups.Contains(group.Value))
                    compositeBeamModel.Groups.Add(group.Value);
            for (int i = 0; i < bb.Results.Count; i++)
                compositeBeamModel.Results.Add(bb.Results[i]);

            // Chain E (c): the beam carries the definitions of its two composite properties (what Super Element Check reads). A property
            // that is not a valid definition (e.g. a vertical offset) leaves them to the beam, which builds them when a check reads them and
            // fails there, as the checks did before.
            GPC.Checkers.CompositeBridge.BridgeCompositeSectionDefinition s, e;
            try { s = startSection.Definition; e = endSection.Definition; }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or InvalidOperationException) { s = e = null; }
            // R6.5 P1 lane 3a: the GPC datum beside the FEM value (the dual rule of R6.2): the draft of the beam, the two definitions, the groups;
            // none when the draft of the beam is refused. The identity is derived from the input beam (no new GUID per solve).
            GpcCompositeBeam datum = null;
            try { if (gH_Beam.Gpc is { } draft) datum = new GpcCompositeBeam(draft, s, e, Groups(draft, groups)); }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or InvalidOperationException) { }
            var identity = Identity(gH_Beam, s, e, datum?.Groups ?? compositeBeamModel.Groups.Select(g => g.FullName).ToArray());
            GH_CompositeBeam ms_beam = new GH_CompositeBeam(compositeBeamModel, identity) { AnalysedGpc = gH_Beam.AnalysedGpc, AnalysedGpcModel = gH_Beam.AnalysedGpcModel };
            ms_beam.StartDefinition = s; ms_beam.EndDefinition = e;
            if (datum != null) ms_beam.AttachGpc(datum);

            DA.SetData(0, ms_beam);
        }

        /// <summary>R6.5 P1 lane 3a: the composite beam by its GPC datum alone: the draft of the beam, the definitions of the two composite
        /// properties (null when a property is not a valid definition, as on the FEM path: the checks that read it give the error), the groups.
        /// No beam draft or no start definition datum: the error of the FEM path, no output.</summary>
        private void SolveGpc(IGH_DataAccess DA, GH_Beam beam, GH_CompositeBeamProperty start, GH_CompositeBeamProperty end, List<GH_Group> groups)
        {
            Common.Gpc.Legacy.LegacyBeamDraft draft = null;
            try { draft = beam?.Gpc; } catch (Exception ex) when (ex is ArgumentException or NotSupportedException or InvalidOperationException) { }
            if (draft == null || start == null || end == null || start.Value == null && start.Gpc == null || end.Value == null && end.Gpc == null)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "A beam and a valid start section are required.");
                return;
            }
            GPC.Checkers.CompositeBridge.BridgeCompositeSectionDefinition s, e;
            try { s = start.Definition; e = end.Definition; }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or InvalidOperationException) { s = e = null; }
            var datum = new GpcCompositeBeam(draft, s, e, Groups(draft, groups));
            var output = GH_CompositeBeam.FromGpc(datum, Identity(beam, datum.Start, datum.End, datum.Groups));
            output.AnalysedGpc = beam.AnalysedGpc; output.AnalysedGpcModel = beam.AnalysedGpcModel;
            DA.SetData(0, output);
        }

        /// <summary>The full names of the groups of the composite beam: those of the beam (its draft), then those of the Groups input it has not,
        /// in order (the rule of the FEM path on the group models).</summary>
        private static IReadOnlyList<string> Groups(Common.Gpc.Legacy.LegacyBeamDraft draft, List<GH_Group> groups)
        {
            var names = new List<string>(draft.Records?.Groups ?? (IReadOnlyList<string>)Array.Empty<string>());
            foreach (var g in groups)
                if (g?.Gpc is { Count: > 0 } chain && Common.Gpc.Builders.GpcBeamBuilders.FullName(chain) is var full && !names.Contains(full)) names.Add(full);
            return names;
        }

        /// <summary>R6.5 P1 lane 3a (coordinator decision 6, 8/11): the identity of the composite beam, derived from the GH GUID of its beam and
        /// the canonical text of its sections (the SHA-256 of their GPC archive) and groups (<see cref="GpcOnlyElements.Identity"/>, the rule
        /// of lane 1a), the same on both paths: no new GUID per solve.</summary>
        private Guid Identity(GH_Beam beam, GPC.Checkers.CompositeBridge.BridgeCompositeSectionDefinition start, GPC.Checkers.CompositeBridge.BridgeCompositeSectionDefinition end,
            IReadOnlyList<string> groups) => GpcOnlyElements.Identity(beam.Guid, ComponentGuid, 0, Digest(start), Digest(end), groups);

        private static string Digest(GPC.Checkers.CompositeBridge.BridgeCompositeSectionDefinition definition)
        {
            if (definition == null) return "null";
            try { return "composite:" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Common.Gpc.GpcDraftArchive.Write(definition))); }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or InvalidOperationException or System.Runtime.Serialization.SerializationException) { return "composite:" + definition.Name; }
        }

        /// <summary>
        /// Provides an Icon for the component.
        /// </summary>
        protected override System.Drawing.Bitmap Icon => Properties.ComponentIcons.For(GetType());

        /// <summary>
        /// Gets the unique ID for this component. Do not change this ID after release.
        /// </summary>
        public override Guid ComponentGuid => new Guid("07260514-76e4-4a8d-8b1e-69288e348b6f");
    }
}
