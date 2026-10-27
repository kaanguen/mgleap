using System;
using System.Collections;
using UnityEngine;
using UnityEngine.XR;
using Unity.XR.CoreUtils;
using UnityEngine.XR.Management;
using UnityEngine.XR.OpenXR;
using MagicLeap.OpenXR.Features;

namespace StadionTracking
{
    /// <summary>Moves only the virtual scenery; the XR rig remains tracked in physical space.</summary>
    public sealed class StraightTreadmillWorld : MonoBehaviour
    {
        [Header("Simulated treadmill")]
        [SerializeField, Min(0f), Tooltip("Belt speed in km/h. Zero pauses without resetting distance.")]
        private float speedKmh;
        [SerializeField, Min(1f), Tooltip("Length of one identical scenery tile in metres.")]
        private float tileLengthMetres = 20f;
        [SerializeField, Tooltip("Follow the closed 400 m measurement line instead of repeating straight tiles.")]
        private bool followOval400m;
        [SerializeField, Range(0f, 400f), Tooltip("Starting distance around the oval; 390 starts 10 m before the first bend.")]
        private float ovalStartMetres = 390f;

        [SerializeField, Tooltip("Render the track while keeping running simulation active.")]
        private bool showTrack = true;

        [Header("Marker placement")]
        [SerializeField, Tooltip("Wait for a floor marker and its detected floor plane instead of estimating body height.")]
        private bool requireMarkerPlacement = true;
        public bool RequiresMarkerPlacement => requireMarkerPlacement;
        public bool MarkerFloorConfirmed { get; private set; }
        public int MarkerPlacementRevision { get; private set; }
        private Vector3 markerFloorPoint, markerFloorNormal = Vector3.up, markerForward = Vector3.forward;
        public void BeginMarkerPlacement()
        {
            requireMarkerPlacement = true;
            automaticHeightOnStart = false;
            SessionPaused = true;
            MarkerFloorConfirmed = placementReady = heightConfirmed = false;
            MarkerPlacementRevision++;
            SetTrackVisible(false);
        }
        public bool PlaceFromMarkerPlane(Vector3 position, Vector3 forward, Vector3 normal)
        {
            if (!requireMarkerPlacement || !HeadPoseReady || normal.y < .9659f) return false;
            forward = Vector3.ProjectOnPlane(forward, normal);
            if (forward.sqrMagnitude < .001f) return false;
            markerFloorPoint = position; markerFloorNormal = normal.normalized; markerForward = forward.normalized;
            MarkerFloorConfirmed = true;
            startPosition = calibrationPosition = position;
            startRotation = calibrationRotation = Quaternion.LookRotation(markerForward, markerFloorNormal);
            heightConfirmed = placementReady = hasCalibrationReference = true;
            surfaceHeight = position.y;
            CalibrationOffsets = Vector3.zero;
            if (RealTrackMode)
            {
                trackPointA = position; TrackPointASet = RealTrackCalibrated = true;
                physicalPhaseReady = false;
            }
            ApplyCurrentPose(); SetTrackVisible(true);
            return true;
        }
        private float DetectedFloorHeightAt(Vector3 point) => markerFloorPoint.y -
            (markerFloorNormal.x * (point.x - markerFloorPoint.x) + markerFloorNormal.z * (point.z - markerFloorPoint.z)) / markerFloorNormal.y;

        [Header("Placement")]
        [SerializeField] private Camera headCamera;
        [SerializeField, Tooltip("Align once to the initial horizontal view after head tracking is ready.")]
        private bool alignToViewOnStart = true;
        [SerializeField, Tooltip("World-space height of the treadmill belt surface in metres. Adjust to your setup.")]
        private float surfaceHeight;
        [SerializeField, Tooltip("Show the height setup before placing or moving the track.")]
        private bool requireHeightCalibration = true;
        [SerializeField, Tooltip("Place directly from fixed body height, independently of the optional UI object.")]
        private bool automaticHeightOnStart = true;
        [SerializeField, Range(80, 230)] private int bodyHeightCm = 184;
        [SerializeField, Range(0, 25)] private float headTopToEyeCm = 12f;

        [Header("Live readout")]
        [SerializeField] private double travelledMetres;
        [SerializeField] private bool placementReady;

