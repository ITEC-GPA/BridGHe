using System;
using System.IO;
using BridGHe.Common.Helpers;
using BridGHe.Common.Models;

/// <summary>Optional full model archives for the common-domain migration, separate from existing fixtures.</summary>
internal static class MigrationCapture
{
    internal static void Write(string label, ModelModel model)
    {
        string folder = Environment.GetEnvironmentVariable("BRIDGHE_MIGRATION_CAPTURE");
        if (string.IsNullOrWhiteSpace(folder)) return;
        Directory.CreateDirectory(folder);
        using var stream = new FileStream(Path.Combine(folder, label + ".bgmodel"), FileMode.CreateNew);
        byte[] bytes = ModelArchive.Write(model);
        stream.Write(bytes, 0, bytes.Length);
    }
}
