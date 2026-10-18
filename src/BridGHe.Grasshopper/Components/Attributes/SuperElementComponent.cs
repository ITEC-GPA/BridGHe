using System;
using System.Collections.Generic;
using BridGHe.Common.Models;
using BridGHe.Grasshopper.DataTypes.Fem;
using BridGHe.Grasshopper.Helpers;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;

namespace BridGHe.Grasshopper.Components.Attributes
{
    public class SuperElementComponent : GH_Component
    {
        /// <summary>
        /// Initializes a new instance of the SuperElementComponent class.
        /// </summary>
        public SuperElementComponent()
          : base("Super Element", "SE", "Defines that the element belong to a Super-Element", CategoryNameConstants.CATEGORY_MODEL, CategoryNameConstants.SUBCATEGORY_ATTRIBUTES)
        {
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("Element", "E", "The element to be made belonging to the Super-Element", GH_ParamAccess.item);
            pManager.AddTextParameter("Name", "N", "The name of the Super-Element", GH_ParamAccess.item);
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
            string name = "";
            if (!DA.GetData(0, ref element))
                return;
            if (!DA.GetData(1, ref name))
                return;

            SuperElementAttributeModel se = new SuperElementAttributeModel(name);
            if (element is GH_Beam beam)
            {
                GH_Beam new_beam = new GH_Beam(beam);
                new_beam.Value.Attributes.Add(se);
                DA.SetData(0, new_beam);
            }
            else if (element is GH_Plate plate)
            {
                GH_Plate new_plate = new GH_Plate(plate);
                new_plate.Value.Attributes.Add(se);
                DA.SetData(0, new_plate);
            }
            else
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Unsupported element type");
            }
        }

        public override GH_Exposure Exposure => GH_Exposure.quarternary;

        /// <summary>
        /// Provides an Icon for the component.
        /// </summary>
        protected override System.Drawing.Bitmap Icon => Properties.ComponentIcons.For(GetType());

        /// <summary>
        /// Gets the unique ID for this component. Do not change this ID after release.
        /// </summary>
        public override Guid ComponentGuid => new Guid("2d01d2b5-9b10-402e-aee3-672cfc4a659b");
    }
}