        private Vector3 startPosition;
        private Quaternion startRotation;
        private double tileOffset;
        private bool heightConfirmed;
        private Vector3 calibrationPosition;
        private Quaternion calibrationRotation;
        private bool hasCalibrationReference;
        public bool RealTrackMode { get; private set; }
        public bool TrackPointASet { get; private set; }
        public bool RealTrackCalibrated { get; private set; }
        public float CalibrationBaselineMetres { get; private set; }
        public const int StartAvatarLane = 3;
        public int AvatarLane { get; private set; } = StartAvatarLane;
        private Vector3 trackPointA;
        private double previousPhysicalPhase, signedPhysicalDistance;
        private bool physicalPhaseReady, raceOriginReady;
        public double RaceStartTrackMetres { get; private set; }
        private float physicalSpeed;
        public double UserTrackPositionMetres { get; private set; }
        public void SetAvatarLane(int lane)
        {
            if (lane < 1 || lane > 8) throw new ArgumentOutOfRangeException(nameof(lane));
            AvatarLane = lane;
        }
        public void SetTrackEnvironment(bool realTrack)
        {
            if (!HeadPoseReady) throw new InvalidOperationException("Head tracking is not ready.");
            if (!realTrack && !requireMarkerPlacement && Vector3.ProjectOnPlane(HeadCamera.transform.forward, Vector3.up).sqrMagnitude < .001f)
                throw new InvalidOperationException("Look horizontally forward before switching to treadmill mode.");
            SessionPaused = true;
            RealTrackMode = realTrack;
            TrackPointASet = RealTrackCalibrated = physicalPhaseReady = raceOriginReady = false;
            RaceStartTrackMetres = 0;
            travelledMetres = 0; signedPhysicalDistance = 0; physicalSpeed = 0;
            CalibrationBaselineMetres = 0; CalibrationOffsets = Vector3.zero;
            hasCalibrationReference = false;
            if (requireMarkerPlacement)
            {
                if (realTrack) followOval400m = true;
                if (MarkerFloorConfirmed) PlaceFromMarkerPlane(markerFloorPoint, markerForward, markerFloorNormal);
                else { placementReady = false; SetTrackVisible(false); }
                return;
            }
            if (realTrack)
            {
                followOval400m = true; placementReady = false;
                SetTrackVisible(false);
            }
            else
            {
                if (!CalibrateFromEyeHeight((bodyHeightCm - headTopToEyeCm) / 100f))
                    throw new InvalidOperationException("Look horizontally forward to place the treadmill world.");
            }
        }
        public void CaptureTrackPointA()
        {
            if (!RealTrackMode || !HeadPoseReady) throw new InvalidOperationException("Select stadium mode and wait for head tracking.");
            trackPointA = HeadCamera.transform.position;
            if (requireMarkerPlacement && !MarkerFloorConfirmed) throw new InvalidOperationException("Scan the floor marker before capturing track points.");
            trackPointA.y = requireMarkerPlacement ? DetectedFloorHeightAt(trackPointA) : trackPointA.y - (bodyHeightCm - headTopToEyeCm) / 100f;
            TrackPointASet = true; RealTrackCalibrated = false; placementReady = false;
            CalibrationBaselineMetres = 0; SetTrackVisible(false);
        }
        public void CaptureTrackPointB(float distanceAlongTrack)
        {
            if (!RealTrackMode || !TrackPointASet || !HeadPoseReady)
                throw new InvalidOperationException("Capture point A first and wait for head tracking.");
            if (float.IsNaN(distanceAlongTrack) || float.IsInfinity(distanceAlongTrack) || distanceAlongTrack < 5 || distanceAlongTrack > 100)
                throw new ArgumentOutOfRangeException(nameof(distanceAlongTrack));
            var a = oval.EvaluateAlignedLane(1, 0);
            var b = oval.EvaluateAlignedLane(1, distanceAlongTrack);
            Vector3 baseline = HeadCamera.transform.position - trackPointA; baseline.y = 0;
            float yaw = (float)StadiumCalibration.SolveYaw(b.X - a.X, b.Z - a.Z, baseline.x, baseline.z);
            Quaternion modelRotation = Quaternion.Euler(0, yaw, 0);
            startPosition = calibrationPosition = trackPointA;
            Vector3 trackForward = modelRotation * new Vector3((float)a.DirectionX, 0, (float)a.DirectionZ);
            Vector3 floorUp = requireMarkerPlacement ? markerFloorNormal : Vector3.up;
            startRotation = calibrationRotation = Quaternion.LookRotation(Vector3.ProjectOnPlane(trackForward, floorUp), floorUp);
            surfaceHeight = trackPointA.y; heightConfirmed = placementReady = RealTrackCalibrated = true;
            hasCalibrationReference = true; CalibrationOffsets = Vector3.zero;
            CalibrationBaselineMetres = baseline.magnitude;
            physicalPhaseReady = false;
            ApplyCurrentPose(); SetTrackVisible(true);
        }
        public void GetLanePoseAtDistance(double distance, int lane, out Vector3 position, out Quaternion rotation)
        {
            if (!followOval400m)
            {
                position = startPosition + startRotation * new Vector3((float)(oval.Radius(lane) - oval.Radius(1)), 0, (float)(distance - travelledMetres));
                rotation = startRotation; return;
            }
            var sample = oval.EvaluateAlignedLane(lane, TrackStartMetres + (RealTrackMode ? RaceStartTrackMetres : 0) + distance);
            position = transform.TransformPoint(new Vector3((float)sample.X, 0, (float)sample.Z));
            rotation = Quaternion.LookRotation(transform.TransformDirection(new Vector3((float)sample.DirectionX, 0, (float)sample.DirectionZ)), Vector3.up);
        }
        public double AvatarLaneSpeedScale(double distance) => followOval400m
            ? oval.LaneSpeedScale(AvatarLane, TrackStartMetres + (RealTrackMode ? RaceStartTrackMetres : 0) + distance) : 1;
        public Vector3 CalibrationOffsets { get; private set; }
        public float SurfaceHeight => surfaceHeight;

