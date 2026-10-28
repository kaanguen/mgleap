using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using StadionTracking;

public partial class MarkerAndAnchorTracker
{
    private void ResetRunningTrackPlacement(bool resetObservations)
    {
        _runningWorldRevision = _runningWorld.MarkerPlacementRevision;
        _runningWorldPlaced = false;
        _runningWorldFloor = null;
        RunningTrackMarkerId = null;
        _runningWorldScanRequestedAt = Time.unscaledTime;
        if (resetObservations)
            foreach (var marker in _markersById.Values) marker.PoseSamples = 0;
    }

    // One shared track/companion replaces the legacy per-marker avatar path in the run scene.
    private void UpdateRunningTrackPlacement()
    {
        if (_runningWorldRevision != _runningWorld.MarkerPlacementRevision)
        {
            ResetRunningTrackPlacement(true);
        }
        if (_runningWorldPlaced)
        {
            // Keep the marker placement when looking away, but follow refinements of its floor.
            while (_runningWorldFloor != null && _runningWorldFloor.subsumedBy != null)
                _runningWorldFloor = _runningWorldFloor.subsumedBy;
            if (_runningWorldFloor != null && _runningWorldFloor.trackingState == TrackingState.Tracking &&
                _runningWorldFloor.classification == PlaneClassification.Floor)
            {
                Transform floorSurface = GetPlaneSurface(_runningWorldFloor);
                Vector3 floorNormal = FloorPlaneGeometry.UpwardNormal(floorSurface);
                _runningWorld.UpdateMarkerFloorPlane(floorSurface.position, floorNormal);
            }
            return;
        }
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
            Transform surface = GetPlaneSurface(plane);
            Vector3 normal = FloorPlaneGeometry.UpwardNormal(surface);
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
            _runningWorldFloor = plane;
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
            Transform surface = GetPlaneSurface(plane);
            Vector3 normal = FloorPlaneGeometry.UpwardNormal(surface);
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

}
