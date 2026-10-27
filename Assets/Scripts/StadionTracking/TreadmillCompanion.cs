using System.Collections.Generic;
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
        private float walkSpeed = 1.5f, runSpeed = 3f, sprintSpeed = 6f;
        private bool hasRunning, hasBlend, hasRate;
        private static readonly int Running = UnityEngine.Animator.StringToHash("isRunning");
        private static readonly int Blend = UnityEngine.Animator.StringToHash("speed");
        private static readonly int Rate = UnityEngine.Animator.StringToHash("locomotionRate");

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
                world.GetLanePoseAtDistance(world.RealTrackMode ? world.UserTrackPositionMetres - world.RaceStartTrackMetres : world.TravelledMetres,
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
                (UseSelectedLane || world.RealTrackMode ? (float)world.AvatarLaneSpeedScale(world.RealTrackMode ? world.UserTrackPositionMetres - world.RaceStartTrackMetres : world.TravelledMetres) : 1);
            if (animator == null) return;
            LocomotionTiming.Evaluate(AppliedSpeedMetresPerSecond, walkSpeed, runSpeed, sprintSpeed,
                out float blend, out float rate);
            if (hasRunning) animator.SetBool(Running, AppliedSpeedMetresPerSecond > 0f);
            if (hasBlend) animator.SetFloat(Blend, blend);
            if (hasRate) animator.SetFloat(Rate, rate);
            else animator.speed = AppliedSpeedMetresPerSecond > 0f ? rate : 1f;
        }

        private void Spawn()
        {
            GetGroundPose(out Vector3 ground, out Quaternion heading);
            avatar = Instantiate(avatarPrefab, ground, heading, transform);
            avatar.name = "Running companion";
            animator = avatar.GetComponentInChildren<Animator>();
            if (animator != null)
            {
                animator.applyRootMotion = false;
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                foreach (var parameter in animator.parameters)
                {
                    hasRunning |= parameter.nameHash == Running && parameter.type == AnimatorControllerParameterType.Bool;
                    hasBlend |= parameter.nameHash == Blend && parameter.type == AnimatorControllerParameterType.Float;
                    hasRate |= parameter.nameHash == Rate && parameter.type == AnimatorControllerParameterType.Float;
                }
                CalibrateGaits();
                animator.Update(0f);
            }
            soleOffset = LowestPointY() - avatar.transform.position.y;
            Debug.Log($"[Treadmill] Avatar spawned in {runMode} mode; shares the configured treadmill pace.");
        }

        private void CalibrateGaits()
        {
            if (animator.runtimeAnimatorController == null) return;
            foreach (var clip in animator.runtimeAnimatorController.animationClips)
            {
                float speed = new Vector2(clip.averageSpeed.x, clip.averageSpeed.z).magnitude;
                if (float.IsNaN(speed) || float.IsInfinity(speed) || speed < 0.1f) continue;
                switch (clip.name)
                {
                    case "walking": walkSpeed = speed; break;
                    case "runninig": runSpeed = speed; break;
                    case "sprint": sprintSpeed = speed; break;
                }
            }
            if (walkSpeed >= runSpeed || runSpeed >= sprintSpeed)
            { walkSpeed = 1.5f; runSpeed = 3f; sprintSpeed = 6f; }
        }

        private float LowestPointY()
        {
            float lowest = float.PositiveInfinity;
            var baked = new Mesh();
            var vertices = new List<Vector3>();
            try
            {
                foreach (var skin in avatar.GetComponentsInChildren<SkinnedMeshRenderer>())
                {
                    if (skin.sharedMesh == null) continue;
                    skin.BakeMesh(baked);
                    baked.GetVertices(vertices);
                    foreach (var vertex in vertices)
                        lowest = Mathf.Min(lowest, skin.transform.TransformPoint(vertex).y);
                }
            }
            finally
            {
                if (Application.isPlaying) Destroy(baked); else DestroyImmediate(baked);
            }
            if (float.IsPositiveInfinity(lowest))
                foreach (var renderer in avatar.GetComponentsInChildren<MeshRenderer>())
                    lowest = Mathf.Min(lowest, renderer.bounds.min.y);
            return float.IsPositiveInfinity(lowest) ? avatar.transform.position.y : lowest;
        }

        private void OnDisable()
        {
            if (avatar != null) avatar.SetActive(false);
        }

    }
}
