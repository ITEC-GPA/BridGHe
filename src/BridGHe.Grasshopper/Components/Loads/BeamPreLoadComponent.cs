using System;
using System.Collections.Generic;
using BridGHe.Common.Models;
using BridGHe.Grasshopper.DataTypes.Fem;
using BridGHe.Grasshopper.Helpers;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Parameters;
using Rhino.Geometry;

namespace BridGHe.Grasshopper.Components.Loads
{
    public class BeamPreLoadComponent : GH_Component
    {
        /// <summary>
        /// Initializes a new instance of the BeamPreLoadComponent class.
        /// </summary>
        public BeamPreLoadComponent()
          : base("Beam PreLoad", "BPL", "Defines the beam preload", CategoryNameConstants.CATEGORY_MODEL, CategoryNameConstants.SUBCATEGORY_LOADS)
        {
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("Beam", "B", "The beam where add the Force density load", GH_ParamAccess.item);
            pManager.AddGenericParameter("LoadCase", "LC", "The load case", GH_ParamAccess.item);
            pManager.AddIntegerParameter("Type", "T", "The load type", GH_ParamAccess.item, 0);
            Param_Integer param = pManager[2] as Param_Integer;
            param.AddNamedValue("Tension", 0);
            param.AddNamedValue("Strain", 1);
            pManager.AddNumberParameter("Value", "V", "The load value", GH_ParamAccess.item);
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("Beam", "B", "The modified beam", GH_ParamAccess.item);
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object is used to retrieve from inputs and store in outputs.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            GH_Beam beam = null;
            GH_LoadCase lc = null;
            int type = 0;
            double value = 0;
            if (!DA.GetData("Beam", ref beam))
                return;
            if (!DA.GetData("LoadCase", ref lc))
                return;
            if (!DA.GetData("Type", ref type))
                return;
            if (!DA.GetData("Value", ref value))
                return;

            Message = type == 0 ? "Tension" : "Strain";

            if (lc.Value.NonStructuralMassAcceleration)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "The connected LoadCase have the Non-structural mass flag set to on");

            BeamPreLoadModel load = new BeamPreLoadModel(lc.Value, value, type == 0 ? BeamPreLoadModel.LoadType.Tension : BeamPreLoadModel.LoadType.Strain);
            GH_Beam newBeam = new GH_Beam(beam);
            newBeam.Value.Loads.Add(load);
            DA.SetData(0, newBeam);
        }

        public override GH_Exposure Exposure => GH_Exposure.secondary;

        /// <summary>
        /// Provides an Icon for the component.
        /// </summary>
        protected override System.Drawing.Bitmap Icon => Properties.ComponentIcons.For(GetType());

        /// <summary>
        /// Gets the unique ID for this component. Do not change this ID after release.
        /// </summary>
        public override Guid ComponentGuid => new Guid("584fa650-0996-4bf8-9825-aa8639444507");
    }
}