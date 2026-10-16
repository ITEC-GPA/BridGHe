using BridGHe.Common.Models;
using BridGHe.Grasshopper.DataTypes.Fem;
using BridGHe.Grasshopper.Helpers;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Parameters;
using GPC.Model.Materials;
using System;
using System.Collections.Generic;

namespace BridGHe.Grasshopper.Components.Checks
{
    public class MixedSectionShearCheckComponent : GH_Component
    {
        protected Dictionary<int, string> _standardOptions = new Dictionary<int, string>()
        {
            { 0, "EC2" },
            { 1, "AASHTO" },
            { 2, "NTC2018" },
            { 3, "SBC 306-CR-18" }
        };

        /// <summary>
        /// Initializes a new instance of the MixedSectionBeamComponent class.
        /// </summary>
        public MixedSectionShearCheckComponent()
          : base("Composite Section Shear Check", "CSSC", "Check the mixed section with input forces", CategoryNameConstants.CATEGORY_CHECKS, CategoryNameConstants.SUBCATEGORY_STEELCHECKS)
        {
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("Beam Section", "BS", "Section to check", GH_ParamAccess.item);
            int i = pManager.AddIntegerParameter("Stardard", "S", "The standard", GH_ParamAccess.item, 0);
            Param_Integer mtParam = pManager[i] as Param_Integer;
            foreach (KeyValuePair<int, string> v in _standardOptions)
                mtParam.AddNamedValue(v.Value, v.Key);
            pManager.AddGenericParameter("Concrete Material", "CM", "Concrete Material", GH_ParamAccess.item);
            pManager.AddGenericParameter("Rebar Material", "RM", "Rebar Material", GH_ParamAccess.item);
            pManager.AddGenericParameter("Steel Material", "SM", "Steel Material", GH_ParamAccess.item);
            pManager.AddNumberParameter("Distance stiffeners ", "DS", "Clear distance between transverse stiffeners", GH_ParamAccess.item, 0);
            pManager.AddGenericParameter("Loads", "L", "The loads to check", GH_ParamAccess.list);
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddNumberParameter("Shear Working Ratio", "SWR", "The shear check Working Ratio", GH_ParamAccess.list);
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object is used to retrieve from inputs and store in outputs.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            GH_MixedSection section = null;
            GH_Material concrete = null;
            GH_Material rebarMat = null;
            GH_Material steel = null;
            List<BeamForceResultModel> beamForceResultModel = new List<BeamForceResultModel>();
            int standard = 0;
            double stiffnersDistance = 0;

            int count = 0;
            if (!DA.GetData(count++, ref section))
                return;
            if (!DA.GetData(count++, ref standard))
                return;
            if (!DA.GetData(count++, ref concrete))
                return;
            if (!DA.GetData(count++, ref rebarMat))
                return;
            if (!DA.GetData(count++, ref steel))
                return;
            if (!DA.GetData(count++, ref stiffnersDistance))
                return;
            if (!DA.GetDataList(count++, beamForceResultModel))
                return;

            double h = section.SteelSection.D;          // heigth
            double bft = section.SteelSection.T1;       // Bottom flange thickness
            double tft = section.SteelSection.T2;        // Top flange thickness
            double wt = section.SteelSection.T3;        // Web thickness

            MaterialModel steelModel = steel.Value;

            List<double> shearworkingRatio = new List<double>();

            if (standard == 0 || standard == 2)
            {

            }
            else if (standard == 1 || standard == 3)   // ACI
            {
                SteelMaterialACI318 steelMaterialACI318 = new SteelMaterialACI318(steelModel.Name, steelModel.Modulus, steelModel.MinimumYieldStress, steelModel.MinimumTensileStress,
                    0.1, SteelMaterial.StressStrainCurveType.ElasticPerfectPlastic, SteelMaterial.SteelTypes.Structural);

                for (int i = 0; i < beamForceResultModel.Count; i++)
                {
                    var force = beamForceResultModel[i];

                    //shear check
                    // il codice è stato implementato secondo capitolo 7.2/7.3 del SBC306 e non 
                    // come da foglio excel dato e soprattutto come da report word
                    double cv;

                    // altezza libera tra le flange. potrebbe essere h di tutta la sezione
                    double hint = h - tft - bft;
                    double htwRatio = hint / wt;

                    if (stiffnersDistance == 0 && htwRatio <= 2.24 * Math.Sqrt(steelMaterialACI318.ElasticModulusTension / steelMaterialACI318.Fyk))
                    {
                        cv = 1.0;
                    }
                    else
                    {
                        double kv;
                        if (stiffnersDistance == 0 && htwRatio < 260)
                        {
                            kv = 5;
                        }
                        else
                        {
                            kv = 5.0 + (5 / Math.Pow(stiffnersDistance / hint, 2));
                        }
                        double rr = stiffnersDistance / hint;

                        // su excel mette il limite a 2.
                        if (rr > 3.0 || rr > Math.Pow(260.0 / (hint / wt), 2))
                            kv = 5.0;

                        double vv = Math.Sqrt(kv * steelMaterialACI318.ElasticModulusTension / steelMaterialACI318.Fyk);

                        if (htwRatio > 1.10 * vv && htwRatio < 1.37 * vv)
                            cv = 1.10 * vv / htwRatio;
                        else if (htwRatio > 1.37 * vv)
                            cv = (1.51 * steelMaterialACI318.ElasticModulusTension * kv) / (Math.Pow(htwRatio, 2) * steelMaterialACI318.Fyk);
                        else
                            cv = 1.0;
                    }

                    double shearResistance = 0.9 * 0.6 * steelMaterialACI318.Fyk * wt * hint * cv; //0.9 = phi di riduzione resistenza taglio

                    double shearWR = force.ShearForce.Y / shearResistance;
                    shearworkingRatio.Add(shearWR);
                }
            }
            else
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"Fail to set the standard");
                return;
            }

            DA.SetDataList(0, shearworkingRatio);
        }

        /// <summary>
        /// Provides an Icon for the component.
        /// </summary>
        protected override System.Drawing.Bitmap Icon => Properties.ComponentIcons.For(GetType());

        /// <summary>
        /// Gets the unique ID for this component. Do not change this ID after release.
        /// </summary>
        public override Guid ComponentGuid => new Guid("b3669fd3-ab2d-462b-a378-c9e6a5578b59");
    }
}