using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

namespace StadionTracking
{
    public enum CollectibleSpawnSource { Fixed, OxygenSensor }
    /// <summary>Distance-driven pickup rows shared by the straight treadmill and the stadium oval.</summary>
    public sealed class TrackCollectibles : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private StraightTreadmillWorld world;
        [SerializeField, Tooltip("Replace this with any 3D prefab. No pickup script, trigger or Rigidbody is required.")]
        private GameObject collectiblePrefab;
        [Header("Spawn amount")]
        [SerializeField] private CollectibleSpawnSource spawnSource = CollectibleSpawnSource.Fixed;
        [SerializeField] private SensorMeasurements oxygenSensor;
        [SerializeField, Range(0, 100), Tooltip("Game mapping only: oxygen at this value produces the minimum row amount.")]
        private float oxygenMinimum = 90f;
        [SerializeField, Range(0, 100)] private float oxygenMaximum = 100f;
        [SerializeField, Range(0, 30)] private int minimumSensorCount = 2;
        [SerializeField, Range(0, 30)] private int maximumSensorCount = 20;
        [SerializeField, Min(.01f)] private float visualScale = 1f;

        [Header("Rows on the track")]
        [SerializeField, Range(1, 8)] private int lane = 1;
        [SerializeField, Min(5f)] private float visibleDistance = 55f;
        [SerializeField, Min(1f)] private float firstSpawnDistance = 6f;
        [SerializeField, Min(.5f)] private float spacing = 1.8f;
        [SerializeField, Range(0, 30)] private int pickupsPerRow = 10;
        [SerializeField, Min(0f)] private float rowGap = 5f;

        [Header("Floating wave (metres)")]
        [SerializeField, Min(.2f)] private float hoverHeight = 1.05f;
        [SerializeField, Min(0f)] private float waveHeight = .25f;
        [SerializeField, Range(0f, .4f)] private float sidewaysWave = .22f;
        [SerializeField, Min(1f)] private float wavelength = 12f;
        [SerializeField, Min(0f)] private float bobHeight = .07f;
        [SerializeField, Min(0f)] private float bobFrequency = .65f;
        [SerializeField] private float spinDegreesPerSecond = 55f;

        [Header("Collection")]
        [SerializeField, Min(.1f), Tooltip("Allowed lateral distance from the pickup row; height is intentionally forgiving for XR.")]
        private float collectionRadius = .85f;
        [SerializeField, Min(.2f)] private float magnetDistance = 2.4f;
        [SerializeField] private UnityEvent onCollected = new UnityEvent();
        public int CollectedCount { get; private set; }
        public event Action<int> Collected;
        public GameObject CollectiblePrefab => collectiblePrefab;
        public int ActiveCount => active.Count;
        public CollectibleSpawnSource SpawnSource => spawnSource;
        public int FixedCount => pickupsPerRow;
        public double? OxygenPercent => oxygenSensor != null && oxygenSensor.TryGetOxygenPercent(out double value) ? value : (double?)null;
        public bool OxygenReady => OxygenPercent.HasValue;
        public int CurrentRowCount => spawnSource == CollectibleSpawnSource.Fixed ? Mathf.Clamp(pickupsPerRow, 0, 30)
            : OxygenPercent.HasValue ? MapOxygenToCount(OxygenPercent.Value) : 0;
        public int MapOxygenToCount(double percent)
        {
            if (double.IsNaN(percent) || double.IsInfinity(percent) || percent <= 0 || percent > 100) return 0;
            float t = Mathf.InverseLerp(oxygenMinimum, Mathf.Max(oxygenMinimum + .01f, oxygenMaximum), (float)percent);
            return Mathf.Clamp(Mathf.RoundToInt(Mathf.Lerp(minimumSensorCount, Mathf.Max(minimumSensorCount, maximumSensorCount), t)), 0, 30);
        }
        public void ConfigureSpawning(CollectibleSpawnSource source, int fixedCount)
        {
            if (!Enum.IsDefined(typeof(CollectibleSpawnSource), source)) throw new ArgumentOutOfRangeException(nameof(source));
            if (fixedCount < 0 || fixedCount > 30) throw new ArgumentOutOfRangeException(nameof(fixedCount));
            spawnSource = source; pickupsPerRow = fixedCount;
            if (source == CollectibleSpawnSource.OxygenSensor) ResolveOxygenSensor();
        }

