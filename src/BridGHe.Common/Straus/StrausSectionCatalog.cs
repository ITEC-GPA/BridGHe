// Source: installed Straus7 R3 API Manual, printed pp. 1003-1009.
using System;
using System.Linq;
namespace BridGHe.Common.Straus
{
    public static class StrausSectionCatalog
    {
        public static readonly StrausSectionShape[] All = new[]
        {
            new StrausSectionShape("CircularSolid", "Circular Solid", 1, false, new[] { "D" }, new[] { 0 }),
            new StrausSectionShape("CircularHollow", "Circular Hollow", 2, false, new[] { "D", "T" }, new[] { 0, 3 }),
            new StrausSectionShape("RectangleSolid", "Rectangle Solid", 3, false, new[] { "B", "D" }, new[] { 0, 1 }),
            new StrausSectionShape("RectangleHollow", "Rectangle Hollow", 4, false, new[] { "B", "D", "T1", "T2" }, new[] { 0, 1, 3, 4 }),
            new StrausSectionShape("LipChannel", "Lipped Channel", 5, false, new[] { "B", "D", "L", "T1", "T2", "T3" }, new[] { 0, 1, 2, 3, 4, 5 }),
            new StrausSectionShape("TopHat", "Top Hat", 6, false, new[] { "B", "D", "L", "T1", "T2", "T3" }, new[] { 0, 1, 2, 3, 4, 5 }),
            new StrausSectionShape("I", "I", 7, false, new[] { "B1", "B2", "D", "T1", "T2", "T3" }, new[] { 0, 1, 2, 3, 4, 5 }),
            new StrausSectionShape("T", "T", 8, false, new[] { "B", "D", "L", "T1", "T2", "T3" }, new[] { 0, 1, 2, 3, 4, 5 }),
            new StrausSectionShape("Angle", "Angle", 9, false, new[] { "B", "D", "T1", "T2" }, new[] { 0, 1, 3, 4 }),
            new StrausSectionShape("Z", "Z", 10, false, new[] { "B1", "B2", "D", "T1", "T2", "T3" }, new[] { 0, 1, 2, 3, 4, 5 }),
            new StrausSectionShape("TrapezoidSolid", "Trapezoid Solid", 12, false, new[] { "B1", "B2", "D" }, new[] { 0, 1, 2 }),
            new StrausSectionShape("TrapezoidHollow", "Trapezoid Hollow", 13, false, new[] { "B1", "B2", "D", "T1", "T2" }, new[] { 0, 1, 2, 3, 4 }),
            new StrausSectionShape("TriangleSolid", "Triangle Solid", 14, false, new[] { "B", "D" }, new[] { 0, 1 }),
            new StrausSectionShape("TriangleHollow", "Triangle Hollow", 15, false, new[] { "B", "D", "T1", "T2" }, new[] { 0, 1, 3, 4 }),
            new StrausSectionShape("Cruciform", "Cruciform", 16, false, new[] { "B", "D", "T1", "T2" }, new[] { 0, 1, 3, 4 }),
            new StrausSectionShape("BglRectangle", "BGL Hollow Rectangle", 1, true, new[] { "D", "B", "tw", "tf", "Ri", "Ro" }, new[] { 0, 1, 2, 3, 4, 5 }),
            new StrausSectionShape("BglI", "BGL I", 2, true, new[] { "D", "B1", "B2", "tw", "tf1", "tf2", "Rr1", "Rr2", "Rt1", "Rt2", "Rt3", "Rt4", "alpha1", "alpha2" }, new[] { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13 }),
            new StrausSectionShape("BglChannel", "BGL Channel", 3, true, new[] { "D", "B1", "B2", "tw", "tf1", "tf2", "Rr1", "Rr2", "Rh1", "Rh2", "Rt1", "Rt2", "Rt3", "Rt4", "alpha1", "alpha2" }, new[] { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15 }),
            new StrausSectionShape("BglT", "BGL T", 4, true, new[] { "D", "B", "tw", "tf", "Rr", "Rt1", "Rt2", "Rt3", "alpha1", "alpha2" }, new[] { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9 }),
            new StrausSectionShape("BglAngle", "BGL Angle", 5, true, new[] { "D", "B", "tw", "tf", "Rr", "Rh", "Rt1", "Rt2", "Rt3", "Rt4", "alpha1", "alpha2" }, new[] { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11 }),
            new StrausSectionShape("BglBulb", "BGL Bulb Flat", 6, true, new[] { "D", "B", "t", "Rr", "Rh", "Rt1", "Rt2", "Rt3", "alpha" }, new[] { 0, 1, 2, 3, 4, 5, 6, 7, 8 }),
        };
        public static StrausSectionShape Get(string key) => All.FirstOrDefault(s => s.Key == key) ?? throw new ArgumentException("Sezione Straus sconosciuta: " + key);
    }
}
