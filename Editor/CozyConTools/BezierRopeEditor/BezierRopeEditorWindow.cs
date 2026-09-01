// BezierRopeEditorWindow.cs
// Editor window for BezierRope with Create/Edit/None modes, scene handles (axes + sphere + anchors),
// Shift+Click insert, Insert/Delete buttons, and Export Mesh functionality.

using UnityEngine;
using UnityEditor;
using System.Collections.Generic;

public class BezierRopeEditorWindow : EditorWindow
{
	// Use SerializedObject for the selected BezierRope to get proper Undo/Redo and inspector-like behavior.
	SerializedObject serializedRope;
	SerializedProperty pointsProp;
	SerializedProperty handleAProp;
	SerializedProperty handleBProp;

	BezierRope targetRope;
	Vector2 scroll;

	enum ToolMode { None, Create, Edit }
	ToolMode mode = ToolMode.None;

	int selectedPoint = -1;
	int selectedHandle = -1; // -1 none, 0 handleA, 1 handleB

	Color pointColor = new Color(1f, 0.9f, 0.2f);
	Color handleColor = new Color(0.2f, 0.9f, 1f);
	Color curveColor = Color.green;

	[MenuItem("Lilithe/Bezier Rope Editor")]
	public static void ShowWindow()
	{
		GetWindow<BezierRopeEditorWindow>("Bezier Rope Editor");
	}

	void OnEnable()
	{
		SceneView.duringSceneGui += OnSceneGUI;
		Selection.selectionChanged += OnSelectionChanged;
		UpdateSerializedTarget();
	}

	void OnDisable()
	{
		SceneView.duringSceneGui -= OnSceneGUI;
		Selection.selectionChanged -= OnSelectionChanged;
	}

	void OnSelectionChanged()
	{
		// If user selects a GameObject that has a BezierRope, auto-assign it.
		if (Selection.activeGameObject != null)
		{
			BezierRope br = Selection.activeGameObject.GetComponent<BezierRope>();
			if (br != null)
			{
				SetTarget(br);
				Repaint();
				return;
			}
		}
	}

	void UpdateSerializedTarget()
	{
		if (targetRope != null)
		{
			serializedRope = new SerializedObject(targetRope);
			pointsProp = serializedRope.FindProperty("points");
			handleAProp = serializedRope.FindProperty("handleA");
			handleBProp = serializedRope.FindProperty("handleB");
		}
		else
		{
			serializedRope = null;
			pointsProp = null;
			handleAProp = null;
			handleBProp = null;
		}
	}

	void SetTarget(BezierRope rope)
	{
		targetRope = rope;
		UpdateSerializedTarget();
		if (targetRope != null)
		{
			Selection.activeGameObject = targetRope.gameObject;
			EditorGUIUtility.PingObject(targetRope.gameObject);
		}
	}

