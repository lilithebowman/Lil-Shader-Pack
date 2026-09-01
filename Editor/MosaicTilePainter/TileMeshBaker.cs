using System.IO;
using System.Text;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

public static class TileMeshBaker
{
	[MenuItem("Lilithe/Mosaic/Bake Tiles + Grout To OBJ + Scene Mesh")]
	public static void BakeCurrentTilesToObj()
	{
		TilePlacer placer = Object.FindObjectOfType<TilePlacer>();
		if (placer == null || placer.settings == null)
		{
			Debug.LogWarning("TileMeshBaker: No TilePlacer or settings found in scene.");
			return;
		}

		string folder = EditorUtility.SaveFolderPanel("Choose export folder", "Assets", "");
		if (string.IsNullOrEmpty(folder))
			return;

		string objPath = Path.Combine(folder, "Mosaic.obj");
		string mtlPath = Path.Combine(folder, "Mosaic.mtl");

		BakeSceneTilesAndGroutToObj(placer, objPath, mtlPath);
		Debug.Log($"TileMeshBaker: Exported OBJ to {objPath} and MTL to {mtlPath}");
	}

	public static void BakeSceneTilesAndGroutToObj(TilePlacer placer, string objPath, string mtlPath)
	{
		// Combined geometry
		List<Vector3> vertices = new List<Vector3>();
		List<Vector3> normals = new List<Vector3>();
		List<Vector2> uvs = new List<Vector2>();

		// Separate face lists
		List<int> tileFaces = new List<int>();   // MosaicTile + PrismTile
		List<int> groutFaces = new List<int>();   // Grout + BorderGrout

		int vertexOffset = 0;

		// -----------------------------
		// 1. Bake children of TilePlacer
		// -----------------------------
		foreach (Transform child in placer.transform)
		{
			string name = child.gameObject.name;

			bool isTileLike =
				name == "MosaicTile" ||
				name == "PrismTile";

			bool isGroutLike =
				name == "Grout" ||
				name == "BorderGrout";

			if (!isTileLike && !isGroutLike)
				continue;

			MeshFilter mf = child.GetComponent<MeshFilter>();
			Mesh mesh = mf != null ? mf.sharedMesh : null;
			if (mesh == null)
				continue;

			Matrix4x4 m = child.localToWorldMatrix;

			foreach (Vector3 v in mesh.vertices)
				vertices.Add(m.MultiplyPoint3x4(v));

			foreach (Vector3 n in mesh.normals)
				normals.Add(m.MultiplyVector(n).normalized);

			if (mesh.uv != null && mesh.uv.Length == mesh.vertexCount)
				uvs.AddRange(mesh.uv);
			else
				for (int i = 0; i < mesh.vertexCount; i++)
					uvs.Add(Vector2.zero);

			if (isTileLike)
			{
				foreach (int idx in mesh.triangles)
					tileFaces.Add(vertexOffset + idx);
			}
			else if (isGroutLike)
			{
				foreach (int idx in mesh.triangles)
					groutFaces.Add(vertexOffset + idx);
			}

			vertexOffset += mesh.vertexCount;
		}

		// -----------------------------
		// 2. Create baked mesh in scene
		// -----------------------------
		Mesh bakedMesh = new Mesh();
		bakedMesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32; // allow >65k verts

		bakedMesh.SetVertices(vertices);
		bakedMesh.SetNormals(normals);
		bakedMesh.SetUVs(0, uvs);

		// Combine tile + grout faces into one triangle array
		List<int> allFaces = new List<int>();
		allFaces.AddRange(tileFaces);
		allFaces.AddRange(groutFaces);

		bakedMesh.SetTriangles(allFaces, 0);
		bakedMesh.RecalculateBounds();

		// Create GameObject under TilePlacer
		GameObject bakedGO = new GameObject("BakedMosaicMesh");
		bakedGO.transform.SetParent(placer.transform);
		bakedGO.transform.localPosition = Vector3.zero;
		bakedGO.transform.localRotation = Quaternion.identity;

		MeshFilter bakedMF = bakedGO.AddComponent<MeshFilter>();
		bakedMF.sharedMesh = bakedMesh;

		MeshRenderer bakedMR = bakedGO.AddComponent<MeshRenderer>();
		bakedMR.sharedMaterials = new Material[]
		{
			placer.settings.overrideTileMaterial != null ? placer.settings.overrideTileMaterial : placer.settings.defaultTileMaterial,
			placer.settings.overrideGroutMaterial != null ? placer.settings.overrideGroutMaterial : placer.settings.groutMaterial
		};

		// -----------------------------
		// 3. Write MTL + OBJ
		// -----------------------------
		WriteMtl(mtlPath);
		WriteObj(vertices, normals, uvs, tileFaces, groutFaces, objPath, Path.GetFileName(mtlPath));
	}

	private static void WriteMtl(string mtlPath)
	{
		StringBuilder sb = new StringBuilder();

		sb.AppendLine("newmtl TileMaterial");
		sb.AppendLine("Kd 1.0 1.0 1.0");

		sb.AppendLine("newmtl GroutMaterial");
		sb.AppendLine("Kd 0.8 0.8 0.8");

		File.WriteAllText(mtlPath, sb.ToString());
	}

	private static void WriteObj(
		List<Vector3> vertices,
		List<Vector3> normals,
		List<Vector2> uvs,
		List<int> tileFaces,
		List<int> groutFaces,
		string objPath,
		string mtlFileName)
	{
		StringBuilder sb = new StringBuilder();

		sb.AppendLine("# Mosaic OBJ");
		sb.AppendLine($"mtllib {mtlFileName}");

		// Vertices
		foreach (Vector3 v in vertices)
			sb.AppendLine($"v {v.x} {v.y} {v.z}");

		// UVs
		foreach (Vector2 uv in uvs)
			sb.AppendLine($"vt {uv.x} {uv.y}");

		// Normals
		foreach (Vector3 n in normals)
			sb.AppendLine($"vn {n.x} {n.y} {n.z}");

		// Tile faces (MosaicTile + PrismTile)
		sb.AppendLine("usemtl TileMaterial");
		for (int i = 0; i < tileFaces.Count; i += 3)
		{
			int i0 = tileFaces[i] + 1;
			int i1 = tileFaces[i + 1] + 1;
			int i2 = tileFaces[i + 2] + 1;

			sb.AppendLine($"f {i0}/{i0}/{i0} {i1}/{i1}/{i1} {i2}/{i2}/{i2}");
		}

		// Grout faces (Grout + BorderGrout)
		sb.AppendLine("usemtl GroutMaterial");
		for (int i = 0; i < groutFaces.Count; i += 3)
		{
			int i0 = groutFaces[i] + 1;
			int i1 = groutFaces[i + 1] + 1;
			int i2 = groutFaces[i + 2] + 1;

			sb.AppendLine($"f {i0}/{i0}/{i0} {i1}/{i1}/{i1} {i2}/{i2}/{i2}");
		}

		File.WriteAllText(objPath, sb.ToString());
	}
}
