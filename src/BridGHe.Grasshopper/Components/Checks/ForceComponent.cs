using BridGHe.Common.Models;
using BridGHe.Grasshopper.Helpers;
using Grasshopper.Kernel;
using Rhino.Geometry;
using System;

namespace BridGHe.Grasshopper.Components.Checks
{
    public class ForceComponent : GH_Component
    {
        /// <summary>
        /// Initializes a new instance of the ForceComponent class.
        /// </summary>
        public ForceComponent()
          : base("Force component", "FC", "Create a force object", CategoryNameConstants.CATEGORY_CHECKS, CategoryNameConstants.SUBCATEGORY_STEELCHECKS)
        {
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddTextParameter("Name", "N", "The force unique name", GH_ParamAccess.item, "");
            pManager.AddNumberParameter("Axial Force", "N", "The beam element results", GH_ParamAccess.item, 0);
            pManager.AddNumberParameter("Shear 2", "V2", "The beam element results", GH_ParamAccess.item, 0);
            pManager.AddNumberParameter("Shear 3", "V3", "The beam element results", GH_ParamAccess.item, 0);
            pManager.AddNumberParameter("Torsion", "T", "The beam element results", GH_ParamAccess.item, 0);
            pManager.AddNumberParameter("Bending 2", "M2", "The beam element results", GH_ParamAccess.item, 0);
            pManager.AddNumberParameter("Bending 3", "M3", "The beam element results", GH_ParamAccess.item, 0);
            pManager.AddNumberParameter("Station", "S", "The beam element result station", GH_ParamAccess.item, 0);
            pManager.AddBooleanParameter("NormalizedLength", "N", "Normalized length", GH_ParamAccess.item, true);
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("Force", "F", "The force", GH_ParamAccess.item);
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object is used to retrieve from inputs and store in outputs.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            string name = "";
            double N = 0;
            double V2 = 0;
            double V3 = 0;
            double T = 0;
            double M2 = 0;
            double M3 = 0;
            double station = 0;
            bool norm = false;

            int n = 0;
            if (!DA.GetData(n++, ref name))
                return;
            if (!DA.GetData(n++, ref N))
                return;
            if (!DA.GetData(n++, ref V2))
                return;
            if (!DA.GetData(n++, ref V3))
                return;
            if (!DA.GetData(n++, ref T))
                return;
            if (!DA.GetData(n++, ref M2))
                return;
            if (!DA.GetData(n++, ref M3))
                return;
            DA.GetData(n++, ref station);
            DA.GetData(n++, ref norm);

            BeamForceResultModel beamForceResultModel = new BeamForceResultModel(new LoadCaseModel(name, name), station, 0, "")
            {
                AxialForce = N,
                ShearForce = new Vector2d(V3, V2),
                Torque = T,
                BendingMoment = new Vector2d(M3, M2),
                NormalizedLength = norm,
            };

            DA.SetData(0, beamForceResultModel);
        }

        /// <summary>
        /// Provides an Icon for the component.
        /// </summary>
        protected override System.Drawing.Bitmap Icon => Properties.ComponentIcons.For(GetType());

        /// <summary>
        /// Gets the unique ID for this component. Do not change this ID after release.
        /// </summary>
        public override Guid ComponentGuid => new Guid("bc2cde43-3af2-4685-8ce7-63e3586edeb5");
    }
}
