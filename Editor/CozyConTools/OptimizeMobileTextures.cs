using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using System.IO;

// CozyCon: Batch-apply mobile texture settings for PNGs to keep Android/iOS under ~100MB.
public static class OptimizeMobileTextures
{
	private const string MenuPath = "Lilithe/Optimize Mobile Textures";
	private const long MaxBudgetBytes = 100L * 1024L * 1024L; // 100 MB per platform
	private static readonly string[] Platforms = { "Android", "iPhone" };
	// Workaround: Some projects (e.g., with Udon/Odin) can throw during reimport. Set to true to force reimport now.
	private const bool ReimportImmediately = false;
	// Optional safety buffer deducted from the upload budget (default 10 MB)
	private const int DefaultBufferMB = 10;
	private const string BufferPrefsKey = "CozyCon_UploadBufferMB";
	private const string MaxSizePrefsKey = "CozyCon_MobileTexMaxSize"; // -1 = Auto
	private const int DefaultMaxSizePref = -1;

	// Allowlist: paths containing any of these substrings or assets labeled with AllowListLabel
	private static readonly string[] AllowListPathContains = new string[] {
		// Examples: "Assets/Textures/Hero", "SkySeries", "UI/Icons"
	};
	private const string AllowListLabel = "CozyConKeep";


	[MenuItem(MenuPath)]
	public static void Run()
	{
		// Prefer textures actually used by open scenes; fallback to all textures in the project
		var sceneTexturePaths = GetOpenSceneTexturePaths();
		List<string> assetPaths;
		if (sceneTexturePaths.Count > 0)
		{
			assetPaths = sceneTexturePaths;
		}
		else
		{
			// Include all textures (Texture2D, Cubemap, etc.)
			var guids = AssetDatabase.FindAssets("t:Texture");
			assetPaths = guids.Select(AssetDatabase.GUIDToAssetPath).Distinct().ToList();
		}

		if (assetPaths.Count == 0)
		{
			Debug.Log("[OptimizeMobileTextures] No Texture assets found.");
			return;
		}

		// If optimizing based on scene, also estimate non-texture bytes in scene to reserve budget
		long nonTextureBytes = 0;
		if (sceneTexturePaths.Count > 0)
		{
			var sceneDeps = GetOpenSceneDependencyPaths();
			var textureSet = new HashSet<string>(assetPaths);
			foreach (var dep in sceneDeps)
			{
				if (textureSet.Contains(dep)) continue; // handled separately
				if (dep.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)) continue;
				nonTextureBytes += GetFileSizeBytes(dep);
			}
		}

		// Determine candidate max sizes based on user preference (Auto or a fixed cap)
		int[] defaultSizes = { 4096, 2048, 1024, 512, 256, 128 };
		int prefMax = GetPreferredMaxSize();
		List<int> sizes = new List<int>();
		if (prefMax > 0)
		{
			// Try the preferred value first, then progressively smaller sizes
			sizes.Add(prefMax);
			foreach (var s in defaultSizes)
				if (s < prefMax) sizes.Add(s);
		}
		else
		{
			sizes.AddRange(defaultSizes);
		}
		int[] candidateSizes = sizes.ToArray();
		int[] candidateBlocks = { 8, 10, 12 }; // ASTC block sizes (smaller block = higher quality/larger size)
		int chosenMaxSize = 1024;
		int chosenAstcBlock = 8;

