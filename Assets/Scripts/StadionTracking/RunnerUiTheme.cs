using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace StadionTracking
{
    // Runner Stats AR UI Style Guide v1.0; reference viewport 1280 x 720.
    public static class RunnerUiTheme
    {
        public static readonly Color Time = Hex("67E8F9");
        public static readonly Color Goal = Hex("D9F99D");
        public static readonly Color Warning = Hex("FF8A7A");
        public static readonly Color Value = Color.white;
        public static readonly Color Label = Hex("E3DEF7");
        public static readonly Color Background = Hex("1E1146");
        public const float PanelWidth = 300, Padding = 20, Gap = 14;
        public static TMP_FontAsset Font => Resources.Load<TMP_FontAsset>("RunnerUI/RunnerSans");
        private static Color Hex(string value) { ColorUtility.TryParseHtmlString("#" + value, out var color); return color; }

        public static void StyleText(TMP_Text text, int size, bool bold = false, bool label = false)
        {
            if (Font != null) text.font = Font;
            text.fontSize = Mathf.Max(13, size);
            text.enableAutoSizing = false;
            text.fontStyle = bold ? FontStyles.Bold : FontStyles.Normal;
            text.color = label ? Label : Value;
            text.characterSpacing = label ? 12 : 0;
            text.raycastTarget = false;
        }

        public static Canvas Panel(string name, float height, Transform parent = null)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler),
                typeof(GraphicRaycaster), typeof(UnityEngine.XR.Interaction.Toolkit.UI.TrackedDeviceGraphicRaycaster),
                typeof(RunnerGlassGraphic), typeof(RunnerArPanel));
            go.layer = 5;
            if (parent != null) go.transform.SetParent(parent, false);
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.additionalShaderChannels |= AdditionalCanvasShaderChannels.TexCoord1 | AdditionalCanvasShaderChannels.TexCoord2;
            go.GetComponent<RectTransform>().sizeDelta = new Vector2(300, height);
            canvas.worldCamera = Camera.main;
            return canvas;
        }

        public static RectTransform Rect(string name, Transform parent, float x, float y, float width, float height)
        {
            var existing = parent.Find(name);
            var go = existing != null ? existing.gameObject : new GameObject(name, typeof(RectTransform));
            go.layer = 5;
            var rect = go.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
            rect.anchoredPosition = new Vector2(x, -y);
            rect.sizeDelta = new Vector2(width, height);
            return rect;
        }

        public static TextMeshProUGUI Text(string name, Transform parent, string value, float x, float y,
            float width, float height, int size = 13, bool bold = false, bool label = false)
        {
            var rect = Rect(name, parent, x, y, width, height);
            var text = rect.GetComponent<TextMeshProUGUI>() ?? rect.gameObject.AddComponent<TextMeshProUGUI>();
            StyleText(text, size, bold, label);
            text.alignment = TextAlignmentOptions.MidlineLeft;
            text.enableWordWrapping = true;
            text.text = value;
            return text;
        }

        public static Button Button(string name, Transform parent, string title, float x, float y, float width,
            UnityEngine.Events.UnityAction action, float height = 40)
        {
            var rect = Rect(name, parent, x, y, width, height);
            var fill = rect.GetComponent<RunnerGlassGraphic>() ?? rect.gameObject.AddComponent<RunnerGlassGraphic>();
            fill.FlatSurface = true;
            fill.color = new Color(1, 1, 1, 0.07f);
            var button = rect.GetComponent<Button>() ?? rect.gameObject.AddComponent<Button>();
            button.targetGraphic = fill;
            var colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1, 1, 1, 1);
            colors.pressedColor = new Color(0.8f, 0.8f, 0.8f, 1);
            colors.disabledColor = new Color(1, 1, 1, 0.3f);
            button.colors = colors;
            var text = Text("Label", rect, title, 8, 0, width - 16, height, 13, false, true);
            text.alignment = TextAlignmentOptions.Center;
            if (action != null) button.onClick.AddListener(action);
            return button;
        }

        public static void Rule(Transform parent, float y)
        {
            var rect = Rect("Divider" + y, parent, 20, y, 260, 1);
            var line = rect.GetComponent<Image>() ?? rect.gameObject.AddComponent<Image>();
            line.color = new Color(1, 1, 1, 0.2f);
            line.raycastTarget = false;
        }
    }
}
