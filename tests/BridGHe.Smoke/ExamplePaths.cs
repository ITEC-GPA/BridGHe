using System;
using System.IO;
using System.Text.RegularExpressions;

/// <summary>One numbered folder per example, including its generated calculations.</summary>
internal static class ExamplePaths
{
    /// <summary>P2R (T5): the <c>example</c> folder of the checkout the test host was built in (the folder of <c>BridGHe.sln</c> above the host,
    /// or <c>BRIDGHE_EXAMPLE_ROOT</c> when set), not of the working directory: a host started from another folder (the main checkout, a
    /// lane) never opens, redirects or writes the examples of that folder. Without a <c>BridGHe.sln</c> above the host: the working directory, as before.</summary>
    internal static readonly string Root = ResolveRoot();

    private static string ResolveRoot()
    {
        string explicitRoot = Environment.GetEnvironmentVariable("BRIDGHE_EXAMPLE_ROOT");
        if (!string.IsNullOrWhiteSpace(explicitRoot)) return Path.GetFullPath(explicitRoot);
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "BridGHe.sln"))) return Path.Combine(dir.FullName, "example");
        return Path.GetFullPath("example");
    }
    internal static readonly string Documentation = Path.Combine(Root, "00-documentazione");
    internal static readonly string Validation = Path.Combine(Documentation, "validazioni");
    internal static readonly string Packages = Path.Combine(Documentation, "archivio-pacchetti");

    internal static string Case(string id)
    {
        if (id == null || !Regex.IsMatch(id, @"^\d{2}-[a-z0-9-]+$"))
            throw new ArgumentException("Expected a numbered example folder.", nameof(id));
        return Path.Combine(Root, id);
    }

    internal static string Calculations(string id) => Path.Combine(Case(id), "calcoli");

    internal static string SuiteCalculation(string name)
    {
        var parts = name.Replace('\\', '/').Split('/', 2);
        if (parts.Length == 2)
            return Path.Combine(Calculations(parts[0]), parts[1]);
        if (name is "benchmark-shrink-free" or "benchmark-shrink-fixed")
            return Path.Combine(Calculations("22-fasi-sezione-gpc"), name);
        throw new ArgumentException("Unknown suite calculation: " + name);
    }
}