		// Pick the highest-quality combo that fits the budget
		bool picked = false;
		long bufferBytes = GetUploadBufferBytes();
		long reservedBytes = nonTextureBytes + bufferBytes;
		long textureBudget = Math.Max(0, MaxBudgetBytes - reservedBytes);
		if (nonTextureBytes > 0 || bufferBytes > 0)
		{
			Debug.LogFormat("[OptimizeMobileTextures] Reserved: NonTex ~{0:0.0} MB + Buffer ~{1:0.0} MB => Texture budget ~{2:0.0} MB",
				nonTextureBytes / (1024f * 1024f), bufferBytes / (1024f * 1024f), textureBudget / (1024f * 1024f));
		}
		foreach (var size in candidateSizes)
		{
			foreach (var block in candidateBlocks)
			{
				long est = EstimateTotalAstcBytes(assetPaths, size, block);
				if (est + reservedBytes <= MaxBudgetBytes)
				{
					chosenMaxSize = size;
					chosenAstcBlock = block;
					Debug.LogFormat("[OptimizeMobileTextures] Chosen size/block: {0} / ASTC {1}x{1} (textures est: {2:0.0} MB; total est: {3:0.0} MB)",
						chosenMaxSize, chosenAstcBlock, est / (1024f * 1024f), (est + reservedBytes) / (1024f * 1024f));
					picked = true;
					break;
				}
			}
			if (picked) break;
		}
		if (!picked)
		{
			// Could not fit even at lowest quality; pick the lowest quality as fallback
			chosenMaxSize = candidateSizes.Last();
			chosenAstcBlock = candidateBlocks.Last();
			long est = EstimateTotalAstcBytes(assetPaths, chosenMaxSize, chosenAstcBlock);
			Debug.LogWarningFormat("[OptimizeMobileTextures] Over budget even at lowest quality; proceeding with {0} / ASTC {1}x{1} (textures est: {2:0.0} MB; total est: {3:0.0} MB)",
				chosenMaxSize, chosenAstcBlock, est / (1024f * 1024f), (est + reservedBytes) / (1024f * 1024f));
		}

		// For summary: store info for each texture
		var summaryRows = new List<(string path, string kind, bool allowlisted, int origW, int origH, long origBytes, int newW, int newH, long astcBytes)>();
		long totalOrigBytes = 0;
		long totalAstcBytes = 0;

		int changed = 0, skipped = 0, errors = 0;
		int reimported = 0; // how many we actually reimported if ReimportImmediately is true
		foreach (var path in assetPaths)
		{
			try
			{
				var importer = AssetImporter.GetAtPath(path) as TextureImporter;
				var tex2D = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
				var cube = AssetDatabase.LoadAssetAtPath<Cubemap>(path);

				// Skip if importer or texture is null
				if (importer == null || (tex2D == null && cube == null))
				{
					// Try to get asset type for logging
					var obj = AssetDatabase.LoadMainAssetAtPath(path);
					string typeName = obj != null ? obj.GetType().Name : "(none)";
					Debug.LogWarning($"[OptimizeMobileTextures] Skipping: {path} (Importer or Texture2D missing, asset type: {typeName})");
					skipped++;
					continue;
				}

				// Do not skip normal maps: compress them too unless truly unsupported

				string kind;
				int origW, origH;
				bool isCube = cube != null;
				if (isCube)
				{
					kind = "Cubemap";
					origW = cube.width; // per-face size
					origH = cube.height;
				}
				else
				{
					kind = "Texture2D";
					origW = tex2D.width;
					origH = tex2D.height;
				}
				long origBytes = origW * origH * 4L; // RGBA32 estimate
				if (isCube) origBytes *= 6; // six faces

				bool isAllow = IsAllowListed(path);

				// Clamp by max size
				int newW = origW, newH = origH;
				int targetMax = chosenMaxSize;
				if (isAllow)
				{
					// For allowlisted textures, don't force our chosenMaxSize; approximate using existing overrides if any
					int maxAndroid = GetExistingPlatformMaxSize(importer, "Android");
					int maxiOS = GetExistingPlatformMaxSize(importer, "iPhone");
					int cap = Math.Max(Math.Max(0, maxAndroid), Math.Max(0, maxiOS));
					if (cap > 0) targetMax = Math.Min(cap, Math.Max(origW, origH));
					else targetMax = Math.Max(origW, origH); // no cap
				}
				if (origW >= origH)
				{
					if (origW > targetMax)
					{
						float scale = (float)targetMax / origW;
						newW = targetMax;
						newH = Mathf.Max(1, Mathf.RoundToInt(origH * scale));
					}
				}
				else
				{
					if (origH > targetMax)
					{
						float scale = (float)targetMax / origH;
						newH = targetMax;
						newW = Mathf.Max(1, Mathf.RoundToInt(origW * scale));
					}
				}
				int blocksX = (newW + chosenAstcBlock - 1) / chosenAstcBlock;
				int blocksY = (newH + chosenAstcBlock - 1) / chosenAstcBlock;
				long astcBytes = (long)blocksX * blocksY * 16L;
				if (isCube) astcBytes *= 6; // six faces
				astcBytes = (long)(astcBytes * 1.2f); // mip overhead

				totalOrigBytes += origBytes;
				totalAstcBytes += astcBytes;
				summaryRows.Add((path, kind, isAllow, origW, origH, origBytes, newW, newH, astcBytes));

				importer.mipmapEnabled = true;
				importer.textureCompression = TextureImporterCompression.Compressed;

				foreach (var platform in Platforms)
				{
					var s = importer.GetPlatformTextureSettings(platform) ?? new TextureImporterPlatformSettings();
					s.name = platform;
					s.overridden = true;
					// Preserve allowlisted size cap while still enforcing ASTC format
					s.maxTextureSize = isAllow ? GetExistingPlatformMaxSize(importer, platform) : chosenMaxSize;
					if (isAllow && (s.maxTextureSize <= 0 || s.maxTextureSize < Math.Max(origW, origH)))
					{
						// If no existing cap or it's lower than original, keep original size
						s.maxTextureSize = Math.Max(origW, origH);
					}
					s.textureCompression = TextureImporterCompression.Compressed;
					s.compressionQuality = 50;
					s.crunchedCompression = false;
					// Map chosen block to Unity format
					switch (chosenAstcBlock)
					{
						case 8: s.format = TextureImporterFormat.ASTC_8x8; break;
						case 10: s.format = TextureImporterFormat.ASTC_10x10; break;
						default: s.format = TextureImporterFormat.ASTC_12x12; break;
					}

					importer.SetPlatformTextureSettings(s);
				}

				// Write settings without forcing immediate reimport to avoid global serialization issues
				bool wrote = AssetDatabase.WriteImportSettingsIfDirty(path);
				if (wrote) changed++;

				if (ReimportImmediately && wrote)
				{
					try
					{
						AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
						reimported++;
					}
					catch (Exception importEx)
					{
						Debug.LogError($"[OptimizeMobileTextures] Error on {path} during ImportAsset: {importEx.Message}");
					}
				}
			}
			catch (Exception ex)
			{
				Debug.LogWarning($"[OptimizeMobileTextures] Error on {path}: {ex.Message}");
				errors++;
			}
		}

