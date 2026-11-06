using System;
using System.IO;
using BridGHe.Common.Helpers;
using BridGHe.Common.Models;

/// <summary>Optional full model archives for the common-domain migration, separate from existing fixtures.
/// <para>FEM path, retired at R6.5 (coordinator decision of 6/11): it writes FEM models (the input and the result of every analysis of
/// the native runners of 50 and 99, <c>BRIDGHE_MIGRATION_CAPTURE</c>), which do not exist after the removal. What goes with this file: the
/// variable and its four calls in <c>BridgeMovingRevisionNative</c> and <c>NativeModularBridgeTests</c>. What stays, as frozen references:
/// the archives already written (<c>gpc-unification-20261102\artifacts\f1\native-50-traced\models</c>, <c>native-99-traced\models</c>,
/// former archives readable as data).</para></summary>
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
