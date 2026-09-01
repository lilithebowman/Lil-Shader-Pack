using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.Linq;
using System.IO;

namespace CozyCon.Tools
{
	/// <summary>
	/// Advanced VRChat Mobile shader compatibility analyzer and safe replacement tool.
	/// Designed to prevent invisible materials and preserve visual quality during Quest optimization.
	/// </summary>
	public class VRChatMobileShaderAnalyzer : EditorWindow
	{
		[MenuItem("Lilithe/VRChat Mobile Shader Analyzer")]
		static void Init()
		{
			VRChatMobileShaderAnalyzer window = (VRChatMobileShaderAnalyzer)EditorWindow.GetWindow(typeof(VRChatMobileShaderAnalyzer));
			window.titleContent = new GUIContent("VRChat Mobile Shader Analyzer");
			window.Show();
		}

		private Vector2 scrollPosition;
		private List<MaterialAnalysis> materialAnalyses = new List<MaterialAnalysis>();
		private bool showAnalysisResults = false;
		private bool showOnlyProblematic = false;
		private bool includeInactiveObjects = false;
		private Shader customReplacementShader = null;

		[System.Serializable]
		public class MaterialAnalysis
		{
			public Material material;
			public string materialPath;
			public string currentShader;
			public string recommendedShader;
			public CompatibilityLevel compatibility;
			public List<string> supportedFeatures = new List<string>();
			public List<string> unsupportedFeatures = new List<string>();
			public List<string> warnings = new List<string>();
			public bool canReplaceAutomatically;
			public List<GameObject> usedByObjects = new List<GameObject>();
		}

		public enum CompatibilityLevel
		{
			FullyCompatible,        // Already VRChat Mobile
			HighlyCompatible,       // Can replace with minimal loss
			PartiallyCompatible,    // Will lose some features
			Incompatible,          // Major feature loss or won't work
			Unknown                // Can't analyze
		}

		void OnGUI()
		{
			scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);

			GUILayout.Label("VRChat Mobile Shader Analyzer", EditorStyles.boldLabel);
			EditorGUILayout.Space();

			EditorGUILayout.HelpBox(
				"This tool analyzes all materials in your scene for VRChat Quest compatibility.\n" +
				"It provides safe shader replacement suggestions and prevents broken materials.",
				MessageType.Info);
			EditorGUILayout.Space();

			// Analysis Options
			GUILayout.Label("Analysis Options", EditorStyles.boldLabel);
			includeInactiveObjects = EditorGUILayout.Toggle("Include Inactive Objects", includeInactiveObjects);
			EditorGUILayout.Space();

			// Analysis Buttons
			if (GUILayout.Button("Analyze All Scene Materials"))
			{
				AnalyzeSceneMaterials();
			}

			if (GUILayout.Button("Analyze Selected Objects Only"))
			{
				AnalyzeSelectedMaterials();
			}
			EditorGUILayout.Space();

			// Results Display Options
			if (showAnalysisResults && materialAnalyses.Count > 0)
			{
				GUILayout.Label("Results", EditorStyles.boldLabel);
				showOnlyProblematic = EditorGUILayout.Toggle("Show Only Problematic Materials", showOnlyProblematic);
				EditorGUILayout.Space();

				DisplayAnalysisResults();
				EditorGUILayout.Space();

				// Replacement Actions
				GUILayout.Label("Shader Replacement Actions", EditorStyles.boldLabel);
				EditorGUILayout.HelpBox("These actions create backups before making changes.", MessageType.Info);

				if (GUILayout.Button("Replace Safe Shaders (High Compatibility Only)"))
				{
					ReplaceSafeShaders();
				}

				if (GUILayout.Button("Replace All Compatible Shaders (With Warnings)"))
				{
					ReplaceAllCompatibleShaders();
				}

				if (GUILayout.Button("Create Backup of All Materials"))
				{
					CreateMaterialBackups();
				}

				if (GUILayout.Button("Restore from Backup"))
				{
					RestoreFromBackup();
				}

				EditorGUILayout.Space();

				// Custom Shader Replacement
				GUILayout.Label("Custom Shader Replacement", EditorStyles.boldLabel);
				EditorGUILayout.HelpBox("Replace ALL analyzed materials with a shader of your choice. Use with caution!", MessageType.Warning);

				customReplacementShader = (Shader)EditorGUILayout.ObjectField("Target Shader", customReplacementShader, typeof(Shader), false);

				EditorGUI.BeginDisabledGroup(customReplacementShader == null);
				if (GUILayout.Button($"Replace All Materials with {(customReplacementShader != null ? customReplacementShader.name : "Selected Shader")}"))
				{
					ReplaceAllMaterialsWithCustomShader();
				}
				EditorGUI.EndDisabledGroup();
			}

			EditorGUILayout.EndScrollView();
		}

		/// <summary>
		/// Analyzes all materials used in the current scene
		/// </summary>
		private void AnalyzeSceneMaterials()
		{
			Debug.Log("[VRChat Mobile Analyzer] Starting comprehensive scene material analysis...");

			materialAnalyses.Clear();
			var processedMaterials = new HashSet<Material>();

			// Get all renderers in scene
			var renderers = includeInactiveObjects ?
				Resources.FindObjectsOfTypeAll<Renderer>() :
				FindObjectsOfType<Renderer>();

			foreach (var renderer in renderers)
			{
				// Skip renderers not in the current scene
				if (renderer.gameObject.scene.name == null) continue;
				if (renderer.sharedMaterials == null) continue;

				foreach (var material in renderer.sharedMaterials)
				{
					if (material == null || processedMaterials.Contains(material)) continue;
					processedMaterials.Add(material);

					var analysis = AnalyzeMaterial(material);

					// Track which objects use this material
					analysis.usedByObjects.Add(renderer.gameObject);
					materialAnalyses.Add(analysis);
				}
			}

			// Group materials used by multiple objects
			GroupMaterialUsage();

			showAnalysisResults = true;
			Debug.Log($"[VRChat Mobile Analyzer] Analysis complete. Found {materialAnalyses.Count} unique materials.");

			// Show summary
			ShowAnalysisSummary();
		}

