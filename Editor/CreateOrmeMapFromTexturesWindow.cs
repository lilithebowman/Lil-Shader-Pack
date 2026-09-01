using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using Lilithe.Tools;

public sealed class CreateOrmeMapFromTexturesWindow : EditorWindow
{
	private enum SourceChannel
	{
		R,
		G,
		B,
		A,
		Grayscale
	}

	private Texture2D _occlusionMap;
	private Texture2D _roughnessMap;
	private Texture2D _metallicMap;
	private Texture2D _emissionMaskMap;
	private GameObject _bakeTargetObject;
	private Material _bakeSourceMaterial;
	private int _bakeResolution = 1024;
	private int _bakeSamples = 32;
	private float _bakeRayDistance = 0.5f;

	private SourceChannel _occlusionChannel = SourceChannel.Grayscale;
	private SourceChannel _roughnessChannel = SourceChannel.Grayscale;
	private SourceChannel _metallicChannel = SourceChannel.Grayscale;
	private SourceChannel _emissionChannel = SourceChannel.Grayscale;

	private bool _invertRoughness;

	[MenuItem("Lilithe/Create ORME Map From Textures")]
	private static void OpenWindow()
	{
		var window = GetWindow<CreateOrmeMapFromTexturesWindow>("Create ORME Map");
		window.minSize = new Vector2(430f, 300f);
		window.Show();
	}

	private void OnEnable()
	{
		SyncSelectionDefaults();
	}

	private void OnSelectionChange()
	{
		SyncSelectionDefaults();
		Repaint();
	}

	private void OnGUI()
	{
		EditorGUILayout.LabelField("Input Maps", EditorStyles.boldLabel);
		EditorGUILayout.HelpBox("Pack up to 4 texture maps into one ORME map. Channels are packed as R=Occlusion, G=Roughness, B=Metallic, A=Emission.", MessageType.Info);

		DrawInputRow("Occlusion -> R", ref _occlusionMap, ref _occlusionChannel);
		DrawInputRow("Roughness -> G", ref _roughnessMap, ref _roughnessChannel);
		DrawInputRow("Metallic -> B", ref _metallicMap, ref _metallicChannel);
		DrawInputRow("Emission Mask -> A", ref _emissionMaskMap, ref _emissionChannel);

		EditorGUILayout.Space();
		_invertRoughness = EditorGUILayout.ToggleLeft("Invert roughness input before packing", _invertRoughness);

		EditorGUILayout.Space();
		using (new EditorGUI.DisabledScope(!HasAnyInput()))
		{
			if (GUILayout.Button("Create ORME Texture", GUILayout.Height(30f)))
			{
				CreatePackedTexture();
			}
		}

		EditorGUILayout.Space(16f);
		EditorGUILayout.LabelField("Bake From Material Settings", EditorStyles.boldLabel);
		EditorGUILayout.HelpBox("Bake AO from a selected mesh, then pack roughness from Smoothness, metallic from Metallic, and emission from the Emission Color field.", MessageType.Info);

		_bakeTargetObject = (GameObject)EditorGUILayout.ObjectField("Target Object", _bakeTargetObject, typeof(GameObject), true);
		_bakeSourceMaterial = (Material)EditorGUILayout.ObjectField("Source Material", _bakeSourceMaterial, typeof(Material), false);
		_bakeResolution = EditorGUILayout.IntField("Bake Resolution", _bakeResolution);
		_bakeResolution = Mathf.Clamp(_bakeResolution, 64, 8192);
		_bakeSamples = EditorGUILayout.IntSlider("AO Samples", _bakeSamples, 1, 128);
		_bakeRayDistance = EditorGUILayout.Slider("AO Ray Distance", _bakeRayDistance, 0.01f, 10f);

		using (new EditorGUI.DisabledScope(!CanBakeFromMaterialSettings()))
		{
			if (GUILayout.Button("Bake ORME From Material Settings", GUILayout.Height(30f)))
			{
				BakeOrmeFromMaterialSettings();
			}
		}
	}

	private static void DrawInputRow(string label, ref Texture2D texture, ref SourceChannel sourceChannel)
	{
		EditorGUILayout.BeginHorizontal();
		texture = (Texture2D)EditorGUILayout.ObjectField(label, texture, typeof(Texture2D), false);
		sourceChannel = (SourceChannel)EditorGUILayout.EnumPopup(sourceChannel, GUILayout.Width(100f));
		EditorGUILayout.EndHorizontal();
	}

	private bool HasAnyInput()
	{
		return _occlusionMap != null
			|| _roughnessMap != null
			|| _metallicMap != null
			|| _emissionMaskMap != null;
	}

	private bool CanBakeFromMaterialSettings()
	{
		return ResolveBakeTarget(out _, out _) && ResolveBakeMaterial(out _);
	}

