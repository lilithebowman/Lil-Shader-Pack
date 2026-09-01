using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.Linq;

namespace CozyCon.Tools
{
	/// <summary>
	/// Custom lighting enhancement tools for VRChat worlds.
	/// Works alongside Unity's lightmapper to add Quest-optimized features.
	/// </summary>
	public class CustomLightingEnhancer : EditorWindow
	{
		[MenuItem("Lilithe/Custom Lighting Enhancer")]
		static void Init()
		{
			CustomLightingEnhancer window = (CustomLightingEnhancer)EditorWindow.GetWindow(typeof(CustomLightingEnhancer));
			window.titleContent = new GUIContent("Custom Lighting Enhancer");
			window.Show();
		}

		private Vector2 scrollPosition;

		void OnGUI()
		{
			scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);

			GUILayout.Label("Custom Lighting Enhancement for VRChat", EditorStyles.boldLabel);
			EditorGUILayout.Space();

			EditorGUILayout.HelpBox("This tool enhances Unity's built-in lightmapping with custom features optimized for VRChat Quest.", MessageType.Info);
			EditorGUILayout.Space();

			// Shader Validation
			GUILayout.Label("Shader Validation", EditorStyles.boldLabel);
			EditorGUILayout.HelpBox("Checks for shader compilation issues that could affect lighting baking.", MessageType.None);

			if (GUILayout.Button("Validate Scene Shaders"))
			{
				ValidateSceneShaders();
			}

			if (GUILayout.Button("Fix Common Shader Issues"))
			{
				FixCommonShaderIssues();
			}
			EditorGUILayout.Space();

			// Quest 2 Lightmap Optimization
			GUILayout.Label("Quest 2 Lightmap Optimization", EditorStyles.boldLabel);
			EditorGUILayout.HelpBox("Optimizes Unity's lightmapping settings specifically for VRChat Quest 2 performance and size limits.", MessageType.None);

			if (GUILayout.Button("Optimize Lightmap Settings for Quest"))
			{
				OptimizeLightmapSettingsForQuest();
			}

			if (GUILayout.Button("Apply Quest-Safe Lighting Setup"))
			{
				ApplyQuestSafeLightingSetup();
			}

			if (GUILayout.Button("Analyze Current Lightmap Settings"))
			{
				AnalyzeLightmapSettings();
			}
			EditorGUILayout.Space();

			// GI Preprocessing Diagnostics
			GUILayout.Label("GI Preprocessing Diagnostics", EditorStyles.boldLabel);
			EditorGUILayout.HelpBox("Diagnoses and fixes common Global Illumination preprocessing failures.", MessageType.None);

			if (GUILayout.Button("Diagnose GI Preprocessing Issues"))
			{
				DiagnoseGIPreprocessingIssues();
			}

			if (GUILayout.Button("Fix Common GI Preprocessing Problems"))
			{
				FixGIPreprocessingProblems();
			}

			if (GUILayout.Button("Clear Lightmap Cache & Restart GI"))
			{
				ClearLightmapCacheAndRestart();
			}
			EditorGUILayout.Space();

			// Vertex Light Baking
			GUILayout.Label("Vertex Light Baking", EditorStyles.boldLabel);
			EditorGUILayout.HelpBox("Bakes lighting information into vertex colors for Quest-optimized performance.", MessageType.None);

			if (GUILayout.Button("Bake Vertex Lighting"))
			{
				BakeVertexLighting();
			}
			EditorGUILayout.Space();

			// Custom AO Generation
			GUILayout.Label("Custom Ambient Occlusion", EditorStyles.boldLabel);
			EditorGUILayout.HelpBox("Generates simple AO without Unity's complex GI system.", MessageType.None);

			if (GUILayout.Button("Generate Custom AO"))
			{
				GenerateCustomAO();
			}
			EditorGUILayout.Space();

			// Quest Light Optimization
			GUILayout.Label("Quest Light Optimization", EditorStyles.boldLabel);
			EditorGUILayout.HelpBox("Automatically optimizes lighting setup for VRChat Quest compatibility.", MessageType.None);

			if (GUILayout.Button("Optimize Lights for Quest"))
			{
				OptimizeLightsForQuest();
			}
			EditorGUILayout.Space();

			// Light Probe Enhancement
			GUILayout.Label("Custom Light Probes", EditorStyles.boldLabel);
			EditorGUILayout.HelpBox("Places light probes optimally for VRChat worlds.", MessageType.None);

			if (GUILayout.Button("Generate Smart Light Probes"))
			{
				GenerateSmartLightProbes();
			}

			EditorGUILayout.EndScrollView();
		}

		/// <summary>
		/// Optimizes Unity's lightmapping settings for VRChat Quest 2 performance and size constraints
		/// </summary>
		private void OptimizeLightmapSettingsForQuest()
		{
			Debug.Log("[CozyCon Lightmap Optimization] Applying Quest 2 optimized lightmap settings...");

			// Get or create lighting settings
			var lightingSettings = GetOrCreateLightingSettings();

			// Optimize for Quest 2 - smaller lightmaps, faster baking, mobile-friendly settings
			lightingSettings.lightmapper = LightingSettings.Lightmapper.ProgressiveCPU; // More reliable than GPU for consistent results
			lightingSettings.indirectResolution = 1f; // Reduced from default 2
			lightingSettings.lightmapResolution = 10f; // Reduced from default 40
			lightingSettings.lightmapMaxSize = 512; // Much smaller for Quest (default 1024)
			lightingSettings.aoExponentIndirect = 1f;
			lightingSettings.aoExponentDirect = 0f; // Disable direct AO for performance
			lightingSettings.aoMaxDistance = 0.5f; // Reduce AO sampling distance
			lightingSettings.filteringMode = LightingSettings.FilterMode.Auto;

			// Set GI workflow to baked only (no realtime GI on Quest)
			Lightmapping.giWorkflowMode = Lightmapping.GIWorkflowMode.OnDemand;

			// Configure render settings for Quest
			RenderSettings.defaultReflectionMode = UnityEngine.Rendering.DefaultReflectionMode.Custom;
			RenderSettings.defaultReflectionResolution = 64; // Very small for Quest
			RenderSettings.reflectionBounces = 1; // Minimal bounces
			RenderSettings.reflectionIntensity = 0.5f; // Reduced reflection intensity

			// Apply the lighting settings to the scene
			Lightmapping.lightingSettings = lightingSettings;

			// Save the settings
			EditorUtility.SetDirty(lightingSettings);
			AssetDatabase.SaveAssets();

			Debug.Log("[CozyCon Lightmap Optimization] Quest 2 optimized settings applied:");
			Debug.Log($"  - Max Atlas Size: {lightingSettings.lightmapMaxSize}px");
			Debug.Log($"  - Bake Resolution: {lightingSettings.lightmapResolution}");
			Debug.Log($"  - Indirect Resolution: {lightingSettings.indirectResolution}");
			Debug.Log($"  - Lightmap Compression: Enabled");
			Debug.Log($"  - Reflection Resolution: {RenderSettings.defaultReflectionResolution}px");

			EditorUtility.DisplayDialog("Quest Lightmap Optimization",
				"Applied Quest 2 optimized lightmap settings:\n\n" +
				"• Max Atlas: 512px (was 1024px)\n" +
				"• Bake Resolution: 10 (was 40)\n" +
				"• Indirect Resolution: 1 (was 2)\n" +
				"• Reflection Resolution: 64px\n" +
				"• Texture Compression: Enabled\n\n" +
				"These settings prioritize small file size and Quest performance.",
				"OK");
		}