		/// <summary>
		/// Analyzes materials from selected objects only
		/// </summary>
		private void AnalyzeSelectedMaterials()
		{
			if (Selection.gameObjects.Length == 0)
			{
				EditorUtility.DisplayDialog("No Selection", "Please select one or more objects to analyze.", "OK");
				return;
			}

			Debug.Log("[VRChat Mobile Analyzer] Analyzing materials from selected objects...");

			materialAnalyses.Clear();
			var processedMaterials = new HashSet<Material>();

			foreach (var selectedObject in Selection.gameObjects)
			{
				var renderers = selectedObject.GetComponentsInChildren<Renderer>(includeInactiveObjects);
				foreach (var renderer in renderers)
				{
					if (renderer.sharedMaterials == null) continue;

					foreach (var material in renderer.sharedMaterials)
					{
						if (material == null || processedMaterials.Contains(material)) continue;
						processedMaterials.Add(material);

						var analysis = AnalyzeMaterial(material);
						analysis.usedByObjects.Add(renderer.gameObject);
						materialAnalyses.Add(analysis);
					}
				}
			}

			GroupMaterialUsage();
			showAnalysisResults = true;
			Debug.Log($"[VRChat Mobile Analyzer] Analysis complete. Found {materialAnalyses.Count} materials in selection.");
		}

		/// <summary>
		/// Performs detailed analysis of a single material
		/// </summary>
		private MaterialAnalysis AnalyzeMaterial(Material material)
		{
			var analysis = new MaterialAnalysis
			{
				material = material,
				materialPath = AssetDatabase.GetAssetPath(material),
				currentShader = material.shader.name
			};

			// Determine compatibility level and recommended shader
			AnalyzeShaderCompatibility(analysis);

			// Analyze specific features
			AnalyzeMaterialFeatures(analysis);

			// Determine if automatic replacement is safe
			DetermineAutomaticReplacementSafety(analysis);

			return analysis;
		}

		/// <summary>
		/// Analyzes shader compatibility and suggests VRChat Mobile alternatives
		/// </summary>
		private void AnalyzeShaderCompatibility(MaterialAnalysis analysis)
		{
			string shaderName = analysis.currentShader.ToLower();

			// Check if already using VRChat Mobile shaders
			if (shaderName.StartsWith("vrchat/mobile/"))
			{
				analysis.compatibility = CompatibilityLevel.FullyCompatible;
				analysis.recommendedShader = analysis.currentShader;
				analysis.canReplaceAutomatically = false; // Already optimal
				return;
			}

			// FIRST: Check our shader mappings for known shaders
			var shaderMappings = GetVRChatMobileShaderMappings();

			// Direct mapping check - this handles most cases including Silent/Filamented
			foreach (var mapping in shaderMappings)
			{
				if (mapping.Key.Any(pattern => shaderName.Contains(pattern.ToLower())))
				{
					analysis.recommendedShader = mapping.Value.shader;
					analysis.compatibility = mapping.Value.compatibility;

					// Add specific warnings for this mapping
					if (mapping.Value.warnings != null)
					{
						analysis.warnings.AddRange(mapping.Value.warnings);
					}
					return; // Exit early - we found a mapping
				}
			}

			// ONLY FOR UNMAPPED SHADERS: Check material properties for transparency handling
			bool hasTransparency = analysis.material.HasProperty("_Color") &&
				analysis.material.GetColor("_Color").a < 1f;
			bool hasAlphaCutoff = analysis.material.HasProperty("_Cutoff") &&
				analysis.material.GetFloat("_Cutoff") > 0f && analysis.material.GetFloat("_Cutoff") < 1f;

			// Check rendering mode for Standard shader (only if not already mapped above)
			bool isStandardTransparent = false;
			bool isStandardCutout = false;
			if (shaderName == "standard")
			{
				// Check rendering mode
				if (analysis.material.HasProperty("_Mode"))
				{
					float mode = analysis.material.GetFloat("_Mode");
					isStandardTransparent = (mode == 3f); // Transparent mode
					isStandardCutout = (mode == 1f); // Cutout mode
				}
			}

			// Map transparent materials to the ONLY VRChat Mobile transparency option
			if (isStandardTransparent || (hasTransparency && !hasAlphaCutoff))
			{
				analysis.recommendedShader = "VRChat/Mobile/Particles/Alpha Blended";
				analysis.compatibility = CompatibilityLevel.PartiallyCompatible;
				analysis.warnings.Add("CRITICAL: Only particle shader supports transparency in VRChat Mobile");
				analysis.warnings.Add("Material will be converted to particle-based transparency");
				return;
			}

			// For cutout materials, still need particle shader (VRChat Mobile has no cutout variants)
			if (isStandardCutout || hasAlphaCutoff)
			{
				analysis.recommendedShader = "VRChat/Mobile/Particles/Alpha Blended";
				analysis.compatibility = CompatibilityLevel.PartiallyCompatible;
				analysis.warnings.Add("CRITICAL: VRChat Mobile has no cutout shaders");
				analysis.warnings.Add("Alpha cutoff converted to particle transparency");
				return;
			}

			// Check for problematic shader types (for unmapped shaders)
			string[] incompatiblePatterns = {
				"ui/", "sprites/", "particles/", "effects/",
				"hdrp/", "urp/universal render pipeline/",
				"hidden/", "error", "gui/"
			};

			foreach (var pattern in incompatiblePatterns)
			{
				if (shaderName.Contains(pattern))
				{
					analysis.compatibility = CompatibilityLevel.Incompatible;
					analysis.recommendedShader = "VRChat/Mobile/Standard Lite";
					analysis.warnings.Add($"Shader type '{pattern}' is not suitable for VRChat Mobile");
					return;
				}
			}

			// Default case for unknown shaders
			analysis.compatibility = CompatibilityLevel.Unknown;
			analysis.recommendedShader = "VRChat/Mobile/Standard Lite";
			analysis.warnings.Add("Unknown shader compatibility - manual review recommended");
		}

