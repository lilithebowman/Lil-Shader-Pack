using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class DuplicateMaterialCleaner
{
	private const string MenuPath = "Tools/Materials/Remove Identical Materials In Selected Folder";

	[MenuItem(MenuPath)]
	private static void RemoveIdenticalMaterialsInSelectedFolder()
	{
		var folderPath = GetSelectedFolderPath();
		if (string.IsNullOrWhiteSpace(folderPath))
		{
			EditorUtility.DisplayDialog("Remove Identical Materials", "Select a folder in the Project window first.", "OK");
			return;
		}

		var materialPaths = AssetDatabase.FindAssets("t:Material", new[] { folderPath })
			.Select(AssetDatabase.GUIDToAssetPath)
			.OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
			.ToList();

		if (materialPaths.Count < 2)
		{
			EditorUtility.DisplayDialog("Remove Identical Materials", "The selected folder does not contain enough materials to compare.", "OK");
			return;
		}

		var duplicates = FindDuplicateMaterials(materialPaths, out var duplicateGroupCount);
		if (duplicates.Count == 0)
		{
			EditorUtility.DisplayDialog("Remove Identical Materials", "No identical materials were found in the selected folder.", "OK");
			return;
		}

		var duplicateCount = duplicates.Count;
		var shouldContinue = EditorUtility.DisplayDialog(
			"Remove Identical Materials",
			$"Found {duplicateCount} duplicate materials across {duplicateGroupCount} groups in\n{folderPath}\n\nReferences across project assets, prefabs, and scenes will be updated before duplicates are deleted.",
			"Remove Duplicates",
			"Cancel");

		if (!shouldContinue)
		{
			return;
		}

		try
		{
			var updatedAssetCount = ReplaceMaterialReferencesAcrossProject(duplicates);
			var deletedCount = DeleteDuplicateMaterials(duplicates);

			AssetDatabase.SaveAssets();
			AssetDatabase.Refresh();

			EditorUtility.DisplayDialog(
				"Remove Identical Materials",
				$"Removed {deletedCount} duplicate materials.\nUpdated {updatedAssetCount} assets, prefabs, or scenes.",
				"OK");
		}
		finally
		{
			EditorUtility.ClearProgressBar();
		}
	}

	[MenuItem(MenuPath, true)]
	private static bool ValidateRemoveIdenticalMaterialsInSelectedFolder()
	{
		return !string.IsNullOrWhiteSpace(GetSelectedFolderPath());
	}

	private static string GetSelectedFolderPath()
	{
		foreach (var selectedObject in Selection.GetFiltered<UnityEngine.Object>(SelectionMode.Assets))
		{
			var assetPath = AssetDatabase.GetAssetPath(selectedObject);
			if (!string.IsNullOrWhiteSpace(assetPath) && AssetDatabase.IsValidFolder(assetPath))
			{
				return assetPath;
			}
		}

		return null;
	}

	private static Dictionary<Material, DuplicateMaterialRecord> FindDuplicateMaterials(List<string> materialPaths, out int duplicateGroupCount)
	{
		var canonicalBySignature = new Dictionary<string, DuplicateMaterialRecord>(StringComparer.Ordinal);
		var duplicateSignatures = new HashSet<string>(StringComparer.Ordinal);
		var duplicates = new Dictionary<Material, DuplicateMaterialRecord>();

		for (var index = 0; index < materialPaths.Count; index++)
		{
			var materialPath = materialPaths[index];
			EditorUtility.DisplayProgressBar("Scanning Materials", materialPath, (float)(index + 1) / materialPaths.Count);

			var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
			if (material == null)
			{
				continue;
			}

			var signature = BuildMaterialSignature(material);
			if (!canonicalBySignature.TryGetValue(signature, out var canonicalRecord))
			{
				canonicalBySignature.Add(signature, new DuplicateMaterialRecord(materialPath, materialPath, material));
				continue;
			}

			duplicates.Add(material, new DuplicateMaterialRecord(materialPath, canonicalRecord.CanonicalPath, canonicalRecord.CanonicalMaterial));
			duplicateSignatures.Add(signature);
			Debug.Log($"Duplicate material: {materialPath} -> {canonicalRecord.CanonicalPath}", material);
		}

		duplicateGroupCount = duplicateSignatures.Count;
		return duplicates;
	}

	private static string BuildMaterialSignature(Material material)
	{
		var copy = new Material(material)
		{
			name = string.Empty,
			hideFlags = HideFlags.None
		};

		try
		{
			return EditorJsonUtility.ToJson(copy, false);
		}
		finally
		{
			UnityEngine.Object.DestroyImmediate(copy);
		}
	}

	private static int ReplaceMaterialReferencesAcrossProject(Dictionary<Material, DuplicateMaterialRecord> duplicates)
	{
		var assetPaths = AssetDatabase.GetAllAssetPaths()
			.Where(path => path.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
			.ToArray();

		var updatedAssetCount = 0;

		for (var index = 0; index < assetPaths.Length; index++)
		{
			var assetPath = assetPaths[index];
			EditorUtility.DisplayProgressBar("Replacing References", assetPath, (float)(index + 1) / assetPaths.Length);

			if (ReplaceMaterialReferencesAtPath(assetPath, duplicates))
			{
				updatedAssetCount++;
			}
		}

		return updatedAssetCount;
	}

	private static bool ReplaceMaterialReferencesAtPath(string assetPath, Dictionary<Material, DuplicateMaterialRecord> duplicates)
	{
		var extension = Path.GetExtension(assetPath);
		if (string.Equals(extension, ".prefab", StringComparison.OrdinalIgnoreCase))
		{
			return ReplaceMaterialReferencesInPrefab(assetPath, duplicates);
		}

		if (string.Equals(extension, ".unity", StringComparison.OrdinalIgnoreCase))
		{
			return ReplaceMaterialReferencesInScene(assetPath, duplicates);
		}

		return ReplaceMaterialReferencesInAssetObjects(assetPath, duplicates);
	}

	private static bool ReplaceMaterialReferencesInPrefab(string assetPath, Dictionary<Material, DuplicateMaterialRecord> duplicates)
	{
		var prefabRoot = PrefabUtility.LoadPrefabContents(assetPath);

		try
		{
			var changed = ReplaceMaterialReferencesInHierarchy(prefabRoot, duplicates);
			if (changed)
			{
				PrefabUtility.SaveAsPrefabAsset(prefabRoot, assetPath);
			}

			return changed;
		}
		finally
		{
			PrefabUtility.UnloadPrefabContents(prefabRoot);
		}
	}

	private static bool ReplaceMaterialReferencesInScene(string assetPath, Dictionary<Material, DuplicateMaterialRecord> duplicates)
	{
		var scene = SceneManager.GetSceneByPath(assetPath);
		var wasAlreadyLoaded = scene.IsValid() && scene.isLoaded;

		if (!wasAlreadyLoaded)
		{
			scene = EditorSceneManager.OpenScene(assetPath, OpenSceneMode.Additive);
		}

		try
		{
			var changed = false;
			foreach (var rootObject in scene.GetRootGameObjects())
			{
				changed |= ReplaceMaterialReferencesInHierarchy(rootObject, duplicates);
			}

			if (changed)
			{
				EditorSceneManager.SaveScene(scene);
			}

			return changed;
		}
		finally
		{
			if (!wasAlreadyLoaded && scene.IsValid() && scene.isLoaded)
			{
				EditorSceneManager.CloseScene(scene, true);
			}
		}
	}

	private static bool ReplaceMaterialReferencesInHierarchy(GameObject root, Dictionary<Material, DuplicateMaterialRecord> duplicates)
	{
		var changed = false;
		var transforms = root.GetComponentsInChildren<Transform>(true);

		foreach (var currentTransform in transforms)
		{
			changed |= ReplaceMaterialReferencesInObject(currentTransform.gameObject, duplicates);
			foreach (var component in currentTransform.GetComponents<Component>())
			{
				if (component == null)
				{
					continue;
				}

				changed |= ReplaceMaterialReferencesInObject(component, duplicates);
			}
		}

		return changed;
	}

	private static bool ReplaceMaterialReferencesInAssetObjects(string assetPath, Dictionary<Material, DuplicateMaterialRecord> duplicates)
	{
		var changed = false;
		foreach (var assetObject in AssetDatabase.LoadAllAssetsAtPath(assetPath))
		{
			if (assetObject == null)
			{
				continue;
			}

			changed |= ReplaceMaterialReferencesInObject(assetObject, duplicates);
		}

		return changed;
	}

	private static bool ReplaceMaterialReferencesInObject(UnityEngine.Object target, Dictionary<Material, DuplicateMaterialRecord> duplicates)
	{
		SerializedObject serializedObject;

		try
		{
			serializedObject = new SerializedObject(target);
		}
		catch (Exception)
		{
			return false;
		}

		var iterator = serializedObject.GetIterator();
		var changed = false;
		var enterChildren = true;

		while (iterator.Next(enterChildren))
		{
			enterChildren = true;
			if (iterator.propertyType != SerializedPropertyType.ObjectReference)
			{
				continue;
			}

			var currentMaterial = iterator.objectReferenceValue as Material;
			if (currentMaterial == null || !duplicates.TryGetValue(currentMaterial, out var duplicateRecord))
			{
				continue;
			}

			iterator.objectReferenceValue = duplicateRecord.CanonicalMaterial;
			changed = true;
		}

		if (!changed)
		{
			return false;
		}

		serializedObject.ApplyModifiedPropertiesWithoutUndo();
		EditorUtility.SetDirty(target);
		return true;
	}

	private static int DeleteDuplicateMaterials(Dictionary<Material, DuplicateMaterialRecord> duplicates)
	{
		var deletedCount = 0;
		var duplicatePaths = duplicates.Values
			.Select(record => record.DuplicatePath)
			.Distinct(StringComparer.OrdinalIgnoreCase)
			.OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
			.ToList();

		for (var index = 0; index < duplicatePaths.Count; index++)
		{
			var duplicatePath = duplicatePaths[index];
			EditorUtility.DisplayProgressBar("Deleting Duplicate Materials", duplicatePath, (float)(index + 1) / duplicatePaths.Count);

			if (AssetDatabase.DeleteAsset(duplicatePath))
			{
				deletedCount++;
			}
		}

		return deletedCount;
	}

	private readonly struct DuplicateMaterialRecord
	{
		public DuplicateMaterialRecord(string duplicatePath, string canonicalPath, Material canonicalMaterial)
		{
			DuplicatePath = duplicatePath;
			CanonicalPath = canonicalPath;
			CanonicalMaterial = canonicalMaterial;
		}

		public string DuplicatePath { get; }
		public string CanonicalPath { get; }
		public Material CanonicalMaterial { get; }
	}
}