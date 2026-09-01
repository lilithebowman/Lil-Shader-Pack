using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

public class ExportSelectedAsWavefrontObjModelWindow : EditorWindow
{
	private struct MeshExportSource
	{
		public Mesh mesh;
		public Matrix4x4 localToWorld;
		public string objectName;
		public Material[] materials;
		public bool isTemporary;
	}

	private struct MaterialExportInfo
	{
		public string exportName;
		public Color diffuseColor;
		public string textureFileName;
	}

	private bool convertToBlenderAxes = true;
	private bool includeInactiveChildren = true;
	private bool includeTexturesInExportFolder = true;
	private Vector2 scroll;

	[MenuItem("Lilithe/Export Selected as Wavefront OBJ Model")]
	public static void ShowWindow()
	{
		GetWindow<ExportSelectedAsWavefrontObjModelWindow>("Wavefront OBJ Export");
	}

	private void OnGUI()
	{
		scroll = EditorGUILayout.BeginScrollView(scroll);
		EditorGUILayout.LabelField("Export Selected as Wavefront OBJ Model", EditorStyles.boldLabel);
		EditorGUILayout.HelpBox("Exports selected hierarchy objects as a Wavefront OBJ + MTL pair for Blender and other DCC tools.", MessageType.Info);

		EditorGUILayout.Space();
		EditorGUILayout.LabelField("Selection", EditorStyles.boldLabel);
		GameObject[] selected = Selection.gameObjects;
		EditorGUILayout.LabelField("Selected Root Objects", selected != null ? selected.Length.ToString() : "0");
		if (selected != null && selected.Length > 0)
		{
			for (int i = 0; i < selected.Length; i++)
			{
				if (selected[i] != null)
				{
					EditorGUILayout.LabelField("- " + selected[i].name);
				}
			}
		}

		EditorGUILayout.Space();
		EditorGUILayout.LabelField("Export Options", EditorStyles.boldLabel);
		includeInactiveChildren = EditorGUILayout.Toggle("Include Inactive Children", includeInactiveChildren);
		convertToBlenderAxes = EditorGUILayout.Toggle("Convert To Blender Axes", convertToBlenderAxes);
		includeTexturesInExportFolder = EditorGUILayout.Toggle("Copy Albedo Textures", includeTexturesInExportFolder);

		EditorGUILayout.Space();
		GUI.enabled = selected != null && selected.Length > 0;
		if (GUILayout.Button("Export"))
		{
			ExportSelectionToObjMtl();
		}
		GUI.enabled = true;

		EditorGUILayout.EndScrollView();
	}

	private void ExportSelectionToObjMtl()
	{
		List<MeshExportSource> sources = CollectMeshSources(Selection.gameObjects, includeInactiveChildren);
		if (sources.Count == 0)
		{
			EditorUtility.DisplayDialog("Wavefront OBJ Export", "No valid MeshFilter/MeshRenderer or SkinnedMeshRenderer data found in the selected objects.", "OK");
			return;
		}

		string objPath = EditorUtility.SaveFilePanel("Export Wavefront OBJ", Application.dataPath, "ExportedSelection", "obj");
		if (string.IsNullOrWhiteSpace(objPath))
		{
			CleanupTemporaryMeshes(sources);
			return;
		}

		if (!objPath.EndsWith(".obj", StringComparison.OrdinalIgnoreCase))
		{
			objPath += ".obj";
		}

		try
		{
			WriteObjAndMtl(objPath, sources, convertToBlenderAxes, includeTexturesInExportFolder);
			EditorUtility.DisplayDialog("Wavefront OBJ Export", "Export complete. OBJ and MTL files were written successfully.", "OK");
			EditorUtility.RevealInFinder(objPath);
		}
		catch (Exception ex)
		{
			Debug.LogError("Wavefront OBJ export failed: " + ex);
			EditorUtility.DisplayDialog("Wavefront OBJ Export", "Export failed. Check the Console for details.\n\n" + ex.Message, "OK");
		}
		finally
		{
			CleanupTemporaryMeshes(sources);
		}
	}

