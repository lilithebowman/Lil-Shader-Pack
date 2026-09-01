using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using MeshPartInfo = CozyCon.Tools.BuildingMeshUtility.MeshPartInfo;

namespace CozyCon.Tools
{
	public class BuildingModelingTool : EditorWindow
	{
		private enum ToolMode
		{
			None,
			EditBuilding,
			CreateWindow,
			CreateDoor
		}

		private const string DefaultRootName = "BuildingDraft";
		private const string WallsContainerName = "Walls";
		private const string WallJoinersContainerName = "WallJoiners";
		private const string OpeningsContainerName = "Openings";
		private const string CeilingObjectName = "Ceiling";
		private const string CeilingJoinersContainerName = "CeilingJoiners";
		private const string FloorObjectName = "Floor";
		private const string FloorJoinersContainerName = "FloorJoiners";
		private const string InteriorFloorSegmentsContainerName = "InteriorFloorSegments";

		private const float AnchorSphereRadius = 0.06f;
		private const float AnchorAxisLength = 0.3f;
		private const float WindowTrimDepth = 0.06f;
		private const float WindowTrimWidth = 0.06f;
		private const float WindowSillCenterOffset = 0.00f;
		private const float WindowSillDepth = 0.16f;
		private const float JoinerSizeMultiplier = 1.05f;
		private static readonly bool SplitWallToolEnabled = false;
		private static readonly string[] WallJoinerOptions = { "Sharp", "Beveled" };

		private BuildingDraftData activeDraft;
		private ToolMode mode;
		private int regularSideCount = 4;
		private float regularRadius = 4f;
		private bool snapToGrid;
		private float gridSize = 0.25f;
		private string selectedOpeningId;
		private int selectedWallIndex = -1;
		private Vector2 windowScroll;
		private bool splitWallOnClickArmed;

		private readonly List<WallRuntimeInfo> wallRuntimeInfo = new List<WallRuntimeInfo>();


		[MenuItem("Lilithe/Building Modeling Tool")]
		public static void ShowWindow()
		{
			GetWindow<BuildingModelingTool>("Building Modeling Tool");
		}

		private void OnEnable()
		{
			SceneView.duringSceneGui += OnSceneGUI;
			TryBindSelection();
		}

		private void OnDisable()
		{
			SceneView.duringSceneGui -= OnSceneGUI;
		}

		private void OnSelectionChange()
		{
			TryBindSelection();
			Repaint();
		}

		private void OnGUI()
		{
			windowScroll = EditorGUILayout.BeginScrollView(windowScroll);

			EditorGUILayout.LabelField("Building Modeling", EditorStyles.boldLabel);
			EditorGUILayout.HelpBox("Create a hollow building shell, then use Edit Building mode to drag corner anchors. Use Window or Door creation modes to click a wall and place openings.", MessageType.Info);

			EditorGUILayout.Space();
			DrawModeButtons();

			EditorGUILayout.Space();
			DrawDraftControls();

			if (activeDraft == null)
			{
				EditorGUILayout.EndScrollView();
				return;
			}

			EditorGUILayout.Space();
			DrawShapeControls();

			EditorGUILayout.Space();
			DrawMaterialControls();

			EditorGUILayout.Space();
			DrawOpeningDefaults();

			EditorGUILayout.Space();
			DrawOpeningsList();

			EditorGUILayout.Space();
			EditorGUILayout.BeginHorizontal();
			if (GUILayout.Button("Rebuild Building"))
			{
				RebuildBuilding(activeDraft);
			}

			if (GUILayout.Button("Export OBJ/MTL") && activeDraft != null)
			{
				ExportActiveDraftAsObjMtl();
			}

			if (GUILayout.Button("Delete All Openings"))
			{
				Undo.RecordObject(activeDraft, "Delete All Openings");
				activeDraft.Openings.Clear();
				selectedOpeningId = null;
				RebuildBuilding(activeDraft);
			}
			EditorGUILayout.EndHorizontal();

			EditorGUILayout.EndScrollView();
		}

		private void DrawModeButtons()
		{
			EditorGUILayout.BeginHorizontal();
			if (GUILayout.Toggle(mode == ToolMode.EditBuilding, "Edit Building", "Button"))
			{
				mode = ToolMode.EditBuilding;
			}
			else if (mode == ToolMode.EditBuilding)
			{
				mode = ToolMode.None;
			}

			if (GUILayout.Toggle(mode == ToolMode.CreateWindow, "Window Creation", "Button"))
			{
				mode = ToolMode.CreateWindow;
			}
			else if (mode == ToolMode.CreateWindow)
			{
				mode = ToolMode.None;
			}

			if (GUILayout.Toggle(mode == ToolMode.CreateDoor, "Door Creation", "Button"))
			{
				mode = ToolMode.CreateDoor;
			}
			else if (mode == ToolMode.CreateDoor)
			{
				mode = ToolMode.None;
			}
			EditorGUILayout.EndHorizontal();
		}

		private void DrawDraftControls()
		{
			EditorGUILayout.BeginHorizontal();
			activeDraft = (BuildingDraftData)EditorGUILayout.ObjectField("Active Building", activeDraft, typeof(BuildingDraftData), true);
			if (GUILayout.Button("Use Selected", GUILayout.Width(110f)))
			{
				TryBindSelection();
			}
			EditorGUILayout.EndHorizontal();

			EditorGUILayout.BeginHorizontal();
			if (GUILayout.Button("New Building"))
			{
				CreateNewDraft();
			}

			if (GUILayout.Button("Duplicate Building") && activeDraft != null)
			{
				DuplicateDraft(activeDraft);
			}
			EditorGUILayout.EndHorizontal();
		}

		private void DrawShapeControls()
		{
			if (!SplitWallToolEnabled)
			{
				splitWallOnClickArmed = false;
			}

			EditorGUI.BeginChangeCheck();
			regularSideCount = EditorGUILayout.IntSlider("Regular Side Count", regularSideCount, 3, 12);
			regularRadius = EditorGUILayout.Slider("Regular Radius", regularRadius, 0.5f, 60f);

			float buildingHeight = EditorGUILayout.FloatField("Building Height", activeDraft.BuildingHeight);
			int buildingFloors = EditorGUILayout.IntSlider("Building Floors", activeDraft.BuildingFloors, 1, 24);
			float wallThickness = EditorGUILayout.FloatField("Wall Thickness", activeDraft.WallThickness);
			float ceilingThickness = EditorGUILayout.FloatField("Ceiling Thickness", activeDraft.RoofThickness);
			float insetRoofOffset = EditorGUILayout.FloatField("Inset Roof Offset", activeDraft.InsetRoofOffset);
			float floorThickness = EditorGUILayout.FloatField("Floor Thickness", activeDraft.FloorThickness);
			float wallUvScaleMultiplier = EditorGUILayout.FloatField("Wall UV Scale", activeDraft.WallUvScaleMultiplier);
			int wallJoinerMode = activeDraft.WallJoinerStyle == BuildingJoinerStyle.Sharp ? 0 : 1;
			wallJoinerMode = EditorGUILayout.Popup("Wall Joiners", wallJoinerMode, WallJoinerOptions);
			BuildingJoinerStyle wallJoinerStyle = wallJoinerMode == 0 ? BuildingJoinerStyle.Sharp : BuildingJoinerStyle.Beveled;
			int bevelSegments = EditorGUILayout.IntSlider("Bevel Segments", activeDraft.CurvedJoinerSegments, 1, 24);
			snapToGrid = EditorGUILayout.Toggle("Snap To Grid", snapToGrid);
			gridSize = EditorGUILayout.FloatField("Grid Size", gridSize);

			if (EditorGUI.EndChangeCheck())
			{
				Undo.RecordObject(activeDraft, "Edit Building Dimensions");
				activeDraft.BuildingHeight = buildingHeight;
				activeDraft.BuildingFloors = buildingFloors;
				activeDraft.WallThickness = wallThickness;
				activeDraft.RoofThickness = ceilingThickness;
				activeDraft.InsetRoofOffset = insetRoofOffset;
				activeDraft.FloorThickness = floorThickness;
				activeDraft.WallUvScaleMultiplier = wallUvScaleMultiplier;
				activeDraft.WallJoinerStyle = wallJoinerStyle;
				activeDraft.CurvedJoinerSegments = bevelSegments;
				gridSize = Mathf.Max(0.01f, gridSize);
				RebuildBuilding(activeDraft);
			}

			EditorGUILayout.BeginHorizontal();
			if (GUILayout.Button("Reset Footprint To Regular Shape"))
			{
				Undo.RecordObject(activeDraft, "Reset Building Footprint");
				activeDraft.RegenerateRegularFootprint(regularSideCount, regularRadius);
				RebuildBuilding(activeDraft);
			}

			if (GUILayout.Button("Auto Fit Radius From Corners"))
			{
				float maxRadius = 0.5f;
				for (int i = 0; i < activeDraft.FootprintCorners.Count; i++)
				{
					maxRadius = Mathf.Max(maxRadius, activeDraft.FootprintCorners[i].magnitude);
				}

				regularRadius = maxRadius;
			}
			EditorGUILayout.EndHorizontal();

			EditorGUILayout.LabelField($"Current Walls: {activeDraft.FootprintCorners.Count}");

			EditorGUILayout.Space(3f);
			EditorGUILayout.LabelField("Floor Settings", EditorStyles.boldLabel);
			EditorGUI.BeginChangeCheck();
			bool enableFloorDoors = EditorGUILayout.Toggle("Enable Floor Doors", activeDraft.EnableFloorDoors);
			bool enableFloorStairs = EditorGUILayout.Toggle("Enable Floor Stairs", activeDraft.EnableFloorStairs);
			float stairStepHeight = EditorGUILayout.FloatField("Stair Step Height", activeDraft.StairStepHeight);
			if (EditorGUI.EndChangeCheck())
			{
				Undo.RecordObject(activeDraft, "Edit Floor Settings");
				activeDraft.EnableFloorDoors = enableFloorDoors;
				activeDraft.EnableFloorStairs = enableFloorStairs;
				activeDraft.StairStepHeight = stairStepHeight;
				RebuildBuilding(activeDraft);
			}

			EditorGUILayout.Space(3f);
			EditorGUILayout.LabelField("Wall Split", EditorStyles.boldLabel);
			if (activeDraft.FootprintCorners.Count > 0)
			{
				selectedWallIndex = Mathf.Clamp(selectedWallIndex, 0, activeDraft.FootprintCorners.Count - 1);
				selectedWallIndex = EditorGUILayout.IntSlider("Selected Wall", selectedWallIndex + 1, 1, activeDraft.FootprintCorners.Count) - 1;
			}

			/* This functionality currently does not work
			 * the scene view makes clicking the wall select the wall
			 * instead of splitting it.
			 */
			EditorGUI.BeginDisabledGroup(!SplitWallToolEnabled);
			if (GUILayout.Button("Split Wall At Click Point"))
			{
				splitWallOnClickArmed = true;
				SceneView.RepaintAll();
			}
			EditorGUI.EndDisabledGroup();

			if (!SplitWallToolEnabled)
			{
				EditorGUILayout.HelpBox("Split wall is temporarily disabled. Implementation code is kept in place for future re-enable.", MessageType.Warning);
			}
			else
			{
				EditorGUILayout.HelpBox(splitWallOnClickArmed
					? "Click a wall in Scene view to split it exactly at the clicked point."
					: "Split is click-driven: press the button, then click a wall where you want the new corner.", MessageType.None);

				if (splitWallOnClickArmed && GUILayout.Button("Cancel Split"))
				{
					splitWallOnClickArmed = false;
				}
			}
		}

		private void DrawMaterialControls()
		{
			EditorGUI.BeginChangeCheck();
			Material defaultWall = (Material)EditorGUILayout.ObjectField("Default Wall Material", activeDraft.DefaultWallMaterial, typeof(Material), false);
			Material ceiling = (Material)EditorGUILayout.ObjectField("Ceiling Material", activeDraft.CeilingMaterial, typeof(Material), false);
			Material floor = (Material)EditorGUILayout.ObjectField("Floor Material", activeDraft.FloorMaterial, typeof(Material), false);
			Material trim = (Material)EditorGUILayout.ObjectField("Trim Material", activeDraft.TrimMaterial, typeof(Material), false);
			Material glass = (Material)EditorGUILayout.ObjectField("Glass Material", activeDraft.GlassMaterial, typeof(Material), false);
			Material door = (Material)EditorGUILayout.ObjectField("Door Material", activeDraft.DoorMaterial, typeof(Material), false);

			if (EditorGUI.EndChangeCheck())
			{
				Undo.RecordObject(activeDraft, "Edit Building Materials");
				activeDraft.DefaultWallMaterial = defaultWall;
				activeDraft.EnsureWallMaterialCount();
				for (int i = 0; i < activeDraft.FootprintCorners.Count; i++)
				{
					activeDraft.SetWallMaterial(i, defaultWall);
				}
				activeDraft.CeilingMaterial = ceiling;
				activeDraft.FloorMaterial = floor;
				activeDraft.TrimMaterial = trim;
				activeDraft.GlassMaterial = glass;
				activeDraft.DoorMaterial = door;
				RebuildBuilding(activeDraft);
			}

			activeDraft.EnsureWallMaterialCount();
			EditorGUILayout.LabelField("Per-Wall Materials", EditorStyles.boldLabel);

			for (int i = 0; i < activeDraft.FootprintCorners.Count; i++)
			{
				EditorGUI.BeginChangeCheck();
				Material wallMat = (Material)EditorGUILayout.ObjectField($"Wall {i + 1}", activeDraft.GetWallMaterial(i), typeof(Material), false);
				if (EditorGUI.EndChangeCheck())
				{
					Undo.RecordObject(activeDraft, "Edit Wall Material");
					activeDraft.SetWallMaterial(i, wallMat);
					RebuildBuilding(activeDraft);
				}
			}
		}

		private void DrawOpeningDefaults()
		{
			EditorGUILayout.LabelField("Opening Defaults", EditorStyles.boldLabel);
			EditorGUI.BeginChangeCheck();
			bool windowTrim = EditorGUILayout.Toggle("Window Trim", activeDraft.SpawnWindowTrim);
			bool windowSill = EditorGUILayout.Toggle("Window Sill", activeDraft.SpawnWindowSill);
			float windowSillThickness = EditorGUILayout.FloatField("Window Sill Thickness", activeDraft.WindowSillThickness);
			bool windowGlass = EditorGUILayout.Toggle("Window Glass", activeDraft.SpawnWindowGlass);
			bool doorTrim = EditorGUILayout.Toggle("Door Trim", activeDraft.SpawnDoorTrim);
			bool spawnDoorPanel = EditorGUILayout.Toggle("Door Panel (New Doors)", activeDraft.SpawnDoorPanelInDoorOpenings);
			float windowFrameInset = EditorGUILayout.FloatField("Window Frame Inset", activeDraft.WindowFrameInset);
			float doorFrameInset = EditorGUILayout.FloatField("Door Frame Inset", activeDraft.DoorFrameInset);
			if (EditorGUI.EndChangeCheck())
			{
				Undo.RecordObject(activeDraft, "Edit Opening Defaults");
				activeDraft.SpawnWindowTrim = windowTrim;
				activeDraft.SpawnWindowSill = windowSill;
				activeDraft.WindowSillThickness = windowSillThickness;
				activeDraft.SpawnWindowGlass = windowGlass;
				activeDraft.SpawnDoorTrim = doorTrim;
				activeDraft.SpawnDoorPanelInDoorOpenings = spawnDoorPanel;
				activeDraft.WindowFrameInset = windowFrameInset;
				activeDraft.DoorFrameInset = doorFrameInset;
				RebuildBuilding(activeDraft);
			}
			EditorGUILayout.HelpBox("Frame inset pulls the inner frame edge inward to prevent z-fighting. Values are clamped per opening to below one quarter of opening width/height.", MessageType.None);
		}

