using System;
using BridGHe.Common.Gpc;
using Grasshopper.Kernel;

namespace BridGHe.Grasshopper.Helpers
{
    internal static class OpenSectionWarnings
    {
        // Task 209 / decision 14b: an advisory threshold from the measured thick profiles,
        // not a universal accuracy bound. B and H are the overall dimensions of one part.
        internal const double ThicknessRatioLimit = 0.30;
        internal const string ThickMessage = "Thick open section: thin-wall torsion and shear centre may be inaccurate.";

        internal static void Add(GH_Component component, double b, double h, double tf, double tw)
        {
            if (!double.IsFinite(b) || !double.IsFinite(h) || !double.IsFinite(tf) || !double.IsFinite(tw)
                || b <= 0 || h <= 0 || tf <= 0 || tw <= 0) return;
            // Multiplication keeps the comparison independent of definition units and of mirror/gap.
            if (tf > ThicknessRatioLimit * b || tw > ThicknessRatioLimit * h)
                component.AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, ThickMessage);
        }

        /// <summary>D5 of the final review of 0.4.1 (0.4.2): the same warning for a T, C or Angle section (single or double) made by a cast,
        /// that is a text such as <c>L40x30x14x12</c> in a Panel (<see cref="GpcSectionCasts.Text"/>), in the component that receives it.
        /// The values of the recipe are those the three section components warn on: B the width (flange) of one part, D the height,
        /// T1 the flange and T2 the web thickness. A section given by a component or read from a file is not warned again here.</summary>
        internal static void AddForCast(GH_Component component, DataTypes.Fem.GH_BeamSection section)
        {
            var recipe = section?.Gpc;
            if (recipe == null || section.HasAttachedGpc) return;
            if (recipe.Kind == BridgeSectionKind.T || recipe.Kind == BridgeSectionKind.C || recipe.Kind == BridgeSectionKind.Angle)
                Add(component, recipe.B, recipe.D, recipe.T1, recipe.T2);
        }
    }
}
