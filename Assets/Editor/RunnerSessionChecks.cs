using System;
using System.Reflection;
using StadionTracking;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

internal static class RunnerSessionChecks
{
    private static readonly MethodInfo TickMethod = typeof(RunnerSession).GetMethod("Tick", BindingFlags.Instance | BindingFlags.NonPublic);

    [MenuItem("Tools/Runner UI/Checks/Check independent session and floor corrections")]
    public static void Verify()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Run these isolated checks outside Play mode.");
        var scene = EditorSceneManager.NewPreviewScene();
        var previousMode = RunnerSessionSettings.Mode;
        try
        {
            VerifyFloorCorrections(scene);
            VerifyLateSourceBinding(scene);
            VerifyIndependentSession(scene);
            VerifyCommandStates(scene);
            Debug.Log("[RunnerSession CHECK] PASS: floor refinements do not add distance; real movement survives continuous refinement; HUD disable/deactivation does not stop time, records or ghost; pause/reset/source switching and command states.");
        }
        finally
        {
            RunnerSessionSettings.Mode = previousMode;
            EditorSceneManager.ClosePreviewScene(scene);
        }
    }

    private static void VerifyLateSourceBinding(Scene scene)
    {
        var late = CreateWorld(scene, false);
        late.SpeedKmh = 18;
        late.SessionPaused = false;
        late.AdvanceSimulation(20); // The source already has 100 metres before it is bound.
        var session = RunnerSession.GetOrCreate(late);
        session.BindWorld(late);
        Tick(session, 1);
        Check(!session.Checkpoints.Recorded100mSeconds.HasValue && !session.Checkpoints.RecordedLapSeconds.HasValue &&
            !session.Checkpoints.GhostRecording.HasRecording, "late-bound 100m must not become a one-second record");
        late.AdvanceSimulation(20);
        Tick(session, 20);
        Check(session.Checkpoints.Recorded100mSeconds >= 20, "only movement measured after binding can produce a 100m record");
        session.ResetSessionStats(false);
        Tick(session, 1);
        Check(!session.Checkpoints.Recorded100mSeconds.HasValue, "reset without resetting the source must not recount its old metres");

        var partial = CreateWorld(scene, false);
        partial.SpeedKmh = 18; partial.SessionPaused = false;
        partial.AdvanceSimulation(30); // 150m: the first physical checkpoint is only 50m away.
        session.BindWorld(partial); Tick(session, 0);
        partial.AdvanceSimulation(10); Tick(session, 10);
        Check(!session.Checkpoints.Recorded100mSeconds.HasValue, "a partial first 100m split is not a full record");
        partial.AdvanceSimulation(20); Tick(session, 20);
        Check(Math.Abs(session.Checkpoints.Recorded100mSeconds.Value - 20) < .001, "the next complete 100m is measured normally");
        partial.AdvanceSimulation(20); Tick(session, 20);
        Check(!session.Checkpoints.RecordedLapSeconds.HasValue && !session.Checkpoints.GhostRecording.HasRecording,
            "the partial first lap must not produce a lap record or ghost");
        partial.AdvanceSimulation(80); Tick(session, 80);
        Check(Math.Abs(session.Checkpoints.RecordedLapSeconds.Value - 80) < .001 && session.Checkpoints.GhostRecording.HasRecording,
            "a subsequent complete lap produces a valid lap record and ghost");

        var fresh = CreateWorld(scene, false);
        fresh.SpeedKmh = 18; fresh.SessionPaused = false;
        session.BindWorld(fresh); // Capture zero here, before the world's first simulated frame.
        fresh.AdvanceSimulation(20); Tick(session, 20);
        Check(Math.Abs(session.Checkpoints.Recorded100mSeconds.Value - 20) < .001,
            "movement between initial binding and the first session tick must not be lost");

        var preview = Create(scene, "Late-bound preview").AddComponent<StadiumTrackPreview>();
        var settings = new StadiumPreviewSettings { MarkerLocalRunDirection = Vector3.forward, SpeedAtFullSlider = 5 };
        Check(preview.Initialize(null, Vector3.zero, Vector3.zero, Quaternion.identity, settings, out _), "late preview initializes");
        preview.SetRequestedSpeed(1); preview.AdvanceRun(21); // 105m before binding.
        session.BindRunner(preview); Tick(session, 1);
        Check(!session.Checkpoints.Recorded100mSeconds.HasValue && !session.Checkpoints.RecordedLapSeconds.HasValue,
            "late-bound preview must not count its existing distance as a new record");
        session.UnbindRunner(preview);
        session.UnbindWorld(fresh);
        foreach (var world in new[] { late, partial, fresh })
        {
            UnityEngine.Object.DestroyImmediate(world.HeadCamera.gameObject);
            UnityEngine.Object.DestroyImmediate(world.gameObject);
        }
        UnityEngine.Object.DestroyImmediate(preview.gameObject);
    }

    private static void VerifyFloorCorrections(Scene scene)
    {
        var world = CreateWorld(scene, true);
        world.SessionPaused = false;
        world.AdvanceSimulation(.1f);
        double initialDistance = world.TravelledMetres;
        for (int i = 0; i < 20; i++)
        {
            world.UpdateMarkerFloorPlane(new Vector3(0, .2f + i * .003f, 0), new Vector3(.02f + i * .001f, 1, .01f).normalized);
            world.AdvanceSimulation(.1f);
            Check(Math.Abs(world.TravelledMetres - initialDistance) < .0001, "stationary head must not gain distance during floor correction");
        }
        world.HeadCamera.transform.position += world.RunnerRotation * Vector3.forward * .5f;
        world.UpdateMarkerFloorPlane(new Vector3(0, .3f, 0), new Vector3(.05f, 1, .015f).normalized);
        world.AdvanceSimulation(.1f);
        Check(world.TravelledMetres > initialDistance + .2, "real walking must count even when the floor changes in the same interval");
        UnityEngine.Object.DestroyImmediate(world.HeadCamera.gameObject);
        UnityEngine.Object.DestroyImmediate(world.gameObject);
    }

    private static void VerifyIndependentSession(Scene scene)
    {
        var world = CreateWorld(scene, false);
        var companion = world.gameObject.AddComponent<TreadmillCompanion>();
        var data = new SerializedObject(companion);
        data.FindProperty("world").objectReferenceValue = world;
        data.FindProperty("avatarPrefab").objectReferenceValue = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Characters/JoggerRealistic/Jogger_Realistic.prefab");
        data.ApplyModifiedPropertiesWithoutUndo();
        companion.SetMode(TreadmillRunMode.TempoPartner);
        RunnerSessionSettings.Mode = TreadmillRunMode.TempoPartner;
        var session = RunnerSession.GetOrCreate(world);
        session.BindWorld(world);
        session.BindCompanion(companion);
        Tick(session, 0);
        var hud = Create(scene, "Disabled test HUD");
        hud.AddComponent<Canvas>();
        var display = hud.AddComponent<ActiveStatsDisplay>();
        display.enabled = false;
        hud.SetActive(false);
        Check(!session.transform.IsChildOf(hud.transform), "session must live outside the HUD hierarchy");
        world.SpeedKmh = 18;
        world.SessionPaused = false;
        world.AdvanceSimulation(20);
        Tick(session, 20);
        Check(Math.Abs(session.ElapsedSeconds - 20) < .001, "clock runs with HUD disabled and inactive");
        Check(session.Checkpoints.Recorded100mSeconds.HasValue, "checkpoints and measured records run without HUD");
        Check(session.GhostPlayback.IsPartnerActive && session.GhostPlayback.TravelledMetres > 0, "ghost runs without HUD");
        double ghostDistance = session.GhostPlayback.TravelledMetres;
        world.SessionPaused = true;
        Tick(session, 5);
        Check(Math.Abs(session.ElapsedSeconds - 20) < .001 && Math.Abs(session.GhostPlayback.TravelledMetres - ghostDistance) < .001, "explicit pause freezes session and ghost");
        var checkpoints = session.Checkpoints;
        world.enabled = false;
        session.UnbindWorld(world);
        Tick(session, 2);
        Tick(session, 3);
        Check(session.ElapsedSeconds == 20 && session.TravelledMetres == 100 && session.Checkpoints == checkpoints &&
            Math.Abs(session.GhostPlayback.TravelledMetres - ghostDistance) < .001,
            "several disabled frames retain clock, distance, checkpoint identity and ghost progress");
        world.enabled = true;
        session.BindWorld(world);
        world.SessionPaused = false;
        world.AdvanceSimulation(1);
        Tick(session, 1);
        Check(session.ElapsedSeconds == 21 && session.TravelledMetres == 105 && session.Checkpoints == checkpoints &&
            session.GhostPlayback.TravelledMetres > ghostDistance,
            "reenabling the same world continues the existing run");
        world.SessionPaused = true;
        session.ResetSessionStats();
        Check(session.ElapsedSeconds == 0 && !session.Checkpoints.Recorded100mSeconds.HasValue, "reset clears clock and measured records");
        var route = Create(scene, "Registered stadium source").AddComponent<StadiumTrackPreview>();
        var settings = new StadiumPreviewSettings { MarkerLocalRunDirection = Vector3.forward };
        Check(route.Initialize(null, Vector3.zero, Vector3.zero, Quaternion.identity, settings, out _), "preview source initializes");
        session.BindRunner(route);
        Tick(session, 0);
        Check(session.Runner == route && session.World == null, "explicit preview binding replaces the treadmill source");
        route.SetRequestedSpeed(1);
        route.AdvanceRun(1);
        Tick(session, 1);
        float previewElapsed = session.ElapsedSeconds;
        route.enabled = false;
        session.UnbindRunner(route);
        Tick(session, 3);
        Check(session.ElapsedSeconds == previewElapsed && session.World == null, "disabled preview suspends instead of silently switching to treadmill");
        route.enabled = true;
        session.BindRunner(route);
        Tick(session, 0);
        Check(session.ElapsedSeconds == previewElapsed, "reenabling the same preview retains its run");
        session.UnbindRunner(route);
        Tick(session, 0);
        Check(session.World == world && session.ElapsedSeconds == 0, "unbinding the preview restores the registered world without scene searches");
        session.UnbindWorld(world);
        Tick(session, 1);
        Check(!session.HasActiveSource && !session.GhostPlayback.IsPartnerActive, "unbinding all sources stops the ghost");
        UnityEngine.Object.DestroyImmediate(hud);
        UnityEngine.Object.DestroyImmediate(route.gameObject);
        UnityEngine.Object.DestroyImmediate(world.HeadCamera.gameObject);
        UnityEngine.Object.DestroyImmediate(world.gameObject);
    }

    private static void VerifyCommandStates(Scene scene)
    {
        var world = CreateWorld(scene, false);
        var host = world.gameObject;
        var client = host.AddComponent<RunnerServerClient>();
        var session = RunnerSession.GetOrCreate(world);
        session.BindWorld(world);
        SetField(client, "world", world);
        SetField(client, "session", session);
        var apply = typeof(RunnerServerClient).GetMethod("Apply", BindingFlags.Instance | BindingFlags.NonPublic);
        foreach (string action in new[] { "resume", "pause", "stop", "reset" })
        {
            apply.Invoke(client, new object[] { "runningMate.command", new Newtonsoft.Json.Linq.JObject { ["action"] = action } });
            string expected = action == "resume" ? "Running" : action == "pause" ? "Paused" : action == "stop" ? "Stopped" : "Ready";
            Check(GetField(client, "state").ToString() == expected && world.SessionPaused == (expected != "Running"), "command state and pause agree: " + action);
        }
        apply.Invoke(client, new object[] { "scene.setTrackEnvironment", new Newtonsoft.Json.Linq.JObject { ["environment"] = "Stadium" } });
        Check(GetField(client, "state").ToString() == "Ready" && world.SessionPaused && world.RealTrackMode, "environment change enters Ready and remains paused");
        apply.Invoke(client, new object[] { "runningMate.configure", new Newtonsoft.Json.Linq.JObject { ["mode"] = "SideBySide", ["paceSeconds"] = 300 } });
        Check(GetField(client, "state").ToString() == "Ready" && world.SessionPaused, "configuration enters Ready and remains paused");
        apply.Invoke(client, new object[] { "scene.rescanMarker", new Newtonsoft.Json.Linq.JObject() });
        Check(GetField(client, "state").ToString() == "Stopped" && world.SessionPaused && !world.PlacementReady, "rescan stops and invalidates placement");
        UnityEngine.Object.DestroyImmediate(world.HeadCamera.gameObject);
        UnityEngine.Object.DestroyImmediate(host);
    }

    private static StraightTreadmillWorld CreateWorld(Scene scene, bool stadium)
    {
        var world = Create(scene, "Session check world").AddComponent<StraightTreadmillWorld>();
        var camera = Create(scene, "Session check head").AddComponent<Camera>();
        camera.enabled = false;
        camera.transform.position = Vector3.up * 1.7f;
        var data = new SerializedObject(world);
        data.FindProperty("headCamera").objectReferenceValue = camera;
        data.FindProperty("followOval400m").boolValue = true;
        data.ApplyModifiedPropertiesWithoutUndo();
        if (stadium) world.SetTrackEnvironment(true);
        Check(world.PlaceFromMarkerPlane(Vector3.zero, Vector3.forward, Vector3.up), "marker floor placement succeeds");
        return world;
    }

    private static GameObject Create(Scene scene, string name)
    {
        var value = new GameObject(name);
        SceneManager.MoveGameObjectToScene(value, scene);
        return value;
    }
    private static void Tick(RunnerSession session, float seconds) => TickMethod.Invoke(session, new object[] { seconds });
    private static void SetField(object value, string name, object field) => value.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(value, field);
    private static object GetField(object value, string name) => value.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(value);
    private static void Check(bool passed, string message) { if (!passed) throw new InvalidOperationException("[RunnerSession CHECK] FAIL: " + message); }
}