        private void ApplyCurrentPose()
        {
            if (followOval400m) ApplyOvalPose();
            else transform.SetPositionAndRotation(startPosition + startRotation * Vector3.back * (float)tileOffset, startRotation);
        }

        public bool RecalibratePlacement(bool includeHeight)
        {
            if (RealTrackMode) throw new InvalidOperationException("Use stadium points A/B to recalibrate the fixed track.");
            if (!HeadPoseReady || !HeightConfirmed) return false;
            var forward = Vector3.ProjectOnPlane(HeadCamera.transform.forward, Vector3.up);
            if (forward.sqrMagnitude < .001f) return false;
            double distance = travelledMetres, offset = tileOffset;
            if (requireMarkerPlacement)
            {
                if (!MarkerFloorConfirmed) return false;
                Vector3 head = HeadCamera.transform.position;
                startPosition = new Vector3(head.x, DetectedFloorHeightAt(head), head.z);
                startRotation = Quaternion.LookRotation(Vector3.ProjectOnPlane(forward, markerFloorNormal), markerFloorNormal);
                surfaceHeight = startPosition.y;
            }
            else if (includeHeight)
            {
                if (!CalibrateFromEyeHeight((bodyHeightCm - headTopToEyeCm) / 100f)) return false;
            }
            else AlignToCurrentView();
            travelledMetres = distance; tileOffset = offset;
            calibrationPosition = startPosition; calibrationRotation = startRotation;
            hasCalibrationReference = true; CalibrationOffsets = Vector3.zero;
            ApplyCurrentPose();
            return true;
        }

        // Absolute offsets from the last calibration: X across, Y along, Z heading degrees.
        public void SetCalibrationOffsets(float across, float along, float heading)
        {
            if (!placementReady) throw new InvalidOperationException("Placement is not ready.");
            if (!hasCalibrationReference)
            { calibrationPosition = startPosition; calibrationRotation = startRotation; hasCalibrationReference = true; }
            CalibrationOffsets = new Vector3(across, along, heading);
            startPosition = calibrationPosition + calibrationRotation * new Vector3(across, 0, along);
            startRotation = calibrationRotation * Quaternion.Euler(0, heading, 0);
            ApplyCurrentPose();
            if (RealTrackMode) physicalPhaseReady = false;
        }
        private readonly StadiumTrackGeometry oval = new StadiumTrackGeometry();

        public bool ShowTrack
        {
            get => showTrack;
            set { showTrack = value; SetTrackVisible(HeightConfirmed); }
        }

