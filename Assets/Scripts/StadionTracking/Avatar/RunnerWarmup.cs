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
        public const float TravelSpeed = 1.25f;
        private Vector3 frontGround, travelGround;
        private Quaternion frontHeading, travelHeading;
        private const float TurnSpeed = 240f, UserClearance = 1.05f;
        private Vector3 returnGround;
        private Quaternion returnHeading;
        private readonly List<Vector3> travelPath = new List<Vector3>();
        private int pathIndex;
        private enum TravelPhase { Turn, Move, Align, Settle }
        private TravelPhase travelPhase;
        private bool walking;
        private string completionStatus;
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
        public string Label => behaviour?.Library?.Find(AnimationId)?.DisplayName ?? "";
        public float TotalDuration { get; private set; }
        public float TotalElapsed { get; private set; }
        public bool Approaching { get; private set; }
        public bool Returning { get; private set; }
        public bool Travelling => Approaching || Returning;
        public float TravelDistance => Travelling ? Vector3.Distance(ground, travelGround) : 0;
        public bool WaitingForSpace { get; private set; }
        public float ReturnDistance => Returning ? TravelDistance : 0;
        public bool Finishing => finishing || Returning;
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
            behaviour.Validate("Walking", true, 1);
            if (world == null || !world.PlacementReady || !world.HeightConfirmed || !world.HeadPoseReady)
                throw new InvalidOperationException("Zuerst Bodenmarker und Avatar-Platzierung einrichten.");
            if (Vector3.ProjectOnPlane(world.HeadCamera.transform.forward, Vector3.up).sqrMagnitude < .01f)
                throw new InvalidOperationException("Zum Platzieren des Warm-up-Avatars geradeaus schauen.");
        }

        public static bool IsExercise(string id) => id == "Mixamo_BatterOnDeck" || id == "Mixamo_StartJumpingJacks" ||
            id == "Mixamo_WarmingUp" || id == "Mixamo_ArmStretching" || id == "Mixamo_AirSquat";

        public void Begin(IReadOnlyList<RunnerWarmupStep> plan, TreadmillCompanion companion = null)
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
            frontGround = world.HeadCamera.transform.position + forward * FrontDistance;
            frontGround.y = world.FloorHeightAt(frontGround);
            frontHeading = Quaternion.LookRotation(-forward, Vector3.up);
            companion = companion != null ? companion : world.GetComponent<TreadmillCompanion>();
            if (companion != null && companion.Mode == TreadmillRunMode.SideBySide)
                companion.GetRunGroundPose(out returnGround, out returnHeading);
            else
                world.GetLanePoseAtDistance(RunnerSession.GetOrCreate(world).GhostPlayback.TravelledMetres,
                    world.AvatarLane, out returnGround, out returnHeading);
            // Materialize at the normal lane pose first, then preserve the actually displayed root pose.
            GameObject sourceAvatar = null;
            if (companion != null && companion.Mode == TreadmillRunMode.SideBySide)
            {
                companion.Synchronize(); sourceAvatar = companion.Avatar;
            }
            else if (companion != null)
            {
                var session = RunnerSession.GetOrCreate(world);
                session.GhostPlayback.Synchronize(session.Checkpoints, null, world, false, companion, 0);
                sourceAvatar = session.GhostPlayback.Avatar;
            }
            ground = sourceAvatar != null ? new Vector3(sourceAvatar.transform.position.x, returnGround.y, sourceAvatar.transform.position.z) : returnGround;
            facing = sourceAvatar != null ? sourceAvatar.transform.rotation : returnHeading;
            markerRevision = world.MarkerPlacementRevision;
            StepIndex = 0; StepElapsed = TotalElapsed = 0;
            Active = true; Status = "Approaching"; finishing = Returning = WaitingForSpace = false; Approaching = true; displayed = false;
            world.GetComponent<RunnerAvatarFinish>()?.Cancel(false);
            RunnerAvatarGreeting.GetOrCreate(world).NotifyWarmupStarted();
            BeginTravel(frontGround, frontHeading);
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
            if (!finishing && (!Travelling || walking) && !behaviour.ApplyTo(animator)) return false;
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
            if (Travelling) { TickTravel(seconds); return; }
            if (finishing)
            {
                exitElapsed += seconds;
                if (exitElapsed >= ExitBlend) BeginReturn();
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
            if (!Active || Finishing || Approaching) return;
            TotalElapsed += StepDuration - StepElapsed;
            StepElapsed = StepDuration;
            if (StepIndex + 1 < steps.Length) { StepIndex++; StepElapsed = 0; PlayStep(); }
            else Finish(true);
        }

        public void Finish(bool completed = false)
        {
            if (!Active || Finishing) return;
            completionStatus = completed ? "Completed" : "Stopped";
            Approaching = false; Rest();
            finishing = true; exitElapsed = 0;
            behaviour.StopSmoothly(ExitBlend);
        }

        private static float SegmentDistance(Vector3 point, Vector3 from, Vector3 to)
        {
            point.y = from.y = to.y = 0;
            var line = to - from;
            float t = line.sqrMagnitude > .0001f ? Mathf.Clamp01(Vector3.Dot(point - from, line) / line.sqrMagnitude) : 0;
            return Vector3.Distance(point, from + line * t);
        }

        private void BeginReturn()
        {
            finishing = false; Returning = true; Approaching = false; Status = "Returning";
            BeginTravel(returnGround, returnHeading);
        }

        private void BeginTravel(Vector3 destination, Quaternion heading)
        {
            travelGround = destination; travelHeading = heading;
            WaitingForSpace = false;
            travelPhase = TravelPhase.Turn; pathIndex = 0; walking = false;
            travelPath.Clear();
            // Prefer a short detour if the direct route would cross the user's personal space.
            var user = world.HeadCamera.transform.position;
            if (SegmentDistance(user, ground, travelGround) < UserClearance)
            {
                var direction = travelGround - ground; direction.y = 0;
                var side = Vector3.Cross(Vector3.up, direction.normalized) * 2;
                var a = user + side; a.y = world.FloorHeightAt(a);
                var b = user - side; b.y = world.FloorHeightAt(b);
                bool safeA = SegmentDistance(user, ground, a) >= UserClearance && SegmentDistance(user, a, travelGround) >= UserClearance;
                bool safeB = SegmentDistance(user, ground, b) >= UserClearance && SegmentDistance(user, b, travelGround) >= UserClearance;
                if (safeA || safeB)
                    travelPath.Add(safeA && (!safeB || Vector3.Distance(ground, a) + Vector3.Distance(a, travelGround) < Vector3.Distance(ground, b) + Vector3.Distance(b, travelGround)) ? a : b);
            }
            travelPath.Add(travelGround);
        }

        private void StartWalking()
        {
            if (walking) return;
            var clip = behaviour.Library.Find("Walking").clip;
            float sourceSpeed = new Vector2(clip.averageSpeed.x, clip.averageSpeed.z).magnitude;
            behaviour.Play("Walking", true, Mathf.Clamp(TravelSpeed / (sourceSpeed > .1f ? sourceSpeed : TravelSpeed), .25f, 2));
            walking = true;
        }

        private void Rest()
        {
            if (walking) behaviour.StopSmoothly(ExitBlend);
            walking = false;
        }

        private void TickTravel(float seconds)
        {
            if (travelPhase == TravelPhase.Settle)
            {
                exitElapsed += seconds;
                if (exitElapsed >= ExitBlend)
                {
                    if (Approaching) { Approaching = false; Status = "Running"; displayed = false; PlayStep(); }
                    else Stop(completionStatus);
                }
                return;
            }
            if (travelPhase == TravelPhase.Align)
            {
                facing = Quaternion.RotateTowards(facing, travelHeading, TurnSpeed * seconds);
                if (Quaternion.Angle(facing, travelHeading) < .1f)
                { facing = travelHeading; exitElapsed = 0; travelPhase = TravelPhase.Settle; }
                return;
            }
            var target = travelPath[pathIndex];
            var direction = target - ground; direction.y = 0;
            if (direction.sqrMagnitude < .000001f)
            {
                ground = target; Rest();
                if (++pathIndex < travelPath.Count) travelPhase = TravelPhase.Turn;
                else { travelPhase = TravelPhase.Align; ground = travelGround; }
                return;
            }
            var heading = Quaternion.LookRotation(direction, Vector3.up);
            facing = Quaternion.RotateTowards(facing, heading, TurnSpeed * seconds);
            if (Quaternion.Angle(facing, heading) > 3) { Rest(); return; }
            var next = Vector3.MoveTowards(ground, target, TravelSpeed * seconds);
            var user = world.HeadCamera.transform.position;
            float nextDistance = new Vector2(next.x - user.x, next.z - user.z).magnitude;
            float currentDistance = new Vector2(ground.x - user.x, ground.z - user.z).magnitude;
            // A lane can initially be beside or beneath the user. Allow moving out of that overlap.
            WaitingForSpace = nextDistance < UserClearance && nextDistance <= currentDistance + .0001f;
            if (WaitingForSpace) { Rest(); return; }
            travelPhase = TravelPhase.Move; StartWalking();
            ground = next;
            ground.y = world.FloorHeightAt(ground);
        }

        internal void NotifyHiddenApproach() { if (Approaching) displayed = true; }

        public void Stop(string status = "Stopped")
        {
            if (!Active) return;
            behaviour.Stop();
            Active = false; finishing = Approaching = Returning = WaitingForSpace = walking = false; displayed = false; Status = status;
        }
        private void OnDisable() { if (Active) Stop("Interrupted"); }
    }
}