		/// <summary>
		/// Gets mapping between common shaders and VRChat Mobile equivalents
		/// </summary>
		private Dictionary<string[], ShaderMapping> GetVRChatMobileShaderMappings()
		{
			return new Dictionary<string[], ShaderMapping>
			{
				// === OFFICIAL VRCHAT MOBILE SHADERS (already compatible) ===
				{
					new[] { "VRChat/Mobile/Standard Lite" },
					new ShaderMapping {
						shader = "VRChat/Mobile/Standard Lite",
						compatibility = CompatibilityLevel.FullyCompatible
					}
				},
				{
					new[] { "VRChat/Mobile/Diffuse" },
					new ShaderMapping {
						shader = "VRChat/Mobile/Diffuse",
						compatibility = CompatibilityLevel.FullyCompatible
					}
				},
				{
					new[] { "VRChat/Mobile/Bumped Diffuse" },
					new ShaderMapping {
						shader = "VRChat/Mobile/Bumped Diffuse",
						compatibility = CompatibilityLevel.FullyCompatible
					}
				},
				{
					new[] { "VRChat/Mobile/Bumped Mapped Specular" },
					new ShaderMapping {
						shader = "VRChat/Mobile/Bumped Mapped Specular",
						compatibility = CompatibilityLevel.FullyCompatible
					}
				},
				{
					new[] { "VRChat/Mobile/Toon Lit" },
					new ShaderMapping {
						shader = "VRChat/Mobile/Toon Lit",
						compatibility = CompatibilityLevel.FullyCompatible
					}
				},
				{
					new[] { "VRChat/Mobile/Toon Standard" },
					new ShaderMapping {
						shader = "VRChat/Mobile/Toon Standard",
						compatibility = CompatibilityLevel.FullyCompatible
					}
				},
				{
					new[] { "VRChat/Mobile/Toon Standard (Outline)" },
					new ShaderMapping {
						shader = "VRChat/Mobile/Toon Standard (Outline)",
						compatibility = CompatibilityLevel.FullyCompatible
					}
				},
				{
					new[] { "VRChat/Mobile/MatCap Lit" },
					new ShaderMapping {
						shader = "VRChat/Mobile/MatCap Lit",
						compatibility = CompatibilityLevel.FullyCompatible
					}
				},
				{
					new[] { "VRChat/Mobile/Lightmapped" },
					new ShaderMapping {
						shader = "VRChat/Mobile/Lightmapped",
						compatibility = CompatibilityLevel.FullyCompatible
					}
				},
				{
					new[] { "VRChat/Mobile/Skybox" },
					new ShaderMapping {
						shader = "VRChat/Mobile/Skybox",
						compatibility = CompatibilityLevel.FullyCompatible
					}
				},
				{
					new[] { "VRChat/Mobile/Particles/Additive" },
					new ShaderMapping {
						shader = "VRChat/Mobile/Particles/Additive",
						compatibility = CompatibilityLevel.FullyCompatible
					}
				},
				{
					new[] { "VRChat/Mobile/Particles/Multiply" },
					new ShaderMapping {
						shader = "VRChat/Mobile/Particles/Multiply",
						compatibility = CompatibilityLevel.FullyCompatible
					}
				},
				{
					new[] { "VRChat/Mobile/Particles/Alpha Blended" },
					new ShaderMapping {
						shader = "VRChat/Mobile/Particles/Alpha Blended",
						compatibility = CompatibilityLevel.FullyCompatible
					}
				},
				{
					new[] { "VRChat/Mobile/World/Supersampled UI" },
					new ShaderMapping {
						shader = "VRChat/Mobile/World/Supersampled UI",
						compatibility = CompatibilityLevel.FullyCompatible
					}
				},

				// === STANDARD UNITY SHADERS ===
				// Standard shaders (opaque only - transparency handled separately)
				{
					new[] { "Standard", "Standard (Specular setup)" },
					new ShaderMapping {
						shader = "VRChat/Mobile/Standard Lite",
						compatibility = CompatibilityLevel.HighlyCompatible,
						warnings = new[] { "Metallic workflow will be converted to Standard Lite" }
					}
				},

				// Legacy shaders
				{
					new[] { "Legacy Shaders/Bumped Diffuse", "Bumped Diffuse" },
					new ShaderMapping {
						shader = "VRChat/Mobile/Bumped Diffuse",
						compatibility = CompatibilityLevel.FullyCompatible,
						warnings = new[] { "Using VRChat Mobile equivalent for better performance" }
					}
				},

				// Specular shaders
				{
					new[] { "Legacy Shaders/Bumped Specular", "Specular" },
					new ShaderMapping {
						shader = "VRChat/Mobile/Bumped Mapped Specular",
						compatibility = CompatibilityLevel.HighlyCompatible,
						warnings = new[] { "Using VRChat Mobile specular equivalent" }
					}
				},

				// Unlit shaders - no direct VRChat equivalent, use simplest
				{
					new[] { "Unlit/Texture", "Unlit/Color" },
					new ShaderMapping {
						shader = "VRChat/Mobile/Diffuse",
						compatibility = CompatibilityLevel.HighlyCompatible,
						warnings = new[] { "Using VRChat/Mobile/Diffuse for unlit materials" }
					}
				},

				// Toon shaders
				{
					new[] { "Toon/", "Cartoon" },
					new ShaderMapping {
						shader = "VRChat/Mobile/Toon Lit",
						compatibility = CompatibilityLevel.PartiallyCompatible,
						warnings = new[] { "Advanced toon effects may be lost" }
					}
				},

				// Autodesk Interactive
				{
					new[] { "Autodesk Interactive" },
					new ShaderMapping {
						shader = "VRChat/Mobile/Standard Lite",
						compatibility = CompatibilityLevel.PartiallyCompatible,
						warnings = new[] { "Autodesk Interactive features not supported on Quest" }
					}
				},

				// Silent/Filamented - Already Quest-optimized, no replacement needed
				{
					new[] { "Silent/Filamented" },
					new ShaderMapping {
						shader = "Silent/Filamented",
						compatibility = CompatibilityLevel.FullyCompatible
					}
				}
			};
		}

