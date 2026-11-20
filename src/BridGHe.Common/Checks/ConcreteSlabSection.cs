using System;
using System.Globalization;

namespace BridGHe.Common.Checks
{
    /// <summary>
    /// P2K (P10, 12/11): the GPC datum of Concrete Slab Section: the numbers of the slab of a composite section (dimensions in mm, total row areas
    /// in mm², covers to the bar axes), what the GPC composite definition is built from (<c>GpcPropertyBuilders.Composite</c>). It replaces the
    /// FEM value <c>SectionConcreteSlabModel</c> as the output of the component, with the same numbers and the same validity rule; no FEM type.
    /// <para>0.5.0 (Brecciarola study item 20, 20/11/2026): the slab can also be given by its bars (<see cref="FromBars"/>): one pitch common
    /// to the two rows and a diameter per row. The row areas are then the areas over the slab width, (W/P)·π·d²/4, and the section of the
    /// checks has <see cref="BarsPerRow"/> bars per row of about the real diameter instead of three equivalent bars (with three bars a dense
    /// row near a face was refused: Ø16/100 on 4500 mm gave bars of Ø62 that left a bottom row at 30 mm from the face).</para>
    /// </summary>
    public sealed record ConcreteSlabSection(double Height, double Width, double TopRebarArea, double TopCover, double BottomRebarArea, double BottomCover)
    {
        /// <summary>The bars per row of the section of the checks of a slab given by its areas (three equivalent bars of a third of the area,
        /// the rule of <c>GpcCompositeSections</c> before 0.5.0).</summary>
        public const int AreaBarsPerRow = 3;

        /// <summary>The largest number of bars per row (the limit of the H bridge engine of the Checker, slab width / pitch ≤ 500).</summary>
        public const int MaximumBarsPerRow = 500;

        /// <summary>The pitch of the bars, common to the two rows [mm]; 0 for a slab given by its row areas.</summary>
        public double Pitch { get; init; }

        /// <summary>The diameter of the bars of the top row [mm] (slab given by its bars; 0: no top row).</summary>
        public double TopDiameter { get; init; }

        /// <summary>The diameter of the bars of the bottom row [mm] (slab given by its bars; 0: no bottom row).</summary>
        public double BottomDiameter { get; init; }

        /// <summary>True for a slab given by its bars (<see cref="FromBars"/>), false for a slab given by its row areas.</summary>
        public bool ByBars => Pitch != 0;

        /// <summary>The area of the two rows.</summary>
        public double TotalRebarArea => TopRebarArea + BottomRebarArea;

        /// <summary>The bars per row of the section of the checks: W/P rounded (at least one) for a slab given by its bars, three for a slab
        /// given by its areas.</summary>
        public int BarsPerRow => ByBars ? Bars(Width, Pitch) : AreaBarsPerRow;

        /// <summary>The slab given by its bars: one pitch for the two rows, a diameter per row (0: no row), the covers to the bar axes. The area
        /// of a row is the area over the slab width, (W/P)·π·d²/4.</summary>
        public static ConcreteSlabSection FromBars(double height, double width, double pitch, double topDiameter, double topCover, double bottomDiameter, double bottomCover) =>
            new(height, width, RowArea(width, pitch, topDiameter), topCover, RowArea(width, pitch, bottomDiameter), bottomCover)
            { Pitch = pitch, TopDiameter = topDiameter, BottomDiameter = bottomDiameter };

        /// <summary>The area of a row of bars of diameter <paramref name="diameter"/> at the pitch <paramref name="pitch"/> over the width
        /// <paramref name="width"/> [mm²]: (W/P)·π·d²/4; 0 without bars.</summary>
        public static double RowArea(double width, double pitch, double diameter) =>
            diameter == 0 ? 0 : width / pitch * Math.PI * diameter * diameter / 4;

        private static int Bars(double width, double pitch)
        {
            double n = width / pitch;
            return !double.IsFinite(n) || n < 1 ? 1 : n > MaximumBarsPerRow ? MaximumBarsPerRow : (int)Math.Round(n, MidpointRounding.AwayFromZero);
        }

        /// <summary>The rule of the former slab value: finite positive height and width, finite nonnegative row areas, finite covers, an active row
        /// axis strictly inside the slab, the top row above the bottom row. For a slab given by its bars also: a positive pitch not smaller than
        /// the diameters, at most <see cref="MaximumBarsPerRow"/> bars per row, each bar inside the slab, the bars of the two rows apart, the
        /// areas of the rows those of the bars.</summary>
        public bool IsValid => double.IsFinite(Height) && Height > 0 && double.IsFinite(Width) && Width > 0
            && double.IsFinite(TopRebarArea) && TopRebarArea >= 0 && double.IsFinite(BottomRebarArea) && BottomRebarArea >= 0
            && double.IsFinite(TopCover) && double.IsFinite(BottomCover)
            && (TopRebarArea == 0 || (TopCover > 0 && TopCover < Height))
            && (BottomRebarArea == 0 || (BottomCover > 0 && BottomCover < Height))
            && (TopRebarArea == 0 || BottomRebarArea == 0 || TopCover + BottomCover < Height)
            && (!ByBars || BarsValid);

        private bool BarsValid => double.IsFinite(Pitch) && Pitch > 0 && double.IsFinite(TopDiameter) && TopDiameter >= 0 && double.IsFinite(BottomDiameter) && BottomDiameter >= 0
            && Pitch >= Math.Max(TopDiameter, BottomDiameter) && Width / Pitch <= MaximumBarsPerRow
            && (TopDiameter == 0 || TopCover >= TopDiameter / 2 && TopCover + TopDiameter / 2 <= Height)
            && (BottomDiameter == 0 || BottomCover >= BottomDiameter / 2 && BottomCover + BottomDiameter / 2 <= Height)
            && (TopDiameter == 0 || BottomDiameter == 0 || Height - TopCover - BottomCover >= (TopDiameter + BottomDiameter) / 2)
            && TopRebarArea == RowArea(Width, Pitch, TopDiameter) && BottomRebarArea == RowArea(Width, Pitch, BottomDiameter);

        /// <summary>The text of a panel: the numbers in the invariant culture.</summary>
        public override string ToString() => ByBars
            ? string.Format(CultureInfo.InvariantCulture, "Concrete Slab H {0} W {1}, pitch {2}, top Ø{3} = {4:0.#} mm² at {5}, bottom Ø{6} = {7:0.#} mm² at {8}, {9} bars per row",
                Height, Width, Pitch, TopDiameter, TopRebarArea, TopCover, BottomDiameter, BottomRebarArea, BottomCover, BarsPerRow)
            : string.Format(CultureInfo.InvariantCulture,
                "Concrete Slab H {0} W {1}, top {2} mm² at {3}, bottom {4} mm² at {5}", Height, Width, TopRebarArea, TopCover, BottomRebarArea, BottomCover);
    }
}
