using System;
using System.Collections.Generic;
using System.Linq;
using BridGHe.Common.Models;
using BridGHe.Grasshopper.DataTypes.Fem;
using BridGHe.Grasshopper.Helpers;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;

namespace BridGHe.Grasshopper.Components.Tools
{
    public class ElementFilterComponent : GH_Component
    {
        /// <summary>
        /// Initializes a new instance of the ElementFilterComponent class.
        /// </summary>
        public ElementFilterComponent()
          : base("Elements Filter", "EF", "Description", CategoryNameConstants.CATEGORY_MODEL, CategoryNameConstants.SUBCATEGORY_TOOLS)
        {
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("Elements", "E", "The elements to filter", GH_ParamAccess.list);
            pManager.AddGenericParameter("Find", "F", "The elements to find", GH_ParamAccess.list);
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("Found", "E", "The elements found", GH_ParamAccess.list);
            pManager.AddGenericParameter("Others", "O", "The other elements", GH_ParamAccess.list);
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object is used to retrieve from inputs and store in outputs.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            List<IGH_GeometricGoo> elements = new List<IGH_GeometricGoo>();
            if (!DA.GetDataList(0, elements))
                return;

            List<IGH_GeometricGoo> found = new List<IGH_GeometricGoo>();
            List<IGH_GeometricGoo> others = new List<IGH_GeometricGoo>(elements);
            List<IGH_GeometricGoo> find = new List<IGH_GeometricGoo>();

            if (DA.GetDataList(1, find))
            {
                for (int i = 0; i < find.Count; i++)
                {
                    IGH_GeometricGoo item = find[i];
                    if (item is GH_Node node)
                    {
                        Guid guid = node.Guid;
                        GH_Node[] nodes = elements.Where(e => e.GetType() == typeof(GH_Node)).
                            Cast<GH_Node>().Where(n => n.Guid == guid).ToArray();
                        for (int j = 0; j < nodes.Length; j++)
                        {
                            others.Remove(nodes[j]);
                        }

                        found.AddRange(nodes);
                        continue;
                    }
                    else if (item is GH_Beam beam)
                    {
                        Guid guid = beam.Guid;
                        GH_Beam[] beams = elements.Where(e => e.GetType() == typeof(GH_Beam)).
                            Cast<GH_Beam>().Where(b => b.Guid == guid).ToArray();
                        for (int j = 0; j < beams.Length; j++)
                        {
                            others.Remove(beams[j]);
                        }

                        found.AddRange(beams);
                        continue;
                    }
                    else if (item is GH_Plate plate)
                    {
                        Guid guid = plate.Guid;
                        GH_Plate[] plates = elements.Where(e => e.GetType() == typeof(GH_Plate)).
                            Cast<GH_Plate>().Where(p => p.Guid == guid).ToArray();
                        for (int j = 0; j < plates.Length; j++)
                        {
                            others.Remove(plates[j]);
                        }

                        found.AddRange(plates);
                        continue;
                    }
                    others.Add(item);
                }
            }

            DA.SetDataList(0, found);
            DA.SetDataList(1, others);
        }

        public override GH_Exposure Exposure => GH_Exposure.secondary;

        /// <summary>
        /// Provides an Icon for the component.
        /// </summary>
        protected override System.Drawing.Bitmap Icon => Properties.ComponentIcons.For(GetType());

        /// <summary>
        /// Gets the unique ID for this component. Do not change this ID after release.
        /// </summary>
        public override Guid ComponentGuid => new Guid("c52e4345-36fa-4944-9ee5-99ed74034221");
    }
}