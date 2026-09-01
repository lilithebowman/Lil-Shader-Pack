/**
 * OcclusionCullingVolumeSetup.cs
 *
 * Editor tool for setting up Occlusion Culling Volumes properly for VRChat worlds.
 * Automatically configures GameObject with appropriate occlusion culling settings.
 *
 * CC0-Attribution Licsense by Lilithe for CozyCon 2025
 */

using UnityEngine;
using UnityEditor;
using System.Linq;

namespace CozyCon.Tools
{
	/// <summary>
	/// Editor tool for setting up Occlusion Culling Volumes properly for VRChat worlds.
	/// Automatically configures GameObject with appropriate occlusion culling settings.
	/// </summary>
	public class OcclusionCullingVolumeSetup : EditorWindow
	{
		[MenuItem("Lilithe/Occlusion Culling Volume Setup")]
		static void Init()
		{
			OcclusionCullingVolumeSetup window = (OcclusionCullingVolumeSetup)EditorWindow.GetWindow(typeof(OcclusionCullingVolumeSetup));
			window.titleContent = new GUIContent("Occlusion Culling Setup");
			window.Show();
		}

		private GameObject targetObject;
		private Vector2 scrollPosition;
		private bool autoDetectBounds = true;
		private Vector3 customSize = new Vector3(10f, 3f, 10f);
		private Vector3 customCenter = Vector3.zero;
		private bool createNewOcclusionVolume = false; // New option

		// Detection and analysis results
		private OcclusionAnalysisResult analysisResult;
		private bool showDetectionResults = false;
		private CameraTestResult cameraTestResult;
		private bool showCameraTestResults = false;

		[System.Serializable]
		public class OcclusionAnalysisResult
		{
			public OcclusionArea[] occlusionAreas;
			public GameObject[] occluders;
			public GameObject[] occludees;
			public GameObject[] misconfiguredObjects;
			public GameObject[] missingVolumes;
			public GameObject[] undersizedVolumes;
			public GameObject[] oversizedVolumes;
			public GameObject[] mispositionedVolumes;
			public VolumeIssue[] volumeIssues;
			public string[] issues;
			public string[] recommendations;
			public int totalRenderers;
			public int staticRenderers;
			public float occlusionCoverage;
		}

		[System.Serializable]
		public class VolumeIssue
		{
			public GameObject gameObject;
			public string issueDescription;
			public Vector3 currentSize;
			public Vector3 recommendedSize;
			public Vector3 currentPosition;
			public Vector3 recommendedPosition;
			public float currentVolume;
			public float recommendedVolume;
			public int affectedRenderers;
			public bool hasPositionIssue;
		}

		[System.Serializable]
		public class CameraTestResult
		{
			public Vector3[] testPositions;
			public GameObject[] incorrectlyCulledObjects;
			public GameObject[] shouldBeCulledObjects;
			public CameraTestPoint[] testPoints;
			public float overallEfficiency;
			public string[] issues;
			public string[] recommendations;
		}

		[System.Serializable]
		public class CameraTestPoint
		{
			public Vector3 position;
			public Vector3 lookDirection;
			public GameObject[] visibleObjects;
			public GameObject[] culledObjects;
			public GameObject[] incorrectlyCulled;
			public float cullingEfficiency;
			public string description;
		}

		void OnGUI()
		{
			scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);

			GUILayout.Label("Occlusion Culling Volume Setup", EditorStyles.boldLabel);
			EditorGUILayout.Space();

			EditorGUILayout.HelpBox(
				"This tool sets up GameObjects with proper Occlusion Culling Volumes for VRChat worlds.\n" +
				"Occlusion culling helps improve performance by hiding objects that are blocked from view.",
				MessageType.Info);

			EditorGUILayout.Space();

			// Target Object Selection
			GUILayout.Label("Target Setup", EditorStyles.boldLabel);
			targetObject = (GameObject)EditorGUILayout.ObjectField("Target GameObject", targetObject, typeof(GameObject), true);

			if (targetObject == null && Selection.activeGameObject != null)
			{
				if (GUILayout.Button("Use Selected GameObject"))
				{
					targetObject = Selection.activeGameObject;
				}
			}

			EditorGUILayout.Space();

			// Volume Configuration
			GUILayout.Label("Volume Configuration", EditorStyles.boldLabel);

			createNewOcclusionVolume = EditorGUILayout.Toggle("Create New Occlusion Volume", createNewOcclusionVolume);

			EditorGUILayout.HelpBox(
				createNewOcclusionVolume
					? "Will create a new empty GameObject with OcclusionArea that encompasses all visible objects in the scene."
					: "Will modify the OcclusionArea component on the selected GameObject.",
				MessageType.Info);

			autoDetectBounds = EditorGUILayout.Toggle("Auto-Detect Scene Bounds", autoDetectBounds);

			if (!autoDetectBounds)
			{
				EditorGUILayout.LabelField("Manual Volume Settings:");
				EditorGUI.indentLevel++;
				customSize = EditorGUILayout.Vector3Field("Size", customSize);
				customCenter = EditorGUILayout.Vector3Field("Center Offset", customCenter);
				EditorGUI.indentLevel--;
			}

			EditorGUILayout.Space();

			// Action Buttons
			if (createNewOcclusionVolume)
			{
				if (GUILayout.Button("🆕 Create Scene-Wide Occlusion Volume", GUILayout.Height(30)))
				{
					CreateSceneWideOcclusionVolume();
				}
			}
			else
			{
				EditorGUI.BeginDisabledGroup(targetObject == null);
				if (GUILayout.Button("🔧 Modify Selected Occlusion Volume", GUILayout.Height(30)))
				{
					ModifyExistingOcclusionVolume();
				}
				EditorGUI.EndDisabledGroup();

				if (targetObject == null)
				{
					EditorGUILayout.HelpBox("Please select a GameObject with an OcclusionArea component to modify.", MessageType.Warning);
				}
			}

			EditorGUILayout.Space();

			// Batch Operations
			GUILayout.Label("Batch Operations", EditorStyles.boldLabel);

			if (GUILayout.Button("Setup All Static Renderers in Scene"))
			{
				SetupAllStaticRenderers();
			}

			if (GUILayout.Button("Setup All Children of Selected Object"))
			{
				SetupAllChildren();
			}

			EditorGUILayout.Space();

			// Information Display
			if (targetObject != null)
			{
				DisplayObjectInfo();
			}

			// Scene Analysis
			EditorGUILayout.Space();
			GUILayout.Label("Scene Detection & Analysis", EditorStyles.boldLabel);

			if (GUILayout.Button("Detect Existing Occlusion Volumes"))
			{
				DetectOcclusionVolumes();
			}

			if (GUILayout.Button("Analyze Scene for Occlusion Opportunities"))
			{
				AnalyzeScene();
			}

			// Detection Results Display
			if (showDetectionResults && analysisResult != null)
			{
				DisplayDetectionResults();
			}

			// Camera-Based Occlusion Testing
			EditorGUILayout.Space();
			GUILayout.Label("🎥 Camera-Based Validation", EditorStyles.boldLabel);

			EditorGUILayout.HelpBox(
				"Test occlusion culling effectiveness by moving the main camera to different viewpoints. " +
				"This will identify objects that are incorrectly culled or should be culled but aren't.",
				MessageType.Info);

			EditorGUILayout.BeginHorizontal();
			if (GUILayout.Button("🎯 Test Current Camera Position"))
			{
				TestSingleCameraPosition();
			}

			if (GUILayout.Button("🔄 Test Multiple Viewpoints"))
			{
				TestMultipleCameraPositions();
			}
			EditorGUILayout.EndHorizontal();

			if (GUILayout.Button("🔍 Smart Viewpoint Analysis", GUILayout.Height(25)))
			{
				SmartViewpointAnalysis();
			}

			// Camera Test Results Display
			if (showCameraTestResults && cameraTestResult != null)
			{
				DisplayCameraTestResults();
			}

			// Auto Fix Section
			EditorGUILayout.Space();
			GUILayout.Label("Auto Fix", EditorStyles.boldLabel);

			EditorGUILayout.HelpBox(
				"Auto Fix will automatically detect and correct common occlusion culling issues in your scene.",
				MessageType.Info);

			if (GUILayout.Button("🔧 Auto Fix Occlusion Issues", GUILayout.Height(30)))
			{
				AutoFixOcclusionIssues();
			}

			EditorGUILayout.EndScrollView();
		}

