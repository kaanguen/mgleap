using UnityEngine;

namespace StadionTracking
{
    public enum TreadmillRunMode
    {
        SideBySide = 0,
        TempoPartner = 1
    }

    /// <summary>Owns the side-by-side avatar; the tempo partner is rendered by LapGhostPlayback.</summary>
    public sealed class TreadmillCompanion : MonoBehaviour
    {
        [SerializeField] private StraightTreadmillWorld world;
        [SerializeField] private GameObject avatarPrefab;
        [SerializeField] private TreadmillRunMode runMode = TreadmillRunMode.SideBySide;
        [SerializeField, Tooltip("Offset in metres from the calibrated belt: positive X is the right-hand neighbouring lane.")]
        private Vector3 sideOffset = new Vector3(1.22f, 0f, 0f);
        private GameObject avatar;
        private Animator animator;
        private float soleOffset;
        private AvatarLocomotion locomotion;

        public bool UseSelectedLane { get; private set; } = true;
        public void UseLanePlacement() { UseSelectedLane = true; PlacementFacing = "RunDirection"; }
        public string PlacementPosition { get; private set; } = "Scene";
        public string PlacementFacing { get; private set; } = "RunDirection";
        public float PlacementDistance => new Vector2(sideOffset.x, sideOffset.z).magnitude;
        public Vector3 PlacementOffset => sideOffset;
        public void ConfigurePlacement(string position, string facing, float distance)
        {
            Vector3 direction;
            switch (position)
            {
                case "N": direction = Vector3.forward; break;
                case "NE": direction = new Vector3(1, 0, 1).normalized; break;
                case "E": direction = Vector3.right; break;
                case "SE": direction = new Vector3(1, 0, -1).normalized; break;
                case "S": direction = Vector3.back; break;
                case "SW": direction = new Vector3(-1, 0, -1).normalized; break;
                case "W": direction = Vector3.left; break;
                case "NW": direction = new Vector3(-1, 0, 1).normalized; break;
                default: throw new System.ArgumentException("Unknown sweet-spot position.");
            }
            if (facing != "FaceUser" && facing != "BackToUser" && facing != "LeftShoulder" && facing != "RightShoulder")
                throw new System.ArgumentException("Unknown sweet-spot facing.");
            if (float.IsNaN(distance) || float.IsInfinity(distance) || distance < .5f || distance > 20)
                throw new System.ArgumentOutOfRangeException(nameof(distance));
            UseSelectedLane = false;
            sideOffset = direction * distance;
            PlacementPosition = position; PlacementFacing = facing;
        }

        public GameObject Avatar => avatar;
        public GameObject AvatarPrefab => avatarPrefab;
        public StraightTreadmillWorld World => world;
        public Animator Animator => animator;
        public float AppliedSpeedMetresPerSecond { get; private set; }
        public TreadmillRunMode Mode
        {
            get => runMode;
            set
            {
                runMode = value == TreadmillRunMode.TempoPartner ? TreadmillRunMode.TempoPartner : TreadmillRunMode.SideBySide;
                if (runMode == TreadmillRunMode.TempoPartner && avatar != null) avatar.SetActive(false);
            }
        }
        public Vector3 GroundPosition
        {
            get
            {
                GetGroundPose(out Vector3 position, out _);
                return position;
            }
        }

        public void SetMode(TreadmillRunMode mode) => Mode = mode;

        private void GetGroundPose(out Vector3 position, out Quaternion rotation)
        {
            if (UseSelectedLane || world.RealTrackMode)
            {
                world.GetLanePoseAtDistance(world.UserDistanceAlongTrack,
                    world.AvatarLane, out position, out rotation);
                return;
            }
            position = world.RunnerGroundPosition + world.RunnerRotation * sideOffset;
            rotation = world.RunnerRotation;
            if (PlacementFacing != "RunDirection")
            {
                Vector3 towardUser = world.RunnerGroundPosition - position;
                towardUser.y = 0;
                if (towardUser.sqrMagnitude > .0001f)
                {
                    float yaw = PlacementFacing == "BackToUser" ? 180 : PlacementFacing == "LeftShoulder" ? 90 : PlacementFacing == "RightShoulder" ? -90 : 0;
                    rotation = Quaternion.LookRotation(towardUser, Vector3.up) * Quaternion.Euler(0, yaw, 0);
                }
            }
        }

        private RunnerSession session;

        private void OnEnable()
        {
            if (!Application.isPlaying) return;
            session = RunnerSession.GetOrCreate(this);
            session.BindCompanion(this);
        }

        private void LateUpdate() => Synchronize();

        public void Synchronize()
        {
            if (runMode == TreadmillRunMode.TempoPartner || world == null || !world.PlacementReady || !world.HeightConfirmed)
            {
                if (avatar != null) avatar.SetActive(false);
                return;
            }
            if (avatar == null)
            {
                if (avatarPrefab == null)
                {
                    Debug.LogError("[Treadmill] Companion avatar prefab is missing.", this);
                    enabled = false;
                    return;
                }
                Spawn();
            }
            avatar.SetActive(true);
            // The world already supplies the virtual forward motion. A second distance integrator
            // would make the companion drift away. Use the fixed belt frame, never the head's gaze.
            GetGroundPose(out Vector3 ground, out Quaternion heading);
            avatar.transform.SetPositionAndRotation(ground - Vector3.up * soleOffset, heading);
            AppliedSpeedMetresPerSecond = world.SessionPaused ? 0f : (1000f / Mathf.Max(1, RunnerSessionSettings.PaceSeconds)) *
                (UseSelectedLane || world.RealTrackMode ? (float)world.AvatarLaneSpeedScale(world.UserDistanceAlongTrack) : 1);
            if (animator == null) return;
            if (locomotion.TryApplyBehaviour()) return;
            var finish=world.GetComponent<RunnerAvatarFinish>();
            if(finish!=null && finish.Apply(avatar,animator,Time.deltaTime))return;
            if (RunnerAvatarGreeting.GetOrCreate(world).Apply(avatar,animator,heading,Time.deltaTime)) return;
            float rate = locomotion.Apply(AppliedSpeedMetresPerSecond);
            if (!locomotion.HasPlaybackRate) animator.speed = AppliedSpeedMetresPerSecond > 0f ? rate : 1f;
        }

        private void Spawn()
        {
            GetGroundPose(out Vector3 ground, out Quaternion heading);
            avatar = Instantiate(avatarPrefab, ground, heading, transform);
            avatar.name = "Running companion";
            AvatarVisibility.Configure(avatar);
            animator = avatar.GetComponentInChildren<Animator>();
            locomotion = AvatarLocomotion.Initialize(animator);
            soleOffset = AvatarGrounding.MeasureSoleOffset(avatar);
            Debug.Log($"[Treadmill] Avatar spawned in {runMode} mode; shares the configured treadmill pace.");
        }

        private void OnDisable()
        {
            if (session != null) session.UnbindCompanion(this);
            if (avatar != null) avatar.SetActive(false);
        }

    }
}
