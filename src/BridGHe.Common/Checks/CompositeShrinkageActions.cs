using System;
using System.Linq;
using BridGHe.Common.Models;

namespace BridGHe.Common.Checks
{
    public sealed record CompositeShrinkageActions(double FreeStrain,double Curvature,double PreStrain,double ThermalGradient,
        double EigenForce,double EigenMoment,double Centroid)
    {
        /// <summary>Elastic uncracked section, N-mm-MPa. Negative eigenstrain is concrete contraction.
        /// Equivalent generalized strains generate only the global restraint effects in Straus;
        /// the concrete eigenstrain must be supplied once to the section checker.</summary>
        public static CompositeShrinkageActions Calculate(CompositeBeamPropertyModel section,double strain)
        {
            if(section?.IsValid!=true || !section.ConsiderConcrete)throw new ArgumentException("Shrinkage requires a valid composite section with concrete enabled.");
            if(!double.IsFinite(strain))throw new ArgumentOutOfRangeException(nameof(strain));
            double n=section.NShrinkage,ea=section.SteelMaterial.Modulus,alpha=section.SteelMaterial.ThermalExpansion;
            if(!double.IsFinite(n)||n<=0||!double.IsFinite(alpha)||alpha==0)throw new ArgumentException("Positive n ritiro and a nonzero steel thermal expansion coefficient are required.");
            var p=section.GetHomogeneizedMechanicalProperties(n,true,section.ConsiderRebar,true);
            if(Math.Abs(p.JxyH)>1e-10*(p.JxxH+p.JyyH)||Math.Abs(p.J11H-p.JxxH)>1e-8*p.JxxH)
                throw new NotSupportedException("Shrinkage actions require the major bending axis to coincide with the slab plane's horizontal axis.");
            double ac=section.ConcreteSection.Width*section.ConcreteSection.Height,sc=ac*section.ConcreteSection.Height/2;
            if(section.ConsiderRebar)foreach(var bar in section.GetReinforcedConcreteSection().Rebars)
            {ac-=bar.RebarSection.Area;sc-=bar.RebarSection.Area*bar.Position.Y;}
            double force=ea/n*ac*strain,moment=ea/n*(sc-ac*p.centroidH.Y)*strain;
            double epsilon=force/(ea*p.areaH/n),curvature=moment/(ea*p.JxxH/n);
            return new(epsilon,curvature,-epsilon,curvature/alpha,force,moment,p.centroidH.Y);
        }
    }
}
