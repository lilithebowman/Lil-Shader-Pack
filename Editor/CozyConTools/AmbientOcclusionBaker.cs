using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Lilithe.Tools
{
    public static class AmbientOcclusionBaker
    {
        public static bool BakeAmbientOcclusionPixels(
            GameObject targetObject,
            Renderer renderer,
            int resolution,
            int sampleCount,
            float rayDistance,
            out Color[] pixels,
            out int size,
            out string error)
        {
            pixels = null;
            size = 0;
            error = null;

            if (targetObject == null)
            {
                error = "No target object is selected.";
                return false;
            }

            if (renderer == null)
            {
                error = "The target object does not have a Renderer.";
                return false;
            }

            MeshFilter filter = renderer.GetComponent<MeshFilter>();
            if (filter == null || filter.sharedMesh == null)
            {
                error = "The target object does not have a mesh to bake.";
                return false;
            }

            Mesh mesh = filter.sharedMesh;
            Vector2[] uvs = mesh.uv;
            Vector3[] vertices = mesh.vertices;
            int[] triangles = mesh.triangles;
            Vector3[] normals = mesh.normals;
            bool hasNormals = normals != null && normals.Length == vertices.Length;

            if (uvs == null || uvs.Length == 0)
            {
                error = "The target mesh has no UVs. Generate UVs before baking ambient occlusion.";
                return false;
            }

            if (triangles == null || triangles.Length < 3)
            {
                error = "The target mesh has no triangles to bake.";
                return false;
            }

            size = Mathf.Clamp(resolution, 64, 8192);
            int samples = Mathf.Clamp(sampleCount, 1, 128);
            float distance = Mathf.Max(0.01f, rayDistance);

            pixels = new Color[size * size];
            for (int i = 0; i < pixels.Length; i++)
            {
                pixels[i] = Color.white;
            }

            Transform meshTransform = renderer.transform;
            try
            {
                int triangleCount = triangles.Length / 3;
                for (int triangleIndex = 0; triangleIndex < triangleCount; triangleIndex++)
                {
                    if (EditorUtility.DisplayCancelableProgressBar(
                        "Baking Ambient Occlusion",
                        "Processing triangle " + (triangleIndex + 1) + " / " + triangleCount,
                        triangleCount <= 1 ? 1f : triangleIndex / (float)(triangleCount - 1)))
                    {
                        error = "Ambient occlusion bake was canceled.";
                        pixels = null;
                        return false;
                    }

                    int triBase = triangleIndex * 3;
                    int aIndex = triangles[triBase];
                    int bIndex = triangles[triBase + 1];
                    int cIndex = triangles[triBase + 2];

                    if (!IsValidTriangleIndex(aIndex, bIndex, cIndex, uvs.Length, vertices.Length))
                    {
                        continue;
                    }

                    Vector2 uv0 = uvs[aIndex];
                    Vector2 uv1 = uvs[bIndex];
                    Vector2 uv2 = uvs[cIndex];

                    Vector3 p0 = meshTransform.TransformPoint(vertices[aIndex]);
                    Vector3 p1 = meshTransform.TransformPoint(vertices[bIndex]);
                    Vector3 p2 = meshTransform.TransformPoint(vertices[cIndex]);

                    Vector3 n0 = hasNormals ? meshTransform.TransformDirection(normals[aIndex]) : Vector3.zero;
                    Vector3 n1 = hasNormals ? meshTransform.TransformDirection(normals[bIndex]) : Vector3.zero;
                    Vector3 n2 = hasNormals ? meshTransform.TransformDirection(normals[cIndex]) : Vector3.zero;
                    Vector3 faceNormal = Vector3.Cross(p1 - p0, p2 - p0).normalized;
                    if (!hasNormals || n0 == Vector3.zero || n1 == Vector3.zero || n2 == Vector3.zero)
                    {
                        n0 = faceNormal;
                        n1 = faceNormal;
                        n2 = faceNormal;
                    }

                    int minX = Mathf.Clamp(Mathf.FloorToInt(Mathf.Min(uv0.x, Mathf.Min(uv1.x, uv2.x)) * size), 0, size - 1);
                    int maxX = Mathf.Clamp(Mathf.CeilToInt(Mathf.Max(uv0.x, Mathf.Max(uv1.x, uv2.x)) * size), 0, size - 1);
                    int minY = Mathf.Clamp(Mathf.FloorToInt(Mathf.Min(uv0.y, Mathf.Min(uv1.y, uv2.y)) * size), 0, size - 1);
                    int maxY = Mathf.Clamp(Mathf.CeilToInt(Mathf.Max(uv0.y, Mathf.Max(uv1.y, uv2.y)) * size), 0, size - 1);

                    for (int y = minY; y <= maxY; y++)
                    {
                        for (int x = minX; x <= maxX; x++)
                        {
                            Vector2 uv = new Vector2((x + 0.5f) / size, (y + 0.5f) / size);
                            if (!PointInTriangleUV(uv, uv0, uv1, uv2))
                            {
                                continue;
                            }

                            Vector3 bary = Barycentric(uv, uv0, uv1, uv2);
                            Vector3 worldPosition = p0 * bary.x + p1 * bary.y + p2 * bary.z;
                            Vector3 worldNormal = (n0 * bary.x + n1 * bary.y + n2 * bary.z).normalized;
                            if (worldNormal == Vector3.zero)
                            {
                                worldNormal = faceNormal;
                            }

                            float ao = SampleAmbientOcclusion(meshTransform, worldPosition, worldNormal, samples, distance);
                            int index = y * size + x;
                            pixels[index] = new Color(ao, ao, ao, 1f);
                        }
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                error = "Failed to bake ambient occlusion: " + ex.Message;
                pixels = null;
                return false;
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        public static bool BakeAndSaveAmbientOcclusion(
            GameObject targetObject,
            Renderer renderer,
            string filePath,
            int resolution,
            int sampleCount,
            float rayDistance,
            out string error)
        {
            error = null;

            if (string.IsNullOrEmpty(filePath))
            {
                error = "No output file path was chosen.";
                return false;
            }

            if (!BakeAmbientOcclusionPixels(targetObject, renderer, resolution, sampleCount, rayDistance, out Color[] pixels, out int size, out error))
            {
                return false;
            }

            Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false, false);
            texture.SetPixels(pixels);
            texture.Apply();
            byte[] bytes = texture.EncodeToPNG();
            UnityEngine.Object.DestroyImmediate(texture);

            string directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllBytes(filePath, bytes);
            AssetDatabase.ImportAsset(filePath);
            AssetDatabase.Refresh();

            TextureImporter importer = AssetImporter.GetAtPath(filePath) as TextureImporter;
            if (importer != null)
            {
                importer.textureType = TextureImporterType.Default;
                importer.isReadable = true;
                importer.sRGBTexture = false;
                importer.wrapMode = TextureWrapMode.Repeat;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.alphaSource = TextureImporterAlphaSource.None;
                importer.mipmapEnabled = false;
                importer.SaveAndReimport();
            }

            AssetDatabase.Refresh();
            return true;
        }

        private static float SampleAmbientOcclusion(Transform meshTransform, Vector3 worldPosition, Vector3 worldNormal, int sampleCount, float rayDistance)
        {
            Vector3 origin = worldPosition + worldNormal * 0.002f;
            BuildOrthonormalBasis(worldNormal, out Vector3 tangent, out Vector3 bitangent);

            int occluded = 0;
            for (int i = 0; i < sampleCount; i++)
            {
                Vector3 localDir = SampleHemisphereDirection(i, sampleCount);
                Vector3 worldDir = (tangent * localDir.x + bitangent * localDir.y + worldNormal * localDir.z).normalized;
                if (Physics.Raycast(origin, worldDir, rayDistance, ~0, QueryTriggerInteraction.Ignore))
                {
                    occluded++;
                }
            }

            return 1f - occluded / (float)sampleCount;
        }

        private static Vector3 SampleHemisphereDirection(int index, int sampleCount)
        {
            float t = (index + 0.5f) / sampleCount;
            float phi = 2f * Mathf.PI * Frac(index * 0.7548776662466927f);
            float r = Mathf.Sqrt(Mathf.Clamp01(t));
            float x = Mathf.Cos(phi) * r;
            float y = Mathf.Sin(phi) * r;
            float z = Mathf.Sqrt(Mathf.Max(0f, 1f - r * r));
            return new Vector3(x, y, z);
        }

        private static void BuildOrthonormalBasis(Vector3 normal, out Vector3 tangent, out Vector3 bitangent)
        {
            Vector3 up = Mathf.Abs(normal.y) < 0.99f ? Vector3.up : Vector3.right;
            tangent = Vector3.Cross(up, normal).normalized;
            if (tangent == Vector3.zero)
            {
                tangent = Vector3.Cross(Vector3.forward, normal).normalized;
            }

            bitangent = Vector3.Cross(normal, tangent).normalized;
        }

        private static bool IsValidTriangleIndex(int a, int b, int c, int uvLength, int vertexLength)
        {
            return a >= 0 && b >= 0 && c >= 0 && a < uvLength && b < uvLength && c < uvLength && a < vertexLength && b < vertexLength && c < vertexLength;
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

        private static float Frac(float value)
        {
            return value - Mathf.Floor(value);
        }
    }
}
