using System.Collections.Generic;
using UnityEngine;

namespace CozyCon.Tools
{
	public static class BuildingMeshUtility
	{
		public struct MeshPartInfo
		{
			public int triangleStart;
			public int triangleCount;
			public Vector3 center;
		}

		public static void AddBox(List<Vector3> vertices, List<int> triangles, List<Vector3> normals, List<Vector2> uvs, Vector3 min, Vector3 max)
		{
			Vector3[] pts =
			{
				new Vector3(min.x, min.y, min.z),
				new Vector3(max.x, min.y, min.z),
				new Vector3(max.x, max.y, min.z),
				new Vector3(min.x, max.y, min.z),
				new Vector3(min.x, min.y, max.z),
				new Vector3(max.x, min.y, max.z),
				new Vector3(max.x, max.y, max.z),
				new Vector3(min.x, max.y, max.z)
			};

			AddQuad(vertices, triangles, normals, uvs, pts[4], pts[5], pts[6], pts[7], Vector3.forward);
			AddQuad(vertices, triangles, normals, uvs, pts[1], pts[0], pts[3], pts[2], Vector3.back);
			AddQuad(vertices, triangles, normals, uvs, pts[0], pts[4], pts[7], pts[3], Vector3.left);
			AddQuad(vertices, triangles, normals, uvs, pts[5], pts[1], pts[2], pts[6], Vector3.right);
			AddQuad(vertices, triangles, normals, uvs, pts[3], pts[7], pts[6], pts[2], Vector3.up);
			AddQuad(vertices, triangles, normals, uvs, pts[0], pts[1], pts[5], pts[4], Vector3.down);
		}

		public static void AddBoxPart(List<Vector3> vertices, List<int> triangles, List<Vector3> normals, List<Vector2> uvs, Vector3 min, Vector3 max, List<MeshPartInfo> parts)
		{
			int triStart = triangles.Count;
			AddBox(vertices, triangles, normals, uvs, min, max);
			parts.Add(new MeshPartInfo
			{
				triangleStart = triStart,
				triangleCount = triangles.Count - triStart,
				center = (min + max) * 0.5f
			});
		}

		public static void OrientTrianglesOutwardPerPart(List<Vector3> vertices, List<int> triangles, List<MeshPartInfo> parts)
		{
			for (int p = 0; p < parts.Count; p++)
			{
				MeshPartInfo part = parts[p];
				int end = part.triangleStart + part.triangleCount;
				for (int i = part.triangleStart; i < end; i += 3)
				{
					int ia = triangles[i];
					int ib = triangles[i + 1];
					int ic = triangles[i + 2];

					Vector3 a = vertices[ia];
					Vector3 b = vertices[ib];
					Vector3 c = vertices[ic];

					Vector3 normal = Vector3.Cross(b - a, c - a);
					Vector3 triCenter = (a + b + c) / 3f;
					if (Vector3.Dot(normal, triCenter - part.center) < 0f)
					{
						triangles[i + 1] = ic;
						triangles[i + 2] = ib;
					}
				}
			}
		}

		public static void AddQuad(List<Vector3> vertices, List<int> triangles, List<Vector3> normals, List<Vector2> uvs, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 normal)
		{
			int start = vertices.Count;
			vertices.Add(a);
			vertices.Add(b);
			vertices.Add(c);
			vertices.Add(d);

			normals.Add(normal);
			normals.Add(normal);
			normals.Add(normal);
			normals.Add(normal);

			uvs.Add(new Vector2(0f, 0f));
			uvs.Add(new Vector2(1f, 0f));
			uvs.Add(new Vector2(1f, 1f));
			uvs.Add(new Vector2(0f, 1f));

			triangles.Add(start);
			triangles.Add(start + 1);
			triangles.Add(start + 2);
			triangles.Add(start);
			triangles.Add(start + 2);
			triangles.Add(start + 3);
		}