		/// <summary>
		/// Sets up occlusion culling volume for the target object
		/// </summary>
		private void SetupOcclusionCullingVolume()
		{
			if (targetObject == null)
			{
				EditorUtility.DisplayDialog("Error", "Please select a target GameObject.", "OK");
				return;
			}

			Undo.RecordObject(targetObject, "Setup Occlusion Culling Volume");

			Debug.Log($"[Occlusion Setup] Setting up OcclusionArea volume for {targetObject.name}");

			// Create or get OcclusionArea component (this is what actually creates occlusion volumes)
			OcclusionArea occlusionArea = targetObject.GetComponent<OcclusionArea>();
			if (occlusionArea == null)
			{
				occlusionArea = targetObject.AddComponent<OcclusionArea>();
				Debug.Log($"[Occlusion Setup] Added OcclusionArea component to {targetObject.name}");
			}

			if (autoDetectBounds)
			{
				// Calculate bounds that ENCLOSE all child renderers
				Bounds enclosingBounds = CalculateEnclosingBounds(targetObject);
				if (enclosingBounds.size != Vector3.zero)
				{
					occlusionArea.center = enclosingBounds.center - targetObject.transform.position;
					occlusionArea.size = enclosingBounds.size;
					Debug.Log($"[Occlusion Setup] Auto-detected enclosing bounds: Size={enclosingBounds.size}, Center={enclosingBounds.center}");
				}
				else
				{
					// Fallback to default size
					occlusionArea.size = customSize;
					occlusionArea.center = customCenter;
					Debug.LogWarning($"[Occlusion Setup] Could not auto-detect bounds for {targetObject.name}, using custom size");
				}
			}
			else
			{
				occlusionArea.size = customSize;
				occlusionArea.center = customCenter;
				Debug.Log($"[Occlusion Setup] Applied custom bounds: Size={customSize}, Center={customCenter}");
			}

			// Mark object as static for occlusion culling
			if (!GameObjectUtility.AreStaticEditorFlagsSet(targetObject, StaticEditorFlags.OccluderStatic))
			{
				var flags = GameObjectUtility.GetStaticEditorFlags(targetObject);
				flags |= StaticEditorFlags.OccluderStatic;
				GameObjectUtility.SetStaticEditorFlags(targetObject, flags);
				Debug.Log($"[Occlusion Setup] Marked {targetObject.name} as Occluder Static");
			}

			// Mark child objects for occlusion culling
			var renderers = targetObject.GetComponentsInChildren<Renderer>();
			foreach (var renderer in renderers)
			{
				if (!GameObjectUtility.AreStaticEditorFlagsSet(renderer.gameObject, StaticEditorFlags.OccludeeStatic))
				{
					var flags = GameObjectUtility.GetStaticEditorFlags(renderer.gameObject);
					flags |= StaticEditorFlags.OccludeeStatic;
					GameObjectUtility.SetStaticEditorFlags(renderer.gameObject, flags);
				}
			}

			EditorUtility.SetDirty(targetObject);
			Debug.Log($"[Occlusion Setup] Successfully configured OcclusionArea for {targetObject.name}");
		}

		/// <summary>
		/// Calculates bounds that enclose all renderers in the object and its children
		/// </summary>
		private Bounds CalculateEnclosingBounds(GameObject obj)
		{
			Renderer[] renderers = obj.GetComponentsInChildren<Renderer>();
			if (renderers.Length == 0)
			{
				return new Bounds();
			}

			Bounds bounds = renderers[0].bounds;
			foreach (Renderer renderer in renderers)
			{
				if (renderer.enabled)
				{
					bounds.Encapsulate(renderer.bounds);
				}
			}

			// Add some padding to ensure complete enclosure
			bounds.Expand(1f);

			return bounds;
		}

		/// <summary>
		/// Checks if an object has scripts that might use trigger colliders
		/// </summary>
		private bool HasTriggerScripts(GameObject obj)
		{
			var scripts = obj.GetComponents<MonoBehaviour>();
			foreach (var script in scripts)
			{
				if (script != null)
				{
					var type = script.GetType();
					// Check for common trigger-related methods
					if (type.GetMethod("OnTriggerEnter") != null ||
						type.GetMethod("OnTriggerExit") != null ||
						type.GetMethod("OnTriggerStay") != null)
					{
						return true;
					}
				}
			}
			return false;
		}

		/// <summary>
		/// Creates a new GameObject with OcclusionArea that encompasses all visible objects in the scene
		/// </summary>
		private void CreateSceneWideOcclusionVolume()
		{
			Debug.Log("[Occlusion Setup] Creating scene-wide occlusion volume...");

			// Create new empty GameObject for the occlusion volume
			GameObject occlusionVolumeObj = new GameObject("Scene Occlusion Volume");
			Undo.RegisterCreatedObjectUndo(occlusionVolumeObj, "Create Scene Occlusion Volume");

			// Add OcclusionArea component
			OcclusionArea occlusionArea = occlusionVolumeObj.AddComponent<OcclusionArea>();

			if (autoDetectBounds)
			{
				// Calculate bounds that encompass ALL visible objects in the scene
				Bounds sceneBounds = CalculateSceneBounds();
				if (sceneBounds.size != Vector3.zero)
				{
					// Position the GameObject at the center of the scene bounds
					occlusionVolumeObj.transform.position = sceneBounds.center;

					// Set OcclusionArea size and center
					occlusionArea.center = Vector3.zero; // Center relative to GameObject position
					occlusionArea.size = sceneBounds.size;

					Debug.Log($"[Occlusion Setup] Scene bounds: Size={sceneBounds.size}, Center={sceneBounds.center}");
				}
				else
				{
					// Fallback to default size
					occlusionArea.size = new Vector3(100f, 20f, 100f);
					occlusionArea.center = Vector3.zero;
					Debug.LogWarning("[Occlusion Setup] Could not detect scene bounds, using default size");
				}
			}
			else
			{
				// Use custom size
				occlusionArea.size = customSize;
				occlusionArea.center = customCenter;
				Debug.Log($"[Occlusion Setup] Applied custom bounds: Size={customSize}, Center={customCenter}");
			}

			// Mark as static for occlusion culling
			var flags = GameObjectUtility.GetStaticEditorFlags(occlusionVolumeObj);
			flags |= StaticEditorFlags.OccluderStatic;
			GameObjectUtility.SetStaticEditorFlags(occlusionVolumeObj, flags);

			// Select the new object
			Selection.activeGameObject = occlusionVolumeObj;
			EditorGUIUtility.PingObject(occlusionVolumeObj);

			EditorUtility.SetDirty(occlusionVolumeObj);
			Debug.Log($"[Occlusion Setup] Created scene-wide occlusion volume: {occlusionVolumeObj.name}");
		}

		/// <summary>
		/// Modifies an existing OcclusionArea component on the selected GameObject
		/// </summary>
		private void ModifyExistingOcclusionVolume()
		{
			if (targetObject == null)
			{
				EditorUtility.DisplayDialog("Error", "Please select a GameObject first.", "OK");
				return;
			}

			Debug.Log($"[Occlusion Setup] Modifying existing occlusion volume on {targetObject.name}");

			// Get or create OcclusionArea component
			OcclusionArea occlusionArea = targetObject.GetComponent<OcclusionArea>();
			if (occlusionArea == null)
			{
				occlusionArea = targetObject.AddComponent<OcclusionArea>();
				Debug.Log($"[Occlusion Setup] Added OcclusionArea component to {targetObject.name}");
			}

			Undo.RecordObject(occlusionArea, "Modify Occlusion Volume");

			if (autoDetectBounds)
			{
				// Calculate bounds that encompass ALL visible objects in the scene
				Bounds sceneBounds = CalculateSceneBounds();
				if (sceneBounds.size != Vector3.zero)
				{
					// Set OcclusionArea relative to the GameObject's position
					occlusionArea.center = sceneBounds.center - targetObject.transform.position;
					occlusionArea.size = sceneBounds.size;
					Debug.Log($"[Occlusion Setup] Auto-detected scene bounds: Size={sceneBounds.size}, Center={sceneBounds.center}");
				}
				else
				{
					// Fallback to default size
					occlusionArea.size = new Vector3(100f, 20f, 100f);
					occlusionArea.center = Vector3.zero;
					Debug.LogWarning($"[Occlusion Setup] Could not detect scene bounds for {targetObject.name}, using default size");
				}
			}
			else
			{
				// Use custom size
				occlusionArea.size = customSize;
				occlusionArea.center = customCenter;
				Debug.Log($"[Occlusion Setup] Applied custom bounds: Size={customSize}, Center={customCenter}");
			}

			// Mark as static for occlusion culling
			if (!GameObjectUtility.AreStaticEditorFlagsSet(targetObject, StaticEditorFlags.OccluderStatic))
			{
				var flags = GameObjectUtility.GetStaticEditorFlags(targetObject);
				flags |= StaticEditorFlags.OccluderStatic;
				GameObjectUtility.SetStaticEditorFlags(targetObject, flags);
				Debug.Log($"[Occlusion Setup] Marked {targetObject.name} as Occluder Static");
			}

			EditorUtility.SetDirty(targetObject);
			Debug.Log($"[Occlusion Setup] Successfully modified OcclusionArea on {targetObject.name}");
		}

		/// <summary>
		/// Calculates bounds that encompass ALL visible objects in the scene
		/// </summary>
		private Bounds CalculateSceneBounds()
		{
			Renderer[] allRenderers = FindObjectsOfType<Renderer>();
			var visibleRenderers = allRenderers.Where(r => r.enabled && r.gameObject.activeInHierarchy).ToArray();

			if (visibleRenderers.Length == 0)
			{
				Debug.LogWarning("[Occlusion Setup] No visible renderers found in scene");
				return new Bounds();
			}

			Bounds sceneBounds = visibleRenderers[0].bounds;
			foreach (Renderer renderer in visibleRenderers)
			{
				sceneBounds.Encapsulate(renderer.bounds);
			}

			// Add padding to ensure complete coverage
			sceneBounds.Expand(2f);

			Debug.Log($"[Occlusion Setup] Calculated scene bounds encompassing {visibleRenderers.Length} visible objects");
			return sceneBounds;
		}

		/// <summary>
		/// Sets up occlusion culling for multiple selected objects
		/// </summary>
		private void SetupMultipleObjects()
		{
			var selectedObjects = Selection.gameObjects;
			if (selectedObjects.Length == 0)
			{
				EditorUtility.DisplayDialog("Error", "Please select one or more GameObjects.", "OK");
				return;
			}

			Undo.RecordObjects(selectedObjects, "Setup Multiple Occlusion Volumes");

			int successCount = 0;
			foreach (var obj in selectedObjects)
			{
				var previousTarget = targetObject;
				targetObject = obj;

				try
				{
					SetupOcclusionCullingVolume();
					successCount++;
				}
				catch (System.Exception e)
				{
					Debug.LogError($"[Occlusion Setup] Failed to setup {obj.name}: {e.Message}");
				}

				targetObject = previousTarget;
			}

			EditorUtility.DisplayDialog("Batch Setup Complete",
				$"Successfully configured {successCount} of {selectedObjects.Length} objects.", "OK");
		}