	void OnGUI()
	{
		scroll = EditorGUILayout.BeginScrollView(scroll);

		EditorGUILayout.LabelField("Bezier Rope / Vine Editor", EditorStyles.boldLabel);
		EditorGUILayout.Space();

		// Object field accepts GameObject or BezierRope directly
		EditorGUI.BeginChangeCheck();
		Object newObj = EditorGUILayout.ObjectField("Target BezierRope", targetRope != null ? (Object)targetRope : null, typeof(BezierRope), true);
		if (EditorGUI.EndChangeCheck())
		{
			SetTarget(newObj as BezierRope);
		}

		EditorGUILayout.BeginHorizontal();
		if (GUILayout.Button("Create New BezierRope GameObject"))
		{
			GameObject go = new GameObject("BezierRope");
			BezierRope br = go.AddComponent<BezierRope>();
			SetTarget(br);
		}
		if (GUILayout.Button("Use Selected"))
		{
			if (Selection.activeGameObject != null)
			{
				BezierRope br = Selection.activeGameObject.GetComponent<BezierRope>();
				if (br != null) SetTarget(br);
				else EditorUtility.DisplayDialog("No BezierRope", "Selected GameObject does not have a BezierRope component.", "OK");
			}
			else EditorUtility.DisplayDialog("No Selection", "Select a GameObject in the Hierarchy first.", "OK");
		}
		EditorGUILayout.EndHorizontal();

		if (targetRope == null)
		{
			EditorGUILayout.HelpBox("Create or assign a BezierRope to begin editing.", MessageType.Info);
			EditorGUILayout.EndScrollView();
			return;
		}

		// Use SerializedObject for editing core properties
		serializedRope.Update();

		EditorGUILayout.Space();
		EditorGUILayout.BeginHorizontal();
		if (GUILayout.Toggle(mode == ToolMode.Create, "Create Mode", "Button")) mode = ToolMode.Create;
		if (GUILayout.Toggle(mode == ToolMode.Edit, "Edit Mode", "Button")) mode = ToolMode.Edit;
		if (GUILayout.Toggle(mode == ToolMode.None, "None", "Button")) mode = ToolMode.None;
		EditorGUILayout.EndHorizontal();

		EditorGUILayout.Space();
		EditorGUILayout.PropertyField(serializedRope.FindProperty("autoRebuild"), new GUIContent("Auto Rebuild Mesh"));
		EditorGUILayout.PropertyField(serializedRope.FindProperty("cylinderMaterial"), new GUIContent("Cylinder Material"));
		EditorGUILayout.PropertyField(serializedRope.FindProperty("radius"), new GUIContent("Radius"));
		EditorGUILayout.IntSlider(serializedRope.FindProperty("radialSegments"), 3, 64, new GUIContent("Radial Segments"));
		EditorGUILayout.IntSlider(serializedRope.FindProperty("lengthSegmentsPerCurve"), 1, 64, new GUIContent("Length Segments Per Curve"));
		EditorGUILayout.PropertyField(serializedRope.FindProperty("closed"), new GUIContent("Closed"));
		EditorGUILayout.PropertyField(serializedRope.FindProperty("capEnds"), new GUIContent("Cap Ends"));
		EditorGUILayout.Slider(serializedRope.FindProperty("autoSmoothFactor"), 0f, 2f, new GUIContent("Auto Smooth Factor"));

		EditorGUILayout.Space();
		serializedRope.ApplyModifiedProperties();

		EditorGUILayout.LabelField($"Points: {targetRope.points.Count}");

		// point list with quick select and local editing
		for (int i = 0; i < targetRope.points.Count; i++)
		{
			EditorGUILayout.BeginHorizontal();
			if (GUILayout.Toggle(selectedPoint == i, "P" + i, "Button", GUILayout.Width(50))) selectedPoint = i;
			Vector3 local = targetRope.points[i];
			Vector3 newLocal = EditorGUILayout.Vector3Field("", local);
			if (newLocal != local)
			{
				Undo.RecordObject(targetRope, "Edit Point");
				targetRope.points[i] = newLocal;
				targetRope.EnsureHandles();
				if (targetRope.autoRebuild) targetRope.BuildMesh();
				EditorUtility.SetDirty(targetRope);
			}
			EditorGUILayout.EndHorizontal();
		}

		EditorGUILayout.Space();
		EditorGUILayout.BeginHorizontal();
		if (GUILayout.Button("Insert After Selected"))
		{
			if (selectedPoint >= 0 && selectedPoint < targetRope.points.Count)
			{
				Vector3 world = targetRope.GetPointWorld(selectedPoint);
				targetRope.InsertPoint(selectedPoint + 1, world + targetRope.transform.right * 0.1f);
				EditorUtility.SetDirty(targetRope);
			}
			else EditorUtility.DisplayDialog("No Selection", "Select a point first.", "OK");
		}
		if (GUILayout.Button("Delete Selected"))
		{
			if (selectedPoint >= 0 && selectedPoint < targetRope.points.Count)
			{
				targetRope.RemovePoint(selectedPoint);
				selectedPoint = -1;
				EditorUtility.SetDirty(targetRope);
			}
			else EditorUtility.DisplayDialog("No Selection", "Select a point first.", "OK");
		}
		if (GUILayout.Button("Rebuild Mesh Now"))
		{
			Undo.RecordObject(targetRope, "Rebuild Mesh");
			targetRope.BuildMesh();
			EditorUtility.SetDirty(targetRope);
		}
		if (GUILayout.Button("Auto Smooth"))
		{
			Undo.RecordObject(targetRope, "Auto Smooth Rope");
			targetRope.AutoSmoothHandles();
			EditorUtility.SetDirty(targetRope);
		}
		EditorGUILayout.EndHorizontal();

		EditorGUILayout.Space();
		EditorGUILayout.LabelField("Leaves", EditorStyles.boldLabel);
		serializedRope.Update();
		EditorGUILayout.PropertyField(serializedRope.FindProperty("leafPrefab"), new GUIContent("Leaf Prefab (optional)"));
		EditorGUILayout.PropertyField(serializedRope.FindProperty("leafCount"), new GUIContent("Leaf Count"));
		EditorGUILayout.PropertyField(serializedRope.FindProperty("leafMinScale"), new GUIContent("Leaf Min Scale"));
		EditorGUILayout.PropertyField(serializedRope.FindProperty("leafMaxScale"), new GUIContent("Leaf Max Scale"));
		EditorGUILayout.PropertyField(serializedRope.FindProperty("leafRandomRotation"), new GUIContent("Leaf Random Rotation"));
		EditorGUILayout.PropertyField(serializedRope.FindProperty("leafOffset"), new GUIContent("Leaf Offset"));
		serializedRope.ApplyModifiedProperties();

		EditorGUILayout.BeginHorizontal();
		if (GUILayout.Button("Spawn Leaves Now"))
		{
			Undo.RecordObject(targetRope, "Spawn Leaves");
			targetRope.SpawnLeavesWithProgress();
			EditorUtility.SetDirty(targetRope);
		}
		if (GUILayout.Button("Export Mesh Asset"))
		{
			string path = EditorUtility.SaveFilePanelInProject("Save Rope Mesh", "BezierRopeMesh", "asset", "Choose location to save generated mesh");
			if (!string.IsNullOrEmpty(path))
			{
				targetRope.SaveMeshAsset(path);
				EditorUtility.DisplayDialog("Saved", "Mesh asset saved to: " + path, "OK");
			}
		}
		EditorGUILayout.EndHorizontal();

		EditorGUILayout.Space();
		EditorGUILayout.HelpBox("Create mode: click in Scene to add points. Edit mode: click a point to select and drag its sphere to move it. Click small anchor spheres to edit tangents. Shift+Click on curve to insert a point between segments.", MessageType.Info);

		EditorGUILayout.EndScrollView();
	}