		private void DrawOpeningsList()
		{
			EditorGUILayout.LabelField("Openings", EditorStyles.boldLabel);
			if (activeDraft.Openings.Count == 0)
			{
				EditorGUILayout.HelpBox("No openings yet. Switch to Window Creation or Door Creation mode and click a wall.", MessageType.None);
				return;
			}

			for (int i = 0; i < activeDraft.Openings.Count; i++)
			{
				BuildingOpeningData opening = activeDraft.Openings[i];
				string label = $"{opening.type} | Wall {opening.wallIndex + 1}";
				EditorGUILayout.BeginVertical("box");
				EditorGUILayout.BeginHorizontal();
				if (GUILayout.Toggle(selectedOpeningId == opening.id, label, "Button"))
				{
					selectedOpeningId = opening.id;
				}

				if (GUILayout.Button("Delete", GUILayout.Width(72f)))
				{
					Undo.RecordObject(activeDraft, "Delete Opening");
					activeDraft.Openings.RemoveAt(i);
					if (selectedOpeningId == opening.id)
					{
						selectedOpeningId = null;
					}
					RebuildBuilding(activeDraft);
					EditorGUILayout.EndHorizontal();
					EditorGUILayout.EndVertical();
					break;
				}
				EditorGUILayout.EndHorizontal();

				EditorGUI.BeginChangeCheck();
				Vector2 size = EditorGUILayout.Vector2Field("Size", opening.size);
				bool showTrim = EditorGUILayout.Toggle("Show Trim", opening.showTrim);
				bool showSill = opening.type == BuildingOpeningType.Window ? EditorGUILayout.Toggle("Show Sill", opening.showSill) : opening.showSill;
				bool showGlass = opening.type == BuildingOpeningType.Window ? EditorGUILayout.Toggle("Show Glass", opening.showGlass) : opening.showGlass;
				bool showDoor = opening.type == BuildingOpeningType.Door && opening.wallIndex >= 0 ? EditorGUILayout.Toggle("Show Door Panel", opening.showDoor) : opening.showDoor;
				if (EditorGUI.EndChangeCheck())
				{
					Undo.RecordObject(activeDraft, "Edit Opening");
					opening.size = new Vector2(Mathf.Max(0.2f, size.x), Mathf.Max(0.2f, size.y));
					opening.showTrim = showTrim;
					opening.showSill = showSill;
					opening.showGlass = showGlass;
					opening.showDoor = showDoor;
					ClampOpeningToWall(activeDraft, opening);
					RebuildBuilding(activeDraft);
				}

				EditorGUILayout.EndVertical();
			}
		}

		private void OnSceneGUI(SceneView sceneView)
		{
			if (activeDraft == null)
			{
				if (mode == ToolMode.EditBuilding && TryHandleSpawnFromHorizontalClick())
				{
					return;
				}

				return;
			}

			EnsureDraftIsValid(activeDraft);
			DrawWallHitHints();

			switch (mode)
			{
				case ToolMode.EditBuilding:
					if (SplitWallToolEnabled && HandleWallClickToSplit())
					{
						return;
					}
					DrawWallSelectionHandles();
					DrawCornerAnchorsAndHandles();
					DrawOpeningCornerHandles();
					if (TryHandleSpawnFromHorizontalClick())
					{
						return;
					}
					break;
				case ToolMode.CreateWindow:
				case ToolMode.CreateDoor:
					HandleWallClickToCreateOpening();
					break;
			}
		}

		private bool TryHandleSpawnFromHorizontalClick()
		{
			Event evt = Event.current;
			if (evt == null)
			{
				return false;
			}
			if (evt.type != EventType.MouseDown || evt.button != 0 || evt.alt)
			{
				return false;
			}

			Ray ray = HandleUtility.GUIPointToWorldRay(evt.mousePosition);
			if (!TryGetHorizontalSpawnPoint(ray, out Vector3 spawnPoint))
			{
				return false;
			}

			GameObject root = new GameObject(GetUniqueDraftName());
			Undo.RegisterCreatedObjectUndo(root, "Create Building Draft");
			BuildingDraftData draft = root.AddComponent<BuildingDraftData>();
			draft.RegenerateRegularFootprint(regularSideCount, regularRadius);
			AssignUnityDefaultMaterials(draft);
			root.transform.position = spawnPoint;
			activeDraft = draft;
			Selection.activeObject = root;

			selectedWallIndex = 0;
			RebuildBuilding(activeDraft);
			evt.Use();
			return true;
		}

		private static bool TryGetHorizontalSpawnPoint(Ray ray, out Vector3 spawnPoint)
		{
			RaycastHit[] hits = Physics.RaycastAll(ray, 20000f);
			if (hits != null && hits.Length > 0)
			{
				Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

				RaycastHit nearestHit = hits[0];
				if (nearestHit.transform != null && nearestHit.transform.GetComponentInParent<BuildingDraftData>() != null)
				{
					spawnPoint = Vector3.zero;
					return false;
				}

				float upDot = Vector3.Dot(nearestHit.normal.normalized, Vector3.up);
				if (upDot >= 0.95f)
				{
					spawnPoint = nearestHit.point;
					return true;
				}

				// A nearer non-horizontal collider blocks the ray; do not spawn through it.
				spawnPoint = Vector3.zero;
				return false;
			}

			Plane floorPlane = new Plane(Vector3.up, Vector3.zero);
			if (floorPlane.Raycast(ray, out float enter))
			{
				spawnPoint = ray.GetPoint(enter);
				return true;
			}

			spawnPoint = Vector3.zero;
			return false;
		}

		private void DrawWallHitHints()
		{
			if (mode != ToolMode.CreateDoor && mode != ToolMode.CreateWindow)
			{
				return;
			}

			Handles.BeginGUI();
			GUILayout.BeginArea(new Rect(12f, 12f, 420f, 60f), EditorStyles.helpBox);
			GUILayout.Label(mode == ToolMode.CreateWindow
				? "Window Creation Mode: click a wall from inside or outside to place a centered window."
				: "Door Creation Mode: click a wall from inside or outside to place a centered door.");
			GUILayout.EndArea();
			Handles.EndGUI();
		}

		private void DrawCornerAnchorsAndHandles()
		{
			for (int i = 0; i < activeDraft.FootprintCorners.Count; i++)
			{
				Vector3 local = new Vector3(activeDraft.FootprintCorners[i].x, 0f, activeDraft.FootprintCorners[i].y);
				Vector3 world = activeDraft.transform.TransformPoint(local);
				DrawAnchorGizmo(world, new Color(0.95f, 0.7f, 0.1f, 1f));

				float size = HandleUtility.GetHandleSize(world) * AnchorSphereRadius;
				EditorGUI.BeginChangeCheck();
				Vector3 moved = Handles.FreeMoveHandle(world, size, Vector3.zero, Handles.SphereHandleCap);
				if (EditorGUI.EndChangeCheck())
				{
					Undo.RecordObject(activeDraft, "Move Building Corner");
					Vector3 movedLocal = activeDraft.transform.InverseTransformPoint(moved);
					Vector2 corner = new Vector2(movedLocal.x, movedLocal.z);
					if (snapToGrid)
					{
						corner = SnapVector2(corner, gridSize);
					}

					activeDraft.FootprintCorners[i] = corner;
					RebuildBuilding(activeDraft);
				}
			}
		}

		private void DrawWallSelectionHandles()
		{
			BuildWallRuntimeCache(activeDraft);
			if (wallRuntimeInfo.Count == 0)
			{
				return;
			}

			for (int i = 0; i < wallRuntimeInfo.Count; i++)
			{
				WallRuntimeInfo wall = wallRuntimeInfo[i];
				Vector3 markerPos = wall.worldPosition;
				float scale = HandleUtility.GetHandleSize(markerPos) * AnchorSphereRadius * 1.8f;

				Color prev = Handles.color;
				bool hasWindow = WallHasWindow(activeDraft, wall.wallIndex);
				Handles.color = selectedWallIndex == wall.wallIndex ? new Color(1f, 0.95f, 0.2f, 1f) : (hasWindow ? new Color(1f, 0.35f, 0.35f, 1f) : new Color(0.7f, 0.7f, 1f, 1f));
				if (Handles.Button(markerPos, Quaternion.identity, scale, scale, Handles.CubeHandleCap))
				{
					selectedWallIndex = wall.wallIndex;
					splitWallOnClickArmed = false;
					Repaint();
				}

				Handles.color = prev;
			}
		}

		private bool HandleWallClickToSplit()
		{
			if (!splitWallOnClickArmed || activeDraft == null)
			{
				return false;
			}

			Event evt = Event.current;
			if (evt == null)
			{
				return false;
			}

			if (evt.type == EventType.KeyDown && evt.keyCode == KeyCode.Escape)
			{
				splitWallOnClickArmed = false;
				evt.Use();
				return true;
			}

			Handles.BeginGUI();
			GUILayout.BeginArea(new Rect(12f, 80f, 420f, 60f), EditorStyles.helpBox);
			GUILayout.Label("Split Wall: click a wall to insert a new corner at that exact point (Esc to cancel).");
			GUILayout.EndArea();
			Handles.EndGUI();

			HandleUtility.AddDefaultControl(GUIUtility.GetControlID(FocusType.Passive));

			Ray ray = HandleUtility.GUIPointToWorldRay(evt.mousePosition);
			if (TryGetWallHit(ray, out RaycastHit previewHit, out WallRuntimeInfo previewWall))
			{
				Vector2 previewUv = WorldToWallUv(previewWall, activeDraft.WallHeight, previewHit.point);
				Vector3 previewLocal = Quaternion.Inverse(previewWall.worldRotation) * (previewHit.point - previewWall.worldCenterlinePosition);
				DrawSplitPreviewLine(previewWall, previewUv.x, activeDraft.WallHeight, previewLocal.z);
			}

			if (evt.type == EventType.MouseMove)
			{
				SceneView.RepaintAll();
			}

			if (evt.type != EventType.MouseDown || evt.button != 0 || evt.alt)
			{
				return true;
			}

			// Consume the click in split mode so SceneView selection cannot hijack it.
			evt.Use();

			if (!TryGetWallHit(ray, out RaycastHit hit, out WallRuntimeInfo wall))
			{
				return true;
			}

			Vector2 uv = WorldToWallUv(wall, activeDraft.WallHeight, hit.point);
			float splitDistance = uv.x;

			if (SplitWallAtDistance(activeDraft, wall.wallIndex, splitDistance))
			{
				splitWallOnClickArmed = false;
			}

			return true;
		}

		private static void DrawSplitPreviewLine(WallRuntimeInfo wall, float splitDistance, float wallHeight, float wallLocalDepth)
		{
			splitDistance = Mathf.Clamp(splitDistance, 0f, wall.length);

			Vector3 start = WallUvToWorld(wall, wallHeight, splitDistance, 0f, wallLocalDepth);
			Vector3 end = WallUvToWorld(wall, wallHeight, splitDistance, wallHeight, wallLocalDepth);

			Color previous = Handles.color;
			Handles.color = new Color(0.1f, 1f, 1f, 1f);
			Handles.DrawAAPolyLine(4f, start, end);
			Handles.color = previous;
		}

		private void DrawOpeningCornerHandles()
		{
			if (activeDraft == null || activeDraft.Openings.Count == 0)
			{
				return;
			}

			BuildWallRuntimeCache(activeDraft);
			for (int i = 0; i < activeDraft.Openings.Count; i++)
			{
				BuildingOpeningData opening = activeDraft.Openings[i];

				// Handle floor doors (negative wallIndex)
				if (opening.wallIndex < 0)
				{
					DrawFloorOpeningCornerHandles(activeDraft, opening);
				}
				else if (TryGetWallInfo(opening.wallIndex, out WallRuntimeInfo wall))
				{
					// Handle wall openings
					Color color = opening.type == BuildingOpeningType.Window ? new Color(0.2f, 0.9f, 1f, 1f) : new Color(1f, 0.45f, 0.2f, 1f);
					DrawOpeningCornerHandle(activeDraft, opening, wall, 0, color);
					DrawOpeningCornerHandle(activeDraft, opening, wall, 1, color);
					DrawOpeningCornerHandle(activeDraft, opening, wall, 2, color);
					DrawOpeningCornerHandle(activeDraft, opening, wall, 3, color);
				}
			}
		}

		private void DrawFloorOpeningCornerHandles(BuildingDraftData draft, BuildingOpeningData opening)
		{
			// Floor doors are positioned on the floor surface
			// opening.center is in local floor space, opening.size is the floor hole size
			Color color = new Color(1f, 0.45f, 0.2f, 1f); // Door color (orange)
			BuildingEditorUtility.GetOpeningMinMax(opening, out float minX, out float maxX, out float minY, out float maxY);

			// Convert floor-local coordinates to world space
			// Floor doors use local center (0,0) and size in XZ plane
			int floorLevel = -(opening.wallIndex + 2); // Extract floor level from wallIndex
			float levelHeight = draft.BuildingHeight / Mathf.Max(1, draft.BuildingFloors);
			float floorY = levelHeight * (floorLevel + 1); // Y position of this floor

			for (int cornerIndex = 0; cornerIndex < 4; cornerIndex++)
			{
				Vector2 cornerUv = GetCornerUv(cornerIndex, minX, maxX, minY, maxY);
				Vector3 local = new Vector3(cornerUv.x, floorY, cornerUv.y);
				Vector3 world = draft.transform.TransformPoint(local);

				DrawAnchorGizmo(world, color);

				float size = HandleUtility.GetHandleSize(world) * AnchorSphereRadius;
				EditorGUI.BeginChangeCheck();
				Vector3 moved = Handles.FreeMoveHandle(world, size, Vector3.zero, Handles.SphereHandleCap);
				if (!EditorGUI.EndChangeCheck())
				{
					continue;
				}

				// Convert moved position back to local space
				Vector3 movedLocal = draft.transform.InverseTransformPoint(moved);
				Vector2 movedUv = new Vector2(movedLocal.x, movedLocal.z);

				if (snapToGrid)
				{
					movedUv = BuildingEditorUtility.SnapVector2(movedUv, gridSize);
				}

				// Calculate new opening bounds based on corner movement
				int oppositeIndex = (cornerIndex + 2) % 4;
				Vector2 oppositeUv = GetCornerUv(oppositeIndex, minX, maxX, minY, maxY);

				float minSize = 0.2f;
				float floorWidth = Mathf.Max(1f, draft.FootprintCorners[0].magnitude * 2f);
				float floorHeight = Mathf.Max(1f, floorWidth);

				float nextMinX = Mathf.Min(movedUv.x, oppositeUv.x);
				float nextMaxX = Mathf.Max(movedUv.x, oppositeUv.x);
				float nextMinY = Mathf.Min(movedUv.y, oppositeUv.y);
				float nextMaxY = Mathf.Max(movedUv.y, oppositeUv.y);

				nextMinX = Mathf.Clamp(nextMinX, -floorWidth, floorWidth - minSize);
				nextMaxX = Mathf.Clamp(nextMaxX, nextMinX + minSize, floorWidth);
				nextMinY = Mathf.Clamp(nextMinY, -floorHeight, floorHeight - minSize);
				nextMaxY = Mathf.Clamp(nextMaxY, nextMinY + minSize, floorHeight);

				Undo.RecordObject(draft, "Resize Floor Opening");
				opening.center = new Vector2((nextMinX + nextMaxX) * 0.5f, (nextMinY + nextMaxY) * 0.5f);
				opening.size = new Vector2(nextMaxX - nextMinX, nextMaxY - nextMinY);
				RebuildBuilding(draft);
			}
		}

