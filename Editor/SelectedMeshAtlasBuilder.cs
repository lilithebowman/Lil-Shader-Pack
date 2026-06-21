using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

public static class SelectedMeshAtlasBuilder
{
	private const string MenuPath = "Tools/Materials/Create Atlased Material From Selected Meshes";
	private const string DebugMenuPath = "Tools/Materials/Create Atlased Material From Selected Meshes (Debug Overlay)";
	private const int MaxAtlasSize = 8192;
	private const int MinimumCellSize = 16;

	private static readonly string[] DiffusePropertyCandidates = { "_BaseMap", "_MainTex" };
	private static readonly string[] DiffuseColorCandidates = { "_BaseColor", "_Color" };
	private static readonly string[] NormalPropertyCandidates = { "_BumpMap", "_NormalMap" };
	private static readonly string[] HeightPropertyCandidates = { "_ParallaxMap", "_HeightMap" };

	[MenuItem(MenuPath)]
	private static void CreateAtlasedMaterialFromSelectedMeshes()
	{
		CreateAtlasedMaterialFromSelectedMeshesInternal(false);
	}

	[MenuItem(DebugMenuPath)]
	private static void CreateAtlasedMaterialFromSelectedMeshesWithDebugOverlay()
	{
		CreateAtlasedMaterialFromSelectedMeshesInternal(true);
	}

	private static void CreateAtlasedMaterialFromSelectedMeshesInternal(bool generateDebugOverlay)
	{
		var rendererInfos = GetSelectedRendererInfos();
		if (rendererInfos.Count == 0)
		{
			EditorUtility.DisplayDialog("Create Atlased Material", "Select one or more GameObjects with MeshRenderer or SkinnedMeshRenderer components.", "OK");
			return;
		}

		var skippedRenderers = rendererInfos.Where(info => info.SkipReason != null).ToList();
		var validRenderers = rendererInfos.Where(info => info.SkipReason == null).ToList();

		if (validRenderers.Count == 0)
		{
			EditorUtility.DisplayDialog("Create Atlased Material", "No supported meshes were found in the selection. Skinned meshes with blend shapes are currently skipped.", "OK");
			return;
		}

		var materialEntries = BuildMaterialEntries(validRenderers);
		if (materialEntries.Count == 0)
		{
			EditorUtility.DisplayDialog("Create Atlased Material", "No materials were found on the selected meshes.", "OK");
			return;
		}

		var savePath = EditorUtility.SaveFilePanelInProject(
			"Save Atlased Material",
			"AtlasedMaterial",
			"mat",
			"Choose where to save the generated atlas material.");

		if (string.IsNullOrWhiteSpace(savePath))
		{
			return;
		}

		var outputFolder = EnsureOutputFolder(savePath);
		var baseName = Path.GetFileNameWithoutExtension(savePath);

		try
		{
			EditorUtility.DisplayProgressBar("Preparing Atlas", "Analyzing material UV usage", 0.05f);
			ConfigureMaterialUvMetadata(materialEntries);

			var layout = CreateLayout(materialEntries.Count);
			AssignAtlasRects(materialEntries, layout);

			EditorUtility.DisplayProgressBar("Preparing Atlas", "Baking atlas textures", 0.15f);
			var atlasTextures = CreateAtlases(materialEntries, layout, outputFolder, baseName, generateDebugOverlay);

			EditorUtility.DisplayProgressBar("Preparing Atlas", "Creating atlas material", 0.65f);
			var atlasMaterial = CreateAtlasMaterial(savePath, materialEntries, atlasTextures);

			EditorUtility.DisplayProgressBar("Preparing Atlas", "Remapping selected meshes", 0.75f);
			var generatedMeshCount = CreateAtlasedMeshes(validRenderers, materialEntries, atlasMaterial, outputFolder, baseName);

			AssetDatabase.SaveAssets();
			AssetDatabase.Refresh();

			var summary = $"Created atlas material and {generatedMeshCount} remapped mesh assets for {validRenderers.Count} renderer(s).";
			if (generateDebugOverlay && atlasTextures.DebugOverlay != null)
			{
				var debugOverlayPath = AssetDatabase.GetAssetPath(atlasTextures.DebugOverlay);
				summary += $"\nDebug overlay saved at:\n{debugOverlayPath}";
			}

			if (skippedRenderers.Count > 0)
			{
				summary += $"\n\nSkipped {skippedRenderers.Count} renderer(s):\n" + string.Join("\n", skippedRenderers.Select(info => $"- {info.Renderer.name}: {info.SkipReason}"));
			}

			EditorUtility.DisplayDialog("Create Atlased Material", summary, "OK");
		}
		finally
		{
			EditorUtility.ClearProgressBar();
		}
	}

	[MenuItem(MenuPath, true)]
	private static bool ValidateCreateAtlasedMaterialFromSelectedMeshes()
	{
		return Selection.gameObjects != null && Selection.gameObjects.Length > 0;
	}

