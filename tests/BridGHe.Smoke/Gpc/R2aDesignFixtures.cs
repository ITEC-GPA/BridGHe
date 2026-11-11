#if GPC_PILOT
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BridGHe.Common.Gpc;
using BridGHe.Common.Gpc.Builders;
using BridGHe.Common.Gpc.Legacy;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Parameters;
using Grasshopper.Kernel.Types;

/// <summary>
/// R6.6 lane R2a: the GPC fixtures and GH helpers of the functional tests on design data (bridge generators, segment sections, properties,
/// materials), without any FEM model. The data are built with the product builders (the GPC data the GH components give): the GPC twins of
/// the FEM fixtures of the lane files (<c>BridgeAssignmentTests.Property</c>, <c>BridgeSegmentSectionsTests.BaseProperty</c>,
/// <c>BridgeMeshTests.DeckProperty</c>, <c>GetBeamPropertyTests.Property</c>), which stay for the callers of other lanes until P2. The GH data
/// are the types given by their GPC datum alone (<c>FromGpc</c>, no FEM value), reached by reflection: the Smoke host does not reference the
/// plug-in. Identities derived from a name: a rebuild gives the same GUIDs.
/// </summary>
internal static class R2aDesignFixtures
{
    internal static Guid Id(string name) => GpcIdentity.Derive(GpcIdentity.NameSpace, "r66-r2a-fixture:" + name);

    // ---------------------------------------------------------------------------------------------------------------------------------------
    // Materials, sections and properties (N-mm-MPa)

    /// <summary>A material of the Custom recipe (kind "Steel", "Concrete", "Undefined"…), Use Poisson.</summary>
    internal static GPC.Model.Materials.Material Material(string kind, string name, double modulus, double poisson, double density = 0) =>
        GpcMaterialBuilders.Material(new LegacyMaterialRecipe { Kind = kind, Name = name, Modulus = modulus, PoissonRatio = poisson, UsePoisson = true, Density = density });

    /// <summary>The steel of the I base property: S355, E 210000, ν 0.3 (kind Steel), with the density given (0: none).</summary>
    internal static GPC.Model.Materials.Material Steel(double density = 0) => Material("Steel", "S355", 210000, .3, density);

    /// <summary>The I section of the base property: B1 = B2 = 600, D 2500, T1 = T2 = 30, T3 16, bottom flange <paramref name="t1"/>.</summary>
    internal static LegacySectionRecipe BaseSection(double t1 = 30) => GpcSectionBuilders.Standard(BridgeSectionKind.I, 2500, b1: 600, b2: 600, t1: t1, t2: 30, t3: 16);

    /// <summary>GPC twin of <c>BridgeSegmentSectionsTests.BaseProperty</c>: Beam on <see cref="BaseSection"/>, <see cref="Steel"/>.</summary>
    internal static LegacyBeamProperty BaseProperty(string name = "Base", double density = 0, double t1 = 30) =>
        GpcPropertyBuilders.Beam(name, 5, BaseSection(t1), Steel(density), "Steel");

    /// <summary>GPC twin of <c>BridgeAssignmentTests.Property</c>: Beam on a solid rectangle B 300, D 1500, material «S» E 210000, ν 0.3.</summary>
    internal static LegacyBeamProperty RectangleProperty(string name = "P") =>
        GpcPropertyBuilders.Beam(name, 5, GpcSectionBuilders.Standard(BridgeSectionKind.SolidRectangle, 1500, 300), Material("Undefined", "S", 210000, .3));

    /// <summary>GPC twin of <c>BridgeMeshTests.DeckProperty</c>: Shell Thin «Slab», membrane <paramref name="membrane"/> and bending 250 mm,
    /// material «C» E 34000, ν 0.2.</summary>
    internal static GPC.Model.ElementProperties.PlateProperty DeckProperty(double membrane = 250) =>
        GpcPropertyBuilders.Plate("Slab", 0, Material("Undefined", "C", 34000, .2), membrane, 250);

    /// <summary>The I section of a property datum (its recipe), and its area as the GPC property holds it.</summary>
    internal static LegacySectionRecipe Recipe(LegacyBeamProperty p) => p.Data.Section;
    internal static double Area(LegacyBeamProperty p) => ((GPC.Model.ElementProperties.ElasticBeamProperty)p.Property).Section.Area;
    internal static GPC.Model.Materials.Material MaterialOf(LegacyBeamProperty p) => ((GPC.Model.ElementProperties.ElasticBeamProperty)p.Property).Material;

    // ---------------------------------------------------------------------------------------------------------------------------------------
    // GH data given by their GPC datum alone (reflection)

    internal static Assembly Plugin => BridgeMergeTests.Initialize();

