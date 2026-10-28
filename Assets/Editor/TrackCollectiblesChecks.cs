using System;
using System.Reflection;
using System.Collections;
using Newtonsoft.Json.Linq;
using StadionTracking;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

internal static class TrackCollectiblesChecks
{
    [MenuItem("Tools/Treadmill/Checks/Check floating collectibles")]
    public static void Verify()
    {
        var scene = EditorSceneManager.NewPreviewScene();
        try
        {
            var host = new GameObject("Pickup test world");
            SceneManager.MoveGameObjectToScene(host, scene);
            var cameraObject = new GameObject("Pickup test head");
            SceneManager.MoveGameObjectToScene(cameraObject, scene);
            var camera = cameraObject.AddComponent<Camera>();
            camera.transform.position = new Vector3(0, 1.7f, 0);
            var world = host.AddComponent<StraightTreadmillWorld>();
            Set(world, "headCamera", camera);
            Set(world, "requireMarkerPlacement", false);
            Set(world, "requireHeightCalibration", false);
            world.ResetDistance();
            world.ConfigureUserSpeed(RunnerSpeedSource.FixedTest, 36);
            var pickups = host.AddComponent<TrackCollectibles>();
            Set(pickups, "world", world);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(TrackCollectiblesSetup.PrefabPath);
            Check(prefab != null, "default prefab exists");
            Set(pickups, "collectiblePrefab", prefab);
            pickups.Tick(0);
            var root = (Transform)Get(pickups, "container");
            Check(pickups.ActiveCount > 15 && pickups.ActiveCount < 40, "bounded rows spawned ahead");
            var first = root.GetChild(0);
            Vector3 initial = first.position;
            Check(initial.z > 5 && initial.y > .6f && initial.y < 1.5f, "floating in front of runner");
            pickups.Tick(.3f);
            Check(Mathf.Abs(first.position.y - initial.y) > .005f, "hover animates");
            world.SessionPaused = true;
            initial = first.position;
            world.AdvanceSimulation(1);
            pickups.Tick(1);
            Check(first.position == initial && pickups.CollectedCount == 0, "pause freezes movement and collection");
            world.SessionPaused = false;
            int notified = 0;
            pickups.Collected += count => notified = count;
            world.AdvanceSimulation(.3f);
            pickups.Tick(.3f);
            Check(first.position.z < initial.z - 2.9f, "moves toward the user with the track");
            world.AdvanceSimulation(1);
            pickups.Tick(1);
            Check(pickups.CollectedCount == 4 && notified == 4, "swept pickup catches four spheres in one fast frame");
            int allocated = root.childCount;
            for (int i = 0; i < 150; i++) { world.AdvanceSimulation(.1f); pickups.Tick(.1f); }
            Check(root.childCount <= allocated + 4, "pool stays bounded over repeated rows");
            world.ResetDistance(); pickups.Tick(0);
            Check(pickups.CollectedCount == 0, "reset restarts the pickup count");
            camera.transform.position = new Vector3(3, 1.7f, 0);
            world.AdvanceSimulation(1.3f); pickups.Tick(1.3f);
            Check(pickups.CollectedCount == 0, "neighbouring lanes do not collect");
            camera.transform.position = new Vector3(0, 1.7f, 0);
            world.ResetDistance();
            Set(world, "followOval400m", true);
            world.ResetDistance(); pickups.ResetPickups(); pickups.Tick(0);
            root = (Transform)Get(pickups, "container");
            world.GetLanePoseAtDistance(6, 1, out Vector3 ground, out Quaternion direction);
            Check(Vector3.Distance(root.GetChild(0).position, ground) < 1.5f, "oval pickups follow shared lane geometry");
            var replacement = new GameObject("Replacement visual");
            SceneManager.MoveGameObjectToScene(replacement, scene);
            replacement.transform.localScale = Vector3.one * .5f;
            Set(pickups, "collectiblePrefab", replacement); pickups.Tick(0);
            root = (Transform)Get(pickups, "container");
            Check(root.GetChild(0).name.StartsWith("Replacement visual") && root.GetChild(0).localScale == Vector3.one * .5f,
                "replacement needs no collider or script and retains prefab scale");
            VerifySpawnModes(world, pickups, scene);
            root = (Transform)Get(pickups, "container");
            world.ShowTrack = false; pickups.Tick(0);
            Check(!root.gameObject.activeSelf, "hidden track hides pickups");
            // Edit-mode preview scenes do not dispatch MonoBehaviour destruction callbacks.
            typeof(TrackCollectibles).GetMethod("OnDestroy", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(pickups, null);
            UnityEngine.Object.DestroyImmediate(pickups);
            Check(root == null, "component cleanup removes independent pool root");
            Debug.Log("[TrackCollectibles CHECK] PASS: wave, approach, pause, fast-frame collection, events, pooling, reset, lane misses, oval placement, prefab replacement, visibility, cleanup, oxygen packets, density changes, stale/disconnected/invalid data, fixed mode and server validation.");
        }
        finally { EditorSceneManager.ClosePreviewScene(scene); }
    }

    private static void VerifySpawnModes(StraightTreadmillWorld world, TrackCollectibles pickups, Scene scene)
    {
        var sensorObject = new GameObject("Oxygen fixture");
        SceneManager.MoveGameObjectToScene(sensorObject, scene);
        var ble = sensorObject.AddComponent<BleConnection>();
        var sensor = sensorObject.AddComponent<SensorMeasurements>();
        sensor.bluetooth = ble;
        sensor.oxygenFormat = SensorMeasurements.OxygenFormat.Standard11073;
        Set(ble, "<Connected>k__BackingField", true);
        var channel = new BleChannel { format = BleValueFormat.PulseOximeter, serviceUuid = "1822", characteristicUuid = "2a5f", staleSeconds = 15 };
        var value = Activator.CreateInstance(typeof(SensorMeasurements).GetNestedType("Value", BindingFlags.NonPublic), true);
        Set(value, "Channel", channel);
        ((IList)Get(sensor, "ordered")).Add(value);
        ((IDictionary)Get(sensor, "byChannel")).Add(BleUuid.Key(channel.Service, channel.Characteristic), value);
        var receive = typeof(SensorMeasurements).GetMethod("OnData", BindingFlags.Instance | BindingFlags.NonPublic);
        Action<byte[]> feed = bytes => receive.Invoke(sensor, new object[] { channel.Service, channel.Characteristic, bytes });
        Set(pickups, "oxygenSensor", sensor);
        world.ResetDistance(); pickups.ResetPickups();
        pickups.ConfigureSpawning(CollectibleSpawnSource.OxygenSensor, 10);
        feed(new byte[] { 0, 90, 0, 70, 0 }); pickups.Tick(0);
        Check(sensor.TryGetOxygenPercent(out double oxygen) && oxygen == 90, "numeric oxygen from standard PLX packet");
        int few = pickups.ActiveCount;
        Check(pickups.CurrentRowCount == 2 && few > 0, "low oxygen uses minimum game density");
        feed(new byte[] { 0, 100, 0, 70, 0 }); pickups.Tick(0);
        Check(pickups.CurrentRowCount == 20 && pickups.ActiveCount > few, "higher oxygen increases visible amount immediately");
        var active = (IList)Get(pickups, "active");
        Check(Math.Abs((double)Get(active[0], "Distance") - 6) < .001, "density changes retain the first row anchor");
        Set(value, "At", Time.realtimeSinceStartup - 16); pickups.Tick(0);
        Check(!sensor.TryGetOxygenPercent(out oxygen) && pickups.ActiveCount == 0, "stale data stops sensor spawns");
        feed(new byte[] { 4, 98, 0, 70, 0, 0, 4 }); pickups.Tick(0);
        Check(!pickups.OxygenReady && pickups.ActiveCount == 0, "PLX warning excludes oxygen from gameplay");
        feed(new byte[] { 0, 255, 7, 70, 0 }); pickups.Tick(0);
        Check(!pickups.OxygenReady, "non-finite oxygen excluded");
        feed(new byte[] { 0, 101, 0, 70, 0 }); pickups.Tick(0);
        Check(!pickups.OxygenReady, "out-of-range oxygen excluded");
        feed(new byte[] { 0, 98, 0, 70, 0 });
        Set(ble, "<Connected>k__BackingField", false); pickups.Tick(0);
        Check(!pickups.OxygenReady && pickups.ActiveCount == 0, "disconnect stops sensor spawns immediately");
        pickups.ConfigureSpawning(CollectibleSpawnSource.Fixed, 12); pickups.Tick(0);
        Check(pickups.CurrentRowCount == 12 && pickups.ActiveCount > 0, "fixed amount works without a connected sensor");
        pickups.ConfigureSpawning(CollectibleSpawnSource.Fixed, 0); pickups.Tick(0);
        Check(pickups.ActiveCount == 0, "fixed zero switches pickups off");
        var client = world.gameObject.AddComponent<RunnerServerClient>();
        Set(client, "world", world);
        var apply = typeof(RunnerServerClient).GetMethod("Apply", BindingFlags.Instance | BindingFlags.NonPublic);
        apply.Invoke(client, new object[] { "runningMate.setCollectibles", new JObject { ["source"] = "Fixed", ["fixedCount"] = 17 } });
        Check(pickups.SpawnSource == CollectibleSpawnSource.Fixed && pickups.FixedCount == 17, "dashboard command configures fixed count");
        foreach (var invalid in new[] {
            new JObject { ["source"] = "Bad", ["fixedCount"] = 12 },
            new JObject { ["source"] = "OxygenSensor", ["fixedCount"] = 1.5 },
            new JObject { ["source"] = "OxygenSensor", ["fixedCount"] = 31 },
            new JObject { ["source"] = "Fixed", ["unknown"] = true } })
        {
            bool rejected = false;
            try { apply.Invoke(client, new object[] { "runningMate.setCollectibles", invalid }); }
            catch (TargetInvocationException e) { rejected = e.InnerException is ArgumentException; }
            Check(rejected && pickups.SpawnSource == CollectibleSpawnSource.Fixed && pickups.FixedCount == 17, "invalid commands rejected without partial changes");
        }
        apply.Invoke(client, new object[] { "runningMate.setCollectibles", new JObject { ["source"] = "OxygenSensor" } });
        Check(pickups.SpawnSource == CollectibleSpawnSource.OxygenSensor && pickups.FixedCount == 17, "sensor selection preserves fixed count");
        pickups.ConfigureSpawning(CollectibleSpawnSource.Fixed, 10); pickups.Tick(0);
    }

    private static object Get(object target, string name) => target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public).GetValue(target);
    private static void Set(object target, string name, object value) => target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public).SetValue(target, value);
    private static void Check(bool condition, string message)
    { if (!condition) throw new InvalidOperationException("[TrackCollectibles CHECK] FAIL: " + message); }
}
