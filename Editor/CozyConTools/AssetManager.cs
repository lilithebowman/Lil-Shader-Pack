/**
 * AssetManager.cs
 *
 * Centralized asset management for LOD generation.
 * Handles saving meshes, textures, materials, and prefabs with consistent naming and directory structure.
 * 
 * CC0-Attribution License by Lilithe for CozyCon 2025
 */

using UnityEngine;
using UnityEditor;
using System.IO;

namespace CozyCon.Tools
{
	/// <summary>
	/// Handles all asset saving operations for LOD generation with consistent patterns
	/// </summary>
	public static class AssetManager
	{
		/// <summary>
		/// Saves a mesh as an asset with proper naming and directory structure
		/// </summary>
		public static void SaveMeshAsAsset(Mesh mesh, string baseName, string savePath)
		{
			try
			{
				// Ensure the mesh directory exists
				string meshSavePath = Path.Combine(savePath, "Meshes");
				EnsureDirectoryExists(meshSavePath);

				// Generate unique mesh name and save
				string meshPath = GetUniqueAssetPath(meshSavePath, baseName, ".asset");
				AssetDatabase.CreateAsset(mesh, meshPath);
				Debug.Log($"[AssetManager] Mesh saved as asset: {meshPath}");
			}
			catch (System.Exception e)
			{
				Debug.LogError($"[AssetManager] Failed to save mesh as asset: {e.Message}");
			}
		}

		/// <summary>
		/// Saves a texture as an asset with proper import settings
		/// </summary>
		public static void SaveTextureAsAsset(Texture2D texture, string baseName, string savePath, bool enableTransparency = true)
		{
			try
			{
				// Ensure the texture directory exists
				string textureSavePath = Path.Combine(savePath, "Textures");
				EnsureDirectoryExists(textureSavePath);

				// Generate unique texture name and save as PNG
				string texturePath = GetUniqueAssetPath(textureSavePath, baseName, ".png");

				// Save as PNG with alpha channel
				byte[] pngData = texture.EncodeToPNG();
				File.WriteAllBytes(texturePath, pngData);

				// Import and configure texture settings
				AssetDatabase.ImportAsset(texturePath);
				ConfigureTextureImportSettings(texturePath, enableTransparency);

				Debug.Log($"[AssetManager] Texture saved as asset: {texturePath}");
			}
			catch (System.Exception e)
			{
				Debug.LogError($"[AssetManager] Failed to save texture as asset: {e.Message}");
			}
		}

		/// <summary>
		/// Saves a material as an asset
		/// </summary>
		public static void SaveMaterialAsAsset(Material material, string baseName, string savePath)
		{
			try
			{
				// Ensure the material directory exists
				string materialSavePath = Path.Combine(savePath, "Materials");
				EnsureDirectoryExists(materialSavePath);

				// Generate unique material name and save
				string materialPath = GetUniqueAssetPath(materialSavePath, baseName, ".mat");
				AssetDatabase.CreateAsset(material, materialPath);
				Debug.Log($"[AssetManager] Material saved as asset: {materialPath}");
			}
			catch (System.Exception e)
			{
				Debug.LogError($"[AssetManager] Failed to save material as asset: {e.Message}");
			}
		}

		/// <summary>
		/// Creates a prefab from the LOD group with proper setup and validation
		/// </summary>
		public static GameObject CreateLODPrefab(GameObject lodGroup, string prefabSavePath, string modelName)
		{
			try
			{
				// Ensure the save directory exists
				EnsureDirectoryExists(prefabSavePath);

				// Refresh the asset database to ensure all assets are properly imported
				AssetDatabase.SaveAssets();
				AssetDatabase.Refresh();

				// Generate unique prefab name
				string prefabPath = GetUniqueAssetPath(prefabSavePath, modelName + "_LOD", ".prefab");

				// Mark all child objects as dirty to ensure proper prefab creation
				MarkHierarchyDirty(lodGroup);

				// Create the prefab
				Debug.Log($"[AssetManager] Creating prefab at: {prefabPath}");
				GameObject prefab = PrefabUtility.SaveAsPrefabAsset(lodGroup, prefabPath);

				if (prefab != null)
				{
					ValidatePrefabCreation(prefab);

					// Select the created prefab in the project view
					EditorGUIUtility.PingObject(prefab);

					Debug.Log($"[AssetManager] Prefab created successfully at: {prefabPath}");
					EditorUtility.DisplayDialog("Prefab Created",
						$"LOD Prefab saved successfully:\n{prefabPath}\n\nThe prefab includes all LOD levels and is ready to use.",
						"OK");

					return prefab;
				}
				else
				{
					Debug.LogError("[AssetManager] Failed to create prefab");
					EditorUtility.DisplayDialog("Prefab Creation Failed",
						"Failed to create the LOD prefab. Check the console for details.",
						"OK");
					return null;
				}
			}
			catch (System.Exception e)
			{
				Debug.LogError($"[AssetManager] Prefab creation error: {e.Message}");
				EditorUtility.DisplayDialog("Prefab Creation Error",
					$"Error creating prefab:\n{e.Message}",
					"OK");
				return null;
			}
		}

