using System.Collections.Generic;
using UnityEngine;

/// <summary>Vertex-colored marking geometry shared by the straight and oval track generators.</summary>
internal sealed class TrackMeshBuilder
{
    private readonly List<Vector3> vertices = new List<Vector3>();
    private readonly List<Color> colors = new List<Color>();
    private readonly List<int> triangles = new List<int>();

    internal void AddQuad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color color)
    {
        int first = vertices.Count;
        vertices.Add(a); vertices.Add(b); vertices.Add(c); vertices.Add(d);
        for (int i = 0; i < 4; i++) colors.Add(color);
        triangles.Add(first); triangles.Add(first + 1); triangles.Add(first + 2);
        triangles.Add(first); triangles.Add(first + 2); triangles.Add(first + 3);
    }

    internal Mesh CreateMesh(string name)
    {
        var mesh = new Mesh { name = name };
        mesh.SetVertices(vertices);
        mesh.SetColors(colors);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }
}
