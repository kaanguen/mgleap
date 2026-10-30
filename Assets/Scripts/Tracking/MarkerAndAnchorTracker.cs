using System.Collections;
using System.Collections.Generic;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.OpenXR;
using MagicLeap.OpenXR.Features.MarkerUnderstanding;
using MagicLeap.Android;
using UnityEngine.XR.ARSubsystems;
using StadionTracking;

public partial class MarkerAndAnchorTracker : MonoBehaviour
{
    [SerializeField] private XROrigin _xrOrigin;
    [SerializeField] private GameObject _markerPrefab;
    [SerializeField] private AprilTagType _aprilTagType = AprilTagType.Dictionary_36H11;
    [SerializeField, Min(0f)] private float _aprilTagLength = 0.1f;
    [SerializeField] private MarkerDetectorProfile _detectorProfile = MarkerDetectorProfile.Default;

    [Header("Marker planes")]
    [SerializeField] private ARPlaneManager _planeManager;
    [SerializeField, Tooltip("After detecting a floor tag, scan and display all detected Floor surfaces in the search area.")]
    private bool _markersOnFloor;
    [SerializeField, Min(1f), Tooltip("Horizontal query padding for scanning the surrounding floor, in meters.")]
    private float _floorSearchExtent = 10f;
    [SerializeField, Min(1f)] private float _floorSearchVerticalExtent = 3f;
    [SerializeField, Min(0.1f)] private float _planeQueryPadding = 1f;
    [SerializeField, Min(0.04f)] private float _minPlaneArea = 0.04f;
    [SerializeField, Min(1)] private int _maxPlaneResults = 100;
    [SerializeField, Min(0.01f)] private float _maxMarkerPlaneDistance = 0.15f;
    [SerializeField, Min(0f), Tooltip("Allow small gaps at detected surface edges, in meters.")]
    private float _planeBoundaryTolerance = 0.05f;
    [Header("Character placement")]
    [SerializeField, Min(0f), Tooltip("Horizontal spawn distance in front of the user's current view, in meters.")]
    private float _characterDistanceFromUser = 5f;
    [SerializeField, Min(0f), Tooltip("If the requested spawn is not covered by a floor polygon, allow an initial spawn on a confirmed floor boundary this many horizontal meters away. Zero disables this fallback.")]
    private float _maxSpawnFloorAdjustment = 2f;
    [SerializeField, Tooltip("Optional gap between the character's soles and the detected surface, in meters.")]
    private float _characterGroundClearance = 0f;
    [SerializeField, Min(0f), Tooltip("Response time for floor height and tilt corrections, in seconds. Zero disables smoothing.")]
    private float _planeSmoothingTime = 0.25f;

    [Header("Stadium route and running")]
    [SerializeField] private StadiumPreviewSettings _stadiumPreview = new StadiumPreviewSettings();
    [SerializeField, Tooltip("Optional shared RunnerStatsCanvas UI. If empty, it is found in the active scene when an avatar spawns.")]
    private UIScript _runnerStatsCanvas;
    [SerializeField, Tooltip("Optional ActiveStats HUD. If empty, it is found in the active scene when a runner spawns.")]
    private ActiveStatsDisplay _activeStatsDisplay;
    [Header("Plane diagnostics")]
    [SerializeField] private bool _logPlaneDiagnostics = true;
    [SerializeField, Min(1f)] private float _planeDiagnosticsInterval = 5f;
    [SerializeField, Min(1)] private int _maxPlaneDiagnosticPairs = 30;
    private readonly Dictionary<ulong, MarkerState> _markersById = new();
    private readonly HashSet<TrackableId> _visiblePlaneIds = new();
    private readonly Dictionary<TrackableId, SmoothedPlane> _smoothedPlanes = new();
    private readonly List<TrackableId> _removedPlaneIds = new();
    private bool _spatialMappingGranted;
    private bool _planeSelectionDirty;
    private bool _planeDetectionConfigured;
    private float _nextPlaneStatusTime;
    private string _lastPlaneStatus;
    private float _nextPlaneDiagnosticsTime;