		/// <summary>
		/// Sets up all static renderers in the scene
		/// </summary>
		private void SetupAllStaticRenderers()
		{
			var allRenderers = FindObjectsOfType<Renderer>();
			var staticRenderers = System.Array.FindAll(allRenderers, r =>
				GameObjectUtility.AreStaticEditorFlagsSet(r.gameObject, StaticEditorFlags.BatchingStatic));

			if (staticRenderers.Length == 0)
			{
				EditorUtility.DisplayDialog("No Static Objects",
					"No static renderers found in scene. Objects should be marked as static for occlusion culling.", "OK");
				return;
			}

			bool confirm = EditorUtility.DisplayDialog("Setup All Static Renderers",
				$"This will setup occlusion culling for {staticRenderers.Length} static renderers. Continue?",
				"Yes", "Cancel");

			if (!confirm) return;

			var gameObjects = System.Array.ConvertAll(staticRenderers, r => r.gameObject);
			Undo.RecordObjects(gameObjects, "Setup All Static Renderers");

			int successCount = 0;
			var previousTarget = targetObject;

			foreach (var renderer in staticRenderers)
			{
				targetObject = renderer.gameObject;
				try
				{
					SetupOcclusionCullingVolume();
					successCount++;
				}
				catch (System.Exception e)
				{
					Debug.LogError($"[Occlusion Setup] Failed to setup {renderer.gameObject.name}: {e.Message}");
				}
			}

			targetObject = previousTarget;

			EditorUtility.DisplayDialog("Batch Setup Complete",
				$"Successfully configured {successCount} of {staticRenderers.Length} static renderers.", "OK");
		}

		/// <summary>
		/// Sets up all children of the selected object
		/// </summary>
		private void SetupAllChildren()
		{
			if (targetObject == null)
			{
				EditorUtility.DisplayDialog("Error", "Please select a parent GameObject.", "OK");
				return;
			}

			var children = targetObject.GetComponentsInChildren<Renderer>();
			if (children.Length == 0)
			{
				EditorUtility.DisplayDialog("No Children",
					"No child objects with renderers found.", "OK");
				return;
			}

			bool confirm = EditorUtility.DisplayDialog("Setup All Children",
				$"This will setup occlusion culling for {children.Length} child renderers. Continue?",
				"Yes", "Cancel");

			if (!confirm) return;

			var gameObjects = System.Array.ConvertAll(children, r => r.gameObject);
			Undo.RecordObjects(gameObjects, "Setup All Children");

			int successCount = 0;
			var previousTarget = targetObject;

			foreach (var child in children)
			{
				targetObject = child.gameObject;
				try
				{
					SetupOcclusionCullingVolume();
					successCount++;
				}
				catch (System.Exception e)
				{
					Debug.LogError($"[Occlusion Setup] Failed to setup {child.gameObject.name}: {e.Message}");
				}
			}

			targetObject = previousTarget;

			EditorUtility.DisplayDialog("Batch Setup Complete",
				$"Successfully configured {successCount} of {children.Length} child renderers.", "OK");
		}

		/// <summary>
		/// Calculates the bounds of an object including all its child renderers
		/// </summary>
		private Bounds CalculateObjectBounds(GameObject obj)
		{
			var renderers = obj.GetComponentsInChildren<Renderer>();
			if (renderers.Length == 0)
				return new Bounds();

			var bounds = renderers[0].bounds;
			foreach (var renderer in renderers)
			{
				bounds.Encapsulate(renderer.bounds);
			}

			return bounds;
		}

		/// <summary>
		/// Displays information about the target object
		/// </summary>
		private void DisplayObjectInfo()
		{
			EditorGUILayout.Space();
			GUILayout.Label("Object Information", EditorStyles.boldLabel);

			EditorGUILayout.LabelField("Name:", targetObject.name);
			EditorGUILayout.LabelField("Position:", targetObject.transform.position.ToString());

			// Check static flags
			var flags = GameObjectUtility.GetStaticEditorFlags(targetObject);
			bool isOccluder = (flags & StaticEditorFlags.OccluderStatic) != 0;
			bool isOccludee = (flags & StaticEditorFlags.OccludeeStatic) != 0;

			EditorGUILayout.LabelField("Occluder Static:", isOccluder ? "✅ Yes" : "❌ No");
			EditorGUILayout.LabelField("Occludee Static:", isOccludee ? "✅ Yes" : "❌ No");

			// Check for existing colliders
			var colliders = targetObject.GetComponents<Collider>();
			EditorGUILayout.LabelField("Colliders:", colliders.Length.ToString());

			// Show bounds info
			if (autoDetectBounds)
			{
				var bounds = CalculateObjectBounds(targetObject);
				if (bounds.size != Vector3.zero)
				{
					EditorGUILayout.LabelField("Calculated Bounds:", bounds.size.ToString());
					EditorGUILayout.LabelField("Volume Size:", $"{bounds.size.x:F1} × {bounds.size.y:F1} × {bounds.size.z:F1}");

					if (GUILayout.Button("🔍 Preview Occluder Bounds"))
					{
						PreviewOccluderBounds(targetObject);
					}
				}
			}

			// Show child renderer info
			var childRenderers = targetObject.GetComponentsInChildren<Renderer>();
			if (childRenderers.Length > 1) // More than just itself
			{
				EditorGUILayout.LabelField("Child Renderers:", (childRenderers.Length - 1).ToString());
				if (GUILayout.Button("📋 List All Included Transforms"))
				{
					ListIncludedTransforms(targetObject);
				}
			}
		}

		/// <summary>
		/// Shows what transforms would be included in the occluder bounds
		/// </summary>
		private void ListIncludedTransforms(GameObject occluder)
		{
			var renderers = occluder.GetComponentsInChildren<Renderer>();
			var message = $"Transforms included in '{occluder.name}' occluder bounds:\n\n";

			foreach (var renderer in renderers)
			{
				var size = renderer.bounds.size;
				message += $"• {renderer.name}\n";
				message += $"  Size: {size.x:F1} × {size.y:F1} × {size.z:F1}\n";
				message += $"  Type: {renderer.GetType().Name}\n\n";
			}

			var bounds = CalculateObjectBounds(occluder);
			message += $"Combined Bounds: {bounds.size.x:F1} × {bounds.size.y:F1} × {bounds.size.z:F1}\n";
			message += $"Total Volume: {(bounds.size.x * bounds.size.y * bounds.size.z):F1} cubic units";

			EditorUtility.DisplayDialog("Occluder Bounds Analysis", message, "OK");
		}

		/// <summary>
		/// Previews the occluder bounds in the scene view
		/// </summary>
		private void PreviewOccluderBounds(GameObject occluder)
		{
			var bounds = CalculateObjectBounds(occluder);

			// Focus scene view on the bounds
			if (SceneView.lastActiveSceneView != null)
			{
				SceneView.lastActiveSceneView.Frame(bounds, false);
			}

			// Log detailed info
			Debug.Log($"[Occluder Bounds] {occluder.name}:\n" +
				$"Size: {bounds.size.x:F2} × {bounds.size.y:F2} × {bounds.size.z:F2}\n" +
				$"Center: {bounds.center}\n" +
				$"Volume: {(bounds.size.x * bounds.size.y * bounds.size.z):F2} cubic units\n" +
				$"Includes {occluder.GetComponentsInChildren<Renderer>().Length} renderer(s)");
		}

		/// <summary>
		/// Analyzes the scene for occlusion culling opportunities
		/// </summary>
		private void AnalyzeScene()
		{
			var allRenderers = FindObjectsOfType<Renderer>();
			var staticRenderers = System.Array.FindAll(allRenderers, r =>
				GameObjectUtility.AreStaticEditorFlagsSet(r.gameObject, StaticEditorFlags.BatchingStatic));

			var occluders = System.Array.FindAll(allRenderers, r =>
				GameObjectUtility.AreStaticEditorFlagsSet(r.gameObject, StaticEditorFlags.OccluderStatic));

			var occludees = System.Array.FindAll(allRenderers, r =>
				GameObjectUtility.AreStaticEditorFlagsSet(r.gameObject, StaticEditorFlags.OccludeeStatic));

			string analysis = $"Scene Occlusion Analysis:\n\n" +
				$"Total Renderers: {allRenderers.Length}\n" +
				$"Static Renderers: {staticRenderers.Length}\n" +
				$"Occluders: {occluders.Length}\n" +
				$"Occludees: {occludees.Length}\n\n" +
				$"Recommendations:\n";

			if (staticRenderers.Length < allRenderers.Length * 0.7f)
			{
				analysis += "• Consider marking more objects as static for better occlusion culling\n";
			}

			if (occluders.Length < staticRenderers.Length * 0.3f)
			{
				analysis += "• Consider setting up more occluders for better performance\n";
			}

			if (occludees.Length < staticRenderers.Length * 0.8f)
			{
				analysis += "• Consider marking more static objects as occludees\n";
			}

			if (occluders.Length > 0 && occludees.Length > 0)
			{
				analysis += "• Scene appears well-configured for occlusion culling ✅\n";
			}

			EditorUtility.DisplayDialog("Scene Analysis", analysis, "OK");
			Debug.Log($"[Occlusion Analysis] {analysis}");
		}