		long finalEstimate = totalAstcBytes;
		Debug.LogFormat("[OptimizeMobileTextures] Done. Changed: {0}, Skipped: {1}, Errors: {2}. Estimated size: {3:0.0} MB",
			changed, skipped, errors, finalEstimate / (1024f * 1024f));

		// If nothing changed, force a step-down to ensure progress
		if (changed == 0)
		{
			int sizeIdx = Array.IndexOf(candidateSizes, chosenMaxSize);
			int blockIdx = Array.IndexOf(candidateBlocks, chosenAstcBlock);
			bool adjusted = false;
			if (sizeIdx >= 0 && sizeIdx < candidateSizes.Length - 1)
			{
				chosenMaxSize = candidateSizes[sizeIdx + 1];
				adjusted = true;
			}
			else if (blockIdx >= 0 && blockIdx < candidateBlocks.Length - 1)
			{
				chosenAstcBlock = candidateBlocks[blockIdx + 1];
				adjusted = true;
			}

			if (adjusted)
			{
				Debug.LogFormat("[OptimizeMobileTextures] No files changed with current plan. Forcing step-down to {0} / ASTC {1}x{1}.", chosenMaxSize, chosenAstcBlock);
				int changed2 = 0, skipped2 = 0, errors2 = 0;
				foreach (var path in assetPaths)
				{
					try
					{
						var importer = AssetImporter.GetAtPath(path) as TextureImporter;
						var tex2D = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
						var cube = AssetDatabase.LoadAssetAtPath<Cubemap>(path);
						if (importer == null || (tex2D == null && cube == null)) { skipped2++; continue; }
						bool isAllow = IsAllowListed(path);
						int origW = tex2D != null ? tex2D.width : cube.width;
						int origH = tex2D != null ? tex2D.height : cube.height;
						foreach (var platform in Platforms)
						{
							var s = importer.GetPlatformTextureSettings(platform) ?? new TextureImporterPlatformSettings();
							s.name = platform;
							s.overridden = true;
							s.maxTextureSize = isAllow ? GetExistingPlatformMaxSize(importer, platform) : chosenMaxSize;
							if (isAllow && (s.maxTextureSize <= 0 || s.maxTextureSize < Math.Max(origW, origH))) s.maxTextureSize = Math.Max(origW, origH);
							s.textureCompression = TextureImporterCompression.Compressed;
							s.compressionQuality = 50;
							s.crunchedCompression = false;
							switch (chosenAstcBlock)
							{
								case 8: s.format = TextureImporterFormat.ASTC_8x8; break;
								case 10: s.format = TextureImporterFormat.ASTC_10x10; break;
								default: s.format = TextureImporterFormat.ASTC_12x12; break;
							}
							importer.SetPlatformTextureSettings(s);
						}
						if (AssetDatabase.WriteImportSettingsIfDirty(path)) changed2++;
					}
					catch (Exception ex)
					{
						Debug.LogWarning($"[OptimizeMobileTextures] Step-down error on {path}: {ex.Message}");
						errors2++;
					}
				}
				Debug.LogFormat("[OptimizeMobileTextures] Step-down pass complete. Changed: {0}, Skipped: {1}, Errors: {2}", changed2, skipped2, errors2);
			}
			else
			{
				Debug.LogWarning("[OptimizeMobileTextures] Already at most aggressive settings. No further changes possible in this pass.");
			}
		}