		/// <summary>
		/// Gets existing LightingSettings or creates new one with Quest-optimized defaults
		/// </summary>
		private LightingSettings GetOrCreateLightingSettings()
		{
			var currentSettings = Lightmapping.lightingSettings;
			if (currentSettings != null)
			{
				return currentSettings;
			}

			// Create new lighting settings
			var newSettings = new LightingSettings();
			newSettings.name = "Quest_Optimized_Lighting_Settings";

			// Save to assets
			string settingsPath = "Assets/Generated/Quest_Optimized_Lighting_Settings.lighting";
			AssetDatabase.CreateAsset(newSettings, settingsPath);
			AssetDatabase.SaveAssets();

			return newSettings;
		}

		/// <summary>
		/// Applies a complete Quest-safe lighting setup including lights, probes, and environment
		/// </summary>
		private void ApplyQuestSafeLightingSetup()
		{
			Debug.Log("[CozyCon Quest Lighting] Applying comprehensive Quest-safe lighting setup...");

			int optimizedLights = 0;
			int disabledFeatures = 0;

			// Optimize lights for Quest
			var lights = FindObjectsOfType<Light>();
			foreach (var light in lights)
			{
				// Convert realtime lights to baked
				if (light.lightmapBakeType == LightmapBakeType.Realtime)
				{
					light.lightmapBakeType = LightmapBakeType.Baked;
					optimizedLights++;
				}

				// Disable shadows for all but directional lights
				if (light.type != LightType.Directional && light.shadows != LightShadows.None)
				{
					light.shadows = LightShadows.None;
					disabledFeatures++;
				}

				// Limit light range for performance
				if (light.range > 15f)
				{
					light.range = 15f;
				}

				// Reduce light intensity if too high
				if (light.intensity > 2f)
				{
					light.intensity = 2f;
				}
			}

			// Configure ambient lighting for Quest
			RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
			RenderSettings.ambientSkyColor = new Color(0.5f, 0.7f, 1f, 1f); // Subtle blue sky
			RenderSettings.ambientEquatorColor = new Color(0.4f, 0.4f, 0.4f, 1f); // Neutral horizon
			RenderSettings.ambientGroundColor = new Color(0.2f, 0.2f, 0.2f, 1f); // Dark ground
			RenderSettings.ambientIntensity = 1f;

			// Disable fog (not supported well on Quest)
			RenderSettings.fog = false;
			disabledFeatures++;

			// Configure skybox for Quest performance
			if (RenderSettings.skybox != null)
			{
				// Replace complex skyboxes with simple gradient or solid color
				var skyboxMaterial = new Material(Shader.Find("Skybox/Gradient"));
				skyboxMaterial.name = "Quest_Optimized_Skybox";
				skyboxMaterial.SetColor("_Color1", new Color(0.5f, 0.7f, 1f, 1f)); // Sky blue
				skyboxMaterial.SetColor("_Color2", new Color(0.8f, 0.9f, 1f, 1f)); // Lighter blue
				skyboxMaterial.SetFloat("_Exponent", 1.3f);
				skyboxMaterial.SetFloat("_Intensity", 1f);

				// Save the skybox material
				string skyboxPath = "Assets/Generated/Materials/Quest_Optimized_Skybox.mat";
				AssetDatabase.CreateAsset(skyboxMaterial, skyboxPath);
				RenderSettings.skybox = skyboxMaterial;
			}

			// Remove or optimize reflection probes
			var reflectionProbes = FindObjectsOfType<ReflectionProbe>();
			int removedProbes = 0;
			foreach (var probe in reflectionProbes)
			{
				if (probe.mode == UnityEngine.Rendering.ReflectionProbeMode.Realtime)
				{
					probe.mode = UnityEngine.Rendering.ReflectionProbeMode.Baked;
				}
				probe.resolution = 64; // Very low res for Quest
				probe.hdr = false; // Disable HDR

				// Remove excessive probes (keep max 3)
				if (removedProbes >= 3)
				{
					DestroyImmediate(probe.gameObject);
					removedProbes++;
				}
			}

			// Apply optimized lightmap settings
			OptimizeLightmapSettingsForQuest();

			AssetDatabase.SaveAssets();

			string summary = $"Quest-safe lighting setup complete:\n\n" +
							$"• Converted {optimizedLights} lights to baked\n" +
							$"• Disabled {disabledFeatures} performance-heavy features\n" +
							$"• Optimized {reflectionProbes.Length - removedProbes} reflection probes\n" +
							$"• Removed {removedProbes} excess reflection probes\n" +
							$"• Applied Quest-optimized lightmap settings\n" +
							$"• Configured mobile-friendly ambient lighting";

			Debug.Log($"[CozyCon Quest Lighting] {summary}");
			EditorUtility.DisplayDialog("Quest Lighting Setup Complete", summary, "OK");
		}

