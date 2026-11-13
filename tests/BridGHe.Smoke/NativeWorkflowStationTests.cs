using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using BridGHe.Common.Checks;
using BridGHe.Common.Helpers;
using BridGHe.Common.Straus;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Special;

/// <summary>Read-only replay of example 19 native files; independently asks Straus for union stations.
/// <para>R6.6 lane R4b: <c>--workflow-archived-replay</c> stays <b>transitional</b> (FEM): it reads the 0.3.0 FEM result archives of 19 REV003
/// (<c>calcoli/REV003-*-results.bgmodel</c>, ignored by git: absent from the worktrees) into the analysis components; no gate runs it. On GPC data
/// the same archives read by value (<c>LegacyArchive.Read</c>, <c>LegacyModelImport.Import</c>, <c>LegacyResultsReader.Import</c>) would seed the
/// components with <c>GH_Model.FromGpc</c> and the analysed model (see the report of R4b). The fixture records stay for
/// <see cref="BridgeStationTests"/> (its GPC replay, R2b). P2CD (13/11): the FEM merge of its FEM replay (Merge) is removed with the FEM types.</para></summary>
internal static class NativeWorkflowStationTests
{
    internal sealed record Sample(double Station, double N, double V1, double V2, double T, double M1, double M2);
    internal sealed record CaseSamples(string Key, Sample[] Original, Sample[] NativeAtUnion);
    internal sealed record Fixture(double Length, string BeamId, CaseSamples[] Cases);
    internal const string FixturePath = "tests/BridGHe.Smoke/Fixtures/bridge-stations-19.json";




}
