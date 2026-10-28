using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using StadionTracking;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

internal static class RunnerStorageChecks
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;

    [MenuItem("Tools/Runner UI/Checks/Check background save retry and session reset")]
    public static async void Verify()
    {
        var scene = EditorSceneManager.NewPreviewScene();
        string directory = Path.Combine(Path.GetTempPath(), "markerml2-retry-check-" + Guid.NewGuid().ToString("N"));
        string blocked = Path.Combine(directory, "blocked");
        object persistence = null;
        RunnerSession session = null;
        Type persistenceType = typeof(RunnerSession).Assembly.GetType("StadionTracking.RunnerRecordPersistence");
        try
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(blocked, "temporarily unavailable storage");
            var storeType = typeof(RunnerSession).Assembly.GetType("StadionTracking.RunnerRecordsStore");
            object store = Activator.CreateInstance(storeType, Hidden, null, new object[] { blocked }, null);
            persistence = Activator.CreateInstance(persistenceType, Hidden, null, new object[] { store, 50 }, null);
            var host = new GameObject("Retry session world");
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(host, scene);
            var world = host.AddComponent<StraightTreadmillWorld>();
            session = RunnerSession.GetOrCreate(world);
            session.BindWorld(world);
            typeof(RunnerSession).GetField("recordsPersistence", Hidden).SetValue(session, persistence);
            typeof(RunnerSession).GetField("activeProfile", Hidden).SetValue(session, "treadmill-400");
            session.Checkpoints.Update(0, 0);
            session.Checkpoints.Update(400, 80);
            object fileGate = storeType.GetField("fileGate", Hidden).GetValue(store);
            Task load;
            Monitor.Enter(fileGate);
            try
            {
                typeof(RunnerSession).GetMethod("SavePersonalBest", Hidden).Invoke(session, null);
                load = (Task)persistenceType.GetMethod("LoadAsync", Hidden).Invoke(persistence, new object[] { "treadmill-400", 400.0 });
                Check(!load.IsCompleted, "load must run off the caller thread while the store is locked");
                Check((int)persistenceType.GetProperty("PendingCount", Hidden).GetValue(persistence) == 1,
                    "submission must return without waiting for file IO");
            }
            finally { Monitor.Exit(fileGate); }
            // Reset while the disk is unavailable. The queued personal best must remain recoverable.
            session.ResetSessionStats();
            await WaitUntil(() =>
            {
                typeof(RunnerSession).GetMethod("PollRecords", Hidden).Invoke(session, null);
                return persistenceType.GetProperty("Error", Hidden).GetValue(persistence) != null &&
                    session.Checkpoints.RecordedLapSeconds == 80;
            }, "pending best must survive a failed save and session reset");
            Check(session.HasPendingRecords, "failed write must not be acknowledged as saved");
            File.Delete(blocked);
            Directory.CreateDirectory(blocked);
            // No new record and no new save call: the worker must retry the old snapshot itself.
            await WaitUntil(() => (int)persistenceType.GetProperty("PendingCount", Hidden).GetValue(persistence) == 0,
                "write must retry after storage recovers without a new PB");
            var saved = Newtonsoft.Json.JsonConvert.DeserializeObject<RunnerPersonalBest>(File.ReadAllText(Path.Combine(blocked, "treadmill-400.json")));
            Check(saved.BestLapSeconds == 80 && persistenceType.GetProperty("Error", Hidden).GetValue(persistence) == null,
                "successful retry persists the snapshot and clears the failure");
            UnityEngine.Debug.Log("[RunnerStorage CHECK] PASS: nonblocking read/write submission, failed write stays pending across reset, queued ghost/PB restores without disk, retry succeeds without a new record.");
        }
        catch (Exception e) { UnityEngine.Debug.LogException(e); }
        finally
        {
            if (session != null) typeof(RunnerSession).GetField("recordsPersistence", Hidden).SetValue(session, null);
            if (persistence != null) await (Task)persistenceType.GetMethod("StopAsync", Hidden).Invoke(persistence, null);
            EditorSceneManager.ClosePreviewScene(scene);
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    private static async Task WaitUntil(Func<bool> ready, string message)
    {
        var watch = Stopwatch.StartNew();
        while (!ready())
        {
            if (watch.Elapsed.TotalSeconds > 10) throw new InvalidOperationException("[RunnerStorage CHECK] FAIL: " + message);
            await Task.Delay(25);
        }
    }
    private static void Check(bool passed, string message)
    { if (!passed) throw new InvalidOperationException("[RunnerStorage CHECK] FAIL: " + message); }
}
