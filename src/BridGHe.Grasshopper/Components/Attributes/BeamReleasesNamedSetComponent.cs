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
    public class BeamReleasesNamedSetComponent : GH_Component
    {
        /// <summary>
        /// Initializes a new instance of the BeamReleasesComponent class.
        /// </summary>
        public BeamReleasesNamedSetComponent()
          : base("Named Beam Releases", "NBR", "Named beam ends releases", CategoryNameConstants.CATEGORY_FEMM, CategoryNameConstants.SUBCATEGORY_ATTRIBUTES)
        {
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddTextParameter("Name", "N", "The name where to define the ends releases", GH_ParamAccess.item);
            int[] idx = new[]
            {
                pManager.AddNumberParameter("Axial N1", "AN1", "Axial load release starting node", GH_ParamAccess.item),
                pManager.AddNumberParameter("Axial N2", "AN2", "Axial load release ending node", GH_ParamAccess.item),
                pManager.AddNumberParameter("Shear 1 N1", "S1N1", "Shear force axis 1 release starting node", GH_ParamAccess.item),
                pManager.AddNumberParameter("Shear 2 N1", "S2N1", "Shear force axis 2 release starting node", GH_ParamAccess.item),
                pManager.AddNumberParameter("Shear 1 N2", "S1N2", "Shear force axis 1 release ending node", GH_ParamAccess.item),
                pManager.AddNumberParameter("Shear 2 N2", "S2N2", "Shear force axis 2 release ending node", GH_ParamAccess.item),
                pManager.AddNumberParameter("Torsion N1", "TN1", "Torsion release starting node", GH_ParamAccess.item),
                pManager.AddNumberParameter("Torsion N2", "TN2", "Torsion release ending node", GH_ParamAccess.item),
                pManager.AddNumberParameter("Moment 1 N1", "M1N1", "Moment axis 1 release starting node", GH_ParamAccess.item),
                pManager.AddNumberParameter("Moment 2 N1", "M2N1", "Moment axis 2 release starting node", GH_ParamAccess.item),
                pManager.AddNumberParameter("Moment 1 N2", "M1N2", "Moment axis 1 release ending node", GH_ParamAccess.item),
                pManager.AddNumberParameter("Moment 2 N2", "M2N2", "Moment axis 2 release ending node", GH_ParamAccess.item)
            };
            foreach (int i in idx)
                pManager[i].Optional = true;
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("Named release", "NBR", "The named release", GH_ParamAccess.item);
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object is used to retrieve from inputs and store in outputs.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            string name = "";
            double axialN1 = 0, axialN2 = 0;
            double shear1N1 = 0, shear2N1 = 0, shear1N2 = 0, shear2N2 = 0;
            double torsionN1 = 0, torsionN2 = 0;
            double moment1N1 = 0, moment2N1 = 0, moment1N2 = 0, moment2N2 = 0;
            if (!DA.GetData("Name", ref name))
                return;
            Dictionary<string, bool> has_values = new Dictionary<string, bool>()
            {
                { "axialN1", DA.GetData(1, ref axialN1) },
                { "axialN2", DA.GetData(2, ref axialN2) },
                { "shear1N1", DA.GetData(3, ref shear1N1) },
                { "shear2N1", DA.GetData(4, ref shear2N1) },
                { "shear1N2", DA.GetData(5, ref shear1N2) },
                { "shear2N2", DA.GetData(6, ref shear2N2) },
                { "torsionN1", DA.GetData(7, ref torsionN1) },
                { "torsionN2", DA.GetData(8, ref torsionN2) },
                { "moment1N1", DA.GetData(9, ref moment1N1) },
                { "moment2N1", DA.GetData(10, ref moment2N1) },
                { "moment1N2", DA.GetData(11, ref moment1N2) },
                { "moment2N2", DA.GetData(12, ref moment2N2) }
            };

            GH_NamedSets new_named = new GH_NamedSets(new BeamReleasesNamedModel(name,
                new[] { has_values["shear1N1"], has_values["shear2N1"], has_values["axialN1"] },
                new[] { has_values["shear1N2"], has_values["shear2N2"], has_values["axialN2"] },
                new[] { has_values["moment1N1"], has_values["moment2N1"], has_values["torsionN1"] },
                new[] { has_values["moment1N2"], has_values["moment2N2"], has_values["torsionN2"] },
                new Vector3d(shear1N1, shear2N1, axialN1),
                new Vector3d(shear1N2, shear2N2, axialN2),
                new Vector3d(moment1N1, moment2N1, torsionN1),
                new Vector3d(moment1N2, moment2N2, torsionN2)));

            DA.SetData("Named release", new_named);
        }

        public override GH_Exposure Exposure => GH_Exposure.secondary;

        /// <summary>
        /// Provides an Icon for the component.
        /// </summary>
        protected override System.Drawing.Bitmap Icon => Properties.ComponentIcons.For(GetType());

        /// <summary>
        /// Gets the unique ID for this component. Do not change this ID after release.
        /// </summary>
        public override Guid ComponentGuid => new Guid("5f7c35bc-b5c4-4ca0-9d0a-d6ad3523cdef");
    }
}