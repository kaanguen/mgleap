using System.Collections.Generic;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using UnityEngine.XR.Management;
using UnityEngine.XR.OpenXR;
using MagicLeap.OpenXR.Features.MarkerUnderstanding;
using MagicLeap.OpenXR.Subsystems;

public class MarkerAndAnchorTracker : MonoBehaviour
{
    [SerializeField] private XROrigin _xrOrigin;
    [SerializeField] private ARAnchorManager _anchorManager;
    [SerializeField] private GameObject _markerPrefab;
    [SerializeField] private AprilTagType _aprilTagType = AprilTagType.Dictionary_36H11;
    [SerializeField, Min(0f)] private float _aprilTagLength = 0.1f;
    [SerializeField] private MarkerDetectorProfile _detectorProfile = MarkerDetectorProfile.Default;
    [SerializeField, Min(0.01f)] private float _confidenceCheckIntervalSeconds = 0.5f;
    [SerializeField] private float _uprightYawOffsetDegrees = 180f;

    private MagicLeapMarkerUnderstandingFeature _markerFeature;
    private MarkerDetector _markerDetector;
    private readonly Dictionary<ulong, AnchorState> _anchorsByMarkerId = new();
    private MLXrAnchorSubsystem _mlAnchorSubsystem;
    private float _nextConfidenceCheckTime;

    private sealed class AnchorState
    {
        public ARAnchor Anchor;
        public Renderer[] Renderers;
        public Canvas[] Canvases;
        public bool Accepted;
        public bool HasConfidence;
        public MLXrAnchorSubsystem.AnchorConfidence LastConfidence;
    }

    private void Start()
    {
        if (_xrOrigin == null)
            _xrOrigin = FindAnyObjectByType<XROrigin>();
        if (_anchorManager == null)
            _anchorManager = FindAnyObjectByType<ARAnchorManager>();

        if (_xrOrigin == null || _xrOrigin.CameraFloorOffsetObject == null)
        {
            Debug.LogError(
                "[MarkerAndAnchorTracker] XROrigin or CameraFloorOffsetObject is missing.");
            enabled = false;
            return;
        }
        if (_anchorManager == null || !_anchorManager.isActiveAndEnabled)
        {
            Debug.LogError("[MarkerAndAnchorTracker] ARAnchorManager is missing or disabled.");
            enabled = false;
            return;
        }
        if (_markerPrefab == null)
        {
            Debug.LogError("[MarkerAndAnchorTracker] Marker prefab is missing.");
            enabled = false;
            return;
        }
        if (OpenXRSettings.Instance == null)
        {
            Debug.LogError("[MarkerAndAnchorTracker] OpenXRSettings.Instance is missing.");
            enabled = false;
            return;
        }

        _markerFeature = OpenXRSettings.Instance
            .GetFeature<MagicLeapMarkerUnderstandingFeature>();
        if (_markerFeature == null || !_markerFeature.enabled)
        {
            Debug.LogError(
                "[MarkerAndAnchorTracker] Marker Understanding feature is missing or disabled.");
            enabled = false;
            return;
        }

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
        if (_markerDetector == null)
        {
            Debug.LogError("[MarkerAndAnchorTracker] AprilTag detector could not be created.");
            enabled = false;
        }
    }

