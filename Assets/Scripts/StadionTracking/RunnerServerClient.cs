using System;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace StadionTracking
{
    /// <summary>RunningServer v1 bridge. All Unity operations execute on the main thread.</summary>
    [DefaultExecutionOrder(-1000)]
    public sealed partial class RunnerServerClient : MonoBehaviour
    {
        private RunnerServerConnection connection;
        private RunnerServerSetup serverSetup;
        private StraightTreadmillWorld world;
        private TreadmillCompanion companion;
        private RunnerSession session;
        private RunnerAvatarBehaviour avatarBehaviour;
        private Canvas statsCanvas;
        private MarkerAndAnchorTracker markerTracker;
        private string deviceId;
        private RunnerSessionState state = RunnerSessionState.Stopped;
        public RunnerSessionState State => state;
        public string ServerAddress { get; private set; }
        public string LastConnectionError { get; private set; }
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
            session = RunnerSession.GetOrCreate(world);
            session.BindWorld(world);
            avatarBehaviour = RunnerAvatarBehaviour.GetOrCreate(world);
            var stats = FindFirstObjectByType<ActiveStatsDisplay>();
            statsCanvas = stats != null ? stats.GetComponent<Canvas>() : null;
            SetSessionState(RunnerSessionState.Stopped);
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
            SetSessionState(RunnerSessionState.Stopped);
            string url = RunnerServerConfiguration.Load(out string configurationError);
            serverSetup = RunnerServerSetup.Create(this, url, configurationError);
        }

        public bool ConnectToServer(string address, out string error)
        {
            error = null;
            if (!RunnerServerConfiguration.TryNormalizeAddress(address, out var uri))
            { error = "Bitte eine gültige IP-Adresse oder ws:// bzw. wss:// Adresse eingeben."; return false; }
            try
            {
                RunnerServerConfiguration.Save(uri);
            }
            catch (Exception e) { error = "Adresse konnte nicht gespeichert werden: " + e.Message; return false; }
            connection?.Dispose();
            PauseRunningSession();
            Debug.Log("[RunningServer] Connecting to " + uri.GetLeftPart(UriPartial.Path));
            ServerAddress = uri.GetLeftPart(UriPartial.Path);
            LastConnectionError = null;
            connection = new RunnerServerConnection(uri, deviceId);
            return true;
        }
        private void Update()
        {
            // Independent of server connectivity: show the avatar as soon as XR placement is ready.
            if (startupAvatarPending && companion != null && world.PlacementReady && world.HeightConfirmed)
            {
                SpawnOnSelectedLane();
            }
            int remaining = 32;
            while (remaining-- > 0 && connection != null && connection.TryRead(out var message))
                ProcessConnectionEvent(message);
            if (connected && Time.unscaledTime >= nextTelemetry)
            {
                nextTelemetry = Time.unscaledTime + .2f;
                SendTelemetry();
            }
        }
        private void ProcessConnectionEvent(RunnerConnectionEvent message)
        {
            switch (message.Kind)
            {
                case RunnerConnectionEventKind.Connecting:
                    if (serverSetup != null) serverSetup.SetStatus(message.Detail +
                        (string.IsNullOrEmpty(LastConnectionError) ? "" : "\n" + LastConnectionError), false);
                    break;
                case RunnerConnectionEventKind.Connected:
                    connected = true;
                    LastConnectionError = null;
                    if (serverSetup != null) serverSetup.gameObject.SetActive(false);
                    Queue(CreateRegistry());
                    nextTelemetry = 0;
                    break;
                case RunnerConnectionEventKind.Error:
                    LastConnectionError = message.Detail;
                    Debug.LogWarning("[RunningServer] " + LastConnectionError);
                    if (serverSetup != null) serverSetup.SetStatus(LastConnectionError, true);
                    break;
                case RunnerConnectionEventKind.Disconnected:
                    PauseRunningSession();
                    if (serverSetup != null) serverSetup.SetStatus(LastConnectionError ??
                        ("Verbindung zu " + ServerAddress + " unterbrochen. Erneuter Versuch folgt."), true);
                    break;
                case RunnerConnectionEventKind.Message: Handle(message.Detail); break;
            }
        }

        private void Queue(JObject message) => connection?.Send(message.ToString(Newtonsoft.Json.Formatting.None));
        private void PauseRunningSession()
        {
            avatarBehaviour?.Stop();
            connected = false;
            SetSessionState(state == RunnerSessionState.Running ? RunnerSessionState.Paused : state);
        }

        private void SetSessionState(RunnerSessionState nextState)
        {
            if (nextState == RunnerSessionState.Running || nextState == RunnerSessionState.Ready || nextState == RunnerSessionState.Stopped)
                avatarBehaviour?.Stop();
            if(nextState==RunnerSessionState.Running && world!=null)RunnerAvatarGreeting.GetOrCreate(world).NotifyRunStarted();
            if((nextState==RunnerSessionState.Running || nextState==RunnerSessionState.Ready) && world!=null)world.GetComponent<RunnerAvatarFinish>()?.Cancel();
            state = nextState;
            world.SessionPaused = nextState != RunnerSessionState.Running;
        }

        private void OnDestroy()
        {
            connection?.Dispose();
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
