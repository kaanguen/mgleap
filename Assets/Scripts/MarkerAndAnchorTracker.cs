using System.Collections;
using System.Collections.Generic;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.OpenXR;
using MagicLeap.OpenXR.Features.MarkerUnderstanding;
using MagicLeap.OpenXR.Features.Planes;
using MagicLeap.Android;
using MagicLeap.OpenXR.Subsystems;
using UnityEngine.XR.ARSubsystems;
using UnityEngine.XR.Management;
using StadionTracking;

public class MarkerAndAnchorTracker : MonoBehaviour
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
    private int _runningWorldRevision;
    private float _runningWorldScanRequestedAt;
    private float _nextRunningPlacementDiagnostic;
    public string RunningTrackStatus { get; private set; } = "Warte auf Bodenmarker.";
    public ulong? RunningTrackMarkerId { get; private set; }
    public void BindRunningWorld(StraightTreadmillWorld world)
    {
        _runningWorld = world;
        _markersOnFloor = true;
        _runningWorldPlaced = false;
        _runningWorldRevision = world.MarkerPlacementRevision;
        _runningWorldScanRequestedAt = Time.unscaledTime;
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
        public AnimatorCullingMode OriginalCullingMode;
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
            if (_markersById.TryGetValue(markerId, out MarkerState existingMarker))
            {
                existingMarker.SessionRotation = pose.rotation;
                existingMarker.ObservationCameraSessionPosition = _xrOrigin.CameraFloorOffsetObject.transform.InverseTransformPoint(_xrOrigin.Camera.transform.position);
                existingMarker.ReportedLength = data.MarkerLength;
                existingMarker.LastSeenTime = Time.unscaledTime;
                existingMarker.PoseSamples++;

                if ((existingMarker.SessionPosition - pose.position).sqrMagnitude > 0.000025f)
                {
                    existingMarker.SessionPosition = pose.position;
                    _planeSelectionDirty = true;
                }
                continue;
            }
            var state = new MarkerState
            {
                SessionPosition = pose.position,
                SessionRotation = pose.rotation,
                ObservationCameraSessionPosition = _xrOrigin.CameraFloorOffsetObject.transform.InverseTransformPoint(_xrOrigin.Camera.transform.position),
                ReportedLength = data.MarkerLength,
                LastSeenTime = Time.unscaledTime,
                PoseSamples = 1
            };
            _markersById.Add(markerId, state);
            Debug.Log($"[MarkerPlacement] Marker {markerId} detected. Waiting for a matching floor plane.");
            _planeSelectionDirty = true;
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

    private void InitializePlaneDetection()
    {

        if (_planeManager == null)
            _planeManager = _xrOrigin.GetComponent<ARPlaneManager>();

        if (_planeManager != null)
            _planeManager.enabled = false;

        var feature = OpenXRSettings.Instance.GetFeature<MagicLeapPlanesFeature>();
        if (_planeManager == null || _planeManager.planePrefab == null ||
            feature == null || !feature.enabled)
        {
            Debug.LogError("[MarkerAndAnchorTracker] Plane detection requires an ARPlaneManager " +
                "with a plane prefab on XROrigin and the Magic Leap Planes feature enabled.");
            return;
        }

        if (!ValidatePlanePrefab())
            return;

        _planeManager.requestedDetectionMode = PlaneDetectionMode.Horizontal | PlaneDetectionMode.Vertical;

        _planeManager.planesChanged += OnPlanesChanged;
        _planeDetectionConfigured = true;
    }

    private bool ValidatePlanePrefab()
    {
        GameObject prefab = _planeManager.planePrefab;
        bool hasPlane = prefab.GetComponent<ARPlane>() != null;
        bool hasFilter = prefab.GetComponent<MeshFilter>() != null;
        bool hasVisualizer = prefab.GetComponent<ARPlaneMeshVisualizer>() != null;
        var renderer = prefab.GetComponent<MeshRenderer>();
        Material material = renderer != null ? renderer.sharedMaterial : null;
        bool hasMaterial = material != null && material.shader != null;
        bool cameraRendersLayer = (_xrOrigin.Camera.cullingMask & (1 << prefab.layer)) != 0;

        Debug.Log($"[MarkerPlanes] Prefab={prefab.name}, active={prefab.activeSelf}, " +
            $"ARPlane={hasPlane}, MeshFilter={hasFilter}, MeshRenderer={renderer != null}, " +
            $"ARPlaneMeshVisualizer={hasVisualizer}, material={(material != null ? material.name : "MISSING")}, " +
            $"shader={(hasMaterial ? material.shader.name : "MISSING")}, " +
            $"shaderSupported={(hasMaterial && material.shader.isSupported)}, " +
            $"layer={prefab.layer}, cameraRendersLayer={cameraRendersLayer}.");

        if (!hasPlane || !hasFilter || !hasVisualizer || renderer == null || !hasMaterial ||
            !prefab.activeSelf || !cameraRendersLayer || !material.shader.isSupported)
        {
            Debug.LogError("[MarkerPlanes] Invalid plane prefab. Its root needs ARPlane, " +
                "ARPlaneMeshVisualizer, MeshFilter, MeshRenderer and a supported material. " +
                "It must be active and on a layer rendered by the XR camera.");
            return false;
        }
        return true;
    }

    // Search for native floor surfaces around markers and avatars.
    private void UpdatePlaneQuery()
    {
        if (!_planeDetectionConfigured || _planeManager == null || _markersById.Count == 0)
            return;

        _spatialMappingGranted = Permissions.CheckPermission(Permissions.SpatialMapping);
        if (!_spatialMappingGranted)
        {
            _planeManager.enabled = false;
            ReportPlaneStatus("Spatial Mapping permission is not granted on the device.");
            return;
        }

        var loader = XRGeneralSettings.Instance?.Manager?.activeLoader;
        if (!(loader?.GetLoadedSubsystem<XRPlaneSubsystem>() is MLXrPlaneSubsystem))
        {
            ReportPlaneStatus("Waiting for the Magic Leap OpenXR plane subsystem.");
            return;
        }

        bool first = true;
        var bounds = new Bounds();
        Transform origin = _xrOrigin.CameraFloorOffsetObject.transform;
        Vector3 spawnTarget = origin.InverseTransformPoint(GetCharacterSpawnPosition());
        foreach (MarkerState marker in _markersById.Values)
        {
            if (first)
            {
                bounds = new Bounds(marker.SessionPosition, Vector3.zero);
                first = false;
            }
            else
                bounds.Encapsulate(marker.SessionPosition);
            bounds.Encapsulate(marker.Instance != null ? marker.PlacementSessionPosition : spawnTarget);
        }

        MLXrPlaneSubsystem.Query = new MLXrPlaneSubsystem.PlanesQuery
        {
            Flags = _planeManager.requestedDetectionMode.ToMLXrQueryFlags() |
                MLXrPlaneSubsystem.MLPlanesQueryFlags.SemanticAll,
            BoundsCenter = bounds.center,
            BoundsRotation = Quaternion.identity,

            BoundsExtents = bounds.extents + (_markersOnFloor
                ? new Vector3(_floorSearchExtent, _floorSearchVerticalExtent, _floorSearchExtent)
                : Vector3.one * _planeQueryPadding),
            MaxResults = (uint)Mathf.Max(1, _maxPlaneResults),
            MinPlaneArea = Mathf.Max(0.04f, _minPlaneArea)
        };

        _planeManager.enabled = true;
    }

    private void OnPlanesChanged(ARPlanesChangedEventArgs changes)
    {
        _planeSelectionDirty = true;
    }

    // Refresh floor visuals and apply avatar placement after tracking updates.
    private void LateUpdate()
    {

        if (!_planeDetectionConfigured || _planeManager == null)
            return;

        if (_planeSelectionDirty)
        {
            _planeSelectionDirty = false;
            RefreshPlaneSelection();
        }

        int renderedPlanes = 0;
        Transform origin = _xrOrigin.CameraFloorOffsetObject.transform;
        foreach (ARPlane plane in _planeManager.trackables)
        {
            var meshFilter = plane.GetComponent<MeshFilter>();
            bool visible = _planeManager.enabled && _spatialMappingGranted &&
                _visiblePlaneIds.Contains(plane.trackableId) &&
                plane.subsumedBy == null && plane.trackingState != TrackingState.None;
            bool hasMesh = meshFilter != null && meshFilter.sharedMesh != null &&
                meshFilter.sharedMesh.vertexCount >= 3;
            var meshRenderer = plane.GetComponent<MeshRenderer>();
            if (meshRenderer != null)
            {
                SmoothedPlane smoothed = UpdateSmoothedPlane(plane, meshFilter, meshRenderer,
                    origin, visible && hasMesh);

                meshRenderer.enabled = false;
                smoothed.Renderer.enabled = visible && hasMesh;
                if (smoothed.Renderer.enabled)
                    renderedPlanes++;
            }
            var outline = plane.GetComponent<LineRenderer>();
            if (outline != null)
            {
                outline.enabled = visible && outline.positionCount >= 3;
                UpdateSmoothedOutline(plane, outline);
            }
            var collider = plane.GetComponent<MeshCollider>();
            if (collider != null)
                collider.enabled = visible && hasMesh;
        }

        _removedPlaneIds.Clear();
        foreach (var entry in _smoothedPlanes)
            if (entry.Value.Source == null)
                _removedPlaneIds.Add(entry.Key);
        foreach (TrackableId id in _removedPlaneIds)
            _smoothedPlanes.Remove(id);

        if (_runningWorld != null) UpdateRunningTrackPlacement();
        else UpdateCharacterPlacements();

        if (_markersById.Count > 0 && _planeManager.enabled && _spatialMappingGranted &&
            Time.unscaledTime >= _nextPlaneStatusTime)
            ReportPlaneStatus($"Detected={_planeManager.trackables.count}, " +
                $"matched={_visiblePlaneIds.Count}, rendered={renderedPlanes}, " +
                $"ARSession={ARSession.state}.");

        if (_logPlaneDiagnostics && _markersById.Count > 0 &&
            Time.unscaledTime >= _nextPlaneDiagnosticsTime)
        {
            _nextPlaneDiagnosticsTime = Time.unscaledTime + Mathf.Max(1f, _planeDiagnosticsInterval);
            LogPlaneDiagnostics();
        }
    }

    // Smooth floor height and tilt without changing the native trackable.
    private SmoothedPlane UpdateSmoothedPlane(ARPlane plane, MeshFilter filter,
        MeshRenderer sourceRenderer, Transform origin, bool visible)
    {
        Vector3 position = origin.InverseTransformPoint(plane.transform.position);
        Vector3 normal = origin.InverseTransformDirection(plane.transform.up).normalized;
        if (normal.y < 0f)
            normal = -normal;

        if (!_smoothedPlanes.TryGetValue(plane.trackableId, out SmoothedPlane state))
        {
            var visual = new GameObject("Smoothed plane visual");
            visual.layer = plane.gameObject.layer;
            visual.transform.SetParent(plane.transform, false);
            state = new SmoothedPlane
            {
                Source = plane,
                Visual = visual.transform,
                Filter = visual.AddComponent<MeshFilter>(),
                Renderer = visual.AddComponent<MeshRenderer>()
            };
            state.Renderer.shadowCastingMode = sourceRenderer.shadowCastingMode;
            state.Renderer.receiveShadows = sourceRenderer.receiveShadows;
            state.Renderer.renderingLayerMask = sourceRenderer.renderingLayerMask;
            state.Renderer.sortingLayerID = sourceRenderer.sortingLayerID;
            state.Renderer.sortingOrder = sourceRenderer.sortingOrder;
            _smoothedPlanes.Add(plane.trackableId, state);
        }

        Mesh mesh = filter != null ? filter.sharedMesh : null;
        if (state.Filter.sharedMesh != mesh)
            state.Filter.sharedMesh = mesh;
        if (state.Renderer.sharedMaterial != sourceRenderer.sharedMaterial)
            state.Renderer.sharedMaterial = sourceRenderer.sharedMaterial;
        bool floor = IsPlaneTypeAllowed(plane) && normal.y >= 0.5f;
        if (visible && floor)
        {

            if (!state.FloorInitialized)
            {
                state.ReferencePoint = position;
                state.Normal = normal;
                state.FloorInitialized = true;
            }

            float targetHeight = GetPlaneHeightAtXZ(state.ReferencePoint, position, normal);
            float blend = PlaneSmoothingBlend(Time.unscaledDeltaTime, _planeSmoothingTime);
            state.ReferencePoint.y = Mathf.Lerp(state.ReferencePoint.y, targetHeight, blend);
            state.Normal = Vector3.Lerp(state.Normal, normal, blend).normalized;
        }

        if (floor && state.FloorInitialized)
        {
            position.y = GetPlaneHeightAtXZ(position, state.ReferencePoint, state.Normal);
            state.Visual.SetPositionAndRotation(origin.TransformPoint(position),
                Quaternion.FromToRotation(origin.TransformDirection(normal),
                    origin.TransformDirection(state.Normal)) * plane.transform.rotation);
        }
        else
            state.Visual.SetPositionAndRotation(plane.transform.position, plane.transform.rotation);

        return state;
    }

    private void UpdateSmoothedOutline(ARPlane plane, LineRenderer outline)
    {
        if (!_smoothedPlanes.TryGetValue(plane.trackableId, out SmoothedPlane state) ||
            !plane.boundary.IsCreated)
            return;

        outline.positionCount = plane.boundary.Length;
        for (int i = 0; i < plane.boundary.Length; i++)
        {
            Vector2 point = plane.boundary[i];
            Vector3 worldPoint = state.Visual.TransformPoint(new Vector3(point.x, 0f, point.y));
            outline.SetPosition(i, outline.useWorldSpace ? worldPoint :
                plane.transform.InverseTransformPoint(worldPoint));
        }
    }

    private static float PlaneSmoothingBlend(float deltaTime, float responseTime)
    {

        return responseTime <= 0f ? 1f : 1f - Mathf.Exp(-Mathf.Max(0f, deltaTime) / responseTime);
    }

    private static float GetPlaneHeightAtXZ(Vector3 point, Vector3 planePoint, Vector3 normal)
    {
        return planePoint.y - (normal.x * (point.x - planePoint.x) +
            normal.z * (point.z - planePoint.z)) / normal.y;
    }

    // One shared track/companion replaces the legacy per-marker avatar path in the run scene.
    private void UpdateRunningTrackPlacement()
    {
        if (_runningWorldRevision != _runningWorld.MarkerPlacementRevision)
        {
            _runningWorldRevision = _runningWorld.MarkerPlacementRevision;
            _runningWorldPlaced = false;
            RunningTrackMarkerId = null;
            _runningWorldScanRequestedAt = Time.unscaledTime;
            foreach (var previous in _markersById.Values) previous.PoseSamples = 0;
        }
        if (_runningWorldPlaced) return; // Looking away must never despawn or reposition the track.
        if (!_spatialMappingGranted || !_planeManager.enabled)
        { RunningTrackStatus = "Warte auf Spatial Mapping und Plane-Erkennung."; return; }
        Transform origin = _xrOrigin.CameraFloorOffsetObject.transform;
        string waitingReason = _markersById.Count == 0 ? "Bodenmarker scannen." : "Marker kurz ruhig erfassen.";
        foreach (var entry in _markersById)
        {
            var marker = entry.Value;
            if (!FloorMarkerPlacement.CanUseScan(marker.PoseSamples, marker.LastSeenTime, _runningWorldScanRequestedAt))
            { waitingReason = $"Marker {entry.Key}: {marker.PoseSamples}/3 Messungen. Marker kurz ruhig scannen."; continue; }
            Vector3 markerWorld = origin.TransformPoint(marker.SessionPosition);
            Vector3 placementPoint = markerWorld;
            var plane = FindClosestPlane(markerWorld, _maxMarkerPlaneDistance, true);
            bool correctedDepth = false;
            if (plane == null)
            {
                plane = FindFloorAlongMarkerRay(marker, origin, markerWorld, out placementPoint);
                correctedDepth = plane != null;
            }
            if (plane == null)
            { waitingReason = DescribeMissingMarkerFloor(entry.Key, markerWorld); continue; }
            if (plane.trackingState != TrackingState.Tracking || plane.classification != PlaneClassification.Floor)
            { waitingReason = $"Marker {entry.Key} gespeichert; Bodenfläche noch nicht stabil getrackt ({plane.trackingState}, {plane.classification})."; continue; }
            Transform surface = _smoothedPlanes.TryGetValue(plane.trackableId, out var smoothed)
                ? smoothed.Visual : plane.transform;
            Vector3 normal = surface.up;
            if (normal.y < 0) normal = -normal;
            if (normal.y < .9659f) { waitingReason = "Erkannte Fläche ist stärker als 15 Grad geneigt."; continue; }
            if (!FloorMarkerPlacement.TryProject(placementPoint.x, placementPoint.y, placementPoint.z,
                surface.position.x, surface.position.y, surface.position.z, normal.x, normal.y, normal.z,
                _maxMarkerPlaneDistance, out double floorY))
            { waitingReason = $"Marker {entry.Key}: Abstand zur geglätteten Bodenfläche noch zu groß."; continue; }
            Vector3 ground = new Vector3(placementPoint.x, (float)floorY, placementPoint.z);
            Quaternion markerRotation = origin.rotation * marker.SessionRotation;
            Vector3 localDirection = _stadiumPreview != null ? _stadiumPreview.MarkerLocalRunDirection : Vector3.up;
            Vector3 forward = Vector3.ProjectOnPlane(markerRotation * localDirection, normal);
            if (forward.sqrMagnitude < .001f) forward = Vector3.ProjectOnPlane(markerRotation * Vector3.right, normal);
            if (forward.sqrMagnitude < .001f) { waitingReason = "Markerrichtung noch nicht bestimmbar."; continue; }
            if (!_runningWorld.PlaceFromMarkerPlane(ground, forward.normalized, normal))
            { waitingReason = "Boden und Marker bestätigt; warte auf Headtracking."; continue; }
            _runningWorldPlaced = true;
            RunningTrackMarkerId = entry.Key;
            RunningTrackStatus = $"Marker {entry.Key} · Bodenfläche bestätigt · Bahn platziert.";
            Debug.Log($"[RunningTrack] Marker {entry.Key} placed track and avatar on floor {plane.trackableId} at {ground}; floorRayCorrection={correctedDepth}, markerPose={markerWorld}.");
            return;
        }
        RunningTrackStatus = waitingReason;
        if (_markersById.Count > 0 && Time.unscaledTime >= _nextRunningPlacementDiagnostic)
        {
            _nextRunningPlacementDiagnostic = Time.unscaledTime + 5;
            Debug.Log("[RunningTrack] " + waitingReason, this);
        }
    }

    private ARPlane FindFloorAlongMarkerRay(MarkerState marker, Transform origin, Vector3 markerWorld, out Vector3 hit)
    {
        hit = Vector3.zero;
        ARPlane selected = null;
        float nearest = float.PositiveInfinity;
        Vector3 camera = origin.TransformPoint(marker.ObservationCameraSessionPosition);
        Vector3 markerNormal = origin.rotation * marker.SessionRotation * Vector3.forward;
        foreach (var plane in _planeManager.trackables)
        {
            if (plane.subsumedBy != null || plane.trackingState != TrackingState.Tracking || plane.classification != PlaneClassification.Floor) continue;
            Transform surface = _smoothedPlanes.TryGetValue(plane.trackableId, out var smooth) ? smooth.Visual : plane.transform;
            Vector3 normal = surface.up;
            if (normal.y < 0) normal = -normal;
            // Depth correction is only valid for a tag lying flat on the detected floor.
            if (Mathf.Abs(Vector3.Dot(markerNormal.normalized, normal.normalized)) < .9f) continue;
            Vector3 p = surface.position;
            if (!FloorMarkerPlacement.TryIntersectFloor(camera.x,camera.y,camera.z, markerWorld.x,markerWorld.y,markerWorld.z,
                p.x,p.y,p.z,normal.x,normal.y,normal.z,10,out double x,out double y,out double z)) continue;
            Vector3 candidate = new Vector3((float)x,(float)y,(float)z);
            Vector3 local = plane.transform.InverseTransformPoint(candidate);
            if (!ContainsPoint(plane,new Vector2(local.x,local.z),_planeBoundaryTolerance)) continue;
            float distance = (candidate-camera).sqrMagnitude;
            if (distance >= nearest) continue;
            nearest = distance; selected = plane; hit = candidate;
        }
        return selected;
    }

    private string DescribeMissingMarkerFloor(ulong markerId, Vector3 markerWorld)
    {
        float closestDistance = float.PositiveInfinity;
        int floors = 0;
        foreach (var floor in _planeManager.trackables)
        {
            if (floor.subsumedBy != null || floor.trackingState == TrackingState.None || !IsPlaneTypeAllowed(floor)) continue;
            floors++;
            closestDistance = Mathf.Min(closestDistance, Mathf.Abs(Vector3.Dot(markerWorld - floor.transform.position, floor.transform.up)));
        }
        if (floors == 0) return $"Marker {markerId} gespeichert; warte auf erkannte Floor-Plane (Flächen gesamt: {_planeManager.trackables.count}).";
        if (closestDistance > _maxMarkerPlaneDistance)
            return $"Marker {markerId} gespeichert; Bodenabstand {closestDistance * 100:F1} cm, erlaubt {_maxMarkerPlaneDistance * 100:F1} cm. Markergröße prüfen.";
        return $"Marker {markerId} gespeichert; Bodenpolygon am Marker noch unvollständig. Boden um den Marker weiter scannen.";
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

            Transform surface = _smoothedPlanes.TryGetValue(plane.trackableId, out SmoothedPlane smoothed)
                ? smoothed.Visual : plane.transform;
            Vector3 normal = surface.up;
            if (Vector3.Dot(normal, Vector3.up) < 0f)
                normal = -normal;

            Vector3 surfacePosition = Mathf.Abs(normal.y) >= 0.5f
                ? new Vector3(worldPosition.x, GetPlaneHeightAtXZ(worldPosition, surface.position, normal), worldPosition.z)
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
                    marker.OriginalCullingMode = marker.Animator.cullingMode;
                    marker.Animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
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
                if (!TryGetLowestCharacterPoint(marker, out Vector3 lowestPoint))
                {
                    lowestPoint = root.position;
                    Debug.LogWarning($"[MarkerPlacement] Marker {entry.Key}: no character mesh found; using prefab pivot.");
                }
                marker.LocalGroundContact = root.InverseTransformPoint(lowestPoint);
                marker.GroundContactReady = true;
                if (marker.Animator != null)
                    marker.Animator.cullingMode = marker.OriginalCullingMode;
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
            Transform surface = _smoothedPlanes.TryGetValue(plane.trackableId, out SmoothedPlane smoothed)
                ? smoothed.Visual : plane.transform;
            Vector3 normal = surface.up;
            if (Mathf.Abs(normal.y) >= 0.5f)
                groundPosition.y = GetPlaneHeightAtXZ(groundPosition, surface.position, normal);
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

    // Find a valid surface covering the requested position.
    private ARPlane FindClosestPlane(Vector3 worldPosition, float maxDistance, bool requireHorizontal)
    {
        ARPlane closest = null;
        float closestDistance = maxDistance;
        foreach (ARPlane plane in _planeManager.trackables)
        {
            if (plane.subsumedBy != null || plane.trackingState == TrackingState.None || !IsPlaneTypeAllowed(plane))
                continue;
            if (requireHorizontal && Mathf.Abs(Vector3.Dot(plane.transform.up, Vector3.up)) < 0.5f)
                continue;
            Vector3 local = plane.transform.InverseTransformPoint(worldPosition);
            float distance = Mathf.Abs(Vector3.Dot(worldPosition - plane.transform.position, plane.transform.up));
            if (distance > closestDistance ||
                !ContainsPoint(plane, new Vector2(local.x, local.z), _planeBoundaryTolerance))
                continue;
            closest = plane;
            closestDistance = distance;
        }
        return closest;
    }

    // Allow initial placement on a nearby confirmed floor within the adjustment limit.
    private ARPlane FindNearbySpawnFloor(Vector3 target, float maxAdjustment, out Vector3 floorPosition)
    {
        floorPosition = target;
        ARPlane bestPlane = null;
        float bestDistanceSquared = maxAdjustment * maxAdjustment;
        foreach (ARPlane plane in _planeManager.trackables)
        {
            if (plane.subsumedBy != null || plane.trackingState == TrackingState.None ||
                plane.classification != PlaneClassification.Floor ||
                (plane.alignment != PlaneAlignment.HorizontalUp && plane.alignment != PlaneAlignment.HorizontalDown) ||
                Mathf.Abs(Vector3.Dot(plane.transform.up, Vector3.up)) < 0.5f)
                continue;

            var boundary = plane.boundary;
            if (!boundary.IsCreated || boundary.Length < 3)
                continue;

            for (int i = 0, j = boundary.Length - 1; i < boundary.Length; j = i++)
            {
                Vector3 a = plane.transform.TransformPoint(new Vector3(boundary[j].x, 0f, boundary[j].y));
                Vector3 b = plane.transform.TransformPoint(new Vector3(boundary[i].x, 0f, boundary[i].y));
                Vector2 start = new Vector2(a.x, a.z);
                Vector2 edge = new Vector2(b.x - a.x, b.z - a.z);
                Vector2 toTarget = new Vector2(target.x - a.x, target.z - a.z);
                float t = edge.sqrMagnitude > 0f ? Mathf.Clamp01(Vector2.Dot(toTarget, edge) / edge.sqrMagnitude) : 0f;
                Vector3 candidate = Vector3.Lerp(a, b, t);
                float distanceSquared = (toTarget - edge * t).sqrMagnitude;
                if (distanceSquared > bestDistanceSquared)
                    continue;
                bestDistanceSquared = distanceSquared;
                bestPlane = plane;
                floorPosition = candidate;
            }
        }
        return bestPlane;
    }

    // Measure the posed character mesh to place its feet on the floor.
    private static bool TryGetLowestCharacterPoint(MarkerState marker, out Vector3 lowestPoint)
    {
        lowestPoint = Vector3.zero;
        float lowestHeight = float.PositiveInfinity;
        bool found = false;
        var bakedMesh = new Mesh();
        var vertices = new List<Vector3>();
        try
        {

            foreach (SkinnedMeshRenderer skin in marker.Instance.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                if (skin.sharedMesh == null)
                    continue;
                skin.BakeMesh(bakedMesh);
                bakedMesh.GetVertices(vertices);
                foreach (Vector3 vertex in vertices)
                {
                    Vector3 point = skin.transform.TransformPoint(vertex);
                    float height = point.y;
                    if (height >= lowestHeight)
                        continue;
                    lowestHeight = height;
                    lowestPoint = point;
                    found = true;
                }
            }
        }
        finally
        {
            Destroy(bakedMesh);
        }
        if (found)
            return true;

        foreach (Renderer renderer in marker.Renderers)
        {
            if (!(renderer is MeshRenderer) || renderer.GetComponentInParent<Canvas>() != null ||
                renderer.GetComponent<TMPro.TMP_Text>() != null)
                continue;
            var filter = renderer.GetComponent<MeshFilter>();
            if (filter == null || filter.sharedMesh == null)
                continue;
            Bounds bounds = filter.sharedMesh.bounds;
            for (int i = 0; i < 8; i++)
            {
                Vector3 corner = bounds.center + Vector3.Scale(bounds.extents,
                    new Vector3((i & 1) == 0 ? -1f : 1f, (i & 2) == 0 ? -1f : 1f,
                        (i & 4) == 0 ? -1f : 1f));
                Vector3 point = renderer.transform.TransformPoint(corner);
                float height = point.y;
                if (height >= lowestHeight)
                    continue;
                lowestHeight = height;
                lowestPoint = point;
                found = true;
            }
        }
        return found;
    }

    private void LogPlaneDiagnostics()
    {
        Transform origin = _xrOrigin.CameraFloorOffsetObject.transform;
        Debug.Log($"[MarkerPlaneDebug] markers={_markersById.Count}, " +
            $"planes={_planeManager.trackables.count}, managerEnabled={_planeManager.enabled}, " +
            $"subsystemRunning={(_planeManager.subsystem != null && _planeManager.subsystem.running)}, " +
            $"permission={_spatialMappingGranted}, maxDistance={_maxMarkerPlaneDistance:F3}m, " +
            $"floorMode={_markersOnFloor}, " +
            $"queryExtents={MLXrPlaneSubsystem.Query.BoundsExtents.ToString("F2")}, " +
            $"edgeTolerance={_planeBoundaryTolerance:F3}m, " +
            $"originPosition={origin.position.ToString("F3")}, originScale={origin.lossyScale.ToString("F3")}, " +
            $"cameraWorldPosition={_xrOrigin.Camera.transform.position.ToString("F3")}, " +
            $"configuredTagLength={_aprilTagLength:F3}m, tagDictionary={_aprilTagType}.");

        int pairs = 0;
        foreach (KeyValuePair<ulong, MarkerState> entry in _markersById)
        {
            Vector3 worldPosition = origin.TransformPoint(entry.Value.SessionPosition);
            Debug.Log($"[MarkerPlaneDebug] marker={entry.Key}, " +
                $"sessionPosition={entry.Value.SessionPosition.ToString("F3")}, " +
                $"worldPosition={worldPosition.ToString("F3")}, " +
                $"poseAge={Time.unscaledTime - entry.Value.LastSeenTime:F2}s, samples={entry.Value.PoseSamples}, " +
                $"reportedTagLength={entry.Value.ReportedLength:F3}m, " +
                $"markerNormal={(origin.rotation * entry.Value.SessionRotation * Vector3.forward).ToString("F3")}.");

            foreach (ARPlane plane in _planeManager.trackables)
            {
                if (pairs++ >= Mathf.Max(1, _maxPlaneDiagnosticPairs))
                {
                    Debug.Log("[MarkerPlaneDebug] Pair limit reached; increase Max Plane Diagnostic Pairs to see more.");
                    return;
                }

                Vector3 local = plane.transform.InverseTransformPoint(worldPosition);
                float distance = Mathf.Abs(Vector3.Dot(worldPosition - plane.transform.position,
                    plane.transform.up));
                int boundaryCount = plane.boundary.IsCreated ? plane.boundary.Length : 0;
                bool inside = ContainsPoint(plane, new Vector2(local.x, local.z), _planeBoundaryTolerance);
                string reason = plane.subsumedBy != null ? "REJECT_MERGED" :
                    plane.trackingState == TrackingState.None ? "REJECT_NO_TRACKING" :
                    !IsPlaneTypeAllowed(plane) ? "REJECT_NOT_FLOOR" :
                    _markersOnFloor && boundaryCount >= 3 ? "ELIGIBLE_ROOM_FLOOR" :
                    !_markersOnFloor && distance > _maxMarkerPlaneDistance ? "REJECT_DISTANCE" :
                    boundaryCount < 3 ? "REJECT_MISSING_BOUNDARY" :
                    !inside ? "REJECT_OUTSIDE_BOUNDARY" :
                    "ELIGIBLE";
                Vector3 projectedPosition = worldPosition - plane.transform.up *
                    Vector3.Dot(worldPosition - plane.transform.position, plane.transform.up);

                var filter = plane.GetComponent<MeshFilter>();
                var renderer = plane.GetComponent<MeshRenderer>();
                if (_smoothedPlanes.TryGetValue(plane.trackableId, out SmoothedPlane displayed))
                    renderer = displayed.Renderer;
                var visualizer = plane.GetComponent<ARPlaneMeshVisualizer>();
                Material material = renderer != null ? renderer.sharedMaterial : null;
                int vertexCount = filter != null && filter.sharedMesh != null
                    ? filter.sharedMesh.vertexCount : 0;
                int indexCount = filter != null && filter.sharedMesh != null && filter.sharedMesh.subMeshCount > 0
                    ? (int)filter.sharedMesh.GetIndexCount(0) : 0;

                Debug.Log($"[MarkerPlaneDebug] marker={entry.Key}, plane={plane.trackableId}, " +
                    $"result={reason}, distance={distance:F3}m, insideBoundary={inside}, " +
                    $"projectedWorldPosition={projectedPosition.ToString("F3")}, " +
                    $"tracking={plane.trackingState}, alignment={plane.alignment}, classification={plane.classification}, " +
                    $"boundaryPoints={boundaryCount}, " +
                    $"localMarker={local.ToString("F3")}, planeCenter={plane.center.ToString("F3")}, " +
                    $"planeSize={plane.size.ToString("F3")}, planeWorldPosition={plane.transform.position.ToString("F3")}, " +
                    $"planeNormal={plane.transform.up.ToString("F3")}, " +
                    $"selectedForAnyMarker={_visiblePlaneIds.Contains(plane.trackableId)}, " +
                    $"active={plane.gameObject.activeInHierarchy}, visualizerEnabled={(visualizer != null && visualizer.enabled)}, " +
                    $"rendererEnabled={(renderer != null && renderer.enabled)}, meshVertices={vertexCount}, " +
                    $"meshIndices={indexCount}, material={(material != null ? material.name : "MISSING")}, " +
                    $"shaderSupported={(material != null && material.shader != null && material.shader.isSupported)}, " +
                    $"cameraRendersLayer={((_xrOrigin.Camera.cullingMask & (1 << plane.gameObject.layer)) != 0)}.");
            }
        }
    }

    private void ReportPlaneStatus(string status)
    {

        if (Time.unscaledTime < _nextPlaneStatusTime)
            return;

        _nextPlaneStatusTime = Time.unscaledTime + 5f;
        if (status == _lastPlaneStatus)
            return;

        _lastPlaneStatus = status;
        Debug.Log($"[MarkerPlanes] {status}");
    }

    private void RefreshPlaneSelection()
    {

        _visiblePlaneIds.Clear();
        if (_markersOnFloor)
        {
            foreach (ARPlane plane in _planeManager.trackables)
            {
                if (plane.subsumedBy == null && plane.trackingState != TrackingState.None &&
                    IsPlaneTypeAllowed(plane) && plane.boundary.IsCreated && plane.boundary.Length >= 3)
                    _visiblePlaneIds.Add(plane.trackableId);
            }
        }
        else
        {
            SelectPlanesAtMarkers();
        }

        foreach (ARPlane plane in _planeManager.trackables)
        {
            bool visible = _visiblePlaneIds.Contains(plane.trackableId);

            var visualizer = plane.GetComponent<ARPlaneMeshVisualizer>();
            if (visualizer != null)
                visualizer.enabled = visible;
        }
    }

    private void SelectPlanesAtMarkers()
    {

        Transform sessionOrigin = _xrOrigin.CameraFloorOffsetObject.transform;
        foreach (MarkerState marker in _markersById.Values)
        {

            Vector3 worldPosition = sessionOrigin.TransformPoint(marker.SessionPosition);
            ARPlane closestPlane = FindClosestPlane(worldPosition, _maxMarkerPlaneDistance, false);
            if (closestPlane != null)
                _visiblePlaneIds.Add(closestPlane.trackableId);
        }
    }

    private bool IsPlaneTypeAllowed(ARPlane plane)
    {

        return !_markersOnFloor ||
            (plane.classification == PlaneClassification.Floor &&
                (plane.alignment == PlaneAlignment.HorizontalUp ||
                 plane.alignment == PlaneAlignment.HorizontalDown));
    }

    private static bool ContainsPoint(ARPlane plane, Vector2 point, float edgeTolerance)
    {

        var boundary = plane.boundary;
        if (!boundary.IsCreated || boundary.Length < 3)
            return false;

        bool inside = false;
        for (int i = 0, j = boundary.Length - 1; i < boundary.Length; j = i++)
        {
            Vector2 a = boundary[j];
            Vector2 b = boundary[i];

            Vector2 edge = b - a;
            float t = edge.sqrMagnitude > 0f
                ? Mathf.Clamp01(Vector2.Dot(point - a, edge) / edge.sqrMagnitude) : 0f;
            Vector2 offset = point - (a + edge * t);
            Vector3 worldOffset = plane.transform.TransformVector(new Vector3(offset.x, 0f, offset.y));
            if (worldOffset.sqrMagnitude <= edgeTolerance * edgeTolerance)
                return true;

            if ((a.y > point.y) != (b.y > point.y) &&
                point.x < (b.x - a.x) * (point.y - a.y) / (b.y - a.y) + a.x)
                inside = !inside;
        }
        return inside;
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