        private readonly RunnerSpeedInput speedInput = new RunnerSpeedInput();
        public RunnerSpeedSource UserSpeedSource => speedInput.Source;
        public float FixedTestSpeedKmh => SanitizeSpeed(speedKmh);
        public bool TreadmillSignalReady => speedInput.HasFreshTreadmill(Time.realtimeSinceStartupAsDouble);
        public bool ConfigureUserSpeed(RunnerSpeedSource source, float testSpeedKmh)
        {
            if (float.IsNaN(testSpeedKmh) || float.IsInfinity(testSpeedKmh) || testSpeedKmh < 0)
                throw new ArgumentOutOfRangeException(nameof(testSpeedKmh));
            bool changed = speedInput.SelectSource(source);
            if (source == RunnerSpeedSource.FixedTest) speedKmh = testSpeedKmh;
            // Changing the user input source must not stop the independently paced avatar.
            return changed;
        }
        // A future BLE/FTMS/USB adapter supplies measured belt speed here; this never drives a physical belt.
        public bool ReportTreadmillSpeedKmh(float measuredKmh) => speedInput.ReportTreadmill(measuredKmh, Time.realtimeSinceStartupAsDouble);
        public void ClearTreadmillSignal() => speedInput.InvalidateTreadmill();

        public float SpeedKmh
        {
            get => (float)speedInput.SpeedKmh(SanitizeSpeed(speedKmh), Time.realtimeSinceStartupAsDouble);
            set => speedKmh = SanitizeSpeed(value);
        }
        public float SpeedMetresPerSecond => RealTrackMode ? (SessionPaused ? 0 : physicalSpeed) : SpeedKmh / 3.6f;
        /// <summary>Current pace in seconds/km; zero denotes pause.</summary>
        public float PaceSecondsPerKilometre => SpeedKmh > 0f ? 3600f / SpeedKmh : 0f;
        public float TrackStartMetres => RealTrackMode ? 0 : followOval400m ? ovalStartMetres : 0;
        public double TravelledMetres => travelledMetres;
        public long CompletedLaps => (long)(travelledMetres / 400.0);
        public double MetresIntoLap => travelledMetres % 400.0;
        public bool PlacementReady => placementReady;
        // A physical belt stop leaves the race clock running; only explicit session pause stops it.
        public bool SessionPaused { get; set; }
        public Vector3 RunnerGroundPosition => startPosition;
        public Quaternion RunnerRotation => startRotation;
        public Camera HeadCamera => headCamera != null ? headCamera : Camera.main;
        public bool HeightConfirmed => requireMarkerPlacement ? MarkerFloorConfirmed : heightConfirmed || !requireHeightCalibration;
        public int BodyHeightCm => bodyHeightCm;
        public bool AutomaticHeightOnStart => !requireMarkerPlacement && automaticHeightOnStart;
        public void SetBodyHeightCentimetres(int centimetres)
        {
            bodyHeightCm = Mathf.Clamp(centimetres, 80, 230);
            automaticHeightOnStart = true;
        }

        public bool HeadPoseReady
        {
            get
            {
                if (HeadCamera == null) return false;
#if UNITY_ANDROID && !UNITY_EDITOR
                return InputDevices.GetDeviceAtXRNode(XRNode.Head)
                    .TryGetFeatureValue(CommonUsages.isTracked, out bool tracked) && tracked;
#else
                return !XRSettings.isDeviceActive ||
                    (InputDevices.GetDeviceAtXRNode(XRNode.Head)
                    .TryGetFeatureValue(CommonUsages.isTracked, out bool tracked) && tracked);
#endif
            }
        }

        private void Start()
        {
            if (headCamera == null) headCamera = Camera.main;
            if (requireMarkerPlacement)
            {
                SessionPaused = true;
                SetTrackVisible(false);
                Debug.Log("[RunningTrack] Waiting for a floor marker and confirmed floor plane; no body-height placement.");
                return;
            }
            if (!alignToViewOnStart && HeightConfirmed) ResetDistance();
            else if (headCamera == null)
                Debug.LogError("[Treadmill] Assign the tracked head camera to VirtualWorld.", this);
            SetTrackVisible(HeightConfirmed);
            Debug.Log($"[Treadmill] World ready; scene={gameObject.scene.name}, calibrationRequired={requireHeightCalibration}, head={headCamera}.");
            if (automaticHeightOnStart && !HeightConfirmed) StartCoroutine(PlaceAutomatically());
        }

