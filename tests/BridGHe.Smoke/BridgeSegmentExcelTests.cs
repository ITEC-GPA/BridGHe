using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Xml.Linq;
using BridGHe.Common.Diagnostics;
using BridGHe.Common.Geometry;
using BridGHe.Common.Models;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Parameters;
using Grasshopper.Kernel.Types;

internal static class BridgeSegmentExcelTests
{
    private static readonly XNamespace S = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static int checks;
    private static void Check(bool condition, string message)
    { if (!condition) throw new Exception("SEGMENT EXCEL: " + message); checks++; }
    private static void Reject(Action action)
    { try { action(); } catch (Exception e) when (e is ArgumentException or IOException) { checks++; return; } throw new Exception("Expected invalid workbook to be rejected."); }
    private static void Edit(string file, Action<XDocument> edit)
    {
        using var zip = ZipFile.Open(file, ZipArchiveMode.Update); var part = zip.GetEntry("xl/worksheets/sheet1.xml");
        XDocument xml; using (var source = part.Open()) xml = XDocument.Load(source);
        edit(xml); part.Delete(); using var target = zip.CreateEntry("xl/worksheets/sheet1.xml").Open(); xml.Save(target);
    }
    private static XElement Cell(XDocument xml, string address) => xml.Descendants(S + "c").Single(c => (string)c.Attribute("r") == address);
    private static void Value(XElement cell, string value, bool text = false)
    {
        cell.RemoveNodes(); cell.Attribute("t")?.Remove();
        if (text) { cell.SetAttributeValue("t", "inlineStr"); cell.Add(new XElement(S + "is", new XElement(S + "t", value))); }
        else cell.Add(new XElement(S + "v", value));
    }
    internal static void Run()
    {
        checks = 0;
        var input = SimpleBridgeTests.Input() with { Segments = new BridgeSegments { MaximumLength = 12000 } };
        var geometry = SimpleBridgeBuilder.Build(input); var property = BridgeSegmentSectionsTests.BaseProperty();
        string folder = Path.GetFullPath("artifacts/segment-excel/tests-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder);
        string template = Path.Combine(folder, "sections.xlsx");
        BridgeSegmentWorkbook.Write(template, geometry);
        var initial = BridgeSegmentWorkbook.Read(template, geometry);
        Check(initial.Table.SequenceEqual(BridgeSegmentSections.Synchronize(geometry, null).Table), "geometry-only template and wildcard round trip");
        var original = File.ReadAllBytes(template);
        Reject(() => BridgeSegmentWorkbook.Write(template, geometry));
        using (var held = File.Open(template, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) Reject(() => BridgeSegmentWorkbook.Write(template, geometry, overwrite:true));
        Check(File.ReadAllBytes(template).SequenceEqual(original), "existing and locked files preserved");
        string filled = Path.Combine(folder, "filled.xlsx"); BridgeSegmentWorkbook.Write(filled, geometry, property);
        Edit(filled, x => Value(Cell(x, "G7"), "50"));
        var edited = BridgeSegmentWorkbook.Read(filled, geometry).Table;
        var sections = BridgeSegmentSections.Create(geometry, property, edited);
        Check(sections.Rules[0].Property.T1 == 50 && sections.Rules.Skip(1).All(r => r.Property.T1 == 30), "edited bottom flange affects only the keyed concio");
        double area = 600 * 50 + 600 * 30 + 16 * (2500 - 50 - 30);
        Check(Math.Abs(sections.Rules[0].Property.SectionArea - area) < 1e-6, "imported section agrees with independent analytical area");
        var mesh = SimpleBridgeBuilder.Build(input with { MaximumElementLength = 1000 });
        Check(BridgeSegmentWorkbook.Read(filled, mesh).Table.SequenceEqual(edited), "FEM mesh refinement does not change section assignments");
        var changed = SimpleBridgeBuilder.Build(input with { Segments = new BridgeSegments { MaximumLength = 10000 } });
        Reject(() => BridgeSegmentWorkbook.Read(filled, changed));
        int ordinal = 0;
        string Fixture(Action<XDocument> edit) { string p = Path.Combine(folder, "case-" + ++ordinal + ".xlsx"); File.Copy(filled, p); Edit(p, edit); return p; }
        var sorted = Fixture(x => {
            var a = x.Descendants(S + "row").Single(r => (int)r.Attribute("r") == 7);
            var b = x.Descendants(S + "row").Single(r => (int)r.Attribute("r") == 8);
            var ac = a.Elements(S + "c").Select(c => new XElement(c)).ToArray(); var bc = b.Elements(S + "c").Select(c => new XElement(c)).ToArray();
            foreach(var c in ac) c.SetAttributeValue("r", ((string)c.Attribute("r"))[0] + "8");
            foreach(var c in bc) c.SetAttributeValue("r", ((string)c.Attribute("r"))[0] + "7");
            a.ReplaceNodes(bc); b.ReplaceNodes(ac);
        });
        Check(BridgeSegmentWorkbook.Read(sorted, geometry).Table.SequenceEqual(edited), "Excel row sorting preserves identity and dimensions");
        foreach (var (address, value, text) in new[] { ("A8", "G1-C1", true), ("B7", "1", false), ("G7", "", true), ("G7", "0", false), ("G7", "NaN", true), ("D6", "renamed", true) })
        { string p = Fixture(x => Value(Cell(x, address), value, text)); Reject(() => BridgeSegmentWorkbook.Read(p, geometry)); }
        Reject(() => BridgeSegmentWorkbook.Read(Fixture(x => x.Descendants(S + "row").Single(r => (int)r.Attribute("r") == 7).Remove()), geometry));
        string formula = Fixture(x => { var c = Cell(x, "G7"); Value(c, "50"); c.AddFirst(new XElement(S + "f", "25*2")); });
        var calculated = BridgeSegmentWorkbook.Read(formula, geometry);
        Check(calculated.Table.SequenceEqual(edited) && calculated.Warnings.Length == 1, "saved formula results accepted with recalculation warning");
        Reject(() => BridgeSegmentWorkbook.Read(Fixture(x => { var c = Cell(x, "G7"); c.RemoveNodes(); c.Add(new XElement(S + "f", "25*2")); }), geometry));
        Component(folder, geometry, property, filled, edited);
        Console.WriteLine($"SEGMENT EXCEL PASS: {checks} checks; count, workbook edits, analytical area, sorting, mesh independence, stale geometry, invalid inputs, formula caches, file protection and GH round trip.");
    }
    internal static void Press(GH_Component component)
    {
        for(var type=component.GetType();type!=null;type=type.BaseType)
        {var field=type.GetField("run",BindingFlags.NonPublic|BindingFlags.Instance|BindingFlags.DeclaredOnly);if(field!=null){field.SetValue(component,true);return;}}
        throw new ArgumentException("Component has no file-operation button: "+component.Name);
    }
    internal static void Solve(GH_Document doc, bool allowErrors = false)
    {
        GH_Document.EnableSolutions = true; doc.Enabled = true; doc.NewSolution(false, GH_SolutionMode.Silent);
        var errors = doc.Objects.OfType<GH_ActiveObject>().SelectMany(o => o.RuntimeMessages(GH_RuntimeMessageLevel.Error).Select(m => o.NickName + ": " + m)).ToArray();
        if (!allowErrors && errors.Length > 0) throw new Exception(string.Join("\n", errors));
    }
    internal static GH_Component Add(GH_Document doc, string type)
    {
        var plugin = BridgeMergeTests.Initialize();
        var component = (GH_Component)Activator.CreateInstance(plugin.GetTypes().Single(t => t.Name == type));
        component.CreateAttributes(); doc.AddObject(component, false); return component;
    }
    internal static void Generic(GH_Component c, int index, object value)
    {
        string type = value is SimpleBridgeGeometry ? "BridgeGeometryGoo" : value is BeamPropertyModel ? "GH_BeamProperty" : throw new ArgumentException("Unsupported fixture input.");
        var goo = (IGH_Goo)Activator.CreateInstance(BridgeMergeTests.Initialize().GetTypes().Single(t => t.Name == type), value);
        var p = (Param_GenericObject)c.Params.Input[index]; p.PersistentData.Clear(); p.PersistentData.Append(goo);
    }
    internal static void Text(GH_Component c, int index, string value)
    { var p = (Param_String)c.Params.Input[index]; p.PersistentData.Clear(); p.SetPersistentData(value); }
    internal static void Mode(GH_Component c, int value)
    { var p = (Param_Integer)c.Params.Input[2]; p.PersistentData.Clear(); p.SetPersistentData(value); }
    private static void Component(string folder, SimpleBridgeGeometry geometry, BeamPropertyModel property, string file, string[] expected)
    {
        using var doc = new GH_Document(); doc.FilePath = Path.Combine(folder, "workflow.ghx");
        var excel = Add(doc, "BridgeSegmentExcelComponent"); var sections = Add(doc, "BridgeSegmentSectionsComponent");
        Generic(excel, 0, geometry); Generic(sections, 0, geometry); Generic(sections, 1, property);
        Text(excel, 1, Path.GetFileName(file)); Mode(excel, 1); sections.Params.Input[2].AddSource(excel.Params.Output[4]);
        Solve(doc);
        Check(excel.Params.Output[0].VolatileData.AllData(true).Single().ScriptVariable().Equals(geometry.Segments.Count), "count available before reading Excel and before section assignment");
        Check(sections.Params.Output[1].VolatileDataCount == 0, "unread Excel must not silently assign base sections");
        Press(excel); excel.ExpireSolution(false); Solve(doc);
        Check(excel.Params.Output[4].VolatileData.AllData(true).Select(g => g.ScriptVariable().ToString()).SequenceEqual(expected), "real GH component reads complete table");
        Check(sections.Params.Output[1].VolatileDataCount == geometry.Segments.Count, "all imported sections reach downstream component");
        GrasshopperBridgeExample.Write(doc, doc.FilePath);
        using var restored = GrasshopperBridgeExample.Read(doc.FilePath); restored.FilePath = doc.FilePath;
        // Cached imported values must survive reopening without touching Excel.
        using(var held = File.Open(file, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) Solve(restored);
        var copy = restored.Objects.OfType<GH_Component>().Single(c => c.GetType().Name == "BridgeSegmentExcelComponent");
        Check(copy.Params.Output[4].VolatileDataCount == expected.Length, "saved imported table survives reopening without file access");
        Text(copy, 1, "missing.xlsx"); Press(copy); copy.ExpireSolution(false); Solve(restored, true);
        Check(copy.Params.Output[4].VolatileDataCount == 0 && restored.Objects.OfType<GH_Component>().Single(c => c.GetType().Name == "BridgeSegmentSectionsComponent").Params.Output[1].VolatileDataCount == 0,
            "failed read clears old table and stops downstream base fallback");
        Mode(copy, 0); Text(copy, 1, "created.xlsx"); copy.ExpireSolution(false); Solve(restored);
        Check(!File.Exists(Path.Combine(folder, "created.xlsx")), "no file written on ordinary solve");
        Press(copy); copy.ExpireSolution(false); Solve(restored);
        Check(File.Exists(Path.Combine(folder, "created.xlsx")), "explicit Create writes workbook");
    }
}
