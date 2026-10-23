using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.XR.Interaction.Toolkit.UI;
using TMPro;
using StadionTracking;

public class UIScript : MonoBehaviour
{
    [SerializeField]
    private Canvas uiPanel;

    [SerializeField]
    private Camera mainCamera;

    [SerializeField]
    private TMP_Text textMesh;

    [SerializeField] private TMP_Text currentSpeedText;
    [SerializeField] private TMP_Text currentLapText;
    [SerializeField] private TMP_Text completedLapsText;
    [SerializeField] private TMP_Text statusText;
    private float nextStatsRefresh;

    [SerializeField]
    private Animator avatar;

    [SerializeField]
    private UnityEngine.UI.Slider slider;

    [Header("Panel follows the user")]
    [SerializeField] private bool followUser = true;
    [SerializeField, Min(0.4f)] private float distanceFromUser = 2.5f;
    [SerializeField] private float verticalOffset = -0.5f;
    [SerializeField] private float horizontalOffset = 0.6f;
    [SerializeField, Min(0.01f), Tooltip("Seconds for the panel to gently catch up with the user. Larger values follow more slowly.")]
    private float followSmoothTime = 0.65f;
    private Vector3 panelFollowVelocity;
    private bool panelPoseInitialized;

    private StadiumTrackPreview runner;
    private bool hadRunner;
    private bool hasSpeedParameter;
    private bool hasRunningParameter;
    private RunnerMapMarker runnerMap;
    private UnityEngine.UI.Button manualModeButton;
    private UnityEngine.UI.Button targetTimeButton;

    public bool IsRunnerStatsCanvas => uiPanel != null && uiPanel.name.StartsWith("RunnerStatsCanvas");
    public bool IsBoundTo(StadiumTrackPreview candidate) => runner == candidate;

    // Pause the previous runner and connect the panel to the newly spawned avatar.
    public void BindRunner(StadiumTrackPreview newRunner, Animator newAvatar)
    {
        if (runner != null)
            runner.SetRequestedSpeed(0f);
        if (avatar != null)
        {
            if (hasSpeedParameter) avatar.SetFloat(speedParameter, 0f);
            if (hasRunningParameter) avatar.SetBool(runningParameter, false);
        }
        runner = newRunner;
        hadRunner = runner != null;
        avatar = newAvatar;
        hasSpeedParameter = avatar != null && HasParameter(avatar, speedParameter, AnimatorControllerParameterType.Float);
        hasRunningParameter = avatar != null && HasParameter(avatar, runningParameter, AnimatorControllerParameterType.Bool);
        if (slider == null && uiPanel != null)
            slider = uiPanel.GetComponentInChildren<UnityEngine.UI.Slider>(true);
        if (slider != null)
        {
            ConfigurePacingSlider();
            slider.SetValueWithoutNotify(slider.minValue);
            slider.interactable = runner != null;
        }
        UpdateAnimation(0f);
        EnsurePacingControls();
        RefreshPacingControls();
        ConnectRunnerMap();
        if (runner != null)
            Debug.Log($"[RunnerStats] Connected to {newRunner.name}; lane {newRunner.Lane}. Speed reset to zero.");
    }

    [SerializeField]
    private string runningParameter = "isRunning";

    [SerializeField]
    private string speedParameter = "speed";

    // Initialize the canvas, speed slider and XR interaction.
    private void Start()
    {
        if (uiPanel == null)
        {
            Debug.LogError("[UIScript] World-space Canvas is missing.");
            enabled = false;
            return;
        }

        if (slider == null)
            slider = uiPanel.GetComponentInChildren<UnityEngine.UI.Slider>(true);

        if (slider == null)
        {
            Debug.LogError("[UIScript] Slider is missing in the world-space Canvas.");
            enabled = false;
            return;
        }

        if (mainCamera == null)
            mainCamera = Camera.main;

        uiPanel.worldCamera = mainCamera;
        slider.interactable = runner != null;
        if (uiPanel.GetComponent<TrackedDeviceGraphicRaycaster>() == null)
            uiPanel.gameObject.AddComponent<TrackedDeviceGraphicRaycaster>();
        EnsureXRUIInputModule();

        slider.onValueChanged.AddListener(UpdateAnimation);
        ConfigurePacingSlider();
        EnsurePacingControls();
        RefreshPacingControls();
        UpdateAnimation(slider.value);
        ConnectRunnerMap();

        if (avatar != null)
        {
            avatar.applyRootMotion = false;

            if (!HasParameter(avatar, speedParameter, AnimatorControllerParameterType.Float))
                Debug.LogError($"[UIScript] Animator float parameter '{speedParameter}' is missing.");
            if (!HasParameter(avatar, runningParameter, AnimatorControllerParameterType.Bool))
                Debug.LogError($"[UIScript] Animator bool parameter '{runningParameter}' is missing.");
        }
    }

