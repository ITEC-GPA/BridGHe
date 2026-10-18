using System;
using System.Collections.Generic;
using System.Linq;
using BridGHe.Common.Helpers;
using BridGHe.Common.Models;
using BridGHe.Grasshopper.DataTypes.Fem;
using BridGHe.Grasshopper.Helpers;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Parameters;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;

namespace BridGHe.Grasshopper.Components.Attributes
{
    public class BeamSupportComponent : GH_Component
    {


        public BeamSupportComponent()
          : base("Beam Support", "BSup", "Beam support", CategoryNameConstants.CATEGORY_MODEL, CategoryNameConstants.SUBCATEGORY_ATTRIBUTES)
        {

        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            // 0
            pManager.AddGenericParameter("Beam", "B", "The beam where to define the beam support", GH_ParamAccess.item);

            // 1
            int i = pManager.AddIntegerParameter("Spring Type", "ST", "The spring type", GH_ParamAccess.item, 1);
            Param_Integer mtParam = pManager[i] as Param_Integer;

            foreach (BeamSupportAttributeModel.SpringType v in Enum.GetValues(typeof(BeamSupportAttributeModel.SpringType)))
                mtParam.AddNamedValue(v.GetDescription(), (int)v);

            // 2
            i = pManager.AddIntegerParameter("Spring Tension Direction", "STD", "The spring direction", GH_ParamAccess.item, 1);
            mtParam = pManager[i] as Param_Integer;
            foreach (BeamSupportAttributeModel.SpringTensionOrientation v in Enum.GetValues(typeof(BeamSupportAttributeModel.SpringTensionOrientation)))
                mtParam.AddNamedValue(v.GetDescription(), (int)v);

            // 3
            pManager.AddNumberParameter("Stiffness", "Stiff", "The simple spring stiffness per unit length of the frame object", GH_ParamAccess.item);
            pManager[pManager.ParamCount - 1].Optional = true;

            // 4
            i = pManager.AddIntegerParameter("Tension Compression", "TC", "The spring type", GH_ParamAccess.item, 1);
            mtParam = pManager[i] as Param_Integer;
            foreach (BeamSupportAttributeModel.SimpleSpringTypes v in Enum.GetValues(typeof(BeamSupportAttributeModel.SimpleSpringTypes)))
                mtParam.AddNamedValue(v.GetDescription(), (int)v);
            pManager[pManager.ParamCount - 1].Optional = true;

            // 5
            pManager.AddGenericParameter("Link property", "LP", "Link Property", GH_ParamAccess.item);
            pManager[pManager.ParamCount - 1].Optional = true;

            // 6
            pManager.AddNumberParameter("Local axis-2 angle", "LA2A", "Local axis-2 angle from default", GH_ParamAccess.item, 0.0);
            pManager[pManager.ParamCount - 1].Optional = true;

            // 7
            i = pManager.AddIntegerParameter("Frame Axis", "FA", "The frame object local axis that corresponds to the positive local ." +
                "axis of the spring.", GH_ParamAccess.item, 5);
            mtParam = pManager[i] as Param_Integer;
            foreach (BeamSupportAttributeModel.LocalAxisDirection v in Enum.GetValues(typeof(BeamSupportAttributeModel.LocalAxisDirection)))
                mtParam.AddNamedValue(v.GetDescription(), (int)v);
            pManager[pManager.ParamCount - 1].Optional = true;

            // 8
            pManager.AddNumberParameter("Plane 1-2 angle", "P12A",
                "This is the angle in the frame object 1-2 (2-3 for sap) plane measured counter clockwise from the " +
                "frame positive 2-axis to the spring positive 3-axis (= 1-axis for sap)", GH_ParamAccess.item, 0.0);
            pManager[pManager.ParamCount - 1].Optional = true;

            // 9
            i = pManager.AddIntegerParameter("Coordinate system", "CS", "Coordinate system", GH_ParamAccess.item, 1);
            mtParam = pManager[i] as Param_Integer;
            foreach (BeamSupportAttributeModel.CoordinateSystem v in Enum.GetValues(typeof(BeamSupportAttributeModel.CoordinateSystem)))
                mtParam.AddNamedValue(v.GetDescription(), (int)v);
            pManager[pManager.ParamCount - 1].Optional = true;

            // 10
            pManager.AddVectorParameter("Custom vector", "V", "Custom direction", GH_ParamAccess.item);
            pManager[pManager.ParamCount - 1].Optional = true;

        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("Beam", "B", "The modified beam", GH_ParamAccess.item);
        }

