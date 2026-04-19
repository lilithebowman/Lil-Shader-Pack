using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

public static class CreateOrmeMaterialsFromFolder
{
	private const string ShaderName = "Lilithe/ORME-Standard-Shader";

	[MenuItem("Tools/Lilithe/Create ORME Materials From Selected Folder")]
	private static void CreateMaterialsFromSelection()
	{
		var selectedFolders = Selection.assetGUIDs
			.Select(AssetDatabase.GUIDToAssetPath)
			.Where(AssetDatabase.IsValidFolder)
			.Distinct()
			.ToList();

		if (selectedFolders.Count == 0)
		{
			EditorUtility.DisplayDialog(
				"Create ORME Materials",
				"Select one or more folders in the Project window.",
				"OK");
			return;
		}

		var shader = Shader.Find(ShaderName);
		if (shader == null)
		{
			EditorUtility.DisplayDialog(
				"Create ORME Materials",
				$"Shader '{ShaderName}' was not found.",
				"OK");
			return;
		}

		var createdCount = 0;
		var skippedCount = 0;

		AssetDatabase.StartAssetEditing();
		try
		{
			foreach (var folderPath in selectedFolders)
			{
				var textures = GetFolderTextures(folderPath);
				if (textures.Count == 0)
				{
					Debug.LogWarning($"[ORME] No textures found in '{folderPath}'.");
					skippedCount++;
					continue;
				}

				var texturesByFolder = textures
					.GroupBy(t => t.FolderPath, StringComparer.OrdinalIgnoreCase)
					.OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase);

				foreach (var folderGroup in texturesByFolder)
				{
					var folderTextures = folderGroup.ToList();
					var baseColors = folderTextures
						.Where(IsBaseColorTexture)
						.OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
						.ToList();

					if (baseColors.Count == 0)
					{
						continue;
					}

					foreach (var baseColor in baseColors)
					{
						var rootName = ExtractRootName(baseColor.Name, "basecolor", "base_color", "base color", "albedo", "diffuse");
						var materialName = string.IsNullOrWhiteSpace(rootName) ? baseColor.Name : rootName;
						var materialPath = Path.Combine(folderGroup.Key, materialName + ".mat").Replace("\\", "/");

						if (AssetDatabase.LoadAssetAtPath<Material>(materialPath) != null)
						{
							Debug.Log($"[ORME] Skipping existing material: {materialPath}");
							skippedCount++;
							continue;
						}

						var normal = FindAssociatedTexture(folderTextures, rootName, new[] { "normal", "normalgl", "normaldx", "nrm", "nor" });
						var height = FindAssociatedTexture(folderTextures, rootName, new[] { "height", "displacement", "disp", "parallax" });
						var orme = FindAssociatedTexture(folderTextures, rootName, new[] { "orme", "occlusionroughnessmetallicemission" });

						if (normal != null)
						{
							EnsureNormalMapImport(normal.Path);
						}

						var material = new Material(shader);
						material.SetTexture("_MainTex", baseColor.Texture);

						if (normal != null)
						{
							material.SetFloat("_UseNormalMap", 1f);
							material.SetTexture("_BumpMap", normal.Texture);
							material.EnableKeyword("_USE_NORMALMAP");
						}
						else
						{
							material.SetFloat("_UseNormalMap", 0f);
							material.DisableKeyword("_USE_NORMALMAP");
						}

						if (height != null)
						{
							material.SetFloat("_UseHeightMap", 1f);
							material.SetTexture("_ParallaxMap", height.Texture);
							material.EnableKeyword("_USE_HEIGHTMAP");
						}
						else
						{
							material.SetFloat("_UseHeightMap", 0f);
							material.DisableKeyword("_USE_HEIGHTMAP");
						}

						if (orme != null)
						{
							material.SetFloat("_UseORME", 1f);
							material.SetTexture("_ORMEMap", orme.Texture);
						}
						else
						{
							material.SetFloat("_UseORME", 0f);
						}

						AssetDatabase.CreateAsset(material, materialPath);
						Debug.Log($"[ORME] Created material: {materialPath}");
						createdCount++;
					}
				}
			}
		}
		finally
		{
			AssetDatabase.StopAssetEditing();
			AssetDatabase.SaveAssets();
			AssetDatabase.Refresh();
		}

