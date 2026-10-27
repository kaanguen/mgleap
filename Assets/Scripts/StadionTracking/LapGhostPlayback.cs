using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

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
        private Material material;
        private Animator animator;
        private UnityEngine.Object source;
        private TreadmillCompanion companion;
        private float soleOffset;
        private float walkSpeed = 1.5f, runSpeed = 3f, sprintSpeed = 6f;
        private bool hasRunning, hasBlend, hasRate;
        private static readonly int Running = Animator.StringToHash("isRunning");
        private static readonly int Blend = Animator.StringToHash("speed");
        private static readonly int Rate = Animator.StringToHash("locomotionRate");

        public GameObject Avatar => avatar;
        public double DistanceIntoLap { get; private set; }
        public double GapMetres { get; private set; }
        public bool IsRecordedLap { get; private set; }
        public bool IsPartnerActive { get; private set; }

        // Called by ActiveStatsDisplay after it updates the common clock and checkpoint tracker.
        public void Synchronize(LapCheckpointTracker tracker, StadiumTrackPreview runner,
            StraightTreadmillWorld world, bool clockRunning)
        {
            UnityEngine.Object activeSource = runner != null ? (UnityEngine.Object)runner : world;
            if (source != activeSource)
            {
                ReleaseAvatar();
                source = activeSource;
                companion = null;
            }
            bool ready = runner != null || (world != null && world.PlacementReady && world.HeightConfirmed);
            if (world != null && companion == null)
            {
                companion = world.GetComponent<TreadmillCompanion>();
                if (companion == null)
                    foreach (var candidate in FindObjectsOfType<TreadmillCompanion>())
                        if (candidate.World == world) { companion = candidate; break; }
            }
            bool partnerMode = runner != null ? RunnerSessionSettings.Mode == TreadmillRunMode.TempoPartner :
                companion != null && companion.isActiveAndEnabled && companion.Mode == TreadmillRunMode.TempoPartner;
            IsPartnerActive = showGhost && ready && tracker != null && isActiveAndEnabled && partnerMode;
            if (!IsPartnerActive)
            {
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
                if (prefab == null) return;
                if (!Spawn(prefab)) return;
            }

            tracker.GhostRecording.Evaluate(tracker.LapElapsedSeconds, tracker.LapLengthMetres,
                tracker.BestLapSeconds, out double distance, out double speed);
            DistanceIntoLap = distance;
            IsRecordedLap = tracker.GhostRecording.HasRecording;
            double userDistance = runner != null ? runner.TravelledMetres : world.TravelledMetres;
            double lapStart = Math.Floor(userDistance / tracker.LapLengthMetres) * tracker.LapLengthMetres;
            GapMetres = userDistance - lapStart - distance; // Positive means the user leads.
            Vector3 ground;
            Quaternion heading;
            if (runner != null)
                runner.GetRunningPose(lapStart + distance, out ground, out heading);
            else
                world.GetRunningPose((float)(lapStart + distance - userDistance), out ground, out heading);
            ground += heading * Vector3.right * laneOffsetMetres;
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
            Animate(clockRunning ? (float)speed : 0f);
        }

        private bool Spawn(GameObject prefab)
        {
            Shader shader = Resources.Load<Shader>("LapGhost");
            if (shader == null)
            {
                Debug.LogError("[LapGhost] LapGhost shader is missing.", this);
                return false;
            }
            // An independent world root prevents inheriting the HUD's tiny scale or moving scenery.
            holder = new GameObject("Tempo Partner");
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(holder, gameObject.scene);
            holder.SetActive(false);
            avatar = Instantiate(prefab, holder.transform, false);
            avatar.name = "Tempo partner avatar";
            foreach (var behaviour in avatar.GetComponentsInChildren<Behaviour>(true))
                if (!(behaviour is Animator)) behaviour.enabled = false;
            foreach (var canvas in avatar.GetComponentsInChildren<Canvas>(true))
                canvas.gameObject.SetActive(false);
            foreach (var collider in avatar.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
            foreach (var body in avatar.GetComponentsInChildren<Rigidbody>(true))
            { body.isKinematic = true; body.detectCollisions = false; }

            material = new Material(shader);
            foreach (var renderer in avatar.GetComponentsInChildren<Renderer>(true))
            {
                renderer.enabled = renderer is SkinnedMeshRenderer || renderer is MeshRenderer;
                if (!renderer.enabled) continue;
                var materials = new Material[renderer.sharedMaterials.Length];
                for (int i = 0; i < materials.Length; i++) materials[i] = material;
                renderer.sharedMaterials = materials;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
            }
            animator = avatar.GetComponentInChildren<Animator>(true);
            hasRunning = hasBlend = hasRate = false;
            walkSpeed = 1.5f; runSpeed = 3f; sprintSpeed = 6f;
            holder.SetActive(true);
            if (animator != null)
            {
                animator.applyRootMotion = false;
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                animator.updateMode = AnimatorUpdateMode.Normal;
                foreach (var parameter in animator.parameters)
                {
                    hasRunning |= parameter.nameHash == Running && parameter.type == AnimatorControllerParameterType.Bool;
                    hasBlend |= parameter.nameHash == Blend && parameter.type == AnimatorControllerParameterType.Float;
                    hasRate |= parameter.nameHash == Rate && parameter.type == AnimatorControllerParameterType.Float;
                }
                if (animator.runtimeAnimatorController != null)
                    foreach (var clip in animator.runtimeAnimatorController.animationClips)
                    {
                        float velocity = new Vector2(clip.averageSpeed.x, clip.averageSpeed.z).magnitude;
                        if (float.IsNaN(velocity) || float.IsInfinity(velocity) || velocity < 0.1f) continue;
                        if (clip.name == "walking") walkSpeed = velocity;
                        if (clip.name == "runninig") runSpeed = velocity;
                        if (clip.name == "sprint") sprintSpeed = velocity;
                    }
                if (walkSpeed >= runSpeed || runSpeed >= sprintSpeed)
                { walkSpeed = 1.5f; runSpeed = 3f; sprintSpeed = 6f; }
                animator.Update(0);
            }
            soleOffset = MeasureSoleOffset();
            return true;
        }

        private float MeasureSoleOffset()
        {
            float lowest = float.PositiveInfinity;
            var mesh = new Mesh();
            var vertices = new List<Vector3>();
            try
            {
                foreach (var skin in avatar.GetComponentsInChildren<SkinnedMeshRenderer>())
                {
                    if (skin.sharedMesh == null) continue;
                    skin.BakeMesh(mesh);
                    mesh.GetVertices(vertices);
                    foreach (var vertex in vertices)
                        lowest = Mathf.Min(lowest, skin.transform.TransformPoint(vertex).y);
                }
                if (float.IsPositiveInfinity(lowest))
                    foreach (var renderer in avatar.GetComponentsInChildren<MeshRenderer>())
                        lowest = Mathf.Min(lowest, renderer.bounds.min.y);
            }
            finally { Dispose(mesh); }
            return float.IsPositiveInfinity(lowest) ? 0 : lowest - avatar.transform.position.y;
        }

        private void Animate(float speed)
        {
            if (animator == null) return;
            LocomotionTiming.Evaluate(speed, walkSpeed, runSpeed, sprintSpeed, out float blend, out float rate);
            if (hasRunning) animator.SetBool(Running, speed > 0);
            if (hasBlend) animator.SetFloat(Blend, blend);
            if (hasRate) animator.SetFloat(Rate, rate);
            animator.speed = speed > 0 ? (hasRate ? 1 : rate) : 0;
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
            Dispose(material);
            holder = avatar = null;
            material = null;
            animator = null;
        }

        private void OnDisable()
        {
            IsPartnerActive = false;
            if (holder != null) holder.SetActive(false);
        }
        private void OnDestroy() => ReleaseAvatar();
    }
}
