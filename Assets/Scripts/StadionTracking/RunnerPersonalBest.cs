using System;

namespace StadionTracking
{
    /// <summary>Versioned personal bests; no partial run or unmeasured target times.</summary>
    [Serializable]
    public sealed class RunnerPersonalBest
    {
        public int Version = 1;
        public double LapLengthMetres;
        public double? Fastest100mSeconds, BestLapSeconds, Fastest1KmSeconds;
        public double[] CheckpointSeconds, GhostTimes, GhostDistances;
        public bool HasRecords() => Fastest100mSeconds.HasValue || BestLapSeconds.HasValue || Fastest1KmSeconds.HasValue;

        public RunnerPersonalBest Copy() => new RunnerPersonalBest
        {
            Version = Version, LapLengthMetres = LapLengthMetres,
            Fastest100mSeconds = Fastest100mSeconds, BestLapSeconds = BestLapSeconds, Fastest1KmSeconds = Fastest1KmSeconds,
            CheckpointSeconds = CheckpointSeconds == null ? null : (double[])CheckpointSeconds.Clone(),
            GhostTimes = GhostTimes == null ? null : (double[])GhostTimes.Clone(),
            GhostDistances = GhostDistances == null ? null : (double[])GhostDistances.Clone()
        };

        public static RunnerPersonalBest Merge(RunnerPersonalBest saved, RunnerPersonalBest candidate)
        {
            if (saved == null) return candidate;
            var lap = candidate.BestLapSeconds.HasValue &&
                (!saved.BestLapSeconds.HasValue || candidate.BestLapSeconds.Value < saved.BestLapSeconds.Value) ? candidate : saved;
            return new RunnerPersonalBest
            {
                LapLengthMetres = candidate.LapLengthMetres,
                Fastest100mSeconds = Faster(saved.Fastest100mSeconds, candidate.Fastest100mSeconds),
                Fastest1KmSeconds = Faster(saved.Fastest1KmSeconds, candidate.Fastest1KmSeconds),
                BestLapSeconds = lap.BestLapSeconds,
                CheckpointSeconds = lap.CheckpointSeconds,
                GhostTimes = lap.GhostTimes,
                GhostDistances = lap.GhostDistances
            };
        }

        private static double? Faster(double? a, double? b) =>
            a.HasValue && b.HasValue ? Math.Min(a.Value, b.Value) : a ?? b;
    }
}
