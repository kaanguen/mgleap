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
            slider.SetValueWithoutNotify(slider.minValue);
            slider.interactable = runner != null;
        }
        UpdateAnimation(0f);
        ConnectRunnerMap();
        if (runner != null)
            Debug.Log($"[RunnerStats] Connected to {newRunner.name}; lane {newRunner.Lane}. Speed reset to zero.");
    }

    [SerializeField]
    private string runningParameter = "isRunning";

    [SerializeField]
    private string speedParameter = "speed";

    public void StopAtTrackFinish()
    {
        if (slider == null)
            return;
        slider.value = slider.minValue;
        UpdateAnimation(slider.value);
    }

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
        float fraction = runner != null && !runner.IsFinished && slider != null ? slider.normalizedValue : 0f;
        bool isRunning = fraction > 0f;
        if (runner != null)
            runner.SetRequestedSpeed(fraction);

        if (textMesh != null)
            textMesh.text = runner == null ? "Warte auf Avatar" : $"Speed: {fraction * runner.SpeedAtFullSlider:F2} m/s";

        if (avatar != null)
        {
            if (hasSpeedParameter) avatar.SetFloat(speedParameter, fraction);
            if (hasRunningParameter) avatar.SetBool(runningParameter, isRunning);
        }
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
            if (runnerMap != null)
                runnerMap.BindRunner(null);
        }
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
