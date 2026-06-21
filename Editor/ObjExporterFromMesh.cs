using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

public static class ObjExporterFromMesh
{
	private const string MenuPath = "Tools/Meshes/Export Selected Meshes To OBJ";
	private const string BlenderAxisToggleKey = "ObjExporterFromMesh.ConvertToBlenderAxes";

	[MenuItem(MenuPath)]
	private static void ExportSelectedMeshesToObj()
	{
		var convertToBlenderAxes = EditorUtility.DisplayDialog(
			"OBJ Axis Conversion",
			"Convert exported coordinates to Blender-friendly axes?\n\nYes: Y-up / Z-forward style conversion\nNo: keep Unity world axes",
			"Yes (Blender Friendly)",
			"No (Keep Unity Axes)");

		EditorPrefs.SetBool(BlenderAxisToggleKey, convertToBlenderAxes);

		var meshSources = CollectSelectedMeshSources();
		if (meshSources.Count == 0)
		{
			EditorUtility.DisplayDialog("Export To OBJ", "Select one or more GameObjects containing MeshFilter or SkinnedMeshRenderer components.", "OK");
			return;
		}

		var objPath = EditorUtility.SaveFilePanel("Export OBJ File", Application.dataPath, "ExportedMesh", "obj");
		if (string.IsNullOrWhiteSpace(objPath))
		{
			return;
		}

		if (!objPath.EndsWith(".obj", StringComparison.OrdinalIgnoreCase))
		{
			objPath += ".obj";
		}

		ExportObj(objPath, meshSources, convertToBlenderAxes);
		Debug.Log("[ObjExporterFromMesh] Exported OBJ: " + objPath);
		EditorUtility.RevealInFinder(objPath);
		EditorUtility.DisplayDialog("Export To OBJ", "OBJ file exported successfully:\n" + objPath, "OK");
	}

	[MenuItem(MenuPath, true)]
	private static bool ValidateExportSelectedMeshesToObj()
	{
		return Selection.gameObjects != null && Selection.gameObjects.Length > 0;
	}