	private static List<MeshExportSource> CollectMeshSources(GameObject[] selectedRoots, bool includeInactive)
	{
		List<MeshExportSource> results = new List<MeshExportSource>();
		HashSet<Component> visited = new HashSet<Component>();

		if (selectedRoots == null)
		{
			return results;
		}

		for (int i = 0; i < selectedRoots.Length; i++)
		{
			GameObject root = selectedRoots[i];
			if (root == null)
			{
				continue;
			}

			MeshFilter[] filters = root.GetComponentsInChildren<MeshFilter>(includeInactive);
			for (int f = 0; f < filters.Length; f++)
			{
				MeshFilter filter = filters[f];
				if (filter == null || visited.Contains(filter) || filter.sharedMesh == null)
				{
					continue;
				}

				MeshRenderer renderer = filter.GetComponent<MeshRenderer>();
				if (renderer == null)
				{
					continue;
				}

				visited.Add(filter);
				results.Add(new MeshExportSource
				{
					mesh = filter.sharedMesh,
					localToWorld = filter.transform.localToWorldMatrix,
					objectName = filter.name,
					materials = renderer.sharedMaterials,
					isTemporary = false
				});
			}

			SkinnedMeshRenderer[] skinnedRenderers = root.GetComponentsInChildren<SkinnedMeshRenderer>(includeInactive);
			for (int s = 0; s < skinnedRenderers.Length; s++)
			{
				SkinnedMeshRenderer skinned = skinnedRenderers[s];
				if (skinned == null || visited.Contains(skinned) || skinned.sharedMesh == null)
				{
					continue;
				}

				Mesh baked = new Mesh();
				skinned.BakeMesh(baked);
				baked.name = skinned.name + "_Baked";

				visited.Add(skinned);
				results.Add(new MeshExportSource
				{
					mesh = baked,
					localToWorld = skinned.transform.localToWorldMatrix,
					objectName = skinned.name,
					materials = skinned.sharedMaterials,
					isTemporary = true
				});
			}
		}

		return results;
	}

