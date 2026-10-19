using System;
using System.Collections.Generic;
using System.Linq;
using BridGHe.Common.Helpers;
using BridGHe.Common.Models;

namespace BridGHe.Common.Diagnostics;

public static class BridgePhaseModel
{
    /// <summary>Independent FEM copy with unchanged geometry/IDs. Old results are always invalidated.</summary>
    public static ModelModel Create(ModelModel source, string name, IReadOnlyList<string> beamIds, BeamPropertyModel property, bool clearLoads)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Phase model name is required.");
        if (beamIds == null || beamIds.Count == 0 || beamIds.Any(string.IsNullOrWhiteSpace) || beamIds.Distinct().Count() != beamIds.Count)
            throw new ArgumentException("Supply unique, non-empty Beam IDs explicitly; no implicit whole-model override.");
        var unknown = beamIds.Except(source.Beams.Select(b => b.BeamId)).ToArray();
        if (unknown.Length > 0) throw new ArgumentException("Unknown Beam IDs: " + string.Join(", ", unknown));
        if (property?.IsValid != true) throw new ArgumentException("A valid replacement beam property is required.");
        var copy = ModelArchive.Clone(source); var newProperty = ModelArchive.Clone(property);
        var selected = beamIds.ToHashSet(StringComparer.Ordinal);
        copy.Name = name;
        foreach (var beam in copy.Beams) if (selected.Contains(beam.BeamId)) beam.BeamProperty = newProperty;
        foreach (var element in copy.Elements) { element.Results.Clear(); if (clearLoads) element.Loads.Clear(); }
        var generated = copy.GeneratedMovingCases.Values.ToHashSet(StringComparer.Ordinal);
        copy.LoadCases.RemoveAll(c => generated.Contains(c.Name));
        copy.GeneratedMovingCases.Clear(); copy.LoadCombinations.Clear(); copy.BridgeWorkflow = null;
        // Global staged-construction definitions refer to the original load history.
        copy.StagedConstruction.Clear();
        if (clearLoads) { copy.LoadCases.Clear(); copy.MovingPaths.Clear(); }
        return copy;
    }
}