		[System.Serializable]
		public class ShaderMapping
		{
			public string shader;
			public CompatibilityLevel compatibility;
			public string[] warnings;
		}

		/// <summary>
		/// Analyzes specific material features and their VRChat Mobile support
		/// </summary>
		private void AnalyzeMaterialFeatures(MaterialAnalysis analysis)
		{
			var material = analysis.material;

			// Check common material properties
			var features = new Dictionary<string, bool>
			{
				{ "Main Texture (_MainTex)", material.HasProperty("_MainTex") },
				{ "Normal Map (_BumpMap)", material.HasProperty("_BumpMap") },
				{ "Emission (_EmissionMap)", material.HasProperty("_EmissionMap") },
				{ "Transparency (_Color.a)", material.HasProperty("_Color") && material.GetColor("_Color").a < 1f },
				{ "Cutoff (_Cutoff)", material.HasProperty("_Cutoff") },
				{ "Metallic (_Metallic)", material.HasProperty("_Metallic") },
				{ "Smoothness (_Glossiness)", material.HasProperty("_Glossiness") },
				{ "Occlusion (_OcclusionMap)", material.HasProperty("_OcclusionMap") },
				{ "Detail Maps", material.HasProperty("_DetailAlbedoMap") || material.HasProperty("_DetailNormalMap") }
			};

			// Categorize features based on VRChat Mobile support
			var supportedInMobile = new HashSet<string>
			{
				"Main Texture (_MainTex)", "Normal Map (_BumpMap)", "Transparency (_Color.a)", "Cutoff (_Cutoff)",
				"Metallic (_Metallic)", "Smoothness (_Glossiness)" // Standard Lite supports these
			};

			var partiallySupported = new HashSet<string>
			{
				"Emission (_EmissionMap)" // Supported but limited
			};

			foreach (var feature in features)
			{
				if (!feature.Value) continue; // Feature not present

				if (supportedInMobile.Contains(feature.Key))
				{
					analysis.supportedFeatures.Add(feature.Key);
				}
				else if (partiallySupported.Contains(feature.Key))
				{
					analysis.supportedFeatures.Add(feature.Key + " (Limited)");
					analysis.warnings.Add($"{feature.Key} support is limited in VRChat Mobile");
				}
				else
				{
					analysis.unsupportedFeatures.Add(feature.Key);
					analysis.warnings.Add($"{feature.Key} will be lost when converting to VRChat Mobile");
				}
			}

			// Check texture sizes
			CheckTextureCompatibility(analysis);
		}

		/// <summary>
		/// Checks texture compatibility with VRChat Quest limitations
		/// </summary>
		private void CheckTextureCompatibility(MaterialAnalysis analysis)
		{
			var material = analysis.material;
			string[] textureProperties = { "_MainTex", "_BumpMap", "_EmissionMap", "_OcclusionMap", "_DetailAlbedoMap" };

			foreach (var propName in textureProperties)
			{
				if (!material.HasProperty(propName)) continue;

				var texture = material.GetTexture(propName) as Texture2D;
				if (texture == null) continue;

				// Check texture size
				if (texture.width > 1024 || texture.height > 1024)
				{
					analysis.warnings.Add($"Texture {propName} is {texture.width}x{texture.height} - Quest recommends ≤1024px");
				}

				// Check texture format
				string assetPath = AssetDatabase.GetAssetPath(texture);
				if (!string.IsNullOrEmpty(assetPath))
				{
					var importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
					if (importer != null)
					{
						var platformSettings = importer.GetPlatformTextureSettings("Android");
						if (!platformSettings.overridden)
						{
							analysis.warnings.Add($"Texture {propName} lacks Android-specific compression settings");
						}
					}
				}
			}
		}

		/// <summary>
		/// Determines if automatic shader replacement is safe for this material
		/// </summary>
		private void DetermineAutomaticReplacementSafety(MaterialAnalysis analysis)
		{
			// Only allow automatic replacement for highly compatible shaders with minimal warnings
			analysis.canReplaceAutomatically =
				analysis.compatibility == CompatibilityLevel.HighlyCompatible &&
				analysis.warnings.Count <= 1 &&
				analysis.unsupportedFeatures.Count <= 2;

			// Never auto-replace transparency materials - too risky
			bool hasTransparency = analysis.material.HasProperty("_Color") &&
				analysis.material.GetColor("_Color").a < 1f;
			bool hasAlphaCutoff = analysis.material.HasProperty("_Cutoff") &&
				analysis.material.GetFloat("_Cutoff") > 0f;
			bool isParticleShader = analysis.recommendedShader.Contains("Particles");

			if ((hasTransparency || hasAlphaCutoff) && isParticleShader)
			{
				analysis.canReplaceAutomatically = false;
				analysis.warnings.Add("MANUAL REVIEW REQUIRED: Transparency conversion to particle shader");
			}
		}

		/// <summary>
		/// Groups materials that are used by multiple objects
		/// </summary>
		private void GroupMaterialUsage()
		{
			// Find duplicate materials and merge their object lists
			for (int i = 0; i < materialAnalyses.Count; i++)
			{
				for (int j = i + 1; j < materialAnalyses.Count; j++)
				{
					if (materialAnalyses[i].material == materialAnalyses[j].material)
					{
						materialAnalyses[i].usedByObjects.AddRange(materialAnalyses[j].usedByObjects);
						materialAnalyses.RemoveAt(j);
						j--;
					}
				}
			}
		}