        private IEnumerator PlaceAutomatically()
        {
            float trackedFor = 0f;
            float nextReport = Time.realtimeSinceStartup + 5f;
            while (!HeightConfirmed)
            {
                trackedFor = HeadPoseReady ? trackedFor + Time.unscaledDeltaTime : 0f;
                if (trackedFor >= 0.5f && CalibrateFromEyeHeight((bodyHeightCm - headTopToEyeCm) / 100f))
                {
                    Debug.Log($"[Treadmill] Automatic world placement ready: body={bodyHeightCm} cm; optional menu is not required.");
                    yield break;
                }
                if (Time.realtimeSinceStartup >= nextReport)
                {
                    Debug.LogWarning($"[Treadmill] Waiting for head pose/forward direction; camera={HeadCamera}, tracked={HeadPoseReady}.");
                    nextReport = Time.realtimeSinceStartup + 5f;
                }
                yield return null;
            }
        }

        private void Update()
        {
            if (requireMarkerPlacement && !MarkerFloorConfirmed) return;
            if (RealTrackMode && !RealTrackCalibrated) return;
            if (!HeightConfirmed) return;
            if (!placementReady)
            {
                if (headCamera == null) return;
                // On the headset, don't anchor to the placeholder camera pose before tracking starts.
                if (XRSettings.isDeviceActive &&
                    (!InputDevices.GetDeviceAtXRNode(XRNode.Head).TryGetFeatureValue(CommonUsages.isTracked, out bool tracked)
                     || !tracked)) return;
                AlignToCurrentView();
            }
            AdvanceSimulation(Time.deltaTime);
        }

        /// <summary>Useful later for feeding belt readings; values use km/h.</summary>
        public void SetSpeedKmh(float value) => SpeedKmh = value;

        /// <summary>Setup/menu API: e.g. 6.0 min/km sets 10 km/h. Invalid pace leaves speed unchanged.</summary>
        public bool TrySetPaceMinutesPerKilometre(float minutes)
        {
            if (float.IsNaN(minutes) || float.IsInfinity(minutes) || minutes <= 0f) return false;
            float speed = 60f / minutes;
            if (float.IsInfinity(speed) || speed <= 0f) return false;
            SpeedKmh = speed;
            return true;
        }

        /// <summary>Minutes:seconds per km, e.g. (7,30) sets 8 km/h.</summary>
        public bool TrySetPace(int minutes, int seconds)
        {
            if (minutes < 0 || seconds < 0 || seconds > 59) return false;
            double totalSeconds = (double)minutes * 60 + seconds;
            if (totalSeconds <= 0) return false;
            SpeedKmh = (float)(3600.0 / totalSeconds);
            return true;
        }

        /// <summary>World pose at an arc-length offset from the user, with the route's actual heading.</summary>
        public void GetRunningPose(float metresAhead, out Vector3 position, out Quaternion rotation)
        {
            if (float.IsNaN(metresAhead) || float.IsInfinity(metresAhead)) metresAhead = 0;
            if (RealTrackMode)
            {
                GetLanePoseAtDistance(UserTrackPositionMetres - RaceStartTrackMetres + metresAhead, 1, out position, out rotation);
                return;
            }
            if (!followOval400m)
            {
                position = startPosition + startRotation * Vector3.forward * metresAhead;
                rotation = startRotation;
                return;
            }
            var sample = oval.Evaluate(1, (ovalStartMetres + travelledMetres % 400.0 + metresAhead) * oval.Length(1) / 400.0);
            position = transform.TransformPoint(new Vector3((float)sample.X, 0, (float)sample.Z));
            Vector3 tangent = transform.TransformDirection(new Vector3((float)sample.DirectionX, 0, (float)sample.DirectionZ));
            rotation = Quaternion.LookRotation(tangent, Vector3.up);
        }

        // The tile pattern is periodic, so wrapping keeps coordinates small on long runs.
        public void AdvanceSimulation(float elapsedSeconds)
        {
            if (RealTrackMode)
            {
                UpdatePhysicalProgress(elapsedSeconds);
                return;
            }
            if (SessionPaused || !HeightConfirmed || !placementReady || float.IsNaN(elapsedSeconds) || float.IsInfinity(elapsedSeconds)
                || elapsedSeconds <= 0f || SpeedKmh <= 0f) return;

            double distance = (double)SpeedKmh / 3.6 * elapsedSeconds;
            travelledMetres += distance;
            if (followOval400m)
            {
                ApplyOvalPose();
                return;
            }
            tileOffset = (tileOffset + distance) % Math.Max(1.0, tileLengthMetres);
            transform.SetPositionAndRotation(startPosition + startRotation * Vector3.back * (float)tileOffset,
                startRotation);
        }

