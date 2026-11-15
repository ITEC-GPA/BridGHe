using BridGHe.Grasshopper.DataTypes.Fem;
using BridGHe.Grasshopper.Helpers;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Parameters;
using GPC.Utilities.Extensions;
using Rhino.Geometry;
using System;
using System.Collections.Generic;

namespace BridGHe.Grasshopper.Components
{
    public class AlignPlateAxesComponent : GH_Component
    {
        private readonly List<string> _axes = new List<string>()
        {
            "1 Red",
            "2 Green",
            "3 Blue"
        };

        /// <summary>
        /// Initializes a new instance of the BeamPointLoadComponent class.
        /// </summary>
        public AlignPlateAxesComponent()
          : base("Align plate axes", "APA", "Align the local plate axes with a direction", CategoryNameConstants.CATEGORY_MODEL, CategoryNameConstants.SUBCATEGORY_TOOLS)
        {
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("Plate", "P", "The plate to align", GH_ParamAccess.item);
            pManager.AddIntegerParameter("Axis", "A", "Axis to align", GH_ParamAccess.item);
            Param_Integer param = (Param_Integer)pManager[pManager.ParamCount - 1];
            for (int i = 0; i < _axes.Count; i++)
                param.AddNamedValue(_axes[i], i);
            pManager.AddVectorParameter("Direction", "D", "The direction used to align axes", GH_ParamAccess.item);
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("Plate", "P", "The plate with aligned axes", GH_ParamAccess.item);
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object is used to retrieve from inputs and store in outputs.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            GH_Plate plate = null;
            int axis = -1;
            Vector3d dir = Vector3d.Unset;

            if (!DA.GetData("Plate", ref plate))
                return;
            if (!DA.GetData("Axis", ref axis))
                return;
            if (!DA.GetData("Direction", ref dir))
                return;

            string ax = _axes[axis];
            Vector3d given = dir;
            dir.Unitize();

            Func<Guid, Common.Gpc.Legacy.LegacyPlateDraft> build = id => Common.Gpc.Builders.GpcPlateBuilders.AlignAxes(plate.Gpc, axis, given.X, given.Y, given.Z, id);
            // R6.5 P1 lane 1a: the decisions on the corners and the angle of the draft (the warning of a direction along the normal included),
            // identity derived from the input draft; P2X: the turned local axis angle is mapped on the layer axes of the draft, as the reader
            // maps it. P2K (P1): the GPC path only.
            if (ax != "3 Blue" && plate.Gpc != null && Common.Gpc.Builders.GpcPlateBuilders.AlignAxesAlongNormal(plate.Gpc, given.X, given.Y, given.Z))
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Unable to riorient the plate");
            GpcOnlyElements.Set(DA, 0, GpcOnlyElements.Plate(this, 0, plate, build, axis, given));
        }

        /// <summary>
        /// Provides an Icon for the component.
        /// </summary>
        protected override System.Drawing.Bitmap Icon => Properties.ComponentIcons.For(GetType());

        /// <summary>
        /// Gets the unique ID for this component. Do not change this ID after release.
        /// </summary>
        public override Guid ComponentGuid => new Guid("376c9d2a-b211-458a-9592-3669a14192ed");
    }
}