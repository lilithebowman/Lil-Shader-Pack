using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.Linq;
using System.IO;

namespace CozyCon.Tools
{
	/// <summary>
	/// Advanced LOD generator with mesh decimation and billboard creation
	/// </summary>
	public class LODGenerator : EditorWindow
	{
		[MenuItem("Lilithe/Performance/LOD Generator")]
		public static void ShowWindow()
		{
			GetWindow<LODGenerator>("LOD Generator");
		}

		// UI State
		private GameObject selectedModel;
		private Vector2 scrollPosition;
		private bool showResults = false;
		private LODGenerationResult generationResult;

		// LOD/Generation Settings
		private bool generateLOD1 = true;
		private bool generateLOD2 = true;
		private bool generateBillboards = false;
		private bool useVRChatOptimizedDistances = true;
		private bool preserveUVs = true;
		private bool preserveNormals = true;
		private float decimationAngle = 45f;
		private bool generateColliders = false;
		private bool createPrefab = true;
		private string prefabSavePath = "Assets/Generated/LOD Prefabs/";
		private bool removeFromSceneAfterPrefab = true;
		private LODFadeMode selectedFadeMode = LODFadeMode.None;
		private float lod0Distance = 0.8f;
		private float lod1Distance = 0.5f;
		private float lod2Distance = 0.25f;
		private float billboardDistance = 0.1f;
		private int billboardResolution = 512;
		private int billboardAngles = 8;
		private bool billboardTransparency = true;
		private string billboardShader = "Standard";

		void OnGUI()
		{
			scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);
			GUILayout.Label("LOD Generator", EditorStyles.boldLabel);
			EditorGUILayout.Space();
			DrawModelSelection();
			DrawLODSettings();
			DrawBillboardSettings();
			DrawDistanceSettings();
			DrawGenerationButtons();
			if (showResults && generationResult != null)
			{
				DrawResults();
			}
			EditorGUILayout.EndScrollView();
		}

		private void DrawModelSelection()
		{
			GUILayout.Label("Model Selection", EditorStyles.boldLabel);
			EditorGUILayout.BeginVertical(GUI.skin.box);
			selectedModel = (GameObject)EditorGUILayout.ObjectField("Target Model", selectedModel, typeof(GameObject), true);
			if (selectedModel == null && Selection.activeGameObject != null)
			{
				if (GUILayout.Button("Use Selected GameObject"))
				{
					selectedModel = Selection.activeGameObject;
				}
			}
			if (selectedModel != null)
			{
				var meshInfo = MeshProcessor.AnalyzeMesh(selectedModel);
				if (meshInfo != null)
				{
					EditorGUILayout.LabelField($"Triangles: {meshInfo.triangleCount:N0}");
					EditorGUILayout.LabelField($"Vertices: {meshInfo.vertexCount:N0}");
					EditorGUILayout.LabelField($"Estimated Size: {meshInfo.sizeKB:F1} KB");
				}
				else
				{
					EditorGUILayout.HelpBox("Selected object has no mesh components.", MessageType.Warning);
				}
			}
			EditorGUILayout.EndVertical();
			EditorGUILayout.Space();
		}