		/// <summary>
		/// Detects existing occlusion volumes and related objects in the scene
		/// </summary>
		private void DetectOcclusionVolumes()
		{
			Debug.Log("[Occlusion Detection] Starting scene detection...");

			analysisResult = new OcclusionAnalysisResult();

			// Find all occlusion areas
			analysisResult.occlusionAreas = FindObjectsOfType<OcclusionArea>();

			// Find all renderers and categorize them
			var allRenderers = FindObjectsOfType<Renderer>();
			analysisResult.totalRenderers = allRenderers.Length;

			var staticRenderers = System.Array.FindAll(allRenderers, r =>
				GameObjectUtility.AreStaticEditorFlagsSet(r.gameObject, StaticEditorFlags.BatchingStatic));
			analysisResult.staticRenderers = staticRenderers.Length;

			// Find occluders and occludees
			var occluders = System.Array.FindAll(allRenderers, r =>
				GameObjectUtility.AreStaticEditorFlagsSet(r.gameObject, StaticEditorFlags.OccluderStatic));
			analysisResult.occluders = System.Array.ConvertAll(occluders, r => r.gameObject);

			var occludees = System.Array.FindAll(allRenderers, r =>
				GameObjectUtility.AreStaticEditorFlagsSet(r.gameObject, StaticEditorFlags.OccludeeStatic));
			analysisResult.occludees = System.Array.ConvertAll(occludees, r => r.gameObject);

			// Detect misconfigured objects and missing volumes
			DetectMisconfiguredObjects();
			DetectMissingVolumes();
			DetectVolumeSizeIssues();

			// Calculate occlusion coverage
			analysisResult.occlusionCoverage = analysisResult.staticRenderers > 0 ?
				(float)(analysisResult.occluders.Length + analysisResult.occludees.Length) / (analysisResult.staticRenderers * 2) : 0f;

			// Generate recommendations
			GenerateRecommendations();

			showDetectionResults = true;

			Debug.Log($"[Occlusion Detection] Found {analysisResult.occlusionAreas.Length} occlusion areas, " +
				$"{analysisResult.occluders.Length} occluders, {analysisResult.occludees.Length} occludees");
		}

		/// <summary>
		/// Detects objects with incorrect occlusion culling configuration
		/// </summary>
		private void DetectMisconfiguredObjects()
		{
			var misconfigured = new System.Collections.Generic.List<GameObject>();
			var allRenderers = FindObjectsOfType<Renderer>();

			foreach (var renderer in allRenderers)
			{
				var obj = renderer.gameObject;
				var flags = GameObjectUtility.GetStaticEditorFlags(obj);

				// Check for common misconfigurations
				bool isStatic = (flags & StaticEditorFlags.BatchingStatic) != 0;
				bool isOccluder = (flags & StaticEditorFlags.OccluderStatic) != 0;
				bool isOccludee = (flags & StaticEditorFlags.OccludeeStatic) != 0;

				// Objects that are occluders but not static
				if (isOccluder && !isStatic)
				{
					misconfigured.Add(obj);
					continue;
				}

				// Large static objects that should be occluders but aren't
				if (isStatic && !isOccluder && !isOccludee)
				{
					var bounds = renderer.bounds;
					if (bounds.size.magnitude > 5f) // Large objects
					{
						misconfigured.Add(obj);
					}
				}
			}

			analysisResult.misconfiguredObjects = misconfigured.ToArray();
		}

		/// <summary>
		/// Detects areas that would benefit from occlusion volumes
		/// </summary>
		private void DetectMissingVolumes()
		{
			var missingVolumes = new System.Collections.Generic.List<GameObject>();
			var allRenderers = FindObjectsOfType<Renderer>();

			foreach (var renderer in allRenderers)
			{
				var obj = renderer.gameObject;
				var flags = GameObjectUtility.GetStaticEditorFlags(obj);
				bool isStatic = (flags & StaticEditorFlags.BatchingStatic) != 0;
				bool isOccluder = (flags & StaticEditorFlags.OccluderStatic) != 0;

				// Static objects that could be occluders but lack proper setup
				if (isStatic && !isOccluder)
				{
					var bounds = renderer.bounds;
					var colliders = obj.GetComponents<Collider>();

					// Large objects without colliders that could block view
					if (bounds.size.magnitude > 3f && colliders.Length == 0)
					{
						missingVolumes.Add(obj);
					}
				}
			}

			analysisResult.missingVolumes = missingVolumes.ToArray();
		}

		/// <summary>
		/// Detects occlusion volumes with size issues (too small/large relative to their content)
		/// </summary>
		private void DetectVolumeSizeIssues()
		{
			var undersizedVolumes = new System.Collections.Generic.List<GameObject>();
			var oversizedVolumes = new System.Collections.Generic.List<GameObject>();
			var mispositionedVolumes = new System.Collections.Generic.List<GameObject>();
			var volumeIssues = new System.Collections.Generic.List<VolumeIssue>();

			// Find all objects with occlusion-related colliders
			var allRenderers = FindObjectsOfType<Renderer>();
			var occluderObjects = System.Array.FindAll(allRenderers, r =>
				GameObjectUtility.AreStaticEditorFlagsSet(r.gameObject, StaticEditorFlags.OccluderStatic));

			foreach (var renderer in occluderObjects)
			{
				var obj = renderer.gameObject;
				var colliders = obj.GetComponents<BoxCollider>();

				foreach (var collider in colliders)
				{
					var issue = AnalyzeVolumeSize(obj, collider, allRenderers);
					if (issue != null)
					{
						volumeIssues.Add(issue);

						if (issue.hasPositionIssue)
						{
							mispositionedVolumes.Add(obj);
						}

						if (issue.currentVolume < issue.recommendedVolume * 0.7f) // Significantly undersized
						{
							undersizedVolumes.Add(obj);
						}
						else if (issue.currentVolume > issue.recommendedVolume * 2.0f) // Significantly oversized
						{
							oversizedVolumes.Add(obj);
						}
					}
				}
			}

			analysisResult.undersizedVolumes = undersizedVolumes.ToArray();
			analysisResult.oversizedVolumes = oversizedVolumes.ToArray();
			analysisResult.mispositionedVolumes = mispositionedVolumes.ToArray();
			analysisResult.volumeIssues = volumeIssues.ToArray();

			Debug.Log($"[Volume Analysis] Found {undersizedVolumes.Count} undersized, {oversizedVolumes.Count} oversized, and {mispositionedVolumes.Count} mispositioned volumes");
		}

		/// <summary>
		/// Analyzes a specific volume and determines if it has size or position issues
		/// </summary>
		private VolumeIssue AnalyzeVolumeSize(GameObject volumeObject, BoxCollider collider, Renderer[] allRenderers)
		{
			var colliderBounds = collider.bounds;

			// Calculate the CORRECT bounds: the occluder object + its children
			Bounds optimalBounds = CalculateOccluderBounds(volumeObject);

			if (optimalBounds.size == Vector3.zero)
			{
				return null; // No geometry to analyze
			}

			// Check for position issues
			bool hasPositionIssue = false;
			string positionIssueDesc = "";

			// Check if volume center is significantly different from geometry center
			var positionDifference = Vector3.Distance(colliderBounds.center, optimalBounds.center);
			var geometrySize = optimalBounds.size.magnitude;

			if (positionDifference > geometrySize * 0.5f) // Volume center is more than 50% of geometry size away
			{
				hasPositionIssue = true;

				// Check specific position issues
				if (colliderBounds.center.y < optimalBounds.min.y - optimalBounds.size.y * 0.1f)
				{
					positionIssueDesc = "Volume positioned underground - will not effectively occlude objects above";
				}
				else if (colliderBounds.center.y > optimalBounds.max.y + optimalBounds.size.y * 0.1f)
				{
					positionIssueDesc = "Volume positioned too high above geometry";
				}
				else
				{
					positionIssueDesc = "Volume center misaligned with geometry - may reduce occlusion effectiveness";
				}
			}

			var currentVolume = colliderBounds.size.x * colliderBounds.size.y * colliderBounds.size.z;
			var recommendedVolume = optimalBounds.size.x * optimalBounds.size.y * optimalBounds.size.z;

			// Check for size issues
			bool hasSizeIssue = false;
			string sizeIssueDesc = "";
			var sizeDifference = Mathf.Abs(currentVolume - recommendedVolume) / Mathf.Max(currentVolume, recommendedVolume);

			if (sizeDifference >= 0.2f) // More than 20% difference
			{
				hasSizeIssue = true;
				if (currentVolume < recommendedVolume * 0.7f)
				{
					sizeIssueDesc = "Volume too small for occluder geometry - may cause incorrect culling";
				}
				else if (currentVolume > recommendedVolume * 2.0f)
				{
					sizeIssueDesc = "Volume too large - inefficient occlusion culling";
				}
				else
				{
					sizeIssueDesc = "Volume size suboptimal for occluder geometry";
				}
			}

			// Only report if there are actual issues
			if (!hasPositionIssue && !hasSizeIssue) return null;

			// Count how many objects might be affected
			int affectedRenderers = CountAffectedRenderers(volumeObject, colliderBounds, optimalBounds, allRenderers);

			// Combine issue descriptions
			string issueDescription;
			if (hasPositionIssue && hasSizeIssue)
			{
				issueDescription = $"{positionIssueDesc}; {sizeIssueDesc}";
			}
			else if (hasPositionIssue)
			{
				issueDescription = positionIssueDesc;
			}
			else
			{
				issueDescription = sizeIssueDesc;
			}

			return new VolumeIssue
			{
				gameObject = volumeObject,
				issueDescription = issueDescription,
				currentSize = colliderBounds.size,
				recommendedSize = optimalBounds.size,
				currentPosition = colliderBounds.center,
				recommendedPosition = optimalBounds.center,
				currentVolume = currentVolume,
				recommendedVolume = recommendedVolume,
				affectedRenderers = affectedRenderers,
				hasPositionIssue = hasPositionIssue
			};
		}

