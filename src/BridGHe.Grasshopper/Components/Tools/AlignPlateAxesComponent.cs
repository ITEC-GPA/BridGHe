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
          : base("Align plate axes", "APA", "Align the local plate axes with a direction", CategoryNameConstants.CATEGORY_FEMM, CategoryNameConstants.SUBCATEGORY_TOOLS)
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
            dir.Unitize();

            GH_Plate newPlate = new GH_Plate(plate);

            plate.Value.GetLocalAxes(out Vector3d axes1, out Vector3d axes2, out Vector3d axes3);
            if (ax == "3 Blue")
            {
                //align the plate normal to axes reversing node order
                if (axes3 * dir < 0)
                {
                    //reversing
                    List<Point3d> ps = new List<Point3d>();
                    foreach (var ppp in newPlate.Value.Points)
                        ps.Add(new Point3d(ppp.X, ppp.Y, ppp.Z));

                    ps.Reverse();
                    newPlate.Value.Points = new System.Collections.ObjectModel.ObservableCollection<Point3d>();

                    foreach (Point3d p in ps)
                        newPlate.Value.Points.Add(p);
                }
            }
            else
            {
                Vector3d plateStartingAxes;
                if (ax == "1 Red")
                    plateStartingAxes = axes1;
                else if (ax == "2 Green")
                    plateStartingAxes = axes2;
                else
                    return;

                plateStartingAxes.Unitize();
                if (Math.Abs(axes3 * dir) > 0.99999)
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Unable to riorient the plate");
                else
                {
                    Vector3d orto = Vector3d.CrossProduct(axes3, dir);
                    orto.Unitize();
                    Vector3d newDir = Vector3d.CrossProduct(orto, axes3);
                    newDir.Unitize();

                    if (newDir * dir < 0)
                        newDir.Reverse();

                    orto = Vector3d.CrossProduct(axes3, plateStartingAxes);
                    double x = newDir * plateStartingAxes;
                    double y = newDir * orto;
                    double angleIncrementDeg = Math.Atan2(y, x).ToDegrees();
                    newPlate.Value.AngleDeg += angleIncrementDeg;
                }
            }

            DA.SetData(0, newPlate);
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