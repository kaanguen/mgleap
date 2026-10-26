using UnityEngine;
using UnityEngine.Rendering;

namespace StadionTracking
{

    public sealed class StadiumTrackPreview : MonoBehaviour
    {

        private readonly StadiumTrackGeometry _geometry = new StadiumTrackGeometry();
        private StadiumRunProgress _run = new StadiumRunProgress();
        private float _animationSpeedFraction;

        public float RequestedSpeedFraction { get; private set; }
        public float SpeedAtFullSlider => Mathf.Max(0.1f, _settings.SpeedAtFullSlider);
        public StadiumPacingMode PacingMode { get; private set; }
        public float TargetLapMinutes { get; private set; } = 4f;
        public double RequestedSpeedMetresPerSecond => RequestedSpeedFraction <= 0f ? 0.0 :
            PacingMode == StadiumPacingMode.TargetLapTime
                ? StadiumPacing.SpeedForLapTime(_run.LapLengthMetres, TargetLapMinutes)
                : RequestedSpeedFraction * SpeedAtFullSlider;

        public void SetPacingMode(StadiumPacingMode mode) => PacingMode = mode;

        public void SetTargetLapMinutes(float minutes)
        {
            if (!float.IsNaN(minutes) && !float.IsInfinity(minutes) && minutes > 0)
                TargetLapMinutes = Mathf.Max(0.1f, minutes);
        }

        // Store this runner's slider input; zero pauses without resetting laps.
        public void SetRequestedSpeed(float fraction)
        {
            RequestedSpeedFraction = float.IsNaN(fraction) || float.IsInfinity(fraction) ? 0f : Mathf.Clamp01(fraction);
        }
        public double CurrentSpeed => _run.CurrentSpeed;
        public double LapLengthMetres => _run.LapLengthMetres;
        public long CurrentLap => _run.CurrentLap;
        public long CompletedLaps => _run.CompletedLaps;
        public double TravelledMetres => _run.TravelledMetres;

        public Vector2 TrackLocalPosition
        {
            get
            {
                StadiumTrackGeometry.Sample sample = _geometry.Evaluate(Lane, _entryMetres + _run.TravelledMetres);
                return new Vector2((float)sample.X, (float)sample.Z);
            }
        }
        private StadiumPreviewSettings _settings;

        private Material _ownedMaterial;

        private LineRenderer _route;
        private LineRenderer _arrow;
        private Animator _animator;
        private bool _hasRunningParameter;
        private bool _hasSpeedParameter;
        private bool _hasPlaybackRateParameter;
        private float _walkSpeed = 1.5f;
        private float _runSpeed = 3f;
        private float _sprintSpeed = 6f;

        private static readonly int RunningParameter = Animator.StringToHash("isRunning");
        private static readonly int SpeedParameter = Animator.StringToHash("speed");
        private static readonly int PlaybackRateParameter = Animator.StringToHash("locomotionRate");
        private double _entryMetres;

        public int Lane { get; private set; }