		private void DrawLODSettings()
		{
			GUILayout.Label("LOD Generation Settings", EditorStyles.boldLabel);
			EditorGUILayout.BeginVertical(GUI.skin.box);
			generateLOD1 = EditorGUILayout.Toggle("Generate LOD1 (50% polygons)", generateLOD1);
			generateLOD2 = EditorGUILayout.Toggle("Generate LOD2 (25% polygons)", generateLOD2);
			EditorGUILayout.Space();
			EditorGUILayout.LabelField("Mesh Preservation Options:", EditorStyles.boldLabel);
			preserveUVs = EditorGUILayout.Toggle("Preserve UV Coordinates", preserveUVs);
			preserveNormals = EditorGUILayout.Toggle("Preserve Normals", preserveNormals);
			decimationAngle = EditorGUILayout.Slider("Decimation Angle (°)", decimationAngle, 0f, 45f);
			EditorGUILayout.HelpBox($"Do not collapse edges where adjacent triangle normals differ by more than {decimationAngle:F1}°. Useful to preserve creases.", MessageType.Info);
			generateColliders = EditorGUILayout.Toggle("Generate LOD Colliders", generateColliders);
			EditorGUILayout.Space();
			EditorGUILayout.LabelField("Output Options:", EditorStyles.boldLabel);
			createPrefab = EditorGUILayout.Toggle("Create Prefab", createPrefab);
			if (createPrefab)
			{
				EditorGUILayout.BeginHorizontal();
				prefabSavePath = EditorGUILayout.TextField("Prefab Save Path:", prefabSavePath);
				if (GUILayout.Button("Browse", GUILayout.Width(60)))
				{
					string selectedPath = EditorUtility.SaveFolderPanel("Select Prefab Save Location", "Assets", "");
					if (!string.IsNullOrEmpty(selectedPath))
					{
						if (selectedPath.StartsWith(Application.dataPath))
						{
							prefabSavePath = "Assets" + selectedPath.Substring(Application.dataPath.Length) + "/";
						}
					}
				}
				EditorGUILayout.EndHorizontal();
				if (!string.IsNullOrEmpty(prefabSavePath) && !prefabSavePath.StartsWith("Assets/"))
				{
					EditorGUILayout.HelpBox("Prefab path must be within the Assets folder.", MessageType.Warning);
				}
				removeFromSceneAfterPrefab = EditorGUILayout.Toggle("Remove from Scene after Prefab", removeFromSceneAfterPrefab);
			}
			EditorGUILayout.EndVertical();
			EditorGUILayout.Space();
		}

		private void DrawBillboardSettings()
		{
			GUILayout.Label("Billboard Settings", EditorStyles.boldLabel);
			EditorGUILayout.BeginVertical(GUI.skin.box);
			generateBillboards = EditorGUILayout.Toggle("Generate Billboards", generateBillboards);
			if (generateBillboards)
			{
				EditorGUI.indentLevel++;
				billboardResolution = EditorGUILayout.IntSlider("Resolution", billboardResolution, 128, 2048);
				billboardAngles = EditorGUILayout.IntSlider("Capture Angles", billboardAngles, 4, 16);
				billboardTransparency = EditorGUILayout.Toggle("Alpha Transparency", billboardTransparency);
				billboardShader = EditorGUILayout.TextField("Billboard Shader", billboardShader);
				EditorGUI.indentLevel--;
				EditorGUILayout.HelpBox($"Will capture {billboardAngles} angles ({360f / billboardAngles:F0}° apart)", MessageType.Info);
			}
			EditorGUILayout.EndVertical();
			EditorGUILayout.Space();
		}

		private void DrawDistanceSettings()
		{
			GUILayout.Label("LOD Distances", EditorStyles.boldLabel);
			EditorGUILayout.BeginVertical(GUI.skin.box);
			useVRChatOptimizedDistances = EditorGUILayout.Toggle("Use VRChat Optimized Distances", useVRChatOptimizedDistances);
			if (useVRChatOptimizedDistances)
			{
				lod0Distance = 0.8f;
				lod1Distance = 0.5f;
				lod2Distance = 0.25f;
				billboardDistance = 0.1f;
				selectedFadeMode = LODFadeMode.None;
			}
			EditorGUILayout.Space();
			EditorGUILayout.LabelField("LOD Fade Mode:", EditorStyles.boldLabel);
			selectedFadeMode = (LODFadeMode)EditorGUILayout.EnumPopup("Fade Mode", selectedFadeMode);
			EditorGUILayout.HelpBox(
				selectedFadeMode == LODFadeMode.CrossFade ?
				"Crossfade: Smooth transitions between LOD levels (recommended for quality)" :
				"None: Instant switching between LOD levels (better for VRChat performance)",
				MessageType.Info);
			if (!useVRChatOptimizedDistances)
			{
				EditorGUILayout.LabelField("Custom Distance Settings (% of screen height):");
				EditorGUI.indentLevel++;
				lod0Distance = EditorGUILayout.Slider("LOD0 (Original)", lod0Distance, 0.3f, 1f);
				if (generateLOD1)
					lod1Distance = EditorGUILayout.Slider("LOD1 (Half)", lod1Distance, 0.15f, lod0Distance);
				if (generateLOD2)
					lod2Distance = EditorGUILayout.Slider("LOD2 (Quarter)", lod2Distance, 0.05f, lod1Distance);
				if (generateBillboards)
					billboardDistance = EditorGUILayout.Slider("Billboards", billboardDistance, 0.01f, lod2Distance);
				EditorGUI.indentLevel--;
			}
			else
			{
				EditorGUILayout.HelpBox(
					"VRChat Optimized:\n" +
					"• LOD0: 80% distance (high detail)\n" +
					"• LOD1: 50% distance (medium detail)\n" +
					"• LOD2: 25% distance (low detail)\n" +
					"• Billboards: 10% distance (very far)",
					MessageType.Info);
			}
			EditorGUILayout.EndVertical();
			EditorGUILayout.Space();
		}

