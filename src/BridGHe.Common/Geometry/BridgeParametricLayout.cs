using System;
using System.Linq;
using Rhino.Geometry;

namespace BridGHe.Common.Geometry;

public sealed record BridgeParametricLayoutResult(Curve Alignment, double[] Supports, double[] Offsets,
    double[] DeckWidths, double[] SpanLengths, int[] BearingTypes);

/// <summary>Convenience layout in mm. Explicit lists override regular spacing; no structural sizing.</summary>
public static class BridgeParametricLayout
{
    public static BridgeParametricLayoutResult Create(int spans, double spanLength, double[] spanLengths,
        int girders, double spacing, double[] offsets, double leftOverhang, double rightOverhang,
        double startOverhang, double endOverhang, double radius, Point3d origin, double heading, Curve alignment = null)
    {
        spanLengths = spanLengths?.Length > 0 ? spanLengths.ToArray() :
            spans is >= 1 and <= 100 ? Enumerable.Repeat(spanLength, spans).ToArray() : throw new ArgumentException("Span Count must be 1..100.");
        offsets = offsets?.Length > 0 ? offsets.ToArray() :
            girders is >= 2 and <= 32 ? Enumerable.Range(0, girders).Select(i => (i - (girders - 1) / 2d) * spacing).ToArray() :
            throw new ArgumentException("Girder Count must be 2..32.");
        if (spanLengths.Length > 100 || spanLengths.Any(s => !double.IsFinite(s) || s <= 0) ||
            offsets.Length < 2 || offsets.Length > 32 || offsets.Any(s => !double.IsFinite(s)) ||
            offsets.Zip(offsets.Skip(1), (a,b) => b-a).Any(d => d <= 0))
            throw new ArgumentException("Spans must be positive; offsets must be strictly increasing, with at least two girders.");
        if (new[]{leftOverhang,rightOverhang,startOverhang,endOverhang}.Any(x => !double.IsFinite(x) || x < 0) ||
            !double.IsFinite(radius) || !double.IsFinite(heading) || !origin.IsValid)
            throw new ArgumentException("Invalid overhang, radius, origin or heading.");
        double length = spanLengths.Sum() + startOverhang + endOverhang;
        if (!double.IsFinite(length) || length <= 0) throw new ArgumentException("Invalid total length.");
        var supports = new double[spanLengths.Length + 1]; supports[0] = startOverhang;
        for (int i=0; i<spanLengths.Length; i++) supports[i+1] = supports[i] + spanLengths[i];
        var widths = new[]{offsets[^1] + leftOverhang, -offsets[0] + rightOverhang};
        if (widths.Any(w => w <= 0)) throw new ArgumentException("Deck edges must lie on opposite sides of the alignment.");
        Curve curve;
        if (alignment != null)
        {
            if (!alignment.IsValid || alignment.IsClosed || Math.Abs(alignment.GetLength()-length) > Math.Max(1e-3,length*1e-8))
                throw new ArgumentException("Custom Alignment must be open and its 3D length must equal spans plus end overhangs (mm).");
            curve = alignment.DuplicateCurve();
        }
        else
        {
            if (radius == 0) curve = new LineCurve(Point3d.Origin, new Point3d(length,0,0));
            else
            {
                double angle = length / Math.Abs(radius), sign = Math.Sign(radius);
                if (angle >= Math.PI) throw new ArgumentException("Automatic circular alignment requires an angle below 180 degrees; increase |Radius|.");
                Point3d P(double t) => new(Math.Abs(radius)*Math.Sin(t), sign*Math.Abs(radius)*(1-Math.Cos(t)),0);
                curve = new ArcCurve(new Arc(Point3d.Origin,P(angle/2),P(angle)));
            }
            curve.Transform(Transform.Rotation(heading*Math.PI/180,Vector3d.ZAxis,Point3d.Origin));
            curve.Transform(Transform.Translation((Vector3d)origin));
        }
        // A single fixed bearing; one longitudinal guide on the same girder at every other support.
        var bearings = Enumerable.Range(0,supports.Length).SelectMany(s =>
            Enumerable.Range(0,offsets.Length).Select(g => g==0 ? (s==0 ? 0 : 1) : 2)).ToArray();
        return new(curve,supports,offsets,widths,spanLengths,bearings);
    }
}