	[MenuItem(DebugMenuPath, true)]
	private static bool ValidateCreateAtlasedMaterialFromSelectedMeshesWithDebugOverlay()
	{
		return ValidateCreateAtlasedMaterialFromSelectedMeshes();
	}

	private static List<RendererInfo> GetSelectedRendererInfos()
	{
		var infos = new List<RendererInfo>();
		var seenRenderers = new HashSet<Renderer>();

		foreach (var root in Selection.gameObjects)
		{
			if (root == null)
			{
				continue;
			}

			foreach (var meshRenderer in root.GetComponentsInChildren<MeshRenderer>(true))
			{
				if (meshRenderer == null || seenRenderers.Contains(meshRenderer))
				{
					continue;
				}

				seenRenderers.Add(meshRenderer);
				var filter = meshRenderer.GetComponent<MeshFilter>();
				if (filter == null || filter.sharedMesh == null)
				{
					continue;
				}

				infos.Add(new RendererInfo(meshRenderer, filter, null, filter.sharedMesh, meshRenderer.sharedMaterials, null));
			}

			foreach (var skinnedRenderer in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
			{
				if (skinnedRenderer == null || seenRenderers.Contains(skinnedRenderer))
				{
					continue;
				}

				seenRenderers.Add(skinnedRenderer);
				var skipReason = skinnedRenderer.sharedMesh == null
					? "Missing shared mesh"
					: skinnedRenderer.sharedMesh.blendShapeCount > 0
						? "Blend shapes are not preserved by this atlas tool"
						: null;

				infos.Add(new RendererInfo(skinnedRenderer, null, skinnedRenderer, skinnedRenderer.sharedMesh, skinnedRenderer.sharedMaterials, skipReason));
			}
		}

		return infos;
	}

	private static List<MaterialEntry> BuildMaterialEntries(List<RendererInfo> rendererInfos)
	{
		var materialEntries = new Dictionary<Material, MaterialEntry>();

		foreach (var rendererInfo in rendererInfos)
		{
			var subMeshCount = rendererInfo.SharedMesh.subMeshCount;
			for (var subMeshIndex = 0; subMeshIndex < subMeshCount; subMeshIndex++)
			{
				var material = ResolveMaterial(rendererInfo.SharedMaterials, subMeshIndex);
				if (material == null)
				{
					continue;
				}

				if (!materialEntries.TryGetValue(material, out var entry))
				{
					entry = new MaterialEntry(material);
					materialEntries.Add(material, entry);
				}

				entry.Usages.Add(new MaterialUsage(rendererInfo, subMeshIndex));
			}
		}

		return materialEntries.Values.OrderBy(entry => entry.Material.name, StringComparer.OrdinalIgnoreCase).ToList();
	}

	private static void ConfigureMaterialUvMetadata(List<MaterialEntry> materialEntries)
	{
		foreach (var entry in materialEntries)
		{
			var transformProperty = ResolvePrimaryUvProperty(entry.Material);
			entry.TextureScale = transformProperty == null ? Vector2.one : entry.Material.GetTextureScale(transformProperty);
			entry.TextureOffset = transformProperty == null ? Vector2.zero : entry.Material.GetTextureOffset(transformProperty);
			entry.TextureWrapMode = ResolvePrimaryWrapMode(entry.Material);
			entry.RespectAlpha = ShouldRespectAlpha(entry.Material);
		}
	}

	private static AtlasLayout CreateLayout(int materialCount)
	{
		var gridSize = Mathf.CeilToInt(Mathf.Sqrt(materialCount));
		var cellSize = 1024;
		while (gridSize * cellSize > MaxAtlasSize && cellSize > MinimumCellSize)
		{
			cellSize /= 2;
		}

		var atlasSize = gridSize * cellSize;
		return new AtlasLayout(gridSize, cellSize, atlasSize);
	}

	private static void AssignAtlasRects(List<MaterialEntry> materialEntries, AtlasLayout layout)
	{
		for (var index = 0; index < materialEntries.Count; index++)
		{
			var cellX = index % layout.GridSize;
			var cellY = index / layout.GridSize;
			materialEntries[index].AtlasRect = new Rect(
				(float)(cellX * layout.CellSize) / layout.AtlasSize,
				(float)(cellY * layout.CellSize) / layout.AtlasSize,
				(float)layout.CellSize / layout.AtlasSize,
				(float)layout.CellSize / layout.AtlasSize);
		}
	}

	private static AtlasTextureSet CreateAtlases(List<MaterialEntry> materialEntries, AtlasLayout layout, string outputFolder, string baseName, bool generateDebugOverlay)
	{
		var diffuseAlphaStats = new List<DiffuseAlphaStat>(materialEntries.Count);
		var diffuseAtlas = BuildAtlasTexture(materialEntries, layout, GetDiffuseTexture, GetDiffuseFallbackColor, false, diffuseAlphaStats);
		var hasNormalSources = materialEntries.Any(entry => GetNormalTexture(entry) != null);
		var hasHeightSources = materialEntries.Any(entry => GetHeightTexture(entry) != null);

		var normalAtlas = hasNormalSources
			? BuildAtlasTexture(materialEntries, layout, GetNormalTexture, GetNormalFallbackColor, true, null)
			: null;
		var heightAtlas = hasHeightSources
			? BuildAtlasTexture(materialEntries, layout, GetHeightTexture, GetHeightFallbackColor, true, null)
			: null;
		var debugOverlayAtlas = generateDebugOverlay ? BuildDebugOverlayTexture(materialEntries, layout) : null;

		var diffusePath = SaveTextureAsset(diffuseAtlas, outputFolder, baseName + "_DiffuseAtlas", false, false);
		var normalPath = normalAtlas != null ? SaveTextureAsset(normalAtlas, outputFolder, baseName + "_NormalAtlas", true, true) : null;
		var heightPath = heightAtlas != null ? SaveTextureAsset(heightAtlas, outputFolder, baseName + "_HeightAtlas", true, false) : null;
		var debugPath = debugOverlayAtlas != null
			? SaveTextureAsset(debugOverlayAtlas, outputFolder, baseName + "_DebugOverlayAtlas", false, false)
			: null;

		LogDiffuseAlphaReport(diffuseAlphaStats);

		return new AtlasTextureSet(
			AssetDatabase.LoadAssetAtPath<Texture2D>(diffusePath),
			string.IsNullOrEmpty(normalPath) ? null : AssetDatabase.LoadAssetAtPath<Texture2D>(normalPath),
			string.IsNullOrEmpty(heightPath) ? null : AssetDatabase.LoadAssetAtPath<Texture2D>(heightPath),
			string.IsNullOrEmpty(debugPath) ? null : AssetDatabase.LoadAssetAtPath<Texture2D>(debugPath));
	}

	private static Texture2D BuildDebugOverlayTexture(List<MaterialEntry> materialEntries, AtlasLayout layout)
	{
		var atlas = new Texture2D(layout.AtlasSize, layout.AtlasSize, TextureFormat.RGBA32, false, false)
		{
			wrapMode = TextureWrapMode.Clamp,
			filterMode = FilterMode.Point,
			name = "DebugOverlayAtlas"
		};

		var background = new Color(0.08f, 0.08f, 0.08f, 1f);
		atlas.SetPixels(Enumerable.Repeat(background, layout.AtlasSize * layout.AtlasSize).ToArray());

		for (var index = 0; index < materialEntries.Count; index++)
		{
			var entry = materialEntries[index];
			var cellColor = GetDebugColor(index);
			var pixelRect = GetPixelRect(entry.AtlasRect, layout.AtlasSize);

			for (var y = 0; y < pixelRect.height; y++)
			{
				for (var x = 0; x < pixelRect.width; x++)
				{
					var border = x == 0 || y == 0 || x == pixelRect.width - 1 || y == pixelRect.height - 1;
					var checker = ((x / 16) + (y / 16)) % 2 == 0;
					var color = border ? Color.black : (checker ? cellColor : Color.Lerp(cellColor, Color.white, 0.25f));
					atlas.SetPixel(pixelRect.x + x, pixelRect.y + y, color);
				}
			}

			Debug.Log($"Atlas debug cell {index}: material '{entry.Material.name}' -> rect {entry.AtlasRect}", entry.Material);
		}

		atlas.Apply(false, false);
		return atlas;
	}

	private static Color GetDebugColor(int index)
	{
		var hue = Mathf.Repeat(index * 0.61803398875f, 1f);
		return Color.HSVToRGB(hue, 0.7f, 0.95f);
	}

	private static Texture2D BuildAtlasTexture(
		List<MaterialEntry> materialEntries,
		AtlasLayout layout,
		Func<MaterialEntry, Texture2D> textureResolver,
		Func<MaterialEntry, Color> fallbackResolver,
		bool linear,
		List<DiffuseAlphaStat> diffuseAlphaStats)
	{
		var atlas = new Texture2D(layout.AtlasSize, layout.AtlasSize, TextureFormat.RGBA32, false, linear)
		{
			wrapMode = TextureWrapMode.Clamp,
			filterMode = FilterMode.Bilinear,
			name = "Atlas"
		};

		var fillColor = linear ? Color.black : Color.clear;
		var blankPixels = Enumerable.Repeat(fillColor, layout.AtlasSize * layout.AtlasSize).ToArray();
		atlas.SetPixels(blankPixels);

		for (var index = 0; index < materialEntries.Count; index++)
		{
			var entry = materialEntries[index];
			var sourceTexture = textureResolver(entry);
			var cellPixels = BakeMaterialCell(entry, sourceTexture, layout.CellSize, linear, fallbackResolver(entry));
			if (diffuseAlphaStats != null)
			{
				var alphaRange = ComputeAlphaRange(cellPixels);
				diffuseAlphaStats.Add(new DiffuseAlphaStat(entry.Material, alphaRange.x, alphaRange.y));
			}

			var pixelRect = GetPixelRect(entry.AtlasRect, layout.AtlasSize);
			atlas.SetPixels(pixelRect.x, pixelRect.y, pixelRect.width, pixelRect.height, cellPixels);
		}

		atlas.Apply(false, false);
		return atlas;
	}

	private static Color[] BakeMaterialCell(MaterialEntry entry, Texture2D sourceTexture, int cellSize, bool linear, Color fallbackColor)
	{
		var pixels = new Color[cellSize * cellSize];
		if (sourceTexture == null)
		{
			var fallback = fallbackColor;
			if (!linear && !entry.RespectAlpha)
			{
				fallback.a = 1f;
			}

			for (var i = 0; i < pixels.Length; i++)
			{
				pixels[i] = fallback;
			}

			return pixels;
		}

		var sampledTexture = CreateReadableTexture(sourceTexture, linear);
		try
		{
			var tint = GetDiffuseTint(entry.Material);
			var wrapMode = sourceTexture.wrapMode;
			for (var y = 0; y < cellSize; y++)
			{
				var v = (y + 0.5f) / cellSize;
				for (var x = 0; x < cellSize; x++)
				{
					var u = (x + 0.5f) / cellSize;
					var sampleUv = Vector2.Scale(new Vector2(u, v), entry.TextureScale) + entry.TextureOffset;
					var sampleColor = SampleTexture(sampledTexture, sampleUv, wrapMode);
					if (!linear)
					{
						sampleColor *= tint;
						if (!entry.RespectAlpha)
						{
							sampleColor.a = 1f;
						}
					}

					pixels[y * cellSize + x] = sampleColor;
				}
			}
		}
		finally
		{
			UnityEngine.Object.DestroyImmediate(sampledTexture);
		}

		return pixels;
	}

	private static Vector2 ComputeAlphaRange(Color[] colors)
	{
		if (colors == null || colors.Length == 0)
		{
			return Vector2.one;
		}

		var minAlpha = 1f;
		var maxAlpha = 0f;
		for (var i = 0; i < colors.Length; i++)
		{
			var alpha = colors[i].a;
			if (alpha < minAlpha)
			{
				minAlpha = alpha;
			}

			if (alpha > maxAlpha)
			{
				maxAlpha = alpha;
			}
		}

		return new Vector2(minAlpha, maxAlpha);
	}

	private static void LogDiffuseAlphaReport(List<DiffuseAlphaStat> stats)
	{
		if (stats == null || stats.Count == 0)
		{
			return;
		}

		const float alphaDetectedThreshold = 0.999f;
		const float variationThreshold = 0.01f;

		var materialsWithAnyTransparency = stats.Count(stat => stat.MinAlpha < alphaDetectedThreshold);
		var materialsWithVariation = stats.Count(stat => (stat.MaxAlpha - stat.MinAlpha) > variationThreshold);

		Debug.Log(
			$"[SelectedMeshAtlasBuilder] Diffuse alpha analysis: {materialsWithAnyTransparency}/{stats.Count} material cell(s) contain alpha < 1.0, " +
			$"{materialsWithVariation}/{stats.Count} cell(s) contain meaningful alpha variation.");

		for (var i = 0; i < stats.Count; i++)
		{
			var stat = stats[i];
			var status = stat.MinAlpha < alphaDetectedThreshold
				? ((stat.MaxAlpha - stat.MinAlpha) > variationThreshold ? "Alpha Variation" : "Flat Alpha")
				: "Opaque";

			Debug.Log(
				$"[SelectedMeshAtlasBuilder] Alpha cell {i}: material '{stat.MaterialName}' min={stat.MinAlpha:F4} max={stat.MaxAlpha:F4} status={status}",
				stat.Material);
		}
	}

	private static AtlasMaterial CreateAtlasMaterial(string savePath, List<MaterialEntry> entries, AtlasTextureSet atlasTextures)
	{
		var referenceMaterial = entries[0].Material;
		var atlasMaterial = new Material(referenceMaterial)
		{
			name = Path.GetFileNameWithoutExtension(savePath)
		};

		ClearInheritedLightingTextures(atlasMaterial);
		AssignTexture(atlasMaterial, DiffusePropertyCandidates, atlasTextures.Diffuse, Vector2.one, Vector2.zero);
		AssignTexture(atlasMaterial, NormalPropertyCandidates, atlasTextures.Normal, Vector2.one, Vector2.zero);
		AssignTexture(atlasMaterial, HeightPropertyCandidates, atlasTextures.Height, Vector2.one, Vector2.zero);

		foreach (var colorProperty in DiffuseColorCandidates)
		{
			if (atlasMaterial.HasProperty(colorProperty))
			{
				atlasMaterial.SetColor(colorProperty, Color.white);
			}
		}

		if (atlasTextures.Normal != null)
		{
			atlasMaterial.EnableKeyword("_NORMALMAP");
		}
		else
		{
			atlasMaterial.DisableKeyword("_NORMALMAP");
		}

		if (atlasTextures.Height != null)
		{
			atlasMaterial.EnableKeyword("_PARALLAXMAP");
		}
		else
		{
			atlasMaterial.DisableKeyword("_PARALLAXMAP");
		}

		AssetDatabase.CreateAsset(atlasMaterial, savePath);
		return new AtlasMaterial(atlasMaterial);
	}

	private static int CreateAtlasedMeshes(List<RendererInfo> rendererInfos, List<MaterialEntry> materialEntries, AtlasMaterial atlasMaterial, string outputFolder, string baseName)
	{
		var entryLookup = materialEntries.ToDictionary(entry => entry.Material);
		var meshCount = 0;

		for (var index = 0; index < rendererInfos.Count; index++)
		{
			var rendererInfo = rendererInfos[index];
			EditorUtility.DisplayProgressBar("Remapping Meshes", rendererInfo.Renderer.name, (float)(index + 1) / rendererInfos.Count);

			var newMesh = BuildAtlasedMesh(rendererInfo, entryLookup);
			if (newMesh == null)
			{
				continue;
			}

			var meshPath = AssetDatabase.GenerateUniqueAssetPath(Path.Combine(outputFolder, $"{baseName}_{rendererInfo.Renderer.name}_Atlased.asset").Replace('\\', '/'));
			AssetDatabase.CreateAsset(newMesh, meshPath);
			meshCount++;

			AssignAtlasedMesh(rendererInfo, newMesh, atlasMaterial.Material);
		}

		return meshCount;
	}

	private static Mesh BuildAtlasedMesh(RendererInfo rendererInfo, Dictionary<Material, MaterialEntry> entryLookup)
	{
		var sourceMesh = rendererInfo.SharedMesh;
		if (sourceMesh == null)
		{
			return null;
		}

		var sourceVertices = sourceMesh.vertices;
		var sourceNormals = sourceMesh.normals;
		var sourceColors = sourceMesh.colors;
		var sourceUv0 = sourceMesh.uv;
		var sourceUv2 = sourceMesh.uv2;
		var sourceUv3 = sourceMesh.uv3;
		var sourceUv4 = sourceMesh.uv4;
		var sourceBoneWeights = sourceMesh.boneWeights;

		var vertices = new List<Vector3>();
		var normals = new List<Vector3>();
		var colors = new List<Color>();
		var uv0 = new List<Vector2>();
		var uv2 = new List<Vector2>();
		var uv3 = new List<Vector2>();
		var uv4 = new List<Vector2>();
		var boneWeights = new List<BoneWeight>();
		var triangles = new List<int>();

		for (var subMeshIndex = 0; subMeshIndex < sourceMesh.subMeshCount; subMeshIndex++)
		{
			var material = ResolveMaterial(rendererInfo.SharedMaterials, subMeshIndex);
			if (material == null || !entryLookup.TryGetValue(material, out var entry))
			{
				continue;
			}

			var sourceTriangles = sourceMesh.GetTriangles(subMeshIndex);
			for (var triangleIndex = 0; triangleIndex < sourceTriangles.Length; triangleIndex++)
			{
				var sourceIndex = sourceTriangles[triangleIndex];
				triangles.Add(vertices.Count);
				vertices.Add(sourceVertices[sourceIndex]);

				if (sourceNormals != null && sourceNormals.Length == sourceVertices.Length)
				{
					normals.Add(sourceNormals[sourceIndex]);
				}

				if (sourceColors != null && sourceColors.Length == sourceVertices.Length)
				{
					colors.Add(sourceColors[sourceIndex]);
				}


				var sourceUv = sourceUv0 != null && sourceUv0.Length == sourceVertices.Length
					? sourceUv0[sourceIndex]
					: Vector2.zero;

				uv0.Add(MapUvToAtlas(sourceUv, entry));

				if (sourceUv2 != null && sourceUv2.Length == sourceVertices.Length)
				{
					uv2.Add(sourceUv2[sourceIndex]);
				}

				if (sourceUv3 != null && sourceUv3.Length == sourceVertices.Length)
				{
					uv3.Add(sourceUv3[sourceIndex]);
				}

				if (sourceUv4 != null && sourceUv4.Length == sourceVertices.Length)
				{
					uv4.Add(sourceUv4[sourceIndex]);
				}

				if (sourceBoneWeights != null && sourceBoneWeights.Length == sourceVertices.Length)
				{
					boneWeights.Add(sourceBoneWeights[sourceIndex]);
				}
			}
		}

		if (triangles.Count == 0)
		{
			return null;
		}

		var mesh = new Mesh
		{
			name = sourceMesh.name + "_Atlased",
			indexFormat = vertices.Count > ushort.MaxValue ? IndexFormat.UInt32 : IndexFormat.UInt16,
			bindposes = sourceMesh.bindposes
		};

		mesh.SetVertices(vertices);
		if (normals.Count == vertices.Count)
		{
			mesh.SetNormals(normals);
		}
		else
		{
			mesh.RecalculateNormals();
		}

		if (colors.Count == vertices.Count)
		{
			mesh.SetColors(colors);
		}

		mesh.SetUVs(0, uv0);
		if (uv2.Count == vertices.Count)
		{
			mesh.SetUVs(1, uv2);
		}

		if (uv3.Count == vertices.Count)
		{
			mesh.SetUVs(2, uv3);
		}

		if (uv4.Count == vertices.Count)
		{
			mesh.SetUVs(3, uv4);
		}

		if (boneWeights.Count == vertices.Count)
		{
			mesh.boneWeights = boneWeights.ToArray();
		}

		mesh.SetTriangles(triangles, 0);
		mesh.RecalculateTangents();
		mesh.RecalculateBounds();
		return mesh;
	}

	private static void ClearInheritedLightingTextures(Material material)
	{
		ClearTexture(material, "_MetallicGlossMap");
		ClearTexture(material, "_OcclusionMap");
		ClearTexture(material, "_EmissionMap");
		ClearTexture(material, "_DetailMask");
		ClearTexture(material, "_DetailAlbedoMap");
		ClearTexture(material, "_DetailNormalMap");

		if (material.HasProperty("_Metallic"))
		{
			material.SetFloat("_Metallic", 0f);
		}

		if (material.HasProperty("_OcclusionStrength"))
		{
			material.SetFloat("_OcclusionStrength", 0f);
		}

		if (material.HasProperty("_Parallax"))
		{
			material.SetFloat("_Parallax", 0f);
		}

		if (material.HasProperty("_EmissionColor"))
		{
			material.SetColor("_EmissionColor", Color.black);
		}

		material.DisableKeyword("_METALLICGLOSSMAP");
		material.DisableKeyword("_SPECGLOSSMAP");
		material.DisableKeyword("_OCCLUSIONMAP");
		material.DisableKeyword("_EMISSION");
		material.DisableKeyword("_DETAIL_MULX2");
	}

	private static void ClearTexture(Material material, string propertyName)
	{
		if (!material.HasProperty(propertyName))
		{
			return;
		}

		material.SetTexture(propertyName, null);
	}

	private static void AssignAtlasedMesh(RendererInfo rendererInfo, Mesh mesh, Material atlasMaterial)
	{
		Undo.RecordObject(rendererInfo.Renderer, "Assign Atlased Material");
		if (rendererInfo.MeshFilter != null)
		{
			Undo.RecordObject(rendererInfo.MeshFilter, "Assign Atlased Mesh");
			rendererInfo.MeshFilter.sharedMesh = mesh;
			EditorUtility.SetDirty(rendererInfo.MeshFilter);
		}

		if (rendererInfo.SkinnedRenderer != null)
		{
			Undo.RecordObject(rendererInfo.SkinnedRenderer, "Assign Atlased Mesh");
			rendererInfo.SkinnedRenderer.sharedMesh = mesh;
			EditorUtility.SetDirty(rendererInfo.SkinnedRenderer);
		}

		rendererInfo.Renderer.sharedMaterials = new[] { atlasMaterial };
		EditorUtility.SetDirty(rendererInfo.Renderer);
	}

	private static Vector2 MapUvToAtlas(Vector2 sourceUv, MaterialEntry entry)
	{
		var transformed = Vector2.Scale(sourceUv, entry.TextureScale) + entry.TextureOffset;
		var normalized = new Vector2(
			NormalizeCoordinate(transformed.x, entry.TextureWrapMode),
			NormalizeCoordinate(transformed.y, entry.TextureWrapMode));

		return new Vector2(
			entry.AtlasRect.xMin + normalized.x * entry.AtlasRect.width,
			entry.AtlasRect.yMin + normalized.y * entry.AtlasRect.height);
	}

	private static Texture2D CreateReadableTexture(Texture2D source, bool linear)
	{
		var previous = RenderTexture.active;
		var temporary = RenderTexture.GetTemporary(source.width, source.height, 0, RenderTextureFormat.ARGB32, linear ? RenderTextureReadWrite.Linear : RenderTextureReadWrite.sRGB);
		try
		{
			Graphics.Blit(source, temporary);
			RenderTexture.active = temporary;
			var copy = new Texture2D(source.width, source.height, TextureFormat.RGBA32, false, linear)
			{
				wrapMode = source.wrapMode,
				filterMode = source.filterMode,
				name = source.name + "_Readable"
			};

			copy.ReadPixels(new Rect(0, 0, source.width, source.height), 0, 0);
			copy.Apply(false, false);
			return copy;
		}
		finally
		{
			RenderTexture.active = previous;
			RenderTexture.ReleaseTemporary(temporary);
		}
	}

	private static Color SampleTexture(Texture2D texture, Vector2 uv, TextureWrapMode wrapMode)
	{
		var sampleUv = new Vector2(NormalizeCoordinate(uv.x, wrapMode), NormalizeCoordinate(uv.y, wrapMode));
		return texture.GetPixelBilinear(sampleUv.x, sampleUv.y);
	}

	private static float NormalizeCoordinate(float value, TextureWrapMode wrapMode)
	{
		switch (wrapMode)
		{
			case TextureWrapMode.Repeat:
				return Mathf.Repeat(value, 1f);
			case TextureWrapMode.Mirror:
				return Mathf.PingPong(value, 1f);
			default:
				return Mathf.Clamp01(value);
		}
	}

	private static string SaveTextureAsset(Texture2D texture, string outputFolder, string fileName, bool linear, bool normalMap)
	{
		var path = AssetDatabase.GenerateUniqueAssetPath(Path.Combine(outputFolder, fileName + ".png").Replace('\\', '/'));
		File.WriteAllBytes(path, texture.EncodeToPNG());
		AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);

		var importer = AssetImporter.GetAtPath(path) as TextureImporter;
		if (importer != null)
		{
			importer.textureType = normalMap ? TextureImporterType.NormalMap : TextureImporterType.Default;
			importer.sRGBTexture = !linear;
			importer.alphaIsTransparency = false;
			importer.wrapMode = TextureWrapMode.Clamp;
			importer.filterMode = FilterMode.Bilinear;
			importer.textureCompression = TextureImporterCompression.Uncompressed;
			importer.SaveAndReimport();
		}

		UnityEngine.Object.DestroyImmediate(texture);
		return path;
	}

