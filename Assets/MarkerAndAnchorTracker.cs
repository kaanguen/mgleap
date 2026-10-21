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
    [SerializeField, Tooltip("Optional gap between the character's soles and the detected surface, in meters.")]
    private float _characterGroundClearance = 0f;
    [Header("Plane diagnostics")]
    [SerializeField] private bool _logPlaneDiagnostics = true;
    [SerializeField, Min(1f)] private float _planeDiagnosticsInterval = 5f;
    [SerializeField, Min(1)] private int _maxPlaneDiagnosticPairs = 30;
    private readonly Dictionary<ulong, MarkerState> _markersById = new();
    private readonly HashSet<TrackableId> _visiblePlaneIds = new();
    private bool _spatialMappingGranted;
    private bool _planeSelectionDirty;
    private bool _planeDetectionConfigured;
    private float _nextPlaneStatusTime;
    private string _lastPlaneStatus;
    private float _nextPlaneDiagnosticsTime;

    private MagicLeapMarkerUnderstandingFeature _markerFeature;
    private MarkerDetector _markerDetector;

    private sealed class MarkerState
    {
        public Vector3 SessionPosition;
        public Quaternion SessionRotation;
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
    }

    private void Start()
    {
        if (_xrOrigin == null)
            _xrOrigin = FindAnyObjectByType<XROrigin>();


        if (_xrOrigin == null || _xrOrigin.CameraFloorOffsetObject == null)
        {
            Debug.LogError(
                "[MarkerAndAnchorTracker] XROrigin or CameraFloorOffsetObject is missing.");
            enabled = false;
            return;
        }
        if (_xrOrigin.Camera == null)
        {
            Debug.LogError("[MarkerAndAnchorTracker] Camera is missing from XROrigin.");
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
            return;
        }
        InitializePlaneDetection();
    }

    private void Update()
    {
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
                existingMarker.ReportedLength = data.MarkerLength;
                existingMarker.LastSeenTime = Time.unscaledTime;
                existingMarker.PoseSamples++;
                // Refine the surface match without spawning another marker object.
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
                ReportedLength = data.MarkerLength,
                LastSeenTime = Time.unscaledTime,
                PoseSamples = 1
            };
            _markersById.Add(markerId, state);
            _planeSelectionDirty = true;
        }
    }

    private void OnDestroy()
    {
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
        // ARPlaneManager manages planes reported by the native subsystem.
        if (_planeManager == null)
            _planeManager = _xrOrigin.GetComponent<ARPlaneManager>();
        // Start scanning only after a marker has been stored.
        if (_planeManager != null)
            _planeManager.enabled = false;

        // Check that the Magic Leap plane provider is enabled.
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

        // Request horizontal and vertical surfaces.
        _planeManager.requestedDetectionMode = PlaneDetectionMode.Horizontal | PlaneDetectionMode.Vertical;
        // Process new, updated, or removed planes.
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

    private void UpdatePlaneQuery()
    {
        if (!_planeDetectionConfigured || _planeManager == null || _markersById.Count == 0)
            return;

        // Permission is granted externally; never open a permission prompt here.
        _spatialMappingGranted = Permissions.CheckPermission(Permissions.SpatialMapping);
        if (!_spatialMappingGranted)
        {
            _planeManager.enabled = false;
            ReportPlaneStatus("Spatial Mapping permission is not granted on the device.");
            return;
        }

        // Wait until the Magic Leap plane subsystem is loaded.
        var loader = XRGeneralSettings.Instance?.Manager?.activeLoader;
        if (!(loader?.GetLoadedSubsystem<XRPlaneSubsystem>() is MLXrPlaneSubsystem))
        {
            ReportPlaneStatus("Waiting for the Magic Leap OpenXR plane subsystem.");
            return;
        }

        // Build one search box around all markers in OpenXR session space.
        bool first = true;
        var bounds = new Bounds();
        foreach (MarkerState marker in _markersById.Values)
        {
            if (first)
            {
                bounds = new Bounds(marker.SessionPosition, Vector3.zero);
                first = false;
            }
            else
                bounds.Encapsulate(marker.SessionPosition);
        }

        // Configure native detection; this script does not detect surfaces itself.
        MLXrPlaneSubsystem.Query = new MLXrPlaneSubsystem.PlanesQuery
        {
            Flags = _planeManager.requestedDetectionMode.ToMLXrQueryFlags() |
                MLXrPlaneSubsystem.MLPlanesQueryFlags.SemanticAll,
            BoundsCenter = bounds.center,
            BoundsRotation = Quaternion.identity,
            // Room-scale floor scanning must extend beyond the marker neighborhood.
            BoundsExtents = bounds.extents + (_markersOnFloor
                ? new Vector3(_floorSearchExtent, _floorSearchVerticalExtent, _floorSearchExtent)
                : Vector3.one * _planeQueryPadding),
            MaxResults = (uint)Mathf.Max(1, _maxPlaneResults),
            MinPlaneArea = Mathf.Max(0.04f, _minPlaneArea)
        };
        // Let ARPlaneManager create and update plane prefab instances.
        _planeManager.enabled = true;
    }

    private void OnPlanesChanged(ARPlanesChangedEventArgs changes)
    {
        _planeSelectionDirty = true;
    }

    private void LateUpdate()
    {
        // Match after both marker poses and native plane boundaries have updated.
        if (!_planeDetectionConfigured || _planeManager == null)
            return;

        if (_planeSelectionDirty)
        {
            _planeSelectionDirty = false;
            RefreshPlaneSelection();
        }

        // The stock visualizer gates rendering on ARSession.state in Update.
        // Native plane tracking and a valid mesh determine visibility here.
        int renderedPlanes = 0;
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
                meshRenderer.enabled = visible && hasMesh;
                if (meshRenderer.enabled)
                    renderedPlanes++;
            }
            var outline = plane.GetComponent<LineRenderer>();
            if (outline != null)
                outline.enabled = visible && outline.positionCount >= 3;
            var collider = plane.GetComponent<MeshCollider>();
            if (collider != null)
                collider.enabled = visible && hasMesh;
        }

        UpdateCharacterPlacements();

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

    private void UpdateCharacterPlacements()
    {
        if (!_spatialMappingGranted || !_planeManager.enabled)
            return;

        Transform origin = _xrOrigin.CameraFloorOffsetObject.transform;
        foreach (KeyValuePair<ulong, MarkerState> entry in _markersById)
        {
            MarkerState marker = entry.Value;
            Vector3 sessionPosition = marker.Instance != null
                ? marker.PlacementSessionPosition : marker.SessionPosition;
            Vector3 worldPosition = origin.TransformPoint(sessionPosition);
            ARPlane plane = marker.Instance != null ? _planeManager.GetPlane(marker.GroundPlaneId) : null;
            if (plane == null || plane.subsumedBy != null || plane.trackingState == TrackingState.None)
                plane = FindCharacterPlane(worldPosition);
            if (plane == null)
                continue;

            // Project onto the detected surface instead of using the marker's height.
            Vector3 normal = plane.transform.up;
            if (Vector3.Dot(normal, Vector3.up) < 0f)
                normal = -normal;
            Vector3 surfacePosition = new Plane(normal, plane.transform.position)
                .ClosestPointOnPlane(worldPosition);

            if (marker.Instance == null)
            {
                marker.Instance = Instantiate(_markerPrefab, surfacePosition,
                    GetUprightRotation(surfacePosition, origin.rotation * marker.SessionRotation,
                        _xrOrigin.Camera.transform.position), origin);
                marker.PlacementSessionPosition = marker.SessionPosition;
                marker.SpawnFrame = Time.frameCount;
                marker.GroundContactReady = false;
                marker.Renderers = marker.Instance.GetComponentsInChildren<Renderer>(true);
                marker.Animator = marker.Instance.GetComponentInChildren<Animator>();
                if (marker.Animator != null)
                {
                    marker.OriginalCullingMode = marker.Animator.cullingMode;
                    marker.Animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                    marker.Animator.applyRootMotion = false;
                }
                // Allow Start and the first animation evaluation before measuring the soles.
                SetRendererVisibility(marker, false);
            }
            marker.GroundPlaneId = plane.trackableId;

            if (!marker.GroundContactReady)
            {
                if (Time.frameCount <= marker.SpawnFrame)
                    continue;

                Transform root = marker.Instance.transform;
                if (!TryGetLowestCharacterPoint(marker, normal, out Vector3 lowestPoint))
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

            // Keep the initial footprint stable and follow refinements of the floor height.
            Transform instanceTransform = marker.Instance.transform;
            Vector3 contactOffset = instanceTransform.TransformVector(marker.LocalGroundContact);
            instanceTransform.position = GetGroundedPosition(surfacePosition, contactOffset,
                normal, _characterGroundClearance);
        }
    }

    private static Vector3 GetGroundedPosition(Vector3 surfacePosition, Vector3 contactOffset,
        Vector3 normal, float clearance)
    {
        return surfacePosition + normal * (clearance - Vector3.Dot(contactOffset, normal));
    }

    private ARPlane FindCharacterPlane(Vector3 worldPosition)
    {
        ARPlane closest = null;
        float closestDistance = _markersOnFloor ? float.PositiveInfinity : _maxMarkerPlaneDistance;
        foreach (ARPlane plane in _planeManager.trackables)
        {
            if (plane.subsumedBy != null || plane.trackingState == TrackingState.None || !IsPlaneTypeAllowed(plane))
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

    private static bool TryGetLowestCharacterPoint(MarkerState marker, Vector3 normal, out Vector3 lowestPoint)
    {
        lowestPoint = Vector3.zero;
        float lowestHeight = float.PositiveInfinity;
        bool found = false;
        var bakedMesh = new Mesh();
        var vertices = new List<Vector3>();
        try
        {
            // Skinned mesh vertices describe the actual pose; renderer bounds can include all animations.
            foreach (SkinnedMeshRenderer skin in marker.Instance.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                if (skin.sharedMesh == null)
                    continue;
                skin.BakeMesh(bakedMesh);
                bakedMesh.GetVertices(vertices);
                foreach (Vector3 vertex in vertices)
                {
                    Vector3 point = skin.transform.TransformPoint(vertex);
                    float height = Vector3.Dot(point, normal);
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

        // Static model fallback; ignore UI, particles and line renderers.
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
                float height = Vector3.Dot(point, normal);
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
        // Report state changes at most once every five seconds.
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
        // Floor mode shows the entire detected floor, including separate patches.
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
            // Keep native tracking running while hiding unrelated surfaces.
            var visualizer = plane.GetComponent<ARPlaneMeshVisualizer>();
            if (visualizer != null)
                visualizer.enabled = visible;
            var collider = plane.GetComponent<MeshCollider>();
            if (collider != null)
                collider.enabled = visible;
        }
    }

    private void SelectPlanesAtMarkers()
    {
        // General mode selects the closest surface containing each marker projection.
        Transform sessionOrigin = _xrOrigin.CameraFloorOffsetObject.transform;
        foreach (MarkerState marker in _markersById.Values)
        {
            // Convert the marker position to Unity world space.
            Vector3 worldPosition = sessionOrigin.TransformPoint(marker.SessionPosition);
            ARPlane closestPlane = null;
            float closestDistance = _maxMarkerPlaneDistance;
            foreach (ARPlane plane in _planeManager.trackables)
            {
                // Skip merged planes and planes without tracking.
                if (plane.subsumedBy != null || plane.trackingState == TrackingState.None)
                    continue;
                if (!IsPlaneTypeAllowed(plane))
                    continue;

                // Local X/Z gives the marker's projection onto the plane.
                Vector3 local = plane.transform.InverseTransformPoint(worldPosition);
                // Measure perpendicular distance to the surface in world units.
                float distance = Mathf.Abs(Vector3.Dot(worldPosition - plane.transform.position,
                    plane.transform.up));
                // Require proximity and a projection inside the plane boundary.
                if (distance > closestDistance ||
                    !ContainsPoint(plane, new Vector2(local.x, local.z), _planeBoundaryTolerance))
                    continue;

                closestPlane = plane;
                closestDistance = distance;
            }
            if (closestPlane != null)
                _visiblePlaneIds.Add(closestPlane.trackableId);
        }

    }

    private bool IsPlaneTypeAllowed(ARPlane plane)
    {
        // Require a natively classified horizontal floor in floor mode.
        return !_markersOnFloor ||
            (plane.classification == PlaneClassification.Floor &&
                (plane.alignment == PlaneAlignment.HorizontalUp ||
                 plane.alignment == PlaneAlignment.HorizontalDown));
    }

    private static bool ContainsPoint(ARPlane plane, Vector2 point, float edgeTolerance)
    {
        // Test a 2D point against the detected boundary polygon.
        var boundary = plane.boundary;
        if (!boundary.IsCreated || boundary.Length < 3)
            return false;

        bool inside = false;
        for (int i = 0, j = boundary.Length - 1; i < boundary.Length; j = i++)
        {
            Vector2 a = boundary[j];
            Vector2 b = boundary[i];
            // Allow detection noise near a boundary, measured in world meters.
            Vector2 edge = b - a;
            float t = edge.sqrMagnitude > 0f
                ? Mathf.Clamp01(Vector2.Dot(point - a, edge) / edge.sqrMagnitude) : 0f;
            Vector2 offset = point - (a + edge * t);
            Vector3 worldOffset = plane.transform.TransformVector(new Vector3(offset.x, 0f, offset.y));
            if (worldOffset.sqrMagnitude <= edgeTolerance * edgeTolerance)
                return true;

            // Ray crossing: an odd number of intersections means inside.
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