    private StraightTreadmillWorld _runningWorld;
    private bool _runningWorldPlaced;
    private ARPlane _runningWorldFloor;
    private int _runningWorldRevision;
    private float _runningWorldScanRequestedAt;
    private float _nextRunningPlacementDiagnostic;
    public string RunningTrackStatus { get; private set; } = "Warte auf Bodenmarker.";
    public bool RunningFloorDetected { get; private set; }
    public bool RunningFloorTracking => _runningWorldFloor != null &&
        _runningWorldFloor.trackingState == TrackingState.Tracking &&
        _planeManager != null && _planeManager.enabled && _spatialMappingGranted;
    public ulong? RunningTrackMarkerId { get; private set; }
    public void BindRunningWorld(StraightTreadmillWorld world)
    {
        _runningWorld = world;
        _markersOnFloor = true;
        ResetRunningTrackPlacement(false);
        if (Application.isPlaying) RunnerSession.GetOrCreate(world).BindMarkerTracker(this);
    }

    private MagicLeapMarkerUnderstandingFeature _markerFeature;
    private MarkerDetector _markerDetector;

    private sealed class MarkerState
    {
        public Vector3 SessionPosition;
        public Quaternion SessionRotation;
        public Vector3 ObservationCameraSessionPosition;
        public float ReportedLength;
        public float LastSeenTime;
        public int PoseSamples;
        public GameObject Instance;
        public TrackableId GroundPlaneId;
        public Vector3 PlacementSessionPosition;
        public Vector3 LocalGroundContact;
        public bool GroundContactReady;
        public int SpawnFrame;
        public Animator Animator;
        public Renderer[] Renderers;

        public StadiumTrackPreview TrackPreview;
        public UIScript CharacterUi;

        public float NextPlacementStatusTime;
    }

    private sealed class SmoothedPlane
    {
        public ARPlane Source;
        public Transform Visual;
        public MeshFilter Filter;
        public MeshRenderer Renderer;

        public Vector3 ReferencePoint;
        public Vector3 Normal;
        public bool FloorInitialized;
    }

    // Initialize marker detection and floor tracking.
    private IEnumerator Start()
    {
        if (_xrOrigin == null)
            _xrOrigin = FindAnyObjectByType<XROrigin>();

        if (_xrOrigin == null || _xrOrigin.CameraFloorOffsetObject == null)
        {
            ReportTrackingError(
                "[MarkerAndAnchorTracker] XROrigin or CameraFloorOffsetObject is missing.");
            enabled = false;
            yield break;
        }
        if (_xrOrigin.Camera == null)
        {
            ReportTrackingError("[MarkerAndAnchorTracker] Camera is missing from XROrigin.");
            enabled = false;
            yield break;
        }

        if (_markerPrefab == null && _runningWorld == null)
        {
            ReportTrackingError("[MarkerAndAnchorTracker] Marker prefab is missing.");
            enabled = false;
            yield break;
        }
        if (OpenXRSettings.Instance == null)
        {
            ReportTrackingError("[MarkerAndAnchorTracker] OpenXRSettings.Instance is missing.");
            enabled = false;
            yield break;
        }

        RunningTrackStatus = "Marker-Tracking und Spatial Mapping freigeben.";
        Permissions.RequestPermissions(new[] { Permissions.MarkerTracking, Permissions.SpatialMapping },
            onPermissionDenied: permission => RunningTrackStatus = "Berechtigung verweigert: " + permission,
            onPermissionDeniedDontAskAgain: permission => RunningTrackStatus = "Berechtigung in den Geräteeinstellungen aktivieren: " + permission);
        while (!Permissions.CheckPermission(Permissions.MarkerTracking) || !Permissions.CheckPermission(Permissions.SpatialMapping))
            yield return new WaitForSecondsRealtime(.25f);

        _markerFeature = OpenXRSettings.Instance
            .GetFeature<MagicLeapMarkerUnderstandingFeature>();
        if (_markerFeature == null || !_markerFeature.enabled)
        {
            ReportTrackingError(
                "[MarkerAndAnchorTracker] Marker Understanding feature is missing or disabled.");
            enabled = false;
            yield break;
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
            ReportTrackingError("[MarkerAndAnchorTracker] AprilTag detector could not be created.");
            enabled = false;
            yield break;
        }
        InitializePlaneDetection();
        RunningTrackStatus = _planeDetectionConfigured ? "Bodenmarker scannen, dann Bodenfläche erfassen." : "Plane-Erkennung ist nicht konfiguriert.";
    }