		private void DrawOpeningCornerHandle(BuildingDraftData draft, BuildingOpeningData opening, WallRuntimeInfo wall, int cornerIndex, Color color)
		{
			BuildingEditorUtility.GetOpeningMinMax(opening, out float minX, out float maxX, out float minY, out float maxY);
			Vector2 cornerUv = GetCornerUv(cornerIndex, minX, maxX, minY, maxY);
			Vector3 world = WallUvToWorld(wall, draft.WallHeight, cornerUv.x, cornerUv.y, 0f);

			DrawAnchorGizmo(world, color);

			float size = HandleUtility.GetHandleSize(world) * AnchorSphereRadius;
			EditorGUI.BeginChangeCheck();
			Vector3 moved = Handles.FreeMoveHandle(world, size, Vector3.zero, Handles.SphereHandleCap);
			if (!EditorGUI.EndChangeCheck())
			{
				return;
			}

			Vector2 movedUv = WorldToWallUv(wall, draft.WallHeight, moved);
			if (snapToGrid)
			{
				movedUv = BuildingEditorUtility.SnapVector2(movedUv, gridSize);
			}
			int oppositeIndex = (cornerIndex + 2) % 4;
			Vector2 oppositeUv = GetCornerUv(oppositeIndex, minX, maxX, minY, maxY);

			float nextMinX = Mathf.Min(movedUv.x, oppositeUv.x);
			float nextMaxX = Mathf.Max(movedUv.x, oppositeUv.x);
			float nextMinY = Mathf.Min(movedUv.y, oppositeUv.y);
			float nextMaxY = Mathf.Max(movedUv.y, oppositeUv.y);

			float minSize = 0.2f;
			nextMinX = Mathf.Clamp(nextMinX, 0f, wall.length - minSize);
			nextMaxX = Mathf.Clamp(nextMaxX, nextMinX + minSize, wall.length);
			nextMinY = Mathf.Clamp(nextMinY, 0f, draft.WallHeight - minSize);
			nextMaxY = Mathf.Clamp(nextMaxY, nextMinY + minSize, draft.WallHeight);

			Undo.RecordObject(draft, "Resize Opening");
			opening.center = new Vector2((nextMinX + nextMaxX) * 0.5f, (nextMinY + nextMaxY) * 0.5f);
			opening.size = new Vector2(nextMaxX - nextMinX, nextMaxY - nextMinY);
			ClampOpeningToWall(draft, opening);
			RebuildBuilding(draft);
		}

		private static Vector2 GetCornerUv(int cornerIndex, float minX, float maxX, float minY, float maxY)
		{
			switch (cornerIndex)
			{
				case 0: return new Vector2(minX, minY);
				case 1: return new Vector2(maxX, minY);
				case 2: return new Vector2(maxX, maxY);
				default: return new Vector2(minX, maxY);
			}
		}

		private void HandleWallClickToCreateOpening()
		{
			Event evt = Event.current;
			if (evt == null)
			{
				return;
			}

			HandleUtility.AddDefaultControl(GUIUtility.GetControlID(FocusType.Passive));
			if (evt.type != EventType.MouseDown || evt.button != 0 || evt.alt)
			{
				return;
			}

			Ray ray = HandleUtility.GUIPointToWorldRay(evt.mousePosition);
			if (!TryGetWallHit(ray, out RaycastHit hit, out WallRuntimeInfo wall))
			{
				return;
			}

			Vector2 uv = WorldToWallUv(wall, activeDraft.WallHeight, hit.point);
			BuildingOpeningData opening = BuildOpeningFromHit(wall.wallIndex, uv, wall.length, activeDraft.WallHeight, mode == ToolMode.CreateWindow ? BuildingOpeningType.Window : BuildingOpeningType.Door);

			Undo.RecordObject(activeDraft, "Create Opening");
			activeDraft.EnsureValidOpeningIds();
			opening.id = Guid.NewGuid().ToString("N");
			activeDraft.Openings.Add(opening);
			ClampOpeningToWall(activeDraft, opening);
			selectedOpeningId = opening.id;
			RebuildBuilding(activeDraft);
			evt.Use();
		}

		private BuildingOpeningData BuildOpeningFromHit(int wallIndex, Vector2 hitUv, float wallLength, float wallHeight, BuildingOpeningType type)
		{
			if (type == BuildingOpeningType.Window)
			{
				const float defaultWindowSizeMeters = 1.0f;
				return new BuildingOpeningData
				{
					wallIndex = wallIndex,
					type = BuildingOpeningType.Window,
					center = hitUv,
					size = new Vector2(defaultWindowSizeMeters, defaultWindowSizeMeters),
					showTrim = activeDraft.SpawnWindowTrim,
					showSill = activeDraft.SpawnWindowSill,
					showGlass = activeDraft.SpawnWindowGlass
				};
			}

			float standardDoorWidth = 1.0f;
			float standardDoorHeight = 2.1f;
			float widthDoor = Mathf.Min(standardDoorWidth, wallLength * 0.85f);
			float heightDoor = Mathf.Min(standardDoorHeight, wallHeight * 0.9f);

			return new BuildingOpeningData
			{
				wallIndex = wallIndex,
				type = BuildingOpeningType.Door,
				center = hitUv,
				size = new Vector2(Mathf.Max(0.6f, widthDoor), Mathf.Max(1.0f, heightDoor)),
				showTrim = activeDraft.SpawnDoorTrim,
				showSill = false,
				showGlass = false,
				showDoor = activeDraft.SpawnDoorPanelInDoorOpenings
			};
		}

		private void ClampOpeningToWall(BuildingDraftData draft, BuildingOpeningData opening)
		{
			if (!TryGetWallInfo(opening.wallIndex, out WallRuntimeInfo wall))
			{
				BuildWallRuntimeCache(draft);
				if (!TryGetWallInfo(opening.wallIndex, out wall))
				{
					return;
				}
			}

			float minSize = 0.2f;
			float width = Mathf.Clamp(opening.size.x, minSize, wall.length * 0.95f);
			float height = Mathf.Clamp(opening.size.y, minSize, draft.WallHeight * 0.95f);

			float halfW = width * 0.5f;
			float halfH = height * 0.5f;

			float cx = Mathf.Clamp(opening.center.x, halfW, Mathf.Max(halfW, wall.length - halfW));
			float cy = Mathf.Clamp(opening.center.y, halfH, Mathf.Max(halfH, draft.WallHeight - halfH));

			opening.size = new Vector2(width, height);
			opening.center = new Vector2(cx, cy);
		}

		private void EnsureDraftIsValid(BuildingDraftData draft)
		{
			if (draft.FootprintCorners.Count < 3)
			{
				draft.RegenerateRegularFootprint(4, 4f);
			}

			draft.EnsureWallMaterialCount();
			draft.EnsureValidOpeningIds();
		}

		private void TryBindSelection()
		{
			activeDraft = null;
			Transform selected = Selection.activeTransform;
			if (selected == null)
			{
				return;
			}

			activeDraft = selected.GetComponentInParent<BuildingDraftData>();
			if (activeDraft != null)
			{
				EnsureDraftIsValid(activeDraft);
				BuildWallRuntimeCache(activeDraft);
				selectedWallIndex = Mathf.Clamp(selectedWallIndex, 0, Math.Max(0, activeDraft.FootprintCorners.Count - 1));
			}
		}

		private void CreateNewDraft()
		{
			GameObject root = new GameObject(GetUniqueDraftName());
			Undo.RegisterCreatedObjectUndo(root, "Create Building Draft");

			BuildingDraftData draft = root.AddComponent<BuildingDraftData>();
			draft.RegenerateRegularFootprint(regularSideCount, regularRadius);
			AssignUnityDefaultMaterials(draft);

			activeDraft = draft;
			Selection.activeObject = root;
			mode = ToolMode.EditBuilding;
			RebuildBuilding(draft);
		}

		private void DuplicateDraft(BuildingDraftData source)
		{
			GameObject duplicate = Instantiate(source.gameObject, source.transform.position + Vector3.right * 2f, source.transform.rotation);
			duplicate.name = GetUniqueDraftName();
			Undo.RegisterCreatedObjectUndo(duplicate, "Duplicate Building Draft");
			activeDraft = duplicate.GetComponent<BuildingDraftData>();
			Selection.activeObject = duplicate;
			RebuildBuilding(activeDraft);
		}

		private string GetUniqueDraftName()
		{
			if (GameObject.Find(DefaultRootName) == null)
			{
				return DefaultRootName;
			}

			int index = 1;
			while (GameObject.Find($"{DefaultRootName}_{index}") != null)
			{
				index++;
			}

			return $"{DefaultRootName}_{index}";
		}

		private void RebuildBuilding(BuildingDraftData draft)
		{
			if (draft == null)
			{
				return;
			}

			EnsureDraftIsValid(draft);
			BuildWallRuntimeCache(draft);

			Transform wallsContainer = EnsureChild(draft.transform, WallsContainerName);
			Transform wallJoinersContainer = EnsureChild(draft.transform, WallJoinersContainerName);
			Transform openingsContainer = EnsureChild(draft.transform, OpeningsContainerName);
			Transform ceilingTransform = EnsureChild(draft.transform, CeilingObjectName);
			Transform floorTransform = EnsureChild(draft.transform, FloorObjectName);
			Transform interiorFloorSegmentsContainer = EnsureChild(draft.transform, InteriorFloorSegmentsContainerName);
			ClearOptionalContainer(draft.transform, CeilingJoinersContainerName);
			ClearOptionalContainer(draft.transform, FloorJoinersContainerName);

			RebuildWalls(draft, wallsContainer);
			RebuildWallJoiners(draft, wallJoinersContainer);
			RebuildCeiling(draft, ceilingTransform);
			RebuildFloor(draft, floorTransform);
			RebuildInteriorFloorSegments(draft, interiorFloorSegmentsContainer);
			RebuildOpeningVisuals(draft, openingsContainer);

			EditorUtility.SetDirty(draft);
			SceneView.RepaintAll();
			Repaint();
		}

		private void RebuildWalls(BuildingDraftData draft, Transform wallsContainer)
		{
			BuildWallRuntimeCache(draft);
			while (wallsContainer.childCount > wallRuntimeInfo.Count)
			{
				Undo.DestroyObjectImmediate(wallsContainer.GetChild(wallsContainer.childCount - 1).gameObject);
			}

			for (int i = 0; i < wallRuntimeInfo.Count; i++)
			{
				WallRuntimeInfo info = wallRuntimeInfo[i];
				GameObject wallObject;
				if (i < wallsContainer.childCount)
				{
					wallObject = wallsContainer.GetChild(i).gameObject;
				}
				else
				{
					wallObject = new GameObject();
					wallObject.transform.SetParent(wallsContainer);
				}

				wallObject.name = $"Wall_{i}";
				wallObject.transform.position = info.worldPosition;
				wallObject.transform.rotation = info.worldRotation;
				wallObject.transform.localScale = Vector3.one;

				MeshFilter meshFilter = GetOrAddComponent<MeshFilter>(wallObject);
				MeshRenderer meshRenderer = GetOrAddComponent<MeshRenderer>(wallObject);
				MeshCollider meshCollider = GetOrAddComponent<MeshCollider>(wallObject);

				Mesh wallMesh = BuildWallMesh(info.length, draft.WallHeight, draft.WallThickness, GetOpeningsForWall(draft, i), info.startMiter, info.endMiter, draft.WallUvScaleMultiplier);
				meshFilter.sharedMesh = wallMesh;
				meshCollider.sharedMesh = wallMesh;
				meshRenderer.sharedMaterial = draft.GetWallMaterial(i);
			}
		}

		private void RebuildWallJoiners(BuildingDraftData draft, Transform joinersContainer)
		{
			if (draft.WallJoinerStyle == BuildingJoinerStyle.Sharp)
			{
				if (BuildingCornerUtility.TryRebuildSharpCornerCaps(draft, joinersContainer))
				{
					return;
				}
			}

			int count = draft.FootprintCorners.Count;
			Vector2 footprintCenter = Vector2.zero;
			for (int i = 0; i < count; i++)
			{
				footprintCenter += draft.FootprintCorners[i];
			}
			footprintCenter /= Mathf.Max(1, count);

			while (joinersContainer.childCount > count)
			{
				Undo.DestroyObjectImmediate(joinersContainer.GetChild(joinersContainer.childCount - 1).gameObject);
			}

			for (int i = 0; i < count; i++)
			{
				int prevIndex = (i - 1 + count) % count;
				int nextIndex = (i + 1) % count;
				Vector2 corner2 = draft.FootprintCorners[i];
				Vector2 prev2 = draft.FootprintCorners[prevIndex];
				Vector2 next2 = draft.FootprintCorners[nextIndex];
				Vector3 corner = new Vector3(corner2.x, 0f, corner2.y);
				Vector3 prevInsetDir = (new Vector3(prev2.x, 0f, prev2.y) - corner).normalized;
				Vector3 nextInsetDir = (new Vector3(next2.x, 0f, next2.y) - corner).normalized;
				Vector3 prevWallDir = (corner - new Vector3(prev2.x, 0f, prev2.y)).normalized;
				Vector3 nextWallDir = (new Vector3(next2.x, 0f, next2.y) - corner).normalized;
				Vector3 prevOutward = ComputeOutwardLocal(prevWallDir, corner, footprintCenter);
				Vector3 nextOutward = ComputeOutwardLocal(nextWallDir, corner, footprintCenter);

				if (!TryGetWallInfo(prevIndex, out WallRuntimeInfo prevWall) || !TryGetWallInfo(i, out WallRuntimeInfo nextWall))
				{
					continue;
				}

				float prevTrim = Mathf.Max(0f, -prevWall.endMiter);
				float nextTrim = Mathf.Max(0f, -nextWall.startMiter);
				if (prevTrim < 0.001f || nextTrim < 0.001f)
				{
					continue;
				}

				GameObject joinerObject;
				if (i < joinersContainer.childCount)
				{
					joinerObject = joinersContainer.GetChild(i).gameObject;
				}
				else
				{
					joinerObject = new GameObject();
					joinerObject.transform.SetParent(joinersContainer);
				}

				joinerObject.name = $"WallJoiner_{i}";
				joinerObject.transform.SetParent(joinersContainer);
				joinerObject.transform.localPosition = new Vector3(corner.x, draft.WallHeight * 0.5f, corner.z);
				joinerObject.transform.localRotation = Quaternion.identity;
				joinerObject.transform.localScale = Vector3.one;

				Material material = draft.GetWallMaterial(i);
				MeshFilter meshFilter = GetOrAddComponent<MeshFilter>(joinerObject);
				MeshRenderer meshRenderer = GetOrAddComponent<MeshRenderer>(joinerObject);
				MeshCollider meshCollider = GetOrAddComponent<MeshCollider>(joinerObject);
				Mesh joinerMesh = BuildBeveledWallJoinerMesh(
					prevInsetDir * prevTrim,
					nextInsetDir * nextTrim,
					prevOutward,
					nextOutward,
					draft.WallThickness * 0.5f,
					draft.WallHeight,
					draft.CurvedJoinerSegments,
					draft.WallUvScaleMultiplier,
					$"WallJoinerMesh_{i}");
				meshFilter.sharedMesh = joinerMesh;
				meshCollider.sharedMesh = joinerMesh;
				meshRenderer.sharedMaterial = material;
			}
		}

