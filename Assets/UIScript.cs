using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.XR.Interaction.Toolkit.UI;
using UnityEngine.UI;
using TMPro;


public class UIScript : MonoBehaviour
{
    [SerializeField]
    private Canvas uiPanel;

    [SerializeField]
    private Camera mainCamera;

    [SerializeField]
    private TextMeshPro textMesh;

    [SerializeField]
    private Animator avatar;

    [SerializeField]
    private Slider slider;

    [SerializeField]
    private string runningParameter = "isRunning";

    [SerializeField]
    private string speedParameter = "speed";

    private void Start()
    {
        if (uiPanel == null)
        {
            Debug.LogError("[UIScript] World-space Canvas is missing.");
            enabled = false;
            return;
        }

        if (slider == null)
            slider = uiPanel.GetComponentInChildren<Slider>(true);

        if (slider == null)
        {
            Debug.LogError("[UIScript] Slider is missing in the world-space Canvas.");
            enabled = false;
            return;
        }

        mainCamera = Camera.main;
        if (mainCamera == null)
        {
            Debug.LogError("[UIScript] Main camera is missing.");
            enabled = false;
            return;
        }

        uiPanel.worldCamera = mainCamera;
        slider.interactable = true;
        if (uiPanel.GetComponent<TrackedDeviceGraphicRaycaster>() == null)
            uiPanel.gameObject.AddComponent<TrackedDeviceGraphicRaycaster>();
        EnsureXRUIInputModule();

        slider.onValueChanged.AddListener(UpdateAnimation);
        UpdateAnimation(slider.value);

        if (avatar == null)
        {
            Debug.LogError(
                "[UIScript] Avatar Animator is missing. Assign the character's Animator " +
                "to the Avatar field.");
        }
        else
        {
            avatar.applyRootMotion = false;

            if (!HasParameter(avatar, speedParameter, AnimatorControllerParameterType.Float))
                Debug.LogError($"[UIScript] Animator float parameter '{speedParameter}' is missing.");
            if (!HasParameter(avatar, runningParameter, AnimatorControllerParameterType.Bool))
                Debug.LogError($"[UIScript] Animator bool parameter '{runningParameter}' is missing.");
        }
    }

    private void UpdateAnimation(float value)
    {
        bool isRunning = !Mathf.Approximately(value, 0f);

        if (textMesh != null)
            textMesh.text = $"Speed: {value:F2}";

        if (avatar != null)
        {
            avatar.SetFloat(speedParameter, value);
            avatar.SetBool(runningParameter, isRunning);
        }
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
        if (slider != null)
            slider.onValueChanged.RemoveListener(UpdateAnimation);
    }
}