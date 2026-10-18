using BridGHe.Common.Models;
using BridGHe.Grasshopper.DataTypes.Fem;
using BridGHe.Grasshopper.Helpers;
using Grasshopper.Kernel;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BridGHe.Grasshopper.Components.Attributes
{
    public class BeamOutputStationsComponent : GH_Component
    {
        /// <summary>
        /// Initializes a new instance of the BeamReleasesComponent class.
        /// </summary>
        public BeamOutputStationsComponent()
          : base("Beam Output Stations", "BOS", "Beam Output Stations", CategoryNameConstants.CATEGORY_MODEL, CategoryNameConstants.SUBCATEGORY_ATTRIBUTES)
        {
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("Beam", "B", "The beam where to define the output stations", GH_ParamAccess.item);
            pManager.AddIntegerParameter("OutputStations", "O", "The minimum number of stations", GH_ParamAccess.item);
            int[] idx = new[]
            {
                pManager.AddBooleanParameter("At Intersections with other elements", "I", "If this item is True, no additional output stations are added at the ends " +
                "of line elements when the frame object is internally meshed. (Only for SAP)", GH_ParamAccess.item, true),
                pManager.AddBooleanParameter("At concentrated load locations", "PL", "If this item is True, no additional output stations are added at point load " +
                "locations (only for SAP)", GH_ParamAccess.item, true),
            };
            foreach (int i in idx)
                pManager[i].Optional = true;
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
            int stations = 0;
            bool atIntersections = true;
            bool atPointLoad = true;

            if (!DA.GetData(0, ref beam))
                return;
            if (!DA.GetData(1, ref stations))
                return;
            DA.GetData(2, ref atIntersections);
            DA.GetData(3, ref atPointLoad);

            GH_Beam newBeam = new GH_Beam(beam);

            BeamOutputStationsModel.AttributeData attributeData = new BeamOutputStationsModel.AttributeData()
            {
                MinimumNumberOfStations = stations,
                NoOutPutAndDesignAtElementEnds = atIntersections,
                NoOutPutAndDesignAtPointLoads = atPointLoad,
            };
            BeamOutputStationsModel attr = new BeamOutputStationsModel(attributeData);
            newBeam.Value.Attributes.Add(attr);

            DA.SetData("Beam", newBeam);
        }

        public override GH_Exposure Exposure => GH_Exposure.secondary;

        /// <summary>
        /// Provides an Icon for the component.
        /// </summary>
        protected override System.Drawing.Bitmap Icon => Properties.ComponentIcons.For(GetType());

        /// <summary>
        /// Gets the unique ID for this component. Do not change this ID after release.
        /// </summary>
        public override Guid ComponentGuid => new Guid("72860a35-4af0-41c0-9e26-79a3db608d5f");
    }
}