	private static string EnsureOutputFolder(string materialSavePath)
	{
		var parentFolder = Path.GetDirectoryName(materialSavePath)?.Replace('\\', '/');
		var folderName = Path.GetFileNameWithoutExtension(materialSavePath) + "_AtlasAssets";
		var outputFolder = parentFolder + "/" + folderName;

		if (!AssetDatabase.IsValidFolder(outputFolder))
		{
			AssetDatabase.CreateFolder(parentFolder, folderName);
		}

		return outputFolder;
	}

	private static Texture2D GetDiffuseTexture(MaterialEntry entry)
	{
		return GetTexture(entry.Material, DiffusePropertyCandidates);
	}

	private static Texture2D GetNormalTexture(MaterialEntry entry)
	{
		return GetTexture(entry.Material, NormalPropertyCandidates);
	}

	private static Texture2D GetHeightTexture(MaterialEntry entry)
	{
		return GetTexture(entry.Material, HeightPropertyCandidates);
	}

	private static Texture2D GetTexture(Material material, string[] propertyCandidates)
	{
		foreach (var propertyName in propertyCandidates)
		{
			if (material.HasProperty(propertyName))
			{
				return material.GetTexture(propertyName) as Texture2D;
			}
		}

		return null;
	}

	private static string ResolvePrimaryUvProperty(Material material)
	{
		return ResolveExistingProperty(material, DiffusePropertyCandidates)
			?? ResolveExistingProperty(material, NormalPropertyCandidates)
			?? ResolveExistingProperty(material, HeightPropertyCandidates);
	}