		/// <summary>
		/// Exports a detailed LOD generation report to a text file
		/// </summary>
		public static void ExportLODReport(LODGenerationResult result, LODGenerationSettings settings)
		{
			if (result == null) return;

			string timestamp = System.DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
			string filename = $"LOD_Report_{result.originalModel.name}_{timestamp}.txt";
			string path = EditorUtility.SaveFilePanel("Export LOD Report", Application.dataPath, filename, "txt");

			if (string.IsNullOrEmpty(path)) return;

			try
			{
				var report = GenerateDetailedReport(result, settings);
				File.WriteAllText(path, report);

				EditorUtility.DisplayDialog("Export Complete",
					$"LOD report exported to:\n{path}", "OK");

				Debug.Log($"[AssetManager] Report exported to: {path}");
			}
			catch (System.Exception e)
			{
				EditorUtility.DisplayDialog("Export Error",
					$"Failed to export report:\n{e.Message}", "OK");
			}
		}

		#region Private Helper Methods

		/// <summary>
		/// Ensures a directory exists, creating it if necessary
		/// </summary>
		private static void EnsureDirectoryExists(string directoryPath)
		{
			if (!Directory.Exists(directoryPath))
			{
				Directory.CreateDirectory(directoryPath);
				AssetDatabase.Refresh();
			}
		}

		/// <summary>
		/// Generates a unique asset path by handling naming conflicts
		/// </summary>
		private static string GetUniqueAssetPath(string directory, string baseName, string extension)
		{
			string assetPath = Path.Combine(directory, baseName + extension);

			// Handle name conflicts
			int counter = 1;
			string originalPath = assetPath;
			while (File.Exists(assetPath))
			{
				string nameWithoutExt = Path.GetFileNameWithoutExtension(originalPath);
				string dir = Path.GetDirectoryName(originalPath);
				assetPath = Path.Combine(dir, $"{nameWithoutExt}_{counter}{extension}");
				counter++;
			}

			return assetPath;
		}

		/// <summary>
		/// Configures texture import settings for proper transparency and optimization
		/// </summary>
		private static void ConfigureTextureImportSettings(string texturePath, bool enableTransparency)
		{
			TextureImporter textureImporter = AssetImporter.GetAtPath(texturePath) as TextureImporter;
			if (textureImporter != null)
			{
				textureImporter.textureType = TextureImporterType.Default;
				textureImporter.alphaSource = TextureImporterAlphaSource.FromInput;
				textureImporter.alphaIsTransparency = enableTransparency;
				textureImporter.mipmapEnabled = true;
				textureImporter.wrapMode = TextureWrapMode.Clamp;
				textureImporter.filterMode = FilterMode.Bilinear;

				// Set compression settings
				TextureImporterPlatformSettings platformSettings = new TextureImporterPlatformSettings();
				platformSettings.name = "Standalone";
				platformSettings.overridden = true;
				platformSettings.format = enableTransparency ? TextureImporterFormat.DXT5 : TextureImporterFormat.DXT1;
				platformSettings.compressionQuality = 100;
				textureImporter.SetPlatformTextureSettings(platformSettings);

				AssetDatabase.ImportAsset(texturePath, ImportAssetOptions.ForceUpdate);
			}
		}

		/// <summary>
		/// Marks all objects in a hierarchy as dirty for proper prefab creation
		/// </summary>
		private static void MarkHierarchyDirty(GameObject root)
		{
			EditorUtility.SetDirty(root);
			foreach (Transform child in root.transform)
			{
				MarkHierarchyDirty(child.gameObject);
			}
		}

