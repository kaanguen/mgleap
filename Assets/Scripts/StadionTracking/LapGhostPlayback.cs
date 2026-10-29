using UnityEngine;

namespace StadionTracking
{
    /// <summary>Visual-only avatar driven by the checkpoint tracker's lap recording and clock.</summary>
    public sealed class LapGhostPlayback : MonoBehaviour
    {
        [SerializeField] private bool showGhost = true;
        [SerializeField, Tooltip("Sideways separation from the running line, in metres.")]
        private float laneOffsetMetres = 1.22f;
        [SerializeField, Min(0.5f), Tooltip("Hide the ghost when it comes this close to the tracked user horizontally.")]
        private float userClearanceMetres = 0.9f;
        private GameObject holder, avatar;
        private Animator animator;
        private UnityEngine.Object source;
        private TreadmillCompanion companion;
        private float soleOffset;
        private AvatarLocomotion locomotion;

        private double normalElapsedSeconds;
        private readonly GhostTestMotion testMotion = new GhostTestMotion();
        public bool TestSpeedEnabled => testMotion.Enabled;
        public double TestSpeedKmh => testMotion.SpeedKmh;
        public double CurrentSpeedKmh { get; private set; }
        public double TravelledMetres { get; private set; }
        public void SetTestSpeed(double? speedKmh) => testMotion.SetSpeed(speedKmh, TravelledMetres);
        public void ResetTestPosition() { testMotion.Reset(); normalElapsedSeconds = 0; TravelledMetres = 0; }

        public GameObject Avatar => avatar;
        public double DistanceIntoLap { get; private set; }
        public double GapMetres { get; private set; }
        public bool IsRecordedLap { get; private set; }
        public bool HasRecordedLap { get; private set; }
        public string PlaybackMode => TestSpeedEnabled ? "TestSpeed" : HasRecordedLap ? "RecordedLap" : "TargetPace";
        public bool IsPartnerActive { get; private set; }

        // Called by the scene session after updating the common clock and checkpoint tracker.
        public void Synchronize(LapCheckpointTracker tracker, StadiumTrackPreview runner,
            StraightTreadmillWorld world, bool clockRunning, TreadmillCompanion activeCompanion = null, float? deltaSeconds = null)
        {
            HasRecordedLap = tracker != null && tracker.GhostRecording.HasRecording;
            UnityEngine.Object activeSource = runner != null ? (UnityEngine.Object)runner : world;
            if (source != activeSource)
            {
                ReleaseAvatar();
                source = activeSource;
                ResetTestPosition();
                companion = activeCompanion;
                // Compatibility for callers that synchronize a standalone playback directly.
                if (world != null && companion == null) companion = world.GetComponent<TreadmillCompanion>();
            }
            if (activeCompanion != null) companion = activeCompanion;
            bool ready = runner != null || (world != null && world.PlacementReady && world.HeightConfirmed);
            bool partnerMode = runner != null ? RunnerSessionSettings.Mode == TreadmillRunMode.TempoPartner :
                companion != null && companion.isActiveAndEnabled && companion.Mode == TreadmillRunMode.TempoPartner;
            IsPartnerActive = showGhost && ready && tracker != null && isActiveAndEnabled && partnerMode;
            if (!IsPartnerActive)
            {
                CurrentSpeedKmh = 0;
                if (holder != null) holder.SetActive(false);
                return;
            }
            if (avatar == null)
            {
                GameObject prefab = runner != null ? runner.GhostAvatarPrefab : null;
                if (world != null)
                {
                    if (companion != null) prefab = companion.AvatarPrefab;
                    // The same avatar is never displayed as a side companion and a tempo partner.
                    companion.Synchronize();
                }
                if (prefab == null) { IsPartnerActive = false; CurrentSpeedKmh = 0; return; }
                Spawn(prefab);
            }

            float deltaTime = deltaSeconds ?? Time.deltaTime;
            // The avatar owns its lap clock; a standing user or missing belt signal cannot hold it at the finish.
            if (clockRunning) normalElapsedSeconds += deltaTime;
            tracker.GhostRecording.EvaluateContinuous(normalElapsedSeconds, tracker.LapLengthMetres,
                tracker.BestLapSeconds, out double normalDistance, out double speed);
            double distance = normalDistance % tracker.LapLengthMetres;
            double userDistance = runner != null ? runner.TravelledMetres : world.TravelledMetres;
            double laneSpeedScale = world != null ? world.AvatarLaneSpeedScale(TravelledMetres) : 1;
            if (testMotion.Enabled)
            {
                // Test speed is actual speed on the selected lane, including outer bends.
                testMotion.Advance(deltaTime / laneSpeedScale, clockRunning);
                TravelledMetres = testMotion.Distance;
                distance = TravelledMetres % tracker.LapLengthMetres;
                speed = testMotion.SpeedKmh / 3.6 / laneSpeedScale;
            }
            else TravelledMetres = normalDistance;
            DistanceIntoLap = distance;
            CurrentSpeedKmh = clockRunning ? speed * 3.6 * laneSpeedScale : 0;
            IsRecordedLap = !testMotion.Enabled && tracker.GhostRecording.HasRecording;
            GapMetres = userDistance - TravelledMetres; // Positive means the user leads.
            Vector3 ground;
            Quaternion heading;
            if (runner != null)
                runner.GetRunningPose(TravelledMetres, out ground, out heading);
            else
                world.GetLanePoseAtDistance(TravelledMetres, world.AvatarLane, out ground, out heading);
            if (runner != null) ground += heading * Vector3.right * laneOffsetMetres;
            var warmup = world != null ? world.GetComponent<RunnerWarmup>() : null;
            if (warmup != null && warmup.Active) warmup.TryGetGroundPose(out ground, out heading);
            avatar.transform.SetPositionAndRotation(ground - Vector3.up * soleOffset, heading);
            // Head tracking can move the user away from the calibrated belt centre.
            // Never move the ghost forward to avoid overlap: that would falsify the race gap.
            Camera userCamera = world != null ? world.HeadCamera : Camera.main;
            bool nearUser = false;
            if (userCamera != null || world != null)
            {
                Vector3 userPosition = userCamera != null ? userCamera.transform.position : world.RunnerGroundPosition;
                float clearance = Mathf.Max(0.5f, userClearanceMetres);
                Vector2 separation = new Vector2(ground.x - userPosition.x, ground.z - userPosition.z);
                nearUser = separation.sqrMagnitude < clearance * clearance;
            }
            holder.SetActive(!nearUser);
            if (nearUser && warmup != null) warmup.NotifyHiddenApproach();
            if (warmup != null && holder.activeSelf && warmup.Apply(avatar, animator)) return;
            if(world!=null && holder.activeSelf && animator!=null && !locomotion.TryApplyBehaviour())
            {
                var finish=world.GetComponent<RunnerAvatarFinish>();
                if(finish!=null && finish.Apply(avatar,animator,deltaTime))return;
                if(RunnerAvatarGreeting.GetOrCreate(world).Apply(avatar,animator,heading,deltaTime))return;
            }
            Animate((float)(CurrentSpeedKmh / 3.6));
        }