	private static TextureWrapMode ResolvePrimaryWrapMode(Material material)
	{
		var texture = GetTexture(material, DiffusePropertyCandidates)
			?? GetTexture(material, NormalPropertyCandidates)
			?? GetTexture(material, HeightPropertyCandidates);

		return texture != null ? texture.wrapMode : TextureWrapMode.Repeat;
	}

	private static bool ShouldRespectAlpha(Material material)
	{
		if (material == null)
		{
			return false;
		}

		if (material.renderQueue >= (int)RenderQueue.AlphaTest)
		{
			return true;
		}

		if (material.IsKeywordEnabled("_ALPHATEST_ON") ||
			material.IsKeywordEnabled("_ALPHABLEND_ON") ||
			material.IsKeywordEnabled("_ALPHAPREMULTIPLY_ON") ||
			material.IsKeywordEnabled("_SURFACE_TYPE_TRANSPARENT"))
		{
			return true;
		}

		if (material.HasProperty("_Mode"))
		{
			var mode = Mathf.RoundToInt(material.GetFloat("_Mode"));
			if (mode != 0)
			{
				return true;
			}
		}

		if (material.HasProperty("_Surface"))
		{
			var surface = Mathf.RoundToInt(material.GetFloat("_Surface"));
			if (surface != 0)
			{
				return true;
			}
		}

		var renderType = material.GetTag("RenderType", false, string.Empty);
		return string.Equals(renderType, "Transparent", StringComparison.OrdinalIgnoreCase)
			|| string.Equals(renderType, "TransparentCutout", StringComparison.OrdinalIgnoreCase);
	}

