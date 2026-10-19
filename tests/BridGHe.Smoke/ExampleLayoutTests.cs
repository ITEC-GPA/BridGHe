using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Xml.Linq;
using Grasshopper.Kernel;

internal static class ExampleLayoutTests
{
    internal static int Run()
    {
        // Deserialization only: never solve a graph or acquire a Straus licence.
        GH_Document.EnableSolutions = false;
        var assembly = Assembly.LoadFrom(BuildPaths.GrasshopperAssembly);
        var field = typeof(Grasshopper.Instances).GetField("m_comServer", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new NotSupportedException("Rhino component server changed.");
        if (field.GetValue(null) != null) throw new InvalidOperationException("Use a fresh process.");
        var server = new GH_ComponentServer();
        field.SetValue(null, server);
        var parse = typeof(GH_ComponentServer).GetMethod("ParseGHA", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance,
            null, new[] { typeof(Assembly), typeof(string) }, null) ?? throw new NotSupportedException("Rhino loader changed.");
        foreach (var a in new[] { typeof(GH_Document).Assembly, assembly }) parse.Invoke(server, new object[] { a, a.Location });
        using var catalog = JsonDocument.Parse(File.ReadAllText(Path.Combine(ExamplePaths.Root, "catalog.json")));
        int documents = 0, objects = 0;
        foreach (var entry in catalog.RootElement.EnumerateArray())
        {
            if (entry.GetProperty("definition").ValueKind == JsonValueKind.Null) continue;
            string file = Path.Combine(ExamplePaths.Root, entry.GetProperty("definition").GetString());
            var xml = XDocument.Load(file);
            int count = int.Parse(xml.Descendants("chunk").Single(c => (string)c.Attribute("name") == "DefinitionObjects")
                .Element("items").Elements("item").Single(i => (string)i.Attribute("name") == "ObjectCount").Value);
            using var doc = GrasshopperBridgeExample.Read(file);
            doc.Enabled = false;
            if (doc.Objects.Count != count) throw new InvalidOperationException("Lost objects while reopening " + file);
            if (doc.Objects.Select(o => o.InstanceGuid).Distinct().Count() != count)
                throw new InvalidOperationException("Duplicate object identity in " + file);
            documents++; objects += count;
            Console.WriteLine("REOPENED " + entry.GetProperty("id").GetString() + ": " + count + " objects");
        }
        if (documents != 20) throw new InvalidOperationException("Expected 20 current definitions.");
        Console.WriteLine($"EXAMPLE REOPEN PASS: {documents} documents, {objects} objects; no solver invoked.");
        return 0;
    }
}
