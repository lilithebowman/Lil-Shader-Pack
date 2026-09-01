using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using System.Text;

// CozyCon: Analyze scene for Quest 2 compatibility issues based on VRChat's actual limitations
public static class Quest2CompatibilityAnalyzer
{
	private const string MenuPath = "Lilithe/Analyze Quest 2 Compatibility";

	// VRChat Quest 2 specific limits (updated based on official VRChat documentation)
	private const long QUEST_WORLD_SIZE_LIMIT = 100L * 1024L * 1024L; // 100 MB world download size
	private const long QUEST_AVATAR_SIZE_LIMIT = 10L * 1024L * 1024L; // 10 MB avatar download size
	private const int QUEST_MAX_TRIANGLES = 200000; // Conservative estimate for worlds
	private const int QUEST_MAX_MATERIALS = 50; // Recommended for performance
	private const int QUEST_MAX_LIGHTS = 4; // Real-time lights maximum
	private const int QUEST_MAX_TEXTURE_SIZE = 2048; // Technical limit
	private const int QUEST_PREFERRED_TEXTURE_SIZE = 1024; // VRChat recommendation (1K textures)
	private const int QUEST_OPTIMAL_TEXTURE_SIZE = 512; // For best performance

	// VRChat Quest specific restrictions
	private static readonly string[] QUEST_INCOMPATIBLE_SHADERS = {
		"Standard", "Standard (Specular setup)",
		"Autodesk Interactive", "HDRP/", "URP/Lit",
		"Legacy Shaders/", "UI/", "Sprites/",
		"Unlit/Texture", "Skybox/"
	};

	private static readonly string[] QUEST_COMPATIBLE_SHADERS = {
		"VRChat/Mobile/Standard Lite", "VRChat/Mobile/Bumped Diffuse",
		"VRChat/Mobile/Bumped Mapped Specular", "VRChat/Mobile/Diffuse",
		"VRChat/Mobile/MatCap Lit", "VRChat/Mobile/Toon Lit",
		"VRChat/Mobile/Particles/", "VRChat/Mobile/Skybox",
		"VRChat/Mobile/Lightmapped", "VRChat/Mobile/VertexLit",
		"Silent/Filamented"  // Popular third-party mobile shader known to work well on Quest
	};

	[MenuItem(MenuPath)]
	public static void AnalyzeQuest2Compatibility()
	{
		Debug.Log("[Quest2Analyzer] Starting Quest 2 compatibility analysis...");

		var issues = new List<Quest2Issue>();
		var warnings = new List<Quest2Issue>();
		var info = new List<Quest2Issue>();

		// Check memory usage
		CheckMemoryUsage(issues, warnings);

		// Check geometry complexity
		CheckGeometryComplexity(issues, warnings);

		// Check lighting setup
		CheckLightingSetup(issues, warnings, info);

		// Check materials and shaders
		CheckMaterialsAndShaders(issues, warnings, info);

		// Check textures
		CheckTextureSettings(issues, warnings, info);

		// Check audio settings
		CheckAudioSettings(warnings, info);

		// Check post-processing and effects
		CheckPostProcessingEffects(issues, warnings, info);

		// Check scene complexity
		CheckSceneComplexity(warnings, info);

		// Output results
		OutputResults(issues, warnings, info);

		// Show results window
		Quest2CompatibilityWindow.ShowWindow(issues, warnings, info);
	}

	private static void CheckMemoryUsage(List<Quest2Issue> issues, List<Quest2Issue> warnings)
	{
		// Use our existing memory analyzer
		var sceneDeps = GetOpenSceneDependencyPaths();
		long totalMemory = 0;

		foreach (var assetPath in sceneDeps)
		{
			if (assetPath.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)) continue;

			var mainAsset = AssetDatabase.LoadMainAssetAtPath(assetPath);
			if (mainAsset == null) continue;

			// Simplified memory estimation for Quest check
			if (mainAsset is Texture2D tex2D)
			{
				totalMemory += EstimateTextureMemoryForQuest(tex2D);
			}
			else if (mainAsset is Cubemap cube)
			{
				totalMemory += EstimateTextureMemoryForQuest(cube.width, cube.height, cube.format) * 6;
			}
			else if (mainAsset is Mesh mesh)
			{
				totalMemory += mesh.vertexCount * 40L; // Rough estimate
			}
			else if (mainAsset is AudioClip clip)
			{
				totalMemory += (long)(clip.frequency * clip.channels * clip.length * 2 * 0.3f); // Compressed estimate
			}
		}

