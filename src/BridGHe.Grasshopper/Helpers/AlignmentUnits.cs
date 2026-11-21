using System;
using System.Collections.Generic;
using System.Globalization;
using Grasshopper.Kernel;
using Rhino;
using Rhino.Geometry;

namespace BridGHe.Grasshopper.Helpers;

/// <summary>0.7.2 (proposal 1 of 21/11/2026): BridGHe reads every coordinate in mm and never converts. Two non-blocking warnings where the
/// alignment enters (Bridge Support Stations, Bridge Alignment Curve): the Rhino document in another unit (the factor from the real unit),
/// and an alignment shorter than 1 m, which also works without a document. Nothing is converted.</summary>
internal static class AlignmentUnits
{
    /// <summary>Alignments shorter than this, in mm, are probably drawn in metres.</summary>
    internal const double MinimumLength = 1000;
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    /// <summary>The warnings for an alignment of the given length (mm) in a document with the given unit system (null: no document). Texts in
    /// English (user 21/11/2026).</summary>
    internal static IReadOnlyList<string> Warnings(double length, UnitSystem? document)
    {
        var warnings = new List<string>();
        if (document is { } units && units != UnitSystem.Millimeters && units != UnitSystem.None && units != UnitSystem.Unset && units != UnitSystem.CustomUnits)
        {
            double factor = RhinoMath.UnitScale(units, UnitSystem.Millimeters);
            if (double.IsFinite(factor) && factor > 0)
            {
                string scale = factor.ToString("0.######", Invariant);
                warnings.Add(factor > 1
                    ? $"The Rhino document is in {Name(units)}: BridGHe reads coordinates in mm, so the bridge comes out {scale} times smaller. Set the document to millimetres or scale the alignment by {scale}."
                    : $"The Rhino document is in {Name(units)}: BridGHe reads coordinates in mm, so the bridge comes out {(1 / factor).ToString("0.######", Invariant)} times larger. Set the document to millimetres or scale the alignment by {scale}.");
            }
        }
        if (double.IsFinite(length) && length > 0 && length < MinimumLength)
            warnings.Add($"Alignment {length.ToString("0.###", Invariant)} mm long: probably drawn in metres. BridGHe reads coordinates in mm.");
        return warnings;
    }

    /// <summary>The warnings for the alignment of a component, in the active Rhino document (none in a run without a document).</summary>
    internal static void Report(GH_ActiveObject component, Curve alignment)
    {
        if (alignment == null || !alignment.IsValid) return;
        foreach (string warning in Warnings(alignment.GetLength(), RhinoDoc.ActiveDoc?.ModelUnitSystem))
            component.AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, warning);
    }

    private static string Name(UnitSystem units) => units switch
    {
        UnitSystem.Meters => "metres",
        UnitSystem.Centimeters => "centimetres",
        UnitSystem.Decimeters => "decimetres",
        UnitSystem.Kilometers => "kilometres",
        UnitSystem.Microns => "microns",
        UnitSystem.Inches => "inches",
        UnitSystem.Feet => "feet",
        UnitSystem.Yards => "yards",
        UnitSystem.Miles => "miles",
        _ => units.ToString().ToLowerInvariant()
    };
}
