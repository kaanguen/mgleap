using System;

namespace StadionTracking
{
    /// <summary>Independent test motion; never changes the recorded best lap.</summary>
    public sealed class GhostTestMotion
    {
        public bool Enabled { get; private set; }
        public double SpeedKmh { get; private set; }
        public double Distance { get; private set; }
        public void SetSpeed(double? speedKmh, double currentDistance)
        {
            if (speedKmh.HasValue && (double.IsNaN(speedKmh.Value) || double.IsInfinity(speedKmh.Value) || speedKmh.Value < 0 || speedKmh.Value > float.MaxValue))
                throw new ArgumentOutOfRangeException(nameof(speedKmh), "Ghost speed must be finite and non-negative.");
            if (!Enabled) Distance = currentDistance;
            Enabled = speedKmh.HasValue;
            SpeedKmh = speedKmh ?? 0;
        }
        public void Reset() => Distance = 0;
        public void Advance(double seconds, bool running)
        {
            if (Enabled && running && seconds > 0 && !double.IsNaN(seconds) && !double.IsInfinity(seconds))
                Distance += SpeedKmh / 3.6 * seconds;
        }
    }
}