	private void SyncSelectionDefaults()
	{
		if (_bakeTargetObject == null && Selection.activeGameObject != null)
		{
			_bakeTargetObject = Selection.activeGameObject;
		}

		if (_bakeSourceMaterial == null && _bakeTargetObject != null)
		{
			if (ResolveBakeTarget(out _, out var targetRenderer))
			{
				_bakeSourceMaterial = targetRenderer != null ? targetRenderer.sharedMaterial : null;
			}
		}
	}

	private bool ResolveBakeTarget(out GameObject targetObject, out Renderer targetRenderer)
	{
		targetObject = _bakeTargetObject != null ? _bakeTargetObject : Selection.activeGameObject;
		targetRenderer = null;

		if (targetObject == null)
		{
			return false;
		}

		targetRenderer = targetObject.GetComponent<Renderer>();
		if (targetRenderer == null)
		{
			targetRenderer = targetObject.GetComponentInChildren<Renderer>();
		}

		return targetRenderer != null;
	}

	private bool ResolveBakeMaterial(out Material sourceMaterial)
	{
		sourceMaterial = _bakeSourceMaterial;
		if (sourceMaterial != null)
		{
			return true;
		}

		if (!ResolveBakeTarget(out _, out var targetRenderer))
		{
			return false;
		}

		sourceMaterial = targetRenderer != null ? targetRenderer.sharedMaterial : null;
		return sourceMaterial != null;
	}

	private void BakeOrmeFromMaterialSettings()
	{
		if (!ResolveBakeTarget(out var targetObject, out var targetRenderer))
		{
			EditorUtility.DisplayDialog("Bake ORME Map", "Select a GameObject with a Renderer that has a MeshFilter.", "OK");
			return;
		}

		if (!ResolveBakeMaterial(out var sourceMaterial))
		{
			EditorUtility.DisplayDialog("Bake ORME Map", "Select a source material or assign one to the target object.", "OK");
			return;
		}

		if (!AmbientOcclusionBaker.BakeAmbientOcclusionPixels(targetObject, targetRenderer, _bakeResolution, _bakeSamples, _bakeRayDistance, out var aoPixels, out var size, out var error))
		{
			EditorUtility.DisplayDialog("Bake ORME Map", error ?? "Failed to bake ambient occlusion.", "OK");
			return;
		}

		var outputPath = EditorUtility.SaveFilePanelInProject(
			"Save ORME Map",
			"New_ORME",
			"png",
			"Choose where to save the generated ORME texture.");

		if (string.IsNullOrWhiteSpace(outputPath))
		{
			return;
		}

		float smoothness = ReadMaterialFloat(sourceMaterial, "_Glossiness", 0.5f);
		float roughness = 1f - Mathf.Clamp01(smoothness);
		float metallic = Mathf.Clamp01(ReadMaterialFloat(sourceMaterial, "_Metallic", 0f));
		Color emissionColor = ReadMaterialColor(sourceMaterial, "_EmissionColor", Color.black);
		float emissionMask = Mathf.Clamp01(Mathf.Max(emissionColor.r, Mathf.Max(emissionColor.g, emissionColor.b)));

		var outPixels = new Color32[size * size];
		for (var i = 0; i < outPixels.Length; i++)
		{
			float occlusion = Mathf.Clamp01(aoPixels[i].r);
			outPixels[i] = new Color32(
				(FloatToByte(occlusion)),
				(FloatToByte(roughness)),
				(FloatToByte(metallic)),
				(FloatToByte(emissionMask)));
		}

		var outputTexture = new Texture2D(size, size, TextureFormat.RGBA32, false, true);
		outputTexture.SetPixels32(outPixels);
		outputTexture.Apply(false, false);

		var bytes = outputTexture.EncodeToPNG();
		DestroyImmediate(outputTexture);

		if (bytes == null || bytes.Length == 0)
		{
			EditorUtility.DisplayDialog("Bake ORME Map", "Failed to encode texture as PNG.", "OK");
			return;
		}

		File.WriteAllBytes(outputPath, bytes);
		AssetDatabase.ImportAsset(outputPath, ImportAssetOptions.ForceUpdate);
		ConfigureImportedTexture(outputPath);
		AssetDatabase.SaveAssets();
		AssetDatabase.Refresh();

		Selection.activeObject = AssetDatabase.LoadAssetAtPath<Texture2D>(outputPath);
		EditorUtility.DisplayDialog("Bake ORME Map", $"Created ORME map:\n{outputPath}", "OK");
	}

