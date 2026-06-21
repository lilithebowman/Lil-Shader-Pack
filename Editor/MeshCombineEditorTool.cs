using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

public static class MeshCombineEditorTool
{
	private const string MenuPath = "Tools/Lilithe Booth/Combine Selected Meshes";

	[MenuItem(MenuPath)]
	private static void CombineSelectedMeshes()
	{
		var selectedRoots = Selection.gameObjects;
		if (selectedRoots == null || selectedRoots.Length == 0)
		{
			EditorUtility.DisplayDialog("Combine Meshes", "Select one or more GameObjects to combine.", "OK");
			return;
		}

		var meshRenderers = GetMeshRenderers(selectedRoots);
		if (meshRenderers.Count == 0)
		{
			EditorUtility.DisplayDialog("Combine Meshes", "No valid MeshRenderer + MeshFilter pairs were found in the selection.", "OK");
			return;
		}

		var savePath = EditorUtility.SaveFilePanelInProject(
			"Save Combined Mesh",
			"CombinedMesh",
			"asset",
			"Choose where to save the combined mesh asset.");

		if (string.IsNullOrWhiteSpace(savePath))
		{
			return;
		}

		var result = BuildCombinedMesh(meshRenderers);
		if (result.Mesh == null || result.Materials.Count == 0)
		{
			EditorUtility.DisplayDialog("Combine Meshes", "Failed to build a combined mesh.", "OK");
			return;
		}

		result.Mesh.name = "CombinedMesh";
		AssetDatabase.CreateAsset(result.Mesh, savePath);
		AssetDatabase.SaveAssets();

		var output = new GameObject("Combined Mesh");
		Undo.RegisterCreatedObjectUndo(output, "Create Combined Mesh Object");

		var filter = output.AddComponent<MeshFilter>();
		var renderer = output.AddComponent<MeshRenderer>();

		filter.sharedMesh = result.Mesh;
		renderer.sharedMaterials = result.Materials.ToArray();

		Selection.activeGameObject = output;
		EditorGUIUtility.PingObject(output);

		EditorUtility.DisplayDialog(
			"Combine Meshes",
			"Combined mesh created.\n\nTip: You get the biggest SetPass reduction when source meshes share fewer materials.",
			"OK");
	}

	[MenuItem(MenuPath, true)]
	private static bool ValidateCombineSelectedMeshes()
	{
		return Selection.gameObjects != null && Selection.gameObjects.Length > 0;
	}

	private static List<MeshRenderer> GetMeshRenderers(GameObject[] roots)
	{
		var renderers = new List<MeshRenderer>();
		var seen = new HashSet<MeshRenderer>();

		foreach (var root in roots)
		{
			if (root == null)
			{
				continue;
			}

			var nestedRenderers = root.GetComponentsInChildren<MeshRenderer>(true);
			foreach (var meshRenderer in nestedRenderers)
			{
				if (meshRenderer == null || seen.Contains(meshRenderer))
				{
					continue;
				}

				var filter = meshRenderer.GetComponent<MeshFilter>();
				if (filter == null || filter.sharedMesh == null)
				{
					continue;
				}

				seen.Add(meshRenderer);
				renderers.Add(meshRenderer);
			}
		}

		return renderers;
	}

	private static CombinedBuildResult BuildCombinedMesh(List<MeshRenderer> meshRenderers)
	{
		var perMaterialCombines = new Dictionary<Material, List<CombineInstance>>();
		var materialOrder = new List<Material>();

		foreach (var meshRenderer in meshRenderers)
		{
			var filter = meshRenderer.GetComponent<MeshFilter>();
			if (filter == null || filter.sharedMesh == null)
			{
				continue;
			}

			var sourceMesh = filter.sharedMesh;
			var sourceMaterials = meshRenderer.sharedMaterials;
			var subMeshCount = sourceMesh.subMeshCount;

			for (var subMeshIndex = 0; subMeshIndex < subMeshCount; subMeshIndex++)
			{
				var material = ResolveMaterial(sourceMaterials, subMeshIndex);
				if (material == null)
				{
					continue;
				}

				if (!perMaterialCombines.TryGetValue(material, out var combineList))
				{
					combineList = new List<CombineInstance>();
					perMaterialCombines.Add(material, combineList);
					materialOrder.Add(material);
				}

				combineList.Add(new CombineInstance
				{
					mesh = sourceMesh,
					subMeshIndex = subMeshIndex,
					transform = meshRenderer.localToWorldMatrix
				});
			}
		}

		if (materialOrder.Count == 0)
		{
			return default;
		}

		var temporaryMeshes = new List<Mesh>(materialOrder.Count);
		var finalCombine = new List<CombineInstance>(materialOrder.Count);

		foreach (var material in materialOrder)
		{
			var instances = perMaterialCombines[material];
			if (instances.Count == 0)
			{
				continue;
			}

			var perMaterialMesh = new Mesh
			{
				indexFormat = IndexFormat.UInt32,
				name = $"Combined_{material.name}"
			};

			perMaterialMesh.CombineMeshes(instances.ToArray(), true, true, false);
			temporaryMeshes.Add(perMaterialMesh);

			finalCombine.Add(new CombineInstance
			{
				mesh = perMaterialMesh,
				subMeshIndex = 0,
				transform = Matrix4x4.identity
			});
		}

		var finalMesh = new Mesh
		{
			indexFormat = IndexFormat.UInt32,
			name = "CombinedMesh"
		};

		finalMesh.CombineMeshes(finalCombine.ToArray(), false, false, false);

		foreach (var temp in temporaryMeshes)
		{
			if (temp != null)
			{
				UnityEngine.Object.DestroyImmediate(temp);
			}
		}

		finalMesh.RecalculateBounds();

		return new CombinedBuildResult
		{
			Mesh = finalMesh,
			Materials = materialOrder
		};
	}

	private static Material ResolveMaterial(Material[] sourceMaterials, int subMeshIndex)
	{
		if (sourceMaterials == null || sourceMaterials.Length == 0)
		{
			return null;
		}

		var index = Mathf.Clamp(subMeshIndex, 0, sourceMaterials.Length - 1);
		return sourceMaterials[index];
	}

	private struct CombinedBuildResult
	{
		public Mesh Mesh;
		public List<Material> Materials;
	}
}
