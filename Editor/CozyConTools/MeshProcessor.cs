/**
 * MeshProcessor.cs
 *
 * High-level mesh processing utilities for LOD generation.
 * Provides mesh analysis and decimation functionality using the MeshProcessorQuadric backend.
 * 
 * CC0-Attribution License by Lilithe for CozyCon 2025
 */

using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.Linq;

namespace CozyCon.Tools
{
	/// <summary>
	/// High-level mesh processing utilities for LOD generation
	/// </summary>
	public static class MeshProcessor
	{
		/// <summary>
		/// Analyzes a GameObject to extract mesh information including triangle counts, vertex counts, and size estimates
		/// </summary>
		/// <param name="gameObject">The GameObject to analyze</param>
		/// <returns>MeshInfo containing analysis results, or null if no meshes found</returns>
		public static MeshInfo AnalyzeMesh(GameObject gameObject)
		{
			if (gameObject == null)
			{
				Debug.LogWarning("[MeshProcessor] Cannot analyze null GameObject");
				return null;
			}

			var meshInfo = new MeshInfo();
			int totalTriangles = 0;
			int totalVertices = 0;
			float totalSizeKB = 0f;
			bool foundMesh = false;

			// Analyze MeshFilter components
			var meshFilters = gameObject.GetComponentsInChildren<MeshFilter>(true);
			foreach (var meshFilter in meshFilters)
			{
				if (meshFilter.sharedMesh != null)
				{
					foundMesh = true;
					var mesh = meshFilter.sharedMesh;
					int triangles = mesh.triangles?.Length / 3 ?? 0;
					int vertices = mesh.vertexCount;

					totalTriangles += triangles;
					totalVertices += vertices;

					// Estimate mesh size in KB (rough calculation based on vertex data)
					// Vector3 (12 bytes) + Vector3 normal (12 bytes) + Vector2 UV (8 bytes) + indices (4 bytes per triangle vertex)
					float meshSizeKB = (vertices * 32f + triangles * 12f) / 1024f;
					totalSizeKB += meshSizeKB;
				}
			}

			// Analyze SkinnedMeshRenderer components
			var skinnedRenderers = gameObject.GetComponentsInChildren<SkinnedMeshRenderer>(true);
			foreach (var renderer in skinnedRenderers)
			{
				if (renderer.sharedMesh != null)
				{
					foundMesh = true;
					var mesh = renderer.sharedMesh;
					int triangles = mesh.triangles?.Length / 3 ?? 0;
					int vertices = mesh.vertexCount;

					totalTriangles += triangles;
					totalVertices += vertices;

					// Add bone weight data overhead for skinned meshes
					float meshSizeKB = (vertices * 48f + triangles * 12f) / 1024f;
					totalSizeKB += meshSizeKB;
				}
			}

			if (!foundMesh)
			{
				Debug.LogWarning($"[MeshProcessor] No meshes found in GameObject '{gameObject.name}'");
				return null;
			}

			meshInfo.triangleCount = totalTriangles;
			meshInfo.vertexCount = totalVertices;
			meshInfo.sizeKB = totalSizeKB;
			meshInfo.reductionPercent = 0f; // No reduction for original mesh

			Debug.Log($"[MeshProcessor] Analyzed '{gameObject.name}': {totalTriangles} triangles, {totalVertices} vertices, {totalSizeKB:F1} KB");

			return meshInfo;
		}

