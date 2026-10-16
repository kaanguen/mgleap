using System.Text;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.OpenXR;
using TMPro;
using MagicLeap.OpenXR.Features.MarkerUnderstanding;

public class MarkerManager : MonoBehaviour
{
    public MarkerType markerType = MarkerType.AprilTag;

    public bool estimateMarkerSize = true;

    public float markerSizeMeters = 0.1f;

    public ArucoType arucoDictionary = ArucoType.Dictionary_5x5_100;

    public AprilTagType aprilTagDictionary = AprilTagType.Dictionary_36H11;

    public GameObject customPrefab;

    public float cubeSize = 0.05f;

    public bool hideObjectWhenLost = false;

    public bool enableInHeadsetHUD = true;

    public bool enableOnGUI = true;

    public TMP_Text customTMPText;

    public Text customUIText;

    // Schnittstellen zum Magic Leap OpenXR Feature & Detektor
    private MagicLeapMarkerUnderstandingFeature _markerFeature;
    private MarkerDetector _markerDetector;
    private GameObject _spawnedCube;

    // HUD / Debugger Felder
    private GameObject _hudRoot;
    private TextMesh _debugTextMesh;
    private string _debugString = "";
    private string _errorMessage = "";
    private string _lastDetectedInfo = "Keine Daten";
    private float _lastDetectionTime = -1f;

    private void Start()
    {
        if (enableInHeadsetHUD && customTMPText == null && customUIText == null)
        {
            CreateInHeadsetHUD();
        }

        if (OpenXRSettings.Instance == null)
        {
            _errorMessage = "OpenXRSettings.Instance ist null! OpenXR ist nicht aktiv.";
            Debug.LogError($"[OpenXRMarker] FEHLER: {_errorMessage}");
            UpdateDebugText(false);
            enabled = false;
            return;
        }

        _markerFeature = OpenXRSettings.Instance.GetFeature<MagicLeapMarkerUnderstandingFeature>();

        if (_markerFeature == null || !_markerFeature.enabled)
        {
            _errorMessage = "'Magic Leap 2 Marker Understanding' Feature ist nicht aktiviert!\nPrüfe Project Settings > XR Plug-in Management > OpenXR.";
            Debug.LogError($"[OpenXRMarker] FEHLER: {_errorMessage}");
            UpdateDebugText(false);
            enabled = false;
            return;
        }

        MarkerDetectorSettings settings = new MarkerDetectorSettings
        {
            MarkerDetectorProfile = MarkerDetectorProfile.Default,
            MarkerType = markerType,
           
        };

        if (markerType == MarkerType.Aruco)
        {
            settings.ArucoSettings.ArucoType = arucoDictionary;
            settings.ArucoSettings.ArucoLength = markerSizeMeters;
            settings.ArucoSettings.EstimateArucoLength = false;
        }
        else if (markerType == MarkerType.AprilTag)
        {
            settings.AprilTagSettings.AprilTagType = aprilTagDictionary;
            settings.AprilTagSettings.AprilTagLength = markerSizeMeters;
            settings.AprilTagSettings.EstimateAprilTagLength = false;
        }
        else if (markerType == MarkerType.QR)
        {
            settings.QRSettings.EstimateQRLength = false;
            settings.QRSettings.QRLength = markerSizeMeters;
        }

        _markerDetector = _markerFeature.CreateMarkerDetector(settings);

        if (_markerDetector == null)
        {
            _errorMessage = "CreateMarkerDetector fehlgeschlagen!\nÜberprüfe Manifest-Permission MARKER_TRACKING.";
            Debug.LogError($"[OpenXRMarker] FEHLER: {_errorMessage}");
            UpdateDebugText(false);
            enabled = false;
            return;
        }

        Debug.Log("[OpenXRMarker] OpenXR Marker Detector erfolgreich gestartet!");
        UpdateDebugText(false);
    }

