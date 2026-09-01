using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using System.Text;

// CozyCon: Analyze scene objects and materials for RAM usage to identify memory-heavy assets
public static class SceneMemoryAnalyzer
{
	private const string MenuPath = "Lilithe/Analyze Scene Memory Usage";

	[MenuItem("Lilithe/Test Debug Output")]
	public static void TestDebugOutput()
	{
		Debug.Log("[SceneMemoryAnalyzer] Test debug output - this should appear in console!");
		Debug.LogWarning("[SceneMemoryAnalyzer] Test warning output");
		Debug.LogError("[SceneMemoryAnalyzer] Test error output");
	}

	[MenuItem(MenuPath)]
	public static void AnalyzeSceneMemory()
	{
		Debug.Log("[SceneMemoryAnalyzer] Starting analysis...");

		var results = new List<MemoryAnalysisResult>();

		// Get all dependencies from open scenes
		var sceneDeps = GetOpenSceneDependencyPaths();

		Debug.Log($"[SceneMemoryAnalyzer] Found {sceneDeps.Count} scene dependencies");

		if (sceneDeps.Count == 0)
		{
			Debug.LogWarning("[SceneMemoryAnalyzer] No open scenes found. Open a scene and try again.");

			// Let's also check what scenes are actually open
			Debug.Log($"[SceneMemoryAnalyzer] Scene count: {EditorSceneManager.sceneCount}");
			for (int i = 0; i < EditorSceneManager.sceneCount; i++)
			{
				var scene = EditorSceneManager.GetSceneAt(i);
				Debug.Log($"[SceneMemoryAnalyzer] Scene {i}: {scene.name}, Path: '{scene.path}', Loaded: {scene.isLoaded}");
			}
			return;
		}

		Debug.Log($"[SceneMemoryAnalyzer] Analyzing {sceneDeps.Count} scene dependencies...");

		// Track analysis progress
		int processed = 0;

		// Analyze each dependency
		foreach (var assetPath in sceneDeps)
		{
			if (assetPath.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)) continue;

			processed++;
			if (processed % 50 == 0) // Progress indicator
			{
				Debug.Log($"[SceneMemoryAnalyzer] Progress: {processed}/{sceneDeps.Count} assets analyzed...");
			}

			var result = AnalyzeAsset(assetPath);
			if (result != null && result.EstimatedRAMBytes > 0)
			{
				results.Add(result);
			}
		}

		// Sort by RAM usage (largest first)
		results = results.OrderByDescending(r => r.EstimatedRAMBytes).ToList();

		// Output to console
		OutputToConsole(results);

		// Show results window
		SceneMemoryAnalyzerWindow.ShowWindow(results);

