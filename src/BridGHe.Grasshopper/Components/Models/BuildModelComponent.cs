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
                    if (param.Attributes.GetTopLevel.DocObject is StagedConstructionComponent scc)
                        scc.SetLoadCases(loadCases);
                }

                DA.GetDataList(4, stages);

                GH_Model model = new GH_Model((ModelModel.ModelUnits)units, elements, combinations, stages);
                model.Value.Name = name;
                // F6 (L12, lead decision of 5/11): a beam belongs to one super element (bridge data of the beam); two attributes with the same
                // name are one super element. The archived 99 REV003 (Super Element on all members plus the schedule's G<group>) stops here.
                foreach (var beam in model.Value.Beams)
                {
                    var names = beam.Attributes.OfType<SuperElementAttributeModel>().Select(a => a.Value).Distinct(StringComparer.Ordinal).ToArray();
                    if (names.Length > 1)
                        throw new ArgumentException($"Beam {beam.BeamId} is in the super elements '{names[0]}' and '{names[1]}' (LegacyBeamInTwoSuperElements): a beam belongs to one super element. Assign each beam to one Super Element.");
                }

                // F6 chain E (e3, point 4): the GPC input of the model from the drafts of the GH types (Build Model on GPC, the rules of the
                // former BuildModel and of the legacy reader), compared bit for bit with the conversion of the FEM model by the runners. Every
                // unit system (e3 point 5): the drafts are in the definition units, the assembly normalises them (coordinator decision 1 of 5/11).
                {
                    try
                    {
                        Helpers.ModelHelper.ParseElements(elements, out List<GH_Node> nodes, out List<GH_Beam> beams, out List<GH_Plate> plates);
                        var input = Common.Gpc.Legacy.LegacyBuildModel.Build(name, new Common.Gpc.BridgeUnits(Common.Gpc.BridgeUnits.Parse(((ModelModel.ModelUnits)units).ToString())), Settings.Instance.Tolerance,
                            nodes.Select(n => (n.Guid, n.Gpc)).ToList(),
                            // Bridge until the removal of FeMM: a draft built on GPC (Bridge State Override) signs its property with the GPC
                            // fingerprint, the conversion of a FEM value with the digest of the former content; the registry of the properties
                            // compares the signatures, so here the draft takes the data of the conversion of its FEM value (same property,
                            // checked bit for bit by gpc-state-override). After the removal every draft signs with the GPC fingerprint.
                            beams.Select(b => (b.Guid, b.HasAttachedGpc ? Common.Gpc.GpcBeamDrafts.With(b.Gpc, b.Gpc.Beam, b.Gpc.Bridge, Common.Gpc.GpcBeamDrafts.FromFemm(b.Value).Property) : b.Gpc)).ToList(),
                            plates.Select(p => (p.Guid, p.Gpc)).ToList(),
                            combinations.Select(c => Common.Gpc.GpcModels.CombinationDraft(c.Value)).ToList(), stages.Select(s => Common.Gpc.GpcModels.StageDraft(s.Value)).ToList());
                        model.AttachInput(input);
                    }
                    catch (Exception ex) when (ex is NotSupportedException || ex is ArgumentException || ex is InvalidOperationException || ex is System.Runtime.Serialization.SerializationException)
                    {
                        // The same error comes from the conversion of the FEM model, which stays the input (temporary bridge, removed with FeMM).
                        AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, "GPC Build Model: " + ex.Message);
                    }
                }

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
