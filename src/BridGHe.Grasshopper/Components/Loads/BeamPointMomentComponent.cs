using System;
using BridGHe.Common.Models;
using BridGHe.Grasshopper.Components.Parameters;
using BridGHe.Grasshopper.DataTypes.Fem;
using BridGHe.Grasshopper.Helpers;
using GH_IO.Serialization;
using Grasshopper.Kernel;
using Rhino.Geometry;

namespace BridGHe.Grasshopper.Components.Loads
{
    public class BeamPointMomentComponent : GH_Component
    {
        /// <summary>
        /// Initializes a new instance of the BeamPointMomentComponent class.
        /// </summary>
        public BeamPointMomentComponent()
          : base("Beam Point Moment", "BPM", "Defines the beam point load moment", CategoryNameConstants.CATEGORY_MODEL, CategoryNameConstants.SUBCATEGORY_LOADS)
        {
        }

        public override void CreateAttributes()
        {
            ComponentAttributes.ComponentWithImageAttributes attr = new ComponentAttributes.ComponentWithImageAttributes(this, Properties.Resources.BeamPointMomentImage);
            attr.AddOverlappedBitmap(new System.Drawing.PointF(5, -65), Properties.Resources.LocalAxisImage);
            m_attributes = attr;
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("Beam", "B", "The beam where add the Force point moment load", GH_ParamAccess.item);
            pManager.AddGenericParameter("LoadCase", "LC", "The load case", GH_ParamAccess.item);
            CoordinateSystemParam cs_param = new CoordinateSystemParam
            {
                Optional = true
            };
            cs_param.ObjectChanged += Param_CoordinateSystem_ObjectChanged;
            pManager.AddParameter(cs_param, "Coordinate System", "CS", "The coordinate system of the load", GH_ParamAccess.item);
            pManager.AddNumberParameter("M1", "M1", "The load in the 1 axis", GH_ParamAccess.item, 0);
            pManager.AddNumberParameter("M2", "M2", "The load in the 2 axis", GH_ParamAccess.item, 0);
            pManager.AddNumberParameter("M3", "M3", "The load in the 3 axis", GH_ParamAccess.item, 0);
            pManager.AddNumberParameter("a", "a", "The distance ratio (0-1) from the starting node", GH_ParamAccess.item);
        }

        private void Param_CoordinateSystem_ObjectChanged(IGH_DocumentObject sender, GH_ObjectChangedEventArgs e)
        {
            SetInputsName();
        }

        private void SetInputsName()
        {
            CoordinateSystemParam cs_param = (CoordinateSystemParam)Params.Input[2];
            if (cs_param.PersistentData.Branches.Count == 0)
            {
                Params.Input[3].NickName = "M1";
                Params.Input[4].NickName = "M2";
                Params.Input[5].NickName = "M3";
            }
            else
            {
                Params.Input[3].NickName = "MX";
                Params.Input[4].NickName = "MY";
                Params.Input[5].NickName = "MZ";
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
            SetInputsName();

            GH_Beam beam = null;
            GH_LoadCase lc = null;
            GH_CoordinateSystem cs = null;
            if (!DA.GetData("Beam", ref beam))
                return;
            if (!DA.GetData("LoadCase", ref lc))
                return;
            bool has_cs = DA.GetData("Coordinate System", ref cs);

            double p1 = 0, p2 = 0, p3 = 0;
            double a = 0;
            bool p1_has_value = DA.GetData("M1", ref p1);
            bool p2_has_value = DA.GetData("M2", ref p2);
            bool p3_has_value = DA.GetData("M3", ref p3);
            bool a_has_value = DA.GetData("a", ref a);

            Message = string.Format("Coordinate System: {0}", cs == null ? "Local" : cs.Value.Name);

            if (lc.Value.NonStructuralMassAcceleration)
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "The connected LoadCase have the Non-structural mass flag set to on");

            BeamPointLoadModel.LoadData load_value = new BeamPointLoadModel.LoadData()
            {
                CoordinateSystem = cs?.Value,
                P = new Vector3d(p1, p2, p3),
                A = a
            };
            BeamPointLoadModel load = new BeamPointLoadModel(lc.Value, load_value, BeamPointLoadModel.LoadType.Moment);
            GH_Beam newBeam = new GH_Beam(beam);
            newBeam.Value.Loads.Add(load);
            DA.SetData(0, newBeam);
        }

        public override bool Read(GH_IReader reader)
        {
            bool success = base.Read(reader);

            SetInputsName();

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
        public override Guid ComponentGuid => new Guid("2ec48e35-f612-4d14-895a-0179c6a7a8c0");
    }
}