    private void ReportTrackingError(string message)
    {
        RunningTrackStatus = message;
        Debug.LogError(message, this);
    }

    // Store detected marker poses and update the floor query.
    private void Update()
    {
        if (_markerFeature == null || _markerDetector == null || !Permissions.CheckPermission(Permissions.MarkerTracking)) return;
        UpdatePlaneQuery();
        _markerFeature.UpdateMarkerDetectors();
        if (_markerDetector.Status != MarkerDetectorStatus.Ready)
            return;

        if (_markerDetector.Data == null)
            return;

        for (int i = 0; i < _markerDetector.Data.Count; i++)
        {
            MarkerData data = _markerDetector.Data[i];
            if (!data.MarkerPose.HasValue || !data.MarkerNumber.HasValue)
                continue;

            ulong markerId = data.MarkerNumber.Value;
            Pose pose = data.MarkerPose.Value;
            if (!_markersById.TryGetValue(markerId, out MarkerState marker))
            {
                marker = new MarkerState { SessionPosition = pose.position };
                _markersById.Add(markerId, marker);
                Debug.Log($"[MarkerPlacement] Marker {markerId} detected. Waiting for a matching floor plane.");
                _planeSelectionDirty = true;
            }
            marker.SessionRotation = pose.rotation;
            marker.ObservationCameraSessionPosition = _xrOrigin.CameraFloorOffsetObject.transform
                .InverseTransformPoint(_xrOrigin.Camera.transform.position);
            marker.ReportedLength = data.MarkerLength;
            marker.LastSeenTime = Time.unscaledTime;
            marker.PoseSamples++;
            if ((marker.SessionPosition - pose.position).sqrMagnitude > 0.000025f)
            {
                marker.SessionPosition = pose.position;
                _planeSelectionDirty = true;
            }
        }
    }

    private void OnDestroy()
    {
        foreach (MarkerState marker in _markersById.Values)
            if (marker.TrackPreview != null)
                Destroy(marker.TrackPreview.gameObject);
        foreach (SmoothedPlane state in _smoothedPlanes.Values)
            if (state.Visual != null)
                Destroy(state.Visual.gameObject);
        _smoothedPlanes.Clear();
        if (_planeManager != null)
        {
            _planeManager.planesChanged -= OnPlanesChanged;
            if (_spatialMappingGranted)
                _planeManager.enabled = false;
        }
        if (_markerFeature != null)
            _markerFeature.DestroyAllMarkerDetectors();
    }

