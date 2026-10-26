using System;
using StadionTracking;
using UnityEditor;
using UnityEngine;

internal static class LapCheckpointTrackerChecks
{
    [MenuItem("Tools/Runner UI/Checks/Check lap checkpoints and best lap")]
    private static void Verify()
    {
        var tracker = new LapCheckpointTracker();
        Check(tracker.BestLapIsExample && Math.Abs(tracker.BestLapSeconds - 100) < 0.001,
            "example best lap starts at 1:40 with 25 second checkpoints");

        tracker.Update(0, 0);
        tracker.Update(100, 25);
        Check(tracker.HasCheckpointResult && Math.Abs(tracker.LastCheckpointDeltaSeconds) < 0.001 &&
            Math.Abs(tracker.LastCheckpointMetres - 100) < 0.001, "first split compares to the example");
        tracker.Update(400, 100);
        Check(Math.Abs(tracker.BestLapSeconds - 100) < 0.001 && tracker.BestLapIsExample,
            "matching sample lap does not replace the example record");
        Check(tracker.HasCheckpointResult && tracker.LastCheckpointLabel == "400 m (Runde)",
            "400m lap completion result is displayed and not swallowed");

        // After 21m past checkpoint, result expires and preview for next checkpoint is shown
        tracker.Update(421, 105.25);
        Check(!tracker.HasCheckpointResult && tracker.NextCheckpointLabel == "100 m",
            "checkpoint result expires after 20m and returns to preview");

        tracker.Update(500, 125);
        Check(tracker.HasCheckpointResult && Math.Abs(tracker.LastCheckpointDeltaSeconds) < 0.001,
            "the next lap resets its checkpoint split clock");
        tracker.Update(800, 195);
        Check(Math.Abs(tracker.BestLapSeconds - 95) < 0.001 && !tracker.BestLapIsExample,
            "a faster completed lap becomes the new personal best");
        Check(tracker.LastCheckpointIsRecord,
            "faster lap triggers record status");

        tracker.Update(900, 218.333333333);
        Check(Math.Abs(tracker.LastCheckpointDeltaSeconds + 0.416666667) < 0.001,
            "new lap checkpoint compares against the updated personal best");

        // 1 km milestone check at 1000m
        tracker.Update(950, 230);
        Check(tracker.NextCheckpointLabel == "1 km",
            "approaching 1000m previews the 1 km checkpoint");
        tracker.Update(1000, 240);
        Check(tracker.HasCheckpointResult && tracker.LastCheckpointLabel == "1 km",
            "crossing 1000m triggers the 1 km checkpoint result");

        // Custom target pace check (e.g. 6:00 min/km = 360 s/km)
        var pacedTracker = new LapCheckpointTracker(400, 360);
        Check(Math.Abs(pacedTracker.BestLapSeconds - 144) < 0.001,
            "paced tracker initializes 400m best lap to 144s for 6:00 pace");
        pacedTracker.Update(0, 0);
        pacedTracker.Update(400, 140);
        Check(!pacedTracker.BestLapIsExample && Math.Abs(pacedTracker.BestLapSeconds - 140) < 0.001,
            "running faster than target pace cracks the checkpoint and sets new PB");

        Debug.Log("[RunnerUI CHECK] Example splits, lap reset, personal-best update, 400m/1km checkpoints and expiration passed.");
    }

    private static void Check(bool passed, string label)
    {
        if (!passed) throw new InvalidOperationException("[Lap checkpoints] FAIL: " + label);
    }
}