		private void RebuildCeiling(BuildingDraftData draft, Transform ceilingTransform)
		{
			ceilingTransform.SetParent(draft.transform);
			ceilingTransform.localPosition = new Vector3(0f, draft.BuildingHeight - draft.RoofThickness * 0.5f - draft.InsetRoofOffset, 0f);
			ceilingTransform.localRotation = Quaternion.identity;
			ceilingTransform.localScale = Vector3.one;

			MeshFilter meshFilter = GetOrAddComponent<MeshFilter>(ceilingTransform.gameObject);
			MeshRenderer meshRenderer = GetOrAddComponent<MeshRenderer>(ceilingTransform.gameObject);

			List<Vector2> innerCorners = GetInnerOffsetFootprint(draft.FootprintCorners, draft.WallThickness * 0.5f);
			Mesh ceilingMesh = BuildSurfacePrismMesh(innerCorners, draft.RoofThickness, "CeilingMesh");
			meshFilter.sharedMesh = ceilingMesh;
			ApplyMeshCollider(ceilingTransform.gameObject, ceilingMesh);
			meshRenderer.sharedMaterial = draft.CeilingMaterial != null ? draft.CeilingMaterial : draft.DefaultWallMaterial;
		}

		private void RebuildCeilingJoiners(BuildingDraftData draft, Transform joinersContainer)
		{
			if (draft.CeilingJoinerStyle == BuildingJoinerStyle.Sharp)
			{
				for (int i = joinersContainer.childCount - 1; i >= 0; i--)
				{
					Undo.DestroyObjectImmediate(joinersContainer.GetChild(i).gameObject);
				}
				return;
			}

			int count = draft.FootprintCorners.Count;
			while (joinersContainer.childCount > count)
			{
				Undo.DestroyObjectImmediate(joinersContainer.GetChild(joinersContainer.childCount - 1).gameObject);
			}

			for (int i = 0; i < count; i++)
			{
				GameObject joinerObject;
				if (i < joinersContainer.childCount)
				{
					joinerObject = joinersContainer.GetChild(i).gameObject;
				}
				else
				{
					joinerObject = new GameObject();
					joinerObject.transform.SetParent(joinersContainer);
				}

				joinerObject.name = $"CeilingJoiner_{i}";
				Vector3 localCorner = new Vector3(draft.FootprintCorners[i].x, draft.BuildingHeight - draft.RoofThickness * 0.5f, draft.FootprintCorners[i].y);
				joinerObject.transform.position = draft.transform.TransformPoint(localCorner);
				joinerObject.transform.rotation = draft.transform.rotation;
				joinerObject.transform.localScale = Vector3.one;

				Material material = draft.CeilingMaterial != null ? draft.CeilingMaterial : draft.DefaultWallMaterial;
				ApplyJoinerMesh(joinerObject, draft.CeilingJoinerStyle, draft.WallThickness * JoinerSizeMultiplier, draft.RoofThickness, draft.CurvedJoinerSegments, draft.WallUvScaleMultiplier, material, $"CeilingJoinerMesh_{i}");
			}
		}

		private void RebuildFloorJoiners(BuildingDraftData draft, Transform joinersContainer)
		{
			if (draft.WallJoinerStyle == BuildingJoinerStyle.Sharp)
			{
				for (int i = joinersContainer.childCount - 1; i >= 0; i--)
				{
					Undo.DestroyObjectImmediate(joinersContainer.GetChild(i).gameObject);
				}
				return;
			}

			int count = draft.FootprintCorners.Count;
			while (joinersContainer.childCount > count)
			{
				Undo.DestroyObjectImmediate(joinersContainer.GetChild(joinersContainer.childCount - 1).gameObject);
			}

			for (int i = 0; i < count; i++)
			{
				GameObject joinerObject;
				if (i < joinersContainer.childCount)
				{
					joinerObject = joinersContainer.GetChild(i).gameObject;
				}
				else
				{
					joinerObject = new GameObject();
					joinerObject.transform.SetParent(joinersContainer);
				}

				joinerObject.name = $"FloorJoiner_{i}";
				Vector3 localCorner = new Vector3(draft.FootprintCorners[i].x, draft.FloorThickness * 0.5f, draft.FootprintCorners[i].y);
				joinerObject.transform.position = draft.transform.TransformPoint(localCorner);
				joinerObject.transform.rotation = draft.transform.rotation;
				joinerObject.transform.localScale = Vector3.one;

				Material material = draft.FloorMaterial != null ? draft.FloorMaterial : draft.DefaultWallMaterial;
				ApplyJoinerMesh(joinerObject, draft.WallJoinerStyle, draft.WallThickness * JoinerSizeMultiplier, draft.FloorThickness, draft.CurvedJoinerSegments, draft.WallUvScaleMultiplier, material, $"FloorJoinerMesh_{i}");
			}
		}

		private void RebuildFloor(BuildingDraftData draft, Transform floorTransform)
		{
			floorTransform.SetParent(draft.transform);
			floorTransform.localPosition = new Vector3(0f, draft.FloorThickness * 0.5f, 0f);
			floorTransform.localRotation = Quaternion.identity;
			floorTransform.localScale = Vector3.one;

			MeshFilter meshFilter = GetOrAddComponent<MeshFilter>(floorTransform.gameObject);
			MeshRenderer meshRenderer = GetOrAddComponent<MeshRenderer>(floorTransform.gameObject);

			List<Vector2> innerCorners = GetInnerOffsetFootprint(draft.FootprintCorners, draft.WallThickness * 0.5f);
			Mesh floorMesh = BuildSurfacePrismMesh(innerCorners, draft.FloorThickness, "FloorMesh");
			meshFilter.sharedMesh = floorMesh;
			ApplyMeshCollider(floorTransform.gameObject, floorMesh);
			meshRenderer.sharedMaterial = draft.FloorMaterial != null ? draft.FloorMaterial : draft.DefaultWallMaterial;
		}

		private void RebuildInteriorFloorSegments(BuildingDraftData draft, Transform segmentsContainer)
		{
			int levels = Mathf.Max(1, draft.BuildingFloors);
			int segmentCount = Mathf.Max(0, levels - 1);

			// Capture existing floor-door data so edits survive rebuilds.
			Dictionary<int, BuildingOpeningData> previousFloorDoorsByWallIndex = new Dictionary<int, BuildingOpeningData>();
			for (int i = 0; i < draft.Openings.Count; i++)
			{
				BuildingOpeningData opening = draft.Openings[i];
				if (opening.wallIndex < 0 && !previousFloorDoorsByWallIndex.ContainsKey(opening.wallIndex))
				{
					previousFloorDoorsByWallIndex.Add(opening.wallIndex, opening);
				}
			}

			// First, remove all existing floor doors from openings
			for (int i = draft.Openings.Count - 1; i >= 0; i--)
			{
				if (draft.Openings[i].wallIndex < 0)
				{
					draft.Openings.RemoveAt(i);
				}
			}

			while (segmentsContainer.childCount > segmentCount)
			{
				Undo.DestroyObjectImmediate(segmentsContainer.GetChild(segmentsContainer.childCount - 1).gameObject);
			}

			if (segmentCount == 0)
			{
				return;
			}

			List<Vector2> innerCorners = GetInnerOffsetFootprint(draft.FootprintCorners, draft.WallThickness * 0.5f);
			float levelHeight = draft.BuildingHeight / levels;

			for (int i = 0; i < segmentCount; i++)
			{
				GameObject segmentObject;
				if (i < segmentsContainer.childCount)
				{
					segmentObject = segmentsContainer.GetChild(i).gameObject;
				}
				else
				{
					segmentObject = new GameObject();
					segmentObject.transform.SetParent(segmentsContainer);
				}

				segmentObject.name = $"InteriorFloor_{i + 1}";
				segmentObject.transform.localPosition = new Vector3(0f, levelHeight * (i + 1) - draft.FloorThickness * 0.5f, 0f);
				segmentObject.transform.localRotation = Quaternion.identity;
				segmentObject.transform.localScale = Vector3.one;

				MeshFilter meshFilter = GetOrAddComponent<MeshFilter>(segmentObject);
				MeshRenderer meshRenderer = GetOrAddComponent<MeshRenderer>(segmentObject);

				// Create a door opening for this floor if enabled
				BuildingOpeningData floorDoor = null;
				if (draft.EnableFloorDoors)
				{
					int floorDoorWallIndex = -(i + 2); // floor 0 = -2, floor 1 = -3, etc.
					if (previousFloorDoorsByWallIndex.TryGetValue(floorDoorWallIndex, out BuildingOpeningData previousFloorDoor))
					{
						floorDoor = new BuildingOpeningData
						{
							id = string.IsNullOrEmpty(previousFloorDoor.id) ? Guid.NewGuid().ToString("N") : previousFloorDoor.id,
							wallIndex = floorDoorWallIndex,
							type = BuildingOpeningType.Door,
							center = previousFloorDoor.center,
							size = new Vector2(Mathf.Max(0.2f, previousFloorDoor.size.x), Mathf.Max(0.2f, previousFloorDoor.size.y)),
							showTrim = false,
							showSill = false,
							showGlass = false,
							showDoor = false
						};
					}
					else
					{
						floorDoor = new BuildingOpeningData
						{
							id = Guid.NewGuid().ToString("N"),
							wallIndex = floorDoorWallIndex,
							type = BuildingOpeningType.Door,
							center = Vector2.zero,
							size = new Vector2(1.0f, 1.0f),
							showTrim = false,
							showSill = false,
							showGlass = false,
							showDoor = false
						};
					}

					Undo.RecordObject(draft, "Add Floor Door");
					draft.Openings.Add(floorDoor);
				}

				// Build floor mesh - with holes if doors are enabled
				Mesh floorMesh;
				if (draft.EnableFloorDoors && floorDoor != null)
				{
					List<BuildingOpeningData> floorOpenings = new List<BuildingOpeningData> { floorDoor };
					floorMesh = BuildFloorMeshWithOpenings(innerCorners, draft.FloorThickness, floorOpenings, $"InteriorFloorMesh_{i + 1}");
				}
				else
				{
					floorMesh = BuildSurfacePrismMesh(innerCorners, draft.FloorThickness, $"InteriorFloorMesh_{i + 1}");
				}

				meshFilter.sharedMesh = floorMesh;
				ApplyMeshCollider(segmentObject, floorMesh);
				meshRenderer.sharedMaterial = draft.FloorMaterial != null ? draft.FloorMaterial : draft.DefaultWallMaterial;

				// Generate stairs if enabled
				if (draft.EnableFloorStairs && floorDoor != null)
				{
					RebuildFloorStairs(draft, segmentObject.transform, floorDoor, innerCorners, levelHeight);
				}
			}
		}

		private void RebuildFloorStairs(BuildingDraftData draft, Transform floorParent, BuildingOpeningData doorOpening, List<Vector2> footprint, float levelHeight)
		{
			// Get the door dimensions and position
			GetOpeningMinMax(doorOpening, out float minX, out float maxX, out float minY, out float maxY);
			
			Vector2 doorSize = new Vector2(maxX - minX, maxY - minY);
			float doorLengthX = doorSize.x;
			float doorLengthZ = doorSize.y;

			// Determine which direction is longer and use that for stairs
			bool stairsAlongX = doorLengthX >= doorLengthZ;
			float stairDistance = stairsAlongX ? doorLengthX : doorLengthZ;
			
			// Calculate number of steps based on step height
			int stepCount = Mathf.Max(1, Mathf.RoundToInt(levelHeight / draft.StairStepHeight));
			float actualStepHeight = levelHeight / stepCount;
			float floorTopLocalY = draft.FloorThickness * 0.5f;

			// Create stairs container if it doesn't exist
			Transform stairsContainer = floorParent.Find("Stairs");
			if (stairsContainer == null)
			{
				GameObject stairsObject = new GameObject("Stairs");
				stairsObject.transform.SetParent(floorParent);
				stairsObject.transform.localPosition = Vector3.zero;
				stairsObject.transform.localRotation = Quaternion.identity;
				stairsContainer = stairsObject.transform;
			}

			// Clear existing stairs
			for (int i = stairsContainer.childCount - 1; i >= 0; i--)
			{
				Undo.DestroyObjectImmediate(stairsContainer.GetChild(i).gameObject);
			}

			// Generate step cuboids along one dimension
			for (int step = 0; step < stepCount; step++)
			{
				GameObject stepObject = new GameObject($"Step_{step + 1}");
				stepObject.transform.SetParent(stairsContainer);
				stepObject.transform.localPosition = Vector3.zero;
				stepObject.transform.localRotation = Quaternion.identity;

				MeshFilter meshFilter = GetOrAddComponent<MeshFilter>(stepObject);
				MeshRenderer meshRenderer = GetOrAddComponent<MeshRenderer>(stepObject);
				BoxCollider boxCollider = GetOrAddComponent<BoxCollider>(stepObject);

				// Start at the current floor top and descend to the level below.
				float stepHeightStart = floorTopLocalY - actualStepHeight * step;
				float stepHeightEnd = floorTopLocalY - actualStepHeight * (step + 1);

				Vector3 stepMin, stepMax;
				
				if (stairsAlongX)
				{
					// Stairs go along X axis
					float stepXStart = minX + (stairDistance / stepCount) * step;
					float stepXEnd = minX + (stairDistance / stepCount) * (step + 1);
					
					stepMin = new Vector3(stepXStart, Mathf.Min(stepHeightStart, stepHeightEnd), minY);
					stepMax = new Vector3(stepXEnd, Mathf.Max(stepHeightStart, stepHeightEnd), maxY);
				}
				else
				{
					// Stairs go along Z axis
					float stepZStart = minY + (stairDistance / stepCount) * step;
					float stepZEnd = minY + (stairDistance / stepCount) * (step + 1);
					
					stepMin = new Vector3(minX, Mathf.Min(stepHeightStart, stepHeightEnd), stepZStart);
					stepMax = new Vector3(maxX, Mathf.Max(stepHeightStart, stepHeightEnd), stepZEnd);
				}

				Mesh stepMesh = BuildCenteredBoxMesh(stepMax.x - stepMin.x, stepMax.y - stepMin.y, stepMax.z - stepMin.z, $"StepMesh_{step + 1}");
				meshFilter.sharedMesh = stepMesh;
				meshRenderer.sharedMaterial = draft.DefaultWallMaterial;
				boxCollider.isTrigger = false;
				boxCollider.center = Vector3.zero;
				boxCollider.size = new Vector3(
					Mathf.Max(0.01f, stepMax.x - stepMin.x),
					Mathf.Max(0.01f, stepMax.y - stepMin.y),
					Mathf.Max(0.01f, stepMax.z - stepMin.z));

				// Position the step in world space
				Vector3 stepCenter = (stepMin + stepMax) * 0.5f;
				stepObject.transform.localPosition = stepCenter;
				stepObject.transform.localRotation = Quaternion.identity;
			}

			if (stairsContainer.childCount > 0)
			{
				stairsContainer.localPosition = Vector3.zero;
				stairsContainer.localRotation = Quaternion.identity;
			}
		}

