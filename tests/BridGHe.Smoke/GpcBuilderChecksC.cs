#if GPC_PILOT
using System;
using System.Collections.Generic;
using System.Linq;
using Grasshopper.Kernel.Types;

/// <summary>
/// R6.2 part C (P2N): regression <c>gpc-builders-c</c>, the runs of the converted producer components of part C (sections, load case,
/// beam and plate properties; the materials are in <c>gpc-material-kinds</c>), with every variant of the inputs that the components
/// distinguish. Each output must carry a datum attached by a GPC builder, equal to the conversion of its FEM value (<c>DRAFT CHECK</c>,
/// the host is always on); a refused variant has no datum and its conversion refuses it too.
/// </summary>
internal static class GpcBuilderChecksC
{
    private static IGH_Goo One(GpcBuilderChecks h, string type, params (int Input, object Value)[] inputs) => GpcBuilderChecks.Output(h.Run(type, new[] { 0 }, inputs), 0);

    /// <summary>A run whose output the reader refuses: an output without an attached datum.</summary>
    private static IGH_Goo Refused(GpcBuilderChecks h, string type, params (int Input, object Value)[] inputs)
    {
        var outputs = h.Run(type, new int[0], inputs);
        var goo = outputs[0].FirstOrDefault();
        if (goo == null) { h.Failures.Add("C " + type + ": no output"); return null; }
        if ((bool)goo.GetType().GetProperty("HasAttachedGpc").GetValue(goo)) h.Failures.Add("C " + type + ": a refused variant carries a datum");
        return goo;
    }

    internal static void Regression()
    {
        using var h = new GpcBuilderChecks("C");
        // Sections: every standard shape, values with decimals and a −0, the principal angle of the asymmetric ones.
        var rect = One(h, "BeamSectionHollowRectComponent", (0, 200.0), (1, 200.0), (2, 8.0), (3, 8.0));
        One(h, "BeamSectionHollowRectComponent", (0, 300.5), (1, 150.25), (2, 12.5), (3, 10.0));
        var solid = One(h, "BeamSectionSolidRectComponent", (0, 400.0), (1, 250.0));
        One(h, "BeamSectionSolidRectComponent", (0, 0.7), (1, 0.35));
        One(h, "BeamSectionSolidCircleComponent", (0, 300.0));
        One(h, "BeamSectionHollowCircleComponent", (0, 323.9), (1, 12.5));
        var i = One(h, "BeamSectionIComponent", (0, 400.0), (1, 600.0), (2, 1800.0), (3, 30.0), (4, 40.0), (5, 18.0), (6, 0.0));
        One(h, "BeamSectionIComponent", (0, 300.0), (1, 300.0), (2, 900.0), (3, 25.0), (4, 25.0), (5, 12.0), (6, 15.0));
        One(h, "BeamSectionTComponent", (0, 300.0), (1, 20.0), (2, 500.0), (3, 12.0));
        var angle = One(h, "BeamSectionAngleComponent", (0, 150.0), (1, 12.0), (2, 100.0), (3, 10.0));
        One(h, "BeamSectionAngleComponent", (0, 120.0), (1, 12.0), (2, 120.0), (3, 12.0));
        Refused(h, "BeamSectionAngleComponent", (0, 150.0), (1, 12.0), (2, 100.0), (3, 10.0), (4, true), (5, 10.0));
        One(h, "BeamSectionCComponent", (0, 100.0), (1, 10.0), (2, 300.0), (3, 8.0));
        Refused(h, "BeamSectionCComponent", (0, 100.0), (1, 10.0), (2, 300.0), (3, 8.0), (4, true), (5, 20.0));
        var generic = One(h, "BeamSectionGenericComponent", (0, 12000.0), (1, 4.5e8), (2, 2.1e7), (3, 3.3e6), (4, 1.0), (5, 2.0), (6, 5000.0), (7, 6000.0), (8, 1.0e6), (9, 2.0e5), (10, 150.0), (11, 450.0), (12, 0.0));
        One(h, "BeamSectionGenericComponent", (0, 9000.0), (1, 3.0e8), (2, 1.0e7), (3, 2.0e6), (4, 0.0), (5, 0.0), (6, 4000.0), (7, 3000.0), (8, 5.0e5), (9, 1.0e5), (10, -0.0), (11, 300.0), (12, 0.25));

        // Load cases: a named case, with the non-structural mass, the default «Dead».
        One(h, "LoadCaseComponent", (0, "Traffic"), (1, false));
        One(h, "LoadCaseComponent", (0, "Permanent NSM"), (1, true));
        One(h, "LoadCaseComponent", (0, "Dead"), (1, false));

        // Beam and plate properties on the outputs of the material and section components.
        var steel = GpcBuilderChecks.Output(h.Run("MaterialSteelComponent", new[] { 0 }, (0, "S355 test"), (1, 2)), 0);
        var concrete = GpcBuilderChecks.Output(h.Run("MaterialConcreteComponent", new[] { 0 }, (0, "C30/37 test"), (1, 6)), 0);
        foreach (var (section, type) in new[] { (rect, 5), (solid, 5), (i, 5), (generic, 5), (rect, 2), (rect, 1), (i, 8), (generic, 2) })
            One(h, "BeamPropertyComponent", (0, "P " + type), (1, type), (2, section), (3, steel));
        One(h, "BeamPropertyComponent", (0, ""), (1, 5), (2, rect), (3, concrete));
        Refused(h, "BeamPropertyComponent", (0, "angle"), (1, 5), (2, angle), (3, steel));
        foreach (int type in new[] { 0, 1, 2, 3, 4, 6 })
            One(h, "PlatePropertyComponent", (0, "Plate " + type), (1, type), (2, 250.0), (3, 220.0), (4, concrete));
        One(h, "PlatePropertyComponent", (0, "Patch"), (1, 5));
        One(h, "PlatePropertyComponent", (0, "Steel plate"), (1, 0), (2, 20.0), (3, 20.0), (4, steel));
        h.Finish(39);
    }
}
#endif
