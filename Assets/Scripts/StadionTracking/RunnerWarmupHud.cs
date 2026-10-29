using TMPro;
using UnityEngine;

namespace StadionTracking
{
    /// <summary>A small, non-interactive world-space banner at the upper centre of the XR view.</summary>
    [DefaultExecutionOrder(1100)]
    public sealed class RunnerWarmupHud : MonoBehaviour
    {
        private RunnerWarmup warmup;
        private StraightTreadmillWorld world;
        private Canvas banner;
        private TextMeshProUGUI title, detail;
        public Canvas Banner => banner;

        public static RunnerWarmupHud GetOrCreate(StraightTreadmillWorld owner)
        {
            var hud = owner.GetComponent<RunnerWarmupHud>();
            if (hud == null) hud = owner.gameObject.AddComponent<RunnerWarmupHud>();
            hud.world = owner; hud.warmup = RunnerWarmup.GetOrCreate(owner);
            return hud;
        }

        private void Build()
        {
            var root = new GameObject("WarmUpBanner", typeof(RectTransform), typeof(Canvas), typeof(UnityEngine.UI.CanvasScaler));
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root, gameObject.scene);
            root.layer = 5;
            banner = root.GetComponent<Canvas>();
            banner.renderMode = RenderMode.WorldSpace;
            banner.sortingOrder = 50;
            banner.additionalShaderChannels |= AdditionalCanvasShaderChannels.TexCoord1 | AdditionalCanvasShaderChannels.TexCoord2;
            ((RectTransform)root.transform).sizeDelta = new Vector2(560, 100);
            title = RunnerUiTheme.Text("WarmUpTitle", root.transform, "Warm UP", 0, 0, 560, 46, 32, true);
            title.alignment = TextAlignmentOptions.Center;
            title.color = RunnerUiTheme.Time;
            detail = RunnerUiTheme.Text("ExerciseCountdown", root.transform, "", 0, 48, 560, 48, 18);
            detail.alignment = TextAlignmentOptions.Center;
        }

        private void LateUpdate()
        {
            bool visible = warmup != null && warmup.Active && world != null && world.HeadPoseReady;
            if (!visible) { if (banner != null) banner.gameObject.SetActive(false); return; }
            if (banner == null) Build();
            banner.gameObject.SetActive(true);
            var camera = world.HeadCamera;
            banner.worldCamera = camera;
            const float distance = 1.8f;
            float width = Vector3.Distance(camera.ViewportToWorldPoint(new Vector3(0, .5f, distance)), camera.ViewportToWorldPoint(new Vector3(1, .5f, distance)));
            float height = Vector3.Distance(camera.ViewportToWorldPoint(new Vector3(.5f, 0, distance)), camera.ViewportToWorldPoint(new Vector3(.5f, 1, distance)));
            banner.transform.localScale = Vector3.one * Mathf.Min(width / 1280f, height / 720f);
            banner.transform.SetPositionAndRotation(camera.ViewportToWorldPoint(new Vector3(.5f, .87f, distance)), camera.transform.rotation);
            detail.text = warmup.Finishing ? "Geschafft · bereit für den Lauf" : string.Format("{0} · {1}/{2} · {3} s", warmup.Label, warmup.StepIndex + 1, warmup.StepCount, Mathf.CeilToInt(warmup.StepDuration - warmup.StepElapsed));
        }

        private void OnDisable() { if (banner != null) banner.gameObject.SetActive(false); }
        private void OnDestroy()
        {
            if (banner == null) return;
            if (Application.isPlaying) Destroy(banner.gameObject); else DestroyImmediate(banner.gameObject);
        }
    }
}
