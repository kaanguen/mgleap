using System.Globalization;
using TMPro;
using UnityEngine;

namespace StadionTracking
{
    public sealed class ActiveStatsDisplay : MonoBehaviour
    {
        [SerializeField] private Canvas statsCanvas;
        [SerializeField] private TMP_Text statsReadout;
        [SerializeField] private TMP_Text timerReadout;
        [SerializeField] private TMP_Text distanceReadout;
        [SerializeField] private TMP_Text paceReadout;
        [SerializeField] private TMP_Text checkpointReadout;
        private TMP_Text partnerReadout;
        [SerializeField] private Camera headCamera;
        [SerializeField] private StraightTreadmillWorld treadmillWorld;
        [SerializeField, Range(0f, 0.25f)] private float horizontalMargin = 0.12f;
        [SerializeField, Range(0f, 0.3f)] private float verticalMargin = 0.22f;
        [SerializeField, Range(0f, 0.2f)] private float safeViewportMargin = 0.06f;
        [SerializeField, Min(0.3f)] private float distanceFromHead = 1.8f;
        [SerializeField, Min(0.0001f)] private float worldScale = 0.0003f;
        [SerializeField, Min(0.05f)] private float refreshInterval = 0.15f;

        private StadiumTrackPreview runner;
        private StadiumTrackPreview timedRunner;
        private StraightTreadmillWorld timedTreadmillWorld;
        private float elapsedSeconds;
        private float nextRefresh;
        private bool sessionStarted;
        private double previousDistance;
        private LapGhostPlayback ghostPlayback;
        private LapCheckpointTracker checkpointTracker = new LapCheckpointTracker(400.0, RunnerSessionSettings.PaceSeconds);

        private void Awake()
        {
            if (statsCanvas == null)
                statsCanvas = GetComponent<Canvas>();
            if (statsReadout == null)
                statsReadout = GetComponentInChildren<TMP_Text>(true);
            EnsureCheckpointReadout();
            EnsurePartnerReadout();
            ghostPlayback = GetComponent<LapGhostPlayback>();
            if (ghostPlayback == null) ghostPlayback = gameObject.AddComponent<LapGhostPlayback>();

            if (statsCanvas == null || (statsReadout == null && timerReadout == null))
            {
                Debug.LogError("[ActiveStats] Assign a Canvas and a TextMeshPro readout.", this);
                enabled = false;
            }
            if (GetComponent<RunnerArPanel>() == null) gameObject.AddComponent<RunnerArPanel>();
        }

        public void BindRunner(StadiumTrackPreview activeRunner)
        {
            if (runner == activeRunner)
                return;

            runner = activeRunner;
            timedRunner = activeRunner;
            treadmillWorld = null;
            timedTreadmillWorld = null;
            elapsedSeconds = 0f;
            checkpointTracker = new LapCheckpointTracker(activeRunner.LapLengthMetres, RunnerSessionSettings.PaceSeconds);
            checkpointTracker.Update(0, 0);
            sessionStarted = false;
            previousDistance = 0;
            nextRefresh = 0f;
        }

