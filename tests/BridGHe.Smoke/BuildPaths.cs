using System;
using System.IO;

internal static class BuildPaths
{
    // Follow the running test's configuration/TFM (and optional RID) within root bin.
    internal static string GrasshopperAssembly
    {
        get
        {
            var projectOutput = new DirectoryInfo(AppContext.BaseDirectory);
            while (projectOutput != null && projectOutput.Name != "BridGHe.Smoke")
                projectOutput = projectOutput.Parent;
            if (projectOutput?.Parent == null)
                throw new InvalidOperationException("Eseguire i test dalla cartella bin/BridGHe.Smoke del progetto.");

            string configurationPath = Path.GetRelativePath(projectOutput.FullName, AppContext.BaseDirectory);
            string assembly = Path.Combine(projectOutput.Parent.FullName, "BridGHe.Grasshopper",
                configurationPath, "BridGHe.Grasshopper.gha");
            if (!File.Exists(assembly))
                throw new FileNotFoundException("Compilare il plugin nella stessa configurazione dei test.", assembly);
            return assembly;
        }
    }
}
