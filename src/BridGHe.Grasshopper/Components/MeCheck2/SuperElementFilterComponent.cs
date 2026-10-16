using BridGHe.Common.Models;
using BridGHe.Grasshopper.DataTypes.Fem;
using BridGHe.Grasshopper.Helpers;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Special;
using System;
using System.Collections.Generic;
using System.Linq;

namespace BridGHe.Grasshopper.Components.MeCheck2
{
    public class SuperElementFilterComponent : GH_Component
    {
        /// <summary>
        /// Initializes a new instance of the ChecksComponent class.
        /// </summary>
        public SuperElementFilterComponent()
          : base("Super Element Filter", "SEF", "Filter the selected Super-Element", CategoryNameConstants.CATEGORY_CHECKS, CategoryNameConstants.SUBCATEGORY_MECHECK2)
        {

        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("FeMM Model", "FM", "The FEM model", GH_ParamAccess.item);
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
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Null model");
                Message = "Error";
                return;
            }

            // Retrieve all te super elements used in the model
            List<SuperElementAttributeModel> superElementsBuffer = Common.Helpers.ModelHelper.GetSuperElements(model.Value);
            List<string> superElementsName = new List<string>();
            for (int i = 0; i < superElementsBuffer.Count; i++)
                superElementsName.Add(superElementsBuffer[i].Value);


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
            for (int i = 0; i < model.Value.Beams.Count; i++)
            {
                BeamModel b = model.Value.Beams[i];
                if (b.HasAttribute(typeof(SuperElementAttributeModel)) && SuperElementAttributeModel.GetSuperElement(b) == super_element)
                {
                    super_beam.Add(new GH_Beam(b));
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