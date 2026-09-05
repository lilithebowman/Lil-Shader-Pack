using System.IO;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Lilithe.Tools
{
    public static class TexturePainterUtility
    {
        private const int DefaultTextureSize = 1024;

        public static bool ExportUvLayoutPng(GameObject targetObject, Renderer renderer, string filePath, int resolution, Color lineColor)
        {
            if (targetObject == null || renderer == null || string.IsNullOrEmpty(filePath))
            {
                return false;
            }

            MeshFilter filter = renderer.GetComponent<MeshFilter>();
            if (filter == null || filter.sharedMesh == null)
            {
                return false;
            }

            Mesh mesh = filter.sharedMesh;
            Vector2[] uvs = mesh.uv;
            int[] triangles = mesh.triangles;
            if (uvs == null || triangles == null || uvs.Length == 0 || triangles.Length < 3)
            {
                return false;
            }

            int size = Mathf.Clamp(resolution, 64, 8192);
            Color[] pixels = new Color[size * size];
            for (int i = 0; i < pixels.Length; i++)
            {
                pixels[i] = new Color(0f, 0f, 0f, 0f);
            }

            var seenEdges = new HashSet<ulong>();
            for (int i = 0; i + 2 < triangles.Length; i += 3)
            {
                int a = triangles[i];
                int b = triangles[i + 1];
                int c = triangles[i + 2];

                if (a < 0 || b < 0 || c < 0 || a >= uvs.Length || b >= uvs.Length || c >= uvs.Length)
                {
                    continue;
                }

                DrawUniqueEdge(pixels, size, uvs[a], uvs[b], lineColor, seenEdges, a, b);
                DrawUniqueEdge(pixels, size, uvs[b], uvs[c], lineColor, seenEdges, b, c);
                DrawUniqueEdge(pixels, size, uvs[c], uvs[a], lineColor, seenEdges, c, a);
            }

            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false, false);
            texture.SetPixels(pixels);
            texture.Apply();
            byte[] bytes = texture.EncodeToPNG();
            Object.DestroyImmediate(texture);

            string directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllBytes(filePath, bytes);
            return true;
        }

        public static Texture2D CreateTextureAsset(string filePath)
        {
            if (string.IsNullOrEmpty(filePath))
            {
                return null;
            }

            string directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            Texture2D texture = new Texture2D(DefaultTextureSize, DefaultTextureSize, TextureFormat.RGBA32, false, false);
            Color[] pixels = new Color[DefaultTextureSize * DefaultTextureSize];
            Color baseColor = new Color(0.5f, 0.5f, 0.5f, 1f);
            for (int i = 0; i < pixels.Length; i++)
            {
                pixels[i] = baseColor;
            }

            texture.SetPixels(pixels);
            texture.Apply();

            File.WriteAllBytes(filePath, texture.EncodeToPNG());
            AssetDatabase.ImportAsset(filePath);
            AssetDatabase.Refresh();

            TextureImporter importer = AssetImporter.GetAtPath(filePath) as TextureImporter;
            if (importer != null)
            {
                importer.textureType = TextureImporterType.Default;
                importer.isReadable = true;
                importer.wrapMode = TextureWrapMode.Repeat;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.alphaSource = TextureImporterAlphaSource.None;
                importer.SaveAndReimport();
            }

            AssetDatabase.Refresh();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(filePath);
        }

        public static bool SaveTextureAssetToDisk(Texture2D texture, out string error)
        {
            error = null;
            if (texture == null)
            {
                error = "No texture is assigned.";
                return false;
            }

            if (!EnsureTextureReadable(texture))
            {
                error = "The texture is not readable and could not be made readable.";
                return false;
            }

            string assetPath = AssetDatabase.GetAssetPath(texture);
            if (string.IsNullOrEmpty(assetPath))
            {
                error = "The selected texture is not a project asset.";
                return false;
            }

            if (!assetPath.EndsWith(".png", System.StringComparison.OrdinalIgnoreCase))
            {
                error = "Only PNG texture assets are supported by this save action.";
                return false;
            }

            string projectRoot = Path.GetDirectoryName(Application.dataPath);
            string absolutePath = Path.GetFullPath(Path.Combine(projectRoot, assetPath));

            try
            {
                byte[] bytes = texture.EncodeToPNG();
                File.WriteAllBytes(absolutePath, bytes);
                AssetDatabase.ImportAsset(assetPath);
                AssetDatabase.Refresh();
                return true;
            }
            catch (System.Exception ex)
            {
                error = "Failed to write texture to disk: " + ex.Message;
                return false;
            }
        }

        public static void PaintTexture(
            Texture2D texture,
            GameObject targetObject,
            Renderer renderer,
            Vector3 worldCenter,
            Color brushColor,
            float opacity,
            float radius)
        {
            if (texture == null || targetObject == null || renderer == null)
            {
                return;
            }

            EnsureTextureIsReadable(texture);
            if (!texture.isReadable)
            {
                return;
            }

            Color[] pixels = texture.GetPixels();
            if (PaintTexturePixelsWorld(pixels, texture.width, texture.height, targetObject, renderer, worldCenter, brushColor, opacity, radius))
            {
                texture.SetPixels(pixels);
                texture.Apply();
                EditorUtility.SetDirty(texture);
            }
        }

        public static bool PaintTexturePixelsWorld(
            Color[] pixels,
            int width,
            int height,
            GameObject targetObject,
            Renderer renderer,
            Vector3 worldCenter,
            Color brushColor,
            float opacity,
            float radius)
        {
            if (pixels == null || pixels.Length != width * height || targetObject == null || renderer == null)
            {
                return false;
            }

            MeshFilter filter = renderer.GetComponent<MeshFilter>();
            if (filter == null || filter.sharedMesh == null)
            {
                return false;
            }

            Mesh mesh = filter.sharedMesh;
            Vector2[] uvs = mesh.uv;
            if (uvs == null || uvs.Length == 0)
            {
                return false;
            }

            Vector3[] vertices = mesh.vertices;
            int[] triangles = mesh.triangles;

            float radiusInWorld = Mathf.Max(0.0001f, radius);
            float radiusSqr = radiusInWorld * radiusInWorld;
            bool changed = false;

            for (int i = 0; i + 2 < triangles.Length; i += 3)
            {
                int aIndex = triangles[i];
                int bIndex = triangles[i + 1];
                int cIndex = triangles[i + 2];

                Vector2 aUv = uvs[aIndex];
                Vector2 bUv = uvs[bIndex];
                Vector2 cUv = uvs[cIndex];

                int minX = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(aUv.x, Mathf.Min(bUv.x, cUv.x)) * width));
                int maxX = Mathf.Min(width - 1, Mathf.CeilToInt(Mathf.Max(aUv.x, Mathf.Max(bUv.x, cUv.x)) * width));
                int minY = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(aUv.y, Mathf.Min(bUv.y, cUv.y)) * height));
                int maxY = Mathf.Min(height - 1, Mathf.CeilToInt(Mathf.Max(aUv.y, Mathf.Max(bUv.y, cUv.y)) * height));

                Vector3 aWorld = targetObject.transform.TransformPoint(vertices[aIndex]);
                Vector3 bWorld = targetObject.transform.TransformPoint(vertices[bIndex]);
                Vector3 cWorld = targetObject.transform.TransformPoint(vertices[cIndex]);

                for (int y = minY; y <= maxY; y++)
                {
                    for (int x = minX; x <= maxX; x++)
                    {
                        Vector2 pixelUv = new Vector2((x + 0.5f) / width, (y + 0.5f) / height);
                        if (!PointInTriangleUV(pixelUv, aUv, bUv, cUv))
                        {
                            continue;
                        }

                        Vector3 bary = Barycentric(pixelUv, aUv, bUv, cUv);
                        Vector3 worldPosition = aWorld * bary.x + bWorld * bary.y + cWorld * bary.z;
                        float distanceSqr = (worldPosition - worldCenter).sqrMagnitude;
                        if (distanceSqr > radiusSqr)
                        {
                            continue;
                        }

                        float distance = Mathf.Sqrt(distanceSqr);
                        float falloff = 1f - distance / radiusInWorld;
                        float alpha = Mathf.Clamp01(opacity * falloff);
                        int index = y * width + x;
                        pixels[index] = Color.Lerp(pixels[index], brushColor, alpha);
                        changed = true;
                    }
                }
            }

            return changed;
        }

        public static bool EnsureTextureReadable(Texture2D texture)
        {
            EnsureTextureIsReadable(texture);
            return texture != null && texture.isReadable;
        }

        public static bool TryComputeUvRadiusFromWorldRadius(
            GameObject targetObject,
            Renderer renderer,
            Vector2 uv,
            float worldRadius,
            out float uvRadius)
        {
            uvRadius = 0f;
            if (targetObject == null || renderer == null)
            {
                return false;
            }

            MeshFilter filter = renderer.GetComponent<MeshFilter>();
            if (filter == null || filter.sharedMesh == null)
            {
                return false;
            }

            Mesh mesh = filter.sharedMesh;
            Vector2[] uvs = mesh.uv;
            Vector3[] vertices = mesh.vertices;
            int[] triangles = mesh.triangles;
            if (uvs == null || vertices == null || triangles == null || uvs.Length == 0 || vertices.Length == 0 || triangles.Length < 3)
            {
                return false;
            }

            for (int i = 0; i + 2 < triangles.Length; i += 3)
            {
                int aIndex = triangles[i];
                int bIndex = triangles[i + 1];
                int cIndex = triangles[i + 2];

                Vector2 uv0 = uvs[aIndex];
                Vector2 uv1 = uvs[bIndex];
                Vector2 uv2 = uvs[cIndex];

                if (!PointInTriangleUV(uv, uv0, uv1, uv2))
                {
                    continue;
                }

                Vector3 p0 = targetObject.transform.TransformPoint(vertices[aIndex]);
                Vector3 p1 = targetObject.transform.TransformPoint(vertices[bIndex]);
                Vector3 p2 = targetObject.transform.TransformPoint(vertices[cIndex]);

                Vector3 dp1 = p1 - p0;
                Vector3 dp2 = p2 - p0;
                float du1 = uv1.x - uv0.x;
                float dv1 = uv1.y - uv0.y;
                float du2 = uv2.x - uv0.x;
                float dv2 = uv2.y - uv0.y;
                float determinant = du1 * dv2 - dv1 * du2;
                if (Mathf.Abs(determinant) < 0.000001f)
                {
                    continue;
                }

                float invDet = 1f / determinant;
                Vector3 dPdu = (dp1 * dv2 - dp2 * dv1) * invDet;
                Vector3 dPdv = (dp2 * du1 - dp1 * du2) * invDet;
                float metersPerUv = Mathf.Max(0.000001f, (dPdu.magnitude + dPdv.magnitude) * 0.5f);

                uvRadius = Mathf.Clamp(worldRadius / metersPerUv, 0.0005f, 0.5f);
                return true;
            }

            return false;
        }

        public static bool TryGetTriangleUvMetric(
            GameObject targetObject,
            Renderer renderer,
            int triangleIndex,
            out float m00,
            out float m01,
            out float m11)
        {
            m00 = 0f;
            m01 = 0f;
            m11 = 0f;

            if (targetObject == null || renderer == null || triangleIndex < 0)
            {
                return false;
            }

            MeshFilter filter = renderer.GetComponent<MeshFilter>();
            if (filter == null || filter.sharedMesh == null)
            {
                return false;
            }

            Mesh mesh = filter.sharedMesh;
            Vector2[] uvs = mesh.uv;
            Vector3[] vertices = mesh.vertices;
            int[] triangles = mesh.triangles;
            if (uvs == null || vertices == null || triangles == null)
            {
                return false;
            }

            int triBase = triangleIndex * 3;
            if (triBase + 2 >= triangles.Length)
            {
                return false;
            }

            int aIndex = triangles[triBase];
            int bIndex = triangles[triBase + 1];
            int cIndex = triangles[triBase + 2];
            if (aIndex < 0 || bIndex < 0 || cIndex < 0 || aIndex >= uvs.Length || bIndex >= uvs.Length || cIndex >= uvs.Length)
            {
                return false;
            }

            Vector2 uv0 = uvs[aIndex];
            Vector2 uv1 = uvs[bIndex];
            Vector2 uv2 = uvs[cIndex];

            Vector3 p0 = targetObject.transform.TransformPoint(vertices[aIndex]);
            Vector3 p1 = targetObject.transform.TransformPoint(vertices[bIndex]);
            Vector3 p2 = targetObject.transform.TransformPoint(vertices[cIndex]);

            Vector3 dp1 = p1 - p0;
            Vector3 dp2 = p2 - p0;
            float du1 = uv1.x - uv0.x;
            float dv1 = uv1.y - uv0.y;
            float du2 = uv2.x - uv0.x;
            float dv2 = uv2.y - uv0.y;

            float determinant = du1 * dv2 - dv1 * du2;
            if (Mathf.Abs(determinant) < 0.000001f)
            {
                return false;
            }

            float invDet = 1f / determinant;
            Vector3 dPdu = (dp1 * dv2 - dp2 * dv1) * invDet;
            Vector3 dPdv = (dp2 * du1 - dp1 * du2) * invDet;

            m00 = Vector3.Dot(dPdu, dPdu);
            m01 = Vector3.Dot(dPdu, dPdv);
            m11 = Vector3.Dot(dPdv, dPdv);
            return m00 > 0f && m11 > 0f;
        }

        public static bool TryFindTriangleIndexByUv(
            GameObject targetObject,
            Renderer renderer,
            Vector2 uv,
            out int triangleIndex)
        {
            triangleIndex = -1;
            if (targetObject == null || renderer == null)
            {
                return false;
            }

            MeshFilter filter = renderer.GetComponent<MeshFilter>();
            if (filter == null || filter.sharedMesh == null)
            {
                return false;
            }

            Mesh mesh = filter.sharedMesh;
            Vector2[] uvs = mesh.uv;
            int[] triangles = mesh.triangles;
            if (uvs == null || triangles == null || uvs.Length == 0 || triangles.Length < 3)
            {
                return false;
            }

            for (int triBase = 0; triBase + 2 < triangles.Length; triBase += 3)
            {
                int aIndex = triangles[triBase];
                int bIndex = triangles[triBase + 1];
                int cIndex = triangles[triBase + 2];
                if (aIndex < 0 || bIndex < 0 || cIndex < 0 || aIndex >= uvs.Length || bIndex >= uvs.Length || cIndex >= uvs.Length)
                {
                    continue;
                }

                if (PointInTriangleUV(uv, uvs[aIndex], uvs[bIndex], uvs[cIndex]))
                {
                    triangleIndex = triBase / 3;
                    return true;
                }
            }

            return false;
        }

        public static bool TryBuildTriangleUvIslandMap(
            GameObject targetObject,
            Renderer renderer,
            out int[] triangleIslandIds)
        {
            triangleIslandIds = null;
            if (targetObject == null || renderer == null)
            {
                return false;
            }

            MeshFilter filter = renderer.GetComponent<MeshFilter>();
            if (filter == null || filter.sharedMesh == null)
            {
                return false;
            }

            Mesh mesh = filter.sharedMesh;
            int[] triangles = mesh.triangles;
            if (triangles == null || triangles.Length < 3)
            {
                return false;
            }

            int triangleCount = triangles.Length / 3;
            triangleIslandIds = new int[triangleCount];
            for (int i = 0; i < triangleIslandIds.Length; i++)
            {
                triangleIslandIds[i] = -1;
            }

            var vertexToTriangles = new Dictionary<int, List<int>>();
            for (int tri = 0; tri < triangleCount; tri++)
            {
                int triBase = tri * 3;
                int aIndex = triangles[triBase];
                int bIndex = triangles[triBase + 1];
                int cIndex = triangles[triBase + 2];

                AddTriangleForVertex(vertexToTriangles, aIndex, tri);
                AddTriangleForVertex(vertexToTriangles, bIndex, tri);
                AddTriangleForVertex(vertexToTriangles, cIndex, tri);
            }

            int islandId = 0;
            var queue = new Queue<int>();

            for (int start = 0; start < triangleCount; start++)
            {
                if (triangleIslandIds[start] >= 0)
                {
                    continue;
                }

                triangleIslandIds[start] = islandId;
                queue.Enqueue(start);

                while (queue.Count > 0)
                {
                    int current = queue.Dequeue();
                    int currentBase = current * 3;
                    int aIndex = triangles[currentBase];
                    int bIndex = triangles[currentBase + 1];
                    int cIndex = triangles[currentBase + 2];

                    FloodNeighborTriangles(vertexToTriangles, triangleIslandIds, islandId, aIndex, queue);
                    FloodNeighborTriangles(vertexToTriangles, triangleIslandIds, islandId, bIndex, queue);
                    FloodNeighborTriangles(vertexToTriangles, triangleIslandIds, islandId, cIndex, queue);
                }

                islandId++;
            }

            return true;
        }

        public static bool PaintTexturePixelsUvBrush(
            Color[] pixels,
            int width,
            int height,
            Vector2 uvCenter,
            float uvRadius,
            Color brushColor,
            float opacity)
        {
            if (pixels == null || pixels.Length != width * height)
            {
                return false;
            }

            float safeRadius = Mathf.Max(0.0005f, uvRadius);
            int minX = Mathf.Max(0, Mathf.FloorToInt((uvCenter.x - safeRadius) * width));
            int maxX = Mathf.Min(width - 1, Mathf.CeilToInt((uvCenter.x + safeRadius) * width));
            int minY = Mathf.Max(0, Mathf.FloorToInt((uvCenter.y - safeRadius) * height));
            int maxY = Mathf.Min(height - 1, Mathf.CeilToInt((uvCenter.y + safeRadius) * height));

            float radiusSqr = safeRadius * safeRadius;
            bool changed = false;

            for (int y = minY; y <= maxY; y++)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    float u = (x + 0.5f) / width;
                    float v = (y + 0.5f) / height;
                    float du = u - uvCenter.x;
                    float dv = v - uvCenter.y;
                    float distanceSqr = du * du + dv * dv;
                    if (distanceSqr > radiusSqr)
                    {
                        continue;
                    }

                    float distance = Mathf.Sqrt(distanceSqr);
                    float falloff = 1f - distance / safeRadius;
                    float alpha = Mathf.Clamp01(opacity * falloff);
                    int index = y * width + x;
                    pixels[index] = Color.Lerp(pixels[index], brushColor, alpha);
                    changed = true;
                }
            }

            return changed;
        }

        public static bool PaintTexturePixelsUvBrushWorldCircular(
            Color[] pixels,
            int width,
            int height,
            Vector2 uvCenter,
            float worldRadius,
            float m00,
            float m01,
            float m11,
            Color brushColor,
            float opacity)
        {
            if (pixels == null || pixels.Length != width * height)
            {
                return false;
            }

            float radius = Mathf.Max(0.0001f, worldRadius);
            float radiusSqr = radius * radius;
            float determinant = m00 * m11 - m01 * m01;
            if (determinant <= 0.0000001f)
            {
                return false;
            }

            float inv00 = m11 / determinant;
            float inv11 = m00 / determinant;
            float duMax = radius * Mathf.Sqrt(Mathf.Max(0f, inv00));
            float dvMax = radius * Mathf.Sqrt(Mathf.Max(0f, inv11));

            int minX = Mathf.Max(0, Mathf.FloorToInt((uvCenter.x - duMax) * width));
            int maxX = Mathf.Min(width - 1, Mathf.CeilToInt((uvCenter.x + duMax) * width));
            int minY = Mathf.Max(0, Mathf.FloorToInt((uvCenter.y - dvMax) * height));
            int maxY = Mathf.Min(height - 1, Mathf.CeilToInt((uvCenter.y + dvMax) * height));

            bool changed = false;
            for (int y = minY; y <= maxY; y++)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    float u = (x + 0.5f) / width;
                    float v = (y + 0.5f) / height;
                    float du = u - uvCenter.x;
                    float dv = v - uvCenter.y;

                    float worldDistanceSqr = m00 * du * du + 2f * m01 * du * dv + m11 * dv * dv;
                    if (worldDistanceSqr > radiusSqr)
                    {
                        continue;
                    }

                    float worldDistance = Mathf.Sqrt(Mathf.Max(0f, worldDistanceSqr));
                    float falloff = 1f - worldDistance / radius;
                    float alpha = Mathf.Clamp01(opacity * falloff);
                    int index = y * width + x;
                    pixels[index] = Color.Lerp(pixels[index], brushColor, alpha);
                    changed = true;
                }
            }

            return changed;
        }

        public static float EstimateUvRadiusFromMetric(float worldRadius, float m00, float m11)
        {
            float maxScale = Mathf.Max(0.000001f, Mathf.Max(Mathf.Sqrt(Mathf.Max(0f, m00)), Mathf.Sqrt(Mathf.Max(0f, m11))));
            return Mathf.Clamp(worldRadius / maxScale, 0.0005f, 0.5f);
        }

        public static Vector3 UvToPreviewPoint(Vector2 uv, Rect previewRect)
        {
            float x = previewRect.x + Mathf.Clamp01(uv.x) * previewRect.width;
            float y = previewRect.y + (1f - Mathf.Clamp01(uv.y)) * previewRect.height;
            return new Vector3(x, y, 0f);
        }

        public static void ClearTexture(Texture2D texture)
        {
            if (texture == null)
            {
                return;
            }

            EnsureTextureIsReadable(texture);
            if (!texture.isReadable)
            {
                return;
            }

            Color[] pixels = texture.GetPixels();
            Color baseColor = new Color(0.5f, 0.5f, 0.5f, 1f);

            for (int i = 0; i < pixels.Length; i++)
            {
                pixels[i] = baseColor;
            }

            texture.SetPixels(pixels);
            texture.Apply();
            EditorUtility.SetDirty(texture);
        }

        public static bool IsTargetRendererHit(Renderer renderer, RaycastHit hit)
        {
            if (renderer == null)
            {
                return false;
            }

            Renderer hitRenderer = hit.collider != null ? hit.collider.GetComponentInParent<Renderer>() : null;
            return hitRenderer == renderer || hit.collider == renderer.GetComponent<Collider>();
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

        private static bool PointInTriangleUV(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
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

        private static void AddTriangleForVertex(Dictionary<int, List<int>> vertexToTriangles, int vertexIndex, int triangleIndex)
        {
            if (!vertexToTriangles.TryGetValue(vertexIndex, out List<int> attachedTriangles))
            {
                attachedTriangles = new List<int>();
                vertexToTriangles[vertexIndex] = attachedTriangles;
            }

            attachedTriangles.Add(triangleIndex);
        }

        private static void FloodNeighborTriangles(
            Dictionary<int, List<int>> vertexToTriangles,
            int[] triangleIslandIds,
            int islandId,
            int vertexIndex,
            Queue<int> queue)
        {
            if (!vertexToTriangles.TryGetValue(vertexIndex, out List<int> neighbors))
            {
                return;
            }

            for (int i = 0; i < neighbors.Count; i++)
            {
                int neighborTri = neighbors[i];
                if (neighborTri < 0 || neighborTri >= triangleIslandIds.Length)
                {
                    continue;
                }

                if (triangleIslandIds[neighborTri] >= 0)
                {
                    continue;
                }

                triangleIslandIds[neighborTri] = islandId;
                queue.Enqueue(neighborTri);
            }
        }

        private static Vector3 Barycentric(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
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

        private static void EnsureTextureIsReadable(Texture2D texture)
        {
            if (texture == null || texture.isReadable)
            {
                return;
            }

            string path = AssetDatabase.GetAssetPath(texture);
            if (string.IsNullOrEmpty(path))
            {
                return;
            }

            TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null)
            {
                return;
            }

            importer.isReadable = true;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SaveAndReimport();
            AssetDatabase.Refresh();
        }

        private static void DrawUniqueEdge(
            Color[] pixels,
            int size,
            Vector2 uvA,
            Vector2 uvB,
            Color color,
            HashSet<ulong> seenEdges,
            int indexA,
            int indexB)
        {
            uint min = (uint)Mathf.Min(indexA, indexB);
            uint max = (uint)Mathf.Max(indexA, indexB);
            ulong key = ((ulong)min << 32) | max;
            if (seenEdges.Contains(key))
            {
                return;
            }

            seenEdges.Add(key);
            DrawLineOnUvTexture(pixels, size, uvA, uvB, color);
        }

        private static void DrawLineOnUvTexture(Color[] pixels, int size, Vector2 uvA, Vector2 uvB, Color color)
        {
            int x0 = Mathf.Clamp(Mathf.RoundToInt(Mathf.Clamp01(uvA.x) * (size - 1)), 0, size - 1);
            int y0 = Mathf.Clamp(Mathf.RoundToInt(Mathf.Clamp01(uvA.y) * (size - 1)), 0, size - 1);
            int x1 = Mathf.Clamp(Mathf.RoundToInt(Mathf.Clamp01(uvB.x) * (size - 1)), 0, size - 1);
            int y1 = Mathf.Clamp(Mathf.RoundToInt(Mathf.Clamp01(uvB.y) * (size - 1)), 0, size - 1);

            int dx = Mathf.Abs(x1 - x0);
            int dy = Mathf.Abs(y1 - y0);
            int sx = x0 < x1 ? 1 : -1;
            int sy = y0 < y1 ? 1 : -1;
            int err = dx - dy;

            while (true)
            {
                pixels[y0 * size + x0] = color;
                if (x0 == x1 && y0 == y1)
                {
                    break;
                }

                int e2 = err * 2;
                if (e2 > -dy)
                {
                    err -= dy;
                    x0 += sx;
                }

                if (e2 < dx)
                {
                    err += dx;
                    y0 += sy;
                }
            }
        }
    }
}