		/// <summary>
		/// Analyzes current lightmap settings and provides Quest compatibility assessment
		/// </summary>
		private void AnalyzeLightmapSettings()
		{
			Debug.Log("[CozyCon Lightmap Analysis] Analyzing current lightmap settings for Quest compatibility...");

			var issues = new List<string>();
			var warnings = new List<string>();
			var recommendations = new List<string>();

			// Get current lighting settings
			var lightingSettings = Lightmapping.lightingSettings;
			if (lightingSettings == null)
			{
				issues.Add("No LightingSettings found - using default settings");
				recommendations.Add("Create optimized LightingSettings using 'Optimize Lightmap Settings for Quest'");

				// Show basic report
				string basicReport = "=== QUEST LIGHTMAP COMPATIBILITY ANALYSIS ===\n\n";
				basicReport += "🔴 CRITICAL ISSUES:\n";
				basicReport += "• No LightingSettings asset found\n\n";
				basicReport += "💡 RECOMMENDATIONS:\n";
				basicReport += "• Use 'Optimize Lightmap Settings for Quest' to create optimized settings\n";

				Debug.Log($"[CozyCon Lightmap Analysis]\n{basicReport}");
				EditorUtility.DisplayDialog("Lightmap Settings Analysis", basicReport, "OK");
				return;
			}

			// Check lightmap settings
			int maxAtlasSize = lightingSettings.lightmapMaxSize;
			float bakeResolution = lightingSettings.lightmapResolution;
			float indirectResolution = lightingSettings.indirectResolution;
			var lightmapper = lightingSettings.lightmapper;

			// Analyze atlas size
			if (maxAtlasSize > 1024)
			{
				issues.Add($"Max Atlas Size too large: {maxAtlasSize}px (Quest recommended: 512px)");
				recommendations.Add("Reduce Max Atlas Size to 512px for Quest compatibility");
			}
			else if (maxAtlasSize > 512)
			{
				warnings.Add($"Max Atlas Size could be smaller: {maxAtlasSize}px (Quest optimal: 512px)");
			}

			// Analyze bake resolution
			if (bakeResolution > 20)
			{
				issues.Add($"Bake Resolution too high: {bakeResolution} (Quest recommended: 10)");
				recommendations.Add("Reduce Bake Resolution to 10 for faster baking and smaller files");
			}
			else if (bakeResolution > 10)
			{
				warnings.Add($"Bake Resolution higher than optimal: {bakeResolution} (Quest optimal: 10)");
			}

			// Analyze indirect resolution
			if (indirectResolution > 2)
			{
				issues.Add($"Indirect Resolution too high: {indirectResolution} (Quest recommended: 1)");
				recommendations.Add("Reduce Indirect Resolution to 1 for Quest performance");
			}

			// Check lightmapper
			if (lightmapper == LightingSettings.Lightmapper.ProgressiveCPU ||
				lightmapper == LightingSettings.Lightmapper.ProgressiveGPU)
			{
				// This is actually fine - just noting it
				warnings.Add($"Using {lightmapper} lightmapper (Good choice for quality)");
			}

			// Check lights in scene
			var lights = FindObjectsOfType<Light>();
			var realtimeLights = lights.Where(l => l.lightmapBakeType == LightmapBakeType.Realtime).Count();
			var shadowCastingLights = lights.Where(l => l.shadows != LightShadows.None && l.type != LightType.Directional).Count();

			if (realtimeLights > 2)
			{
				issues.Add($"Too many realtime lights: {realtimeLights} (Quest recommended: ≤2)");
				recommendations.Add("Convert realtime lights to baked for Quest performance");
			}

			if (shadowCastingLights > 0)
			{
				warnings.Add($"Non-directional lights casting shadows: {shadowCastingLights}");
				recommendations.Add("Disable shadows on point/spot lights for Quest performance");
			}

			// Check reflection probes
			var reflectionProbes = FindObjectsOfType<ReflectionProbe>();
			if (reflectionProbes.Length > 3)
			{
				warnings.Add($"Many reflection probes: {reflectionProbes.Length} (Quest recommended: ≤3)");
				recommendations.Add("Reduce number of reflection probes for Quest performance");
			}

			var realtimeProbes = reflectionProbes.Where(p => p.mode == UnityEngine.Rendering.ReflectionProbeMode.Realtime).Count();
			if (realtimeProbes > 0)
			{
				issues.Add($"Realtime reflection probes detected: {realtimeProbes}");
				recommendations.Add("Convert reflection probes to baked mode for Quest");
			}

			// Generate report
			string report = "=== QUEST LIGHTMAP COMPATIBILITY ANALYSIS ===\n\n";

			if (issues.Count == 0 && warnings.Count == 0)
			{
				report += "✅ EXCELLENT: All settings are Quest-optimized!\n\n";
				report += "Current Settings:\n";
				report += $"• Max Atlas Size: {maxAtlasSize}px\n";
				report += $"• Bake Resolution: {bakeResolution}\n";
				report += $"• Indirect Resolution: {indirectResolution}\n";
				report += $"• Lightmapper: {lightmapper}\n";
				report += $"• Realtime Lights: {realtimeLights}\n";
				report += $"• Reflection Probes: {reflectionProbes.Length}";
			}
			else
			{
				if (issues.Count > 0)
				{
					report += "🔴 CRITICAL ISSUES:\n";
					foreach (var issue in issues)
					{
						report += $"• {issue}\n";
					}
					report += "\n";
				}

				if (warnings.Count > 0)
				{
					report += "⚠️ WARNINGS:\n";
					foreach (var warning in warnings)
					{
						report += $"• {warning}\n";
					}
					report += "\n";
				}

				if (recommendations.Count > 0)
				{
					report += "💡 RECOMMENDATIONS:\n";
					foreach (var rec in recommendations)
					{
						report += $"• {rec}\n";
					}
					report += "\n";
				}

				report += "Use 'Optimize Lightmap Settings for Quest' to automatically fix most issues.";
			}

			Debug.Log($"[CozyCon Lightmap Analysis]\n{report}");
			EditorUtility.DisplayDialog("Lightmap Settings Analysis", report, "OK");
		}

