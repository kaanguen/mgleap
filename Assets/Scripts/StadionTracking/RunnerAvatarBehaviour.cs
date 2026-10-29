using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace StadionTracking
{
    /// <summary>Scene-scoped manual animation selection shared by companion and tempo partner.</summary>
    public sealed class RunnerAvatarBehaviour : MonoBehaviour
    {
        public const string LibraryResource = "JoggerBehaviourLibrary";
        private const string AutoState = "Base Layer.idle";
        private static readonly int Rate = Animator.StringToHash("behaviourRate");
        private static readonly Dictionary<int, RunnerAvatarBehaviour> Instances = new Dictionary<int, RunnerAvatarBehaviour>();
        private readonly Dictionary<Animator, int> applied = new Dictionary<Animator, int>();
        private RunnerAvatarAnimationLibrary library;
        private RunnerAvatarAnimation selected;
        private int revision;
        public bool Active => selected != null;
        public string AnimationId => selected != null ? selected.id : "Automatic";
        public string AnimationLabel => selected != null ? selected.DisplayName : "Automatische Laufbewegung";
        public bool Loop { get; private set; }
        public float PlaybackSpeed { get; private set; } = 1;
        public float ElapsedSeconds { get; private set; }
        public float HoldSeconds { get; private set; } = 10;
        private const float PhaseBlend=.18f;
        public float DurationSeconds => selected == null ? 0 : selected.IsSequence ? selected.startClip.length+HoldSeconds+selected.endClip.length-2*PhaseBlend : selected.Duration;
        private float CycleTime => Loop && DurationSeconds>0 ? ElapsedSeconds%DurationSeconds : ElapsedSeconds;
        private int SequencePhase => !Active || !selected.IsSequence ? 0 : CycleTime<selected.startClip.length-PhaseBlend ? 0 : CycleTime<selected.startClip.length+HoldSeconds-2*PhaseBlend ? 1 : 2;

        public RunnerAvatarAnimationLibrary Library => library != null ? library : library = Resources.Load<RunnerAvatarAnimationLibrary>(LibraryResource);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetRegistry() => Instances.Clear();

        public static RunnerAvatarBehaviour GetOrCreate(Component owner)
        {
            int scene = owner.gameObject.scene.handle;
            if (Instances.TryGetValue(scene, out var existing) && existing != null) return existing;
            var instance = owner.GetComponent<RunnerAvatarBehaviour>();
            if (instance == null) instance = owner.gameObject.AddComponent<RunnerAvatarBehaviour>();
            Instances[scene] = instance;
            return instance;
        }
        public static RunnerAvatarBehaviour ForScene(Scene scene)
        {
            Instances.TryGetValue(scene.handle, out var instance);
            return instance;
        }

        public void Validate(string id, bool loop, float speed, float holdSeconds=10)
        {
            if (string.IsNullOrWhiteSpace(id) || Library == null || Library.Find(id)?.clip == null)
                throw new ArgumentException("Diese Avatar-Animation ist nicht verfügbar: " + id);
            if(id=="Mixamo_Plank" && (float.IsNaN(holdSeconds) || float.IsInfinity(holdSeconds) || holdSeconds<1 || holdSeconds>300))
                throw new ArgumentOutOfRangeException(nameof(holdSeconds),"Plank-Haltedauer muss zwischen 1 und 300 Sekunden liegen.");
            if (float.IsNaN(speed) || float.IsInfinity(speed) || speed < .25f || speed > 2)
                throw new ArgumentOutOfRangeException(nameof(speed), "Animationstempo muss zwischen 0,25 und 2 liegen.");
        }
        public void Play(string id, bool loop = true, float speed = 1, float holdSeconds=10)
        {
            Validate(id, loop, speed, holdSeconds);
            selected = Library.Find(id);
            HoldSeconds = holdSeconds;
            Loop = id == "Mixamo_StartJumpingJacks" || loop;
            PlaybackSpeed = speed;
            ElapsedSeconds = 0;
            revision++;
        }

        // Called before normal locomotion in both avatar owners, including while the run is paused.
        internal bool ApplyTo(Animator animator)
        {
            if (!Active || animator == null || !animator.isActiveAndEnabled) return false;
            int phase=SequencePhase;
            string phaseName=selected.IsSequence ? new[]{"_Start","_Hold","_End"}[phase] : "";
            int state = Animator.StringToHash("Base Layer." + selected.stateName+phaseName);
            if (!animator.HasState(0, state)) return false;
            animator.applyRootMotion = false;
            animator.speed = 1;
            animator.SetFloat(Rate, PlaybackSpeed);
            if (!applied.TryGetValue(animator, out int version) || version != revision)
            {
                float offset=0;
                if(selected.IsSequence)
                {
                    offset=phase==0?CycleTime:phase==1?(CycleTime-selected.startClip.length+PhaseBlend)%selected.holdClip.length:CycleTime-selected.startClip.length-HoldSeconds+2*PhaseBlend;
                }
                animator.CrossFadeInFixedTime(state, .18f, 0, Mathf.Max(0,offset));
                applied[animator] = revision;
            }
            return true;
        }

        public void Tick(float seconds)
        {
            if (!Active || float.IsNaN(seconds) || float.IsInfinity(seconds) || seconds <= 0) return;
            // Wait for an avatar to actually display the request before timing a one-shot.
            bool displayed = false;
            foreach (var pair in applied)
                if (pair.Key != null && pair.Key.isActiveAndEnabled && pair.Value == revision) { displayed = true; break; }
            if (!displayed) return;
            int previousPhase=SequencePhase;
            int previousCycle=DurationSeconds>0?(int)(ElapsedSeconds/DurationSeconds):0;
            ElapsedSeconds += seconds * PlaybackSpeed;
            if(selected.IsSequence && (previousPhase!=SequencePhase || (Loop && previousCycle!=(int)(ElapsedSeconds/DurationSeconds))))revision++;
            if (!Loop && ElapsedSeconds >= DurationSeconds) Stop();
        }
        private void Update() => Tick(Time.deltaTime);

        public void Stop() => Clear(0);
        public void StopSmoothly(float blendSeconds) => Clear(Mathf.Max(0, blendSeconds));
        private void Clear(float blendSeconds)
        {
            foreach (var pair in applied)
            {
                var animator = pair.Key;
                if (animator == null) continue;
                animator.speed = 1;
                if (animator.HasState(0, Animator.StringToHash(AutoState)))
                {
                    animator.SetBool("isRunning", false);
                    animator.SetFloat("speed", 0);
                    animator.SetFloat("locomotionRate", 1);
                    // Immediate idle also works for a stationary ghost whose animation is frozen next frame.
                    if (blendSeconds > 0) animator.CrossFadeInFixedTime(AutoState, blendSeconds, 0, 0);
                    else animator.Play(AutoState, 0, 0);
                    if (animator.isActiveAndEnabled) animator.Update(0);
                }
            }
            applied.Clear();
            selected = null;
            Loop = false;
            PlaybackSpeed = 1;
            ElapsedSeconds = 0;
        }
        private void OnDestroy()
        {
            Stop();
            int handle = gameObject.scene.handle;
            if (Instances.TryGetValue(handle, out var value) && value == this) Instances.Remove(handle);
        }
    }
}