        // Align the track to the spawn or known marker and create the route preview.
        public bool Initialize(Transform sessionOrigin, Vector3 spawnGroundPosition,
            Vector3 markerWorldPosition, Quaternion markerWorldRotation,
            StadiumPreviewSettings settings, out Vector3 entryGroundPosition)
        {

            entryGroundPosition = spawnGroundPosition;
            _settings = settings;
            PacingMode = settings.PacingMode;
            SetTargetLapMinutes(settings.TargetLapMinutes);

            int referenceLane = Mathf.Clamp(settings.ReferenceLane, 1, StadiumTrackGeometry.LaneCount);
            StadiumTrackGeometry.Sample reference = _geometry.Evaluate(referenceLane, settings.ReferenceMetres);
            Vector3 localDirection = Direction(reference);

            Vector3 worldDirection = Vector3.ProjectOnPlane(
                markerWorldRotation * settings.MarkerLocalRunDirection, Vector3.up);
            if (worldDirection.sqrMagnitude < 0.0001f)
            {

                Debug.LogWarning("[StadiumTracking] Marker heading is vertical/undefined. " +
                    "Set Marker Local Run Direction to a horizontal marker axis (floor tag: Up; wall tag: often Forward).");
                return false;
            }

            worldDirection = Quaternion.AngleAxis(settings.HeadingOffsetDegrees, Vector3.up) * worldDirection.normalized;

            Quaternion rotation = Quaternion.LookRotation(worldDirection, Vector3.up) *
                Quaternion.Inverse(Quaternion.LookRotation(localDirection, Vector3.up));
            Vector3 referencePoint = Position(reference);

            Vector3 anchor = spawnGroundPosition;
            if (settings.AnchorMode == StadiumAnchorMode.KnownMarkerPosition)
            {

                anchor = new Vector3(markerWorldPosition.x, spawnGroundPosition.y, markerWorldPosition.z);

                referencePoint += new Vector3(settings.MarkerOffsetFromReference.x, 0, settings.MarkerOffsetFromReference.y);
            }

            transform.SetParent(sessionOrigin, false);

            transform.SetPositionAndRotation(anchor - rotation * referencePoint, rotation);

            Vector3 localSpawn = transform.InverseTransformPoint(spawnGroundPosition);
            Lane = _geometry.DetectLane(localSpawn.x, localSpawn.z);

            if (Lane == 0)
            {
                Lane = referenceLane;
                Debug.LogWarning($"[StadiumTracking] Spawn is outside the oval lanes; projecting onto configured lane {Lane}.");
            }

            _run = new StadiumRunProgress(_geometry.Length(Lane));

            _entryMetres = _geometry.Project(Lane, localSpawn.x, localSpawn.z);
            entryGroundPosition = transform.TransformPoint(Position(_geometry.Evaluate(Lane, _entryMetres)));
            if (Vector3.Distance(entryGroundPosition, spawnGroundPosition) > Mathf.Max(0.1f, settings.MaxEntryCorrection))
            {

                Debug.LogWarning("[StadiumTracking] Entry is too far from spawn. Check marker calibration or use AvatarSpawnPreview.");
                return false;
            }

            Material material = settings.LineMaterial;
            if (material == null)
            {
                Shader shader = Resources.Load<Shader>("StadiumRoute");
                if (shader == null)
                {
                    Debug.LogError("[StadiumTracking] StadiumRoute shader is missing.");
                    return false;
                }
                material = _ownedMaterial = new Material(shader);
            }

            _route = CreateLine("Lap route", material, settings.RouteColor, settings.LineWidth);
            _arrow = CreateLine("Running direction", material, settings.DirectionColor, settings.LineWidth * 1.5f);
            DrawRoute();
            Debug.Log($"[StadiumTracking] {settings.AnchorMode}: lane {Lane}, " +
                $"entry {_entryMetres:F2} m, lap {_geometry.Length(Lane):F3} m. " +
                "Cyan = one full lap, yellow = running direction. Use the speed slider to start; zero pauses.");
            return true;
        }

        // Apply the current path heading and animation; the tracker sets the final position.
        public void AlignAvatar(Transform avatar, Animator animator, Vector3 groundPosition)
        {

            Vector3 position = transform.position;
            position.y = groundPosition.y;
            transform.position = position;

            Vector3 tangent = transform.TransformDirection(Direction(_geometry.Evaluate(Lane, _entryMetres + _run.TravelledMetres)));
            avatar.rotation = Quaternion.LookRotation(tangent, Vector3.up);
            if (animator == null)
                return;
            if (_animator != animator)
            {

                _animator = animator;
                _hasRunningParameter = _hasSpeedParameter = _hasPlaybackRateParameter = false;
                foreach (AnimatorControllerParameter parameter in animator.parameters)
                {
                    _hasRunningParameter |= parameter.nameHash == RunningParameter && parameter.type == AnimatorControllerParameterType.Bool;
                    _hasSpeedParameter |= parameter.nameHash == SpeedParameter && parameter.type == AnimatorControllerParameterType.Float;
                    _hasPlaybackRateParameter |= parameter.nameHash == PlaybackRateParameter && parameter.type == AnimatorControllerParameterType.Float;
                }
                CalibrateLocomotion(animator);
            }

            animator.applyRootMotion = false;
            LocomotionTiming.Evaluate(_animationSpeedFraction * SpeedAtFullSlider,
                _walkSpeed, _runSpeed, _sprintSpeed, out float blend, out float playbackRate);
            if (_hasRunningParameter)
                animator.SetBool(RunningParameter, _animationSpeedFraction > 0f);
            if (_hasSpeedParameter)
                animator.SetFloat(SpeedParameter, _hasPlaybackRateParameter ? blend : _animationSpeedFraction);
            if (_hasPlaybackRateParameter)
                animator.SetFloat(PlaybackRateParameter, playbackRate);
        }

