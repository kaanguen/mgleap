namespace StadionTracking
{
    public enum RunnerSessionState { Stopped, Ready, Running, Paused }
    public enum RunnerTrackingState { WaitingForMarker, FloorDetected, Placed, TrackingLost }

    public static class RunnerTrackingStatus
    {
        public static RunnerTrackingState Evaluate(bool headReady, bool floorDetected,
            bool floorConfirmed, bool floorTracked, bool placementReady)
        {
            if (!headReady || (floorConfirmed && !floorTracked)) return RunnerTrackingState.TrackingLost;
            if (floorConfirmed && placementReady) return RunnerTrackingState.Placed;
            return floorDetected ? RunnerTrackingState.FloorDetected : RunnerTrackingState.WaitingForMarker;
        }

        public static string Label(RunnerTrackingState state)
        {
            switch (state)
            {
                case RunnerTrackingState.Placed: return "BAHN PLATZIERT";
                case RunnerTrackingState.FloorDetected: return "BODEN ERKANNT · MARKER SCANNEN";
                case RunnerTrackingState.TrackingLost: return "TRACKING VERLOREN";
                default: return "BODENMARKER UND BODEN SUCHEN";
            }
        }
    }
}
