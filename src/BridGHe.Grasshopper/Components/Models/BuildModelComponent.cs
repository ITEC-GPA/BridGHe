using BridGHe.Common.Helpers;
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
        /// <summary>P2CH (14/11/2026, 83-01): the case catalogue of the last solution that gave one to the Load Combinations parameters and the
        /// stages (null before), read by the parameters to compare the catalogues of the components they feed.</summary>
        internal IReadOnlyList<Common.Gpc.Legacy.LegacyCaseData> CaseCatalog { get; private set; }

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

                // R6.3: Build Model on the GPC data of the GH types: the drafts of the elements (as attached by the GPC builders or read from a
                // file), the combinations and stages by value. P2K (P1): no FEM model is built (the GPC input alone).
                GpcOnlyModels.ParseElements(elements, out List<GH_Node> nodes, out List<GH_Beam> beams, out List<GH_Plate> plates);
                var nodeDrafts = nodes.Select(n => (n.Guid, n.Gpc)).ToList();
                var beamDrafts = beams.Select(b => (b.Guid, b.Gpc)).ToList();
                var plateDrafts = plates.Select(p => (p.Guid, p.Gpc)).ToList();
                var cases = Common.Gpc.Legacy.LegacyBuildModel.Cases(nodeDrafts.Select(d => d.Gpc.Records), beamDrafts.Select(d => d.Gpc.Records), plateDrafts.Select(d => d.Gpc.Records));

                if (DA.GetDataList(3, combinations))
                {
                    // Check the load cases names if the combinatoin is provided by a TextPanel (R6.3: on the case records of the drafts)
                    IEnumerable<string> lcases = combinations.SelectMany(c => c.Gpc.Factors).Select(f => f.Case.Name).Distinct();
                    foreach (string lcase in lcases)
                    {
                        if (cases.Count(lc => lc.Name == lcase) == 0)
                        {
                            AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"The load case {lcase} does not exists.");
                            return;
                        }
                    }
                }

                // Connect the LoadCombinationsComponent (R6.5 P1 lane 3c: the menus follow the case records; no FEM cases, null).
                // P2CH (14/11/2026, 83-01): the catalogue of this Build Model, read by the Load Combinations parameters to compare the catalogues
                // of the components they feed (set with the update of the parameters and of the stages, never apart from it).
                CaseCatalog = cases;
                IEnumerable<IGH_Param> combinations_params = Params.Input[3].Sources.Where(s => s.GetType() == typeof(LoadCombinationsParam));
                foreach (LoadCombinationsParam param in combinations_params)
                {
                    param.UpdateLoadCases(cases);
                    if (param.CatalogueConflict is string conflict) AddRuntimeMessage(GH_RuntimeMessageLevel.Error, conflict);
                }


                // Set the load cases in the StagedConstructionComponent
                foreach (IGH_Param param in Params.Input[4].Sources)
                {
                    if (param.Attributes.GetTopLevel.DocObject is StagedConstructionComponent scc)
                        scc.SetLoadCases(cases);
                }

                DA.GetDataList(4, stages);

                // R6.5 P1 lane 3c: the model by its GPC input alone (no FEM value is built). P2K (P1): the only path.
                GH_Model model = null;

                // F6 chain E (e3, point 4): the GPC input of the model from the drafts of the GH types (Build Model on GPC, the rules of the
                // former BuildModel and of the legacy reader). Every unit system (e3 point 5): the drafts are in the definition units, the
                // assembly normalises them (coordinator decision 1 of 5/11). R6.3: bridges 2 and 3 removed: an error of the GPC assembly is the
                // error of Build Model (no model; before, a remark and the conversion of the FEM model, which the analysis refused with the same
                // message), and every draft takes its own property data (all drafts sign with the GPC fingerprint).
                try
                {
                    var buildCounts = new Common.Gpc.Legacy.LegacyBuildModel.BuildCounts();
                    var input = Common.Gpc.Legacy.LegacyBuildModel.Build(name, new Common.Gpc.BridgeUnits(Common.Gpc.BridgeUnits.Parse(((Common.Gpc.DefinitionUnits)units).ToString())), Settings.Instance.Tolerance,
                        nodeDrafts, beamDrafts, plateDrafts, combinations.Select(c => c.Gpc).ToList(), stages.Select(s => s.Gpc).ToList(), buildCounts, gpcOnly: true);
                    // R6.5 (coordinator, 7/11): the copies of an element with the same ID and the same data are kept once, and named.
                    foreach (var (family, word) in new[] { (1, "beams"), (2, "plates") })
                        if (buildCounts.DroppedCopies[family].Count > 0)
                            AddRuntimeMessage(GH_RuntimeMessageLevel.Remark, $"Elements given more than once with the same data, kept once: {word} {string.Join(", ", buildCounts.DroppedCopies[family].Distinct())}.");
                    model = GH_Model.FromGpc(input);
                    model.AttachElementGuids(buildCounts.ElementGuids);
                    // R6.4 (5), decision (i) = A (coordinator, 7/11; examples 11–19): a model rebuilt from beams that all come from one analysed GPC
                    // model (Super Element Filter or Decompose Model on the output of an analysis), and nothing else, keeps that model: Build
                    // BridGHe Model reads its actions from it by BridGHe ID, as the FEM merge read the results the beams carried.
                    var analysed = beams.Count == 0 || nodes.Count != 0 || plates.Count != 0 ? null : beams[0].AnalysedGpcModel;
                    if (analysed != null && Common.Gpc.GpcResultCatalog.Of(analysed) != null
                        && beams.All(b => b.AnalysedGpc is GPC.Model.Elements.BeamElement e && ReferenceEquals(b.AnalysedGpcModel, analysed) && analysed.BeamElements.TryGetValue(e.Id, out var own) && ReferenceEquals(own, e)))
                        model.AttachLinkedResults(analysed);
                }
                catch (ArgumentException x) when (x.Data[Common.Gpc.Legacy.LegacyAssembly.TwoSuperElementsKey] is string[] two)
                {
                    // F6 (L12, lead decision of 5/11): a beam belongs to one super element (bridge data of the beam); two attributes with the same
                    // name are one super element. The archived 99 REV003 (Super Element on all members plus the schedule's G<group>) stops here.
                    throw new ArgumentException($"Beam {two[0]} is in the super elements '{two[1]}' and '{two[2]}' (LegacyBeamInTwoSuperElements): a beam belongs to one super element. Assign each beam to one Super Element.");
                }

                DA.SetData(0, model);
                Message = ((Common.Gpc.DefinitionUnits)units).GetDescription();
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

        // P2K (P1): the FEM enums the registration of the ports names, shadowed by private types of the same name, members, integers and
        // descriptions (Common/Models): the ports and menus are those of before, token for token (contract audit), with no FEM type.
        private static class ModelModel
        {
            internal enum ModelUnits
            {
                [System.ComponentModel.Description("kNm (m, kN, T, kPa, s, kJ, C)")] Knm,
                [System.ComponentModel.Description("SI (m, N, kg, Pa, s, J, C)")] SI,
                [System.ComponentModel.Description("Nmm (mm, N, T, MPa, s, mJ, C)")] Nmm,
                [System.ComponentModel.Description("PSI (in, lbf, lb, psi, s, btu, F)")] IPS
            }
        }
    }
}