	private static string ResolveExistingProperty(Material material, string[] propertyCandidates)
	{
		foreach (var propertyName in propertyCandidates)
		{
			if (material.HasProperty(propertyName) && material.GetTexture(propertyName) != null)
			{
				return propertyName;
			}
		}

		foreach (var propertyName in propertyCandidates)
		{
			if (material.HasProperty(propertyName))
			{
				return propertyName;
			}
		}

		return null;
	}

	private static Color GetDiffuseTint(Material material)
	{
		foreach (var propertyName in DiffuseColorCandidates)
		{
			if (material.HasProperty(propertyName))
			{
				return material.GetColor(propertyName);
			}
		}

		return Color.white;
	}

	private static Color GetDiffuseFallbackColor(MaterialEntry entry)
	{
		return GetDiffuseTint(entry.Material);
	}

	private static Color GetNormalFallbackColor(MaterialEntry entry)
	{
		return new Color(0.5f, 0.5f, 1f, 1f);
	}

	private static Color GetHeightFallbackColor(MaterialEntry entry)
	{
		return new Color(0.5f, 0.5f, 0.5f, 1f);
	}

	private static void AssignTexture(Material material, string[] propertyCandidates, Texture2D texture, Vector2 scale, Vector2 offset)
	{
		foreach (var propertyName in propertyCandidates)
		{
			if (!material.HasProperty(propertyName))
			{
				continue;
			}

			material.SetTexture(propertyName, texture);
			material.SetTextureScale(propertyName, scale);
			material.SetTextureOffset(propertyName, offset);
		}
	}

