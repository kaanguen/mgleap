using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace StadionTracking
{
    /// <summary>Scene-owned clock, records and ghost simulation, independent of every HUD.</summary>
    [DefaultExecutionOrder(100)]
    public sealed class RunnerSession : MonoBehaviour
    {
        private static readonly Dictionary<int, RunnerSession> Sessions = new Dictionary<int, RunnerSession>();
        private StadiumTrackPreview runner, timedRunner, suspendedRunner;
        private StraightTreadmillWorld world, timedWorld;
        private TreadmillCompanion companion;
        private MarkerAndAnchorTracker markerTracker;
        private bool sessionStarted;
        private double previousDistance, boundWorldDistance, boundRunnerDistance;
        private RunnerRecordPersistence recordsPersistence;
        private string activeProfile;
        private LapCheckpointTracker queuedTracker;
        private int queuedRevision = -1;
        private Task<RunnerRecordPersistence.LoadResult> recordsLoad;
        private LapCheckpointTracker loadingTracker;
        private string loadWarning, lastReportedError;
        public bool HasPendingRecords => recordsPersistence != null && recordsPersistence.PendingCount > 0;
        public string RecordsStatus { get; private set; }

        public float ElapsedSeconds { get; private set; }
        public LapCheckpointTracker Checkpoints { get; private set; } =
            new LapCheckpointTracker(400.0, RunnerSessionSettings.PaceSeconds);
        public LapGhostPlayback GhostPlayback { get; private set; }
        public StadiumTrackPreview Runner => runner != null && runner.isActiveAndEnabled ? runner : null;
        public StraightTreadmillWorld World => Runner == null && suspendedRunner == null && world != null && world.isActiveAndEnabled ? world : null;
        public bool HasActiveSource => Runner != null || World != null;
        public double TravelledMetres { get; private set; }
        public double SpeedMetresPerSecond { get; private set; }

        public RunnerTrackingState TrackingState => RunnerTrackingStatus.Evaluate(
            world != null ? world.HeadPoseReady : Runner != null,
            markerTracker != null && markerTracker.isActiveAndEnabled && markerTracker.RunningFloorDetected,
            world != null ? world.MarkerFloorConfirmed : Runner != null,
            markerTracker == null || (markerTracker.isActiveAndEnabled && markerTracker.RunningFloorTracking),
            world != null ? world.PlacementReady : Runner != null);
        public string TrackingDescription => RunnerTrackingStatus.Label(TrackingState);
        public void BindMarkerTracker(MarkerAndAnchorTracker tracker) => markerTracker = tracker;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetRegistry() => Sessions.Clear();

        public static RunnerSession GetOrCreate(Component owner)
        {
            int sceneId = owner.gameObject.scene.handle;
            if (Sessions.TryGetValue(sceneId, out var existing) && existing != null) return existing;
            var root = new GameObject("Runner Session");
            SceneManager.MoveGameObjectToScene(root, owner.gameObject.scene);
            var session = root.AddComponent<RunnerSession>();
            session.EnsureGhostPlayback();
            Sessions[sceneId] = session;
            return session;
        }

        private void Awake() => EnsureGhostPlayback();

        private void EnsureGhostPlayback()
        {
            GhostPlayback = GetComponent<LapGhostPlayback>();
            if (GhostPlayback == null) GhostPlayback = gameObject.AddComponent<LapGhostPlayback>();
            if (Application.isPlaying && recordsPersistence == null)
                recordsPersistence = RunnerRecordPersistence.ForDirectory(Path.Combine(Application.persistentDataPath, "runner-records"));
        }

        public void BindWorld(StraightTreadmillWorld source)
        {
            if (world == source) return;
            world = source;
            boundWorldDistance = source != null ? source.TravelledMetres : 0;
        }
        public void BindRunner(StadiumTrackPreview source)
        {
            if (runner != source) boundRunnerDistance = source != null ? source.TravelledMetres : 0;
            runner = source;
            suspendedRunner = null;
        }
        public void BindCompanion(TreadmillCompanion source) => companion = source;
        public void UnbindWorld(StraightTreadmillWorld source) { if (world == source) world = null; }
        public void UnbindRunner(StadiumTrackPreview source)
        {
            if (runner != source) return;
            runner = null;
            suspendedRunner = source != null && !source.isActiveAndEnabled ? source : null;
        }
        public void UnbindCompanion(TreadmillCompanion source) { if (companion == source) companion = null; }

        public void RefreshTargetPace() => Checkpoints.SetTargetPace(RunnerSessionSettings.PaceSeconds);

        public void ResetSessionStats(bool restorePersonalBest = true)
        {
            GhostPlayback.ResetTestPosition();
            var activeRunner = Runner;
            var activeWorld = World;
            timedRunner = activeRunner;
            timedWorld = activeWorld;
            double baseline = activeRunner != null ? activeRunner.TravelledMetres :
                activeWorld != null ? activeWorld.TravelledMetres : 0;
            InitializeSession(activeRunner != null ? activeRunner.LapLengthMetres : 400.0,
                restorePersonalBest, CurrentProfile(), baseline);
        }

        private void InitializeSession(double lapLength, bool restore, string profile, double initialDistance)
        {
            SavePersonalBest();
            activeProfile = profile;
            ElapsedSeconds = 0;
            previousDistance = initialDistance;
            sessionStarted = false;
            Checkpoints = new LapCheckpointTracker(lapLength, RunnerSessionSettings.PaceSeconds);
            // Seed at the distance observed on binding, not at an invented zero sample.
            // The tracker can then reject an incomplete first split/lap correctly.
            Checkpoints.Update(initialDistance, 0);
            queuedTracker = Checkpoints;
            queuedRevision = Checkpoints.PersonalBestRevision;
            recordsLoad = restore && recordsPersistence != null && activeProfile != null
                ? recordsPersistence.LoadAsync(activeProfile, lapLength) : null;
            loadingTracker = Checkpoints;
            loadWarning = null;
            RecordsStatus = recordsPersistence == null ? null : recordsPersistence.Error;
        }

        private void LateUpdate() => Tick(Time.deltaTime);

        internal void Tick(float deltaSeconds)
        {
            PollRecords();
            var activeRunner = Runner;
            var activeWorld = World;
            if (activeRunner == null && activeWorld == null)
            {
                // A temporarily inactive source is suspended, not replaced by an empty run.
                SpeedMetresPerSecond = 0;
                GhostPlayback.Suspend();
                SavePersonalBest();
                return;
            }
            string profile = CurrentProfile();
            if (activeRunner != timedRunner || activeWorld != timedWorld || profile != activeProfile)
            {
                double baseline = activeRunner != null
                    ? (activeRunner != timedRunner ? boundRunnerDistance : activeRunner.TravelledMetres)
                    : (activeWorld != timedWorld ? boundWorldDistance : activeWorld.TravelledMetres);
                timedRunner = activeRunner;
                timedWorld = activeWorld;
                GhostPlayback.ResetTestPosition();
                InitializeSession(activeRunner != null ? activeRunner.LapLengthMetres : 400.0, true, profile, baseline);
            }
            SpeedMetresPerSecond = activeRunner != null ? activeRunner.CurrentSpeed :
                activeWorld != null && activeWorld.PlacementReady ? activeWorld.SpeedMetresPerSecond : 0;
            TravelledMetres = activeRunner != null ? activeRunner.TravelledMetres :
                activeWorld != null ? activeWorld.TravelledMetres : 0;
            if (TravelledMetres < previousDistance)
            {
                ElapsedSeconds = 0;
                sessionStarted = false;
                Checkpoints.Update(TravelledMetres, 0);
                GhostPlayback.ResetTestPosition();
            }
            previousDistance = TravelledMetres;
            bool ready = activeRunner != null || (activeWorld != null && activeWorld.PlacementReady && activeWorld.HeightConfirmed);
            bool paused = activeRunner != null ? activeRunner.RequestedSpeedFraction <= 0f :
                activeWorld != null && activeWorld.SessionPaused;
            if (ready && !paused) sessionStarted = true;
            bool clockRunning = ready && sessionStarted && !paused && Time.timeScale > 0;
            if (clockRunning) ElapsedSeconds += deltaSeconds;
            if (activeRunner != null || activeWorld != null) Checkpoints.Update(TravelledMetres, ElapsedSeconds);
            GhostPlayback.Synchronize(Checkpoints, activeRunner, activeWorld, clockRunning, companion, deltaSeconds);
            SavePersonalBest();
        }

        private string CurrentProfile()
        {
            if (Runner != null) return "preview-" + Runner.LapLengthMetres.ToString("F4", CultureInfo.InvariantCulture).Replace('.', '_');
            return World == null ? null : World.RealTrackMode ? "stadium-400" : "treadmill-400";
        }

        private void SavePersonalBest()
        {
            if (recordsPersistence == null || activeProfile == null ||
                (queuedTracker == Checkpoints && queuedRevision == Checkpoints.PersonalBestRevision)) return;
            var snapshot = Checkpoints.ExportPersonalBest();
            recordsPersistence.QueueSave(activeProfile, snapshot);
            // This marks submission only. Pending writes are acknowledged by the worker after success,
            // and retained/retried there on failure, independent of this tracker's lifetime.
            queuedTracker = Checkpoints;
            queuedRevision = Checkpoints.PersonalBestRevision;
        }

        private void PollRecords()
        {
            if (recordsLoad != null && recordsLoad.IsCompleted)
            {
                var completed = recordsLoad;
                recordsLoad = null;
                if (completed.Status == TaskStatus.RanToCompletion && loadingTracker == Checkpoints)
                {
                    loadWarning = completed.Result.Warning;
                    var current = Checkpoints.ExportPersonalBest();
                    var best = completed.Result.Best;
                    if (best != null)
                    {
                        // Loading must not erase a better split measured while the disk read was pending.
                        Checkpoints.TryRestorePersonalBest(RunnerPersonalBest.Merge(best, current));
                        if (!current.HasRecords())
                        {
                            queuedTracker = Checkpoints;
                            queuedRevision = Checkpoints.PersonalBestRevision;
                        }
                    }
                }
                else if (completed.IsFaulted)
                    loadWarning = "Bestzeiten konnten nicht geladen werden: " + completed.Exception.GetBaseException().Message;
            }
            RecordsStatus = recordsPersistence != null && recordsPersistence.Error != null
                ? recordsPersistence.Error : loadWarning;
            if (RecordsStatus != null && RecordsStatus != lastReportedError)
                Debug.LogWarning("[RunnerRecords] " + RecordsStatus);
            lastReportedError = RecordsStatus;
        }

        private void OnApplicationPause(bool paused)
        {
            if (paused) SavePersonalBest();
        }

        private void OnDestroy()
        {
            SavePersonalBest();
            int sceneId = gameObject.scene.handle;
            if (Sessions.TryGetValue(sceneId, out var current) && current == this) Sessions.Remove(sceneId);
        }
    }
}
