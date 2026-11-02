// Compiled into Common only in an explicitly instrumented diagnostic build.
// No dependencies on GH and no mutation of the arguments passed to Straus7.
using System;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace St7API
{
    internal static class StrausApiCapture
    {
        private static readonly object Gate = new object();
        private static readonly string PathName = Environment.GetEnvironmentVariable("BRIDGHE_STRAUS_TRACE");
        private static StreamWriter writer;
        private static long sequence;

        static StrausApiCapture()
        {
            AppDomain.CurrentDomain.ProcessExit += (_, __) => { lock (Gate) { writer?.Dispose(); writer = null; } };
        }

        internal static object[] Snapshot(object[] values) => string.IsNullOrEmpty(PathName) ? null : values.Select(Freeze).ToArray();

        private static object Freeze(object value)
        {
            string Bits(double x) => unchecked((ulong)BitConverter.DoubleToInt64Bits(x)).ToString("X16", CultureInfo.InvariantCulture);
            if (value is double d) return new { doubleBits = Bits(d) };
            if (value is double[] doubles) return new { doubleBits = doubles.Select(Bits).ToArray() };
            if (value is int[] integers) return integers.ToArray();
            if (value is StringBuilder text) return text.ToString();
            return value;
        }

        internal static void Record(string api, object[] before, int code, object[] refs)
        {
            if (before == null) return;
            lock (Gate)
            {
                if (writer == null)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(PathName)));
                    // Never overwrite an earlier capture, even after a failed test.
                    writer = new StreamWriter(new GZipStream(new FileStream(PathName, FileMode.CreateNew, FileAccess.Write, FileShare.Read), CompressionLevel.Fastest), new UTF8Encoding(false));
                }
                writer.WriteLine(JsonSerializer.Serialize(new { sequence = ++sequence, api, before, code, refs = refs.Select(Freeze).ToArray() }));
                if (api == "St7SaveFile" || api == "St7CloseFile") writer.Flush();
            }
        }
    }
}