		/// <summary>
		/// Calculates the proper bounding box for an occluder (itself + children)
		/// </summary>
		private Bounds CalculateOccluderBounds(GameObject occluderObject)
		{
			// Reuse existing method that already calculates object + children bounds
			Bounds combinedBounds = CalculateObjectBounds(occluderObject);

			if (combinedBounds.size == Vector3.zero)
			{
				return combinedBounds; // No geometry
			}

			// Add small padding (5% of size) to ensure proper coverage
			var padding = combinedBounds.size * 0.05f;
			combinedBounds.size += padding;

			return combinedBounds;
		}

		/// <summary>
		/// Counts renderers that might be incorrectly affected by wrong volume size
		/// </summary>
		private int CountAffectedRenderers(GameObject volumeObject, Bounds currentBounds, Bounds optimalBounds, Renderer[] allRenderers)
		{
			int affectedCount = 0;

			foreach (var renderer in allRenderers)
			{
				// Skip the occluder object itself and its children
				if (renderer.transform.IsChildOf(volumeObject.transform) || renderer.gameObject == volumeObject)
					continue;

				var rendererBounds = renderer.bounds;
				bool currentlyIntersects = currentBounds.Intersects(rendererBounds);
				bool shouldIntersect = optimalBounds.Intersects(rendererBounds);

				// Count objects that are incorrectly included/excluded
				if (currentlyIntersects != shouldIntersect)
				{
					affectedCount++;
				}
			}

			return affectedCount;
		}       /// <summary>
				/// Generates recommendations based on analysis results
				/// </summary>
		private void GenerateRecommendations()
		{
			var issues = new System.Collections.Generic.List<string>();
			var recommendations = new System.Collections.Generic.List<string>();

			// Check occlusion area coverage
			if (analysisResult.occlusionAreas.Length == 0)
			{
				issues.Add("No OcclusionArea components found in scene");
				recommendations.Add("Add OcclusionArea components to define culling regions");
			}

			// Check static object ratio
			float staticRatio = analysisResult.totalRenderers > 0 ?
				(float)analysisResult.staticRenderers / analysisResult.totalRenderers : 0f;
			if (staticRatio < 0.6f)
			{
				issues.Add($"Only {staticRatio:P0} of objects are static");
				recommendations.Add("Mark more non-moving objects as static for better culling");
			}

			// Check occluder/occludee setup
			if (analysisResult.occluders.Length == 0)
			{
				issues.Add("No occluder objects found");
				recommendations.Add("Set up large static objects as occluders");
			}

			if (analysisResult.occludees.Length == 0)
			{
				issues.Add("No occludee objects found");
				recommendations.Add("Mark objects that can be hidden as occludees");
			}

			// Check misconfigured objects
			if (analysisResult.misconfiguredObjects.Length > 0)
			{
				issues.Add($"{analysisResult.misconfiguredObjects.Length} objects have incorrect configuration");
				recommendations.Add("Fix static flags and occlusion settings on flagged objects");
			}

			// Check missing volumes
			if (analysisResult.missingVolumes.Length > 0)
			{
				issues.Add($"{analysisResult.missingVolumes.Length} objects could benefit from occlusion volumes");
				recommendations.Add("Add colliders and proper static flags to large objects");
			}

			// Check volume size issues
			if (analysisResult.undersizedVolumes != null && analysisResult.undersizedVolumes.Length > 0)
			{
				issues.Add($"{analysisResult.undersizedVolumes.Length} occlusion volumes are too small");
				recommendations.Add("Resize undersized volumes to prevent incorrect culling of visible objects");
			}

			if (analysisResult.oversizedVolumes != null && analysisResult.oversizedVolumes.Length > 0)
			{
				issues.Add($"{analysisResult.oversizedVolumes.Length} occlusion volumes are too large");
				recommendations.Add("Optimize oversized volumes for better performance");
			}

			// Check volume position issues
			if (analysisResult.mispositionedVolumes != null && analysisResult.mispositionedVolumes.Length > 0)
			{
				issues.Add($"{analysisResult.mispositionedVolumes.Length} occlusion volumes are positioned incorrectly");
				recommendations.Add("Relocate mispositioned volumes to align with their geometry for effective culling");
			}

			// Overall health check
			if (analysisResult.occlusionCoverage < 0.3f)
			{
				issues.Add("Low occlusion culling coverage");
				recommendations.Add("Increase the number of properly configured occluders and occludees");
			}

			analysisResult.issues = issues.ToArray();
			analysisResult.recommendations = recommendations.ToArray();
		}

		/// <summary>
		/// Displays the detection results in the GUI
		/// </summary>
		private void DisplayDetectionResults()
		{
			EditorGUILayout.Space();
			GUILayout.Label("Detection Results", EditorStyles.boldLabel);

			// Summary stats
			EditorGUILayout.LabelField("Scene Overview:", EditorStyles.boldLabel);
			EditorGUI.indentLevel++;
			EditorGUILayout.LabelField($"Total Renderers: {analysisResult.totalRenderers}");
			EditorGUILayout.LabelField($"Static Renderers: {analysisResult.staticRenderers}");
			EditorGUILayout.LabelField($"Occlusion Areas: {analysisResult.occlusionAreas.Length}");
			EditorGUILayout.LabelField($"Occluders: {analysisResult.occluders.Length}");
			EditorGUILayout.LabelField($"Occludees: {analysisResult.occludees.Length}");
			EditorGUILayout.LabelField($"Coverage: {analysisResult.occlusionCoverage:P1}");
			EditorGUI.indentLevel--;

			// Issues
			if (analysisResult.issues.Length > 0)
			{
				EditorGUILayout.Space();
				EditorGUILayout.LabelField("⚠️ Issues Found:", EditorStyles.boldLabel);
				EditorGUI.indentLevel++;
				foreach (var issue in analysisResult.issues)
				{
					EditorGUILayout.LabelField($"• {issue}");
				}
				EditorGUI.indentLevel--;
			}

			// Recommendations
			if (analysisResult.recommendations.Length > 0)
			{
				EditorGUILayout.Space();
				EditorGUILayout.LabelField("💡 Recommendations:", EditorStyles.boldLabel);
				EditorGUI.indentLevel++;
				foreach (var recommendation in analysisResult.recommendations)
				{
					EditorGUILayout.LabelField($"• {recommendation}");
				}
				EditorGUI.indentLevel--;
			}

			// Problem objects
			if (analysisResult.misconfiguredObjects.Length > 0)
			{
				EditorGUILayout.Space();
				EditorGUILayout.LabelField("🔧 Misconfigured Objects:", EditorStyles.boldLabel);
				EditorGUI.indentLevel++;
				for (int i = 0; i < Mathf.Min(analysisResult.misconfiguredObjects.Length, 5); i++)
				{
					var obj = analysisResult.misconfiguredObjects[i];
					EditorGUILayout.BeginHorizontal();
					EditorGUILayout.LabelField($"• {obj.name}");
					if (GUILayout.Button("Select", GUILayout.Width(60)))
					{
						Selection.activeGameObject = obj;
						EditorGUIUtility.PingObject(obj);
					}
					if (GUILayout.Button("Fix", GUILayout.Width(40)))
					{
						FixSingleObject(obj);
					}
					EditorGUILayout.EndHorizontal();
				}
				if (analysisResult.misconfiguredObjects.Length > 5)
				{
					EditorGUILayout.LabelField($"... and {analysisResult.misconfiguredObjects.Length - 5} more");
				}
				EditorGUI.indentLevel--;
			}

			// Missing volumes
			if (analysisResult.missingVolumes.Length > 0)
			{
				EditorGUILayout.Space();
				EditorGUILayout.LabelField("📦 Objects Needing Volumes:", EditorStyles.boldLabel);
				EditorGUI.indentLevel++;
				for (int i = 0; i < Mathf.Min(analysisResult.missingVolumes.Length, 5); i++)
				{
					var obj = analysisResult.missingVolumes[i];
					EditorGUILayout.BeginHorizontal();
					EditorGUILayout.LabelField($"• {obj.name}");
					if (GUILayout.Button("Select", GUILayout.Width(60)))
					{
						Selection.activeGameObject = obj;
						EditorGUIUtility.PingObject(obj);
					}
					if (GUILayout.Button("Add Volume", GUILayout.Width(80)))
					{
						AddVolumeToObject(obj);
					}
					EditorGUILayout.EndHorizontal();
				}
				if (analysisResult.missingVolumes.Length > 5)
				{
					EditorGUILayout.LabelField($"... and {analysisResult.missingVolumes.Length - 5} more");
				}
				EditorGUI.indentLevel--;
			}

			// Volume size issues
			if (analysisResult.volumeIssues != null && analysisResult.volumeIssues.Length > 0)
			{
				EditorGUILayout.Space();
				EditorGUILayout.LabelField("📏 Volume Size Issues:", EditorStyles.boldLabel);
				EditorGUI.indentLevel++;
				for (int i = 0; i < Mathf.Min(analysisResult.volumeIssues.Length, 8); i++)
				{
					var issue = analysisResult.volumeIssues[i];
					EditorGUILayout.BeginVertical(GUI.skin.box);

					EditorGUILayout.BeginHorizontal();
					if (issue.hasPositionIssue)
					{
						EditorGUILayout.LabelField($"🚨 {issue.gameObject.name}", EditorStyles.boldLabel);
					}
					else if (issue.currentVolume < issue.recommendedVolume * 0.7f)
					{
						EditorGUILayout.LabelField($"⚠️ {issue.gameObject.name}", EditorStyles.boldLabel);
					}
					else
					{
						EditorGUILayout.LabelField($"📊 {issue.gameObject.name}", EditorStyles.boldLabel);
					}
					if (GUILayout.Button("Select", GUILayout.Width(60)))
					{
						Selection.activeGameObject = issue.gameObject;
						EditorGUIUtility.PingObject(issue.gameObject);
					}
					EditorGUILayout.EndHorizontal();

					EditorGUILayout.LabelField($"Issue: {issue.issueDescription}");
					EditorGUILayout.LabelField($"Affected Renderers: {issue.affectedRenderers}");

					// Show size information
					EditorGUILayout.LabelField($"Current Size: {issue.currentSize:F1}");
					EditorGUILayout.LabelField($"Recommended Size: {issue.recommendedSize:F1}");

					// Show position information if there's a position issue
					if (issue.hasPositionIssue)
					{
						EditorGUILayout.LabelField($"Current Position: {issue.currentPosition:F1}");
						EditorGUILayout.LabelField($"Recommended Position: {issue.recommendedPosition:F1}");
					}

					// Action buttons
					EditorGUILayout.BeginHorizontal();
					if (GUILayout.Button("🔧 Auto-Resize Volume"))
					{
						AutoResizeVolume(issue);
					}
					if (issue.hasPositionIssue && GUILayout.Button("📍 Relocate Volume"))
					{
						RelocateVolume(issue);
					}
					EditorGUILayout.EndHorizontal();

					EditorGUILayout.EndVertical();
				}
				if (analysisResult.volumeIssues.Length > 8)
				{
					EditorGUILayout.LabelField($"... and {analysisResult.volumeIssues.Length - 8} more issues");
				}
				EditorGUI.indentLevel--;
			}
		}

