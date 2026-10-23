using System;

namespace StadionTracking
{
    // The controller's walking/running/sprint thresholds are 0.25, 0.5 and 1.
    internal static class LocomotionTiming
    {
        internal static void Evaluate(float metresPerSecond, float walkSpeed, float runSpeed,
            float sprintSpeed, out float blend, out float playbackRate)
        {
            if (metresPerSecond <= 0f)
            {
                blend = 0f;
                playbackRate = 1f; // Keep idle and the transition to idle playing normally.
                return;
            }

            if (metresPerSecond < walkSpeed)
            {
                blend = 0.25f;
                playbackRate = metresPerSecond / walkSpeed;
            }
            else if (metresPerSecond < runSpeed)
            {
                blend = 0.25f + 0.25f * (metresPerSecond - walkSpeed) / (runSpeed - walkSpeed);
                playbackRate = 1f;
            }
            else
            {
                blend = 0.5f + 0.5f * Math.Min(1f,
                    (metresPerSecond - runSpeed) / (sprintSpeed - runSpeed));
                playbackRate = Math.Max(1f, metresPerSecond / sprintSpeed);
            }
        }
    }
}
