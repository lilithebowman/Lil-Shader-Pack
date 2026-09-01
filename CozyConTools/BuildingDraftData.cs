using System;
using System.Collections.Generic;
using UnityEngine;

namespace CozyCon.Tools
{
	[ExecuteAlways]
	public class BuildingDraftData : MonoBehaviour
	{
		[SerializeField] private List<Vector2> footprintCorners = new List<Vector2>();
		[SerializeField] private float wallHeight = 3f;
		[SerializeField] private int buildingFloors = 1;
		[SerializeField] private float wallThickness = 0.2f;
		[SerializeField] private float roofThickness = 0.2f;
		[SerializeField] private float insetRoofOffset = 0f;
		[SerializeField] private float floorThickness = 0.2f;
		[SerializeField] private Material defaultWallMaterial;
		[SerializeField] private Material ceilingMaterial;
		[SerializeField] private Material floorMaterial;
		[SerializeField] private Material trimMaterial;
		[SerializeField] private Material glassMaterial;
		[SerializeField] private Material doorMaterial;
		[SerializeField] private List<Material> wallMaterials = new List<Material>();
		[SerializeField] private List<BuildingOpeningData> openings = new List<BuildingOpeningData>();
		[SerializeField] private bool spawnWindowTrim = true;
		[SerializeField] private bool spawnWindowSill = true;
		[SerializeField] private bool spawnWindowGlass = true;
		[SerializeField] private bool spawnDoorTrim = true;
		[SerializeField] private bool spawnDoorPanelInDoorOpenings;
		[SerializeField] private float windowSillThickness = 0.04f;
		[SerializeField] private float windowFrameInset = 0.02f;
		[SerializeField] private float doorFrameInset = 0.02f;
		[SerializeField] private BuildingJoinerStyle wallJoinerStyle = BuildingJoinerStyle.Sharp;
		[SerializeField] private BuildingJoinerStyle ceilingJoinerStyle = BuildingJoinerStyle.Sharp;
		[SerializeField] private int curvedJoinerSegments = 10;
		[SerializeField] private bool enableFloorDoors = true;
		[SerializeField] private bool enableFloorStairs = false;
		[SerializeField] private float stairStepHeight = 0.3f;
		[SerializeField] private float wallUvScaleMultiplier = 1f;

		private void OnValidate()
		{
			wallJoinerStyle = NormalizeJoinerStyle(wallJoinerStyle);
			ceilingJoinerStyle = NormalizeJoinerStyle(ceilingJoinerStyle);
			curvedJoinerSegments = Mathf.Clamp(curvedJoinerSegments, 1, 32);
			wallUvScaleMultiplier = Mathf.Clamp(wallUvScaleMultiplier, 0.01f, 100f);
		}

		public List<Vector2> FootprintCorners => footprintCorners;
		public List<BuildingOpeningData> Openings => openings;

		public float WallHeight
		{
			get => wallHeight;
			set => wallHeight = Mathf.Max(0.4f, value);
		}

		public float BuildingHeight
		{
			get => wallHeight;
			set => wallHeight = Mathf.Max(0.4f, value);
		}

		public int BuildingFloors
		{
			get => buildingFloors;
			set => buildingFloors = Mathf.Clamp(value, 1, 64);
		}

		public float WallThickness
		{
			get => wallThickness;
			set => wallThickness = Mathf.Clamp(value, 0.05f, 3f);
		}

		public float RoofThickness
		{
			get => roofThickness;
			set => roofThickness = Mathf.Clamp(value, 0.05f, 3f);
		}

		public float InsetRoofOffset
		{
			get => insetRoofOffset;
			set => insetRoofOffset = Mathf.Clamp(value, 0f, 100f);
		}

		public float FloorThickness
		{
			get => floorThickness;
			set => floorThickness = Mathf.Clamp(value, 0.05f, 3f);
		}

		public Material DefaultWallMaterial
		{
			get => defaultWallMaterial;
			set => defaultWallMaterial = value;
		}

		public Material CeilingMaterial
		{
			get => ceilingMaterial;
			set => ceilingMaterial = value;
		}

		public Material FloorMaterial
		{
			get => floorMaterial;
			set => floorMaterial = value;
		}

		public Material TrimMaterial
		{
			get => trimMaterial;
			set => trimMaterial = value;
		}

		public Material GlassMaterial
		{
			get => glassMaterial;
			set => glassMaterial = value;
		}

		public Material DoorMaterial
		{
			get => doorMaterial;
			set => doorMaterial = value;
		}

		public bool SpawnWindowTrim
		{
			get => spawnWindowTrim;
			set => spawnWindowTrim = value;
		}