		/// <summary>
		/// Automatically fixes common occlusion culling issues in the scene
		/// </summary>
		private void AutoFixOcclusionIssues()
		{
			// First, detect issues if not already done
			if (analysisResult == null)
			{
				DetectOcclusionVolumes();
			}

			if (analysisResult.issues.Length == 0)
			{
				EditorUtility.DisplayDialog("Auto Fix", "No issues detected! Scene occlusion is already well-configured.", "OK");
				return;
			}

			bool confirm = EditorUtility.DisplayDialog("Auto Fix Occlusion Issues",
				$"This will automatically fix {analysisResult.issues.Length} detected issues:\n\n" +
				string.Join("\n• ", analysisResult.issues) + "\n\n" +
				"This action can be undone. Continue?",
				"Fix Issues", "Cancel");

			if (!confirm) return;

			int fixedCount = 0;
			var allObjects = new System.Collections.Generic.List<GameObject>();

			// Collect all objects that will be modified
			allObjects.AddRange(analysisResult.misconfiguredObjects);
			allObjects.AddRange(analysisResult.missingVolumes);

			// Add static renderers that need occludee flags
			var allRenderers = FindObjectsOfType<Renderer>();
			var staticRenderers = System.Array.FindAll(allRenderers, r =>
				GameObjectUtility.AreStaticEditorFlagsSet(r.gameObject, StaticEditorFlags.BatchingStatic) &&
				!GameObjectUtility.AreStaticEditorFlagsSet(r.gameObject, StaticEditorFlags.OccludeeStatic));

			foreach (var renderer in staticRenderers)
			{
				if (!allObjects.Contains(renderer.gameObject))
					allObjects.Add(renderer.gameObject);
			}

			// Record undo for all objects
			if (allObjects.Count > 0)
			{
				Undo.RecordObjects(allObjects.ToArray(), "Auto Fix Occlusion Issues");
			}

			// Fix misconfigured objects
			foreach (var obj in analysisResult.misconfiguredObjects)
			{
				if (FixSingleObject(obj))
					fixedCount++;
			}

			// Add volumes to objects that need them
			foreach (var obj in analysisResult.missingVolumes)
			{
				if (AddVolumeToObject(obj))
					fixedCount++;
			}

			// Set occludee flags for remaining static objects
			foreach (var renderer in staticRenderers)
			{
				var flags = GameObjectUtility.GetStaticEditorFlags(renderer.gameObject);
				flags |= StaticEditorFlags.OccludeeStatic;
				GameObjectUtility.SetStaticEditorFlags(renderer.gameObject, flags);
				fixedCount++;
			}

			// Auto-fix critically undersized volumes (prevents visible object culling)
			if (analysisResult.volumeIssues != null)
			{
				foreach (var issue in analysisResult.volumeIssues)
				{
					// Only auto-fix severely undersized volumes that cause visible culling issues
					if (issue.currentVolume < issue.recommendedVolume * 0.5f && issue.affectedRenderers > 0)
					{
						try
						{
							var colliders = issue.gameObject.GetComponents<BoxCollider>();
							if (colliders.Length > 0)
							{
								var collider = colliders[0];
								collider.size = issue.recommendedSize;
								EditorUtility.SetDirty(issue.gameObject);
								fixedCount++;
								Debug.Log($"[Auto Fix] Auto-resized critically undersized volume: {issue.gameObject.name}");
							}
						}
						catch (System.Exception e)
						{
							Debug.LogWarning($"[Auto Fix] Could not auto-resize volume {issue.gameObject.name}: {e.Message}");
						}
					}

					// Auto-fix critically mispositioned volumes (underground volumes, etc.)
					if (issue.hasPositionIssue)
					{
						// Check if it's an underground volume (critical position issue)
						if (issue.currentPosition.y < issue.recommendedPosition.y - 2f) // More than 2 units underground
						{
							try
							{
								var colliders = issue.gameObject.GetComponents<BoxCollider>();
								if (colliders.Length > 0)
								{
									var collider = colliders[0];
									var localRecommendedCenter = issue.gameObject.transform.InverseTransformPoint(issue.recommendedPosition);
									collider.center = localRecommendedCenter;
									EditorUtility.SetDirty(issue.gameObject);
									fixedCount++;
									Debug.Log($"[Auto Fix] Auto-relocated underground volume: {issue.gameObject.name}");
								}
							}
							catch (System.Exception e)
							{
								Debug.LogWarning($"[Auto Fix] Could not auto-relocate volume {issue.gameObject.name}: {e.Message}");
							}
						}
					}
				}
			}

			// Refresh analysis
			DetectOcclusionVolumes();

			EditorUtility.DisplayDialog("Auto Fix Complete",
				$"Successfully fixed {fixedCount} occlusion issues!\n\n" +
				$"New occlusion coverage: {analysisResult.occlusionCoverage:P1}\n" +
				$"Remaining issues: {analysisResult.issues.Length}",
				"OK");

			Debug.Log($"[Auto Fix] Fixed {fixedCount} occlusion issues. Coverage improved to {analysisResult.occlusionCoverage:P1}");
		}

		/// <summary>
		/// Fixes a single misconfigured object
		/// </summary>
		private bool FixSingleObject(GameObject obj)
		{
			try
			{
				var flags = GameObjectUtility.GetStaticEditorFlags(obj);
				var renderer = obj.GetComponent<Renderer>();

				if (renderer == null) return false;

				// Ensure object is static
				if ((flags & StaticEditorFlags.BatchingStatic) == 0)
				{
					flags |= StaticEditorFlags.BatchingStatic;
				}

				// Determine if it should be an occluder or occludee based on size
				var bounds = renderer.bounds;
				if (bounds.size.magnitude > 5f) // Large objects become occluders
				{
					flags |= StaticEditorFlags.OccluderStatic;

					// Add collider if missing
					if (obj.GetComponent<Collider>() == null)
					{
						var collider = obj.AddComponent<BoxCollider>();
						collider.isTrigger = true;
						collider.size = bounds.size;
						collider.center = bounds.center - obj.transform.position;
					}
				}
				else // Smaller objects become occludees
				{
					flags |= StaticEditorFlags.OccludeeStatic;
				}

				GameObjectUtility.SetStaticEditorFlags(obj, flags);
				EditorUtility.SetDirty(obj);

				Debug.Log($"[Auto Fix] Fixed {obj.name}");
				return true;
			}
			catch (System.Exception e)
			{
				Debug.LogError($"[Auto Fix] Failed to fix {obj.name}: {e.Message}");
				return false;
			}
		}

		/// <summary>
		/// Adds an occlusion volume to an object
		/// </summary>
		private bool AddVolumeToObject(GameObject obj)
		{
			try
			{
				var renderer = obj.GetComponent<Renderer>();
				if (renderer == null) return false;

				// Add BoxCollider if missing
				var collider = obj.GetComponent<BoxCollider>();
				if (collider == null)
				{
					collider = obj.AddComponent<BoxCollider>();
				}

				// Configure collider
				collider.isTrigger = true;
				var bounds = renderer.bounds;
				collider.size = bounds.size;
				collider.center = bounds.center - obj.transform.position;

				// Set appropriate static flags
				var flags = GameObjectUtility.GetStaticEditorFlags(obj);
				flags |= StaticEditorFlags.BatchingStatic;

				if (bounds.size.magnitude > 5f)
				{
					flags |= StaticEditorFlags.OccluderStatic;
				}
				else
				{
					flags |= StaticEditorFlags.OccludeeStatic;
				}

				GameObjectUtility.SetStaticEditorFlags(obj, flags);
				EditorUtility.SetDirty(obj);

				Debug.Log($"[Auto Fix] Added volume to {obj.name}");
				return true;
			}
			catch (System.Exception e)
			{
				Debug.LogError($"[Auto Fix] Failed to add volume to {obj.name}: {e.Message}");
				return false;
			}
		}

