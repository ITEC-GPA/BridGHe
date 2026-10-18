using BridGHe.Common.Helpers;
using BridGHe.Common.Models;
using BridGHe.Grasshopper.Components.Definitions;
using BridGHe.Grasshopper.Components.Parameters;
using BridGHe.Grasshopper.DataTypes.Fem;
using BridGHe.Grasshopper.Helpers;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Parameters;
using Grasshopper.Kernel.Types;
using System;
using System.Collections.Generic;
using System.Linq;

namespace BridGHe.Grasshopper.Components.Models
{
    public class BuildModelComponent : GH_Component
    {
        /// <summary>
        /// Initializes a new instance of the BuildModelComponent class.
        /// </summary>
        public BuildModelComponent()
          : base("Build Model", "BM", "Build the FEM model", CategoryNameConstants.CATEGORY_MODEL, CategoryNameConstants.SUBCATEGORY_MODEL)
        {
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("Elements", "E", "The model elements", GH_ParamAccess.list);
            pManager.AddTextParameter("Name", "N", "The name of the model", GH_ParamAccess.item, "");
            pManager.AddIntegerParameter("Units", "U", "The measure units", GH_ParamAccess.item, 2);
            Param_Integer type_param = (Param_Integer)pManager[2];
            Array names = Enum.GetValues(typeof(ModelModel.ModelUnits));
            int i = 0;
            foreach (ModelModel.ModelUnits name in names)
                type_param.AddNamedValue(name.GetDescription(), i++);
            LoadCombinationsParam combs = new LoadCombinationsParam
            {
                Optional = true
            };
            pManager.AddParameter(combs, "Load Combinations", "LCB", "The load cases combinations", GH_ParamAccess.list);
            pManager.AddGenericParameter("Staged constructions", "SC", "The staged contruction definitions", GH_ParamAccess.list);
            pManager[pManager.ParamCount - 1].Optional = true;
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("Model", "M", "The FEM model", GH_ParamAccess.item);
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object is used to retrieve from inputs and store in outputs.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            try
            {
                List<IGH_GeometricGoo> elements = new List<IGH_GeometricGoo>();
                string name = "";
                int units = 0;
                List<GH_LoadCombination> combinations = new List<GH_LoadCombination>();
                List<GH_StagedConstruction> stages = new List<GH_StagedConstruction>();

                if (!DA.GetDataList(0, elements))
                    return;
                DA.GetData(1, ref name);
                if (!DA.GetData(2, ref units))
                    return;

                Helpers.ModelHelper.GetLoadCases(elements, out List<LoadCaseModel> loadCases);

                if (DA.GetDataList(3, combinations))
                {
                    // Check the load cases names if the combinatoin is provided by a TextPanel
                    IEnumerable<string> lcases = combinations.SelectMany(c => c.LoadCases).Select(lc => lc.Name).Distinct();
                    foreach (string lcase in lcases)
                    {
                        if (loadCases.Count(lc => lc.Name == lcase) == 0)
                        {
                            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"The load case {lcase} does not exists.");
                            return;
                        }
                    }
                }

                // Connect the LoadCombinationsComponent
                IEnumerable<IGH_Param> combinations_params = Params.Input[3].Sources.Where(s => s.GetType() == typeof(LoadCombinationsParam));
                foreach (LoadCombinationsParam param in combinations_params)
                    param.UpdateLoadCases(loadCases);


                // Set the load cases in the StagedConstructionComponent
                foreach (IGH_Param param in Params.Input[4].Sources)
                {
                    if (((GH_Attributes<IGH_Component>)param.Attributes.GetTopLevel).Owner is StagedConstructionComponent scc)
                        scc.SetLoadCases(loadCases);
                }

                DA.GetDataList(4, stages);

                GH_Model model = new GH_Model((ModelModel.ModelUnits)units, elements, combinations, stages);
                model.Value.Name = name;

                DA.SetData(0, model);
                Message = ((ModelModel.ModelUnits)units).GetDescription();
            }
            catch (Exception e)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, e.Message);
            }
        }

        public override GH_Exposure Exposure => GH_Exposure.tertiary;

        /// <summary>
        /// Provides an Icon for the component.
        /// </summary>
        protected override System.Drawing.Bitmap Icon => Properties.ComponentIcons.For(GetType());

        /// <summary>
        /// Gets the unique ID for this component. Do not change this ID after release.
        /// </summary>
        public override Guid ComponentGuid => new Guid("d99dbb1b-13c4-4398-902e-ce66e5076ff4");
    }
}