using System.Text;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using MagicLeap.OpenXR.Features.MarkerUnderstanding;

public class MarkerDebugUI : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private MarkerTracker _tracker;
    [SerializeField] private MarkerAnchorManager _anchorManager;

    [Header("UI Outputs (Optional)")]
    [SerializeField] private TMP_Text _tmpText;
    [SerializeField] private Text _uiText;

    [Header("Display Modes")]
    public bool enableHeadsetHUD = true;
    public bool enableOnGUI = true;

    private GameObject _hudRoot;
    private TextMesh _debugTextMesh;
    private string _debugString = "";

    private readonly StringBuilder _sb = new();

    private void Start()
    {
        if (_tracker == null)
            _tracker = FindObjectOfType<MarkerTracker>();

        if (_anchorManager == null)
            _anchorManager = FindObjectOfType<MarkerAnchorManager>();

        if (enableHeadsetHUD && _tmpText == null && _uiText == null)
            CreateHeadsetHUD();
    }

    private void LateUpdate()
    {
        RefreshDebugText();
        FollowHeadsetHUD();
    }


    // Aggregates tracker status, marker list, and spatial anchor count
    private void RefreshDebugText()
    {
        _sb.Clear();
        _sb.AppendLine("<b><color=#00E5FF>--- ML2 MARKER DEBUG ---</color></b>");

        if (_tracker == null)
        {
            _sb.AppendLine("<color=#FF4444>MarkerTracker not found.</color>");
            SetText();
            return;
        }

        if (!string.IsNullOrEmpty(_tracker.ErrorMessage))
        {
            _sb.AppendLine("<b>Status: <color=#FF4444>ERROR</color></b>");
            _sb.AppendLine($"<color=#FF8888>{_tracker.ErrorMessage}</color>");
            SetText();
            return;
        }

        var status = _tracker.DetectorStatus;
        string statusColor = status switch
        {
            MarkerDetectorStatus.Ready => "#44FF44",
            MarkerDetectorStatus.Pending => "#FFDD44",
            _ => "#FF4444"
        };
        _sb.AppendLine($"<b>Detector: <color={statusColor}>{status}</color></b>");

        var markers = _tracker.CurrentMarkers;
        _sb.AppendLine($"Markers Detected: {markers.Count}");

        for (int i = 0; i < markers.Count; i++)
        {
            var m = markers[i];
            string id = m.MarkerNumber.HasValue
                ? $"#{m.MarkerNumber.Value}"
                : (!string.IsNullOrEmpty(m.MarkerString) ? m.MarkerString : "?");

            _sb.Append($"  [{i}] ID:{id}");

            if (m.MarkerPose.HasValue)
            {
                var p = m.MarkerPose.Value.position;
                _sb.Append($"  Pos:({p.x:F2},{p.y:F2},{p.z:F2})");
            }
            _sb.AppendLine();
        }

        if (_anchorManager != null)
        {
            _sb.AppendLine($"Anchors: {_anchorManager.AnchoredMarkers.Count}");
            foreach (var kvp in _anchorManager.AnchoredMarkers)
            {
                var pos = kvp.Value != null ? kvp.Value.transform.position : Vector3.zero;
                _sb.AppendLine($"  {kvp.Key} -> ({pos.x:F2},{pos.y:F2},{pos.z:F2})");
            }
        }

        SetText();
    }

    private void SetText()
    {
        _debugString = _sb.ToString();

        if (_debugTextMesh != null)
            _debugTextMesh.text = _debugString;
        if (_tmpText != null)
            _tmpText.text = _debugString;
        if (_uiText != null)
            _uiText.text = _debugString;
    }

    private void CreateHeadsetHUD()
    {
        _hudRoot = new GameObject("ML2_MarkerDebugHUD");

        var textObj = new GameObject("Text");
        textObj.transform.SetParent(_hudRoot.transform, false);

        _debugTextMesh = textObj.AddComponent<TextMesh>();
        _debugTextMesh.fontSize = 20;
        _debugTextMesh.characterSize = 0.0035f;
        _debugTextMesh.anchor = TextAnchor.UpperLeft;
        _debugTextMesh.alignment = TextAlignment.Left;
        _debugTextMesh.color = Color.white;
    }

    private void FollowHeadsetHUD()
    {
        if (_hudRoot == null)
            return;

        Camera cam = Camera.main;
        if (cam != null)
        {
            _hudRoot.transform.position = cam.transform.position
                + cam.transform.forward * 0.85f
                - cam.transform.right * 0.18f
                - cam.transform.up * 0.10f;
            _hudRoot.transform.rotation = cam.transform.rotation;
        }
    }

    private void OnGUI()
    {
        if (!enableOnGUI) return;

        int w = 380, h = 260;
        var rect = new Rect(20, 20, w, h);

        GUI.color = new Color(0.08f, 0.08f, 0.12f, 0.90f);
        GUI.Box(rect, GUIContent.none);
        GUI.color = Color.white;

        var style = new GUIStyle(GUI.skin.label)
        {
            fontSize = 12,
            richText = true,
            alignment = TextAnchor.UpperLeft
        };

        GUILayout.BeginArea(new Rect(28, 25, w - 16, h - 10));
        GUILayout.Label(_debugString, style);
        GUILayout.EndArea();
    }

    private void OnDestroy()
    {
        if (_hudRoot != null)
            Destroy(_hudRoot);
    }
}