        private void UpdatePhysicalProgress(float seconds)
        {
            physicalSpeed = 0;
            if (!RealTrackCalibrated || !HeadPoseReady || seconds <= 0 || float.IsNaN(seconds) || float.IsInfinity(seconds)) return;
            Vector3 local = transform.InverseTransformPoint(HeadCamera.transform.position);
            double phase = oval.Project(1, local.x, local.z) * 400 / oval.Length(1);
            if (double.IsNaN(phase) || double.IsInfinity(phase)) return;
            UserTrackPositionMetres = phase;
            if (!SessionPaused && !raceOriginReady)
            { RaceStartTrackMetres = phase; raceOriginReady = true; physicalPhaseReady = false; }
            double delta = physicalPhaseReady ? StadiumCalibration.WrappedDelta(previousPhysicalPhase, phase) : 0;
            previousPhysicalPhase = phase; physicalPhaseReady = true;
            if (SessionPaused || Math.Abs(delta) > 5) return; // Ignore tracking/relocalization jumps.
            signedPhysicalDistance += delta;
            double before = travelledMetres;
            travelledMetres = Math.Max(travelledMetres, Math.Max(0, signedPhysicalDistance));
            physicalSpeed = (float)((travelledMetres - before) / seconds);
        }

        [ContextMenu("Align track to current view and reset distance")]
        public void AlignToCurrentView()
        {
            if (!HeightConfirmed) return;
            if (headCamera == null) headCamera = Camera.main;
            if (headCamera == null) return;
            Vector3 forward = Vector3.ProjectOnPlane(headCamera.transform.forward, Vector3.up);
            if (forward.sqrMagnitude < 0.001f) return;
            Vector3 head = headCamera.transform.position;
            transform.SetPositionAndRotation(new Vector3(head.x, surfaceHeight, head.z),
                Quaternion.LookRotation(forward.normalized, Vector3.up));
            CaptureOrigin();
        }

        public void BeginHeightCalibration()
        {
            requireHeightCalibration = true;
            heightConfirmed = false;
            placementReady = false;
            SetTrackVisible(false);
        }

        public bool CalibrateFromEyeHeight(float eyeHeightMetres)
        {
            if (RealTrackMode || requireMarkerPlacement) return false;
            if (!HeadPoseReady || float.IsNaN(eyeHeightMetres) || float.IsInfinity(eyeHeightMetres)
                || eyeHeightMetres < 0.5f || eyeHeightMetres > 2.5f) return false;
            headCamera = HeadCamera;
            return ConfirmSurfaceHeight(headCamera.transform.position.y - eyeHeightMetres);
        }

        public bool TryCalibrateFromFloorOrigin()
        {
            if (requireMarkerPlacement) return false;
            if (!HeadPoseReady) return false;
            var origin = HeadCamera.GetComponentInParent<XROrigin>();
            if (origin == null) return false;
            var subsystem = ActiveInputSubsystem();
            return subsystem != null && subsystem.running &&
                subsystem.GetTrackingOriginMode() == TrackingOriginModeFlags.Floor &&
                ConfirmSurfaceHeight(origin.Origin.transform.position.y);
        }

        // Magic Leap OpenXR Tracking Origin Example: use the active loader's input subsystem,
        // check supported modes, request Floor, and verify the actual mode before calibration.
        // https://developer-docs.magicleap.cloud/docs/guides/unity-openxr/reference-space/tracking-origin-example/
        public bool TryRequestFloorOrigin(out string reason)
        {
            reason = "";
            if (!HeadPoseReady)
            { reason = "Auf Headtracking warten und erneut versuchen."; return false; }
            var origin = HeadCamera.GetComponentInParent<XROrigin>();
            var subsystem = ActiveInputSubsystem();
            if (origin == null || subsystem == null || !subsystem.running)
            { reason = "OpenXR noch nicht bereit. Bitte Größe verwenden oder erneut versuchen."; return false; }
            var feature = OpenXRSettings.Instance?.GetFeature<MagicLeapReferenceSpacesFeature>();
            if (feature == null || !feature.enabled)
            { reason = "Magic Leap 2 Reference Spaces ist nicht aktiv. Bitte Größe verwenden."; return false; }
            if (subsystem.GetTrackingOriginMode() == TrackingOriginModeFlags.Floor) return true;
            if ((subsystem.GetSupportedTrackingOriginModes() & TrackingOriginModeFlags.Floor) == 0)
            { reason = "OpenXR bietet keine Bodenreferenz an. Bitte Größe verwenden."; return false; }
            // XROrigin applies TrySetTrackingOriginMode and keeps its camera offset in sync.
            origin.RequestedTrackingOriginMode = XROrigin.TrackingOriginMode.Floor;
            return true;
        }

