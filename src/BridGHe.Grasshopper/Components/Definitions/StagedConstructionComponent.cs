using BridGHe.Common.Models;
using BridGHe.Grasshopper.Components.Parameters;
using BridGHe.Grasshopper.DataTypes;
using BridGHe.Grasshopper.DataTypes.Fem;
using BridGHe.Grasshopper.Helpers;
using GH_IO.Serialization;
using Grasshopper.Kernel;
using System;
using System.Collections.Generic;
using System.Linq;

namespace BridGHe.Grasshopper.Components.Definitions
{
    public class StagedConstructionComponent : GH_Component
    {
        protected List<LoadCaseModel> _loadCases;
        // R6.3: the case records Build Model gives (null after a file is opened: the conversion of the stored FEM cases, until R6.5).
        private IReadOnlyList<Common.Gpc.Legacy.LegacyCaseData> _caseRecords;
        private IReadOnlyList<Common.Gpc.Legacy.LegacyCaseData> CaseRecords => _caseRecords ??= _loadCases.Select(Common.Gpc.GpcModels.CaseDraft).ToArray();

        /// <summary>
        /// Initializes a new instance of the StagedConstructionComponent class.
        /// </summary>
        public StagedConstructionComponent()
          : base("Staged Construction ", "SC", "Defines a staged construction", CategoryNameConstants.CATEGORY_MODEL, CategoryNameConstants.SUBCATEGORY_DEFINITIONS)
        {
            _loadCases = new List<LoadCaseModel>();
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddTextParameter("Name", "N", "The name of the stage", GH_ParamAccess.item);
            pManager.AddGenericParameter("Groups", "G", "The groups to assign to the beam", GH_ParamAccess.list);
            LoadCombinationsParam combs = new LoadCombinationsParam
            {
                Optional = true
            };
            pManager.AddParameter(combs, "Load Combinations", "LCB", "The load cases combinations", GH_ParamAccess.list);
            pManager.AddBooleanParameter("Morph", "M", "Morph the elements to their new positions", GH_ParamAccess.item, false);
            pManager.AddBooleanParameter("Rotate clusters", "RC", "Remove the rigid body rotation of each cluster of elements", GH_ParamAccess.item, false);
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("Staged Construction", "SC", "The defined staged contruction", GH_ParamAccess.item);
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object is used to retrieve from inputs and store in outputs.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            string name = string.Empty;
            List<GH_Group> groups = new List<GH_Group>();
            List<GH_LoadCombination> combinations = new List<GH_LoadCombination>();
            bool morph = false;
            bool rotate = false;
            if (!DA.GetData(0, ref name))
                return;
            Message = name;
            if (!DA.GetDataList(1, groups))
                return;
            DA.GetDataList(2, combinations);
            if (!DA.GetData(3, ref morph))
                return;
            if (!DA.GetData(4, ref rotate))
                return;

            // Connect the LoadCombinationsComponent
            IEnumerable<IGH_Param> combinations_params = Params.Input[2].Sources.Where(s => s.GetType() == typeof(LoadCombinationsParam));
            foreach (LoadCombinationsParam param in combinations_params)
                param.UpdateLoadCases(_caseRecordsOnly ? null : _loadCases, CaseRecords);

            if (combinations.Count == 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Input parameter Load Combinations failed to collect data");
                return;
            }

            // R6.5 P1 lane 2a: with the switch, or a group or combination given by its GPC datum alone, the stage by its GPC datum alone (rule
            // of passo 75).
            if (GpcOnlyProperties.On(groups, combinations))
            {
                GpcOnlyElements.Set(DA, 0, GpcOnlyProperties.Stage(this, () => Common.Gpc.Builders.GpcCaseBuilders.Stage(name, groups.Select(GpcBuilderAttach.GroupChain).ToArray(),
                    combinations.Select(c => c.Gpc).ToArray(), morph, rotate)));
                return;
            }
            StagedConstructionModel sc = new StagedConstructionModel
            {
                Name = name,
                Marph = morph,
                Rotate = rotate
            };
            sc.AddGroups(groups.Select(g => g.Value));
            sc.AddLoadCombinations(combinations.Select(c => c.Value));

            var output = new GH_StagedConstruction(sc);
            // R6.3 (dual rule until R6.5): the stage built on the GPC data of the groups and of the combinations.
            GpcBuilderAttach.Stage(output, () => Common.Gpc.Builders.GpcCaseBuilders.Stage(name, groups.Select(GpcBuilderAttach.GroupChain).ToArray(),
                combinations.Select(c => c.Gpc).ToArray(), morph, rotate));
            DA.SetData(0, output);
        }

        private bool _caseRecordsOnly;

        /// <summary>R6.3: the cases of Build Model, FEM values (until R6.5) and case records.</summary>
        public void SetLoadCases(IEnumerable<LoadCaseModel> loadCases, IReadOnlyList<Common.Gpc.Legacy.LegacyCaseData> caseRecords)
        {
            _loadCases.Clear();
            // R6.5 P1 lane 3c: null: Build Model on elements given by their GPC data alone (only the case records).
            _caseRecordsOnly = loadCases == null;
            if (loadCases != null) _loadCases.AddRange(loadCases);
            _caseRecords = caseRecords;
        }

        public override bool Write(GH_IWriter writer)
        {
            if (!base.Write(writer)) return false;
            writer.SetByteArray("LoadCases", Common.Helpers.ModelArchive.Write(_loadCases));
            return true;
        }

        public override bool Read(GH_IReader reader)
        {
            if (!base.Read(reader)) return false;
            _loadCases.Clear();
            if (reader.ItemExists("LoadCases")) _loadCases.AddRange(Common.Helpers.ModelArchive.Read<List<LoadCaseModel>>(reader.GetByteArray("LoadCases")));
            _caseRecords = null;
            return true;
        }

        /// <summary>
        /// Provides an Icon for the component.
        /// </summary>
        protected override System.Drawing.Bitmap Icon => Properties.ComponentIcons.For(GetType());

        /// <summary>
        /// Gets the unique ID for this component. Do not change this ID after release.
        /// </summary>
        public override Guid ComponentGuid => new Guid("0d40d9ed-b87d-402b-bf96-e34ec04826a4");
    }
}