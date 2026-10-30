using System;
using System.Reflection;
using Newtonsoft.Json.Linq;
using StadionTracking;
using UnityEditor;
using UnityEngine;

internal static class BleRemoteChecks
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;

    [MenuItem("Tools/Runner UI/Checks/Check unfiltered BLE discovery and remote telemetry")]
    public static void CheckDiscovery()
    {
        var root = new GameObject("BLE remote regression check");
        root.hideFlags = HideFlags.HideAndDontSave;
        root.SetActive(false);
        var profile = ScriptableObject.CreateInstance<BleDeviceProfile>();
        try
        {
            profile.nameContains = new[] { "tree", "thre" };
            profile.deviceId = "old-device";
            var ble = root.AddComponent<BleConnection>();
            ble.profile = profile;
            typeof(BleConnection).GetField("<Scanning>k__BackingField", Hidden).SetValue(ble, true);
            var found = typeof(BleConnection).GetMethod("OnDeviceFound", Hidden);
            found.Invoke(ble, new object[] { "AA:01", "Other manufacturer" });
            found.Invoke(ble, new object[] { "AA:02", null });
            found.Invoke(ble, new object[] { "aa:01", "Updated name" });
            found.Invoke(ble, new object[] { "", "Invalid identifier" });
            Require(ble.DeviceIds.Count == 2, "All devices are discovered without profile filters; duplicates are merged.");
            Require(ble.DeviceNames[0] == "Updated name" && ble.DeviceNames[1] == "Ohne Namen", "Names update and unnamed devices remain visible.");
            ble.SelectDevice(1);
            Require(!ble.TryConnectDevice("unknown", out string error) && !string.IsNullOrEmpty(error), "Unknown device IDs are rejected.");
            Require(ble.SelectedIndex == 1, "Rejected commands do not change selection.");
            var telemetryMethod = typeof(RunnerServerClient).GetMethod("BleTelemetry", BindingFlags.Static | BindingFlags.NonPublic);
            var telemetry = (JObject)telemetryMethod.Invoke(null, new object[] { ble, false, double.NaN, null });
            Require((bool)telemetry["available"] && (bool)telemetry["scanning"], "Discovery state is transmitted.");
            Require((string)telemetry["selectedDeviceId"] == "AA:02" && ((JArray)telemetry["devices"]).Count == 2, "Telemetry includes stable IDs and the full device list.");
            Require(telemetry["spo2"].Type == JTokenType.Null, "Unavailable sensor values stay null.");
            typeof(BleConnection).GetMethod("OnScanFinished", Hidden).Invoke(ble, null);
            found.Invoke(ble, new object[] { "late", "Late callback" });
            Require(ble.DeviceIds.Count == 2 && !ble.Scanning, "Late discovery callbacks are ignored after stopping.");
            typeof(BleConnection).GetField("requesting", Hidden).SetValue(ble, true);
            typeof(BleConnection).GetMethod("OnApplicationPause", Hidden).Invoke(ble, new object[] { true });
            Require(ble.Initializing, "A permission Activity pause must not cancel initialization.");
            typeof(BleConnection).GetMethod("OnApplicationPause", Hidden).Invoke(ble, new object[] { false });
            ble.StopScan();
            Require(!ble.Initializing, "Stop cancels permission/initialization waits.");
            Require(ble.Status == "Suche abgebrochen.", "Cancellation is not reported as an empty completed scan.");
            var absent = (JObject)telemetryMethod.Invoke(null, new object[] { null, false, double.NaN, null });
            Require(!(bool)absent["available"] && ((JArray)absent["devices"]).Count == 0, "Missing BLE components are explicit.");
            CheckBackgroundUi();
            Debug.Log("[BLE] PASS: unfiltered discovery, unnamed devices, deduplication, stable selection, cancellation and remote telemetry.");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(root);
            UnityEngine.Object.DestroyImmediate(profile);
        }
    }

    private static void CheckBackgroundUi()
    {
        var root = new GameObject("BLE background UI check", typeof(RectTransform), typeof(Canvas), typeof(CanvasGroup));
        root.hideFlags = HideFlags.HideAndDontSave;
        root.SetActive(false);
        try
        {
            var ble = root.AddComponent<BleConnection>();
            var panel = root.AddComponent<RunnerArPanel>();
            var ui = root.AddComponent<HeartRateUI>();
            var measurements = root.AddComponent<SensorMeasurements>();
            measurements.bluetooth = ble;
            typeof(RunnerServerClient).GetMethod("PrepareRemoteBle", BindingFlags.Static | BindingFlags.NonPublic)
                .Invoke(null, new object[] { ble });
            Require(ble.isActiveAndEnabled && measurements.isActiveAndEnabled, "Remote sensor data stays active.");
            Require(!panel.isActiveAndEnabled && !ui.enabled && !root.GetComponent<Canvas>().enabled,
                "Remote scan does not open the local panel or compete with the running HUD.");
            Require(root.GetComponent<CanvasGroup>().alpha == 0 && !root.GetComponent<CanvasGroup>().blocksRaycasts,
                "Hidden BLE UI cannot intercept headset input.");
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