    private void Update()
    {
        if (_markerFeature == null || _markerDetector == null)
        {
            UpdateDebugText(false);
            return;
        }

        _markerFeature.UpdateMarkerDetectors();

        bool isDetected = false;

        if (_markerDetector.Data != null && _markerDetector.Data.Count > 0)
        {
            var markerData = _markerDetector.Data[0];

            if (markerData.MarkerPose.HasValue)
            {
                isDetected = true;
                _lastDetectionTime = Time.time;
                Pose pose = markerData.MarkerPose.Value;

                if (_spawnedCube == null)
                {
                        _spawnedCube = Instantiate(customPrefab);
                
                }

                _spawnedCube.SetActive(true);
                _spawnedCube.transform.position = pose.position;
                _spawnedCube.transform.rotation = pose.rotation;

                string idOrContent;
                if (markerType == MarkerType.QR)
                {
                    idOrContent = !string.IsNullOrEmpty(markerData.MarkerString) ? markerData.MarkerString : "Text erkannt";
                }
                else
                {
                    idOrContent = markerData.MarkerNumber.HasValue ? $"#{markerData.MarkerNumber.Value}" : "--";
                }

                Camera cam = Camera.main ?? FindObjectOfType<Camera>();
                float distance = cam != null ? Vector3.Distance(cam.transform.position, pose.position) : 0f;

                Vector3 euler = pose.rotation.eulerAngles;
                float pitch = (euler.x > 180f) ? euler.x - 360f : euler.x;
                float yaw = (euler.y > 180f) ? euler.y - 360f : euler.y;
                float roll = (euler.z > 180f) ? euler.z - 360f : euler.z;

                _lastDetectedInfo = $"ID / Wert: {idOrContent}\n" +
                                    $"Distanz:   {distance:F2} m\n" +
                                    $"Position:  X:{pose.position.x:F2}  Y:{pose.position.y:F2}  Z:{pose.position.z:F2}\n" +
                                    $"Winkel:    P:{pitch:F0}°  Y:{yaw:F0}°  R:{roll:F0}°";
            }
        }

        // Falls Marker verloren ging und das Objekt ausgeblendet werden soll
        if (!isDetected && _spawnedCube != null && hideObjectWhenLost)
        {
            _spawnedCube.SetActive(false);
        }

        UpdateDebugText(isDetected);
    }

    private void LateUpdate()
    {
        // HUD sanft im Blickfeld des Nutzers mitführen (Lazy-Follow)
        if (_hudRoot != null && enableInHeadsetHUD)
        {
            Camera cam = Camera.main ?? FindObjectOfType<Camera>();
            if (cam != null)
            {
                // Platzierung: 0.85m vor der Kamera, leicht nach unten links versetzt
                Vector3 targetPos = cam.transform.position + (cam.transform.forward * 0.85f) - (cam.transform.right * 0.18f) - (cam.transform.up * 0.10f);
                Quaternion targetRot = cam.transform.rotation;

                _hudRoot.transform.position = Vector3.Lerp(_hudRoot.transform.position, targetPos, Time.deltaTime * 6f);
                _hudRoot.transform.rotation = Quaternion.Slerp(_hudRoot.transform.rotation, targetRot, Time.deltaTime * 6f);
            }
        }
    }

    private void UpdateDebugText(bool isDetected)
    {
        StringBuilder sb = new StringBuilder();
        sb.AppendLine("<b><color=#00E5FF>--- ML2 MARKER DEBUGGER ---</color></b>");

        if (!string.IsNullOrEmpty(_errorMessage))
        {
            sb.AppendLine("<b>Status:    <color=#FF4444>[ FEHLER ]</color></b>");
            sb.AppendLine($"<color=#FF8888>{_errorMessage}</color>");
        }
        else if (isDetected)
        {
            sb.AppendLine("<b>Status:    <color=#44FF44>[ DETEKTIERT ]</color></b>");
        }
        else
        {
            sb.AppendLine("<b>Status:    <color=#FFDD44>[ SUCHT / SCANNING... ]</color></b>");
        }

        string targetType = markerType switch
        {
            MarkerType.AprilTag => $"AprilTag ({aprilTagDictionary})",
            MarkerType.Aruco => $"ArUco ({arucoDictionary})",
            MarkerType.QR => "QR Code",
            _ => markerType.ToString()
        };

        sb.AppendLine($"Ziel:      {targetType}");
        sb.AppendLine($"Größe:     {markerSizeMeters * 100f:F1} cm ({markerSizeMeters:F2} m)");

        if (isDetected)
        {
            sb.AppendLine(_lastDetectedInfo);
            sb.AppendLine("Letzter:   Gerade eben");
        }
        else
        {
            if (_lastDetectionTime > 0f)
            {
                float elapsed = Time.time - _lastDetectionTime;
                sb.AppendLine($"Letzter:   Vor {elapsed:F1}s");
                sb.AppendLine(_lastDetectedInfo);
            }
            else
            {
                sb.AppendLine("Letzter:   Noch kein Marker erfasst");
            }
        }

        _debugString = sb.ToString();

        if (_debugTextMesh != null)
        {
            _debugTextMesh.text = _debugString;
        }
        if (customTMPText != null)
        {
            customTMPText.text = _debugString;
        }
        if (customUIText != null)
        {
            customUIText.text = _debugString;
        }
    }