		private void DrawGenerationButtons()
		{
			GUILayout.Label("Generation", EditorStyles.boldLabel);
			EditorGUI.BeginDisabledGroup(selectedModel == null);
			if (GUILayout.Button("🔍 Analyze Mesh Only", GUILayout.Height(30)))
			{
				AnalyzeMeshDetailed();
			}
			if (GUILayout.Button("🎯 Generate LODs", GUILayout.Height(30)))
			{
				GenerateLODs();
			}
			EditorGUILayout.BeginHorizontal();
			if (GUILayout.Button("📊 Estimate Performance Gain"))
			{
				EstimatePerformanceGain();
			}
			if (GUILayout.Button("🔧 Optimize for VRChat"))
			{
				OptimizeForVRChat();
			}
			EditorGUILayout.EndHorizontal();
			EditorGUI.EndDisabledGroup();
			if (selectedModel == null)
			{
				EditorGUILayout.HelpBox("Please select a model to generate LODs.", MessageType.Warning);
			}
		}

		private void DrawResults()
		{
			EditorGUILayout.Space();
			GUILayout.Label("📊 Generation Results", EditorStyles.boldLabel);
			EditorGUILayout.BeginVertical(GUI.skin.box);
			EditorGUILayout.LabelField("LOD Generation Complete!", EditorStyles.boldLabel);
			if (generationResult.originalMesh != null)
			{
				EditorGUILayout.LabelField($"Original: {generationResult.originalMesh.triangleCount:N0} triangles");
				if (generationResult.lod1Mesh != null)
					EditorGUILayout.LabelField($"LOD1: {generationResult.lod1Mesh.triangleCount:N0} triangles (-{generationResult.lod1Mesh.reductionPercent:F1}%)");
				if (generationResult.lod2Mesh != null)
					EditorGUILayout.LabelField($"LOD2: {generationResult.lod2Mesh.triangleCount:N0} triangles (-{generationResult.lod2Mesh.reductionPercent:F1}%)");
				if (generationResult.billboards?.Length > 0)
					EditorGUILayout.LabelField($"Billboards: {generationResult.billboards.Length} angles generated");
				EditorGUILayout.LabelField($"Total Size Reduction: {generationResult.totalSizeReduction:F1}%");
			}
			EditorGUILayout.BeginHorizontal();
			if (GUILayout.Button("📋 Export Report"))
			{
				AssetManager.ExportLODReport(generationResult, GetCurrentSettings());
			}
			if (GUILayout.Button("📁 Show in Project"))
			{
				if (generationResult.lodGroup != null)
				{
					Selection.activeGameObject = generationResult.lodGroup;
					EditorGUIUtility.PingObject(generationResult.lodGroup);
				}
			}
			EditorGUILayout.EndHorizontal();
			EditorGUILayout.EndVertical();
		}