	void OnSceneGUI(SceneView sv)
	{
		if (targetRope == null) return;

		// Draw sampled curve
		targetRope.SampleCurve(out List<Vector3> positions, out List<Vector3> tangents);
		Handles.color = curveColor;
		for (int i = 0; i < positions.Count - 1; i++) Handles.DrawLine(positions[i], positions[i + 1]);

		// Draw points, axes, main sphere and anchors
		for (int i = 0; i < targetRope.points.Count; i++)
		{
			Vector3 worldPos = targetRope.GetPointWorld(i);
			float visualScale = HandleUtility.GetHandleSize(worldPos);
			float axisLength = 0.32f * visualScale;
			float sphereSize = 0.08f * visualScale;

			// axes (like the Pipe tool)
			Color prev = Handles.color;
			Handles.color = Color.red;
			Handles.DrawLine(worldPos - Vector3.right * axisLength, worldPos + Vector3.right * axisLength);
			Handles.color = Color.green;
			Handles.DrawLine(worldPos - Vector3.up * axisLength, worldPos + Vector3.up * axisLength);
			Handles.color = Color.blue;
			Handles.DrawLine(worldPos - Vector3.forward * axisLength, worldPos + Vector3.forward * axisLength);
			Handles.color = prev;

			// main sphere
			Handles.color = pointColor;
			if (Handles.Button(worldPos, Quaternion.identity, sphereSize, sphereSize * 1.2f, Handles.SphereHandleCap))
			{
				selectedPoint = i;
				selectedHandle = -1;
				Repaint();
			}

			// if selected, allow dragging with PositionHandle
			if (selectedPoint == i && selectedHandle == -1)
			{
				EditorGUI.BeginChangeCheck();
				Vector3 newWorld = Handles.PositionHandle(worldPos, Quaternion.identity);
				if (EditorGUI.EndChangeCheck())
				{
					Undo.RecordObject(targetRope, "Move Point");
					targetRope.SetPointWorld(i, newWorld);
					EditorUtility.SetDirty(targetRope);
				}
			}

			// control anchors (two small spheres) and connecting debug lines
			Vector3 haWorld = targetRope.transform.TransformPoint(targetRope.points[i] + targetRope.handleA[i]);
			Vector3 hbWorld = targetRope.transform.TransformPoint(targetRope.points[i] + targetRope.handleB[i]);

			Handles.color = handleColor;
			Handles.DrawLine(worldPos, haWorld);
			Handles.DrawLine(worldPos, hbWorld);

			float hSize = 0.06f * visualScale;
			if (Handles.Button(haWorld, Quaternion.identity, hSize, hSize * 1.2f, Handles.SphereHandleCap))
			{
				selectedPoint = i;
				selectedHandle = 0;
			}
			if (Handles.Button(hbWorld, Quaternion.identity, hSize, hSize * 1.2f, Handles.SphereHandleCap))
			{
				selectedPoint = i;
				selectedHandle = 1;
			}

			if (selectedPoint == i && selectedHandle != -1)
			{
				EditorGUI.BeginChangeCheck();
				Vector3 handleWorld = selectedHandle == 0 ? haWorld : hbWorld;
				Vector3 newHandleWorld = Handles.PositionHandle(handleWorld, Quaternion.identity);
				if (EditorGUI.EndChangeCheck())
				{
					Undo.RecordObject(targetRope, "Move Handle");
					Vector3 local = targetRope.transform.InverseTransformPoint(newHandleWorld) - targetRope.points[i];
					if (selectedHandle == 0) targetRope.handleA[i] = local;
					else targetRope.handleB[i] = local;
					targetRope.EnsureHandles();
					if (targetRope.autoRebuild) targetRope.BuildMesh();
					EditorUtility.SetDirty(targetRope);
				}
			}
		}

		// Create mode: click to add points (prevents deselection)
		Event evt = Event.current;
		if (mode == ToolMode.Create && evt != null)
		{
			HandleUtility.AddDefaultControl(GUIUtility.GetControlID(FocusType.Passive));
			if (evt.type == EventType.MouseDown && evt.button == 0 && !evt.alt)
			{
				Ray worldRay = HandleUtility.GUIPointToWorldRay(evt.mousePosition);
				if (TryGetScenePosition(worldRay, out Vector3 worldPosition))
				{
					Undo.RecordObject(targetRope, "Add Bezier Point");
					targetRope.AddPoint(worldPosition);
					selectedPoint = targetRope.points.Count - 1;
					evt.Use();
				}
			}
		}

		// Edit mode: Shift+Click on curve to insert point between segments
		if (mode == ToolMode.Edit && evt != null && evt.type == EventType.MouseDown && evt.button == 0 && evt.shift && !evt.alt)
		{
			Ray worldRay = HandleUtility.GUIPointToWorldRay(evt.mousePosition);
			if (TryGetScenePosition(worldRay, out Vector3 worldPosition))
			{
				int insertIndex = FindNearestSegmentIndex(positions, worldPosition);
				if (insertIndex >= 0)
				{
					targetRope.InsertPoint(insertIndex + 1, worldPosition);
					selectedPoint = insertIndex + 1;
					evt.Use();
				}
			}
		}

		if (Event.current.type == EventType.Layout) HandleUtility.AddDefaultControl(GUIUtility.GetControlID(FocusType.Passive));
	}