		// Print summary table
		Debug.Log("[OptimizeMobileTextures] --- Texture Compression Summary ---\n" +
			"Asset Path | Type | Allow | Orig WxH | Orig Size (KB) | New WxH | ASTC (KB)\n" +
			string.Join("\n", summaryRows.Select(row =>
			$"{row.path} | {row.kind} | {(row.allowlisted ? "Y" : "-")} | {row.origW}x{row.origH} | {row.origBytes / 1024} | {row.newW}x{row.newH} | {row.astcBytes / 1024}")) +
			$"\nTOTAL: Buffer={bufferBytes / (1024f * 1024f):0.0} MB, NonTex={nonTextureBytes / (1024f * 1024f):0.0} MB, Tex={totalAstcBytes / (1024f * 1024f):0.0} MB, TOTAL={(bufferBytes + nonTextureBytes + totalAstcBytes) / (1024f * 1024f):0.0} MB");

		if (finalEstimate > MaxBudgetBytes)
			Debug.LogWarningFormat("[OptimizeMobileTextures] Still over 100MB (~{0:0.0} MB). Consider reducing to 128 or ASTC 12x12 and re-run.",
				finalEstimate / (1024f * 1024f));
		else
			Debug.Log("[OptimizeMobileTextures] Estimate within 100MB budget.");
	}

	[MenuItem("Lilithe/Probe Scene Upload Size")]
	public static void ProbeSceneUploadSize()
	{
		// Build a temporary asset bundle of the open scenes using LZ4 to approximate VRChat upload size
		var scenePaths = new List<string>();
		for (int i = 0; i < EditorSceneManager.sceneCount; i++)
		{
			var sc = EditorSceneManager.GetSceneAt(i);
			if (!string.IsNullOrEmpty(sc.path)) scenePaths.Add(sc.path);
		}
		if (scenePaths.Count == 0)
		{
			Debug.LogWarning("[CozyCon Probe] No open scenes found. Open a scene and try again.");
			return;
		}

		var target = EditorUserBuildSettings.activeBuildTarget;
		long bytes = BuildScenesBundleAndGetSize(scenePaths, target);
		if (bytes <= 0)
		{
			Debug.LogWarning("[CozyCon Probe] Probe build produced no output.");
			return;
		}
		Debug.LogFormat("[CozyCon Probe] Active Target: {0}. Approx upload size (bundle): {1:0.00} MB", target, bytes / (1024f * 1024f));
	}

	[MenuItem("Lilithe/Analyze Build Size (Top Assets)")]
	public static void AnalyzeBuildSizeTop()
	{
		var deps = GetOpenSceneDependencyPaths();
		if (deps.Count == 0)
		{
			Debug.LogWarning("[CozyCon Analyze] No open scenes. Open a scene to analyze its dependencies.");
			return;
		}

		var rows = new List<(string path, string category, long bytes)>();
		long total = 0;
		var catTotals = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
		foreach (var p in deps)
		{
			if (p.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)) continue;
			long sz = GetFileSizeBytes(p);
			if (sz <= 0) continue;
			total += sz;
			string cat = CategorizeDependency(p);
			rows.Add((p, cat, sz));
			catTotals[cat] = catTotals.TryGetValue(cat, out var v) ? v + sz : sz;
		}

		var top = rows.OrderByDescending(r => r.bytes).Take(30).ToList();
		string report = "[CozyCon Analyze] Top assets by on-disk size (scene deps)\n" +
			string.Join("\n", top.Select(r => $"{r.bytes / (1024f * 1024f):0.00} MB\t[{r.category}]\t{r.path}")) +
			$"\n-- Category totals --\n" +
			string.Join("\n", catTotals.OrderByDescending(kv => kv.Value).Select(kv => $"{kv.Value / (1024f * 1024f):0.00} MB\t{kv.Key}")) +
			$"\nTOTAL (deps on disk): {total / (1024f * 1024f):0.00} MB";
		Debug.Log(report);

		Debug.Log("[CozyCon Analyze] Tips: Large lighting files (LightingData.asset, Lightmap-*) and ReflectionProbe-* often dominate. Consider Non-Directional lightmaps, High lightmap compression, smaller atlas size, fewer bakes, and reducing/refactoring probes. Prune 'Always Included Shaders' in Graphics Settings and strip unused variants. Long audio to lower quality and 22kHz; mark short SFX mono. Meshes: disable Read/Write, increase compression.");
	}

	private static string CategorizeDependency(string path)
	{
		string file = Path.GetFileName(path);
		if (file.Equals("LightingData.asset", StringComparison.OrdinalIgnoreCase)) return "LightingData";
		if (file.Equals("OcclusionCullingData.asset", StringComparison.OrdinalIgnoreCase)) return "Occlusion";
		if (file.StartsWith("Lightmap-", StringComparison.OrdinalIgnoreCase)) return "Lightmap";
		if (file.StartsWith("ReflectionProbe-", StringComparison.OrdinalIgnoreCase)) return "ReflectionProbe";
		var t = AssetDatabase.GetMainAssetTypeAtPath(path);
		if (t == typeof(UnityEngine.AudioClip)) return "Audio";
		if (t == typeof(Texture2D) || t == typeof(Cubemap)) return "Texture";
		if (t == typeof(Mesh)) return "Mesh";
		if (t == typeof(Material)) return "Material";
		if (t == typeof(Shader)) return "Shader";
		if (t == typeof(RuntimeAnimatorController) || t == typeof(AnimationClip)) return "Animation";
		if (t == typeof(GameObject)) return "Prefab";
		return t != null ? t.Name : "Other";
	}

	[MenuItem("Lilithe/Optimize Audio for Size")]
	public static void OptimizeAudio()
	{
		var deps = GetOpenSceneDependencyPaths();
		List<string> paths = deps.Count > 0 ? deps : AssetDatabase.FindAssets("t:AudioClip").Select(AssetDatabase.GUIDToAssetPath).ToList();
		int changed = 0, skipped = 0;
		foreach (var p in paths.Distinct())
		{
			var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(p);
			var imp = AssetImporter.GetAtPath(p) as AudioImporter;
			if (clip == null || imp == null) { skipped++; continue; }
			bool allow = IsAllowListed(p);
			float len = clip.length;
			var def = imp.defaultSampleSettings;
			def.compressionFormat = AudioCompressionFormat.Vorbis;
			def.loadType = len > 30f ? AudioClipLoadType.Streaming : AudioClipLoadType.CompressedInMemory;
			def.quality = len > 120f ? 0.3f : (len > 30f ? 0.35f : 0.45f);
			def.sampleRateSetting = AudioSampleRateSetting.OverrideSampleRate;
			def.sampleRateOverride = (uint)(len > 120f ? 22050 : 44100);
			imp.defaultSampleSettings = def;
			imp.forceToMono = !allow && len <= 10f;
			def.preloadAudioData = len <= 30f;
			// Apply to Android/iPhone overrides
			foreach (var platform in Platforms)
			{
				imp.SetOverrideSampleSettings(platform, def);
			}
			if (AssetDatabase.WriteImportSettingsIfDirty(p)) changed++;
		}
		Debug.Log($"[CozyCon Audio] Changed: {changed}, Skipped: {skipped}.");
	}

	[MenuItem("Lilithe/Optimize Models for Size")]
	public static void OptimizeModels()
	{
		var deps = GetOpenSceneDependencyPaths();
		List<string> paths = deps.Count > 0 ? deps : AssetDatabase.FindAssets("t:Model").Select(AssetDatabase.GUIDToAssetPath).ToList();
		int changed = 0, skipped = 0;
		foreach (var p in paths.Distinct())
		{
			var imp = AssetImporter.GetAtPath(p) as ModelImporter;
			if (imp == null) { skipped++; continue; }
			bool allow = IsAllowListed(p);
			bool any = false;
			if (!allow && imp.isReadable) { imp.isReadable = false; any = true; }
			var targetCompression = allow ? imp.meshCompression : ModelImporterMeshCompression.Medium;
			if (imp.meshCompression != targetCompression) { imp.meshCompression = targetCompression; any = true; }
			if (any && AssetDatabase.WriteImportSettingsIfDirty(p)) changed++; else skipped++;
		}
		Debug.Log($"[CozyCon Models] Changed: {changed}, Skipped: {skipped}.");
	}

	private static long BuildScenesBundleAndGetSize(List<string> scenePaths, BuildTarget target)
	{
		try
		{
			// Use a safe path outside Library to satisfy VRChat restrictions
			string projectRoot = Directory.GetParent(Application.dataPath).FullName;
			string outDir = Path.Combine(projectRoot, "Temp", "CozyConProbe", target.ToString());
			if (Directory.Exists(outDir)) Directory.Delete(outDir, true);
			Directory.CreateDirectory(outDir);

			var build = new AssetBundleBuild
			{
				assetBundleName = "cozycon_probe",
				assetNames = scenePaths.ToArray()
			};

			var manifest = BuildPipeline.BuildAssetBundles(
				outDir,
				new[] { build },
				BuildAssetBundleOptions.ChunkBasedCompression | BuildAssetBundleOptions.ForceRebuildAssetBundle | BuildAssetBundleOptions.StrictMode,
				target);
			if (manifest == null)
				return 0;

			string bundlePath = Path.Combine(outDir, "cozycon_probe");
			if (!File.Exists(bundlePath))
				return 0;
			return new FileInfo(bundlePath).Length;
		}
		catch (Exception ex)
		{
			Debug.LogWarning("[CozyCon Probe] Build error: " + ex.Message);
			return 0;
		}
	}

	private static long EstimateTotalAstcBytes(List<string> assetPaths, int maxSize, int astcBlock)
	{
		long total = 0;
		foreach (var path in assetPaths)
		{
			var tex2D = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
			var cube = AssetDatabase.LoadAssetAtPath<Cubemap>(path);
			bool isCube = cube != null;
			if (tex2D == null && cube == null) continue;

			int w = isCube ? cube.width : tex2D.width;
			int h = isCube ? cube.height : tex2D.height;
			int targetMax = maxSize;
			if (IsAllowListed(path))
			{
				// For allowlisted textures, approximate using original size (no downscale)
				targetMax = Math.Max(w, h);
			}
			if (w >= h)
			{
				if (w > targetMax) { float s = (float)targetMax / w; w = targetMax; h = Mathf.Max(1, Mathf.RoundToInt(h * s)); }
			}
			else
			{
				if (h > targetMax) { float s = (float)targetMax / h; h = targetMax; w = Mathf.Max(1, Mathf.RoundToInt(w * s)); }
			}

			int blocksX = (w + astcBlock - 1) / astcBlock;
			int blocksY = (h + astcBlock - 1) / astcBlock;
			long bytes = (long)blocksX * blocksY * 16L;
			if (isCube) bytes *= 6;
			bytes = (long)(bytes * 1.2f); // mip overhead estimate
			total += bytes;
		}
		return total;
	}

	private static bool IsAllowListed(string assetPath)
	{
		// Label-based allow
		var labels = AssetDatabase.GetLabels(AssetDatabase.LoadMainAssetAtPath(assetPath));
		if (labels != null && labels.Any(l => string.Equals(l, AllowListLabel, StringComparison.OrdinalIgnoreCase))) return true;
		// Path-based allow
		foreach (var token in AllowListPathContains)
		{
			if (!string.IsNullOrEmpty(token) && assetPath.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0)
				return true;
		}
		return false;
	}

	private static int GetExistingPlatformMaxSize(TextureImporter importer, string platform)
	{
		var s = importer.GetPlatformTextureSettings(platform);
		if (s != null && s.overridden && s.maxTextureSize > 0) return s.maxTextureSize;
		return 0;
	}

	private static List<string> GetOpenSceneTexturePaths()
	{
		var deps = GetOpenSceneDependencyPaths();
		var list = new List<string>();
		foreach (var p in deps)
		{
			var t = AssetDatabase.GetMainAssetTypeAtPath(p);
			if (t == typeof(Texture2D) || t == typeof(Cubemap)) list.Add(p);
		}
		return list.Distinct().ToList();
	}

	private static List<string> GetOpenSceneDependencyPaths()
	{
		var scenePaths = new List<string>();
		for (int i = 0; i < EditorSceneManager.sceneCount; i++)
		{
			var sc = EditorSceneManager.GetSceneAt(i);
			if (sc.path != null && sc.path.Length > 0) scenePaths.Add(sc.path);
		}
		if (scenePaths.Count == 0) return new List<string>();
		var deps = AssetDatabase.GetDependencies(scenePaths.ToArray(), true);
		return deps != null ? deps.Distinct().ToList() : new List<string>();
	}

	private static long GetFileSizeBytes(string assetPath)
	{
		try
		{
			if (string.IsNullOrEmpty(assetPath)) return 0;
			if (!assetPath.StartsWith("Assets")) return 0;
			string projectRoot = Directory.GetParent(Application.dataPath).FullName;
			string fullPath = Path.Combine(projectRoot, assetPath).Replace('/', Path.DirectorySeparatorChar);
			if (!File.Exists(fullPath)) return 0;
			var fi = new FileInfo(fullPath);
			return fi.Exists ? fi.Length : 0;
		}
		catch { return 0; }
	}

	// ===== Mobile Texture Max Size preference & menu =====
	private static int GetPreferredMaxSize()
	{
		return EditorPrefs.GetInt(MaxSizePrefsKey, DefaultMaxSizePref);
	}

	private static void SetPreferredMaxSize(int size)
	{
		EditorPrefs.SetInt(MaxSizePrefsKey, size);
		Debug.Log(size > 0
			? $"[CozyCon] Mobile Texture Max Size preference set to {size}"
			: "[CozyCon] Mobile Texture Max Size preference set to Auto");
	}

	[MenuItem("Lilithe/Mobile Texture Max Size/Auto", false, 20)]
	public static void Menu_MaxSize_Auto()
	{
		SetPreferredMaxSize(-1);
	}
	[MenuItem("Lilithe/Mobile Texture Max Size/Auto", true)]
	public static bool MenuValidate_MaxSize_Auto()
	{
		Menu.SetChecked("Lilithe/Mobile Texture Max Size/Auto", GetPreferredMaxSize() <= 0);
		return true;
	}

	[MenuItem("Lilithe/Mobile Texture Max Size/4096", false, 21)]
	public static void Menu_MaxSize_4096() => SetPreferredMaxSize(4096);
	[MenuItem("Lilithe/Mobile Texture Max Size/4096", true)]
	public static bool MenuValidate_MaxSize_4096() { Menu.SetChecked("Lilithe/Mobile Texture Max Size/4096", GetPreferredMaxSize() == 4096); return true; }

	[MenuItem("Lilithe/Mobile Texture Max Size/2048", false, 22)]
	public static void Menu_MaxSize_2048() => SetPreferredMaxSize(2048);
	[MenuItem("Lilithe/Mobile Texture Max Size/2048", true)]
	public static bool MenuValidate_MaxSize_2048() { Menu.SetChecked("Lilithe/Mobile Texture Max Size/2048", GetPreferredMaxSize() == 2048); return true; }

	[MenuItem("Lilithe/Mobile Texture Max Size/1024", false, 23)]
	public static void Menu_MaxSize_1024() => SetPreferredMaxSize(1024);
	[MenuItem("Lilithe/Mobile Texture Max Size/1024", true)]
	public static bool MenuValidate_MaxSize_1024() { Menu.SetChecked("Lilithe/Mobile Texture Max Size/1024", GetPreferredMaxSize() == 1024); return true; }

	[MenuItem("Lilithe/Mobile Texture Max Size/512", false, 24)]
	public static void Menu_MaxSize_512() => SetPreferredMaxSize(512);
	[MenuItem("Lilithe/Mobile Texture Max Size/512", true)]
	public static bool MenuValidate_MaxSize_512() { Menu.SetChecked("Lilithe/Mobile Texture Max Size/512", GetPreferredMaxSize() == 512); return true; }

	[MenuItem("Lilithe/Mobile Texture Max Size/256", false, 25)]
	public static void Menu_MaxSize_256() => SetPreferredMaxSize(256);
	[MenuItem("Lilithe/Mobile Texture Max Size/256", true)]
	public static bool MenuValidate_MaxSize_256() { Menu.SetChecked("Lilithe/Mobile Texture Max Size/256", GetPreferredMaxSize() == 256); return true; }

	[MenuItem("Lilithe/Mobile Texture Max Size/128", false, 26)]
	public static void Menu_MaxSize_128() => SetPreferredMaxSize(128);
	[MenuItem("Lilithe/Mobile Texture Max Size/128", true)]
	public static bool MenuValidate_MaxSize_128() { Menu.SetChecked("Lilithe/Mobile Texture Max Size/128", GetPreferredMaxSize() == 128); return true; }

	// ===== Upload buffer helpers =====
	private static long GetUploadBufferBytes()
	{
		return (long)GetUploadBufferMB() * 1024L * 1024L;
	}

	private static int GetUploadBufferMB()
	{
		return EditorPrefs.GetInt(BufferPrefsKey, DefaultBufferMB);
	}

	private static void SetUploadBufferMB(int mb)
	{
		EditorPrefs.SetInt(BufferPrefsKey, Mathf.Max(0, mb));
		Debug.Log($"[CozyCon] Upload buffer set to {mb} MB");
	}

	[MenuItem("Lilithe/Upload Buffer/Show Current", false, 0)]
	public static void ShowUploadBuffer()
	{
		Debug.Log($"[CozyCon] Current upload buffer: {GetUploadBufferMB()} MB");
	}

	[MenuItem("Lilithe/Upload Buffer/0 MB", false, 10)]
	public static void SetBuffer0()
	{
		SetUploadBufferMB(0);
	}

	[MenuItem("Lilithe/Upload Buffer/5 MB", false, 11)]
	public static void SetBuffer5()
	{
		SetUploadBufferMB(5);
	}

	[MenuItem("Lilithe/Upload Buffer/10 MB (Default)", false, 12)]
	public static void SetBuffer10()
	{
		SetUploadBufferMB(10);
	}

	[MenuItem("Lilithe/Upload Buffer/20 MB", false, 13)]
	public static void SetBuffer20()
	{
		SetUploadBufferMB(20);
	}

	[MenuItem("Lilithe/Upload Buffer/50 MB", false, 14)]
	public static void SetBuffer50()
	{
		SetUploadBufferMB(50);
	}
}