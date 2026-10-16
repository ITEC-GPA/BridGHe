using System;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Parameters;
using BridGHe.Common.Models;
using BridGHe.Common.Helpers;
using System.Collections.Generic;
using BridGHe.Grasshopper.Helpers;
using BridGHe.Grasshopper.DataTypes.Fem;

namespace BridGHe.Grasshopper.Components.Definitions
{
    public class LoadCombinationComponent : GH_Component
    {
        public LoadCombinationComponent()
          : base("Load Combination", "LCombo", "A load combination definition", CategoryNameConstants.CATEGORY_FEMM, CategoryNameConstants.SUBCATEGORY_DEFINITIONS)
        {

        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddTextParameter("Name", "N", "The load combination name", GH_ParamAccess.item);
            pManager.AddGenericParameter("LoadCases", "LCS", "The loadcases", GH_ParamAccess.list);
            pManager.AddNumberParameter("Coefficients", "COEFF", "The coefficients", GH_ParamAccess.list);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("LoadCombination", "LCombo", "The defined load combination", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            string name = "";

            List<GH_LoadCase> loadCases = new List<GH_LoadCase>();
            List<double> values = new List<double>();
            if (!DA.GetData(0, ref name))
                return;
            if (!DA.GetDataList(1, loadCases))
                return;
            if (!DA.GetDataList(2, values))
                return;

            GH_LoadCombination loadCombination = new GH_LoadCombination(name);
            Dictionary<LoadCaseModel, double> loadComboValues = loadCombination.Value.Values;

            for (int i = 0; i < loadCases.Count; i++)
            {
                double loadCaseValue = 0;
                if (i < values.Count)
                {
                    // se values è meno lunga di loadcase allora mette 0
                    loadCaseValue = values[i];
                }

                if (loadComboValues.ContainsKey(loadCases[i].Value))
                {
                    loadComboValues[loadCases[i].Value] += loadCaseValue;
                }
                else
                {
                    loadComboValues.Add(loadCases[i].Value, loadCaseValue);
                }
            }

            DA.SetData(0, loadCombination);
        }

        protected override System.Drawing.Bitmap Icon => Properties.ComponentIcons.For(GetType());

        public override Guid ComponentGuid => new Guid("bc427929-4998-48b4-821b-16d92af627cf");
    }
}
