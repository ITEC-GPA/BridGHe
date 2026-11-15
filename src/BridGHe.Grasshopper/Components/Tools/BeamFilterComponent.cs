using BridGHe.Common.Helpers;
using BridGHe.Grasshopper.DataTypes.Fem;
using BridGHe.Grasshopper.Helpers;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Parameters;
using Rhino.Geometry;
using System;
using System.Collections.Generic;
using System.Linq;

namespace BridGHe.Grasshopper.Components
{
    public class BeamFilterComponent : GH_Component
    {
        /// <summary>
        /// Initializes a new instance of the ElementFilterComponent class.
        /// </summary>
        public BeamFilterComponent()
          : base("Beam Filter", "BF", "Beam filter", CategoryNameConstants.CATEGORY_MODEL, CategoryNameConstants.SUBCATEGORY_TOOLS)
        {
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("Beams", "Bs", "The beams to filter", GH_ParamAccess.list);
            pManager.AddTextParameter("IDs", "ID", "The element IDs to search", GH_ParamAccess.list);
            pManager[pManager.ParamCount - 1].Optional = true;
            pManager.AddPointParameter("Ini Pos", "IPs", "The initial positions to search", GH_ParamAccess.list);
            pManager[pManager.ParamCount - 1].Optional = true;
            pManager.AddPointParameter("End Pos", "EPs", "The end positions to search", GH_ParamAccess.list);
            pManager[pManager.ParamCount - 1].Optional = true;
            pManager.AddIntegerParameter("Units", "U", "The measure units", GH_ParamAccess.item, 2);
            Param_Integer type_param = (Param_Integer)pManager[pManager.ParamCount - 1];
            Array names = Enum.GetValues(typeof(ModelModel.ModelUnits));
            foreach (ModelModel.ModelUnits name in names)
                type_param.AddNamedValue(name.GetDescription(), (int)name);
            pManager.AddTextParameter("Properties name", "PNs", "The beam property names to search", GH_ParamAccess.list);
            pManager[pManager.ParamCount - 1].Optional = true;
            pManager.AddGenericParameter("Groups", "G", "The node groups to search", GH_ParamAccess.list);
            pManager[pManager.ParamCount - 1].Optional = true;
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("Beams", "Bs", "The filtered beams", GH_ParamAccess.list);
            pManager.AddGenericParameter("Others", "Os", "The other beams", GH_ParamAccess.list);
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object is used to retrieve from inputs and store in outputs.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            List<GH_Beam> beams = new List<GH_Beam>();
            List<string> ids = new List<string>();
            List<Point3d> iniPositions = new List<Point3d>();
            List<Point3d> endPositions = new List<Point3d>();
            int units = -1;
            List<string> props = new List<string>();
            List<GH_Group> groups = new List<GH_Group>();

            if (!DA.GetDataList(0, beams))
                return;
            DA.GetDataList(1, ids);
            DA.GetDataList(2, iniPositions);
            DA.GetDataList(3, endPositions);
            if (!DA.GetData(4, ref units))
                return;
            DA.GetDataList(5, props);
            DA.GetDataList(6, groups);

            if (ids.Count == 0 && iniPositions.Count == 0 && endPositions.Count == 0 && props.Count == 0 && groups.Count == 0)
            {
                DA.AbortComponentSolution();
                return;
            }

            // P2K (P1): the tolerance of the former ModelHelper.GetTolerance (the same setting).
            double tol = Settings.Instance.Tolerance;

            // R6.5 P1 lane 1a: the partition of the GPC drafts (GpcBeamFilter); a refused draft gives an error and no output (no FEM partition
            // to fall back on). P2K (P1): the GPC path only. R65-C2-04: the input objects pass through unchanged.
            var only = GpcBeamFilter.Select(beams, ids, iniPositions, endPositions, props, groups, tol);
            if (only == null) { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "The GPC datum of a beam is refused: the beams cannot be filtered."); return; }
            DA.SetDataList(0, beams.Where((b, i) => only[i]).ToList());
            DA.SetDataList(1, beams.Where((b, i) => !only[i]).ToList());
        }

        public override GH_Exposure Exposure => GH_Exposure.secondary;

        /// <summary>
        /// Provides an Icon for the component.
        /// </summary>
        protected override System.Drawing.Bitmap Icon => Properties.ComponentIcons.For(GetType());

        /// <summary>
        /// Gets the unique ID for this component. Do not change this ID after release.
        /// </summary>
        public override Guid ComponentGuid => new Guid("7bdf1a95-62ad-45a7-8768-e1fd7fdc69a8");

        // Private menu choices preserve the published values, order and descriptions.
        private static class ModelModel
        {
            internal enum ModelUnits
            {
                [System.ComponentModel.Description("kNm (m, kN, T, kPa, s, kJ, C)")] Knm,
                [System.ComponentModel.Description("SI (m, N, kg, Pa, s, J, C)")] SI,
                [System.ComponentModel.Description("Nmm (mm, N, T, MPa, s, mJ, C)")] Nmm,
                [System.ComponentModel.Description("PSI (in, lbf, lb, psi, s, btu, F)")] IPS
            }
        }
    }
}