    // Find a confirmed floor, spawn the avatar and bind the shared controls.
    private void UpdateCharacterPlacements()
    {
        if (!_spatialMappingGranted || !_planeManager.enabled)
            return;

        Transform origin = _xrOrigin.CameraFloorOffsetObject.transform;
        Vector3 spawnPosition = GetCharacterSpawnPosition();
        foreach (KeyValuePair<ulong, MarkerState> entry in _markersById)
        {
            MarkerState marker = entry.Value;

            if (marker.Instance != null && marker.TrackPreview != null && marker.GroundContactReady)
            {
                UpdateRunningCharacter(marker, origin);
                continue;
            }
            Vector3 worldPosition = marker.Instance != null
                ? origin.TransformPoint(marker.PlacementSessionPosition) : spawnPosition;
            ARPlane plane = marker.Instance != null ? _planeManager.GetPlane(marker.GroundPlaneId) : null;
            if (plane == null || plane.subsumedBy != null || plane.trackingState == TrackingState.None)
                plane = FindClosestPlane(worldPosition, float.PositiveInfinity, true);

            if (plane == null && marker.Instance == null && _maxSpawnFloorAdjustment > 0f)
            {
                plane = FindNearbySpawnFloor(worldPosition, _maxSpawnFloorAdjustment,
                    out Vector3 nearbyFloorPosition);
                if (plane != null)
                {
                    float adjustment = Vector2.Distance(new Vector2(worldPosition.x, worldPosition.z),
                        new Vector2(nearbyFloorPosition.x, nearbyFloorPosition.z));
                    Debug.Log($"[MarkerPlacement] Marker {entry.Key}: requested spawn not covered. " +
                        $"Using confirmed nearby floor {adjustment:F2} m away on plane {plane.trackableId}.");
                    worldPosition = nearbyFloorPosition;
                }
            }
            if (plane == null)
            {
                if (Time.unscaledTime >= marker.NextPlacementStatusTime)
                {
                    marker.NextPlacementStatusTime = Time.unscaledTime + 5f;
                    Debug.LogWarning($"[MarkerPlacement] Marker {entry.Key}: waiting for floor at " +
                        $"target={worldPosition.ToString("F2")}, detectedPlanes={_planeManager.trackables.count}, " +
                        $"nearbyFloorLimit={_maxSpawnFloorAdjustment:F2} m, floorOnly={_markersOnFloor}. " +
                        "Scan the floor ahead. A visible marker plane does not necessarily cover the avatar target.");
                }
                continue;
            }

            Transform surface = GetPlaneSurface(plane);
            Vector3 normal = FloorPlaneGeometry.UpwardNormal(surface);

            Vector3 surfacePosition = Mathf.Abs(normal.y) >= 0.5f
                ? new Vector3(worldPosition.x, FloorPlaneGeometry.HeightAtXZ(worldPosition, surface.position, normal), worldPosition.z)
                : new Plane(normal, surface.position).ClosestPointOnPlane(worldPosition);

            if (marker.Instance == null)
            {
                if (_stadiumPreview != null && _stadiumPreview.Enabled)
                {
                    var previewObject = new GameObject($"Stadium preview {entry.Key}");
                    previewObject.layer = _markerPrefab.layer;
                    var preview = previewObject.AddComponent<StadiumTrackPreview>();

                    if (preview.Initialize(origin, surfacePosition, origin.TransformPoint(marker.SessionPosition),
                        origin.rotation * marker.SessionRotation, _stadiumPreview, out Vector3 entryPosition))
                    {
                        marker.TrackPreview = preview;
                        preview.GhostAvatarPrefab = _markerPrefab;
                        surfacePosition = entryPosition;
                        worldPosition = entryPosition;
                    }
                    else

                        Destroy(previewObject);
                }
                marker.Instance = Instantiate(_markerPrefab, surfacePosition,
                    GetUprightRotation(surfacePosition, origin.rotation * marker.SessionRotation,
                        _xrOrigin.Camera.transform.position), origin);
                AvatarVisibility.Configure(marker.Instance);
                marker.PlacementSessionPosition = origin.InverseTransformPoint(worldPosition);
                marker.SpawnFrame = Time.frameCount;
                marker.GroundContactReady = false;
                marker.Renderers = marker.Instance.GetComponentsInChildren<Renderer>(true);
                marker.Animator = marker.Instance.GetComponentInChildren<Animator>();

                marker.CharacterUi = ResolveRunnerStatsCanvas();
                if (marker.CharacterUi == null)
                    marker.CharacterUi = marker.Instance.GetComponentInChildren<UIScript>(true);
                if (marker.TrackPreview != null)
                {
                    if (marker.CharacterUi != null)
                        marker.CharacterUi.BindRunner(marker.TrackPreview, marker.Animator);
                    else
                        Debug.LogWarning("[StadiumTracking] No RunnerStatsCanvas UI found. Avatar waits for speed input.");

                    ResolveActiveStatsDisplay()?.BindRunner(marker.TrackPreview);
                }
                if (marker.Animator != null)
                {
                    marker.Animator.applyRootMotion = false;
                }

                SetRendererVisibility(marker, false);
            }

            if (marker.TrackPreview != null)
                marker.TrackPreview.AlignAvatar(marker.Instance.transform, marker.Animator, surfacePosition);
            marker.GroundPlaneId = plane.trackableId;

            if (!marker.GroundContactReady)
            {
                if (Time.frameCount <= marker.SpawnFrame)
                    continue;

                Transform root = marker.Instance.transform;
                if (!AvatarGrounding.TryGetLowestPoint(marker.Instance, out Vector3 lowestPoint))
                {
                    lowestPoint = root.position;
                    Debug.LogWarning($"[MarkerPlacement] Marker {entry.Key}: no character mesh found; using prefab pivot.");
                }
                marker.LocalGroundContact = root.InverseTransformPoint(lowestPoint);
                marker.GroundContactReady = true;
                SetRendererVisibility(marker, true);
                Debug.Log($"[MarkerPlacement] Marker {entry.Key} placed on plane {plane.trackableId}, " +
                    $"surfacePosition={surfacePosition.ToString("F3")}, " +
                    $"localSolePoint={marker.LocalGroundContact.ToString("F3")}.");
            }

            Transform instanceTransform = marker.Instance.transform;
            Vector3 contactOffset = instanceTransform.TransformVector(marker.LocalGroundContact);
            instanceTransform.position = GetGroundedPosition(surfacePosition, contactOffset,
                _characterGroundClearance);
        }
    }

