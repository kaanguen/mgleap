using System;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace StadionTracking
{
    public sealed partial class RunnerServerClient
    {
        private void SendTelemetry()
        {
            var run = new JObject { ["state"] = state.ToString(), ["distance"] = world.TravelledMetres,
                ["elapsed"] = session.ElapsedSeconds, ["laps"] = world.CompletedLaps,
                ["lapLength"] = 400, ["speed"] = world.SessionPaused ? 0 : world.SpeedMetresPerSecond,
                ["speedKmh"] = world.RealTrackMode ? world.SpeedMetresPerSecond * 3.6f : world.SpeedKmh, ["mode"] = RunnerSessionSettings.Mode.ToString(),
                ["heightCm"] = world.BodyHeightCm, ["paceSeconds"] = RunnerSessionSettings.PaceSeconds,
                ["placementReady"] = world.PlacementReady, ["trackVisible"] = world.ShowTrack };
            var ghost = session.GhostPlayback;
            run["userSpeedSource"] = world.RealTrackMode ? "HeadTracking" : world.UserSpeedSource.ToString();
            run["fixedUserSpeedKmh"] = world.FixedTestSpeedKmh;
            run["treadmillSignalReady"] = world.TreadmillSignalReady;
            run["environment"] = world.RealTrackMode ? "Stadium" : "Treadmill";
            run["avatarLane"] = world.AvatarLane;
            run["stadiumCalibration"] = new JObject { ["pointA"] = world.TrackPointASet,
                ["calibrated"] = world.RealTrackCalibrated, ["baselineMetres"] = world.CalibrationBaselineMetres,
                ["raceStartTrackMetres"] = world.RaceStartTrackMetres };
            run["markerPlacement"] = new JObject {
                ["required"] = world.RequiresMarkerPlacement, ["floorConfirmed"] = world.MarkerFloorConfirmed,
                ["markerId"] = markerTracker?.RunningTrackMarkerId?.ToString(),
                ["status"] = markerTracker != null ? markerTracker.RunningTrackStatus : "Marker-Tracker fehlt." };
            run["records"] = new JObject {
                ["fastest100mSeconds"] = session.Checkpoints.Recorded100mSeconds,
                ["bestLapSeconds"] = session.Checkpoints.RecordedLapSeconds,
                ["fastest1KmSeconds"] = session.Checkpoints.Recorded1KmSeconds };
            run["trackingState"] = session.TrackingState.ToString();
            run["trackingStatus"] = session.TrackingDescription;
            run["recordsStatus"] = session.RecordsStatus;
            run["headPoseReady"] = world.HeadPoseReady;
            run["heightConfirmed"] = world.HeightConfirmed;
            run["calibration"] = new JObject { ["along"] = world.CalibrationOffsets.y,
                ["across"] = world.CalibrationOffsets.x, ["heading"] = world.CalibrationOffsets.z,
                ["surfaceHeight"] = world.SurfaceHeight };
            run["placement"] = companion == null ? null : new JObject {
                ["laneActive"] = companion.UseSelectedLane || world.RealTrackMode, ["position"] = companion.PlacementPosition, ["facing"] = companion.PlacementFacing,
                ["distance"] = companion.PlacementDistance, ["across"] = companion.PlacementOffset.x,
                ["along"] = companion.PlacementOffset.z };
            run["trackPositionMetres"] = world.RealTrackMode ? world.UserTrackPositionMetres : (world.TrackStartMetres + world.TravelledMetres) % 400;
            run["ghost"] = ghost == null ? null : new JObject {
                ["active"] = ghost.IsPartnerActive, ["distance"] = ghost.TravelledMetres,
                ["trackPositionMetres"] = world.GetTrackDistance(ghost.TravelledMetres) % 400,
                ["speedKmh"] = ghost.CurrentSpeedKmh, ["gapMetres"] = ghost.GapMetres,
                ["testMode"] = ghost.TestSpeedEnabled, ["testSpeedKmh"] = ghost.TestSpeedKmh,
                ["recordedLap"] = ghost.IsRecordedLap, ["hasRecording"] = ghost.HasRecordedLap,
                ["playbackMode"] = ghost.PlaybackMode };
            run["behaviour"] = avatarBehaviour == null ? null : new JObject {
                ["active"] = avatarBehaviour.Active, ["animationId"] = avatarBehaviour.AnimationId,
                ["label"] = avatarBehaviour.AnimationLabel, ["loop"] = avatarBehaviour.Loop,
                ["speed"] = avatarBehaviour.PlaybackSpeed, ["elapsed"] = avatarBehaviour.ElapsedSeconds,
                ["duration"] = avatarBehaviour.DurationSeconds };
            run["warmup"] = WarmupTelemetry();
            var ble = FindBleConnection();
            var pickups = world.GetComponent<TrackCollectibles>();
            run["collectibles"] = pickups == null ? null : new JObject {
                ["source"] = pickups.SpawnSource.ToString(), ["fixedCount"] = pickups.FixedCount,
                ["rowCount"] = pickups.CurrentRowCount, ["oxygenReady"] = pickups.OxygenReady,
                ["oxygenPercent"] = pickups.OxygenPercent.HasValue ? new JValue(pickups.OxygenPercent.Value) : JValue.CreateNull(),
                ["collected"] = pickups.CollectedCount };
            var sensor = FindBleMeasurements(ble);
            double oxygen = double.NaN;
            bool oxygenReady = sensor != null && sensor.TryGetOxygenPercent(out oxygen);
            var hudGroup = statsCanvas != null ? statsCanvas.GetComponent<CanvasGroup>() : null;
            Queue(new JObject { ["type"] = "telemetry", ["data"] = new JObject {
                ["treadmill"] = run, ["runningMate"] = run.DeepClone(),
                ["ble"] = BleTelemetry(ble, oxygenReady, oxygen, sensor),
                ["huds"] = new JObject { ["stadium"] = statsCanvas != null && (hudGroup == null || hudGroup.alpha > 0) } } });
        }

        private BleConnection FindBleConnection()
        {
            foreach (var candidate in FindObjectsByType<BleConnection>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (candidate.gameObject.scene == gameObject.scene) return candidate;
            return null;
        }

        private static void ActivateBleHierarchy(Transform node)
        {
            if (node.parent != null) ActivateBleHierarchy(node.parent);
            if (!node.gameObject.activeSelf) node.gameObject.SetActive(true);
        }

        internal static void PrepareRemoteBle(BleConnection ble)
        {
            // The transport lives on the legacy sensor canvas. Keep that canvas out of
            // panel arbitration while its data components run for dashboard commands.
            var canvas = ble.GetComponentInParent<Canvas>(true);
            if (canvas != null)
            {
                var panel = canvas.GetComponent<RunnerArPanel>();
                if (panel != null) panel.enabled = false;
                var localUi = canvas.GetComponent<HeartRateUI>();
                if (localUi != null) localUi.enabled = false;
                canvas.enabled = false;
                var group = canvas.GetComponent<CanvasGroup>();
                if (group != null) { group.alpha = 0; group.interactable = group.blocksRaycasts = false; }
            }
            ActivateBleHierarchy(ble.transform);
            ble.enabled = true;
            foreach (var measurements in FindObjectsByType<SensorMeasurements>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (measurements.gameObject.scene == ble.gameObject.scene && measurements.bluetooth == ble)
                { ActivateBleHierarchy(measurements.transform); measurements.enabled = true; }
        }

        private static SensorMeasurements FindBleMeasurements(BleConnection ble)
        {
            if (ble == null) return null;
            foreach (var sensor in FindObjectsByType<SensorMeasurements>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (sensor.gameObject.scene == ble.gameObject.scene && sensor.bluetooth == ble) return sensor;
            return null;
        }

        private static JObject BleTelemetry(BleConnection ble, bool oxygenReady, double oxygen, SensorMeasurements sensor)
        {
            var devices = new JArray();
            if (ble != null)
                for (int i = 0; i < ble.DeviceIds.Count; i++)
                    devices.Add(new JObject { ["id"] = ble.DeviceIds[i], ["name"] = ble.DeviceNames[i] });
            var connectedDevices = new JArray();
            if (ble != null && ble.Connected)
                connectedDevices.Add(new JObject { ["id"] = ble.ConnectedDeviceId, ["name"] = ble.ConnectedDeviceName });
            double hr = double.NaN, temperature = double.NaN, pulse = double.NaN, perfusion = double.NaN;
            bool matching = sensor != null && sensor.bluetooth == ble && ble != null && ble.Connected;
            return new JObject {
                ["available"] = ble != null, ["connected"] = ble != null && ble.Connected,
                ["scanning"] = ble != null && ble.Scanning, ["initializing"] = ble != null && ble.Initializing,
                ["connecting"] = ble != null && ble.Busy, ["status"] = ble != null ? ble.Status : "Keine BLE-Komponente in der Szene.",
                ["deviceId"] = ble != null ? ble.ConnectedDeviceId : null,
                ["deviceName"] = ble != null ? ble.ConnectedDeviceName : null,
                ["connectedDevices"] = connectedDevices,
                ["selectedDeviceId"] = ble != null && ble.SelectedIndex >= 0 && ble.SelectedIndex < ble.DeviceIds.Count ? ble.DeviceIds[ble.SelectedIndex] : null,
                ["devices"] = devices,
                ["hr"] = matching && sensor.TryGetHeartRateBpm(out hr) ? new JValue(hr) : JValue.CreateNull(),
                ["temperature"] = matching && sensor.TryGetTemperatureCelsius(out temperature) ? new JValue(temperature) : JValue.CreateNull(),
                ["spo2"] = matching && oxygenReady ? new JValue(oxygen) : JValue.CreateNull(),
                ["pr"] = matching && sensor.TryGetPulseBpm(out pulse) ? new JValue(pulse) : JValue.CreateNull(),
                ["pi"] = matching && sensor.TryGetPerfusionPercent(out perfusion) ? new JValue(perfusion) : JValue.CreateNull()
            };
        }
    }
}