	private void CreatePackedTexture()
	{
		if (!TryGetOutputSize(out var width, out var height))
		{
			EditorUtility.DisplayDialog("Create ORME Map", "Select at least one texture map.", "OK");
			return;
		}

		var outputPath = EditorUtility.SaveFilePanelInProject(
			"Save ORME Map",
			"New_ORME",
			"png",
			"Choose where to save the generated ORME texture.");

		if (string.IsNullOrWhiteSpace(outputPath))
		{
			return;
		}

		var occlusionPixels = CreateScaledPixels(_occlusionMap, width, height);
		var roughnessPixels = CreateScaledPixels(_roughnessMap, width, height);
		var metallicPixels = CreateScaledPixels(_metallicMap, width, height);
		var emissionPixels = CreateScaledPixels(_emissionMaskMap, width, height);

		var outPixels = new Color32[width * height];
		for (var i = 0; i < outPixels.Length; i++)
		{
			var occlusion = occlusionPixels == null ? 1f : ReadChannel(occlusionPixels[i], _occlusionChannel);
			var roughness = roughnessPixels == null ? 0f : ReadChannel(roughnessPixels[i], _roughnessChannel);
			if (_invertRoughness)
			{
				roughness = 1f - roughness;
			}

			var metallic = metallicPixels == null ? 0f : ReadChannel(metallicPixels[i], _metallicChannel);
			var emission = emissionPixels == null ? 0f : ReadChannel(emissionPixels[i], _emissionChannel);

			outPixels[i] = new Color(
				Mathf.Clamp01(occlusion),
				Mathf.Clamp01(roughness),
				Mathf.Clamp01(metallic),
				Mathf.Clamp01(emission));
		}

		var outputTexture = new Texture2D(width, height, TextureFormat.RGBA32, false, true);
		outputTexture.SetPixels32(outPixels);
		outputTexture.Apply(false, false);

		var bytes = outputTexture.EncodeToPNG();
		DestroyImmediate(outputTexture);

		if (bytes == null || bytes.Length == 0)
		{
			EditorUtility.DisplayDialog("Create ORME Map", "Failed to encode texture as PNG.", "OK");
			return;
		}

		File.WriteAllBytes(outputPath, bytes);
		AssetDatabase.ImportAsset(outputPath, ImportAssetOptions.ForceUpdate);

		ConfigureImportedTexture(outputPath);
		AssetDatabase.SaveAssets();
		AssetDatabase.Refresh();

		var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(outputPath);
		Selection.activeObject = texture;

		EditorUtility.DisplayDialog("Create ORME Map", $"Created ORME map:\n{outputPath}", "OK");
	}

	private bool TryGetOutputSize(out int width, out int height)
	{
		width = 0;
		height = 0;

		UpdateSize(_occlusionMap, ref width, ref height);
		UpdateSize(_roughnessMap, ref width, ref height);
		UpdateSize(_metallicMap, ref width, ref height);
		UpdateSize(_emissionMaskMap, ref width, ref height);

		return width > 0 && height > 0;
	}

	private static void UpdateSize(Texture2D texture, ref int width, ref int height)
	{
		if (texture == null)
		{
			return;
		}

		width = Mathf.Max(width, texture.width);
		height = Mathf.Max(height, texture.height);
	}

	private static Color32[] CreateScaledPixels(Texture2D source, int width, int height)
	{
		if (source == null)
		{
			return null;
		}

		var previousRt = RenderTexture.active;
		var rt = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);

		try
		{
			Graphics.Blit(source, rt);
			RenderTexture.active = rt;

			var readable = new Texture2D(width, height, TextureFormat.RGBA32, false, true);
			readable.ReadPixels(new Rect(0, 0, width, height), 0, 0);
			readable.Apply(false, false);

			var pixels = readable.GetPixels32();
			DestroyImmediate(readable);
			return pixels;
		}
		finally
		{
			RenderTexture.active = previousRt;
			RenderTexture.ReleaseTemporary(rt);
		}
	}

	private static float ReadChannel(Color32 color, SourceChannel channel)
	{
		switch (channel)
		{
			case SourceChannel.R:
				return color.r / 255f;
			case SourceChannel.G:
				return color.g / 255f;
			case SourceChannel.B:
				return color.b / 255f;
			case SourceChannel.A:
				return color.a / 255f;
			case SourceChannel.Grayscale:
			default:
				return (0.299f * color.r + 0.587f * color.g + 0.114f * color.b) / 255f;
		}
	}

	private static void ConfigureImportedTexture(string assetPath)
	{
		var importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
		if (importer == null)
		{
			return;
		}

		importer.textureType = TextureImporterType.Default;
		importer.sRGBTexture = false;
		importer.alphaSource = TextureImporterAlphaSource.FromInput;
		importer.alphaIsTransparency = false;
		importer.mipmapEnabled = false;
		importer.SaveAndReimport();
	}

	private static float ReadMaterialFloat(Material material, string propertyName, float fallback)
	{
		return material != null && material.HasProperty(propertyName) ? material.GetFloat(propertyName) : fallback;
	}

	private static Color ReadMaterialColor(Material material, string propertyName, Color fallback)
	{
		return material != null && material.HasProperty(propertyName) ? material.GetColor(propertyName) : fallback;
	}

	private static byte FloatToByte(float value)
	{
		return (byte)Mathf.RoundToInt(Mathf.Clamp01(value) * 255f);
	}
}