        private void CalibrateLocomotion(Animator animator)
        {
            // Root motion is disabled for placement, but imported clip velocities still
            // tell us the speed at which each gait's steps were recorded.
            _walkSpeed = 1.5f;
            _runSpeed = 3f;
            _sprintSpeed = 6f;
            if (animator.runtimeAnimatorController == null)
                return;
            foreach (AnimationClip clip in animator.runtimeAnimatorController.animationClips)
            {
                Vector3 velocity = clip.averageSpeed;
                float speed = new Vector2(velocity.x, velocity.z).magnitude;
                if (float.IsNaN(speed) || float.IsInfinity(speed) || speed < 0.1f)
                    continue; // In-place clips need the nominal gait speeds above.
                switch (clip.name)
                {
                    case "walking": _walkSpeed = speed; break;
                    case "runninig": _runSpeed = speed; break;
                    case "sprint": _sprintSpeed = speed; break;
                }
            }
            // Preserve a valid ordering if an imported clip has no useful trajectory.
            if (_walkSpeed >= _runSpeed || _runSpeed >= _sprintSpeed)
            {
                _walkSpeed = 1.5f;
                _runSpeed = 3f;
                _sprintSpeed = 6f;
            }
        }

        // Advance continuously around the selected lane and return the ground position.
        public Vector3 AdvanceRun(float deltaTime)
        {
            _run.Advance(RequestedSpeedMetresPerSecond, deltaTime);
            _animationSpeedFraction = (float)(_run.CurrentSpeed / SpeedAtFullSlider);
            return transform.TransformPoint(Position(_geometry.Evaluate(Lane, _entryMetres + _run.TravelledMetres)));
        }

        // Draw a complete lap from the entry point and a short direction arrow.
        private void DrawRoute()
        {

            const int segments = 800;
            _route.positionCount = segments + 1;
            for (int i = 0; i <= segments; i++)
                _route.SetPosition(i, Raised(_geometry.Evaluate(Lane, _entryMetres + _geometry.Length(Lane) * i / segments)));

            StadiumTrackGeometry.Sample tipSample = _geometry.Evaluate(Lane, _entryMetres + 3);
            Vector3 tip = Raised(tipSample);
            Vector3 tangent = Direction(tipSample);
            Vector3 side = Vector3.Cross(Vector3.up, tangent);

            _arrow.positionCount = 5;
            _arrow.SetPosition(0, Raised(_geometry.Evaluate(Lane, _entryMetres)));
            _arrow.SetPosition(1, tip);
            _arrow.SetPosition(2, tip - tangent * 0.8f + side * 0.4f);
            _arrow.SetPosition(3, tip);
            _arrow.SetPosition(4, tip - tangent * 0.8f - side * 0.4f);
        }

        private LineRenderer CreateLine(string label, Material material, Color color, float width)
        {
            var child = new GameObject(label);
            child.layer = gameObject.layer;
            child.transform.SetParent(transform, false);
            var line = child.AddComponent<LineRenderer>();

            line.sharedMaterial = material;

            line.useWorldSpace = false;
            line.loop = false;
            line.startWidth = line.endWidth = Mathf.Max(0.01f, width);
            line.startColor = line.endColor = color;
            line.numCapVertices = 3;

            line.shadowCastingMode = ShadowCastingMode.Off;
            line.receiveShadows = false;
            return line;
        }

        private Vector3 Raised(StadiumTrackGeometry.Sample sample) =>
            Position(sample) + Vector3.up * Mathf.Max(0.01f, _settings.HeightAboveGround);

        private static Vector3 Position(StadiumTrackGeometry.Sample sample) => new Vector3((float)sample.X, 0, (float)sample.Z);
        private static Vector3 Direction(StadiumTrackGeometry.Sample sample) => new Vector3((float)sample.DirectionX, 0, (float)sample.DirectionZ);

        private void OnDestroy()
        {

            if (_ownedMaterial != null)
                Destroy(_ownedMaterial);
        }
    }
}