        private void LateUpdate()
        {
            if (headCamera == null)
                headCamera = Camera.main;
            if (headCamera == null)
                return;

            statsCanvas.worldCamera = headCamera;

            if (runner == null || !runner.isActiveAndEnabled)
                runner = FindObjectOfType<StadiumTrackPreview>();
            if (treadmillWorld == null || !treadmillWorld.isActiveAndEnabled)
                treadmillWorld = FindObjectOfType<StraightTreadmillWorld>();

            StadiumTrackPreview activeRunner = runner != null && runner.isActiveAndEnabled ? runner : null;
            StraightTreadmillWorld activeTreadmillWorld = activeRunner == null &&
                treadmillWorld != null && treadmillWorld.isActiveAndEnabled ? treadmillWorld : null;

            if (activeRunner != timedRunner || activeTreadmillWorld != timedTreadmillWorld)
            {
                timedRunner = activeRunner;
                timedTreadmillWorld = activeTreadmillWorld;
                elapsedSeconds = 0f;
                checkpointTracker = new LapCheckpointTracker(
                    activeRunner != null ? activeRunner.LapLengthMetres : 400.0,
                    RunnerSessionSettings.PaceSeconds);
                checkpointTracker.Update(0, 0);
                sessionStarted = false;
                previousDistance = 0;
            }

            double speedMetresPerSecond = activeRunner != null
                ? activeRunner.CurrentSpeed
                : activeTreadmillWorld != null && activeTreadmillWorld.PlacementReady
                    ? activeTreadmillWorld.SpeedMetresPerSecond
                    : 0.0;
            long completedLaps = activeRunner != null
                ? activeRunner.CompletedLaps
                : activeTreadmillWorld != null ? activeTreadmillWorld.CompletedLaps : 0;
            double travelledMetres = activeRunner != null
                ? activeRunner.TravelledMetres
                : activeTreadmillWorld != null ? activeTreadmillWorld.TravelledMetres : 0.0;

            if (travelledMetres < previousDistance)
            {
                elapsedSeconds = 0;
                sessionStarted = false;
                checkpointTracker.Update(travelledMetres, 0);
            }
            previousDistance = travelledMetres;
            bool ready = activeRunner != null || (activeTreadmillWorld != null &&
                activeTreadmillWorld.PlacementReady && activeTreadmillWorld.HeightConfirmed);
            bool explicitlyPaused = activeRunner != null
                ? activeRunner.RequestedSpeedFraction <= 0f
                : activeTreadmillWorld != null && activeTreadmillWorld.SessionPaused;
            if (ready && !explicitlyPaused && speedMetresPerSecond > 0) sessionStarted = true;
            bool clockRunning = ready && sessionStarted && !explicitlyPaused && Time.timeScale > 0;
            // A belt speed of zero counts as time lost; the explicit Pause button freezes the race.
            // Match the scaled clock used by both movement simulations.
            if (clockRunning) elapsedSeconds += Time.deltaTime;

            if (activeRunner != null || activeTreadmillWorld != null)
                checkpointTracker.Update(travelledMetres, elapsedSeconds);
            ghostPlayback.Synchronize(checkpointTracker, activeRunner, activeTreadmillWorld, clockRunning);

            if (Time.unscaledTime < nextRefresh)
                return;

            string timeValue = FormatElapsedTime(elapsedSeconds);
            string distanceValue = (travelledMetres / 1000.0).ToString("F2", CultureInfo.GetCultureInfo("de-DE"));
            string paceValue = FormatPace(speedMetresPerSecond);
            if (timerReadout != null)
            {
                timerReadout.text = "<mspace=34>" + timeValue + "</mspace>";
                distanceReadout.text = "<mspace=16>" + distanceValue + "</mspace>";
                paceReadout.text = "<mspace=16>" + paceValue + "</mspace>";
            }
            else if (statsReadout != null) statsReadout.text = activeRunner == null && activeTreadmillWorld == null
                ? FormatStats("--:--", "--", "--:--", "--,--")
                : FormatStats(
                    FormatPace(speedMetresPerSecond),
                    completedLaps.ToString(),
                    FormatElapsedTime(elapsedSeconds),
                    (travelledMetres / 1000.0).ToString("F2", CultureInfo.GetCultureInfo("de-DE")));
            if (checkpointReadout != null)
                checkpointReadout.text = activeRunner == null && activeTreadmillWorld == null
                    ? "CHECKPOINT --"
                    : FormatCheckpointStatus(checkpointTracker);
            if (partnerReadout != null)
            {
                partnerReadout.gameObject.SetActive(ghostPlayback.IsPartnerActive);
                partnerReadout.text = checkpointTracker.GhostRecording.HasRecording
                    ? $"BESTE RUNDE {FormatSplitTime(checkpointTracker.BestLapSeconds)}"
                    : $"ZIELPACE {RunnerSessionSettings.PaceSeconds / 60}:{RunnerSessionSettings.PaceSeconds % 60:00} min/km";
            }
            nextRefresh = Time.unscaledTime + refreshInterval;
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

        private static string FormatStats(string pace, string laps, string time, string distanceKm)
        {
            return "<size=13><color=#E3DEF7>LAUFSESSION\nZEIT</color></size>\n" +
                $"<size=56><color=#67E8F9><b>{time}</b></color></size>\n" +
                "<size=13><color=#E3DEF7>DISTANZ                      PACE</color></size>\n" +
                $"<size=28><color=#FFFFFF><b>{distanceKm}        {pace}</b></color></size>\n" +
                "<size=13><color=#E3DEF7>km                              min/km</color></size>";
        }
    }
}
