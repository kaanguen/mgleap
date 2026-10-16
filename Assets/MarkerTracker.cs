using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.OpenXR;
using MagicLeap.OpenXR.Features.MarkerUnderstanding;

public class MarkerTracker : MonoBehaviour
{
    [Header("Marker Configuration")]
    public MarkerType markerType = MarkerType.AprilTag;
    public float markerSizeMeters = 0.1f;

    [Header("AprilTag")]
    public AprilTagType aprilTagDictionary = AprilTagType.Dictionary_36H11;

    [Header("Profile")]
    public MarkerDetectorProfile detectorProfile = MarkerDetectorProfile.Default;

    public event Action<IReadOnlyList<MarkerData>> OnMarkersUpdated;

    public IReadOnlyList<MarkerData> CurrentMarkers => _currentMarkers;
    public MarkerDetectorStatus DetectorStatus =>
        _markerDetector != null ? _markerDetector.Status : MarkerDetectorStatus.Error;
    public string ErrorMessage => _errorMessage;

    private MagicLeapMarkerUnderstandingFeature _markerFeature;
    private MarkerDetector _markerDetector;
    private IReadOnlyList<MarkerData> _currentMarkers = Array.Empty<MarkerData>();
    private string _errorMessage;

    private void Start()
    {
        if (OpenXRSettings.Instance == null)
        {
            SetError("OpenXRSettings.Instance is null. OpenXR is not active.");
            return;
        }

        _markerFeature = OpenXRSettings.Instance
            .GetFeature<MagicLeapMarkerUnderstandingFeature>();

        if (_markerFeature == null || !_markerFeature.enabled)
        {
            SetError("'Magic Leap 2 Marker Understanding' feature is not enabled.\n" +
                     "-> Project Settings > XR Plug-in Management > OpenXR");
            return;
        }

        // Configure detector settings based on selected marker type
        var settings = new MarkerDetectorSettings
        {
            MarkerDetectorProfile = detectorProfile,
            MarkerType = markerType
        };

        if (markerType == MarkerType.AprilTag)
        {
            settings.AprilTagSettings = new AprilTagSettings
            {
                AprilTagType = aprilTagDictionary,
                AprilTagLength = markerSizeMeters,
                EstimateAprilTagLength = false
            };
        }

        // Initialize native OpenXR marker detector
        _markerDetector = _markerFeature.CreateMarkerDetector(settings);

        if (_markerDetector == null)
        {
            SetError("CreateMarkerDetector failed. Check MARKER_TRACKING permission.");
            return;
        }

        Debug.Log($"[MarkerTracker] Detector started: {markerType}");
    }

    private void Update()
    {
        if (_markerFeature == null || _markerDetector == null)
            return;

        // Poll detector state from OpenXR runtime
        _markerFeature.UpdateMarkerDetectors();

        // Broadcast markers whenever detector is ready and has results
        if (_markerDetector.Status == MarkerDetectorStatus.Ready &&
            _markerDetector.Data != null && _markerDetector.Data.Count > 0)
        {
            _currentMarkers = _markerDetector.Data;
            OnMarkersUpdated?.Invoke(_currentMarkers);
        }
        else
        {
            _currentMarkers = Array.Empty<MarkerData>();
        }
    }

    private void OnDestroy()
    {
        if (_markerFeature != null && _markerDetector != null)
        {
            _markerFeature.DestroyMarkerDetector(_markerDetector);
            _markerDetector = null;
            Debug.Log("[MarkerTracker] Detector stopped.");
        }
    }

    private void SetError(string msg)
    {
        _errorMessage = msg;
        Debug.LogError($"[MarkerTracker] {msg}");
        enabled = false;
    }
}
