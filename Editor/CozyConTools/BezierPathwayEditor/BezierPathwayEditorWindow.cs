// BezierPathwayEditorWindow.cs
// Editor window for BezierPathway with Create/Edit/Damage modes,
// scene handles, Shift+Click insert, Insert/Delete buttons,
// Export OBJ functionality, UV preview, End Caps toggle, and Sides toggle.

using UnityEngine;
using UnityEditor;
using System.Collections.Generic;

public class BezierPathwayEditorWindow : EditorWindow
{
	SerializedObject serializedPath;
	SerializedProperty pointsProp;
	SerializedProperty handleAProp;
	SerializedProperty handleBProp;
	SerializedProperty loopPathwayProp;
	SerializedProperty generateEndCapsProp;
	SerializedProperty autoSmoothFactorProp;

	// NEW serialized props for sides
	SerializedProperty generateSidesProp;
	SerializedProperty sideWidthProp;
	SerializedProperty sideHeightProp;

	BezierPathway targetPath;
	Vector2 scroll;

	enum ToolMode { None, Create, Edit, Damage }
	ToolMode mode = ToolMode.None;

	int selectedPoint = -1;
	int selectedHandle = -1; // -1 none, 0 handleA, 1 handleB

	Color pointColor = new Color(1f, 0.9f, 0.2f);
	Color handleColor = new Color(0.2f, 0.9f, 1f);
	Color curveColor = Color.green;
	Color damageColor = new Color(1f, 0.3f, 0.3f);

	Vector3 damageTriA;
	Vector3 damageTriB;
	Vector3 damageTriC;
	bool placingDamage = false;
	int damageStep = 0;

	Texture2D previewTextureCache;

	[MenuItem("Lilithe/Bezier Pathway Editor")]
	public static void ShowWindow()
	{
		GetWindow<BezierPathwayEditorWindow>("Bezier Pathway Editor");
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
		if (Selection.activeGameObject != null)
		{
			BezierPathway bp = Selection.activeGameObject.GetComponent<BezierPathway>();
			if (bp != null)
			{
				SetTarget(bp);
				Repaint();
				return;
			}
		}
	}

	void UpdateSerializedTarget()
	{
		if (targetPath != null)
		{
			serializedPath = new SerializedObject(targetPath);
			pointsProp = serializedPath.FindProperty("points");
			handleAProp = serializedPath.FindProperty("handleA");
			handleBProp = serializedPath.FindProperty("handleB");
			loopPathwayProp = serializedPath.FindProperty("loopPathway");
			generateEndCapsProp = serializedPath.FindProperty("generateEndCaps");
			autoSmoothFactorProp = serializedPath.FindProperty("autoSmoothFactor");

			// NEW: find side properties
			generateSidesProp = serializedPath.FindProperty("generateSides");
			sideWidthProp = serializedPath.FindProperty("sideWidth");
			sideHeightProp = serializedPath.FindProperty("sideHeight");
		}
		else
		{
			serializedPath = null;
			pointsProp = null;
			handleAProp = null;
			handleBProp = null;
			loopPathwayProp = null;
			generateEndCapsProp = null;
			autoSmoothFactorProp = null;
			generateSidesProp = null;
			sideWidthProp = null;
			sideHeightProp = null;
		}
	}

	void SetTarget(BezierPathway path)
	{
		targetPath = path;
		UpdateSerializedTarget();
		if (targetPath != null)
		{
			Selection.activeGameObject = targetPath.gameObject;
			EditorGUIUtility.PingObject(targetPath.gameObject);
			previewTextureCache = null;
		}
	}

