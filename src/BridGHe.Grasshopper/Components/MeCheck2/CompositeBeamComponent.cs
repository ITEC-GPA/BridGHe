using BridGHe.Common.Models;
using BridGHe.Grasshopper.DataTypes;
using BridGHe.Grasshopper.DataTypes.Fem;
using BridGHe.Grasshopper.Helpers;
using Grasshopper.Kernel;
using System;
using System.Collections.Generic;

namespace BridGHe.Grasshopper.Components.MeCheck2
{
    public class CompositeBeamComponent : GH_Component
    {
        /// <summary>
        /// Initializes a new instance of the CompositeBeamComponent class.
        /// </summary>
        public CompositeBeamComponent()
          : base("Composite beam", "CB", "Assign the composite sections to the beam", CategoryNameConstants.CATEGORY_CHECKS, CategoryNameConstants.SUBCATEGORY_MECHECK2)
        {
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("Beam", "B", "The beam to define the mixed section", GH_ParamAccess.item);
            pManager.AddGenericParameter("Section Start", "SS", "Section on the starting node", GH_ParamAccess.item);
            pManager.AddGenericParameter("Section End", "SE", "Section on the starting node", GH_ParamAccess.item);
            pManager.AddGenericParameter("Groups", "G", "The groups to assign to the beam", GH_ParamAccess.list);
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
            if (!DA.GetData(2, ref endSection))
                return;
            DA.GetDataList(3, groups);

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
            for (int i = 0; i < bb.Results.Count; i++)
                compositeBeamModel.Results.Add(bb.Results[i]);

            GH_CompositeBeam ms_beam = new GH_CompositeBeam(compositeBeamModel);

            DA.SetData(0, ms_beam);
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