		/// <summary>
		/// Displays analysis results in the GUI
		/// </summary>
		private void DisplayAnalysisResults()
		{
			var materialsToShow = showOnlyProblematic ?
				materialAnalyses.Where(m => m.compatibility != CompatibilityLevel.FullyCompatible).ToList() :
				materialAnalyses;

			EditorGUILayout.LabelField($"Showing {materialsToShow.Count()} of {materialAnalyses.Count} materials");

			foreach (var analysis in materialsToShow)
			{
				DisplayMaterialAnalysis(analysis);
			}
		}

		/// <summary>
		/// Displays a single material analysis
		/// </summary>
		private void DisplayMaterialAnalysis(MaterialAnalysis analysis)
		{
			EditorGUILayout.BeginVertical("box");

			// Header with material name and compatibility
			EditorGUILayout.BeginHorizontal();

			// Compatibility color coding
			var originalColor = GUI.color;
			switch (analysis.compatibility)
			{
				case CompatibilityLevel.FullyCompatible:
					GUI.color = Color.green;
					break;
				case CompatibilityLevel.HighlyCompatible:
					GUI.color = Color.yellow;
					break;
				case CompatibilityLevel.PartiallyCompatible:
					GUI.color = new Color(1f, 0.5f, 0f); // Orange
					break;
				case CompatibilityLevel.Incompatible:
					GUI.color = Color.red;
					break;
				default:
					GUI.color = Color.gray;
					break;
			}

			EditorGUILayout.LabelField($"● {analysis.material.name}", EditorStyles.boldLabel);
			GUI.color = originalColor;

			EditorGUILayout.LabelField($"({analysis.compatibility})", GUILayout.Width(120));
			EditorGUILayout.EndHorizontal();

			// Current and recommended shader
			EditorGUILayout.LabelField($"Current: {analysis.currentShader}");
			if (analysis.currentShader != analysis.recommendedShader)
			{
				EditorGUILayout.LabelField($"Recommended: {analysis.recommendedShader}");
			}

			// Used by objects
			if (analysis.usedByObjects.Count > 0)
			{
				EditorGUILayout.LabelField($"Used by {analysis.usedByObjects.Count} object(s):");
				EditorGUI.indentLevel++;
				foreach (var obj in analysis.usedByObjects.Take(3))
				{
					EditorGUILayout.ObjectField(obj, typeof(GameObject), true);
				}
				if (analysis.usedByObjects.Count > 3)
				{
					EditorGUILayout.LabelField($"...and {analysis.usedByObjects.Count - 3} more");
				}
				EditorGUI.indentLevel--;
			}

			// Features
			if (analysis.supportedFeatures.Count > 0)
			{
				EditorGUILayout.LabelField("✅ Supported Features: " + string.Join(", ", analysis.supportedFeatures));
			}

			if (analysis.unsupportedFeatures.Count > 0)
			{
				EditorGUILayout.LabelField("❌ Will be lost: " + string.Join(", ", analysis.unsupportedFeatures));
			}

			// Warnings
			if (analysis.warnings.Count > 0)
			{
				EditorGUILayout.LabelField("⚠️ Warnings:");
				EditorGUI.indentLevel++;
				foreach (var warning in analysis.warnings)
				{
					EditorGUILayout.LabelField($"• {warning}", EditorStyles.wordWrappedLabel);
				}
				EditorGUI.indentLevel--;
			}

			// Individual replace button for manual control
			EditorGUILayout.BeginHorizontal();
			if (analysis.compatibility != CompatibilityLevel.FullyCompatible &&
				analysis.compatibility != CompatibilityLevel.Incompatible)
			{
				if (GUILayout.Button($"Replace with {Path.GetFileName(analysis.recommendedShader)}", GUILayout.Width(200)))
				{
					ReplaceSingleMaterial(analysis);
				}
			}

			if (GUILayout.Button("Select in Project", GUILayout.Width(120)))
			{
				Selection.activeObject = analysis.material;
				EditorGUIUtility.PingObject(analysis.material);
			}
			EditorGUILayout.EndHorizontal();

			EditorGUILayout.EndVertical();
			EditorGUILayout.Space();
		}

		/// <summary>
		/// Shows a summary of the analysis results
		/// </summary>
		private void ShowAnalysisSummary()
		{
			var summary = new Dictionary<CompatibilityLevel, int>();
			foreach (var analysis in materialAnalyses)
			{
				if (!summary.ContainsKey(analysis.compatibility))
					summary[analysis.compatibility] = 0;
				summary[analysis.compatibility]++;
			}

			string summaryText = "=== VRChat Mobile Shader Analysis Summary ===\n\n";

			foreach (var kvp in summary)
			{
				string icon = GetCompatibilityIcon(kvp.Key);
				summaryText += $"{icon} {kvp.Key}: {kvp.Value} materials\n";
			}

			int canAutoReplace = materialAnalyses.Count(m => m.canReplaceAutomatically);
			summaryText += $"\n✅ {canAutoReplace} materials can be safely auto-replaced\n";
			summaryText += $"⚠️ {materialAnalyses.Count - canAutoReplace} materials need manual review";

			Debug.Log($"[VRChat Mobile Analyzer]\n{summaryText}");
			EditorUtility.DisplayDialog("Analysis Complete", summaryText, "OK");
		}

		/// <summary>
		/// Gets an icon for the compatibility level
		/// </summary>
		private string GetCompatibilityIcon(CompatibilityLevel level)
		{
			switch (level)
			{
				case CompatibilityLevel.FullyCompatible: return "✅";
				case CompatibilityLevel.HighlyCompatible: return "🟡";
				case CompatibilityLevel.PartiallyCompatible: return "🟠";
				case CompatibilityLevel.Incompatible: return "🔴";
				default: return "❓";
			}
		}

