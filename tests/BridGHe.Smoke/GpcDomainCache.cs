using System;
using System.Globalization;
using System.Reflection;

/// <summary>
/// Opt-in control of the process-wide gPC failure-domain cache (GPCChecker.Concrete with FailureDomainCache), to separate its effect
/// in timing tests: BRIDGHE_GPC_DOMAIN_CACHE_CAPACITY=0 disables it. Sets without the cache are reported, not failed, unless asked.
/// </summary>
internal static class GpcDomainCache
{
    private const BindingFlags Static = BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;
    private static Type CacheType => Type.GetType("GPC.Checkers.Concrete.SectionSolvers.FailureDomainCache, GPCChecker.Concrete", false);

    internal static void Configure()
    {
        string value = Environment.GetEnvironmentVariable("BRIDGHE_GPC_DOMAIN_CACHE_CAPACITY");
        if (string.IsNullOrEmpty(value)) return;
        var type = CacheType ?? throw new InvalidOperationException("BRIDGHE_GPC_DOMAIN_CACHE_CAPACITY is set, but this gPC set has no FailureDomainCache.");
        type.GetProperty("Capacity", Static).SetValue(null, int.Parse(value, CultureInfo.InvariantCulture));
        Console.WriteLine("GPC DOMAIN CACHE: capacity " + value);
    }

    /// <summary>Counters of the cache in this process; null when the loaded gPC set has none.</summary>
    internal static object Statistics()
    {
        var type = CacheType; if (type == null) return null;
        object Get(string name) => type.GetProperty(name, Static)?.GetValue(null);
        return new { Capacity = Get("Capacity"), Count = Get("Count"), Hits = Get("Hits"), Misses = Get("Misses"), Computations = Get("Computations"), MeshBuilds = Get("MeshBuilds") };
    }
}