    // Send slider input to the bound runner and display its requested speed.
    private void UpdateAnimation(float value)
    {
        bool timed = runner != null && runner.PacingMode == StadiumPacingMode.TargetLapTime;
        float fraction = runner != null && slider != null
            ? timed ? (slider.value > 0f ? 1f : 0f) : slider.normalizedValue : 0f;
        bool isRunning = fraction > 0f;
        if (runner != null)
        {
            if (timed && slider.value > 0f)
                runner.SetTargetLapMinutes(slider.value);
            runner.SetRequestedSpeed(fraction);
        }

        RefreshStats();

        if (avatar != null && runner == null)
        {
            if (hasSpeedParameter) avatar.SetFloat(speedParameter, fraction);
            if (hasRunningParameter) avatar.SetBool(runningParameter, isRunning);
        }
    }

    private void RefreshStats()
    {
        bool connected = runner != null;
        if (textMesh != null)
            textMesh.text = !connected ? "ZIELTEMPO  -- m/s"
                : runner.PacingMode == StadiumPacingMode.TargetLapTime
                    ? $"ZIELZEIT  {FormatLapTime(runner.TargetLapMinutes)} / RUNDE  |  {runner.RequestedSpeedMetresPerSecond:F2} m/s"
                    : $"ZIELTEMPO  {runner.RequestedSpeedMetresPerSecond:F2} m/s";
        if (currentSpeedText != null)
            currentSpeedText.text = connected
                ? $"<size=45%>AKTUELLES TEMPO</size>\n{runner.CurrentSpeed:F2} <size=55%>m/s</size>"
                : "<size=45%>AKTUELLES TEMPO</size>\n-- <size=55%>m/s</size>";
        if (currentLapText != null)
            currentLapText.text = connected
                ? $"<size=55%>AKTUELLE RUNDE</size>\n{runner.CurrentLap}"
                : "<size=55%>AKTUELLE RUNDE</size>\n--";
        if (completedLapsText != null)
            completedLapsText.text = connected
                ? $"<size=55%>GELAUFENE RUNDEN</size>\n{runner.CompletedLaps}"
                : "<size=55%>GELAUFENE RUNDEN</size>\n--";
        if (statusText != null)
            statusText.text = !connected ? "RUNNER STATS  /  WARTE AUF AVATAR"
                : $"RUNNER STATS  /  {(runner.CurrentSpeed > 0 ? "LÄUFT" : "PAUSE")}  /  BAHN {runner.Lane}";
        nextStatsRefresh = Time.unscaledTime + 0.1f;
    }

    private static string FormatLapTime(float minutes)
    {
        int seconds = Mathf.RoundToInt(minutes * 60f);
        return $"{seconds / 60}:{seconds % 60:00}";
    }

    // Reuse the slider: manual speed 0..1, or lap time 0..20 minutes (zero pauses).
    private void ConfigurePacingSlider()
    {
        if (slider == null) return;
        slider.SetValueWithoutNotify(slider.minValue);
        slider.wholeNumbers = false;
        slider.minValue = 0;
        slider.maxValue = runner != null && runner.PacingMode == StadiumPacingMode.TargetLapTime
            ? Mathf.Max(20f, runner.TargetLapMinutes) : 1f;
    }

    private void SelectPacingMode(StadiumPacingMode mode)
    {
        if (runner == null || (runner.PacingMode == mode && mode == StadiumPacingMode.ManualSpeed)) return;
        runner.SetRequestedSpeed(0f);
        runner.SetPacingMode(mode);
        ConfigurePacingSlider();
        if (mode == StadiumPacingMode.TargetLapTime)
            slider.SetValueWithoutNotify(runner.TargetLapMinutes);
        UpdateAnimation(slider.value);
        RefreshPacingControls();
    }

    // Add a compact mode row below the existing stats card, without editing the prefab.
    private void EnsurePacingControls()
    {
        if (manualModeButton != null || slider == null || textMesh == null) return;
        var row = new GameObject("Pacing modes", typeof(RectTransform));
        row.layer = slider.gameObject.layer;
        row.transform.SetParent(slider.transform.parent, false);
        var rect = row.GetComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = new Vector2(0, -57);
        rect.sizeDelta = new Vector2(84, 10);
        manualModeButton = CreateModeButton(rect, "MANUELL", -21.5f, StadiumPacingMode.ManualSpeed);
        targetTimeButton = CreateModeButton(rect, "ZIELZEIT", 21.5f, StadiumPacingMode.TargetLapTime);
    }

    private UnityEngine.UI.Button CreateModeButton(RectTransform parent, string title, float x, StadiumPacingMode mode)
    {
        var go = new GameObject(title, typeof(RectTransform), typeof(UnityEngine.UI.Image), typeof(UnityEngine.UI.Button));
        go.layer = parent.gameObject.layer;
        go.transform.SetParent(parent, false);
        var rect = go.GetComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = new Vector2(x, 0);
        rect.sizeDelta = new Vector2(41, 10);
        var button = go.GetComponent<UnityEngine.UI.Button>();
        button.targetGraphic = go.GetComponent<UnityEngine.UI.Image>();
        button.onClick.AddListener(() => SelectPacingMode(mode));
        var label = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
        label.layer = go.layer;
        label.transform.SetParent(go.transform, false);
        var labelRect = label.GetComponent<RectTransform>();
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.offsetMin = labelRect.offsetMax = Vector2.zero;
        var text = label.GetComponent<TextMeshProUGUI>();
        text.font = textMesh.font;
        text.fontSize = 3;
        text.alignment = TextAlignmentOptions.Center;
        text.text = title;
        text.color = Color.white;
        text.raycastTarget = false;
        return button;
    }