	private static Material ResolveMaterial(Material[] materials, int subMeshIndex)
	{
		if (materials == null || materials.Length == 0)
		{
			return null;
		}

		var index = Mathf.Clamp(subMeshIndex, 0, materials.Length - 1);
		return materials[index];
	}

	private static RectInt GetPixelRect(Rect uvRect, int atlasSize)
	{
		return new RectInt(
			Mathf.RoundToInt(uvRect.x * atlasSize),
			Mathf.RoundToInt(uvRect.y * atlasSize),
			Mathf.RoundToInt(uvRect.width * atlasSize),
			Mathf.RoundToInt(uvRect.height * atlasSize));
	}

	private readonly struct RendererInfo
	{
		public RendererInfo(Renderer renderer, MeshFilter meshFilter, SkinnedMeshRenderer skinnedRenderer, Mesh sharedMesh, Material[] sharedMaterials, string skipReason)
		{
			Renderer = renderer;
			MeshFilter = meshFilter;
			SkinnedRenderer = skinnedRenderer;
			SharedMesh = sharedMesh;
			SharedMaterials = sharedMaterials ?? Array.Empty<Material>();
			SkipReason = skipReason;
		}

		public Renderer Renderer { get; }
		public MeshFilter MeshFilter { get; }
		public SkinnedMeshRenderer SkinnedRenderer { get; }
		public Mesh SharedMesh { get; }
		public Material[] SharedMaterials { get; }
		public string SkipReason { get; }
	}