        private void Spawn(GameObject prefab)
        {
            // An independent world root prevents inheriting the HUD's tiny scale or moving scenery.
            holder = new GameObject("Tempo Partner");
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(holder, gameObject.scene);
            holder.SetActive(false);
            avatar = Instantiate(prefab, holder.transform, false);
            avatar.name = "Tempo partner avatar";
            AvatarVisibility.Configure(avatar);
            foreach (var behaviour in avatar.GetComponentsInChildren<Behaviour>(true))
                if (!(behaviour is Animator)) behaviour.enabled = false;
            foreach (var canvas in avatar.GetComponentsInChildren<Canvas>(true))
                canvas.gameObject.SetActive(false);
            foreach (var collider in avatar.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
            foreach (var body in avatar.GetComponentsInChildren<Rigidbody>(true))
            { body.isKinematic = true; body.detectCollisions = false; }

            // Keep the prefab's normal appearance, matching the side-by-side companion.
            animator = avatar.GetComponentInChildren<Animator>(true);
            holder.SetActive(true);
            if (animator != null)
                animator.updateMode = AnimatorUpdateMode.Normal;
            locomotion = AvatarLocomotion.Initialize(animator);
            soleOffset = AvatarGrounding.MeasureSoleOffset(avatar);
        }

        private void Animate(float speed)
        {
            if (animator == null) return;
            if (locomotion.TryApplyBehaviour()) return;
            float rate = locomotion.Apply(speed);
            animator.speed = speed > 0 ? (locomotion.HasPlaybackRate ? 1 : rate) : 0;
        }

        private static void Dispose(UnityEngine.Object value)
        {
            if (value == null) return;
            if (Application.isPlaying) Destroy(value); else DestroyImmediate(value);
        }

        private void ReleaseAvatar()
        {
            if (holder != null) holder.SetActive(false);
            Dispose(holder);
            holder = avatar = null;
            animator = null;
            locomotion = null;
        }

        internal void Suspend()
        {
            IsPartnerActive = false;
            CurrentSpeedKmh = 0;
            if (holder != null) holder.SetActive(false);
        }

        private void OnDisable() => Suspend();
        private void OnDestroy() => ReleaseAvatar();
    }
}