		public static void AddQuadFacingPoint(List<Vector3> vertices, List<int> triangles, List<Vector3> normals, List<Vector2> uvs, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 point)
		{
			int triStart = triangles.Count;
			Vector3 authoredNormal = Vector3.Cross(b - a, c - a).normalized;
			AddQuad(vertices, triangles, normals, uvs, a, b, c, d, authoredNormal);

			Vector3 faceNormal = Vector3.Cross(b - a, c - a);
			Vector3 faceCenter = (a + b + c + d) * 0.25f;
			if (Vector3.Dot(faceNormal, point - faceCenter) < 0f)
			{
				int t = triangles[triStart + 1];
				triangles[triStart + 1] = triangles[triStart + 2];
				triangles[triStart + 2] = t;

				t = triangles[triStart + 4];
				triangles[triStart + 4] = triangles[triStart + 5];
				triangles[triStart + 5] = t;
			}
		}

		public static void AddTriangle(List<Vector3> vertices, List<int> triangles, List<Vector3> normals, List<Vector2> uvs, Vector3 a, Vector3 b, Vector3 c, Vector3 normal)
		{
			int start = vertices.Count;
			vertices.Add(a);
			vertices.Add(b);
			vertices.Add(c);

			normals.Add(normal);
			normals.Add(normal);
			normals.Add(normal);

			uvs.Add(new Vector2(0f, 0f));
			uvs.Add(new Vector2(1f, 0f));
			uvs.Add(new Vector2(0.5f, 1f));

			triangles.Add(start);
			triangles.Add(start + 1);
			triangles.Add(start + 2);
		}

		public static Mesh BuildCenteredBoxMesh(float width, float height, float depth, string meshName)
		{
			List<Vector3> vertices = new List<Vector3>();
			List<int> triangles = new List<int>();
			List<Vector3> normals = new List<Vector3>();
			List<Vector2> uvs = new List<Vector2>();
			List<MeshPartInfo> parts = new List<MeshPartInfo>();

			Vector3 min = new Vector3(-width * 0.5f, -height * 0.5f, -depth * 0.5f);
			Vector3 max = new Vector3(width * 0.5f, height * 0.5f, depth * 0.5f);
			AddBoxPart(vertices, triangles, normals, uvs, min, max, parts);
			OrientTrianglesOutwardPerPart(vertices, triangles, parts);

			Mesh mesh = new Mesh();
			mesh.name = meshName;
			mesh.SetVertices(vertices);
			mesh.SetTriangles(triangles, 0);
			mesh.SetNormals(normals);
			mesh.SetUVs(0, uvs);
			EnsureMeshFacesPointOutward(mesh, Vector3.zero);
			return mesh;
		}

		public static Mesh BuildDoorPanelMesh(Vector2 openingSize, float wallThickness, string meshName = "DoorPanel")
		{
			float sideMargin = Mathf.Min(0.02f, openingSize.x * 0.1f);
			float topBottomMargin = Mathf.Min(0.02f, openingSize.y * 0.1f);
			float width = Mathf.Max(0.02f, openingSize.x - sideMargin * 2f);
			float height = Mathf.Max(0.02f, openingSize.y - topBottomMargin * 2f);
			float thickness = Mathf.Clamp(wallThickness * 0.2f, 0.02f, 0.08f);

			return BuildCenteredBoxMesh(width, height, thickness, meshName);
		}

