using System;
using System.Linq;
using StadionTracking;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

internal static class LapGhostChecks
{
    [MenuItem("Tools/Runner UI/Checks/Check lap ghost")]
    public static void Verify()
    {
        VerifyRecording();
        VerifyAvatar();
        Debug.Log("[LapGhost CHECK] PASS: target pace, uneven recorded pace, exact lap boundaries, " +
            "stops, resets, partial laps, overtaking, curve poses, animation, collision-free avatar and cleanup.");
    }

    private static void VerifyRecording()
    {
        var tracker = new LapCheckpointTracker(400, 250);
        tracker.Update(0, 0);
        Evaluate(tracker, 20, out double distance, out double speed);
        Check(Near(distance, 80) && Near(speed, 4), "initial ghost follows target pace");
        tracker.Update(100, 20);
        tracker.Update(100, 25); // Standing still is part of the recording.
        tracker.Update(200, 45);
        tracker.Update(390, 83);
        tracker.Update(410, 87); // Exact finish time is 85, not the 87-second sample time.
        Check(Near(tracker.BestLapSeconds, 85) && Near(tracker.LapElapsedSeconds, 2),
            "crossing sample is divided at the exact lap boundary");
        Check(tracker.GhostRecording.HasRecording && tracker.GhostRecording.BestSampleCount >= 6,
            "the new best preserves its motion samples");
        Evaluate(tracker, 10, out distance, out speed);
        Check(Near(distance, 50) && Near(speed, 5), "recorded motion is interpolated");
        Evaluate(tracker, 22, out distance, out speed);
        Check(Near(distance, 100) && Near(speed, 0), "recorded stops are replayed");
        Evaluate(tracker, 85, out distance, out speed);
        Check(Near(distance, 400) && Near(speed, 0), "ghost waits at finish without wrapping early");
        tracker.Update(800, 190);
        Evaluate(tracker, 10, out distance, out speed);
        Check(Near(distance, 50) && Near(tracker.BestLapSeconds, 85), "slower lap preserves the ghost");
        tracker.Update(0, 0);
        Check(tracker.GhostRecording.HasRecording && Near(tracker.LapElapsedSeconds, 0),
            "distance/clock reset retains best recording but resets playback");
        var partial = new LapCheckpointTracker();
        partial.Update(350, 0);
        partial.Update(400, 5);
        Check(!partial.GhostRecording.HasRecording, "partial first lap cannot become the ghost");
        partial.Update(800, 85);
        Check(partial.GhostRecording.HasRecording && Near(partial.BestLapSeconds, 80),
            "first full lap after partial start is recorded");
        var jump = new LapCheckpointTracker();
        jump.Update(0, 0);
        jump.Update(850, 170);
        Check(Near(jump.BestLapSeconds, 80) && Near(jump.LapElapsedSeconds, 10),
            "one sample can cross multiple laps without corrupting recording");
    }

