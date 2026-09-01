// File: Assets/Editor/CozyConTools/GenerateAO/BakeNonOverlapUVs.cs
// Utility to rebake meshes' UVs into non-overlapping UV0s and assign them to renderers.
// Tuned packMargin to avoid huge gaps between islands and copy generated uv2 into uv0.

#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEditor;

namespace CozyConTools.GenerateAO
{
	public static class BakeNonOverlapUVs
	{
		const string kGeneratedAOUVFolder = "Assets/GeneratedMeshes/AOUnwrapped";

		public static void BakeNewUVMapForModel(GameObject model)
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

			EditorUtility.DisplayDialog("AO UV Bake Complete", $"Created {bakedMeshes} rebaked mesh asset(s) and assigned them to {assignedRenderers} renderer(s).", "OK");
		}

		public static Mesh GetOrCreateRebakedUVMesh(Mesh sourceMesh, Dictionary<Mesh, Mesh> remappedMeshes, ref int bakedMeshes)
		{
			if (sourceMesh == null) return null;
			if (remappedMeshes.TryGetValue(sourceMesh, out Mesh existing)) return existing;

			Mesh clonedMesh = Object.Instantiate(sourceMesh);
			clonedMesh.name = sourceMesh.name + "_AOUV";

			UnwrapParam unwrapParameters = new UnwrapParam();
			UnwrapParam.SetDefaults(out unwrapParameters);

			// Adds a configurable AO texture resolution and computes a true 2px UV margin.
			// packMargin = pixelMargin / textureResolution

			// Change this to match your AO bake output resolution.
			int kAOTexResolution = sourceMesh != null ? Mathf.Max(1, Mathf.NextPowerOfTwo((int)(Mathf.Max(sourceMesh.bounds.size.x, sourceMesh.bounds.size.y, sourceMesh.bounds.size.z) * 10))) : 1024;

			// Converts pixel margin → UV margin.
			float ComputeUVPackMargin(int pixelMargin)
			{
				return (float)pixelMargin / kAOTexResolution;
			}

			// Tuned parameters: smaller packMargin to avoid huge gaps between islands
			unwrapParameters.hardAngle = 88f;
			unwrapParameters.angleError = 8f;
			unwrapParameters.areaError = 15f;
			unwrapParameters.packMargin = ComputeUVPackMargin(2);

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

			// Copy generated uv2 into uv (UV0) so materials that sample UV0 will use the rebaked layout.
			clonedMesh.uv = clonedMesh.uv2;
			clonedMesh.RecalculateBounds();

			string assetPath = AssetDatabase.GenerateUniqueAssetPath($"{kGeneratedAOUVFolder}/{SanitizeAssetName(sourceMesh.name)}_AOUV.asset");
			AssetDatabase.CreateAsset(clonedMesh, assetPath);
			remappedMeshes[sourceMesh] = clonedMesh;
			bakedMeshes++;
			return clonedMesh;
		}

		public static bool HasMesh(GameObject go)
		{
			return go.GetComponentInChildren<MeshFilter>() != null ||
				   go.GetComponentInChildren<SkinnedMeshRenderer>() != null;
		}

		public static void EnsureFolderExists(string assetFolder)
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

		static string SanitizeAssetName(string name)
		{
			if (string.IsNullOrEmpty(name)) return "mesh";
			var invalid = Path.GetInvalidFileNameChars();
			foreach (var c in invalid) name = name.Replace(c, '_');
			return name;
		}
	}
}
#endif