	void OnGUI()
	{
		scroll = EditorGUILayout.BeginScrollView(scroll);

		EditorGUILayout.LabelField("Bezier Pathway Editor", EditorStyles.boldLabel);
		EditorGUILayout.Space();

		EditorGUI.BeginChangeCheck();
		Object newObj = EditorGUILayout.ObjectField("Target Pathway", targetPath ? (Object)targetPath : null, typeof(BezierPathway), true);
		if (EditorGUI.EndChangeCheck())
		{
			SetTarget(newObj as BezierPathway);
		}

		EditorGUILayout.BeginHorizontal();
		if (GUILayout.Button("Create New Pathway"))
		{
			GameObject go = new GameObject("BezierPathway");
			BezierPathway bp = go.AddComponent<BezierPathway>();
			SetTarget(bp);
		}
		if (GUILayout.Button("Use Selected"))
		{
			if (Selection.activeGameObject != null)
			{
				BezierPathway bp = Selection.activeGameObject.GetComponent<BezierPathway>();
				if (bp != null) SetTarget(bp);
				else EditorUtility.DisplayDialog("No Pathway", "Selected GameObject does not have a BezierPathway component.", "OK");
			}
			else EditorUtility.DisplayDialog("No Selection", "Select a GameObject in the Hierarchy first.", "OK");
		}
		EditorGUILayout.EndHorizontal();

		if (targetPath == null)
		{
			EditorGUILayout.HelpBox("Create or assign a BezierPathway to begin editing.", MessageType.Info);
			EditorGUILayout.EndScrollView();
			return;
		}

		serializedPath.Update();

		EditorGUILayout.Space();
		EditorGUILayout.BeginHorizontal();
		if (GUILayout.Toggle(mode == ToolMode.Create, "Create Mode", "Button")) mode = ToolMode.Create;
		if (GUILayout.Toggle(mode == ToolMode.Edit, "Edit Mode", "Button")) mode = ToolMode.Edit;
		if (GUILayout.Toggle(mode == ToolMode.Damage, "Add Damage", "Button")) mode = ToolMode.Damage;
		if (GUILayout.Toggle(mode == ToolMode.None, "None", "Button")) mode = ToolMode.None;
		EditorGUILayout.EndHorizontal();

		EditorGUILayout.Space();
		EditorGUILayout.PropertyField(serializedPath.FindProperty("autoRebuild"), new GUIContent("Auto Rebuild Mesh"));
		EditorGUILayout.PropertyField(serializedPath.FindProperty("pathMaterial"), new GUIContent("Path Material"));
		EditorGUILayout.PropertyField(serializedPath.FindProperty("sidingMaterial"), new GUIContent("Siding Material (optional)"));
		EditorGUILayout.PropertyField(serializedPath.FindProperty("pathWidth"), new GUIContent("Path Width"));
		EditorGUILayout.PropertyField(serializedPath.FindProperty("pathHeight"), new GUIContent("Path Height"));
		EditorGUILayout.PropertyField(serializedPath.FindProperty("sidingWidth"), new GUIContent("Siding Width"));
		EditorGUILayout.PropertyField(serializedPath.FindProperty("sidingHeight"), new GUIContent("Siding Height"));
		if (autoSmoothFactorProp != null)
		{
			EditorGUILayout.Slider(autoSmoothFactorProp, 0f, 2f, new GUIContent("Auto Smooth Factor"));
		}

		if (loopPathwayProp != null)
		{
			EditorGUILayout.PropertyField(loopPathwayProp, new GUIContent("Loop Pathway"));
		}

		// New toggle for end caps
		if (generateEndCapsProp != null)
		{
			bool isLoop = loopPathwayProp != null && loopPathwayProp.boolValue;
			if (isLoop)
			{
				generateEndCapsProp.boolValue = false;
			}

			using (new EditorGUI.DisabledScope(isLoop))
			{
				EditorGUILayout.PropertyField(generateEndCapsProp, new GUIContent("Generate End Caps"));
			}

			if (isLoop)
			{
				EditorGUILayout.HelpBox("End caps are disabled when Loop Pathway is enabled.", MessageType.Info);
			}
		}

		// NEW: Sides toggle and parameters
		if (generateSidesProp != null)
		{
			EditorGUILayout.Space();
			EditorGUILayout.LabelField("Sides", EditorStyles.boldLabel);
			EditorGUILayout.PropertyField(generateSidesProp, new GUIContent("Generate Sides"));
			EditorGUILayout.PropertyField(sideWidthProp, new GUIContent("Side Width (outward)"));
			EditorGUILayout.PropertyField(sideHeightProp, new GUIContent("Side Height (above path)"));
		}

		EditorGUILayout.Space();

		EditorGUILayout.LabelField("Usage Instructions", EditorStyles.boldLabel);
		EditorGUILayout.HelpBox(targetPath.usageInstructions, MessageType.Info);

		EditorGUILayout.Space();

		EditorGUILayout.LabelField("UV Tile Preview (per 1m segment)", EditorStyles.boldLabel);
		DrawUVPreview();

		EditorGUILayout.Space();

		serializedPath.ApplyModifiedProperties();

		EditorGUILayout.LabelField($"Points: {targetPath.points.Count}");

		for (int i = 0; i < targetPath.points.Count; i++)
		{
			EditorGUILayout.BeginHorizontal();
			if (GUILayout.Toggle(selectedPoint == i, "P" + i, "Button", GUILayout.Width(50))) selectedPoint = i;

			Vector3 local = targetPath.points[i];
			Vector3 newLocal = EditorGUILayout.Vector3Field("", local);
			if (newLocal != local)
			{
				Undo.RecordObject(targetPath, "Edit Point");
				targetPath.points[i] = newLocal;
				targetPath.EnsureHandles();
				if (targetPath.autoRebuild) targetPath.BuildMesh();
				EditorUtility.SetDirty(targetPath);
			}
			EditorGUILayout.EndHorizontal();
		}

		EditorGUILayout.Space();
		EditorGUILayout.BeginHorizontal();
		if (GUILayout.Button("Insert After Selected"))
		{
			if (selectedPoint >= 0 && selectedPoint < targetPath.points.Count)
			{
				Vector3 world = targetPath.GetPointWorld(selectedPoint);
				targetPath.InsertPoint(selectedPoint + 1, world + targetPath.transform.right * 0.5f);
				EditorUtility.SetDirty(targetPath);
			}
			else EditorUtility.DisplayDialog("No Selection", "Select a point first.", "OK");
		}
		if (GUILayout.Button("Delete Selected"))
		{
			if (selectedPoint >= 0 && selectedPoint < targetPath.points.Count)
			{
				targetPath.RemovePoint(selectedPoint);
				selectedPoint = -1;
				EditorUtility.SetDirty(targetPath);
			}
			else EditorUtility.DisplayDialog("No Selection", "Select a point first.", "OK");
		}
		if (GUILayout.Button("Rebuild Mesh Now"))
		{
			Undo.RecordObject(targetPath, "Rebuild Mesh");
			targetPath.BuildMesh();
			EditorUtility.SetDirty(targetPath);
		}
		if (GUILayout.Button("Auto Smooth"))
		{
			Undo.RecordObject(targetPath, "Auto Smooth Pathway");
			targetPath.AutoSmoothHandles();
			EditorUtility.SetDirty(targetPath);
		}
		if (GUILayout.Button("Bake Child Mesh (VRChat)"))
		{
			Undo.RecordObject(targetPath, "Bake Child Mesh");
			bool baked = targetPath.BakeRenderableChild();
			if (baked)
			{
				EditorUtility.SetDirty(targetPath);
				EditorUtility.DisplayDialog("Baked", "Created/updated baked child mesh asset at:\n" + targetPath.bakedMeshAssetPath, "OK");
			}
			else
			{
				EditorUtility.DisplayDialog("Bake Failed", "Could not bake a child mesh. Ensure the pathway has at least two points.", "OK");
			}
		}
		EditorGUILayout.EndHorizontal();

		EditorGUILayout.PropertyField(serializedPath.FindProperty("bakedChildName"), new GUIContent("Baked Child Name"));
		EditorGUILayout.PropertyField(serializedPath.FindProperty("bakedAssetFolder"), new GUIContent("Baked Asset Folder"));
		EditorGUILayout.PropertyField(serializedPath.FindProperty("disableSourceRendererAfterBake"), new GUIContent("Disable Source Renderer After Bake"));

		EditorGUILayout.Space();
		if (GUILayout.Button("Export OBJ"))
		{
			string path = EditorUtility.SaveFilePanelInProject(
				"Save Pathway OBJ",
				"BezierPathwayMesh",
				"obj",
				"Choose location to save generated OBJ/MTL pair");
			if (!string.IsNullOrEmpty(path))
			{
				targetPath.BuildMesh(); // ensure up to date
				targetPath.SaveMeshObj(path);
				EditorUtility.DisplayDialog("Saved", "OBJ/MTL saved to: " + path, "OK");
			}
		}

		EditorGUILayout.Space();
		EditorGUILayout.HelpBox(
			"Scene controls:\n" +
			"- Create Mode: Left click to add points.\n" +
			"- Edit Mode: Left click a point to select; drag the sphere to move it. Use handles to edit tangents.\n" +
			"- Damage Mode: Left click three times to place triangle vertices.\n" +
			"- Shift+Left Click on curve to insert a point between segments.\n" +
			"- Hold Alt and drag in Scene to orbit (standard Unity camera controls).",
			MessageType.Info);

		EditorGUILayout.EndScrollView();
	}

