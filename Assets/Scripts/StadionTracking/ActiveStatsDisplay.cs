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

        private void Awake()
        {
            if (statsCanvas == null)
                statsCanvas = GetComponent<Canvas>();
            if (statsReadout == null)
                statsReadout = GetComponentInChildren<TMP_Text>(true);

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

            if (speedMetresPerSecond > 0f)
                elapsedSeconds += Time.unscaledDeltaTime;

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
            nextRefresh = Time.unscaledTime + refreshInterval;
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
