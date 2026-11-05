using System;
using System.Collections.Generic;
using BridGHe.Common.Models;
using BridGHe.Grasshopper.DataTypes.Fem;
using BridGHe.Grasshopper.Helpers;
using Grasshopper.Kernel;
using Rhino.Geometry;

namespace BridGHe.Grasshopper.Components.Results
{
    public class PlateResultsComponent : GH_Component
    {
        /// <summary>
        /// Initializes a new instance of the PlateResultsComponent class.
        /// </summary>
        public PlateResultsComponent()
          : base("Plate Results", "PR", "The Plate analysis results", CategoryNameConstants.CATEGORY_MODEL, CategoryNameConstants.SUBCATEGORY_RESULTS)
        {
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("Plate", "P", "The plate with results", GH_ParamAccess.item);
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddTextParameter("Load cases", "LC", "The load case name", GH_ParamAccess.list);
            pManager.AddTextParameter("Nodes", "N", "Node id of the results", GH_ParamAccess.list);
            pManager.AddNumberParameter("Steps", "S", "The steps", GH_ParamAccess.list);
            pManager.AddVectorParameter("Force", "F", "Internal direct force per length (X = F11 direct force, Y = F22 direct force, Z = F12 shear force) (>0 st7)", GH_ParamAccess.list);
            pManager.AddNumberParameter("Force Max", "FMAX", "The maximum principal force per length (>0 st7)", GH_ParamAccess.list);
            pManager.AddNumberParameter("Force Min", "FMIN", "The minimum principal force per length (>0 st7)", GH_ParamAccess.list);
            pManager.AddNumberParameter("Force Angle", "FANG", "The angle from the area local 1 axis to the direction of the maximum principal force (>0 st7)", GH_ParamAccess.list);
            pManager.AddNumberParameter("Force Von Mises", "FVM", "The internal Von Mises force per length (>0 st7)", GH_ParamAccess.list);
            pManager.AddVectorParameter("Moment", "M", "The internal moment per length (X = M11 bending moment, Y = M22 bending moment, Z = M12 twisting moment) (>0 st7)", GH_ParamAccess.list);
            pManager.AddNumberParameter("Moment Max", "MMAX", "The maximum principal moment per length (>0 st7)", GH_ParamAccess.list);
            pManager.AddNumberParameter("Moment Min", "MMIN", "The minimum principal moment per length (>0 st7)", GH_ParamAccess.list);
            pManager.AddNumberParameter("Moment Angle", "MANG", "The angle from the area local 1 axis to the direction of the maximum principal moment (>0 st7)", GH_ParamAccess.list);
            pManager.AddVectorParameter("Shear Force", "SF", "The transverse shear force per length (X = V13, Y = V23) (>0 st7)", GH_ParamAccess.list);
            pManager.AddNumberParameter("Shear Force Max", "VMAX", "The maximum transverse shear force (only sap2000)", GH_ParamAccess.list);
            pManager.AddNumberParameter("Shear Force Angle", "VANG", "The angle from the area local 1 axis to the direction of Vmax (only sap2000)", GH_ParamAccess.list);
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object is used to retrieve from inputs and store in outputs.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            GH_PlateResults plate = null;
            if (!DA.GetData(0, ref plate))
                return;

            List<string> lc = new List<string>();
            List<string> nodes = new List<string>();
            List<double> steps = new List<double>();
            List<Vector3d> forces = new List<Vector3d>();
            List<double> fmax = new List<double>();
            List<double> fmin = new List<double>();
            List<double> fang = new List<double>();
            List<double> fvm = new List<double>();
            List<Vector3d> moments = new List<Vector3d>();
            List<double> mmax = new List<double>();
            List<double> mmin = new List<double>();
            List<double> mang = new List<double>();
            List<Vector2d> shearForces = new List<Vector2d>();
            List<double> sf_max = new List<double>();
            List<double> sf_ang = new List<double>();
            // F6 C4/R7: the rows of the analysed GPC element Decompose Model linked (the results catalog of a BridGHe analysis); no FEM results.
            static double Z(double v) => Common.Gpc.GpcResultRows.Shown(v);
            var catalog = plate.AnalysedGpc is GPC.Model.Elements.AreaElement ? Common.Gpc.GpcResultCatalog.Of(plate.AnalysedGpcModel) : null;
            if (catalog != null)
                foreach (var row in Common.Gpc.GpcResultRows.Plate(catalog, plate.AnalysedGpcModel, (GPC.Model.Elements.AreaElement)plate.AnalysedGpc))
                {
                    var v = row.Values;
                    lc.Add(catalog.Entries[row.Entry].Label); steps.Add(0); nodes.Add(row.Node);
                    forces.Add(new Vector3d(Z(v[0]), Z(v[1]), Z(v[2]))); fmax.Add(Z(row.ForceMax)); fmin.Add(Z(row.ForceMin)); fang.Add(Z(row.ForceAngle)); fvm.Add(Z(row.ForceVonMises));
                    moments.Add(new Vector3d(Z(v[5]), Z(v[6]), Z(v[7]))); mmax.Add(Z(row.MomentMax)); mmin.Add(Z(row.MomentMin)); mang.Add(Z(row.MomentAngle));
                    shearForces.Add(new Vector2d(Z(v[3]), Z(v[4]))); sf_max.Add(double.NaN); sf_ang.Add(double.NaN);
                }
            else AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, Common.Gpc.GpcResultCatalog.NoAnalysedResultsMessage);

            DA.SetDataList(0, lc);
            DA.SetDataList(1, nodes);
            DA.SetDataList(2, steps);
            DA.SetDataList(3, forces);
            DA.SetDataList(4, fmax);
            DA.SetDataList(5, fmin);
            DA.SetDataList(6, fang);
            DA.SetDataList(7, fvm);
            DA.SetDataList(8, moments);
            DA.SetDataList(9, mmax);
            DA.SetDataList(10, mmin);
            DA.SetDataList(11, mang);
            DA.SetDataList(12, shearForces);
            DA.SetDataList(13, sf_max);
            DA.SetDataList(14, sf_ang);
        }

        /// <summary>
        /// Provides an Icon for the component.
        /// </summary>
        protected override System.Drawing.Bitmap Icon => Properties.ComponentIcons.For(GetType());

        /// <summary>
        /// Gets the unique ID for this component. Do not change this ID after release.
        /// </summary>
        public override Guid ComponentGuid => new Guid("12f10928-8575-4e6e-a16a-e299fd7f5b39");
    }
}