		public bool SpawnWindowSill
		{
			get => spawnWindowSill;
			set => spawnWindowSill = value;
		}

		public bool SpawnWindowGlass
		{
			get => spawnWindowGlass;
			set => spawnWindowGlass = value;
		}

		public bool SpawnDoorTrim
		{
			get => spawnDoorTrim;
			set => spawnDoorTrim = value;
		}

		public bool SpawnDoorPanelInDoorOpenings
		{
			get => spawnDoorPanelInDoorOpenings;
			set => spawnDoorPanelInDoorOpenings = value;
		}

		public float WindowSillThickness
		{
			get => windowSillThickness;
			set => windowSillThickness = Mathf.Max(0.005f, value);
		}

		public float WindowFrameInset
		{
			get => windowFrameInset;
			set => windowFrameInset = Mathf.Max(0.001f, value);
		}

		public float DoorFrameInset
		{
			get => doorFrameInset;
			set => doorFrameInset = Mathf.Max(0.001f, value);
		}

		public BuildingJoinerStyle WallJoinerStyle
		{
			get => NormalizeJoinerStyle(wallJoinerStyle);
			set => wallJoinerStyle = NormalizeJoinerStyle(value);
		}

		public BuildingJoinerStyle CeilingJoinerStyle
		{
			get => NormalizeJoinerStyle(ceilingJoinerStyle);
			set => ceilingJoinerStyle = NormalizeJoinerStyle(value);
		}

		public int CurvedJoinerSegments
		{
			get => curvedJoinerSegments;
			set => curvedJoinerSegments = Mathf.Clamp(value, 1, 32);
		}

		public bool EnableFloorDoors
		{
			get => enableFloorDoors;
			set => enableFloorDoors = value;
		}

		public bool EnableFloorStairs
		{
			get => enableFloorStairs;
			set => enableFloorStairs = value;
		}

		public float StairStepHeight
		{
			get => stairStepHeight;
			set => stairStepHeight = Mathf.Clamp(value, 0.1f, 2f);
		}

		public float WallUvScaleMultiplier
		{
			get => wallUvScaleMultiplier;
			set => wallUvScaleMultiplier = Mathf.Clamp(value, 0.01f, 100f);
		}

		private static BuildingJoinerStyle NormalizeJoinerStyle(BuildingJoinerStyle style)
		{
			return style == BuildingJoinerStyle.Curved ? BuildingJoinerStyle.Beveled : style;
		}

		public void EnsureWallMaterialCount()
		{
			int wallCount = Mathf.Max(0, footprintCorners.Count);
			while (wallMaterials.Count < wallCount)
			{
				wallMaterials.Add(defaultWallMaterial);
			}

			if (wallMaterials.Count > wallCount)
			{
				wallMaterials.RemoveRange(wallCount, wallMaterials.Count - wallCount);
			}
		}

		public Material GetWallMaterial(int index)
		{
			EnsureWallMaterialCount();
			if (index < 0 || index >= wallMaterials.Count)
			{
				return defaultWallMaterial;
			}

			return wallMaterials[index] != null ? wallMaterials[index] : defaultWallMaterial;
		}

		public void SetWallMaterial(int index, Material material)
		{
			EnsureWallMaterialCount();
			if (index < 0 || index >= wallMaterials.Count)
			{
				return;
			}

			wallMaterials[index] = material;
		}

		public void EnsureValidOpeningIds()
		{
			for (int i = 0; i < openings.Count; i++)
			{
				if (string.IsNullOrEmpty(openings[i].id))
				{
					openings[i].id = Guid.NewGuid().ToString("N");
				}
			}
		}

		public void RegenerateRegularFootprint(int sideCount, float radius)
		{
			sideCount = Mathf.Clamp(sideCount, 3, 20);
			radius = Mathf.Max(0.2f, radius);

			footprintCorners.Clear();
			float step = Mathf.PI * 2f / sideCount;
			float angleOffset = sideCount == 4 ? step * 0.5f : 0f;
			for (int i = 0; i < sideCount; i++)
			{
				float angle = step * i + angleOffset;
				footprintCorners.Add(new Vector2(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius));
			}

			EnsureWallMaterialCount();
		}
	}

	[Serializable]
	public class BuildingOpeningData
	{
		public string id;
		public int wallIndex;
		public BuildingOpeningType type;
		public Vector2 center;
		public Vector2 size;
		public bool showTrim = true;
		public bool showSill = true;
		public bool showGlass = true;
		public bool showDoor;
	}

	public enum BuildingOpeningType
	{
		Window,
		Door
	}

	public enum BuildingJoinerStyle
	{
		Sharp,
		Curved,
		Beveled
	}
}
