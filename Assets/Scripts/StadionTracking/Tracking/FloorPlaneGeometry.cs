using UnityEngine;

namespace StadionTracking
{
    internal static class FloorPlaneGeometry
    {
        // Callers accept floor normals with a nonzero vertical component before projecting.
        internal static float HeightAtXZ(Vector3 point, Vector3 planePoint, Vector3 normal) =>
            planePoint.y - (normal.x * (point.x - planePoint.x) +
                normal.z * (point.z - planePoint.z)) / normal.y;

        internal static Vector3 UpwardNormal(Transform surface) =>
            surface.up.y < 0f ? -surface.up : surface.up;
    }
}
