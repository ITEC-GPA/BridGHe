using System;
using System.Collections.Generic;
using System.Linq;
using BridGHe.Common.Models;
using BridGHe.Grasshopper.DataTypes.Fem;
using BridGHe.Grasshopper.Helpers;
using Grasshopper.Kernel;
using Rhino.Geometry;

namespace BridGHe.Grasshopper.Components
{
    public class BeamOverrideComponent : GH_Component
    {
        /// <summary>
        /// Initializes a new instance of the BeamOverrideComponent class.
        /// </summary>
        public BeamOverrideComponent()
          : base("Beam Override", "BO", "Override one of the creation parameters", CategoryNameConstants.CATEGORY_MODEL, CategoryNameConstants.SUBCATEGORY_TOOLS)
        {
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("Beam", "B", "The beam where add the distributed load", GH_ParamAccess.item);
            pManager.AddGenericParameter("Property", "BP", "The property to assign to the generated beam", GH_ParamAccess.item);
            pManager[1].Optional = true;
            pManager.AddGenericParameter("Groups", "G", "The groups to assign to the beam", GH_ParamAccess.list);
            pManager[2].Optional = true;
            pManager.AddNumberParameter("Angle", "A", "The rotation angle along the principal axis [deg]", GH_ParamAccess.item);
            pManager[3].Optional = true;
            pManager.AddVectorParameter("Offset", "O", "The translation offset along the principal axis", GH_ParamAccess.item);
            pManager[4].Optional = true;
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
            GH_BeamProperty bp = null;
            List<GH_Group> groups = new List<GH_Group>();
            double a = 0;
            Vector3d offset = Vector3d.Unset;
            if (!DA.GetData("Beam", ref beam))
                return;
            bool has_property = DA.GetData("Property", ref bp);
            bool has_groups = DA.GetDataList("Groups", groups);
            bool has_angle = DA.GetData("Angle", ref a);
            bool has_offset = DA.GetData("Offset", ref offset);

            if (!has_property && !has_groups && !has_angle && !has_offset)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Almost one of parameter must to collect data");
                return;
            }

            GH_Beam new_beam = new GH_Beam(beam);
            //new_beam.Value.Loads.Add(load);
            if (has_property && bp.IsValid)
            {
                new_beam.Value.BeamProperty = bp.Value;
            }
            if (has_groups)
            {
                new_beam.Value.Groups.Clear();
                foreach (GroupModel group in groups.Select(g => g.Value))
                    new_beam.Value.Groups.Add(group);
            }
            if (has_angle)
            {
                new_beam.Value.AngleDeg = a;
            }
            if (has_offset)
            {
                new_beam.Value.Offset = new Vector2d(offset.X, offset.Y);
            }
            DA.SetData(0, new_beam);
        }

        /// <summary>
        /// Provides an Icon for the component.
        /// </summary>
        protected override System.Drawing.Bitmap Icon => Properties.ComponentIcons.For(GetType());

        /// <summary>
        /// Gets the unique ID for this component. Do not change this ID after release.
        /// </summary>
        public override Guid ComponentGuid => new Guid("6962f28d-7b4a-4e37-bf9b-ea7f1f197e04");
    }
}