		EditorUtility.DisplayDialog(
			"Create ORME Materials",
			$"Done. Created: {createdCount}, Skipped: {skippedCount}",
			"OK");
	}

	[MenuItem("Tools/Lilithe/Create ORME Materials From Selected Folder", true)]
	private static bool ValidateCreateMaterialsFromSelection()
	{
		return Selection.assetGUIDs
			.Select(AssetDatabase.GUIDToAssetPath)
			.Any(AssetDatabase.IsValidFolder);
	}

	private static List<TextureRecord> GetFolderTextures(string folderPath)
	{
		var textureGuids = AssetDatabase.FindAssets("t:Texture2D", new[] { folderPath });
		var textures = new List<TextureRecord>(textureGuids.Length);

		foreach (var guid in textureGuids)
		{
			var path = AssetDatabase.GUIDToAssetPath(guid);
			var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
			if (texture == null)
			{
				continue;
			}

			var textureFolder = Path.GetDirectoryName(path)?.Replace("\\", "/") ?? folderPath;
			textures.Add(new TextureRecord(path, textureFolder, Path.GetFileNameWithoutExtension(path), texture));
		}

		return textures;
	}

	private static bool IsBaseColorTexture(TextureRecord texture)
	{
		return ContainsToken(texture.Name, "basecolor")
			|| ContainsToken(texture.Name, "base_color")
			|| ContainsToken(texture.Name, "base color")
			|| ContainsToken(texture.Name, "albedo")
			|| ContainsToken(texture.Name, "diffuse");
	}

	private static TextureRecord FindFirstByTokens(IEnumerable<TextureRecord> textures, params string[] tokens)
	{
		return textures
			.OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
			.FirstOrDefault(t => tokens.Any(token => ContainsToken(t.Name, token)));
	}

	private static TextureRecord FindAssociatedTexture(List<TextureRecord> textures, string rootName, IReadOnlyCollection<string> tokens)
	{
		var tokenMatches = textures
			.Where(t => tokens.Any(token => ContainsToken(t.Name, token)))
			.ToList();

		if (tokenMatches.Count == 0)
		{
			return null;
		}

		if (!string.IsNullOrWhiteSpace(rootName))
		{
			var normalizedRoot = NormalizeForCompare(rootName);
			var rooted = tokenMatches
				.Where(t => NormalizeForCompare(t.Name).StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase))
				.OrderBy(t => t.Name.Length)
				.ThenBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
				.FirstOrDefault();

			if (rooted != null)
			{
				return rooted;
			}
		}

		return tokenMatches
			.OrderBy(t => t.Name.Length)
			.ThenBy(t => t.Name, StringComparer.OrdinalIgnoreCase)
			.FirstOrDefault();
	}

	private static string ExtractRootName(string fileName, params string[] removableTokens)
	{
		var normalized = fileName;
		foreach (var token in removableTokens)
		{
			normalized = RemoveToken(normalized, token);
		}

		normalized = normalized.Trim(' ', '_', '-', '.');
		return string.IsNullOrWhiteSpace(normalized) ? fileName : normalized;
	}

	private static void EnsureNormalMapImport(string assetPath)
	{
		var importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
		if (importer == null)
		{
			return;
		}

		if (importer.textureType != TextureImporterType.NormalMap)
		{
			importer.textureType = TextureImporterType.NormalMap;
			importer.SaveAndReimport();
		}
	}

	private static bool ContainsToken(string source, string token)
	{
		return NormalizeForCompare(source).Contains(NormalizeForCompare(token), StringComparison.OrdinalIgnoreCase);
	}

	private static string RemoveToken(string source, string token)
	{
		var normalizedSource = NormalizeForCompare(source);
		var normalizedToken = NormalizeForCompare(token);
		var index = normalizedSource.IndexOf(normalizedToken, StringComparison.OrdinalIgnoreCase);

		if (index < 0)
		{
			return source;
		}

		var sourceChars = source.ToCharArray();
		var keepMask = Enumerable.Repeat(true, sourceChars.Length).ToArray();
		var normalizedPositions = new List<int>();

		for (var i = 0; i < sourceChars.Length; i++)
		{
			if (char.IsLetterOrDigit(sourceChars[i]))
			{
				normalizedPositions.Add(i);
			}
		}

		for (var i = index; i < index + normalizedToken.Length && i < normalizedPositions.Count; i++)
		{
			keepMask[normalizedPositions[i]] = false;
		}

		var resultChars = new List<char>(sourceChars.Length);
		for (var i = 0; i < sourceChars.Length; i++)
		{
			if (keepMask[i])
			{
				resultChars.Add(sourceChars[i]);
			}
		}

		return new string(resultChars.ToArray());
	}

	private static string NormalizeForCompare(string value)
	{
		if (string.IsNullOrEmpty(value))
		{
			return string.Empty;
		}

		var chars = value.Where(char.IsLetterOrDigit).ToArray();
		return new string(chars).ToLowerInvariant();
	}

	private sealed class TextureRecord
	{
		public TextureRecord(string path, string folderPath, string name, Texture2D texture)
		{
			Path = path;
			FolderPath = folderPath;
			Name = name;
			Texture = texture;
		}

		public string Path { get; }
		public string FolderPath { get; }
		public string Name { get; }
		public Texture2D Texture { get; }
	}
}
