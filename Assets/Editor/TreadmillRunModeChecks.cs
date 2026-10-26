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
        float originalSpeed = world.SpeedKmh, originalLead = companion.PacerLeadMetres;
        var geometry = new StadiumTrackGeometry();
        double start = new SerializedObject(world).FindProperty("ovalStartMetres").floatValue;
        try
        {
            companion.SetMode(TreadmillRunMode.SideBySide);
            companion.Synchronize();
            var avatar = companion.Avatar;
            Check(avatar != null, "one avatar spawned");
            Vector3 sidePosition = avatar.transform.position;
            world.SpeedKmh = 12;
            world.AdvanceSimulation(2);
            companion.Synchronize();
            Check(Vector3.Distance(sidePosition, avatar.transform.position) < 0.0001f, "side mode preserves configured offset");

            Check(world.TrySetPaceMinutesPerKilometre(6) && Mathf.Abs(world.SpeedKmh - 10) < 0.0001f,
                "6:00 min/km = 10 km/h");
            Check(world.TrySetPace(7, 30) && Mathf.Abs(world.SpeedKmh - 8) < 0.0001f, "7:30 min/km = 8 km/h");
            Check(!world.TrySetPaceMinutesPerKilometre(float.NaN) && !world.TrySetPace(0, 60) &&
                Mathf.Abs(world.SpeedKmh - 8) < 0.0001f, "invalid pace cannot change running speed");

            companion.PacerLeadMetres = 3;
            companion.SetMode(TreadmillRunMode.Pacer);
            bool sawCurveHeading = false;
            world.SpeedKmh = 36;
            for (int i = 0; i < 400; i++)
            {
                world.AdvanceSimulation(0.1f);
                companion.Synchronize();
                Vector3 local = world.transform.InverseTransformPoint(companion.GroundPosition);
                double projected = geometry.Project(1, local.x, local.z) * 400 / geometry.Length(1);
                double userDistance = ((start + world.TravelledMetres) % 400 + 400) % 400;
                double gap = ((projected - userDistance) % 400 + 400) % 400;
                Check(Math.Abs(gap - 3) < 0.0002, "pacer stays 3 m ahead along track, including lap seam");
                var sample = geometry.Evaluate(1, projected * geometry.Length(1) / 400);
                Vector3 tangent = world.transform.TransformDirection(new Vector3((float)sample.DirectionX, 0, (float)sample.DirectionZ));
                Check(Vector3.Angle(avatar.transform.forward, tangent) < 0.05f, "pacer faces own curve tangent");
                sawCurveHeading |= Quaternion.Angle(avatar.transform.rotation, world.RunnerRotation) > 1f;
                Check(Mathf.Abs(companion.AppliedSpeedMetresPerSecond - world.SpeedMetresPerSecond) < 0.0001f,
                    "pacer animation uses shared pace");
            }
            Check(sawCurveHeading, "pacer turns on curved sections");
            Vector3 paused = avatar.transform.position;
            Quaternion pausedRotation = avatar.transform.rotation;
            world.SpeedKmh = 0;
            world.AdvanceSimulation(10);
            companion.Synchronize();
            Check(avatar.transform.position == paused && avatar.transform.rotation == pausedRotation &&
                !companion.Animator.GetBool("isRunning"), "pause holds pacer and uses idle");
            companion.SetMode(TreadmillRunMode.SideBySide);
            companion.Synchronize();
            Check(companion.Avatar == avatar && companion.transform.childCount == 1, "mode switch reuses avatar");
            Check(Vector3.Distance(sidePosition, avatar.transform.position) < 0.0001f, "switch back restores side offset");
            Debug.Log("[Treadmill] PASS: both run modes, shared pace conversion, 3 m path lead through bends/lap seam, pause, no duplicate spawn.");
        }
        finally
        {
            companion.PacerLeadMetres = originalLead;
            companion.SetMode(originalMode);
            world.SpeedKmh = originalSpeed;
            companion.Synchronize();
        }
    }

    private static void Check(bool value, string label)
    {
        if (!value) throw new InvalidOperationException("[Treadmill modes] FAIL: " + label);
    }
}
