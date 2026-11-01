using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;

namespace BridGHe.Common.Diagnostics;

/// <summary>One GPC library of a release set.</summary>
public sealed record GpcReleaseEntry(string File, string Version, string Sha256);

/// <summary>
/// Compares the GPC libraries BridGHe runs with against its release set. Two failures change the engine without any
/// visible sign: a bin not rebuilt after lib/GPC changed in the checkout Rhino loads the plugin from, and another plugin
/// that loads a different GPC set first in the same Rhino (plugins share the first loaded copy of each assembly).
/// </summary>
public static class GpcReleaseSet
{
    /// <summary>The manifest of lib/GPC as the post-build publishes it next to the plugin.</summary>
    public const string ShippedManifest = "gpc-release-set.json";

    public static bool IsGpc(string assemblyName) =>
        assemblyName != null && (assemblyName.StartsWith("GPC", StringComparison.Ordinal) || assemblyName == "DelaunayMesh");

    /// <summary>The DLLs of a release-set.json written by Checker/build/Release-Set.ps1.</summary>
    public static GpcReleaseEntry[] Read(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.GetProperty("files").EnumerateArray()
            .Select(e => new GpcReleaseEntry(e.GetProperty("file").GetString(), e.GetProperty("assemblyVersion").GetString(), e.GetProperty("sha256").GetString()))
            .Where(e => e.File.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)).ToArray();
    }

    /// <summary>lib/GPC/release-set.json of the BridGHe checkout that contains the plugin folder, or null for an installed plugin.</summary>
    public static string RepositoryManifest(string pluginFolder)
    {
        // bin\Release is two levels below the root, the private outputs obj\bin\<project>\<configuration>\<framework> five.
        var folder = string.IsNullOrEmpty(pluginFolder) ? null : new DirectoryInfo(pluginFolder);
        for (int level = 0; folder != null && level <= 6; level++, folder = folder.Parent)
        {
            string manifest = Path.Combine(folder.FullName, "lib", "GPC", "release-set.json");
            if (File.Exists(Path.Combine(folder.FullName, "BridGHe.sln")) && File.Exists(manifest)) return manifest;
        }
        return null;
    }

    /// <summary>The message for GPC libraries next to the plugin that are not those of lib/GPC (bin not rebuilt), or null.</summary>
    public static string StaleBin(string pluginFolder, IEnumerable<GpcReleaseEntry> repository, Func<string, string> hash)
    {
        var differences = new List<string>();
        foreach (var entry in repository)
        {
            string path = Path.Combine(pluginFolder, entry.File);
            if (!File.Exists(path) || string.Equals(hash(path), entry.Sha256, StringComparison.OrdinalIgnoreCase)) continue;
            string version = null;
            try { version = AssemblyName.GetAssemblyName(path).Version?.ToString(); }
            catch (Exception e) when (e is BadImageFormatException or IOException) { }
            string name = Path.GetFileNameWithoutExtension(entry.File);
            differences.Add(version == entry.Version ? $"{name} {version} con contenuto diverso" : $"{name} {version ?? "?"} invece di {entry.Version}");
        }
        return differences.Count == 0 ? null : $"Bin non aggiornato: le librerie GPC in {pluginFolder} non sono quelle di lib/GPC ({string.Join("; ", differences)}). " +
            "Chiudere Rhino e ricompilare BridGHe (dotnet build BridGHe.sln -c Release): risultati e tempi sono quelli delle librerie vecchie.";
    }

    /// <summary>The message for loaded copies of the set's assemblies that differ from it, or null; assemblies not loaded yet are ignored.</summary>
    public static string ForeignLoaded(IEnumerable<GpcReleaseEntry> shipped, IEnumerable<(string Name, string Version, string Path)> loaded, Func<string, string> hash)
    {
        var assemblies = loaded.ToArray();
        var differences = new List<string>();
        foreach (var entry in shipped)
        {
            string name = Path.GetFileNameWithoutExtension(entry.File);
            foreach (var copy in assemblies.Where(a => string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase)))
            {
                string where = string.IsNullOrEmpty(copy.Path) ? "posizione sconosciuta" : copy.Path;
                if (copy.Version != entry.Version) { differences.Add($"{name} {copy.Version} invece di {entry.Version}, da {where}"); continue; }
                string actual = null;
                try { if (!string.IsNullOrEmpty(copy.Path)) actual = hash(copy.Path); }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
                if (actual == null) differences.Add($"{name} {copy.Version} non verificabile, da {where}");
                else if (!string.Equals(actual, entry.Sha256, StringComparison.OrdinalIgnoreCase)) differences.Add($"{name} {copy.Version} con contenuto diverso, da {where}");
            }
        }
        return differences.Count == 0 ? null : "Librerie GPC caricate diverse dal set di BridGHe, probabilmente caricate prima da un altro plugin " +
            $"({string.Join("; ", differences)}). Allineare i plugin allo stesso set GPC.";
    }

    public static (string Name, string Version, string Path) Describe(Assembly assembly)
    {
        var name = assembly.GetName();
        string location = null;
        try { location = assembly.Location; } catch (NotSupportedException) { }
        return (name.Name, name.Version?.ToString(), location);
    }

    public static string Sha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }
}