	void DrawUVPreview()
	{
		if (targetPath == null) return;

		Texture mainTex = null;
		if (targetPath.pathMaterial != null)
			mainTex = targetPath.pathMaterial.mainTexture;

		Rect r = GUILayoutUtility.GetRect(256, 128, GUILayout.ExpandWidth(false));
		EditorGUI.DrawRect(r, new Color(0.15f, 0.15f, 0.15f));

		if (mainTex != null)
		{
			EditorGUI.DrawPreviewTexture(r, mainTex, null, ScaleMode.ScaleToFit);
		}
		else
		{
			Color col = Color.gray;
			if (targetPath.pathMaterial != null && targetPath.pathMaterial.HasProperty("_Color"))
				col = targetPath.pathMaterial.color;
			EditorGUI.DrawRect(r, col);
		}

		Handles.BeginGUI();
		Color old = Handles.color;
		Handles.color = new Color(1f, 1f, 1f, 0.9f);

		Vector3 p1 = new Vector3(r.xMin, r.yMin);
		Vector3 p2 = new Vector3(r.xMax, r.yMin);
		Vector3 p3 = new Vector3(r.xMax, r.yMax);
		Vector3 p4 = new Vector3(r.xMin, r.yMax);
		Handles.DrawAAPolyLine(2f, new Vector3[] { p1, p2, p3, p4, p1 });

		int grid = 4;
		for (int i = 1; i < grid; i++)
		{
			float t = i / (float)grid;
			Vector3 a = new Vector3(Mathf.Lerp(r.xMin, r.xMax, t), r.yMin);
			Vector3 b = new Vector3(Mathf.Lerp(r.xMin, r.xMax, t), r.yMax);
			Handles.DrawLine(a, b);
			Vector3 c = new Vector3(r.xMin, Mathf.Lerp(r.yMin, r.yMax, t));
			Vector3 d = new Vector3(r.xMax, Mathf.Lerp(r.yMin, r.yMax, t));
			Handles.DrawLine(c, d);
		}

		Handles.color = new Color(1f, 0.8f, 0.2f, 1f);
		Handles.DrawAAPolyLine(3f, new Vector3[] { p1, p2, p3, p4, p1 });

		Handles.color = old;
		Handles.EndGUI();

		Rect labelRect = new Rect(r.x, r.yMax + 2, r.width, 18);
		EditorGUI.LabelField(labelRect, "Each 1m segment maps to full 0..1 UV tile (overlapping).");
	}

