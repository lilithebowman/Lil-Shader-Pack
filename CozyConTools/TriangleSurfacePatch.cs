#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace CozyCon.Tools
{
	[ExecuteAlways]
	public class TriangleSurfacePatch : MonoBehaviour
	{
		[Serializable]
		public struct TriangleFace
		{
			public int a;
			public int b;
			public int c;

			public TriangleFace(int aIndex, int bIndex, int cIndex)
			{
				a = aIndex;
				b = bIndex;
				c = cIndex;
			}
		}

		private struct EdgeKey : IEquatable<EdgeKey>
		{
			public readonly int min;
			public readonly int max;

			public EdgeKey(int v0, int v1)
			{
				if (v0 < v1)
				{
					min = v0;
					max = v1;
				}
				else
				{
					min = v1;
					max = v0;
				}
			}

			public bool Equals(EdgeKey other)
			{
				return min == other.min && max == other.max;
			}

			public override bool Equals(object obj)
			{
				return obj is EdgeKey key && Equals(key);
			}

			public override int GetHashCode()
			{
				unchecked
				{
					return (min * 397) ^ max;
				}
			}
		}

		private struct FaceUv
		{
			public int chartId;
			public Vector2 a;
			public Vector2 b;
			public Vector2 c;

			public Vector2 GetByVertex(int vertexIndex, TriangleFace face)
			{
				if (vertexIndex == face.a)
				{
					return a;
				}

				if (vertexIndex == face.b)
				{
					return b;
				}

				return c;
			}

			public void SetByVertex(int vertexIndex, TriangleFace face, Vector2 value)
			{
				if (vertexIndex == face.a)
				{
					a = value;
				}
				else if (vertexIndex == face.b)
				{
					b = value;
				}
				else
				{
					c = value;
				}
			}
		}

		private const string AnchorsContainerName = "Anchors";
		private const string MeshFilterName = "TriangleSurfaceMesh";
		private const float AnchorVisualScale = 0.08f;
		private const float Epsilon = 1e-5f;
		private const string MetadataDirectory = "Assets/Editor/CozyConTools/TriangleSurfaceMetadata";

		[SerializeField] private Transform anchorsContainer;
		[SerializeField] private List<Transform> anchors = new List<Transform>();
		[SerializeField] private List<TriangleFace> faces = new List<TriangleFace>();
		[SerializeField] private Material surfaceMaterial;
		[SerializeField] private Mesh generatedMesh;

		public int AnchorCount => anchors.Count;
		public int FaceCount => faces.Count;
		public Material SurfaceMaterial
		{
			get => surfaceMaterial;
			set
			{
				surfaceMaterial = value;
				ApplyMaterial();
			}
		}

		public IReadOnlyList<TriangleFace> Faces => faces;

		public void SetEditorHelperActive(bool active)
		{
			if (Application.isPlaying)
			{
				enabled = false;
				return;
			}

			enabled = active;
			if (anchorsContainer != null)
			{
				anchorsContainer.gameObject.SetActive(active);
			}
		}

		public string SaveMetadataToTextFile(string filePath)
		{
			if (string.IsNullOrEmpty(filePath))
			{
				return null;
			}

			StringBuilder builder = new StringBuilder();
			builder.AppendLine("TriangleSurfacePatch");
			builder.AppendLine("Anchors=" + anchors.Count);

			for (int i = 0; i < anchors.Count; i++)
			{
				Transform anchor = anchors[i];
				if (anchor == null)
				{
					continue;
				}

				Vector3 position = anchor.position;
				builder.AppendLine(string.Format("A:{0:F6},{1:F6},{2:F6}", position.x, position.y, position.z));
			}

			builder.AppendLine("Faces=" + faces.Count);
			for (int i = 0; i < faces.Count; i++)
			{
				TriangleFace face = faces[i];
				builder.AppendLine(string.Format("F:{0},{1},{2}", face.a, face.b, face.c));
			}

			File.WriteAllText(filePath, builder.ToString());
			return filePath;
		}

		public bool TryLoadMetadataFromTextFile(string filePath)
		{
			if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
			{
				return false;
			}

			string[] lines = File.ReadAllLines(filePath);
			if (lines == null || lines.Length == 0)
			{
				return false;
			}

			EnsureScaffold();
			anchors.Clear();
			faces.Clear();

			int expectedAnchorCount = -1;
			int expectedFaceCount = -1;
			int anchorIndex = 0;
			int faceIndex = 0;

			for (int i = 0; i < lines.Length; i++)
			{
				string line = lines[i].Trim();
				if (string.IsNullOrEmpty(line) || line.StartsWith("TriangleSurfacePatch"))
				{
					continue;
				}

				if (line.StartsWith("Anchors="))
				{
					int.TryParse(line.Substring("Anchors=".Length), out expectedAnchorCount);
					continue;
				}

				if (line.StartsWith("Faces="))
				{
					int.TryParse(line.Substring("Faces=".Length), out expectedFaceCount);
					continue;
				}

				if (line.StartsWith("A:"))
				{
					string[] values = line.Substring(2).Split(',');
					if (values.Length == 3)
					{
						float x = float.Parse(values[0]);
						float y = float.Parse(values[1]);
						float z = float.Parse(values[2]);
						AddAnchor(new Vector3(x, y, z));
						anchorIndex++;
					}
					continue;
				}

				if (line.StartsWith("F:"))
				{
					string[] values = line.Substring(2).Split(',');
					if (values.Length == 3)
					{
						int a = int.Parse(values[0]);
						int b = int.Parse(values[1]);
						int c = int.Parse(values[2]);
						faces.Add(new TriangleFace(a, b, c));
						faceIndex++;
					}
				}
			}

			if (expectedAnchorCount > 0 && anchorIndex != expectedAnchorCount)
			{
				return false;
			}

			if (expectedFaceCount > 0 && faceIndex != expectedFaceCount)
			{
				return false;
			}

			RebuildMesh();
			return true;
		}

		public void LoadFromMesh(Mesh sourceMesh, Transform sourceTransform)
		{
			if (sourceMesh == null)
			{
				return;
			}

			EnsureScaffold();
			anchors.Clear();
			faces.Clear();

			Vector3[] vertices = sourceMesh.vertices;
			int[] triangles = sourceMesh.triangles;
			for (int i = 0; i < vertices.Length; i++)
			{
				Vector3 worldPosition = sourceTransform != null ? sourceTransform.TransformPoint(vertices[i]) : vertices[i];
				AddAnchor(worldPosition);
			}

			for (int i = 0; i < triangles.Length; i += 3)
			{
				if (i + 2 >= triangles.Length)
				{
					break;
				}

				faces.Add(new TriangleFace(triangles[i], triangles[i + 1], triangles[i + 2]));
			}

			RebuildMesh();
		}

		private void OnEnable()
		{
			if (Application.isPlaying)
			{
				Destroy(gameObject);
				return;
			}

			EnsureScaffold();
			RebuildMesh();
		}

		private void OnValidate()
		{
			if (Application.isPlaying)
			{
				return;
			}

			EnsureScaffold();
			RebuildMesh();
		}

		public void InitializeTriangle(Vector3 center, Vector3 normal, float radius)
		{
			EnsureScaffold();
			anchors.Clear();
			faces.Clear();

			Vector3 safeNormal = normal.sqrMagnitude > Epsilon ? normal.normalized : Vector3.up;
			Vector3 tangent = Vector3.Cross(Mathf.Abs(Vector3.Dot(safeNormal, Vector3.up)) > 0.95f ? Vector3.right : Vector3.up, safeNormal).normalized;
			Vector3 bitangent = Vector3.Cross(safeNormal, tangent).normalized;

			float clampedRadius = Mathf.Max(0.05f, radius);
			Vector3 p0 = center + tangent * clampedRadius;
			Vector3 p1 = center + (-0.5f * tangent + 0.8660254f * bitangent) * clampedRadius;
			Vector3 p2 = center + (-0.5f * tangent - 0.8660254f * bitangent) * clampedRadius;

			int i0 = AddAnchor(p0);
			int i1 = AddAnchor(p1);
			int i2 = AddAnchor(p2);
			faces.Add(new TriangleFace(i0, i1, i2));

			RebuildMesh();
		}

		public int AddAnchor(Vector3 worldPosition)
		{
			EnsureScaffold();
			GameObject anchorObject = new GameObject("Anchor " + anchors.Count);
			anchorObject.transform.SetParent(anchorsContainer, true);
			anchorObject.transform.position = worldPosition;
			anchorObject.transform.localScale = Vector3.one * AnchorVisualScale;

			TriangleSurfaceAnchor anchor = anchorObject.GetComponent<TriangleSurfaceAnchor>();
			if (anchor == null)
			{
				anchor = anchorObject.AddComponent<TriangleSurfaceAnchor>();
			}

			int index = anchors.Count;
			anchor.Initialize(this, index);
			anchors.Add(anchorObject.transform);
			return index;
		}

		public void AddTriangle(int a, int b, int c)
		{
			if (!IsValidAnchorIndex(a) || !IsValidAnchorIndex(b) || !IsValidAnchorIndex(c))
			{
				return;
			}

			if (a == b || b == c || a == c)
			{
				return;
			}

			faces.Add(new TriangleFace(a, b, c));
			RebuildMesh();
		}

		public bool MergeAnchorInto(int sourceAnchorIndex, int targetAnchorIndex)
		{
			if (!IsValidAnchorIndex(sourceAnchorIndex) || !IsValidAnchorIndex(targetAnchorIndex) || sourceAnchorIndex == targetAnchorIndex)
			{
				return false;
			}

			for (int i = 0; i < faces.Count; i++)
			{
				TriangleFace face = faces[i];
				if (face.a == sourceAnchorIndex)
				{
					face.a = targetAnchorIndex;
				}

				if (face.b == sourceAnchorIndex)
				{
					face.b = targetAnchorIndex;
				}

				if (face.c == sourceAnchorIndex)
				{
					face.c = targetAnchorIndex;
				}

				if (face.a > sourceAnchorIndex)
				{
					face.a--;
				}

				if (face.b > sourceAnchorIndex)
				{
					face.b--;
				}

				if (face.c > sourceAnchorIndex)
				{
					face.c--;
				}

				faces[i] = face;
			}

			for (int i = faces.Count - 1; i >= 0; i--)
			{
				TriangleFace face = faces[i];
				if (face.a == face.b || face.b == face.c || face.a == face.c)
				{
					faces.RemoveAt(i);
				}
			}

			anchors.RemoveAt(sourceAnchorIndex);
			RebuildMesh();
			return true;
		}

		public Transform GetAnchor(int index)
		{
			if (!IsValidAnchorIndex(index))
			{
				return null;
			}

			return anchors[index];
		}

		public bool TryGetAnchorIndex(Transform anchorTransform, out int index)
		{
			for (int i = 0; i < anchors.Count; i++)
			{
				if (anchors[i] == anchorTransform)
				{
					index = i;
					return true;
				}
			}

			index = -1;
			return false;
		}

		public void RebuildMesh()
		{
			RebuildMesh(null);
		}

		public void AutoUnwrap(float maxStretch)
		{
			if (faces.Count == 0)
			{
				return;
			}

			maxStretch = Mathf.Max(0f, maxStretch);
			List<FaceUv> faceUvs = BuildFaceUvs(maxStretch);
			PackCharts(faceUvs);
			RebuildMesh(faceUvs);
		}

		public void SmoothNormals(float smoothingFactor)
		{
			smoothingFactor = Mathf.Clamp01(smoothingFactor);
			if (smoothingFactor <= Epsilon)
			{
				return;
			}

			if (generatedMesh == null || generatedMesh.vertexCount == 0)
			{
				RebuildMesh();
			}

			if (generatedMesh == null || generatedMesh.vertexCount == 0)
			{
				return;
			}

			Vector3[] normals = generatedMesh.normals;
			if (normals == null || normals.Length != generatedMesh.vertexCount)
			{
				generatedMesh.RecalculateNormals();
				normals = generatedMesh.normals;
			}

			List<int> anchorForVertex = BuildAnchorMappingForGeneratedVertices();
			if (anchorForVertex.Count != generatedMesh.vertexCount)
			{
				return;
			}

			Vector3[] anchorNormalSums = new Vector3[anchors.Count];
			int[] anchorNormalCounts = new int[anchors.Count];
			for (int i = 0; i < anchorForVertex.Count; i++)
			{
				int anchorIndex = anchorForVertex[i];
				if (!IsValidAnchorIndex(anchorIndex))
				{
					continue;
				}

				anchorNormalSums[anchorIndex] += normals[i];
				anchorNormalCounts[anchorIndex]++;
			}

			for (int i = 0; i < normals.Length; i++)
			{
				int anchorIndex = anchorForVertex[i];
				if (!IsValidAnchorIndex(anchorIndex) || anchorNormalCounts[anchorIndex] == 0)
				{
					continue;
				}

				Vector3 anchorAverage = anchorNormalSums[anchorIndex] / anchorNormalCounts[anchorIndex];
				if (anchorAverage.sqrMagnitude <= Epsilon)
				{
					continue;
				}

				Vector3 from = normals[i].sqrMagnitude > Epsilon ? normals[i].normalized : anchorAverage.normalized;
				Vector3 to = anchorAverage.normalized;
				normals[i] = Vector3.Slerp(from, to, smoothingFactor).normalized;
			}

			generatedMesh.normals = normals;
			generatedMesh.RecalculateTangents();
		}

		private void RebuildMesh(List<FaceUv> faceUvs)
		{
			EnsureScaffold();
			SyncAnchorMetadata();

			if (generatedMesh == null)
			{
				generatedMesh = new Mesh();
				generatedMesh.name = MeshFilterName;
			}
			else
			{
				generatedMesh.Clear();
			}

			if (faces.Count == 0)
			{
				ApplyMeshToFilter();
				return;
			}

			List<Vector3> vertices = new List<Vector3>(faces.Count * 3);
			List<int> triangles = new List<int>(faces.Count * 3);
			List<Vector2> uvs = new List<Vector2>(faces.Count * 3);

			for (int i = 0; i < faces.Count; i++)
			{
				TriangleFace face = faces[i];
				if (!IsValidFace(face))
				{
					continue;
				}

				Vector3 localA = transform.InverseTransformPoint(anchors[face.a].position);
				Vector3 localB = transform.InverseTransformPoint(anchors[face.b].position);
				Vector3 localC = transform.InverseTransformPoint(anchors[face.c].position);

				int start = vertices.Count;
				vertices.Add(localA);
				vertices.Add(localB);
				vertices.Add(localC);

				triangles.Add(start);
				triangles.Add(start + 1);
				triangles.Add(start + 2);

				if (faceUvs != null && i < faceUvs.Count)
				{
					uvs.Add(faceUvs[i].a);
					uvs.Add(faceUvs[i].b);
					uvs.Add(faceUvs[i].c);
				}
				else
				{
					uvs.Add(new Vector2(localA.x, localA.z));
					uvs.Add(new Vector2(localB.x, localB.z));
					uvs.Add(new Vector2(localC.x, localC.z));
				}
			}

			generatedMesh.SetVertices(vertices);
			generatedMesh.SetTriangles(triangles, 0, true);
			generatedMesh.SetUVs(0, uvs);
			generatedMesh.RecalculateNormals();
			generatedMesh.RecalculateBounds();
			generatedMesh.RecalculateTangents();

			ApplyMeshToFilter();
			ApplyMaterial();
		}

		private List<int> BuildAnchorMappingForGeneratedVertices()
		{
			List<int> mapping = new List<int>(faces.Count * 3);
			for (int i = 0; i < faces.Count; i++)
			{
				TriangleFace face = faces[i];
				if (!IsValidFace(face))
				{
					continue;
				}

				mapping.Add(face.a);
				mapping.Add(face.b);
				mapping.Add(face.c);
			}

			return mapping;
		}

		private List<FaceUv> BuildFaceUvs(float maxStretch)
		{
			List<FaceUv> faceUvs = new List<FaceUv>(faces.Count);
			for (int i = 0; i < faces.Count; i++)
			{
				faceUvs.Add(new FaceUv { chartId = -1 });
			}

			Dictionary<EdgeKey, List<int>> edgeToFaces = BuildEdgeMap();
			int nextChartId = 0;

			for (int seedFace = 0; seedFace < faces.Count; seedFace++)
			{
				if (faceUvs[seedFace].chartId >= 0 || !IsValidFace(faces[seedFace]))
				{
					continue;
				}

				TriangleFace seed = faces[seedFace];
				Vector3 seedA = anchors[seed.a].position;
				Vector3 seedB = anchors[seed.b].position;
				Vector3 seedC = anchors[seed.c].position;

				float ab = Vector3.Distance(seedA, seedB);
				float ac = Vector3.Distance(seedA, seedC);
				float bc = Vector3.Distance(seedB, seedC);
				if (ab < Epsilon || ac < Epsilon || bc < Epsilon)
				{
					continue;
				}

				FaceUv seedUv = faceUvs[seedFace];
				seedUv.chartId = nextChartId;
				seedUv.a = Vector2.zero;
				seedUv.b = new Vector2(ab, 0f);
				ComputeThirdPoint(seedUv.a, seedUv.b, ac, bc, 1f, out seedUv.c);
				faceUvs[seedFace] = seedUv;

				Queue<int> queue = new Queue<int>();
				queue.Enqueue(seedFace);

				while (queue.Count > 0)
				{
					int currentFaceIndex = queue.Dequeue();
					TriangleFace currentFace = faces[currentFaceIndex];
					FaceUv currentUv = faceUvs[currentFaceIndex];

					TryGrowToNeighbor(currentFaceIndex, currentFace, currentUv, currentFace.a, currentFace.b, currentFace.c, faceUvs, edgeToFaces, queue, maxStretch);
					TryGrowToNeighbor(currentFaceIndex, currentFace, currentUv, currentFace.b, currentFace.c, currentFace.a, faceUvs, edgeToFaces, queue, maxStretch);
					TryGrowToNeighbor(currentFaceIndex, currentFace, currentUv, currentFace.c, currentFace.a, currentFace.b, faceUvs, edgeToFaces, queue, maxStretch);
				}

				nextChartId++;
			}

			for (int i = 0; i < faceUvs.Count; i++)
			{
				if (faceUvs[i].chartId < 0)
				{
					faceUvs[i] = BuildFallbackFaceUv(i, nextChartId++);
				}
			}

			return faceUvs;
		}

		private void TryGrowToNeighbor(
			int currentFaceIndex,
			TriangleFace currentFace,
			FaceUv currentUv,
			int edgeA,
			int edgeB,
			int currentThird,
			List<FaceUv> faceUvs,
			Dictionary<EdgeKey, List<int>> edgeToFaces,
			Queue<int> queue,
			float maxStretch)
		{
			EdgeKey edge = new EdgeKey(edgeA, edgeB);
			if (!edgeToFaces.TryGetValue(edge, out List<int> linkedFaces))
			{
				return;
			}

			for (int i = 0; i < linkedFaces.Count; i++)
			{
				int neighborFaceIndex = linkedFaces[i];
				if (neighborFaceIndex == currentFaceIndex)
				{
					continue;
				}

				if (faceUvs[neighborFaceIndex].chartId >= 0)
				{
					continue;
				}

				TriangleFace neighborFace = faces[neighborFaceIndex];
				if (!TryGetThirdVertex(neighborFace, edgeA, edgeB, out int neighborThird))
				{
					continue;
				}

				Vector2 uvA = currentUv.GetByVertex(edgeA, currentFace);
				Vector2 uvB = currentUv.GetByVertex(edgeB, currentFace);
				Vector2 uvCurrentThird = currentUv.GetByVertex(currentThird, currentFace);

				Vector3 worldA = anchors[edgeA].position;
				Vector3 worldB = anchors[edgeB].position;
				Vector3 worldThird = anchors[neighborThird].position;

				float worldAToThird = Vector3.Distance(worldA, worldThird);
				float worldBToThird = Vector3.Distance(worldB, worldThird);
				float worldAB = Vector3.Distance(worldA, worldB);
				if (worldAToThird < Epsilon || worldBToThird < Epsilon || worldAB < Epsilon)
				{
					continue;
				}

				Vector2 edgeDirection = uvB - uvA;
				float signedSide = Mathf.Sign(Cross2D(edgeDirection, uvCurrentThird - uvA));
				if (Mathf.Abs(signedSide) < Epsilon)
				{
					signedSide = 1f;
				}

				if (!ComputeThirdPoint(uvA, uvB, worldAToThird, worldBToThird, -signedSide, out Vector2 uvNeighborThird))
				{
					continue;
				}

				float stretch = ComputeStretch(worldAB, worldAToThird, worldBToThird, Vector2.Distance(uvA, uvB), Vector2.Distance(uvA, uvNeighborThird), Vector2.Distance(uvB, uvNeighborThird));
				if (stretch > maxStretch)
				{
					continue;
				}

				FaceUv neighborUv = faceUvs[neighborFaceIndex];
				neighborUv.chartId = currentUv.chartId;
				neighborUv.SetByVertex(edgeA, neighborFace, uvA);
				neighborUv.SetByVertex(edgeB, neighborFace, uvB);
				neighborUv.SetByVertex(neighborThird, neighborFace, uvNeighborThird);
				faceUvs[neighborFaceIndex] = neighborUv;
				queue.Enqueue(neighborFaceIndex);
			}
		}

		private FaceUv BuildFallbackFaceUv(int faceIndex, int chartId)
		{
			TriangleFace face = faces[faceIndex];
			Vector3 a = anchors[face.a].position;
			Vector3 b = anchors[face.b].position;
			Vector3 c = anchors[face.c].position;

			float ab = Vector3.Distance(a, b);
			float ac = Vector3.Distance(a, c);
			float bc = Vector3.Distance(b, c);

			FaceUv fallback = new FaceUv
			{
				chartId = chartId,
				a = Vector2.zero,
				b = new Vector2(ab, 0f),
				c = new Vector2(ac * 0.5f, bc * 0.5f)
			};

			ComputeThirdPoint(fallback.a, fallback.b, ac, bc, 1f, out fallback.c);
			return fallback;
		}

		private void PackCharts(List<FaceUv> faceUvs)
		{
			Dictionary<int, Bounds2D> chartBounds = new Dictionary<int, Bounds2D>();
			for (int i = 0; i < faceUvs.Count; i++)
			{
				FaceUv faceUv = faceUvs[i];
				if (!chartBounds.TryGetValue(faceUv.chartId, out Bounds2D bounds))
				{
					bounds = new Bounds2D(faceUv.a);
				}

				bounds.Encapsulate(faceUv.a);
				bounds.Encapsulate(faceUv.b);
				bounds.Encapsulate(faceUv.c);
				chartBounds[faceUv.chartId] = bounds;
			}

			List<int> chartIds = new List<int>(chartBounds.Keys);
			chartIds.Sort();

			const float padding = 0.1f;
			float cursorX = 0f;
			float maxHeight = 0f;
			Dictionary<int, Vector2> chartOffsets = new Dictionary<int, Vector2>();

			for (int i = 0; i < chartIds.Count; i++)
			{
				int chartId = chartIds[i];
				Bounds2D bounds = chartBounds[chartId];
				Vector2 size = bounds.Size;
				chartOffsets[chartId] = new Vector2(cursorX - bounds.min.x, -bounds.min.y);
				cursorX += size.x + padding;
				if (size.y > maxHeight)
				{
					maxHeight = size.y;
				}
			}

			float totalWidth = Mathf.Max(cursorX, Epsilon);
			float totalHeight = Mathf.Max(maxHeight, Epsilon);
			float scale = 1f / Mathf.Max(totalWidth, totalHeight);

			for (int i = 0; i < faceUvs.Count; i++)
			{
				FaceUv faceUv = faceUvs[i];
				Vector2 offset = chartOffsets[faceUv.chartId];
				faceUv.a = (faceUv.a + offset) * scale;
				faceUv.b = (faceUv.b + offset) * scale;
				faceUv.c = (faceUv.c + offset) * scale;
				faceUvs[i] = faceUv;
			}
		}

		private Dictionary<EdgeKey, List<int>> BuildEdgeMap()
		{
			Dictionary<EdgeKey, List<int>> map = new Dictionary<EdgeKey, List<int>>();
			for (int faceIndex = 0; faceIndex < faces.Count; faceIndex++)
			{
				TriangleFace face = faces[faceIndex];
				if (!IsValidFace(face))
				{
					continue;
				}

				AddEdge(map, new EdgeKey(face.a, face.b), faceIndex);
				AddEdge(map, new EdgeKey(face.b, face.c), faceIndex);
				AddEdge(map, new EdgeKey(face.c, face.a), faceIndex);
			}

			return map;
		}

		private static void AddEdge(Dictionary<EdgeKey, List<int>> map, EdgeKey key, int faceIndex)
		{
			if (!map.TryGetValue(key, out List<int> faceIndices))
			{
				faceIndices = new List<int>();
				map[key] = faceIndices;
			}

			faceIndices.Add(faceIndex);
		}

		private static bool TryGetThirdVertex(TriangleFace face, int a, int b, out int third)
		{
			if (face.a != a && face.a != b)
			{
				third = face.a;
				return true;
			}

			if (face.b != a && face.b != b)
			{
				third = face.b;
				return true;
			}

			if (face.c != a && face.c != b)
			{
				third = face.c;
				return true;
			}

			third = -1;
			return false;
		}

		private static float ComputeStretch(float worldAB, float worldAC, float worldBC, float uvAB, float uvAC, float uvBC)
		{
			float r0 = uvAB / Mathf.Max(worldAB, Epsilon);
			float r1 = uvAC / Mathf.Max(worldAC, Epsilon);
			float r2 = uvBC / Mathf.Max(worldBC, Epsilon);
			float min = Mathf.Min(r0, Mathf.Min(r1, r2));
			float max = Mathf.Max(r0, Mathf.Max(r1, r2));
			if (min < Epsilon)
			{
				return float.MaxValue;
			}

			return (max / min) - 1f;
		}

		private static bool ComputeThirdPoint(Vector2 a, Vector2 b, float acLength, float bcLength, float sideSign, out Vector2 c)
		{
			Vector2 ab = b - a;
			float d = ab.magnitude;
			if (d < Epsilon)
			{
				c = a;
				return false;
			}

			float x = ((acLength * acLength) - (bcLength * bcLength) + (d * d)) / (2f * d);
			float ySquared = (acLength * acLength) - (x * x);
			if (ySquared < -Epsilon)
			{
				c = a;
				return false;
			}

			float y = Mathf.Sqrt(Mathf.Max(0f, ySquared));
			Vector2 ex = ab / d;
			Vector2 ey = new Vector2(-ex.y, ex.x) * (sideSign >= 0f ? 1f : -1f);
			c = a + (ex * x) + (ey * y);
			return true;
		}

		private static float Cross2D(Vector2 lhs, Vector2 rhs)
		{
			return (lhs.x * rhs.y) - (lhs.y * rhs.x);
		}

		private bool IsValidFace(TriangleFace face)
		{
			if (!IsValidAnchorIndex(face.a) || !IsValidAnchorIndex(face.b) || !IsValidAnchorIndex(face.c))
			{
				return false;
			}

			return face.a != face.b && face.b != face.c && face.a != face.c;
		}

		private bool IsValidAnchorIndex(int index)
		{
			return index >= 0 && index < anchors.Count && anchors[index] != null;
		}

		private void EnsureScaffold()
		{
			if (anchorsContainer == null)
			{
				Transform existing = transform.Find(AnchorsContainerName);
				if (existing != null)
				{
					anchorsContainer = existing;
				}
				else
				{
					GameObject container = new GameObject(AnchorsContainerName);
					container.transform.SetParent(transform, false);
					anchorsContainer = container.transform;
				}
			}

			MeshFilter filter = gameObject.GetComponent<MeshFilter>();
			if (filter == null)
			{
				filter = gameObject.AddComponent<MeshFilter>();
			}

			MeshRenderer renderer = gameObject.GetComponent<MeshRenderer>();
			if (renderer == null)
			{
				renderer = gameObject.AddComponent<MeshRenderer>();
			}

			ApplyMeshToFilter();
			ApplyMaterial();
		}

		private void ApplyMeshToFilter()
		{
			MeshFilter filter = gameObject.GetComponent<MeshFilter>();
			if (filter != null)
			{
				filter.sharedMesh = generatedMesh;
			}
		}

		private void ApplyMaterial()
		{
			MeshRenderer renderer = gameObject.GetComponent<MeshRenderer>();
			if (renderer != null && surfaceMaterial != null)
			{
				renderer.sharedMaterial = surfaceMaterial;
			}
		}

		private void SyncAnchorMetadata()
		{
			for (int i = 0; i < anchors.Count; i++)
			{
				Transform anchorTransform = anchors[i];
				if (anchorTransform == null)
				{
					continue;
				}

				TriangleSurfaceAnchor anchor = anchorTransform.GetComponent<TriangleSurfaceAnchor>();
				if (anchor == null)
				{
					anchor = anchorTransform.gameObject.AddComponent<TriangleSurfaceAnchor>();
				}

				anchor.Initialize(this, i);
				anchorTransform.name = "Anchor " + i;
			}
		}

		[Serializable]
		private struct Bounds2D
		{
			public Vector2 min;
			public Vector2 max;

			public Bounds2D(Vector2 point)
			{
				min = point;
				max = point;
			}

			public void Encapsulate(Vector2 point)
			{
				min = Vector2.Min(min, point);
				max = Vector2.Max(max, point);
			}

			public Vector2 Size => max - min;
		}
	}

	[Serializable]
	public class TriangleSurfacePatchData
	{
		public string name;
		public List<Vector3> anchorPositions;
		public List<TriangleSurfacePatch.TriangleFace> faces;
	}

	public class TriangleSurfaceAnchor : MonoBehaviour
	{
		[SerializeField] private TriangleSurfacePatch owner;
		[SerializeField] private int anchorIndex;

		public TriangleSurfacePatch Owner => owner;
		public int AnchorIndex => anchorIndex;

		public void Initialize(TriangleSurfacePatch patch, int index)
		{
			owner = patch;
			anchorIndex = index;
		}
	}
}
#endif
