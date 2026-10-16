using System;
using System.Collections.Generic;
using BridGHe.Common.Models;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Parameters;
using Rhino.Geometry;
using BridGHe.Common.Helpers;
using System.ComponentModel;
using BridGHe.Grasshopper.Helpers;
using BridGHe.Grasshopper.DataTypes.Fem;

namespace BridGHe.Grasshopper.Components.Attributes
{
    public class BeamTaperComponent : GH_Component
    {
        /// <summary>
        /// Initializes a new instance of the BeamReleasesComponent class.
        /// </summary>
        public BeamTaperComponent()
          : base("Beam Taper", "BT", "Beam taper", CategoryNameConstants.CATEGORY_FEMM, CategoryNameConstants.SUBCATEGORY_ATTRIBUTES)
        {
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("Beam", "B", "The beam where to define the ends releases", GH_ParamAccess.item);
            pManager.AddNumberParameter("NFromX", "NFX", "Section multiplier of dimension x of start point", GH_ParamAccess.item, 1);
            pManager.AddNumberParameter("NFromY", "NFY", "Section multiplier of dimension y of start point", GH_ParamAccess.item, 1);
            pManager.AddNumberParameter("NToX", "NTX", "Section multiplier of dimension x of end point", GH_ParamAccess.item, 1);
            pManager.AddNumberParameter("NToY", "NTY", "Section multiplier of dimension y of end point", GH_ParamAccess.item, 1);
            pManager.AddIntegerParameter("AlignmentX", "AX", "Alignment for x", GH_ParamAccess.item, (int)BeamTaperAttributeModel.AlignmentCodes.Center);
            Param_Integer type_param = (Param_Integer)pManager[pManager.ParamCount - 1];
            foreach (Enum code in Enum.GetValues(typeof(BeamTaperAttributeModel.AlignmentCodes)))
            {
                type_param.AddNamedValue(code.GetDescription(), (int)(BeamTaperAttributeModel.AlignmentCodes)code);
            }
            pManager.AddIntegerParameter("AlignmentY", "AY", "Alignment for y", GH_ParamAccess.item, (int)BeamTaperAttributeModel.AlignmentCodes.Center);
            type_param = (Param_Integer)pManager[pManager.ParamCount - 1];
            foreach (Enum code in Enum.GetValues(typeof(BeamTaperAttributeModel.AlignmentCodes)))
            {
                type_param.AddNamedValue(code.GetDescription(), (int)(BeamTaperAttributeModel.AlignmentCodes)code);
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
            double nFromX = 0, nToX = 0, nFromY = 0, nToY = 0;
            int alignmentX = 0, alignmentY = 0;
            if (!DA.GetData(0, ref beam))
                return;
            if (!DA.GetData(1, ref nFromX) || nFromX <= 0)
                return;
            if (!DA.GetData(2, ref nFromY) || nFromY <= 0)
                return;
            if (!DA.GetData(3, ref nToX) || nToX <= 0)
                return;
            if (!DA.GetData(4, ref nToY) || nToY <= 0)
                return;
            if (!DA.GetData(5, ref alignmentX))
                return;
            if (!DA.GetData(6, ref alignmentY))
                return;

            //Taper cannot be applied to beams with mirrors, like st7
            if (beam.Value.BeamProperty.Mirror != SectionModel.MirrorTypes.None)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Unable to apply the taper attribute to mirrored beams");
                return;
            }

            GH_Beam new_beam = new GH_Beam(beam);

            BeamTaperAttributeModel.AttributeData data = new BeamTaperAttributeModel.AttributeData()
            {
                NFromX = nFromX,
                NToX = nToX,
                NFromY = nFromY,
                NToY = nToY,
                AlignmentX = (BeamTaperAttributeModel.AlignmentCodes)alignmentX,
                AlignmentY = (BeamTaperAttributeModel.AlignmentCodes)alignmentY
            };
            BeamTaperAttributeModel attr = new BeamTaperAttributeModel(data);
            new_beam.Value.Attributes.Add(attr);

            DA.SetData("Beam", new_beam);
        }

        public override GH_Exposure Exposure => GH_Exposure.secondary;

        /// <summary>
        /// Provides an Icon for the component.
        /// </summary>
        protected override System.Drawing.Bitmap Icon => Properties.ComponentIcons.For(GetType());

        /// <summary>
        /// Gets the unique ID for this component. Do not change this ID after release.
        /// </summary>
        public override Guid ComponentGuid => new Guid("5010971e-bdee-44d6-9edc-9d6beb8ad165");
    }
}