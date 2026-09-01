using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Lilithe.Tools
{
    public enum UvUnwrapMode
    {
        Standard,
        CubeProjection,
        MinimizeStretch
    }

    public static class TextureUvUtility
    {
        public static void GenerateUvLayout(Mesh mesh, UvUnwrapMode unwrapMode)
        {
            if (mesh == null)
            {
                return;
            }

            if (unwrapMode == UvUnwrapMode.Standard)
            {
                Unwrapping.GenerateSecondaryUVSet(mesh);
                if (mesh.uv2 != null && mesh.uv2.Length == mesh.vertexCount)
                {
                    mesh.SetUVs(0, mesh.uv2);
                }
                return;
            }

            if (unwrapMode == UvUnwrapMode.CubeProjection)
            {
                GenerateCubeProjectionUvLayout(mesh);
                return;
            }

            Vector3[] vertices = mesh.vertices;
            if (vertices == null || vertices.Length == 0)
            {
                return;
            }

            Bounds bounds = mesh.bounds;
            Vector3 center = bounds.center;
            Vector3 extents = bounds.extents;
            Vector2[] uvs = new Vector2[vertices.Length];

            for (int i = 0; i < vertices.Length; i++)
            {
                Vector3 local = vertices[i] - center;
                uvs[i] = ProjectVertexToUv(local, extents, unwrapMode);
            }

            mesh.SetUVs(0, uvs);
        }

        private static void GenerateCubeProjectionUvLayout(Mesh mesh)
        {
            Vector3[] vertices = mesh.vertices;
            if (vertices == null || vertices.Length == 0)
            {
                return;
            }

            int subMeshCount = Mathf.Max(1, mesh.subMeshCount);
            Vector3[] normals = mesh.normals;
            bool hasNormals = normals != null && normals.Length == vertices.Length;

            Bounds bounds = mesh.bounds;
            Vector3 center = bounds.center;
            Vector3 extents = bounds.extents;

            var newVertices = new List<Vector3>();
            var newUvs = new List<Vector2>();
            var newNormals = hasNormals ? new List<Vector3>() : null;
            var newTrianglesBySubmesh = new List<int>[subMeshCount];

            for (int subMesh = 0; subMesh < subMeshCount; subMesh++)
            {
                int[] triangles = mesh.GetTriangles(subMesh);
                var submeshTriangles = new List<int>(triangles.Length);

                for (int i = 0; i + 2 < triangles.Length; i += 3)
                {
                    int aIndex = triangles[i];
                    int bIndex = triangles[i + 1];
                    int cIndex = triangles[i + 2];

                    Vector3 a = vertices[aIndex];
                    Vector3 b = vertices[bIndex];
                    Vector3 c = vertices[cIndex];
                    BoxFace face = GetBoxFace(a - center, b - center, c - center);

                    AppendProjectedVertex(aIndex, a, face, center, extents, vertices, normals, hasNormals, newVertices, newUvs, newNormals, submeshTriangles);
                    AppendProjectedVertex(bIndex, b, face, center, extents, vertices, normals, hasNormals, newVertices, newUvs, newNormals, submeshTriangles);
                    AppendProjectedVertex(cIndex, c, face, center, extents, vertices, normals, hasNormals, newVertices, newUvs, newNormals, submeshTriangles);
                }

                newTrianglesBySubmesh[subMesh] = submeshTriangles;
            }

            mesh.Clear();
            mesh.vertices = newVertices.ToArray();
            if (newNormals != null)
            {
                mesh.normals = newNormals.ToArray();
            }

            mesh.uv = newUvs.ToArray();
            mesh.subMeshCount = subMeshCount;
            for (int subMesh = 0; subMesh < subMeshCount; subMesh++)
            {
                mesh.SetTriangles(newTrianglesBySubmesh[subMesh].ToArray(), subMesh);
            }

            mesh.RecalculateBounds();
        }

        public static Vector3? GetWorldPositionFromUV(GameObject targetObject, Renderer renderer, Vector2 uv)
        {
            if (targetObject == null || renderer == null)
            {
                return null;
            }

            MeshFilter filter = renderer.GetComponent<MeshFilter>();
            if (filter == null || filter.sharedMesh == null)
            {
                return null;
            }

            Mesh mesh = filter.sharedMesh;
            if (mesh.uv == null || mesh.uv.Length == 0)
            {
                return null;
            }

            Vector3[] vertices = mesh.vertices;
            int[] triangles = mesh.triangles;

            for (int i = 0; i + 2 < triangles.Length; i += 3)
            {
                int aIndex = triangles[i];
                int bIndex = triangles[i + 1];
                int cIndex = triangles[i + 2];

                Vector2 aUv = mesh.uv[aIndex];
                Vector2 bUv = mesh.uv[bIndex];
                Vector2 cUv = mesh.uv[cIndex];

                if (PointInTriangleUV(uv, aUv, bUv, cUv))
                {
                    Vector3 a = vertices[aIndex];
                    Vector3 b = vertices[bIndex];
                    Vector3 c = vertices[cIndex];
                    Vector3 bary = Barycentric(uv, aUv, bUv, cUv);
                    Vector3 world = a * bary.x + b * bary.y + c * bary.z;
                    return targetObject.transform.TransformPoint(world);
                }
            }

            return null;
        }

        private static Vector2 ProjectVertexToUv(Vector3 localPosition, Vector3 extents, UvUnwrapMode unwrapMode)
        {
            Vector3 safeExtents = new Vector3(
                Mathf.Max(0.0001f, extents.x),
                Mathf.Max(0.0001f, extents.y),
                Mathf.Max(0.0001f, extents.z));

            switch (unwrapMode)
            {
                case UvUnwrapMode.CubeProjection:
                    return CubeProjectionUv(localPosition, safeExtents);
                case UvUnwrapMode.MinimizeStretch:
                    return MinimizeStretchUv(localPosition, safeExtents);
                default:
                    return new Vector2(localPosition.x / (2f * safeExtents.x) + 0.5f, localPosition.y / (2f * safeExtents.y) + 0.5f);
            }
        }

        private static void AppendProjectedVertex(
            int sourceIndex,
            Vector3 sourceVertex,
            BoxFace face,
            Vector3 center,
            Vector3 extents,
            Vector3[] sourceVertices,
            Vector3[] sourceNormals,
            bool hasNormals,
            List<Vector3> newVertices,
            List<Vector2> newUvs,
            List<Vector3> newNormals,
            List<int> newTriangles)
        {
            Vector3 local = sourceVertex - center;
            newVertices.Add(sourceVertex);
            newUvs.Add(ProjectBoxFaceUv(local, extents, face));

            if (hasNormals && newNormals != null)
            {
                newNormals.Add(sourceNormals[sourceIndex]);
            }

            newTriangles.Add(newVertices.Count - 1);
        }

        private static Vector2 ProjectBoxFaceUv(Vector3 local, Vector3 extents, BoxFace face)
        {
            Vector2 uv;
            switch (face)
            {
                case BoxFace.PositiveX:
                    uv = new Vector2(Normalize(local.z, extents.z), Normalize(local.y, extents.y));
                    break;
                case BoxFace.NegativeX:
                    uv = new Vector2(1f - Normalize(local.z, extents.z), Normalize(local.y, extents.y));
                    break;
                case BoxFace.PositiveY:
                    uv = new Vector2(Normalize(local.x, extents.x), Normalize(local.z, extents.z));
                    break;
                case BoxFace.NegativeY:
                    uv = new Vector2(Normalize(local.x, extents.x), 1f - Normalize(local.z, extents.z));
                    break;
                case BoxFace.PositiveZ:
                    uv = new Vector2(Normalize(local.x, extents.x), Normalize(local.y, extents.y));
                    break;
                default:
                    uv = new Vector2(1f - Normalize(local.x, extents.x), Normalize(local.y, extents.y));
                    break;
            }

            Rect tile = GetCubeAtlasTile(face);
            return new Vector2(
                tile.xMin + uv.x * tile.width,
                tile.yMin + uv.y * tile.height);
        }

        private static Rect GetCubeAtlasTile(BoxFace face)
        {
            const float columnWidth = 1f / 3f;
            const float rowHeight = 1f / 2f;

            switch (face)
            {
                case BoxFace.PositiveX:
                    return new Rect(0f * columnWidth, 0f * rowHeight, columnWidth, rowHeight);
                case BoxFace.NegativeX:
                    return new Rect(1f * columnWidth, 0f * rowHeight, columnWidth, rowHeight);
                case BoxFace.PositiveY:
                    return new Rect(2f * columnWidth, 0f * rowHeight, columnWidth, rowHeight);
                case BoxFace.NegativeY:
                    return new Rect(0f * columnWidth, 1f * rowHeight, columnWidth, rowHeight);
                case BoxFace.PositiveZ:
                    return new Rect(1f * columnWidth, 1f * rowHeight, columnWidth, rowHeight);
                default:
                    return new Rect(2f * columnWidth, 1f * rowHeight, columnWidth, rowHeight);
            }
        }

        private static BoxFace GetBoxFace(Vector3 localA, Vector3 localB, Vector3 localC)
        {
            Vector3 normal = Vector3.Cross(localB - localA, localC - localA);
            float absX = Mathf.Abs(normal.x);
            float absY = Mathf.Abs(normal.y);
            float absZ = Mathf.Abs(normal.z);

            if (absX >= absY && absX >= absZ)
            {
                return normal.x >= 0f ? BoxFace.PositiveX : BoxFace.NegativeX;
            }

            if (absY >= absX && absY >= absZ)
            {
                return normal.y >= 0f ? BoxFace.PositiveY : BoxFace.NegativeY;
            }

            return normal.z >= 0f ? BoxFace.PositiveZ : BoxFace.NegativeZ;
        }

        private static float Normalize(float value, float extent)
        {
            return value / (Mathf.Max(0.0001f, extent) * 2f) + 0.5f;
        }

        private enum BoxFace
        {
            PositiveX,
            NegativeX,
            PositiveY,
            NegativeY,
            PositiveZ,
            NegativeZ
        }

        private static Vector2 CubeProjectionUv(Vector3 local, Vector3 extents)
        {
            float absX = Mathf.Abs(local.x);
            float absY = Mathf.Abs(local.y);
            float absZ = Mathf.Abs(local.z);

            float xNorm = local.x / Mathf.Max(extents.x, 0.0001f);
            float yNorm = local.y / Mathf.Max(extents.y, 0.0001f);
            float zNorm = local.z / Mathf.Max(extents.z, 0.0001f);

            if (absX >= absY && absX >= absZ)
            {
                float u = (local.z / Mathf.Max(extents.z, 0.0001f)) * 0.5f + 0.5f;
                float v = (local.y / Mathf.Max(extents.y, 0.0001f)) * 0.5f + 0.5f;
                return local.x >= 0f ? new Vector2(u, v) : new Vector2(1f - u, v);
            }

            if (absY >= absX && absY >= absZ)
            {
                float u = (local.x / Mathf.Max(extents.x, 0.0001f)) * 0.5f + 0.5f;
                float v = (local.z / Mathf.Max(extents.z, 0.0001f)) * 0.5f + 0.5f;
                return local.y >= 0f ? new Vector2(u, v) : new Vector2(1f - u, 1f - v);
            }

            float u2 = (local.x / Mathf.Max(extents.x, 0.0001f)) * 0.5f + 0.5f;
            float v2 = (local.y / Mathf.Max(extents.y, 0.0001f)) * 0.5f + 0.5f;
            return local.z >= 0f ? new Vector2(u2, v2) : new Vector2(1f - u2, v2);
        }

        private static Vector2 MinimizeStretchUv(Vector3 local, Vector3 extents)
        {
            float absX = Mathf.Abs(local.x);
            float absY = Mathf.Abs(local.y);
            float absZ = Mathf.Abs(local.z);

            if (absX >= absY && absX >= absZ)
            {
                return new Vector2(local.y / (2f * extents.y) + 0.5f, local.z / (2f * extents.z) + 0.5f);
            }

            if (absY >= absX && absY >= absZ)
            {
                return new Vector2(local.x / (2f * extents.x) + 0.5f, local.z / (2f * extents.z) + 0.5f);
            }

            return new Vector2(local.x / (2f * extents.x) + 0.5f, local.y / (2f * extents.y) + 0.5f);
        }

        public static bool PointInTriangleUV(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
        {
            const float epsilon = 0.000001f;
            float d1 = SignedArea2D(p, a, b);
            float d2 = SignedArea2D(p, b, c);
            float d3 = SignedArea2D(p, c, a);

            bool hasNeg = d1 < -epsilon || d2 < -epsilon || d3 < -epsilon;
            bool hasPos = d1 > epsilon || d2 > epsilon || d3 > epsilon;
            return !(hasNeg && hasPos);
        }

        private static float SignedArea2D(Vector2 p1, Vector2 p2, Vector2 p3)
        {
            return (p1.x - p3.x) * (p2.y - p3.y) - (p2.x - p3.x) * (p1.y - p3.y);
        }

        public static Vector3 Barycentric(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
        {
            float denom = ((b.y - c.y) * (a.x - c.x) + (c.x - b.x) * (a.y - c.y));
            if (Mathf.Abs(denom) < 0.00001f)
            {
                return new Vector3(1f, 0f, 0f);
            }

            float w1 = ((b.y - c.y) * (p.x - c.x) + (c.x - b.x) * (p.y - c.y)) / denom;
            float w2 = ((c.y - a.y) * (p.x - c.x) + (a.x - c.x) * (p.y - c.y)) / denom;
            float w3 = 1f - w1 - w2;
            return new Vector3(w1, w2, w3);
        }
    }
}
