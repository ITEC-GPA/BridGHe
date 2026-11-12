using System;
using System.Collections.Generic;
using System.Linq;
using BridGHe.Grasshopper.DataTypes.Fem;
using BridGHe.Grasshopper.Helpers;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;

namespace BridGHe.Grasshopper.Components.Attributes
{
    public class ElementIdComponent : GH_Component
    {
        /// <summary>
        /// Initializes a new instance of the SuperElementComponent class.
        /// </summary>
        public ElementIdComponent()
          : base("Element Id", "EI", "Defines user element ID for node/beam/plate", CategoryNameConstants.CATEGORY_MODEL, CategoryNameConstants.SUBCATEGORY_ATTRIBUTES)
        {
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("Element", "E", "The element where assign the element ID", GH_ParamAccess.item);
            pManager.AddTextParameter("ID", "ID", "The ID assigned by user", GH_ParamAccess.item);
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("Element", "E", "The modified element", GH_ParamAccess.item);
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object is used to retrieve from inputs and store in outputs.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            IGH_GeometricGoo element = null;
            string id = null;
            if (!DA.GetData(0, ref element))
                return;
            if (!DA.GetData(1, ref id))
                return;

            if (string.IsNullOrEmpty(id))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Empty string not admitted");
                return;
            }

            // R6.2 lane A: "already has an ID" on the GPC draft (the first Element ID of its records: null when the value has none, "" for an empty
            // one, as the former attribute value in the message). P2K (P1): the GPC path only: the output takes the draft built on GPC (identity
            // derived from the input draft).
            if (element is GH_Beam beam)
            {
                if (OtherId(() => beam.Gpc.Records.FirstLabel, out string other))
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Beam " + other + " has already an ID");
                    return;
                }
                Func<Guid, Common.Gpc.Legacy.LegacyBeamDraft> build = gid => Common.Gpc.Builders.GpcElementBuilders.WithLabel(beam.Gpc, id, gid);
                GpcOnlyElements.Set(DA, 0, GpcOnlyElements.Beam(this, 0, beam, build, id));
            }
            else if (element is GH_Plate plate)
            {
                if (OtherId(() => plate.Gpc.Records.FirstLabel, out string other))
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Plate " + other + " has already an ID");
                    return;
                }
                Func<Guid, Common.Gpc.Legacy.LegacyPlateDraft> build = gid => Common.Gpc.Builders.GpcPlateBuilders.WithLabel(plate.Gpc, id, gid);
                GpcOnlyElements.Set(DA, 0, GpcOnlyElements.Plate(this, 0, plate, build, id));
            }
            else if (element is GH_Node node)
            {
                if (OtherId(() => node.Gpc.Records.FirstLabel, out string other))
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Node " + other + " has already an ID");
                    return;
                }
                Func<Guid, Common.Gpc.Legacy.LegacyNodeDraft> build = gid => Common.Gpc.Builders.GpcNodeBuilders.WithLabel(node.Gpc, id, gid);
                GpcOnlyElements.Set(DA, 0, GpcOnlyElements.Node(this, 0, node, build, id));
            }
            else
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Unsupported element type");
            }
        }

        /// <summary>R6.2 lane A: whether the element has an Element ID already, and its value for the message, on the GPC draft. P2K (P1): the
        /// draft alone (the former decision of <c>GpcBuilderAttach.Decide</c> without a FEM value).</summary>
        private static bool OtherId(Func<string> firstLabel, out string other)
        {
            other = firstLabel();
            return other != null;
        }

        public override GH_Exposure Exposure => GH_Exposure.quarternary;

        /// <summary>
        /// Provides an Icon for the component.
        /// </summary>
        protected override System.Drawing.Bitmap Icon => Properties.ComponentIcons.For(GetType());

        /// <summary>
        /// Gets the unique ID for this component. Do not change this ID after release.
        /// </summary>
        public override Guid ComponentGuid => new Guid("3467176f-73ce-4557-b60b-961f5841f7a6");
    }
}