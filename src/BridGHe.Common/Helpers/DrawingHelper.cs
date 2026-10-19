using System;
using Rhino.Display;
using Rhino.Geometry;

namespace BridGHe.Common.Helpers
{
    public static class DrawingHelper
    {
        public static readonly double ArrowSize = 0.3;
        [ThreadStatic] private static double multiplier;
        public static IDisposable Scale(double factor)
        {
            if (!double.IsFinite(factor) || factor <= 0) throw new ArgumentOutOfRangeException(nameof(factor));
            var scope = new ScaleScope(multiplier); multiplier = factor; return scope;
        }
        private sealed class ScaleScope : IDisposable
        {
            private readonly double previous;
            public ScaleScope(double previous) => this.previous = previous;
            public void Dispose() => multiplier = previous;
        }

        public static double GetScaleFactor(RhinoViewport viewport, Point3d point)
        {
            double scale; // pixel per unit length in WorldXY
            viewport.GetWorldToScreenScale(point, out scale);
            return 20 / scale * (multiplier > 0 ? multiplier : 1);
        }
    }
}