        private static XRInputSubsystem ActiveInputSubsystem() =>
            XRGeneralSettings.Instance?.Manager?.activeLoader?.GetLoadedSubsystem<XRInputSubsystem>();

        public void AdjustSurfaceHeight(float deltaMetres)
        {
            if (!HeightConfirmed || float.IsNaN(deltaMetres) || float.IsInfinity(deltaMetres)) return;
            surfaceHeight += deltaMetres;
            if (hasCalibrationReference) calibrationPosition += Vector3.up * deltaMetres;
            startPosition += Vector3.up * deltaMetres;
            transform.position += Vector3.up * deltaMetres;
        }

        private bool ConfirmSurfaceHeight(float height)
        {
            Vector3 forward = Vector3.ProjectOnPlane(HeadCamera.transform.forward, Vector3.up);
            if (forward.sqrMagnitude < 0.001f) return false;
            surfaceHeight = height;
            heightConfirmed = true;
            AlignToCurrentView();
            SetTrackVisible(true);
            return placementReady;
        }

        private void SetTrackVisible(bool visible)
        {
            foreach (var renderer in GetComponentsInChildren<MeshRenderer>(true))
                if (renderer.GetComponentInParent<Animator>() == null) renderer.enabled = visible && showTrack && (!RealTrackMode || RealTrackCalibrated) && (!requireMarkerPlacement || MarkerFloorConfirmed);
        }

        [ContextMenu("Reset travelled distance")]
        public void ResetDistance()
        {
            if (RealTrackMode)
            {
                travelledMetres = signedPhysicalDistance = 0; physicalSpeed = 0; physicalPhaseReady = raceOriginReady = false; RaceStartTrackMetres = 0;
                return;
            }
            if (placementReady) transform.SetPositionAndRotation(startPosition, startRotation);
            CaptureOrigin();
        }

        private void CaptureOrigin()
        {
            startPosition = transform.position;
            startRotation = transform.rotation;
            travelledMetres = 0;
            tileOffset = 0;
            hasCalibrationReference = false;
            CalibrationOffsets = Vector3.zero;
            placementReady = true;
            if (followOval400m) ApplyOvalPose();
        }

        private void ApplyOvalPose()
        {
            // Published rounded stadium dimensions give 400.0012 m on lane 1's measurement line.
            // Normalize the distance parameter to exactly 400 m without changing the shared geometry.
            double pathMetres = (TrackStartMetres + (RealTrackMode ? 0 : travelledMetres % 400.0)) * oval.Length(1) / 400.0;
            var sample = oval.Evaluate(1, pathMetres);
            Vector3 point = new Vector3((float)sample.X, 0f, (float)sample.Z);
            Vector3 tangent = new Vector3((float)sample.DirectionX, 0f, (float)sample.DirectionZ);
            Quaternion rotation = startRotation * Quaternion.Inverse(Quaternion.LookRotation(tangent, Vector3.up));
            // Pin the current virtual running point to the physical belt and keep the tangent forward.
            // All turning happens to the scenery; the XR camera remains under tracking control.
            transform.SetPositionAndRotation(startPosition - rotation * point, rotation);
        }

        private void OnValidate()
        {
            speedKmh = SanitizeSpeed(speedKmh);
            if (float.IsNaN(tileLengthMetres) || float.IsInfinity(tileLengthMetres)) tileLengthMetres = 20f;
            tileLengthMetres = Mathf.Max(1f, tileLengthMetres);
            if (float.IsNaN(surfaceHeight) || float.IsInfinity(surfaceHeight)) surfaceHeight = 0f;
        }

        private static float SanitizeSpeed(float speed) =>
            float.IsNaN(speed) || float.IsInfinity(speed) ? 0f : Mathf.Max(0f, speed);
    }
}