		Debug.Log($"[SceneMemoryAnalyzer] Analysis complete! Found {results.Count} memory-consuming assets.");
	}

	[MenuItem("Lilithe/Quick Memory Summary")]
	public static void QuickMemorySummary()
	{
		var sceneDeps = GetOpenSceneDependencyPaths();
		if (sceneDeps.Count == 0)
		{
			Debug.LogWarning("[SceneMemoryAnalyzer] No open scenes found.");
			return;
		}

		var categories = new Dictionary<string, (int count, long totalBytes)>();

		foreach (var assetPath in sceneDeps)
		{
			if (assetPath.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)) continue;

			var result = AnalyzeAsset(assetPath);
			if (result != null && result.EstimatedRAMBytes > 0)
			{
				if (!categories.ContainsKey(result.AssetType))
					categories[result.AssetType] = (0, 0);

				var current = categories[result.AssetType];
				categories[result.AssetType] = (current.count + 1, current.totalBytes + result.EstimatedRAMBytes);
			}
		}

		Debug.Log("[SceneMemoryAnalyzer] Quick Memory Summary by Asset Type:");
		foreach (var kvp in categories.OrderByDescending(x => x.Value.totalBytes))
		{
			Debug.Log($"  {kvp.Key}: {kvp.Value.count} assets, {kvp.Value.totalBytes / (1024f * 1024f):F1} MB");
		}

		long total = categories.Values.Sum(x => x.totalBytes);
		Debug.Log($"  TOTAL: {total / (1024f * 1024f):F1} MB estimated RAM usage");
	}

	private static MemoryAnalysisResult AnalyzeAsset(string assetPath)
	{
		try
		{
			var mainAsset = AssetDatabase.LoadMainAssetAtPath(assetPath);
			if (mainAsset == null) return null;

			var assetType = mainAsset.GetType();
			long ramBytes = 0;
			string details = "";

			// Analyze different asset types
			if (assetType == typeof(Texture2D))
			{
				var result = AnalyzeTexture2D(assetPath, mainAsset as Texture2D);
				ramBytes = result.ramBytes;
				details = result.details;
			}
			else if (assetType == typeof(Cubemap))
			{
				var result = AnalyzeCubemap(assetPath, mainAsset as Cubemap);
				ramBytes = result.ramBytes;
				details = result.details;
			}
			else if (assetType == typeof(Mesh))
			{
				var result = AnalyzeMesh(assetPath, mainAsset as Mesh);
				ramBytes = result.ramBytes;
				details = result.details;
			}
			else if (assetType == typeof(AudioClip))
			{
				var result = AnalyzeAudioClip(assetPath, mainAsset as AudioClip);
				ramBytes = result.ramBytes;
				details = result.details;
			}
			else if (assetType == typeof(Material))
			{
				var result = AnalyzeMaterial(assetPath, mainAsset as Material);
				ramBytes = result.ramBytes;
				details = result.details;
			}
			else if (assetType == typeof(Shader))
			{
				var result = AnalyzeShader(assetPath, mainAsset as Shader);
				ramBytes = result.ramBytes;
				details = result.details;
			}
			else if (assetPath.Contains("LightingData.asset"))
			{
				ramBytes = GetFileSizeBytes(assetPath); // Approximate RAM as file size for lighting data
				details = "Lighting data asset";
			}
			else if (assetPath.Contains("Lightmap-") || assetPath.Contains("ReflectionProbe-"))
			{
				ramBytes = GetFileSizeBytes(assetPath);
				details = assetPath.Contains("Lightmap-") ? "Lightmap texture" : "Reflection probe";
			}

			if (ramBytes > 0)
			{
				return new MemoryAnalysisResult
				{
					AssetPath = assetPath,
					AssetType = assetType.Name,
					EstimatedRAMBytes = ramBytes,
					Details = details
				};
			}

			return null;
		}
		catch (Exception ex)
		{
			Debug.LogWarning($"[SceneMemoryAnalyzer] Error analyzing {assetPath}: {ex.Message}");
			return null;
		}
	}

	private static (long ramBytes, string details) AnalyzeTexture2D(string assetPath, Texture2D texture)
	{
		if (texture == null) return (0, "");

		var importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
		int width = texture.width;
		int height = texture.height;
		bool hasMips = texture.mipmapCount > 1;

		// Estimate bytes based on format and size
		long ramBytes = EstimateTextureRAM(texture.format, width, height, hasMips);

		string compression = importer?.textureCompression.ToString() ?? "Unknown";
		string details = $"{width}x{height}, {texture.format}, Mips: {texture.mipmapCount}, Compression: {compression}";

		return (ramBytes, details);
	}

	private static (long ramBytes, string details) AnalyzeCubemap(string assetPath, Cubemap cubemap)
	{
		if (cubemap == null) return (0, "");

		int faceSize = cubemap.width;
		bool hasMips = cubemap.mipmapCount > 1;

		// Cubemap has 6 faces
		long ramBytes = EstimateTextureRAM(cubemap.format, faceSize, faceSize, hasMips) * 6;

		string details = $"Cubemap {faceSize}x{faceSize}, {cubemap.format}, 6 faces, Mips: {cubemap.mipmapCount}";

		return (ramBytes, details);
	}

	private static (long ramBytes, string details) AnalyzeMesh(string assetPath, Mesh mesh)
	{
		if (mesh == null) return (0, "");

		int vertices = mesh.vertexCount;
		int triangles = mesh.triangles.Length / 3;

		// Estimate RAM: vertices * (position + normal + UV + color + other attributes)
		// Rough estimate: 32-48 bytes per vertex depending on attributes
		long vertexDataBytes = vertices * 40L; // Average estimate

		// Add index buffer (usually 16-bit or 32-bit indices)
		long indexBytes = mesh.triangles.Length * (vertices > 65535 ? 4L : 2L);

		long ramBytes = vertexDataBytes + indexBytes;

		string details = $"{vertices} vertices, {triangles} triangles, {mesh.subMeshCount} submeshes";

		return (ramBytes, details);
	}

	private static (long ramBytes, string details) AnalyzeAudioClip(string assetPath, AudioClip clip)
	{
		if (clip == null) return (0, "");

		var importer = AssetImporter.GetAtPath(assetPath) as AudioImporter;

		// Estimate uncompressed size: samples * channels * bytes_per_sample
		int channels = clip.channels;
		int frequency = clip.frequency;
		float length = clip.length;

		// Assume 16-bit samples for estimation
		long uncompressedBytes = (long)(frequency * channels * length * 2);

		// Apply compression factor based on import settings
		float compressionFactor = 1.0f;
		if (importer != null)
		{
			var settings = importer.defaultSampleSettings;
			switch (settings.compressionFormat)
			{
				case AudioCompressionFormat.PCM:
					compressionFactor = 1.0f;
					break;
				case AudioCompressionFormat.Vorbis:
					compressionFactor = settings.quality * 0.3f + 0.1f; // Rough estimate
					break;
				case AudioCompressionFormat.MP3:
					compressionFactor = 0.1f; // Rough estimate
					break;
				case AudioCompressionFormat.ADPCM:
					compressionFactor = 0.25f;
					break;
			}
		}

		long ramBytes = (long)(uncompressedBytes * compressionFactor);

		string loadType = importer?.defaultSampleSettings.loadType.ToString() ?? "Unknown";
		string compression = importer?.defaultSampleSettings.compressionFormat.ToString() ?? "Unknown";
		string details = $"{length:F1}s, {channels}ch, {frequency}Hz, {compression}, {loadType}";

		return (ramBytes, details);
	}

	private static (long ramBytes, string details) AnalyzeMaterial(string assetPath, Material material)
	{
		if (material == null) return (0, "");

		// Materials themselves don't use much RAM, but let's estimate based on properties
		long ramBytes = 1024; // Base material overhead

		// Add estimation for texture references (though textures are counted separately)
		int texturePropertyCount = 0;
		var shader = material.shader;
		if (shader != null)
		{
			for (int i = 0; i < ShaderUtil.GetPropertyCount(shader); i++)
			{
				if (ShaderUtil.GetPropertyType(shader, i) == ShaderUtil.ShaderPropertyType.TexEnv)
				{
					texturePropertyCount++;
				}
			}
		}

		ramBytes += texturePropertyCount * 64; // Small overhead per texture property

		string shaderName = shader != null ? shader.name : "Unknown";
		string details = $"Shader: {shaderName}, {texturePropertyCount} texture properties";

		return (ramBytes, details);
	}

	private static (long ramBytes, string details) AnalyzeShader(string assetPath, Shader shader)
	{
		if (shader == null) return (0, "");

		// Shaders can vary widely in size, estimate based on variants and complexity
		long ramBytes = 4096; // Base shader overhead

		// Try to get more info about the shader
		string details = $"Shader: {shader.name}";

		// Add estimation based on number of passes and properties
		int propertyCount = ShaderUtil.GetPropertyCount(shader);
		ramBytes += propertyCount * 16;

		details += $", {propertyCount} properties";

		return (ramBytes, details);
	}

	private static long EstimateTextureRAM(TextureFormat format, int width, int height, bool hasMips)
	{
		// Check if it's a compressed format that needs special handling
		if (IsCompressedFormat(format))
		{
			return EstimateCompressedTextureRAM(format, width, height, hasMips);
		}

		int bytesPerPixel = GetBytesPerPixel(format);
		long baseBytes = (long)width * height * bytesPerPixel;

		// Add mipmap overhead (roughly 33% more)
		if (hasMips)
		{
			baseBytes = (long)(baseBytes * 1.33f);
		}

		return baseBytes;
	}

	private static bool IsCompressedFormat(TextureFormat format)
	{
		switch (format)
		{
			case TextureFormat.DXT1:
			case TextureFormat.DXT1Crunched:
			case TextureFormat.DXT5:
			case TextureFormat.DXT5Crunched:
			case TextureFormat.BC4:
			case TextureFormat.BC5:
			case TextureFormat.BC6H:
			case TextureFormat.BC7:
			case TextureFormat.ASTC_4x4:
			case TextureFormat.ASTC_5x5:
			case TextureFormat.ASTC_6x6:
			case TextureFormat.ASTC_8x8:
			case TextureFormat.ASTC_10x10:
			case TextureFormat.ASTC_12x12:
				return true;
			default:
				return false;
		}
	}

	private static int GetBytesPerPixel(TextureFormat format)
	{
		switch (format)
		{
			case TextureFormat.RGBA32:
			case TextureFormat.ARGB32:
			case TextureFormat.BGRA32:
				return 4;
			case TextureFormat.RGB24:
				return 3;
			case TextureFormat.RGBA4444:
			case TextureFormat.ARGB4444:
			case TextureFormat.RGB565:
				return 2;
			case TextureFormat.Alpha8:
			case TextureFormat.R8:
				return 1;
			case TextureFormat.RGBAFloat:
				return 16;
			case TextureFormat.RGBAHalf:
				return 8;
			case TextureFormat.RGFloat:
				return 8;
			case TextureFormat.RGHalf:
				return 4;
			case TextureFormat.RFloat:
				return 4;
			case TextureFormat.RHalf:
				return 2;
			// Compressed formats - more accurate calculations
			case TextureFormat.DXT1:
			case TextureFormat.DXT1Crunched:
				return 0; // Special case - handled separately (4 bits per pixel = 0.5 bytes)
			case TextureFormat.DXT5:
			case TextureFormat.DXT5Crunched:
				return 1; // 8 bits per pixel = 1 byte
			case TextureFormat.BC4:
				return 0; // 4 bits per pixel = 0.5 bytes
			case TextureFormat.BC5:
			case TextureFormat.BC6H:
			case TextureFormat.BC7:
				return 1; // 8 bits per pixel = 1 byte
			case TextureFormat.ASTC_4x4:
				return 1; // 8 bits per pixel
			case TextureFormat.ASTC_5x5:
				return 0; // ~5.12 bits per pixel
			case TextureFormat.ASTC_6x6:
				return 0; // ~3.56 bits per pixel
			case TextureFormat.ASTC_8x8:
				return 0; // 2 bits per pixel
			case TextureFormat.ASTC_10x10:
				return 0; // ~1.28 bits per pixel
			case TextureFormat.ASTC_12x12:
				return 0; // ~0.89 bits per pixel
			default:
				return 4; // Default to 4 bytes per pixel for unknown formats
		}
	}

	private static long EstimateCompressedTextureRAM(TextureFormat format, int width, int height, bool hasMips)
	{
		long baseBytes = 0;

		switch (format)
		{
			case TextureFormat.DXT1:
			case TextureFormat.DXT1Crunched:
			case TextureFormat.BC4:
				// 4 bits per pixel
				baseBytes = (long)width * height / 2;
				break;
			case TextureFormat.DXT5:
			case TextureFormat.DXT5Crunched:
			case TextureFormat.BC5:
			case TextureFormat.BC6H:
			case TextureFormat.BC7:
				// 8 bits per pixel
				baseBytes = (long)width * height;
				break;
			case TextureFormat.ASTC_4x4:
				baseBytes = (long)width * height; // 8 bits per pixel
				break;
			case TextureFormat.ASTC_5x5:
				baseBytes = (long)width * height * 5 / 8; // ~5.12 bits per pixel
				break;
			case TextureFormat.ASTC_6x6:
				baseBytes = (long)width * height * 4 / 9; // ~3.56 bits per pixel
				break;
			case TextureFormat.ASTC_8x8:
				baseBytes = (long)width * height / 4; // 2 bits per pixel
				break;
			case TextureFormat.ASTC_10x10:
				baseBytes = (long)width * height * 16 / 100; // ~1.28 bits per pixel
				break;
			case TextureFormat.ASTC_12x12:
				baseBytes = (long)width * height * 16 / 144; // ~0.89 bits per pixel
				break;
			default:
				// Fall back to regular calculation
				return EstimateTextureRAM(format, width, height, hasMips);
		}

		// Add mipmap overhead (roughly 33% more)
		if (hasMips)
		{
			baseBytes = (long)(baseBytes * 1.33f);
		}

		return baseBytes;
	}

	private static void OutputToConsole(List<MemoryAnalysisResult> results)
	{
		if (results.Count == 0)
		{
			Debug.Log("[SceneMemoryAnalyzer] No memory-consuming assets found in scene.");
			return;
		}

		var sb = new StringBuilder();
		sb.AppendLine("[SceneMemoryAnalyzer] Scene Memory Analysis Results:");
		sb.AppendLine("Size (MB) | Type | Asset Path | Details");
		sb.AppendLine(new string('-', 120));

		long totalRAM = 0;
		foreach (var result in results)
		{
			totalRAM += result.EstimatedRAMBytes;
			sb.AppendLine($"{result.EstimatedRAMBytes / (1024f * 1024f):F2} MB | {result.AssetType} | {result.AssetPath} | {result.Details}");
		}

		sb.AppendLine(new string('-', 120));
		sb.AppendLine($"TOTAL ESTIMATED RAM: {totalRAM / (1024f * 1024f):F2} MB");
		sb.AppendLine($"Analyzed {results.Count} assets with memory usage.");

		Debug.Log(sb.ToString());
	}

	private static List<string> GetOpenSceneDependencyPaths()
	{
		var scenePaths = new List<string>();
		for (int i = 0; i < EditorSceneManager.sceneCount; i++)
		{
			var scene = EditorSceneManager.GetSceneAt(i);
			if (!string.IsNullOrEmpty(scene.path))
			{
				scenePaths.Add(scene.path);
			}
		}

		if (scenePaths.Count == 0) return new List<string>();

		var dependencies = AssetDatabase.GetDependencies(scenePaths.ToArray(), true);
		return dependencies?.Distinct().ToList() ?? new List<string>();
	}

	private static long GetFileSizeBytes(string assetPath)
	{
		try
		{
			if (string.IsNullOrEmpty(assetPath) || !assetPath.StartsWith("Assets")) return 0;

			string projectRoot = System.IO.Directory.GetParent(Application.dataPath).FullName;
			string fullPath = System.IO.Path.Combine(projectRoot, assetPath).Replace('/', System.IO.Path.DirectorySeparatorChar);

			if (!System.IO.File.Exists(fullPath)) return 0;

			var fileInfo = new System.IO.FileInfo(fullPath);
			return fileInfo.Exists ? fileInfo.Length : 0;
		}
		catch
		{
			return 0;
		}
	}
}

public class MemoryAnalysisResult
{
	public string AssetPath { get; set; }
	public string AssetType { get; set; }
	public long EstimatedRAMBytes { get; set; }
	public string Details { get; set; }
}