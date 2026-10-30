using System;
using System.Collections.Generic;

namespace StadionTracking
{
    /// <summary>Time/distance samples in lap coordinates, independent of Unity and world placement.</summary>
    public sealed class LapGhostRecording
    {
        private struct Sample
        {
            public double Time, Distance;
            public Sample(double time, double distance) { Time = time; Distance = distance; }
        }

        private readonly List<Sample> current = new List<Sample>();
        private Sample[] best;
        private double startTime, startDistance;
        private bool completeLap;

        public bool HasRecording => best != null;
        public int BestSampleCount => best == null ? 0 : best.Length;

        public void ExportBest(out double[] times, out double[] distances)
        {
            times = distances = null;
            if (best == null) return;
            times = new double[best.Length];
            distances = new double[best.Length];
            for (int i = 0; i < best.Length; i++) { times[i] = best[i].Time; distances[i] = best[i].Distance; }
        }

        public bool TryRestoreBest(double[] times, double[] distances, double lapLength, double lapSeconds)
        {
            if (times == null && distances == null) { best = null; return true; }
            if (lapLength <= 0 || lapSeconds <= 0 || double.IsNaN(lapLength) || double.IsInfinity(lapLength) ||
                double.IsNaN(lapSeconds) || double.IsInfinity(lapSeconds)) return false;
            if (times == null || distances == null || times.Length != distances.Length || times.Length < 2 || times.Length > 100000)
                return false;
            var restored = new Sample[times.Length];
            for (int i = 0; i < times.Length; i++)
            {
                if (double.IsNaN(times[i]) || double.IsInfinity(times[i]) || double.IsNaN(distances[i]) || double.IsInfinity(distances[i]) ||
                    times[i] < 0 || distances[i] < 0 || distances[i] > lapLength + .001 ||
                    (i > 0 && (times[i] <= times[i - 1] || distances[i] < distances[i - 1]))) return false;
                restored[i] = new Sample(times[i], distances[i]);
            }
            int last = times.Length - 1;
            if (Math.Abs(times[0]) > .000001 || Math.Abs(distances[0]) > .000001 ||
                Math.Abs(times[last] - lapSeconds) > .001 || Math.Abs(distances[last] - lapLength) > .001) return false;
            best = restored;
            return true;
        }

        internal void Begin(double distance, double time, bool isCompleteLap)
        {
            startDistance = distance;
            startTime = time;
            completeLap = isCompleteLap;
            current.Clear();
            current.Add(new Sample(0, 0));
        }

        internal void Record(double distance, double time, bool force = false)
        {
            if (!completeLap) return;
            double relativeTime = time - startTime;
            var last = current[current.Count - 1];
            // Keep boundary samples exact; ordinary samples are limited to 10 Hz.
            if (relativeTime <= last.Time || distance < startDistance + last.Distance) return;
            if (force || relativeTime - last.Time >= 0.1)
                current.Add(new Sample(relativeTime, distance - startDistance));
        }

        internal void Finish(double distance, double time, bool isNewBest)
        {
            Record(distance, time, true);
            if (completeLap && isNewBest && current.Count >= 2)
                best = current.ToArray();
            Begin(distance, time, true);
        }

        /// <summary>Replay consecutive laps independently of the user's distance or belt speed.</summary>
        public void EvaluateContinuous(double elapsedSeconds, double lapLength, double targetLapSeconds,
            out double totalDistance, out double speed)
        {
            elapsedSeconds = Math.Max(0, elapsedSeconds);
            double duration = Math.Max(.001, targetLapSeconds);
            double laps = Math.Floor(elapsedSeconds / duration);
            Evaluate(elapsedSeconds % duration, lapLength, targetLapSeconds, out double lapDistance, out speed);
            totalDistance = laps * lapLength + lapDistance;
        }

        /// <summary>Hold at the finish until the runner starts their next lap.</summary>
        public void Evaluate(double lapSeconds, double lapLength, double targetLapSeconds,
            out double distance, out double speed)
        {
            lapSeconds = Math.Max(0, lapSeconds);
            if (best == null)
            {
                speed = lapSeconds < targetLapSeconds ? lapLength / targetLapSeconds : 0;
                distance = Math.Min(lapLength, lapSeconds * lapLength / targetLapSeconds);
                return;
            }
            if (lapSeconds >= best[best.Length - 1].Time)
            {
                distance = lapLength;
                speed = 0;
                return;
            }
            int low = 0, high = best.Length - 1;
            while (high - low > 1)
            {
                int middle = (low + high) / 2;
                if (best[middle].Time <= lapSeconds) low = middle;
                else high = middle;
            }
            Sample before = best[low], after = best[high];
            speed = (after.Distance - before.Distance) / (after.Time - before.Time);
            distance = before.Distance + (lapSeconds - before.Time) * speed;
        }
    }
}
