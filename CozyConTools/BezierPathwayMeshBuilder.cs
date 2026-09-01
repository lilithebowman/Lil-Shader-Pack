// BezierPathwayMeshBuilder.cs
// Mesh builder for BezierPathway moved into its own file.
// Contains BuildMesh(BezierPathway) and SaveMeshObj(...) exporter.

using System.Collections.Generic;
using System.Text;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
using System.IO;
#endif

public static class BezierPathwayMeshBuilder
{
	// Helper to add a quad with consistent winding (a,b,c,d) -> (a,b,c) (a,c,d)
	static void AddQuad(List<int> tris, int a, int b, int c, int d)
	{
		tris.Add(a); tris.Add(b); tris.Add(c);
		tris.Add(a); tris.Add(c); tris.Add(d);
	}

	// Public entry point: builds mesh for the provided BezierPathway instance.
	public static void BuildMesh(BezierPathway path)
	{
		if (path == null) return;
		if (path.loopPathway) path.generateEndCaps = false;

		// Ensure components are present on the path (EnsureComponents is public on the component)
		path.EnsureComponents();

		MeshFilter meshFilter = path.meshFilter;
		MeshRenderer meshRenderer = path.meshRenderer;
		Mesh mesh = meshFilter.sharedMesh;
		if (mesh == null)
		{
			mesh = new Mesh();
			mesh.name = "BezierPathwayMesh";
			meshFilter.sharedMesh = mesh;
		}
		else mesh.Clear();

		// Dense sample + arc-length resample (world-space positions)
		path.DenseSampleCurve(out List<Vector3> densePos, out List<Vector3> denseTan);
		List<Vector3> sliceTangents;
		List<Vector3> sliceCentersWorld = path.ResampleByArcLength(densePos, 1.0f, out sliceTangents);

		if (sliceCentersWorld == null || sliceCentersWorld.Count < 2)
		{
			mesh.Clear();
			return;
		}

		int sliceCount = sliceCentersWorld.Count;
		float halfW = path.pathWidth * 0.5f;
		Vector3 worldUp = Vector3.up;

		// Precompute per-slice right vectors and left/right world positions
		Vector3[] rights = new Vector3[sliceCount];
		Vector3[] leftWorld = new Vector3[sliceCount];
		Vector3[] rightWorld = new Vector3[sliceCount];
		Vector3[] forwards = new Vector3[sliceCount];

		for (int i = 0; i < sliceCount; i++)
		{
			Vector3 centerW = sliceCentersWorld[i];
			Vector3 forwardW = sliceTangents[Mathf.Min(i, sliceTangents.Count - 1)];
			if (forwardW.sqrMagnitude < 1e-6f) forwardW = Vector3.forward;
			forwardW.Normalize();

			Vector3 rightW = Vector3.Cross(worldUp, forwardW);
			if (rightW.sqrMagnitude < 1e-6f) rightW = Vector3.right;
			rightW.Normalize();

			rights[i] = rightW;
			forwards[i] = forwardW;
			leftWorld[i] = centerW - rightW * halfW;
			rightWorld[i] = centerW + rightW * halfW;
		}

		// Vertex lists
		List<Vector3> vertsLocal = new List<Vector3>();
		List<Vector3> normalsLocal = new List<Vector3>();
		List<Vector2> uv0 = new List<Vector2>();
		List<Vector2> uv1 = new List<Vector2>();

		// Triangles for submeshes
		List<int> trisPath = new List<int>();
		List<int> trisSides = new List<int>();

		// Local-space normals for bottom and top
		Vector3 localDownNormal = path.transform.InverseTransformDirection(-worldUp).normalized;
		Vector3 localUpNormal = path.transform.InverseTransformDirection(worldUp).normalized;

		// Build vertices per slice.
		int[] sliceBaseIndex = new int[sliceCount];

		for (int i = 0; i < sliceCount; i++)
		{
			sliceBaseIndex[i] = vertsLocal.Count;

			// Baseline inner edge positions (world)
			Vector3 leftBaseW = leftWorld[i];
			Vector3 rightBaseW = rightWorld[i];

			// Path top positions (pathHeight above baseline)
			Vector3 leftPathTopW = leftBaseW + worldUp * path.pathHeight;
			Vector3 rightPathTopW = rightBaseW + worldUp * path.pathHeight;

			// Convert to local
			Vector3 leftBaseLocal = path.transform.InverseTransformPoint(leftBaseW);
			Vector3 rightBaseLocal = path.transform.InverseTransformPoint(rightBaseW);
			Vector3 leftPathTopLocal = path.transform.InverseTransformPoint(leftPathTopW);
			Vector3 rightPathTopLocal = path.transform.InverseTransformPoint(rightPathTopW);

			// --- PATH VERTICES (per slice) ---
			vertsLocal.Add(leftBaseLocal);   // 0
			vertsLocal.Add(rightBaseLocal);  // 1
			vertsLocal.Add(leftPathTopLocal);  // 2
			vertsLocal.Add(rightPathTopLocal); // 3

			normalsLocal.Add(localDownNormal);
			normalsLocal.Add(localDownNormal);
			normalsLocal.Add(localUpNormal);
			normalsLocal.Add(localUpNormal);

			// UV0: X across path (0..1), V along slices (repeat every 1m)
			float vCoord = (float)i;
			uv0.Add(new Vector2(0f, vCoord));
			uv0.Add(new Vector2(1f, vCoord));
			uv0.Add(new Vector2(0f, vCoord));
			uv0.Add(new Vector2(1f, vCoord));

			// UV1: world-space based secondary UVs
			uv1.Add(new Vector2(leftBaseLocal.x * 0.1f, leftBaseLocal.z * 0.1f));
			uv1.Add(new Vector2(rightBaseLocal.x * 0.1f, rightBaseLocal.z * 0.1f));
			uv1.Add(new Vector2(leftPathTopLocal.x * 0.1f, leftPathTopLocal.z * 0.1f));
			uv1.Add(new Vector2(rightPathTopLocal.x * 0.1f, rightPathTopLocal.z * 0.1f));

			if (!path.generateSides)
			{
				// --- PATH VERTICAL WALL DUPLICATES (when sides disabled) ---
				vertsLocal.Add(leftBaseLocal);    // 4
				vertsLocal.Add(leftPathTopLocal); // 5
				vertsLocal.Add(rightBaseLocal);   // 6
				vertsLocal.Add(rightPathTopLocal);// 7

				Vector3 leftOutLocal = path.transform.InverseTransformDirection(-rights[i]).normalized;
				Vector3 rightOutLocal = path.transform.InverseTransformDirection(rights[i]).normalized;

				normalsLocal.Add(leftOutLocal);
				normalsLocal.Add(leftOutLocal);
				normalsLocal.Add(rightOutLocal);
				normalsLocal.Add(rightOutLocal);

				uv0.Add(new Vector2(0f, vCoord));
				uv0.Add(new Vector2(0f, vCoord));
				uv0.Add(new Vector2(1f, vCoord));
				uv0.Add(new Vector2(1f, vCoord));

				uv1.Add(new Vector2(leftBaseLocal.x * 0.1f, leftBaseLocal.z * 0.1f));
				uv1.Add(new Vector2(leftPathTopLocal.x * 0.1f, leftPathTopLocal.z * 0.1f));
				uv1.Add(new Vector2(rightBaseLocal.x * 0.1f, rightBaseLocal.z * 0.1f));
				uv1.Add(new Vector2(rightPathTopLocal.x * 0.1f, rightPathTopLocal.z * 0.1f));
			}
			else
			{
				// --- SIDES: full side geometry per slice ---
				Vector3 leftInnerBottomW = leftBaseW;
				Vector3 leftInnerTopW = leftBaseW + worldUp * path.sideHeight;
				Vector3 leftOuterBottomW = leftBaseW - rights[i] * path.sideWidth;
				Vector3 leftOuterTopW = leftOuterBottomW + worldUp * path.sideHeight;

				Vector3 rightInnerBottomW = rightBaseW;
				Vector3 rightInnerTopW = rightBaseW + worldUp * path.sideHeight;
				Vector3 rightOuterBottomW = rightBaseW + rights[i] * path.sideWidth;
				Vector3 rightOuterTopW = rightOuterBottomW + worldUp * path.sideHeight;

				Vector3 leftInnerBottomLocal = path.transform.InverseTransformPoint(leftInnerBottomW);
				Vector3 leftInnerTopLocal = path.transform.InverseTransformPoint(leftInnerTopW);
				Vector3 leftOuterBottomLocal = path.transform.InverseTransformPoint(leftOuterBottomW);
				Vector3 leftOuterTopLocal = path.transform.InverseTransformPoint(leftOuterTopW);

				Vector3 rightInnerBottomLocal = path.transform.InverseTransformPoint(rightInnerBottomW);
				Vector3 rightInnerTopLocal = path.transform.InverseTransformPoint(rightInnerTopW);
				Vector3 rightOuterBottomLocal = path.transform.InverseTransformPoint(rightOuterBottomW);
				Vector3 rightOuterTopLocal = path.transform.InverseTransformPoint(rightOuterTopW);

				// Append left side verts
				vertsLocal.Add(leftInnerBottomLocal); // 4
				vertsLocal.Add(leftInnerTopLocal);    // 5
				vertsLocal.Add(leftOuterBottomLocal); // 6
				vertsLocal.Add(leftOuterTopLocal);    // 7

				// Append right side verts
				vertsLocal.Add(rightInnerBottomLocal); // 8
				vertsLocal.Add(rightInnerTopLocal);    // 9
				vertsLocal.Add(rightOuterBottomLocal); // 10
				vertsLocal.Add(rightOuterTopLocal);    // 11

				Vector3 leftInnerNormalLocal = path.transform.InverseTransformDirection(rights[i]).normalized;
				Vector3 leftOuterNormalLocal = path.transform.InverseTransformDirection(-rights[i]).normalized;
				Vector3 rightInnerNormalLocal = path.transform.InverseTransformDirection(-rights[i]).normalized;
				Vector3 rightOuterNormalLocal = path.transform.InverseTransformDirection(rights[i]).normalized;

				normalsLocal.Add(leftInnerNormalLocal); // left inner bottom
				normalsLocal.Add(localUpNormal);        // left inner top (top face)
				normalsLocal.Add(leftOuterNormalLocal); // left outer bottom
				normalsLocal.Add(localUpNormal);        // left outer top (top face)

				normalsLocal.Add(rightInnerNormalLocal); // right inner bottom
				normalsLocal.Add(localUpNormal);         // right inner top
				normalsLocal.Add(rightOuterNormalLocal); // right outer bottom
				normalsLocal.Add(localUpNormal);         // right outer top

				// UV0: make sides tile per 1m along V (same vCoord as path) and U across inner->outer (0..1)
				uv0.Add(new Vector2(0f, vCoord)); // left inner bottom
				uv0.Add(new Vector2(0f, vCoord)); // left inner top
				uv0.Add(new Vector2(1f, vCoord)); // left outer bottom
				uv0.Add(new Vector2(1f, vCoord)); // left outer top

				uv0.Add(new Vector2(0f, vCoord)); // right inner bottom
				uv0.Add(new Vector2(0f, vCoord)); // right inner top
				uv0.Add(new Vector2(1f, vCoord)); // right outer bottom
				uv0.Add(new Vector2(1f, vCoord)); // right outer top

				// UV1: world-space based secondary UVs
				uv1.Add(new Vector2(leftInnerBottomLocal.x * 0.1f, leftInnerBottomLocal.z * 0.1f));
				uv1.Add(new Vector2(leftInnerTopLocal.x * 0.1f, leftInnerTopLocal.z * 0.1f));
				uv1.Add(new Vector2(leftOuterBottomLocal.x * 0.1f, leftOuterBottomLocal.z * 0.1f));
				uv1.Add(new Vector2(leftOuterTopLocal.x * 0.1f, leftOuterTopLocal.z * 0.1f));

				uv1.Add(new Vector2(rightInnerBottomLocal.x * 0.1f, rightInnerBottomLocal.z * 0.1f));
				uv1.Add(new Vector2(rightInnerTopLocal.x * 0.1f, rightInnerTopLocal.z * 0.1f));
				uv1.Add(new Vector2(rightOuterBottomLocal.x * 0.1f, rightOuterBottomLocal.z * 0.1f));
				uv1.Add(new Vector2(rightOuterTopLocal.x * 0.1f, rightOuterTopLocal.z * 0.1f));
			}
		}

		// Build quads between slices
		for (int i = 0; i < sliceCount - 1; i++)
		{
			int baseA = sliceBaseIndex[i];
			int baseB = sliceBaseIndex[i + 1];

			// --- PATH BOTTOM QUAD (faces down) ---
			int nearBL = baseA + 0;
			int nearBR = baseA + 1;
			int farBL = baseB + 0;
			int farBR = baseB + 1;
			AddQuad(trisPath, nearBL, nearBR, farBR, farBL);

			// --- PATH TOP QUAD (faces up) ---
			int nearTL = baseA + 2;
			int nearTR = baseA + 3;
			int farTL = baseB + 2;
			int farTR = baseB + 3;
			AddQuad(trisPath, farTR, nearTR, nearTL, farTL);

			if (!path.generateSides)
			{
				int nearLeftInnerB = baseA + 4;
				int nearLeftInnerT = baseA + 5;
				int farLeftInnerB = baseB + 4;
				int farLeftInnerT = baseB + 5;
				AddQuad(trisPath, nearLeftInnerB, farLeftInnerB, farLeftInnerT, nearLeftInnerT);

				int nearRightInnerB = baseA + 6;
				int nearRightInnerT = baseA + 7;
				int farRightInnerB = baseB + 6;
				int farRightInnerT = baseB + 7;
				AddQuad(trisPath, farRightInnerB, nearRightInnerB, nearRightInnerT, farRightInnerT);
			}
			else
			{
				int nearLeftInnerB = baseA + 4;
				int nearLeftInnerT = baseA + 5;
				int nearLeftOuterB = baseA + 6;
				int nearLeftOuterT = baseA + 7;

				int farLeftInnerB = baseB + 4;
				int farLeftInnerT = baseB + 5;
				int farLeftOuterB = baseB + 6;
				int farLeftOuterT = baseB + 7;

				// LEFT SIDE
				AddQuad(trisSides, nearLeftOuterB, farLeftOuterB, farLeftOuterT, nearLeftOuterT);
				AddQuad(trisSides, nearLeftInnerT, nearLeftOuterT, farLeftOuterT, farLeftInnerT);
				AddQuad(trisSides, nearLeftInnerB, nearLeftInnerT, farLeftInnerT, farLeftInnerB);
				AddQuad(trisSides, farLeftInnerB, farLeftOuterB, nearLeftOuterB, nearLeftInnerB);

				int nearRightInnerB = baseA + 8;
				int nearRightInnerT = baseA + 9;
				int nearRightOuterB = baseA + 10;
				int nearRightOuterT = baseA + 11;

				int farRightInnerB = baseB + 8;
				int farRightInnerT = baseB + 9;
				int farRightOuterB = baseB + 10;
				int farRightOuterT = baseB + 11;

				// RIGHT SIDE
				AddQuad(trisSides, farRightOuterB, nearRightOuterB, nearRightOuterT, farRightOuterT);
				AddQuad(trisSides, farRightInnerT, farRightOuterT, nearRightOuterT, nearRightInnerT);
				AddQuad(trisSides, farRightInnerB, farRightInnerT, nearRightInnerT, nearRightInnerB);
				AddQuad(trisSides, nearRightInnerB, nearRightOuterB, farRightOuterB, farRightInnerB);
			}
		}

		// --- END CAPS (path submesh) ---
		if (path.generateEndCaps && !path.loopPathway)
		{
			BezierPathwayEndCapsBuilder.GenerateEndCaps(path, sliceBaseIndex, vertsLocal, normalsLocal, uv0, uv1, trisPath);
			if (path.generateSides)
			{
				BezierPathwayEndCapsBuilder.GenerateSideEndCaps(path, sliceBaseIndex, trisSides);
			}

		}

		// Assign to mesh
		mesh.SetVertices(vertsLocal);
		mesh.SetNormals(normalsLocal);
		mesh.SetUVs(0, uv0);
		mesh.SetUVs(1, uv1);

		mesh.subMeshCount = path.generateSides ? 2 : 1;
		mesh.SetTriangles(trisPath, 0);
		if (path.generateSides) mesh.SetTriangles(trisSides, 1);

		mesh.RecalculateBounds();

		// Assign materials: pathMaterial first, sidingMaterial second (if present)
		if (path.meshRenderer != null)
		{
			if (path.generateSides)
			{
				Material[] mats = new Material[2];
				mats[0] = path.pathMaterial != null ? path.pathMaterial : null;
				mats[1] = path.sidingMaterial != null ? path.sidingMaterial : path.pathMaterial;
				path.meshRenderer.sharedMaterials = mats;
			}
			else
			{
				path.meshRenderer.sharedMaterial = path.pathMaterial;
			}
		}
	}