    private void CreateInHeadsetHUD()
    {
        _hudRoot = new GameObject("ML2_MarkerDebugHUD");

        // Hintergrund-Panel (Quad) für bessere Lesbarkeit in jeder Umgebung
        // GameObject bg = GameObject.CreatePrimitive(PrimitiveType.Quad);
        // bg.name = "Background";
        // bg.transform.SetParent(_hudRoot.transform, false);
        // bg.transform.localPosition = new Vector3(0.18f, -0.09f, 0.005f);
        // bg.transform.localScale = new Vector3(0.42f, 0.24f, 1f);
        // Destroy(bg.GetComponent<Collider>());

        // var bgRenderer = bg.GetComponent<MeshRenderer>();
        // Shader unlitShader = Shader.Find("Sprites/Default") ?? Shader.Find("Unlit/Color");
        // if (unlitShader != null)
        // {
        //     Material bgMat = new Material(unlitShader);
        //     bgMat.color = new Color(0.08f, 0.08f, 0.12f, 0.85f);
        //     bgRenderer.material = bgMat;
        // }

        // 3D TextMesh für das schwebende Display
        GameObject textObj = new GameObject("Text");
        textObj.transform.SetParent(_hudRoot.transform, false);
        textObj.transform.localPosition = Vector3.zero;
        _debugTextMesh = textObj.AddComponent<TextMesh>();
        _debugTextMesh.fontSize = 20;
        _debugTextMesh.characterSize = 0.0035f;
        _debugTextMesh.anchor = TextAnchor.UpperLeft;
        _debugTextMesh.alignment = TextAlignment.Left;
        _debugTextMesh.color = Color.white;

        // Sofortige Ausrichtung auf die Kamera
        Camera cam = Camera.main ?? FindObjectOfType<Camera>();
        if (cam != null)
        {
            _hudRoot.transform.position = cam.transform.position + (cam.transform.forward * 0.85f) - (cam.transform.right * 0.18f) - (cam.transform.up * 0.10f);
            _hudRoot.transform.rotation = cam.transform.rotation;
        }
    }

    private void OnGUI()
    {
        if (!enableOnGUI) return;

        int width = 340;
        int height = 190;
        Rect rect = new Rect(20, 20, width, height);

        GUI.color = new Color(0.08f, 0.08f, 0.12f, 0.90f);
        GUI.Box(rect, GUIContent.none);
        GUI.color = Color.white;

        GUIStyle contentStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 12,
            richText = true,
            alignment = TextAnchor.UpperLeft
        };

        GUILayout.BeginArea(new Rect(28, 25, width - 16, height - 10));
        GUILayout.Label(_debugString, contentStyle);
        GUILayout.EndArea();
    }

    private void OnDestroy()
    {
        if (_hudRoot != null)
        {
            Destroy(_hudRoot);
        }

        if (_markerFeature != null && _markerDetector != null)
        {
            _markerFeature.DestroyMarkerDetector(_markerDetector);
            _markerDetector = null;
            Debug.Log("[OpenXRMarker] Marker Detector sauber beendet.");
        }
    }
}