		private void RebuildOpeningVisuals(BuildingDraftData draft, Transform openingsContainer)
		{
			Dictionary<string, BuildingOpeningData> openingById = new Dictionary<string, BuildingOpeningData>();
			for (int i = 0; i < draft.Openings.Count; i++)
			{
				openingById[draft.Openings[i].id] = draft.Openings[i];
			}

			for (int i = openingsContainer.childCount - 1; i >= 0; i--)
			{
				Transform child = openingsContainer.GetChild(i);
				if (!openingById.ContainsKey(child.name))
				{
					Undo.DestroyObjectImmediate(child.gameObject);
				}
			}

			for (int i = 0; i < draft.Openings.Count; i++)
			{
				BuildingOpeningData opening = draft.Openings[i];

				Transform openingRoot = openingsContainer.Find(opening.id);
				if (openingRoot == null)
				{
					GameObject openingObject = new GameObject(opening.id);
					openingObject.transform.SetParent(openingsContainer);
					openingRoot = openingObject.transform;
				}

				// Handle floor doors (negative wallIndex)
				if (opening.wallIndex < 0)
				{
					PositionFloorOpening(openingRoot, opening);
					openingRoot.localScale = Vector3.one;
					RebuildOpeningDecoration(draft, opening, openingRoot, 10f); // Large dummy wall length for floor doors
				}
				else if (TryGetWallInfo(opening.wallIndex, out WallRuntimeInfo wall))
				{
					// Handle wall openings
					ClampOpeningToWall(draft, opening);
					PositionOpeningRoot(openingRoot, wall, draft.WallHeight, opening.center);
					openingRoot.localScale = Vector3.one;
					RebuildOpeningDecoration(draft, opening, openingRoot, wall.length);
				}
			}
		}

		private void RebuildOpeningDecoration(BuildingDraftData draft, BuildingOpeningData opening, Transform openingRoot, float wallLength)
		{
			float configuredInset = opening.type == BuildingOpeningType.Window ? draft.WindowFrameInset : draft.DoorFrameInset;
			EnsureDecorationChild(openingRoot, "Trim", opening.showTrim, draft.TrimMaterial, BuildTrimMesh(opening.size, draft.WallThickness, configuredInset));

			bool showSill = opening.type == BuildingOpeningType.Window && opening.showSill;
			EnsureDecorationChild(openingRoot, "Sill", showSill, draft.TrimMaterial, BuildSillMesh(opening.size, draft.WallThickness, draft.WindowSillThickness));

			bool showGlass = opening.type == BuildingOpeningType.Window && opening.showGlass;
			EnsureDecorationChild(openingRoot, "Glass", showGlass, draft.GlassMaterial, BuildGlassMesh(opening.size, draft.WallThickness));

			bool showDoor = opening.type == BuildingOpeningType.Door && opening.wallIndex >= 0 && opening.showDoor;
			Material doorMaterial = draft.DoorMaterial != null ? draft.DoorMaterial : draft.DefaultWallMaterial;
			EnsureDecorationChild(openingRoot, "Door", showDoor, doorMaterial, BuildingMeshUtility.BuildDoorPanelMesh(opening.size, draft.WallThickness));
		}

		private void EnsureDecorationChild(Transform parent, string childName, bool enabled, Material material, Mesh mesh)
		{
			Transform child = parent.Find(childName);
			if (!enabled)
			{
				if (child != null)
				{
					Undo.DestroyObjectImmediate(child.gameObject);
				}
				return;
			}

			if (child == null)
			{
				GameObject childObject = new GameObject(childName);
				childObject.transform.SetParent(parent);
				childObject.transform.localPosition = Vector3.zero;
				childObject.transform.localRotation = Quaternion.identity;
				childObject.transform.localScale = Vector3.one;
				child = childObject.transform;
			}

			MeshFilter meshFilter = GetOrAddComponent<MeshFilter>(child.gameObject);
			MeshRenderer meshRenderer = GetOrAddComponent<MeshRenderer>(child.gameObject);
			meshFilter.sharedMesh = mesh;
			meshRenderer.sharedMaterial = material;

			if ((childName == "Door" || childName == "Glass") && mesh != null)
			{
				BoxCollider boxCollider = GetOrAddComponent<BoxCollider>(child.gameObject);
				boxCollider.size = mesh.bounds.size;
				boxCollider.center = mesh.bounds.center;
			}
		}

		private void PositionOpeningRoot(Transform openingRoot, WallRuntimeInfo wall, float wallHeight, Vector2 uv)
		{
			Vector3 localPoint = new Vector3(uv.x - wall.length * 0.5f, uv.y - wallHeight * 0.5f, 0f);
			openingRoot.position = wall.worldCenterlinePosition + wall.worldRotation * localPoint;
			openingRoot.rotation = wall.worldRotation;
		}

		private void PositionFloorOpening(Transform openingRoot, BuildingOpeningData opening)
		{
			// Position floor doors flat on the floor at the center
			openingRoot.localPosition = Vector3.zero;
			openingRoot.localRotation = Quaternion.identity;
		}

		private void BuildWallRuntimeCache(BuildingDraftData draft)
		{
			BuildingWallRuntimeUtility.BuildWallRuntimeCache(draft, wallRuntimeInfo);
		}

		private static float ComputeCornerMiterExtension(Vector3 inDir, Vector3 outDir, float halfThickness)
		{
			return BuildingWallRuntimeUtility.ComputeCornerMiterExtension(inDir, outDir, halfThickness);
		}

		private bool TryGetWallInfo(int wallIndex, out WallRuntimeInfo info)
		{
			return BuildingWallRuntimeUtility.TryGetWallInfo(wallIndex, wallRuntimeInfo, out info);
		}

		private bool TryGetWallHit(Ray ray, out RaycastHit hit, out WallRuntimeInfo wall)
		{
			return BuildingWallRuntimeUtility.TryGetWallHit(ray, activeDraft, wallRuntimeInfo, out hit, out wall);
		}

		private int ParseWallIndex(string name)
		{
			return BuildingWallRuntimeUtility.ParseWallIndex(name);
		}

		private static Vector2 WorldToWallUv(WallRuntimeInfo wall, float wallHeight, Vector3 world)
		{
			return BuildingWallRuntimeUtility.WorldToWallUv(wall, wallHeight, world);
		}

		private static Vector3 WallUvToWorld(WallRuntimeInfo wall, float wallHeight, float u, float v, float z)
		{
			return BuildingWallRuntimeUtility.WallUvToWorld(wall, wallHeight, u, v, z);
		}

		private static void GetOpeningMinMax(BuildingOpeningData opening, out float minX, out float maxX, out float minY, out float maxY)
		{
			BuildingEditorUtility.GetOpeningMinMax(opening, out minX, out maxX, out minY, out maxY);
		}

		private List<BuildingOpeningData> GetOpeningsForWall(BuildingDraftData draft, int wallIndex)
		{
			List<BuildingOpeningData> result = new List<BuildingOpeningData>();
			for (int i = 0; i < draft.Openings.Count; i++)
			{
				if (draft.Openings[i].wallIndex == wallIndex)
				{
					result.Add(draft.Openings[i]);
				}
			}

			return result;
		}

		private Mesh BuildWallMesh(float length, float height, float thickness, List<BuildingOpeningData> openings, float startExtra, float endExtra, float uvScaleMultiplier)
		{
			float meshLength = length + startExtra + endExtra;
			List<float> xCuts = new List<float> { 0f, meshLength };
			List<float> yCuts = new List<float> { 0f, height };

			for (int i = 0; i < openings.Count; i++)
			{
				GetOpeningMinMax(openings[i], out float minX, out float maxX, out float minY, out float maxY);
				minX += startExtra;
				maxX += startExtra;
				minX = Mathf.Clamp(minX, 0f, meshLength);
				maxX = Mathf.Clamp(maxX, 0f, meshLength);
				minY = Mathf.Clamp(minY, 0f, height);
				maxY = Mathf.Clamp(maxY, 0f, height);

				xCuts.Add(minX);
				xCuts.Add(maxX);
				yCuts.Add(minY);
				yCuts.Add(maxY);
			}

			SortAndUnique(xCuts);
			SortAndUnique(yCuts);

			List<Vector3> vertices = new List<Vector3>();
			List<int> triangles = new List<int>();
			List<Vector3> normals = new List<Vector3>();
			List<Vector2> uvs = new List<Vector2>();
			List<MeshPartInfo> parts = new List<MeshPartInfo>();

			for (int xi = 0; xi < xCuts.Count - 1; xi++)
			{
				float x0 = xCuts[xi];
				float x1 = xCuts[xi + 1];
				if (x1 - x0 < 0.001f)
				{
					continue;
				}

				for (int yi = 0; yi < yCuts.Count - 1; yi++)
				{
					float y0 = yCuts[yi];
					float y1 = yCuts[yi + 1];
					if (y1 - y0 < 0.001f)
					{
						continue;
					}

					float cx = (x0 + x1) * 0.5f;
					float cy = (y0 + y1) * 0.5f;
					if (IsPointInsideOpeningShifted(cx, cy, openings, startExtra))
					{
						continue;
					}

					Vector3 min = new Vector3(x0 - meshLength * 0.5f, y0 - height * 0.5f, -thickness * 0.5f);
					Vector3 max = new Vector3(x1 - meshLength * 0.5f, y1 - height * 0.5f, thickness * 0.5f);
					AddBoxPart(vertices, triangles, normals, uvs, min, max, parts);
				}
			}

			OrientTrianglesOutwardPerPart(vertices, triangles, parts);

			Mesh mesh = new Mesh();
			mesh.name = "WallMesh";
			mesh.SetVertices(vertices);
			mesh.SetTriangles(triangles, 0);
			mesh.SetNormals(normals);
			mesh.SetUVs(0, uvs);
			BuildingMeshUtility.ApplyWorldScaleQuadUvsAndPackedLightmap(mesh, uvScaleMultiplier);
			mesh.RecalculateNormals();
			mesh.RecalculateBounds();
			return mesh;
		}

		private Mesh BuildSurfacePrismMesh(List<Vector2> corners, float thickness, string meshName)
		{
			List<Vector3> vertices = new List<Vector3>();
			List<int> triangles = new List<int>();
			List<Vector3> normals = new List<Vector3>();
			List<Vector2> uvs = new List<Vector2>();
			List<MeshPartInfo> parts = new List<MeshPartInfo>();

			int count = corners.Count;
			if (count < 3)
			{
				return new Mesh();
			}

			if (!TryTriangulatePolygon(corners, out List<int> polygonTriangles))
			{
				return new Mesh();
			}

			float half = thickness * 0.5f;
			Vector3 prismCenter = ComputePolygonCenter3D(corners, 0f);

			for (int i = 0; i < count; i++)
			{
				vertices.Add(new Vector3(corners[i].x, half, corners[i].y));
				normals.Add(Vector3.up);
				uvs.Add(corners[i]);
			}

			for (int i = 0; i < count; i++)
			{
				vertices.Add(new Vector3(corners[i].x, -half, corners[i].y));
				normals.Add(Vector3.down);
				uvs.Add(corners[i]);
			}

			for (int i = 0; i < polygonTriangles.Count; i += 3)
			{
				int triStart = triangles.Count;
				triangles.Add(polygonTriangles[i]);
				triangles.Add(polygonTriangles[i + 2]);
				triangles.Add(polygonTriangles[i + 1]);
				parts.Add(new MeshPartInfo { triangleStart = triStart, triangleCount = 3, center = prismCenter });
			}

			int bottomOffset = count;
			for (int i = 0; i < polygonTriangles.Count; i += 3)
			{
				int triStart = triangles.Count;
				triangles.Add(bottomOffset + polygonTriangles[i]);
				triangles.Add(bottomOffset + polygonTriangles[i + 1]);
				triangles.Add(bottomOffset + polygonTriangles[i + 2]);
				parts.Add(new MeshPartInfo { triangleStart = triStart, triangleCount = 3, center = prismCenter });
			}

			for (int i = 0; i < count; i++)
			{
				int next = (i + 1) % count;
				Vector3 aTop = new Vector3(corners[i].x, half, corners[i].y);
				Vector3 bTop = new Vector3(corners[next].x, half, corners[next].y);
				Vector3 aBottom = new Vector3(corners[i].x, -half, corners[i].y);
				Vector3 bBottom = new Vector3(corners[next].x, -half, corners[next].y);

				Vector3 faceNormal = Vector3.Cross(bTop - aTop, bBottom - aTop).normalized;

				int start = vertices.Count;
				vertices.Add(aTop);
				vertices.Add(bTop);
				vertices.Add(bBottom);
				vertices.Add(aBottom);

				normals.Add(faceNormal);
				normals.Add(faceNormal);
				normals.Add(faceNormal);
				normals.Add(faceNormal);

				uvs.Add(new Vector2(0f, 1f));
				uvs.Add(new Vector2(1f, 1f));
				uvs.Add(new Vector2(1f, 0f));
				uvs.Add(new Vector2(0f, 0f));

				int triStart = triangles.Count;
				triangles.Add(start);
				triangles.Add(start + 1);
				triangles.Add(start + 2);
				triangles.Add(start);
				triangles.Add(start + 2);
				triangles.Add(start + 3);
				parts.Add(new MeshPartInfo { triangleStart = triStart, triangleCount = 6, center = prismCenter });
			}

			OrientTrianglesOutwardPerPart(vertices, triangles, parts);

			Mesh mesh = new Mesh();
			mesh.name = meshName;
			mesh.SetVertices(vertices);
			mesh.SetTriangles(triangles, 0);
			mesh.SetNormals(normals);
			mesh.SetUVs(0, uvs);
			mesh.RecalculateNormals();
			mesh.RecalculateBounds();
			return mesh;
		}

