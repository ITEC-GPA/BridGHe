using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BridGHe.Common.Diagnostics;
using Grasshopper.Kernel;

namespace BridGHe.Grasshopper.Helpers
{
    /// <summary>
    /// Load-time check of the GPC libraries against the release set of BridGHe (see GpcReleaseSet). Mismatches are written
    /// to the Rhino command line and shown as a warning on the components whose results come from GPC. Libraries loaded
    /// later are checked when they load.
    /// </summary>
    public static class GpcReleaseGuard
    {
        private static readonly object Sync = new();
        private static readonly Dictionary<string, string> Hashes = new(StringComparer.OrdinalIgnoreCase);
        private static GpcReleaseEntry[] shipped;
        private static string stale, foreign;
        private static bool started;

        public static string Warning
        {
            get { lock (Sync) return stale == null && foreign == null ? null : string.Join(" ", new[] { stale, foreign }.Where(m => m != null)); }
        }

        public static void Start()
        {
            lock (Sync) { if (started) return; started = true; }
            try
            {
                // Empty when Grasshopper loads plugins from byte arrays.
                string folder = Path.GetDirectoryName(typeof(GpcReleaseGuard).Assembly.Location);
                if (string.IsNullOrEmpty(folder)) { Write("Posizione del plugin sconosciuta; set GPC caricato non verificato."); return; }
                string repository = GpcReleaseSet.RepositoryManifest(folder);
                if (repository != null)
                {
                    string message = GpcReleaseSet.StaleBin(folder, GpcReleaseSet.Read(File.ReadAllText(repository)), Hash);
                    if (message != null) { lock (Sync) stale = message; Write(message); }
                }
                string manifest = Path.Combine(folder, GpcReleaseSet.ShippedManifest);
                if (!File.Exists(manifest))
                {
                    // Not a mismatch: say it once on the command line, without a warning on the components.
                    Write(GpcReleaseSet.ShippedManifest + " non trovato accanto al plugin; set GPC caricato non verificato.");
                    return;
                }
                shipped = GpcReleaseSet.Read(File.ReadAllText(manifest));
                AppDomain.CurrentDomain.AssemblyLoad += (_, e) => { if (GpcReleaseSet.IsGpc(e.LoadedAssembly.GetName().Name)) CheckLoaded(); };
                CheckLoaded();
            }
            catch (Exception e) { Report("Verifica del set GPC non riuscita: " + e.Message); }
        }

        /// <summary>Adds the warning, once, to a component whose results come from GPC.</summary>
        public static void Warn(GH_ActiveObject component)
        {
            if (Warning is string warning && !component.RuntimeMessages(GH_RuntimeMessageLevel.Warning).Contains(warning))
                component.AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, warning);
        }

        private static void CheckLoaded()
        {
            if (shipped == null) return;
            string message = GpcReleaseSet.ForeignLoaded(shipped, AppDomain.CurrentDomain.GetAssemblies().Where(a => !a.IsDynamic).Select(GpcReleaseSet.Describe), Hash);
            if (message != null) Report(message);
        }

        private static void Report(string message)
        {
            lock (Sync) { if (foreign == message) return; foreign = message; }
            Write(message);
        }

        private static string Hash(string path)
        {
            lock (Sync) if (Hashes.TryGetValue(path, out string known)) return known;
            string hash = GpcReleaseSet.Sha256(path);
            lock (Sync) Hashes[path] = hash;
            return hash;
        }

        private static void Write(string message)
        {
            try { Rhino.RhinoApp.WriteLine("BridGHe: " + message); } catch (Exception) { }
        }
    }
}
