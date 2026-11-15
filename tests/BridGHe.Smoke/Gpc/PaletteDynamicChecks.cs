using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using GH_IO.Serialization;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Parameters;
using Grasshopper.Kernel.Types;

/// <summary>
/// Task 145, PAL-127-01..03: runtime and restored ports, outside the frozen initial palette.
/// Expected values below are handwritten from the cited sources at 59f8768.
/// No fixture generation, screenshots or solver/native calculations.
/// </summary>
internal static class PaletteDynamicChecks
{
    private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private sealed record Port(string Name, string NickName, string Type, GH_ParamAccess Access, bool Optional);

    // Components/ElementProperties/BeamPropertyComponent.cs:79-117,153-161 @59f8768.
    // Every port is required and item-access; these literals do not come from the component or palette JSON.
    private static readonly Port[] BeamPorts =
    {
        new("Name", "N", "Param_String", GH_ParamAccess.item, false),
        new("Type", "T", "Param_Integer", GH_ParamAccess.item, false),
        new("Section", "S", "Param_GenericObject", GH_ParamAccess.item, false),
        new("Material", "M", "Param_GenericObject", GH_ParamAccess.item, false),
        new("Max tension", "MT", "Param_Number", GH_ParamAccess.item, false),
        new("Max compression", "MC", "Param_Number", GH_ParamAccess.item, false),
        new("Cut off type", "T", "Param_Integer", GH_ParamAccess.item, false),
        new("Keep mass", "KM", "Param_Boolean", GH_ParamAccess.item, false)
    };