		private Mesh BuildFloorMeshWithOpenings(List<Vector2> corners, float thickness, List<BuildingOpeningData> openings, string meshName)
		{
			// Calculate bounding box
			if (corners == null || corners.Count < 3)
			{
				return BuildSurfacePrismMesh(corners, thickness, meshName);
			}

			float minX = corners[0].x, maxX = corners[0].x;
			float minZ = corners[0].y, maxZ = corners[0].y;
			for (int i = 1; i < corners.Count; i++)
			{
				minX = Mathf.Min(minX, corners[i].x);
				maxX = Mathf.Max(maxX, corners[i].x);
				minZ = Mathf.Min(minZ, corners[i].y);
				maxZ = Mathf.Max(maxZ, corners[i].y);
			}

			// Create grid
			float cellSize = 0.2f; // Adjust grid resolution
			List<float> xCuts = new List<float>();
			List<float> zCuts = new List<float>();

			for (float x = minX; x <= maxX; x += cellSize)
			{
				xCuts.Add(x);
			}
			if (xCuts[xCuts.Count - 1] < maxX)
			{
				xCuts.Add(maxX);
			}

			for (float z = minZ; z <= maxZ; z += cellSize)
			{
				zCuts.Add(z);
			}
			if (zCuts[zCuts.Count - 1] < maxZ)
			{
				zCuts.Add(maxZ);
			}

			// Force exact cut lines at opening bounds so the hole perimeter is not staircase-approximated.
			for (int i = 0; i < openings.Count; i++)
			{
				GetOpeningMinMax(openings[i], out float oMinX, out float oMaxX, out float oMinY, out float oMaxY);
				xCuts.Add(Mathf.Clamp(oMinX, minX, maxX));
				xCuts.Add(Mathf.Clamp(oMaxX, minX, maxX));
				zCuts.Add(Mathf.Clamp(oMinY, minZ, maxZ));
				zCuts.Add(Mathf.Clamp(oMaxY, minZ, maxZ));
			}

			SortAndUnique(xCuts);
			SortAndUnique(zCuts);

			List<Vector3> vertices = new List<Vector3>();
			List<int> triangles = new List<int>();
			List<Vector3> normals = new List<Vector3>();
			List<Vector2> uvs = new List<Vector2>();
			List<MeshPartInfo> parts = new List<MeshPartInfo>();

			Vector3 prismCenter = ComputePolygonCenter3D(corners, 0f);
			float half = thickness * 0.5f;

			for (int xi = 0; xi < xCuts.Count - 1; xi++)
			{
				for (int zi = 0; zi < zCuts.Count - 1; zi++)
				{
					float x0 = xCuts[xi];
					float x1 = xCuts[xi + 1];
					float z0 = zCuts[zi];
					float z1 = zCuts[zi + 1];

					Vector2 cellCenter = new Vector2((x0 + x1) * 0.5f, (z0 + z1) * 0.5f);

					// Check if cell center is inside the footprint polygon
					if (!IsPointInsidePolygon(cellCenter, corners))
					{
						continue;
					}

					// Remove any cell that overlaps an opening area.
					bool insideOpening = false;
					const float eps = 0.0001f;
					for (int i = 0; i < openings.Count; i++)
					{
						GetOpeningMinMax(openings[i], out float oMinX, out float oMaxX, out float oMinY, out float oMaxY);
						bool overlapsOpening = x0 < oMaxX - eps && x1 > oMinX + eps && z0 < oMaxY - eps && z1 > oMinY + eps;
						if (overlapsOpening)
						{
							insideOpening = true;
							break;
						}
					}

					if (insideOpening)
					{
						continue;
					}

					// Add box for this cell
					Vector3 min = new Vector3(x0, -half, z0);
					Vector3 max = new Vector3(x1, half, z1);
					int triStart = triangles.Count;
					AddBox(vertices, triangles, normals, uvs, min, max);
					parts.Add(new MeshPartInfo { triangleStart = triStart, triangleCount = triangles.Count - triStart, center = prismCenter });
				}
			}

			// Add interior walls for openings
			for (int oi = 0; oi < openings.Count; oi++)
			{
				GetOpeningMinMax(openings[oi], out float oMinX, out float oMaxX, out float oMinY, out float oMaxY);
				Vector3 holeCenter = new Vector3((oMinX + oMaxX) * 0.5f, 0f, (oMinY + oMaxY) * 0.5f);
				
				// Create walls around the opening perimeter
				// Each wall is wound so its normal points inward toward the opening center.
				Vector3[] topCorners = new Vector3[4]
				{
					new Vector3(oMinX, half, oMinY),
					new Vector3(oMinX, half, oMaxY),
					new Vector3(oMaxX, half, oMaxY),
					new Vector3(oMaxX, half, oMinY)
				};

				Vector3[] bottomCorners = new Vector3[4]
				{
					new Vector3(oMinX, -half, oMinY),
					new Vector3(oMinX, -half, oMaxY),
					new Vector3(oMaxX, -half, oMaxY),
					new Vector3(oMaxX, -half, oMinY)
				};

				// Create 4 walls around the opening (inward-facing)
				// Wall 0: X min face
				AddQuadFacingPoint(vertices, triangles, normals, uvs, topCorners[0], topCorners[1], bottomCorners[1], bottomCorners[0], holeCenter);

				// Wall 1: Z max face
				AddQuadFacingPoint(vertices, triangles, normals, uvs, topCorners[1], topCorners[2], bottomCorners[2], bottomCorners[1], holeCenter);

				// Wall 2: X max face
				AddQuadFacingPoint(vertices, triangles, normals, uvs, topCorners[3], topCorners[2], bottomCorners[2], bottomCorners[3], holeCenter);

				// Wall 3: Z min face
				AddQuadFacingPoint(vertices, triangles, normals, uvs, topCorners[0], topCorners[3], bottomCorners[3], bottomCorners[0], holeCenter);
			}

			OrientTrianglesOutwardPerPart(vertices, triangles, parts);

			Mesh mesh = new Mesh();
			mesh.name = meshName;
			mesh.SetVertices(vertices);
			mesh.SetTriangles(triangles, 0);
			mesh.SetNormals(normals);
			mesh.SetUVs(0, uvs);
			mesh.RecalculateNormals();
			mesh.RecalculateBounds();
			return mesh;
		}

		private bool SplitWallAtDistance(BuildingDraftData draft, int wallIndex, float distanceAlongWall)
		{
			if (draft == null)
			{
				return false;
			}

			int oldCornerCount = draft.FootprintCorners.Count;
			if (oldCornerCount < 3 || wallIndex < 0 || wallIndex >= oldCornerCount)
			{
				return false;
			}

			if (WallHasWindow(draft, wallIndex))
			{
				EditorUtility.DisplayDialog("Split Blocked", "Cannot split this wall because a window is defined on it.", "OK");
				return false;
			}

			int nextCornerIndex = wallIndex == oldCornerCount - 1 ? 0 : wallIndex + 1;
			int insertCornerIndex = wallIndex == oldCornerCount - 1 ? oldCornerCount : wallIndex + 1;

			Vector2 a = draft.FootprintCorners[wallIndex];
			Vector2 b = draft.FootprintCorners[nextCornerIndex];
			float wallLength = Vector2.Distance(a, b);
			if (wallLength < 0.001f)
			{
				return false;
			}

			float minSplitGap = Mathf.Min(0.2f, wallLength * 0.25f);
			float splitDistance = distanceAlongWall <= 0f
				? wallLength * 0.5f
				: Mathf.Clamp(distanceAlongWall, minSplitGap, wallLength - minSplitGap);
			float t = splitDistance / wallLength;
			Vector2 splitPoint = Vector2.Lerp(a, b, t);

			Undo.RecordObject(draft, "Split Wall");

			List<Material> oldMaterials = new List<Material>(oldCornerCount);
			draft.EnsureWallMaterialCount();
			for (int i = 0; i < oldCornerCount; i++)
			{
				oldMaterials.Add(draft.GetWallMaterial(i));
			}

			draft.FootprintCorners.Insert(insertCornerIndex, splitPoint);
			draft.EnsureWallMaterialCount();
			int newWallCount = draft.FootprintCorners.Count;
			for (int i = 0; i < newWallCount; i++)
			{
				Material mapped;
				if (i <= wallIndex)
				{
					mapped = oldMaterials[i];
				}
				else if (i == wallIndex + 1)
				{
					mapped = oldMaterials[wallIndex];
				}
				else
				{
					mapped = oldMaterials[i - 1];
				}

				draft.SetWallMaterial(i, mapped);
			}

			for (int i = 0; i < draft.Openings.Count; i++)
			{
				BuildingOpeningData opening = draft.Openings[i];
				if (opening.wallIndex == wallIndex)
				{
					if (opening.center.x > splitDistance)
					{
						opening.wallIndex = wallIndex + 1;
						opening.center = new Vector2(opening.center.x - splitDistance, opening.center.y);
					}
				}
				else if (wallIndex < oldCornerCount - 1 && opening.wallIndex > wallIndex)
				{
					opening.wallIndex += 1;
				}
			}

			selectedWallIndex = wallIndex + 1;
			RebuildBuilding(draft);
			return true;
		}

		private static bool WallHasWindow(BuildingDraftData draft, int wallIndex)
		{
			for (int i = 0; i < draft.Openings.Count; i++)
			{
				BuildingOpeningData opening = draft.Openings[i];
				if (opening.wallIndex == wallIndex && opening.type == BuildingOpeningType.Window)
				{
					return true;
				}
			}

			return false;
		}

		private Mesh BuildTrimMesh(Vector2 openingSize, float wallThickness, float configuredInset)
		{
			List<Vector3> vertices = new List<Vector3>();
			List<int> triangles = new List<int>();
			List<Vector3> normals = new List<Vector3>();
			List<Vector2> uvs = new List<Vector2>();
			List<MeshPartInfo> parts = new List<MeshPartInfo>();

			float halfW = openingSize.x * 0.5f;
			float halfH = openingSize.y * 0.5f;
			float depth = wallThickness + WindowTrimDepth;
			float trim = WindowTrimWidth;
			float maxInset = Mathf.Max(0.001f, Mathf.Min(openingSize.x, openingSize.y) * 0.249f);
			float inset = Mathf.Clamp(configuredInset, 0.001f, maxInset);
			float innerHalfW = Mathf.Max(0.01f, halfW - inset);
			float innerHalfH = Mathf.Max(0.01f, halfH - inset);

			AddBoxPart(vertices, triangles, normals, uvs, new Vector3(-halfW - trim, innerHalfH, -depth * 0.5f), new Vector3(halfW + trim, halfH + trim, depth * 0.5f), parts);
			AddBoxPart(vertices, triangles, normals, uvs, new Vector3(-halfW - trim, -halfH - trim, -depth * 0.5f), new Vector3(halfW + trim, -innerHalfH, depth * 0.5f), parts);
			AddBoxPart(vertices, triangles, normals, uvs, new Vector3(-halfW - trim, -innerHalfH, -depth * 0.5f), new Vector3(-innerHalfW, innerHalfH, depth * 0.5f), parts);
			AddBoxPart(vertices, triangles, normals, uvs, new Vector3(innerHalfW, -innerHalfH, -depth * 0.5f), new Vector3(halfW + trim, innerHalfH, depth * 0.5f), parts);

			OrientTrianglesOutwardPerPart(vertices, triangles, parts);

			Mesh mesh = new Mesh();
			mesh.name = "OpeningTrim";
			mesh.SetVertices(vertices);
			mesh.SetTriangles(triangles, 0);
			mesh.SetNormals(normals);
			mesh.SetUVs(0, uvs);
			mesh.RecalculateNormals();
			mesh.RecalculateBounds();
			return mesh;
		}

		private Mesh BuildSillMesh(Vector2 openingSize, float wallThickness, float sillThickness)
		{
			List<Vector3> vertices = new List<Vector3>();
			List<int> triangles = new List<int>();
			List<Vector3> normals = new List<Vector3>();
			List<Vector2> uvs = new List<Vector2>();
			List<MeshPartInfo> parts = new List<MeshPartInfo>();

			float halfW = openingSize.x * 0.5f;
			float height = Mathf.Max(0.005f, sillThickness);
			float y = -openingSize.y * 0.5f - WindowSillCenterOffset;
			float depth = wallThickness + WindowSillDepth;

			AddBoxPart(
				vertices,
				triangles,
				normals,
				uvs,
				new Vector3(-halfW - WindowTrimWidth * 0.4f, y - height * 0.5f, -depth * 0.5f),
				new Vector3(halfW + WindowTrimWidth * 0.4f, y + height * 0.5f, depth * 0.5f),
				parts);

			OrientTrianglesOutwardPerPart(vertices, triangles, parts);

			Mesh mesh = new Mesh();
			mesh.name = "WindowSill";
			mesh.SetVertices(vertices);
			mesh.SetTriangles(triangles, 0);
			mesh.SetNormals(normals);
			mesh.SetUVs(0, uvs);
			mesh.RecalculateNormals();
			mesh.RecalculateBounds();
			return mesh;
		}

		private Mesh BuildGlassMesh(Vector2 openingSize, float wallThickness)
		{
			List<Vector3> vertices = new List<Vector3>();
			List<int> triangles = new List<int>();
			List<Vector3> normals = new List<Vector3>();
			List<Vector2> uvs = new List<Vector2>();
			List<MeshPartInfo> parts = new List<MeshPartInfo>();

			float margin = Mathf.Min(0.01f, Mathf.Min(openingSize.x, openingSize.y) * 0.02f);
			float halfW = Mathf.Max(0.01f, openingSize.x * 0.5f - margin);
			float halfH = Mathf.Max(0.01f, openingSize.y * 0.5f - margin);
			float thickness = Mathf.Min(0.03f, wallThickness * 0.2f);

			AddBoxPart(
				vertices,
				triangles,
				normals,
				uvs,
				new Vector3(-halfW, -halfH, -thickness * 0.5f),
				new Vector3(halfW, halfH, thickness * 0.5f),
				parts);

			OrientTrianglesOutwardPerPart(vertices, triangles, parts);

			Mesh mesh = new Mesh();
			mesh.name = "WindowGlass";
			mesh.SetVertices(vertices);
			mesh.SetTriangles(triangles, 0);
			mesh.SetNormals(normals);
			mesh.SetUVs(0, uvs);
			mesh.RecalculateNormals();
			mesh.RecalculateBounds();
			return mesh;
		}

		private static bool IsPointInsideOpening(float x, float y, List<BuildingOpeningData> openings)
		{
			for (int i = 0; i < openings.Count; i++)
			{
				GetOpeningMinMax(openings[i], out float minX, out float maxX, out float minY, out float maxY);
				if (x > minX && x < maxX && y > minY && y < maxY)
				{
					return true;
				}
			}

			return false;
		}

		private static bool IsPointInsideOpeningShifted(float x, float y, List<BuildingOpeningData> openings, float xShift)
		{
			for (int i = 0; i < openings.Count; i++)
			{
				GetOpeningMinMax(openings[i], out float minX, out float maxX, out float minY, out float maxY);
				minX += xShift;
				maxX += xShift;
				if (x > minX && x < maxX && y > minY && y < maxY)
				{
					return true;
				}
			}

			return false;
		}

		private static void SortAndUnique(List<float> list)
		{
			list.Sort();
			for (int i = list.Count - 2; i >= 0; i--)
			{
				if (Mathf.Abs(list[i] - list[i + 1]) < 0.0001f)
				{
					list.RemoveAt(i + 1);
				}
			}
		}

		private static bool TryTriangulatePolygon(List<Vector2> polygon, out List<int> triangles)
		{
			triangles = new List<int>();
			int count = polygon.Count;
			if (count < 3)
			{
				return false;
			}

			List<int> verts = new List<int>(count);
			for (int i = 0; i < count; i++)
			{
				verts.Add(i);
			}

			if (SignedArea(polygon) < 0f)
			{
				verts.Reverse();
			}

			int guard = 0;
			while (verts.Count > 3 && guard < 10000)
			{
				guard++;
				bool earFound = false;

				for (int i = 0; i < verts.Count; i++)
				{
					int prev = verts[(i - 1 + verts.Count) % verts.Count];
					int curr = verts[i];
					int next = verts[(i + 1) % verts.Count];

					if (!IsConvex(polygon[prev], polygon[curr], polygon[next]))
					{
						continue;
					}

					bool hasPointInside = false;
					for (int j = 0; j < verts.Count; j++)
					{
						int test = verts[j];
						if (test == prev || test == curr || test == next)
						{
							continue;
						}

						if (PointInTriangle(polygon[test], polygon[prev], polygon[curr], polygon[next]))
						{
							hasPointInside = true;
							break;
						}
					}

					if (hasPointInside)
					{
						continue;
					}

					triangles.Add(prev);
					triangles.Add(curr);
					triangles.Add(next);
					verts.RemoveAt(i);
					earFound = true;
					break;
				}

				if (!earFound)
				{
					return false;
				}
			}

			if (verts.Count == 3)
			{
				triangles.Add(verts[0]);
				triangles.Add(verts[1]);
				triangles.Add(verts[2]);
				return true;
			}

			return false;
		}

		private static float SignedArea(List<Vector2> polygon)
		{
			float area = 0f;
			for (int i = 0; i < polygon.Count; i++)
			{
				Vector2 a = polygon[i];
				Vector2 b = polygon[(i + 1) % polygon.Count];
				area += a.x * b.y - b.x * a.y;
			}

			return area * 0.5f;
		}

