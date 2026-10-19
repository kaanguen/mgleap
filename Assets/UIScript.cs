using UnityEngine;
using UnityEngine.UI;

public class UIScript : MonoBehaviour
{
    [SerializeField] private Gestures gestures;
    [SerializeField] private Slider speedSlider;
    [SerializeField] private string runningParameter = "isRunning";

    [SerializeField] private TextMesh speedValueText;
    [SerializeField, Min(0.01f)] private float minimumRunningSpeed = 0.1f;
    [SerializeField, Min(0.01f)] private float maximumRunningSpeed = 2f;

    private Animator selectedAnimator;

    private void Awake()
    {
        if (gestures == null)
            gestures = FindAnyObjectByType<Gestures>();

        if (speedSlider == null)
            speedSlider = GetComponentInChildren<Slider>();

        if (gestures == null)
        {
            Debug.LogError("[UIScript] Gestures component is missing.");
            enabled = false;
            return;
        }

        if (speedSlider == null)
        {
            Debug.LogError("[UIScript] Speed Slider is missing.");
            enabled = false;
            return;
        }

        speedSlider.minValue = 0f;
        speedSlider.maxValue = 100f;
        speedSlider.onValueChanged.AddListener(SetAnimationSpeed);
        speedValueText.text = speedSlider.value.ToString();
        gestures.AvatarSelected += SelectAvatar;
    }

    private void Start()
    {
        SetAnimationSpeed(speedSlider.value);
    }

    private void SelectAvatar(Animator animator)
    {
        selectedAnimator = animator;
        SetAnimationSpeed(speedSlider.value);
    }

    private void SetAnimationSpeed(float sliderValue)
    {
        if (selectedAnimator == null || !selectedAnimator.isActiveAndEnabled)
            return;

        float normalizedSpeed = Mathf.Clamp01(sliderValue / 100f);
        bool shouldRun = normalizedSpeed > 0f;
        selectedAnimator.SetBool(runningParameter, shouldRun);
        selectedAnimator.speed = shouldRun
            ? Mathf.Lerp(minimumRunningSpeed, maximumRunningSpeed, normalizedSpeed)
            : 1f;
    }

    private void OnDestroy()
    {
        if (speedSlider != null)
            speedSlider.onValueChanged.RemoveListener(SetAnimationSpeed);

        if (gestures != null)
            gestures.AvatarSelected -= SelectAvatar;
    }
}
