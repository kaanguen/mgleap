using System;
using StadionTracking;
using UnityEditor;
using UnityEngine;

internal static class TreadmillRunModeChecks
{
    [MenuItem("Tools/Treadmill/Verify both running modes in Play mode")]
    public static void Verify()
    {
        if (!EditorApplication.isPlaying) throw new InvalidOperationException("Enter Play mode first.");
        var world = UnityEngine.Object.FindFirstObjectByType<StraightTreadmillWorld>();
        var companion = UnityEngine.Object.FindFirstObjectByType<TreadmillCompanion>();
        Check(world != null && companion != null && world.PlacementReady, "world and companion ready");
        var originalMode = companion.Mode;
        float originalSpeed = world.SpeedKmh;
        var playback = UnityEngine.Object.FindFirstObjectByType<LapGhostPlayback>();
        Check(playback != null, "tempo partner playback ready");
        var tracker = new LapCheckpointTracker(400, RunnerSessionSettings.PaceSeconds);
        tracker.Update(world.TravelledMetres, 0);
        try
        {
            companion.SetMode(TreadmillRunMode.SideBySide);
            companion.Synchronize();
            playback.Synchronize(tracker, null, world, true);
            var sideAvatar = companion.Avatar;
            Check(sideAvatar != null && sideAvatar.activeInHierarchy, "side avatar visible");
            Check(playback.Avatar == null || !playback.Avatar.activeInHierarchy, "no second avatar in side mode");
            Vector3 sidePosition = sideAvatar.transform.position;
            world.SpeedKmh = 12;
            world.AdvanceSimulation(2);
            companion.Synchronize();
            Check(Vector3.Distance(sidePosition, sideAvatar.transform.position) < 0.0001f,
                "side mode preserves configured offset");
            Check(world.TrySetPaceMinutesPerKilometre(6) && Mathf.Abs(world.SpeedKmh - 10) < 0.0001f,
                "6:00 min/km = 10 km/h");
            Check(world.TrySetPace(7, 30) && Mathf.Abs(world.SpeedKmh - 8) < 0.0001f,
                "7:30 min/km = 8 km/h");
            Check(!world.TrySetPaceMinutesPerKilometre(float.NaN) && !world.TrySetPace(0, 60),
                "invalid pace cannot change speed");

            companion.SetMode(TreadmillRunMode.TempoPartner);
            companion.Synchronize();
            playback.Synchronize(tracker, null, world, true);
            var partnerAvatar = playback.Avatar;
            Check(!sideAvatar.activeInHierarchy && partnerAvatar != null && playback.IsPartnerActive,
                "tempo partner replaces side avatar");
            Check(Math.Abs(playback.DistanceIntoLap) < .001, "partner has no fixed three metre lead");
            companion.SetMode(TreadmillRunMode.SideBySide);
            companion.Synchronize();
            playback.Synchronize(tracker, null, world, true);
            Check(sideAvatar.activeInHierarchy && !partnerAvatar.activeInHierarchy, "switch back shows only side avatar");
            companion.SetMode(TreadmillRunMode.TempoPartner);
            companion.Synchronize();
            playback.Synchronize(tracker, null, world, true);
            Check(playback.Avatar == partnerAvatar && !sideAvatar.activeInHierarchy,
                "switching modes reuses one partner without duplicate spawning");
            Debug.Log("[Treadmill] PASS: side mode, tempo partner, pace conversion, no fixed lead, no simultaneous avatars.");
        }
        finally
        {
            companion.SetMode(originalMode);
            world.SpeedKmh = originalSpeed;
            companion.Synchronize();
            playback.Synchronize(tracker, null, world, true);
        }
    }

    private static void Check(bool value, string label)
    {
        if (!value) throw new InvalidOperationException("[Treadmill modes] FAIL: " + label);
    }
}