		private static bool IsConvex(Vector2 a, Vector2 b, Vector2 c)
		{
			return ((b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x)) > 0f;
		}

		private static bool PointInTriangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
		{
			float area = Mathf.Abs((b.x - a.x) * (c.y - a.y) - (c.x - a.x) * (b.y - a.y));
			float area1 = Mathf.Abs((a.x - p.x) * (b.y - p.y) - (b.x - p.x) * (a.y - p.y));
			float area2 = Mathf.Abs((b.x - p.x) * (c.y - p.y) - (c.x - p.x) * (b.y - p.y));
			float area3 = Mathf.Abs((c.x - p.x) * (a.y - p.y) - (a.x - p.x) * (c.y - p.y));
			return Mathf.Abs(area - (area1 + area2 + area3)) < 0.0001f;
		}

		private static bool IsPointInsidePolygon(Vector2 point, List<Vector2> polygon)
		{
			if (polygon == null || polygon.Count < 3)
			{
				return false;
			}

			bool inside = false;
			for (int i = 0, j = polygon.Count - 1; i < polygon.Count; j = i++)
			{
				Vector2 a = polygon[i];
				Vector2 b = polygon[j];
				bool crosses = (a.y > point.y) != (b.y > point.y);
				if (crosses)
				{
					float denominator = b.y - a.y;
					if (Mathf.Abs(denominator) < 0.000001f)
					{
						continue;
					}

					float x = ((b.x - a.x) * (point.y - a.y) / denominator) + a.x;
					if (point.x < x)
					{
						inside = !inside;
					}
				}
			}

			return inside;
		}

		private static Vector2 SnapVector2(Vector2 value, float size)
		{
			return BuildingEditorUtility.SnapVector2(value, size);
		}

		private static Vector3 ComputeOutwardLocal(Vector3 wallDir, Vector3 corner, Vector2 footprintCenter)
		{
			Vector3 outward = Vector3.Cross(Vector3.up, wallDir).normalized;
			Vector3 centerOffset = corner - new Vector3(footprintCenter.x, 0f, footprintCenter.y);
			if (Vector3.Dot(outward, centerOffset) < 0f)
			{
				outward *= -1f;
			}

			return outward;
		}

		private static void ApplyJoinerMesh(GameObject joinerObject, BuildingJoinerStyle style, float thickness, float height, int curvedSegments, float uvScaleMultiplier, Material material, string meshName)
		{
			MeshFilter meshFilter = GetOrAddComponent<MeshFilter>(joinerObject);
			MeshRenderer meshRenderer = GetOrAddComponent<MeshRenderer>(joinerObject);
			MeshCollider meshCollider = GetOrAddComponent<MeshCollider>(joinerObject);
			joinerObject.transform.localScale = Vector3.one;

			Mesh mesh;
			switch (style)
			{
				case BuildingJoinerStyle.Beveled:
					mesh = BuildCenteredBoxMesh(thickness, height, thickness, meshName);
					break;
				case BuildingJoinerStyle.Curved:
					mesh = BuildCenteredBoxMesh(thickness, height, thickness, meshName);
					break;
				default:
					mesh = BuildCenteredBoxMesh(thickness, height, thickness, meshName);
					break;
			}

			BuildingMeshUtility.ApplyWorldScaleQuadUvsAndPackedLightmap(mesh, uvScaleMultiplier);

			meshFilter.sharedMesh = mesh;
			meshCollider.sharedMesh = mesh;
			meshRenderer.sharedMaterial = material;
		}

		private Mesh BuildBeveledWallJoinerMesh(Vector3 prevInset, Vector3 nextInset, Vector3 prevOutward, Vector3 nextOutward, float halfThickness, float height, int segments, float uvScaleMultiplier, string meshName)
		{
			Vector3 outerPrev = prevInset + prevOutward * halfThickness;
			Vector3 outerNext = nextInset + nextOutward * halfThickness;
			Vector3 innerPrev = prevInset - prevOutward * halfThickness;
			Vector3 innerNext = nextInset - nextOutward * halfThickness;
			Vector3 prevTowardCorner = prevInset.sqrMagnitude > 0.0001f ? -prevInset.normalized : Vector3.zero;
			Vector3 nextTowardCorner = nextInset.sqrMagnitude > 0.0001f ? -nextInset.normalized : Vector3.zero;

			if ((outerNext - outerPrev).sqrMagnitude < 0.0001f || (innerNext - innerPrev).sqrMagnitude < 0.0001f)
			{
				return new Mesh { name = meshName };
			}

			Vector3 outerControl = TryIntersectLines2D(
				new Vector2(outerPrev.x, outerPrev.z),
				new Vector2(prevTowardCorner.x, prevTowardCorner.z),
				new Vector2(outerNext.x, outerNext.z),
				new Vector2(nextTowardCorner.x, nextTowardCorner.z),
				out Vector2 outerControl2)
				? new Vector3(outerControl2.x, 0f, outerControl2.y)
				: (outerPrev + outerNext) * 0.5f;

			Vector3 innerControl = TryIntersectLines2D(
				new Vector2(innerPrev.x, innerPrev.z),
				new Vector2(prevTowardCorner.x, prevTowardCorner.z),
				new Vector2(innerNext.x, innerNext.z),
				new Vector2(nextTowardCorner.x, nextTowardCorner.z),
				out Vector2 innerControl2)
				? new Vector3(innerControl2.x, 0f, innerControl2.y)
				: (innerPrev + innerNext) * 0.5f;

			segments = Mathf.Clamp(segments, 1, 64);
			float halfHeight = height * 0.5f;
			List<Vector3> vertices = new List<Vector3>();
			List<int> triangles = new List<int>();
			List<Vector3> normals = new List<Vector3>();
			List<Vector2> uvs = new List<Vector2>();

			Vector3[] outerTop = new Vector3[segments + 1];
			Vector3[] outerBottom = new Vector3[segments + 1];
			Vector3[] innerTop = new Vector3[segments + 1];
			Vector3[] innerBottom = new Vector3[segments + 1];
			for (int i = 0; i <= segments; i++)
			{
				float t = i / (float)segments;
				Vector3 outerPoint = EvaluateQuadraticBezier(outerNext, outerControl, outerPrev, t);
				Vector3 innerPoint = EvaluateQuadraticBezier(innerNext, innerControl, innerPrev, t);
				outerTop[i] = new Vector3(outerPoint.x, halfHeight, outerPoint.z);
				outerBottom[i] = new Vector3(outerPoint.x, -halfHeight, outerPoint.z);
				innerTop[i] = new Vector3(innerPoint.x, halfHeight, innerPoint.z);
				innerBottom[i] = new Vector3(innerPoint.x, -halfHeight, innerPoint.z);
			}

			for (int i = 0; i < segments; i++)
			{
				AddQuad(vertices, triangles, normals, uvs, outerTop[i], outerTop[i + 1], innerTop[i + 1], innerTop[i], Vector3.up);
				AddQuad(vertices, triangles, normals, uvs, innerBottom[i], innerBottom[i + 1], outerBottom[i + 1], outerBottom[i], Vector3.down);
				Vector3 outerNormal = Vector3.Cross(outerTop[i + 1] - outerTop[i], outerBottom[i] - outerTop[i]).normalized;
				Vector3 innerNormal = Vector3.Cross(innerTop[i] - innerTop[i + 1], innerBottom[i + 1] - innerTop[i + 1]).normalized;
				AddQuad(vertices, triangles, normals, uvs, outerTop[i], outerTop[i + 1], outerBottom[i + 1], outerBottom[i], outerNormal);
				AddQuad(vertices, triangles, normals, uvs, innerTop[i + 1], innerTop[i], innerBottom[i], innerBottom[i + 1], innerNormal);
			}

			Vector3 prevCapNormal = Vector3.Cross(outerTop[segments] - innerTop[segments], innerBottom[segments] - innerTop[segments]).normalized;
			Vector3 nextCapNormal = Vector3.Cross(innerTop[0] - outerTop[0], outerBottom[0] - outerTop[0]).normalized;
			AddQuad(vertices, triangles, normals, uvs, outerTop[0], innerTop[0], innerBottom[0], outerBottom[0], nextCapNormal);
			AddQuad(vertices, triangles, normals, uvs, innerTop[segments], outerTop[segments], outerBottom[segments], innerBottom[segments], prevCapNormal);

			Mesh mesh = new Mesh();
			mesh.name = meshName;
			mesh.SetVertices(vertices);
			mesh.SetTriangles(triangles, 0);
			mesh.SetNormals(normals);
			mesh.SetUVs(0, uvs);
			BuildingMeshUtility.ApplyWorldScaleQuadUvsAndPackedLightmap(mesh, uvScaleMultiplier);
			Vector3 meshCenter = (outerPrev + outerNext + innerPrev + innerNext) * 0.25f;
			EnsureMeshFacesPointOutward(mesh, meshCenter);
			return mesh;
		}

		private static Vector3 EvaluateQuadraticBezier(Vector3 a, Vector3 b, Vector3 c, float t)
		{
			float oneMinusT = 1f - t;
			return oneMinusT * oneMinusT * a + 2f * oneMinusT * t * b + t * t * c;
		}

		private static Mesh BuildCenteredBoxMesh(float width, float height, float depth, string meshName)
		{
			return BuildingMeshUtility.BuildCenteredBoxMesh(width, height, depth, meshName);
		}

		private static Mesh BuildQuarterCornerMesh(float radius, float height, int segments, string meshName)
		{
			return BuildingMeshUtility.BuildQuarterCornerMesh(radius, height, segments, meshName);
		}

		private static Mesh BuildCylinderMesh(float radius, float height, int segments, string meshName)
		{
			return BuildingMeshUtility.BuildCylinderMesh(radius, height, segments, meshName);
		}

		private static List<Vector2> GetOuterOffsetFootprint(List<Vector2> corners, float offset)
		{
			List<Vector2> plus = GetOffsetFootprint(corners, Mathf.Abs(offset));
			List<Vector2> minus = GetOffsetFootprint(corners, -Mathf.Abs(offset));

			float plusArea = Mathf.Abs(SignedArea(plus));
			float minusArea = Mathf.Abs(SignedArea(minus));
			return plusArea >= minusArea ? plus : minus;
		}

		private static List<Vector2> GetInnerOffsetFootprint(List<Vector2> corners, float offset)
		{
			List<Vector2> plus = GetOffsetFootprint(corners, Mathf.Abs(offset));
			List<Vector2> minus = GetOffsetFootprint(corners, -Mathf.Abs(offset));

			float plusArea = Mathf.Abs(SignedArea(plus));
			float minusArea = Mathf.Abs(SignedArea(minus));
			return plusArea <= minusArea ? plus : minus;
		}

		private static List<Vector2> GetOffsetFootprint(List<Vector2> corners, float offset)
		{
			List<Vector2> result = new List<Vector2>(corners.Count);
			if (corners == null || corners.Count == 0)
			{
				return result;
			}

			for (int i = 0; i < corners.Count; i++)
			{
				int prev = (i - 1 + corners.Count) % corners.Count;
				int next = (i + 1) % corners.Count;

				Vector2 pPrev = corners[prev];
				Vector2 p = corners[i];
				Vector2 pNext = corners[next];

				Vector2 d1 = (p - pPrev).normalized;
				Vector2 d2 = (pNext - p).normalized;

				Vector2 n1 = new Vector2(-d1.y, d1.x);
				Vector2 n2 = new Vector2(-d2.y, d2.x);

				Vector2 l1Point = p + n1 * offset;
				Vector2 l2Point = p + n2 * offset;

				if (TryIntersectLines2D(l1Point, d1, l2Point, d2, out Vector2 intersection))
				{
					result.Add(intersection);
				}
				else
				{
					Vector2 fallback = (n1 + n2);
					if (fallback.sqrMagnitude < 0.000001f)
					{
						fallback = n1.sqrMagnitude > 0f ? n1 : n2;
					}
					result.Add(p + fallback.normalized * offset);
				}
			}

			return result;
		}

		private static bool TryIntersectLines2D(Vector2 p1, Vector2 d1, Vector2 p2, Vector2 d2, out Vector2 intersection)
		{
			float cross = d1.x * d2.y - d1.y * d2.x;
			if (Mathf.Abs(cross) < 0.00001f)
			{
				intersection = Vector2.zero;
				return false;
			}

			Vector2 diff = p2 - p1;
			float t = (diff.x * d2.y - diff.y * d2.x) / cross;
			intersection = p1 + d1 * t;
			return true;
		}

		// Call this from BuildWallRuntimeCache or equivalent
		private void ComputeWallRuntimeInfoWithMiters(BuildingDraftData draft)
		{
			wallRuntimeInfo.Clear();
			int count = draft.FootprintCorners.Count;
			if (count < 2) return;

			// Precompute world-space corner positions
			Vector3[] worldCorners = new Vector3[count];
			for (int i = 0; i < count; i++)
			{
				Vector3 local = new Vector3(draft.FootprintCorners[i].x, 0f, draft.FootprintCorners[i].y);
				worldCorners[i] = draft.transform.TransformPoint(local);
			}

			float halfThickness = draft.WallThickness * 0.5f;

			for (int i = 0; i < count; i++)
			{
				int prev = (i - 1 + count) % count;
				int next = (i + 1) % count;

				Vector3 p = worldCorners[i];
				Vector3 pPrev = worldCorners[prev];
				Vector3 pNext = worldCorners[next];

				// Directions along centerlines (from corner to next/prev)
				Vector3 dirToNext = (pNext - p).normalized;
				Vector3 dirToPrev = (pPrev - p).normalized;

				// Wall index is the wall that starts at this corner and goes to next corner
				int wallIndex = i;

				// Compute centerline for this wall (start corner p to end corner pNext)
				Vector3 wallCenter = (p + pNext) * 0.5f;
				Vector3 wallDir = (pNext - p);
				float wallLength = wallDir.magnitude;
				Quaternion wallRot = Quaternion.LookRotation(wallDir.normalized, Vector3.up);

				// Default miters (distance along wall centerline to trim)
				float startMiter = 0f;
				float endMiter = 0f;

				// Determine joiner style for this corner (use the corner's joiner style)
				BuildingJoinerStyle joinerStyle = draft.WallJoinerStyle;

				if (joinerStyle == BuildingJoinerStyle.Sharp)
				{
					// Force a 45-degree miter: offset along each wall by halfThickness / sin(45) = halfThickness * sqrt(2)
					// But we want the distance along the centerline to move the wall endpoint inward so the outer faces meet at 45deg.
					// For centerline-based walls, the miter distance along centerline = halfThickness / tan(45/2) is not needed;
					// simpler: compute intersection of two offset lines at halfThickness outward and measure distance along centerline.
					// For a guaranteed 45° visual miter, we can use halfThickness * Mathf.Sqrt(2).
					float miterDistance = halfThickness * Mathf.Sqrt(2f);

					// startMiter is how far to move the start point forward along the wall (toward the wall center)
					startMiter = miterDistance;
					// endMiter is how far to move the end point backward along the wall (toward the wall center)
					endMiter = miterDistance;
				}
				else // Beveled
				{
					// For beveled corners we will trim walls to meet the bevel boundary.
					// We'll compute a bevel radius (distance from corner along each wall centerline)
					// and set start/end miter distances to that radius.
					// Use CurvedJoinerSegments to control the bevel geometry later.
					float bevelRadius = halfThickness * 1.0f; // base radius; you can scale if you want a larger bevel
															  // Optionally scale bevelRadius by a user parameter; here we keep it simple.
					startMiter = bevelRadius;
					endMiter = bevelRadius;
				}

				// Build runtime info for this wall (start at corner p, end at pNext)
				WallRuntimeInfo info = new WallRuntimeInfo
				{
					wallIndex = wallIndex,
					worldPosition = wallCenter,
					worldCenterlinePosition = wallCenter,
					worldRotation = wallRot,
					length = wallLength,
					startMiter = startMiter,
					endMiter = endMiter,
					startLocal = draft.transform.InverseTransformPoint(p),
					endLocal = draft.transform.InverseTransformPoint(pNext)
				};

				wallRuntimeInfo.Add(info);
			}
		}

