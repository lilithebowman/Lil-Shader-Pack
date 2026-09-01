using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public class PlantFoliageWindow : EditorWindow
{
	private enum PlantRunStage
	{
		Idle,
		ClearingExisting,
		Spawning,
		Finalizing
	}

	private enum PlantCountMode
	{
		Density,
		FixedCount
	}

	private enum PrefabUpAxis
	{
		X,
		Y,
		Z,
		NegativeX,
		NegativeY,
		NegativeZ
	}

	private struct SurfaceTriangle
	{
		public Vector3 a;
		public Vector3 b;
		public Vector3 c;
		public Vector3 na;
		public Vector3 nb;
		public Vector3 nc;
		public Vector3 normal;
		public float cumulativeArea;
	}

	private GameObject targetObject;
	private PlantCountMode countMode = PlantCountMode.Density;
	private float densityPerSquareMeter = 2f;
	private int fixedCount = 100;
	private string plantedParentName = "Planted Foliage";
	private bool clearBeforePlanting = true;
	private bool alignToSurfaceNormal = true;
	private PrefabUpAxis prefabUpAxis = PrefabUpAxis.Y;
	private bool onlyUseUpwardFacingSurfaces = true;
	[Range(0f, 1f)]
	private float minUpwardDot = 0.1f;
	private float surfaceOffset = 0f;
	private bool randomizeYaw = true;
	private float minScale = 1f;
	private float maxScale = 1f;
	private bool manualPlantMode;
	private float manualBrushRadius = 1f;
	private float manualDensityPerSquareMeter = 8f;
	private int randomSeed = 12345;
	private int batchSizePerUpdate = 250;
	private Vector2 scroll;
	private int manualClickIndex;
	private bool manualClickArmed;
	private bool manualRemoveClickArmed;

	private PlantRunStage runStage = PlantRunStage.Idle;
	private bool isPlanting;
	private bool cancelRequested;
	private int undoGroup = -1;
	private int targetCountInRun;
	private int plantedCountInRun;
	private int clearStartChildCount;
	private float totalAreaInRun;
	private double runStartTime;
	private string stageText = "Idle";
	private Vector3 upReferenceInRun = Vector3.up;
	private Transform plantedParentInRun;
	private List<GameObject> validPrefabsInRun;
	private List<SurfaceTriangle> trianglesInRun;
	private System.Random randomInRun;

	[SerializeField]
	private GameObject[] foliagePrefabs = Array.Empty<GameObject>();

	[MenuItem("Lilithe/Plant Foliage")]
	public static void ShowWindow()
	{
		GetWindow<PlantFoliageWindow>("Plant Foliage");
	}

	private void OnEnable()
	{
		EditorApplication.update += OnEditorUpdate;
		SceneView.duringSceneGui += OnSceneGUI;
	}

	private void OnDisable()
	{
		EditorApplication.update -= OnEditorUpdate;
		SceneView.duringSceneGui -= OnSceneGUI;
		manualPlantMode = false;
		manualClickArmed = false;
		manualRemoveClickArmed = false;
		if (isPlanting)
		{
			EditorUtility.ClearProgressBar();
			ResetRunState();
		}
	}

	private void OnGUI()
	{
		scroll = EditorGUILayout.BeginScrollView(scroll);

		EditorGUILayout.LabelField("Plant Foliage", EditorStyles.boldLabel);
		EditorGUILayout.HelpBox("Plants prefab instances on the surface of the selected mesh object.", MessageType.Info);

		targetObject = (GameObject)EditorGUILayout.ObjectField("Target GameObject", targetObject, typeof(GameObject), true);
		countMode = (PlantCountMode)EditorGUILayout.EnumPopup("Placement Mode", countMode);

		if (countMode == PlantCountMode.Density)
		{
			densityPerSquareMeter = EditorGUILayout.FloatField("Density (objects/m^2)", densityPerSquareMeter);
			densityPerSquareMeter = Mathf.Max(0f, densityPerSquareMeter);
		}
		else
		{
			fixedCount = EditorGUILayout.IntField("Object Count", fixedCount);
			fixedCount = Mathf.Max(0, fixedCount);
		}

		EditorGUILayout.Space();
		EditorGUILayout.LabelField("Foliage Prefabs", EditorStyles.boldLabel);
		SerializedObject serializedObject = new SerializedObject(this);
		SerializedProperty prefabsProperty = serializedObject.FindProperty("foliagePrefabs");
		EditorGUILayout.PropertyField(prefabsProperty, true);
		serializedObject.ApplyModifiedProperties();

		EditorGUILayout.Space();
		EditorGUILayout.LabelField("Placement Options", EditorStyles.boldLabel);
		plantedParentName = EditorGUILayout.TextField("Parent Name", plantedParentName);
		clearBeforePlanting = EditorGUILayout.Toggle("Clear Existing Children", clearBeforePlanting);
		alignToSurfaceNormal = EditorGUILayout.Toggle("Align To Surface Normal", alignToSurfaceNormal);
		prefabUpAxis = (PrefabUpAxis)EditorGUILayout.EnumPopup("Prefab Upright Axis", prefabUpAxis);
		onlyUseUpwardFacingSurfaces = EditorGUILayout.Toggle("Only Upward Facing Surfaces", onlyUseUpwardFacingSurfaces);
		if (onlyUseUpwardFacingSurfaces)
		{
			minUpwardDot = EditorGUILayout.Slider("Min Upward Dot", minUpwardDot, 0f, 1f);
		}
		surfaceOffset = EditorGUILayout.FloatField("Surface Offset", surfaceOffset);
		randomizeYaw = EditorGUILayout.Toggle("Random Yaw", randomizeYaw);
		minScale = EditorGUILayout.FloatField("Min Scale", minScale);
		maxScale = EditorGUILayout.FloatField("Max Scale", maxScale);
		if (maxScale < minScale)
		{
			maxScale = minScale;
		}

		EditorGUILayout.Space();
		EditorGUILayout.LabelField("Manual Planting", EditorStyles.boldLabel);
		manualBrushRadius = EditorGUILayout.FloatField("Manual Brush Radius", manualBrushRadius);
		manualBrushRadius = Mathf.Max(0.01f, manualBrushRadius);
		manualDensityPerSquareMeter = EditorGUILayout.FloatField("Manual Density (objects/m^2)", manualDensityPerSquareMeter);
		manualDensityPerSquareMeter = Mathf.Max(0f, manualDensityPerSquareMeter);
		EditorGUILayout.HelpBox("Enable Manually Plant, then click on the target mesh in Scene view. Each click assigns a circular region and plants a random set of prefabs using manual density.", MessageType.None);

		randomSeed = EditorGUILayout.IntField("Random Seed", randomSeed);
		batchSizePerUpdate = EditorGUILayout.IntField("Batch Size Per Update", batchSizePerUpdate);
		batchSizePerUpdate = Mathf.Max(1, batchSizePerUpdate);

		if (isPlanting)
		{
			EditorGUILayout.Space();
			EditorGUILayout.HelpBox(stageText, MessageType.Info);
			EditorGUILayout.LabelField("Planted In Current Run", $"{plantedCountInRun} / {targetCountInRun}");
		}

		EditorGUILayout.Space();
		EditorGUILayout.BeginHorizontal();
		GUI.enabled = !isPlanting;
		if (GUILayout.Button("Plant Foliage"))
		{
			Plant();
		}
		if (GUILayout.Button(manualPlantMode ? "Stop Manual Planting" : "Manually Plant"))
		{
			ToggleManualPlantMode();
		}
		GUI.enabled = true;
		if (isPlanting)
		{
			if (GUILayout.Button("Cancel Planting"))
			{
				cancelRequested = true;
			}
		}
		if (GUILayout.Button("Clear Planted Foliage"))
		{
			ClearPlantedFoliage();
		}
		EditorGUILayout.EndHorizontal();

		if (manualPlantMode)
		{
			EditorGUILayout.HelpBox("Manual planting is active in Scene view. Left-click plants in a circular region, Shift+left-click removes planted foliage in that region.", MessageType.Info);
		}

		EditorGUILayout.EndScrollView();
	}

	private void ToggleManualPlantMode()
	{
		if (manualPlantMode)
		{
			manualPlantMode = false;
			manualClickArmed = false;
			manualRemoveClickArmed = false;
			SceneView.RepaintAll();
			return;
		}

		if (isPlanting)
		{
			EditorUtility.DisplayDialog("Plant Foliage", "Finish or cancel the active planting run before manual planting.", "OK");
			return;
		}

		if (!ValidateSetupForPlanting(showDialogs: true, out _, out _, out _, out _))
		{
			return;
		}

		manualPlantMode = true;
		manualClickArmed = false;
		manualRemoveClickArmed = false;
		SceneView.RepaintAll();
	}

	private void Plant()
	{
		if (isPlanting)
		{
			EditorUtility.DisplayDialog("Plant Foliage", "A planting run is already in progress.", "OK");
			return;
		}

		if (!ValidateSetupForPlanting(showDialogs: true, out List<GameObject> validPrefabs, out List<SurfaceTriangle> triangles, out float totalArea, out Vector3 upReference))
		{
			return;
		}

		int targetCount = countMode == PlantCountMode.Density
			? Mathf.RoundToInt(totalArea * densityPerSquareMeter)
			: fixedCount;
		targetCount = Mathf.Max(0, targetCount);

		if (targetCount > 50000)
		{
			bool proceed = EditorUtility.DisplayDialog(
				"Large Planting Run",
				$"This run will place about {targetCount} objects. Continue?",
				"Continue",
				"Cancel");
			if (!proceed)
			{
				return;
			}
		}

		if (targetCount == 0)
		{
			Debug.Log("Plant Foliage: Computed object count is 0. Nothing was planted.");
			return;
		}

		Transform plantedParent = GetOrCreatePlantedParent();
		if (plantedParent == null)
		{
			EditorUtility.DisplayDialog("Plant Foliage", "Could not create the Planted Foliage parent object.", "OK");
			return;
		}

		Undo.IncrementCurrentGroup();
		undoGroup = Undo.GetCurrentGroup();
		Undo.SetCurrentGroupName("Plant Foliage");

		isPlanting = true;
		cancelRequested = false;
		targetCountInRun = targetCount;
		plantedCountInRun = 0;
		totalAreaInRun = totalArea;
		plantedParentInRun = plantedParent;
		validPrefabsInRun = validPrefabs;
		trianglesInRun = triangles;
		upReferenceInRun = upReference;
		randomInRun = new System.Random(randomSeed);
		clearStartChildCount = plantedParent.childCount;
		runStartTime = EditorApplication.timeSinceStartup;

		if (clearBeforePlanting && plantedParent.childCount > 0)
		{
			runStage = PlantRunStage.ClearingExisting;
			stageText = $"Stage: Clearing existing foliage ({clearStartChildCount} children)...";
		}
		else
		{
			runStage = PlantRunStage.Spawning;
			stageText = $"Stage: Spawning foliage 0 / {targetCountInRun}";
		}

		Repaint();
	}

	private bool ValidateSetupForPlanting(
		bool showDialogs,
		out List<GameObject> validPrefabs,
		out List<SurfaceTriangle> triangles,
		out float totalArea,
		out Vector3 upReference)
	{
		validPrefabs = null;
		triangles = null;
		totalArea = 0f;
		upReference = Vector3.up;

		if (targetObject == null)
		{
			if (showDialogs)
			{
				EditorUtility.DisplayDialog("Plant Foliage", "Assign a target GameObject first.", "OK");
			}
			return false;
		}

		if (foliagePrefabs == null || foliagePrefabs.Length == 0)
		{
			if (showDialogs)
			{
				EditorUtility.DisplayDialog("Plant Foliage", "Add at least one foliage prefab.", "OK");
			}
			return false;
		}

		validPrefabs = new List<GameObject>();
		for (int i = 0; i < foliagePrefabs.Length; i++)
		{
			if (foliagePrefabs[i] != null)
			{
				validPrefabs.Add(foliagePrefabs[i]);
			}
		}

		if (validPrefabs.Count == 0)
		{
			if (showDialogs)
			{
				EditorUtility.DisplayDialog("Plant Foliage", "All foliage prefab slots are empty.", "OK");
			}
			return false;
		}

		upReference = targetObject.transform.up.sqrMagnitude > 1e-6f ? targetObject.transform.up.normalized : Vector3.up;
		if (!TryBuildSurfaceData(targetObject, upReference, onlyUseUpwardFacingSurfaces, minUpwardDot, out triangles, out totalArea))
		{
			if (showDialogs)
			{
				EditorUtility.DisplayDialog("Plant Foliage", "No valid MeshFilter triangles found on the target or its children.", "OK");
			}
			return false;
		}

		return true;
	}

	private void OnSceneGUI(SceneView sceneView)
	{
		if (!manualPlantMode || isPlanting)
		{
			return;
		}

		Event currentEvent = Event.current;
		if (currentEvent == null)
		{
			return;
		}

		if (!ValidateSetupForPlanting(showDialogs: false, out List<GameObject> validPrefabs, out List<SurfaceTriangle> triangles, out _, out Vector3 upReference))
		{
			Handles.BeginGUI();
			GUI.Label(new Rect(12f, 12f, 380f, 24f), "Plant Foliage: Assign target and prefabs for manual planting.", EditorStyles.helpBox);
			Handles.EndGUI();
			return;
		}

		bool hasHit = TryRaycastTargetSurface(currentEvent.mousePosition, out Vector3 hitPoint, out Vector3 hitNormal);

		if (hasHit && currentEvent.type == EventType.Repaint)
		{
			DrawManualBrush(hitPoint, hitNormal, manualBrushRadius);
		}

		if (!currentEvent.alt)
		{
			HandleUtility.AddDefaultControl(GUIUtility.GetControlID(FocusType.Passive));
		}

		if (currentEvent.type == EventType.MouseDown && currentEvent.button == 0 && !currentEvent.alt && !currentEvent.shift)
		{
			manualClickArmed = hasHit;
			currentEvent.Use();
		}

		if (currentEvent.type == EventType.MouseDown && currentEvent.button == 0 && !currentEvent.alt && currentEvent.shift)
		{
			manualRemoveClickArmed = hasHit;
			currentEvent.Use();
		}

		if (currentEvent.type == EventType.MouseUp && currentEvent.button == 0 && !currentEvent.alt && !currentEvent.shift)
		{
			if (manualClickArmed && hasHit)
			{
				int desiredCount = ComputeManualPlantCount(manualBrushRadius, manualDensityPerSquareMeter);
				int planted = PlantInBrushRegion(hitPoint, manualBrushRadius, desiredCount, validPrefabs, triangles, upReference);
				Debug.Log($"Plant Foliage: Manual planting placed {planted} objects in a radius {manualBrushRadius:F2}m region (target count {desiredCount}).");
			}
			manualClickArmed = false;
			currentEvent.Use();
		}

		if (currentEvent.type == EventType.MouseUp && currentEvent.button == 0 && !currentEvent.alt && currentEvent.shift)
		{
			if (manualRemoveClickArmed && hasHit)
			{
				int removed = RemoveInBrushRegion(hitPoint, manualBrushRadius);
				Debug.Log($"Plant Foliage: Manual remove deleted {removed} objects in a radius {manualBrushRadius:F2}m region.");
			}
			manualRemoveClickArmed = false;
			currentEvent.Use();
		}

		if (currentEvent.type == EventType.MouseMove)
		{
			sceneView.Repaint();
		}
	}

	private static void DrawManualBrush(Vector3 center, Vector3 normal, float radius)
	{
		Color fillColor = new Color(0.2f, 0.8f, 0.35f, 0.14f);
		Color wireColor = new Color(0.2f, 0.95f, 0.35f, 0.95f);
		Handles.color = fillColor;
		Handles.DrawSolidDisc(center, normal, radius);
		Handles.color = wireColor;
		Handles.DrawWireDisc(center, normal, radius);
		Handles.color = Color.white;
	}

	private bool TryRaycastTargetSurface(Vector2 mousePosition, out Vector3 closestPoint, out Vector3 closestNormal)
	{
		closestPoint = default;
		closestNormal = Vector3.up;
		if (targetObject == null)
		{
			return false;
		}

		Ray mouseRay = HandleUtility.GUIPointToWorldRay(mousePosition);
		if (TryRaycastTargetSurfaceWithColliders(mouseRay, out closestPoint, out closestNormal))
		{
			Vector3 colliderUpReference = targetObject.transform.up.sqrMagnitude > 1e-6f ? targetObject.transform.up.normalized : Vector3.up;
			if (Vector3.Dot(closestNormal, colliderUpReference) < 0f)
			{
				closestNormal = -closestNormal;
			}
			return true;
		}

		MeshFilter[] meshFilters = targetObject.GetComponentsInChildren<MeshFilter>(true);
		float bestDistance = float.PositiveInfinity;

		for (int i = 0; i < meshFilters.Length; i++)
		{
			MeshFilter filter = meshFilters[i];
			if (filter == null || filter.sharedMesh == null)
			{
				continue;
			}

			if (!TryIntersectRayWithMeshFilter(mouseRay, filter, out float hitDistance, out Vector3 hitPoint, out Vector3 hitNormal))
			{
				continue;
			}

			if (hitDistance < bestDistance)
			{
				bestDistance = hitDistance;
				closestPoint = hitPoint;
				closestNormal = hitNormal;
			}
		}

		if (!float.IsFinite(bestDistance))
		{
			return false;
		}

		Vector3 upReference = targetObject.transform.up.sqrMagnitude > 1e-6f ? targetObject.transform.up.normalized : Vector3.up;
		if (Vector3.Dot(closestNormal, upReference) < 0f)
		{
			closestNormal = -closestNormal;
		}
		return true;
	}

	private bool TryRaycastTargetSurfaceWithColliders(Ray ray, out Vector3 closestPoint, out Vector3 closestNormal)
	{
		closestPoint = default;
		closestNormal = Vector3.up;

		Collider[] colliders = targetObject.GetComponentsInChildren<Collider>(true);
		if (colliders == null || colliders.Length == 0)
		{
			return false;
		}

		float bestDistance = float.PositiveInfinity;
		bool foundHit = false;
		for (int i = 0; i < colliders.Length; i++)
		{
			Collider collider = colliders[i];
			if (collider == null)
			{
				continue;
			}

			if (!collider.Raycast(ray, out RaycastHit hit, float.PositiveInfinity))
			{
				continue;
			}

			if (hit.distance < bestDistance)
			{
				bestDistance = hit.distance;
				closestPoint = hit.point;
				closestNormal = hit.normal.sqrMagnitude > 1e-8f ? hit.normal.normalized : Vector3.up;
				foundHit = true;
			}
		}

		return foundHit;
	}

	private static bool TryIntersectRayWithMeshFilter(Ray ray, MeshFilter filter, out float bestDistance, out Vector3 bestPoint, out Vector3 bestNormal)
	{
		bestDistance = float.PositiveInfinity;
		bestPoint = default;
		bestNormal = Vector3.up;

		Mesh mesh = filter.sharedMesh;
		if (mesh == null)
		{
			return false;
		}

		Vector3[] vertices = mesh.vertices;
		int[] indices = mesh.triangles;
		Transform tf = filter.transform;

		for (int triIndex = 0; triIndex < indices.Length; triIndex += 3)
		{
			Vector3 a = tf.TransformPoint(vertices[indices[triIndex]]);
			Vector3 b = tf.TransformPoint(vertices[indices[triIndex + 1]]);
			Vector3 c = tf.TransformPoint(vertices[indices[triIndex + 2]]);
			if (!TryIntersectRayTriangle(ray, a, b, c, out float distance, out Vector3 point, out Vector3 normal))
			{
				continue;
			}

			if (distance < bestDistance)
			{
				bestDistance = distance;
				bestPoint = point;
				bestNormal = normal;
			}
		}

		return float.IsFinite(bestDistance);
	}

	private static bool TryIntersectRayTriangle(Ray ray, Vector3 a, Vector3 b, Vector3 c, out float distance, out Vector3 point, out Vector3 normal)
	{
		distance = 0f;
		point = default;
		normal = Vector3.up;

		Vector3 edge1 = b - a;
		Vector3 edge2 = c - a;
		Vector3 pVec = Vector3.Cross(ray.direction, edge2);
		float det = Vector3.Dot(edge1, pVec);
		if (Mathf.Abs(det) <= 1e-8f)
		{
			return false;
		}

		float invDet = 1f / det;
		Vector3 tVec = ray.origin - a;
		float u = Vector3.Dot(tVec, pVec) * invDet;
		if (u < 0f || u > 1f)
		{
			return false;
		}

		Vector3 qVec = Vector3.Cross(tVec, edge1);
		float v = Vector3.Dot(ray.direction, qVec) * invDet;
		if (v < 0f || u + v > 1f)
		{
			return false;
		}

		float t = Vector3.Dot(edge2, qVec) * invDet;
		if (t < 0f)
		{
			return false;
		}

		distance = t;
		point = ray.origin + ray.direction * t;
		Vector3 triNormal = Vector3.Cross(edge1, edge2);
		normal = triNormal.sqrMagnitude > 1e-8f ? triNormal.normalized : Vector3.up;
		return true;
	}

	private int PlantInBrushRegion(
		Vector3 center,
		float radius,
		int desiredCount,
		List<GameObject> validPrefabs,
		List<SurfaceTriangle> sourceTriangles,
		Vector3 upReference)
	{
		if (desiredCount <= 0)
		{
			return 0;
		}

		Transform plantedParent = GetOrCreatePlantedParent();
		if (plantedParent == null)
		{
			EditorUtility.DisplayDialog("Plant Foliage", "Could not create the Planted Foliage parent object.", "OK");
			return 0;
		}

		if (!TryBuildBrushSurfaceData(sourceTriangles, center, radius, out List<SurfaceTriangle> brushTriangles, out float brushArea))
		{
			return 0;
		}

		Undo.IncrementCurrentGroup();
		int clickUndoGroup = Undo.GetCurrentGroup();
		Undo.SetCurrentGroupName("Plant Foliage (Manual)");

		System.Random random = new System.Random(unchecked(randomSeed + manualClickIndex));
		manualClickIndex++;

		int plantedCount = 0;
		int attempts = 0;
		int maxAttempts = Mathf.Max(desiredCount * 25, 100);
		float radiusSqr = radius * radius;

		while (plantedCount < desiredCount && attempts < maxAttempts)
		{
			attempts++;
			SurfaceTriangle tri = SelectTriangleByArea(brushTriangles, brushArea, random);
			SamplePointAndNormalOnTriangle(tri, random, upReference, out Vector3 point, out Vector3 sampledNormal);
			if ((point - center).sqrMagnitude > radiusSqr)
			{
				continue;
			}

			Quaternion rotation = CalculateRotation(sampledNormal, alignToSurfaceNormal, randomizeYaw, random, AxisToVector(prefabUpAxis));
			GameObject prefab = validPrefabs[random.Next(validPrefabs.Count)];

			GameObject instance = PrefabUtility.InstantiatePrefab(prefab, plantedParent) as GameObject;
			if (instance == null)
			{
				instance = Instantiate(prefab, plantedParent);
			}

			Undo.RegisterCreatedObjectUndo(instance, "Plant Foliage Instance");
			instance.name = prefab.name;
			instance.transform.position = point + sampledNormal * surfaceOffset;
			instance.transform.rotation = rotation * instance.transform.rotation;
			float scale = Mathf.Lerp(minScale, maxScale, (float)random.NextDouble());
			instance.transform.localScale = Vector3.one * scale;
			plantedCount++;
		}

		Undo.CollapseUndoOperations(clickUndoGroup);
		EditorUtility.SetDirty(plantedParent.gameObject);
		return plantedCount;
	}

	private int RemoveInBrushRegion(Vector3 center, float radius)
	{
		Transform plantedParent = FindRootLevelPlantedParent();
		if (plantedParent == null)
		{
			return 0;
		}

		float radiusSqr = radius * radius;
		List<GameObject> toRemove = new List<GameObject>();
		for (int i = 0; i < plantedParent.childCount; i++)
		{
			Transform child = plantedParent.GetChild(i);
			if ((child.position - center).sqrMagnitude <= radiusSqr)
			{
				toRemove.Add(child.gameObject);
			}
		}

		if (toRemove.Count == 0)
		{
			return 0;
		}

		Undo.IncrementCurrentGroup();
		int removeUndoGroup = Undo.GetCurrentGroup();
		Undo.SetCurrentGroupName("Plant Foliage (Manual Remove)");

		for (int i = 0; i < toRemove.Count; i++)
		{
			Undo.DestroyObjectImmediate(toRemove[i]);
		}

		Undo.CollapseUndoOperations(removeUndoGroup);
		EditorUtility.SetDirty(plantedParent.gameObject);
		return toRemove.Count;
	}

	private static int ComputeManualPlantCount(float radius, float densityPerSquareMeter)
	{
		float area = Mathf.PI * radius * radius;
		return Mathf.Max(0, Mathf.RoundToInt(area * Mathf.Max(0f, densityPerSquareMeter)));
	}

	private void OnEditorUpdate()
	{
		if (!isPlanting)
		{
			return;
		}

		if (cancelRequested)
		{
			CancelRun("Cancelled from window button.");
			return;
		}

		switch (runStage)
		{
			case PlantRunStage.ClearingExisting:
				ProcessClearBatch();
				break;
			case PlantRunStage.Spawning:
				ProcessSpawnBatch();
				break;
			case PlantRunStage.Finalizing:
				FinalizeRun();
				break;
		}

		Repaint();
	}

	private void ProcessClearBatch()
	{
		int ops = Mathf.Max(1, batchSizePerUpdate);
		for (int i = 0; i < ops && plantedParentInRun.childCount > 0; i++)
		{
			Undo.DestroyObjectImmediate(plantedParentInRun.GetChild(plantedParentInRun.childCount - 1).gameObject);
		}

		int cleared = clearStartChildCount - plantedParentInRun.childCount;
		float clearProgress = clearStartChildCount > 0 ? cleared / (float)clearStartChildCount : 1f;
		stageText = $"Stage: Clearing existing foliage {cleared} / {clearStartChildCount}";

		if (ShowProgress("Clearing Existing", stageText, 0.05f + 0.35f * clearProgress))
		{
			CancelRun("Cancelled while clearing existing foliage.");
			return;
		}

		if (plantedParentInRun.childCount == 0)
		{
			runStage = PlantRunStage.Spawning;
			stageText = $"Stage: Spawning foliage 0 / {targetCountInRun}";
		}
	}

	private void ProcessSpawnBatch()
	{
		int ops = Mathf.Max(1, batchSizePerUpdate);
		int remaining = targetCountInRun - plantedCountInRun;
		int toSpawn = Mathf.Min(ops, remaining);

		for (int i = 0; i < toSpawn; i++)
		{
			SurfaceTriangle tri = SelectTriangleByArea(trianglesInRun, totalAreaInRun, randomInRun);
			SamplePointAndNormalOnTriangle(tri, randomInRun, upReferenceInRun, out Vector3 point, out Vector3 sampledNormal);
			Quaternion rotation = CalculateRotation(sampledNormal, alignToSurfaceNormal, randomizeYaw, randomInRun, AxisToVector(prefabUpAxis));
			GameObject prefab = validPrefabsInRun[randomInRun.Next(validPrefabsInRun.Count)];

			GameObject instance = PrefabUtility.InstantiatePrefab(prefab, plantedParentInRun) as GameObject;
			if (instance == null)
			{
				instance = Instantiate(prefab, plantedParentInRun);
			}

			Undo.RegisterCreatedObjectUndo(instance, "Plant Foliage Instance");
			instance.name = prefab.name;
			instance.transform.position = point + sampledNormal * surfaceOffset;
			instance.transform.rotation = rotation * instance.transform.rotation;
			float scale = Mathf.Lerp(minScale, maxScale, (float)randomInRun.NextDouble());
			instance.transform.localScale = Vector3.one * scale;
			plantedCountInRun++;
		}

		float spawnProgress = targetCountInRun > 0 ? plantedCountInRun / (float)targetCountInRun : 1f;
		stageText = $"Stage: Spawning foliage {plantedCountInRun} / {targetCountInRun}";
		if (ShowProgress("Spawning Foliage", stageText, 0.4f + 0.58f * spawnProgress))
		{
			CancelRun("Cancelled while spawning foliage.");
			return;
		}

		if (plantedCountInRun >= targetCountInRun)
		{
			runStage = PlantRunStage.Finalizing;
		}
	}

	private bool ShowProgress(string stageTitle, string details, float progress)
	{
		bool cancelFromProgress = EditorUtility.DisplayCancelableProgressBar(
			"Plant Foliage",
			$"{stageTitle}\n{details}",
			Mathf.Clamp01(progress));
		if (cancelFromProgress)
		{
			cancelRequested = true;
		}
		return cancelFromProgress;
	}

	private void FinalizeRun()
	{
		EditorUtility.ClearProgressBar();
		if (undoGroup >= 0)
		{
			Undo.CollapseUndoOperations(undoGroup);
		}

		if (plantedParentInRun != null)
		{
			EditorUtility.SetDirty(plantedParentInRun.gameObject);
		}

		double elapsed = EditorApplication.timeSinceStartup - runStartTime;
		Debug.Log($"Plant Foliage: Planted {plantedCountInRun} objects under '{plantedParentInRun.name}' in {elapsed:F2}s.");
		ResetRunState();
	}

	private void CancelRun(string reason)
	{
		EditorUtility.ClearProgressBar();
		if (undoGroup >= 0)
		{
			Undo.CollapseUndoOperations(undoGroup);
		}

		double elapsed = EditorApplication.timeSinceStartup - runStartTime;
		Debug.LogWarning($"Plant Foliage: {reason} Created {plantedCountInRun} / {targetCountInRun} objects in {elapsed:F2}s.");
		ResetRunState();
	}

	private void ResetRunState()
	{
		runStage = PlantRunStage.Idle;
		isPlanting = false;
		cancelRequested = false;
		undoGroup = -1;
		targetCountInRun = 0;
		plantedCountInRun = 0;
		clearStartChildCount = 0;
		totalAreaInRun = 0f;
		stageText = "Idle";
		upReferenceInRun = Vector3.up;
		plantedParentInRun = null;
		validPrefabsInRun = null;
		trianglesInRun = null;
		randomInRun = null;
		runStartTime = 0d;
	}

	private void ClearPlantedFoliage()
	{
		if (targetObject == null)
		{
			EditorUtility.DisplayDialog("Plant Foliage", "Assign a target GameObject first.", "OK");
			return;
		}

		Transform plantedParent = FindRootLevelPlantedParent();
		if (plantedParent == null)
		{
			Debug.Log("Plant Foliage: No planted parent found to clear.");
			return;
		}

		ClearChildren(plantedParent);
		EditorUtility.SetDirty(plantedParent.gameObject);
	}

	private Transform GetOrCreatePlantedParent()
	{
		if (string.IsNullOrWhiteSpace(plantedParentName))
		{
			plantedParentName = "Planted Foliage";
		}

		Transform parent = FindRootLevelPlantedParent();
		if (parent != null)
		{
			return parent;
		}

		GameObject go = new GameObject(plantedParentName);
		Undo.RegisterCreatedObjectUndo(go, "Create Planted Foliage Parent");
		go.transform.SetParent(null, false);
		if (targetObject.scene.IsValid())
		{
			UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(go, targetObject.scene);
		}
		return go.transform;
	}

	private Transform FindRootLevelPlantedParent()
	{
		if (targetObject == null || !targetObject.scene.IsValid())
		{
			return null;
		}

		GameObject[] roots = targetObject.scene.GetRootGameObjects();
		for (int i = 0; i < roots.Length; i++)
		{
			if (roots[i] != null && roots[i].name == plantedParentName)
			{
				return roots[i].transform;
			}
		}

		return null;
	}

	private static void ClearChildren(Transform parent)
	{
		for (int i = parent.childCount - 1; i >= 0; i--)
		{
			Undo.DestroyObjectImmediate(parent.GetChild(i).gameObject);
		}
	}

	private static Quaternion CalculateRotation(Vector3 normal, bool alignToNormal, bool addRandomYaw, System.Random random, Vector3 prefabUprightAxis)
	{
		Quaternion rotation = Quaternion.identity;
		Vector3 safeNormal = normal.sqrMagnitude > 1e-6f ? normal.normalized : Vector3.up;
		Vector3 safePrefabAxis = prefabUprightAxis.sqrMagnitude > 1e-6f ? prefabUprightAxis.normalized : Vector3.up;

		if (alignToNormal && safeNormal.sqrMagnitude > 1e-6f)
		{
			rotation = Quaternion.FromToRotation(safePrefabAxis, safeNormal);
		}

		if (addRandomYaw)
		{
			Vector3 yawAxis = alignToNormal ? safeNormal : Vector3.up;
			rotation = Quaternion.AngleAxis((float)(random.NextDouble() * 360.0), yawAxis) * rotation;
		}

		return rotation;
	}

	private static Vector3 AxisToVector(PrefabUpAxis axis)
	{
		switch (axis)
		{
			case PrefabUpAxis.X:
				return Vector3.right;
			case PrefabUpAxis.Y:
				return Vector3.up;
			case PrefabUpAxis.Z:
				return Vector3.forward;
			case PrefabUpAxis.NegativeX:
				return Vector3.left;
			case PrefabUpAxis.NegativeY:
				return Vector3.down;
			case PrefabUpAxis.NegativeZ:
				return Vector3.back;
			default:
				return Vector3.up;
		}
	}

	private static bool TryBuildBrushSurfaceData(
		List<SurfaceTriangle> sourceTriangles,
		Vector3 center,
		float radius,
		out List<SurfaceTriangle> brushTriangles,
		out float brushArea)
	{
		brushTriangles = new List<SurfaceTriangle>();
		brushArea = 0f;
		float radiusSqr = radius * radius;

		for (int i = 0; i < sourceTriangles.Count; i++)
		{
			SurfaceTriangle tri = sourceTriangles[i];
			Vector3 closest = ClosestPointOnTriangle(center, tri.a, tri.b, tri.c);
			if ((closest - center).sqrMagnitude > radiusSqr)
			{
				continue;
			}

			float area = GetTriangleArea(tri.a, tri.b, tri.c);
			if (area <= 1e-8f)
			{
				continue;
			}

			brushArea += area;
			tri.cumulativeArea = brushArea;
			brushTriangles.Add(tri);
		}

		return brushTriangles.Count > 0 && brushArea > 0f;
	}

	private static float GetTriangleArea(Vector3 a, Vector3 b, Vector3 c)
	{
		return 0.5f * Vector3.Cross(b - a, c - a).magnitude;
	}

	private static Vector3 ClosestPointOnTriangle(Vector3 point, Vector3 a, Vector3 b, Vector3 c)
	{
		Vector3 ab = b - a;
		Vector3 ac = c - a;
		Vector3 ap = point - a;
		float d1 = Vector3.Dot(ab, ap);
		float d2 = Vector3.Dot(ac, ap);
		if (d1 <= 0f && d2 <= 0f)
		{
			return a;
		}

		Vector3 bp = point - b;
		float d3 = Vector3.Dot(ab, bp);
		float d4 = Vector3.Dot(ac, bp);
		if (d3 >= 0f && d4 <= d3)
		{
			return b;
		}

		float vc = d1 * d4 - d3 * d2;
		if (vc <= 0f && d1 >= 0f && d3 <= 0f)
		{
			float v = d1 / (d1 - d3);
			return a + v * ab;
		}

		Vector3 cp = point - c;
		float d5 = Vector3.Dot(ab, cp);
		float d6 = Vector3.Dot(ac, cp);
		if (d6 >= 0f && d5 <= d6)
		{
			return c;
		}

		float vb = d5 * d2 - d1 * d6;
		if (vb <= 0f && d2 >= 0f && d6 <= 0f)
		{
			float w = d2 / (d2 - d6);
			return a + w * ac;
		}

		float va = d3 * d6 - d5 * d4;
		if (va <= 0f && (d4 - d3) >= 0f && (d5 - d6) >= 0f)
		{
			float w = (d4 - d3) / ((d4 - d3) + (d5 - d6));
			return b + w * (c - b);
		}

		float denom = 1f / (va + vb + vc);
		float v2 = vb * denom;
		float w2 = vc * denom;
		return a + ab * v2 + ac * w2;
	}

	private static SurfaceTriangle SelectTriangleByArea(List<SurfaceTriangle> triangles, float totalArea, System.Random random)
	{
		float sample = (float)random.NextDouble() * totalArea;
		int low = 0;
		int high = triangles.Count - 1;
		while (low < high)
		{
			int mid = (low + high) >> 1;
			if (sample <= triangles[mid].cumulativeArea) high = mid;
			else low = mid + 1;
		}
		return triangles[low];
	}

	private static void SamplePointAndNormalOnTriangle(SurfaceTriangle triangle, System.Random random, Vector3 upReference, out Vector3 point, out Vector3 normal)
	{
		float u = (float)random.NextDouble();
		float v = (float)random.NextDouble();
		float sqrtU = Mathf.Sqrt(u);
		float b0 = 1f - sqrtU;
		float b1 = sqrtU * (1f - v);
		float b2 = sqrtU * v;

		point = triangle.a * b0 + triangle.b * b1 + triangle.c * b2;
		normal = triangle.na * b0 + triangle.nb * b1 + triangle.nc * b2;
		if (normal.sqrMagnitude <= 1e-8f)
		{
			normal = triangle.normal;
		}
		else
		{
			normal.Normalize();
			if (Vector3.Dot(normal, triangle.normal) < 0f)
			{
				normal = -normal;
			}
		}

		if (Vector3.Dot(normal, upReference) < 0f)
		{
			normal = -normal;
		}
	}

	private static bool TryBuildSurfaceData(
		GameObject root,
		Vector3 upReference,
		bool onlyUseUpwardFacingSurfaces,
		float minUpwardDot,
		out List<SurfaceTriangle> triangles,
		out float totalArea)
	{
		triangles = new List<SurfaceTriangle>();
		totalArea = 0f;

		MeshFilter[] meshFilters = root.GetComponentsInChildren<MeshFilter>(true);
		for (int i = 0; i < meshFilters.Length; i++)
		{
			MeshFilter filter = meshFilters[i];
			if (filter == null || filter.sharedMesh == null)
			{
				continue;
			}

			Mesh mesh = filter.sharedMesh;
			Vector3[] vertices = mesh.vertices;
			Vector3[] meshNormals = mesh.normals;
			bool hasMeshNormals = meshNormals != null && meshNormals.Length == vertices.Length;
			int[] indices = mesh.triangles;
			Transform tf = filter.transform;

			for (int triIndex = 0; triIndex < indices.Length; triIndex += 3)
			{
				Vector3 a = tf.TransformPoint(vertices[indices[triIndex]]);
				Vector3 b = tf.TransformPoint(vertices[indices[triIndex + 1]]);
				Vector3 c = tf.TransformPoint(vertices[indices[triIndex + 2]]);
				Vector3 cross = Vector3.Cross(b - a, c - a);
				Vector3 faceNormal = cross.sqrMagnitude > 1e-8f ? cross.normalized : Vector3.up;
				// Use absolute dot so inverted triangle winding does not cull valid upward surfaces.
				if (onlyUseUpwardFacingSurfaces && Mathf.Abs(Vector3.Dot(faceNormal, upReference)) < minUpwardDot)
				{
					continue;
				}
				float area = 0.5f * cross.magnitude;
				if (area <= 1e-8f)
				{
					continue;
				}

				int ia = indices[triIndex];
				int ib = indices[triIndex + 1];
				int ic = indices[triIndex + 2];
				Vector3 na = hasMeshNormals ? tf.TransformDirection(meshNormals[ia]).normalized : faceNormal;
				Vector3 nb = hasMeshNormals ? tf.TransformDirection(meshNormals[ib]).normalized : faceNormal;
				Vector3 nc = hasMeshNormals ? tf.TransformDirection(meshNormals[ic]).normalized : faceNormal;

				totalArea += area;
				SurfaceTriangle triangle = new SurfaceTriangle
				{
					a = a,
					b = b,
					c = c,
					na = na,
					nb = nb,
					nc = nc,
					normal = faceNormal,
					cumulativeArea = totalArea
				};
				triangles.Add(triangle);
			}
		}

		return triangles.Count > 0 && totalArea > 0f;
	}
}
