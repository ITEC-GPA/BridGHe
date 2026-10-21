using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Xml.Linq;
using Grasshopper.Kernel;

internal static class ExampleLayoutTests
{
    internal static int Run(string selection = null, bool refreshContracts = false, bool solvePortable = false)
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
        // Older bridge graphs contain the standard List Item component. Without MathComponents,
        // GH opens a modal missing-component dialog inside the hidden Rhino host.
        var libraries = new[] { "MathComponents", "CurveComponents", "SurfaceComponents", "VectorComponents" }
            .Select(name=>Assembly.LoadFrom(@"C:\Program Files\Rhino 8\Plug-ins\Grasshopper\Components\"+name+".gha"));
        foreach (var a in new[] { typeof(GH_Document).Assembly, assembly }.Concat(libraries)) parse.Invoke(server, new object[] { a, a.Location });
        var registered = server.ObjectProxies.Select(p => p.Guid).ToHashSet();
        using var catalog = JsonDocument.Parse(File.ReadAllText(Path.Combine(ExamplePaths.Root, "catalog.json")));
        int documents = 0, objects = 0, wires = 0;
        var files = catalog.RootElement.EnumerateArray()
            .Where(e => selection == null || e.GetProperty("id").GetString().StartsWith(selection + "-", StringComparison.Ordinal))
            .Where(e => !solvePortable || e.GetProperty("number").GetInt32() is >= 23 and <= 45)
            .SelectMany(Definitions).Select(f => Path.Combine(ExamplePaths.Root, f)).ToArray();
        if (files.Length == 0) throw new ArgumentException("No current GHX definition for selection " + selection);
        var stale = new List<string>();
        foreach (var file in files)
        {
            var xml = XDocument.Load(file);
            var objectChunks = xml.Descendants("chunk").Single(c => (string)c.Attribute("name") == "DefinitionObjects").Element("chunks").Elements("chunk");
            foreach (var chunk in objectChunks)
            {
                var id = chunk.Element("items")?.Elements("item").SingleOrDefault(i => (string)i.Attribute("name") == "GUID")?.Value;
                if (id != null && !registered.Contains(Guid.Parse(id))) throw new InvalidOperationException("Unregistered GH object " + id + " in " + file);
            }
            int count = int.Parse(xml.Descendants("chunk").Single(c => (string)c.Attribute("name") == "DefinitionObjects")
                .Element("items").Elements("item").Single(i => (string)i.Attribute("name") == "ObjectCount").Value);
            using var doc = GrasshopperBridgeExample.Read(file);
            doc.Enabled = false;
            if (doc.Objects.Count != count) throw new InvalidOperationException("Lost objects while reopening " + file);
            if (doc.Objects.Select(o => o.InstanceGuid).Distinct().Count() != count)
                throw new InvalidOperationException("Duplicate object identity in " + file);
            // Counted objects alone do not detect dropped connections or stale saved port layouts.
            var parameters = doc.Objects.OfType<IGH_Param>().Concat(doc.Objects.OfType<GH_Component>().SelectMany(c => c.Params.Input.Concat(c.Params.Output)))
                .ToDictionary(p => p.InstanceGuid);
            foreach (var saved in objectChunks.SelectMany(c => c.Descendants("chunk")).Where(c => c.Element("items")?.Elements("item").Any(i => (string)i.Attribute("name") == "SourceCount") == true))
            {
                Guid id = Guid.Parse(Value(saved, "InstanceGuid"));
                var sources = saved.Element("items").Elements("item").Where(i => (string)i.Attribute("name") == "Source")
                    .OrderBy(i => (int?)i.Attribute("index") ?? 0).Select(i => Guid.Parse(i.Value)).ToArray();
                if (sources.Length != int.Parse(Value(saved, "SourceCount")) || !parameters.TryGetValue(id, out var target)
                    || !target.Sources.Select(p => p.InstanceGuid).SequenceEqual(sources))
                    throw new InvalidOperationException("Lost or reordered connections for parameter " + id + " in " + file);
                wires += sources.Length;
            }
            var drift = new List<string>();
            foreach (var chunk in objectChunks)
            {
                var container = chunk.Element("chunks")?.Elements("chunk").SingleOrDefault(c => (string)c.Attribute("name") == "Container");
                if (container == null || !Guid.TryParse(Value(container, "InstanceGuid"), out var id)) continue;
                if (doc.Objects.Single(o => o.InstanceGuid == id) is not GH_Component component || component.GetType().Assembly != assembly) continue;
                var fresh = (GH_Component)Activator.CreateInstance(component.GetType());
                foreach (var (key, current, expected) in new[] { ("param_input", component.Params.Input, fresh.Params.Input), ("param_output", component.Params.Output, fresh.Params.Output) })
                {
                    var saved = container.Element("chunks").Elements("chunk").Where(c => (string)c.Attribute("name") == key).OrderBy(c => (int)c.Attribute("index")).ToArray();
                    if (current.Count != expected.Count || current.Where((p, i) => p.Name != expected[i].Name || p.Access != expected[i].Access || p.Optional != expected[i].Optional).Any())
                        throw new InvalidOperationException("Live parameter contract differs from a fresh component: " + component.Name + " in " + file);
                    if (saved.Length != expected.Count || saved.Where((p, i) => Value(p, "Name") != expected[i].Name
                        || (Value(p, "Access") ?? "0") != ((int)expected[i].Access).ToString()
                        || (Value(p, "Optional") == "true") != expected[i].Optional).Any())
                        drift.Add(component.Name + " / " + key);
                }
            }
            if (drift.Count > 0)
            {
                if (refreshContracts) { GrasshopperBridgeExample.Write(doc, file); Console.WriteLine("REFRESHED " + Path.GetFileName(file) + ": " + string.Join(", ", drift)); }
                else stale.Add(Path.GetFileName(file) + ": " + string.Join(", ", drift));
            }
            if (solvePortable) SolvePortable(doc, file);
            documents++; objects += count;
            Console.WriteLine("REOPENED " + Path.GetFileName(file) + ": " + count + " objects");
        }
        if (stale.Count > 0) throw new InvalidOperationException("Stale saved port contracts:\n" + string.Join("\n", stale));
        Console.WriteLine($"EXAMPLE REOPEN PASS: {documents} documents, {objects} objects, {wires} preserved connections; current port contracts; solved={solvePortable}; no solver invoked.");
        return 0;
    }
    private static void SolvePortable(GH_Document doc, string file)
    {
        var components = doc.Objects.OfType<GH_Component>().ToArray();
        if (components.Any(c => c.GetType().Name.Contains("Straus", StringComparison.Ordinal) || c.GetType().Name is "MovingStaticAnalysisComponent" or "BridgeCombinationExcelComponent" or "BridgeReportComponent"))
            throw new InvalidOperationException("Portable replay cannot execute external I/O components: " + file);
        foreach (var build in components.Where(c => c.GetType().Name == "BuildBridgeModel"))
            build.GetType().GetField("_run", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(build, true);
        GH_Document.EnableSolutions = true; doc.Enabled = true; doc.NewSolution(false, GH_SolutionMode.Silent);
        var errors = doc.Objects.OfType<GH_ActiveObject>().SelectMany(c => c.RuntimeMessages(GH_RuntimeMessageLevel.Error).Select(e => c.Name + ": " + e)).ToArray();
        if (errors.Length > 0) throw new InvalidOperationException(file + "\n" + string.Join("\n", errors));
        if (components.Any(c => c.Params.Output.Count > 0 && c.Params.Output.All(p => p.VolatileDataCount == 0)))
            throw new InvalidOperationException("A component produced no output in " + file);
        Console.WriteLine("SAVED EXAMPLE SOLVED: " + Path.GetFileName(file));
        doc.Enabled = false; GH_Document.EnableSolutions = false;
    }
    private static string Value(XElement chunk, string name) => chunk.Element("items")?.Elements("item").SingleOrDefault(i => (string)i.Attribute("name") == name)?.Value;
    private static IEnumerable<string> Definitions(JsonElement entry)
    {
        if (entry.GetProperty("definition").ValueKind != JsonValueKind.Null) yield return entry.GetProperty("definition").GetString();
        if (entry.TryGetProperty("additionalDefinitions", out var others)) foreach (var file in others.EnumerateArray()) yield return file.GetString();
    }
}