    internal static void Regression()
    {
        var failures = new List<string>();
        int assertions = 0;
        void Equal(string at, object expected, object actual)
        {
            assertions++;
            if (!Equals(expected, actual)) failures.Add(at + ": expected [" + expected + "], found [" + actual + "]");
        }
        void Ports(GH_Component component, int count, string at)
        {
            Equal(at + " count", count, component.Params.Input.Count);
            for (int i = 0; i < Math.Min(count, component.Params.Input.Count); i++)
            {
                var expected = BeamPorts[i]; var actual = component.Params.Input[i]; string label = at + " input[" + i + "]";
                Equal(label + " Name", expected.Name, actual.Name);
                Equal(label + " NickName", expected.NickName, actual.NickName);
                Equal(label + " type", expected.Type, actual.GetType().Name);
                Equal(label + " access", expected.Access, actual.Access);
                Equal(label + " Optional", expected.Optional, actual.Optional);
            }
        }
        void CutoffChoices(GH_Component component, string at)
        {
            if (component.Params.Input.Count <= 6 || component.Params.Input[6] is not Param_Integer integer)
            { Equal(at + " integer port available", true, false); return; }
            // Same read-only reflection already used by ParametricBridgeExample.cs:133-135.
            // BeamPropertyComponent.cs:28 is the independent literal oracle, including values and order.
            var field = typeof(Param_Integer).GetField("m_namedValues", Instance)
                ?? throw new Exception("PAL-DYNAMIC: this Grasshopper host does not expose m_namedValues.");
            var choices = ((IEnumerable)field.GetValue(integer)).Cast<object>().Select(value =>
                ((string)value.GetType().GetField("Name").GetValue(value),
                 (int)value.GetType().GetField("Value").GetValue(value))).ToArray();
            Equal(at + " named count", 2, choices.Length);
            if (choices.Length >= 2)
            {
                Equal(at + " choice[0] name", "Brittle", choices[0].Item1);
                Equal(at + " choice[0] value", 0, choices[0].Item2);
                Equal(at + " choice[1] name", "Ductile", choices[1].Item1);
                Equal(at + " choice[1] value", 1, choices[1].Item2);
            }
        }
        void Nicknames(GH_Component component, string[] expected, string at)
        {
            Equal(at + " count", expected.Length, component.Params.Input.Count);
            for (int i = 0; i < Math.Min(expected.Length, component.Params.Input.Count); i++)
                Equal(at + " input[" + i + "]", expected[i], component.Params.Input[i].NickName);
        }

        var beam = New("BeamPropertyComponent");
        Ports(beam, 4, "initial Beam Property");
        SetBeamType(beam, 3);
        Ports(beam, 8, "Type3"); CutoffChoices(beam, "Type3");
        // An additional expiry must not duplicate the dynamic ports.
        beam.ExpireSolution(false); Ports(beam, 8, "Type3 repeated expiry");
        if (beam.Params.Input.Count == 8)
        {
            ((Param_Number)beam.Params.Input[4]).SetPersistentData(new GH_Number(7.25));
            ((Param_Number)beam.Params.Input[5]).SetPersistentData(new GH_Number(-3.5));
            ((Param_Integer)beam.Params.Input[6]).SetPersistentData(new GH_Integer(1));
            ((Param_Boolean)beam.Params.Input[7]).SetPersistentData(new GH_Boolean(true));
            // BeamPropertyComponent.cs:227-256: fresh 4-port instance must read all 8 archived ports.
            var restored = RoundTrip(beam);
            Ports(restored, 8, "Type3 archive"); CutoffChoices(restored, "Type3 archive");
            if (restored.Params.Input.Count == 8)
            {
                Equal("archive Type", 3, ((Param_Integer)restored.Params.Input[1]).PersistentData.AllData(true).OfType<GH_Integer>().Single().Value);
                Equal("archive Max tension", 7.25, ((Param_Number)restored.Params.Input[4]).PersistentData.AllData(true).OfType<GH_Number>().Single().Value);
                Equal("archive Max compression", -3.5, ((Param_Number)restored.Params.Input[5]).PersistentData.AllData(true).OfType<GH_Number>().Single().Value);
                Equal("archive Cut off type", 1, ((Param_Integer)restored.Params.Input[6]).PersistentData.AllData(true).OfType<GH_Integer>().Single().Value);
                Equal("archive Keep mass", true, ((Param_Boolean)restored.Params.Input[7]).PersistentData.AllData(true).OfType<GH_Boolean>().Single().Value);
                SetBeamType(restored, 5); Ports(restored, 4, "restored Type5");
                Ports(RoundTrip(restored), 4, "Type5 archive");
            }
        }
        SetBeamType(beam, 5); Ports(beam, 4, "Type5");
        SetBeamType(beam, 3); Ports(beam, 8, "Type3 again"); CutoffChoices(beam, "Type3 again");

        // A real list containing both types is a different canvas input: any Cutoff Bar needs its ports.
        var mixedType = (Param_Integer)beam.Params.Input[1];
        mixedType.SetPersistentData(new GH_Integer(5));
        mixedType.ExpireSolution(false); beam.ExpireSolution(false);
        Equal("mixed Type values", "3,5", string.Join(",", mixedType.PersistentData.AllData(true).OfType<GH_Integer>().Select(v => v.Value)));
        Ports(beam, 8, "mixed Type3 and Type5");
        Ports(RoundTrip(beam), 8, "mixed Type archive");
        SetBeamType(beam, 5); Ports(beam, 4, "mixed list replaced with Type5");

        // BeamPointLoadComponent.cs:33-42,50-64,115-119 and BeamPointMomentComponent.cs:33-44,52-66,112-116.
        foreach (var (type, prefix) in new[] { ("BeamPointLoadComponent", "P"), ("BeamPointMomentComponent", "M") })
        {
            var component = New(type);
            string[] local = { "B", "LC", "CS", prefix + "1", prefix + "2", prefix + "3", "a" };
            string[] global = { "B", "LC", "CS", prefix + "X", prefix + "Y", prefix + "Z", "a" };
            Nicknames(component, local, type + " local");
            var axes = component.Params.Input[2];
            // Same persistent-data command as the Global menu, CoordinateSystemParam.cs:49.
            var setGlobal = axes.GetType().GetMethod("SetGlobal", Instance)
                ?? throw new Exception("PAL-DYNAMIC: SetGlobal unavailable.");
            setGlobal.Invoke(axes, null);
            Nicknames(component, global, type + " Global event");
            Nicknames(RoundTrip(component), global, type + " Global archive");
            var data = axes.GetType().GetProperty("PersistentData").GetValue(axes);
            data.GetType().GetMethod("Clear", Type.EmptyTypes).Invoke(data, null);
            // Notify the same persistent-data event observed by the component; no private naming method is called.
            var changed = axes.GetType().GetMethod("OnObjectChanged", Instance, null, new[] { typeof(GH_ObjectEventType) }, null)
                ?? throw new Exception("PAL-DYNAMIC: OnObjectChanged(event type) unavailable.");
            changed.Invoke(axes, new object[] { GH_ObjectEventType.PersistentData });
            Nicknames(component, local, type + " Local event");
            Nicknames(RoundTrip(component), local, type + " Local archive");
        }

        // MaterialCustomComponent.cs:31-54,85-87,169-182; DefinitionUnits.IPS=3, Nmm=2.
        // Exercise the normal document solution and hard-coded UI values, not UpdateInputDescriptions by reflection.
        using (var document = new GH_Document())
        {
            var material = BridgeSegmentExcelTests.Add(document, "MaterialCustomComponent");
            P2ceReader.Set(material.Params.Input[0], "Palette units");
            P2ceReader.Set(material.Params.Input[1], new GH_Integer(0));
            foreach (int unit in new[] { 3, 2, 3 })
            {
                P2ceReader.Set(material.Params.Input[2], new GH_Integer(unit));
                BridgeSegmentExcelTests.Solve(document, true);
                string[] psi = { "N", "K", "U", "Modulus (psi)", "\u03bd", "Thermal Expansion (1/F)",
                    "Shear Modulus (psi)", "\u03bd?", "Density (lb/in\u00b3)", "Yielding (psi)", "Strength for Steel (psi)" };
                string[] nmm = { "N", "K", "U", "Modulus (MPa)", "\u03bd", "Thermal Expansion (1/C)",
                    "Shear Modulus (MPa)", "\u03bd?", "Density (ton/mm\u00b3)", "Yielding (MPa)", "Strength for Steel (MPa)" };
                Nicknames(material, unit == 3 ? psi : nmm, "Custom Material units=" + unit);
            }
        }

        // BridgeResultToolComponents.cs:78,83-94: emulate the stored old optional flag/tooltip.
        // Seed wrong values before Write, so a constructor-only test cannot satisfy this restoration check.
        var summary = New("BridgeCheckSummaryComponent");
        summary.Params.Input[1].Optional = false;
        summary.Params.Input[1].Description = "Old required labels";
        summary.PrincipalParameterIndex = 1;
        var recovered = RoundTrip(summary);
        Equal("Labels archive input count", 2, recovered.Params.Input.Count);
        Equal("Labels archive name", "Labels", recovered.Params.Input[1].Name);
        Equal("Labels archive nickname", "ID", recovered.Params.Input[1].NickName);
        Equal("Labels archive type", "Param_String", recovered.Params.Input[1].GetType().Name);
        Equal("Labels archive access", GH_ParamAccess.list, recovered.Params.Input[1].Access);
        Equal("Labels archive Optional", true, recovered.Params.Input[1].Optional);
        Equal("Labels archive description",
            "Optional custom label per result. Leave empty for automatic beam/local station/combination labels. Results without FEM provenance use their tree position and phase.",
            recovered.Params.Input[1].Description);
        Equal("Labels archive principal input", 0, recovered.PrincipalParameterIndex);

        foreach (string failure in failures) Console.WriteLine("FAIL PAL-DYNAMIC: " + failure);
        if (failures.Count != 0) throw new Exception("PAL-DYNAMIC FAIL: " + failures.Count + " differences (listed above).");
        Console.WriteLine("PAL-DYNAMIC PASS: " + assertions + " assertions; initial frozen palette unchanged.");
    }

