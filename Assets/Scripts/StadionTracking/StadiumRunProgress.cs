using System;

namespace StadionTracking
{

    public sealed class StadiumRunProgress
    {
        public const double TargetMetres = 400.0;
        public double TravelledMetres { get; private set; }
        public bool IsFinished => TravelledMetres >= TargetMetres;

        // Accumulate speed times elapsed time, pausing at zero and stopping at exactly 400 metres.
        public void Advance(double metresPerSecond, double deltaSeconds)
        {

            if (IsFinished || metresPerSecond <= 0 || deltaSeconds <= 0 ||
                double.IsNaN(metresPerSecond) || double.IsInfinity(metresPerSecond) ||
                double.IsNaN(deltaSeconds) || double.IsInfinity(deltaSeconds))
                return;

            TravelledMetres = Math.Min(TargetMetres, TravelledMetres + metresPerSecond * deltaSeconds);
        }
    }
}