        private sealed class Pickup
        {
            public GameObject Visual;
            public Quaternion BaseRotation;
            public double Distance;
        }
        private readonly List<Pickup> active = new List<Pickup>();
        private readonly Stack<Pickup> pool = new Stack<Pickup>();
        private Transform container;
        private double nextDistance, previousProgress;
        private int indexInRow, placementRevision;
        private bool initialized, previousRealMode;
        private float animationTime;
        private GameObject currentPrefab;
        private int currentRowCount = -1;
        private double rowOrigin, rowStart;
        private float nextSensorSearch;
        private double RowSpan => Mathf.Max(.5f, spacing) * 9;
        private double RowPeriod => RowSpan + Mathf.Max(.5f, spacing) + Mathf.Max(0, rowGap);

        private void Awake()
        {
            if (world == null) world = GetComponent<StraightTreadmillWorld>();
        }

        private void LateUpdate() => Tick(Time.deltaTime);

        private void ResolveOxygenSensor()
        {
            if (oxygenSensor == null)
                foreach (var candidate in FindObjectsByType<SensorMeasurements>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                    if (candidate.gameObject.scene == gameObject.scene) { oxygenSensor = candidate; break; }
            // The existing sensor canvas starts hidden in the running scene. Open its scan/connect
            // controls when selecting sensor mode, so the BLE component can actually receive packets.
            if (oxygenSensor != null && spawnSource == CollectibleSpawnSource.OxygenSensor)
            {
                oxygenSensor.gameObject.SetActive(true);
                oxygenSensor.enabled = true;
            }
        }

        // Public to allow deterministic checks without an XR headset or a running physics simulation.
        public void Tick(float elapsedSeconds)
        {
            if (oxygenSensor == null && Time.realtimeSinceStartup >= nextSensorSearch)
            {
                ResolveOxygenSensor();
                nextSensorSearch = Time.realtimeSinceStartup + 1;
            }
            if (world == null || collectiblePrefab == null)
            {
                ClearVisuals();
                initialized = false;
                return;
            }
            double progress = world.UserDistanceAlongTrack;
            if (!initialized || progress < previousProgress - .01 || placementRevision != world.MarkerPlacementRevision ||
                previousRealMode != world.RealTrackMode || currentPrefab != collectiblePrefab)
                ResetPickups();
            bool visible = world.PlacementReady && world.HeightConfirmed && world.ShowTrack && world.HeadPoseReady;
            if (container != null) container.gameObject.SetActive(visible && isActiveAndEnabled);
            if (!visible) { previousProgress = progress; return; }
            if (!world.SessionPaused && !float.IsNaN(elapsedSeconds) && !float.IsInfinity(elapsedSeconds))
                animationTime += Mathf.Max(0, elapsedSeconds);

            int count = CurrentRowCount;
            if (count != currentRowCount || nextDistance < progress - 2)
            {
                foreach (var pickup in active) { pickup.Visual.SetActive(false); pool.Push(pickup); }
                active.Clear();
                currentRowCount = count;
                // Keep rows anchored when the sensor changes; they must not retreat from the runner.
                rowStart = rowOrigin + Math.Max(0, Math.Floor((progress - rowOrigin) / RowPeriod)) * RowPeriod;
                nextDistance = rowStart; indexInRow = 0;
            }
            while (count > 0 && nextDistance <= progress + Mathf.Max(5, visibleDistance))
            {
                if (nextDistance > progress + .01) Spawn(nextDistance);
                if (++indexInRow >= count)
                { indexInRow = 0; rowStart += RowPeriod; }
                nextDistance = rowStart + (count > 1 ? RowSpan * indexInRow / (count - 1) : 0);
            }

            // Compare with the local lane at the player, so bends and lane changes stay correct.
            world.GetLanePoseAtDistance(progress, lane, out Vector3 playerGround, out Quaternion playerRotation);
            Vector3 head = world.HeadCamera.transform.position;
            float lateral = Mathf.Abs(Vector3.Dot(head - playerGround, playerRotation * Vector3.right));
            bool inReach = lateral <= collectionRadius;
            for (int i = active.Count - 1; i >= 0; i--)
            {
                var pickup = active[i];
                double ahead = pickup.Distance - progress;
                world.GetLanePoseAtDistance(pickup.Distance, lane, out Vector3 ground, out Quaternion rotation);
                float phase = (float)(pickup.Distance % Math.Max(1, wavelength)) * Mathf.PI * 2 / Mathf.Max(1, wavelength);
                Vector3 position = ground + rotation * new Vector3(Mathf.Sin(phase) * sidewaysWave,
                    hoverHeight + Mathf.Sin(phase) * waveHeight + Mathf.Sin(animationTime * bobFrequency * Mathf.PI * 2 + phase) * bobHeight, 0);

                if (!world.SessionPaused && inReach && ahead <= magnetDistance && ahead >= 0)
                {
                    float attraction = 1 - (float)ahead / Mathf.Max(.2f, magnetDistance);
                    Vector3 chest = head - playerRotation * Vector3.up * .45f;
                    position = Vector3.Lerp(position, chest, attraction * attraction);
                }
                pickup.Visual.transform.SetPositionAndRotation(position,
                    rotation * Quaternion.Euler(0, animationTime * spinDegreesPerSecond, 0) * pickup.BaseRotation);
                // Swept progress prevents missed pickups at high speed or on a slow frame.
                bool collected = !world.SessionPaused && inReach && pickup.Distance >= previousProgress - .01 && ahead <= 0;
                if (collected || ahead < -2)
                {
                    pickup.Visual.SetActive(false);
                    pool.Push(pickup);
                    active.RemoveAt(i);
                    if (collected)
                    {
                        CollectedCount++;
                        Collected?.Invoke(CollectedCount);
                        onCollected.Invoke();
                    }
                }
            }
            previousProgress = progress;
        }

        public void ResetPickups()
        {
            ClearVisuals();
            if (world == null) return;
            currentPrefab = collectiblePrefab;
            previousProgress = world.UserDistanceAlongTrack;
            nextDistance = previousProgress + Mathf.Max(1, firstSpawnDistance);
            rowOrigin = rowStart = nextDistance;
            currentRowCount = -1;
            placementRevision = world.MarkerPlacementRevision;
            previousRealMode = world.RealTrackMode;
            indexInRow = 0; animationTime = 0; CollectedCount = 0; initialized = true;
        }

        private void Spawn(double distance)
        {
            if (container == null)
            {
                var root = new GameObject("Track pickups (pooled)");
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root, gameObject.scene);
                container = root.transform; // Independent of the scrolling/wrapping scenery transform.
            }
            Pickup pickup;
            if (pool.Count > 0) pickup = pool.Pop();
            else
            {
                var visual = Instantiate(collectiblePrefab, container);
                visual.transform.localScale *= Mathf.Max(.01f, visualScale);
                pickup = new Pickup { Visual = visual, BaseRotation = visual.transform.localRotation };
            }
            pickup.Distance = distance;
            pickup.Visual.SetActive(true);
            active.Add(pickup);
        }

        private void ClearVisuals()
        {
            active.Clear(); pool.Clear();
            if (container != null)
            {
                container.gameObject.SetActive(false);
                if (Application.isPlaying) Destroy(container.gameObject);
                else DestroyImmediate(container.gameObject);
                container = null;
            }
        }
        private void OnDisable() { if (container != null) container.gameObject.SetActive(false); }
        private void OnDestroy() => ClearVisuals();
    }
}
