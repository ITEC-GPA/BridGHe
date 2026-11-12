using System;
using System.Globalization;

namespace BridGHe.Common.Checks
{
    /// <summary>
    /// P2K (P10, 12/11): the GPC datum of Concrete Slab Section: the numbers of the slab of a composite section (dimensions in mm, total row areas
    /// in mm², covers to the bar axes), what the GPC composite definition is built from (<c>GpcPropertyBuilders.Composite</c>). It replaces the
    /// FEM value <c>SectionConcreteSlabModel</c> as the output of the component, with the same numbers and the same validity rule; no FEM type.
    /// </summary>
    public sealed record ConcreteSlabSection(double Height, double Width, double TopRebarArea, double TopCover, double BottomRebarArea, double BottomCover)
    {
        /// <summary>The area of the two rows.</summary>
        public double TotalRebarArea => TopRebarArea + BottomRebarArea;

        /// <summary>The rule of the former slab value: finite positive height and width, finite nonnegative row areas, finite covers, an active row
        /// axis strictly inside the slab, the top row above the bottom row.</summary>
        public bool IsValid => double.IsFinite(Height) && Height > 0 && double.IsFinite(Width) && Width > 0
            && double.IsFinite(TopRebarArea) && TopRebarArea >= 0 && double.IsFinite(BottomRebarArea) && BottomRebarArea >= 0
            && double.IsFinite(TopCover) && double.IsFinite(BottomCover)
            && (TopRebarArea == 0 || (TopCover > 0 && TopCover < Height))
            && (BottomRebarArea == 0 || (BottomCover > 0 && BottomCover < Height))
            && (TopRebarArea == 0 || BottomRebarArea == 0 || TopCover + BottomCover < Height);

        /// <summary>The text of a panel: the numbers in the invariant culture.</summary>
        public override string ToString() => string.Format(CultureInfo.InvariantCulture,
            "Concrete Slab H {0} W {1}, top {2} mm² at {3}, bottom {4} mm² at {5}", Height, Width, TopRebarArea, TopCover, BottomRebarArea, BottomCover);
    }
}
