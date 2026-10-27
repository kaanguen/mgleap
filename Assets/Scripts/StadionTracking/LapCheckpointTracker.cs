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
        private double lastSegmentDistance;
        private double lastKmDistance;
        private bool completeLap;

        public LapCheckpointTracker(double lapLengthMetres = 400, double targetPaceSecondsPerKm = 0)
        {
            if (lapLengthMetres <= 0 || double.IsNaN(lapLengthMetres) || double.IsInfinity(lapLengthMetres))
                throw new ArgumentOutOfRangeException(nameof(lapLengthMetres));

            this.lapLengthMetres = lapLengthMetres;
            checkpointLengthMetres = lapLengthMetres / CheckpointsPerLap;

            // When no custom pace is provided (e.g. in standalone tests), default to 250 s/km (100 s per 400 m lap, 25 s / 100 m)
            this.targetPaceSecondsPerKm = targetPaceSecondsPerKm > 0 && !double.IsInfinity(targetPaceSecondsPerKm)
                ? targetPaceSecondsPerKm : 250.0;
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
        public double LapLengthMetres => lapLengthMetres;
        public double LapElapsedSeconds => Math.Max(0, lastElapsedSeconds - lapStartedAt);
        public LapGhostRecording GhostRecording { get; } = new LapGhostRecording();
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

            if (!initialized || travelledMetres < lastDistance || elapsedSeconds < lastElapsedSeconds)
            {
                lastDistance = travelledMetres;
                lastElapsedSeconds = elapsedSeconds;
                lapStartedAt = elapsedSeconds;
                last100mTime = elapsedSeconds;
                lastKmTime = elapsedSeconds;
                lastSegmentDistance = travelledMetres;
                lastKmDistance = travelledMetres;
                completeLap = travelledMetres % lapLengthMetres == 0;
                GhostRecording.Begin(travelledMetres, elapsedSeconds, completeLap);
                initialized = true;
                HasCheckpointResult = false;
                Array.Clear(currentCheckpointTimes, 0, currentCheckpointTimes.Length);
                long initialIdx = (long)Math.Floor((travelledMetres % lapLengthMetres) / checkpointLengthMetres);
                UpdateNextCheckpoint(initialIdx, travelledMetres);
                return;
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
                    GhostRecording.Record(boundaryDistance, checkpointTime, true);
                    ProcessCheckpoint(boundary, boundaryDistance, checkpointTime);
                }
            }

            // Expire after processing crossings too: a delayed sample may already
            // be more than 20 metres or 6 seconds past the latest checkpoint.
            if (HasCheckpointResult && (travelledMetres >= checkpointDisplayUntilDistance ||
                elapsedSeconds >= checkpointDisplayUntilTime))
                HasCheckpointResult = false;

            GhostRecording.Record(travelledMetres, elapsedSeconds);
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
            bool full100mSegment = Math.Abs(boundaryDistance - lastSegmentDistance - 100.0) < 0.001;
            lastSegmentDistance = boundaryDistance;
            last100mTime = checkpointTime;
            if (full100mSegment && seg100m > 0 && seg100m < Fastest100mSeconds)
            {
                Fastest100mSeconds = seg100m;
            }

            double delta = splitTime - bestSplitTime;
            bool isRecord = completeLap && delta < -0.05;

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
                bool fullKmSegment = Math.Abs(boundaryDistance - lastKmDistance - 1000.0) < 0.001;
                lastKmDistance = boundaryDistance;
                lastKmTime = checkpointTime;
                if (fullKmSegment && seg1Km > 0 && seg1Km < Fastest1KmSeconds)
                {
                    Fastest1KmSeconds = seg1Km;
                }

                LastCheckpointMetres = boundaryDistance;
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
                bool isNewBest = completeLap && splitTime > 0 && splitTime < bestLapSeconds;
                if (isNewBest)
                {
                    bestLapSeconds = splitTime;
                    Array.Copy(currentCheckpointTimes, bestCheckpointTimes, CheckpointsPerLap);
                    BestLapIsExample = false;
                    if (!isKmMark)
                        LastCheckpointIsRecord = true;
                }

                GhostRecording.Finish(boundaryDistance, checkpointTime, isNewBest);
                completeLap = true;
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