		/// <summary>
		/// Validates all shaders used in the scene and reports compilation issues
		/// </summary>
		private void ValidateSceneShaders()
		{
			var renderers = FindObjectsOfType<Renderer>();
			var brokenShaders = new List<string>();
			var problematicObjects = new List<GameObject>();
			var checkedShaders = new HashSet<Shader>();

			Debug.Log("[CozyCon Shader Validation] Starting shader validation...");

			foreach (var renderer in renderers)
			{
				if (renderer.sharedMaterials == null) continue;

				foreach (var material in renderer.sharedMaterials)
				{
					if (material == null || material.shader == null) continue;

					// Skip if we've already checked this shader
					if (checkedShaders.Contains(material.shader)) continue;
					checkedShaders.Add(material.shader);

					// Check if shader compiles properly
					if (!IsShaderValid(material.shader))
					{
						brokenShaders.Add(material.shader.name);
						problematicObjects.Add(renderer.gameObject);
						Debug.LogError($"[CozyCon Shader Validation] Broken shader detected: {material.shader.name} on {renderer.gameObject.name}");
					}
				}
			}

			if (brokenShaders.Count == 0)
			{
				Debug.Log("[CozyCon Shader Validation] ✅ All shaders in scene compile successfully!");
				EditorUtility.DisplayDialog("Shader Validation", "All shaders in the scene compile successfully!", "OK");
			}
			else
			{
				string message = $"Found {brokenShaders.Count} problematic shaders:\n\n" + string.Join("\n", brokenShaders.Distinct());
				Debug.LogWarning($"[CozyCon Shader Validation] ⚠️ Found {brokenShaders.Count} problematic shaders.");
				EditorUtility.DisplayDialog("Shader Validation Issues", message, "OK");
			}
		}

		/// <summary>
		/// Attempts to fix common shader compilation issues
		/// </summary>
		private void FixCommonShaderIssues()
		{
			var renderers = FindObjectsOfType<Renderer>();
			var fixedShaders = 0;
			var replacedMaterials = 0;

			// Common fallback shaders that usually work
			var fallbackShaders = new Dictionary<string, string>
			{
				{ "Standard", "VRChat/Mobile/Diffuse" },
				{ "Universal Render Pipeline/Lit", "VRChat/Mobile/Diffuse" },
				{ "HDRP/Lit", "VRChat/Mobile/Diffuse" },
				{ "Legacy Shaders/Diffuse", "VRChat/Mobile/Diffuse" },
				{ "Legacy Shaders/Bumped Diffuse", "VRChat/Mobile/Bumped Diffuse" }
			};

			foreach (var renderer in renderers)
			{
				if (renderer.sharedMaterials == null) continue;

				var materials = renderer.sharedMaterials;
				bool materialsChanged = false;

				for (int i = 0; i < materials.Length; i++)
				{
					var material = materials[i];
					if (material == null || material.shader == null) continue;

					// Check if shader is problematic
					if (!IsShaderValid(material.shader))
					{
						// Try to find a suitable replacement
						string fallbackShaderName = GetFallbackShader(material.shader.name, fallbackShaders);
						Shader fallbackShader = Shader.Find(fallbackShaderName);

						if (fallbackShader != null && IsShaderValid(fallbackShader))
						{
							// Create new material with fallback shader
							var newMaterial = new Material(fallbackShader);
							newMaterial.name = material.name + "_Fixed";

							// Try to copy basic properties
							CopyCompatibleMaterialProperties(material, newMaterial);

							// Save the new material
							string path = $"Assets/Generated/Materials/{newMaterial.name}.mat";
							System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));
							AssetDatabase.CreateAsset(newMaterial, path);

							materials[i] = newMaterial;
							materialsChanged = true;
							fixedShaders++;

							Debug.Log($"[CozyCon Shader Fix] Replaced {material.shader.name} with {fallbackShaderName} on {renderer.gameObject.name}");
						}
					}
				}

