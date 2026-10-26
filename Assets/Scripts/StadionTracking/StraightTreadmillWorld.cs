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
        private readonly StadiumTrackGeometry oval = new StadiumTrackGeometry();

        public float SpeedKmh
        {
            get => SanitizeSpeed(speedKmh);
            set => speedKmh = SanitizeSpeed(value);
        }
        public float SpeedMetresPerSecond => SpeedKmh / 3.6f;
        public double TravelledMetres => travelledMetres;
        public long CompletedLaps => (long)(travelledMetres / 400.0);
        public double MetresIntoLap => travelledMetres % 400.0;
        public bool PlacementReady => placementReady;
        public Vector3 RunnerGroundPosition => startPosition;
        public Quaternion RunnerRotation => startRotation;
        public Camera HeadCamera => headCamera != null ? headCamera : Camera.main;
        public bool HeightConfirmed => heightConfirmed || !requireHeightCalibration;
        public bool AutomaticHeightOnStart => automaticHeightOnStart;

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

        // The tile pattern is periodic, so wrapping keeps coordinates small on long runs.
        public void AdvanceSimulation(float elapsedSeconds)
        {
            if (!HeightConfirmed || !placementReady || float.IsNaN(elapsedSeconds) || float.IsInfinity(elapsedSeconds)
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
            if (!HeadPoseReady || float.IsNaN(eyeHeightMetres) || float.IsInfinity(eyeHeightMetres)
                || eyeHeightMetres < 0.5f || eyeHeightMetres > 2.5f) return false;
            headCamera = HeadCamera;
            return ConfirmSurfaceHeight(headCamera.transform.position.y - eyeHeightMetres);
        }

        public bool TryCalibrateFromFloorOrigin()
        {
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
            foreach (var renderer in GetComponentsInChildren<MeshRenderer>(true)) renderer.enabled = visible;
        }

        [ContextMenu("Reset travelled distance")]
        public void ResetDistance()
        {
            if (placementReady) transform.SetPositionAndRotation(startPosition, startRotation);
            CaptureOrigin();
        }

        private void CaptureOrigin()
        {
            startPosition = transform.position;
            startRotation = transform.rotation;
            travelledMetres = 0;
            tileOffset = 0;
            placementReady = true;
            if (followOval400m) ApplyOvalPose();
        }

        private void ApplyOvalPose()
        {
            // Published rounded stadium dimensions give 400.0012 m on lane 1's measurement line.
            // Normalize the distance parameter to exactly 400 m without changing the shared geometry.
            double pathMetres = (ovalStartMetres + travelledMetres % 400.0) * oval.Length(1) / 400.0;
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
