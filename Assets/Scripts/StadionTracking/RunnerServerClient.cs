using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace StadionTracking
{
    /// <summary>RunningServer v1 bridge. All Unity operations execute on the main thread.</summary>
    [DefaultExecutionOrder(-1000)]
    public sealed class RunnerServerClient : MonoBehaviour
    {
        [Serializable] private sealed class Configuration { public string serverUrl = "ws://127.0.0.1:8081/ws"; }
        private readonly ConcurrentQueue<string> incoming = new ConcurrentQueue<string>();
        private readonly ConcurrentQueue<string> outgoing = new ConcurrentQueue<string>();
        private CancellationTokenSource lifetime;
        private RunnerServerSetup serverSetup;
        private StraightTreadmillWorld world;
        private TreadmillCompanion companion;
        private ActiveStatsDisplay stats;
        private Canvas statsCanvas;
        private MarkerAndAnchorTracker markerTracker;
        private string deviceId, state = "Stopped";
        private bool connected;
        private bool startupAvatarPending = true;
        private float nextTelemetry;
        private readonly System.Collections.Generic.Dictionary<string, string> acknowledgements =
            new System.Collections.Generic.Dictionary<string, string>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
            OnSceneLoaded(SceneManager.GetActiveScene(), LoadSceneMode.Single);
        }
        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (scene.name == "RunnerMainMenu") { SceneManager.LoadScene("TreadmillStraightTest"); return; }
            var target = FindFirstObjectByType<StraightTreadmillWorld>();
            if (target != null && target.GetComponent<RunnerServerClient>() == null)
                target.gameObject.AddComponent<RunnerServerClient>();
        }
        private void Awake()
        {
            DisableDimming();
            world = GetComponent<StraightTreadmillWorld>();
            companion = FindFirstObjectByType<TreadmillCompanion>();
            stats = FindFirstObjectByType<ActiveStatsDisplay>();
            statsCanvas = stats != null ? stats.GetComponent<Canvas>() : null;
            world.SessionPaused = true;
            world.BeginMarkerPlacement();
            foreach (var tracker in FindObjectsByType<MarkerAndAnchorTracker>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (tracker.gameObject.scene != gameObject.scene) continue;
                markerTracker = tracker;
                tracker.BindRunningWorld(world);
                tracker.gameObject.SetActive(true);
                tracker.enabled = true;
                break;
            }
            if (markerTracker == null) Debug.LogError("[RunningTrack] Marker tracker is missing from the run scene.");
            world.SetAvatarLane(StraightTreadmillWorld.StartAvatarLane);
            if (companion != null)
            {
                companion.UseLanePlacement();
                companion.enabled = true;
            }
            foreach (var nav in FindObjectsByType<RunnerSessionNavigation>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                nav.enabled = false;
                var pause = nav.transform.Find("PauseSession");
                var menu = nav.transform.Find("BackToMenu");
                if (pause != null) pause.gameObject.SetActive(false);
                if (menu != null) menu.gameObject.SetActive(false);
            }
            deviceId = SystemInfo.deviceUniqueIdentifier;
        }
        private void Start()
        {
            DisableDimming();
            // Start after all scene Awake calls, including the old session bootstrap.
            world.SessionPaused = true;
            string url = new Configuration().serverUrl;
            string configurationError = null;
            var overridePath = Path.Combine(Application.persistentDataPath, "runner-server.json");
            try
            {
                var config = Resources.Load<TextAsset>("RunnerServer");
                if (config != null) url = JsonUtility.FromJson<Configuration>(config.text).serverUrl;
                if (File.Exists(overridePath)) url = JsonUtility.FromJson<Configuration>(File.ReadAllText(overridePath)).serverUrl;
            }
            catch (Exception e) { configurationError = "Gespeicherte Adresse konnte nicht geladen werden: " + e.Message; }
            serverSetup = RunnerServerSetup.Create(this, url, configurationError);
        }

        public bool ConnectToServer(string address, out string error)
        {
            error = null;
            if (!RunnerServerSetup.TryNormalizeAddress(address, out var uri))
            { error = "Bitte eine gültige IP-Adresse oder ws:// bzw. wss:// Adresse eingeben."; return false; }
            try
            {
                File.WriteAllText(Path.Combine(Application.persistentDataPath, "runner-server.json"),
                    JsonUtility.ToJson(new Configuration { serverUrl = uri.AbsoluteUri }, true));
            }
            catch (Exception e) { error = "Adresse konnte nicht gespeichert werden: " + e.Message; return false; }
            lifetime?.Cancel();
            lifetime?.Dispose();
            connected = false;
            world.SessionPaused = true;
            if (state == "Running") state = "Paused";
            while (incoming.TryDequeue(out _)) { }
            while (outgoing.TryDequeue(out _)) { }
            lifetime = new CancellationTokenSource();
            Debug.Log("[RunningServer] Connecting to " + uri.GetLeftPart(UriPartial.Path));
            _ = ConnectLoop(uri, lifetime.Token);
            return true;
        }
        private async Task ConnectLoop(Uri uri, CancellationToken token)
        {
            int delay = 500;
            while (!token.IsCancellationRequested)
            {
                try
                {
                    using (var socket = new ClientWebSocket())
                    {
                        socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(10);
                        using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(token))
                        { timeout.CancelAfter(5000); await socket.ConnectAsync(uri, timeout.Token); }
                        await Send(socket, new JObject { ["type"] = "hello", ["role"] = "headset", ["deviceId"] = deviceId,
                            ["platform"] = "Magic Leap 2" }.ToString(), token);
                        if (token.IsCancellationRequested) return;
                        incoming.Enqueue("__connected");
                        delay = 500;
                        using (var connection = CancellationTokenSource.CreateLinkedTokenSource(token))
                        {
                            var receive = Receive(socket, connection.Token);
                            try
                            {
                                while (!receive.IsCompleted && socket.State == WebSocketState.Open && !token.IsCancellationRequested)
                                {
                                    while (outgoing.TryDequeue(out var json)) await Send(socket, json, connection.Token);
                                    await Task.WhenAny(receive, Task.Delay(25, connection.Token));
                                }
                                await receive;
                            }
                            finally { connection.Cancel(); socket.Abort(); }
                        }
                    }
                }
                catch (Exception e) when (!token.IsCancellationRequested)
                { incoming.Enqueue("__error:" + e.Message); }
                catch (Exception) when (token.IsCancellationRequested) { }
                finally
                {
                    if (!token.IsCancellationRequested)
                    {
                        incoming.Enqueue("__disconnected");
                        while (outgoing.TryDequeue(out _)) { }
                    }
                }
                try { await Task.Delay(delay, token); } catch (OperationCanceledException) { break; }
                delay = Math.Min(delay * 2, 10000);
            }
        }
        private static Task Send(ClientWebSocket socket, string json, CancellationToken token)
        {
            var bytes = Encoding.UTF8.GetBytes(json);
            return socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, token);
        }
        private async Task Receive(ClientWebSocket socket, CancellationToken token)
        {
            var buffer = new byte[4096];
            while (socket.State == WebSocketState.Open)
            {
                using (var message = new MemoryStream())
                {
                    WebSocketReceiveResult result;
                    do
                    {
                        result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), token);
                        if (result.MessageType == WebSocketMessageType.Close) return;
                        if (result.MessageType != WebSocketMessageType.Text || message.Length + result.Count > 65536)
                            throw new InvalidDataException("Invalid or oversized server message.");
                        message.Write(buffer, 0, result.Count);
                    } while (!result.EndOfMessage);
                    if (incoming.Count >= 128) throw new InvalidDataException("Server command queue is full.");
                    if (token.IsCancellationRequested) return;
                    incoming.Enqueue(Encoding.UTF8.GetString(message.ToArray()));
                }
            }
        }
        private void Update()
        {
            // Independent of server connectivity: show the avatar as soon as XR placement is ready.
            if (startupAvatarPending && companion != null && world.PlacementReady && world.HeightConfirmed)
            {
                SpawnOnStartLane();
            }
            int remaining = 32;
            while (remaining-- > 0 && incoming.TryDequeue(out var json))
            {
                if (json == "__connected")
                {
                    connected = true;
                    if (serverSetup != null) serverSetup.gameObject.SetActive(false);
                    Queue(new JObject { ["type"] = "registry", ["data"] = new JObject {
                        ["project"] = "MarkerML2", ["avatars"] = new JArray("runner"), ["huds"] = new JArray("stadium"),
                        ["capabilities"] = new JObject { ["ambientLightCalibration"] = false,
                            ["treadmillControl"] = true },
                        ["commands"] = new JArray("runningMate.configure", "runningMate.command", "scene.setRunningMatePathVisible",
                            "scene.setRunningMateHudVisible", "scene.setHudVisible", "runningMate.setGhostSpeed", "runningMate.setUserSpeed", "avatar.configurePlacement", "avatar.reposition", "scene.recalibrate", "scene.adjustHeight", "runningMate.adjust", "runningMate.setLane", "scene.setTrackEnvironment", "scene.captureTrackPoint", "scene.rescanMarker", "sensor.connect", "sensor.disconnect") } });
                    nextTelemetry = 0;
                }
                else if (json == "__disconnected")
                {
                    connected = false; world.SessionPaused = true; if (state == "Running") state = "Paused";
                    if (serverSetup != null) serverSetup.SetStatus("Nicht verbunden. Adresse prüfen oder erneut verbinden.", true);
                }
                else if (json.StartsWith("__error:"))
                {
                    Debug.LogWarning("[RunningServer] " + json.Substring(8));
                    if (serverSetup != null) serverSetup.SetStatus("Verbindung fehlgeschlagen. WLAN und Adresse prüfen.", true);
                }
                else Handle(json);
            }
            if (connected && Time.unscaledTime >= nextTelemetry)
            {
                nextTelemetry = Time.unscaledTime + .2f;
                SendTelemetry();
            }
        }
        private void SendTelemetry()
        {
            var run = new JObject { ["state"] = state, ["distance"] = world.TravelledMetres,
                ["elapsed"] = stats != null ? stats.ElapsedSeconds : 0, ["laps"] = world.CompletedLaps,
                ["lapLength"] = 400, ["speed"] = world.SessionPaused ? 0 : world.SpeedMetresPerSecond,
                ["speedKmh"] = world.RealTrackMode ? world.SpeedMetresPerSecond * 3.6f : world.SpeedKmh, ["mode"] = RunnerSessionSettings.Mode.ToString(),
                ["heightCm"] = world.BodyHeightCm, ["paceSeconds"] = RunnerSessionSettings.PaceSeconds,
                ["placementReady"] = world.PlacementReady, ["trackVisible"] = world.ShowTrack };
            var ghost = stats != null ? stats.GetComponent<LapGhostPlayback>() : null;
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
                ["fastest100mSeconds"] = stats?.Recorded100mSeconds,
                ["bestLapSeconds"] = stats?.RecordedLapSeconds,
                ["fastest1KmSeconds"] = stats?.Recorded1KmSeconds };
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
                ["trackPositionMetres"] = (world.TrackStartMetres + (world.RealTrackMode ? world.RaceStartTrackMetres : 0) + ghost.TravelledMetres) % 400,
                ["speedKmh"] = ghost.CurrentSpeedKmh, ["gapMetres"] = ghost.GapMetres,
                ["testMode"] = ghost.TestSpeedEnabled, ["testSpeedKmh"] = ghost.TestSpeedKmh,
                ["recordedLap"] = ghost.IsRecordedLap, ["hasRecording"] = ghost.HasRecordedLap,
                ["playbackMode"] = ghost.PlaybackMode };
            var ble = FindFirstObjectByType<BleConnection>();
            Queue(new JObject { ["type"] = "telemetry", ["data"] = new JObject {
                ["treadmill"] = run, ["runningMate"] = run.DeepClone(),
                ["ble"] = new JObject { ["connected"] = ble != null && ble.Connected },
                ["huds"] = new JObject { ["stadium"] = statsCanvas != null && (statsCanvas.GetComponent<CanvasGroup>() == null || statsCanvas.GetComponent<CanvasGroup>().alpha > 0) } } });
        }

        private void Queue(JObject message) => outgoing.Enqueue(message.ToString(Newtonsoft.Json.Formatting.None));
        private void Handle(string json)
        {
            JObject message;
            try { message = JObject.Parse(json); } catch (Exception e) { Debug.LogWarning("[RunningServer] " + e.Message); return; }
            if ((string)message["type"] != "command") return;
            var id = (string)message["commandId"];
            if (string.IsNullOrEmpty(id)) return;
            if (acknowledgements.TryGetValue(id, out var previous)) { outgoing.Enqueue(previous); return; }
            var ack = new JObject { ["type"] = "ack", ["commandId"] = id };
            try
            {
                Apply((string)message["commandType"], message["commandPayload"] as JObject ?? new JObject());
                ack["success"] = true;
                ack["data"] = new JObject { ["state"] = state };
            }
            catch (Exception e) { ack["success"] = false; ack["error"] = e.Message; }
            var response = ack.ToString(Newtonsoft.Json.Formatting.None);
            if (acknowledgements.Count >= 256) acknowledgements.Clear();
            acknowledgements[id] = response;
            outgoing.Enqueue(response);
            nextTelemetry = 0;
        }
        private void Apply(string command, JObject p)
        {
            switch (command)
            {
                case "scene.rescanMarker":
                    world.BeginMarkerPlacement();
                    startupAvatarPending = true;
                    if (companion != null) companion.enabled = true;
                    state = "Stopped";
                    break;
                case "runningMate.setLane":
                    RequireCalibrationReady();
                    int lane = (int)RunnerCommandValues.Number(p, "lane", world.AvatarLane, 1, 8);
                    world.SetAvatarLane(lane);
                    if (companion != null) companion.UseLanePlacement();
                    break;
                case "scene.setTrackEnvironment":
                    if (!world.SessionPaused) throw new InvalidOperationException("Pause the session before switching the track environment.");
                    var environment = (string)p["environment"];
                    if (environment != "Stadium" && environment != "Treadmill") throw new ArgumentException("environment must be Stadium or Treadmill.");
                    world.SetTrackEnvironment(environment == "Stadium");
                    if (companion != null) companion.UseLanePlacement();
                    if (stats != null) stats.ResetSessionStats();
                    state = "Ready";
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
                    foreach (var field in p.Properties())
                        if (field.Name != "position" && field.Name != "facing" && field.Name != "distance")
                            throw new ArgumentException("Unsupported placement field: " + field.Name);
                    if (world.RealTrackMode) throw new InvalidOperationException("Use lane selection for avatar placement on a real stadium track.");
                    companion.ConfigurePlacement((string)p["position"], (string)p["facing"], RunnerCommandValues.Decimal(p, "distance", 2, .5f, 20));
                    break;
                case "avatar.reposition":
                case "scene.recalibrate":
                    RequireCalibrationReady();
                    bool includeHeight = command == "scene.recalibrate" && (p["includeHeight"] == null || RunnerCommandValues.Boolean(p, "includeHeight"));
                    if (!world.RecalibratePlacement(includeHeight)) throw new InvalidOperationException("Head pose cannot be used for calibration. Look horizontally forward and retry.");
                    break;
                case "scene.adjustHeight":
                    RequireCalibrationReady();
                    world.AdjustSurfaceHeight(RunnerCommandValues.Decimal(p, "deltaMetres", 0, -.5f, .5f));
                    break;
                case "runningMate.adjust":
                    RequireCalibrationReady();
                    bool reset = p["reset"] != null && RunnerCommandValues.Boolean(p, "reset");
                    var offsets = world.CalibrationOffsets;
                    var across = reset ? 0 : RunnerCommandValues.Decimal(p, "across", offsets.x, -100, 100);
                    var along = reset ? 0 : RunnerCommandValues.Decimal(p, "along", offsets.y, -100, 100);
                    var heading = reset ? 0 : RunnerCommandValues.Decimal(p, "heading", offsets.z, -180, 180);
                    world.SetCalibrationOffsets(across, along, heading);
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
                    var playback = stats != null ? stats.GetComponent<LapGhostPlayback>() : null;
                    if (playback == null) throw new InvalidOperationException("Ghost playback is unavailable.");
                    if (!p.ContainsKey("speedKmh")) throw new ArgumentException("speedKmh is required; null restores best-lap playback.");
                    var value = p["speedKmh"];
                    if (value.Type != JTokenType.Null && value.Type != JTokenType.Integer && value.Type != JTokenType.Float)
                        throw new ArgumentException("speedKmh must be a non-negative number or null.");
                    playback.SetTestSpeed(value.Type == JTokenType.Null ? (double?)null : (double)value);
                    break;
                case "runningMate.configure":
                    // Validate the complete payload before changing session settings.
                    foreach (var property in p.Properties())
                        if (property.Name != "paceSeconds" && property.Name != "heightCm" && property.Name != "mode" && property.Name != "showTrack")
                            throw new ArgumentException("Unsupported runningMate.configure field: " + property.Name);
                    var pace = RunnerCommandValues.Number(p, "paceSeconds", RunnerSessionSettings.PaceSeconds, 180, 1200);
                    var height = RunnerCommandValues.Number(p, "heightCm", world.BodyHeightCm, 80, 230);
                    var mode = (string)p["mode"] ?? RunnerSessionSettings.Mode.ToString();
                    if (mode != "SideBySide" && mode != "TempoPartner") throw new ArgumentException("mode must be SideBySide or TempoPartner.");
                    bool showTrack = p["showTrack"] == null ? world.ShowTrack : RunnerCommandValues.Boolean(p, "showTrack");
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
                    if (stats != null) stats.RefreshTargetPace();
                    world.SessionPaused = true;
                    state = "Ready";
                    break;
                case "runningMate.command":
                    switch ((string)p["action"])
                    {
                        case "spawn":
                            if (!world.PlacementReady || !world.HeightConfirmed)
                                throw new InvalidOperationException("Place or calibrate the track before spawning the avatar.");
                            SpawnOnStartLane(); break;
                        case "start": case "resume":
                            if (!world.PlacementReady || !world.HeightConfirmed || (world.RealTrackMode && !world.RealTrackCalibrated)) throw new InvalidOperationException("Placement is not ready. Calibrate stadium points A/B first.");
                            if ((string)p["action"] == "start") SpawnOnStartLane();
                            else if (companion != null) companion.enabled = true;
                            world.SessionPaused = false; state = "Running"; break;
                        case "pause": world.SessionPaused = true; state = "Paused"; break;
                        case "stop": world.SessionPaused = true; state = "Stopped"; break;
                        case "reset":
                            world.SessionPaused = true; if (world.PlacementReady) world.ResetDistance();
                            if (stats != null) stats.ResetSessionStats(); state = "Ready"; break;
                        case "despawn": startupAvatarPending = false; if (companion != null) companion.enabled = false; break;
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
        private void SpawnOnStartLane()
        {
            startupAvatarPending = false;
            world.SetAvatarLane(StraightTreadmillWorld.StartAvatarLane);
            if (companion == null) throw new InvalidOperationException("Companion is unavailable.");
            companion.UseLanePlacement();
            companion.enabled = true;
            companion.Synchronize();
            // The tempo partner is updated by ActiveStatsDisplay in the same frame.
        }
        private void RequireCalibrationReady()
        {
            if (!world.SessionPaused) throw new InvalidOperationException("Pause the session before changing calibration.");
            if (!world.HeadPoseReady || !world.PlacementReady || !world.HeightConfirmed)
                throw new InvalidOperationException("Wait for head tracking, then scan a floor marker and its detected floor plane.");
        }
        private void SetHud(bool visible)
        {
            // Keep the common race clock and ghost simulation alive while hiding graphics.
            if (statsCanvas == null) return;
            var group = statsCanvas.GetComponent<CanvasGroup>();
            if (group == null) group = statsCanvas.gameObject.AddComponent<CanvasGroup>();
            group.alpha = visible ? 1 : 0;
        }
        private void OnDestroy()
        {
            lifetime?.Cancel(); lifetime?.Dispose();
            if (serverSetup != null) Destroy(serverSetup.gameObject);
        }

        private static void DisableDimming()
        {
            var settings = UnityEngine.XR.OpenXR.OpenXRSettings.Instance;
            if (settings == null) return;
            var rendering = settings.GetFeature<MagicLeap.OpenXR.Features.MagicLeapRenderingExtensionsFeature>();
            if (rendering == null) return;
            rendering.GlobalDimmerEnabled = false;
            rendering.GlobalDimmerValue = 0f;
            // Additive passthrough also disables segmented dimming driven by rendered alpha.
            rendering.BlendMode = UnityEngine.XR.OpenXR.NativeTypes.XrEnvironmentBlendMode.Additive;
        }

        private void OnApplicationPause(bool paused)
        {
            if (!paused) DisableDimming();
        }
    }
}