    private static void VerifyAvatar()
    {
        var scene = EditorSceneManager.NewPreviewScene();
        LapGhostPlayback playback = null;
        try
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Jogger.prefab");
            Check(prefab != null, "avatar prefab exists");
            var root = Create(scene, "Ghost checks");
            var world = root.AddComponent<StraightTreadmillWorld>();
            var data = new SerializedObject(world);
            data.FindProperty("requireHeightCalibration").boolValue = false;
            data.FindProperty("followOval400m").boolValue = true;
            data.ApplyModifiedPropertiesWithoutUndo();
            world.ResetDistance();
            var companion = root.AddComponent<TreadmillCompanion>();
            data = new SerializedObject(companion);
            data.FindProperty("world").objectReferenceValue = world;
            data.FindProperty("avatarPrefab").objectReferenceValue = prefab;
            data.ApplyModifiedPropertiesWithoutUndo();
            companion.SetMode(TreadmillRunMode.TempoPartner);
            playback = root.AddComponent<LapGhostPlayback>();
            var cameraObject = Create(scene, "Tracked user for ghost clearance");
            var camera = cameraObject.AddComponent<Camera>();
            camera.enabled = false;
            camera.transform.position = world.RunnerGroundPosition + Vector3.up * 1.7f;
            data = new SerializedObject(world);
            data.FindProperty("headCamera").objectReferenceValue = camera;
            data.ApplyModifiedPropertiesWithoutUndo();
            var startTracker = new LapCheckpointTracker();
            startTracker.Update(0, 0);
            playback.Synchronize(startTracker, null, world, false);
            var initialAvatar = playback.Avatar;
            Vector3 initialPosition = initialAvatar.transform.position;
            Vector3 relativeStart = Quaternion.Inverse(world.RunnerRotation) *
                (initialPosition - world.RunnerGroundPosition);
            Check(initialAvatar.activeInHierarchy && Near(relativeStart.x, 1.22) && Near(relativeStart.z, 0),
                "ghost starts one lane beside the user without a false distance lead");
            camera.transform.position = new Vector3(initialPosition.x, camera.transform.position.y, initialPosition.z);
            playback.Synchronize(startTracker, null, world, false);
            Check(!initialAvatar.activeInHierarchy && Near(playback.DistanceIntoLap, 0),
                "ghost hides when tracked user moves into it without changing race progress");
            camera.transform.position = world.RunnerGroundPosition + Vector3.up * 1.7f;
            playback.Synchronize(startTracker, null, world, false);
            Check(initialAvatar.activeInHierarchy, "ghost becomes visible again after user moves away");
            companion.SetMode(TreadmillRunMode.SideBySide);
            companion.Synchronize();
            playback.Synchronize(startTracker, null, world, false);
            Check(!initialAvatar.activeInHierarchy && companion.Avatar != null && companion.Avatar.activeInHierarchy,
                "side mode displays only the side companion");
            var sideAvatar = companion.Avatar;
            companion.SetMode(TreadmillRunMode.TempoPartner);
            companion.Synchronize();
            playback.Synchronize(startTracker, null, world, false);
            Check(initialAvatar.activeInHierarchy && !sideAvatar.activeInHierarchy,
                "tempo partner mode displays only the same partner avatar");
            // Keep test measurements on the reference path.
            data = new SerializedObject(playback);
            data.FindProperty("laneOffsetMetres").floatValue = 0;
            data.ApplyModifiedPropertiesWithoutUndo();
            var tracker = new LapCheckpointTracker();
            tracker.Update(0, 0);
            world.SpeedKmh = 18; // 5 m/s; ghost target is 4 m/s.
            world.AdvanceSimulation(10);
            tracker.Update(world.TravelledMetres, 10);
            playback.Synchronize(tracker, null, world, true);
            var avatar = playback.Avatar;
            Check(avatar != null && avatar.activeInHierarchy, "ghost is spawned automatically");
            Check(Near(playback.GapMetres, 10), "runner can overtake independent target ghost");
            CheckPose(playback, world, -10);
            var animator = avatar.GetComponentInChildren<Animator>();
            Check(animator != null && !animator.applyRootMotion && animator.GetBool("isRunning"),
                "ghost animation follows its own speed without root motion");
            Check(avatar.GetComponentsInChildren<Collider>(true).All(c => !c.enabled), "no ghost collisions");
            Check(avatar.GetComponentsInChildren<Renderer>().All(r =>
                !r.enabled || r.sharedMaterial.shader.name == "StadionTracking/LapGhost"), "cyan ghost material applied");
            Check(!ShaderUtil.GetShaderMessages(Resources.Load<Shader>("LapGhost"))
                .Any(m => m.severity.ToString() == "Error"), "ghost shader has no compilation errors");

            world.SpeedKmh = 0;
            tracker.Update(world.TravelledMetres, 20);
            playback.Synchronize(tracker, null, world, true);
            Check(Near(playback.GapMetres, -30), "ghost passes stopped runner while race time continues");
            CheckPose(playback, world, 30);
            Vector3 before = avatar.transform.position;
            world.SessionPaused = true;
            world.SpeedKmh = 18; // Even incoming belt readings must not move a paused session.
            world.AdvanceSimulation(10);
            playback.Synchronize(tracker, null, world, false);
            Check(avatar.transform.position == before && Near(world.TravelledMetres, 50) && animator.speed == 0,
                "explicit pause freezes both simulation and ghost animation");
            world.SessionPaused = false;
            world.AdvanceSimulation(60);
            tracker.Update(world.TravelledMetres, 80);
            playback.Synchronize(tracker, null, world, true);
            CheckPose(playback, world, -30);
            Check(playback.Avatar == avatar, "updates reuse one ghost avatar");
            var bestTracker = new LapCheckpointTracker();
            bestTracker.Update(0, 0);
            playback.Synchronize(bestTracker, null, world, true);
            Check(!playback.IsRecordedLap, "partner starts with target pace");
            bestTracker.Update(100, 15);
            bestTracker.Update(400, 80);
            playback.Synchronize(bestTracker, null, world, true);
            Check(playback.IsRecordedLap && playback.Avatar == avatar && Near(playback.DistanceIntoLap, 0),
                "same partner switches to new best only at next lap start");

            // Stadium route uses absolute lap distance rather than treadmill-relative distance.
            var stadium = Create(scene, "Stadium ghost checks").AddComponent<StadiumTrackPreview>();
            var settings = new StadiumPreviewSettings { MarkerLocalRunDirection = Vector3.forward };
            Check(stadium.Initialize(null, Vector3.zero, Vector3.zero, Quaternion.identity,
                settings, out _), "stadium route initializes");
            stadium.GhostAvatarPrefab = prefab;
            var previousMode = RunnerSessionSettings.Mode;
            RunnerSessionSettings.Mode = TreadmillRunMode.TempoPartner;
            try { playback.Synchronize(tracker, stadium, null, true); }
            finally { RunnerSessionSettings.Mode = previousMode; }
            Check(playback.Avatar != avatar && playback.Avatar != null, "source switch replaces previous ghost");
            stadium.GetRunningPose(playback.DistanceIntoLap, out _, out Quaternion expectedRotation);
            Check(Quaternion.Angle(playback.Avatar.transform.rotation, expectedRotation) < 0.01f,
                "stadium ghost follows its own tangent");
            playback.Synchronize(tracker, null, null, false);
            Check(playback.Avatar == null, "leaving the session cleans up ghost");
        }
        finally
        {
            if (playback != null) UnityEngine.Object.DestroyImmediate(playback);
            EditorSceneManager.ClosePreviewScene(scene);
        }
    }

    private static void CheckPose(LapGhostPlayback playback, StraightTreadmillWorld world, float ahead)
    {
        world.GetRunningPose(ahead, out Vector3 position, out Quaternion rotation);
        Vector3 actual = playback.Avatar.transform.position;
        Check(Vector2.Distance(new Vector2(actual.x, actual.z), new Vector2(position.x, position.z)) < 0.001f,
            "treadmill ghost follows curved world position");
        Check(Quaternion.Angle(playback.Avatar.transform.rotation, rotation) < 0.01f,
            "treadmill ghost follows curved world heading");
    }

    private static GameObject Create(Scene scene, string name)
    {
        var value = new GameObject(name);
        SceneManager.MoveGameObjectToScene(value, scene);
        return value;
    }

    private static void Evaluate(LapCheckpointTracker tracker, double time, out double distance, out double speed) =>
        tracker.GhostRecording.Evaluate(time, tracker.LapLengthMetres, tracker.BestLapSeconds, out distance, out speed);
    private static bool Near(double a, double b) => Math.Abs(a - b) < 0.001;
    private static void Check(bool passed, string label)
    {
        if (!passed) throw new InvalidOperationException("[LapGhost] FAIL: " + label);
    }
}