		public static void ApplyWorldScaleQuadUvsAndPackedLightmap(Mesh mesh, float uvScaleMultiplier = 1f)
		{
			if (mesh == null)
			{
				return;
			}

			Vector3[] vertices = mesh.vertices;
			int vertexCount = vertices != null ? vertices.Length : 0;
			if (vertexCount < 4)
			{
				return;
			}

			int quadCount = vertexCount / 4;
			if (quadCount <= 0)
			{
				return;
			}

			float uvScale = Mathf.Max(0.0001f, uvScaleMultiplier);
			Vector2[] uv0 = new Vector2[vertexCount];
			Vector2[] uv2 = new Vector2[vertexCount];
			List<float> quadWidths = new List<float>(quadCount);
			List<float> quadHeights = new List<float>(quadCount);
			for (int q = 0; q < quadCount; q++)
			{
				int i = q * 4;
				Vector3 a = vertices[i];
				Vector3 b = vertices[i + 1];
				Vector3 d = vertices[i + 3];

				float width = Mathf.Max(0.0001f, Vector3.Distance(a, b));
				float height = Mathf.Max(0.0001f, Vector3.Distance(a, d));

				quadWidths.Add(width);
				quadHeights.Add(height);

				// Keep texel density consistent with local/world dimensions per face.
				uv0[i] = new Vector2(0f, 0f);
				uv0[i + 1] = new Vector2(width * uvScale, 0f);
				uv0[i + 2] = new Vector2(width * uvScale, height * uvScale);
				uv0[i + 3] = new Vector2(0f, height * uvScale);
			}

			const float padding = 0.01f;
			float totalArea = 0f;
			for (int q = 0; q < quadCount; q++)
			{
				totalArea += (quadWidths[q] + padding * 2f) * (quadHeights[q] + padding * 2f);
			}

			float targetRowWidth = Mathf.Max(0.1f, Mathf.Sqrt(Mathf.Max(0.0001f, totalArea)));
			List<float> placedX = new List<float>(quadCount);
			List<float> placedY = new List<float>(quadCount);
			float rowX = padding;
			float rowY = padding;
			float rowHeight = 0f;
			float atlasWidth = 0f;
			float atlasHeight = 0f;

			for (int q = 0; q < quadCount; q++)
			{
				float blockWidth = quadWidths[q] + padding * 2f;
				float blockHeight = quadHeights[q] + padding * 2f;

				if (rowX + blockWidth > targetRowWidth && rowX > padding)
				{
					rowY += rowHeight;
					rowX = padding;
					rowHeight = 0f;
				}

				float x = rowX + padding;
				float y = rowY + padding;
				placedX.Add(x);
				placedY.Add(y);

				rowX += blockWidth;
				rowHeight = Mathf.Max(rowHeight, blockHeight);
				atlasWidth = Mathf.Max(atlasWidth, rowX);
				atlasHeight = Mathf.Max(atlasHeight, rowY + rowHeight);
			}

			atlasWidth = Mathf.Max(atlasWidth, padding * 2f + 0.0001f);
			atlasHeight = Mathf.Max(atlasHeight, padding * 2f + 0.0001f);
			float invAtlasWidth = 1f / atlasWidth;
			float invAtlasHeight = 1f / atlasHeight;

			for (int q = 0; q < quadCount; q++)
			{
				int i = q * 4;
				float x0 = placedX[q] * invAtlasWidth;
				float y0 = placedY[q] * invAtlasHeight;
				float x1 = (placedX[q] + quadWidths[q]) * invAtlasWidth;
				float y1 = (placedY[q] + quadHeights[q]) * invAtlasHeight;

				uv2[i] = new Vector2(x0, y0);
				uv2[i + 1] = new Vector2(x1, y0);
				uv2[i + 2] = new Vector2(x1, y1);
				uv2[i + 3] = new Vector2(x0, y1);
			}

			mesh.uv = uv0;
			mesh.uv2 = uv2;
		}

