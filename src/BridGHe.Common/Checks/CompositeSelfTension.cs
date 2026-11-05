using GPC.Checkers.CompositeBridge;
using GPC.Geometry;
using GPC.Model.Sections.Concrete;

namespace BridGHe.Common.Checks
{
    /// <summary>
    /// The self-equilibrated stresses of a composite section under a slab strain (Composite Beam Self Tension): the creep coefficient of the
    /// modular ratio n from the moduli of the section, the reference system of the checks at the homogenised centroid (axes -X, -Y), the H
    /// bridge engine with EN 1994-2 at the rare SLS (<see cref="BridgeCheckAdapter.GetSelfTension"/>). Chain E step 2: one function for the
    /// former section of the property and the analysis section of its definition.
    /// </summary>
    public static class CompositeSelfTension
    {
        public static BridgeStressResult Calculate(ReinforcedConcreteSection section, double n, double strain)
        {
            if (section == null) throw new System.ArgumentNullException(nameof(section));
            double phi = BridgeCreepCoefficient.FromModularRatio(n, section.SteelSections[0].Section.SteelMaterial.ElasticModulusTension, section.ConcreteMaterial.ElasticModulusCompression);
            var reference = new CoordinateSystem(section.GetHomogenizedCentroid(phi, out double _, out double _), new Vector3d(-1, 0, 0), new Vector3d(0, -1, 0));
            var options = new BridgeAnalysisOptions { Standard = BridgeStandard.Eurocode4, LimitState = BridgeLimitState.Rare };
            return new BridgeCheckAdapter(section, reference, options).GetSelfTension(strain, phi);
        }
    }
}
