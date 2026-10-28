using System;
using System.IO;
using System.Net.Sockets;
using System.Reflection;
using StadionTracking;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

internal static class RunnerFeatureChecks
{
    private const BindingFlags HiddenInstance = BindingFlags.Instance | BindingFlags.NonPublic;

    [MenuItem("Tools/Runner UI/Checks/Check saved records and diagnostics")]
    public static void Verify()
    {
        VerifySavedRecords();
        VerifyDiagnosticsAndDisconnect();
        VerifyTrackingAndSceneIsolation();
        Debug.Log("[RunnerFeatures CHECK] PASS: record/ghost roundtrip, faster-only merge, backup recovery, isolated profiles/scenes, connection diagnostics, disconnect pause, tracking loss and repeated rescan.");
    }

    [MenuItem("Tools/Runner UI/Checks/Check connection timeout and address change")]
    public static void VerifyConnectionAttempts()
    {
        var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        int port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        var uri = new Uri("ws://127.0.0.1:" + port + "/ws");
        var type = typeof(RunnerSession).Assembly.GetType("StadionTracking.RunnerServerConnection");
        var read = type.GetMethod("TryRead", HiddenInstance);
        Func<object> create = () => Activator.CreateInstance(type, HiddenInstance, null, new object[] { uri, "editor-regression" }, null);
        var oldAttempt = (IDisposable)create();
        oldAttempt.Dispose();
        // Drain the old attempt's initial Connecting notification before checking cancellation.
        while ((bool)read.Invoke(oldAttempt, new object[] { null })) { }
        var currentAttempt = (IDisposable)create();
        bool sawConnecting = false, sawTimeout = false, sawDisconnect = false;
        string lastDetail = null;
        double deadline = EditorApplication.timeSinceStartup + 9;
        EditorApplication.CallbackFunction poll = null;
        poll = () =>
        {
            try
            {
                var oldArgs = new object[] { null };
                Check(!(bool)read.Invoke(oldAttempt, oldArgs), "cancelled address attempt must not report a late error or disconnect");
                var args = new object[] { null };
                while ((bool)read.Invoke(currentAttempt, args))
                {
                    object message = args[0];
                    string kind = message.GetType().GetField("Kind", HiddenInstance).GetValue(message).ToString();
                    string detail = (string)message.GetType().GetField("Detail", HiddenInstance).GetValue(message);
                    if (kind == "Error") lastDetail = detail;
                    if (kind == "Connecting") sawConnecting = detail.Contains(uri.GetLeftPart(UriPartial.Path));
                    if (kind == "Error") sawTimeout = detail.Contains("Zeitüberschreitung") && detail.Contains(uri.AbsoluteUri);
                    if (kind == "Disconnected") sawDisconnect = true;
                    args[0] = null;
                }
                if (!(sawConnecting && sawTimeout && sawDisconnect))
                {
                    Check(EditorApplication.timeSinceStartup < deadline, "connection result within nine seconds: connecting=" + sawConnecting + ", timeout=" + sawTimeout + ", disconnected=" + sawDisconnect + ", detail=" + lastDetail);
                    return;
                }
                Debug.Log("[RunnerConnection CHECK] PASS: cancelled attempt stays silent; replacement has independent events and reports its exact target, timeout and disconnect.");
            }
            catch (Exception e) { Debug.LogException(e); }
            currentAttempt.Dispose();
            listener.Stop();
            EditorApplication.update -= poll;
        };
        EditorApplication.update += poll;
    }