		public static Mesh BuildQuarterCornerMesh(float radius, float height, int segments, string meshName)
		{
			radius = Mathf.Max(0.001f, radius);
			segments = Mathf.Clamp(segments, 2, 64);
			float halfHeight = height * 0.5f;

			List<Vector3> vertices = new List<Vector3>();
			List<int> triangles = new List<int>();
			List<Vector3> normals = new List<Vector3>();
			List<Vector2> uvs = new List<Vector2>();

			Vector3[] topArc = new Vector3[segments + 1];
			Vector3[] bottomArc = new Vector3[segments + 1];
			for (int i = 0; i <= segments; i++)
			{
				float t = i / (float)segments;
				float angle = t * Mathf.PI * 0.5f;
				float x = Mathf.Cos(angle) * radius;
				float z = Mathf.Sin(angle) * radius;
				topArc[i] = new Vector3(x, halfHeight, z);
				bottomArc[i] = new Vector3(x, -halfHeight, z);
			}

			for (int i = 0; i < segments; i++)
			{
				Vector3 aTop = topArc[i];
				Vector3 bTop = topArc[i + 1];
				Vector3 aBottom = bottomArc[i];
				Vector3 bBottom = bottomArc[i + 1];
				Vector3 normal = new Vector3((aTop.x + bTop.x) * 0.5f, 0f, (aTop.z + bTop.z) * 0.5f).normalized;
				AddQuad(vertices, triangles, normals, uvs, aTop, bTop, bBottom, aBottom, normal);
			}

			Vector3 cornerTop = new Vector3(0f, halfHeight, 0f);
			Vector3 cornerBottom = new Vector3(0f, -halfHeight, 0f);
			AddQuad(vertices, triangles, normals, uvs, cornerTop, topArc[0], bottomArc[0], cornerBottom, Vector3.back);
			AddQuad(vertices, triangles, normals, uvs, topArc[segments], cornerTop, cornerBottom, bottomArc[segments], Vector3.left);

			int topCenterIndex = vertices.Count;
			vertices.Add(cornerTop);
			normals.Add(Vector3.up);
			uvs.Add(new Vector2(0f, 0f));
			for (int i = 0; i <= segments; i++)
			{
				vertices.Add(topArc[i]);
				normals.Add(Vector3.up);
				uvs.Add(new Vector2(topArc[i].x / radius, topArc[i].z / radius));
			}
			for (int i = 0; i < segments; i++)
			{
				triangles.Add(topCenterIndex);
				triangles.Add(topCenterIndex + 1 + i);
				triangles.Add(topCenterIndex + 2 + i);
			}

			int bottomCenterIndex = vertices.Count;
			vertices.Add(cornerBottom);
			normals.Add(Vector3.down);
			uvs.Add(new Vector2(0f, 0f));
			for (int i = 0; i <= segments; i++)
			{
				vertices.Add(bottomArc[i]);
				normals.Add(Vector3.down);
				uvs.Add(new Vector2(bottomArc[i].x / radius, bottomArc[i].z / radius));
			}
			for (int i = 0; i < segments; i++)
			{
				triangles.Add(bottomCenterIndex);
				triangles.Add(bottomCenterIndex + 2 + i);
				triangles.Add(bottomCenterIndex + 1 + i);
			}

			Mesh mesh = new Mesh();
			mesh.name = meshName;
			mesh.SetVertices(vertices);
			mesh.SetTriangles(triangles, 0);
			mesh.SetNormals(normals);
			mesh.SetUVs(0, uvs);
			EnsureMeshFacesPointOutward(mesh, new Vector3(radius * 0.35f, 0f, radius * 0.35f));
			return mesh;
		}

