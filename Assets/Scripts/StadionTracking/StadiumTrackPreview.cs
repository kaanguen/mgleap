using UnityEngine;
using UnityEngine.Rendering;

namespace StadionTracking
{

    public sealed class StadiumTrackPreview : MonoBehaviour
    {

        private readonly StadiumTrackGeometry _geometry = new StadiumTrackGeometry();
        private readonly StadiumRunProgress _run = new StadiumRunProgress();
        private float _animationSpeedFraction;

        public float RequestedSpeedFraction { get; private set; }
        public float SpeedAtFullSlider => Mathf.Max(0.1f, _settings.SpeedAtFullSlider);

        // Store this runner's slider input; finished runs remain stopped.
        public void SetRequestedSpeed(float fraction)
        {
            RequestedSpeedFraction = _run.IsFinished ? 0f : Mathf.Clamp01(fraction);
        }
        public bool IsFinished => _run.IsFinished;
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

        private static readonly int RunningParameter = Animator.StringToHash("isRunning");
        private static readonly int SpeedParameter = Animator.StringToHash("speed");
        private double _entryMetres;

        public int Lane { get; private set; }

        // Align the track to the spawn or known marker and create the route preview.
        public bool Initialize(Transform sessionOrigin, Vector3 spawnGroundPosition,
            Vector3 markerWorldPosition, Quaternion markerWorldRotation,
            StadiumPreviewSettings settings, out Vector3 entryGroundPosition)
        {

            entryGroundPosition = spawnGroundPosition;
            _settings = settings;

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

            _route = CreateLine("400m route", material, settings.RouteColor, settings.LineWidth);
            _arrow = CreateLine("Running direction", material, settings.DirectionColor, settings.LineWidth * 1.5f);
            DrawRoute();
            Debug.Log($"[StadiumTracking] {settings.AnchorMode}: lane {Lane}, " +
                $"entry {_entryMetres:F2} m, lap {_geometry.Length(Lane):F3} m. " +
                "Cyan = 400 metres, yellow = running direction. Use the speed slider to start; zero pauses.");
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
                _hasRunningParameter = _hasSpeedParameter = false;
                foreach (AnimatorControllerParameter parameter in animator.parameters)
                {
                    _hasRunningParameter |= parameter.nameHash == RunningParameter && parameter.type == AnimatorControllerParameterType.Bool;
                    _hasSpeedParameter |= parameter.nameHash == SpeedParameter && parameter.type == AnimatorControllerParameterType.Float;
                }
            }

            animator.applyRootMotion = false;
            if (_hasRunningParameter)
                animator.SetBool(RunningParameter, _animationSpeedFraction > 0f);
            if (_hasSpeedParameter)
                animator.SetFloat(SpeedParameter, _animationSpeedFraction);
        }

        // Advance toward 400 metres and return the next world-space ground position.
        public Vector3 AdvanceRun(float deltaTime)
        {
            bool wasFinished = _run.IsFinished;
            float fraction = RequestedSpeedFraction;
            _run.Advance(fraction * Mathf.Max(0.1f, _settings.SpeedAtFullSlider), deltaTime);
            _animationSpeedFraction = _run.IsFinished ? 0f : fraction;
            if (_run.IsFinished)
                RequestedSpeedFraction = 0f;
            if (!wasFinished && _run.IsFinished)
                Debug.Log($"[StadiumTracking] Finished 400 metres on lane {Lane}. Avatar stopped.");
            return transform.TransformPoint(Position(_geometry.Evaluate(Lane, _entryMetres + _run.TravelledMetres)));
        }

        // Draw 400 metres from the entry point and a short direction arrow.
        private void DrawRoute()
        {

            const int segments = 800;
            _route.positionCount = segments + 1;
            for (int i = 0; i <= segments; i++)
                _route.SetPosition(i, Raised(_geometry.Evaluate(Lane, _entryMetres + 400.0 * i / segments)));

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