		/// <summary>
		/// Automatically resizes an occlusion volume based on volume issue analysis
		/// </summary>
		private void AutoResizeVolume(VolumeIssue issue)
		{
			try
			{
				var colliders = issue.gameObject.GetComponents<BoxCollider>();
				if (colliders.Length == 0)
				{
					EditorUtility.DisplayDialog("Error", $"No BoxCollider found on {issue.gameObject.name}", "OK");
					return;
				}

				bool confirmResize = EditorUtility.DisplayDialog("Resize Volume",
					$"Resize {issue.gameObject.name} from {issue.currentSize:F1} to {issue.recommendedSize:F1}?\n\n" +
					$"Issue: {issue.issueDescription}\n" +
					$"This will affect {issue.affectedRenderers} renderers.",
					"Resize", "Cancel");

				if (!confirmResize) return;

				Undo.RecordObject(colliders[0], "Resize Occlusion Volume");

				// Resize the first BoxCollider to the recommended size
				var collider = colliders[0];
				collider.size = issue.recommendedSize;

				// Optionally adjust center if needed (keep current center for now)
				EditorUtility.SetDirty(issue.gameObject);

				Debug.Log($"[Volume Resize] Resized {issue.gameObject.name} to {issue.recommendedSize:F1}");

				// Re-run detection to update results
				DetectOcclusionVolumes();
			}
			catch (System.Exception e)
			{
				Debug.LogError($"[Volume Resize] Failed to resize {issue.gameObject.name}: {e.Message}");
			}
		}

		/// <summary>
		/// Relocates an occlusion volume to the optimal position based on geometry
		/// </summary>
		private void RelocateVolume(VolumeIssue issue)
		{
			try
			{
				var colliders = issue.gameObject.GetComponents<BoxCollider>();
				if (colliders.Length == 0)
				{
					EditorUtility.DisplayDialog("Error", $"No BoxCollider found on {issue.gameObject.name}", "OK");
					return;
				}

				var currentPos = issue.currentPosition;
				var recommendedPos = issue.recommendedPosition;
				var distance = Vector3.Distance(currentPos, recommendedPos);

				bool confirmRelocate = EditorUtility.DisplayDialog("Relocate Volume",
					$"Relocate {issue.gameObject.name}'s occlusion volume?\n\n" +
					$"From: {currentPos:F1}\n" +
					$"To: {recommendedPos:F1}\n" +
					$"Distance: {distance:F1} units\n\n" +
					$"Issue: {issue.issueDescription}",
					"Relocate", "Cancel");

				if (!confirmRelocate) return;

				Undo.RecordObject(colliders[0], "Relocate Occlusion Volume");

				// Calculate the new center in local space
				var collider = colliders[0];
				var localRecommendedCenter = issue.gameObject.transform.InverseTransformPoint(recommendedPos);
				collider.center = localRecommendedCenter;

				EditorUtility.SetDirty(issue.gameObject);

				Debug.Log($"[Volume Relocate] Moved {issue.gameObject.name} volume from {currentPos:F1} to {recommendedPos:F1}");

				// Re-run detection to update results
				DetectOcclusionVolumes();
			}
			catch (System.Exception e)
			{
				Debug.LogError($"[Volume Relocate] Failed to relocate {issue.gameObject.name}: {e.Message}");
			}
		}

		#region Camera-Based Occlusion Testing

		/// <summary>
		/// Tests occlusion culling from the current camera position
		/// </summary>
		private void TestSingleCameraPosition()
		{
			var mainCamera = Camera.main;
			if (mainCamera == null)
			{
				EditorUtility.DisplayDialog("No Main Camera",
					"No main camera found in scene. Please ensure you have a camera tagged as 'MainCamera'.", "OK");
				return;
			}

			Debug.Log("[Camera Test] Testing occlusion culling from current camera position...");

			var testPoint = CreateCameraTestPoint(mainCamera.transform.position, mainCamera.transform.forward,
				"Current Camera Position");

			cameraTestResult = new CameraTestResult
			{
				testPositions = new Vector3[] { mainCamera.transform.position },
				testPoints = new CameraTestPoint[] { testPoint }
			};

			AnalyzeCameraTestResults();
			showCameraTestResults = true;

			Debug.Log($"[Camera Test] Found {testPoint.incorrectlyCulled.Length} incorrectly culled objects from current position");
		}

		/// <summary>
		/// Tests occlusion culling from multiple strategic viewpoints
		/// </summary>
		private void TestMultipleCameraPositions()
		{
			var mainCamera = Camera.main;
			if (mainCamera == null)
			{
				EditorUtility.DisplayDialog("No Main Camera",
					"No main camera found in scene. Please ensure you have a camera tagged as 'MainCamera'.", "OK");
				return;
			}

			Debug.Log("[Camera Test] Testing occlusion culling from multiple viewpoints...");

			// Generate test positions around the scene
			var testPositions = GenerateTestPositions(mainCamera.transform.position);
			var testPoints = new System.Collections.Generic.List<CameraTestPoint>();

			var originalPosition = mainCamera.transform.position;
			var originalRotation = mainCamera.transform.rotation;

			try
			{
				for (int i = 0; i < testPositions.Length; i++)
				{
					var position = testPositions[i];

					// Point camera towards scene center
					var centerDirection = (Vector3.zero - position).normalized;

					var testPoint = CreateCameraTestPoint(position, centerDirection, $"Test Position {i + 1}");
					testPoints.Add(testPoint);

					if (EditorUtility.DisplayCancelableProgressBar("Testing Camera Positions",
						$"Testing position {i + 1} of {testPositions.Length}", (float)i / testPositions.Length))
					{
						break;
					}
				}
			}
			finally
			{
				// Restore original camera position
				mainCamera.transform.position = originalPosition;
				mainCamera.transform.rotation = originalRotation;
				EditorUtility.ClearProgressBar();
			}

			cameraTestResult = new CameraTestResult
			{
				testPositions = testPositions,
				testPoints = testPoints.ToArray()
			};

			AnalyzeCameraTestResults();
			showCameraTestResults = true;

			Debug.Log($"[Camera Test] Completed testing {testPositions.Length} viewpoints");
		}

		/// <summary>
		/// Performs intelligent analysis of optimal viewpoints based on scene geometry
		/// </summary>
		private void SmartViewpointAnalysis()
		{
			var mainCamera = Camera.main;
			if (mainCamera == null)
			{
				EditorUtility.DisplayDialog("No Main Camera",
					"No main camera found in scene. Please ensure you have a camera tagged as 'MainCamera'.", "OK");
				return;
			}

			Debug.Log("[Smart Analysis] Analyzing scene for optimal occlusion testing viewpoints...");

			// Find all rendered objects to determine scene bounds
			var allRenderers = FindObjectsOfType<Renderer>();
			if (allRenderers.Length == 0)
			{
				EditorUtility.DisplayDialog("No Renderers", "No renderers found in scene to analyze.", "OK");
				return;
			}

			// Calculate scene bounds
			Bounds sceneBounds = allRenderers[0].bounds;
			foreach (var renderer in allRenderers)
			{
				sceneBounds.Encapsulate(renderer.bounds);
			}

			// Generate smart test positions based on scene geometry
			var smartPositions = GenerateSmartTestPositions(sceneBounds, allRenderers);
			var testPoints = new System.Collections.Generic.List<CameraTestPoint>();

			var originalPosition = mainCamera.transform.position;
			var originalRotation = mainCamera.transform.rotation;

			try
			{
				for (int i = 0; i < smartPositions.Length; i++)
				{
					var position = smartPositions[i].position;
					var direction = smartPositions[i].direction;
					var description = smartPositions[i].description;

					var testPoint = CreateCameraTestPoint(position, direction, description);
					testPoints.Add(testPoint);

					if (EditorUtility.DisplayCancelableProgressBar("Smart Viewpoint Analysis",
						$"Testing {description}", (float)i / smartPositions.Length))
					{
						break;
					}
				}
			}
			finally
			{
				mainCamera.transform.position = originalPosition;
				mainCamera.transform.rotation = originalRotation;
				EditorUtility.ClearProgressBar();
			}

			cameraTestResult = new CameraTestResult
			{
				testPositions = smartPositions.Select(sp => sp.position).ToArray(),
				testPoints = testPoints.ToArray()
			};

			AnalyzeCameraTestResults();
			showCameraTestResults = true;

			Debug.Log($"[Smart Analysis] Completed analysis of {smartPositions.Length} strategic viewpoints");
		}

		/// <summary>
		/// Creates a test point by moving camera and analyzing visibility
		/// </summary>
		private CameraTestPoint CreateCameraTestPoint(Vector3 position, Vector3 lookDirection, string description)
		{
			var mainCamera = Camera.main;
			var allRenderers = FindObjectsOfType<Renderer>();

			// Move camera to test position
			mainCamera.transform.position = position;
			mainCamera.transform.LookAt(position + lookDirection);

			// Force camera to render to update visibility
			mainCamera.Render();

			var visibleObjects = new System.Collections.Generic.List<GameObject>();
			var culledObjects = new System.Collections.Generic.List<GameObject>();
			var incorrectlyCulled = new System.Collections.Generic.List<GameObject>();

			foreach (var renderer in allRenderers)
			{
				// Check if object should be visible based on geometric line-of-sight
				bool shouldBeVisible = ShouldBeVisibleFromPosition(position, renderer);
				bool isActuallyVisible = renderer.isVisible;

				if (isActuallyVisible)
				{
					visibleObjects.Add(renderer.gameObject);
				}
				else
				{
					culledObjects.Add(renderer.gameObject);

					// If it should be visible but isn't, it's incorrectly culled
					if (shouldBeVisible)
					{
						incorrectlyCulled.Add(renderer.gameObject);
					}
				}
			}

			float efficiency = allRenderers.Length > 0 ?
				(float)(allRenderers.Length - incorrectlyCulled.Count) / allRenderers.Length : 1f;

			return new CameraTestPoint
			{
				position = position,
				lookDirection = lookDirection,
				visibleObjects = visibleObjects.ToArray(),
				culledObjects = culledObjects.ToArray(),
				incorrectlyCulled = incorrectlyCulled.ToArray(),
				cullingEfficiency = efficiency,
				description = description
			};
		}

