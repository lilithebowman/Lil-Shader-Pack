/**
 * BillboardGenerator.cs
 *
 * Specialized billboard generation for LOD systems.
 * Handles texture capture from multiple angles, transparency processing, and material creation.
 * 
 * CC0-Attribution License by Lilithe for CozyCon 2025
 */

using UnityEngine;
using UnityEditor;
using System.Collections.Generic;

namespace CozyCon.Tools
{
	/// <summary>
	/// Handles billboard generation including texture capture, material creation, and quad generation
	/// </summary>
	public static class BillboardGenerator
	{
		/// <summary>
		/// Creates billboard objects for the specified original object
		/// </summary>
		public static GameObject[] CreateBillboards(GameObject original, GameObject parent, LODGenerationSettings settings)
		{
			List<GameObject> billboardObjects = new List<GameObject>();

			// Create a container for all billboards
			GameObject billboardContainer = new GameObject("BillboardContainer");
			billboardContainer.transform.SetParent(parent.transform);
			billboardContainer.transform.localPosition = Vector3.zero;
			billboardContainer.transform.localRotation = Quaternion.identity;
			billboardContainer.transform.localScale = Vector3.one;

			// Create render textures for each angle
			var billboardTextures = CaptureObjectFromAngles(original, settings);

			Debug.Log($"[BillboardGenerator] Creating {billboardTextures.Length} billboard quads");

			for (int i = 0; i < billboardTextures.Length; i++)
			{
				GameObject billboardObj = CreateBillboardQuad(billboardTextures[i], i, billboardContainer, original, settings);
				billboardObj.transform.localPosition = Vector3.zero;
				billboardObj.transform.localRotation = Quaternion.identity;
				// Copy the original scale to maintain size
				billboardObj.transform.localScale = original.transform.localScale;
				billboardObjects.Add(billboardObj);
			}

			Debug.Log($"[BillboardGenerator] Created {billboardObjects.Count} billboard objects in container");
			return billboardObjects.ToArray();
		}

		/// <summary>
		/// Captures the object from multiple angles to create billboard textures
		/// </summary>
		public static Texture2D[] CaptureObjectFromAngles(GameObject obj, LODGenerationSettings settings)
		{
			List<Texture2D> textures = new List<Texture2D>();
			GameObject tempObj = null;
			GameObject tempCameraObj = null;
			RenderTexture renderTexture = null;

			try
			{
				// Create a temporary copy of the object for billboard capture
				tempObj = Object.Instantiate(obj);
				tempObj.name = obj.name + "_BillboardCapture";

				// Position temporary object at origin for consistent capture
				tempObj.transform.position = Vector3.zero;
				tempObj.transform.rotation = Quaternion.identity;
				tempObj.transform.localScale = obj.transform.localScale;

				// Clean up temporary object
				CleanupTempObject(tempObj);

				// Set up capture layer
				int captureLayer = GetTemporaryLayer();
				if (captureLayer == -1)
				{
					Debug.LogError("[BillboardGenerator] No available layers for billboard capture!");
					return textures.ToArray();
				}

				SetObjectToLayer(tempObj, captureLayer);

				// Create and configure capture camera
				var cameraSetup = CreateCaptureCamera(captureLayer, settings);
				tempCameraObj = cameraSetup.cameraObject;
				renderTexture = cameraSetup.renderTexture;

				// Calculate positioning
				Bounds bounds = CalculateObjectBounds(tempObj);
				float distance = bounds.size.magnitude * 2.5f;
				ConfigureCameraForObject(cameraSetup.camera, bounds, distance);

				// Capture from each angle
				for (int angle = 0; angle < settings.billboardAngles; angle++)
				{
					var capturedTexture = CaptureFromAngle(cameraSetup.camera, bounds, distance, angle, settings, obj.name);
					if (capturedTexture != null)
					{
						textures.Add(capturedTexture);
					}
				}
			}
			catch (System.Exception e)
			{
				Debug.LogError($"[BillboardGenerator] Billboard capture failed: {e.Message}");
			}
			finally
			{
				CleanupCaptureResources(tempObj, tempCameraObj, renderTexture);
			}

			return textures.ToArray();
		}

