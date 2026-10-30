using UnityEngine;

namespace StadionTracking
{
    public sealed class TrackOxygenBubbleBurst : MonoBehaviour
    {
        private LineRenderer line;
        private Vector3 right, up;
        private float age;
        public static void Spawn(Vector3 position, Camera head, Material material)
        {
            var go = new GameObject("Oxygen collection ring");
            if (head != null && Vector3.Distance(position, head.transform.position) < .6f)
                position = head.transform.position + head.transform.forward * Mathf.Max(.6f, head.nearClipPlane + .2f);
            go.transform.position = position;
            var effect = go.AddComponent<TrackOxygenBubbleBurst>();
            effect.right = head != null ? head.transform.right : Vector3.right;
            effect.up = head != null ? head.transform.up : Vector3.up;
            effect.line = go.AddComponent<LineRenderer>();
            effect.line.sharedMaterial = material;
            effect.line.useWorldSpace = true;
            effect.line.loop = true;
            effect.line.positionCount = 24;
            effect.line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            effect.line.receiveShadows = false;
        }
        private void Update()
        {
            age += Time.deltaTime;
            float t = Mathf.Clamp01(age / .22f);
            float radius = Mathf.Lerp(.035f, .16f, t);
            line.widthMultiplier = Mathf.Lerp(.012f, 0, t);
            line.startColor = line.endColor = new Color(.25f, .65f, 1, 1 - t);
            for (int i = 0; i < 24; i++)
            {
                float angle = i * Mathf.PI * 2 / 24;
                line.SetPosition(i, transform.position + (right * Mathf.Cos(angle) + up * Mathf.Sin(angle)) * radius);
            }
            if (t >= 1) Destroy(gameObject);
        }
    }
}
