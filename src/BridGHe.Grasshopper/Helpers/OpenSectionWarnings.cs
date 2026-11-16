using System;
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
    }
}
