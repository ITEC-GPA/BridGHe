using System;
using System.IO;

internal static class BuildPaths
{
    // Release tests load the actual post-build distribution; Debug uses private outputs.
    internal static string GrasshopperAssembly
    {
        get
        {
            var projectOutput = new DirectoryInfo(AppContext.BaseDirectory);
            while (projectOutput != null && projectOutput.Name != "BridGHe.Smoke")
                projectOutput = projectOutput.Parent;
            if (projectOutput?.Parent == null)
                throw new InvalidOperationException("Eseguire i test dalla cartella obj/bin/BridGHe.Smoke del progetto.");

            string configurationPath = Path.GetRelativePath(projectOutput.FullName, AppContext.BaseDirectory);
            string assembly = configurationPath.Split(Path.DirectorySeparatorChar)[0] == "Release"
                ? Path.Combine(projectOutput.Parent.Parent.Parent.FullName, "bin", "Release", "BridGHe.Grasshopper.gha")
                : Path.Combine(projectOutput.Parent.FullName, "BridGHe.Grasshopper",
                    configurationPath, "BridGHe.Grasshopper.gha");
            if (!File.Exists(assembly))
                throw new FileNotFoundException("Compilare il plugin nella stessa configurazione dei test.", assembly);
            return assembly;
        }
    }
}
