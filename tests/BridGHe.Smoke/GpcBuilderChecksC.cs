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
        var traffic = One(h, "LoadCaseComponent", (0, "Traffic"), (1, false));
        var nsm = One(h, "LoadCaseComponent", (0, "Permanent NSM"), (1, true));
        var dead = One(h, "LoadCaseComponent", (0, "Dead"), (1, false));
        var traffic2 = One(h, "LoadCaseComponent", (0, "Traffic"), (1, false));

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

        // R6.2 part C, rest: Composite Beam Property (its five outputs) on Concrete Slab Section, the I section and the material components;
        // variants: both rows of bars with the reinforcement material and n,sh; one row without n,sh (n∞); no bars without the reinforcement
        // material (the bars of the steel); without concrete; without concrete and bars (the steel property at every output).
        var rebar = GpcBuilderChecks.Output(h.Run("MaterialRebarComponent", new[] { 0 }, (0, "B450C test")), 0);
        var girder = One(h, "BeamSectionIComponent", (0, 600.0), (1, 400.0), (2, 2500.0), (3, 30.0), (4, 25.0), (5, 16.0), (6, 0.0));
        IGH_Goo Slab(params (int Input, object Value)[] inputs) => GpcBuilderChecks.Output(h.Run("BeamSectionConcreteSlabComponent", new int[0], inputs), 0);
        var slabBoth = Slab((0, 300.0), (1, 3000.0), (2, 4000.0), (3, 50.0), (4, 3000.0), (5, 45.0));
        var slabTop = Slab((0, 250.0), (1, 2400.5), (2, 2500.0), (3, 60.0));
        var slabNone = Slab((0, 280.0), (1, 2800.0));
        var all = new[] { 0, 1, 2, 3, 4 };
        h.Run("BeamPropertyCompositeComponent", all, (0, "G1 composite"), (1, slabBoth), (2, girder), (3, concrete), (4, steel), (5, rebar), (6, 6.0), (7, 18.0), (8, 15.5));
        h.Run("BeamPropertyCompositeComponent", all, (0, "G2 composite"), (1, slabTop), (2, i), (3, concrete), (4, steel), (5, rebar), (6, 6.2), (7, 17.75));
        h.Run("BeamPropertyCompositeComponent", all, (0, "G3 no bars"), (1, slabNone), (2, girder), (3, concrete), (4, steel), (6, 6.0), (7, 18.0), (8, 16.0));
        h.Run("BeamPropertyCompositeComponent", all, (0, "G4 cracked"), (1, slabBoth), (2, girder), (3, concrete), (4, steel), (5, rebar), (6, 6.0), (7, 18.0), (8, 15.5), (9, false), (10, true));
        var steelOnly = h.Run("BeamPropertyCompositeComponent", all, (0, "G5 steel"), (1, slabBoth), (2, girder), (3, concrete), (4, steel), (5, rebar), (6, 6.0), (7, 18.0), (9, false), (10, false));
        if (Enumerable.Range(2, 3).Any(o => !ReferenceEquals(steelOnly[o].First(), steelOnly[1].First()))) h.Failures.Add("C Composite Beam Property: without concrete and bars every property output is the steel property");

        // Group: a root group, a child, a grandchild (the chain of names of the parents); the Group parameter casts a text with its separator.
        var deck = One(h, "GroupComponent", (0, "Deck"));
        var pour = One(h, "GroupComponent", (0, "Pour 1"), (1, deck));
        One(h, "GroupComponent", (0, "Segment A"), (1, pour));
        GroupCast(h, "Deck::Pour 1::Segment A", 3);
        GroupCast(h, "Spalle", 1);

        // Beam Section parameter: the units menu converts the internalised section on GPC (a standard and a generic section, two units);
        // the mirror menu gives a section without recipe (the reader refuses it).
        SectionMenu(h, rect, "Knm");
        SectionMenu(h, i, "Knm");
        SectionMenu(h, generic, "Knm");
        SectionMenu(h, generic, "IPS");
        SectionMenu(h, One(h, "BeamSectionCComponent", (0, 100.0), (1, 10.0), (2, 300.0), (3, 8.0)), null);

        // R6.3: Load Combination (factors in order; a missing factor 0; the same case twice adds; the same name from another Load Case is
        // another case; two «Dead» from two components are the same definition), Staged Construction (groups and combinations), the case
        // update of the Load Combinations parameter (Build Model and Staged Construction: cases removed and added).
        var c1 = One(h, "LoadCombinationComponent", (0, "ULS 1"), (1, new[] { dead, traffic, nsm }), (2, new[] { 1.35, 1.5, 1.35 }));
        One(h, "LoadCombinationComponent", (0, "ULS 2"), (1, new[] { dead, traffic, traffic }), (2, new[] { 1.0, 0.75, 0.5 }));
        One(h, "LoadCombinationComponent", (0, "SLS"), (1, new[] { dead, traffic, nsm }), (2, new[] { 1.0 }));
        One(h, "LoadCombinationComponent", (0, "Two traffic"), (1, new[] { traffic, traffic2 }), (2, new[] { 1.0, 2.0 }));
        var deadAgain = One(h, "LoadCaseComponent", (0, "Dead"), (1, false));
        var c2 = One(h, "LoadCombinationComponent", (0, "Two dead"), (1, new[] { dead, deadAgain, traffic }), (2, new[] { 1.0, 0.35, 1.5 }));
        One(h, "StagedConstructionComponent", (0, "Stage 1"), (1, new[] { deck, pour }), (2, new[] { c1, c2 }), (3, true), (4, false));
        One(h, "StagedConstructionComponent", (0, "Stage 2"), (1, new[] { deck }), (2, new[] { c2 }));
        CombinationUpdate(h, new[] { c1, c2 }, new[] { dead, traffic });
        CombinationUpdate(h, new[] { c1 }, new[] { dead, nsm, traffic2, One(h, "LoadCaseComponent", (0, "Wind"), (1, false)) });
        h.Finish(60);
    }

    /// <summary>R6.3: the case update of the Load Combinations parameter (its public UpdateLoadCases, the call of Build Model) on internalised
    /// copies of combinations: every combination it changes carries the combination built on the case records.</summary>
    private static void CombinationUpdate(GpcBuilderChecks h, IGH_Goo[] combinations, IGH_Goo[] cases)
    {
        var type = System.Reflection.Assembly.LoadFrom(BuildPaths.GrasshopperAssembly).GetTypes().Single(t => t.Name == "LoadCombinationsParam");
        var param = (Grasshopper.Kernel.IGH_Param)Activator.CreateInstance(type);
        var data = param.GetType().GetProperty("PersistentData").GetValue(param);
        foreach (var c in combinations) data.GetType().GetMethods().First(m => m.Name == "Append" && m.GetParameters().Length == 1).Invoke(data, new object[] { c.Duplicate() });
        using var doc = new Grasshopper.Kernel.GH_Document { Enabled = true };
        param.CreateAttributes(); doc.AddObject(param, false); doc.NewSolution(false, Grasshopper.Kernel.GH_SolutionMode.Silent);
        var fem = cases.Select(c => c.GetType().GetProperty("Value").GetValue(c)).ToList();
        var femList = (System.Collections.IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(fem[0].GetType()));
        foreach (var f in fem) femList.Add(f);
        var records = cases.Select(c => (BridGHe.Common.Gpc.Legacy.LegacyCaseData)c.GetType().GetProperty("Gpc").GetValue(c)).ToArray();
        var updated = param.VolatileData.AllData(true).ToList();
        if (updated.Count != combinations.Length) h.Failures.Add("C Load Combinations update: " + updated.Count + " combinations collected of " + combinations.Length);
        type.GetMethod("UpdateLoadCases").Invoke(param, new object[] { femList, records });
        h.Runs++;
        foreach (var goo in updated)
            if (!(bool)goo.GetType().GetProperty("HasAttachedGpc").GetValue(goo)) h.Failures.Add("C Load Combinations update: a changed combination carries no combination built on GPC");
            else h.Attached++;
    }

    /// <summary>The Group parameter casting a text (its protected PreferredCast): the deepest group carries the chain built on GPC.</summary>
    private static void GroupCast(GpcBuilderChecks h, string text, int depth)
    {
        var param = (Grasshopper.Kernel.IGH_Param)Activator.CreateInstance(System.Reflection.Assembly.LoadFrom(BuildPaths.GrasshopperAssembly).GetTypes().Single(t => t.Name == "GroupParam"));
        var cast = param.GetType().GetMethod("PreferredCast", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        var group = (IGH_Goo)cast.Invoke(param, new object[] { new GH_String(text) });
        h.Runs++;
        var chain = (IReadOnlyList<string>)group?.GetType().GetProperty("Gpc").GetValue(group);
        if (group == null || !(bool)group.GetType().GetProperty("HasAttachedGpc").GetValue(group) || chain.Count != depth)
            h.Failures.Add("C Group parameter cast '" + text + "': no chain built on GPC of " + depth + " names");
        else h.Attached++;
    }

    /// <summary>The menus of the Beam Section parameter on an internalised section (their private handlers): <paramref name="units"/>
    /// converts it from N-mm (the datum built on GPC), null sets the mirror (no datum: refused, as the conversion).</summary>
    private static void SectionMenu(GpcBuilderChecks h, IGH_Goo section, string units)
    {
        var type = System.Reflection.Assembly.LoadFrom(BuildPaths.GrasshopperAssembly).GetTypes().Single(t => t.Name == "BeamSectionParam");
        var param = (Grasshopper.Kernel.IGH_Param)Activator.CreateInstance(type);
        var data = param.GetType().GetProperty("PersistentData").GetValue(param);
        data.GetType().GetMethods().First(m => m.Name == "Append" && m.GetParameters().Length == 1).Invoke(data, new object[] { section });
        const System.Reflection.BindingFlags F = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        if (units != null) type.GetMethod("OnUnitsClick", F).Invoke(param, new object[] { new System.Windows.Forms.ToolStripMenuItem(units), EventArgs.Empty });
        else type.GetMethod("OnSetMirror", F).Invoke(param, new object[] { null, EventArgs.Empty });
        h.Runs++;
        var result = (IGH_Goo)data.GetType().GetMethod("get_FirstItem").Invoke(data, new object[] { false });
        bool attached = (bool)result.GetType().GetProperty("HasAttachedGpc").GetValue(result);
        if (ReferenceEquals(result, section)) h.Failures.Add("C Beam Section menu " + (units ?? "mirror") + ": the section was not replaced");
        else if (units != null && !attached) h.Failures.Add("C Beam Section menu " + units + ": no recipe built on GPC");
        else if (units == null && attached) h.Failures.Add("C Beam Section menu mirror: a mirrored section carries a recipe");
        else if (units != null) h.Attached++;
    }
}
#endif
