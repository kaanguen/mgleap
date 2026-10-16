using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using Unity.XR.CoreUtils;
using MagicLeap.OpenXR.Features.MarkerUnderstanding;


[RequireComponent(typeof(MarkerTracker))]
public class MarkerAnchorManager : MonoBehaviour
{
    [Header("References")]
    [Tooltip("XR Origin used for coordinate transformation.")]
    [SerializeField] private XROrigin _xrOrigin;

    [Header("Prefab")]
    [Tooltip("Prefab instantiated at anchor position (optional).")]
    [SerializeField] private GameObject _markerPrefab;

    [Header("Alignment")]
    [Tooltip("Keep object standing upright and facing the camera?")]
    [SerializeField] private bool _faceCameraYOnly = true;

    [Tooltip("Additional rotation offset in degrees.")]
    [SerializeField] private Vector3 _rotationOffset = Vector3.zero;

    [Tooltip("Small offset from the marker surface in meters.")]
    [SerializeField] private float _markerSurfaceOffset = 0.01f;

    private readonly Dictionary<string, GameObject> _anchoredMarkers = new();

    public IReadOnlyDictionary<string, GameObject> AnchoredMarkers => _anchoredMarkers;

    private MarkerTracker _tracker;

    // ───────────────────────────────────────────────────────────────────
    private void Awake()
    {
        _tracker = GetComponent<MarkerTracker>();

        if (_xrOrigin == null)
            _xrOrigin = FindObjectOfType<XROrigin>();

    }

    private void OnEnable()
    {
        _tracker.OnMarkersUpdated += HandleMarkersUpdated;
    }

    private void OnDisable()
    {
        _tracker.OnMarkersUpdated -= HandleMarkersUpdated;
    }

    // Process detected markers and instantiate one anchor per unique marker ID
    private void HandleMarkersUpdated(IReadOnlyList<MarkerData> markers)
    {
        for (int i = 0; i < markers.Count; i++)
        {
            var data = markers[i];
            if (!data.MarkerPose.HasValue)
                continue;

            string markerId = GetMarkerId(data);
            if (string.IsNullOrEmpty(markerId))
                continue;

            // Skip if this marker is already anchored
            if (_anchoredMarkers.ContainsKey(markerId))
                continue;

            // Convert local marker pose to Unity world coordinates
            Pose localPose = data.MarkerPose.Value;
            Vector3 worldPos = _xrOrigin != null
                ? _xrOrigin.transform.TransformPoint(localPose.position)
                : localPose.position;

            Quaternion markerWorldRotation = _xrOrigin != null
                ? _xrOrigin.transform.rotation * localPose.rotation
                : localPose.rotation;
            worldPos += markerWorldRotation * Vector3.up * _markerSurfaceOffset;

            Quaternion worldRot;
            if (_faceCameraYOnly)
            {
                Camera cam = Camera.main;
                if (cam != null)
                {
                    // Keep object standing upright by locking pitch/roll and aligning yaw towards camera
                    Vector3 directionToCam = cam.transform.position - worldPos;
                    directionToCam.y = 0;

                    worldRot = directionToCam.sqrMagnitude > 0.001f
                        ? Quaternion.LookRotation(directionToCam.normalized, Vector3.up)
                        : Quaternion.identity;
                }
                else
                {
                    worldRot = markerWorldRotation;
                }
            }
            else
            {
                worldRot = markerWorldRotation;
            }

            if (_rotationOffset != Vector3.zero)
            {
                worldRot *= Quaternion.Euler(_rotationOffset);
            }

            GameObject anchorObj = CreateAnchoredObject(markerId, worldPos, worldRot);
            _anchoredMarkers[markerId] = anchorObj;

            Debug.Log($"[MarkerAnchor] Created anchor for marker {markerId} at {worldPos}");
        }
    }
    

    // Instantiates prefab and binds ARAnchor to lock it in ML2 spatial space
    private GameObject CreateAnchoredObject(string markerId, Vector3 position, Quaternion rotation)
    {
        GameObject obj;

        if (_markerPrefab != null)
        {
            obj = Instantiate(_markerPrefab, position, rotation);
        }
        else
        {
            obj = new GameObject($"MarkerAnchor_{markerId}");
            obj.transform.SetPositionAndRotation(position, rotation);
        }

        obj.name = $"MarkerAnchor_{markerId}";

        // ARAnchor ties the object to ML2 Spatial Anchor Subsystem
        if (obj.GetComponent<ARAnchor>() == null)
        {
            obj.AddComponent<ARAnchor>();
        }

        return obj;
    }

    // Extracts unique ID: number for ArUco/AprilTag or payload string for QR
    private static string GetMarkerId(MarkerData data)
    {
        if (data.MarkerNumber.HasValue)
            return $"N_{data.MarkerNumber.Value}";

        if (!string.IsNullOrEmpty(data.MarkerString))
            return $"S_{data.MarkerString}";

        return null;
    }
}