		/// <summary>
		/// Validates that a prefab was created correctly with proper LOD components
		/// </summary>
		private static void ValidatePrefabCreation(GameObject prefab)
		{
			var prefabLODGroup = prefab.GetComponent<LODGroup>();
			if (prefabLODGroup != null)
			{
				var prefabLODs = prefabLODGroup.GetLODs();
				Debug.Log($"[AssetManager] Prefab created with {prefabLODs.Length} LODs");
				for (int i = 0; i < prefabLODs.Length; i++)
				{
					Debug.Log($"[AssetManager] Prefab LOD {i}: {prefabLODs[i].renderers.Length} renderers");
				}
			}
			else
			{
				Debug.LogWarning("[AssetManager] Prefab missing LOD Group component!");
			}
		}

		/// <summary>
		/// Generates a detailed text report of the LOD generation results
		/// </summary>
		private static string GenerateDetailedReport(LODGenerationResult result, LODGenerationSettings settings)
		{
			var report = new System.Text.StringBuilder();

			report.AppendLine("LOD GENERATION REPORT");
			report.AppendLine("====================");
			report.AppendLine($"Generated: {System.DateTime.Now}");
			report.AppendLine($"Original Model: {result.originalModel.name}");
			report.AppendLine();

			// Original mesh info
			report.AppendLine("ORIGINAL MESH");
			report.AppendLine("-------------");
			report.AppendLine($"Triangles: {result.originalMesh.triangleCount:N0}");
			report.AppendLine($"Vertices: {result.originalMesh.vertexCount:N0}");
			report.AppendLine($"Size: {result.originalMesh.sizeKB:F1} KB");
			report.AppendLine();

			// LOD levels
			if (result.lod1Mesh != null)
			{
				report.AppendLine("LOD1 (50% REDUCTION)");
				report.AppendLine("-------------------");
				report.AppendLine($"Triangles: {result.lod1Mesh.triangleCount:N0}");
				report.AppendLine($"Vertices: {result.lod1Mesh.vertexCount:N0}");
				report.AppendLine($"Size: {result.lod1Mesh.sizeKB:F1} KB");
				report.AppendLine();
			}

			if (result.lod2Mesh != null)
			{
				report.AppendLine("LOD2 (75% REDUCTION)");
				report.AppendLine("-------------------");
				report.AppendLine($"Triangles: {result.lod2Mesh.triangleCount:N0}");
				report.AppendLine($"Vertices: {result.lod2Mesh.vertexCount:N0}");
				report.AppendLine($"Size: {result.lod2Mesh.sizeKB:F1} KB");
				report.AppendLine();
			}

			// Billboards
			if (result.billboards?.Length > 0)
			{
				report.AppendLine("BILLBOARDS");
				report.AppendLine("----------");
				report.AppendLine($"Count: {result.billboards.Length}");
				report.AppendLine($"Resolution: {settings.billboardResolution}x{settings.billboardResolution}");
				report.AppendLine($"Angles: {settings.billboardAngles} ({360f / settings.billboardAngles:F0}° apart)");
				report.AppendLine();
			}

			// Settings used
			report.AppendLine("GENERATION SETTINGS");
			report.AppendLine("------------------");
			report.AppendLine($"Decimation Angle: {settings.decimationAngle:F1}°");
			report.AppendLine($"Preserve UVs: {settings.preserveUVs}");
			report.AppendLine($"Preserve Normals: {settings.preserveNormals}");
			report.AppendLine($"VRChat Optimized: {settings.useVRChatOptimizedDistances}");
			report.AppendLine($"LOD Fade Mode: {settings.selectedFadeMode}");
			report.AppendLine();

			// Summary
			report.AppendLine("PERFORMANCE SUMMARY");
			report.AppendLine("------------------");
			report.AppendLine($"Total Size Reduction: {result.totalSizeReduction:F1}%");
			report.AppendLine($"LOD Group Created: {result.lodGroup.name}");
			if (!string.IsNullOrEmpty(result.prefabPath))
			{
				report.AppendLine($"Prefab Path: {result.prefabPath}");
			}

			return report.ToString();
		}

		#endregion
	}
}