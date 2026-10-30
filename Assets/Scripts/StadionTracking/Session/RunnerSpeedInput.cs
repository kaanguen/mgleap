using System;
namespace StadionTracking
{
    public enum RunnerSpeedSource { FixedTest, Treadmill }

    /// <summary>Separates simulated user speed from a future treadmill sensor feed.</summary>
    public sealed class RunnerSpeedInput
    {
        public const double SignalTimeoutSeconds = 2;
        public RunnerSpeedSource Source { get; private set; } = RunnerSpeedSource.FixedTest;
        private bool received;
        private double beltSpeedKmh, receivedAt;
        public bool SelectSource(RunnerSpeedSource source)
        {
            if (!Enum.IsDefined(typeof(RunnerSpeedSource), source)) throw new ArgumentOutOfRangeException(nameof(source));
            bool changed = Source != source;
            Source = source;
            if (changed) InvalidateTreadmill();
            return changed;
        }
        public bool ReportTreadmill(double speedKmh, double timestamp)
        {
            if (!Finite(speedKmh) || speedKmh < 0 || !Finite(timestamp))
            { InvalidateTreadmill(); return false; }
            beltSpeedKmh = speedKmh; receivedAt = timestamp; received = true;
            return true;
        }
        public void InvalidateTreadmill() { received = false; beltSpeedKmh = 0; }
        public bool HasFreshTreadmill(double now) => received && Finite(now) && now >= receivedAt && now - receivedAt <= SignalTimeoutSeconds;
        public double SpeedKmh(double testSpeedKmh, double now) => Source == RunnerSpeedSource.Treadmill
            ? (HasFreshTreadmill(now) ? beltSpeedKmh : 0)
            : (Finite(testSpeedKmh) ? Math.Max(0, testSpeedKmh) : 0);
        private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    }
}
