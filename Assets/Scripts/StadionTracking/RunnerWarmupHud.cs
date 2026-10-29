using TMPro;
using UnityEngine;

namespace StadionTracking
{
    /// <summary>Stereo-safe, non-interactive warm-up banner inside the common view of both eyes.</summary>
    [DefaultExecutionOrder(1100)]
    public sealed class RunnerWarmupHud : MonoBehaviour
    {
        public const string TextMaterialResource = "RunnerUI/WarmupText";
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

        private void OnEnable() => Application.onBeforeRender += BeforeRender;
        [BeforeRenderOrder(1000)]
        private void BeforeRender()
        {
            if (banner != null && banner.gameObject.activeSelf && world != null && world.HeadCamera != null)
                Place(world.HeadCamera);
        }

        private void Build()
        {
            var root = new GameObject("WarmUpBanner", typeof(RectTransform), typeof(Canvas), typeof(UnityEngine.UI.CanvasScaler));
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root, gameObject.scene);
            root.layer = 5;
            banner = root.GetComponent<Canvas>();
            banner.renderMode = RenderMode.WorldSpace;
            banner.overrideSorting = true; banner.sortingOrder = 1000;
            banner.additionalShaderChannels |= AdditionalCanvasShaderChannels.TexCoord1 | AdditionalCanvasShaderChannels.TexCoord2;
            ((RectTransform)root.transform).sizeDelta = new Vector2(560, 112);
            title = RunnerUiTheme.Text("WarmUpTitle", root.transform, "Warm UP", 0, 0, 560, 50, 36, true);
            title.alignment = TextAlignmentOptions.Center;
            title.color = RunnerUiTheme.Time;
            detail = RunnerUiTheme.Text("ExerciseCountdown", root.transform, "", 0, 54, 560, 58, 20);
            detail.alignment = TextAlignmentOptions.Center;
            var material = Resources.Load<Material>(TextMaterialResource);
            if (material != null) title.fontSharedMaterial = detail.fontSharedMaterial = material;
            else Debug.LogError("[Warmup HUD] Missing Resources/" + TextMaterialResource + ". Rebuild with the Warm-up HUD material.", this);
        }

        // Coordinates are camera-local metres, not the unrelated mono projection used by a game view.
        public static Rect EyeRect(Camera camera, float distance, Camera.MonoOrStereoscopicEye eye)
        {
            var bottom = camera.transform.InverseTransformPoint(camera.ViewportToWorldPoint(new Vector3(0, 0, distance), eye));
            var top = camera.transform.InverseTransformPoint(camera.ViewportToWorldPoint(new Vector3(1, 1, distance), eye));
            return Rect.MinMaxRect(Mathf.Min(bottom.x, top.x), Mathf.Min(bottom.y, top.y), Mathf.Max(bottom.x, top.x), Mathf.Max(bottom.y, top.y));
        }
        public static Rect VisibleRect(Camera camera, float distance, bool stereo)
        {
            if (!stereo) return EyeRect(camera, distance, Camera.MonoOrStereoscopicEye.Mono);
            var left = EyeRect(camera, distance, Camera.MonoOrStereoscopicEye.Left);
            var right = EyeRect(camera, distance, Camera.MonoOrStereoscopicEye.Right);
            return Rect.MinMaxRect(Mathf.Max(left.xMin, right.xMin), Mathf.Max(left.yMin, right.yMin),
                Mathf.Min(left.xMax, right.xMax), Mathf.Min(left.yMax, right.yMax));
        }

        private void Place(Camera camera)
        {
            float distance = Mathf.Max(1.8f, camera.nearClipPlane + .3f);
            var rect = VisibleRect(camera, distance, camera.stereoEnabled);
            if (rect.width <= 0 || rect.height <= 0) return;
            float scale = Mathf.Min(rect.width * .7f / 560, rect.height * .16f / 112);
            banner.transform.localScale = Vector3.one * scale;
            var centre = new Vector3(rect.center.x, Mathf.Lerp(rect.yMin, rect.yMax, .78f), distance);
            banner.transform.SetPositionAndRotation(camera.transform.TransformPoint(centre), camera.transform.rotation);
            banner.worldCamera = camera;
        }

        private void LateUpdate()
        {
            bool visible = warmup != null && warmup.Active && world != null && world.HeadPoseReady;
            if (!visible) { if (banner != null) banner.gameObject.SetActive(false); return; }
            if (banner == null) Build();
            banner.gameObject.SetActive(true); banner.enabled = true;
            detail.text = warmup.Approaching ? (warmup.WaitingForSpace ? "Bitte etwas Platz für den Hinweg lassen" : "Ich komme zu dir") : warmup.Returning ? (warmup.WaitingForSpace ? "Bitte etwas Platz für den Rückweg lassen" : "Zurück zur Bahn") :
                warmup.Finishing ? "Geschafft · zurück zur Bahn" : string.Format("{0} · {1}/{2} · {3} s", warmup.Label, warmup.StepIndex + 1, warmup.StepCount, Mathf.CeilToInt(warmup.StepDuration - warmup.StepElapsed));
            Place(world.HeadCamera);
        }

        private void OnDisable()
        {
            Application.onBeforeRender -= BeforeRender;
            if (banner != null) banner.gameObject.SetActive(false);
        }
        private void OnDestroy()
        {
            if (banner == null) return;
            if (Application.isPlaying) Destroy(banner.gameObject); else DestroyImmediate(banner.gameObject);
        }
    }
}