		/// <summary>
		/// Replaces only the safest shaders automatically
		/// </summary>
		private void ReplaceSafeShaders()
		{
			var safeToReplace = materialAnalyses.Where(m => m.canReplaceAutomatically).ToList();

			if (safeToReplace.Count == 0)
			{
				EditorUtility.DisplayDialog("No Safe Replacements",
					"No materials were found that can be safely auto-replaced.\n\n" +
					"Use 'Replace All Compatible Shaders' for materials that need manual review.",
					"OK");
				return;
			}

			bool confirm = EditorUtility.DisplayDialog("Confirm Safe Replacement",
				$"Replace {safeToReplace.Count} materials with VRChat Mobile shaders?\n\n" +
				"These materials have minimal risk of visual changes.\n" +
				"Backups will be created automatically.",
				"Replace", "Cancel");

			if (!confirm) return;

			CreateMaterialBackups();
			int replaced = 0;

			foreach (var analysis in safeToReplace)
			{
				if (ReplaceMaterialShader(analysis))
				{
					replaced++;
				}
			}

			Debug.Log($"[VRChat Mobile Analyzer] Successfully replaced {replaced} materials with VRChat Mobile shaders.");
			EditorUtility.DisplayDialog("Replacement Complete",
				$"Successfully replaced {replaced} materials.\n\n" +
				"Check the Console for detailed logs.\n" +
				"Use 'Restore from Backup' if needed.",
				"OK");

			// Refresh analysis
			AnalyzeSceneMaterials();
		}

		/// <summary>
		/// Replaces all compatible shaders with user confirmation for each risk level
		/// </summary>
		private void ReplaceAllCompatibleShaders()
		{
			var compatibleMaterials = materialAnalyses.Where(m =>
				m.compatibility == CompatibilityLevel.HighlyCompatible ||
				m.compatibility == CompatibilityLevel.PartiallyCompatible).ToList();

			if (compatibleMaterials.Count == 0)
			{
				EditorUtility.DisplayDialog("No Compatible Materials",
					"No materials found that can be replaced with VRChat Mobile shaders.",
					"OK");
				return;
			}

			bool confirm = EditorUtility.DisplayDialog("Confirm Shader Replacement",
				$"Replace {compatibleMaterials.Count} materials with VRChat Mobile shaders?\n\n" +
				"⚠️ Some materials may lose visual features.\n" +
				"⚠️ Review warnings for each material carefully.\n\n" +
				"Backups will be created automatically.",
				"Replace All", "Cancel");

			if (!confirm) return;

			CreateMaterialBackups();
			int replaced = 0;

			foreach (var analysis in compatibleMaterials)
			{
				if (ReplaceMaterialShader(analysis))
				{
					replaced++;
				}
			}

			Debug.Log($"[VRChat Mobile Analyzer] Replaced {replaced} materials. Check materials for visual changes.");
			EditorUtility.DisplayDialog("Replacement Complete",
				$"Replaced {replaced} materials with VRChat Mobile shaders.\n\n" +
				"⚠️ Please check your scene for visual changes.\n" +
				"⚠️ Use 'Restore from Backup' if needed.\n\n" +
				"Run analysis again to see updated results.",
				"OK");

			// Refresh analysis
			AnalyzeSceneMaterials();
		}

		/// <summary>
		/// Replaces a single material's shader safely
		/// </summary>
		private void ReplaceSingleMaterial(MaterialAnalysis analysis)
		{
			bool confirm = EditorUtility.DisplayDialog("Confirm Shader Replacement",
				$"Replace shader on '{analysis.material.name}'?\n\n" +
				$"From: {analysis.currentShader}\n" +
				$"To: {analysis.recommendedShader}\n\n" +
				(analysis.warnings.Count > 0 ?
					"⚠️ Warnings:\n" + string.Join("\n", analysis.warnings) + "\n\n" : "") +
				"A backup will be created automatically.",
				"Replace", "Cancel");

			if (!confirm) return;

			// Create backup for just this material
			CreateSingleMaterialBackup(analysis.material);

			if (ReplaceMaterialShader(analysis))
			{
				Debug.Log($"[VRChat Mobile Analyzer] Successfully replaced shader on {analysis.material.name}");
				EditorUtility.DisplayDialog("Replacement Complete",
					$"Successfully replaced shader on '{analysis.material.name}'.\n\n" +
					"Check the material for visual changes.",
					"OK");

				// Refresh analysis for this material
				var newAnalysis = AnalyzeMaterial(analysis.material);
				int index = materialAnalyses.FindIndex(m => m.material == analysis.material);
				if (index >= 0)
				{
					materialAnalyses[index] = newAnalysis;
				}
			}
		}

		/// <summary>
		/// Actually performs the shader replacement with property migration
		/// </summary>
		private bool ReplaceMaterialShader(MaterialAnalysis analysis)
		{
			try
			{
				var material = analysis.material;
				var newShader = Shader.Find(analysis.recommendedShader);

				if (newShader == null)
				{
					Debug.LogError($"[VRChat Mobile Analyzer] Shader not found: {analysis.recommendedShader}");
					return false;
				}

				// Store current properties before changing shader
				var oldProperties = StoreCurrentMaterialProperties(material);

				// Change shader
				material.shader = newShader;

				// Migrate compatible properties
				MigrateMaterialProperties(material, oldProperties, analysis);

				// Mark as dirty to save changes
				EditorUtility.SetDirty(material);

				Debug.Log($"[VRChat Mobile Analyzer] Replaced {analysis.currentShader} with {analysis.recommendedShader} on {material.name}");
				return true;
			}
			catch (System.Exception e)
			{
				Debug.LogError($"[VRChat Mobile Analyzer] Failed to replace shader on {analysis.material.name}: {e.Message}");
				return false;
			}
		}

