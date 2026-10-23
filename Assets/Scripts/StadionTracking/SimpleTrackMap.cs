using System;

namespace StadionTracking
{

    public static class SimpleTrackMap
    {
        public const int PixelWidth = 1536;
        public const int PixelHeight = 768;
        public const double PixelsPerMetre = 8.0;

        public readonly struct MapPoint
        {
            public readonly double U, V;
            public MapPoint(double u, double v) { U = u; V = v; }
        }

        // Project all lanes onto the single-lane PNG and return normalized image coordinates.
        public static MapPoint ToUv(double x, double z)
        {

            double h = StadiumTrackGeometry.StraightLength / 2;
            double centreX = Math.Max(-h, Math.Min(h, x));
            double dx = x - centreX;
            double distance = Math.Sqrt(dx * dx + z * z);
            double mapRadius = StadiumTrackGeometry.InnerRadius + 0.30;
            if (distance > 0.000001)
            {
                x = centreX + dx * mapRadius / distance;
                z *= mapRadius / distance;
            }

            return new MapPoint(0.5 + x * PixelsPerMetre / PixelWidth,
                0.5 + z * PixelsPerMetre / PixelHeight);
        }
    }
}
