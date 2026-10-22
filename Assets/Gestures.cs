using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.XR;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit;

public class Gestures : MonoBehaviour
{
    [SerializeField] private InputActionAsset inputActionAsset;
    [SerializeField] private string handMap = "RightHand";
    [SerializeField] private LayerMask selectableLayers = ~0;
    [SerializeField, Min(0.1f)] private float pointerRayLength = 10f;
    [SerializeField] private XRRayInteractor rayInteractor;
    [SerializeField] private XRInteractorLineVisual rayVisual;
    [SerializeField] private ActionBasedController actionBasedController;
    [SerializeField] private Transform rayOrigin;

    private InputAction aimAction;
    private InputAction pinchValueAction;
    private InputAction pinchReadyAction;
    private InputAction pinchPressAction;
    private Slider activeSlider;

    private void Awake()
    {
        if (!TryFindActions())
        {
            enabled = false;
            return;
        }

        SetupInteractor();
    }

    private bool TryFindActions()
    {
        if (inputActionAsset == null)
        {
            Debug.LogError("[Gestures] Input action asset is missing.");
            return false;
        }

        string prefix = $"{handMap}/";
        aimAction = inputActionAsset.FindAction($"{prefix}Aim", false);
        pinchValueAction = inputActionAsset.FindAction($"{prefix}PinchValue", false);
        pinchReadyAction = inputActionAsset.FindAction($"{prefix}PinchReady", false);

        if (aimAction != null && pinchValueAction != null && pinchReadyAction != null)
            return true;

        Debug.LogError($"[Gestures] Aim, PinchValue or PinchReady is missing in '{handMap}'.");
        return false;
    }

    private void SetupInteractor()
    {
        if (rayInteractor == null)
        {
            Debug.LogError("[Gestures] XRRayInteractor is missing.");
            enabled = false;
            return;
        }

        rayVisual ??= rayInteractor.GetComponent<XRInteractorLineVisual>();
        actionBasedController ??= rayInteractor.GetComponent<ActionBasedController>();
        rayOrigin ??= CreateRayOrigin();

        if (rayVisual == null || actionBasedController == null)
        {
            Debug.LogError("[Gestures] XRInteractorLineVisual or ActionBasedController is missing.");
            enabled = false;
            return;
        }

        rayInteractor.rayOriginTransform = rayOrigin;
        rayInteractor.maxRaycastDistance = pointerRayLength;
        rayInteractor.raycastMask = selectableLayers;
        rayInteractor.enableUIInteraction = true;

        actionBasedController.enableInputTracking = false;
        // XRI checks the Performed phase. A Value action performs even for a weak pinch,
        // so use a separate Button action with an explicit press threshold.
        pinchPressAction = new InputAction("Hand UI Pinch Press", InputActionType.Button,
            interactions: "Press(pressPoint=0.7)");
        foreach (var binding in pinchValueAction.bindings)
            pinchPressAction.AddBinding(binding);
        actionBasedController.uiPressAction = new InputActionProperty(pinchPressAction);
        actionBasedController.uiPressActionValue = new InputActionProperty(pinchValueAction);
        actionBasedController.selectAction = new InputActionProperty(pinchPressAction);
        actionBasedController.selectActionValue = new InputActionProperty(pinchValueAction);
        SetInteractorActive(false);
    }

    private void OnEnable()
    {
        pinchPressAction?.Enable();
        aimAction?.Enable();
        pinchValueAction?.Enable();
        pinchReadyAction?.Enable();
    }

    private void OnDisable()
    {
        pinchPressAction?.Disable();
        aimAction?.Disable();
        pinchValueAction?.Disable();
        pinchReadyAction?.Disable();
        activeSlider = null;
        SetInteractorActive(false);
    }

    private void Start()
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        const string handPermission = "com.magicleap.permission.HAND_TRACKING";
        if (!UnityEngine.Android.Permission.HasUserAuthorizedPermission(handPermission))
            UnityEngine.Android.Permission.RequestUserPermission(handPermission);
#endif
    }

    private void OnDestroy()
    {
        // Release the runtime-created action; the original asset belongs to the scene.
        pinchPressAction?.Disable();
        pinchPressAction?.Dispose();
    }

    private void Update()
    {
        if (aimAction == null)
            return;

        PoseState pose = aimAction.ReadValue<PoseState>();
        bool tracked = pose.isTracked || pose.position.sqrMagnitude > 0.0001f;
        if (tracked)
        {
            rayOrigin.localPosition = pose.position;
            rayOrigin.localRotation = pose.rotation;
        }

        SetInteractorActive(tracked);
        UpdateSliderDrag();
    }

    private void UpdateSliderDrag()
    {
        bool pinching = pinchPressAction != null && pinchPressAction.IsPressed();
        if (!pinching)
        {
            activeSlider = null;
            return;
        }

        if (!rayInteractor.TryGetCurrentUIRaycastResult(out var hit))
            return;

        activeSlider ??= hit.gameObject.GetComponentInParent<Slider>();
        if (activeSlider == null || activeSlider.transform is not RectTransform sliderTransform)
            return;

        Vector3 localPoint = sliderTransform.InverseTransformPoint(hit.worldPosition);
        float normalizedValue = Mathf.InverseLerp(
            sliderTransform.rect.xMin,
            sliderTransform.rect.xMax,
            localPoint.x);
        activeSlider.value = Mathf.Lerp(
            activeSlider.minValue,
            activeSlider.maxValue,
            normalizedValue);
    }

    private void SetInteractorActive(bool active)
    {
        if (rayInteractor == null)
            return;

        if (rayInteractor.enabled != active)
            rayInteractor.enabled = active;
        if (rayVisual != null && rayVisual.enabled != active)
            rayVisual.enabled = active;
    }

    private Transform CreateRayOrigin()
    {
        XROrigin xrOrigin = FindFirstObjectByType<XROrigin>();
        Transform parent = xrOrigin?.CameraFloorOffsetObject?.transform ?? transform;
        GameObject origin = new($"{handMap} Hand Ray Origin");
        origin.transform.SetParent(parent, false);
        return origin.transform;
    }
}
