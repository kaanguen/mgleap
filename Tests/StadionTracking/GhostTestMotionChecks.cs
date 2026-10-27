using System;
using StadionTracking;

internal static class GhostTestMotionChecks
{
    private static void Near(double actual, double expected)
    {
        if (Math.Abs(actual - expected) > .00001) throw new Exception($"Expected {expected}, got {actual}");
    }
    public static void Main()
    {
        var tracker = new LapCheckpointTracker(400, 250);
        tracker.Update(0, 0); tracker.Update(200, 40); tracker.Update(400, 80);
        double bestSeconds = tracker.BestLapSeconds;
        int bestSamples = tracker.GhostRecording.BestSampleCount;
        var motion = new GhostTestMotion();
        motion.SetSpeed(36, 390);
        motion.Advance(2, true);
        Near(motion.Distance, 410); // Cross the finish independently of the runner.
        motion.SetSpeed(18, 123);
        Near(motion.Distance, 410); // Speed edits preserve the ghost position.
        motion.Advance(10, false);
        Near(motion.Distance, 410); // Session pause freezes independent motion.
        motion.Advance(2, true);
        Near(motion.Distance, 420);
        motion.SetSpeed(0, 0);
        motion.Advance(100, true);
        Near(motion.Distance, 420);
        motion.SetSpeed(null, 0);
        if (motion.Enabled) throw new Exception("Best-lap mode must disable test integration.");
        motion.SetSpeed(7.5, 73);
        Near(motion.Distance, 73); // Re-entry seeds the latest recorded ghost position.
        motion.Reset();
        Near(motion.Distance, 0);
        Near(motion.SpeedKmh, 7.5);
        foreach (double invalid in new[] { -1d, double.NaN, double.PositiveInfinity })
        {
            bool rejected = false;
            try { motion.SetSpeed(invalid, 0); } catch (ArgumentOutOfRangeException) { rejected = true; }
            if (!rejected) throw new Exception("Invalid speed accepted.");
        }
        if (!tracker.GhostRecording.HasRecording || tracker.GhostRecording.BestSampleCount != bestSamples)
            throw new Exception("Best recording changed during test mode.");
        Near(tracker.BestLapSeconds, bestSeconds);
        tracker.GhostRecording.Evaluate(20, 400, bestSeconds, out double distance, out double speed);
        Near(distance, 100); Near(speed, 5);
        Console.WriteLine("PASS: independent lap crossing, continuous speed edits, pause, zero speed, best-lap return, reset and input validation.");
    }
}
