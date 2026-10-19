using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR.Interaction.Toolkit;

public class Gestures : MonoBehaviour
{
    public event Action<Animator> AvatarSelected;


    [SerializeField] private InputAction pointerPositionInputAction = new(
        "<HandInteraction>{RightHand}/pointerPosition", expectedControlType: "Vector3");
    [SerializeField] private InputAction pointerRotationInputAction = new(
        "<HandInteraction>{RightHand}/pointerRotation", expectedControlType: "Quaternion");
    [SerializeField] private InputAction pinchValueInputAction = new(
        "<HandInteraction>{RightHand}/pinchValue", expectedControlType: "Axis");

    [SerializeField] private LayerMask selectableLayers = ~0;
    [SerializeField, Min(0.1f)] private float pointerRayLength = 10f;
    [SerializeField, Range(0f, 1f)] private float pinchPressPoint = 0.75f;
    [SerializeField] private string animationParameter = "isRunning";
    [SerializeField] private XRRayInteractor rayInteractor;
    [SerializeField] private XRInteractorLineVisual rayVisual;
    [SerializeField] private Transform rayOrigin;
    [SerializeField] private ActionBasedController actionBasedController;

    private Animator selectedAnimator;
    private bool wasPinching;

    private void Awake()
    {
        rayInteractor ??= GetComponentInChildren<XRRayInteractor>();
        rayVisual ??= rayInteractor?.GetComponent<XRInteractorLineVisual>();
        actionBasedController ??= rayInteractor?.GetComponent<ActionBasedController>();
        rayOrigin ??= CreateRayOrigin();

        if (rayInteractor == null || rayVisual == null || actionBasedController == null)
        {
            Debug.LogError("[Gestures] XR ray setup is incomplete.");
            enabled = false;
            return;
        }

        rayInteractor.rayOriginTransform = rayOrigin;
        rayInteractor.maxRaycastDistance = pointerRayLength;
        rayInteractor.raycastMask = selectableLayers;
        rayInteractor.enabled = true;
        rayVisual.enabled = true;

        InputActionProperty pinchAction = new(pinchValueInputAction);
        actionBasedController.uiPressAction = pinchAction;
        actionBasedController.uiPressActionValue = pinchAction;
    }

    private void OnEnable()
    {
        pointerPositionInputAction.Enable();
        pointerRotationInputAction.Enable();
        pinchValueInputAction.Enable();
    }

    private void Update()
    {
        UpdateRayOrigin();

        float pinchValue = pinchValueInputAction.ReadValue<float>();
        bool isPinching = pinchValue >= pinchPressPoint;

        if (isPinching && !wasPinching)
            SelectAvatarWithPointer();

        if (selectedAnimator?.isActiveAndEnabled == true)
            selectedAnimator.SetBool(animationParameter, isPinching);

        wasPinching = isPinching;
    }

    private void SelectAvatarWithPointer()
    {
        if (rayInteractor.TryGetCurrentUIRaycastResult(out _))
            return;

        if (!rayInteractor.TryGetCurrent3DRaycastHit(out RaycastHit hit))
        {
            ClearSelection();
            return;
        }

        Animator animator = hit.collider.GetComponentInParent<Animator>();
        if (animator == null || !HasBoolParameter(animator, animationParameter))
        {
            ClearSelection();
            return;
        }

        selectedAnimator = animator;
        AvatarSelected?.Invoke(selectedAnimator);
    }

    private Transform CreateRayOrigin()
    {
        GameObject origin = new("Hand Pointer Ray Origin");
        origin.transform.SetParent(transform, false);
        return origin.transform;
    }

    private void UpdateRayOrigin()
    {
        rayOrigin.SetPositionAndRotation(
            pointerPositionInputAction.ReadValue<Vector3>(),
            pointerRotationInputAction.ReadValue<Quaternion>());
    }

    private void ClearSelection()
    {
        selectedAnimator = null;
    }

    private static bool HasBoolParameter(Animator animator, string parameterName)
    {
        foreach (AnimatorControllerParameter parameter in animator.parameters)
        {
            if (parameter.name == parameterName &&
                parameter.type == AnimatorControllerParameterType.Bool)
            {
                return true;
            }
        }

        return false;
    }

    private void OnDisable()
    {
        pointerPositionInputAction.Disable();
        pointerRotationInputAction.Disable();
        pinchValueInputAction.Disable();
        selectedAnimator = null;
        wasPinching = false;
    }

    private void OnDestroy()
    {
        pointerPositionInputAction.Dispose();
        pointerRotationInputAction.Dispose();
        pinchValueInputAction.Dispose();
    }
}
