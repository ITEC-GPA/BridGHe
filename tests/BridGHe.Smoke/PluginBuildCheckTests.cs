using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Gpc.PluginChecks;
using Grasshopper.Kernel;

/// <summary>The load-time check shared by the gPC plug-ins (src/BridGHe.Grasshopper/Helpers/PluginBuildCheck.cs, linked here).</summary>
internal static class PluginBuildCheckTests
{
    private const string A = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", B = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb", C = "cccccccccccccccccccccccccccccccccccccccc";

    internal static void Run()
    {
        Require(PluginBuildCheck.CommitFromVersion("0.2.0+4B947C8F00D8010DD2E93BE83AE2656D64D4FD25") == "4b947c8f00d8010dd2e93be83ae2656d64d4fd25", "SourceLink suffix of the informational version");
        Require(PluginBuildCheck.CommitFromVersion("1.1.0") == null && PluginBuildCheck.CommitFromVersion("1.0+abc") == null && PluginBuildCheck.CommitFromVersion(null) == null, "versions without a commit");

        string root = Path.Combine(Path.GetTempPath(), "bridghe-plugin-check-" + Guid.NewGuid().ToString("N"));
        try
        {
            string repository = Path.Combine(root, "repo"), bin = Path.Combine(repository, "bin", "Release");
            Directory.CreateDirectory(bin);
            Write(Path.Combine(repository, ".git", "HEAD"), "ref: refs/heads/master\n");
            Write(Path.Combine(repository, ".git", "refs", "heads", "master"), A + "\n");
            Require(PluginBuildCheck.FindRepository(bin, out string head, out string branch) == repository && head == A && branch == "master", "branch read from a loose reference");
            File.Delete(Path.Combine(repository, ".git", "refs", "heads", "master"));
            Write(Path.Combine(repository, ".git", "packed-refs"), "# pack-refs with: peeled fully-peeled sorted\n" + C + " refs/heads/other\n" + B + " refs/heads/master\n");
            Require(PluginBuildCheck.FindRepository(bin, out head, out branch) == repository && head == B && branch == "master", "branch read from packed-refs");
            Write(Path.Combine(repository, ".git", "HEAD"), C.ToUpperInvariant() + "\n");
            Require(PluginBuildCheck.FindRepository(bin, out head, out branch) == repository && head == C && branch == null, "detached HEAD");

            // A worktree of the repository and a submodule inside it: their .git files point to the git directories.
            string worktree = Path.Combine(root, "worktree");
            Write(Path.Combine(worktree, ".git"), "gitdir: ../repo/.git/worktrees/worktree\n");
            Write(Path.Combine(repository, ".git", "worktrees", "worktree", "HEAD"), "ref: refs/heads/feature\n");
            Write(Path.Combine(repository, ".git", "worktrees", "worktree", "commondir"), "../..\n");
            Write(Path.Combine(repository, ".git", "refs", "heads", "feature"), A + "\n");
            Require(PluginBuildCheck.FindRepository(Path.Combine(worktree, "bin"), out head, out branch) == worktree && head == A && branch == "feature", "worktree: HEAD in its git directory, branch in the common one");
            string submodule = Path.Combine(repository, "Sub");
            Write(Path.Combine(submodule, ".git"), "gitdir: ../.git/modules/Sub\n");
            Write(Path.Combine(repository, ".git", "modules", "Sub", "HEAD"), "ref: refs/heads/main\n");
            Write(Path.Combine(repository, ".git", "modules", "Sub", "refs", "heads", "main"), B + "\n");
            Require(PluginBuildCheck.FindRepository(Path.Combine(submodule, "bin"), out head, out branch) == submodule && head == B && branch == "main", "a submodule is its own checkout");
            string outside = Path.Combine(root, "installed");
            Directory.CreateDirectory(outside);
            Require(PluginBuildCheck.FindRepository(outside, out head, out branch) == null && head == null, "an installed plug-in has no checkout");

            Require(PluginBuildCheck.StaleBin("BridGHe", A, repository, A.ToUpperInvariant(), "master") == null && PluginBuildCheck.StaleBin("BridGHe", null, repository, A, null) == null, "current bin, or nothing to compare");
            string stale = PluginBuildCheck.StaleBin("BridGHe", A, repository, B, "master");
            Require(stale != null && stale.Contains("compilato dal commit aaaaaaaa") && stale.Contains("al commit bbbbbbbb (master)") && stale.Contains("ricompilare BridGHe"), "a bin built from another commit names both commits");

            // Shared libraries: this process runs with the GPC copies of its own output.
            var model = PluginBuildCheck.Describe(typeof(GPC.Model.Materials.ConcreteMaterialEN1992).Assembly);
            string libraries = Path.Combine(root, "libraries");
            Directory.CreateDirectory(libraries);
            File.Copy(model.Item3, Path.Combine(libraries, "GPCModel.dll"));
            File.Copy(model.Item3, Path.Combine(libraries, "MathNet.Numerics.dll"));
            var loaded = new[] { model, Tuple.Create("MathNet.Numerics", "5.0.0.0", model.Item3), Tuple.Create("GPCGeometry", "2.3.0.0", model.Item3) };
            Require(PluginBuildCheck.ForeignLibraries("Arachne", libraries, loaded, PluginBuildCheck.Sha256) == null, "the same copy, libraries not shipped and third-party libraries are accepted");
            string own = Path.Combine(libraries, "GPCModel.dll");
            string message = PluginBuildCheck.ForeignLibraries("Arachne", libraries, loaded, path => path == own ? "00" : PluginBuildCheck.Sha256(path));
            Require(message != null && message.Contains("GPCModel " + model.Item2 + " da " + model.Item3 + ", con contenuto diverso") && message.Contains("di Arachne"), "same version with other content");
            File.Copy(typeof(GPC.Utilities.Units.UnitsConvert).Assembly.Location, own, true);
            message = PluginBuildCheck.ForeignLibraries("Arachne", libraries, loaded, PluginBuildCheck.Sha256);
            Require(message != null && message.Contains("GPCModel " + model.Item2 + " da " + model.Item3 + " invece di " + typeof(GPC.Utilities.Units.UnitsConvert).Assembly.GetName().Version), "another loaded version");
            Require(PluginBuildCheck.ForeignLibraries("Arachne", libraries, new[] { Tuple.Create("GPCModel", "5.33.0.0", own) }, PluginBuildCheck.Sha256) == null, "the plug-in's own copy");

            Components();
            Guard(root);
        }
        finally { try { Directory.Delete(root, true); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { } }
        Console.WriteLine("PLUGIN BUILD CHECK PASS: commits, git layouts, shared libraries, component warnings, published plugin and stale checkout.");
    }

    /// <summary>The warning on the plug-in's components of a real document, once, and again after a new solution.</summary>
    private static void Components()
    {
        System.Reflection.Assembly.LoadFrom(@"C:\Program Files\Rhino 8\Plug-ins\Grasshopper\Grasshopper.dll");
        System.Reflection.Assembly.LoadFrom(@"C:\Program Files\Rhino 8\Plug-ins\Grasshopper\GH_IO.dll");
        var plugin = System.Reflection.Assembly.LoadFrom(BuildPaths.GrasshopperAssembly);
        using var document = new GH_Document();
        var studs = (GH_Component)Activator.CreateInstance(plugin.GetType("BridGHe.Grasshopper.Components.BridgeDetails.BridgeStudsComponent", true));
        var probe = new Probe();
        studs.CreateAttributes(); probe.CreateAttributes();
        document.AddObject(studs, false); document.AddObject(probe, false);
        GH_Document.EnableSolutions = true; document.Enabled = true;
        document.NewSolution(false, GH_SolutionMode.Silent);
        int Count(GH_Component component, string text) => component.RuntimeMessages(GH_RuntimeMessageLevel.Warning).Count(m => m == text);
        PluginBuildCheck.ShowOn(document, plugin, "W"); PluginBuildCheck.ShowOn(document, plugin, "W");
        Require(Count(studs, "W") == 1 && Count(probe, "W") == 0, "the warning appears once, only on the plug-in's components");
        PluginBuildCheck.Attach(document, plugin, () => "V");
        studs.ExpireSolution(false); document.NewSolution(false, GH_SolutionMode.Silent);
        Require(Count(studs, "V") == 1 && Count(studs, "W") == 0, "after a new solution the current warning is added again");

        // The copy inside the published plugin, as its assembly priority starts it: no warning for a bin built from HEAD.
        var check = plugin.GetType("Gpc.PluginChecks.PluginBuildCheck", true);
        check.GetMethod("Start").Invoke(null, new object[] { plugin, "BridGHe", false });
        Require(check.GetProperty("Warning").GetValue(null) == null, "no warning for the published plugin: " + check.GetProperty("Warning").GetValue(null));
        Require(PluginBuildCheck.BuildCommit(plugin) != null, "the published plugin records its commit");
    }

    /// <summary>The published plugin copied into a checkout whose HEAD moved after the build, started by its assembly priority in a new process.</summary>
    private static void Guard(string root)
    {
        string checkout = Path.Combine(root, "checkout"), plugin = Path.Combine(checkout, "bin", "Release");
        Copy(Path.GetDirectoryName(BuildPaths.GrasshopperAssembly), plugin);
        Write(Path.Combine(checkout, ".git", "HEAD"), "ref: refs/heads/master\n");
        Write(Path.Combine(checkout, ".git", "refs", "heads", "master"), C + "\n");
        var start = new ProcessStartInfo("dotnet") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = Environment.CurrentDirectory };
        start.ArgumentList.Add(typeof(PluginBuildCheckTests).Assembly.Location); start.ArgumentList.Add("--plugin-check"); start.ArgumentList.Add(plugin);
        using var process = Process.Start(start);
        var output = process.StandardOutput.ReadToEndAsync(); var error = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(300000)) { process.Kill(true); throw new TimeoutException("plugin check process"); }
        string text = output.Result;
        string built = PluginBuildCheck.BuildCommit(System.Reflection.Assembly.LoadFrom(BuildPaths.GrasshopperAssembly)).Substring(0, 8);
        string warning = text.Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.StartsWith("PLUGIN CHECK WARNING: "));
        Require(process.ExitCode == 0 && warning != null, "the plugin check process runs: " + text + error.Result);
        Require(warning.Contains("Bin non aggiornato") && warning.Contains("compilato dal commit " + built) && warning.Contains("al commit cccccccc (master)"), "the plugin reports a checkout that moved after the build");
        Require(text.Contains("PLUGIN CHECK COMPONENT WARNINGS 1"), "the warning reaches a component of a document after its solution");
    }

    /// <summary>--plugin-check: loads the plugin in the folder as Grasshopper does and solves a document with one of its components.</summary>
    internal static int Child(string folder)
    {
        System.Reflection.Assembly.LoadFrom(@"C:\Program Files\Rhino 8\Plug-ins\Grasshopper\Grasshopper.dll");
        System.Reflection.Assembly.LoadFrom(@"C:\Program Files\Rhino 8\Plug-ins\Grasshopper\GH_IO.dll");
        var plugin = System.Reflection.Assembly.LoadFrom(Path.Combine(folder, "BridGHe.Grasshopper.gha"));
        // The two checks as BridGHeAssemblyPriority starts them; its category registration would make the component
        // server load every plug-in of the user's Grasshopper folders, which never returns without a user interface.
        plugin.GetType("BridGHe.Grasshopper.Helpers.GpcReleaseGuard", true).GetMethod("Start").Invoke(null, null);
        var check = plugin.GetType("Gpc.PluginChecks.PluginBuildCheck", true);
        check.GetMethod("Start").Invoke(null, new object[] { plugin, "BridGHe", false });
        string warning = (string)check.GetProperty("Warning").GetValue(null);
        Console.WriteLine("PLUGIN CHECK WARNING: " + (warning ?? "none"));
        var document = new GH_Document();
        Grasshopper.Instances.DocumentServer.AddDocument(document);
        var studs = (GH_Component)Activator.CreateInstance(plugin.GetType("BridGHe.Grasshopper.Components.BridgeDetails.BridgeStudsComponent", true));
        studs.CreateAttributes(); document.AddObject(studs, false);
        GH_Document.EnableSolutions = true; document.Enabled = true;
        document.NewSolution(false, GH_SolutionMode.Silent);
        Console.WriteLine("PLUGIN CHECK COMPONENT WARNINGS " + studs.RuntimeMessages(GH_RuntimeMessageLevel.Warning).Count(m => warning != null && m == warning));
        return 0;
    }

    private sealed class Probe : GH_Component
    {
        public Probe() : base("Probe", "Probe", "Component of another assembly", "Test", "Test") { }
        public override Guid ComponentGuid => new Guid("5d7e8a3c-61f4-4f1b-9a52-0d2c3b6e4a71");
        protected override void RegisterInputParams(GH_InputParamManager p) { }
        protected override void RegisterOutputParams(GH_OutputParamManager p) { }
        protected override void SolveInstance(IGH_DataAccess da) { }
    }

    private static void Write(string path, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        File.WriteAllText(path, text);
    }

    private static void Copy(string source, string target)
    {
        Directory.CreateDirectory(target);
        foreach (string file in Directory.GetFiles(source)) File.Copy(file, Path.Combine(target, Path.GetFileName(file)));
        foreach (string folder in Directory.GetDirectories(source)) Copy(folder, Path.Combine(target, Path.GetFileName(folder)));
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("PLUGIN BUILD CHECK: " + message);
    }
}
