using System;
using System.Collections.Generic;
using System.Linq;
using BridGHe.Common.Gpc;
using BridGHe.Common.Gpc.Legacy;
using BridGHe.Common.Helpers;

/// <summary>
/// TESTS ONLY, F6 (a)/(b): a FEM model of the regression tests with stored results as an analysed GPC model, so that a result consumer
/// ported to GPC runs on the same data as its former FEM path. The model is converted by the legacy reader, its stored results become a
/// GPC dataset through <see cref="LegacyResultsReader"/> (the inverse of the <see cref="GpcResults"/> conventions) and a results catalog
/// lists its cases in the order of their first stored result (label = case), as a BridGHe analysis does. Node displacements and reactions
/// are global and come back exactly; beam actions go through the principal frame (exact for the axis-aligned beams of these tests).
/// </summary>
internal static class GpcSyntheticResults
{
    internal const string Dataset = "BridGHe synthetic results";


    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<GPC.Model.Models.Model, GPC.Checkers.CompositeBridge.BridgeModelData> bridges = new();
    /// <summary>L12: the bridge data of a model <see cref="Analysed(ModelModel, double)"/> gave (its beams' GUIDs), or null.</summary>
    internal static GPC.Checkers.CompositeBridge.BridgeModelData BridgeOf(GPC.Model.Models.Model analysed) => analysed != null && bridges.TryGetValue(analysed, out var data) ? data : null;

}
