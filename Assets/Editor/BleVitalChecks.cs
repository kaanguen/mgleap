using System;
using System.Collections;
using System.Reflection;
using Newtonsoft.Json.Linq;
using StadionTracking;
using UnityEditor;
using UnityEngine;

internal static class BleVitalChecks
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;

    [MenuItem("Tools/Runner UI/Checks/Check BLE vital telemetry and connected device")]
    public static void CheckVitals()
    {
        var root = new GameObject("BLE vital telemetry check");
        root.hideFlags = HideFlags.HideAndDontSave;
        root.SetActive(false);
        var profile = ScriptableObject.CreateInstance<BleDeviceProfile>();
        BleConnection ble = null;
        try
        {
            profile.channels.Add(new BleChannel { id = "hr", serviceUuid = "180d", characteristicUuid = "2a37", format = BleValueFormat.HeartRate });
            profile.channels.Add(new BleChannel { id = "temperature", serviceUuid = "1809", characteristicUuid = "2a1c", format = BleValueFormat.Temperature });
            profile.channels.Add(new BleChannel { id = "oxygen", serviceUuid = "1822", characteristicUuid = "2a5f", format = BleValueFormat.PulseOximeter });
            ble = root.AddComponent<BleConnection>();
            var sensor = root.AddComponent<SensorMeasurements>();
            sensor.bluetooth = ble;
            sensor.oxygenFormat = SensorMeasurements.OxygenFormat.Standard11073;
            root.SetActive(true);
            Set(ble, "<Connected>k__BackingField", true);
            Set(ble, "<ActiveProfile>k__BackingField", profile);
            Set(ble, "connectedId", "AA:02");
            ble.DeviceIds.Add("AA:02"); ble.DeviceNames.Add("Test sensor");
            typeof(SensorMeasurements).GetMethod("Configure", Hidden).Invoke(sensor, new object[] { "AA:02" });
            var data = typeof(SensorMeasurements).GetMethod("OnData", Hidden);
            data.Invoke(sensor, new object[] { BleUuid.Normalize("180d"), BleUuid.Normalize("2a37"), new byte[] { 0, 72 } });
            data.Invoke(sensor, new object[] { BleUuid.Normalize("1809"), BleUuid.Normalize("2a1c"), new byte[] { 0, 0x42, 0x0e, 0, 0xfe } });
            data.Invoke(sensor, new object[] { BleUuid.Normalize("1822"), BleUuid.Normalize("2a5f"), new byte[] { 0x10, 98, 0, 74, 0, 0x19, 0xf0 } });
            Require(sensor.TryGetHeartRateBpm(out double hr) && hr == 72, "Heart rate stays numeric.");
            Require(sensor.TryGetTemperatureCelsius(out double temperature) && Math.Abs(temperature - 36.5) < .0001, "Celsius decoding.");
            Require(sensor.TryGetOxygenPercent(out double oxygen) && oxygen == 98, "Oxygen decoding.");
            Require(sensor.TryGetPulseBpm(out double pulse) && pulse == 74, "Pulse decoding.");
            Require(sensor.TryGetPerfusionPercent(out double pi) && pi == 2.5, "Perfusion decoding.");
            var telemetryMethod = typeof(RunnerServerClient).GetMethod("BleTelemetry", BindingFlags.Static | BindingFlags.NonPublic);
            Func<JObject> snapshot = () => (JObject)telemetryMethod.Invoke(null, new object[] { ble, true, oxygen, sensor });
            var telemetry = snapshot();
            Require((double)telemetry["hr"] == 72 && (double)telemetry["temperature"] == 36.5, "Vital values reach telemetry.");
            Require((string)telemetry["connectedDevices"][0]["name"] == "Test sensor" && (string)telemetry["deviceId"] == "AA:02", "Connected device identity reaches telemetry.");
            data.Invoke(sensor, new object[] { BleUuid.Normalize("1809"), BleUuid.Normalize("2a1c"), new byte[] { 1, 0xda, 3, 0, 0xff } });
            Require(sensor.TryGetTemperatureCelsius(out temperature) && Math.Abs(temperature - 37) < .0001, "Fahrenheit is converted to Celsius once.");
            data.Invoke(sensor, new object[] { BleUuid.Normalize("180d"), BleUuid.Normalize("2a37"), new byte[] { 0 } });
            Require(!sensor.TryGetHeartRateBpm(out hr) && snapshot()["hr"].Type == JTokenType.Null, "Malformed packets cannot reuse the previous reading.");
            foreach (var value in (IEnumerable)typeof(SensorMeasurements).GetField("ordered", Hidden).GetValue(sensor))
                value.GetType().GetField("At").SetValue(value, Time.realtimeSinceStartup - 1000);
            Require(!sensor.TryGetTemperatureCelsius(out temperature) && snapshot()["temperature"].Type == JTokenType.Null, "Stale temperature is not transmitted.");
            Set(ble, "<Connected>k__BackingField", false);
            telemetry = snapshot();
            Require(((JArray)telemetry["connectedDevices"]).Count == 0 && telemetry["spo2"].Type == JTokenType.Null, "Disconnect clears live device and values.");
            Debug.Log("[BLE] PASS: numeric vital values, temperature conversion, device identity, malformed packets, staleness and disconnect telemetry.");
        }
        finally
        {
            if (ble != null) { Set(ble, "<Connected>k__BackingField", false); Set(ble, "<ActiveProfile>k__BackingField", null); }
            UnityEngine.Object.DestroyImmediate(root);
            UnityEngine.Object.DestroyImmediate(profile);
        }
    }

    private static void Set(BleConnection ble, string field, object value) => typeof(BleConnection).GetField(field, Hidden).SetValue(ble, value);
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
}