	private static void WriteObjAndMtl(string objPath, List<MeshExportSource> sources, bool blenderAxes, bool copyTextures)
	{
		string exportDirectory = Path.GetDirectoryName(objPath);
		if (string.IsNullOrWhiteSpace(exportDirectory))
		{
			throw new InvalidOperationException("Export directory is invalid.");
		}

		Directory.CreateDirectory(exportDirectory);

		string objFileName = Path.GetFileNameWithoutExtension(objPath);
		string mtlFileName = objFileName + ".mtl";
		string mtlPath = Path.Combine(exportDirectory, mtlFileName);

		Dictionary<Material, MaterialExportInfo> materialMap = BuildMaterialMap(sources, exportDirectory, copyTextures);

		StringBuilder obj = new StringBuilder(1024 * 1024);
		obj.AppendLine("# Wavefront OBJ exported from Unity");
		obj.AppendLine("# Tool: Export Selected as Waveform OBJ Model");
		obj.AppendLine("mtllib " + mtlFileName);

		int vertexOffset = 0;
		int uvOffset = 0;
		int normalOffset = 0;

		for (int sourceIndex = 0; sourceIndex < sources.Count; sourceIndex++)
		{
			MeshExportSource source = sources[sourceIndex];
			Mesh mesh = source.mesh;
			if (mesh == null)
			{
				continue;
			}

			Vector3[] vertices = mesh.vertices;
			if (vertices == null || vertices.Length == 0)
			{
				continue;
			}

			Vector2[] uv = mesh.uv;
			bool hasUv = uv != null && uv.Length == vertices.Length;

			Vector3[] normals = mesh.normals;
			if (normals == null || normals.Length != vertices.Length)
			{
				mesh.RecalculateNormals();
				normals = mesh.normals;
			}

			obj.AppendLine();
			obj.AppendLine("o " + SanitizeName(source.objectName));

			for (int i = 0; i < vertices.Length; i++)
			{
				Vector3 worldVertex = source.localToWorld.MultiplyPoint3x4(vertices[i]);
				if (blenderAxes)
				{
					worldVertex = ConvertUnityToBlenderAxes(worldVertex);
				}
				obj.AppendLine("v " + F(worldVertex.x) + " " + F(worldVertex.y) + " " + F(worldVertex.z));
			}

			if (hasUv)
			{
				for (int i = 0; i < uv.Length; i++)
				{
					obj.AppendLine("vt " + F(uv[i].x) + " " + F(uv[i].y));
				}
			}

			Matrix4x4 normalMatrix = source.localToWorld.inverse.transpose;
			for (int i = 0; i < normals.Length; i++)
			{
				Vector3 worldNormal = normalMatrix.MultiplyVector(normals[i]).normalized;
				if (blenderAxes)
				{
					worldNormal = ConvertUnityToBlenderAxes(worldNormal).normalized;
				}
				obj.AppendLine("vn " + F(worldNormal.x) + " " + F(worldNormal.y) + " " + F(worldNormal.z));
			}

			for (int subMesh = 0; subMesh < mesh.subMeshCount; subMesh++)
			{
				Material material = null;
				if (source.materials != null && subMesh < source.materials.Length)
				{
					material = source.materials[subMesh];
				}

				if (material != null && materialMap.ContainsKey(material))
				{
					obj.AppendLine("usemtl " + materialMap[material].exportName);
				}
				else
				{
					obj.AppendLine("usemtl Default_Material");
				}

				int[] triangles = mesh.GetTriangles(subMesh);
				for (int t = 0; t < triangles.Length; t += 3)
				{
					int a = triangles[t] + 1;
					int b = triangles[t + 1] + 1;
					int c = triangles[t + 2] + 1;

					if (blenderAxes)
					{
						int swap = b;
						b = c;
						c = swap;
					}

					if (hasUv)
					{
						obj.AppendLine(
							"f " +
							(a + vertexOffset) + "/" + (a + uvOffset) + "/" + (a + normalOffset) + " " +
							(b + vertexOffset) + "/" + (b + uvOffset) + "/" + (b + normalOffset) + " " +
							(c + vertexOffset) + "/" + (c + uvOffset) + "/" + (c + normalOffset));
					}
					else
					{
						obj.AppendLine(
							"f " +
							(a + vertexOffset) + "//" + (a + normalOffset) + " " +
							(b + vertexOffset) + "//" + (b + normalOffset) + " " +
							(c + vertexOffset) + "//" + (c + normalOffset));
					}
				}
			}

			vertexOffset += vertices.Length;
			normalOffset += normals.Length;
			if (hasUv)
			{
				uvOffset += uv.Length;
			}
		}

		File.WriteAllText(objPath, obj.ToString(), Encoding.UTF8);

		StringBuilder mtl = new StringBuilder(32 * 1024);
		mtl.AppendLine("# Wavefront MTL exported from Unity");
		mtl.AppendLine();
		mtl.AppendLine("newmtl Default_Material");
		mtl.AppendLine("Ka 0.200000 0.200000 0.200000");
		mtl.AppendLine("Kd 0.800000 0.800000 0.800000");
		mtl.AppendLine("Ks 0.000000 0.000000 0.000000");
		mtl.AppendLine("d 1.000000");
		mtl.AppendLine("illum 2");
		mtl.AppendLine();

		foreach (KeyValuePair<Material, MaterialExportInfo> pair in materialMap)
		{
			MaterialExportInfo info = pair.Value;
			mtl.AppendLine("newmtl " + info.exportName);
			mtl.AppendLine("Ka 0.200000 0.200000 0.200000");
			mtl.AppendLine("Kd " + F(info.diffuseColor.r) + " " + F(info.diffuseColor.g) + " " + F(info.diffuseColor.b));
			mtl.AppendLine("Ks 0.000000 0.000000 0.000000");
			mtl.AppendLine("d " + F(info.diffuseColor.a));
			mtl.AppendLine("illum 2");
			if (!string.IsNullOrWhiteSpace(info.textureFileName))
			{
				mtl.AppendLine("map_Kd " + info.textureFileName);
			}
			mtl.AppendLine();
		}

		File.WriteAllText(mtlPath, mtl.ToString(), Encoding.UTF8);
	}