		// Returns points along the bevel from wall A to wall B in world space.
		// cornerPos: world-space corner point
		// dirA: normalized direction along wall A away from corner (centerline direction)
		// dirB: normalized direction along wall B away from corner
		// radius: distance from corner along each wall centerline to start the bevel
		// segments: number of segments for the bevel arc (>=1)
		private static Vector3[] GenerateBevelPoints(Vector3 cornerPos, Vector3 dirA, Vector3 dirB, float radius, int segments)
		{
			// Compute the two start points along each wall centerline
			Vector3 pA = cornerPos + dirA * radius;
			Vector3 pB = cornerPos + dirB * radius;

			// Compute outward normals for each wall (assuming up is Vector3.up)
			Vector3 normalA = Vector3.Cross(Vector3.up, dirA).normalized;
			Vector3 normalB = Vector3.Cross(Vector3.up, dirB).normalized;

			// Outer edge points (outer face) at half thickness offset
			// If you need the bevel to follow the outer face, offset by half thickness along normals.
			// For a simple bevel between centerlines, we interpolate between pA and pB.

			// Build a simple linear/arc interpolation in the plane defined by dirA and dirB.
			// Compute angle between dirA and dirB around up axis
			float angleA = Mathf.Atan2(dirA.z, dirA.x);
			float angleB = Mathf.Atan2(dirB.z, dirB.x);

			// Normalize angles so angleB > angleA (choose the smaller arc that goes from A to B)
			float delta = Mathf.DeltaAngle(angleA * Mathf.Rad2Deg, angleB * Mathf.Rad2Deg) * Mathf.Deg2Rad;
			// If delta is negative, go the other way
			if (delta < 0f)
			{
				// swap so we always go positive
				float tmp = angleA;
				angleA = angleB;
				angleB = tmp;
				delta = -delta;
			}

			// Use a circular arc centered at cornerPos with radius 'radius'
			Vector3[] points = new Vector3[Mathf.Max(2, segments + 1)];
			for (int i = 0; i < points.Length; i++)
			{
				float t = (float)i / (points.Length - 1);
				float ang = angleA + delta * t;
				Vector3 p = cornerPos + new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang)) * radius;
				points[i] = p;
			}

			return points;
		}


		private static Vector3 ComputePolygonCenter3D(List<Vector2> corners, float y)
		{
			if (corners == null || corners.Count == 0)
			{
				return new Vector3(0f, y, 0f);
			}

			Vector2 center = Vector2.zero;
			for (int i = 0; i < corners.Count; i++)
			{
				center += corners[i];
			}
			center /= corners.Count;
			return new Vector3(center.x, y, center.y);
		}

		private static void EnsureMeshFacesPointOutward(Mesh mesh, Vector3 center)
		{
			BuildingMeshUtility.EnsureMeshFacesPointOutward(mesh, center);
		}

		private static void EnsureMeshFacesPointTowardCenter(Mesh mesh, Vector3 center)
		{
			BuildingMeshUtility.EnsureMeshFacesPointTowardCenter(mesh, center);
		}

		private static Material GetUnityDefaultMaterial()
		{
			return AssetDatabase.GetBuiltinExtraResource<Material>("Default-Material.mat");
		}

		private static void AssignUnityDefaultMaterials(BuildingDraftData draft)
		{
			Material defaultMat = GetUnityDefaultMaterial();
			if (defaultMat == null || draft == null)
			{
				return;
			}

			draft.DefaultWallMaterial = defaultMat;
			draft.CeilingMaterial = defaultMat;
			draft.FloorMaterial = defaultMat;
			draft.TrimMaterial = defaultMat;
			draft.GlassMaterial = defaultMat;
			draft.DoorMaterial = defaultMat;
			draft.EnsureWallMaterialCount();
			for (int i = 0; i < draft.FootprintCorners.Count; i++)
			{
				draft.SetWallMaterial(i, defaultMat);
			}
		}

		private void ExportActiveDraftAsObjMtl()
		{
			if (activeDraft == null)
			{
				EditorUtility.DisplayDialog("Export OBJ/MTL", "No active building to export.", "OK");
				return;
			}

			string objPath = EditorUtility.SaveFilePanel("Export Building As OBJ", Application.dataPath, activeDraft.name, "obj");
			if (string.IsNullOrEmpty(objPath))
			{
				return;
			}

			string folder = Path.GetDirectoryName(objPath);
			if (string.IsNullOrEmpty(folder))
			{
				EditorUtility.DisplayDialog("Export OBJ/MTL", "Invalid export path.", "OK");
				return;
			}

			string objFileName = Path.GetFileName(objPath);
			string mtlFileName = Path.GetFileNameWithoutExtension(objPath) + ".mtl";
			string mtlPath = Path.Combine(folder, mtlFileName);

			MeshFilter[] filters = activeDraft.GetComponentsInChildren<MeshFilter>(true);
			if (filters == null || filters.Length == 0)
			{
				EditorUtility.DisplayDialog("Export OBJ/MTL", "No meshes found under the active building.", "OK");
				return;
			}

			StringBuilder obj = new StringBuilder(1024 * 64);
			StringBuilder mtl = new StringBuilder(1024 * 16);
			HashSet<string> writtenMaterials = new HashSet<string>();

			obj.AppendLine($"# Exported from Building Modeling Tool: {activeDraft.name}");
			obj.AppendLine($"mtllib {mtlFileName}");

			int vertexOffset = 0;
			int uvOffset = 0;
			int normalOffset = 0;

			Matrix4x4 rootWorldToLocal = activeDraft.transform.worldToLocalMatrix;

			for (int fi = 0; fi < filters.Length; fi++)
			{
				MeshFilter filter = filters[fi];
				if (filter == null || filter.sharedMesh == null)
				{
					continue;
				}

				MeshRenderer renderer = filter.GetComponent<MeshRenderer>();
				if (renderer == null)
				{
					continue;
				}

				Mesh mesh = filter.sharedMesh;
				Vector3[] vertices = mesh.vertices;
				Vector3[] normals = mesh.normals;
				Vector2[] uvs = mesh.uv;

				Matrix4x4 localToRoot = rootWorldToLocal * filter.transform.localToWorldMatrix;

				obj.AppendLine($"o {SanitizeObjName(filter.name)}");

				for (int i = 0; i < vertices.Length; i++)
				{
					Vector3 v = localToRoot.MultiplyPoint3x4(vertices[i]);
					obj.AppendLine($"v {Fmt(v.x)} {Fmt(v.y)} {Fmt(v.z)}");
				}

				if (uvs != null && uvs.Length == vertices.Length)
				{
					for (int i = 0; i < uvs.Length; i++)
					{
						obj.AppendLine($"vt {Fmt(uvs[i].x)} {Fmt(uvs[i].y)}");
					}
				}
				else
				{
					for (int i = 0; i < vertices.Length; i++)
					{
						obj.AppendLine("vt 0 0");
					}
				}

				if (normals != null && normals.Length == vertices.Length)
				{
					for (int i = 0; i < normals.Length; i++)
					{
						Vector3 n = localToRoot.MultiplyVector(normals[i]).normalized;
						obj.AppendLine($"vn {Fmt(n.x)} {Fmt(n.y)} {Fmt(n.z)}");
					}
				}
				else
				{
					for (int i = 0; i < vertices.Length; i++)
					{
						obj.AppendLine("vn 0 1 0");
					}
				}

				Material[] materials = renderer.sharedMaterials;
				int subMeshCount = Mathf.Min(mesh.subMeshCount, materials != null ? materials.Length : 0);

				for (int sm = 0; sm < subMeshCount; sm++)
				{
					Material mat = materials[sm];
					string matName = EnsureMaterialName(mat, writtenMaterials, mtl);
					obj.AppendLine($"usemtl {matName}");

					int[] tris = mesh.GetTriangles(sm);
					for (int ti = 0; ti < tris.Length; ti += 3)
					{
						int a = tris[ti] + 1 + vertexOffset;
						int b = tris[ti + 1] + 1 + vertexOffset;
						int c = tris[ti + 2] + 1 + vertexOffset;

						int ua = tris[ti] + 1 + uvOffset;
						int ub = tris[ti + 1] + 1 + uvOffset;
						int uc = tris[ti + 2] + 1 + uvOffset;

						int na = tris[ti] + 1 + normalOffset;
						int nb = tris[ti + 1] + 1 + normalOffset;
						int nc = tris[ti + 2] + 1 + normalOffset;

						obj.AppendLine($"f {a}/{ua}/{na} {b}/{ub}/{nb} {c}/{uc}/{nc}");
					}
				}

				if (subMeshCount == 0)
				{
					string matName = EnsureMaterialName(null, writtenMaterials, mtl);
					obj.AppendLine($"usemtl {matName}");
					int[] tris = mesh.triangles;
					for (int ti = 0; ti < tris.Length; ti += 3)
					{
						int a = tris[ti] + 1 + vertexOffset;
						int b = tris[ti + 1] + 1 + vertexOffset;
						int c = tris[ti + 2] + 1 + vertexOffset;

						int ua = tris[ti] + 1 + uvOffset;
						int ub = tris[ti + 1] + 1 + uvOffset;
						int uc = tris[ti + 2] + 1 + uvOffset;

						int na = tris[ti] + 1 + normalOffset;
						int nb = tris[ti + 1] + 1 + normalOffset;
						int nc = tris[ti + 2] + 1 + normalOffset;

						obj.AppendLine($"f {a}/{ua}/{na} {b}/{ub}/{nb} {c}/{uc}/{nc}");
					}
				}

				vertexOffset += vertices.Length;
				uvOffset += vertices.Length;
				normalOffset += vertices.Length;
			}

			File.WriteAllText(objPath, obj.ToString());
			File.WriteAllText(mtlPath, mtl.ToString());

			AssetDatabase.Refresh();
			EditorUtility.DisplayDialog("Export OBJ/MTL", $"Exported:\n{objFileName}\n{mtlFileName}", "OK");
		}

		private static string EnsureMaterialName(Material material, HashSet<string> writtenMaterials, StringBuilder mtl)
		{
			string rawName = material != null ? material.name : "DefaultMaterial";
			string matName = SanitizeObjName(rawName);
			if (writtenMaterials.Contains(matName))
			{
				return matName;
			}

			writtenMaterials.Add(matName);
			Color c = material != null && material.HasProperty("_Color") ? material.color : Color.white;
			mtl.AppendLine($"newmtl {matName}");
			mtl.AppendLine($"Kd {Fmt(c.r)} {Fmt(c.g)} {Fmt(c.b)}");
			mtl.AppendLine($"d {Fmt(c.a)}");
			mtl.AppendLine("illum 2");
			mtl.AppendLine();

			return matName;
		}

		private static string SanitizeObjName(string name)
		{
			if (string.IsNullOrEmpty(name))
			{
				return "Object";
			}

			StringBuilder sb = new StringBuilder(name.Length);
			for (int i = 0; i < name.Length; i++)
			{
				char ch = name[i];
				sb.Append(char.IsWhiteSpace(ch) ? '_' : ch);
			}

			return sb.ToString();
		}

		private static string Fmt(float value)
		{
			return value.ToString("0.######", CultureInfo.InvariantCulture);
		}

		private static void AddBox(List<Vector3> vertices, List<int> triangles, List<Vector3> normals, List<Vector2> uvs, Vector3 min, Vector3 max)
		{
			BuildingMeshUtility.AddBox(vertices, triangles, normals, uvs, min, max);
		}

		private static void AddBoxPart(List<Vector3> vertices, List<int> triangles, List<Vector3> normals, List<Vector2> uvs, Vector3 min, Vector3 max, List<BuildingMeshUtility.MeshPartInfo> parts)
		{
			BuildingMeshUtility.AddBoxPart(vertices, triangles, normals, uvs, min, max, parts);
		}

		private static void OrientTrianglesOutwardPerPart(List<Vector3> vertices, List<int> triangles, List<BuildingMeshUtility.MeshPartInfo> parts)
		{
			BuildingMeshUtility.OrientTrianglesOutwardPerPart(vertices, triangles, parts);
		}

		private static void AddQuad(List<Vector3> vertices, List<int> triangles, List<Vector3> normals, List<Vector2> uvs, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 normal)
		{
			BuildingMeshUtility.AddQuad(vertices, triangles, normals, uvs, a, b, c, d, normal);
		}

		private static void AddQuadFacingPoint(List<Vector3> vertices, List<int> triangles, List<Vector3> normals, List<Vector2> uvs, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 point)
		{
			BuildingMeshUtility.AddQuadFacingPoint(vertices, triangles, normals, uvs, a, b, c, d, point);
		}

		private static void AddTriangle(List<Vector3> vertices, List<int> triangles, List<Vector3> normals, List<Vector2> uvs, Vector3 a, Vector3 b, Vector3 c, Vector3 normal)
		{
			BuildingMeshUtility.AddTriangle(vertices, triangles, normals, uvs, a, b, c, normal);
		}

		private static T GetOrAddComponent<T>(GameObject target) where T : Component
		{
			T component = target.GetComponent<T>();
			if (component == null)
			{
				component = target.AddComponent<T>();
			}

			return component;
		}

		private static void ApplyMeshCollider(GameObject target, Mesh mesh)
		{
			MeshCollider collider = GetOrAddComponent<MeshCollider>(target);
			collider.convex = false;
			collider.isTrigger = false;
			// Force recook so collider always matches latest generated mesh, including openings.
			collider.sharedMesh = null;
			collider.sharedMesh = mesh;
		}

		private static Transform EnsureChild(Transform parent, string childName)
		{
			Transform child = parent.Find(childName);
			if (child == null)
			{
				GameObject childObject = new GameObject(childName);
				childObject.transform.SetParent(parent);
				childObject.transform.localPosition = Vector3.zero;
				childObject.transform.localRotation = Quaternion.identity;
				childObject.transform.localScale = Vector3.one;
				child = childObject.transform;
			}

			return child;
		}

		private static void ClearOptionalContainer(Transform parent, string childName)
		{
			Transform child = parent.Find(childName);
			if (child != null)
			{
				Undo.DestroyObjectImmediate(child.gameObject);
			}
		}

		private static void DrawAnchorGizmo(Vector3 position, Color sphereColor)
		{
			float scale = HandleUtility.GetHandleSize(position);
			float axisLength = AnchorAxisLength * scale;

			Color previous = Handles.color;

			Handles.color = Color.red;
			Handles.DrawLine(position - Vector3.right * axisLength, position + Vector3.right * axisLength);
			Handles.color = Color.green;
			Handles.DrawLine(position - Vector3.up * axisLength, position + Vector3.up * axisLength);
			Handles.color = Color.blue;
			Handles.DrawLine(position - Vector3.forward * axisLength, position + Vector3.forward * axisLength);

			Handles.color = sphereColor;
			Handles.SphereHandleCap(0, position, Quaternion.identity, AnchorSphereRadius * scale * 2f, EventType.Repaint);

			Handles.color = previous;
		}
	}
}
