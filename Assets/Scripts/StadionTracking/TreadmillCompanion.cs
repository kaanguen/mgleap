using System.Collections.Generic;
using UnityEngine;

namespace StadionTracking
{
    /// <summary>A companion at the same virtual progress as the treadmill runner.</summary>
    public sealed class TreadmillCompanion : MonoBehaviour
    {
        [SerializeField] private StraightTreadmillWorld world;
        [SerializeField] private GameObject avatarPrefab;
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

        public GameObject Avatar => avatar;
        public Animator Animator => animator;
        public float AppliedSpeedMetresPerSecond { get; private set; }
        public Vector3 GroundPosition => world.RunnerGroundPosition + world.RunnerRotation * sideOffset;

        private void LateUpdate() => Synchronize();

        public void Synchronize()
        {
            if (world == null || !world.PlacementReady || !world.HeightConfirmed)
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
            avatar.transform.SetPositionAndRotation(GroundPosition - Vector3.up * soleOffset, world.RunnerRotation);
            AppliedSpeedMetresPerSecond = world.SpeedMetresPerSecond;
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
            avatar = Instantiate(avatarPrefab, GroundPosition, world.RunnerRotation, transform);
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
            Debug.Log("[Treadmill] Companion spawned on adjacent lane; shares treadmill speed and progress.");
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
