using UnityEngine;

namespace StadionTracking
{
    /// <summary>Presentation only. TrackCollectibles owns movement, pooling and collection.</summary>
    public sealed class TrackOxygenBubbleVisual : MonoBehaviour
    {
        [SerializeField] private Transform model;
        [SerializeField] private TrailRenderer flightTrail;
        [SerializeField] private Material collectionMaterial;
        private Vector3 restScale;
        public TrailRenderer FlightTrail => flightTrail;

        private void Awake() { if (model != null) restScale = model.localScale; }
        private void OnEnable() => ClearTrail();
        private void OnDisable() => ClearTrail();

        public void Animate(Camera head, float animationTime, bool attracting, bool paused)
        {
            if (head != null)
            {
                Vector3 view = head.transform.position - transform.position;
                if (view.sqrMagnitude > .0001f)
                    transform.rotation = Quaternion.LookRotation(view, head.transform.up);
            }
            if (model != null)
            {
                // Also works in deterministic editor checks, where Awake is not dispatched.
                if (restScale == Vector3.zero) restScale = model.localScale;
                model.localScale = restScale * (1 + Mathf.Sin(animationTime * 5) * .045f);
            }
            if (flightTrail != null)
            {
                bool emit = attracting && !paused;
                if (emit && !flightTrail.emitting) flightTrail.Clear();
                flightTrail.emitting = emit;
            }
        }

        public void PlayCollected(Camera head)
        {
            if (Application.isPlaying && collectionMaterial != null)
                TrackOxygenBubbleBurst.Spawn(transform.position, head, collectionMaterial);
        }

        private void ClearTrail()
        {
            if (flightTrail == null) return;
            flightTrail.emitting = false;
            flightTrail.Clear();
        }
    }
}
