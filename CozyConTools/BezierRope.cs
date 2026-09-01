// BezierRope.cs
// Runtime component for a Bezier-based rope/vine.
// Place this file outside any Editor folder so it compiles into player builds.
//
// Public API is provided so the Editor window can add/insert/remove points and draw the curve.

using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

[ExecuteAlways]
[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class BezierRope : MonoBehaviour
{
	[Header("Curve Points (local space)")]
	public List<Vector3> points = new List<Vector3>() { Vector3.zero, Vector3.right * 2f };

	[Header("Control handles (local offsets)")]
	public List<Vector3> handleA = new List<Vector3>();
	public List<Vector3> handleB = new List<Vector3>();
	[Range(0f, 2f)]
	[Tooltip("Strength used by Auto Smooth when placing tangent handles.")]
	public float autoSmoothFactor = 1f;

	[Header("Rope/Vine Settings")]
	public float radius = 0.05f;
	public int radialSegments = 8;
	public int lengthSegmentsPerCurve = 12;
	public bool closed = false;
	public bool capEnds = true;
	public bool smoothNormals = true;
	public Material cylinderMaterial;
	public enum Mode { Rope, Vine }
	public Mode mode = Mode.Vine;

	[Header("Leaf Settings")]
	public GameObject leafPrefab;
	public int leafCount = 0;
	public float leafMinScale = 0.1f;
	public float leafMaxScale = 0.3f;
	public float leafRandomRotation = 30f;
	public float leafOffset = 0.0f;

	[Header("Debug")]
	public bool autoRebuild = true;

	MeshFilter mf;
	Mesh mesh;

	void OnEnable()
	{
		mf = GetComponent<MeshFilter>();
		EnsureMeshRendererMaterial();
		if (mf.sharedMesh == null)
		{
			mesh = new Mesh();
			mesh.name = "BezierRopeMesh";
			mf.sharedMesh = mesh;
		}
		else mesh = mf.sharedMesh;
		EnsureHandles();
		if (autoRebuild) BuildMesh();
	}

	void OnValidate()
	{
		EnsureHandles();
		EnsureMeshRendererMaterial();
		if (autoRebuild) BuildMesh();
	}

	/// <summary>Ensure handle lists match points count.</summary>
	public void EnsureHandles()
	{
		while (handleA.Count < points.Count) handleA.Add(Vector3.left * 0.5f);
		while (handleB.Count < points.Count) handleB.Add(Vector3.right * 0.5f);
		while (handleA.Count > points.Count) handleA.RemoveAt(handleA.Count - 1);
		while (handleB.Count > points.Count) handleB.RemoveAt(handleB.Count - 1);
	}

	void EnsureMeshRendererMaterial()
	{
		MeshRenderer meshRenderer = GetComponent<MeshRenderer>();
		if (meshRenderer == null) return;

#if UNITY_EDITOR
		if (cylinderMaterial == null)
			cylinderMaterial = AssetDatabase.GetBuiltinExtraResource<Material>("Default-Material.mat");
#endif

		if (cylinderMaterial != null && meshRenderer.sharedMaterial != cylinderMaterial)
			meshRenderer.sharedMaterial = cylinderMaterial;
	}

	// ---------------- Public Editor API ----------------

	/// <summary>Get world-space position of a point.</summary>
	public Vector3 GetPointWorld(int index)
	{
		if (index < 0 || index >= points.Count) return transform.position;
		return transform.TransformPoint(points[index]);
	}

	/// <summary>Set world-space position of a point.</summary>
	public void SetPointWorld(int index, Vector3 worldPos)
	{
		if (index < 0 || index >= points.Count) return;
#if UNITY_EDITOR
		Undo.RecordObject(this, "Move Bezier Point");
#endif
		points[index] = transform.InverseTransformPoint(worldPos);
		EnsureHandles();
		if (autoRebuild) BuildMesh();
	}

	/// <summary>Append a point at world position.</summary>
	public void AddPoint(Vector3 worldPos)
	{
#if UNITY_EDITOR
		Undo.RecordObject(this, "Add Bezier Point");
#endif
		points.Add(transform.InverseTransformPoint(worldPos));
		EnsureHandles();
		if (autoRebuild) BuildMesh();
	}

	/// <summary>Insert a point before index (clamped).</summary>
	public void InsertPoint(int index, Vector3 worldPos)
	{
		int clamped = Mathf.Clamp(index, 0, points.Count);
#if UNITY_EDITOR
		Undo.RecordObject(this, "Insert Bezier Point");
#endif
		points.Insert(clamped, transform.InverseTransformPoint(worldPos));
		EnsureHandles();
		if (autoRebuild) BuildMesh();
	}

	/// <summary>Remove a point (keeps at least 2).</summary>
	public void RemovePoint(int index)
	{
		if (points.Count <= 2) return;
		if (index < 0 || index >= points.Count) return;
#if UNITY_EDITOR
		Undo.RecordObject(this, "Remove Bezier Point");
#endif
		points.RemoveAt(index);
		EnsureHandles();
		if (autoRebuild) BuildMesh();
	}

	/// <summary>Recompute all handles from neighboring points to create a smooth curve.</summary>
	public void AutoSmoothHandles()
	{
		EnsureHandles();
		BezierAutoSmooth.Apply(points, handleA, handleB, closed, autoSmoothFactor);
		if (autoRebuild) BuildMesh();
	}

	// ---------------- Bezier math ----------------

	/// <summary>Sample curve into world-space positions and tangents.</summary>
	public void SampleCurve(out List<Vector3> positions, out List<Vector3> tangents)
	{
		positions = new List<Vector3>();
		tangents = new List<Vector3>();
		if (points.Count < 2) return;

		int segmentsPer = Mathf.Max(1, lengthSegmentsPerCurve);
		for (int i = 0; i < points.Count - 1; i++)
		{
			Vector3 p0 = points[i];
			Vector3 p3 = points[i + 1];
			Vector3 p1 = p0 + handleB[i];
			Vector3 p2 = p3 + handleA[i + 1];

			for (int s = 0; s <= segmentsPer; s++)
			{
				float t = s / (float)segmentsPer;
				positions.Add(transform.TransformPoint(BezierCurveUtils.EvaluateCubic(p0, p1, p2, p3, t)));
				tangents.Add(transform.TransformDirection(BezierCurveUtils.EvaluateCubicTangent(p0, p1, p2, p3, t).normalized));
			}
		}

		if (closed)
		{
			Vector3 p0 = points[points.Count - 1];
			Vector3 p3 = points[0];
			Vector3 p1 = p0 + handleB[points.Count - 1];
			Vector3 p2 = p3 + handleA[0];
			for (int s = 1; s <= segmentsPer; s++)
			{
				float t = s / (float)segmentsPer;
				positions.Add(transform.TransformPoint(BezierCurveUtils.EvaluateCubic(p0, p1, p2, p3, t)));
				tangents.Add(transform.TransformDirection(BezierCurveUtils.EvaluateCubicTangent(p0, p1, p2, p3, t).normalized));
			}
		}
	}

	void SampleCurveLocal(out List<Vector3> positions, out List<Vector3> tangents)
	{
		positions = new List<Vector3>();
		tangents = new List<Vector3>();
		if (points.Count < 2) return;

		int segmentsPer = Mathf.Max(1, lengthSegmentsPerCurve);
		for (int i = 0; i < points.Count - 1; i++)
		{
			Vector3 p0 = points[i];
			Vector3 p3 = points[i + 1];
			Vector3 p1 = p0 + handleB[i];
			Vector3 p2 = p3 + handleA[i + 1];

			for (int s = 0; s <= segmentsPer; s++)
			{
				float t = s / (float)segmentsPer;
				positions.Add(BezierCurveUtils.EvaluateCubic(p0, p1, p2, p3, t));
				tangents.Add(BezierCurveUtils.EvaluateCubicTangent(p0, p1, p2, p3, t).normalized);
			}
		}

		if (closed)
		{
			Vector3 p0 = points[points.Count - 1];
			Vector3 p3 = points[0];
			Vector3 p1 = p0 + handleB[points.Count - 1];
			Vector3 p2 = p3 + handleA[0];
			for (int s = 1; s <= segmentsPer; s++)
			{
				float t = s / (float)segmentsPer;
				positions.Add(BezierCurveUtils.EvaluateCubic(p0, p1, p2, p3, t));
				tangents.Add(BezierCurveUtils.EvaluateCubicTangent(p0, p1, p2, p3, t).normalized);
			}
		}
	}

	Vector3[] CalculateOutwardNormals(Vector3[] verts, List<Vector3> ringCenters, int vertsPerRing)
	{
		Vector3[] outwardNormals = new Vector3[verts.Length];

		for (int ringIndex = 0; ringIndex < ringCenters.Count; ringIndex++)
		{
			Vector3 ringCenter = ringCenters[ringIndex];
			int ringOffset = ringIndex * vertsPerRing;

			for (int vertexIndex = 0; vertexIndex < vertsPerRing; vertexIndex++)
			{
				Vector3 outward = verts[ringOffset + vertexIndex] - ringCenter;
				outwardNormals[ringOffset + vertexIndex] = outward.sqrMagnitude > 1e-6f ? outward.normalized : Vector3.up;
			}
		}

		return outwardNormals;
	}

	void AddCap(
		List<Vector3> vertexList,
		List<Vector3> normalList,
		List<Vector2> uvList,
		List<int> triangleList,
		Vector3 ringCenter,
		Vector3 capNormal,
		int ringVertexOffset,
		int vertsPerRing,
		bool isStartCap)
	{
		int capVertexStart = vertexList.Count;
		for (int vertexIndex = 0; vertexIndex < vertsPerRing; vertexIndex++)
		{
			Vector3 vertex = vertexList[ringVertexOffset + vertexIndex];
			Vector3 radial = vertex - ringCenter;
			vertexList.Add(vertex);
			normalList.Add(capNormal);
			uvList.Add(new Vector2(0.5f + radial.x / (radius * 2f), 0.5f + radial.y / (radius * 2f)));
		}

		int centerIndex = vertexList.Count;
		vertexList.Add(ringCenter);
		normalList.Add(capNormal);
		uvList.Add(new Vector2(0.5f, 0.5f));

		for (int segmentIndex = 0; segmentIndex < radialSegments; segmentIndex++)
		{
			int current = capVertexStart + segmentIndex;
			int next = capVertexStart + segmentIndex + 1;

			if (isStartCap)
			{
				triangleList.Add(centerIndex);
				triangleList.Add(next);
				triangleList.Add(current);
			}
			else
			{
				triangleList.Add(centerIndex);
				triangleList.Add(current);
				triangleList.Add(next);
			}
		}
	}

	// ---------------- Mesh builder ----------------

	/// <summary>Build a cylindrical mesh along the curve and assign to MeshFilter.</summary>
	public void BuildMesh()
	{
		if (mesh == null) mesh = new Mesh();
		mesh.Clear();
		EnsureMeshRendererMaterial();

		SampleCurveLocal(out List<Vector3> positions, out List<Vector3> tangents);
		if (positions.Count < 2)
		{
			if (mf == null) mf = GetComponent<MeshFilter>();
			if (mf != null) mf.sharedMesh = mesh;
			return;
		}

		int rings = positions.Count;
		int vertsPerRing = radialSegments + 1;
		Vector3[] verts = new Vector3[rings * vertsPerRing];
		Vector3[] normals = new Vector3[verts.Length];
		Vector2[] uvs = new Vector2[verts.Length];
		List<int> triangleList = new List<int>((rings - 1) * radialSegments * 6 + (capEnds && !closed ? radialSegments * 6 : 0));

		Vector3 up = Vector3.up;
		for (int i = 0; i < rings; i++)
		{
			Vector3 forward = tangents[i];
			if (forward.sqrMagnitude < 1e-6f) forward = Vector3.forward;
			Vector3 right = Vector3.Cross(up, forward).normalized;
			if (right.sqrMagnitude < 1e-6f) right = Vector3.Cross(Vector3.forward, forward).normalized;
			Vector3 localUp = Vector3.Cross(forward, right).normalized;
			up = localUp;

			for (int r = 0; r < vertsPerRing; r++)
			{
				float ang = (r / (float)radialSegments) * Mathf.PI * 2f;
				Vector3 offset = right * Mathf.Cos(ang) * radius + localUp * Mathf.Sin(ang) * radius;
				verts[i * vertsPerRing + r] = positions[i] + offset;
				normals[i * vertsPerRing + r] = offset.normalized;
				uvs[i * vertsPerRing + r] = new Vector2(r / (float)radialSegments, i / (float)(rings - 1));
			}
		}

		normals = CalculateOutwardNormals(verts, positions, vertsPerRing);

		for (int i = 0; i < rings - 1; i++)
		{
			for (int r = 0; r < radialSegments; r++)
			{
				int a = i * vertsPerRing + r;
				int b = (i + 1) * vertsPerRing + r;
				int c = (i + 1) * vertsPerRing + (r + 1);
				int d = i * vertsPerRing + (r + 1);

				triangleList.Add(a);
				triangleList.Add(c);
				triangleList.Add(b);

				triangleList.Add(a);
				triangleList.Add(d);
				triangleList.Add(c);
			}
		}

		List<Vector3> vertexList = new List<Vector3>(verts);
		List<Vector3> normalList = new List<Vector3>(normals);
		List<Vector2> uvList = new List<Vector2>(uvs);

		if (capEnds && !closed)
		{
			Vector3 startNormal = tangents[0].sqrMagnitude > 1e-6f ? -tangents[0].normalized : Vector3.back;
			Vector3 endNormal = tangents[rings - 1].sqrMagnitude > 1e-6f ? tangents[rings - 1].normalized : Vector3.forward;

			AddCap(vertexList, normalList, uvList, triangleList, positions[0], startNormal, 0, vertsPerRing, true);
			AddCap(vertexList, normalList, uvList, triangleList, positions[rings - 1], endNormal, (rings - 1) * vertsPerRing, vertsPerRing, false);
		}

		mesh.vertices = vertexList.ToArray();
		mesh.triangles = triangleList.ToArray();
		mesh.uv = uvList.ToArray();
		mesh.normals = normalList.ToArray();
		mesh.RecalculateBounds();

		if (mf == null) mf = GetComponent<MeshFilter>();
		mf.sharedMesh = mesh;
	}

	// ---------------- Leaves ----------------

#if UNITY_EDITOR
	class SpawnLeavesOperation
	{
		public BezierRope rope;
		public List<Vector3> positions;
		public List<Vector3> tangents;
		public System.Random random;
		public int nextIndex;
		public int totalCount;
	}

	SpawnLeavesOperation activeLeafSpawn;

	void UpdateLeafSpawnProgress()
	{
		if (activeLeafSpawn == null || activeLeafSpawn.rope != this) return;

		int batchSize = Mathf.Max(1, Mathf.Min(25, activeLeafSpawn.totalCount - activeLeafSpawn.nextIndex));
		for (int i = 0; i < batchSize; i++)
		{
			if (activeLeafSpawn.nextIndex >= activeLeafSpawn.totalCount) break;
			int spawnIndex = activeLeafSpawn.nextIndex++;
			float u = (float)activeLeafSpawn.random.NextDouble() * (activeLeafSpawn.positions.Count - 1);
			int idx = Mathf.Clamp(Mathf.RoundToInt(u), 0, activeLeafSpawn.positions.Count - 1);
			Vector3 pos = activeLeafSpawn.positions[idx];
			Vector3 tan = activeLeafSpawn.tangents[idx];
			SpawnLeafInstance(spawnIndex, pos, tan, activeLeafSpawn.random);
		}

		float progress = activeLeafSpawn.totalCount == 0 ? 1f : activeLeafSpawn.nextIndex / (float)activeLeafSpawn.totalCount;
		bool cancel = EditorUtility.DisplayCancelableProgressBar("Spawning Leaves", $"Leaf {activeLeafSpawn.nextIndex}/{activeLeafSpawn.totalCount}", progress);
		if (cancel || activeLeafSpawn.nextIndex >= activeLeafSpawn.totalCount)
		{
			EditorApplication.update -= UpdateLeafSpawnProgress;
			EditorUtility.ClearProgressBar();
			activeLeafSpawn = null;
		}
	}

	public void SpawnLeavesWithProgress()
	{
		if (activeLeafSpawn != null && activeLeafSpawn.rope == this)
			return;

		List<Transform> toRemove = new List<Transform>();
		foreach (Transform t in transform)
			if (t.name.StartsWith("Leaf_")) toRemove.Add(t);
		foreach (var t in toRemove) { if (Application.isEditor) DestroyImmediate(t.gameObject); else Destroy(t.gameObject); }

		if (leafCount <= 0) return;

		SampleCurve(out List<Vector3> positions, out List<Vector3> tangents);
		if (positions.Count == 0) return;

		activeLeafSpawn = new SpawnLeavesOperation
		{
			rope = this,
			positions = positions,
			tangents = tangents,
			random = new System.Random(12345),
			nextIndex = 0,
			totalCount = leafCount
		};

		EditorApplication.update += UpdateLeafSpawnProgress;
		UpdateLeafSpawnProgress();
	}

	void SpawnLeafInstance(int index, Vector3 pos, Vector3 tan, System.Random rnd)
	{
		Vector3 normal = Vector3.Cross(tan, Vector3.up).normalized;
		if (normal.sqrMagnitude < 1e-6f) normal = Vector3.Cross(tan, Vector3.right).normalized;

		GameObject leaf;
		if (leafPrefab != null)
		{
			leaf = Instantiate(leafPrefab, pos + normal * leafOffset, Quaternion.identity, transform);
			leaf.name = "Leaf_" + index;
		}
		else
		{
			leaf = CreateTwoSidedQuad("Leaf_" + index);
			leaf.transform.SetParent(transform, false);
			leaf.transform.position = pos + normal * leafOffset;
		}

		float scale = Mathf.Lerp(leafMinScale, leafMaxScale, (float)rnd.NextDouble());
		leaf.transform.localScale = Vector3.one * scale;

		Quaternion rot = Quaternion.LookRotation(tan, normal);
		float jitter = (float)(rnd.NextDouble() * 2.0 - 1.0) * leafRandomRotation;
		leaf.transform.rotation = rot * Quaternion.Euler(0, jitter, 0);
	}
#endif

	/// <summary>Spawn two-sided quads along the curve as leaves.</summary>
	public void SpawnLeaves()
	{
		List<Transform> toRemove = new List<Transform>();
		foreach (Transform t in transform)
			if (t.name.StartsWith("Leaf_")) toRemove.Add(t);
		foreach (var t in toRemove) { if (Application.isEditor) DestroyImmediate(t.gameObject); else Destroy(t.gameObject); }

		if (leafCount <= 0) return;

		SampleCurve(out List<Vector3> positions, out List<Vector3> tangents);
		if (positions.Count == 0) return;

		System.Random rnd = new System.Random(12345);
		for (int i = 0; i < leafCount; i++)
		{
			float u = (float)rnd.NextDouble() * (positions.Count - 1);
			int idx = Mathf.Clamp(Mathf.RoundToInt(u), 0, positions.Count - 1);
			Vector3 pos = positions[idx];
			Vector3 tan = tangents[idx];
			Vector3 normal = Vector3.Cross(tan, Vector3.up).normalized;
			if (normal.sqrMagnitude < 1e-6f) normal = Vector3.Cross(tan, Vector3.right).normalized;

			GameObject leaf;
			if (leafPrefab != null)
			{
				leaf = Instantiate(leafPrefab, pos + normal * leafOffset, Quaternion.identity, transform);
				leaf.name = "Leaf_" + i;
			}
			else
			{
				leaf = CreateTwoSidedQuad("Leaf_" + i);
				leaf.transform.SetParent(transform, false);
				leaf.transform.position = pos + normal * leafOffset;
			}

			float scale = Mathf.Lerp(leafMinScale, leafMaxScale, (float)rnd.NextDouble());
			leaf.transform.localScale = Vector3.one * scale;

			Quaternion rot = Quaternion.LookRotation(tan, normal);
			float jitter = (float)(rnd.NextDouble() * 2.0 - 1.0) * leafRandomRotation;
			leaf.transform.rotation = rot * Quaternion.Euler(0, jitter, 0);
		}
	}

	GameObject CreateTwoSidedQuad(string name)
	{
		GameObject go = new GameObject(name);
		MeshFilter mfLocal = go.AddComponent<MeshFilter>();
		MeshRenderer mr = go.AddComponent<MeshRenderer>();
		Mesh m = new Mesh();
		m.name = name + "_Mesh";
		Vector3[] v = new Vector3[] {
			new Vector3(-0.5f, -0.5f, 0),
			new Vector3(0.5f, -0.5f, 0),
			new Vector3(0.5f, 0.5f, 0),
			new Vector3(-0.5f, 0.5f, 0)
		};
		int[] t1 = new int[] { 0, 1, 2, 0, 2, 3 };
		int[] t2 = new int[] { 2, 1, 0, 3, 2, 0 };
		int[] tris = new int[t1.Length + t2.Length];
		t1.CopyTo(tris, 0);
		t2.CopyTo(tris, t1.Length);
		Vector2[] uv = new Vector2[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) };
		m.vertices = v;
		m.triangles = tris;
		m.uv = uv;
		m.RecalculateNormals();
		mfLocal.sharedMesh = m;
		mr.sharedMaterial = new Material(Shader.Find("Standard"));
		return go;
	}

	// ---------------- Editor helper ----------------

#if UNITY_EDITOR
	/// <summary>Save the generated mesh as an asset at the given path (editor only).</summary>
	public void SaveMeshAsset(string assetPath)
	{
		if (mesh == null) return;
		Mesh copy = Instantiate(mesh);
		AssetDatabase.CreateAsset(copy, assetPath);
		AssetDatabase.SaveAssets();
		AssetDatabase.Refresh();
	}
#endif
}