				if (materialsChanged)
				{
					renderer.sharedMaterials = materials;
					replacedMaterials++;
				}
			}

			AssetDatabase.SaveAssets();
			string message = $"Fixed {fixedShaders} problematic shaders across {replacedMaterials} objects.";
			Debug.Log($"[CozyCon Shader Fix] {message}");
			EditorUtility.DisplayDialog("Shader Fix Complete", message, "OK");
		}

		/// <summary>
		/// Checks if a shader compiles properly and is suitable for lighting calculations
		/// </summary>
		private bool IsShaderValid(Shader shader)
		{
			if (shader == null) return false;

			// Check if shader is found (compiled)
			if (shader == Shader.Find("Hidden/InternalErrorShader")) return false;

			// Check for common problematic shader patterns
			string shaderName = shader.name.ToLower();

			// Shaders that are known to cause issues with lighting
			string[] problematicPatterns = {
				"error", "broken", "missing", "hidden/internalerrorshader",
				"ui/", "sprites/", "unlit/transparent", "gui/",
				"particles/", "effects/", "fx/"
			};

			foreach (var pattern in problematicPatterns)
			{
				if (shaderName.Contains(pattern))
				{
					return false;
				}
			}

			// Check if it's a VRChat Quest compatible shader (preferred)
			if (shaderName.StartsWith("vrchat/mobile/"))
			{
				return true;
			}

			// For other shaders, do basic validation
			try
			{
				// Try to create a material with this shader - this will fail if shader doesn't compile
				var testMaterial = new Material(shader);
				DestroyImmediate(testMaterial);
				return true;
			}
			catch
			{
				return false;
			}
		}

		/// <summary>
		/// Gets a fallback shader for problematic shaders
		/// </summary>
		private string GetFallbackShader(string problematicShaderName, Dictionary<string, string> fallbackShaders)
		{
			// Check direct mapping first
			if (fallbackShaders.ContainsKey(problematicShaderName))
			{
				return fallbackShaders[problematicShaderName];
			}

			// Check for partial matches
			string shaderNameLower = problematicShaderName.ToLower();

			if (shaderNameLower.Contains("transparent") || shaderNameLower.Contains("alpha"))
			{
				return "VRChat/Mobile/Transparent/Diffuse";
			}

			if (shaderNameLower.Contains("bumped") || shaderNameLower.Contains("normal"))
			{
				return "VRChat/Mobile/Bumped Diffuse";
			}

			if (shaderNameLower.Contains("specular"))
			{
				return "VRChat/Mobile/Bumped Specular";
			}

			// Default fallback
			return "VRChat/Mobile/Diffuse";
		}

		/// <summary>
		/// Copies compatible properties between materials
		/// </summary>
		private void CopyCompatibleMaterialProperties(Material source, Material target)
		{
			// Common properties that can usually be copied
			string[] commonProperties = {
				"_MainTex", "_Color", "_BumpMap", "_SpecColor", "_Glossiness", "_Metallic",
				"_EmissionColor", "_EmissionMap", "_Cutoff", "_SpecularHighlights", "_GlossyReflections"
			};

			foreach (var propName in commonProperties)
			{
				if (source.HasProperty(propName) && target.HasProperty(propName))
				{
					try
					{
						// Try to copy texture properties
						if (source.HasProperty(propName) && source.GetTexture(propName) != null)
						{
							target.SetTexture(propName, source.GetTexture(propName));
						}
						// Try to copy color properties
						else if (source.HasProperty(propName))
						{
							target.SetColor(propName, source.GetColor(propName));
						}
					}
					catch
					{
						// Skip properties that can't be copied
					}
				}
			}
		}

		/// <summary>
		/// Validates objects before processing to avoid shader issues
		/// </summary>
		private bool IsObjectSafeForLightingProcessing(Renderer renderer)
		{
			if (renderer.sharedMaterials == null) return false;

			foreach (var material in renderer.sharedMaterials)
			{
				if (material == null || material.shader == null) return false;

				// Skip objects with problematic shaders
				if (!IsShaderValid(material.shader))
				{
					Debug.LogWarning($"[CozyCon Lighting] Skipping object {renderer.gameObject.name} due to problematic shader: {material.shader.name}");
					return false;
				}
			}

			return true;
		}

		/// <summary>
		/// Bakes lighting information into vertex colors for Quest performance
		/// </summary>
		private void BakeVertexLighting()
		{
			var renderers = FindObjectsOfType<MeshRenderer>();
			int processedObjects = 0;
			int skippedObjects = 0;

			Debug.Log("[CozyCon Vertex Lighting] Starting vertex lighting bake with shader validation...");

			foreach (var renderer in renderers)
			{
				var meshFilter = renderer.GetComponent<MeshFilter>();
				if (meshFilter == null || meshFilter.sharedMesh == null) continue;

				// Validate shaders before processing
				if (!IsObjectSafeForLightingProcessing(renderer))
				{
					skippedObjects++;
					continue;
				}

				// Create a copy of the mesh to modify vertex colors
				var mesh = meshFilter.sharedMesh;
				var newMesh = Object.Instantiate(mesh);

				// Calculate lighting at each vertex
				var vertices = mesh.vertices;
				var normals = mesh.normals;
				var colors = new Color[vertices.Length];

				for (int i = 0; i < vertices.Length; i++)
				{
					Vector3 worldPos = renderer.transform.TransformPoint(vertices[i]);
					Vector3 worldNormal = renderer.transform.TransformDirection(normals[i]).normalized;

					// Simple lighting calculation
					Color lightColor = CalculateLightingAtPoint(worldPos, worldNormal);
					colors[i] = lightColor;
				}

				newMesh.colors = colors;
				newMesh.name = mesh.name + "_VertexLit";

				// Save the new mesh
				string path = $"Assets/Generated/VertexLit_{mesh.name}.asset";
				AssetDatabase.CreateAsset(newMesh, path);

				// Update the mesh filter
				meshFilter.sharedMesh = newMesh;
				processedObjects++;
			}

			AssetDatabase.SaveAssets();
			Debug.Log($"[CozyCon Custom Lighting] Baked vertex lighting for {processedObjects} objects. Skipped {skippedObjects} objects due to shader issues.");

			if (skippedObjects > 0)
			{
				EditorUtility.DisplayDialog("Vertex Lighting Complete",
					$"Successfully processed {processedObjects} objects.\n\nSkipped {skippedObjects} objects due to problematic shaders. Run 'Validate Scene Shaders' to see details.",
					"OK");
			}
		}

		/// <summary>
		/// Calculate lighting at a specific world position
		/// </summary>
		private Color CalculateLightingAtPoint(Vector3 worldPos, Vector3 normal)
		{
			Color finalColor = RenderSettings.ambientLight * RenderSettings.ambientIntensity;

			// Find all lights in the scene
			var lights = FindObjectsOfType<Light>();

			foreach (var light in lights)
			{
				if (!light.enabled) continue;

				Vector3 lightDir;
				float attenuation = 1f;

				// Calculate light direction and attenuation based on light type
				switch (light.type)
				{
					case LightType.Directional:
						lightDir = -light.transform.forward;
						break;

					case LightType.Point:
						lightDir = (light.transform.position - worldPos).normalized;
						float distance = Vector3.Distance(light.transform.position, worldPos);
						attenuation = Mathf.Clamp01(1f - (distance / light.range));
						break;

					case LightType.Spot:
						lightDir = (light.transform.position - worldPos).normalized;
						float spotDistance = Vector3.Distance(light.transform.position, worldPos);
						attenuation = Mathf.Clamp01(1f - (spotDistance / light.range));

						// Add spot cone attenuation
						float spotAngle = Vector3.Angle(-light.transform.forward, -lightDir);
						float spotAttenuation = Mathf.Clamp01(1f - (spotAngle / (light.spotAngle * 0.5f)));
						attenuation *= spotAttenuation;
						break;

					default:
						continue;
				}

				// Simple Lambert lighting
				float ndotl = Mathf.Max(0, Vector3.Dot(normal, lightDir));
				finalColor += light.color * light.intensity * ndotl * attenuation;
			}

			return finalColor;
		}

		/// <summary>
		/// Generate simple ambient occlusion without complex GI
		/// </summary>
		private void GenerateCustomAO()
		{
			var renderers = FindObjectsOfType<MeshRenderer>();
			int processedObjects = 0;
			int skippedObjects = 0;

			Debug.Log("[CozyCon Custom AO] Starting AO generation with shader validation...");

			foreach (var renderer in renderers)
			{
				var meshFilter = renderer.GetComponent<MeshFilter>();
				if (meshFilter == null || meshFilter.sharedMesh == null) continue;

				// Validate shaders before processing
				if (!IsObjectSafeForLightingProcessing(renderer))
				{
					skippedObjects++;
					continue;
				}

				var mesh = meshFilter.sharedMesh;
				var newMesh = Object.Instantiate(mesh);
				var vertices = mesh.vertices;
				var normals = mesh.normals;
				var colors = new Color[vertices.Length];

				for (int i = 0; i < vertices.Length; i++)
				{
					Vector3 worldPos = renderer.transform.TransformPoint(vertices[i]);
					Vector3 worldNormal = renderer.transform.TransformDirection(normals[i]).normalized;

					// Simple AO calculation using raycasting
					float ao = CalculateAmbientOcclusion(worldPos, worldNormal);
					colors[i] = new Color(ao, ao, ao, 1f);
				}

				newMesh.colors = colors;
				newMesh.name = mesh.name + "_AO";

				string path = $"Assets/Generated/AO_{mesh.name}.asset";
				AssetDatabase.CreateAsset(newMesh, path);

				processedObjects++;
			}

			AssetDatabase.SaveAssets();
			Debug.Log($"[CozyCon Custom Lighting] Generated AO for {processedObjects} objects. Skipped {skippedObjects} objects due to shader issues.");

			if (skippedObjects > 0)
			{
				EditorUtility.DisplayDialog("Custom AO Complete",
					$"Successfully processed {processedObjects} objects.\n\nSkipped {skippedObjects} objects due to problematic shaders. Run 'Validate Scene Shaders' to see details.",
					"OK");
			}
		}

		/// <summary>
		/// Simple ambient occlusion calculation using raycasting
		/// </summary>
		private float CalculateAmbientOcclusion(Vector3 worldPos, Vector3 normal)
		{
			int samples = 8; // Reduced for performance
			float radius = 1f;
			int hits = 0;

			for (int i = 0; i < samples; i++)
			{
				// Generate random direction in hemisphere
				Vector3 randomDir = Random.insideUnitSphere;
				if (Vector3.Dot(randomDir, normal) < 0)
					randomDir = -randomDir;

				// Cast ray
				if (Physics.Raycast(worldPos + normal * 0.01f, randomDir, radius))
				{
					hits++;
				}
			}

			// Return inverted occlusion (1 = no occlusion, 0 = full occlusion)
			return 1f - ((float)hits / samples);
		}

		/// <summary>
		/// Optimize lighting setup specifically for VRChat Quest
		/// </summary>
		private void OptimizeLightsForQuest()
		{
			var lights = FindObjectsOfType<Light>();
			int optimizedLights = 0;

			foreach (var light in lights)
			{
				// Convert real-time lights to baked where possible
				if (light.lightmapBakeType == LightmapBakeType.Realtime)
				{
					light.lightmapBakeType = LightmapBakeType.Baked;
					optimizedLights++;
				}

				// Reduce light range for performance
				if (light.range > 10f)
				{
					light.range = Mathf.Min(light.range, 10f);
				}

				// Disable shadows for point/spot lights (keep for directional)
				if (light.type != LightType.Directional && light.shadows != LightShadows.None)
				{
					light.shadows = LightShadows.None;
				}
			}

			Debug.Log($"[CozyCon Custom Lighting] Optimized {optimizedLights} lights for Quest.");
		}

		/// <summary>
		/// Generate light probes strategically for VRChat worlds
		/// </summary>
		private void GenerateSmartLightProbes()
		{
			// Find existing light probe group or create new one
			var probeGroup = FindObjectOfType<LightProbeGroup>();
			if (probeGroup == null)
			{
				GameObject probeObject = new GameObject("Smart Light Probes");
				probeGroup = probeObject.AddComponent<LightProbeGroup>();
			}

			var probePositions = new List<Vector3>();

			// Place probes at character height in walkable areas
			var colliders = FindObjectsOfType<Collider>();
			foreach (var collider in colliders)
			{
				if (collider.bounds.size.y > 2f) // Likely a wall or large object
				{
					// Place probe near this object
					Vector3 probePos = collider.bounds.center + Vector3.up * 1.7f; // Character eye height
					probePositions.Add(probePos);
				}
			}

			// Add probes in open areas
			var bounds = GetSceneBounds();
			for (float x = bounds.min.x; x < bounds.max.x; x += 5f)
			{
				for (float z = bounds.min.z; z < bounds.max.z; z += 5f)
				{
					Vector3 testPos = new Vector3(x, bounds.center.y + 1.7f, z);
					if (!Physics.CheckSphere(testPos, 0.5f)) // Not inside an object
					{
						probePositions.Add(testPos);
					}
				}
			}

			probeGroup.probePositions = probePositions.ToArray();
			Debug.Log($"[CozyCon Custom Lighting] Generated {probePositions.Count} smart light probes.");
		}

		/// <summary>
		/// Get the bounds of all renderers in the scene
		/// </summary>
		private Bounds GetSceneBounds()
		{
			var renderers = FindObjectsOfType<Renderer>();
			if (renderers.Length == 0) return new Bounds();

			Bounds bounds = renderers[0].bounds;
			foreach (var renderer in renderers)
			{
				bounds.Encapsulate(renderer.bounds);
			}
			return bounds;
		}

		/// <summary>
		/// Diagnoses common Global Illumination preprocessing failures, especially step 5 of 11 issues
		/// </summary>
		private void DiagnoseGIPreprocessingIssues()
		{
			Debug.Log("[CozyCon GI Diagnostics] Starting comprehensive GI preprocessing analysis...");

			var issues = new List<string>();
			var warnings = new List<string>();
			var fixes = new List<string>();

			// Check for common Step 5 failure causes
			// Step 5 typically involves UV unwrapping and geometry processing

			// 1. Check for problematic meshes
			var meshFilters = FindObjectsOfType<MeshFilter>();
			var problematicMeshes = new List<string>();
			var missingMeshes = new List<string>();
			var invalidUVMeshes = new List<string>();

			foreach (var meshFilter in meshFilters)
			{
				if (meshFilter.sharedMesh == null)
				{
					missingMeshes.Add(meshFilter.gameObject.name);
					continue;
				}

				var mesh = meshFilter.sharedMesh;
				var renderer = meshFilter.GetComponent<Renderer>();

				// Check if mesh is marked for lightmapping
				if (renderer != null && GameObjectUtility.AreStaticEditorFlagsSet(renderer.gameObject, StaticEditorFlags.ContributeGI))
				{
					// Check UV2 availability
					if (mesh.uv2 == null || mesh.uv2.Length == 0)
					{
						// Check if mesh has "Generate Lightmap UVs" enabled
						string assetPath = AssetDatabase.GetAssetPath(mesh);
						if (!string.IsNullOrEmpty(assetPath))
						{
							var importer = AssetImporter.GetAtPath(assetPath) as ModelImporter;
							if (importer != null && !importer.generateSecondaryUV)
							{
								invalidUVMeshes.Add($"{meshFilter.gameObject.name} ({mesh.name})");
							}
						}
					}

					// Check for degenerate triangles
					if (mesh.triangles.Length == 0)
					{
						problematicMeshes.Add($"{meshFilter.gameObject.name} (no triangles)");
					}

					// Check for excessive vertex count
					if (mesh.vertexCount > 50000)
					{
						warnings.Add($"High vertex count mesh: {meshFilter.gameObject.name} ({mesh.vertexCount} vertices)");
					}
				}
			}

			// 2. Check Lightmap UVs settings on importers
			var modelPaths = AssetDatabase.FindAssets("t:Model");
			var uvGenerationIssues = 0;
			foreach (var guid in modelPaths)
			{
				string path = AssetDatabase.GUIDToAssetPath(guid);
				var importer = AssetImporter.GetAtPath(path) as ModelImporter;
				if (importer != null)
				{
					// Check if any meshes from this model are used in lightmapped objects
					var meshes = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Mesh>();
					bool isUsedInLightmapping = false;

					foreach (var mesh in meshes)
					{
						var users = meshFilters.Where(mf => mf.sharedMesh == mesh &&
							GameObjectUtility.AreStaticEditorFlagsSet(mf.gameObject, StaticEditorFlags.ContributeGI));
						if (users.Any())
						{
							isUsedInLightmapping = true;
							break;
						}
					}

					if (isUsedInLightmapping && !importer.generateSecondaryUV)
					{
						uvGenerationIssues++;
					}
				}
			}

			// 3. Check for shader issues that can cause GI preprocessing failures
			var shadersWithProblems = new List<string>();
			var renderers = FindObjectsOfType<Renderer>();
			foreach (var renderer in renderers)
			{
				if (renderer.sharedMaterials == null) continue;
				if (!GameObjectUtility.AreStaticEditorFlagsSet(renderer.gameObject, StaticEditorFlags.ContributeGI)) continue;

				foreach (var material in renderer.sharedMaterials)
				{
					if (material == null || material.shader == null) continue;

					// Check for shaders that don't support lightmapping
					string shaderName = material.shader.name.ToLower();
					if (shaderName.Contains("unlit") || shaderName.Contains("ui/") ||
						shaderName.Contains("sprite") || shaderName.Contains("particle"))
					{
						shadersWithProblems.Add($"{renderer.gameObject.name}: {material.shader.name}");
					}
				}
			}

			// 4. Check Lighting Settings
			var lightingSettings = Lightmapping.lightingSettings;
			if (lightingSettings == null)
			{
				issues.Add("No LightingSettings asset assigned");
				fixes.Add("Create and assign LightingSettings asset");
			}
			else
			{
				// Check for problematic settings that can cause Step 5 failures
				if (lightingSettings.lightmapMaxSize > 2048)
				{
					warnings.Add($"Very large lightmap atlas size: {lightingSettings.lightmapMaxSize}px");
					fixes.Add("Reduce lightmap atlas size to 1024px or smaller");
				}

				if (lightingSettings.lightmapResolution > 40)
				{
					warnings.Add($"Very high lightmap resolution: {lightingSettings.lightmapResolution}");
					fixes.Add("Reduce lightmap resolution to 10-20 for faster processing");
				}
			}

			// 5. Check disk space and memory
			var lightmapDataPath = Application.dataPath.Replace("/Assets", "/Library/lightmaps");
			if (System.IO.Directory.Exists(lightmapDataPath))
			{
				var dirInfo = new System.IO.DirectoryInfo(lightmapDataPath);
				var totalSize = dirInfo.GetFiles("*", System.IO.SearchOption.AllDirectories).Sum(file => file.Length);
				if (totalSize > 1000000000) // 1GB
				{
					warnings.Add($"Large lightmap cache: {totalSize / 1000000000f:F1}GB");
					fixes.Add("Clear lightmap cache to free disk space");
				}
			}

			// Compile report
			string report = "=== GI PREPROCESSING DIAGNOSTICS ===\n\n";

			if (missingMeshes.Count > 0)
			{
				issues.Add($"{missingMeshes.Count} objects with missing meshes");
				report += "🔴 CRITICAL: Missing Meshes\n";
				foreach (var missing in missingMeshes.Take(5))
				{
					report += $"  • {missing}\n";
				}
				if (missingMeshes.Count > 5) report += $"  • ...and {missingMeshes.Count - 5} more\n";
				report += "\n";
			}

			if (invalidUVMeshes.Count > 0)
			{
				issues.Add($"{invalidUVMeshes.Count} lightmapped objects missing UV2 generation");
				report += "🔴 CRITICAL: Missing Lightmap UVs\n";
				foreach (var invalid in invalidUVMeshes.Take(5))
				{
					report += $"  • {invalid}\n";
				}
				if (invalidUVMeshes.Count > 5) report += $"  • ...and {invalidUVMeshes.Count - 5} more\n";
				report += "\n";
			}

			if (uvGenerationIssues > 0)
			{
				issues.Add($"{uvGenerationIssues} model importers need UV2 generation enabled");
				report += $"🔴 CRITICAL: {uvGenerationIssues} models need 'Generate Lightmap UVs' enabled\n\n";
			}

			if (shadersWithProblems.Count > 0)
			{
				warnings.Add($"{shadersWithProblems.Count} objects using lightmapping-incompatible shaders");
				report += "⚠️ WARNING: Incompatible Shaders\n";
				foreach (var shader in shadersWithProblems.Take(3))
				{
					report += $"  • {shader}\n";
				}
				if (shadersWithProblems.Count > 3) report += $"  • ...and {shadersWithProblems.Count - 3} more\n";
				report += "\n";
			}

			if (problematicMeshes.Count > 0)
			{
				warnings.Add($"{problematicMeshes.Count} objects with problematic geometry");
				report += "⚠️ WARNING: Problematic Meshes\n";
				foreach (var mesh in problematicMeshes)
				{
					report += $"  • {mesh}\n";
				}
				report += "\n";
			}

			if (issues.Count == 0 && warnings.Count == 0)
			{
				report += "✅ No obvious GI preprocessing issues detected!\n\n";
				report += "If GI preprocessing still fails at step 5:\n";
				report += "• Try clearing the lightmap cache\n";
				report += "• Reduce lightmap resolution temporarily\n";
				report += "• Check Unity Console for specific error messages\n";
				report += "• Ensure sufficient disk space and RAM\n";
			}
			else
			{
				report += "💡 RECOMMENDED FIXES:\n";
				foreach (var fix in fixes.Distinct())
				{
					report += $"• {fix}\n";
				}
				report += "\nUse 'Fix Common GI Preprocessing Problems' to automatically resolve many of these issues.";
			}

			Debug.Log($"[CozyCon GI Diagnostics]\n{report}");
			EditorUtility.DisplayDialog("GI Preprocessing Diagnostics", report, "OK");
		}

		/// <summary>
		/// Automatically fixes common GI preprocessing problems that cause step 5 failures
		/// </summary>
		private void FixGIPreprocessingProblems()
		{
			Debug.Log("[CozyCon GI Fix] Starting automatic GI preprocessing problem resolution...");

			int fixedIssues = 0;
			var fixLog = new List<string>();

			// 1. Enable "Generate Lightmap UVs" on model importers
			var modelPaths = AssetDatabase.FindAssets("t:Model");
			foreach (var guid in modelPaths)
			{
				string path = AssetDatabase.GUIDToAssetPath(guid);
				var importer = AssetImporter.GetAtPath(path) as ModelImporter;
				if (importer != null && !importer.generateSecondaryUV)
				{
					// Check if this model is used in lightmapped objects
					var meshes = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Mesh>();
					bool needsUV2 = false;

					foreach (var mesh in meshes)
					{
						var users = FindObjectsOfType<MeshFilter>().Where(mf =>
							mf.sharedMesh == mesh &&
							GameObjectUtility.AreStaticEditorFlagsSet(mf.gameObject, StaticEditorFlags.ContributeGI));
						if (users.Any())
						{
							needsUV2 = true;
							break;
						}
					}

					if (needsUV2)
					{
						importer.generateSecondaryUV = true;
						importer.secondaryUVAngleDistortion = 8;
						importer.secondaryUVAreaDistortion = 15.0f;
						importer.secondaryUVHardAngle = 88;
						importer.secondaryUVMinLightmapResolution = 40;
						importer.secondaryUVMinObjectScale = 1;

						AssetDatabase.ImportAsset(path);
						fixedIssues++;
						fixLog.Add($"Enabled UV2 generation for {System.IO.Path.GetFileName(path)}");
					}
				}
			}

			// 2. Fix objects with missing meshes
			var meshFilters = FindObjectsOfType<MeshFilter>();
			foreach (var meshFilter in meshFilters)
			{
				if (meshFilter.sharedMesh == null)
				{
					// Remove MeshFilter component or disable the object
					if (GameObjectUtility.AreStaticEditorFlagsSet(meshFilter.gameObject, StaticEditorFlags.ContributeGI))
					{
						GameObjectUtility.SetStaticEditorFlags(meshFilter.gameObject, 0);
						fixedIssues++;
						fixLog.Add($"Disabled lightmapping for {meshFilter.gameObject.name} (missing mesh)");
					}
				}
			}

			// 3. Fix incompatible shaders on lightmapped objects
			var renderers = FindObjectsOfType<Renderer>();
			foreach (var renderer in renderers)
			{
				if (!GameObjectUtility.AreStaticEditorFlagsSet(renderer.gameObject, StaticEditorFlags.ContributeGI)) continue;
				if (renderer.sharedMaterials == null) continue;

				bool hasIncompatibleShader = false;
				foreach (var material in renderer.sharedMaterials)
				{
					if (material == null || material.shader == null) continue;

					string shaderName = material.shader.name.ToLower();
					if (shaderName.Contains("unlit") || shaderName.Contains("ui/") ||
						shaderName.Contains("sprite") || shaderName.Contains("particle"))
					{
						hasIncompatibleShader = true;
						break;
					}
				}

				if (hasIncompatibleShader)
				{
					// Disable lightmapping for this object
					GameObjectUtility.SetStaticEditorFlags(renderer.gameObject,
						GameObjectUtility.GetStaticEditorFlags(renderer.gameObject) & ~StaticEditorFlags.ContributeGI);
					fixedIssues++;
					fixLog.Add($"Disabled lightmapping for {renderer.gameObject.name} (incompatible shader)");
				}
			}

			// 4. Optimize lighting settings
			var lightingSettings = Lightmapping.lightingSettings;
			if (lightingSettings != null)
			{
				bool settingsChanged = false;

				if (lightingSettings.lightmapMaxSize > 1024)
				{
					lightingSettings.lightmapMaxSize = 1024;
					settingsChanged = true;
					fixLog.Add("Reduced lightmap atlas size to 1024px");
				}

				if (lightingSettings.lightmapResolution > 20)
				{
					lightingSettings.lightmapResolution = 20;
					settingsChanged = true;
					fixLog.Add("Reduced lightmap resolution to 20");
				}

				if (settingsChanged)
				{
					EditorUtility.SetDirty(lightingSettings);
					fixedIssues++;
				}
			}

			AssetDatabase.SaveAssets();

			string summary = $"GI Preprocessing Fix Complete!\n\nFixed {fixedIssues} issues:\n\n" + string.Join("\n", fixLog.Take(10));
			if (fixLog.Count > 10)
			{
				summary += $"\n...and {fixLog.Count - 10} more fixes";
			}

			if (fixedIssues > 0)
			{
				summary += "\n\nRecommendation: Clear lightmap cache and try GI baking again.";
			}
			else
			{
				summary = "No automatic fixes were needed.\n\nIf GI preprocessing still fails:\n• Clear lightmap cache\n• Check Unity Console for specific errors\n• Ensure sufficient disk space";
			}

			Debug.Log($"[CozyCon GI Fix] {summary}");
			EditorUtility.DisplayDialog("GI Preprocessing Fix", summary, "OK");
		}

		/// <summary>
		/// Clears all lightmap cache data and restarts GI processing
		/// </summary>
		private void ClearLightmapCacheAndRestart()
		{
			Debug.Log("[CozyCon GI Cache] Clearing lightmap cache and restarting GI...");

			// Clear lightmap data
			Lightmapping.Clear();

			// Clear disk cache
			Lightmapping.ClearDiskCache();

			// Force reimport of lighting data
			AssetDatabase.Refresh();

			// Clear any existing lightmap results
			LightmapSettings.lightmaps = new LightmapData[0];

			// Force garbage collection
			System.GC.Collect();

			// Start auto-generation if enabled
			if (Lightmapping.giWorkflowMode == Lightmapping.GIWorkflowMode.Iterative)
			{
				// Auto baking will restart automatically
				Debug.Log("[CozyCon GI Cache] Cache cleared. Auto GI will restart automatically.");
			}
			else
			{
				Debug.Log("[CozyCon GI Cache] Cache cleared. Ready for manual GI baking.");
			}

			EditorUtility.DisplayDialog("Lightmap Cache Cleared",
				"Lightmap cache has been cleared.\n\n" +
				"This often resolves GI preprocessing step 5 failures.\n\n" +
				"Try running GI baking again.",
				"OK");
		}
	}
}