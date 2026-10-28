using UnityEngine;

namespace StadionTracking
{
    /// <summary>Shared animator parameters and imported gait calibration for runner avatars.</summary>
    internal sealed class AvatarLocomotion
    {
        private static readonly int Running = Animator.StringToHash("isRunning");
        private static readonly int Blend = Animator.StringToHash("speed");
        private static readonly int Rate = Animator.StringToHash("locomotionRate");
        private readonly Animator animator;
        private readonly bool hasRunning, hasBlend;
        private float walkSpeed = 1.5f, runSpeed = 3f, sprintSpeed = 6f;

        internal bool HasPlaybackRate { get; }

        internal AvatarLocomotion(Animator animator)
        {
            this.animator = animator;
            foreach (var parameter in animator.parameters)
            {
                hasRunning |= parameter.nameHash == Running && parameter.type == AnimatorControllerParameterType.Bool;
                hasBlend |= parameter.nameHash == Blend && parameter.type == AnimatorControllerParameterType.Float;
                HasPlaybackRate |= parameter.nameHash == Rate && parameter.type == AnimatorControllerParameterType.Float;
            }
            if (animator.runtimeAnimatorController == null) return;
            // Imported root velocities calibrate the gaits even when placement disables root motion.
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

        // Callers retain their own idle/freeze policy through Animator.speed.
        internal float Apply(float speed, float? legacyBlend = null)
        {
            LocomotionTiming.Evaluate(speed, walkSpeed, runSpeed, sprintSpeed, out float blend, out float rate);
            if (hasRunning) animator.SetBool(Running, speed > 0f);
            if (hasBlend) animator.SetFloat(Blend, !HasPlaybackRate && legacyBlend.HasValue ? legacyBlend.Value : blend);
            if (HasPlaybackRate) animator.SetFloat(Rate, rate);
            return rate;
        }
    }
}
