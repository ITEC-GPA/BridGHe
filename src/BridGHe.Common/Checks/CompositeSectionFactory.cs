using System;
using GPC.Model.Materials;
using GPC.Model.Sections;
using GPC.Model.Sections.Concrete;
using GPC.Model.Sections.Rebar;

namespace BridGHe.Common.Checks
{
    public static class CompositeSectionFactory
    {
        // The final geometric parameter of the current gPC API is horizontal eccentricity.
        // It cannot receive the historical vertical-offset input without changing the structure.
        public static ReinforcedConcreteSection Create(double width, double height, ConcreteMaterial concrete,
            IRebarSection top, double topPitch, double topCover, IRebarSection bottom, double bottomPitch,
            SectionH steel, SteelMaterial material, double bottomCover, double verticalOffset, string name)
        {
            if (!double.IsFinite(verticalOffset) || Math.Abs(verticalOffset) > 1e-9)
                throw new NotSupportedException("Vertical Offset diverso da zero: l'API gPC attuale espone eccentricità orizzontale. La trasposizione non sostituisce un offset verticale con uno orizzontale; usare 0 finché il distacco verticale non viene adattato.");
            return ReinforcedConcreteSection.CreateBridgeSection(width, height, concrete, top, topPitch, topCover, bottom, bottomPitch, steel, material, bottomCover, 0, name);
        }
    }
}