    private void RefreshPacingControls()
    {
        if (manualModeButton == null) return;
        bool connected = runner != null;
        manualModeButton.interactable = targetTimeButton.interactable = connected;
        bool timed = connected && runner.PacingMode == StadiumPacingMode.TargetLapTime;
        Color active = new Color(0.09f, 0.40f, 0.48f);
        Color inactive = new Color(0.15f, 0.19f, 0.23f);
        manualModeButton.targetGraphic.color = connected && !timed ? active : inactive;
        targetTimeButton.targetGraphic.color = timed ? active : inactive;
    }

    // Gently follow the user and clear references to a destroyed runner.
    private void LateUpdate()
    {

        if (mainCamera == null)
            mainCamera = Camera.main;
        if (uiPanel != null && mainCamera != null)
        {
            uiPanel.worldCamera = mainCamera;
            if (followUser)
            {
                Transform head = mainCamera.transform;
                Vector3 forward = Vector3.ProjectOnPlane(head.forward, Vector3.up);
                if (forward.sqrMagnitude < 0.0001f)
                    forward = Vector3.Cross(head.right, Vector3.up);
                forward.Normalize();
                Vector3 right = Vector3.Cross(Vector3.up, forward);
                Vector3 targetPosition = head.position + forward * Mathf.Max(0.4f, distanceFromUser)
                    + right * horizontalOffset + Vector3.up * verticalOffset;
                Transform panel = uiPanel.transform;
                if (!panelPoseInitialized)
                {

                    panel.position = targetPosition;
                    panel.rotation = Quaternion.LookRotation(targetPosition - head.position, Vector3.up);
                    panelPoseInitialized = true;
                }
                else
                {
                    float responseTime = Mathf.Max(0.01f, followSmoothTime);
                    panel.position = Vector3.SmoothDamp(panel.position, targetPosition,
                        ref panelFollowVelocity, responseTime, Mathf.Infinity, Time.unscaledDeltaTime);
                    Vector3 facing = panel.position - head.position;
                    if (facing.sqrMagnitude > 0.0001f)
                    {
                        float blend = 1f - Mathf.Exp(-Time.unscaledDeltaTime / responseTime);
                        panel.rotation = Quaternion.Slerp(panel.rotation,
                            Quaternion.LookRotation(facing, Vector3.up), blend);
                    }
                }
            }
            else
            {
                panelPoseInitialized = false;
                panelFollowVelocity = Vector3.zero;
            }
        }
        if (hadRunner && runner == null)
        {

            hadRunner = false;
            avatar = null;
            if (slider != null)
            {
                slider.SetValueWithoutNotify(slider.minValue);
                slider.interactable = false;
            }
            UpdateAnimation(0f);
            RefreshPacingControls();
            if (runnerMap != null)
                runnerMap.BindRunner(null);
        }
        if (Time.unscaledTime >= nextStatsRefresh)
            RefreshStats();
    }

    // Attach the position overlay to the existing stadium image.
    private void ConnectRunnerMap()
    {
        if (uiPanel == null)
            return;
        if (runnerMap == null)
        {

            foreach (UnityEngine.UI.RawImage image in uiPanel.GetComponentsInChildren<UnityEngine.UI.RawImage>(true))
            {
                if (image.name != "stadion")
                    continue;
                runnerMap = image.GetComponent<RunnerMapMarker>();
                if (runnerMap == null)
                    runnerMap = image.gameObject.AddComponent<RunnerMapMarker>();
                break;
            }
        }
        if (runnerMap != null)
            runnerMap.BindRunner(runner);
    }

    private static bool HasParameter(
        Animator animator,
        string parameterName,
        AnimatorControllerParameterType parameterType)
    {
        foreach (AnimatorControllerParameter parameter in animator.parameters)
        {
            if (parameter.name == parameterName && parameter.type == parameterType)
                return true;
        }

        return false;
    }

    // Use the XR input module for hand-controlled UI.
    private static void EnsureXRUIInputModule()
    {
        EventSystem eventSystem = EventSystem.current;
        if (eventSystem == null)
        {
            Debug.LogError("[UIScript] No EventSystem found for XR UI input.");
            return;
        }

        var inputSystemModule = eventSystem.GetComponent<InputSystemUIInputModule>();
        if (inputSystemModule != null)
            inputSystemModule.enabled = false;

        var xrUiModule = eventSystem.GetComponent<XRUIInputModule>();
        if (xrUiModule == null)
            xrUiModule = eventSystem.gameObject.AddComponent<XRUIInputModule>();

        xrUiModule.enabled = true;
    }

    private void OnDestroy()
    {
        if (runner != null)
            runner.SetRequestedSpeed(0f);
        if (slider != null)
            slider.onValueChanged.RemoveListener(UpdateAnimation);
    }
}
