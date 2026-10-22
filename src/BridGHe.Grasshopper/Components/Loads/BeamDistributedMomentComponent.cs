using BridGHe.Common.Helpers;
using BridGHe.Common.Models;
using BridGHe.Grasshopper.ComponentAttributes;
using BridGHe.Grasshopper.DataTypes.Fem;
using BridGHe.Grasshopper.Helpers;
using GH_IO.Serialization;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Parameters;
using System;
using System.Linq;

namespace BridGHe.Grasshopper.Components.Loads
{
    public class BeamDistributedMomentComponent : GH_Component
    {
        /// <summary>
        /// Initializes a new instance of the BeamDistributedMomentComponent class.
        /// </summary>
        public BeamDistributedMomentComponent()
          : base("Beam Distributed Moment", "BDM", "Assign a beam distributed moment", CategoryNameConstants.CATEGORY_MODEL, CategoryNameConstants.SUBCATEGORY_LOADS)
        {
        }

        public override void CreateAttributes()
        {
            ComponentWithImageAttributes attr = new ComponentWithImageAttributes(this, Properties.Resources.BeamDistributedMomentType1Image);
            attr.AddOverlappedBitmap(new System.Drawing.PointF(5, -75), Properties.Resources.LocalAxisImage);
            m_attributes = attr;
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("Beam", "B", "The beam where add the distributed load", GH_ParamAccess.item);
            pManager.AddGenericParameter("LoadCase", "LC", "The load case", GH_ParamAccess.item);
            pManager.AddIntegerParameter("Type", "T", "Load Type", GH_ParamAccess.item, 0);
            Param_Integer type_param = (Param_Integer)pManager[2];
            Array values = Enum.GetValues(typeof(BeamDistributedLoadModel.LoadSchema));
            foreach (BeamDistributedLoadModel.LoadSchema val in values)
                type_param.AddNamedValue(val.GetDescription(), (int)val);

            pManager.AddIntegerParameter("Direction", "D", "The load direction", GH_ParamAccess.item);
            Param_Integer dir_param = (Param_Integer)pManager[3];
            dir_param.AddNamedValue("1", 0);
            dir_param.AddNamedValue("2", 1);
            dir_param.AddNamedValue("3", 2);
            dir_param.Optional = true;

            pManager.AddNumberParameter("P1", "P1", "The P1 value", GH_ParamAccess.item);
            pManager[4].Optional = true;
            pManager.AddNumberParameter("PA", "PA", "The PA value", GH_ParamAccess.item);
            pManager[5].Optional = true;
            pManager.AddNumberParameter("PB", "PB", "The PB value", GH_ParamAccess.item);
            pManager[6].Optional = true;
            pManager.AddNumberParameter("P2", "P2", "The P2 value", GH_ParamAccess.item);
            pManager[7].Optional = true;
            pManager.AddNumberParameter("a", "a", "The distance ratio (0-1) from the starting node", GH_ParamAccess.item, 0);
            pManager.AddNumberParameter("b", "b", "The distance ratio (0-1) from the ending node", GH_ParamAccess.item, 0);
        }

        private void SetSchemaImage()
        {
            Param_Integer type_param = (Param_Integer)Params.Input[2];
            // Sources are restored after Read. Missing internal data is normal for
            // an external slider or Value List, and must not abort deserialization.
            switch (type_param.PersistentData.Branches.SelectMany(b => b).FirstOrDefault()?.Value ?? 0)
            {
                case 0:
                    ((ComponentWithImageAttributes)m_attributes).Bitmap = Properties.Resources.BeamDistributedMomentType1Image;
                    break;
                case 1:
                    ((ComponentWithImageAttributes)m_attributes).Bitmap = Properties.Resources.BeamDistributedMomentType2Image;
                    break;
                case 2:
                    ((ComponentWithImageAttributes)m_attributes).Bitmap = Properties.Resources.BeamDistributedMomentType3Image;
                    break;
                case 3:
                    ((ComponentWithImageAttributes)m_attributes).Bitmap = Properties.Resources.BeamDistributedMomentType4Image;
                    break;
                case 4:
                    ((ComponentWithImageAttributes)m_attributes).Bitmap = Properties.Resources.BeamDistributedMomentType5Image;
                    break;
                case 5:
                    ((ComponentWithImageAttributes)m_attributes).Bitmap = Properties.Resources.BeamDistributedMomentType6Image;
                    break;
            }
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("Beam", "B", "The modified beam", GH_ParamAccess.item);
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object is used to retrieve from inputs and store in outputs.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            GH_Beam beam = null;
            GH_LoadCase lc = null;
            int schema = 0;
            int dir = 0;
            if (!DA.GetData("Beam", ref beam))
                return;
            if (!DA.GetData("LoadCase", ref lc))
                return;
            if (!DA.GetData("Type", ref schema))
                return;
            bool has_dir = DA.GetData("Direction", ref dir);

            double p1 = 0, pa = 0, pb = 0, p2 = 0;
            double a = 0, b = 0;
            bool p1_has_value = DA.GetData("P1", ref p1);
            bool pa_has_value = DA.GetData("PA", ref pa);
            bool pb_has_value = DA.GetData("PB", ref pb);
            bool p2_has_value = DA.GetData("P2", ref p2);
            if (!DA.GetData("a", ref a))
                return;
            if (!DA.GetData("b", ref b))
                return;

            Message = string.Format("Direction: Local axis {0}", dir + 1);

            if (lc.Value.NonStructuralMassAcceleration)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "The connected LoadCase have the Non-structural mass flag set to on");

