using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using St7API;

namespace BridGHe.Common.Straus
{
    /// <summary>Single process-wide gate: the native API is shared by every component.</summary>
    public static class StrausRuntime
    {
        public static object Gate { get; } = new object();
        private static readonly HashSet<int> Models = new HashSet<int>();
        private static bool initialized;

        static StrausRuntime()
        {
            NativeLibrary.SetDllImportResolver(typeof(St7).Assembly, Resolve);
        }

        private static IntPtr Resolve(string name, Assembly assembly, DllImportSearchPath? search)
        {
            if (!name.Equals("St7API.dll", StringComparison.OrdinalIgnoreCase)) return IntPtr.Zero;
            string configured = Environment.GetEnvironmentVariable("BRIDGHE_STRAUS_HOME");
            string root = configured ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Straus7 R31");
            string file = Path.Combine(root, "Bin64", "St7API.dll");
            if (!File.Exists(file)) throw new DllNotFoundException($"Straus7 R3 x64 non trovato: {file}. Impostare BRIDGHE_STRAUS_HOME alla cartella di installazione R3.");
            // LOAD_WITH_ALTERED_SEARCH_PATH also resolves the vendor's adjacent native dependencies.
            IntPtr handle = LoadLibraryEx(file, IntPtr.Zero, 0x00000008);
            if (handle == IntPtr.Zero) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(), file);
            return handle;
        }

        [DllImport("kernel32", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr LoadLibraryEx(string fileName, IntPtr file, uint flags);

        public static void EnsureInitialized()
        {
            lock (Gate)
            {
                if (initialized) return;
                Check(St7.St7SetLicenceOptions(St7.lmAbort, 0, 0), "St7SetLicenceOptions");
                Check(St7.St7Init(), "St7Init");
                initialized = true;
            }
        }

        public static int AcquireModelId()
        {
            lock (Gate)
            {
                EnsureInitialized();
                for (int id = 1; id <= 32; id++) if (Models.Add(id)) return id;
                throw new InvalidOperationException("Tutti i 32 identificativi Straus sono in uso.");
            }
        }

        public static void ReleaseModelId(int id) { lock (Gate) Models.Remove(id); }

        public static void Check(int code, string operation)
        {
            if (code == St7.ERR7_NoError) return;
            var message = new StringBuilder(1024);
            if (St7.St7GetAPIErrorString(code, message, message.Capacity) != 0)
                St7.St7GetSolverErrorString(code, message, message.Capacity);
            throw new InvalidOperationException($"{operation}: Straus {code}: {message}");
        }

        /// <summary>Caller must hold Gate for the entire lifetime of the file.</summary>
        public static TemporaryModel NewTemporaryModel() => new TemporaryModel();

        public sealed class TemporaryModel : IDisposable
        {
            public int Id { get; }
            private readonly string directory;
            private bool open;
            internal TemporaryModel()
            {
                Id = AcquireModelId();
                directory = Path.Combine(Path.GetTempPath(), "BridGHe", Guid.NewGuid().ToString("N"));
                try
                {
                    Directory.CreateDirectory(directory);
                    Check(St7.St7NewFile(Id, Path.Combine(directory, "section.st7"), directory + Path.DirectorySeparatorChar), "St7NewFile");
                    open = true;
                }
                catch { Dispose(); throw; }
            }
            public void Dispose()
            {
                if (open) { St7.St7CloseFile(Id); open = false; }
                ReleaseModelId(Id);
                // Only this unique directory created by this instance is removed.
                try { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
    }
}
