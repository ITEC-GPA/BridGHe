using System;
using System.Linq;

namespace BridGHe.Common.Checks
{
    public sealed record CompositeShrinkageActions(double FreeStrain,double Curvature,double PreStrain,double ThermalGradient,
        double EigenForce,double EigenMoment,double Centroid)
    {

        /// <summary>F6 chain E step 2: the same actions from the Checker definition of the composite section (the GPC datum of the composite
        /// property): n,sh and the choices of the definition, E and α of its analysis steel (the steel material of the property), the
        /// homogenised values and the bars of its analysis section (one bar per row), the slab of its section. Equal bit for bit to
        /// the former FEM overload (deleted in P2CE phase 1) on the property of the definition (gpc-composite-sections).</summary>
        public static CompositeShrinkageActions Calculate(GPC.Checkers.CompositeBridge.BridgeCompositeSectionDefinition definition,double strain)
        {
            if(definition==null || !definition.ConsiderConcrete)throw new ArgumentException("Shrinkage requires a valid composite section with concrete enabled.");
            if(definition.AnalysisSteel==null)throw new ArgumentException("Shrinkage requires the steel material of the composite section (a definition of a constant section).");
            if(!double.IsFinite(strain))throw new ArgumentOutOfRangeException(nameof(strain));
            double n=definition.NShrinkage,ea=definition.AnalysisSteel.ElasticModulusTension,alpha=definition.AnalysisSteel.AlfaThermalExpansion;
            if(!double.IsFinite(n)||n<=0||!double.IsFinite(alpha)||alpha==0)throw new ArgumentException("Positive n ritiro and a nonzero steel thermal expansion coefficient are required.");
            var p=Gpc.GpcCompositeSections.AnalysisProperties(definition,n,true,definition.ConsiderRebar,true);
            // Lane INR (user decision 12/11, option B): the n,sh property writes as I11 the inertia about the axis nearest X (SectionAxisInertias),
            // so X must be principal and that axis: a wide slab (Iyy > Ixx) is accepted now; before, I11 was the maximum and only Ixx >= Iyy was.
            if(Math.Abs(p.JxyH)>1e-10*(p.JxxH+p.JyyH)||Math.Abs(Helpers.CompositePropertyAdapter.SectionAxisInertias(p.J11H,p.J22H,p.JxxH,p.JyyH,p.angleX).I11-p.JxxH)>1e-8*p.JxxH)
                throw new NotSupportedException("Shrinkage actions require the principal axes of the section to coincide with the horizontal and vertical axes of the slab plane.");
            double width=definition.Section.Width,height=definition.Section.Height,ac=width*height,sc=ac*height/2;
            if(definition.ConsiderRebar)foreach(var bar in definition.AnalysisSection().Rebars)
            {ac-=bar.RebarSection.Area;sc-=bar.RebarSection.Area*bar.Position.Y;}
            double force=ea/n*ac*strain,moment=ea/n*(sc-ac*p.centroidH.Y)*strain;
            double epsilon=force/(ea*p.areaH/n),curvature=moment/(ea*p.JxxH/n);
            return new(epsilon,curvature,-epsilon,curvature/alpha,force,moment,p.centroidH.Y);
        }
    }
}