            bool input_errors = false;
            if (!has_dir)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Input parameter Direction failed to collect data");
                input_errors = true;
            }

            switch (schema)
            {
                case 0:
                    ((ComponentWithImageAttributes)m_attributes).Bitmap = Properties.Resources.BeamDistributedMomentType1Image;
                    if (!pa_has_value)
                    {
                        AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Input parameter 'PA' failed to collect data");
                        input_errors = true;
                    }
                    break;
                case 1:
                    ((ComponentWithImageAttributes)m_attributes).Bitmap = Properties.Resources.BeamDistributedMomentType2Image;
                    if (!pa_has_value || !pb_has_value)
                    {
                        AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Input parameters 'PA', 'PB' failed to collect data");
                        input_errors = true;
                    }
                    break;
                case 2:
                    ((ComponentWithImageAttributes)m_attributes).Bitmap = Properties.Resources.BeamDistributedMomentType3Image;
                    if (!p1_has_value || !pa_has_value || !p2_has_value)
                    {
                        AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Input parameters 'P1', 'PA', 'P2' failed to collect data");
                        input_errors = true;
                    }
                    if (a == 0)
                    {
                        AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Input parameter 'a' must be greater than 0");
                        input_errors = true;
                    }
                    break;
                case 3:
                    ((ComponentWithImageAttributes)m_attributes).Bitmap = Properties.Resources.BeamDistributedMomentType4Image;
                    if (!p1_has_value || !pa_has_value || !pb_has_value)
                    {
                        AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Input parameters 'P1', 'PA', 'PB' failed to collect data");
                        input_errors = true;
                    }
                    if (a == 0)
                    {
                        AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Input parameter 'a' must be greater than 0");
                        input_errors = true;
                    }
                    break;
                case 4:
                    ((ComponentWithImageAttributes)m_attributes).Bitmap = Properties.Resources.BeamDistributedMomentType5Image;
                    if (!pa_has_value || !pb_has_value || !p2_has_value)
                    {
                        AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Input parameters 'PA', 'PB', 'P2' failed to collect data");
                        input_errors = true;
                    }
                    if (b == 0)
                    {
                        AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Input parameter 'b' must be greater than 0");
                        input_errors = true;
                    }
                    break;
                case 5:
                    ((ComponentWithImageAttributes)m_attributes).Bitmap = Properties.Resources.BeamDistributedMomentType6Image;
                    if (!p1_has_value || !pa_has_value || !pb_has_value || !p2_has_value)
                    {
                        AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Input parameters 'P1', 'PA', 'PB', 'P2' failed to collect data");
                        input_errors = true;
                    }
                    if (a == 0 || b == 0)
                    {
                        AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Input parameters 'a' and 'b' must be greater than 0");
                        input_errors = true;
                    }
                    break;
            }
            if (a >= 1)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "The 'a' value must be lower than 1");
                input_errors = true;
            }
            if (b >= 1)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "The 'b' value must be lower than 1");
                input_errors = true;
            }
            if (a + b >= 1)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "The sum of a+b must less than 1");
                input_errors = true;
            }


            if (input_errors)
                return;

            BeamDistributedLoadModel.LoadSchema load_schema = (BeamDistributedLoadModel.LoadSchema)schema;
            BeamDistributedLoadModel.LoadData load_value = new BeamDistributedLoadModel.LoadData()
            {
                LoadSchema = load_schema,
                LoadDirection = (BeamDistributedLoadModel.LoadDirection)dir,
                P1 = p1,
                PA = pa,
                PB = pb,
                P2 = p2,
                A = a,
                B = b
            };
            BeamDistributedLoadModel load = new BeamDistributedLoadModel(lc.Value, load_value, BeamDistributedLoadModel.LoadType.Moment);
            GH_Beam newBeam = new GH_Beam(beam);
            newBeam.Value.Loads.Add(load);
            DA.SetData(0, newBeam);
        }

        public override bool Read(GH_IReader reader)
        {
            bool success = base.Read(reader);
            // After the document was read, we have to set the right Direction input choices.
            //SetDirectionContextMenuChoices();
            SetSchemaImage();

            return success;
        }

        public override GH_Exposure Exposure => GH_Exposure.secondary;

        /// <summary>
        /// Provides an Icon for the component.
        /// </summary>
        protected override System.Drawing.Bitmap Icon => Properties.ComponentIcons.For(GetType());

        /// <summary>
        /// Gets the unique ID for this component. Do not change this ID after release.
        /// </summary>
        public override Guid ComponentGuid => new Guid("fee217c4-8b33-4c74-8f3a-743818d6c78c");
    }
}
