using System.Globalization;
using TMPro;
using UnityEngine;

namespace StadionTracking
{
    [DefaultExecutionOrder(200)]
    public sealed class ActiveStatsDisplay : MonoBehaviour
    {
        [SerializeField] private Canvas statsCanvas;
        [SerializeField] private TMP_Text statsReadout;
        [SerializeField] private TMP_Text timerReadout;
        [SerializeField] private TMP_Text distanceReadout;
        [SerializeField] private TMP_Text paceReadout;
        [SerializeField] private TMP_Text checkpointReadout;
        private TMP_Text partnerReadout, trackingReadout;
        [SerializeField] private Camera headCamera;
        [SerializeField] private StraightTreadmillWorld treadmillWorld;
        [SerializeField, Min(0.05f)] private float refreshInterval = 0.15f;

        private float nextRefresh;
        private RunnerSession session;
        private RunnerSession Session => session != null ? session : (session = RunnerSession.GetOrCreate(this));

        public float ElapsedSeconds => Session.ElapsedSeconds;
        public double? Recorded100mSeconds => Session.Checkpoints.Recorded100mSeconds;
        public double? RecordedLapSeconds => Session.Checkpoints.RecordedLapSeconds;
        public double? Recorded1KmSeconds => Session.Checkpoints.Recorded1KmSeconds;

        public void RefreshTargetPace() { Session.RefreshTargetPace(); nextRefresh = 0; }
        public void ResetSessionStats() { Session.ResetSessionStats(); nextRefresh = 0; }

        private void Awake()
        {
            if (statsCanvas == null)
                statsCanvas = GetComponent<Canvas>();
            if (statsReadout == null)
                statsReadout = GetComponentInChildren<TMP_Text>(true);
            EnsureCheckpointReadout();
            EnsurePartnerReadout();
            EnsureTrackingReadout();
            session = RunnerSession.GetOrCreate(this);
            if (treadmillWorld != null) session.BindWorld(treadmillWorld);

            if (statsCanvas == null || (statsReadout == null && timerReadout == null))
            {
                Debug.LogError("[ActiveStats] Assign a Canvas and a TextMeshPro readout.", this);
                enabled = false;
            }
            if (GetComponent<RunnerArPanel>() == null) gameObject.AddComponent<RunnerArPanel>();
        }

        public void BindRunner(StadiumTrackPreview activeRunner)
        {
            Session.BindRunner(activeRunner);
            nextRefresh = 0;
        }

        private void LateUpdate()
        {
            if (headCamera == null)
                headCamera = Camera.main;
            if (headCamera == null)
                return;

            statsCanvas.worldCamera = headCamera;

            var activeSession = Session;
            var activeRunner = activeSession.Runner;
            var activeTreadmillWorld = activeSession.World;
            var checkpointTracker = activeSession.Checkpoints;
            var ghostPlayback = activeSession.GhostPlayback;
            double travelledMetres = activeSession.TravelledMetres;
            double speedMetresPerSecond = activeSession.SpeedMetresPerSecond;

            if (Time.unscaledTime < nextRefresh)
                return;

            string timeValue = FormatElapsedTime(activeSession.ElapsedSeconds);
            string distanceValue = (travelledMetres / 1000.0).ToString("F2", CultureInfo.GetCultureInfo("de-DE"));
            string paceValue = FormatPace(speedMetresPerSecond);
            if (timerReadout != null)
            {
                timerReadout.text = "<mspace=34>" + timeValue + "</mspace>";
                distanceReadout.text = "<mspace=16>" + distanceValue + "</mspace>";
                paceReadout.text = "<mspace=16>" + paceValue + "</mspace>";
            }
            else if (statsReadout != null) statsReadout.text = activeRunner == null && activeTreadmillWorld == null
                ? FormatStats("--:--", "--:--", "--,--")
                : FormatStats(
                    paceValue, timeValue, distanceValue);
            if (checkpointReadout != null)
                checkpointReadout.text = activeRunner == null && activeTreadmillWorld == null
                    ? "CHECKPOINT --"
                    : FormatCheckpointStatus(checkpointTracker);
            if (partnerReadout != null)
            {
                partnerReadout.gameObject.SetActive(ghostPlayback.IsPartnerActive);
                partnerReadout.text = ghostPlayback.TestSpeedEnabled
                    ? $"GHOST TEST {ghostPlayback.TestSpeedKmh:F1} km/h"
                    : checkpointTracker.GhostRecording.HasRecording
                    ? $"BESTE RUNDE {FormatSplitTime(checkpointTracker.BestLapSeconds)}"
                    : $"ZIELPACE {RunnerSessionSettings.PaceSeconds / 60}:{RunnerSessionSettings.PaceSeconds % 60:00} min/km";
            }
            if (trackingReadout != null)
            {
                trackingReadout.text = activeSession.TrackingDescription;
                trackingReadout.color = activeSession.TrackingState == RunnerTrackingState.TrackingLost
                    ? RunnerUiTheme.Warning : RunnerUiTheme.Label;
            }
            nextRefresh = Time.unscaledTime + refreshInterval;
        }

        private void EnsureTrackingReadout()
        {
            if (statsCanvas == null) return;
            trackingReadout = RunnerUiTheme.Text("TrackingStatus", statsCanvas.transform, "", 20, 292, 260, 40, 13);
            var rect = (RectTransform)statsCanvas.transform;
            rect.sizeDelta = new Vector2(rect.sizeDelta.x, Mathf.Max(396, rect.sizeDelta.y));
            MoveHudButton(statsCanvas.transform, "PauseSession", 342);
            MoveHudButton(statsCanvas.transform, "BackToMenu", 342);
        }

