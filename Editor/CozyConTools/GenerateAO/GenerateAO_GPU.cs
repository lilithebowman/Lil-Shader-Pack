// Assets/Editor/CozyConTools/GenerateAO/GenerateAO_GPU.cs
// AO baker with selectable mode (Auto/GPU/CPU), automatic AO radius calculation,
// camera-based UV->world rendering, debug visualization using DebugAORenderer,
// automatic debug RT saving, an RT sanity test button, and extra diagnostics.
//
// This version adds a "Run RT Sanity Test" button that fills uvPosRT/uvNormalRT
// with a visible gradient so you can confirm the readback/save pipeline works.
// If the sanity test images are visible but uvPos_debug.png from the real render
// is still black, the problem is almost certainly the UVUnwrap shader or the
// way the shader is compiled/used in your project (SRP/URP/HDRP differences).
//
// Save this file as:
//   Assets/Editor/CozyConTools/GenerateAO/GenerateAO_GPU.cs
//
// Keep DebugAORenderer.cs, Hidden/Lilithe/UVUnwrap shader and AOCompute.compute
// in the same project. Run "Run RT Sanity Test" first to confirm RT readback works.

using UnityEngine;
using UnityEditor;
using System.IO;
using System.Collections.Generic;
using CozyConTools.GenerateAO;

public class GenerateAO_GPU : EditorWindow
{
	enum BakeMode { Auto, GPU, CPU }

	bool debugRendererActive;
	Vector2 scrollPosition;
	Texture2D uvCoveragePreviewTexture;
	Texture2D uvNormalPreviewTexture;
	string uvPreviewStatus = "No UV preview rendered yet.";
	string uvOverlapStatus = "UV overlap not checked.";
	bool uvOverlapDetected;
	float uvOverlapRatio;

	GameObject model;
	int textureSize = 1024;
	int samples = 64;
	bool saveAsPNG = true;
	BakeMode mode = BakeMode.Auto;

	// AO radius controls
	float aoRadius = 1.0f;                 // manual radius
	bool autoRadius = true;                // compute radius from model bounds
	float autoRadiusScale = 1.05f;         // scale factor applied to computed bounding sphere radius
	float occlusionFalloffPower = 2.0f;    // higher values bias occlusion toward nearer blockers
	float computedAOAutoRadius = 0f;

	// Debug visualization
	bool showDebugSphere = true;
	bool showDebugRays = false;
	int debugRayStride = 10; // draw 1 in N rays (higher = fewer drawn)

	Material uvUnwrapMaterial;
	ComputeShader aoCompute;

	RenderTexture uvPosRT;
	RenderTexture uvNormalRT;
	RenderTexture aoRT;

	ComputeBuffer vertexBuffer;
	ComputeBuffer indexBuffer;

	// Combined bounds computed during mesh collection
	Bounds combinedWorldBounds;

	// Temporary layer used for camera rendering (unused but reserved)
	const int kTempLayer = 31;
	const int kUVOverlapCheckResolution = 256;
	const string kGeneratedAOUVFolder = "Assets/GeneratedMeshes/AOUnwrapped";

	[MenuItem("Lilithe/Generate Ambient Occlusion Map for Model")]
	static void Init()
	{
		var window = GetWindow<GenerateAO_GPU>();
		window.titleContent = new GUIContent("AO Baker");
		window.Show();
	}

	void OnGUI()
	{
		scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);
		GUILayout.Label("Ambient Occlusion Baker", EditorStyles.boldLabel);
		bool previewSettingsChanged = false;

		EditorGUI.BeginChangeCheck();
		model = (GameObject)EditorGUILayout.ObjectField("Model", model, typeof(GameObject), false);

		if (GUILayout.Button("Use Current"))
		{
			var selected = Selection.activeGameObject;
			if (selected == null)
				EditorUtility.DisplayDialog("No Selection", "No object is currently selected in the Hierarchy.", "OK");
			else if (!HasMesh(selected))
				EditorUtility.DisplayDialog("Invalid Selection", "The selected object does not contain a MeshFilter or SkinnedMeshRenderer.", "OK");
			else
			{
				model = selected;
				previewSettingsChanged = true;
			}
		}

		textureSize = EditorGUILayout.IntField("Texture Size", textureSize);
		samples = EditorGUILayout.IntSlider("Samples", samples, 4, 512);
		saveAsPNG = EditorGUILayout.Toggle("Save as PNG", saveAsPNG);

		// AO radius controls
		EditorGUILayout.Space();
		EditorGUILayout.LabelField("AO Radius", EditorStyles.boldLabel);
		autoRadius = EditorGUILayout.Toggle("Auto Radius (fit model)", autoRadius);
		if (autoRadius)
		{
			autoRadiusScale = EditorGUILayout.FloatField("Auto Radius Scale", autoRadiusScale);
			EditorGUILayout.LabelField("Computed Radius", computedAOAutoRadius.ToString("F4") + " (world units)");
		}
		else
		{
			aoRadius = EditorGUILayout.FloatField("Manual Radius", aoRadius);
		}
		occlusionFalloffPower = EditorGUILayout.Slider("Falloff Power", occlusionFalloffPower, 0.25f, 8f);

		// Mode selector
		EditorGUILayout.Space();
		EditorGUILayout.LabelField("Bake Mode", EditorStyles.boldLabel);
		mode = (BakeMode)EditorGUILayout.EnumPopup("Mode", mode);
		EditorGUILayout.HelpBox("Auto: try GPU then fallback to CPU. GPU: requires AOCompute.compute. CPU: uses raycasts (slower).", MessageType.Info);

		// Debug options
		EditorGUILayout.Space();
		EditorGUILayout.LabelField("Scene Preview", EditorStyles.boldLabel);
		showDebugSphere = EditorGUILayout.Toggle("Show Debug Sphere", showDebugSphere);
		showDebugRays = EditorGUILayout.Toggle("Draw CPU Rays (legacy)", showDebugRays);
		if (showDebugRays)
		{
			debugRayStride = EditorGUILayout.IntSlider("Debug Ray Stride (1 = every ray)", debugRayStride, 1, 128);
		}

		if (EditorGUI.EndChangeCheck())
		{
			previewSettingsChanged = true;
		}

		if (previewSettingsChanged)
		{
			UpdateDebugSpherePreview();
			UpdateUVOverlapStatus();
		}

		EditorGUILayout.Space();
		EditorGUILayout.LabelField("UV Preview", EditorStyles.boldLabel);
		EditorGUILayout.HelpBox("Refresh UV Preview renders the current UV unwrap into preview textures so you can verify island coverage without relying on scene gizmos.", MessageType.Info);
		EditorGUILayout.BeginHorizontal();
		if (GUILayout.Button("Refresh UV Preview"))
		{
			RenderUVPreview();
		}
		if (GUILayout.Button("Check UV Overlap"))
		{
			UpdateUVOverlapStatus();
		}
		EditorGUILayout.EndHorizontal();
		EditorGUILayout.LabelField(uvPreviewStatus, EditorStyles.wordWrappedMiniLabel);
		EditorGUILayout.LabelField(uvOverlapStatus, EditorStyles.wordWrappedMiniLabel);
		if (uvOverlapDetected)
		{
			EditorGUILayout.HelpBox("The current UV0 layout overlaps in bake space, so different surfaces will share AO texels.", MessageType.Warning);
			if (GUILayout.Button("Bake New Non-Overlapping UV Map"))
			{
				BakeNewUVMapForModel();
			}
		}
		DrawUVPreviewPanel();

		EditorGUILayout.Space();

		// Sanity test button: fills uvPosRT/uvNormalRT with visible data and saves them.
		if (GUILayout.Button("Run RT Sanity Test (fills uvPos/uvNormal with gradient)"))
		{
			AllocateRTs();
			RunRTSanityFillAndSave();
			RebuildPreviewTextures();
			uvPreviewStatus = "RT sanity textures rendered into the preview panel.";
		}

