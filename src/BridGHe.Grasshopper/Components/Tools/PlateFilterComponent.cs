using BridGHe.Common.Helpers;
using BridGHe.Common.Models;
using BridGHe.Grasshopper.DataTypes.Fem;
using BridGHe.Grasshopper.Helpers;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Parameters;
using Rhino.Geometry;
using System;
using System.Collections.Generic;
using System.Linq;

namespace BridGHe.Grasshopper.Components.Tools
{
    public class PlateFilterComponent : GH_Component
    {
        /// <summary>
        /// Initializes a new instance of the ElementFilterComponent class.
        /// </summary>
        public PlateFilterComponent()
          : base("Plate Filter", "PF", "Plate filter", CategoryNameConstants.CATEGORY_MODEL, CategoryNameConstants.SUBCATEGORY_TOOLS)
        {
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("Plates", "Ps", "The plates to filter", GH_ParamAccess.list);

            pManager.AddTextParameter("IDs", "ID", "The element IDs to search", GH_ParamAccess.list);
            pManager[pManager.ParamCount - 1].Optional = true;

            pManager.AddPointParameter("Positions", "Ps", "The positions to search", GH_ParamAccess.list);
            pManager[pManager.ParamCount - 1].Optional = true;

            pManager.AddIntegerParameter("Units", "U", "The measure units", GH_ParamAccess.item);
            Param_Integer type_param = (Param_Integer)pManager[pManager.ParamCount - 1];
            Array names = Enum.GetValues(typeof(ModelModel.ModelUnits));
            foreach (ModelModel.ModelUnits name in names)
                type_param.AddNamedValue(name.GetDescription(), (int)name);

            pManager.AddTextParameter("Properties name", "PNs", "The plate property names to search", GH_ParamAccess.list);
            pManager[pManager.ParamCount - 1].Optional = true;

            pManager.AddGenericParameter("Groups", "G", "The node groups to search", GH_ParamAccess.list);
            pManager[pManager.ParamCount - 1].Optional = true;
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("Plates", "Ps", "The filtered plates", GH_ParamAccess.list);
            pManager.AddGenericParameter("Others", "Os", "The other plates", GH_ParamAccess.list);
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object is used to retrieve from inputs and store in outputs.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            List<GH_Plate> plates = new List<GH_Plate>();
            List<string> ids = new List<string>();
            List<Point3d> positions = new List<Point3d>();
            int units = -1;
            List<string> props = new List<string>();
            List<GH_Group> groups = new List<GH_Group>();

            if (!DA.GetDataList(0, plates))
                return;
            DA.GetDataList(1, ids);
            DA.GetDataList(2, positions);
            if (!DA.GetData(3, ref units))
                return;
            DA.GetDataList(4, props);
            DA.GetDataList(5, groups);

            if (ids.Count == 0 && positions.Count == 0 && props.Count == 0 && groups.Count == 0)
            {
                DA.AbortComponentSolution();
                return;
            }

            List<GH_Plate> filtered = new List<GH_Plate>();
            List<GH_Plate> others = new List<GH_Plate>();
            double tol = Helpers.ModelHelper.GetTolerance();

            // R6.2 lane A: the filter decides on the GPC drafts (first Element ID, groups by full name, property name, corners in the definition
            // units); the FEM value is the bridge for a refused draft until R6.5, and under the Smoke host the two decisions must agree.
            var groupNames = groups.Select(g => string.Join("\\", GpcBuilderAttach.GroupChain(g))).ToList();
            for (int i = 0; i < plates.Count; i++)
            {
                GH_Plate p = plates[i];
                bool add = GpcBuilderAttach.Decide(() => Matches(p.Gpc, ids, groupNames, props, positions, tol), p.Value == null || GpcOnlyProperties.WithoutFem(groups) ? null : () => FemMatches(p, ids, groups, props, positions, tol), "Plate Filter"); // R6.5 P1 lane 1a: a plate without FEM value on its draft alone
                if (add)
                    filtered.Add(p);
                else
                    others.Add(p);
            }

            DA.SetDataList(0, filtered);
            DA.SetDataList(1, others);
        }

        /// <summary>R6.2 lane A: the selection on the GPC draft of a plate: its first Element ID is one of the IDs, or one of its groups (all, at
        /// list level) has one of the full names, or its property has one of the names, or one of its corners is within the tolerance of one of
        /// the positions (the distance of Rhino, as before).</summary>
        private static bool Matches(Common.Gpc.Legacy.LegacyPlateDraft d, List<string> ids, List<string> groupNames, List<string> props, List<Point3d> positions, double tol)
        {
            string label = d.Records.FirstLabel;
            if (label != null && ids.Any(id => label.Equals(id))) return true;
            if (groupNames.Any(name => d.Records.Groups.Contains(name, StringComparer.Ordinal))) return true;
            if (props.Any(name => name == d.Plate.PlateProperty?.Name)) return true;
            return positions.Any(point => d.Corners.Any(c => new Point3d(c.X, c.Y, c.Z).DistanceTo(point) < tol));
        }

        /// <summary>The selection on the FEM value (until R6.5).</summary>
        private static bool FemMatches(GH_Plate p, List<string> ids, List<GH_Group> groups, List<string> props, List<Point3d> positions, double tol)
        {
            {
                bool add = false;
                for (int j = 0; j < ids.Count; j++)
                {
                    string id = ids[j];
                    if (p.Value.HasId(id))
                    {
                        add = true;
                        break;
                    }
                }
                if (!add)
                {
                    for (int j = 0; j < groups.Count; j++)
                    {
                        GH_Group g = groups[j];
                        if (p.Value.HasGroup(g.Value))
                        {
                            add = true;
                            break;
                        }
                    }
                    if (!add)
                    {
                        for (int j = 0; j < props.Count; j++)
                        {
                            string bp = props[j];
                            if (bp == p.Value.PlateProperty.Name)
                            {
                                add = true;
                                break;
                            }
                        }
                        if (!add)
                        {
                            for (int j = 0; j < positions.Count; j++)
                            {
                                Point3d point = positions[j];
                                for (int k = 0; k < p.Value.Points.Count; k++)
                                {
                                    Point3d plateP = p.Value.Points[k];
                                    if (plateP.DistanceTo(point) < tol)
                                    {
                                        add = true;
                                        break;
                                    }
                                }
                                if (add) break;
                            }
                        }
                    }
                }
                return add;
            }
        }

        public override GH_Exposure Exposure => GH_Exposure.secondary;

        /// <summary>
        /// Provides an Icon for the component.
        /// </summary>
        protected override System.Drawing.Bitmap Icon => Properties.ComponentIcons.For(GetType());

        /// <summary>
        /// Gets the unique ID for this component. Do not change this ID after release.
        /// </summary>
        public override Guid ComponentGuid => new Guid("65613b73-39f2-4a8f-8212-96c44a775516");
    }
}