		/// <summary>
		/// Stores current material properties before shader change
		/// </summary>
		private Dictionary<string, object> StoreCurrentMaterialProperties(Material material)
		{
			var properties = new Dictionary<string, object>();

			// Common properties to preserve
			string[] textureProps = { "_MainTex", "_BumpMap", "_EmissionMap", "_OcclusionMap" };
			string[] colorProps = { "_Color", "_EmissionColor", "_SpecColor" };
			string[] floatProps = { "_Cutoff", "_Glossiness", "_Metallic", "_BumpScale" };

			foreach (var prop in textureProps)
			{
				if (material.HasProperty(prop))
				{
					properties[prop] = material.GetTexture(prop);
				}
			}

			foreach (var prop in colorProps)
			{
				if (material.HasProperty(prop))
				{
					properties[prop] = material.GetColor(prop);
				}
			}

			foreach (var prop in floatProps)
			{
				if (material.HasProperty(prop))
				{
					properties[prop] = material.GetFloat(prop);
				}
			}

			return properties;
		}

		/// <summary>
		/// Migrates properties from old shader to new VRChat Mobile shader
		/// </summary>
		private void MigrateMaterialProperties(Material material, Dictionary<string, object> oldProperties, MaterialAnalysis analysis)
		{
			// Property mappings for VRChat Mobile shaders
			var propertyMappings = new Dictionary<string, string>
			{
				{ "_MainTex", "_MainTex" },
				{ "_Color", "_Color" },
				{ "_BumpMap", "_BumpMap" },
				{ "_BumpScale", "_BumpScale" },
				{ "_Cutoff", "_Cutoff" }
			};

			foreach (var mapping in propertyMappings)
			{
				string oldProp = mapping.Key;
				string newProp = mapping.Value;

				if (oldProperties.ContainsKey(oldProp) && material.HasProperty(newProp))
				{
					try
					{
						if (oldProperties[oldProp] is Texture texture)
						{
							material.SetTexture(newProp, texture);
						}
						else if (oldProperties[oldProp] is Color color)
						{
							material.SetColor(newProp, color);
						}
						else if (oldProperties[oldProp] is float floatValue)
						{
							material.SetFloat(newProp, floatValue);
						}
					}
					catch (System.Exception e)
					{
						Debug.LogWarning($"[VRChat Mobile Analyzer] Could not migrate property {oldProp} on {material.name}: {e.Message}");
					}
				}
			}

			// Special handling for transparency
			if (analysis.recommendedShader.Contains("Transparent") && oldProperties.ContainsKey("_Color"))
			{
				var color = (Color)oldProperties["_Color"];
				if (color.a < 1f && material.HasProperty("_Color"))
				{
					material.SetColor("_Color", color);
				}
			}
		}

		/// <summary>
		/// Creates backups of all materials before replacement
		/// </summary>
		private void CreateMaterialBackups()
		{
			string backupFolder = "Assets/Generated/MaterialBackups";
			Directory.CreateDirectory(backupFolder);

			foreach (var analysis in materialAnalyses)
			{
				CreateSingleMaterialBackup(analysis.material);
			}

			AssetDatabase.SaveAssets();
			Debug.Log($"[VRChat Mobile Analyzer] Created backups for {materialAnalyses.Count} materials in {backupFolder}");
		}

		/// <summary>
		/// Creates a backup of a single material
		/// </summary>
		private void CreateSingleMaterialBackup(Material material)
		{
			string backupFolder = "Assets/Generated/MaterialBackups";
			Directory.CreateDirectory(backupFolder);

			string timestamp = System.DateTime.Now.ToString("yyyyMMdd_HHmmss");
			string backupPath = $"{backupFolder}/{material.name}_backup_{timestamp}.mat";

			// Create a copy of the material
			var backup = Object.Instantiate(material);
			backup.name = material.name + "_backup";

			AssetDatabase.CreateAsset(backup, backupPath);
			Debug.Log($"[VRChat Mobile Analyzer] Created backup: {backupPath}");
		}

		/// <summary>
		/// Restores materials from the most recent backup
		/// </summary>
		private void RestoreFromBackup()
		{
			string backupFolder = "Assets/Generated/MaterialBackups";

			if (!Directory.Exists(backupFolder))
			{
				EditorUtility.DisplayDialog("No Backups Found",
					"No material backups found. Create backups before making changes.",
					"OK");
				return;
			}

			// Find all backup materials
			var backupGuids = AssetDatabase.FindAssets("t:Material", new[] { backupFolder });
			if (backupGuids.Length == 0)
			{
				EditorUtility.DisplayDialog("No Backups Found",
					"No material backups found in the backup folder.",
					"OK");
				return;
			}

			bool confirm = EditorUtility.DisplayDialog("Confirm Restore",
				$"Restore {backupGuids.Length} materials from backup?\n\n" +
				"This will overwrite current material settings with the most recent backup.",
				"Restore", "Cancel");

			if (!confirm) return;

			int restored = 0;
			foreach (var guid in backupGuids)
			{
				string backupPath = AssetDatabase.GUIDToAssetPath(guid);
				var backupMaterial = AssetDatabase.LoadAssetAtPath<Material>(backupPath);

				if (backupMaterial != null)
				{
					// Find original material by name (remove "_backup_timestamp")
					string originalName = backupMaterial.name.Split('_')[0];
					var originalMaterial = materialAnalyses.FirstOrDefault(m =>
						m.material.name == originalName)?.material;

					if (originalMaterial != null)
					{
						// Copy properties from backup to original
						EditorUtility.CopySerialized(backupMaterial, originalMaterial);
						EditorUtility.SetDirty(originalMaterial);
						restored++;
					}
				}
			}

			AssetDatabase.SaveAssets();
			Debug.Log($"[VRChat Mobile Analyzer] Restored {restored} materials from backup");
			EditorUtility.DisplayDialog("Restore Complete",
				$"Restored {restored} materials from backup.\n\n" +
				"Run analysis again to see current state.",
				"OK");
		}

