using System;
using System.Collections.Generic;
using BridGHe.Common.Models;
using BridGHe.Grasshopper.DataTypes.Fem;
using BridGHe.Grasshopper.Helpers;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Parameters;
using Rhino.Geometry;

namespace BridGHe.Grasshopper.Components.Attributes
{
    public class PlateSupportComponent : GH_Component
    {
        /// <summary>
        /// Initializes a new instance of the PlateSupportComponent class.
        /// </summary>
        public PlateSupportComponent()
          : base("Plate Support", "PS", "Define a plate support", CategoryNameConstants.CATEGORY_FEMM, CategoryNameConstants.SUBCATEGORY_ATTRIBUTES)
        {
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("Plate", "P", "The plate where define the support", GH_ParamAccess.item);
            pManager.AddNumberParameter("Stiffness", "S", "The stiffness of the support", GH_ParamAccess.item);
            pManager.AddBooleanParameter("Compression only", "CO", "Compression only support", GH_ParamAccess.item, false);
            pManager.AddIntegerParameter("Face", "F", "Face", GH_ParamAccess.item, 0);
            Param_Integer type_param = (Param_Integer)pManager[3];
            type_param.AddNamedValue("Top", 0);
            type_param.AddNamedValue("Bottom", 1);
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("Plate", "P", "The modified plate", GH_ParamAccess.item);
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object is used to retrieve from inputs and store in outputs.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            GH_Plate plate = null;
            double stiffness = 0;
            bool compressionOnly = false;
            int face = 0;
            if (!DA.GetData("Plate", ref plate))
                return;

            GH_Plate newPlate = new GH_Plate(plate);

            if (!DA.GetData(1, ref stiffness))
                return;
            if (!DA.GetData(2, ref compressionOnly))
                return;
            if (!DA.GetData(3, ref face))
                return;

            PlateSupportAttributeModel.AttributeData attr = new PlateSupportAttributeModel.AttributeData
            {
                Stiffness = stiffness,
                CompressionOnly = compressionOnly
            };
            if (face == 0)
            {
                attr.Face = PlateSupportAttributeModel.SupportedFace.Top;
            }
            else if (face == 1)
            {
                attr.Face = PlateSupportAttributeModel.SupportedFace.Bottom;
            }
            else
            {
                throw new NotSupportedException();
            }
            newPlate.Value.Attributes.Add(new PlateSupportAttributeModel(attr));

            DA.SetData("Plate", newPlate);
        }

        public override GH_Exposure Exposure => GH_Exposure.tertiary;

        /// <summary>
        /// Provides an Icon for the component.
        /// </summary>
        protected override System.Drawing.Bitmap Icon => Properties.ComponentIcons.For(GetType());

        /// <summary>
        /// Gets the unique ID for this component. Do not change this ID after release.
        /// </summary>
        public override Guid ComponentGuid => new Guid("7489fa91-6962-46b7-95b7-cfdeddc411f2");
    }
}