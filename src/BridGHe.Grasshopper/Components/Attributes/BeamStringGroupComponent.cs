using System;
using System.Collections.Generic;
using BridGHe.Common.Models;
using BridGHe.Grasshopper.DataTypes.Fem;
using BridGHe.Grasshopper.Helpers;
using Grasshopper.Kernel;

namespace BridGHe.Grasshopper.Components.Attributes
{
    public class BeamStringGroupComponent : GH_Component
    {
        /// <summary>
        /// Initializes a new instance of the StringGroupComponent class.
        /// </summary>
        public BeamStringGroupComponent() : base("String Group", "SG", "String Group - Available only for Straus7", CategoryNameConstants.CATEGORY_FEMM, CategoryNameConstants.SUBCATEGORY_ATTRIBUTES)
        {
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("Beam", "B", "The beam where to define string group", GH_ParamAccess.list);
            pManager.AddIntegerParameter("String group ID", "SGID", "id string group", GH_ParamAccess.item);
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("Beam", "B", "The modified beam", GH_ParamAccess.list);
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object is used to retrieve from inputs and store in outputs.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            List<GH_Beam> listBeams = new List<GH_Beam>();
            if (!DA.GetDataList(0, listBeams))
                return;

            int id = 0;
            if (!DA.GetData(1, ref id))
                return;

            BeamStringGroupAttributeModel.AttributeData data = new BeamStringGroupAttributeModel.AttributeData
            {
                ID = id
            };

            BeamStringGroupAttributeModel sg = new BeamStringGroupAttributeModel(data);

            List<GH_Beam> newBeams = new List<GH_Beam>();

            for (int i = 0; i < listBeams.Count; i++)
            {
                GH_Beam newBeam = new GH_Beam(listBeams[i]);
                newBeam.Value.Attributes.Add(sg);
                newBeams.Add(newBeam);
            }

            DA.SetDataList(0, newBeams);
        }

        /// <summary>
        /// Provides an Icon for the component.
        /// </summary>
        protected override System.Drawing.Bitmap Icon => Properties.ComponentIcons.For(GetType());

        public override GH_Exposure Exposure => GH_Exposure.secondary;


        /// <summary>
        /// Gets the unique ID for this component. Do not change this ID after release.
        /// </summary>
        public override Guid ComponentGuid
        {
            get { return new Guid("a8cfb01f-5fac-41dc-b757-4d01d6103c15"); }
        }
    }
}