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
            Vector3d given = dir;
            dir.Unitize();

            // R6.5 P1 lane 1a: a beam given by its GPC draft alone: the same decisions on the ends and angles of the draft (DraftAxes), the draft
            // the reader gives the changed former value (ends swapped, or the BridGHe angle turned), identity derived from the input draft.
            if (beam.Value == null)
            {
                Func<Guid, Common.Gpc.Legacy.LegacyBeamDraft> build = id => Aligned(this, beam.Gpc, ax, dir, id);
                GpcOnlyElements.Set(DA, 0, GpcOnlyElements.Beam(this, 0, beam, build, axis, given));
                return;
            }

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
        /// <summary>R6.5 P1 lane 1a: the component on a GPC draft, with the decisions and expressions of the FEM path above on the axes of the
        /// draft (<see cref="DraftAxes"/>): axis 3 against the direction swaps the ends (<see cref="Common.Gpc.Builders.GpcBeamBuilders.Reversed"/>);
        /// the other axes turn the BridGHe angle by the increment (<see cref="Common.Gpc.Builders.GpcBeamBuilders.Override"/>), with the warning of
        /// the component for a direction along the beam; otherwise the draft is unchanged (re-identified).</summary>
        private static Common.Gpc.Legacy.LegacyBeamDraft Aligned(GH_Component component, Common.Gpc.Legacy.LegacyBeamDraft d, string ax, Vector3d dir, Guid id)
        {
            if (ax == "3 Blue")
            {
                DraftAxes(d, true, out _, out _, out Vector3d beamDir3);
                return beamDir3 * dir < 0 ? Common.Gpc.Builders.GpcBeamBuilders.Reversed(d, id) : Common.Gpc.GpcDrafts.Reidentify(d, id);
            }
            Vector3d beamDir, beamStartingAxes;
            if (ax == "1 Red") DraftAxes(d, true, out beamStartingAxes, out _, out beamDir);
            else if (ax == "2 Green") DraftAxes(d, true, out _, out beamStartingAxes, out beamDir);
            else if (ax == "Local x") DraftAxes(d, false, out beamStartingAxes, out _, out beamDir);
            else if (ax == "Local y") DraftAxes(d, false, out _, out beamStartingAxes, out beamDir);
            else throw new NotSupportedException();
            beamStartingAxes.Unitize();
            if (Math.Abs(beamDir * dir) > 0.99999)
            {
                component.AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Unable to riorient the beam");
                return Common.Gpc.GpcDrafts.Reidentify(d, id);
            }
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
            double angle = d.AngleDeg;
            angle += angleIncrementDeg;
            return Common.Gpc.Builders.GpcBeamBuilders.Override(d, null, null, angle, null, id);
        }

        /// <summary>The local axes of the former beam (<c>BeamModel.GetLocalAxes</c>) on a draft, the same expressions: its ends (definition units,
        /// as the FEM value), its BridGHe angle and, for the non-principal axes, less the angle of its property (radians).</summary>
        private static void DraftAxes(Common.Gpc.Legacy.LegacyBeamDraft d, bool getPrincipal, out Vector3d axes1, out Vector3d axes2, out Vector3d axes3)
        {
            var pointFrom = new Point3d(d.Beam.StartPoint.X, d.Beam.StartPoint.Y, d.Beam.StartPoint.Z);
            var pointTo = new Point3d(d.Beam.EndPoint.X, d.Beam.EndPoint.Y, d.Beam.EndPoint.Z);
            double r = d.AngleDeg / 180 * Math.PI;
            if (!getPrincipal)
                r -= d.Property?.Angle ?? 0;
            axes3 = pointTo - pointFrom;
            axes3.Unitize();
            Vector3d v = pointTo - pointFrom;
            v.Unitize();
            if (!(Vector3d.CrossProduct(Vector3d.ZAxis, v).Length < 1.0E-12)) // Not vertical
            {
                axes1 = Vector3d.CrossProduct(Vector3d.ZAxis, axes3);
                axes1.Unitize();
                axes2 = Vector3d.CrossProduct(axes3, axes1);
                axes2.Unitize();
            }
            else // Vertical
            {
                axes2 = Vector3d.CrossProduct(axes3, Vector3d.YAxis);
                axes2.Unitize();
                axes1 = Vector3d.CrossProduct(axes2, axes3);
                axes1.Unitize();
            }
            Plane pl = new Plane(pointFrom, axes1, axes2);
            pl.Rotate(r, pl.Normal);
            axes1 = pl.XAxis;
            axes2 = pl.YAxis;
        }

        protected override System.Drawing.Bitmap Icon => Properties.ComponentIcons.For(GetType());

        /// <summary>
        /// Gets the unique ID for this component. Do not change this ID after release.
        /// </summary>
        public override Guid ComponentGuid => new Guid("28bac788-3e40-4658-b60d-f2be5183eb23");
    }
}