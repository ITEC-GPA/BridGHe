using System;
using System.Collections.Generic;
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

            // R6.5 P1 lane 3c. P2K (P1): the model by its GPC input alone, the only path.
            if (model.GpcInput == null)
                return;
            // P30-01 (C1, 11/11): the results saved with the model could not be read: its elements come without them, Run is needed.
            if (model.ResultsNotice != null && !model.HasAnalysedGpc) AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, model.ResultsNotice);

            List<GH_Node> nodes = new List<GH_Node>();
            List<GH_Beam> beams = new List<GH_Beam>();
            List<GH_Plate> plates = new List<GH_Plate>();

            try
            {
                // R6.5 P1 lane 3c: the elements by the drafts the model carries, with their identity, and the GH GUIDs of the model (those Build Model
                // was given; Element Filter, downstream, filters by them).
                var carried = GpcModelDraftsAttach.OfGpcOnly(model);
                var guids = model.ElementGuidsOf(carried);
                int g = 0;
                foreach (var d in carried.Nodes) nodes.Add(GH_Node.FromGpc(d, guids[g++]));
                foreach (var d in carried.Beams) beams.Add(GH_Beam.FromGpc(d, guids[g++]));
                foreach (var d in carried.Plates) plates.Add(GH_Plate.FromGpc(d, guids[g++]));
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