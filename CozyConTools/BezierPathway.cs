// BezierPathway.cs
// Core component: curve data, handles, and small helpers.
// Delegates mesh construction and OBJ export to BezierPathwayMeshBuilder.

using System.Collections.Generic;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

[ExecuteAlways]
[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class BezierPathway : MonoBehaviour
{
	[Header("Bezier Curve")]
	public List<Vector3> points = new List<Vector3>();
	public List<Vector3> handleA = new List<Vector3>();
	public List<Vector3> handleB = new List<Vector3>();

	[Tooltip("Number of sample steps per curve segment used for arc-length sampling.")]
	public int lengthSamplesPerCurve = 64;
	[Range(0f, 2f)]
	[Tooltip("Strength used by Auto Smooth when placing tangent handles.")]
	public float autoSmoothFactor = 1f;

	[Header("Curve Options")]
	[Tooltip("When enabled, the final point connects back to the first point.")]
	public bool loopPathway = false;

	[Header("Pathway")]
	public bool autoRebuild = true;
	public Material pathMaterial;
	public Material sidingMaterial;

	[Tooltip("Width of the main pathway (X axis).")]
	public float pathWidth = 1.0f;   // 1m wide

	[Tooltip("Height of the main pathway (Y axis).")]
	public float pathHeight = 0.2f;  // 0.2m tall

	[Header("Legacy siding fields (kept for compatibility)")]
	public float sidingWidth = 0.3f;
	public float sidingHeight = 0.3f;
	public float sidingOffset = 0.1f;
	public int lengthSegmentsPerCurve = 8;

	[Header("Mesh Options")]
	public bool generateEndCaps = true;

	[Header("Sides")]
	[Tooltip("Generate left/right sides attached to the pathway.")]
	public bool generateSides = false;
	[Tooltip("Outward thickness of the side (meters).")]
	public float sideWidth = 0.1f;
	[Tooltip("Height of the side above the path base (meters).")]
	public float sideHeight = 0.25f;

	[Header("Bake For VRChat")]
	[Tooltip("Child object name used for the baked render mesh.")]
	public string bakedChildName = "BezierPathway_Baked";
	[Tooltip("Project folder where baked mesh assets are created.")]
	public string bakedAssetFolder = "Assets/CozyConTools/Generated/BezierPathway";
	[Tooltip("Disables the source renderer on this object after baking so only the baked child is visible.")]
	public bool disableSourceRendererAfterBake = true;
	[HideInInspector] public string bakedMeshAssetPath = "";

	[System.Serializable]
	public struct DamageTriangle
	{
		public Vector3 a;
		public Vector3 b;
		public Vector3 c;

		public DamageTriangle(Vector3 a, Vector3 b, Vector3 c)
		{
			this.a = a;
			this.b = b;
			this.c = c;
		}
	}

	[Header("Damage")]
	public List<DamageTriangle> damageTriangles = new List<DamageTriangle>();

	[Header("Instructions")]
	[TextArea(4, 10)]
	public string usageInstructions =
		"Bezier Pathway Tool:\n" +
		"- Create Mode: Click in Scene to add points (left click).\n" +
		"- Edit Mode: Click points or tangent handles to move them.\n" +
		"- Damage Mode: Click three times to define a triangular region; a pyramid-shaped cut is added.\n" +
		"- Shift+Click on the curve to insert a point between segments.\n" +
		"- Each 1m segment maps to full 0..1 UV tile (overlapping).\n" +
		"- UV1 is generated for lightmapping.\n" +
		"- Use 'Rebuild Mesh Now' if Auto Rebuild is disabled.";

	// Mesh components (exposed so the mesh builder can access them)
	[HideInInspector] public MeshFilter meshFilter;
	[HideInInspector] public MeshRenderer meshRenderer;
	[HideInInspector] public Mesh mesh;

	void Reset()
	{
		EnsureComponents();
		AssignDefaultMaterials();

		if (meshFilter.sharedMesh == null)
		{
			mesh = new Mesh();
			mesh.name = "BezierPathwayMesh";
			meshFilter.sharedMesh = mesh;
		}
		else mesh = meshFilter.sharedMesh;

		if (points.Count == 0)
		{
			points.Add(Vector3.zero);
			points.Add(Vector3.forward * 4f);
			EnsureHandles(true);
		}

		BuildMesh();
	}

	// MADE PUBLIC: so external mesh builder can ensure components are present
	public void EnsureComponents()
	{
		meshFilter = GetComponent<MeshFilter>();
		meshRenderer = GetComponent<MeshRenderer>();
		if (meshFilter == null) meshFilter = gameObject.AddComponent<MeshFilter>();
		if (meshRenderer == null) meshRenderer = gameObject.AddComponent<MeshRenderer>();
	}

	void AssignDefaultMaterials()
	{
#if UNITY_EDITOR
		if (pathMaterial == null)
			pathMaterial = AssetDatabase.GetBuiltinExtraResource<Material>("Default-Diffuse.mat");
		if (sidingMaterial == null)
			sidingMaterial = AssetDatabase.GetBuiltinExtraResource<Material>("Default-Diffuse.mat");
#endif
	}

	void OnValidate()
	{
		EnsureComponents();
		AssignDefaultMaterials();
		if (loopPathway) generateEndCaps = false;
		EnsureHandles();
		if (autoRebuild) BuildMesh();
	}

	void Awake()
	{
		EnsureComponents();
		AssignDefaultMaterials();

		if (meshFilter.sharedMesh == null)
		{
			mesh = new Mesh();
			mesh.name = "BezierPathwayMesh";
			meshFilter.sharedMesh = mesh;
		}
		else mesh = meshFilter.sharedMesh;
	}

	// ---------------- Curve utilities -------------------------------------

	public void EnsureHandles(bool recalculateAll = false)
	{
		int count = points.Count;
		if (handleA == null) handleA = new List<Vector3>();
		if (handleB == null) handleB = new List<Vector3>();

		int oldCountA = handleA.Count;
		int oldCountB = handleB.Count;

		while (handleA.Count < count) handleA.Add(Vector3.zero);
		while (handleB.Count < count) handleB.Add(Vector3.zero);
		while (handleA.Count > count) handleA.RemoveAt(handleA.Count - 1);
		while (handleB.Count > count) handleB.RemoveAt(handleB.Count - 1);

		float defaultLen = 0.5f;

		if (count == 0) return;
		if (count == 1)
		{
			if (recalculateAll || oldCountA == 0) handleA[0] = -Vector3.forward * defaultLen;
			if (recalculateAll || oldCountB == 0) handleB[0] = Vector3.forward * defaultLen;
			return;
		}

		for (int i = 0; i < count; i++)
		{
			bool setA = recalculateAll || i >= oldCountA;
			bool setB = recalculateAll || i >= oldCountB;
			if (!setA && !setB) continue;

			Vector3 forwardLocal = BezierCurveUtils.ComputeAutoHandleForward(points, i, loopPathway, Vector3.forward);

			if (setA) handleA[i] = -forwardLocal * defaultLen;
			if (setB) handleB[i] = forwardLocal * defaultLen;
		}
	}

	public void AddPoint(Vector3 worldPos)
	{
		points.Add(transform.InverseTransformPoint(worldPos));
		EnsureHandles();
		if (autoRebuild) BuildMesh();
	}

	public void InsertPoint(int index, Vector3 worldPos)
	{
		index = Mathf.Clamp(index, 0, points.Count);
		points.Insert(index, transform.InverseTransformPoint(worldPos));
		EnsureHandles();
		if (autoRebuild) BuildMesh();
	}

	public void RemovePoint(int index)
	{
		if (index < 0 || index >= points.Count) return;
		points.RemoveAt(index);
		handleA.RemoveAt(index);
		handleB.RemoveAt(index);
		if (autoRebuild) BuildMesh();
	}

	public Vector3 GetPointWorld(int index)
	{
		if (index < 0 || index >= points.Count) return transform.position;
		return transform.TransformPoint(points[index]);
	}

	public void SetPointWorld(int index, Vector3 worldPos)
	{
		if (index < 0 || index >= points.Count) return;
		points[index] = transform.InverseTransformPoint(worldPos);
		EnsureHandles();
		if (autoRebuild) BuildMesh();
	}

	/// <summary>
	/// Recompute all handles from neighboring points to create a smooth curve.
	/// </summary>
	public void AutoSmoothHandles()
	{
		EnsureHandles();
		BezierAutoSmooth.Apply(points, handleA, handleB, loopPathway, autoSmoothFactor);
		if (autoRebuild) BuildMesh();
	}

	// Sample the whole curve densely (for arc-length resampling)
	public void DenseSampleCurve(out List<Vector3> positions, out List<Vector3> tangents)
	{
		positions = new List<Vector3>();
		tangents = new List<Vector3>();

		if (points.Count < 2)
		{
			positions.Add(transform.position);
			tangents.Add(Vector3.forward);
			return;
		}

		int segmentCount = loopPathway ? points.Count : points.Count - 1;
		for (int i = 0; i < segmentCount; i++)
		{
			Vector3 p0 = transform.TransformPoint(points[i]);
			int nextIndex = (i + 1) % points.Count;
			Vector3 p3 = transform.TransformPoint(points[nextIndex]);
			Vector3 p1 = transform.TransformPoint(points[i] + handleB[i]);
			Vector3 p2 = transform.TransformPoint(points[nextIndex] + handleA[nextIndex]);

			int steps = Mathf.Max(4, lengthSamplesPerCurve);
			int startStep = (i > 0) ? 1 : 0;
			for (int s = startStep; s <= steps; s++)
			{
				float t = s / (float)steps;
				positions.Add(BezierCurveUtils.EvaluateCubic(p0, p1, p2, p3, t));
				tangents.Add(BezierCurveUtils.EvaluateCubicTangent(p0, p1, p2, p3, t).normalized);
			}
		}

		if (loopPathway && positions.Count > 1)
		{
			Vector3 first = positions[0];
			Vector3 last = positions[positions.Count - 1];
			if ((first - last).sqrMagnitude > 1e-6f)
			{
				positions.Add(first);
				tangents.Add(tangents[0]);
			}
		}
	}

	// Resample by arc length to produce positions spaced at exactly 'spacing' meters.
	public List<Vector3> ResampleByArcLength(List<Vector3> densePositions, float spacing, out List<Vector3> outTangents)
	{
		outTangents = new List<Vector3>();
		List<float> cumulative = new List<float>(densePositions.Count);
		cumulative.Add(0f);
		for (int i = 1; i < densePositions.Count; i++)
		{
			float d = Vector3.Distance(densePositions[i - 1], densePositions[i]);
			cumulative.Add(cumulative[i - 1] + d);
		}

		float total = cumulative[cumulative.Count - 1];
		if (total <= 0f)
		{
			for (int i = 0; i < densePositions.Count; i++) outTangents.Add(Vector3.forward);
			return new List<Vector3>(densePositions);
		}

		List<Vector3> result = new List<Vector3>();

		for (float s = 0f; s <= total + 1e-6f; s += spacing)
		{
			if (s > total) s = total;
			int idx = cumulative.BinarySearch(s);
			if (idx < 0) idx = ~idx;
			if (idx == 0) idx = 1;
			if (idx >= cumulative.Count) idx = cumulative.Count - 1;

			float tSeg = (s - cumulative[idx - 1]) / Mathf.Max(1e-6f, (cumulative[idx] - cumulative[idx - 1]));
			Vector3 p = Vector3.Lerp(densePositions[idx - 1], densePositions[idx], tSeg);
			result.Add(p);

			Vector3 tan;
			if (idx < densePositions.Count - 1)
				tan = (densePositions[idx + 1] - densePositions[Mathf.Max(0, idx - 1)]).normalized;
			else
				tan = (densePositions[idx] - densePositions[idx - 1]).normalized;
			outTangents.Add(tan);

			if (s == total) break;
		}

		if (result.Count == 0 || result[result.Count - 1] != densePositions[densePositions.Count - 1])
		{
			result.Add(densePositions[densePositions.Count - 1]);
			outTangents.Add((densePositions[densePositions.Count - 1] - densePositions[densePositions.Count - 2]).normalized);
		}

		return result;
	}

	// ---------------- Mesh building delegation ---------------------------------------

	// Build mesh by delegating to the mesh builder class
	public void BuildMesh()
	{
		EnsureComponents();
		BezierPathwayMeshBuilder.BuildMesh(this);
	}

	// Simple wrapper to export OBJ via the mesh builder
	public void SaveMeshObj(string assetPath)
	{
		EnsureComponents();
		BezierPathwayMeshBuilder.SaveMeshObj(this, assetPath);
	}

#if UNITY_EDITOR
	/// <summary>
	/// Bakes the generated mesh into a child GameObject using a saved mesh asset so it renders without runtime C# execution.
	/// </summary>
	public bool BakeRenderableChild()
	{
		EnsureComponents();
		return BezierPathwayMeshBuilder.BakeRenderableChild(this);
	}
#endif

	// -----------------------------------------------------------------------------
	// Compatibility wrappers used by editor tools and other callers
	// These forward to the internal sampling / damage logic so external code
	// (editor windows, exporters, etc.) can keep calling the same API.
	// -----------------------------------------------------------------------------

	/// <summary>
	/// Public wrapper used by editor code to sample the curve densely.
	/// For compatibility with older code that called SampleCurve on the component.
	/// </summary>
	public void SampleCurve(out List<Vector3> positions, out List<Vector3> tangents)
	{
		// DenseSampleCurve already exists and returns world-space positions and tangents.
		// Keep the same behavior as before by forwarding the call.
		DenseSampleCurve(out positions, out tangents);
	}

	/// <summary>
	/// Public wrapper to add a damage triangle to the pathway and optionally rebuild.
	/// Kept for compatibility with editor/exporter code that calls AddDamageTriangle on the component.
	/// </summary>
	public void AddDamageTriangle(Vector3 a, Vector3 b, Vector3 c)
	{
		// Ensure the list exists
		if (damageTriangles == null) damageTriangles = new List<DamageTriangle>();

		damageTriangles.Add(new DamageTriangle(a, b, c));

		// Keep previous behavior: rebuild mesh if autoRebuild is enabled.
		if (autoRebuild) BuildMesh();
	}

}