        protected override void BeforeSolveInstance()
        {
            //IGH_Param typeParamSpringType = Params.Input[1];
            //typeParamSpringType.CollectData();

            //if (typeParamSpringType.VolatileDataCount > 0)
            //{
            //    int selectedVal = typeParamSpringType.VolatileData.StructureProxy.First().Cast<GH_Integer>().First().Value;

            //    Params.Input[3].Optional = true;
            //    Params.Input[4].Optional = true;
            //    Params.Input[5].Optional = true;
            //    Params.Input[6].Optional = true;

            //    if (selectedVal == _springTypes[0].Key)
            //    {
            //        Params.Input[3].Optional = false;
            //        Params.Input[4].Optional = false;
            //    }
            //    else if (selectedVal == _springTypes[1].Key)
            //    {
            //        Params.Input[5].Optional = false;
            //        Params.Input[6].Optional = false;
            //    }
            //    Params.OnParametersChanged();
            //}


            base.BeforeSolveInstance();
        }

        public override void ExpireSolution(bool recompute)
        {
            base.ExpireSolution(recompute);
        }


        protected override void SolveInstance(IGH_DataAccess DA)
        {
            GH_Beam beam = null;
            int springType = 0;
            int springTensionOrientation = 0;

            double springStiffness = 0;
            int simpleSpringType = 0;

            GH_ObjectWrapper linkProperty = null;
            double localAxis2Angle = 0;

            int axis = 0;
            double plane12Angle = 0;
            int coordinateSystem = 0;
            Vector3d customDirection = Vector3d.Unset;

            if (!DA.GetData(0, ref beam))
                return;

            if (!DA.GetData(1, ref springType))
                return;

            if (!DA.GetData(2, ref springTensionOrientation))
                return;

            // validazione input
            if (springType == (int)BeamSupportAttributeModel.SpringType.Simple)
            {
                if (!DA.GetData(3, ref springStiffness))
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Invalid spring stiffness");
                    return;
                }

                if (!DA.GetData(4, ref simpleSpringType))
                    return;

            }
            else if (springType == (int)BeamSupportAttributeModel.SpringType.LinkProperty)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Link property non disponibile nel solutore R3. Usare il tipo Simple Spring.");
                return;
            }
            else
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Invalid spring type");
                return;
            }

            if (springTensionOrientation == (int)BeamSupportAttributeModel.SpringTensionOrientation.BeamLocalAxis)
            {
                if (!DA.GetData(7, ref axis))
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Invalid Axis");
                    return;
                }
            }
            else if (springTensionOrientation == (int)BeamSupportAttributeModel.SpringTensionOrientation.BeamObject12Plane)
            {
                if (!DA.GetData(8, ref plane12Angle))
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Invalid plane 23 angle");
                    return;
                }
            }
            else if (springTensionOrientation == (int)BeamSupportAttributeModel.SpringTensionOrientation.CustomVector)
            {
                if (!DA.GetData(9, ref coordinateSystem))
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Invalid coordinate system");
                    return;
                }

                if (!DA.GetData(10, ref customDirection))
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Invalid custom direction");
                    return;
                }
            }
            else
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Invalid tension direction");
                return;
            }


            var attributeData = new BeamSupportAttributeModel.AttributeData()
            {
                Stiffness = springStiffness,
                SpringType = (BeamSupportAttributeModel.SpringType)springType,
                SpringTensionOrientation = (BeamSupportAttributeModel.SpringTensionOrientation)springTensionOrientation,
                SimpleSpringType = (BeamSupportAttributeModel.SimpleSpringTypes)simpleSpringType,
                LocalAxis2Angle = localAxis2Angle,
                Plane12Angle = plane12Angle,
                CoordinateSystem = (BeamSupportAttributeModel.CoordinateSystem)coordinateSystem,
                LocalAxisDirection = (BeamSupportAttributeModel.LocalAxisDirection)axis,
                CustomDirection = customDirection,
                LinkProperty = linkProperty == null ? null : linkProperty.Value,
            };


            BeamSupportAttributeModel supportModel = new BeamSupportAttributeModel(attributeData);


            GH_Beam returnBeam = new GH_Beam(beam);
            returnBeam.Value.Attributes.Add(supportModel);

            DA.SetData(0, returnBeam);

        }


        public override GH_Exposure Exposure => GH_Exposure.secondary;

        protected override System.Drawing.Bitmap Icon => Properties.ComponentIcons.For(GetType());

        public override Guid ComponentGuid => new Guid("ed5438f5-aa16-4bcb-a5c9-a31bc27d4745");
    }
}