		/// <summary>
		/// Creates a billboard quad with the specified texture and settings
		/// </summary>
		public static GameObject CreateBillboardQuad(Texture2D texture, int angleIndex, GameObject parent, GameObject originalModel, LODGenerationSettings settings)
		{
			// Create quad mesh
			GameObject quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
			quad.name = $"Billboard_Angle_{angleIndex}";
			quad.transform.SetParent(parent.transform);

			Debug.Log($"[BillboardGenerator] Creating billboard quad {angleIndex} with texture {texture.name} ({texture.width}x{texture.height})");

			// Create and configure material
			Material billboardMaterial = CreateBillboardMaterial(texture, angleIndex, originalModel.name, settings);

			// Apply material
			Renderer quadRenderer = quad.GetComponent<Renderer>();
			quadRenderer.material = billboardMaterial;

			// Remove collider (not needed for billboards)
			var collider = quad.GetComponent<Collider>();
			if (collider) Object.DestroyImmediate(collider);

			Debug.Log($"[BillboardGenerator] Billboard quad {angleIndex} created successfully with renderer {quadRenderer.GetInstanceID()}");
			return quad;
		}

		#region Private Helper Methods

		private static void CleanupTempObject(GameObject tempObj)
		{
			// Remove any scripts that might interfere with capture
			var scripts = tempObj.GetComponentsInChildren<MonoBehaviour>();
			foreach (var script in scripts)
			{
				if (script != null)
					Object.DestroyImmediate(script);
			}

			// Remove any colliders (not needed for visual capture)
			var colliders = tempObj.GetComponentsInChildren<Collider>();
			foreach (var collider in colliders)
			{
				if (collider != null)
					Object.DestroyImmediate(collider);
			}
		}

		private static int GetTemporaryLayer()
		{
			// Find an unused layer (layers 8-31 are user-defined)
			for (int i = 8; i < 32; i++)
			{
				string layerName = LayerMask.LayerToName(i);
				if (string.IsNullOrEmpty(layerName))
				{
					return i;
				}
			}
			return -1; // No available layer found
		}

		private static void SetObjectToLayer(GameObject obj, int layer)
		{
			List<Transform> allTransforms = new List<Transform>();
			GetAllChildTransforms(obj.transform, allTransforms);

			foreach (Transform t in allTransforms)
			{
				t.gameObject.layer = layer;
			}
		}

		private static void GetAllChildTransforms(Transform parent, List<Transform> transforms)
		{
			transforms.Add(parent);
			foreach (Transform child in parent)
			{
				GetAllChildTransforms(child, transforms);
			}
		}

		private static CameraSetup CreateCaptureCamera(int captureLayer, LODGenerationSettings settings)
		{
			GameObject tempCameraObj = new GameObject("TempCaptureCamera");
			Camera captureCamera = tempCameraObj.AddComponent<Camera>();

			// Configure camera for transparent background capture
			captureCamera.backgroundColor = Color.clear;
			captureCamera.clearFlags = CameraClearFlags.SolidColor;
			captureCamera.cullingMask = 1 << captureLayer; // Only render our capture layer
			captureCamera.orthographic = false;
			captureCamera.nearClipPlane = 0.1f;
			captureCamera.farClipPlane = 1000f;

			// Set up render texture with alpha support
			RenderTexture renderTexture = new RenderTexture(settings.billboardResolution, settings.billboardResolution, 24, RenderTextureFormat.ARGB32);
			renderTexture.antiAliasing = 4; // Enable anti-aliasing for better quality
			captureCamera.targetTexture = renderTexture;

			return new CameraSetup
			{
				cameraObject = tempCameraObj,
				camera = captureCamera,
				renderTexture = renderTexture
			};
		}

		private static void ConfigureCameraForObject(Camera camera, Bounds bounds, float distance)
		{
			// Adjust camera field of view to frame object properly
			float boundingRadius = bounds.size.magnitude * 0.5f;
			camera.fieldOfView = Mathf.Atan(boundingRadius / distance) * Mathf.Rad2Deg * 2.2f;
		}

		private static Texture2D CaptureFromAngle(Camera camera, Bounds bounds, float distance, int angle, LODGenerationSettings settings, string objectName)
		{
			float angleRad = (angle * 360f / settings.billboardAngles) * Mathf.Deg2Rad;
			Vector3 cameraPos = bounds.center + new Vector3(
				Mathf.Sin(angleRad) * distance,
				0,
				Mathf.Cos(angleRad) * distance
			);

			camera.transform.position = cameraPos;
			camera.transform.LookAt(bounds.center);

			// Render the isolated object
			camera.Render();

			// Create texture with alpha channel
			RenderTexture.active = camera.targetTexture;
			Texture2D capturedTexture = new Texture2D(settings.billboardResolution, settings.billboardResolution, TextureFormat.RGBA32, false);
			capturedTexture.ReadPixels(new Rect(0, 0, settings.billboardResolution, settings.billboardResolution), 0, 0);
			capturedTexture.Apply();

			// Process alpha channel for proper transparency
			ProcessTransparency(capturedTexture, settings);

			capturedTexture.name = $"{objectName}_Billboard_Angle_{angle}";
			return capturedTexture;
		}

