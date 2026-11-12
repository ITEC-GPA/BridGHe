using System;
using System.Collections.Generic;
using System.Linq;
using BridGHe.Grasshopper.DataTypes.Fem;
using BridGHe.Grasshopper.Helpers;
using Grasshopper.Kernel;
using Rhino.Geometry;

namespace BridGHe.Grasshopper.Components.Results
{
    public class BeamResultsComponent : GH_Component
    {
        /// <summary>
        /// Initializes a new instance of the BeamResultsComponent class.
        /// </summary>
        public BeamResultsComponent()
          : base("Beam Results", "BR", "The Beam analysis results", CategoryNameConstants.CATEGORY_MODEL, CategoryNameConstants.SUBCATEGORY_RESULTS)
        {
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("Beam", "B", "The beam with results", GH_ParamAccess.item);
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddTextParameter("Load cases", "LC", "The load case name", GH_ParamAccess.list);
            pManager.AddNumberParameter("Stations", "ST", "The stations", GH_ParamAccess.list);
            pManager.AddNumberParameter("Steps", "S", "The steps", GH_ParamAccess.list);
            pManager.AddVectorParameter("Shear Forces", "SF", "The shear forces in the principal direction 1 and 2 (>0 st7)", GH_ParamAccess.list);
            pManager.AddVectorParameter("Bending Moments", "BM", "The bending moments in the principal direction 1 and 2 (>0 st7)", GH_ParamAccess.list);
            pManager.AddNumberParameter("Axial Forces", "AF", "The axial forces", GH_ParamAccess.list);
            pManager.AddNumberParameter("Torques", "T", "The torque (>0 st7)", GH_ParamAccess.list);
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object is used to retrieve from inputs and store in outputs.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            GH_BeamResults beam = null;
            if (!DA.GetData(0, ref beam))
                return;

            List<string> lc = new List<string>();
            List<double> stations = new List<double>();
            List<double> steps = new List<double>();
            List<Vector2d> shearForces = new List<Vector2d>();
            List<Vector2d> bendingMoments = new List<Vector2d>();
            List<double> axialForces = new List<double>();
            List<double> torques = new List<double>();
            // F6 C4/R7: the rows of the analysed GPC element Decompose Model linked (the results catalog of a BridGHe analysis); no FEM results.
            static double Z(double v) => Common.Gpc.GpcResultRows.Shown(v);
            var catalog = beam.AnalysedGpc is GPC.Model.Elements.BeamElement ? Common.Gpc.GpcResultCatalog.Of(beam.AnalysedGpcModel) : null;
            if (catalog != null)
                foreach (var row in Common.Gpc.GpcResultRows.Beam(catalog, beam.AnalysedGpcModel, (GPC.Model.Elements.BeamElement)beam.AnalysedGpc))
                {
                    lc.Add(catalog.Entries[row.Entry].Label);
                    steps.Add(0);
                    stations.Add(Z(row.Station));
                    shearForces.Add(new Vector2d(Z(row.Shear1), Z(row.Shear2)));
                    bendingMoments.Add(new Vector2d(Z(row.Bending1), Z(row.Bending2)));
                    axialForces.Add(Z(row.Axial));
                    torques.Add(Z(row.Torque));
                }
            else AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, Common.Gpc.GpcResultCatalog.NoAnalysedResultsMessage);

            DA.SetDataList(0, lc);
            DA.SetDataList(1, stations);
            DA.SetDataList(2, steps);
            DA.SetDataList(3, shearForces);
            DA.SetDataList(4, bendingMoments);
            DA.SetDataList(5, axialForces);
            DA.SetDataList(6, torques);
        }

        /// <summary>
        /// Provides an Icon for the component.
        /// </summary>
        protected override System.Drawing.Bitmap Icon => Properties.ComponentIcons.For(GetType());

        /// <summary>
        /// Gets the unique ID for this component. Do not change this ID after release.
        /// </summary>
        public override Guid ComponentGuid => new Guid("3a6035ca-82e4-42c6-8fe2-9e6cb39270e4");
    }
}