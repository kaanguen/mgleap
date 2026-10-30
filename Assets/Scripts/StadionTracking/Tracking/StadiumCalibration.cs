using System;

namespace StadionTracking
{
    /// <summary>Rigid two-point fit of the surveyed track model, without changing its scale.</summary>
    public static class StadiumCalibration
    {
        public static double SolveYaw(double localX, double localZ, double worldX, double worldZ)
        {
            double expected = Math.Sqrt(localX * localX + localZ * localZ);
            double actual = Math.Sqrt(worldX * worldX + worldZ * worldZ);
            if (double.IsNaN(actual) || double.IsInfinity(actual) || expected < 3 || actual < 3)
                throw new ArgumentException("Calibration points must be at least 3 m apart.");
            if (Math.Abs(actual - expected) > Math.Max(2, expected * .2))
                throw new ArgumentException("Measured baseline does not match the specified distance. Check point B and head tracking.");
            double cosine = (localX * worldX + localZ * worldZ) / (expected * actual);
            double sine = (localX * worldZ - localZ * worldX) / (expected * actual);
            return -Math.Atan2(sine, cosine) * 180 / Math.PI;
        }
        public static double WrappedDelta(double before, double after)
        {
            double delta = after - before;
            if (delta > 200) delta -= 400;
            if (delta < -200) delta += 400;
            return delta;
        }
    }
}
