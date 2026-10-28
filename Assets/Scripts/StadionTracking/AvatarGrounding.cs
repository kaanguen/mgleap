using System.Collections.Generic;
using UnityEngine;

namespace StadionTracking
{
    internal static class AvatarGrounding
    {
        /// <summary>World-space distance from the avatar root to its lowest visible mesh point.</summary>
        internal static float MeasureSoleOffset(GameObject avatar)
        {
            if (TryGetLowestSkinnedPoint(avatar, out Vector3 point))
                return point.y - avatar.transform.position.y;
            float lowest = float.PositiveInfinity;
            foreach (var renderer in avatar.GetComponentsInChildren<MeshRenderer>())
                lowest = Mathf.Min(lowest, renderer.bounds.min.y);
            return float.IsPositiveInfinity(lowest) ? 0f : lowest - avatar.transform.position.y;
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