	private sealed class MaterialEntry
	{
		public MaterialEntry(Material material)
		{
			Material = material;
			Usages = new List<MaterialUsage>();
			TextureScale = Vector2.one;
			TextureWrapMode = TextureWrapMode.Repeat;
			RespectAlpha = false;
		}

		public Material Material { get; }
		public List<MaterialUsage> Usages { get; }
		public Rect AtlasRect { get; set; }
		public Vector2 TextureScale { get; set; }
		public Vector2 TextureOffset { get; set; }
		public TextureWrapMode TextureWrapMode { get; set; }
		public bool RespectAlpha { get; set; }
	}

	private readonly struct MaterialUsage
	{
		public MaterialUsage(RendererInfo rendererInfo, int subMeshIndex)
		{
			RendererInfo = rendererInfo;
			SubMeshIndex = subMeshIndex;
		}

		public RendererInfo RendererInfo { get; }
		public int SubMeshIndex { get; }
	}

	private readonly struct AtlasLayout
	{
		public AtlasLayout(int gridSize, int cellSize, int atlasSize)
		{
			GridSize = gridSize;
			CellSize = cellSize;
			AtlasSize = atlasSize;
		}

		public int GridSize { get; }
		public int CellSize { get; }
		public int AtlasSize { get; }
	}

	private readonly struct AtlasTextureSet
	{
		public AtlasTextureSet(Texture2D diffuse, Texture2D normal, Texture2D height, Texture2D debugOverlay)
		{
			Diffuse = diffuse;
			Normal = normal;
			Height = height;
			DebugOverlay = debugOverlay;
		}

		public Texture2D Diffuse { get; }
		public Texture2D Normal { get; }
		public Texture2D Height { get; }
		public Texture2D DebugOverlay { get; }
	}

	private readonly struct AtlasMaterial
	{
		public AtlasMaterial(Material material)
		{
			Material = material;
		}

		public Material Material { get; }
	}

	private readonly struct DiffuseAlphaStat
	{
		public DiffuseAlphaStat(Material material, float minAlpha, float maxAlpha)
		{
			Material = material;
			MaterialName = material != null ? material.name : "<null>";
			MinAlpha = minAlpha;
			MaxAlpha = maxAlpha;
		}

		public Material Material { get; }
		public string MaterialName { get; }
		public float MinAlpha { get; }
		public float MaxAlpha { get; }
	}
}