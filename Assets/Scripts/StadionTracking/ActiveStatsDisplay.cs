using System.Globalization;
using TMPro;
using UnityEngine;

namespace StadionTracking
{
    public sealed class ActiveStatsDisplay : MonoBehaviour
    {
        [SerializeField] private Canvas statsCanvas;
        [SerializeField] private TMP_Text statsReadout;
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

            if (statsCanvas == null || statsReadout == null)
            {
                Debug.LogError("[ActiveStats] Assign a Canvas and a TextMeshPro readout.", this);
                enabled = false;
            }
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
            transform.localScale = Vector3.one * worldScale;

            RectTransform canvasRect = (RectTransform)transform;
            float panelWidth = canvasRect.rect.width * worldScale;
            float panelHeight = canvasRect.rect.height * worldScale;
            float viewWidth = Vector3.Distance(
                headCamera.ViewportToWorldPoint(new Vector3(0f, 0f, distanceFromHead)),
                headCamera.ViewportToWorldPoint(new Vector3(1f, 0f, distanceFromHead)));
            float viewHeight = Vector3.Distance(
                headCamera.ViewportToWorldPoint(new Vector3(0f, 0f, distanceFromHead)),
                headCamera.ViewportToWorldPoint(new Vector3(0f, 1f, distanceFromHead)));
            if (viewWidth <= 0f || viewHeight <= 0f)
                return;

            float halfPanelWidth = panelWidth / (2f * viewWidth);
            float halfPanelHeight = panelHeight / (2f * viewHeight);
            float centerX = Mathf.Clamp(
                horizontalMargin + halfPanelWidth,
                halfPanelWidth + safeViewportMargin,
                1f - halfPanelWidth - safeViewportMargin);
            float centerY = Mathf.Clamp(
                1f - verticalMargin - halfPanelHeight,
                halfPanelHeight + safeViewportMargin,
                1f - halfPanelHeight - safeViewportMargin);
            Vector3 panelPosition = headCamera.ViewportToWorldPoint(
                new Vector3(centerX, centerY, distanceFromHead));
            transform.SetPositionAndRotation(
                panelPosition,
                headCamera.transform.rotation);

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

            statsReadout.text = activeRunner == null && activeTreadmillWorld == null
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
            return $"<size=62%><color=#55EAF7>LAUFSESSION</color></size><pos=63%><size=60%><color=#D5E0EC>RUNDE {laps}</color></size>\n" +
                "<size=50%><color=#C4CFDC>ZEIT</color></size>\n" +
                $"<size=235%><color=#55EAF7><b>{time}</b></color></size>\n\n" +
                "<size=55%><color=#C4CFDC>DISTANZ</color><pos=55%>PACE</size>\n" +
                $"<size=130%><b>{distanceKm}</b> <size=55%>km</size><pos=55%><b>{pace}</b> <size=55%>min/km</size>";
        }
    }
}
