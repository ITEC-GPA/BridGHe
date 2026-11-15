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
        // R6.3: the case records Build Model gives. P2P-01 (13/11/2026, review of ChatGPT task 45, P2CE phase 2a): null is an unknown catalogue
        // (a file opened before Build Model gave the cases, with no saved records), which changes nothing in the Load Combinations parameters;
        // an array, also empty, is a known catalogue (from Build Model, from the «LoadCaseRecords» chunk, from the cases of a 0.3.0 file). Before,
        // an unknown catalogue was taken as an empty one and the factors of a standalone Load Combinations parameter were cleared.
        private IReadOnlyList<Common.Gpc.Legacy.LegacyCaseData> _caseRecords;
        // P2P-01: the reason a saved catalogue could not be read (a damaged «LoadCaseRecords» chunk or «LoadCases» item), reported by the solution;
        // the catalogue then stays unknown (no fallback to an empty one).
        private string _caseRecordsError;
        /// <summary>P2CH (14/11/2026, 83-01): the catalogue of this stage (null: unknown), read by the Load Combinations parameters that feed it.</summary>
        internal IReadOnlyList<Common.Gpc.Legacy.LegacyCaseData> CaseRecords => _caseRecords;
        /// <summary>P2P-01: the chunk of the case records (version 1, a count, one record of <c>GpcDraftArchive</c> per case, in order).</summary>
        internal const string CaseRecordsChunk = "LoadCaseRecords";

        /// <summary>
        /// Initializes a new instance of the StagedConstructionComponent class.
        /// </summary>
        public StagedConstructionComponent()
          : base("Staged Construction ", "SC", "Defines a staged construction", CategoryNameConstants.CATEGORY_MODEL, CategoryNameConstants.SUBCATEGORY_DEFINITIONS)
        {
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

            // Connect the LoadCombinationsComponent. P2P-01: only with a known catalogue (an unknown one changes nothing).
            if (_caseRecordsError != null) AddRuntimeMessage(GH_RuntimeMessageLevel.Error, _caseRecordsError);
            if (_caseRecords != null)
            {
                IEnumerable<IGH_Param> combinations_params = Params.Input[2].Sources.Where(s => s.GetType() == typeof(LoadCombinationsParam));
                foreach (LoadCombinationsParam param in combinations_params)
                {
                    param.UpdateLoadCases(_caseRecords);
                    // P2CH (14/11/2026, 83-01): a parameter that feeds components with other catalogues changes nothing, and says so here too.
                    if (param.CatalogueConflict is string conflict) AddRuntimeMessage(GH_RuntimeMessageLevel.Error, conflict);
                }
            }

            if (combinations.Count == 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Input parameter Load Combinations failed to collect data");
                return;
            }

            // R6.5 P1 lane 2a: the stage by its GPC datum alone, built on the GPC data of the groups and of the combinations (rule of passo 75).
            // P2K (P1): the only path (GroupChain reads the GPC datum of a group; no FEM value).
            GpcOnlyElements.Set(DA, 0, GpcOnlyProperties.Stage(this, () => Common.Gpc.Builders.GpcCaseBuilders.Stage(name, groups.Select(GpcBuilderAttach.GroupChain).ToArray(),
                combinations.Select(c => c.Gpc).ToArray(), morph, rotate)));
        }

        /// <summary>R6.3: the case records of Build Model (a known catalogue; P2CE phase 2b: the FEM cases parameter, always null, removed).
        /// P2CG (13/11/2026, CE-01/CE-03): null (no catalogue) changes nothing; a catalogue is also given at once to the standalone Load Combinations
        /// parameters of the stage, for their form only (the stage has already solved in this solution; the combinations follow the catalogue at the
        /// next solve of the stage, as before, so no solution is asked for here).</summary>
        public void SetLoadCases(IReadOnlyList<Common.Gpc.Legacy.LegacyCaseData> caseRecords)
        {
            if (caseRecords == null) return;
            _caseRecords = caseRecords;
            _caseRecordsError = null;
            foreach (var param in Params.Input[2].Sources.Where(s => s.GetType() == typeof(LoadCombinationsParam)).Cast<LoadCombinationsParam>())
                param.GiveCaseCatalog(caseRecords);
        }

        /// <summary>P2P-01: the saved «LoadCases» item as 0.4.0 wrote it until now (an empty list of FEM cases: the files of 0.3.0 read it), kept
        /// so that the item does not change; the catalogue itself is in <see cref="CaseRecordsChunk"/>. P2CE phase 2b: the same bytes, a constant
        /// (<c>GpcLegacyPayloads.EmptyLoadCasesItem</c>, without the FEM type).</summary>
        private static readonly byte[] EmptyLoadCases = Common.Gpc.GpcLegacyPayloads.EmptyLoadCasesItem;

        public override bool Write(GH_IWriter writer)
        {
            if (!base.Write(writer)) return false;
            writer.SetByteArray("LoadCases", EmptyLoadCases);
            // P2P-01: a known catalogue is saved (an unknown one writes no chunk: it stays unknown at the next opening).
            if (_caseRecords != null)
            {
                var chunk = writer.CreateChunk(CaseRecordsChunk);
                chunk.SetInt32("Version", 1);
                chunk.SetInt32("Count", _caseRecords.Count);
                for (int i = 0; i < _caseRecords.Count; i++) chunk.SetByteArray("Case", i, Common.Gpc.GpcDraftArchive.Write(_caseRecords[i]));
            }
            return true;
        }

        public override bool Read(GH_IReader reader)
        {
            if (!base.Read(reader)) return false;
            (_caseRecords, _caseRecordsError) = ReadCaseRecords(reader);
            return true;
        }

        /// <summary>P2P-01, P2CE phase 2a: the saved catalogue: the «LoadCaseRecords» chunk when present (a count of 0 is a known empty catalogue);
        /// otherwise the «LoadCases» item of 0.3.0 and of the development files read by value (<c>GpcLegacyPayloads.Cases</c>, no FEM CLR type):
        /// its cases are a known catalogue, an empty or missing item an unknown one. A chunk of another version, truncated or damaged: an error,
        /// the catalogue unknown.</summary>
        internal static (IReadOnlyList<Common.Gpc.Legacy.LegacyCaseData> Records, string Error) ReadCaseRecords(GH_IReader reader)
        {
            var chunk = reader.FindChunk(CaseRecordsChunk);
            try
            {
                if (chunk != null)
                {
                    if (!chunk.ItemExists("Version") || chunk.GetInt32("Version") != 1)
                        throw new System.Runtime.Serialization.SerializationException("unsupported version " + (chunk.ItemExists("Version") ? chunk.GetInt32("Version").ToString() : "(none)"));
                    int count = chunk.ItemExists("Count") ? chunk.GetInt32("Count") : throw new System.Runtime.Serialization.SerializationException("no count");
                    if (count < 0) throw new System.Runtime.Serialization.SerializationException("negative count");
                    // P2CG (13/11/2026, review of ChatGPT task 51, CE-02): the count must be the number of the case records of the chunk, checked
                    // before the array is made (a count smaller than the records gave a partial catalogue that removed factors; a huge one, an array).
                    // Other items stay allowed. A gap in the indices with the right number is found below (a case missing).
                    int present = chunk.Items.Count(item => item.HasIndex && string.Equals(item.Name, "Case", StringComparison.OrdinalIgnoreCase));
                    if (present != count)
                    {
                        if (present > count) throw new System.Runtime.Serialization.SerializationException(present + " case records for a count of " + count);
                        int gap = 0; while (gap < present && chunk.ItemExists("Case", gap)) gap++; // the first missing index (at most the number present)
                        throw new System.Runtime.Serialization.SerializationException("case " + (gap + 1) + " of " + count + " missing (truncated)");
                    }
                    var records = new Common.Gpc.Legacy.LegacyCaseData[count];
                    for (int i = 0; i < count; i++)
                        records[i] = chunk.ItemExists("Case", i) ? Common.Gpc.GpcDraftArchive.ReadCase(chunk.GetByteArray("Case", i))
                            : throw new System.Runtime.Serialization.SerializationException("case " + (i + 1) + " of " + count + " missing (truncated)");
                    return (records, null);
                }
                if (!reader.ItemExists("LoadCases")) return (null, null);
                var cases = Common.Gpc.GpcLegacyPayloads.Cases(reader.GetByteArray("LoadCases"));
                return (cases.Length == 0 ? null : cases, null);
            }
            catch (Exception x) when (x is System.Runtime.Serialization.SerializationException || x is ArgumentException || x is NotSupportedException || x is InvalidOperationException
                || x is System.IO.IOException || x is FormatException)
            {
                return (null, "The saved load case records of the stage cannot be read (" + x.Message + "): run Build Model to give the cases again.");
            }
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