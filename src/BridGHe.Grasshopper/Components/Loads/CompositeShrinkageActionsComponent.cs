using System;
using BridGHe.Common.Checks;
using BridGHe.Grasshopper.DataTypes;
using BridGHe.Grasshopper.Helpers;
using Grasshopper.Kernel;

namespace BridGHe.Grasshopper.Components.Loads
{
    public sealed class CompositeShrinkageActionsComponent:GH_Component
    {
        public CompositeShrinkageActionsComponent():base("Composite Shrinkage Actions","Shrinkage","Equivalent free strain and thermal gradient for a generic composite Straus beam using n ritiro. Pass the same concrete strain and the calculated FEM case to Bridge Shrinkage Action for the phase workflow.",CategoryNameConstants.CATEGORY_MODEL,CategoryNameConstants.SUBCATEGORY_LOADS){}
        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            p.AddGenericParameter("Composite property","P","H + reinforced slab in N-mm-MPa with n ritiro",GH_ParamAccess.item);
            p.AddNumberParameter("Concrete strain","Eps","Dimensionless; negative for shrinkage. Same value must be used for the primary section autostress.",GH_ParamAccess.item,-.0002);
        }
        protected override void RegisterOutputParams(GH_OutputParamManager p)
        {
            p.AddNumberParameter("Straus pre-strain","PS","Connect to Beam Pre Load with Type = Strain. Positive means contraction in Straus.",GH_ParamAccess.item);
            p.AddNumberParameter("Axis 2 thermal gradient","G2","Connect to Beam Gradient Load Axis 2 [temperature/mm]. Requires steel thermal expansion coefficient.",GH_ParamAccess.item);
            p.AddNumberParameter("Free centroid strain","Eps0","Free axial strain at the homogenized centroid [dimensionless]",GH_ParamAccess.item);
            p.AddNumberParameter("Free curvature","K","Free curvature along section local axis 2 [1/mm]",GH_ParamAccess.item);
        }
        protected override void SolveInstance(IGH_DataAccess da)
        {
            GH_CompositeBeamProperty property=null;double strain=-.0002;if(!da.GetData(0,ref property)||!da.GetData(1,ref strain))return;
            try{var a=CompositeShrinkageActions.Calculate(property?.Value,strain);da.SetData(0,a.PreStrain);da.SetData(1,a.ThermalGradient);da.SetData(2,a.FreeStrain);da.SetData(3,a.Curvature);}
            catch(Exception ex){AddRuntimeMessage(GH_RuntimeMessageLevel.Error,ex.Message);}
        }
        protected override System.Drawing.Bitmap Icon=>Properties.ComponentIcons.For(GetType());
        public override Guid ComponentGuid=>new Guid("861d0a91-6375-4cab-a106-0f6922dd827a");
    }
}
