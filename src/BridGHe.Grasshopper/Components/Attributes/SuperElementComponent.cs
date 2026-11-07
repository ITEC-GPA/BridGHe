using System;
using System.Collections.Generic;
using BridGHe.Common.Models;
using BridGHe.Grasshopper.DataTypes.Fem;
using BridGHe.Grasshopper.Helpers;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Types;
using Rhino.Geometry;

namespace BridGHe.Grasshopper.Components.Attributes
{
    public class SuperElementComponent : GH_Component
    {
        /// <summary>
        /// Initializes a new instance of the SuperElementComponent class.
        /// </summary>
        public SuperElementComponent()
          : base("Super Element", "SE", "Defines that the element belong to a Super-Element", CategoryNameConstants.CATEGORY_MODEL, CategoryNameConstants.SUBCATEGORY_ATTRIBUTES)
        {
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("Element", "E", "The element to be made belonging to the Super-Element", GH_ParamAccess.item);
            pManager.AddTextParameter("Name", "N", "The name of the Super-Element", GH_ParamAccess.item);
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("Element", "E", "The modified element", GH_ParamAccess.item);
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object is used to retrieve from inputs and store in outputs.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            IGH_GeometricGoo element = null;
            string name = "";
            if (!DA.GetData(0, ref element))
                return;
            if (!DA.GetData(1, ref name))
                return;

            SuperElementAttributeModel se = new SuperElementAttributeModel(name);
            if (element is GH_Beam beam)
            {
                Func<Guid, Common.Gpc.Legacy.LegacyBeamDraft> build = id => Common.Gpc.Builders.GpcBeamBuilders.WithSuperElement(beam.Gpc, name, id);
                // R6.5 P1 lane 1a: a beam given by its GPC draft alone gives the draft built on GPC (identity derived from the input draft).
                if (beam.Value == null) { GpcOnlyElements.Set(DA, 0, GpcOnlyElements.Beam(this, 0, beam, build, name)); return; }
                GH_Beam new_beam = new GH_Beam(beam);
                new_beam.Value.Attributes.Add(se);
                // R6.2 (lane B): the GPC draft built on GPC from the draft of the input (identity of the conversion of the FEM output until R6.5).
                GpcBeamInputs.Attach(new_beam, build);
                DA.SetData(0, new_beam);
            }
            else
            {
                // Decision of the user (7/11): a super element takes only beams; any other element (a plate, a node) is an error, with no output.
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, OnlyBeams);
            }
        }

        /// <summary>The error for an element that is not a beam (decision of the user, 7/11: a super element takes only beams).</summary>
        public const string OnlyBeams = Common.Gpc.Legacy.LegacyModelImport.SuperElementOnlyBeams;

        public override GH_Exposure Exposure => GH_Exposure.quarternary;

        /// <summary>
        /// Provides an Icon for the component.
        /// </summary>
        protected override System.Drawing.Bitmap Icon => Properties.ComponentIcons.For(GetType());

        /// <summary>
        /// Gets the unique ID for this component. Do not change this ID after release.
        /// </summary>
        public override Guid ComponentGuid => new Guid("2d01d2b5-9b10-402e-aee3-672cfc4a659b");
    }
}