		/// <summary>
		/// Creates a decimated version of a GameObject using the specified reduction ratio
		/// </summary>
		/// <param name="originalObject">The original GameObject to decimate</param>
		/// <param name="reductionRatio">The reduction ratio (0.5 = 50% reduction)</param>
		/// <param name="lodName">Name suffix for the LOD (e.g., "LOD1", "LOD2")</param>
		/// <param name="settings">LOD generation settings</param>
		/// <returns>New GameObject with decimated meshes, or null if decimation failed</returns>
		public static GameObject CreateDecimatedMesh(GameObject originalObject, float reductionRatio, string lodName, LODGenerationSettings settings)
		{
			if (originalObject == null)
			{
				Debug.LogError("[MeshProcessor] Cannot decimate null GameObject");
				return null;
			}

			Debug.Log($"[MeshProcessor] Creating decimated mesh for '{originalObject.name}' with {reductionRatio:P0} reduction");

			// Create a copy of the original object
			GameObject decimatedObject = Object.Instantiate(originalObject);
			decimatedObject.name = originalObject.name + "_" + lodName;

			bool hasDecimatedAnyMesh = false;

			// Decimate MeshFilter components
			var meshFilters = decimatedObject.GetComponentsInChildren<MeshFilter>(true);
			foreach (var meshFilter in meshFilters)
			{
				if (meshFilter.sharedMesh != null)
				{
					var originalMesh = meshFilter.sharedMesh;

					// Skip very low poly meshes
					if (originalMesh.triangles == null || originalMesh.triangles.Length < 12)
					{
						Debug.Log($"[MeshProcessor] Skipping decimation of low-poly mesh '{originalMesh.name}' ({originalMesh.triangles?.Length / 3 ?? 0} triangles)");
						continue;
					}

					try
					{
						var decimatedMesh = MeshProcessorQuadric.DecimateMesh(originalMesh, reductionRatio);
						if (decimatedMesh != null && decimatedMesh.triangles != null && decimatedMesh.triangles.Length >= 3)
						{
							decimatedMesh.name = originalMesh.name + "_" + lodName;
							meshFilter.sharedMesh = decimatedMesh;
							meshFilter.mesh = decimatedMesh;
							hasDecimatedAnyMesh = true;

							Debug.Log($"[MeshProcessor] Decimated '{originalMesh.name}': {originalMesh.triangles.Length / 3} -> {decimatedMesh.triangles.Length / 3} triangles");
						}
						else
						{
							Debug.LogWarning($"[MeshProcessor] Decimation failed for mesh '{originalMesh.name}' - keeping original");
						}
					}
					catch (System.Exception e)
					{
						Debug.LogError($"[MeshProcessor] Error decimating mesh '{originalMesh.name}': {e.Message}");
					}
				}
			}

			// Decimate SkinnedMeshRenderer components
			var skinnedRenderers = decimatedObject.GetComponentsInChildren<SkinnedMeshRenderer>(true);
			foreach (var renderer in skinnedRenderers)
			{
				if (renderer.sharedMesh != null)
				{
					var originalMesh = renderer.sharedMesh;

					// Skip very low poly meshes
					if (originalMesh.triangles == null || originalMesh.triangles.Length < 12)
					{
						Debug.Log($"[MeshProcessor] Skipping decimation of low-poly skinned mesh '{originalMesh.name}' ({originalMesh.triangles?.Length / 3 ?? 0} triangles)");
						continue;
					}

					try
					{
						var decimatedMesh = MeshProcessorQuadric.DecimateMesh(originalMesh, reductionRatio);
						if (decimatedMesh != null && decimatedMesh.triangles != null && decimatedMesh.triangles.Length >= 3)
						{
							decimatedMesh.name = originalMesh.name + "_" + lodName;
							renderer.sharedMesh = decimatedMesh;
							hasDecimatedAnyMesh = true;

							Debug.Log($"[MeshProcessor] Decimated skinned mesh '{originalMesh.name}': {originalMesh.triangles.Length / 3} -> {decimatedMesh.triangles.Length / 3} triangles");
						}
						else
						{
							Debug.LogWarning($"[MeshProcessor] Decimation failed for skinned mesh '{originalMesh.name}' - keeping original");
						}
					}
					catch (System.Exception e)
					{
						Debug.LogError($"[MeshProcessor] Error decimating skinned mesh '{originalMesh.name}': {e.Message}");
					}
				}
			}

			// Handle colliders if requested
			if (!settings.generateColliders)
			{
				// Remove colliders from decimated LOD
				var colliders = decimatedObject.GetComponentsInChildren<Collider>(true);
				foreach (var collider in colliders)
				{
					Object.DestroyImmediate(collider);
				}
			}
			else
			{
				// Update mesh colliders to use decimated meshes
				var meshColliders = decimatedObject.GetComponentsInChildren<MeshCollider>(true);
				foreach (var meshCollider in meshColliders)
				{
					var meshFilter = meshCollider.GetComponent<MeshFilter>();
					if (meshFilter != null && meshFilter.sharedMesh != null)
					{
						meshCollider.sharedMesh = meshFilter.sharedMesh;
					}
				}
			}

			if (!hasDecimatedAnyMesh)
			{
				Debug.LogWarning($"[MeshProcessor] No meshes were successfully decimated for '{originalObject.name}'");
				Object.DestroyImmediate(decimatedObject);
				return null;
			}

			Debug.Log($"[MeshProcessor] Successfully created decimated GameObject '{decimatedObject.name}'");
			return decimatedObject;
		}
	}
}