		private void AnalyzeMeshDetailed()
		{
			if (selectedModel == null) return;
			var meshInfo = MeshProcessor.AnalyzeMesh(selectedModel);
			if (meshInfo == null)
			{
				EditorUtility.DisplayDialog("Analysis Failed", "No meshes found in selected object.", "OK");
				return;
			}
			string report = "Detailed Mesh Analysis:\n\n";
			report += $"Triangles: {meshInfo.triangleCount:N0}\n";
			report += $"Vertices: {meshInfo.vertexCount:N0}\n";
			report += $"Estimated Size: {meshInfo.sizeKB:F1} KB\n\n";
			if (meshInfo.triangleCount > 10000)
				report += "⚠️ High polygon count - excellent LOD candidate\n";
			else if (meshInfo.triangleCount > 5000)
				report += "✅ Medium polygon count - good LOD candidate\n";
			else
				report += "ℹ️ Low polygon count - LOD may have minimal impact\n";
			if (meshInfo.triangleCount > 7500)
				report += "🎮 VRChat: May impact Quest performance\n";
			else if (meshInfo.triangleCount > 2500)
				report += "🎮 VRChat: Good for PC, consider optimization for Quest\n";
			else
				report += "🎮 VRChat: Excellent for all platforms\n";
			EditorUtility.DisplayDialog("Mesh Analysis", report, "OK");
		}