	private static List<MeshSource> CollectSelectedMeshSources()
	{
		var results = new List<MeshSource>();
		var visited = new HashSet<Component>();

		foreach (var root in Selection.gameObjects)
		{
			if (root == null)
			{
				continue;
			}

			foreach (var filter in root.GetComponentsInChildren<MeshFilter>(true))
			{
				if (filter == null || visited.Contains(filter))
				{
					continue;
				}

				var renderer = filter.GetComponent<MeshRenderer>();
				if (renderer == null || filter.sharedMesh == null)
				{
					continue;
				}

				visited.Add(filter);
				results.Add(new MeshSource(filter.sharedMesh, filter.transform.localToWorldMatrix, filter.name));
			}

			foreach (var skinned in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
			{
				if (skinned == null || visited.Contains(skinned) || skinned.sharedMesh == null)
				{
					continue;
				}

				visited.Add(skinned);
				var baked = new Mesh();
				skinned.BakeMesh(baked);
				baked.name = skinned.name + "_Baked";
				results.Add(new MeshSource(baked, skinned.transform.localToWorldMatrix, skinned.name, true));
			}
		}

		return results;
	}

	private static void ExportObj(string path, List<MeshSource> meshSources, bool convertToBlenderAxes)
	{
		var directory = Path.GetDirectoryName(path);
		if (!string.IsNullOrWhiteSpace(directory) && !Directory.Exists(directory))
		{
			Directory.CreateDirectory(directory);
		}

		var sb = new StringBuilder(1024 * 1024);
		sb.AppendLine("# Exported from Unity");
		sb.AppendLine("# Axis mode: " + (convertToBlenderAxes ? "Blender-friendly" : "Unity-world"));

		var vertexOffset = 0;
		var uvOffset = 0;
		var normalOffset = 0;

		for (var i = 0; i < meshSources.Count; i++)
		{
			var source = meshSources[i];
			var mesh = source.Mesh;
			if (mesh == null)
			{
				continue;
			}

			var vertices = mesh.vertices;
			if (vertices == null || vertices.Length == 0)
			{
				continue;
			}

			var normals = mesh.normals;
			if (normals == null || normals.Length != vertices.Length)
			{
				mesh.RecalculateNormals();
				normals = mesh.normals;
			}

			var uvs = mesh.uv;
			var hasUv = uvs != null && uvs.Length == vertices.Length;

			sb.AppendLine($"o {SanitizeName(source.Name)}");

			for (var v = 0; v < vertices.Length; v++)
			{
				var worldVertex = source.LocalToWorld.MultiplyPoint3x4(vertices[v]);
				if (convertToBlenderAxes)
				{
					worldVertex = ConvertUnityToBlenderAxes(worldVertex);
				}

				sb.AppendLine($"v {F(worldVertex.x)} {F(worldVertex.y)} {F(worldVertex.z)}");
			}

			if (hasUv)
			{
				for (var uvIndex = 0; uvIndex < uvs.Length; uvIndex++)
				{
					var uv = uvs[uvIndex];
					sb.AppendLine($"vt {F(uv.x)} {F(uv.y)}");
				}
			}

			var normalMatrix = source.LocalToWorld.inverse.transpose;
			for (var n = 0; n < normals.Length; n++)
			{
				var worldNormal = normalMatrix.MultiplyVector(normals[n]).normalized;
				if (convertToBlenderAxes)
				{
					worldNormal = ConvertUnityToBlenderAxes(worldNormal).normalized;
				}

				sb.AppendLine($"vn {F(worldNormal.x)} {F(worldNormal.y)} {F(worldNormal.z)}");
			}

			for (var subMesh = 0; subMesh < mesh.subMeshCount; subMesh++)
			{
				var triangles = mesh.GetTriangles(subMesh);
				for (var t = 0; t < triangles.Length; t += 3)
				{
					var i0 = triangles[t] + 1;
					var i1 = triangles[t + 1] + 1;
					var i2 = triangles[t + 2] + 1;

					if (hasUv)
					{
						sb.AppendLine($"f {i0 + vertexOffset}/{i0 + uvOffset}/{i0 + normalOffset} {i1 + vertexOffset}/{i1 + uvOffset}/{i1 + normalOffset} {i2 + vertexOffset}/{i2 + uvOffset}/{i2 + normalOffset}");
					}
					else
					{
						sb.AppendLine($"f {i0 + vertexOffset}//{i0 + normalOffset} {i1 + vertexOffset}//{i1 + normalOffset} {i2 + vertexOffset}//{i2 + normalOffset}");
					}
				}
			}

			vertexOffset += vertices.Length;
			normalOffset += normals.Length;
			if (hasUv)
			{
				uvOffset += uvs.Length;
			}
		}

		File.WriteAllText(path, sb.ToString(), Encoding.UTF8);

		foreach (var source in meshSources)
		{
			if (source.IsTemporary && source.Mesh != null)
			{
				UnityEngine.Object.DestroyImmediate(source.Mesh);
			}
		}
	}

	private static Vector3 ConvertUnityToBlenderAxes(Vector3 vector)
	{
		// Keep Y as up and flip forward/handedness so Blender OBJ default import does not tilt the model.
		return new Vector3(vector.x, vector.y, -vector.z);
	}

	private static string SanitizeName(string name)
	{
		if (string.IsNullOrWhiteSpace(name))
		{
			return "MeshObject";
		}

		var cleaned = new string(name.Select(ch => char.IsLetterOrDigit(ch) || ch == '_' ? ch : '_').ToArray());
		return string.IsNullOrWhiteSpace(cleaned) ? "MeshObject" : cleaned;
	}

	private static string F(float value)
	{
		return value.ToString("0.######", CultureInfo.InvariantCulture);
	}

	private readonly struct MeshSource
	{
		public MeshSource(Mesh mesh, Matrix4x4 localToWorld, string name, bool isTemporary = false)
		{
			Mesh = mesh;
			LocalToWorld = localToWorld;
			Name = name;
			IsTemporary = isTemporary;
		}

		public Mesh Mesh { get; }
		public Matrix4x4 LocalToWorld { get; }
		public string Name { get; }
		public bool IsTemporary { get; }
	}
}