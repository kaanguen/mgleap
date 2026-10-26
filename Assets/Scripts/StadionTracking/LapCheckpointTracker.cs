using System;
using System.Collections.Generic;

namespace StadionTracking
{
    public sealed class LapCheckpointTracker
    {
        private const int CheckpointsPerLap = 4;
        private readonly double lapLengthMetres;
        private readonly double checkpointLengthMetres;
        private readonly double targetPaceSecondsPerKm;
        private readonly double[] bestCheckpointTimes = new double[CheckpointsPerLap];
        private readonly double[] currentCheckpointTimes = new double[CheckpointsPerLap];
        private readonly Dictionary<int, double> bestKmTimes = new Dictionary<int, double>();

        private double lastDistance;
        private double lastElapsedSeconds;
        private double lapStartedAt;
        private double bestLapSeconds;
        private bool initialized;

        private double checkpointDisplayUntilDistance;
        private double checkpointDisplayUntilTime;
        private double last100mTime;
        private double lastKmTime;

        public LapCheckpointTracker(double lapLengthMetres = 400, double targetPaceSecondsPerKm = 0)
        {
            if (lapLengthMetres <= 0 || double.IsNaN(lapLengthMetres) || double.IsInfinity(lapLengthMetres))
                throw new ArgumentOutOfRangeException(nameof(lapLengthMetres));

            this.lapLengthMetres = lapLengthMetres;
            checkpointLengthMetres = lapLengthMetres / CheckpointsPerLap;

            // When no custom pace is provided (e.g. in standalone tests), default to 250 s/km (100 s per 400 m lap, 25 s / 100 m)
            this.targetPaceSecondsPerKm = targetPaceSecondsPerKm > 0 ? targetPaceSecondsPerKm : 250.0;
            bestLapSeconds = this.targetPaceSecondsPerKm * (lapLengthMetres / 1000.0);

            for (int i = 0; i < CheckpointsPerLap; i++)
            {
                bestCheckpointTimes[i] = bestLapSeconds * (i + 1) / CheckpointsPerLap;
            }

            Fastest100mSeconds = this.targetPaceSecondsPerKm * 0.1;
            Fastest1KmSeconds = this.targetPaceSecondsPerKm;

            UpdateNextCheckpoint(0, 0);
        }

        public double BestLapSeconds => bestLapSeconds;
        public bool BestLapIsExample { get; private set; } = true;
        public bool HasCheckpointResult { get; private set; }
        public double LastCheckpointMetres { get; private set; }
        public double LastCheckpointDeltaSeconds { get; private set; }
        public bool LastCheckpointIsRecord { get; private set; }
        public string LastCheckpointLabel { get; private set; } = "";
        public double NextCheckpointMetres { get; private set; }
        public double NextCheckpointBestSeconds { get; private set; }
        public string NextCheckpointLabel { get; private set; } = "";
        public double Fastest100mSeconds { get; private set; }
        public double Fastest1KmSeconds { get; private set; }

        public double GetBestKmSeconds(int kmIndex)
        {
            return bestKmTimes.TryGetValue(kmIndex, out double time) ? time : targetPaceSecondsPerKm * kmIndex;
        }

        public void Update(double travelledMetres, double elapsedSeconds)
        {
            if (double.IsNaN(travelledMetres) || double.IsInfinity(travelledMetres) ||
                double.IsNaN(elapsedSeconds) || double.IsInfinity(elapsedSeconds) ||
                travelledMetres < 0 || elapsedSeconds < 0)
                return;

            if (!initialized || travelledMetres < lastDistance)
            {
                lastDistance = travelledMetres;
                lastElapsedSeconds = elapsedSeconds;
                lapStartedAt = elapsedSeconds;
                last100mTime = elapsedSeconds;
                lastKmTime = elapsedSeconds;
                initialized = true;
                HasCheckpointResult = false;
                Array.Clear(currentCheckpointTimes, 0, currentCheckpointTimes.Length);
                long initialIdx = (long)Math.Floor((travelledMetres % lapLengthMetres) / checkpointLengthMetres);
                UpdateNextCheckpoint(initialIdx, travelledMetres);
                return;
            }

            // Keep checkpoint result visible for 20 metres or up to 6 seconds, then switch back to upcoming preview
            if (HasCheckpointResult)
            {
                if (travelledMetres >= checkpointDisplayUntilDistance || elapsedSeconds >= checkpointDisplayUntilTime)
                {
                    HasCheckpointResult = false;
                }
            }

            if (travelledMetres > lastDistance)
            {
                long firstBoundary = (long)Math.Floor(lastDistance / checkpointLengthMetres) + 1;
                long lastBoundary = (long)Math.Floor(travelledMetres / checkpointLengthMetres);
                for (long boundary = firstBoundary; boundary <= lastBoundary; boundary++)
                {
                    double boundaryDistance = boundary * checkpointLengthMetres;
                    double fraction = (boundaryDistance - lastDistance) / (travelledMetres - lastDistance);
                    double checkpointTime = lastElapsedSeconds +
                        (elapsedSeconds - lastElapsedSeconds) * fraction;
                    ProcessCheckpoint(boundary, boundaryDistance, checkpointTime);
                }
            }

            lastDistance = travelledMetres;
            lastElapsedSeconds = elapsedSeconds;
            long checkpointIndex = (long)Math.Floor((travelledMetres % lapLengthMetres) / checkpointLengthMetres);
            UpdateNextCheckpoint(checkpointIndex, travelledMetres);
        }