		private void GenerateLODs()
		{
			if (selectedModel == null) return;
			generationResult = new LODGenerationResult();
			generationResult.originalModel = selectedModel;
			// Analyze original mesh
			// Diagnostic: Print mesh info for selectedModel
			var meshFilters = selectedModel.GetComponentsInChildren<MeshFilter>(true);
			foreach (var mf in meshFilters)
			{
				if (mf.sharedMesh != null)
				{
					Debug.Log($"[LODGenerator] Input MeshFilter {mf.gameObject.name}: mesh={mf.sharedMesh.name}, tris={mf.sharedMesh.triangles?.Length / 3}");
				}
				else
				{
					Debug.LogWarning($"[LODGenerator] Input MeshFilter {mf.gameObject.name}: mesh=None");
				}
			}
			var skinnedRenderers = selectedModel.GetComponentsInChildren<SkinnedMeshRenderer>(true);
			foreach (var smr in skinnedRenderers)
			{
				if (smr.sharedMesh != null)
				{
					Debug.Log($"[LODGenerator] Input SkinnedMeshRenderer {smr.gameObject.name}: mesh={smr.sharedMesh.name}, tris={smr.sharedMesh.triangles?.Length / 3}");
				}
				else
				{
					Debug.LogWarning($"[LODGenerator] Input SkinnedMeshRenderer {smr.gameObject.name}: mesh=None");
				}
			}

			generationResult.originalMesh = MeshProcessor.AnalyzeMesh(selectedModel);
			if (generationResult.originalMesh == null)
			{
				EditorUtility.DisplayDialog("Generation Failed", "No meshes found in selected object.", "OK");
				return;
			}
			// Create LOD Group parent
			GameObject lodParent = new GameObject(selectedModel.name + "_LODGroup");
			lodParent.AddComponent<LODGroup>();
			lodParent.transform.position = selectedModel.transform.position;
			lodParent.transform.rotation = selectedModel.transform.rotation;
			// Scale is the original scale of the original object
			lodParent.transform.localScale = selectedModel.transform.localScale;
			generationResult.lodGroup = lodParent;
			// LOD0 (original)
			GameObject lod0Object = Instantiate(selectedModel, lodParent.transform);
			lod0Object.name = selectedModel.name + "_LOD0";
			// Fix orientation: reset local transform to match parent
			lod0Object.transform.localPosition = Vector3.zero;
			lod0Object.transform.localRotation = Quaternion.identity;
			lod0Object.transform.localScale = Vector3.one;
			GameObject lod1Object = null;
			GameObject lod2Object = null;
			// LOD1
			if (generateLOD1)
			{
				lod1Object = MeshProcessor.CreateDecimatedMesh(selectedModel, 0.5f, "LOD1", GetCurrentSettings());
				if (lod1Object == null)
				{
					EditorUtility.DisplayDialog("LOD Generation Aborted", "LOD1 decimation failed (unable to reduce triangle count). LOD generation has been aborted.", "OK");
					Object.DestroyImmediate(lodParent);
					showResults = false;
					return;
				}
				// Validate decimated mesh
				var lod1MeshFilters = lod1Object.GetComponentsInChildren<MeshFilter>(true);
				bool valid = false;
				foreach (var mf in lod1MeshFilters)
				{
					if (mf.sharedMesh != null && mf.sharedMesh.triangles != null && mf.sharedMesh.triangles.Length >= 12)
					{
						valid = true;
						break;
					}
				}
				if (valid)
				{
					lod1Object.transform.SetParent(lodParent.transform);
					lod1Object.name = selectedModel.name + "_LOD1";
					// Save LOD1 mesh as asset and re-assign
					var lod1MeshFiltersSave = lod1Object.GetComponentsInChildren<MeshFilter>(true);
					foreach (var mf in lod1MeshFiltersSave)
					{
						if (mf.sharedMesh != null)
						{
							string meshName = selectedModel.name + "_LOD1_" + mf.gameObject.name;
							AssetManager.SaveMeshAsAsset(mf.sharedMesh, meshName, prefabSavePath);
							// Re-load the mesh asset and re-assign to ensure serialization
							string meshAssetPath = Path.Combine(prefabSavePath, meshName + ".asset").Replace("\\", "/");
							var meshAsset = AssetDatabase.LoadAssetAtPath<Mesh>(meshAssetPath);
							if (meshAsset != null)
							{
								mf.sharedMesh = meshAsset;
								mf.mesh = meshAsset;
							}
						}
					}
					generationResult.lod1Mesh = MeshProcessor.AnalyzeMesh(lod1Object);
				}
				else
				{
					Debug.LogWarning($"LOD1 decimation produced an invalid mesh (zero tris). Aborting LOD generation for {selectedModel.name}.");
					Object.DestroyImmediate(lod1Object);
					Object.DestroyImmediate(lodParent);
					EditorUtility.DisplayDialog("LOD Generation Aborted", "LOD1 decimation failed (zero triangles). LOD generation has been aborted.", "OK");
					showResults = false;
					return;
				}
			}
			// LOD2
			if (generateLOD2)
			{
				lod2Object = MeshProcessor.CreateDecimatedMesh(selectedModel, 0.25f, "LOD2", GetCurrentSettings());
				if (lod2Object == null)
				{
					EditorUtility.DisplayDialog("LOD2 Decimation Failed", "LOD2 decimation failed (unable to reduce triangle count). Skipping LOD2 for this model.", "OK");
					lod2Object = null;
				}
				else
				{
					// Validate decimated mesh
					var lod2MeshFilters = lod2Object.GetComponentsInChildren<MeshFilter>(true);
					bool valid = false;
					foreach (var mf in lod2MeshFilters)
					{
						if (mf.sharedMesh != null && mf.sharedMesh.triangles != null && mf.sharedMesh.triangles.Length >= 12)
						{
							valid = true;
							break;
						}
					}
					if (valid)
					{
						lod2Object.transform.SetParent(lodParent.transform);
						lod2Object.name = selectedModel.name + "_LOD2";
						// Save LOD2 mesh as asset and re-assign
						var lod2MeshFiltersSave = lod2Object.GetComponentsInChildren<MeshFilter>(true);
						foreach (var mf in lod2MeshFiltersSave)
						{
							if (mf.sharedMesh != null)
							{
								string meshName = selectedModel.name + "_LOD2_" + mf.gameObject.name;
								AssetManager.SaveMeshAsAsset(mf.sharedMesh, meshName, prefabSavePath);
								// Re-load the mesh asset and re-assign to ensure serialization
								string meshAssetPath = Path.Combine(prefabSavePath, meshName + ".asset").Replace("\\", "/");
								var meshAsset = AssetDatabase.LoadAssetAtPath<Mesh>(meshAssetPath);
								if (meshAsset != null)
								{
									mf.sharedMesh = meshAsset;
									mf.mesh = meshAsset;
								}
							}
						}
						generationResult.lod2Mesh = MeshProcessor.AnalyzeMesh(lod2Object);
					}
					else
					{
						Debug.LogWarning($"LOD2 decimation produced an invalid mesh (zero tris). Skipping LOD2 for {selectedModel.name}.");
						Object.DestroyImmediate(lod2Object);
						lod2Object = null;
					}
				}
			}

			// --- Assign MeshRenderers to LODGroup ---
			var lodGroup = lodParent.GetComponent<LODGroup>();
			var lods = new List<LOD>();
			// LOD0
			var lod0Renderers = lod0Object.GetComponentsInChildren<MeshRenderer>(true);
			lods.Add(new LOD(lod0Distance, lod0Renderers));
			// LOD1
			if (generateLOD1 && lod1Object != null)
			{
				var lod1Renderers = lod1Object.GetComponentsInChildren<MeshRenderer>(true);
				lods.Add(new LOD(lod1Distance, lod1Renderers));
			}
			// LOD2
			if (generateLOD2 && lod2Object != null)
			{
				var lod2Renderers = lod2Object.GetComponentsInChildren<MeshRenderer>(true);
				lods.Add(new LOD(lod2Distance, lod2Renderers));
			}
			// Billboards
			GameObject[] billboardObjects = null;
			if (generateBillboards && billboardObjects != null && billboardObjects.Length > 0)
			{
				var billboardRenderers = billboardObjects.SelectMany(b => b.GetComponentsInChildren<MeshRenderer>(true)).ToArray();
				if (billboardRenderers.Length > 0)
				{
					lods.Add(new LOD(billboardDistance, billboardRenderers));
				}
			}

			lodGroup.SetLODs(lods.ToArray());
			lodGroup.RecalculateBounds();

			// Save as prefab if requested
			if (createPrefab)
			{
				var prefab = AssetManager.CreateLODPrefab(lodParent, prefabSavePath, selectedModel.name);
				generationResult.createdPrefab = prefab;
				generationResult.prefabPath = prefabSavePath;
				if (removeFromSceneAfterPrefab && prefab != null)
				{
					DestroyImmediate(lodParent);
				}
			}
			showResults = true;
		}

