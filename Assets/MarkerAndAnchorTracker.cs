using System.Collections.Generic;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.OpenXR;
using MagicLeap.OpenXR.Features.MarkerUnderstanding;

public class MarkerAndAnchorTracker : MonoBehaviour
{
    [SerializeField] private XROrigin _xrOrigin;
    [SerializeField] private ARAnchorManager _anchorManager;
    [SerializeField] private GameObject _markerPrefab;
    [SerializeField] private AprilTagType _aprilTagType = AprilTagType.Dictionary_36H11;
    [SerializeField, Min(0f)] private float _aprilTagLength = 0.1f;
    [SerializeField] private MarkerDetectorProfile _detectorProfile = MarkerDetectorProfile.Default;
    [SerializeField, Min(0f)] private float _maxReprojectionErrorMeters = 0.02f;

    private MagicLeapMarkerUnderstandingFeature _markerFeature;
    private MarkerDetector _markerDetector;
    private readonly Dictionary<ulong, GameObject> _anchorsByMarkerId = new();

    private void Start()
    {
        if (_xrOrigin == null)
            _xrOrigin = FindAnyObjectByType<XROrigin>();
        if (_anchorManager == null)
            _anchorManager = FindAnyObjectByType<ARAnchorManager>();

        if (!Check(_xrOrigin != null && _xrOrigin.CameraFloorOffsetObject != null,
                   "XROrigin or CameraFloorOffsetObject is missing."))
            return;
        if (!Check(_anchorManager != null && _anchorManager.isActiveAndEnabled,
                   "ARAnchorManager is missing or disabled."))
            return;
        if (!Check(_markerPrefab != null, "Marker prefab is missing."))
            return;
        if (!Check(OpenXRSettings.Instance != null, "OpenXRSettings.Instance is missing."))
            return;

        _markerFeature = OpenXRSettings.Instance
            .GetFeature<MagicLeapMarkerUnderstandingFeature>();
        if (!Check(_markerFeature != null && _markerFeature.enabled,
                   "Marker Understanding feature is missing or disabled."))
            return;

        var settings = new MarkerDetectorSettings
        {
            MarkerDetectorProfile = _detectorProfile,
            MarkerType = MarkerType.AprilTag,
            AprilTagSettings = new AprilTagSettings
            {
                AprilTagType = _aprilTagType,
                AprilTagLength = _aprilTagLength,
                EstimateAprilTagLength = false
            }
        };
        _markerDetector = _markerFeature.CreateMarkerDetector(settings);
        Check(_markerDetector != null, "AprilTag detector could not be created.");
    }

    private void Update()
    {
        _markerFeature.UpdateMarkerDetectors();
        if (_markerDetector.Status != MarkerDetectorStatus.Ready)
            return;

        Transform originTransform = _xrOrigin.CameraFloorOffsetObject.transform;
        if (_markerDetector.Data == null)
            return;
        for (int i = 0; i < _markerDetector.Data.Count; i++)
        {
            MarkerData data = _markerDetector.Data[i];
            if (!data.MarkerPose.HasValue || !data.MarkerNumber.HasValue)
                continue;
            if (_maxReprojectionErrorMeters > 0f &&
                data.ReprojectionErrorMeters > _maxReprojectionErrorMeters)
                continue;

            ulong markerId = data.MarkerNumber.Value;
            if (_anchorsByMarkerId.ContainsKey(markerId))
                continue;

            Pose pose = data.MarkerPose.Value;
            Vector3 position = originTransform.TransformPoint(pose.position);
            Quaternion rotation = originTransform.rotation * pose.rotation;
            GameObject instance = Instantiate(_markerPrefab, position, rotation);
            instance.AddComponent<ARAnchor>();
            _anchorsByMarkerId.Add(markerId, instance);
            Debug.Log($"[AprilTagAnchorPlacer] Anchor created for marker {markerId}.");
        }
    }

    private void OnDestroy()
    {
        if (_markerFeature != null)
            _markerFeature.DestroyAllMarkerDetectors();
    }

    private bool Check(bool condition, string message)
    {
        if (condition)
            return true;
        Debug.LogError($"[AprilTagAnchorPlacer] {message}");
        enabled = false;
        return false;
    }
}