		/// <summary>
		/// Replaces all materials in the scene with a user-selected shader
		/// </summary>
		private void ReplaceAllMaterialsWithCustomShader()
		{
			if (customReplacementShader == null)
			{
				EditorUtility.DisplayDialog("No Shader Selected",
					"Please select a target shader first.",
					"OK");
				return;
			}

			// Safety confirmation
			bool confirm = EditorUtility.DisplayDialog("Confirm Custom Shader Replacement",
				$"Replace ALL materials in the scene with '{customReplacementShader.name}'?\n\n" +
				"⚠️ WARNING: This will affect ALL materials, not just analyzed ones.\n" +
				"⚠️ This action will create automatic backups but may cause visual changes.\n" +
				"⚠️ Test thoroughly before using in production.\n\n" +
				"Materials will be backed up automatically.",
				"Replace All", "Cancel");

			if (!confirm) return;

			// Create backup first
			CreateMaterialBackups();

			var allMaterials = new List<Material>();

			// Find all materials in the scene
			var renderers = FindObjectsOfType<Renderer>(includeInactiveObjects);
			foreach (var renderer in renderers)
			{
				if (renderer.sharedMaterials != null)
				{
					foreach (var material in renderer.sharedMaterials)
					{
						if (material != null && !allMaterials.Contains(material))
						{
							allMaterials.Add(material);
						}
					}
				}
			}

			int replacedCount = 0;
			int skippedCount = 0;
			var errorMessages = new List<string>();

			foreach (var material in allMaterials)
			{
				try
				{
					if (material.shader == customReplacementShader)
					{
						skippedCount++; // Already using target shader
						continue;
					}

					// Store original shader for logging
					string originalShader = material.shader.name;

					// Migrate basic properties if possible
					var originalProperties = new Dictionary<string, object>();
					MigrateBasicProperties(material, originalProperties);

					// Change shader
					material.shader = customReplacementShader;

					// Restore compatible properties
					foreach (var prop in originalProperties)
					{
						try
						{
							if (material.HasProperty(prop.Key))
							{
								if (prop.Value is Color color)
									material.SetColor(prop.Key, color);
								else if (prop.Value is float floatVal)
									material.SetFloat(prop.Key, floatVal);
								else if (prop.Value is Texture texture)
									material.SetTexture(prop.Key, texture);
								else if (prop.Value is Vector4 vector)
									material.SetVector(prop.Key, vector);
							}
						}
						catch (System.Exception e)
						{
							// Property couldn't be set, skip it
							Debug.LogWarning($"[Custom Shader Replacement] Could not set property {prop.Key} on {material.name}: {e.Message}");
						}
					}

					EditorUtility.SetDirty(material);
					replacedCount++;

					Debug.Log($"[Custom Shader Replacement] {material.name}: {originalShader} → {customReplacementShader.name}");
				}
				catch (System.Exception e)
				{
					errorMessages.Add($"Failed to replace shader on {material.name}: {e.Message}");
					skippedCount++;
				}
			}

			AssetDatabase.SaveAssets();

			// Show results
			string summary = $"Custom Shader Replacement Complete!\n\n" +
							$"✅ Successfully replaced: {replacedCount} materials\n" +
							$"⏭️ Skipped (already using target): {skippedCount} materials\n" +
							$"📦 Backup created in: Assets/Generated/MaterialBackups/\n\n" +
							$"Target shader: {customReplacementShader.name}";

			if (errorMessages.Count > 0)
			{
				summary += $"\n\n⚠️ Errors: {errorMessages.Count}";
				foreach (var error in errorMessages.Take(3))
				{
					summary += $"\n• {error}";
				}
				if (errorMessages.Count > 3)
				{
					summary += $"\n• ...and {errorMessages.Count - 3} more errors";
				}
			}

			summary += "\n\n💡 Tip: Use 'Restore from Backup' if you need to undo these changes.";

			Debug.Log($"[Custom Shader Replacement] {summary}");
			EditorUtility.DisplayDialog("Custom Shader Replacement Complete", summary, "OK");

			// Refresh analysis if we have results
			if (showAnalysisResults)
			{
				AnalyzeSceneMaterials();
			}
		}

		/// <summary>
		/// Migrates basic shader properties that are commonly supported
		/// </summary>
		private void MigrateBasicProperties(Material material, Dictionary<string, object> properties)
		{
			// Common properties that most shaders support
			string[] commonProperties = {
				"_MainTex", "_Color", "_BaseMap", "_BaseColor",
				"_BumpMap", "_NormalMap", "_MetallicGlossMap",
				"_EmissionColor", "_EmissionMap", "_Cutoff",
				"_Glossiness", "_Metallic", "_Smoothness"
			};

			foreach (var propName in commonProperties)
			{
				if (material.HasProperty(propName))
				{
					try
					{
						// Try to get the property value
						var shader = material.shader;
						for (int i = 0; i < ShaderUtil.GetPropertyCount(shader); i++)
						{
							if (ShaderUtil.GetPropertyName(shader, i) == propName)
							{
								var propType = ShaderUtil.GetPropertyType(shader, i);
								switch (propType)
								{
									case ShaderUtil.ShaderPropertyType.Color:
										properties[propName] = material.GetColor(propName);
										break;
									case ShaderUtil.ShaderPropertyType.Vector:
										properties[propName] = material.GetVector(propName);
										break;
									case ShaderUtil.ShaderPropertyType.Float:
									case ShaderUtil.ShaderPropertyType.Range:
										properties[propName] = material.GetFloat(propName);
										break;
									case ShaderUtil.ShaderPropertyType.TexEnv:
										properties[propName] = material.GetTexture(propName);
										break;
								}
								break;
							}
						}
					}
					catch
					{
						// Property couldn't be read, skip it
					}
				}
			}
		}
	}
}