	// Simple OBJ exporter for the mesh attached to the pathway.
	public static void SaveMeshObj(BezierPathway path, string assetPath)
	{
#if UNITY_EDITOR
		if (path == null || path.meshFilter == null || path.meshFilter.sharedMesh == null) return;

		Mesh mesh = path.meshFilter.sharedMesh;
		Material[] mats = path.meshRenderer != null ? path.meshRenderer.sharedMaterials : null;

		string objPath = assetPath;
		string mtlPath = Path.ChangeExtension(objPath, "mtl");

		StringBuilder sb = new StringBuilder();

		sb.AppendLine("# Exported by BezierPathwayMeshBuilder");
		sb.AppendLine("mtllib " + Path.GetFileName(mtlPath));

		foreach (Vector3 v in mesh.vertices)
			sb.AppendLine(string.Format("v {0} {1} {2}", v.x, v.y, v.z));

		foreach (Vector3 n in mesh.normals)
			sb.AppendLine(string.Format("vn {0} {1} {2}", n.x, n.y, n.z));

		Vector2[] uvs = mesh.uv;
		for (int i = 0; i < uvs.Length; i++)
			sb.AppendLine(string.Format("vt {0} {1}", uvs[i].x, uvs[i].y));

		int subCount = mesh.subMeshCount;
		for (int s = 0; s < subCount; s++)
		{
			string matName = (mats != null && s < mats.Length && mats[s] != null) ? mats[s].name : ("mat" + s);
			sb.AppendLine("usemtl " + matName);
			sb.AppendLine("g " + matName);

			int[] tris = mesh.GetTriangles(s);
			for (int i = 0; i < tris.Length; i += 3)
			{
				int a = tris[i] + 1;
				int b = tris[i + 1] + 1;
				int c = tris[i + 2] + 1;
				sb.AppendLine(string.Format("f {0}/{0}/{0} {1}/{1}/{1} {2}/{2}/{2}", a, b, c));
			}
		}

		File.WriteAllText(objPath, sb.ToString());

		StringBuilder mtl = new StringBuilder();
		for (int s = 0; s < subCount; s++)
		{
			string matName = (mats != null && s < mats.Length && mats[s] != null) ? mats[s].name : ("mat" + s);
			mtl.AppendLine("newmtl " + matName);
			Color col = (mats != null && s < mats.Length && mats[s] != null && mats[s].HasProperty("_Color")) ? mats[s].color : Color.white;
			mtl.AppendLine(string.Format("Kd {0} {1} {2}", col.r, col.g, col.b));
			mtl.AppendLine();
		}
		File.WriteAllText(mtlPath, mtl.ToString());

		AssetDatabase.ImportAsset(objPath);
		AssetDatabase.ImportAsset(mtlPath);
#endif
	}

#if UNITY_EDITOR
	// Creates/updates a child render object with a persisted mesh asset for worlds that cannot execute runtime C#.
	public static bool BakeRenderableChild(BezierPathway path)
	{
		if (path == null) return false;

		BuildMesh(path);

		if (path.meshFilter == null || path.meshFilter.sharedMesh == null)
			return false;

		Mesh sourceMesh = path.meshFilter.sharedMesh;
		if (sourceMesh.vertexCount == 0)
			return false;

		string folder = string.IsNullOrWhiteSpace(path.bakedAssetFolder)
			? "Assets/CozyConTools/Generated/BezierPathway"
			: path.bakedAssetFolder.Trim();

		if (!folder.StartsWith("Assets/"))
			folder = "Assets/CozyConTools/Generated/BezierPathway";

		EnsureFolderExists(folder);

		string safeName = MakeSafeAssetName(path.gameObject.name);
		string desiredPath = string.IsNullOrWhiteSpace(path.bakedMeshAssetPath)
			? AssetDatabase.GenerateUniqueAssetPath(folder + "/" + safeName + "_Baked.asset")
			: path.bakedMeshAssetPath;

		Mesh existingAsset = AssetDatabase.LoadAssetAtPath<Mesh>(desiredPath);
		if (existingAsset == null)
		{
			desiredPath = AssetDatabase.GenerateUniqueAssetPath(folder + "/" + safeName + "_Baked.asset");
		}

		if (AssetDatabase.LoadAssetAtPath<Mesh>(desiredPath) != null)
		{
			AssetDatabase.DeleteAsset(desiredPath);
		}

		Mesh bakedMesh = Object.Instantiate(sourceMesh);
		bakedMesh.name = safeName + "_Baked";
		AssetDatabase.CreateAsset(bakedMesh, desiredPath);
		AssetDatabase.SaveAssets();
		AssetDatabase.Refresh();

		path.bakedMeshAssetPath = desiredPath;

		string childName = string.IsNullOrWhiteSpace(path.bakedChildName) ? "BezierPathway_Baked" : path.bakedChildName.Trim();
		Transform child = path.transform.Find(childName);
		if (child == null)
		{
			GameObject childObj = new GameObject(childName);
			child = childObj.transform;
			child.SetParent(path.transform, false);
			child.localPosition = Vector3.zero;
			child.localRotation = Quaternion.identity;
			child.localScale = Vector3.one;
		}

		MeshFilter childFilter = child.GetComponent<MeshFilter>();
		if (childFilter == null) childFilter = child.gameObject.AddComponent<MeshFilter>();
		MeshRenderer childRenderer = child.GetComponent<MeshRenderer>();
		if (childRenderer == null) childRenderer = child.gameObject.AddComponent<MeshRenderer>();

		Mesh assetMesh = AssetDatabase.LoadAssetAtPath<Mesh>(desiredPath);
		childFilter.sharedMesh = assetMesh;

		if (path.meshRenderer != null)
		{
			if (path.generateSides)
			{
				Material[] mats = new Material[2];
				mats[0] = path.pathMaterial;
				mats[1] = path.sidingMaterial != null ? path.sidingMaterial : path.pathMaterial;
				childRenderer.sharedMaterials = mats;
			}
			else
			{
				childRenderer.sharedMaterial = path.pathMaterial;
			}
		}

		if (path.disableSourceRendererAfterBake && path.meshRenderer != null)
		{
			path.meshRenderer.enabled = false;
		}

		EditorUtility.SetDirty(path);
		EditorUtility.SetDirty(child.gameObject);
		EditorUtility.SetDirty(childFilter);
		EditorUtility.SetDirty(childRenderer);
		return true;
	}

	static void EnsureFolderExists(string folder)
	{
		if (AssetDatabase.IsValidFolder(folder)) return;

		string[] parts = folder.Split('/');
		if (parts.Length < 2) return;

		string current = parts[0];
		for (int i = 1; i < parts.Length; i++)
		{
			string next = current + "/" + parts[i];
			if (!AssetDatabase.IsValidFolder(next))
			{
				AssetDatabase.CreateFolder(current, parts[i]);
			}
			current = next;
		}
	}

	static string MakeSafeAssetName(string name)
	{
		if (string.IsNullOrWhiteSpace(name)) return "BezierPathway";

		char[] chars = name.ToCharArray();
		for (int i = 0; i < chars.Length; i++)
		{
			char c = chars[i];
			if (!(char.IsLetterOrDigit(c) || c == '_' || c == '-'))
			{
				chars[i] = '_';
			}
		}

		return new string(chars);
	}
#endif
}
