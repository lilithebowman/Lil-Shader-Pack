/**
 * LODDataModels.cs
 *
 * Data structures for LOD generation results and information.
 * Extracted from LODGenerator for better organization and reusability.
 * 
 * CC0-Attribution License by Lilithe for CozyCon 2025
 */

using UnityEngine;

namespace CozyCon.Tools
{
	/// <summary>
	/// Contains the complete results of a LOD generation operation
	/// </summary>
	[System.Serializable]
	public class LODGenerationResult
	{
		public GameObject originalModel;
		public GameObject lodGroup;
		public GameObject createdPrefab;
		public string prefabPath;
		public MeshInfo originalMesh;
		public MeshInfo lod1Mesh;
		public MeshInfo lod2Mesh;
		public BillboardInfo[] billboards;
		public float totalSizeReduction;
		public string resultPath;
	}

	/// <summary>
	/// Information about a mesh including triangle/vertex counts and size estimates
	/// </summary>
	[System.Serializable]
	public class MeshInfo
	{
		public Mesh mesh;
		public int triangleCount;
		public int vertexCount;
		public float sizeKB;
		public float reductionPercent;
	}

	/// <summary>
	/// Information about a generated billboard including texture, angle, and rendering components
	/// </summary>
	[System.Serializable]
	public class BillboardInfo
	{
		public Texture2D texture;
		public float angle;
		public Material material;
		public Mesh quad;
	}

	/// <summary>
	/// Configuration settings for LOD generation
	/// </summary>
	[System.Serializable]
	public class LODGenerationSettings
	{
		// LOD Generation Settings
		public bool generateLOD1 = true;
		public bool generateLOD2 = true;
		public bool generateBillboards = false;
		public bool useVRChatOptimizedDistances = true;
		public bool preserveUVs = true;
		public bool preserveNormals = true;
		public float decimationAngle = 5f;
		public bool generateColliders = false;
		public bool createPrefab = true;
		public string prefabSavePath = "Assets/Generated/LOD Prefabs/";
		public bool removeFromSceneAfterPrefab = true;
		public LODFadeMode selectedFadeMode = LODFadeMode.None;

		// LOD Distances
		public float lod0Distance = 0.8f;
		public float lod1Distance = 0.5f;
		public float lod2Distance = 0.25f;
		public float billboardDistance = 0.1f;

		// Billboard Settings
		public int billboardResolution = 512;
		public int billboardAngles = 8;
		public bool billboardTransparency = true;
		public string billboardShader = "Standard";
	}
}