	private static Dictionary<Material, MaterialExportInfo> BuildMaterialMap(List<MeshExportSource> sources, string exportDirectory, bool copyTextures)
	{
		Dictionary<Material, MaterialExportInfo> materialMap = new Dictionary<Material, MaterialExportInfo>();
		HashSet<string> usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

		for (int i = 0; i < sources.Count; i++)
		{
			Material[] mats = sources[i].materials;
			if (mats == null)
			{
				continue;
			}

			for (int m = 0; m < mats.Length; m++)
			{
				Material mat = mats[m];
				if (mat == null || materialMap.ContainsKey(mat))
				{
					continue;
				}

				string baseName = SanitizeName(mat.name);
				string uniqueName = MakeUniqueName(baseName, usedNames);
				Color diffuse = mat.HasProperty("_Color") ? mat.color : Color.white;
				string copiedTextureName = null;

				if (copyTextures)
				{
					Texture tex = null;
					if (mat.HasProperty("_MainTex"))
					{
						tex = mat.mainTexture;
					}
					if (tex != null)
					{
						copiedTextureName = CopyTextureAssetToExportFolder(tex, exportDirectory);
					}
				}

				materialMap[mat] = new MaterialExportInfo
				{
					exportName = uniqueName,
					diffuseColor = diffuse,
					textureFileName = copiedTextureName
				};
			}
		}

		return materialMap;
	}

	private static string CopyTextureAssetToExportFolder(Texture texture, string exportDirectory)
	{
		string assetPath = AssetDatabase.GetAssetPath(texture);
		if (string.IsNullOrWhiteSpace(assetPath))
		{
			return null;
		}

		string sourcePath = Path.GetFullPath(assetPath);
		if (!File.Exists(sourcePath))
		{
			return null;
		}

		string textureFileName = Path.GetFileName(sourcePath);
		string destinationPath = Path.Combine(exportDirectory, textureFileName);

		if (!File.Exists(destinationPath))
		{
			File.Copy(sourcePath, destinationPath, false);
		}

		return textureFileName;
	}

	private static void CleanupTemporaryMeshes(List<MeshExportSource> sources)
	{
		for (int i = 0; i < sources.Count; i++)
		{
			if (sources[i].isTemporary && sources[i].mesh != null)
			{
				DestroyImmediate(sources[i].mesh);
			}
		}
	}

	private static Vector3 ConvertUnityToBlenderAxes(Vector3 value)
	{
		// Preserve Y-up and flip handedness for Blender's default OBJ import orientation.
		return new Vector3(value.x, value.y, -value.z);
	}

	private static string SanitizeName(string raw)
	{
		if (string.IsNullOrWhiteSpace(raw))
		{
			return "Unnamed";
		}

		char[] chars = raw.ToCharArray();
		for (int i = 0; i < chars.Length; i++)
		{
			char c = chars[i];
			if (!(char.IsLetterOrDigit(c) || c == '_' || c == '-'))
			{
				chars[i] = '_';
			}
		}

		string sanitized = new string(chars).Trim('_');
		return string.IsNullOrWhiteSpace(sanitized) ? "Unnamed" : sanitized;
	}

	private static string MakeUniqueName(string baseName, HashSet<string> usedNames)
	{
		string candidate = string.IsNullOrWhiteSpace(baseName) ? "Material" : baseName;
		if (!usedNames.Contains(candidate))
		{
			usedNames.Add(candidate);
			return candidate;
		}

		int index = 1;
		while (true)
		{
			string numbered = candidate + "_" + index;
			if (!usedNames.Contains(numbered))
			{
				usedNames.Add(numbered);
				return numbered;
			}
			index++;
		}
	}

	private static string F(float value)
	{
		return value.ToString("0.######", CultureInfo.InvariantCulture);
	}
}
