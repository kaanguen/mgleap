using System.Collections.Generic;
using UnityEngine;

namespace StadionTracking
{
    internal static class AvatarGrounding
    {
        /// <summary>World-space distance from the avatar root to its lowest visible mesh point.</summary>
        internal static float MeasureSoleOffset(GameObject avatar)
        {
            return TryGetLowestPoint(avatar, out Vector3 point)
                ? point.y - avatar.transform.position.y : 0f;
        }

        internal static bool TryGetLowestPoint(GameObject avatar, out Vector3 lowestPoint)
        {
            if (TryGetLowestSkinnedPoint(avatar, out lowestPoint)) return true;
            float lowestHeight = float.PositiveInfinity;
            bool found = false;
            foreach (Renderer renderer in avatar.GetComponentsInChildren<Renderer>(true))
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
                    float height = point.y;
                    if (height >= lowestHeight)
                        continue;
                    lowestHeight = height;
                    lowestPoint = point;
                    found = true;
                }
            }
            return found;
        }

        internal static bool TryGetLowestSkinnedPoint(GameObject avatar, out Vector3 lowestPoint)
        {
            lowestPoint = Vector3.zero;
            float lowest = float.PositiveInfinity;
            bool found = false;
            var mesh = new Mesh();
            var vertices = new List<Vector3>();
            try
            {
                foreach (var skin in avatar.GetComponentsInChildren<SkinnedMeshRenderer>())
                {
                    if (skin.sharedMesh == null) continue;
                    skin.BakeMesh(mesh);
                    mesh.GetVertices(vertices);
                    foreach (var vertex in vertices)
                    {
                        Vector3 point = skin.transform.TransformPoint(vertex);
                        if (point.y >= lowest) continue;
                        lowest = point.y;
                        lowestPoint = point;
                        found = true;
                    }
                }
            }
            finally
            {
                if (Application.isPlaying) Object.Destroy(mesh); else Object.DestroyImmediate(mesh);
            }
            return found;
        }
    }
}