    private static GH_Component New(string name)
    {
        var type = R2aDesignFixtures.Plugin.GetExportedTypes().Single(t => t.Name == name);
        var component = (GH_Component)Activator.CreateInstance(type);
        component.CreateAttributes(); return component;
    }
    private static void SetBeamType(GH_Component component, int type)
    {
        // Task 176: the single-item overload adds a value. Replace the old selection explicitly,
        // as editing a single canvas value does; retaining [3,5] correctly keeps the cutoff ports.
        var parameter = (Param_Integer)component.Params.Input[1];
        parameter.PersistentData.Clear();
        parameter.SetPersistentData(new GH_Integer(type));
        if (parameter.PersistentData.DataCount != 1 || parameter.PersistentData.AllData(true).OfType<GH_Integer>().Single().Value != type)
            throw new Exception("PAL-DYNAMIC: SetBeamType did not replace the single Type value.");
        parameter.ExpireSolution(false);
        component.ExpireSolution(false);
    }
    private static GH_Component RoundTrip(GH_Component source)
    {
        var writer = new GH_LooseChunk("Component");
        if (!source.Write(writer)) throw new Exception("PAL-DYNAMIC: Write failed for " + source.Name);
        var reader = new GH_LooseChunk("Component"); reader.Deserialize_Binary(writer.Serialize_Binary());
        var target = New(source.GetType().Name);
        if (!target.Read(reader)) throw new Exception("PAL-DYNAMIC: Read failed for " + source.Name);
        return target;
    }
}
