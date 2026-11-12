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

            // R6.5 P1 lane 3a (P2K: every input): the composite beam by its GPC datum alone (the draft of the beam, the composite properties,
            // the groups); no FEM value is read or made.
            SolveGpc(DA, gH_Beam, startSection, endSection, groups);
        }

        /// <summary>R6.5 P1 lane 3a: the composite beam by its GPC datum alone: the draft of the beam, the definitions of the two composite
        /// properties, the groups. No beam draft or no start definition datum: the error of the FEM path, no output. R25-01 (C1, 11/11): a
        /// property that is not a valid definition gives its error and no output (before, both definitions null and a datum called valid).</summary>
        private void SolveGpc(IGH_DataAccess DA, GH_Beam beam, GH_CompositeBeamProperty start, GH_CompositeBeamProperty end, List<GH_Group> groups)
        {
            Common.Gpc.Legacy.LegacyBeamDraft draft = null;
            try { draft = beam?.Gpc; } catch (Exception ex) when (ex is ArgumentException or NotSupportedException or InvalidOperationException) { }
            if (draft == null || start == null || end == null || start.Value == null && start.Gpc == null || end.Value == null && end.Gpc == null)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "A beam and a valid start section are required.");
                return;
            }
            // R25-01 (C1, 11/11): a composite property that is not a valid definition is refused here, with the message of the definition, and
            // nothing is given (without a FEM value there is no composite beam the checks could refuse later; the FEM path keeps its rule above).
            GPC.Checkers.CompositeBridge.BridgeCompositeSectionDefinition s = null, e = null;
            try { s = start.Definition; e = end.Definition; }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or InvalidOperationException)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, (s == null ? "Section Start" : "Section End") + ": the composite section is refused: " + ex.Message);
                return;
            }
            var datum = new GpcCompositeBeam(draft, s, e, Groups(draft, groups));
            var output = GH_CompositeBeam.FromGpc(datum, Identity(draft, datum.Start, datum.End, datum.Groups));
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

        /// <summary>R6.5 P1 lane 3a (coordinator decision 6, 8/11): the identity of the composite beam, derived from its beam and the canonical
        /// text of its sections (the SHA-256 of their GPC archive) and groups (<see cref="GpcOnlyElements.Identity"/>, the rule of lane 1a): no
        /// new GUID per solve. P2K (P7): derived from the GUID of the draft of the beam, as <see cref="GpcOnlyElements.Beam"/> does, not from
        /// the GH GUID of the beam (new at every solve for a beam given by its draft alone, <c>GH_Beam.FromGpc</c> without a GUID).</summary>
        private Guid Identity(Common.Gpc.Legacy.LegacyBeamDraft beam, GPC.Checkers.CompositeBridge.BridgeCompositeSectionDefinition start, GPC.Checkers.CompositeBridge.BridgeCompositeSectionDefinition end,
            IReadOnlyList<string> groups) => GpcOnlyElements.Identity(beam.Beam.Guid, ComponentGuid, 0, Digest(start), Digest(end), groups);

        /// <summary>P2F (P7): the content of a composite definition, the GPC input fingerprint (<c>ModelValues.Fingerprint</c>, the same in every
        /// process for the same content) of a copy read back from its archive. Before, the SHA-256 of the archive itself, whose bytes change once
        /// the definition has been fingerprinted (the fingerprint, taken elsewhere on the same definition, changes what the archive writes, not
        /// what it fingerprints): the composite beam got another GUID at every solve. The copy leaves the input definition as it is (the
        /// fingerprint is never taken on it here: shared-properties).</summary>
        private static string Digest(GPC.Checkers.CompositeBridge.BridgeCompositeSectionDefinition definition)
        {
            if (definition == null) return "null";
            try { return "composite:" + GPC.Model.Core.ModelValues.Fingerprint(new object[] { Common.Gpc.GpcDraftArchive.ReadComposite(Common.Gpc.GpcDraftArchive.Write(definition)) }); }
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
