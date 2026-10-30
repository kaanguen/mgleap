using System;

namespace StadionTracking
{
    public enum StadiumPacingMode { ManualSpeed, TargetLapTime }

    public static class StadiumPacing
    {
        // Use the actual lane length, so outer lanes meet the same lap time.
        public static double SpeedForLapTime(double lapMetres, double targetMinutes)
        {
            if (lapMetres <= 0 || targetMinutes <= 0 ||
                double.IsNaN(lapMetres) || double.IsInfinity(lapMetres) ||
                double.IsNaN(targetMinutes) || double.IsInfinity(targetMinutes))
                return 0;
            double speed = lapMetres / (targetMinutes * 60.0);
            return double.IsInfinity(speed) ? 0 : speed;
        }
    }
}