		EditorGUILayout.Space();
		if (GUILayout.Button("Generate AO"))
		{
			if (!ValidateModel())
			{
				EditorGUILayout.EndScrollView();
				return;
			}
			GenerateAO();
		}
		EditorGUILayout.EndScrollView();
	}

	void OnEnable()
	{
		EnableToolDebugRenderer();
		UpdateDebugSpherePreview();
		UpdateUVOverlapStatus();
	}

	void OnFocus()
	{
		EnableToolDebugRenderer();
		UpdateDebugSpherePreview();
		UpdateUVOverlapStatus();
	}

	void OnLostFocus()
	{
		SceneView.RepaintAll();
	}

	void EnableToolDebugRenderer()
	{
		if (debugRendererActive)
		{
			return;
		}

		debugRendererActive = true;
		DebugAORenderer.SetEnabled(true);
		DebugAORenderer.SetAutoExpireRays(false, 60f);
		SceneView.RepaintAll();
		Repaint();
	}

	void UpdateDebugSpherePreview()
	{
		if (!debugRendererActive)
		{
			EnableToolDebugRenderer();
		}

		if (!showDebugSphere || model == null || !HasMesh(model))
		{
			computedAOAutoRadius = autoRadius ? 0f : aoRadius;
			DebugAORenderer.ClearSphere();
			SceneView.RepaintAll();
			return;
		}

		if (!TryCalculateModelWorldBounds(model, out Bounds previewBounds))
		{
			computedAOAutoRadius = 0f;
			DebugAORenderer.ClearSphere();
			SceneView.RepaintAll();
			return;
		}

		combinedWorldBounds = previewBounds;
		computedAOAutoRadius = autoRadius ? previewBounds.extents.magnitude * autoRadiusScale : aoRadius;
		DebugAORenderer.SetSphere(previewBounds.center, computedAOAutoRadius);
		SceneView.RepaintAll();
	}

	bool TryCalculateModelWorldBounds(GameObject root, out Bounds bounds)
	{
		bounds = new Bounds(Vector3.zero, Vector3.zero);
		if (root == null) return false;

		bool boundsInitialized = false;
		var meshFilters = root.GetComponentsInChildren<MeshFilter>(true);
		for (int i = 0; i < meshFilters.Length; i++)
		{
			var mf = meshFilters[i];
			if (mf == null || mf.sharedMesh == null) continue;
			EncapsulateWorldVertices(mf.sharedMesh, mf.transform.localToWorldMatrix, ref bounds, ref boundsInitialized);
		}

		var skinnedRenderers = root.GetComponentsInChildren<SkinnedMeshRenderer>(true);
		for (int i = 0; i < skinnedRenderers.Length; i++)
		{
			var smr = skinnedRenderers[i];
			if (smr == null || smr.sharedMesh == null) continue;

			Mesh baked = new Mesh();
			smr.BakeMesh(baked);
			EncapsulateWorldVertices(baked, smr.transform.localToWorldMatrix, ref bounds, ref boundsInitialized);
			Object.DestroyImmediate(baked);
		}

		return boundsInitialized;
	}

	void EncapsulateWorldVertices(Mesh mesh, Matrix4x4 worldMatrix, ref Bounds bounds, ref bool boundsInitialized)
	{
		var vertices = mesh.vertices;
		for (int i = 0; i < vertices.Length; i++)
		{
			Vector3 worldVertex = worldMatrix.MultiplyPoint3x4(vertices[i]);
			if (!boundsInitialized)
			{
				bounds = new Bounds(worldVertex, Vector3.zero);
				boundsInitialized = true;
			}
			else
			{
				bounds.Encapsulate(worldVertex);
			}
		}
	}

	bool ValidateModel()
	{
		if (model == null)
		{
			EditorUtility.DisplayDialog("Error", "Please assign a model.", "OK");
			return false;
		}
		if (!HasMesh(model))
		{
			EditorUtility.DisplayDialog("Error", "The assigned model does not contain a MeshFilter or SkinnedMeshRenderer.", "OK");
			return false;
		}
		if (textureSize <= 0)
		{
			EditorUtility.DisplayDialog("Error", "Texture size must be > 0.", "OK");
			return false;
		}
		if (samples <= 0)
		{
			EditorUtility.DisplayDialog("Error", "Samples must be > 0.", "OK");
			return false;
		}
		if (autoRadius && autoRadiusScale <= 0f)
		{
			EditorUtility.DisplayDialog("Error", "Auto Radius Scale must be > 0.", "OK");
			return false;
		}
		if (occlusionFalloffPower <= 0f)
		{
			EditorUtility.DisplayDialog("Error", "Falloff Power must be > 0.", "OK");
			return false;
		}
		return true;
	}

	bool HasMesh(GameObject go)
	{
		return go.GetComponentInChildren<MeshFilter>() != null ||
			   go.GetComponentInChildren<SkinnedMeshRenderer>() != null;
	}

	void GenerateAO()
	{
		// Ensure debug renderer enabled and keep rays visible for debugging
		DebugAORenderer.SetEnabled(true);
		DebugAORenderer.SetAutoExpireRays(false, 60f);
		DebugAORenderer.ClearAll();

		// Load resources (UV unwrap shader and compute shader if present)
		bool computeAvailable = LoadResources();

		// Allocate render targets
		AllocateRTs();

		// Collect and render all meshes under model, upload combined buffers, and create combined collider GO
		GameObject combinedColliderGO = null;
		Mesh combinedMesh = CollectRenderAndUploadAllMeshes(out combinedColliderGO, true);

		if (combinedMesh == null)
		{
			EditorUtility.DisplayDialog("Error", "No valid combined mesh could be created. Aborting.", "OK");
			CleanupAndReturn(combinedColliderGO, combinedMesh);
			DebugAORenderer.ClearAll();
			return;
		}

		// Compute auto radius from combinedWorldBounds if requested
		if (autoRadius)
		{
			float sphereRadius = combinedWorldBounds.extents.magnitude;
			computedAOAutoRadius = sphereRadius * autoRadiusScale;
		}
		else
		{
			computedAOAutoRadius = aoRadius;
		}

		float effectiveRadius = computedAOAutoRadius;

		// Show debug sphere if requested
		if (showDebugSphere)
		{
			DebugAORenderer.SetSphere(combinedWorldBounds.center, effectiveRadius);
		}
		else
		{
			DebugAORenderer.ClearSphere();
		}

		// Clear AO RT to white (opaque)
		RenderTexture.active = aoRT;
		GL.Clear(true, true, new Color(1f, 1f, 1f, 1f));
		RenderTexture.active = null;

		bool gpuSucceeded = false;
		if (mode == BakeMode.GPU)
		{
			if (!computeAvailable)
			{
				EditorUtility.DisplayDialog("Error", "GPU mode selected but compute shader not found or failed to load. Aborting GPU run.", "OK");
			}
			else
			{
				gpuSucceeded = TryRunComputeAOWithDiagnostics(effectiveRadius);
				if (!gpuSucceeded)
					EditorUtility.DisplayDialog("Error", "GPU compute failed. See Console for details.", "OK");
			}
		}
		else if (mode == BakeMode.CPU)
		{
			gpuSucceeded = false; // force CPU
		}
		else // Auto
		{
			if (computeAvailable)
			{
				gpuSucceeded = TryRunComputeAOWithDiagnostics(effectiveRadius);
				if (!gpuSucceeded)
					Debug.LogWarning("[GenerateAO] GPU compute failed; falling back to CPU.");
			}
			else
			{
				Debug.Log("[GenerateAO] Compute shader not available; using CPU fallback.");
			}
		}

		if (!gpuSucceeded)
		{
			// CPU fallback uses combinedColliderGO for raycasts and the effective radius
			GenerateAO_CPU_Fallback(combinedColliderGO, combinedMesh, effectiveRadius);
		}
		else
		{
			bool allWhite = SaveTexture(aoRT, model.name);
			if (allWhite)
			{
				EditorUtility.DisplayDialog("Warning", "Generated AO map is uniformly white (no occlusion detected). Check UV->world buffers, radius, and collider setup.", "OK");
			}
		}

		// Cleanup combined collider and mesh
		if (combinedColliderGO != null)
		{
			var mc = combinedColliderGO.GetComponent<MeshCollider>();
			if (mc != null)
			{
				var shared = mc.sharedMesh;
				mc.sharedMesh = null;
				if (shared != null) Object.DestroyImmediate(shared);
			}
			Object.DestroyImmediate(combinedColliderGO);
		}

		ReleaseBuffers();
		RebuildPreviewTextures();
		uvPreviewStatus = "UV preview updated from the latest bake buffers.";
		SceneView.RepaintAll();
		EditorUtility.DisplayDialog("Done", "AO map generation finished.", "OK");
	}

	void RenderUVPreview()
	{
		if (model == null)
		{
			uvPreviewStatus = "Assign a model before rendering the UV preview.";
			ClearPreviewTextures();
			Repaint();
			return;
		}

		if (!HasMesh(model))
		{
			uvPreviewStatus = "The selected model does not contain a MeshFilter or SkinnedMeshRenderer.";
			ClearPreviewTextures();
			Repaint();
			return;
		}

		LoadResources();
		AllocateRTs();
		GameObject combinedColliderGO = null;
		Mesh combinedMesh = CollectRenderAndUploadAllMeshes(out combinedColliderGO, false);
		if (combinedMesh == null)
		{
			uvPreviewStatus = "UV preview render failed. Check the Console for UV unwrap or shader issues.";
			ReleaseCombinedColliderAndMesh(combinedColliderGO, combinedMesh);
			ReleaseBuffers();
			ClearPreviewTextures();
			Repaint();
			return;
		}

		RebuildPreviewTextures();
		UpdateUVOverlapStatus();
		uvPreviewStatus = $"UV preview updated for {model.name} at {System.DateTime.Now:HH:mm:ss}.";
		ReleaseCombinedColliderAndMesh(combinedColliderGO, combinedMesh);
		ReleaseBuffers();
		UpdateDebugSpherePreview();
		Repaint();
	}

	bool LoadResources()
	{
		// UV unwrap shader
		Shader unwrapShader = Shader.Find("Hidden/Lilithe/UVUnwrap");
		if (unwrapShader == null)
		{
			Debug.LogWarning("[GenerateAO] Could not find shader 'Hidden/Lilithe/UVUnwrap'. UV->world buffers will be empty without it.");
			uvUnwrapMaterial = null;
		}
		else
		{
			uvUnwrapMaterial = new Material(unwrapShader);
		}

		// Find compute shader (best-effort)
		string[] guids = AssetDatabase.FindAssets("AOCompute t:ComputeShader");
		if (guids.Length == 0) guids = AssetDatabase.FindAssets("t:ComputeShader");

		if (guids.Length > 0)
		{
			string path = AssetDatabase.GUIDToAssetPath(guids[0]);
			aoCompute = AssetDatabase.LoadAssetAtPath<ComputeShader>(path);
			if (aoCompute != null)
			{
				Debug.Log($"[GenerateAO] Found compute shader at: {path}");
				AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
				return true;
			}
		}

		aoCompute = null;
		return false;
	}

	void AllocateRTs()
	{
		if (uvPosRT != null) { uvPosRT.Release(); uvPosRT = null; }
		if (uvNormalRT != null) { uvNormalRT.Release(); uvNormalRT = null; }
		if (aoRT != null) { aoRT.Release(); aoRT = null; }

		uvPosRT = new RenderTexture(textureSize, textureSize, 0, RenderTextureFormat.ARGBFloat);
		uvPosRT.Create();

		uvNormalRT = new RenderTexture(textureSize, textureSize, 0, RenderTextureFormat.ARGBFloat);
		uvNormalRT.Create();

		aoRT = new RenderTexture(textureSize, textureSize, 0, RenderTextureFormat.ARGB32);
		aoRT.enableRandomWrite = true;
		aoRT.Create();
	}

	// Render a mesh into a target RT using Graphics.DrawMeshNow with an orthographic projection.
	// passIndex: 0 => world position pass, 1 => world normal pass (the UVUnwrap shader must implement these passes).
	void RenderMeshToRT_WithCamera(Mesh mesh, Matrix4x4 worldMatrix, RenderTexture targetRT, int passIndex)
	{
		if (uvUnwrapMaterial == null || mesh == null || targetRT == null) return;

		// Create a temporary material instance so SetPass doesn't affect shared material
		Material matInstance = new Material(uvUnwrapMaterial);

		RenderTexture prev = RenderTexture.active;
		RenderTexture.active = targetRT;

		GL.PushMatrix();
		GL.LoadOrtho();

		// Use the requested pass explicitly
		matInstance.SetPass(passIndex);
		Graphics.DrawMeshNow(mesh, worldMatrix);

		GL.PopMatrix();
		RenderTexture.active = prev;

		Object.DestroyImmediate(matInstance);
	}

	// Fill uvPosRT/uvNormalRT with a visible gradient and save them to Assets for sanity checking.
	void RunRTSanityFillAndSave()
	{
		if (uvPosRT == null || uvNormalRT == null)
		{
			Debug.LogWarning("[GenerateAO] RTs not allocated for sanity test.");
			return;
		}

		// Create a small texture with a gradient and blit it into uvPosRT and uvNormalRT
		Texture2D fill = new Texture2D(textureSize, textureSize, TextureFormat.RGBAFloat, false);
		for (int y = 0; y < textureSize; y++)
		{
			for (int x = 0; x < textureSize; x++)
			{
				float u = (float)x / (textureSize - 1);
				float v = (float)y / (textureSize - 1);
				// uvPosRT: encode world-like positions as RGB gradient
				Color posCol = new Color(u, v, 0.5f, 1f);
				fill.SetPixel(x, y, posCol);
			}
		}
		fill.Apply();

		// Blit into uvPosRT
		RenderTexture prev = RenderTexture.active;
		Graphics.Blit(fill, uvPosRT);
		// For normals, create a simple normal-like map (pointing up)
		Texture2D ntex = new Texture2D(textureSize, textureSize, TextureFormat.RGBAFloat, false);
		Color ncol = new Color(0.0f, 1.0f, 0.0f, 1f); // up vector encoded
		Color[] ncols = ntex.GetPixels();
		for (int i = 0; i < ncols.Length; i++) ncols[i] = ncol;
		ntex.SetPixels(ncols);
		ntex.Apply();
		Graphics.Blit(ntex, uvNormalRT);
		RenderTexture.active = prev;

		Object.DestroyImmediate(fill);
		Object.DestroyImmediate(ntex);

		SaveDebugRTs();
		EditorUtility.DisplayDialog("RT Sanity Test", "Wrote uvPos_debug.png and uvNormal_debug.png to Assets. Inspect them to confirm RT readback works.", "OK");
	}

	// Collects all MeshFilter and SkinnedMeshRenderer meshes under `model`,
	// renders each into uvPosRT/uvNormalRT using each mesh's world matrix (camera-based),
	// builds a combined world-space vertex array and combined index array,
	// uploads them to vertexBuffer/indexBuffer, computes combinedWorldBounds,
	// and returns a combined Mesh attached to a temporary GameObject with a MeshCollider.
	Mesh CollectRenderAndUploadAllMeshes(out GameObject combinedColliderGO, bool saveDebugTextures)
	{
		combinedColliderGO = null;
		combinedWorldBounds = new Bounds(Vector3.zero, Vector3.zero);
		computedAOAutoRadius = 0f;

		if (model == null) return null;

		var meshFilters = model.GetComponentsInChildren<MeshFilter>(true);
		var skinnedRenderers = model.GetComponentsInChildren<SkinnedMeshRenderer>(true);

		if ((meshFilters == null || meshFilters.Length == 0) && (skinnedRenderers == null || skinnedRenderers.Length == 0))
		{
			Debug.LogError("[GenerateAO] No meshes found under model.");
			return null;
		}

		var bakedMeshes = new List<Mesh>();
		var combinedVerts = new List<Vector3>();
		var combinedIndices = new List<int>();

		bool boundsInitialized = false;

		// Clear UV RTs before rendering
		if (uvUnwrapMaterial != null)
		{
			RenderTexture.active = uvPosRT;
			GL.Clear(true, true, Color.clear);
			RenderTexture.active = uvNormalRT;
			GL.Clear(true, true, Color.clear);
			RenderTexture.active = null;
		}

		// Render MeshFilters
		foreach (var mf in meshFilters)
		{
			if (mf == null || mf.sharedMesh == null) continue;
			Mesh mesh = mf.sharedMesh;
			Matrix4x4 worldMatrix = mf.transform.localToWorldMatrix;

			if (uvUnwrapMaterial != null)
			{
				RenderMeshToRT_WithCamera(mesh, worldMatrix, uvPosRT, 0);
				RenderMeshToRT_WithCamera(mesh, worldMatrix, uvNormalRT, 1);
			}

			int baseVert = combinedVerts.Count;
			var verts = mesh.vertices;
			for (int i = 0; i < verts.Length; i++)
			{
				Vector3 wv = worldMatrix.MultiplyPoint3x4(verts[i]);
				combinedVerts.Add(wv);
				if (!boundsInitialized)
				{
					combinedWorldBounds = new Bounds(wv, Vector3.zero);
					boundsInitialized = true;
				}
				else combinedWorldBounds.Encapsulate(wv);
			}

			var tris = mesh.triangles;
			for (int i = 0; i < tris.Length; i++)
				combinedIndices.Add(tris[i] + baseVert);
		}

		// Render SkinnedMeshRenderers (bake)
		foreach (var smr in skinnedRenderers)
		{
			if (smr == null || smr.sharedMesh == null) continue;
			Mesh baked = new Mesh();
			smr.BakeMesh(baked);
			bakedMeshes.Add(baked);

			Matrix4x4 worldMatrix = smr.transform.localToWorldMatrix;

			if (uvUnwrapMaterial != null)
			{
				RenderMeshToRT_WithCamera(baked, worldMatrix, uvPosRT, 0);
				RenderMeshToRT_WithCamera(baked, worldMatrix, uvNormalRT, 1);
			}

			int baseVert = combinedVerts.Count;
			var verts = baked.vertices;
			for (int i = 0; i < verts.Length; i++)
			{
				Vector3 wv = worldMatrix.MultiplyPoint3x4(verts[i]);
				combinedVerts.Add(wv);
				if (!boundsInitialized)
				{
					combinedWorldBounds = new Bounds(wv, Vector3.zero);
					boundsInitialized = true;
				}
				else combinedWorldBounds.Encapsulate(wv);
			}

			var tris = baked.triangles;
			for (int i = 0; i < tris.Length; i++)
				combinedIndices.Add(tris[i] + baseVert);
		}

		// After rendering UV->world, do a quick sanity check: sample a few pixels from uvPosRT to ensure it's not all zero.
		if (uvUnwrapMaterial != null)
		{
			bool uvPosHasData = false;
			try
			{
				Texture2D check = new Texture2D(4, 4, TextureFormat.RGBAFloat, false);
				RenderTexture.active = uvPosRT;
				check.ReadPixels(new Rect(0, 0, Mathf.Min(4, uvPosRT.width), Mathf.Min(4, uvPosRT.height)), 0, 0);
				check.Apply();
				RenderTexture.active = null;
				var pixels = check.GetPixels();
				for (int i = 0; i < pixels.Length; i++)
				{
					if (pixels[i].r != 0f || pixels[i].g != 0f || pixels[i].b != 0f)
					{
						uvPosHasData = true;
						break;
					}
				}
				Object.DestroyImmediate(check);
			}
			catch
			{
				// ignore readback errors; we'll still proceed
			}

			if (!uvPosHasData)
			{
				Debug.LogWarning("[GenerateAO] UV->world position render target appears empty (all zeros). Saved debug RTs for inspection.");
			}
		}

		// Upload combined buffers
		ReleaseBuffers();

		if (combinedVerts.Count == 0 || combinedIndices.Count == 0)
		{
			Debug.LogError("[GenerateAO] Combined mesh is empty.");
			foreach (var bm in bakedMeshes) Object.DestroyImmediate(bm);
			return null;
		}

		vertexBuffer = new ComputeBuffer(combinedVerts.Count, sizeof(float) * 3);
		vertexBuffer.SetData(combinedVerts.ToArray());

		indexBuffer = new ComputeBuffer(combinedIndices.Count, sizeof(int));
		indexBuffer.SetData(combinedIndices.ToArray());

		// Create combined mesh for CPU fallback collider
		Mesh combinedMesh = new Mesh();
		combinedMesh.indexFormat = (combinedVerts.Count > 65535) ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16;
		combinedMesh.SetVertices(combinedVerts);
		combinedMesh.SetTriangles(combinedIndices, 0);
		combinedMesh.RecalculateBounds();
		combinedMesh.RecalculateNormals();

		combinedColliderGO = new GameObject("AO_TempCombinedCollider");
		combinedColliderGO.hideFlags = HideFlags.HideAndDontSave;
		combinedColliderGO.transform.position = Vector3.zero;
		combinedColliderGO.transform.rotation = Quaternion.identity;
		var mc = combinedColliderGO.AddComponent<MeshCollider>();
		mc.sharedMesh = combinedMesh;
		mc.convex = false;

		// Compute auto radius from combinedWorldBounds
		if (combinedWorldBounds.size != Vector3.zero)
		{
			float sphereRadius = combinedWorldBounds.extents.magnitude;
			computedAOAutoRadius = sphereRadius * autoRadiusScale;
		}
		else
		{
			computedAOAutoRadius = 0f;
		}

		foreach (var bm in bakedMeshes) Object.DestroyImmediate(bm);

		// after rendering all meshes into uvPosRT/uvNormalRT
		if (saveDebugTextures)
		{
			SaveDebugRTs();
		}
		int nonEmpty = CountNonEmptyUVPixels(16);
		Debug.Log($"[GenerateAO] uvPosRT non-empty sample count = {nonEmpty}");
		if (nonEmpty == 0)
		{
			EditorUtility.DisplayDialog("UV Render Empty", "uvPosRT appears empty (all zeros). Saved uvPos_debug.png and uvNormal_debug.png to Assets. Check shader, UVs, or render path.", "OK");
			// abort early to avoid wasting time
			return null;
		}

		return combinedMesh;
	}

	void UpdateUVOverlapStatus()
	{
		uvOverlapDetected = false;
		uvOverlapRatio = 0f;

		if (model == null)
		{
			uvOverlapStatus = "UV overlap not checked.";
			return;
		}

		if (!HasMesh(model))
		{
			uvOverlapStatus = "The selected model does not contain any meshes to analyze.";
			return;
		}

		if (!TryAnalyzeUVOverlap(model, kUVOverlapCheckResolution, out int overlapPixels, out int coveredPixels, out int missingUVMeshes))
		{
			uvOverlapStatus = "Unable to analyze UV overlap for the selected model.";
			return;
		}

		uvOverlapDetected = overlapPixels > 0;
		uvOverlapRatio = coveredPixels > 0 ? (float)overlapPixels / coveredPixels : 0f;
		uvOverlapStatus = uvOverlapDetected
			? $"UV overlap detected: {uvOverlapRatio:P1} of sampled UV coverage overlaps at {kUVOverlapCheckResolution}x{kUVOverlapCheckResolution}."
			: $"No UV overlap detected in sampled UV coverage at {kUVOverlapCheckResolution}x{kUVOverlapCheckResolution}.";

		if (missingUVMeshes > 0)
		{
			uvOverlapStatus += $" {missingUVMeshes} mesh(es) had no usable UV0 data.";
		}
	}

	bool TryAnalyzeUVOverlap(GameObject root, int resolution, out int overlapPixels, out int coveredPixels, out int missingUVMeshes)
	{
		overlapPixels = 0;
		coveredPixels = 0;
		missingUVMeshes = 0;
		if (root == null || resolution <= 0) return false;

		int[] occupancy = new int[resolution * resolution];
		bool anyTriangles = false;

		var meshFilters = root.GetComponentsInChildren<MeshFilter>(true);
		for (int i = 0; i < meshFilters.Length; i++)
		{
			var mesh = meshFilters[i] != null ? meshFilters[i].sharedMesh : null;
			if (!RasterizeMeshUVOccupancy(mesh, occupancy, resolution, ref coveredPixels, ref overlapPixels))
			{
				if (mesh != null) missingUVMeshes++;
			}
			else
			{
				anyTriangles = true;
			}
		}

		var skinnedMeshes = root.GetComponentsInChildren<SkinnedMeshRenderer>(true);
		for (int i = 0; i < skinnedMeshes.Length; i++)
		{
			var mesh = skinnedMeshes[i] != null ? skinnedMeshes[i].sharedMesh : null;
			if (!RasterizeMeshUVOccupancy(mesh, occupancy, resolution, ref coveredPixels, ref overlapPixels))
			{
				if (mesh != null) missingUVMeshes++;
			}
			else
			{
				anyTriangles = true;
			}
		}

		return anyTriangles || missingUVMeshes > 0;
	}

	bool RasterizeMeshUVOccupancy(Mesh mesh, int[] occupancy, int size, ref int coveredPixels, ref int overlapPixels)
	{
		if (mesh == null) return false;
		Vector2[] uvs = mesh.uv;
		int[] triangles = mesh.triangles;
		if (uvs == null || uvs.Length == 0 || triangles == null || triangles.Length < 3) return false;

		bool anyTriangles = false;
		for (int i = 0; i < triangles.Length; i += 3)
		{
			int i0 = triangles[i];
			int i1 = triangles[i + 1];
			int i2 = triangles[i + 2];
			if (i0 < 0 || i0 >= uvs.Length || i1 < 0 || i1 >= uvs.Length || i2 < 0 || i2 >= uvs.Length) continue;

			Vector2 p0 = UVToPixel(uvs[i0], size);
			Vector2 p1 = UVToPixel(uvs[i1], size);
			Vector2 p2 = UVToPixel(uvs[i2], size);
			FillTriangleOccupancy(occupancy, size, p0, p1, p2, ref coveredPixels, ref overlapPixels);
			anyTriangles = true;
		}

		return anyTriangles;
	}

	void FillTriangleOccupancy(int[] occupancy, int size, Vector2 p0, Vector2 p1, Vector2 p2, ref int coveredPixels, ref int overlapPixels)
	{
		float area = EdgeFunction(p0, p1, p2);
		if (Mathf.Abs(area) < 1e-5f) return;

		float edgeEpsilon = Mathf.Abs(area) * 0.0025f;
		int minX = Mathf.Clamp(Mathf.FloorToInt(Mathf.Min(p0.x, Mathf.Min(p1.x, p2.x))), 0, size - 1);
		int maxX = Mathf.Clamp(Mathf.CeilToInt(Mathf.Max(p0.x, Mathf.Max(p1.x, p2.x))), 0, size - 1);
		int minY = Mathf.Clamp(Mathf.FloorToInt(Mathf.Min(p0.y, Mathf.Min(p1.y, p2.y))), 0, size - 1);
		int maxY = Mathf.Clamp(Mathf.CeilToInt(Mathf.Max(p0.y, Mathf.Max(p1.y, p2.y))), 0, size - 1);

		for (int y = minY; y <= maxY; y++)
		{
			for (int x = minX; x <= maxX; x++)
			{
				Vector2 sample = new Vector2(x + 0.5f, y + 0.5f);
				float w0 = EdgeFunction(p1, p2, sample);
				float w1 = EdgeFunction(p2, p0, sample);
				float w2 = EdgeFunction(p0, p1, sample);
				bool sameSign = (w0 >= 0f && w1 >= 0f && w2 >= 0f) || (w0 <= 0f && w1 <= 0f && w2 <= 0f);
				bool strictlyInside = Mathf.Abs(w0) > edgeEpsilon && Mathf.Abs(w1) > edgeEpsilon && Mathf.Abs(w2) > edgeEpsilon;
				if (!sameSign || !strictlyInside) continue;

				int index = (y * size) + x;
				if (occupancy[index] == 0)
				{
					coveredPixels++;
				}
				else if (occupancy[index] == 1)
				{
					overlapPixels++;
				}
				occupancy[index]++;
			}
		}
	}

	void BakeNewUVMapForModel()
	{
		if (model == null || !HasMesh(model))
		{
			EditorUtility.DisplayDialog("No Meshes", "Assign a model with meshes before baking a new UV map.", "OK");
			return;
		}

		if (!EditorUtility.DisplayDialog("Bake New UV Map", "This will create new mesh assets with Unity-generated non-overlapping UV0s and assign them to the selected model's renderers. Continue?", "Bake UV Map", "Cancel"))
		{
			return;
		}

		EnsureFolderExists(kGeneratedAOUVFolder);
		var remappedMeshes = new Dictionary<Mesh, Mesh>();
		int assignedRenderers = 0;
		int bakedMeshes = 0;

		var meshFilters = model.GetComponentsInChildren<MeshFilter>(true);
		for (int i = 0; i < meshFilters.Length; i++)
		{
			var mf = meshFilters[i];
			if (mf == null || mf.sharedMesh == null) continue;

			Mesh rebakedMesh = GetOrCreateRebakedUVMesh(mf.sharedMesh, remappedMeshes, ref bakedMeshes);
			if (rebakedMesh == null) continue;

			Undo.RecordObject(mf, "Assign rebaked AO UV mesh");
			mf.sharedMesh = rebakedMesh;
			EditorUtility.SetDirty(mf);
			assignedRenderers++;
		}

		var skinnedMeshes = model.GetComponentsInChildren<SkinnedMeshRenderer>(true);
		for (int i = 0; i < skinnedMeshes.Length; i++)
		{
			var smr = skinnedMeshes[i];
			if (smr == null || smr.sharedMesh == null) continue;

			Mesh rebakedMesh = GetOrCreateRebakedUVMesh(smr.sharedMesh, remappedMeshes, ref bakedMeshes);
			if (rebakedMesh == null) continue;

			Undo.RecordObject(smr, "Assign rebaked AO UV mesh");
			smr.sharedMesh = rebakedMesh;
			EditorUtility.SetDirty(smr);
			assignedRenderers++;
		}

		AssetDatabase.SaveAssets();
		AssetDatabase.Refresh();
		UpdateUVOverlapStatus();
		RebuildPreviewTextures();
		Repaint();

		EditorUtility.DisplayDialog("AO UV Bake Complete", $"Created {bakedMeshes} rebaked mesh asset(s) and assigned them to {assignedRenderers} renderer(s).", "OK");
	}

	Mesh GetOrCreateRebakedUVMesh(Mesh sourceMesh, Dictionary<Mesh, Mesh> remappedMeshes, ref int bakedMeshes)
	{
		if (sourceMesh == null) return null;
		if (remappedMeshes.TryGetValue(sourceMesh, out Mesh existing)) return existing;

		Mesh clonedMesh = Object.Instantiate(sourceMesh);
		clonedMesh.name = sourceMesh.name + "_AOUV";

		UnwrapParam unwrapParameters = new UnwrapParam();
		UnwrapParam.SetDefaults(out unwrapParameters);
		unwrapParameters.hardAngle = 88f;
		unwrapParameters.angleError = 8f;
		unwrapParameters.areaError = 15f;
		unwrapParameters.packMargin = 2f;

		try
		{
			Unwrapping.GenerateSecondaryUVSet(clonedMesh, unwrapParameters);
		}
		catch (System.Exception ex)
		{
			Debug.LogWarning($"[GenerateAO] Failed to generate UVs for mesh '{sourceMesh.name}': {ex.Message}");
			Object.DestroyImmediate(clonedMesh);
			remappedMeshes[sourceMesh] = null;
			return null;
		}

		if (clonedMesh.uv2 == null || clonedMesh.uv2.Length == 0)
		{
			Debug.LogWarning($"[GenerateAO] Unity did not generate UV2 data for mesh '{sourceMesh.name}'.");
			Object.DestroyImmediate(clonedMesh);
			remappedMeshes[sourceMesh] = null;
			return null;
		}

		clonedMesh.uv = clonedMesh.uv2;
		clonedMesh.RecalculateBounds();

		string assetPath = AssetDatabase.GenerateUniqueAssetPath($"{kGeneratedAOUVFolder}/{SanitizeAssetName(sourceMesh.name)}_AOUV.asset");
		AssetDatabase.CreateAsset(clonedMesh, assetPath);
		remappedMeshes[sourceMesh] = clonedMesh;
		bakedMeshes++;
		return clonedMesh;
	}

	void EnsureFolderExists(string assetFolder)
	{
		if (AssetDatabase.IsValidFolder(assetFolder)) return;

		string[] parts = assetFolder.Split('/');
		string current = parts[0];
		for (int i = 1; i < parts.Length; i++)
		{
			string next = current + "/" + parts[i];
			if (!AssetDatabase.IsValidFolder(next))
			{
				AssetDatabase.CreateFolder(current, parts[i]);
			}
			current = next;
		}
	}

	string SanitizeAssetName(string name)
	{
		if (string.IsNullOrEmpty(name)) return "Mesh";
		char[] invalid = Path.GetInvalidFileNameChars();
		for (int i = 0; i < invalid.Length; i++)
		{
			name = name.Replace(invalid[i], '_');
		}
		return name;
	}

	void DrawUVPreviewPanel()
	{
		EditorGUILayout.BeginHorizontal();
		DrawPreviewTile("UV Layout", uvCoveragePreviewTexture, "Mesh UV islands with filled coverage and highlighted seams.");
		DrawPreviewTile("Normal Preview", uvNormalPreviewTexture, "Surface directions remapped into RGB.");
		DrawPreviewTile("AO Result", aoRT, "Shown after a successful bake or compute pass.");
		EditorGUILayout.EndHorizontal();
	}

	void DrawPreviewTile(string label, Texture texture, string description)
	{
		EditorGUILayout.BeginVertical(GUILayout.Width(170f));
		EditorGUILayout.LabelField(label, EditorStyles.boldLabel);
		Rect previewRect = GUILayoutUtility.GetRect(160f, 160f, GUILayout.ExpandWidth(false));
		EditorGUI.DrawRect(previewRect, new Color(0.12f, 0.12f, 0.12f, 1f));
		if (texture != null)
		{
			EditorGUI.DrawPreviewTexture(previewRect, texture, null, ScaleMode.ScaleToFit);
		}
		else
		{
			EditorGUI.LabelField(previewRect, "No preview", EditorStyles.centeredGreyMiniLabel);
		}
		EditorGUILayout.LabelField(description, EditorStyles.wordWrappedMiniLabel, GUILayout.Width(160f));
		EditorGUILayout.EndVertical();
	}

	void RebuildPreviewTextures()
	{
		ReplacePreviewTexture(ref uvCoveragePreviewTexture, BuildUVLayoutPreviewTexture(model, textureSize));
		ReplacePreviewTexture(ref uvNormalPreviewTexture, BuildNormalPreviewTexture(uvNormalRT));
	}

	void ReplacePreviewTexture(ref Texture2D target, Texture2D replacement)
	{
		if (target != null)
		{
			Object.DestroyImmediate(target);
		}
		target = replacement;
	}

	Texture2D BuildUVLayoutPreviewTexture(GameObject root, int size)
	{
		if (root == null || size <= 0) return null;

		Texture2D preview = new Texture2D(size, size, TextureFormat.RGBA32, false);
		Color background = new Color(0.08f, 0.08f, 0.08f, 1f);
		Color fill = new Color(0.58f, 0.64f, 0.72f, 1f);
		Color edge = new Color(1f, 1f, 1f, 1f);
		Color[] pixels = new Color[size * size];
		for (int i = 0; i < pixels.Length; i++)
		{
			pixels[i] = background;
		}

		RasterizeUVLayout(root, pixels, size, fill, edge);
		preview.SetPixels(pixels);
		preview.Apply();
		return preview;
	}

	void RasterizeUVLayout(GameObject root, Color[] pixels, int size, Color fillColor, Color edgeColor)
	{
		var meshFilters = root.GetComponentsInChildren<MeshFilter>(true);
		for (int i = 0; i < meshFilters.Length; i++)
		{
			var mesh = meshFilters[i] != null ? meshFilters[i].sharedMesh : null;
			RasterizeMeshUVs(mesh, pixels, size, fillColor, edgeColor);
		}

		var skinnedMeshes = root.GetComponentsInChildren<SkinnedMeshRenderer>(true);
		for (int i = 0; i < skinnedMeshes.Length; i++)
		{
			var mesh = skinnedMeshes[i] != null ? skinnedMeshes[i].sharedMesh : null;
			RasterizeMeshUVs(mesh, pixels, size, fillColor, edgeColor);
		}
	}

	void RasterizeMeshUVs(Mesh mesh, Color[] pixels, int size, Color fillColor, Color edgeColor)
	{
		if (mesh == null) return;
		Vector2[] uvs = mesh.uv;
		int[] triangles = mesh.triangles;
		if (uvs == null || uvs.Length == 0 || triangles == null || triangles.Length < 3) return;

		for (int i = 0; i < triangles.Length; i += 3)
		{
			int i0 = triangles[i];
			int i1 = triangles[i + 1];
			int i2 = triangles[i + 2];
			if (i0 < 0 || i0 >= uvs.Length || i1 < 0 || i1 >= uvs.Length || i2 < 0 || i2 >= uvs.Length) continue;

			Vector2 p0 = UVToPixel(uvs[i0], size);
			Vector2 p1 = UVToPixel(uvs[i1], size);
			Vector2 p2 = UVToPixel(uvs[i2], size);
			FillTriangle(pixels, size, p0, p1, p2, fillColor);
			DrawLine(pixels, size, p0, p1, edgeColor);
			DrawLine(pixels, size, p1, p2, edgeColor);
			DrawLine(pixels, size, p2, p0, edgeColor);
		}
	}

	Vector2 UVToPixel(Vector2 uv, int size)
	{
		float x = Mathf.Clamp01(uv.x) * (size - 1);
		float y = (1f - Mathf.Clamp01(uv.y)) * (size - 1);
		return new Vector2(x, y);
	}

	void FillTriangle(Color[] pixels, int size, Vector2 p0, Vector2 p1, Vector2 p2, Color fillColor)
	{
		float area = EdgeFunction(p0, p1, p2);
		if (Mathf.Abs(area) < 1e-5f) return;

		int minX = Mathf.Clamp(Mathf.FloorToInt(Mathf.Min(p0.x, Mathf.Min(p1.x, p2.x))), 0, size - 1);
		int maxX = Mathf.Clamp(Mathf.CeilToInt(Mathf.Max(p0.x, Mathf.Max(p1.x, p2.x))), 0, size - 1);
		int minY = Mathf.Clamp(Mathf.FloorToInt(Mathf.Min(p0.y, Mathf.Min(p1.y, p2.y))), 0, size - 1);
		int maxY = Mathf.Clamp(Mathf.CeilToInt(Mathf.Max(p0.y, Mathf.Max(p1.y, p2.y))), 0, size - 1);

		for (int y = minY; y <= maxY; y++)
		{
			for (int x = minX; x <= maxX; x++)
			{
				Vector2 sample = new Vector2(x + 0.5f, y + 0.5f);
				float w0 = EdgeFunction(p1, p2, sample);
				float w1 = EdgeFunction(p2, p0, sample);
				float w2 = EdgeFunction(p0, p1, sample);
				if ((w0 >= 0f && w1 >= 0f && w2 >= 0f) || (w0 <= 0f && w1 <= 0f && w2 <= 0f))
				{
					pixels[(y * size) + x] = fillColor;
				}
			}
		}
	}

	float EdgeFunction(Vector2 a, Vector2 b, Vector2 c)
	{
		return ((c.x - a.x) * (b.y - a.y)) - ((c.y - a.y) * (b.x - a.x));
	}

	void DrawLine(Color[] pixels, int size, Vector2 start, Vector2 end, Color color)
	{
		int x0 = Mathf.RoundToInt(start.x);
		int y0 = Mathf.RoundToInt(start.y);
		int x1 = Mathf.RoundToInt(end.x);
		int y1 = Mathf.RoundToInt(end.y);

		int dx = Mathf.Abs(x1 - x0);
		int dy = Mathf.Abs(y1 - y0);
		int sx = x0 < x1 ? 1 : -1;
		int sy = y0 < y1 ? 1 : -1;
		int err = dx - dy;

		while (true)
		{
			if (x0 >= 0 && x0 < size && y0 >= 0 && y0 < size)
			{
				pixels[(y0 * size) + x0] = color;
			}

			if (x0 == x1 && y0 == y1) break;
			int e2 = err * 2;
			if (e2 > -dy)
			{
				err -= dy;
				x0 += sx;
			}
			if (e2 < dx)
			{
				err += dx;
				y0 += sy;
			}
		}
	}

	Texture2D BuildNormalPreviewTexture(RenderTexture source)
	{
		if (source == null) return null;
		Texture2D pixels = ReadRenderTexture(source, TextureFormat.RGBAFloat);
		if (pixels == null) return null;

		Color[] input = pixels.GetPixels();
		Color[] output = new Color[input.Length];
		for (int i = 0; i < input.Length; i++)
		{
			Color c = input[i];
			bool occupied = Mathf.Abs(c.r) > 1e-6f || Mathf.Abs(c.g) > 1e-6f || Mathf.Abs(c.b) > 1e-6f;
			output[i] = occupied
				? new Color((c.r * 0.5f) + 0.5f, (c.g * 0.5f) + 0.5f, (c.b * 0.5f) + 0.5f, 1f)
				: new Color(0.08f, 0.08f, 0.08f, 1f);
		}

		Texture2D preview = new Texture2D(pixels.width, pixels.height, TextureFormat.RGBA32, false);
		preview.SetPixels(output);
		preview.Apply();
		Object.DestroyImmediate(pixels);
		return preview;
	}

	Texture2D ReadRenderTexture(RenderTexture source, TextureFormat format)
	{
		if (source == null) return null;
		RenderTexture previous = RenderTexture.active;
		Texture2D texture = new Texture2D(source.width, source.height, format, false);
		RenderTexture.active = source;
		texture.ReadPixels(new Rect(0, 0, source.width, source.height), 0, 0);
		texture.Apply();
		RenderTexture.active = previous;
		return texture;
	}

	void ClearPreviewTextures()
	{
		ReplacePreviewTexture(ref uvCoveragePreviewTexture, null);
		ReplacePreviewTexture(ref uvNormalPreviewTexture, null);
	}

	// Try to run compute shader; pass effectiveRadius so compute shader can use it
	bool TryRunComputeAOWithDiagnostics(float effectiveRadius)
	{
		if (aoCompute == null)
		{
			Debug.LogWarning("[GenerateAO] aoCompute is null.");
			return false;
		}

		int kernel = -1;
		try
		{
			kernel = aoCompute.FindKernel("CSMain");
		}
		catch (System.Exception ex)
		{
			Debug.LogWarning($"[GenerateAO] Exception finding kernel 'CSMain': {ex.Message}");
			return false;
		}

		if (kernel < 0)
		{
			Debug.LogWarning("[GenerateAO] Kernel 'CSMain' not found in compute shader.");
			return false;
		}

		aoCompute.SetTexture(kernel, "_UVPosTex", uvPosRT);
		aoCompute.SetTexture(kernel, "_UVNormalTex", uvNormalRT);
		aoCompute.SetTexture(kernel, "_Result", aoRT);

		if (vertexBuffer != null) aoCompute.SetBuffer(kernel, "_Vertices", vertexBuffer);
		if (indexBuffer != null) aoCompute.SetBuffer(kernel, "_Indices", indexBuffer);

		aoCompute.SetInt("_VertexCount", vertexBuffer != null ? vertexBuffer.count : 0);
		aoCompute.SetInt("_IndexCount", indexBuffer != null ? indexBuffer.count : 0);
		aoCompute.SetInt("_Samples", samples);
		aoCompute.SetFloat("_Radius", effectiveRadius);
		aoCompute.SetFloat("_OcclusionFalloffPower", occlusionFalloffPower);
		aoCompute.SetInt("_TexSize", textureSize);

		int tgX = Mathf.CeilToInt(textureSize / 8.0f);
		int tgY = Mathf.CeilToInt(textureSize / 8.0f);

		try
		{
			aoCompute.Dispatch(kernel, tgX, tgY, 1);
			return true;
		}
		catch (System.Exception ex)
		{
			Debug.LogWarning($"[GenerateAO] Compute dispatch failed: {ex.Message}");
			// Reimport compute shader to surface compile errors
			try
			{
				string[] guids = AssetDatabase.FindAssets("t:ComputeShader");
				if (guids.Length > 0)
				{
					string path = AssetDatabase.GUIDToAssetPath(guids[0]);
					AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
					Debug.Log($"[GenerateAO] Reimported compute shader at: {path}");
				}
			}
			catch { }
			return false;
		}
	}

	// CPU fallback: reads uvPosRT/uvNormalRT and performs hemisphere raycasts using the combined MeshCollider
	void GenerateAO_CPU_Fallback(GameObject combinedColliderGO, Mesh combinedMesh, float effectiveRadius)
	{
		// Read back UV pos and normal RTs into CPU textures
		Texture2D posTex = new Texture2D(textureSize, textureSize, TextureFormat.RGBAFloat, false);
		RenderTexture.active = uvPosRT;
		posTex.ReadPixels(new Rect(0, 0, textureSize, textureSize), 0, 0);
		posTex.Apply();

		Texture2D normalTex = new Texture2D(textureSize, textureSize, TextureFormat.RGBAFloat, false);
		RenderTexture.active = uvNormalRT;
		normalTex.ReadPixels(new Rect(0, 0, textureSize, textureSize), 0, 0);
		normalTex.Apply();

		RenderTexture.active = null;

		Texture2D aoTex = new Texture2D(textureSize, textureSize, TextureFormat.RGBA32, false);

		// Ensure MeshCollider exists on combinedColliderGO
		MeshCollider tempCollider = null;
		bool addedCollider = false;
		if (combinedColliderGO != null)
		{
			tempCollider = combinedColliderGO.GetComponent<MeshCollider>();
			if (tempCollider == null)
			{
				tempCollider = combinedColliderGO.AddComponent<MeshCollider>();
				tempCollider.sharedMesh = combinedMesh;
				tempCollider.convex = false;
				addedCollider = true;
			}
			else
			{
				tempCollider.sharedMesh = combinedMesh;
			}
			combinedColliderGO.SetActive(true);
		}
		else
		{
			EditorUtility.DisplayDialog("Error", "No combined collider available for CPU raycasts.", "OK");
			Object.DestroyImmediate(posTex);
			Object.DestroyImmediate(normalTex);
			return;
		}

		float epsilon = 0.001f;
		System.Random rng = new System.Random();
		int totalPixels = textureSize * textureSize;
		int processed = 0;

		int sqrtS = Mathf.CeilToInt(Mathf.Sqrt(samples));
		int drawCounter = 0;
		int nonEmptyTexels = 0;

		for (int y = 0; y < textureSize; y++)
		{
			for (int x = 0; x < textureSize; x++)
			{
				processed++;
				if (processed % 1024 == 0)
				{
					if (EditorUtility.DisplayCancelableProgressBar("Baking AO (CPU)", $"Processing texel {processed}/{totalPixels}", (float)processed / totalPixels))
					{
						EditorUtility.ClearProgressBar();
						if (addedCollider && tempCollider != null) DestroyImmediate(tempCollider);
						Object.DestroyImmediate(posTex);
						Object.DestroyImmediate(normalTex);
						DebugAORenderer.ClearAll();
						return;
					}
				}

				Color posC = posTex.GetPixel(x, y);
				Color nC = normalTex.GetPixel(x, y);

				if (posC.a < 0.5f)
				{
					aoTex.SetPixel(x, y, Color.white);
					continue;
				}

				nonEmptyTexels++;

				Vector3 worldPos = new Vector3(posC.r, posC.g, posC.b);
				Vector3 normal = new Vector3((nC.r * 2f) - 1f, (nC.g * 2f) - 1f, (nC.b * 2f) - 1f);
				if (normal.sqrMagnitude < 1e-6f) normal = Vector3.up;
				normal.Normalize();

				int occluded = 0;
				float occlusionSum = 0f;
				int sIndex = 0;
				for (int sy = 0; sy < sqrtS; sy++)
				{
					for (int sx = 0; sx < sqrtS; sx++)
					{
						if (sIndex++ >= samples) break;

						float u1 = ((sx + (float)rng.NextDouble()) / sqrtS);
						float u2 = ((sy + (float)rng.NextDouble()) / sqrtS);

						float r = Mathf.Sqrt(u1);
						float theta = 2f * Mathf.PI * u2;
						float xS = r * Mathf.Cos(theta);
						float yS = r * Mathf.Sin(theta);
						float zS = Mathf.Sqrt(Mathf.Max(0f, 1f - u1));

						Vector3 tangent = Vector3.Cross(normal, Mathf.Abs(normal.x) > 0.99f ? Vector3.up : Vector3.right).normalized;
						Vector3 bitangent = Vector3.Cross(normal, tangent);

						Vector3 sampleDir = (tangent * xS) + (bitangent * yS) + (normal * zS);
						sampleDir.Normalize();

						Vector3 origin = worldPos + normal * epsilon;

						bool hit = tempCollider.Raycast(new Ray(origin, sampleDir), out RaycastHit hitInfo, effectiveRadius);
						if (hit)
						{
							if (hitInfo.distance > 0.0005f)
							{
								occluded++;
								occlusionSum += EvaluateDistanceOcclusion(hitInfo.distance, effectiveRadius, epsilon);
							}
						}

						// Debug drawing: draw only a subset controlled by debugRayStride and showDebugRays
						if (showDebugRays && ((drawCounter++ % debugRayStride) == 0))
						{
							float drawLen = hit ? hitInfo.distance : effectiveRadius;
							DebugAORenderer.AddRay(origin, sampleDir, drawLen, hit);
						}
					}
				}

				float occ = occlusionSum / samples;
				float aoValue = 1f - occ;
				aoTex.SetPixel(x, y, new Color(aoValue, aoValue, aoValue, 1f));
			}
		}

		EditorUtility.ClearProgressBar();

		if (addedCollider && tempCollider != null)
			DestroyImmediate(tempCollider);

		aoTex.Apply();

		Debug.Log($"[GenerateAO] CPU finished. Non-empty texels: {nonEmptyTexels} / {totalPixels}");

		// Check for all-white result and warn
		if (IsTextureAllWhite(aoTex))
		{
			EditorUtility.DisplayDialog("Warning", "Generated AO map is uniformly white (no occlusion detected). Check UV->world buffers, radius, and collider setup.", "OK");
		}

		// Save AO texture (force alpha = 1)
		string path = EditorUtility.SaveFilePanel("Save AO Texture", "", model.name + "_AO." + (saveAsPNG ? "png" : "jpg"), saveAsPNG ? "png" : "jpg");
		if (!string.IsNullOrEmpty(path))
		{
			Color[] cols = aoTex.GetPixels();
			for (int i = 0; i < cols.Length; i++) cols[i].a = 1f;
			aoTex.SetPixels(cols);
			aoTex.Apply();

			byte[] bytes = saveAsPNG ? aoTex.EncodeToPNG() : aoTex.EncodeToJPG();
			File.WriteAllBytes(path, bytes);
			AssetDatabase.Refresh();
		}

		Object.DestroyImmediate(posTex);
		Object.DestroyImmediate(normalTex);
		Object.DestroyImmediate(aoTex);
	}

	void ReleaseBuffers()
	{
		if (vertexBuffer != null) { vertexBuffer.Release(); vertexBuffer = null; }
		if (indexBuffer != null) { indexBuffer.Release(); indexBuffer = null; }
	}

	float EvaluateDistanceOcclusion(float hitDistance, float radius, float epsilon)
	{
		if (radius <= epsilon) return 0f;
		float normalizedDistance = Mathf.Clamp01((hitDistance - epsilon) / Mathf.Max(1e-6f, radius - epsilon));
		return Mathf.Pow(1f - normalizedDistance, occlusionFalloffPower);
	}

	void ReleaseCombinedColliderAndMesh(GameObject combinedColliderGO, Mesh combinedMesh)
	{
		if (combinedColliderGO != null)
		{
			var mc = combinedColliderGO.GetComponent<MeshCollider>();
			if (mc != null)
			{
				var shared = mc.sharedMesh;
				mc.sharedMesh = null;
				if (shared != null) Object.DestroyImmediate(shared);
			}
			Object.DestroyImmediate(combinedColliderGO);
		}
		else if (combinedMesh != null)
		{
			Object.DestroyImmediate(combinedMesh);
		}
	}

	// Save uvPosRT and uvNormalRT to Assets for inspection
	void SaveDebugRTs()
	{
		try
		{
			if (uvPosRT != null)
			{
				Texture2D p = new Texture2D(uvPosRT.width, uvPosRT.height, TextureFormat.RGBAFloat, false);
				RenderTexture.active = uvPosRT;
				p.ReadPixels(new Rect(0, 0, uvPosRT.width, uvPosRT.height), 0, 0);
				p.Apply();
				File.WriteAllBytes(Path.Combine(Application.dataPath, "uvPos_debug.png"), p.EncodeToPNG());
				Object.DestroyImmediate(p);
				Debug.Log("[GenerateAO] Saved uvPos_debug.png to Assets.");
			}

			if (uvNormalRT != null)
			{
				Texture2D n = new Texture2D(uvNormalRT.width, uvNormalRT.height, TextureFormat.RGBAFloat, false);
				RenderTexture.active = uvNormalRT;
				n.ReadPixels(new Rect(0, 0, uvNormalRT.width, uvNormalRT.height), 0, 0);
				n.Apply();
				File.WriteAllBytes(Path.Combine(Application.dataPath, "uvNormal_debug.png"), n.EncodeToPNG());
				Object.DestroyImmediate(n);
				Debug.Log("[GenerateAO] Saved uvNormal_debug.png to Assets.");
			}
		}
		catch (System.Exception ex)
		{
			Debug.LogWarning($"[GenerateAO] Failed to save debug RTs: {ex.Message}");
		}
		finally
		{
			RenderTexture.active = null;
			AssetDatabase.Refresh();
		}
	}

	// Count non-empty pixels in uvPosRT (fast sample of grid)
	int CountNonEmptyUVPixels(int sampleStride = 8)
	{
		if (uvPosRT == null) return 0;
		int count = 0;
		try
		{
			Texture2D t = new Texture2D(uvPosRT.width, uvPosRT.height, TextureFormat.RGBAFloat, false);
			RenderTexture.active = uvPosRT;
			t.ReadPixels(new Rect(0, 0, uvPosRT.width, uvPosRT.height), 0, 0);
			t.Apply();
			RenderTexture.active = null;
			Color[] cols = t.GetPixels();
			for (int i = 0; i < cols.Length; i += sampleStride)
			{
				var c = cols[i];
				if (c.a > 0.5f) count++;
			}
			Object.DestroyImmediate(t);
		}
		catch { RenderTexture.active = null; }
		return count;
	}

	// Save RenderTexture to disk, force alpha=1. Returns true if the texture is uniformly white (no AO).
	bool SaveTexture(RenderTexture rt, string name)
	{
		Texture2D tex = new Texture2D(rt.width, rt.height, TextureFormat.RGBA32, false);
		RenderTexture.active = rt;
		tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
		RenderTexture.active = null;

		// Check if all-white before forcing alpha
		bool allWhite = IsTextureAllWhite(tex);

		// Force alpha = 1 to avoid transparent PNGs
		Color[] cols = tex.GetPixels();
		for (int i = 0; i < cols.Length; i++)
		{
			cols[i].a = 1f;
		}
		tex.SetPixels(cols);
		tex.Apply();

		string path = EditorUtility.SaveFilePanel("Save AO Texture", "", name + "_AO." + (saveAsPNG ? "png" : "jpg"), saveAsPNG ? "png" : "jpg");
		if (!string.IsNullOrEmpty(path))
		{
			byte[] bytes = saveAsPNG ? tex.EncodeToPNG() : tex.EncodeToJPG();
			File.WriteAllBytes(path, bytes);
			AssetDatabase.Refresh();
		}

		Object.DestroyImmediate(tex);
		return allWhite;
	}

	// Utility: check whether a Texture2D is uniformly white (RGB all ~1.0)
	bool IsTextureAllWhite(Texture2D tex)
	{
		if (tex == null) return false;
		Color[] cols;
		try
		{
			cols = tex.GetPixels();
		}
		catch
		{
			// If texture format doesn't allow GetPixels, assume not all white
			return false;
		}

		const float eps = 1e-4f;
		for (int i = 0; i < cols.Length; i++)
		{
			Color c = cols[i];
			if (c.r < 1f - eps || c.g < 1f - eps || c.b < 1f - eps)
				return false;
		}
		return true;
	}

	void CleanupAndReturn(GameObject combinedColliderGO, Mesh combinedMesh)
	{
		ReleaseCombinedColliderAndMesh(combinedColliderGO, combinedMesh);
		ReleaseBuffers();
		DebugAORenderer.ClearAll();
	}

	void OnDisable()
	{
		ReleaseBuffers();
		if (uvPosRT != null) { uvPosRT.Release(); uvPosRT = null; }
		if (uvNormalRT != null) { uvNormalRT.Release(); uvNormalRT = null; }
		if (aoRT != null) { aoRT.Release(); aoRT = null; }
		ClearPreviewTextures();
		debugRendererActive = false;
		DebugAORenderer.SetEnabled(false);
		DebugAORenderer.ClearAll();
		DebugAORenderer.SetAutoExpireRays(true, 5f);
	}
}