    // Advance along the path and apply heading, ground height and foot offset.
    private void UpdateRunningCharacter(MarkerState marker, Transform origin)
    {
        Vector3 groundPosition = marker.TrackPreview.AdvanceRun(Time.deltaTime);

        ARPlane plane = FindClosestPlane(groundPosition, 0.5f, true);
        if (plane != null)
        {
            Transform surface = GetPlaneSurface(plane);
            Vector3 normal = surface.up;
            if (Mathf.Abs(normal.y) >= 0.5f)
                groundPosition.y = FloorPlaneGeometry.HeightAtXZ(groundPosition, surface.position, normal);
            marker.GroundPlaneId = plane.trackableId;
        }

        marker.TrackPreview.AlignAvatar(marker.Instance.transform, marker.Animator, groundPosition);
        Vector3 contactOffset = marker.Instance.transform.TransformVector(marker.LocalGroundContact);
        marker.Instance.transform.position = GetGroundedPosition(groundPosition, contactOffset, _characterGroundClearance);

        marker.PlacementSessionPosition = origin.InverseTransformPoint(groundPosition);
    }

    // Find and cache the shared runner controls in the scene.
    private UIScript ResolveRunnerStatsCanvas()
    {
        if (_runnerStatsCanvas != null)
            return _runnerStatsCanvas;

        foreach (UIScript ui in FindObjectsByType<UIScript>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
        {
            if (!ui.IsRunnerStatsCanvas)
                continue;
            _runnerStatsCanvas = ui;
            break;
        }
        return _runnerStatsCanvas;
    }

    private ActiveStatsDisplay ResolveActiveStatsDisplay()
    {
        if (_activeStatsDisplay != null)
            return _activeStatsDisplay;

        _activeStatsDisplay = FindObjectOfType<ActiveStatsDisplay>();
        return _activeStatsDisplay;
    }

    private Vector3 GetCharacterSpawnPosition()
    {
        Transform user = _xrOrigin.Camera.transform;
        return GetForwardGroundPosition(user.position, user.forward, user.right,
            _characterDistanceFromUser);
    }

    private static Vector3 GetForwardGroundPosition(Vector3 position, Vector3 forward,
        Vector3 right, float distance)
    {
        Vector3 direction = Vector3.ProjectOnPlane(forward, Vector3.up);
        if (direction.sqrMagnitude < 0.0001f)
            direction = Vector3.ProjectOnPlane(Vector3.Cross(right, Vector3.up), Vector3.up);
        if (direction.sqrMagnitude < 0.0001f)
            direction = Vector3.forward;
        return position + direction.normalized * Mathf.Max(0f, distance);
    }

    private static Vector3 GetGroundedPosition(Vector3 surfacePosition, Vector3 contactOffset,
        float clearance)
    {
        return new Vector3(surfacePosition.x, surfacePosition.y + clearance - contactOffset.y,
            surfacePosition.z);
    }

    private void OnDisable()
    {
        if (_planeManager != null && _spatialMappingGranted)
            _planeManager.enabled = false;
    }

    private static void SetRendererVisibility(MarkerState state, bool visible)
    {
        for (int i = 0; i < state.Renderers.Length; i++)
        {
            if (state.Renderers[i] != null)
                state.Renderers[i].enabled = visible;
        }
    }

    private static Quaternion GetUprightRotation(
        Vector3 spawnPosition,
        Quaternion markerRotation,
        Vector3 userPosition)
    {
        Vector3 forward = Vector3.ProjectOnPlane(userPosition - spawnPosition, Vector3.up);
        if (forward.sqrMagnitude < 0.0001f)
        {
            forward = Vector3.ProjectOnPlane(
                markerRotation * Vector3.forward,
                Vector3.up);
        }
        if (forward.sqrMagnitude < 0.0001f)
            forward = Vector3.forward;

        return Quaternion.LookRotation(forward.normalized, Vector3.up);
    }

}