		public static Mesh BuildCylinderMesh(float radius, float height, int segments, string meshName)
		{
			segments = Mathf.Clamp(segments, 4, 64);
			float halfHeight = height * 0.5f;
			List<Vector3> vertices = new List<Vector3>();
			List<int> triangles = new List<int>();
			List<Vector3> normals = new List<Vector3>();
			List<Vector2> uvs = new List<Vector2>();

			for (int i = 0; i < segments; i++)
			{
				float t0 = (float)i / segments * Mathf.PI * 2f;
				float t1 = (float)(i + 1) / segments * Mathf.PI * 2f;

				Vector3 b0 = new Vector3(Mathf.Cos(t0) * radius, -halfHeight, Mathf.Sin(t0) * radius);
				Vector3 b1 = new Vector3(Mathf.Cos(t1) * radius, -halfHeight, Mathf.Sin(t1) * radius);
				Vector3 t0v = new Vector3(Mathf.Cos(t0) * radius, halfHeight, Mathf.Sin(t0) * radius);
				Vector3 t1v = new Vector3(Mathf.Cos(t1) * radius, halfHeight, Mathf.Sin(t1) * radius);

				Vector3 n0 = new Vector3(Mathf.Cos(t0), 0f, Mathf.Sin(t0));
				Vector3 n1 = new Vector3(Mathf.Cos(t1), 0f, Mathf.Sin(t1));

				int s = vertices.Count;
				vertices.Add(b0);
				vertices.Add(b1);
				vertices.Add(t1v);
				vertices.Add(t0v);
				normals.Add(n0);
				normals.Add(n1);
				normals.Add(n1);
				normals.Add(n0);
				uvs.Add(new Vector2((float)i / segments, 0f));
				uvs.Add(new Vector2((float)(i + 1) / segments, 0f));
				uvs.Add(new Vector2((float)(i + 1) / segments, 1f));
				uvs.Add(new Vector2((float)i / segments, 1f));
				triangles.Add(s);
				triangles.Add(s + 2);
				triangles.Add(s + 1);
				triangles.Add(s);
				triangles.Add(s + 3);
				triangles.Add(s + 2);

				int tb = vertices.Count;
				vertices.Add(Vector3.up * halfHeight);
				vertices.Add(t1v);
				vertices.Add(t0v);
				normals.Add(Vector3.up);
				normals.Add(Vector3.up);
				normals.Add(Vector3.up);
				uvs.Add(new Vector2(0.5f, 0.5f));
				uvs.Add(new Vector2(0.5f + Mathf.Cos(t1) * 0.5f, 0.5f + Mathf.Sin(t1) * 0.5f));
				uvs.Add(new Vector2(0.5f + Mathf.Cos(t0) * 0.5f, 0.5f + Mathf.Sin(t0) * 0.5f));
				triangles.Add(tb);
				triangles.Add(tb + 2);
				triangles.Add(tb + 1);

				int bb = vertices.Count;
				vertices.Add(Vector3.down * halfHeight);
				vertices.Add(b0);
				vertices.Add(b1);
				normals.Add(Vector3.down);
				normals.Add(Vector3.down);
				normals.Add(Vector3.down);
				uvs.Add(new Vector2(0.5f, 0.5f));
				uvs.Add(new Vector2(0.5f + Mathf.Cos(t0) * 0.5f, 0.5f + Mathf.Sin(t0) * 0.5f));
				uvs.Add(new Vector2(0.5f + Mathf.Cos(t1) * 0.5f, 0.5f + Mathf.Sin(t1) * 0.5f));
				triangles.Add(bb);
				triangles.Add(bb + 2);
				triangles.Add(bb + 1);
			}

			Mesh mesh = new Mesh();
			mesh.name = meshName;
			mesh.SetVertices(vertices);
			mesh.SetTriangles(triangles, 0);
			mesh.SetNormals(normals);
			mesh.SetUVs(0, uvs);
			EnsureMeshFacesPointOutward(mesh, Vector3.zero);
			return mesh;
		}

		public static void EnsureMeshFacesPointOutward(Mesh mesh, Vector3 center)
		{
			if (mesh == null)
			{
				return;
			}

			Vector3[] vertices = mesh.vertices;
			int[] triangles = mesh.triangles;
			bool changed = false;

			for (int i = 0; i < triangles.Length; i += 3)
			{
				int ia = triangles[i];
				int ib = triangles[i + 1];
				int ic = triangles[i + 2];

				Vector3 a = vertices[ia];
				Vector3 b = vertices[ib];
				Vector3 c = vertices[ic];

				Vector3 faceNormal = Vector3.Cross(b - a, c - a);
				Vector3 faceCenter = (a + b + c) / 3f;
				if (Vector3.Dot(faceNormal, faceCenter - center) < 0f)
				{
					triangles[i + 1] = ic;
					triangles[i + 2] = ib;
					changed = true;
				}
			}

			if (changed)
			{
				mesh.triangles = triangles;
			}

			mesh.RecalculateNormals();
			mesh.RecalculateBounds();
		}

		public static void EnsureMeshFacesPointTowardCenter(Mesh mesh, Vector3 center)
		{
			if (mesh == null)
			{
				return;
			}

			Vector3[] vertices = mesh.vertices;
			int[] triangles = mesh.triangles;
			bool changed = false;

			for (int i = 0; i < triangles.Length; i += 3)
			{
				int ia = triangles[i];
				int ib = triangles[i + 1];
				int ic = triangles[i + 2];

				Vector3 a = vertices[ia];
				Vector3 b = vertices[ib];
				Vector3 c = vertices[ic];

				Vector3 faceNormal = Vector3.Cross(b - a, c - a);
				Vector3 faceCenter = (a + b + c) / 3f;
				if (Vector3.Dot(faceNormal, center - faceCenter) < 0f)
				{
					triangles[i + 1] = ic;
					triangles[i + 2] = ib;
					changed = true;
				}
			}

			if (changed)
			{
				mesh.triangles = triangles;
			}

			mesh.RecalculateNormals();
			mesh.RecalculateBounds();
		}
	}
}
