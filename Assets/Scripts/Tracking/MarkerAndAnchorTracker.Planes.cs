using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using MagicLeap.OpenXR.Features.Planes;
using UnityEngine.XR.OpenXR;
using UnityEngine.XR.Management;
using MagicLeap.OpenXR.Subsystems;
using MagicLeap.Android;
using StadionTracking;

public partial class MarkerAndAnchorTracker
{
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
        RunningFloorDetected = false;
        Transform origin = _xrOrigin.CameraFloorOffsetObject.transform;
        foreach (ARPlane plane in _planeManager.trackables)
        {
            if (_planeManager.enabled && _spatialMappingGranted && plane.subsumedBy == null && plane.trackingState == TrackingState.Tracking &&
                plane.classification == PlaneClassification.Floor) RunningFloorDetected = true;
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

            float targetHeight = FloorPlaneGeometry.HeightAtXZ(state.ReferencePoint, position, normal);
            float blend = PlaneSmoothingBlend(Time.unscaledDeltaTime, _planeSmoothingTime);
            state.ReferencePoint.y = Mathf.Lerp(state.ReferencePoint.y, targetHeight, blend);
            state.Normal = Vector3.Lerp(state.Normal, normal, blend).normalized;
        }

        if (floor && state.FloorInitialized)
        {
            position.y = FloorPlaneGeometry.HeightAtXZ(position, state.ReferencePoint, state.Normal);
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

    private Transform GetPlaneSurface(ARPlane plane) =>
        _smoothedPlanes.TryGetValue(plane.trackableId, out var state) && state.Visual != null
            ? state.Visual : plane.transform;

}
