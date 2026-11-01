using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using BridGHe.Common.Diagnostics;

/// <summary>The load-time check of the GPC libraries: a bin older than lib/GPC and a set loaded by another plugin.</summary>
internal static class GpcReleaseSetTests
{
    private const string Manifest = "lib/GPC/release-set.json";

    internal static void Run()
    {
        var set = GpcReleaseSet.Read(File.ReadAllText(Manifest));
        string Version(string file) => set.Single(e => e.File == file).Version;
        Require(set.Length >= 7 && set.All(e => e.File.EndsWith(".dll")) && set.Any(e => e.File == "GPCModel.dll"), "the manifest lists the DLLs of the set");

        // This process runs with the copies of lib/GPC in its own output.
        _ = typeof(GPC.Checkers.CompositeBridge.HBridgeSection).Assembly; _ = typeof(GPC.Model.Materials.ConcreteMaterialEN1992).Assembly;
        var loaded = AppDomain.CurrentDomain.GetAssemblies().Where(a => !a.IsDynamic).Select(GpcReleaseSet.Describe).ToArray();
        Require(loaded.Count(a => GpcReleaseSet.IsGpc(a.Name)) >= 2 && GpcReleaseSet.ForeignLoaded(set, loaded, GpcReleaseSet.Sha256) == null, "loaded copies of lib/GPC match the set");
        var model = loaded.First(a => a.Name == "GPCModel");
        string message = GpcReleaseSet.ForeignLoaded(set, new[] { (model.Name, "5.0.0.0", model.Path) }, GpcReleaseSet.Sha256);
        Require(message != null && message.Contains($"GPCModel 5.0.0.0 invece di {Version("GPCModel.dll")}") && message.Contains(model.Path), "another loaded version is named with its file");
        Require(GpcReleaseSet.ForeignLoaded(set, new[] { model }, _ => "00")?.Contains("GPCModel " + model.Version + " con contenuto diverso") == true, "same version with other content");
        Require(GpcReleaseSet.ForeignLoaded(set, new[] { (model.Name, model.Version, (string)null) }, GpcReleaseSet.Sha256)?.Contains("non verificabile") == true, "a copy without location is not taken as verified");
        Require(GpcReleaseSet.ForeignLoaded(set, new[] { ("Other", "1.0.0.0", model.Path) }, GpcReleaseSet.Sha256) == null, "assemblies outside the set are ignored");

        string root = Path.Combine(Path.GetTempPath(), "bridghe-gpc-set-" + Guid.NewGuid().ToString("N"));
        try
        {
            string plugin = Path.Combine(root, "bin", "Release");
            Copy(Path.GetDirectoryName(BuildPaths.GrasshopperAssembly), plugin);
            Directory.CreateDirectory(Path.Combine(root, "lib", "GPC"));
            File.WriteAllText(Path.Combine(root, "BridGHe.sln"), "");
            File.Copy(Manifest, Path.Combine(root, "lib", "GPC", "release-set.json"));
            Require(GpcReleaseSet.RepositoryManifest(plugin) == Path.Combine(root, "lib", "GPC", "release-set.json") && GpcReleaseSet.RepositoryManifest(Path.GetTempPath()) == null,
                "the checkout is found above bin/Release and nowhere else");
            Require(GpcReleaseSet.StaleBin(plugin, set, GpcReleaseSet.Sha256) == null, "a bin built from lib/GPC is current");
            var newer = set.Select(e => e.File == "GPCModel.dll" ? e with { Version = "9.9.9.9", Sha256 = "00" } : e).ToArray();
            message = GpcReleaseSet.StaleBin(plugin, newer, GpcReleaseSet.Sha256);
            Require(message != null && message.Contains($"GPCModel {Version("GPCModel.dll")} invece di 9.9.9.9") && message.Contains("dotnet build BridGHe.sln -c Release"), "a bin older than lib/GPC names the library and the rebuild");
            message = GpcReleaseSet.StaleBin(plugin, set.Select(e => e.File == "GPCModel.dll" ? e with { Sha256 = "00" } : e), GpcReleaseSet.Sha256);
            Require(message?.Contains($"GPCModel {Version("GPCModel.dll")} con contenuto diverso") == true, "a rebuilt library with the same version is still stale");

            // The real guard in a new process (a copy of the plugin cannot load next to the original): lib/GPC newer than the bin
            // for GPCModel, and a CompositeBridge loaded after the start that is not the one of the shipped set.
            Rewrite(Manifest, Path.Combine(root, "lib", "GPC", "release-set.json"), "GPCModel.dll");
            Rewrite(Manifest, Path.Combine(plugin, GpcReleaseSet.ShippedManifest), "GPCChecker.CompositeBridge.dll");
            var start = new ProcessStartInfo("dotnet") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = Environment.CurrentDirectory };
            start.ArgumentList.Add(typeof(GpcReleaseSetTests).Assembly.Location); start.ArgumentList.Add("--gpc-guard"); start.ArgumentList.Add(plugin);
            using (var process = Process.Start(start))
            {
                var output = process.StandardOutput.ReadToEndAsync(); var error = process.StandardError.ReadToEndAsync();
                if (!process.WaitForExit(300000)) { process.Kill(true); throw new TimeoutException("GPC guard process"); }
                string text = output.Result;
                string warning = text.Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.StartsWith("GPC GUARD WARNING: "));
                Require(process.ExitCode == 0 && warning != null, "the guard process runs: " + text + error.Result);
                Require(warning.Contains("Bin non aggiornato") && warning.Contains($"GPCModel {Version("GPCModel.dll")} invece di 9.9.9.9"), "the plugin reports a bin older than lib/GPC at load");
                Require(warning.Contains($"GPCChecker.CompositeBridge {Version("GPCChecker.CompositeBridge.dll")} invece di 9.9.9.9"), "a GPC library loaded later from another set is reported when it loads");
                Require(text.Contains("GPC GUARD COMPONENT WARNINGS 1"), "the warning appears once on Super Element Check");
            }
        }
        finally { try { Directory.Delete(root, true); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { } }

        // The published plugin: built from lib/GPC and running with the same libraries.
        System.Reflection.Assembly.LoadFrom(@"C:\Program Files\Rhino 8\Plug-ins\Grasshopper\Grasshopper.dll");
        var guard = System.Reflection.Assembly.LoadFrom(BuildPaths.GrasshopperAssembly).GetType("BridGHe.Grasshopper.Helpers.GpcReleaseGuard", true);
        guard.GetMethod("Start").Invoke(null, null);
        Require(guard.GetProperty("Warning").GetValue(null) == null, "no warning for the published plugin: " + guard.GetProperty("Warning").GetValue(null));
        Console.WriteLine("GPC RELEASE SET PASS: stale bin, foreign set, late loads, single component warning, published plugin.");
    }

    /// <summary>--gpc-guard: starts the guard of the plugin in the folder, as Grasshopper does, and prints what a component shows.</summary>
    internal static int Guard(string folder)
    {
        System.Reflection.Assembly.LoadFrom(@"C:\Program Files\Rhino 8\Plug-ins\Grasshopper\Grasshopper.dll");
        System.Reflection.Assembly.LoadFrom(@"C:\Program Files\Rhino 8\Plug-ins\Grasshopper\GH_IO.dll");
        var plugin = System.Reflection.Assembly.LoadFrom(Path.Combine(folder, "BridGHe.Grasshopper.gha"));
        var guard = plugin.GetType("BridGHe.Grasshopper.Helpers.GpcReleaseGuard", true);
        guard.GetMethod("Start").Invoke(null, null);
        _ = typeof(GPC.Checkers.CompositeBridge.HBridgeSection).Assembly;
        Console.WriteLine("GPC GUARD WARNING: " + (guard.GetProperty("Warning").GetValue(null) ?? "none"));
        var component = (Grasshopper.Kernel.GH_Component)Activator.CreateInstance(plugin.GetType("BridGHe.Grasshopper.Components.BridgeChecks.SuperElementCheckComponent", true));
        var warn = guard.GetMethod("Warn");
        warn.Invoke(null, new object[] { component }); warn.Invoke(null, new object[] { component });
        Console.WriteLine("GPC GUARD COMPONENT WARNINGS " + component.RuntimeMessages(Grasshopper.Kernel.GH_RuntimeMessageLevel.Warning).Count);
        return 0;
    }

    private static void Rewrite(string source, string target, string file)
    {
        var json = JsonNode.Parse(File.ReadAllText(source));
        var entry = json["files"].AsArray().Single(e => (string)e["file"] == file);
        entry["assemblyVersion"] = "9.9.9.9"; entry["sha256"] = "00";
        File.WriteAllText(target, json.ToJsonString());
    }

    private static void Copy(string source, string target)
    {
        Directory.CreateDirectory(target);
        foreach (string file in Directory.GetFiles(source)) File.Copy(file, Path.Combine(target, Path.GetFileName(file)));
        foreach (string folder in Directory.GetDirectories(source)) Copy(folder, Path.Combine(target, Path.GetFileName(folder)));
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("GPC RELEASE SET: " + message);
    }
}
