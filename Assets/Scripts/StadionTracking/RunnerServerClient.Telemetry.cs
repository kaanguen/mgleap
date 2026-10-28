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
            var ble = FindFirstObjectByType<BleConnection>();
            var hudGroup = statsCanvas != null ? statsCanvas.GetComponent<CanvasGroup>() : null;
            Queue(new JObject { ["type"] = "telemetry", ["data"] = new JObject {
                ["treadmill"] = run, ["runningMate"] = run.DeepClone(),
                ["ble"] = new JObject { ["connected"] = ble != null && ble.Connected },
                ["huds"] = new JObject { ["stadium"] = statsCanvas != null && (hudGroup == null || hudGroup.alpha > 0) } } });
        }

    }
}
