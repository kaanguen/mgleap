using System;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace StadionTracking
{
    public sealed partial class RunnerServerClient
    {
        private static JObject CreateRegistry() => new JObject
        {
            ["type"] = "registry",
            ["data"] = new JObject
            {
                ["project"] = "MarkerML2",
                ["avatars"] = new JArray("runner"),
                ["huds"] = new JArray("stadium"),
                ["animations"] = AnimationCatalog(),
                ["capabilities"] = new JObject
                {
                    ["ambientLightCalibration"] = false,
                    ["treadmillControl"] = true,
                    ["warmupSequence"] = true,
                    ["avatarBehaviour"] = Resources.Load<RunnerAvatarAnimationLibrary>(RunnerAvatarBehaviour.LibraryResource) != null
                },
                ["commands"] = new JArray(
                    "runningMate.configure", "runningMate.command", "scene.setRunningMatePathVisible",
                    "scene.setRunningMateHudVisible", "scene.setHudVisible", "runningMate.setGhostSpeed",
                    "runningMate.setUserSpeed", "avatar.configurePlacement", "avatar.reposition",
                    "scene.recalibrate", "scene.adjustHeight", "runningMate.adjust", "runningMate.setLane",
                    "scene.setTrackEnvironment", "scene.captureTrackPoint", "scene.rescanMarker",
                    "sensor.connect", "sensor.disconnect", "runningMate.setCollectibles",
                    "avatar.playAnimation", "avatar.stopAnimation",
                    "exercise.startWarmup", "exercise.stopWarmup", "exercise.nextWarmup")
            }
        };

        private static JArray AnimationCatalog()
        {
            var result = new JArray();
            var library = Resources.Load<RunnerAvatarAnimationLibrary>(RunnerAvatarBehaviour.LibraryResource);
            if (library == null) return result;
            foreach (var entry in library.animations)
                if (entry != null && entry.clip != null)
                    result.Add(new JObject { ["id"] = entry.id, ["name"] = entry.label,
                        ["category"] = entry.category, ["duration"] = entry.Duration, ["supportsHoldDuration"] = entry.IsSequence });
            return result;
        }

        private void Handle(string json)
        {
            JObject message;
            try { message = JObject.Parse(json); } catch (Exception e) { Debug.LogWarning("[RunningServer] " + e.Message); return; }
            if (!TryReadEnvelopeText(message, "type", out string type))
            { Debug.LogWarning("[RunningServer] Invalid message: type must be a non-empty string."); return; }
            if (type != "command") return;
            if (!TryReadEnvelopeText(message, "commandId", out string id))
            { Debug.LogWarning("[RunningServer] Invalid command: commandId must be a non-empty string."); return; }
            if (acknowledgements.TryGetValue(id, out var previous)) { connection?.Send(previous); return; }
            var ack = new JObject { ["type"] = "ack", ["commandId"] = id };
            try
            {
                if (!TryReadEnvelopeText(message, "commandType", out string command))
                    throw new ArgumentException("commandType must be a non-empty string.");
                var payload = message["commandPayload"];
                if (payload != null && payload.Type != JTokenType.Null && payload.Type != JTokenType.Object)
                    throw new ArgumentException("commandPayload must be an object or null.");
                Apply(command, payload as JObject ?? new JObject());
                ack["success"] = true;
                ack["data"] = new JObject { ["state"] = state.ToString() };
            }
            catch (Exception e) { ack["success"] = false; ack["error"] = e.Message; }
            var response = ack.ToString(Newtonsoft.Json.Formatting.None);
            if (acknowledgements.Count >= 256) acknowledgements.Clear();
            acknowledgements[id] = response;
            connection?.Send(response);
            nextTelemetry = 0;
        }
        private static bool TryReadEnvelopeText(JObject message, string key, out string value)
        {
            var token = message[key];
            value = token != null && token.Type == JTokenType.String ? (string)token : null;
            return !string.IsNullOrWhiteSpace(value);
        }

        private void Apply(string command, JObject p)
        {
            var activeWarmup = world.GetComponent<RunnerWarmup>();
            if (activeWarmup != null && activeWarmup.Active &&
                (command == "avatar.playAnimation" || command == "avatar.stopAnimation" || command == "runningMate.configure" ||
                 command == "runningMate.setLane" || command == "runningMate.adjust" || command == "avatar.configurePlacement" ||
                 command == "avatar.reposition" || command == "scene.recalibrate" || command == "scene.adjustHeight" ||
                 command == "scene.setTrackEnvironment" || command == "scene.captureTrackPoint"))
                throw new InvalidOperationException("Zuerst den Warm-up-Modus beenden.");
            switch (command)
            {
                case "exercise.startWarmup":
                    StartWarmup(p);
                    break;
                case "exercise.stopWarmup":
                    RunnerCommandValues.RequireFields(p, "exercise.stopWarmup");
                    world.GetComponent<RunnerWarmup>()?.Stop();
                    companion?.Synchronize();
                    break;
                case "exercise.nextWarmup":
                    RunnerCommandValues.RequireFields(p, "exercise.nextWarmup");
                    if (activeWarmup == null || !activeWarmup.Active) throw new InvalidOperationException("Kein Warm-up aktiv.");
                    activeWarmup.Next();
                    break;
                case "avatar.playAnimation":
                    RunnerCommandValues.RequireFields(p, "avatar.playAnimation", "animationId", "loop", "speed", "holdSeconds");
                    if (p["animationId"]?.Type != JTokenType.String) throw new ArgumentException("animationId must be a string.");
                    string animationId = (string)p["animationId"];
                    bool animationLoop = RunnerCommandValues.Boolean(p, "loop", true);
                    float animationSpeed = RunnerCommandValues.Decimal(p, "speed", 1, .25f, 2);
                    float holdSeconds=RunnerCommandValues.Decimal(p,"holdSeconds",10,1,300);
                    if(p["holdSeconds"]!=null && animationId!="Mixamo_Plank")throw new ArgumentException("holdSeconds is only available for Plank.");
                    var behaviour = avatarBehaviour ?? (avatarBehaviour = RunnerAvatarBehaviour.GetOrCreate(world));
                    behaviour.Validate(animationId, animationLoop, animationSpeed,holdSeconds);
                    if (!world.PlacementReady || !world.HeightConfirmed || !world.HeadPoseReady)
                        throw new InvalidOperationException("Zuerst Bodenmarker und Avatar-Platzierung unter Kalibrierung einrichten.");
                    if (companion == null) throw new InvalidOperationException("Companion is unavailable.");
                    // Validate everything before pausing the running session or changing the avatar.
                    if (state == RunnerSessionState.Running) SetSessionState(RunnerSessionState.Paused);
                    companion.enabled = true;
                    world.GetComponent<RunnerAvatarFinish>()?.Cancel(false);
                    behaviour.Play(animationId, animationLoop, animationSpeed,holdSeconds);
                    companion.Synchronize();
                    break;
                case "avatar.stopAnimation":
                    RunnerCommandValues.RequireFields(p, "avatar.stopAnimation");
                    avatarBehaviour?.Stop();
                    break;
                case "runningMate.setCollectibles":
                    RunnerCommandValues.RequireFields(p, "collectibles", "source", "fixedCount");
                    var pickups = world.GetComponent<TrackCollectibles>();
                    if (pickups == null) throw new InvalidOperationException("Track collectibles are unavailable.");
                    string source = (string)p["source"];
                    if (source != "Fixed" && source != "OxygenSensor") throw new ArgumentException("source must be Fixed or OxygenSensor.");
                    int fixedCount = (int)RunnerCommandValues.Number(p, "fixedCount", pickups.FixedCount, 0, 30);
                    pickups.ConfigureSpawning(source == "Fixed" ? CollectibleSpawnSource.Fixed : CollectibleSpawnSource.OxygenSensor, fixedCount);
                    break;
                case "scene.rescanMarker":
                    world.GetComponent<RunnerWarmup>()?.Stop();
                    world.BeginMarkerPlacement();
                    startupAvatarPending = true;
                    if (companion != null) companion.enabled = true;
                    SetSessionState(RunnerSessionState.Stopped);
                    break;
                case "runningMate.setLane":
                    RequireCalibrationReady();
                    int lane = (int)RunnerCommandValues.Number(p, "lane", world.AvatarLane, 1, 8);
                    world.GetComponent<RunnerAvatarFinish>()?.Cancel();
                    world.SetAvatarLane(lane);
                    if (companion != null) companion.UseLanePlacement();
                    break;
                case "scene.setTrackEnvironment":
                    if (!world.SessionPaused) throw new InvalidOperationException("Pause the session before switching the track environment.");
                    var environment = (string)p["environment"];
                    if (environment != "Stadium" && environment != "Treadmill") throw new ArgumentException("environment must be Stadium or Treadmill.");
                    world.SetTrackEnvironment(environment == "Stadium");
                    if (companion != null) companion.UseLanePlacement();
                    session.ResetSessionStats();
                    SetSessionState(RunnerSessionState.Ready);
                    break;
                case "scene.captureTrackPoint":
                    if (!world.SessionPaused) throw new InvalidOperationException("Pause before capturing calibration points.");
                    var point = (string)p["point"];
                    if (point == "A") world.CaptureTrackPointA();
                    else if (point == "B") world.CaptureTrackPointB(RunnerCommandValues.Decimal(p, "distanceAlongTrack", 20, 5, 100));
                    else throw new ArgumentException("point must be A or B.");
                    break;
                case "avatar.configurePlacement":
                    RequireCalibrationReady();
                    if (companion == null) throw new InvalidOperationException("Companion is unavailable.");
                    if (companion.Mode != TreadmillRunMode.SideBySide)
                        throw new InvalidOperationException("Sweet-spot placement applies to Side by Side. The active Tempo-Partner follows the track; use world calibration to align it.");
                    RunnerCommandValues.RequireFields(p, "placement", "position", "facing", "distance");
                    if (world.RealTrackMode) throw new InvalidOperationException("Use lane selection for avatar placement on a real stadium track.");
                    companion.ConfigurePlacement((string)p["position"], (string)p["facing"], RunnerCommandValues.Decimal(p, "distance", 2, .5f, 20));
                    world.GetComponent<RunnerAvatarFinish>()?.Cancel();
                    break;
                case "avatar.reposition":
                case "scene.recalibrate":
                    RequireCalibrationReady();
                    bool includeHeight = command == "scene.recalibrate" && RunnerCommandValues.Boolean(p, "includeHeight", true);
                    if (!world.RecalibratePlacement(includeHeight)) throw new InvalidOperationException("Head pose cannot be used for calibration. Look horizontally forward and retry.");
                    world.GetComponent<RunnerAvatarFinish>()?.Cancel();
                    break;
                case "scene.adjustHeight":
                    RequireCalibrationReady();
                    world.AdjustSurfaceHeight(RunnerCommandValues.Decimal(p, "deltaMetres", 0, -.5f, .5f));
                    break;
                case "runningMate.adjust":
                    RequireCalibrationReady();
                    bool reset = RunnerCommandValues.Boolean(p, "reset", false);
                    var offsets = world.CalibrationOffsets;
                    var across = reset ? 0 : RunnerCommandValues.Decimal(p, "across", offsets.x, -100, 100);
                    var along = reset ? 0 : RunnerCommandValues.Decimal(p, "along", offsets.y, -100, 100);
                    var heading = reset ? 0 : RunnerCommandValues.Decimal(p, "heading", offsets.z, -180, 180);
                    world.SetCalibrationOffsets(across, along, heading);
                    world.GetComponent<RunnerAvatarFinish>()?.Cancel();
                    break;
                case "runningMate.setUserSpeed":
                    if (world.RealTrackMode) throw new InvalidOperationException("The real stadium uses head tracking. Select the treadmill environment for fixed test speed or belt speed.");
                    var speedSource = (string)p["source"];
                    if (speedSource != "FixedTest" && speedSource != "Treadmill")
                        throw new ArgumentException("source must be FixedTest or Treadmill.");
                    float fixedKmh = RunnerCommandValues.Decimal(p, "speedKmh", world.FixedTestSpeedKmh, 0, 100);
                    world.ConfigureUserSpeed((RunnerSpeedSource)Enum.Parse(typeof(RunnerSpeedSource), speedSource), fixedKmh);
                    break;
                case "runningMate.setGhostSpeed":
                    var playback = session.GhostPlayback;
                    if (playback == null) throw new InvalidOperationException("Ghost playback is unavailable.");
                    if (!p.ContainsKey("speedKmh")) throw new ArgumentException("speedKmh is required; null restores best-lap playback.");
                    var value = p["speedKmh"];
                    if (value.Type != JTokenType.Null && value.Type != JTokenType.Integer && value.Type != JTokenType.Float)
                        throw new ArgumentException("speedKmh must be a non-negative number or null.");
                    playback.SetTestSpeed(value.Type == JTokenType.Null ? (double?)null : (double)value);
                    break;
                case "runningMate.configure":
                    // Validate the complete payload before changing session settings.
                    RunnerCommandValues.RequireFields(p, "runningMate.configure", "paceSeconds", "heightCm", "mode", "showTrack");
                    var pace = RunnerCommandValues.Number(p, "paceSeconds", RunnerSessionSettings.PaceSeconds, 180, 1200);
                    var height = RunnerCommandValues.Number(p, "heightCm", world.BodyHeightCm, 80, 230);
                    var mode = (string)p["mode"] ?? RunnerSessionSettings.Mode.ToString();
                    if (mode != "SideBySide" && mode != "TempoPartner") throw new ArgumentException("mode must be SideBySide or TempoPartner.");
                    bool showTrack = RunnerCommandValues.Boolean(p, "showTrack", world.ShowTrack);
                    if (p["heightCm"] != null && !world.HeadPoseReady) throw new InvalidOperationException("Head tracking is not ready for height calibration.");
                    RunnerSessionSettings.PaceSeconds = (int)pace;
                    RunnerSessionSettings.HeightCm = (int)height;
                    RunnerSessionSettings.Mode = (TreadmillRunMode)Enum.Parse(typeof(TreadmillRunMode), mode);
                    // Target pace belongs to the ghost. User/belt speed is configured independently.
                    world.SetBodyHeightCentimetres((int)height);
                    if (p["heightCm"] != null && !world.RealTrackMode && !world.RequiresMarkerPlacement) world.CalibrateFromEyeHeight((height - 12) / 100f);
                    world.ShowTrack = showTrack;
                    RunnerSessionSettings.ShowTrack = showTrack;
                    if (companion != null) companion.SetMode(RunnerSessionSettings.Mode);
                    session.RefreshTargetPace();
                    SetSessionState(RunnerSessionState.Ready);
                    break;
                case "runningMate.command":
                    switch ((string)p["action"])
                    {
                        case "spawn":
                            if (!world.PlacementReady || !world.HeightConfirmed)
                                throw new InvalidOperationException("Place or calibrate the track before spawning the avatar.");
                            SpawnOnSelectedLane(); break;
                        case "start": case "resume":
                            if (!world.PlacementReady || !world.HeightConfirmed || (world.RealTrackMode && !world.RealTrackCalibrated)) throw new InvalidOperationException("Placement is not ready. Calibrate stadium points A/B first.");
                            if ((string)p["action"] == "start") SpawnOnSelectedLane();
                            else if (companion != null) companion.enabled = true;
                            SetSessionState(RunnerSessionState.Running); break;
                        case "pause": SetSessionState(RunnerSessionState.Paused); break;
                        case "stop":
                            bool finishRun=state==RunnerSessionState.Running || state==RunnerSessionState.Paused;
                            SetSessionState(RunnerSessionState.Stopped);
                            if(finishRun)RunnerAvatarFinish.GetOrCreate(world).Begin(companion,session.GhostPlayback);
                            break;
                        case "reset":
                            SetSessionState(RunnerSessionState.Ready);
                            if (world.PlacementReady) world.ResetDistance();
                            session.ResetSessionStats();
                            break;
                        case "despawn": world.GetComponent<RunnerWarmup>()?.Stop(); world.GetComponent<RunnerAvatarFinish>()?.Cancel(); startupAvatarPending = false; if (companion != null) companion.enabled = false; break;
                        default: throw new ArgumentException("Unsupported running action.");
                    }
                    break;
                case "scene.setRunningMatePathVisible": world.ShowTrack = RunnerCommandValues.Boolean(p, "visible"); break;
                case "scene.setHudVisible":
                    if ((string)p["id"] != "stadium") throw new ArgumentException("Only the stadium HUD exists.");
                    SetHud(RunnerCommandValues.Boolean(p, "visible")); break;
                case "scene.setRunningMateHudVisible": SetHud(RunnerCommandValues.Boolean(p, "visible")); break;
                case "sensor.connect": case "sensor.disconnect":
                    var ble = FindFirstObjectByType<BleConnection>();
                    if (ble == null) throw new InvalidOperationException("No BLE component in this scene.");
                    if (command == "sensor.disconnect") ble.Disconnect();
                    else { if (ble.DeviceIds.Count == 0) throw new InvalidOperationException("No discovered BLE device. Scan and select a device first."); ble.Connect(); }
                    break;
                default: throw new NotSupportedException("Command is not implemented by MarkerML2: " + command);
            }
        }
        private void SpawnOnSelectedLane()
        {
            world.GetComponent<RunnerAvatarFinish>()?.Cancel();
            startupAvatarPending = false;
            if (companion == null) throw new InvalidOperationException("Companion is unavailable.");
            companion.UseLanePlacement();
            companion.enabled = true;
            companion.Synchronize();
            // The independent session synchronizes the tempo partner later in the same frame.
        }
        private void RequireCalibrationReady()
        {
            if (!world.SessionPaused) throw new InvalidOperationException("Pause the session before changing calibration.");
            if (!world.HeadPoseReady || !world.PlacementReady || !world.HeightConfirmed)
                throw new InvalidOperationException("Wait for head tracking, then scan a floor marker and its detected floor plane.");
        }
        private void SetHud(bool visible)
        {
            // Visibility only; the independent session owns the clock and ghost.
            if (statsCanvas == null) return;
            var group = statsCanvas.GetComponent<CanvasGroup>();
            if (group == null) group = statsCanvas.gameObject.AddComponent<CanvasGroup>();
            group.alpha = visible ? 1 : 0;
        }
    }
}
