using System;
using System.Collections.Generic;
using System.Linq;
using BridGHe.Common.Models;

namespace BridGHe.Common.Helpers;

public sealed record BeamPropertyReplacementResult(BeamModel[] Beams, bool[] Replaced, string[] UnusedOriginalNames);

/// <summary>Apply explicit original/replacement property pairs by exact original name, never by beam list position.</summary>
public static class BeamPropertyReplacement
{
    public static BeamPropertyReplacementResult Apply(IReadOnlyList<BeamModel> beams,
        IReadOnlyList<BeamPropertyModel> originals, IReadOnlyList<BeamPropertyModel> replacements)
    {
        if (beams == null || beams.Any(b => b?.IsValid != true))
            throw new ArgumentException("Supply valid beams with an assigned Beam Property.");
        if (originals == null || replacements == null || originals.Count != replacements.Count)
            throw new ArgumentException("Original Properties and Replacement Properties must have the same number of items; no list broadcasting.");
        var pairs = new Dictionary<string, BeamPropertyModel>(StringComparer.Ordinal);
        var definitions = new Dictionary<string, (BeamPropertyModel Property, byte[] Data)>(StringComparer.Ordinal);
        for (int i = 0; i < originals.Count; i++)
        {
            var original = originals[i]; var replacement = replacements[i];
            if (original?.IsValid != true || string.IsNullOrWhiteSpace(original.Name) ||
                replacement?.IsValid != true || string.IsNullOrWhiteSpace(replacement.Name))
                throw new ArgumentException($"Pair {i + 1}: supply valid, named original and replacement Beam Property objects.");
            // Repeated identical properties from Get Beam Property are harmless. Conflicting names are not.
            if (!definitions.TryGetValue(replacement.Name, out var definition))
            {
                definition = (replacement, ModelArchive.Write(replacement));
                definitions.Add(replacement.Name, definition);
            }
            else if (!ReferenceEquals(definition.Property, replacement) && !definition.Data.SequenceEqual(ModelArchive.Write(replacement)))
                throw new ArgumentException($"Replacement name '{replacement.Name}' has different definitions. Give different properties distinct names.");
            if (pairs.TryGetValue(original.Name, out var previous) && previous.Name != replacement.Name)
                throw new ArgumentException($"Original name '{original.Name}' is paired with more than one replacement.");
            pairs[original.Name] = definition.Property;
        }
        var used = new HashSet<string>(StringComparer.Ordinal);
        var replaced = beams.Select(b => b.BeamProperty.Name != null && pairs.ContainsKey(b.BeamProperty.Name)).ToArray();
        bool changed = replaced.Any(v => v);
        // Clone a whole graph once: preserve shared load cases and avoid serializing each beam separately.
        var result = ModelArchive.Clone(beams.ToArray());
        var copies = new Dictionary<string, BeamPropertyModel>(StringComparer.Ordinal);
        for (int i = 0; i < result.Length; i++)
        {
            if (replaced[i])
            {
                string name = beams[i].BeamProperty.Name;
                var replacement = pairs[name];
                if (!copies.TryGetValue(replacement.Name, out var copy))
                {
                    copy = ModelArchive.Clone(replacement); copies.Add(replacement.Name, copy);
                }
                result[i].BeamProperty = copy; used.Add(name);
            }
            // A stiffness/material change invalidates the old solution also on the other supplied members.
            if (changed) result[i].Results.Clear();
        }
        return new(result, replaced, pairs.Keys.Where(name => !used.Contains(name)).ToArray());
    }
}
