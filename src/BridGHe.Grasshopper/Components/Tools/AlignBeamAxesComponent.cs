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
    public class AlignBeamAxesComponent : GH_Component
    {
        private List<string> _axes = new List<string>()
        {
            "1 Red",
            "2 Green",
            "3 Blue",
            "Local x",
            "Local y"
        };

        /// <summary>
        /// Initializes a new instance of the BeamPointLoadComponent class.
        /// </summary>
        public AlignBeamAxesComponent()
          : base("Align beam axes", "ABA", "Align the beam axes (local or principal) with a direction", CategoryNameConstants.CATEGORY_MODEL, CategoryNameConstants.SUBCATEGORY_TOOLS)
        {
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("Beam", "B", "The beam to align", GH_ParamAccess.item);
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
            pManager.AddGenericParameter("Beam", "B", "The beam with aligned axes", GH_ParamAccess.item);
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object is used to retrieve from inputs and store in outputs.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            GH_Beam beam = null;
            int axis = -1;
            Vector3d dir = Vector3d.Unset;

            if (!DA.GetData("Beam", ref beam))
                return;
            if (!DA.GetData("Axis", ref axis))
                return;
            if (!DA.GetData("Direction", ref dir))
                return;

            string ax = _axes[axis];
            dir.Unitize();

            GH_Beam newBeam = new GH_Beam(beam);

            if (ax == "3 Blue")
            {
                //align the beam direction to axes reversing start node and end node
                Vector3d beamDir = beam.Value.PointTo - beam.Value.PointFrom;
                beamDir.Unitize();

                Point3d pointTo = new Point3d(beam.Value.PointTo);
                Point3d pointFrom = new Point3d(beam.Value.PointFrom);

                if (beamDir * dir < 0)
                {
                    //reversing 
                    newBeam.Value.PointFrom = pointTo;
                    newBeam.Value.PointTo = pointFrom;
                }
            }
            else
            {
                Vector3d beamDir;
                Vector3d beamStartingAxes;
                if (ax == "1 Red")
                    beam.Value.GetLocalAxes(true, out beamStartingAxes, out _, out beamDir, out _);
                else if (ax == "2 Green")
                    beam.Value.GetLocalAxes(true, out _, out beamStartingAxes, out beamDir, out _);
                else if (ax == "Local x")
                    beam.Value.GetLocalAxes(false, out beamStartingAxes, out _, out beamDir, out _);
                else if (ax == "Local y")
                    beam.Value.GetLocalAxes(false, out _, out beamStartingAxes, out beamDir, out _);
                else
                    throw new NotSupportedException();

                beamStartingAxes.Unitize();
                if (Math.Abs(beamDir * dir) > 0.99999)
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Unable to riorient the beam");   //ortogonal, unable to reverse
                else
                {
                    Vector3d orto = Vector3d.CrossProduct(beamDir, dir);
                    orto.Unitize();
                    Vector3d newDir = Vector3d.CrossProduct(orto, beamDir);
                    newDir.Unitize();
                    if (newDir * dir < 0)
                        newDir.Reverse();

                    orto = Vector3d.CrossProduct(beamDir, beamStartingAxes);
                    double x = newDir * beamStartingAxes;
                    double y = newDir * orto;
                    double angleIncrementDeg = Math.Atan2(y, x).ToDegrees();
                    newBeam.Value.AngleDeg += angleIncrementDeg;
                }
            }

            DA.SetData(0, newBeam);
        }

        /// <summary>
        /// Provides an Icon for the component.
        /// </summary>
        protected override System.Drawing.Bitmap Icon => Properties.ComponentIcons.For(GetType());

        /// <summary>
        /// Gets the unique ID for this component. Do not change this ID after release.
        /// </summary>
        public override Guid ComponentGuid => new Guid("28bac788-3e40-4658-b60d-f2be5183eb23");
    }
}