		private LODGenerationSettings GetCurrentSettings()
		{
			return new LODGenerationSettings
			{
				generateLOD1 = this.generateLOD1,
				generateLOD2 = this.generateLOD2,
				generateBillboards = this.generateBillboards,
				useVRChatOptimizedDistances = this.useVRChatOptimizedDistances,
				preserveUVs = this.preserveUVs,
				preserveNormals = this.preserveNormals,
				decimationAngle = this.decimationAngle,
				generateColliders = this.generateColliders,
				createPrefab = this.createPrefab,
				prefabSavePath = this.prefabSavePath,
				removeFromSceneAfterPrefab = this.removeFromSceneAfterPrefab,
				selectedFadeMode = this.selectedFadeMode,
				lod0Distance = this.lod0Distance,
				lod1Distance = this.lod1Distance,
				lod2Distance = this.lod2Distance,
				billboardDistance = this.billboardDistance,
				billboardResolution = this.billboardResolution,
				billboardAngles = this.billboardAngles,
				billboardTransparency = this.billboardTransparency,
				billboardShader = this.billboardShader
			};
		}

		private void EstimatePerformanceGain()
		{
			// Placeholder for future implementation
			EditorUtility.DisplayDialog("Estimate Performance Gain", "Feature coming soon!", "OK");
		}

		private void OptimizeForVRChat()
		{
			// Placeholder for future implementation
			EditorUtility.DisplayDialog("Optimize for VRChat", "Feature coming soon!", "OK");
		}
	}
}