        private void ProcessCheckpoint(long boundary, double boundaryDistance, double checkpointTime)
        {
            int checkpointIndex = (int)((boundary - 1) % CheckpointsPerLap);
            double splitTime = Math.Max(0, checkpointTime - lapStartedAt);
            double bestSplitTime = bestCheckpointTimes[checkpointIndex];
            currentCheckpointTimes[checkpointIndex] = splitTime;

            // 100m segment tracking
            double seg100m = Math.Max(0, checkpointTime - last100mTime);
            last100mTime = checkpointTime;
            bool is100mRecord = false;
            if (seg100m > 0 && (BestLapIsExample || seg100m < Fastest100mSeconds))
            {
                Fastest100mSeconds = seg100m;
                is100mRecord = true;
            }

            double delta = splitTime - bestSplitTime;
            bool isRecord = delta < -0.05 || is100mRecord;

            LastCheckpointMetres = checkpointLengthMetres * (checkpointIndex + 1);
            LastCheckpointDeltaSeconds = delta;
            LastCheckpointIsRecord = isRecord;

            // Check if boundary is an exact kilometer mark (1000m, 2000m, 3000m...)
            int kmIndex = (int)Math.Round(boundaryDistance / 1000.0);
            bool isKmMark = Math.Abs(boundaryDistance - kmIndex * 1000.0) < 0.1 && kmIndex > 0;

            if (isKmMark)
            {
                double targetKmTime = bestKmTimes.TryGetValue(kmIndex, out var prevKmTime)
                    ? prevKmTime
                    : targetPaceSecondsPerKm * kmIndex;

                double kmDelta = checkpointTime - targetKmTime;
                bool isKmRecord = kmDelta < -0.05;
                if (isKmRecord || !bestKmTimes.ContainsKey(kmIndex))
                {
                    bestKmTimes[kmIndex] = checkpointTime;
                }

                double seg1Km = Math.Max(0, checkpointTime - lastKmTime);
                lastKmTime = checkpointTime;
                if (seg1Km > 0 && (BestLapIsExample || seg1Km < Fastest1KmSeconds))
                {
                    Fastest1KmSeconds = seg1Km;
                }

                LastCheckpointLabel = $"{kmIndex} km";
                LastCheckpointDeltaSeconds = kmDelta;
                LastCheckpointIsRecord = isKmRecord;
            }
            else if (checkpointIndex == CheckpointsPerLap - 1)
            {
                LastCheckpointLabel = $"{lapLengthMetres:0} m (Runde)";
            }
            else
            {
                LastCheckpointLabel = $"{LastCheckpointMetres:0} m";
            }

            // Lap finish (400 m)
            if (checkpointIndex == CheckpointsPerLap - 1)
            {
                if (splitTime < bestLapSeconds)
                {
                    bestLapSeconds = splitTime;
                    for (int i = 0; i < CheckpointsPerLap; i++)
                    {
                        bestCheckpointTimes[i] = bestLapSeconds * (i + 1) / CheckpointsPerLap;
                    }
                    BestLapIsExample = false;
                    LastCheckpointIsRecord = true;
                }

                lapStartedAt = checkpointTime;
                Array.Clear(currentCheckpointTimes, 0, currentCheckpointTimes.Length);
            }

            HasCheckpointResult = true;
            checkpointDisplayUntilDistance = boundaryDistance + 20.0;
            checkpointDisplayUntilTime = checkpointTime + 6.0;
        }

        private void UpdateNextCheckpoint(long checkpointIndex, double currentDistance = 0)
        {
            int index = (int)(checkpointIndex % CheckpointsPerLap);
            double lapCheckpoint = checkpointLengthMetres * (index + 1);

            double totalNext100m = (Math.Floor(currentDistance / checkpointLengthMetres) + 1) * checkpointLengthMetres;
            int nextKm = (int)Math.Round(totalNext100m / 1000.0);
            bool nextIsKm = Math.Abs(totalNext100m - nextKm * 1000.0) < 0.1 && nextKm > 0;

            if (nextIsKm)
            {
                NextCheckpointMetres = 1000.0 * nextKm;
                NextCheckpointLabel = $"{nextKm} km";
                NextCheckpointBestSeconds = bestKmTimes.TryGetValue(nextKm, out var prevTime)
                    ? prevTime
                    : targetPaceSecondsPerKm * nextKm;
            }
            else
            {
                NextCheckpointMetres = lapCheckpoint;
                NextCheckpointLabel = $"{lapCheckpoint:0} m";
                NextCheckpointBestSeconds = bestCheckpointTimes[index];
            }
        }
    }
}