    private static void VerifySavedRecords()
    {
        string directory = Path.Combine(Path.GetTempPath(), "markerml2-record-check-" + Guid.NewGuid().ToString("N"));
        try
        {
            var storeType = typeof(RunnerSession).Assembly.GetType("StadionTracking.RunnerRecordsStore");
            object store = Activator.CreateInstance(storeType, HiddenInstance, null, new object[] { directory }, null);
            var save = storeType.GetMethod("SaveBetter", HiddenInstance);
            var load = storeType.GetMethod("Load", HiddenInstance);
            var tracker = new LapCheckpointTracker(400, 360);
            tracker.Update(0, 0); tracker.Update(100, 20); tracker.Update(200, 45);
            tracker.Update(300, 60); tracker.Update(400, 80); tracker.Update(1000, 200);
            save.Invoke(store, new object[] { "treadmill-400", tracker.ExportPersonalBest() });
            var args = new object[] { "treadmill-400", 400.0, null };
            var saved = (RunnerPersonalBest)load.Invoke(store, args);
            var restored = new LapCheckpointTracker(400, 400);
            Check(restored.TryRestorePersonalBest(saved), "stored snapshot restores into a new tracker");
            Check(restored.RecordedLapSeconds == 80 && restored.Recorded1KmSeconds == 200, "measured best times survive restart");
            restored.GhostRecording.Evaluate(20, 400, 80, out double distance, out _);
            Check(Math.Abs(distance - 100) < .001, "uneven ghost samples survive restart");
            var invalid = tracker.ExportPersonalBest();
            invalid.GhostTimes[1] = 0;
            Check(!restored.TryRestorePersonalBest(invalid) && restored.RecordedLapSeconds == 80, "invalid recording is rejected without replacing valid records");
            var slower = new LapCheckpointTracker(400, 360);
            slower.Update(0, 0); slower.Update(100, 15); slower.Update(200, 60); slower.Update(300, 95); slower.Update(400, 120);
            save.Invoke(store, new object[] { "treadmill-400", slower.ExportPersonalBest() });
            saved = (RunnerPersonalBest)load.Invoke(store, new object[] { "treadmill-400", 400.0, null });
            Check(saved.BestLapSeconds == 80 && saved.Fastest100mSeconds == 15 && saved.Fastest1KmSeconds == 200,
                "slower lap cannot replace PB, while a faster 100m is merged");
            Check(load.Invoke(store, new object[] { "stadium-400", 400.0, null }) == null, "stadium and treadmill profiles stay separate");
            File.WriteAllText(Path.Combine(directory, "treadmill-400.json"), "{broken");
            args = new object[] { "treadmill-400", 400.0, null };
            saved = (RunnerPersonalBest)load.Invoke(store, args);
            Check(saved != null && saved.BestLapSeconds == 80 && args[2] != null, "corrupt main file recovers from backup with a warning");
            var improved = new LapCheckpointTracker(400, 360);
            improved.Update(0, 0); improved.Update(400, 70);
            save.Invoke(store, new object[] { "treadmill-400", improved.ExportPersonalBest() });
            var backup = Newtonsoft.Json.JsonConvert.DeserializeObject<RunnerPersonalBest>(File.ReadAllText(Path.Combine(directory, "treadmill-400.json.bak")));
            Check(new LapCheckpointTracker(400).TryRestorePersonalBest(backup) && backup.BestLapSeconds == 80,
                "saving after recovery must not replace a valid backup with the corrupt primary");
            File.WriteAllText(Path.Combine(directory, "treadmill-400.json"), "{broken again");
            saved = (RunnerPersonalBest)load.Invoke(store, new object[] { "treadmill-400", 400.0, null });
            Check(saved != null && saved.BestLapSeconds == 80, "backup remains usable after a subsequent save and another primary failure");
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    private static void VerifyDiagnosticsAndDisconnect()
    {
        var assembly = typeof(RunnerSession).Assembly;
        var connectionType = assembly.GetType("StadionTracking.RunnerServerConnection");
        var describe = connectionType.GetMethod("DescribeFailure", BindingFlags.Static | BindingFlags.NonPublic);
        var uri = new Uri("ws://127.0.0.1:8081/ws");
        string timeout = (string)describe.Invoke(null, new object[] { new OperationCanceledException(), uri });
        string refused = (string)describe.Invoke(null, new object[] { new SocketException((int)SocketError.ConnectionRefused), uri });
        Check(timeout.Contains("Zeitüberschreitung") && timeout.Contains(uri.AbsoluteUri), "timeout includes the destination");
        Check(refused.Contains("abgelehnt") && refused.Contains(uri.AbsoluteUri), "connection refusal is distinct from timeout");
        var scene = EditorSceneManager.NewPreviewScene();
        try
        {
            var host = new GameObject("Connection state check");
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(host, scene);
            host.SetActive(false);
            var world = host.AddComponent<StraightTreadmillWorld>();
            var client = host.AddComponent<RunnerServerClient>();
            typeof(RunnerServerClient).GetField("world", HiddenInstance).SetValue(client, world);
            var setState = typeof(RunnerServerClient).GetMethod("SetSessionState", HiddenInstance);
            setState.Invoke(client, new object[] { RunnerSessionState.Running });
            var eventType = assembly.GetType("StadionTracking.RunnerConnectionEvent");
            var kindType = assembly.GetType("StadionTracking.RunnerConnectionEventKind");
            var process = typeof(RunnerServerClient).GetMethod("ProcessConnectionEvent", HiddenInstance);
            foreach (string kind in new[] { "Error", "Disconnected" })
            {
                object message = Activator.CreateInstance(eventType, HiddenInstance, null,
                    new object[] { Enum.Parse(kindType, kind), kind == "Error" ? timeout : null }, null);
                process.Invoke(client, new[] { message });
            }
            Check(client.State == RunnerSessionState.Paused && world.SessionPaused, "disconnect pauses a running session");
            Check(client.LastConnectionError == timeout, "disconnect does not overwrite the concrete failure");
        }
        finally { EditorSceneManager.ClosePreviewScene(scene); }
    }

    private static void VerifyTrackingAndSceneIsolation()
    {
        Check(RunnerTrackingStatus.Evaluate(true, false, false, false, false) == RunnerTrackingState.WaitingForMarker, "initial marker search");
        Check(RunnerTrackingStatus.Evaluate(true, true, false, false, false) == RunnerTrackingState.FloorDetected, "detected floor before placement");
        Check(RunnerTrackingStatus.Evaluate(true, true, true, true, true) == RunnerTrackingState.Placed, "valid floor and placed track");
        Check(RunnerTrackingStatus.Evaluate(false, true, true, true, true) == RunnerTrackingState.TrackingLost, "lost head tracking");
        Check(RunnerTrackingStatus.Evaluate(true, true, true, false, true) == RunnerTrackingState.TrackingLost, "lost confirmed floor tracking");
        var first = EditorSceneManager.NewPreviewScene();
        var second = EditorSceneManager.NewPreviewScene();
        try
        {
            var a = new GameObject("First scene source");
            var b = new GameObject("Second scene source");
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(a, first);
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(b, second);
            var world = a.AddComponent<StraightTreadmillWorld>();
            int revision = world.MarkerPlacementRevision;
            for (int i = 0; i < 3; i++) world.BeginMarkerPlacement();
            Check(world.MarkerPlacementRevision == revision + 3 && !world.PlacementReady && world.SessionPaused, "repeated rescan invalidates placement and stays paused");
            var sessionA = RunnerSession.GetOrCreate(a.transform);
            var sessionB = RunnerSession.GetOrCreate(b.transform);
            Check(sessionA != sessionB && sessionA.gameObject.scene == first && sessionB.gameObject.scene == second, "scenes own independent sessions");
            Check(RunnerSession.GetOrCreate(a.transform) == sessionA, "repeated source registration reuses a session");
        }
        finally { EditorSceneManager.ClosePreviewScene(first); EditorSceneManager.ClosePreviewScene(second); }
    }

    private static void Check(bool passed, string message)
    { if (!passed) throw new InvalidOperationException("[RunnerFeatures CHECK] FAIL: " + message); }
}
