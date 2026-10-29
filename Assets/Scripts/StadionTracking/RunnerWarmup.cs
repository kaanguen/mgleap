using System;
using System.Collections.Generic;
using UnityEngine;

namespace StadionTracking
{
    [Serializable]
    public sealed class RunnerWarmupStep
    {
        public string animationId;
        public float seconds;
        public RunnerWarmupStep(string id, float duration) { animationId = id; seconds = duration; }
    }

    /// <summary>Pre-run exercise sequence. Shares the existing Mixamo playback, never the run clock.</summary>
    public sealed class RunnerWarmup : MonoBehaviour
    {
        public const float FrontDistance = 2.5f;
        private const float ExitBlend = .4f;
        private StraightTreadmillWorld world;
        private RunnerAvatarBehaviour behaviour;
        private RunnerWarmupStep[] steps = Array.Empty<RunnerWarmupStep>();
        private Vector3 ground;
        private Quaternion facing;
        private int markerRevision;
        private bool displayed, finishing;
        private float exitElapsed;
        public bool Active { get; private set; }
        public string Status { get; private set; } = "Idle";
        public int StepIndex { get; private set; }
        public int StepCount => steps.Length;
        public float StepElapsed { get; private set; }
        public float StepDuration => steps.Length == 0 ? 0 : steps[StepIndex].seconds;
        public string AnimationId => steps.Length == 0 ? null : steps[StepIndex].animationId;
        public string Label => behaviour?.Library?.Find(AnimationId)?.label ?? "";
        public float TotalDuration { get; private set; }
        public float TotalElapsed { get; private set; }
        public bool Finishing => finishing;
        public IReadOnlyList<RunnerWarmupStep> Steps => steps;

        public static RunnerWarmup GetOrCreate(StraightTreadmillWorld owner)
        {
            var warmup = owner.GetComponent<RunnerWarmup>();
            if (warmup == null) warmup = owner.gameObject.AddComponent<RunnerWarmup>();
            warmup.world = owner;
            warmup.behaviour = RunnerAvatarBehaviour.GetOrCreate(owner);
            return warmup;
        }

        public void Validate(IReadOnlyList<RunnerWarmupStep> plan)
        {
            if (plan == null || plan.Count < 1 || plan.Count > 20)
                throw new ArgumentException("Wähle 1 bis 20 Warm-up-Schritte.");
            foreach (var step in plan)
            {
                if (step == null || !IsExercise(step.animationId))
                    throw new ArgumentException("Im Warm-up sind nur die fünf Mixamo-Aufwärmübungen ohne Plank verfügbar.");
                if (float.IsNaN(step.seconds) || float.IsInfinity(step.seconds) || step.seconds < 5 || step.seconds > 300 || step.seconds != Mathf.Floor(step.seconds))
                    throw new ArgumentException("Die Dauer jeder Übung muss 5 bis 300 ganze Sekunden betragen.");
                behaviour.Validate(step.animationId, true, 1);
            }
            if (world == null || !world.PlacementReady || !world.HeightConfirmed || !world.HeadPoseReady)
                throw new InvalidOperationException("Zuerst Bodenmarker und Avatar-Platzierung einrichten.");
            if (Vector3.ProjectOnPlane(world.HeadCamera.transform.forward, Vector3.up).sqrMagnitude < .01f)
                throw new InvalidOperationException("Zum Platzieren des Warm-up-Avatars geradeaus schauen.");
        }

        public static bool IsExercise(string id) => id == "Mixamo_BatterOnDeck" || id == "Mixamo_StartJumpingJacks" ||
            id == "Mixamo_WarmingUp" || id == "Mixamo_ArmStretching" || id == "Mixamo_AirSquat";

        public void Begin(IReadOnlyList<RunnerWarmupStep> plan)
        {
            Validate(plan);
            if (!world.SessionPaused) throw new InvalidOperationException("Warm-up ist nur vor dem Lauf verfügbar.");
            Stop();
            steps = new RunnerWarmupStep[plan.Count];
            TotalDuration = 0;
            for (int i = 0; i < plan.Count; i++)
            {
                steps[i] = new RunnerWarmupStep(plan[i].animationId, plan[i].seconds);
                TotalDuration += steps[i].seconds;
            }
            var forward = Vector3.ProjectOnPlane(world.HeadCamera.transform.forward, Vector3.up).normalized;
            ground = world.HeadCamera.transform.position + forward * FrontDistance;
            ground.y = world.FloorHeightAt(ground);
            facing = Quaternion.LookRotation(-forward, Vector3.up);
            markerRevision = world.MarkerPlacementRevision;
            StepIndex = 0; StepElapsed = TotalElapsed = 0;
            Active = true; Status = "Running"; finishing = false; displayed = false;
            world.GetComponent<RunnerAvatarFinish>()?.Cancel(false);
            RunnerAvatarGreeting.GetOrCreate(world).NotifyWarmupStarted();
            PlayStep();
        }

        private void PlayStep()
        {
            displayed = false;
            behaviour.Play(AnimationId, true, 1);
        }

        public bool TryGetGroundPose(out Vector3 position, out Quaternion rotation)
        {
            position = ground; rotation = facing; return Active;
        }

        public bool Apply(GameObject avatar, Animator animator)
        {
            if (!Active || avatar == null || animator == null || !animator.isActiveAndEnabled) return false;
            var look = animator.GetComponent<RunnerGreetingLook>();
            if (look != null) look.SetTarget(null, 0);
            animator.SetBool("isRunning", false); animator.SetFloat("speed", 0);
            animator.applyRootMotion = false; animator.speed = 1;
            if (!finishing && !behaviour.ApplyTo(animator)) return false;
            displayed = true;
            return true;
        }

        private void Update() => Tick(Time.deltaTime);
        public void Tick(float seconds)
        {
            if (!Active) return;
            if (!world.SessionPaused || !world.PlacementReady || !world.HeightConfirmed || !world.HeadPoseReady || markerRevision != world.MarkerPlacementRevision)
            { Stop("Interrupted"); return; }
            if (!displayed || float.IsNaN(seconds) || float.IsInfinity(seconds) || seconds <= 0) return;
            displayed = false;
            if (finishing)
            {
                exitElapsed += seconds;
                if (exitElapsed >= ExitBlend) Stop("Completed");
                return;
            }
            // Count only time actually shown. A newly selected step must be displayed before its clock starts.
            float remaining = StepDuration - StepElapsed;
            float advance = Mathf.Min(seconds, remaining);
            StepElapsed += advance; TotalElapsed += advance;
            if (StepElapsed >= StepDuration) Next();
        }

        public void Next()
        {
            if (!Active || finishing) return;
            TotalElapsed += StepDuration - StepElapsed;
            StepElapsed = StepDuration;
            if (StepIndex + 1 < steps.Length) { StepIndex++; StepElapsed = 0; PlayStep(); }
            else
            {
                finishing = true; exitElapsed = 0;
                behaviour.StopSmoothly(ExitBlend);
            }
        }

        public void Stop(string status = "Stopped")
        {
            if (!Active) return;
            behaviour.Stop();
            Active = false; finishing = false; displayed = false; Status = status;
        }
        private void OnDisable() { if (Active) Stop("Interrupted"); }
    }
}