    private void Update()
    {
        _markerFeature.UpdateMarkerDetectors();
        CheckAnchorConfidence();
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

            ulong markerId = data.MarkerNumber.Value;
            if (_anchorsByMarkerId.ContainsKey(markerId))
                continue;

            Pose pose = data.MarkerPose.Value;
            Vector3 position = originTransform.TransformPoint(pose.position);
            Quaternion rotation = GetUprightMarkerRotation(
                originTransform,
                pose.rotation,
                _uprightYawOffsetDegrees);
            GameObject instance = Instantiate(_markerPrefab, position, rotation);
            ARAnchor anchor = instance.AddComponent<ARAnchor>();
            _anchorsByMarkerId.Add(markerId, new AnchorState
            {
                Anchor = anchor,
                Renderers = instance.GetComponentsInChildren<Renderer>(true),
                Canvases = instance.GetComponentsInChildren<Canvas>(true)
            });
            SetRendererVisibility(_anchorsByMarkerId[markerId], false);
            Debug.Log($"[MarkerAndAnchorTracker] Anchor created for marker {markerId}.");
        }
    }

    private static Quaternion GetUprightMarkerRotation(
        Transform originTransform,
        Quaternion markerRotation,
        float yawOffsetDegrees)
    {
        Vector3 markerForward = originTransform.TransformDirection(
            markerRotation * Vector3.forward);
        Vector3 uprightForward = Vector3.ProjectOnPlane(markerForward, Vector3.up);

        if (uprightForward.sqrMagnitude < 0.0001f)
        {
            Vector3 markerRight = originTransform.TransformDirection(
                markerRotation * Vector3.right);
            uprightForward = Vector3.ProjectOnPlane(markerRight, Vector3.up);
        }

        if (uprightForward.sqrMagnitude < 0.0001f)
            return Quaternion.identity;

        Quaternion uprightRotation = Quaternion.LookRotation(
            uprightForward.normalized,
            Vector3.up);
        return uprightRotation * Quaternion.Euler(0f, yawOffsetDegrees, 0f);
    }

    private void OnDestroy()
    {
        if (_markerFeature != null)
            _markerFeature.DestroyAllMarkerDetectors();
    }

    private void CheckAnchorConfidence()
    {
        if (Time.time < _nextConfidenceCheckTime)
            return;

        _nextConfidenceCheckTime = Time.time + _confidenceCheckIntervalSeconds;
        MLXrAnchorSubsystem subsystem = GetAnchorSubsystem();

        foreach (KeyValuePair<ulong, AnchorState> entry in _anchorsByMarkerId)
        {
            AnchorState state = entry.Value;
            if (state.Accepted)
                continue;

            if (state.Anchor == null)
                continue;

            if (subsystem != null &&
                !state.Anchor.pending &&
                state.Anchor.trackingState == TrackingState.Tracking)
            {
                MLXrAnchorSubsystem.AnchorConfidence confidence =
                    subsystem.GetAnchorConfidence(state.Anchor);
                if (!state.HasConfidence || state.LastConfidence != confidence)
                {
                    state.LastConfidence = confidence;
                    state.HasConfidence = true;
                    Debug.Log(
                        $"[MarkerAndAnchorTracker] Marker {entry.Key} confidence: {confidence}.");
                }

                if (HasUsableConfidence(confidence))
                {
                    state.Accepted = true;
                    SetRendererVisibility(state, true);
                }
            }
        }
    }

    private static void SetRendererVisibility(AnchorState state, bool visible)
    {
        if (state.Renderers != null)
        {
            for (int i = 0; i < state.Renderers.Length; i++)
            {
                if (state.Renderers[i] != null)
                    state.Renderers[i].enabled = visible;
            }
        }

        if (state.Canvases != null)
        {
            for (int i = 0; i < state.Canvases.Length; i++)
            {
                if (state.Canvases[i] != null)
                    state.Canvases[i].enabled = visible;
            }
        }
    }

    private static bool HasUsableConfidence(
        MLXrAnchorSubsystem.AnchorConfidence confidence)
    {
        return confidence == MLXrAnchorSubsystem.AnchorConfidence.Medium ||
               confidence == MLXrAnchorSubsystem.AnchorConfidence.High;
    }

    private MLXrAnchorSubsystem GetAnchorSubsystem()
    {
        if (_mlAnchorSubsystem != null && _mlAnchorSubsystem.running)
            return _mlAnchorSubsystem;

        _mlAnchorSubsystem = null;
        XRGeneralSettings settings = XRGeneralSettings.Instance;
        if (settings == null || settings.Manager == null ||
            settings.Manager.activeLoader == null)
            return null;

        _mlAnchorSubsystem = settings.Manager.activeLoader
            .GetLoadedSubsystem<XRAnchorSubsystem>() as MLXrAnchorSubsystem;
        return _mlAnchorSubsystem != null && _mlAnchorSubsystem.running
            ? _mlAnchorSubsystem
            : null;
    }

}