    /// <summary>The GH type <paramref name="type"/> (simple name) made by its static <c>FromGpc</c> from <paramref name="args"/> (the optional
    /// arguments not given: null).</summary>
    internal static IGH_Goo Goo(string type, params object[] args)
    {
        var t = Plugin.GetTypes().Single(x => x.Name == type);
        var method = t.GetMethods(BindingFlags.Public | BindingFlags.Static).Single(m => m.Name == "FromGpc");
        int n = method.GetParameters().Length;
        return (IGH_Goo)method.Invoke(null, args.Concat(Enumerable.Repeat((object)null, n - args.Length)).ToArray());
    }

    internal static IGH_Goo Beam(LegacyBeamDraft draft, Guid? guid = null) => Goo("GH_Beam", draft, guid);
    internal static IGH_Goo Node(LegacyNodeDraft draft, Guid? guid = null) => Goo("GH_Node", draft, guid);
    internal static IGH_Goo Plate(LegacyPlateDraft draft, Guid? guid = null) => Goo("GH_Plate", draft, guid);
    internal static IGH_Goo Property(LegacyBeamProperty property) => Goo("GH_BeamProperty", property);
    internal static IGH_Goo PlateProperty(GPC.Model.ElementProperties.PlateProperty property) => Goo("GH_PlateProperty", property);
    internal static IGH_Goo MaterialGoo(GPC.Model.Materials.Material material, string kind = null) => Goo("GH_Material", material, kind);
    internal static IGH_Goo Case(LegacyCaseData loadCase) => Goo("GH_LoadCase", loadCase);
    internal static IGH_Goo Composite(GPC.Checkers.CompositeBridge.BridgeCompositeSectionDefinition definition) => Goo("GH_CompositeBeamProperty", definition);

    /// <summary>The GPC datum of a GH value (its <c>Gpc</c>), null when it has none.</summary>
    internal static object Datum(object goo) => goo?.GetType().GetProperty("Gpc")?.GetValue(goo);
    internal static T Datum<T>(object goo) where T : class => Datum(goo) as T;

    /// <summary>Whether a GH value has a FEM value (its <c>Value</c>): never for the data of these tests.</summary>
    internal static bool HasFem(object goo) => goo?.GetType().GetProperty("Value")?.GetValue(goo) != null;

    internal static T Get<T>(object goo, string property) => (T)goo.GetType().GetProperty(property).GetValue(goo);

    // ---------------------------------------------------------------------------------------------------------------------------------------
    // Components

    /// <summary>Sets the persistent data of a generic input (branch {0}).</summary>
    internal static void Input(GH_Component component, int index, params IGH_Goo[] values)
    {
        var p = (Param_GenericObject)component.Params.Input[index]; p.PersistentData.Clear();
        foreach (var value in values) p.PersistentData.Append(value, new GH_Path(0));
        p.ExpireSolution(false);
    }

    internal static IGH_Goo[] Outputs(GH_Component component, int index = 0) => component.Params.Output[index].VolatileData.AllData(true).ToArray();

    /// <summary>The values of a non-GH output (booleans, numbers, texts).</summary>
    internal static T[] Values<T>(GH_Component component, int index) => Outputs(component, index).Select(g => (T)g.ScriptVariable()).ToArray();

    /// <summary>Every output value of the given GH kind has its GPC datum and no FEM value (the path of P2).</summary>
    internal static bool GpcOnly(IEnumerable<IGH_Goo> values) => values.All(g => g != null && Datum(g) != null && !HasFem(g));

    /// <summary>The switches of the GPC-only producers of the plug-in (<c>GpcOnlyElements.GeneratorsWithoutFem</c>,
    /// <c>PropertiesWithoutFem</c>: the path that stays in P2) on for the scope, restored after.</summary>
    internal static IDisposable WithoutFem(bool generators = true, bool properties = true) => new Switches(generators, properties);

    private sealed class Switches : IDisposable
    {
        private readonly PropertyInfo generators, properties;
        private readonly bool oldGenerators, oldProperties;
        internal Switches(bool g, bool p)
        {
            var type = Plugin.GetType("BridGHe.Grasshopper.Helpers.GpcOnlyElements", true);
            generators = type.GetProperty("GeneratorsWithoutFem", BindingFlags.Public | BindingFlags.Static);
            properties = type.GetProperty("PropertiesWithoutFem", BindingFlags.Public | BindingFlags.Static);
            oldGenerators = (bool)generators.GetValue(null); oldProperties = (bool)properties.GetValue(null);
            if (g) generators.SetValue(null, true);
            if (p) properties.SetValue(null, true);
        }
        public void Dispose() { generators.SetValue(null, oldGenerators); properties.SetValue(null, oldProperties); }
    }
}
#endif