	int FindNearestSegmentIndex(List<Vector3> sampledPositions, Vector3 worldPos)
	{
		if (sampledPositions == null || sampledPositions.Count < 2) return -1;
		float best = float.MaxValue;
		int bestIdx = -1;
		for (int i = 0; i < sampledPositions.Count - 1; i++)
		{
			Vector3 a = sampledPositions[i];
			Vector3 b = sampledPositions[i + 1];
			float d = HandleUtility.DistancePointToLineSegment(worldPos, a, b);
			if (d < best)
			{
				best = d;
				bestIdx = i;
			}
		}

		int segmentsPer = Mathf.Max(1, targetRope.lengthSegmentsPerCurve);
		int curveIndex = bestIdx / (segmentsPer + 1);
		return Mathf.Clamp(curveIndex, 0, targetRope.points.Count - 2);
	}

	static bool TryGetScenePosition(Ray ray, out Vector3 worldPosition)
	{
		if (Physics.Raycast(ray, out RaycastHit hitInfo, 10000f))
		{
			worldPosition = hitInfo.point;
			return true;
		}

		Plane groundPlane = new Plane(Vector3.up, Vector3.zero);
		if (groundPlane.Raycast(ray, out float enter))
		{
			worldPosition = ray.GetPoint(enter);
			return true;
		}

		worldPosition = Vector3.zero;
		return false;
	}
}
