using System;
using System.Collections.Generic;
using System.Linq;
using BridGHe.Grasshopper.DataTypes.Fem;
using BridGHe.Grasshopper.Helpers;
using Grasshopper.Kernel;
using Rhino.Geometry;

namespace BridGHe.Grasshopper.Components.Models
{
    public class PlateComponent : GH_Component
    {
        /// <summary>
        /// Initializes a new instance of the PlateComponent class.
        /// </summary>
        public PlateComponent()
          : base("Plate", "PL", "Define a Beam", CategoryNameConstants.CATEGORY_MODEL, CategoryNameConstants.SUBCATEGORY_MODEL)
        {
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddMeshParameter("Mesh", "M", "The generating mesh face", GH_ParamAccess.item);
            pManager.AddGenericParameter("Property", "PP", "The property to assign to the generated plate", GH_ParamAccess.item);
            pManager.AddGenericParameter("Groups", "G", "The group to assign to the plate", GH_ParamAccess.list);
            pManager[2].Optional = true;
            pManager.AddNumberParameter("Angle", "A", "The local axis angle [deg]", GH_ParamAccess.item, 0);
            pManager.AddNumberParameter("Offset", "O", "The plate offset", GH_ParamAccess.item, 0);
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("Plate", "P", "The generated plate", GH_ParamAccess.item);
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object is used to retrieve from inputs and store in outputs.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            Mesh face = new Mesh();
            GH_PlateProperty pp = null;
            List<GH_Group> groups = new List<GH_Group>();
            double angle = 0;
            double offset = 0;

            if (!DA.GetData("Mesh", ref face))
                return;
            if (!DA.GetData("Property", ref pp))
                return;
            DA.GetDataList("Groups", groups);
            DA.GetData("Angle", ref angle);
            DA.GetData("Offset", ref offset);

            if (face.Vertices.Count < 3)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Mesh with less than 3 vertices");
            // PAR (A07, user decision of 8/11, «Sì, come proponi»): a plate takes one face of 3 or 4 vertices, on both paths (FEM and GPC): an error
            // and no output (0.3.0 warned and made a plate of the first naked edge, of 6, 8 or 9 nodes: no longer, the Converter writes plates of
            // 3 or 4 nodes).
            if (face.Vertices.Count > 4)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, Common.Gpc.Builders.GpcPlateBuilders.MoreThanFourNodes);
                return;
            }

            Polyline edges = face.GetNakedEdges().FirstOrDefault();

            Point3d[] vertexes = new Point3d[edges.Count - 1];
            for (int i = 0; i < edges.Count - 1; i++)
                vertexes[i] = edges[i];

            // R6.5 P1 lane 2a: a property or a group given by its GPC datum alone gives the plate by its draft alone (rule of passo 75: no FEM
            // value from a null value), with the identity by content of the generators (decision 1 of passo 73: the one rule shared with P2;
            // the parameters of the builder: corners, property, groups, angle, offset). A refused plate or property gives an error and no plate.
            // P2K (P1): the only path, for every input (GroupChain reads the GPC datum of a group; no FEM value).
            var draft = GpcOnlyElements.Generated(this, () =>
            {
                var chains = groups.Select(g => GpcBuilderAttach.GroupChain(g)).ToArray();
                return Common.Gpc.Builders.GpcPlateBuilders.Create(vertexes.Select(v => (v.X, v.Y, v.Z)).ToArray(), pp.Gpc, chains, angle, offset, "",
                    GpcOnlyElements.ContentIdentity(ComponentGuid, 0, vertexes, pp.Gpc, chains, angle, offset));
            });
            if (draft != null) DA.SetData("Plate", GH_Plate.FromGpc(draft));
        }

        public override GH_Exposure Exposure => GH_Exposure.secondary;

        /// <summary>
        /// Provides an Icon for the component.
        /// </summary>
        protected override System.Drawing.Bitmap Icon => Properties.ComponentIcons.For(GetType());

        /// <summary>
        /// Gets the unique ID for this component. Do not change this ID after release.
        /// </summary>
        public override Guid ComponentGuid => new Guid("886746b3-f39f-4efb-95d8-a7101caa7bf2");
    }
}