		/// <summary>
		/// Determines if an object should be visible from a given position using geometric analysis
		/// </summary>
		private bool ShouldBeVisibleFromPosition(Vector3 viewPosition, Renderer renderer)
		{
			// Simple line-of-sight check - can be made more sophisticated
			var rendererBounds = renderer.bounds;
			var direction = (rendererBounds.center - viewPosition).normalized;
			var distance = Vector3.Distance(viewPosition, rendererBounds.center);

			// Cast ray to see if anything blocks the view
			RaycastHit hit;
			if (Physics.Raycast(viewPosition, direction, out hit, distance))
			{
				// If we hit the renderer itself or its collider, it should be visible
				return hit.collider.GetComponent<Renderer>() == renderer;
			}

			// No obstruction found, should be visible
			return true;
		}

		/// <summary>
		/// Generates test positions around the current camera position
		/// </summary>
		private Vector3[] GenerateTestPositions(Vector3 centerPosition)
		{
			var positions = new System.Collections.Generic.List<Vector3>();
			float radius = 10f;
			int numPositions = 8;

			for (int i = 0; i < numPositions; i++)
			{
				float angle = (float)i / numPositions * 360f * Mathf.Deg2Rad;
				var offset = new Vector3(Mathf.Cos(angle) * radius, 0, Mathf.Sin(angle) * radius);
				positions.Add(centerPosition + offset);
			}

			// Add some height variations
			positions.Add(centerPosition + Vector3.up * 5f);
			positions.Add(centerPosition + Vector3.up * 10f);

			return positions.ToArray();
		}

		private struct SmartTestPosition
		{
			public Vector3 position;
			public Vector3 direction;
			public string description;
		}

		/// <summary>
		/// Generates intelligent test positions based on scene geometry
		/// </summary>
		private SmartTestPosition[] GenerateSmartTestPositions(Bounds sceneBounds, Renderer[] allRenderers)
		{
			var positions = new System.Collections.Generic.List<SmartTestPosition>();

			// Add corner positions looking inward
			var corners = new Vector3[]
			{
				new Vector3(sceneBounds.min.x, sceneBounds.center.y, sceneBounds.min.z),
				new Vector3(sceneBounds.max.x, sceneBounds.center.y, sceneBounds.min.z),
				new Vector3(sceneBounds.min.x, sceneBounds.center.y, sceneBounds.max.z),
				new Vector3(sceneBounds.max.x, sceneBounds.center.y, sceneBounds.max.z)
			};

			for (int i = 0; i < corners.Length; i++)
			{
				var direction = (sceneBounds.center - corners[i]).normalized;
				positions.Add(new SmartTestPosition
				{
					position = corners[i],
					direction = direction,
					description = $"Corner View {i + 1}"
				});
			}

			// Add positions near large occluders
			var occluders = System.Array.FindAll(allRenderers, r =>
				GameObjectUtility.AreStaticEditorFlagsSet(r.gameObject, StaticEditorFlags.OccluderStatic));

			foreach (var occluder in occluders)
			{
				var bounds = occluder.bounds;
				var testPos = bounds.center + bounds.size.normalized * bounds.size.magnitude;
				var direction = (sceneBounds.center - testPos).normalized;

				positions.Add(new SmartTestPosition
				{
					position = testPos,
					direction = direction,
					description = $"Near Occluder: {occluder.name}"
				});
			}

			return positions.ToArray();
		}

		/// <summary>
		/// Analyzes camera test results and generates recommendations
		/// </summary>
		private void AnalyzeCameraTestResults()
		{
			if (cameraTestResult?.testPoints == null) return;

			var allIncorrectlyCulled = new System.Collections.Generic.HashSet<GameObject>();
			var issues = new System.Collections.Generic.List<string>();
			var recommendations = new System.Collections.Generic.List<string>();

			float totalEfficiency = 0f;
			int criticalIssueCount = 0;

			foreach (var testPoint in cameraTestResult.testPoints)
			{
				totalEfficiency += testPoint.cullingEfficiency;

				if (testPoint.incorrectlyCulled.Length > 0)
				{
					foreach (var obj in testPoint.incorrectlyCulled)
					{
						allIncorrectlyCulled.Add(obj);
					}

					if (testPoint.incorrectlyCulled.Length > 3) // More than 3 objects incorrectly culled
					{
						criticalIssueCount++;
					}
				}
			}

			cameraTestResult.overallEfficiency = totalEfficiency / cameraTestResult.testPoints.Length;
			cameraTestResult.incorrectlyCulledObjects = new GameObject[allIncorrectlyCulled.Count];
			allIncorrectlyCulled.CopyTo(cameraTestResult.incorrectlyCulledObjects);

			// Generate issues and recommendations
			if (allIncorrectlyCulled.Count > 0)
			{
				issues.Add($"{allIncorrectlyCulled.Count} objects are being incorrectly culled");
				recommendations.Add("Check occluder volume sizes - they may be too large for their geometry");
			}

			if (criticalIssueCount > 0)
			{
				issues.Add($"{criticalIssueCount} viewpoints have critical culling issues (>3 objects incorrectly culled)");
				recommendations.Add("Focus on fixing the most problematic occluders first");
			}

			if (cameraTestResult.overallEfficiency < 0.8f)
			{
				issues.Add($"Low culling efficiency: {cameraTestResult.overallEfficiency:P1}");
				recommendations.Add("Review and optimize occlusion volume placement and sizing");
			}

			cameraTestResult.issues = issues.ToArray();
			cameraTestResult.recommendations = recommendations.ToArray();
		}

		/// <summary>
		/// Displays camera test results in the UI
		/// </summary>
		private void DisplayCameraTestResults()
		{
			EditorGUILayout.Space();
			GUILayout.Label("🎥 Camera Test Results", EditorStyles.boldLabel);

			// Overall statistics
			EditorGUILayout.LabelField("Overall Efficiency:", $"{cameraTestResult.overallEfficiency:P1}");
			EditorGUILayout.LabelField("Test Points:", cameraTestResult.testPoints.Length.ToString());
			EditorGUILayout.LabelField("Incorrectly Culled Objects:", cameraTestResult.incorrectlyCulledObjects.Length.ToString());

			// Issues
			if (cameraTestResult.issues.Length > 0)
			{
				EditorGUILayout.Space();
				EditorGUILayout.LabelField("⚠️ Issues Found:", EditorStyles.boldLabel);
				foreach (var issue in cameraTestResult.issues)
				{
					EditorGUILayout.LabelField($"• {issue}");
				}
			}

			// Recommendations
			if (cameraTestResult.recommendations.Length > 0)
			{
				EditorGUILayout.Space();
				EditorGUILayout.LabelField("💡 Recommendations:", EditorStyles.boldLabel);
				foreach (var recommendation in cameraTestResult.recommendations)
				{
					EditorGUILayout.LabelField($"• {recommendation}");
				}
			}

			// Incorrectly culled objects
			if (cameraTestResult.incorrectlyCulledObjects.Length > 0)
			{
				EditorGUILayout.Space();
				EditorGUILayout.LabelField("🚨 Incorrectly Culled Objects:", EditorStyles.boldLabel);
				for (int i = 0; i < Mathf.Min(cameraTestResult.incorrectlyCulledObjects.Length, 10); i++)
				{
					var obj = cameraTestResult.incorrectlyCulledObjects[i];
					EditorGUILayout.BeginHorizontal();
					EditorGUILayout.LabelField($"• {obj.name}");
					if (GUILayout.Button("Select", GUILayout.Width(60)))
					{
						Selection.activeGameObject = obj;
						EditorGUIUtility.PingObject(obj);
					}
					EditorGUILayout.EndHorizontal();
				}
				if (cameraTestResult.incorrectlyCulledObjects.Length > 10)
				{
					EditorGUILayout.LabelField($"... and {cameraTestResult.incorrectlyCulledObjects.Length - 10} more");
				}
			}

			// Test point details
			if (cameraTestResult.testPoints.Length > 0)
			{
				EditorGUILayout.Space();
				EditorGUILayout.LabelField("📍 Test Point Details:", EditorStyles.boldLabel);
				for (int i = 0; i < Mathf.Min(cameraTestResult.testPoints.Length, 5); i++)
				{
					var testPoint = cameraTestResult.testPoints[i];
					EditorGUILayout.BeginVertical(GUI.skin.box);
					EditorGUILayout.LabelField($"{testPoint.description}", EditorStyles.boldLabel);
					EditorGUILayout.LabelField($"Position: {testPoint.position:F1}");
					EditorGUILayout.LabelField($"Efficiency: {testPoint.cullingEfficiency:P1}");
					EditorGUILayout.LabelField($"Incorrectly Culled: {testPoint.incorrectlyCulled.Length}");

					if (GUILayout.Button("Move Camera Here"))
					{
						var mainCamera = Camera.main;
						if (mainCamera != null)
						{
							mainCamera.transform.position = testPoint.position;
							mainCamera.transform.LookAt(testPoint.position + testPoint.lookDirection);
							SceneView.lastActiveSceneView?.LookAt(testPoint.position);
						}
					}
					EditorGUILayout.EndVertical();
				}
			}
		}

		#endregion

	}
}