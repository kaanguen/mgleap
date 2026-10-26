using UnityEngine;
using UnityEngine.UI;

namespace StadionTracking
{
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class RunnerGlassGraphic : MaskableGraphic
    {
        [SerializeField] private bool flatSurface;
        [SerializeField, Range(0.28f, 0.5f)] private float tintOpacity = 0.28f;
        [SerializeField] private Material glassMaterial;
        public bool FlatSurface { get => flatSurface; set { flatSurface = value; SetVerticesDirty(); } }

        protected override void OnEnable()
        {
            base.OnEnable();
            if (glassMaterial == null) glassMaterial = Resources.Load<Material>("RunnerUI/Glass");
            material = glassMaterial;
            if (canvas != null)
                canvas.additionalShaderChannels |= AdditionalCanvasShaderChannels.TexCoord1 | AdditionalCanvasShaderChannels.TexCoord2;
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            Rect r = rectTransform.rect;
            float padding = flatSurface ? 0 : 42;
            for (int i = 0; i < 4; i++)
            {
                float u = (i == 1 || i == 2) ? 1 : 0;
                float v = i >= 2 ? 1 : 0;
                var vert = UIVertex.simpleVert;
                vert.position = new Vector3(Mathf.Lerp(r.xMin - padding, r.xMax + padding, u),
                    Mathf.Lerp(r.yMin - padding, r.yMax + padding, v));
                vert.uv0 = new Vector2(Mathf.Lerp(-padding / r.width, 1 + padding / r.width, u),
                    Mathf.Lerp(-padding / r.height, 1 + padding / r.height, v));
                vert.uv1 = new Vector4(r.width, r.height, flatSurface ? 22 : 28, tintOpacity);
                vert.uv2 = new Vector4(flatSurface ? 1 : 0, 0, 0, 0);
                vert.color = color;
                vh.AddVert(vert);
            }
            vh.AddTriangle(0, 1, 2); vh.AddTriangle(2, 3, 0);
        }
    }
}
