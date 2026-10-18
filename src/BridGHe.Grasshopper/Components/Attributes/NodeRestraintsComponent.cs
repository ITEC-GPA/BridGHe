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
    public class NodeRestraintsComponent : GH_Component
    {
        /// <summary>
        /// Initializes a new instance of the NodeRestraintsComponent class.
        /// </summary>
        public NodeRestraintsComponent()
          : base("Restraints", "NR", "Define a node restraints \n false = not restrained; true = restrained", CategoryNameConstants.CATEGORY_MODEL, CategoryNameConstants.SUBCATEGORY_ATTRIBUTES)
        {
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("Node", "N", "The node where define the restraits", GH_ParamAccess.item);
            int i = 0;
            i = pManager.AddGenericParameter("Transational", "T", "The transational restraints", GH_ParamAccess.list);
            pManager[i].Optional = true;
            i = pManager.AddGenericParameter("Rotation", "R", "The rotation restraints", GH_ParamAccess.list);
            pManager[i].Optional = true;
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("Node", "N", "The modified node", GH_ParamAccess.item);
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object is used to retrieve from inputs and store in outputs.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            GH_Node node = null;
            if (!DA.GetData("Node", ref node))
                return;

            GH_Node newNode = new GH_Node(node);

            List<IGH_Goo> trans = new List<IGH_Goo>();
            List<IGH_Goo> rot = new List<IGH_Goo>();
            DA.GetDataList(1, trans);
            DA.GetDataList(2, rot);

            NodeRestraintAttributeModel.AttributeData attr = new NodeRestraintAttributeModel.AttributeData();
            for (int i = 0; i < trans.Count && i < 3; i++)
            {
                if (trans[i].CastTo(out bool b))
                {
                    if (i == 0)
                        attr.Tx = b;
                    else if (i == 1)
                        attr.Ty = b;
                    else if (i == 2)
                        attr.Tz = b;
                    continue;
                }
                if (trans[i].CastTo(out double n))
                {
                    //if (i == 0)
                    //{
                    //    attr.Tx = true;
                    //    attr.TransationalDisplacement = new Vector3d(attr.TransationalDisplacement) { X = n };
                    //}
                    //else if (i == 1)
                    //{
                    //    attr.Ty = true;
                    //    attr.TransationalDisplacement = new Vector3d(attr.TransationalDisplacement) { Y = n };
                    //}
                    //else if (i == 2)
                    //{
                    //    attr.Tz = true;
                    //    attr.TransationalDisplacement = new Vector3d(attr.TransationalDisplacement) { Z = n };
                    //}
                    //continue;
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Defines the displacement using Displacement load");
                    DA.AbortComponentSolution();
                    return;
                }
                if (trans[i].CastTo(out string s))
                {
                    if (bool.TryParse(s, out b))
                    {
                        if (i == 0)
                            attr.Tx = b;
                        else if (i == 1)
                            attr.Ty = b;
                        else if (i == 2)
                            attr.Tz = b;
                    }
                    else if (double.TryParse(s, out n))
                    {
                        //if (i == 0)
                        //{
                        //    attr.Tx = true;
                        //    attr.TransationalDisplacement = new Vector3d(attr.TransationalDisplacement) { X = n };
                        //}
                        //else if (i == 1)
                        //{
                        //    attr.Ty = true;
                        //    attr.TransationalDisplacement = new Vector3d(attr.TransationalDisplacement) { Y = n };
                        //}
                        //else if (i == 2)
                        //{
                        //    attr.Tz = true;
                        //    attr.TransationalDisplacement = new Vector3d(attr.TransationalDisplacement) { Z = n };
                        //}
                        AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Defines the displacement using Displacement load");
                        DA.AbortComponentSolution();
                        return;
                    }
                    continue;
                }
            }

            for (int i = 0; i < rot.Count && i < 3; i++)
            {
                if (rot[i].CastTo(out bool b))
                {
                    if (i == 0)
                        attr.Rx = b;
                    else if (i == 1)
                        attr.Ry = b;
                    else if (i == 2)
                        attr.Rz = b;
                }
                if (rot[i].CastTo(out double n))
                {
                    //if (i == 0)
                    //{
                    //    attr.Rx = true;
                    //    attr.RotationDisplacement = new Vector3d(attr.RotationDisplacement) { X = n };
                    //}
                    //else if (i == 1)
                    //{
                    //    attr.Ry = true;
                    //    attr.RotationDisplacement = new Vector3d(attr.RotationDisplacement) { Y = n };
                    //}
                    //else if (i == 2)
                    //{
                    //    attr.Rz = true;
                    //    attr.RotationDisplacement = new Vector3d(attr.RotationDisplacement) { Z = n };
                    //}
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Defines the displacement using Displacement load");
                    DA.AbortComponentSolution();
                    return;
                }
                if (rot[i].CastTo(out string s))
                {
                    if (bool.TryParse(s, out b))
                    {
                        if (i == 0)
                            attr.Rx = b;
                        else if (i == 1)
                            attr.Ry = b;
                        else if (i == 2)
                            attr.Rz = b;
                    }
                    else if (double.TryParse(s, out n))
                    {
                        //if (i == 0)
                        //{
                        //    attr.Rx = true;
                        //    attr.RotationDisplacement = new Vector3d(attr.RotationDisplacement) { X = n };
                        //}
                        //else if (i == 1)
                        //{
                        //    attr.Ry = true;
                        //    attr.RotationDisplacement = new Vector3d(attr.RotationDisplacement) { Y = n };
                        //}
                        //else if (i == 2)
                        //{
                        //    attr.Rz = true;
                        //    attr.RotationDisplacement = new Vector3d(attr.RotationDisplacement) { Z = n };
                        //}
                        AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Defines the displacement using Displacement load");
                        DA.AbortComponentSolution();
                        return;
                    }
                }
            }

            newNode.Value.Attributes.Add(new NodeRestraintAttributeModel(attr));

            DA.SetData("Node", newNode);
        }

        /// <summary>
        /// Provides an Icon for the component.
        /// </summary>
        protected override System.Drawing.Bitmap Icon => Properties.ComponentIcons.For(GetType());

        /// <summary>
        /// Gets the unique ID for this component. Do not change this ID after release.
        /// </summary>
        public override Guid ComponentGuid => new Guid("59ef0220-f913-42ef-88cf-f0f32a9ae608");
    }
}