		private static void ProcessTransparency(Texture2D texture, LODGenerationSettings settings)
		{
			if (!settings.billboardTransparency) return;

			Color[] pixels = texture.GetPixels();

			for (int i = 0; i < pixels.Length; i++)
			{
				Color pixel = pixels[i];

				// If the pixel is very close to the clear color (background), make it transparent
				if (pixel.a < 0.1f || (pixel.r < 0.05f && pixel.g < 0.05f && pixel.b < 0.05f && pixel.a < 0.5f))
				{
					pixels[i] = Color.clear;
				}
				else
				{
					// Enhance alpha based on luminance for better edge detection
					float luminance = (pixel.r * 0.299f + pixel.g * 0.587f + pixel.b * 0.114f);
					pixel.a = Mathf.Max(pixel.a, luminance > 0.1f ? 1.0f : 0.0f);
					pixels[i] = pixel;
				}
			}

			texture.SetPixels(pixels);
			texture.Apply();
		}

		private static Material CreateBillboardMaterial(Texture2D texture, int angleIndex, string modelName, LODGenerationSettings settings)
		{
			Material billboardMaterial = new Material(Shader.Find(settings.billboardShader));
			billboardMaterial.mainTexture = texture;
			billboardMaterial.name = $"{modelName}_Billboard_Material_Angle_{angleIndex}";

			// Configure transparency settings
			if (settings.billboardTransparency && texture.format == TextureFormat.RGBA32)
			{
				ConfigureTransparentMaterial(billboardMaterial, angleIndex);
			}
			else
			{
				ConfigureOpaqueMaterial(billboardMaterial, angleIndex);
			}

			return billboardMaterial;
		}

		private static void ConfigureTransparentMaterial(Material material, int angleIndex)
		{
			// Configure material for alpha blending
			material.SetFloat("_Mode", 3); // Transparent mode
			material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
			material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
			material.SetInt("_ZWrite", 0);
			material.DisableKeyword("_ALPHATEST_ON");
			material.EnableKeyword("_ALPHABLEND_ON");
			material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
			material.renderQueue = 3000;

			// Set alpha cutoff for better transparency
			material.SetFloat("_Cutoff", 0.1f);
			Debug.Log($"[BillboardGenerator] Billboard material {angleIndex} configured for transparency");
		}

		private static void ConfigureOpaqueMaterial(Material material, int angleIndex)
		{
			// Opaque mode
			material.SetFloat("_Mode", 0);
			material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.One);
			material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.Zero);
			material.SetInt("_ZWrite", 1);
			material.DisableKeyword("_ALPHATEST_ON");
			material.DisableKeyword("_ALPHABLEND_ON");
			material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
			material.renderQueue = -1;
			Debug.Log($"[BillboardGenerator] Billboard material {angleIndex} configured for opaque rendering");
		}

		private static Bounds CalculateObjectBounds(GameObject obj)
		{
			var renderers = obj.GetComponentsInChildren<Renderer>();
			if (renderers.Length == 0) return new Bounds();

			Bounds bounds = renderers[0].bounds;
			foreach (var renderer in renderers)
			{
				bounds.Encapsulate(renderer.bounds);
			}
			return bounds;
		}

		private static void CleanupCaptureResources(GameObject tempObj, GameObject tempCameraObj, RenderTexture renderTexture)
		{
			// Ensure cleanup always happens
			if (tempObj != null)
				Object.DestroyImmediate(tempObj);

			if (renderTexture != null)
			{
				RenderTexture.active = null;
				renderTexture.Release();
			}

			if (tempCameraObj != null)
				Object.DestroyImmediate(tempCameraObj);
		}

		#endregion

		#region Helper Structs

		private struct CameraSetup
		{
			public GameObject cameraObject;
			public Camera camera;
			public RenderTexture renderTexture;
		}

		#endregion
	}
}