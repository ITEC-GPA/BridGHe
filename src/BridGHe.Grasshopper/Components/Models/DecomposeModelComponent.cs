using System;
using System.Collections.Generic;
using BridGHe.Common.Models;
using BridGHe.Grasshopper.DataTypes.Fem;
using BridGHe.Grasshopper.Helpers;
using Grasshopper.Kernel;

namespace BridGHe.Grasshopper.Components.Models
{
    public class DecomposeModelComponent : GH_Component
    {
        /// <summary>
        /// Initializes a new instance of the DecomposeModelComponent class.
        /// </summary>
        public DecomposeModelComponent()
          : base("Decompose Model", "DM", "Decompose a FEM model in his elements", CategoryNameConstants.CATEGORY_MODEL, CategoryNameConstants.SUBCATEGORY_MODEL)
        {
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("Model", "M", "The FEM model", GH_ParamAccess.item);
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("Nodes", "N", "The nodes results", GH_ParamAccess.list);
            pManager.AddGenericParameter("Beams", "B", "The beams results", GH_ParamAccess.list);
            pManager.AddGenericParameter("Plates", "P", "The plates results", GH_ParamAccess.list);
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object is used to retrieve from inputs and store in outputs.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            GH_Model model = null;
            if (!DA.GetData("Model", ref model))
                return;

            if (model.Value == null)
                return;

            List<GH_Node> nodes = new List<GH_Node>();
            List<GH_Beam> beams = new List<GH_Beam>();
            List<GH_Plate> plates = new List<GH_Plate>();

            try
            {
                foreach (NodeModel node in model.Value.Nodes)
                    nodes.Add(new GH_Node(node, model.GetGuid(node)));
                foreach (BeamModel beam in model.Value.Beams)
                    beams.Add(new GH_Beam(beam, model.GetGuid(beam)));
                foreach (PlateModel plate in model.Value.Plates)
                    plates.Add(new GH_Plate(plate, model.GetGuid(plate)));
                // R6.4 (2), extension (α): the drafts the model carries for its elements (Build Model, the analysis, a file, ...), with the
                // identity of the conversion of every FEM value until R6.5; none: the conversion of the FEM values, as before.
                var drafts = GpcModelDraftsAttach.Of(model);
                if (drafts != null)
                {
                    for (int i = 0; i < nodes.Count; i++) GpcModelDraftsAttach.Attach(nodes[i], drafts.Nodes[i]);
                    for (int i = 0; i < beams.Count; i++) GpcModelDraftsAttach.Attach(beams[i], drafts.Beams[i]);
                    for (int i = 0; i < plates.Count; i++) GpcModelDraftsAttach.Attach(plates[i], drafts.Plates[i]);
                }
                // F6 C4: the analysed GPC elements, by the numbering of the legacy reader used by the analysis (FEM order, from 1).
                if (model.HasAnalysedGpc)
                {
                    var gpc = model.Gpc;
                    if (gpc.NodesElements.Count == nodes.Count && gpc.BeamElements.Count == beams.Count && gpc.AreaElements.Count == plates.Count)
                    {
                        for (int i = 0; i < nodes.Count; i++) { nodes[i].AnalysedGpc = gpc.NodesElements[i + 1]; nodes[i].AnalysedGpcModel = gpc; }
                        for (int i = 0; i < beams.Count; i++) { beams[i].AnalysedGpc = gpc.BeamElements[i + 1]; beams[i].AnalysedGpcModel = gpc; }
                        for (int i = 0; i < plates.Count; i++) { plates[i].AnalysedGpc = gpc.AreaElements[i + 1]; plates[i].AnalysedGpcModel = gpc; }
                    }
                    else AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, "Modello GPC analizzato con un numero di elementi diverso dal modello FEM: risultati GPC non collegati.");
                }
            }
            catch (Exception e)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, e.Message);
            }

            DA.SetDataList(0, nodes);
            DA.SetDataList(1, beams);
            DA.SetDataList(2, plates);
        }

        public override GH_Exposure Exposure => GH_Exposure.tertiary;

        /// <summary>
        /// Provides an Icon for the component.
        /// </summary>
        protected override System.Drawing.Bitmap Icon => Properties.ComponentIcons.For(GetType());

        /// <summary>
        /// Gets the unique ID for this component. Do not change this ID after release.
        /// </summary>
        public override Guid ComponentGuid => new Guid("e39b6cad-cf34-4766-a8b5-fa3fe2effeb9");
    }
}