		if (totalMemory > QUEST_WORLD_SIZE_LIMIT)
		{
			issues.Add(new Quest2Issue
			{
				Category = "Memory",
				Severity = IssueSeverity.Critical,
				Title = "World Size Exceeds VRChat Quest 2 Limit",
				Description = $"Estimated download size: {totalMemory / (1024f * 1024f):F1} MB (VRChat Limit: {QUEST_WORLD_SIZE_LIMIT / (1024f * 1024f)} MB)",
				Recommendation = "Use 'Optimize Mobile Textures' tool, reduce texture sizes, compress audio, or remove non-essential assets. VRChat enforces a strict 100MB limit for Quest worlds."
			});
		}
		else if (totalMemory > QUEST_WORLD_SIZE_LIMIT * 0.8f)
		{
			warnings.Add(new Quest2Issue
			{
				Category = "Memory",
				Severity = IssueSeverity.Warning,
				Title = "Memory Usage Close to Quest 2 Limit",
				Description = $"Estimated memory usage: {totalMemory / (1024f * 1024f):F1} MB (80% of limit)",
				Recommendation = "Consider optimizing textures and audio to provide headroom for Quest 2 users."
			});
		}
	}

	private static void CheckGeometryComplexity(List<Quest2Issue> issues, List<Quest2Issue> warnings)
	{
		var sceneDeps = GetOpenSceneDependencyPaths();
		int totalTriangles = 0;
		var highPolyMeshes = new List<string>();

		foreach (var assetPath in sceneDeps)
		{
			var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(assetPath);
			if (mesh == null) continue;

			int triangles = mesh.triangles.Length / 3;
			totalTriangles += triangles;

			if (triangles > 5000)
			{
				highPolyMeshes.Add($"{System.IO.Path.GetFileName(assetPath)} ({triangles:N0} triangles)");
			}
		}

		if (totalTriangles > QUEST_MAX_TRIANGLES)
		{
			issues.Add(new Quest2Issue
			{
				Category = "Geometry",
				Severity = IssueSeverity.Critical,
				Title = "Triangle Count Exceeds Quest 2 Recommendations",
				Description = $"Total triangles: {totalTriangles:N0} (Recommended max: {QUEST_MAX_TRIANGLES:N0})",
				Recommendation = "Reduce mesh complexity, use LOD systems, or implement occlusion culling."
			});
		}

		if (highPolyMeshes.Count > 0)
		{
			warnings.Add(new Quest2Issue
			{
				Category = "Geometry",
				Severity = IssueSeverity.Warning,
				Title = "High-Poly Meshes Detected",
				Description = $"Found {highPolyMeshes.Count} meshes with >5K triangles:\n" + string.Join("\n", highPolyMeshes.Take(5)) + (highPolyMeshes.Count > 5 ? "\n..." : ""),
				Recommendation = "Consider creating lower-poly versions for distant objects or implementing LOD."
			});
		}
	}

	private static void CheckLightingSetup(List<Quest2Issue> issues, List<Quest2Issue> warnings, List<Quest2Issue> info)
	{
		var lights = FindObjectsOfType<Light>();
		var realtimeLights = lights.Where(l => l.lightmapBakeType == LightmapBakeType.Realtime).ToArray();
		var mixedLights = lights.Where(l => l.lightmapBakeType == LightmapBakeType.Mixed).ToArray();

		if (realtimeLights.Length > QUEST_MAX_LIGHTS)
		{
			issues.Add(new Quest2Issue
			{
				Category = "Lighting",
				Severity = IssueSeverity.Critical,
				Title = "Too Many Real-time Lights",
				Description = $"Found {realtimeLights.Length} real-time lights (Recommended max: {QUEST_MAX_LIGHTS})",
				Recommendation = "Set lights to 'Baked' mode or reduce the number of real-time lights."
			});
		}

		if (mixedLights.Length > 2)
		{
			warnings.Add(new Quest2Issue
			{
				Category = "Lighting",
				Severity = IssueSeverity.Warning,
				Title = "Many Mixed Mode Lights",
				Description = $"Found {mixedLights.Length} mixed mode lights",
				Recommendation = "Mixed lights can be expensive on Quest. Consider using baked lighting where possible."
			});
		}

		// Check for expensive light types
		var spotLights = lights.Where(l => l.type == LightType.Spot && l.lightmapBakeType == LightmapBakeType.Realtime).ToArray();
		var pointLights = lights.Where(l => l.type == LightType.Point && l.lightmapBakeType == LightmapBakeType.Realtime).ToArray();

		if (spotLights.Length > 1 || pointLights.Length > 2)
		{
			warnings.Add(new Quest2Issue
			{
				Category = "Lighting",
				Severity = IssueSeverity.Warning,
				Title = "Expensive Real-time Light Types",
				Description = $"Spot lights: {spotLights.Length}, Point lights: {pointLights.Length}",
				Recommendation = "Directional lights are most efficient for Quest. Limit spot/point lights."
			});
		}

		// Check for shadows
		var shadowCastingLights = lights.Where(l => l.shadows != LightShadows.None).ToArray();
		if (shadowCastingLights.Length > 1)
		{
			warnings.Add(new Quest2Issue
			{
				Category = "Lighting",
				Severity = IssueSeverity.Warning,
				Title = "Multiple Shadow-Casting Lights",
				Description = $"Found {shadowCastingLights.Length} lights with shadows enabled",
				Recommendation = "Limit shadow-casting lights to 1 (usually the main directional light) for Quest performance."
			});
		}
	}

	private static void CheckMaterialsAndShaders(List<Quest2Issue> issues, List<Quest2Issue> warnings, List<Quest2Issue> info)
	{
		var sceneDeps = GetOpenSceneDependencyPaths();
		var materials = new List<Material>();
		var incompatibleShaders = new List<string>();
		var nonOptimalShaders = new List<string>();
		var goodShaders = new List<string>();

		foreach (var assetPath in sceneDeps)
		{
			var material = AssetDatabase.LoadAssetAtPath<Material>(assetPath);
			if (material != null)
			{
				materials.Add(material);

				var shader = material.shader;
				if (shader != null)
				{
					string shaderName = shader.name;
					bool isCompatible = false;

					// Check if shader is VRChat Quest compatible
					foreach (var compatibleShader in QUEST_COMPATIBLE_SHADERS)
					{
						if (shaderName.StartsWith(compatibleShader))
						{
							isCompatible = true;
							goodShaders.Add($"{System.IO.Path.GetFileName(assetPath)}: {shaderName}");
							break;
						}
					}

					if (!isCompatible)
					{
						// Check if it's a known incompatible shader
						foreach (var incompatibleShader in QUEST_INCOMPATIBLE_SHADERS)
						{
							if (shaderName.StartsWith(incompatibleShader) || shaderName.Contains(incompatibleShader))
							{
								incompatibleShaders.Add($"{System.IO.Path.GetFileName(assetPath)}: {shaderName}");
								break;
							}
						}

						// If not explicitly incompatible, it might still work but isn't optimal
						if (!incompatibleShaders.Any(s => s.Contains(System.IO.Path.GetFileName(assetPath))))
						{
							nonOptimalShaders.Add($"{System.IO.Path.GetFileName(assetPath)}: {shaderName}");
						}
					}
				}
			}
		}

		if (materials.Count > QUEST_MAX_MATERIALS)
		{
			warnings.Add(new Quest2Issue
			{
				Category = "Materials",
				Severity = IssueSeverity.Warning,
				Title = "High Material Count May Impact Performance",
				Description = $"Found {materials.Count} materials (Recommended max: {QUEST_MAX_MATERIALS} for optimal performance)",
				Recommendation = "Consider consolidating materials, using texture atlases, or implementing material LOD systems to reduce draw calls on Quest."
			});
		}

		if (incompatibleShaders.Count > 0)
		{
			issues.Add(new Quest2Issue
			{
				Category = "Shaders",
				Severity = IssueSeverity.Critical,
				Title = "VRChat Quest Incompatible Shaders Detected",
				Description = $"Found {incompatibleShaders.Count} materials using shaders that don't work on Quest:\n" + string.Join("\n", incompatibleShaders.Take(5)) + (incompatibleShaders.Count > 5 ? "\n..." : ""),
				Recommendation = "Replace with VRChat/Mobile/* shaders. Standard, Autodesk Interactive, and HDRP/URP shaders are not supported on Quest. Only VRChat's mobile shaders work reliably."
			});
		}

		if (nonOptimalShaders.Count > 0)
		{
			warnings.Add(new Quest2Issue
			{
				Category = "Shaders",
				Severity = IssueSeverity.Warning,
				Title = "Non-Optimal Shaders May Cause Issues",
				Description = $"Found {nonOptimalShaders.Count} materials using shaders that may not perform well on Quest",
				Recommendation = "Consider replacing with VRChat/Mobile/* shaders for guaranteed Quest compatibility and better performance."
			});
		}

		if (goodShaders.Count > 0)
		{
			info.Add(new Quest2Issue
			{
				Category = "Shaders",
				Severity = IssueSeverity.Info,
				Title = "Quest-Compatible Shaders Found",
				Description = $"✅ {goodShaders.Count} materials are using VRChat Mobile shaders - excellent for Quest compatibility!",
				Recommendation = "These shaders will work well on Quest. Continue using VRChat/Mobile/* shaders for new materials."
			});
		}
	}

	private static void CheckTextureSettings(List<Quest2Issue> issues, List<Quest2Issue> warnings, List<Quest2Issue> info)
	{
		var sceneDeps = GetOpenSceneDependencyPaths();
		var oversizedTextures = new List<string>();
		var largeTextures = new List<string>();
		var uncompressedTextures = new List<string>();
		var noMobileOverrides = new List<string>();
		var goodTextures = new List<string>();

		foreach (var assetPath in sceneDeps)
		{
			var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
			if (texture == null) continue;

			var importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
			if (importer == null) continue;

			// Check texture size against VRChat recommendations
			int maxSize = Mathf.Max(texture.width, texture.height);
			string fileName = System.IO.Path.GetFileName(assetPath);

			if (maxSize > QUEST_MAX_TEXTURE_SIZE)
			{
				oversizedTextures.Add($"{fileName} ({texture.width}x{texture.height})");
			}
			else if (maxSize > QUEST_PREFERRED_TEXTURE_SIZE)
			{
				largeTextures.Add($"{fileName} ({texture.width}x{texture.height})");
			}
			else if (maxSize <= QUEST_OPTIMAL_TEXTURE_SIZE)
			{
				goodTextures.Add($"{fileName} ({texture.width}x{texture.height})");
			}

			// Check compression - VRChat requires compressed textures for Quest
			if (texture.format == TextureFormat.RGBA32 || texture.format == TextureFormat.RGB24 ||
				texture.format == TextureFormat.ARGB32 || texture.format == TextureFormat.BGRA32)
			{
				uncompressedTextures.Add($"{fileName} ({texture.format})");
			}

			// Check mobile platform overrides - essential for Quest
			var androidSettings = importer.GetPlatformTextureSettings("Android");
			if (androidSettings == null || !androidSettings.overridden)
			{
				noMobileOverrides.Add(fileName);
			}
		}

		if (oversizedTextures.Count > 0)
		{
			issues.Add(new Quest2Issue
			{
				Category = "Textures",
				Severity = IssueSeverity.Critical,
				Title = "Textures Exceed VRChat Quest Technical Limit",
				Description = $"Found {oversizedTextures.Count} textures larger than {QUEST_MAX_TEXTURE_SIZE}px (VRChat's technical limit):\n" + string.Join("\n", oversizedTextures.Take(3)) + (oversizedTextures.Count > 3 ? "\n..." : ""),
				Recommendation = $"Reduce texture sizes to {QUEST_MAX_TEXTURE_SIZE}px or smaller immediately. VRChat may fail to load these textures on Quest."
			});
		}

		if (largeTextures.Count > 0)
		{
			warnings.Add(new Quest2Issue
			{
				Category = "Textures",
				Severity = IssueSeverity.Warning,
				Title = "Textures Larger Than VRChat Quest Recommendations",
				Description = $"Found {largeTextures.Count} textures larger than {QUEST_PREFERRED_TEXTURE_SIZE}px (VRChat recommends 1K textures for Quest):\n" + string.Join("\n", largeTextures.Take(3)) + (largeTextures.Count > 3 ? "\n..." : ""),
				Recommendation = $"VRChat recommends 1K ({QUEST_PREFERRED_TEXTURE_SIZE}px) textures for Quest. Consider reducing these to improve performance and reduce world size."
			});
		}

		if (uncompressedTextures.Count > 0)
		{
			issues.Add(new Quest2Issue
			{
				Category = "Textures",
				Severity = IssueSeverity.Critical,
				Title = "Uncompressed Textures Found",
				Description = $"Found {uncompressedTextures.Count} uncompressed textures",
				Recommendation = "Use compressed formats (DXT/ASTC) for all textures to reduce memory usage."
			});
		}

		if (noMobileOverrides.Count > 0)
		{
			warnings.Add(new Quest2Issue
			{
				Category = "Textures",
				Severity = IssueSeverity.Warning,
				Title = "Missing Mobile Platform Overrides",
				Description = $"Found {noMobileOverrides.Count} textures without Android platform settings",
				Recommendation = "Set up Android platform overrides to ensure proper compression for Quest."
			});
		}

		if (goodTextures.Count > 0)
		{
			info.Add(new Quest2Issue
			{
				Category = "Textures",
				Severity = IssueSeverity.Info,
				Title = "Optimally Sized Textures",
				Description = $"Found {goodTextures.Count} textures at optimal size (≤{QUEST_OPTIMAL_TEXTURE_SIZE}px) for Quest performance:\n" + string.Join("\n", goodTextures.Take(3)) + (goodTextures.Count > 3 ? "\n..." : ""),
				Recommendation = "These textures are optimally sized for VRChat Quest. 512px textures provide the best performance."
			});
		}
	}

	private static void CheckAudioSettings(List<Quest2Issue> warnings, List<Quest2Issue> info)
	{
		var sceneDeps = GetOpenSceneDependencyPaths();
		var largeLosslessAudio = new List<string>();
		var highSampleRateAudio = new List<string>();

		foreach (var assetPath in sceneDeps)
		{
			var audioClip = AssetDatabase.LoadAssetAtPath<AudioClip>(assetPath);
			if (audioClip == null) continue;

			var importer = AssetImporter.GetAtPath(assetPath) as AudioImporter;
			if (importer == null) continue;

			var settings = importer.defaultSampleSettings;

			// Check for uncompressed long audio
			if (audioClip.length > 10f && settings.compressionFormat == AudioCompressionFormat.PCM)
			{
				largeLosslessAudio.Add($"{System.IO.Path.GetFileName(assetPath)} ({audioClip.length:F1}s, PCM)");
			}

			// Check sample rate
			if (settings.sampleRateOverride > 44100 || (settings.sampleRateOverride == 0 && audioClip.frequency > 44100))
			{
				highSampleRateAudio.Add($"{System.IO.Path.GetFileName(assetPath)} ({audioClip.frequency}Hz)");
			}
		}

		if (largeLosslessAudio.Count > 0)
		{
			warnings.Add(new Quest2Issue
			{
				Category = "Audio",
				Severity = IssueSeverity.Warning,
				Title = "Large Uncompressed Audio Files",
				Description = $"Found {largeLosslessAudio.Count} long uncompressed audio clips",
				Recommendation = "Use compressed audio (Vorbis) for clips longer than 10 seconds to save memory."
			});
		}

		if (highSampleRateAudio.Count > 0)
		{
			info.Add(new Quest2Issue
			{
				Category = "Audio",
				Severity = IssueSeverity.Info,
				Title = "High Sample Rate Audio",
				Description = $"Found {highSampleRateAudio.Count} audio clips with >44.1kHz sample rate",
				Recommendation = "Consider reducing to 44.1kHz or 22kHz for Quest to save memory and processing."
			});
		}
	}

	private static void CheckPostProcessingEffects(List<Quest2Issue> issues, List<Quest2Issue> warnings, List<Quest2Issue> info)
	{
		var postProcessVolumes = FindObjectsOfType<UnityEngine.Rendering.PostProcessing.PostProcessVolume>();

		if (postProcessVolumes.Length > 0)
		{
			issues.Add(new Quest2Issue
			{
				Category = "Post-Processing",
				Severity = IssueSeverity.Critical,
				Title = "Post-Processing Effects Will Not Work on Quest",
				Description = $"Found {postProcessVolumes.Length} Post-Process Volumes. VRChat completely disables all post-processing effects on Quest.",
				Recommendation = "Remove or disable all Post-Process Volumes. Consider using custom shaders or alternative techniques for visual effects on Quest."
			});
		}

		// Check for reflection probes
		var reflectionProbes = FindObjectsOfType<ReflectionProbe>();
		if (reflectionProbes.Length > 2)
		{
			warnings.Add(new Quest2Issue
			{
				Category = "Reflections",
				Severity = IssueSeverity.Warning,
				Title = "Many Reflection Probes",
				Description = $"Found {reflectionProbes.Length} reflection probes",
				Recommendation = "Limit reflection probes for Quest. Use baked probes and lower resolution where possible."
			});
		}
	}

	private static void CheckSceneComplexity(List<Quest2Issue> warnings, List<Quest2Issue> info)
	{
		// Count active GameObjects
		var rootObjects = EditorSceneManager.GetActiveScene().GetRootGameObjects();
		int totalObjects = 0;
		foreach (var root in rootObjects)
		{
			totalObjects += root.GetComponentsInChildren<Transform>().Length;
		}

		if (totalObjects > 1000)
		{
			warnings.Add(new Quest2Issue
			{
				Category = "Scene Complexity",
				Severity = IssueSeverity.Warning,
				Title = "High GameObject Count",
				Description = $"Scene contains {totalObjects} GameObjects",
				Recommendation = "Consider reducing object count, using static batching, or implementing object pooling."
			});
		}

		// Check for particle systems with VRChat Quest limitations
		var particleSystems = FindObjectsOfType<ParticleSystem>();
		var heavyParticles = new List<string>();
		var manyParticles = new List<string>();

		foreach (var ps in particleSystems)
		{
			var main = ps.main;
			var emission = ps.emission;

			// Check for high particle counts (Quest performs poorly with many particles)
			if (main.maxParticles > 100)
			{
				heavyParticles.Add($"{ps.gameObject.name} ({main.maxParticles} max particles)");
			}

			// Check emission rate
			if (emission.enabled && emission.rateOverTime.constant > 50)
			{
				manyParticles.Add($"{ps.gameObject.name} ({emission.rateOverTime.constant}/sec emission)");
			}
		}

		if (heavyParticles.Count > 0)
		{
			warnings.Add(new Quest2Issue
			{
				Category = "Particle Systems",
				Severity = IssueSeverity.Warning,
				Title = "High Particle Count Systems",
				Description = $"Found {heavyParticles.Count} particle systems with >100 max particles:\n" + string.Join("\n", heavyParticles.Take(3)) + (heavyParticles.Count > 3 ? "\n..." : ""),
				Recommendation = "VRChat Quest performs poorly with many particles. Consider reducing max particles to 50 or fewer, or use simpler effects."
			});
		}

		if (manyParticles.Count > 0)
		{
			warnings.Add(new Quest2Issue
			{
				Category = "Particle Systems",
				Severity = IssueSeverity.Warning,
				Title = "High Emission Rate Particle Systems",
				Description = $"Found {manyParticles.Count} particle systems with high emission rates:\n" + string.Join("\n", manyParticles.Take(3)) + (manyParticles.Count > 3 ? "\n..." : ""),
				Recommendation = "High emission rates can cause performance issues on Quest. Consider reducing emission rates or using burst emission instead."
			});
		}

		if (particleSystems.Length > 10)
		{
			warnings.Add(new Quest2Issue
			{
				Category = "Particle Systems",
				Severity = IssueSeverity.Warning,
				Title = "Many Particle Systems",
				Description = $"Found {particleSystems.Length} particle systems in the scene",
				Recommendation = "VRChat Quest has limited particle performance. Consider combining effects, using simpler alternatives, or disabling some particles for Quest users."
			});
		}
		else if (particleSystems.Length > 5)
		{
			info.Add(new Quest2Issue
			{
				Category = "Particle Systems",
				Severity = IssueSeverity.Info,
				Title = "Multiple Particle Systems",
				Description = $"Found {particleSystems.Length} particle systems in the scene",
				Recommendation = "Monitor particle system performance on Quest. Test all effects on actual Quest hardware."
			});
		}
	}

	private static long EstimateTextureMemoryForQuest(Texture2D texture)
	{
		return EstimateTextureMemoryForQuest(texture.width, texture.height, texture.format);
	}

	private static long EstimateTextureMemoryForQuest(int width, int height, TextureFormat format)
	{
		// Estimate Quest memory usage (typically ASTC compressed)
		if (format.ToString().Contains("ASTC"))
		{
			// ASTC compression ratios
			if (format == TextureFormat.ASTC_4x4) return width * height; // 8 bpp
			if (format == TextureFormat.ASTC_6x6) return width * height * 4 / 9; // ~3.56 bpp
			if (format == TextureFormat.ASTC_8x8) return width * height / 4; // 2 bpp
			return width * height / 8; // Conservative estimate for other ASTC formats
		}
		else if (format.ToString().Contains("DXT"))
		{
			if (format == TextureFormat.DXT1 || format == TextureFormat.DXT1Crunched)
				return width * height / 2; // 4 bpp
			else
				return width * height; // 8 bpp
		}
		else
		{
			// Uncompressed formats
			switch (format)
			{
				case TextureFormat.RGBA32:
				case TextureFormat.ARGB32:
					return width * height * 4;
				case TextureFormat.RGB24:
					return width * height * 3;
				case TextureFormat.RGBA4444:
				case TextureFormat.RGB565:
					return width * height * 2;
				default:
					return width * height * 4; // Conservative estimate
			}
		}
	}

	private static void OutputResults(List<Quest2Issue> issues, List<Quest2Issue> warnings, List<Quest2Issue> info)
	{
		var sb = new StringBuilder();
		sb.AppendLine("[Quest2Analyzer] Quest 2 Compatibility Analysis Complete");
		sb.AppendLine($"Critical Issues: {issues.Count}, Warnings: {warnings.Count}, Info: {info.Count}");
		sb.AppendLine();

		if (issues.Count > 0)
		{
			sb.AppendLine("=== CRITICAL ISSUES ===");
			foreach (var issue in issues)
			{
				sb.AppendLine($"❌ [{issue.Category}] {issue.Title}");
				sb.AppendLine($"   {issue.Description}");
				sb.AppendLine($"   💡 {issue.Recommendation}");
				sb.AppendLine();
			}
		}

		if (warnings.Count > 0)
		{
			sb.AppendLine("=== WARNINGS ===");
			foreach (var warning in warnings)
			{
				sb.AppendLine($"⚠️ [{warning.Category}] {warning.Title}");
				sb.AppendLine($"   {warning.Description}");
				sb.AppendLine($"   💡 {warning.Recommendation}");
				sb.AppendLine();
			}
		}

		if (info.Count > 0)
		{
			sb.AppendLine("=== INFORMATION ===");
			foreach (var infoItem in info)
			{
				sb.AppendLine($"ℹ️ [{infoItem.Category}] {infoItem.Title}");
				sb.AppendLine($"   {infoItem.Description}");
				sb.AppendLine($"   💡 {infoItem.Recommendation}");
				sb.AppendLine();
			}
		}

		if (issues.Count == 0 && warnings.Count == 0)
		{
			sb.AppendLine("✅ No critical Quest 2 compatibility issues found!");
			sb.AppendLine("Your world appears to be well-optimized for Quest 2 users.");
		}

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

	// Helper method to find objects of type in scene
	private static T[] FindObjectsOfType<T>() where T : UnityEngine.Object
	{
		return UnityEngine.Object.FindObjectsOfType<T>();
	}
}

public class Quest2Issue
{
	public string Category { get; set; }
	public IssueSeverity Severity { get; set; }
	public string Title { get; set; }
	public string Description { get; set; }
	public string Recommendation { get; set; }
}

public enum IssueSeverity
{
	Info,
	Warning,
	Critical
}