	void OnSceneGUI(SceneView sv)
	{
		if (targetPath == null) return;

		targetPath.SampleCurve(out List<Vector3> positions, out List<Vector3> tangents);
		Handles.color = curveColor;
		for (int i = 0; i < positions.Count - 1; i++)
			Handles.DrawLine(positions[i], positions[i + 1]);

		DrawPointsAndHandles();

		Event evt = Event.current;

		HandleCreateMode(evt);
		HandleEditInsertMode(evt);
		HandleDamageMode(evt);

		if (Event.current.type == EventType.Layout)
			HandleUtility.AddDefaultControl(GUIUtility.GetControlID(FocusType.Passive));
	}

	void DrawPointsAndHandles()
	{
		for (int i = 0; i < targetPath.points.Count; i++)
		{
			Vector3 worldPos = targetPath.GetPointWorld(i);
			float visualScale = HandleUtility.GetHandleSize(worldPos);
			float axisLength = 0.32f * visualScale;
			float sphereSize = 0.08f * visualScale;

			Handles.color = Color.red;
			Handles.DrawLine(worldPos - Vector3.right * axisLength, worldPos + Vector3.right * axisLength);
			Handles.color = Color.green;
			Handles.DrawLine(worldPos - Vector3.up * axisLength, worldPos + Vector3.up * axisLength);
			Handles.color = Color.blue;
			Handles.DrawLine(worldPos - Vector3.forward * axisLength, worldPos + Vector3.forward * axisLength);

			Handles.color = pointColor;
			if (Handles.Button(worldPos, Quaternion.identity, sphereSize, sphereSize * 1.2f, Handles.SphereHandleCap))
			{
				selectedPoint = i;
				selectedHandle = -1;
				Repaint();
			}

			if (selectedPoint == i && selectedHandle == -1)
			{
				EditorGUI.BeginChangeCheck();
				Vector3 newWorld = Handles.PositionHandle(worldPos, Quaternion.identity);
				if (EditorGUI.EndChangeCheck())
				{
					Undo.RecordObject(targetPath, "Move Point");
					targetPath.SetPointWorld(i, newWorld);
					EditorUtility.SetDirty(targetPath);
				}
			}

			// transform handle offsets as directions, not as positions
			Vector3 haWorld = targetPath.transform.TransformPoint(targetPath.points[i]) + targetPath.transform.TransformDirection(targetPath.handleA[i]);
			Vector3 hbWorld = targetPath.transform.TransformPoint(targetPath.points[i]) + targetPath.transform.TransformDirection(targetPath.handleB[i]);

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
					Undo.RecordObject(targetPath, "Move Handle");
					// compute local offset from point (directional)
					Vector3 local = targetPath.transform.InverseTransformPoint(newHandleWorld) - targetPath.points[i];
					if (selectedHandle == 0) targetPath.handleA[i] = local;
					else targetPath.handleB[i] = local;

					// IMPORTANT: do NOT call EnsureHandles() here — that would overwrite the user's manual handle edit.
					// Only rebuild mesh and mark dirty.
					if (targetPath.autoRebuild) targetPath.BuildMesh();
					EditorUtility.SetDirty(targetPath);
				}
			}
		}
	}

	void HandleCreateMode(Event evt)
	{
		if (mode != ToolMode.Create || evt == null) return;

		HandleUtility.AddDefaultControl(GUIUtility.GetControlID(FocusType.Passive));

		if (evt.type == EventType.MouseDown && evt.button == 0 && !evt.alt)
		{
			Ray worldRay = HandleUtility.GUIPointToWorldRay(evt.mousePosition);
			if (TryGetScenePosition(worldRay, out Vector3 worldPosition))
			{
				Undo.RecordObject(targetPath, "Add Path Point");
				targetPath.AddPoint(worldPosition);
				selectedPoint = targetPath.points.Count - 1;
				evt.Use();
			}
		}
	}

	void HandleEditInsertMode(Event evt)
	{
		if (mode != ToolMode.Edit || evt == null) return;

		if (evt.type == EventType.MouseDown && evt.button == 0 && evt.shift && !evt.alt)
		{
			Ray worldRay = HandleUtility.GUIPointToWorldRay(evt.mousePosition);
			if (TryGetScenePosition(worldRay, out Vector3 worldPosition))
			{
				targetPath.SampleCurve(out List<Vector3> positions, out _);
				int insertIndex = FindNearestSegmentIndex(positions, worldPosition);
				if (insertIndex >= 0)
				{
					targetPath.InsertPoint(insertIndex + 1, worldPosition);
					selectedPoint = insertIndex + 1;
					evt.Use();
				}
			}
		}
	}

	void HandleDamageMode(Event evt)
	{
		if (mode != ToolMode.Damage || evt == null) return;

		HandleUtility.AddDefaultControl(GUIUtility.GetControlID(FocusType.Passive));

		Handles.color = damageColor;

		if (placingDamage)
		{
			if (damageStep >= 1) Handles.DrawLine(damageTriA, damageTriB);
			if (damageStep >= 2) Handles.DrawLine(damageTriB, damageTriC);
			if (damageStep == 2) Handles.DrawLine(damageTriC, damageTriA);
		}

		if (evt.type == EventType.MouseDown && evt.button == 0 && !evt.alt)
		{
			Ray ray = HandleUtility.GUIPointToWorldRay(evt.mousePosition);
			if (TryGetScenePosition(ray, out Vector3 pos))
			{
				if (!placingDamage)
				{
					placingDamage = true;
					damageStep = 0;
				}

				if (damageStep == 0) damageTriA = pos;
				else if (damageStep == 1) damageTriB = pos;
				else if (damageStep == 2)
				{
					damageTriC = pos;
					placingDamage = false;

					Undo.RecordObject(targetPath, "Add Damage Triangle");
					targetPath.AddDamageTriangle(damageTriA, damageTriB, damageTriC);
					if (targetPath.autoRebuild) targetPath.BuildMesh();
					EditorUtility.SetDirty(targetPath);
				}

				damageStep++;
				evt.Use();
			}
		}
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

		int segmentsPer = Mathf.Max(1, targetPath.lengthSegmentsPerCurve);
		int curveIndex = bestIdx / (segmentsPer + 1);
		return Mathf.Clamp(curveIndex, 0, targetPath.points.Count - 2);
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
