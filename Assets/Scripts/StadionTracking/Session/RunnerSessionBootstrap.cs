using UnityEngine;

namespace StadionTracking
{
    // Applied in Awake, before automatic height calibration starts in Start.
    public sealed class RunnerSessionBootstrap : MonoBehaviour
    {
        private void Awake()
        {
            if (!RunnerSessionSettings.Pending) return;
            var world = GetComponent<StraightTreadmillWorld>();
            var companion = FindFirstObjectByType<TreadmillCompanion>();
            if (world == null) return;
            world.ShowTrack = RunnerSessionSettings.ShowTrack;
            world.SetBodyHeightCentimetres(RunnerSessionSettings.HeightCm);
            world.TrySetPace(RunnerSessionSettings.PaceSeconds / 60, RunnerSessionSettings.PaceSeconds % 60);
            if (companion != null) companion.SetMode(RunnerSessionSettings.Mode);
            RunnerSessionSettings.Pending = false;
        }
    }
}
