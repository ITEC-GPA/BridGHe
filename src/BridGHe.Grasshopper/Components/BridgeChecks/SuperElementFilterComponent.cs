using BridGHe.Common.Models;
using BridGHe.Grasshopper.DataTypes.Fem;
using BridGHe.Grasshopper.Helpers;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Special;
using System;
using System.Collections.Generic;
using System.Linq;

namespace BridGHe.Grasshopper.Components.BridgeChecks
{
    public class SuperElementFilterComponent : GH_Component
    {
        /// <summary>
        /// Initializes a new instance of the ChecksComponent class.
        /// </summary>
        public SuperElementFilterComponent()
          : base("Super Element Filter", "SEF", "Filter the selected Super-Element", CategoryNameConstants.CATEGORY_CHECKS, CategoryNameConstants.SUBCATEGORY_TOOLS)
        {

        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("BridGHe Model", "FM", "The FEM model", GH_ParamAccess.item);
            pManager.AddTextParameter("Super-Element", "SE", "The name of the Super-Element to filter", GH_ParamAccess.item);
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("Beams", "B", "The beams of selected super element", GH_ParamAccess.list);
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object is used to retrieve from inputs and store in outputs.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            Message = "";
            GH_Model model = new GH_Model();
            int n = 0;
            string super_element = "";

            if (!DA.GetData(n++, ref model))
                return;

            if (model.Value == null)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Awaiting a model from the upstream component");
                Message = "Error";
                return;
            }

            // R6.4 (3): the super elements of the model and the super element of every beam from the GPC input of the model (the bridge data
            // of its beams, one super element per beam, L12), the beams in the numbering of the conversion (FEM beam i = GPC beam i + 1); the
            // names in the order of the beams. Until R6.5 the test host compares them with the FEM attributes on every run (GpcDraftCheck); a
            // model whose GPC input cannot be made (e.g. beams without nodes, upstream of Build Model: bridge-details-model-example) is read from
            // its FEM attributes, as before, until R6.5.
            Common.Gpc.GpcModels.Conversion input = null;
            try { input = model.GpcInput; }
            catch (Exception ex) when (ex is ArgumentException || ex is NotSupportedException || ex is InvalidOperationException) { input = null; }
            string[] FemOf() => model.Value.Beams.Select(b => SuperElementAttributeModel.GetSuperElement(b)).ToArray();
            List<string> FemNames() => Common.Helpers.ModelHelper.GetSuperElements(model.Value).Select(a => a.Value).ToList();
            string[] superElementOf; List<string> superElementsName;
            if (input != null)
            {
                var gpcBeams = input.Model.BeamElements.Values.OrderBy(b => b.Id).ToArray();
                if (gpcBeams.Length != model.Value.Beams.Count) throw new InvalidOperationException($"GpcSuperElementFilterNumbering: {gpcBeams.Length} GPC beams, {model.Value.Beams.Count} FEM beams.");
                superElementOf = gpcBeams.Select(b => input.Bridge.Beam(b.Guid)?.SuperElement).ToArray();
                superElementsName = superElementOf.Where(s => s != null).Distinct(StringComparer.Ordinal).ToList();
                var of = superElementOf; var names = superElementsName;
                Common.Gpc.GpcDraftCheck.Attached("Super Element Filter", string.Join("\n", names) + "\n--\n" + string.Join("\n", of.Select(s => s ?? "-")),
                    () => string.Join("\n", FemNames()) + "\n--\n" + string.Join("\n", FemOf().Select(s => s ?? "-")));
            }
            else { superElementOf = FemOf(); superElementsName = FemNames(); }


            // Connect the ValueList component on the analysis results input item
            IEnumerable<IGH_Param> keys_analysisResultLists = Params.Input[n].Sources.Where(s => s.GetType() == typeof(GH_ValueList));
            foreach (GH_ValueList vallist in keys_analysisResultLists)
            {
                if (vallist.ListMode != GH_ValueListMode.DropDown)
                    vallist.ListMode = GH_ValueListMode.DropDown;

                ComponentsHelper.SetValueList(vallist, superElementsName);
                vallist.NickName = "Super-Element";
            }

            if (DA.GetData(n++, ref super_element))
            {
                if (superElementsName.Count(a => a == super_element) == 0)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error, $"The super element {super_element} does not exists.");
                    return;
                }
            }
            else
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Input parameter Super-Element failed to collect data");
            }

            List<GH_Beam> super_beam = new List<GH_Beam>();
            // F6 (a)/(b), chain C: the beams keep the link to their beam of the GPC bridge model (FEM beam i is GPC beam i + 1, the numbering
            // of the conversion), as Decompose Model does for an analysed model.
            // R6.4 (5) (coordinator, 7/11; examples 11–19): on the output of an analysis the beams keep their analysed GPC beam too, by the same
            // numbering, as Decompose Model does (Build Model then knows the analysis a model rebuilt from them comes from; Beam Results reads them).
            var bridge = model.GpcBridge?.Model;
            if (bridge == null && model.HasAnalysedGpc && model.Gpc is GPC.Model.Models.Model analysedModel && analysedModel.BeamElements.Count == model.Value.Beams.Count)
                bridge = analysedModel;
            // R6.4 (2), extension (α): the drafts the model carries for its beams, with the identity of the conversion of every FEM value until
            // R6.5; none: the conversion of the FEM values, as before.
            var drafts = GpcModelDraftsAttach.Of(model);
            for (int i = 0; i < model.Value.Beams.Count; i++)
            {
                BeamModel b = model.Value.Beams[i];
                if (superElementOf[i] == super_element)
                {
                    var goo = new GH_Beam(b) { AnalysedGpc = bridge?.BeamElements[i + 1], AnalysedGpcModel = bridge };
                    if (drafts != null) GpcModelDraftsAttach.Attach(goo, drafts.Beams[i]);
                    super_beam.Add(goo);
                }
            }

            //var ordered = super_beam.OrderBy(i => ((ElementIdAttributeModel)i.Value.Attributes.FirstOrDefault(at => at is ElementIdAttributeModel)).Value).ToList();
            super_beam.Sort((a, b) =>
            {
                var idA = int.MaxValue;
                var idB = int.MaxValue;
                for (int i = 0; i < a.Value.Attributes.Count; i++)
                {
                    if (a.Value.Attributes[i] is ElementIdAttributeModel elementIdAttributeModel)
                    {
                        if (int.TryParse(elementIdAttributeModel.Value, out int id))
                        {
                            idA = id;
                            break;
                        }
                    }
                }
                for (int i = 0; i < b.Value.Attributes.Count; i++)
                {
                    if (b.Value.Attributes[i] is ElementIdAttributeModel elementIdAttributeModel)
                    {
                        if (int.TryParse(elementIdAttributeModel.Value, out int id))
                        {
                            idB = id;
                            break;
                        }
                    }
                }
                return idA.CompareTo(idB);
            });
            DA.SetDataList(0, super_beam);
        }

        /// <summary>
        /// Provides an Icon for the component.
        /// </summary>
        protected override System.Drawing.Bitmap Icon => Properties.ComponentIcons.For(GetType());

        /// <summary>
        /// Gets the unique ID for this component. Do not change this ID after release.
        /// </summary>
        public override Guid ComponentGuid => new Guid("850d46e5-0410-408f-aef0-16a6a202e1a4");
    }
}