        private void EnsurePartnerReadout()
        {
            if (statsCanvas == null || checkpointReadout == null) return;
            partnerReadout = statsCanvas.transform.Find("TempoPartnerStatus")?.GetComponent<TMP_Text>();
            if (partnerReadout == null)
            {
                partnerReadout = Instantiate(checkpointReadout, statsCanvas.transform, false);
                partnerReadout.name = "TempoPartnerStatus";
            }
            partnerReadout.rectTransform.anchoredPosition = new Vector2(20, -267);
            partnerReadout.rectTransform.sizeDelta = new Vector2(260, 22);
            partnerReadout.text = "";
            partnerReadout.gameObject.SetActive(false);
            var rect = (RectTransform)statsCanvas.transform;
            rect.sizeDelta = new Vector2(rect.sizeDelta.x, Mathf.Max(354, rect.sizeDelta.y));
            MoveHudButton(statsCanvas.transform, "PauseSession", 300);
            MoveHudButton(statsCanvas.transform, "BackToMenu", 300);
        }

        private static string FormatPace(double speedMetresPerSecond)
        {
            if (speedMetresPerSecond <= 0 || double.IsNaN(speedMetresPerSecond) ||
                double.IsInfinity(speedMetresPerSecond))
                return "--:--";

            int secondsPerKilometre = Mathf.RoundToInt((float)(1000.0 / speedMetresPerSecond));
            return $"{secondsPerKilometre / 60}:{secondsPerKilometre % 60:00}";
        }

        private static string FormatElapsedTime(float elapsedSeconds)
        {
            int totalSeconds = Mathf.FloorToInt(elapsedSeconds);
            int hours = totalSeconds / 3600;
            int minutes = totalSeconds / 60 % 60;
            int seconds = totalSeconds % 60;
            return hours > 0 ? $"{hours}:{minutes:00}:{seconds:00}" : $"{minutes:00}:{seconds:00}";
        }

        private void EnsureCheckpointReadout()
        {
            if (checkpointReadout != null || statsCanvas == null)
                return;

            Transform canvasTransform = statsCanvas.transform;
            checkpointReadout = canvasTransform.Find("Checkpoint")?.GetComponent<TMP_Text>();
            if (checkpointReadout == null)
            {
                var source = timerReadout != null ? timerReadout : statsReadout;
                if (source == null)
                    return;

                var readoutObject = new GameObject("Checkpoint", typeof(RectTransform), typeof(TextMeshProUGUI));
                readoutObject.layer = canvasTransform.gameObject.layer;
                var rect = (RectTransform)readoutObject.transform;
                rect.SetParent(canvasTransform, false);
                rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
                rect.anchoredPosition = new Vector2(20, -244);
                rect.sizeDelta = new Vector2(260, 22);
                checkpointReadout = readoutObject.GetComponent<TextMeshProUGUI>();
                checkpointReadout.font = source.font;
                checkpointReadout.fontSharedMaterial = source.fontSharedMaterial;
                checkpointReadout.color = RunnerUiTheme.Label;
                checkpointReadout.fontSize = 13;
                checkpointReadout.alignment = TextAlignmentOptions.MidlineLeft;
                checkpointReadout.enableAutoSizing = false;
                checkpointReadout.raycastTarget = false;
            }

            RectTransform canvasRect = (RectTransform)canvasTransform;
            canvasRect.sizeDelta = new Vector2(canvasRect.sizeDelta.x, Mathf.Max(330, canvasRect.sizeDelta.y));
            MoveHudButton(canvasTransform, "PauseSession", 280);
            MoveHudButton(canvasTransform, "BackToMenu", 280);
        }

        private static void MoveHudButton(Transform canvasTransform, string buttonName, float top)
        {
            Transform button = canvasTransform.Find(buttonName);
            if (button is RectTransform rect)
                rect.anchoredPosition = new Vector2(rect.anchoredPosition.x, -top);
        }

        private static string FormatCheckpointStatus(LapCheckpointTracker tracker)
        {
            if (!tracker.HasCheckpointResult)
                return $"{tracker.NextCheckpointLabel} CHECKPOINT  |  " +
                    $"{(tracker.BestLapIsExample ? "BEISPIEL-PB" : "BESTZEIT")} " +
                    $"{FormatSplitTime(tracker.NextCheckpointBestSeconds)}";

            string comparison = tracker.LastCheckpointDeltaSeconds < -0.05
                ? (tracker.LastCheckpointIsRecord
                    ? $"REKORD -{FormatSplitTime(-tracker.LastCheckpointDeltaSeconds)}"
                    : $"{FormatSplitTime(-tracker.LastCheckpointDeltaSeconds)} schneller")
                : tracker.LastCheckpointDeltaSeconds > 0.05
                    ? $"{FormatSplitTime(tracker.LastCheckpointDeltaSeconds)} langsamer"
                    : "gleichauf mit BESTZEIT";

            return $"{tracker.LastCheckpointLabel}  |  {comparison}";
        }

        private static string FormatSplitTime(double seconds)
        {
            double absSec = System.Math.Abs(seconds);
            int totalTenths = Mathf.RoundToInt((float)(absSec * 10));
            return $"{totalTenths / 600:00}:{totalTenths / 10 % 60:00}.{totalTenths % 10}";
        }

        private static string FormatStats(string pace, string time, string distanceKm)
        {
            return "<size=13><color=#E3DEF7>LAUFSESSION\nZEIT</color></size>\n" +
                $"<size=56><color=#67E8F9><b>{time}</b></color></size>\n" +
                "<size=13><color=#E3DEF7>DISTANZ                      PACE</color></size>\n" +
                $"<size=28><color=#FFFFFF><b>{distanceKm}        {pace}</b></color></size>\n" +
                "<size=13><color=#E3DEF7>km                              min/km</color></size>";
        }
    }
}
