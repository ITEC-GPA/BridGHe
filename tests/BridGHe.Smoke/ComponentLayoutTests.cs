using System;
using System.Linq;
using System.Reflection;
using Grasshopper.Kernel;

internal static class ComponentLayoutTests
{
    internal static void Run()
    {
        var assembly = Assembly.LoadFrom(BuildPaths.GrasshopperAssembly);
        var objects = assembly.GetTypes().Where(t => !t.IsAbstract &&
            (typeof(GH_Component).IsAssignableFrom(t) || typeof(IGH_Param).IsAssignableFrom(t)))
            .Select(t => (IGH_DocumentObject)Activator.CreateInstance(t)).ToArray();
        void Require(bool value, string message) { if (!value) throw new Exception(message); }
        Require(objects.All(o => o.Category == "BridGHe"), "All components and parameters must share the BridGHe tab.");
        Require(objects.All(o => o.SubCategory != "09-Patterning" && o.SubCategory != "Straus Sections" &&
            o.SubCategory != "Moving Loads" && o.SubCategory != "Attributes"), "Obsolete panels must not reappear.");
        Require(objects.Where(o => o.GetType().Namespace.EndsWith(".Sections")).All(o => o.SubCategory == "03-Sections"),
            "Every gPC/native Straus section must be in 03-Sections.");
        Require(objects.Where(o => o.GetType().Name.Contains("Moving")).All(o => o.SubCategory == "09-Loads"),
            "All moving-load components must be in 09-Loads.");
        Require(objects.Single(o => o.GetType().Name == "BeamSupportComponent").SubCategory == "07-Attributes",
            "Beam Support belongs to 07-Attributes.");
        Require(objects.Count(o => o.Name == "Composite Beam Property") == 1, "Only one Composite Beam Property is exposed.");
        Require(!objects.Any(o => o.GetType().Name is "SectionGeometricComponent" or "RemeshComponent" or "CompositeSectionComponent_OBSOLETE"),
            "Removed components must not be registered.");
    }
}
