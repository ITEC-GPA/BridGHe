using BridGHe.Grasshopper.Helpers;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Parameters;
using GPC.Model.Materials;
using System;
using System.Collections.Generic;

namespace BridGHe.Grasshopper.Components.BridgeChecks
{
    public class CreepAndShrinkageConcreteComponent : GH_Component
    {
        protected Dictionary<int, string> _cementType = new Dictionary<int, string>()
        {
            { 0, "Class R" },
            { 1, "Class N" },
            { 2, "Class S" },
        };


        /// <summary>
        /// Initializes a new instance of the MeshRemap class.
        /// </summary>
        public CreepAndShrinkageConcreteComponent()
          : base("Creep And Shrinkage Concrete", "CS", "Calculate Creep And Shrinkage for Concrete according to standard EN1992", CategoryNameConstants.CATEGORY_CHECKS, CategoryNameConstants.SUBCATEGORY_BRIDGE)
        {
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddNumberParameter("Fck", "Fck", "", GH_ParamAccess.item);
            pManager.AddNumberParameter("Elastic Modulus Steel", "Ea", "Elastic modulus of steel", GH_ParamAccess.item, 210000);
            pManager.AddNumberParameter("Concrete Area", "Ac[mm]", "Cross sectional area", GH_ParamAccess.item);
            pManager.AddNumberParameter("RH", "RH[%]", "Relative humidity of the ambient enviroment in %", GH_ParamAccess.item);
            pManager.AddNumberParameter("u", "u[mm]", "The perimeter of the member in contact with the atmosphere", GH_ParamAccess.item);
            pManager.AddNumberParameter("t0", "t0[days]", "The age of concrete at loading in days. Default value 28 days", GH_ParamAccess.item, 28);
            pManager.AddNumberParameter("t", "t[days]", "The age of concrete in days at the moment considerated. If is 0, consider infinite time", GH_ParamAccess.item, 0);
            int i = pManager.AddIntegerParameter("Cement Type", "CT", "The type of cement", GH_ParamAccess.item, 1);
            Param_Integer mtParam = pManager[i] as Param_Integer;
            foreach (KeyValuePair<int, string> v in _cementType)
                mtParam.AddNamedValue(v.Value, v.Key);

        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddNumberParameter("n0", "n0", "Homogenization coefficient for instant time loads", GH_ParamAccess.item);
            pManager.AddNumberParameter("n ∞", "n ∞", "Homogenization coefficient for infinite time loads", GH_ParamAccess.item);
            pManager.AddNumberParameter("y(t,t0)", "y", "Creep coefficient", GH_ParamAccess.item);
            pManager.AddNumberParameter("εcs", "ε", "Total Shrinkage strain", GH_ParamAccess.item);
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object is used to retrieve from inputs and store in outputs.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            double fck = 0;
            double Es = 0;
            double rh = 0;
            double concreteArea = 0;
            double u = 0;
            double t0 = 0;
            double t = 0;
            int cementType = 0;

            int count = 0;
            if (!DA.GetData(count++, ref fck))
                return;
            if (!DA.GetData(count++, ref Es))
                return;
            if (!DA.GetData(count++, ref concreteArea))
                return;
            if (!DA.GetData(count++, ref rh))
                return;
            if (!DA.GetData(count++, ref u))
                return;
            if (!DA.GetData(count++, ref t0))
                return;
            if (!DA.GetData(count++, ref t))
                return;
            if (!DA.GetData(count++, ref cementType))
                return;

            ConcreteMaterialEN1992 concreteMaterialEN1992 = new ConcreteMaterialEN1992("", fck, ConcreteMaterial.CompressionStressStrainDiagrams.ParabolaRectangle,
                0.2, 0.0025, 1e-6, (ConcreteMaterial.CementTypes)cementType);

            double n0 = Es / concreteMaterialEN1992.ElasticModulusCompression;
            var timeEffects = new ConcreteTimeEffects(fck, (ConcreteMaterialEuropeanCommon.CementTypes)cementType,
                rh, ConcreteTimeEffects.NotionalSize(concreteArea, u), t0);
            double psiInf = timeEffects.AtAge(t == 0 ? double.PositiveInfinity : t).CreepCoefficient;
            // gPC phi is referred to Ec=1.05 Ecm; preserve the actual creep compliance.
            double n1 = n0 * (1 + psiInf / 1.05);
            double epsilonCS = double.NaN;
            AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
                "Ritiro: l'API temporale precedente non è più disponibile; epsilonCS = NaN. Il metodo legacy a tempo infinito presenta difetti documentati nella libreria gPC e non viene usato.");

            DA.SetData(0, n0);
            DA.SetData(1, n1);
            DA.SetData(2, psiInf);
            DA.SetData(3, epsilonCS);
        }

        public override GH_Exposure Exposure => GH_Exposure.secondary;

        /// <summary>
        /// Provides an Icon for the component.
        /// </summary>
        protected override System.Drawing.Bitmap Icon => Properties.ComponentIcons.For(GetType());

        /// <summary>
        /// Gets the unique ID for this component. Do not change this ID after release.
        /// </summary>
        public override Guid ComponentGuid => new Guid("69fc44cb-48cd-4b15-af60-b025bd59bad5");
    }
}