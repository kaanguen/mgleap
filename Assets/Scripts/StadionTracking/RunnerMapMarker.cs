using UnityEngine;

namespace StadionTracking
{

    [RequireComponent(typeof(UnityEngine.UI.RawImage))]
    public sealed class RunnerMapMarker : MonoBehaviour
    {
        [SerializeField] private Color markerColor = RunnerUiTheme.Goal;
        [SerializeField, Range(0.005f, 0.1f)] private float markerWidthFraction = 0.025f;

        private UnityEngine.UI.RawImage map;
        private RunnerMapDot dot;
        private StadiumTrackPreview runner;

        // Show the runner currently selected by the shared stats panel.
        public void BindRunner(StadiumTrackPreview target)
        {
            runner = target;
            EnsureDot();
        }

        private void EnsureDot()
        {
            if (map == null)
            {
                map = GetComponent<UnityEngine.UI.RawImage>();

                Texture2D simpleTrack = Resources.Load<Texture2D>("SimpleTrack");
                if (simpleTrack != null)
                {
                    map.texture = simpleTrack;
                    map.uvRect = new Rect(0, 0, 1, 1);
                }
                else
                    Debug.LogError("[RunnerStats] SimpleTrack.png is missing from Resources.");
            }
            if (dot != null)
                return;
            var marker = new GameObject("Runner position", typeof(RectTransform), typeof(CanvasRenderer), typeof(RunnerMapDot));
            marker.layer = gameObject.layer;
            marker.transform.SetParent(transform, false);
            dot = marker.GetComponent<RunnerMapDot>();
            dot.raycastTarget = false;
            dot.color = markerColor;
            dot.rectTransform.anchorMin = dot.rectTransform.anchorMax = map.rectTransform.pivot;
            dot.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            dot.enabled = false;
        }

        // Map the runner position onto the PNG and move the overlay dot.
        private void LateUpdate()
        {
            if (dot == null)
                return;
            bool visible = runner != null && map != null && map.texture != null;
            dot.enabled = visible;
            if (!visible)
                return;

            Vector2 position = runner.TrackLocalPosition;
            SimpleTrackMap.MapPoint mapped = SimpleTrackMap.ToUv(position.x, position.y);
            Vector2 uv = new Vector2((float)mapped.U, (float)mapped.V);

            Rect crop = map.uvRect;
            if (Mathf.Abs(crop.width) < 0.00001f || Mathf.Abs(crop.height) < 0.00001f)
            {
                dot.enabled = false;
                return;
            }
            Vector2 relative = new Vector2((uv.x - crop.x) / crop.width, (uv.y - crop.y) / crop.height);
            dot.enabled = relative.x >= 0 && relative.x <= 1 && relative.y >= 0 && relative.y <= 1;
            Rect rect = map.rectTransform.rect;
            dot.rectTransform.anchoredPosition = new Vector2(rect.xMin + relative.x * rect.width,
                rect.yMin + relative.y * rect.height);
            float size = Mathf.Abs(rect.width) * markerWidthFraction;
            dot.rectTransform.sizeDelta = new Vector2(size, size);
            dot.color = markerColor;
        }
    }

    public sealed class RunnerMapDot : UnityEngine.UI.MaskableGraphic
    {
        protected override void OnPopulateMesh(UnityEngine.UI.VertexHelper mesh)
        {
            mesh.Clear();
            Rect rect = rectTransform.rect;
            const int segments = 24;
            mesh.AddVert(rect.center, color, Vector2.zero);
            for (int i = 0; i < segments; i++)
            {
                float angle = 2 * Mathf.PI * i / segments;
                mesh.AddVert(rect.center + new Vector2(Mathf.Cos(angle) * rect.width * 0.5f,
                    Mathf.Sin(angle) * rect.height * 0.5f), color, Vector2.zero);
            }
            for (int i = 0; i < segments; i++)
                mesh.AddTriangle(0, i + 1, (i + 1) % segments + 1);
        }
    }
}
