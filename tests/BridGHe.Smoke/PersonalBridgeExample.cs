using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using BridGHe.Common.Models;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Parameters;
using Grasshopper.Kernel.Special;

internal static class PersonalBridgeExample
{
    private static readonly string Root = Path.GetFullPath("example/99-Ponte completo GPC");
    private static readonly string Original = Path.Combine(Root, "flusso-acciaio-misto_REV001.ghx");
    private static readonly string Artifacts = Path.GetFullPath("artifacts/example99");

    internal static int Run(string mode)
    {
        try
        {
            Directory.CreateDirectory(Artifacts);
            Console.WriteLine("EXAMPLE 99: registering components");
            GH_Document.EnableSolutions = false;
            var plugin = BridgeMergeTests.Initialize();
            var server = Grasshopper.Instances.ComponentServer;
            foreach (string library in new[] { "MathComponents", "CurveComponents", "SurfaceComponents", "VectorComponents" })
            {
                var standard = Assembly.LoadFrom(@"C:\Program Files\Rhino 8\Plug-ins\Grasshopper\Components\" + library + ".gha");
                typeof(GH_ComponentServer).GetMethod("ParseGHA", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                    null, new[] { typeof(Assembly), typeof(string) }, null).Invoke(server, new object[] { standard, standard.Location });
            }
            var registered = server.ObjectProxies.Select(p => p.Guid).ToHashSet();
            var xml = System.Xml.Linq.XDocument.Load(Original);
            var ids = xml.Descendants("chunk").Single(c => (string)c.Attribute("name") == "DefinitionObjects").Element("chunks").Elements("chunk")
                .Select(c => c.Element("items").Elements("item").Single(i => (string)i.Attribute("name") == "GUID").Value).Select(Guid.Parse);
            var missing = ids.Where(id => !registered.Contains(id)).Distinct().ToArray();
            if (missing.Length > 0) throw new InvalidOperationException("Unregistered object GUIDs: " + string.Join(", ", missing));
            string source=mode.StartsWith("replay")||mode.StartsWith("finalize")?Path.Combine(Root,"REV"+mode.Substring(mode.Length-3),"flusso-acciaio-misto_REV"+mode.Substring(mode.Length-3)+".ghx"):Original;
            Console.WriteLine("EXAMPLE 99: reading " + Path.GetFileName(source));
            using var doc = GrasshopperBridgeExample.Read(source); doc.FilePath = source;
            if(mode.StartsWith("replay")||mode.StartsWith("finalize"))return new PersonalBridgeRevision(doc,plugin,Root).Replay(mode.StartsWith("finalize"));
            if (mode is "rev002" or "rev003") return new PersonalBridgeRevision(doc, plugin, Root).Build(mode);
            if (mode != "inspect") throw new ArgumentException("Unknown example 99 mode: " + mode);
            foreach (var toggle in doc.Objects.OfType<GH_BooleanToggle>()) toggle.Value = false;
            foreach (var c in doc.Objects.OfType<GH_Component>().Where(c => c.GetType().Name.Contains("PlotComponent")))
                foreach (var p in c.Params.Input.OfType<Param_String>().Where(p => p.Name is "File" or "Output Path"))
                { p.RemoveAllSources(); p.PersistentData.Clear(); }
            Console.WriteLine("EXAMPLE 99: solving with native analysis disabled");
            GH_Document.EnableSolutions = true; doc.Enabled = true; doc.NewSolution(false, GH_SolutionMode.Silent);
            Console.WriteLine("EXAMPLE 99: collecting values");
            var data = doc.Objects.OfType<GH_ActiveObject>().Select(o => new
            {
                id = o.InstanceGuid, type = o.GetType().Name, name = o.Name, nickname = o.NickName,
                x = o.Attributes.Pivot.X, y = o.Attributes.Pivot.Y,
                errors = o.RuntimeMessages(GH_RuntimeMessageLevel.Error), warnings = o.RuntimeMessages(GH_RuntimeMessageLevel.Warning),
                inputs = o is GH_Component c ? c.Params.Input.Select((p, i) => new { i, p.Name, p.Access, p.Optional,
                    sources = p.Sources.Select(s => s.Attributes.GetTopLevel.DocObject.InstanceGuid),
                    count = p.VolatileDataCount, values = p.VolatileData.AllData(true).Take(5).Select(g => Describe(g?.ScriptVariable())) }).ToArray() : null,
                outputs = o is GH_Component comp ? comp.Params.Output.Select((p, i) => new { i, p.Name, count = p.VolatileDataCount,
                    values = p.VolatileData.AllData(true).Take(3).Select(g => Describe(g?.ScriptVariable())) }).ToArray() : null,
                value = o is IGH_Param param ? param.VolatileData.AllData(true).Take(6).Select(g => Describe(g?.ScriptVariable())).ToArray() : null
            }).ToArray();
            File.WriteAllText(Path.Combine(Artifacts, "live-inventory.json"), JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true }));
            foreach (var o in data.Where(o => o.errors.Count > 0 || o.warnings.Count > 0))
                Console.WriteLine(o.id + " " + o.name + ": " + string.Join("; ", o.errors.Concat(o.warnings)));
            Console.WriteLine("EXAMPLE 99 INSPECTED: " + doc.Objects.Count + " objects");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); Console.Error.Flush(); return 1; }
    }

    private static object Describe(object value)
    {
        if (value is ModelModel model) return new { type = "Model", model.Name, nodes = model.Nodes.Count, beams = model.Beams.Count,
            cases = model.LoadCases.Select(c => c.Name), combos = model.LoadCombinations.Select(c => c.Name),
            results = model.Beams.Sum(b => b.Results.Count), units = model.Units.ToString() };
        if (value is BeamModel beam) return new { type = "Beam", beam.BeamId, beam.Length, results = beam.Results.Count };
        var text = value?.ToString(); return text?.Length > 350 ? text.Substring(0, 350) : text;
    }
}
