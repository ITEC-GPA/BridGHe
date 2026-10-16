using StressAnalysisResult = BridGHe.Common.Checks.BridgeStressResult;
using BridGHe.Grasshopper.Helpers;
using Grasshopper.Kernel;
using GPC.Checkers.Concrete.Results;
using System;

namespace BridGHe.Grasshopper.Components.MeCheck2
{
    public class CompositeBeamTensionResultComponent : GH_Component
    {
        /// <summary>
        /// Initializes a new instance of the MeshRemap class.
        /// </summary>
        public CompositeBeamTensionResultComponent()
          : base("Composite Section Linear Tension Results", "CSLTR", "Results for Composite Section Linear Tension analysis", CategoryNameConstants.CATEGORY_CHECKS, CategoryNameConstants.SUBCATEGORY_MECHECK2)
        {
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("Linear Tension Results", "LTR", "The Linear Tension Results", GH_ParamAccess.item);
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("Point1_ConcreteEpsilon", "P1_CE", "Point1_ConcreteEpsilon", GH_ParamAccess.item);
            pManager.AddGenericParameter("Point1_ConcreteSigma", "P1_CS", "Point1_ConcreteSigma", GH_ParamAccess.item);
            pManager.AddGenericParameter("Point2_RebarSigma", "P2_RS", "Point2_RebarSigma", GH_ParamAccess.item);
            pManager.AddGenericParameter("Point3_RebarSigma", "P3_RS", "Point3_RebarSigma", GH_ParamAccess.item);
            pManager.AddGenericParameter("Point4_ConcreteSigma", "P4_CS", "Point4_ConcreteSigma", GH_ParamAccess.item);
            pManager.AddGenericParameter("Point5_SteelSigma", "P5_SS", "Point5_SteelSigma", GH_ParamAccess.item);
            pManager.AddGenericParameter("Point6_SteelSigma", "P6_SS", "Point6_SteelSigma", GH_ParamAccess.item);
            pManager.AddGenericParameter("Point6_SteelTau ", "P6_ST", "Point6_SteelTau ", GH_ParamAccess.item);
            pManager.AddGenericParameter("Point6_SigmaId ", "P6_Id", "Point6_SigmaId ", GH_ParamAccess.item);
            pManager.AddGenericParameter("Point7_SteelSigma", "P7_SS", "Point7_SteelSigma", GH_ParamAccess.item);
            pManager.AddGenericParameter("Point7_SteelTau ", "P7_ST", "Point7_SteelTau ", GH_ParamAccess.item);
            pManager.AddGenericParameter("Point7_SigmaId ", "P7_Id", "Point7_SigmaId ", GH_ParamAccess.item);
            pManager.AddGenericParameter("Point8_SteelSigma", "P8_SS", "Point8_SteelSigma", GH_ParamAccess.item);
            pManager.AddGenericParameter("Point8_SteelEpsilon", "P8_SE", "Point8_SteelEpsilon", GH_ParamAccess.item);
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object is used to retrieve from inputs and store in outputs.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            StressAnalysisResult result = null;

            int count = 0;
            if (!DA.GetData(count++, ref result))
                return;

            int index = 0;
            DA.SetData(index++, result.MeCheckResults.Point1_ConcreteEpsilon);
            DA.SetData(index++, result.MeCheckResults.Point1_ConcreteSigma);
            DA.SetData(index++, result.MeCheckResults.Point2_RebarSigma);
            DA.SetData(index++, result.MeCheckResults.Point3_RebarSigma);
            DA.SetData(index++, result.MeCheckResults.Point4_ConcreteSigma);
            DA.SetData(index++, result.MeCheckResults.Point5_SteelSigma);
            DA.SetData(index++, result.MeCheckResults.Point6_SteelSigma);
            DA.SetData(index++, result.MeCheckResults.Point6_SteelTau);
            DA.SetData(index++, result.MeCheckResults.Point6_SteelSigmaId);
            DA.SetData(index++, result.MeCheckResults.Point7_SteelSigma);
            DA.SetData(index++, result.MeCheckResults.Point7_SteelTau);
            DA.SetData(index++, result.MeCheckResults.Point7_SteelSigmaId);
            DA.SetData(index++, result.MeCheckResults.Point8_SteelSigma);
            DA.SetData(index++, result.MeCheckResults.Point8_SteelEpsilon);
        }

        public override GH_Exposure Exposure => GH_Exposure.tertiary;

        /// <summary>
        /// Provides an Icon for the component.
        /// </summary>
        protected override System.Drawing.Bitmap Icon => Properties.ComponentIcons.For(GetType());

        /// <summary>
        /// Gets the unique ID for this component. Do not change this ID after release.
        /// </summary>
        public override Guid ComponentGuid => new Guid("ac8892ff-18d2-4fe4-a8f5-d91053702b6d");
    }
}