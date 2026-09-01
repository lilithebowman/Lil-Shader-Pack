using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace CozyCon.Tools
{
	public class TableModellingTool : EditorWindow
	{
		private enum ToolMode
		{
			None,
			Create,
			Edit
		}

		private enum TableShape
		{
			Square,
			Ellipsoid
		}

		private enum LegCountMode
		{
			Total,
			PerMetre
		}

		private const string RootName = "TableDraft";
		private const string AnchorsContainerName = "Anchors";
		private const string GeometryContainerName = "Geometry";
		private const string TabletopObjectName = "Tabletop";
		private const string LegsObjectName = "Legs";
		private const string EditorOnlyTag = "EditorOnly";
		private const float AnchorSphereRadius = 0.06f;
		private const float AnchorAxisLength = 0.3f;

		private readonly Vector2[] defaultCorners =
		{
			new Vector2(-0.75f, -0.75f),
			new Vector2(0.75f, -0.75f),
			new Vector2(0.75f, 0.75f),
			new Vector2(-0.75f, 0.75f)
		};

		private ToolMode mode;
		private TableShape tableShape = TableShape.Square;
		private LegCountMode legCountMode = LegCountMode.Total;
		private GameObject activeRoot;
		private Transform anchorsContainer;
		private Transform geometryContainer;
		private Transform selectedAnchor;
		private Vector2 scrollPosition;

		private float tableHeight = 0.75f;
		private float tabletopThickness = 0.08f;
		private bool roundedCorners = true;
		private float cornerRadius = 0.18f;
		private int cornerSegments = 6;
		private int ellipsoidSegments = 32;
		private int totalLegs = 4;
		private float legsPerMetre = 1.2f;
		private float legInset = 0.08f;
		private Vector2 legSize = new Vector2(0.08f, 0.08f);
		private bool generateMeshColliders;
		private Material tableMaterial;
		private string meshExportFolder = "Assets";

		[MenuItem("Lilithe/Table Modelling Tool")]
		public static void ShowWindow()
		{
			GetWindow<TableModellingTool>("Table Modelling Tool");
		}

		private void OnEnable()
		{
			SceneView.duringSceneGui += OnSceneGUI;
			TryBindRootFromSelection(Selection.activeTransform);
		}

		private void OnDisable()
		{
			SceneView.duringSceneGui -= OnSceneGUI;
		}

		private void OnSelectionChange()
		{
			TryBindRootFromSelection(Selection.activeTransform);
			Repaint();
		}

		private void OnGUI()
		{
			scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);

			EditorGUILayout.LabelField("Table Modelling", EditorStyles.boldLabel);
			EditorGUILayout.HelpBox("Create mode: click in the Scene view to create a table. Edit mode: drag corner anchors to adjust the tabletop footprint.", MessageType.Info);
			EditorGUILayout.Space();

			DrawModeButtons();
			EditorGUILayout.Space();
			DrawRootControls();
			EditorGUILayout.Space();
			DrawTableControls();
			EditorGUILayout.Space();

			EditorGUILayout.BeginHorizontal();
			if (GUILayout.Button("Rebuild Table"))
			{
				RebuildTable();
			}

			if (GUILayout.Button("New Table At Origin"))
			{
				CreateTable(Vector3.zero);
			}
			EditorGUILayout.EndHorizontal();

			EditorGUILayout.Space();
			DrawExportControls();

			EditorGUILayout.EndScrollView();
		}

		private void DrawExportControls()
		{
			EditorGUILayout.LabelField("Mesh Export", EditorStyles.boldLabel);
			EditorGUILayout.HelpBox("Export generated tabletop and legs as mesh assets so they are preserved for builds and VRChat upload.", MessageType.None);

			EditorGUILayout.BeginHorizontal();
			EditorGUILayout.PrefixLabel("Export Folder");
			EditorGUILayout.SelectableLabel(meshExportFolder, EditorStyles.textField, GUILayout.Height(EditorGUIUtility.singleLineHeight));
			if (GUILayout.Button("Browse", GUILayout.Width(80f)))
			{
				ChooseExportFolder();
			}
			EditorGUILayout.EndHorizontal();

			using (new EditorGUI.DisabledScope(activeRoot == null))
			{
				if (GUILayout.Button("Export Mesh Assets"))
				{
					ExportMeshAssets();
				}
			}
		}

		private void DrawModeButtons()
		{
			EditorGUILayout.BeginHorizontal();

			bool createPressed = GUILayout.Toggle(mode == ToolMode.Create, "Create Mode", "Button");
			if (createPressed && mode != ToolMode.Create)
			{
				mode = ToolMode.Create;
			}
			else if (!createPressed && mode == ToolMode.Create)
			{
				mode = ToolMode.None;
			}

			bool editPressed = GUILayout.Toggle(mode == ToolMode.Edit, "Edit Mode", "Button");
			if (editPressed && mode != ToolMode.Edit)
			{
				mode = ToolMode.Edit;
			}
			else if (!editPressed && mode == ToolMode.Edit)
			{
				mode = ToolMode.None;
			}

			EditorGUILayout.EndHorizontal();
		}

		private void DrawRootControls()
		{
			EditorGUILayout.BeginHorizontal();
			GameObject selectedRoot = (GameObject)EditorGUILayout.ObjectField("Active Table", activeRoot, typeof(GameObject), true);
			if (selectedRoot != activeRoot)
			{
				if (selectedRoot == null)
				{
					UnbindActiveRoot();
				}
				else if (!BindToRoot(selectedRoot))
				{
					EditorUtility.DisplayDialog("Invalid Table", "Selected object is not a valid table draft root.", "OK");
				}
			}

			if (GUILayout.Button("Use Selected", GUILayout.Width(110f)))
			{
				TryBindRootFromSelection(Selection.activeTransform);
			}
			EditorGUILayout.EndHorizontal();
		}

		private void DrawTableControls()
		{
			EditorGUI.BeginChangeCheck();
			tableShape = (TableShape)EditorGUILayout.EnumPopup("Table Shape", tableShape);
			tableHeight = EditorGUILayout.Slider("Table Height", tableHeight, 0.2f, 2.5f);
			tabletopThickness = EditorGUILayout.Slider("Tabletop Thickness", tabletopThickness, 0.02f, 0.4f);

			if (tableShape == TableShape.Square)
			{
				roundedCorners = EditorGUILayout.Toggle("Rounded Corners", roundedCorners);
				if (roundedCorners)
				{
					cornerRadius = EditorGUILayout.Slider("Corner Radius", cornerRadius, 0.01f, 1f);
					cornerSegments = EditorGUILayout.IntSlider("Corner Segments", cornerSegments, 2, 16);
				}
			}
			else
			{
				ellipsoidSegments = EditorGUILayout.IntSlider("Ellipsoid Segments", ellipsoidSegments, 12, 96);
			}

			legCountMode = (LegCountMode)EditorGUILayout.EnumPopup("Leg Count Mode", legCountMode);
			if (legCountMode == LegCountMode.Total)
			{
				totalLegs = EditorGUILayout.IntSlider("Total Legs", totalLegs, 1, 64);
			}
			else
			{
				legsPerMetre = EditorGUILayout.Slider("Legs Per Metre", legsPerMetre, 0.1f, 8f);
			}

			legInset = EditorGUILayout.Slider("Leg Inset From Edge", legInset, 0f, 0.5f);
			legSize = EditorGUILayout.Vector2Field("Leg Size", legSize);
			generateMeshColliders = EditorGUILayout.Toggle("Generate Mesh Colliders", generateMeshColliders);
			EditorGUILayout.HelpBox("Disabled is recommended for VRChat safety. Enable only if you specifically need mesh colliders.", MessageType.None);
			tableMaterial = (Material)EditorGUILayout.ObjectField("Table Material", tableMaterial, typeof(Material), false);

			if (EditorGUI.EndChangeCheck())
			{
				tableHeight = Mathf.Max(0.05f, tableHeight);
				tabletopThickness = Mathf.Max(0.01f, tabletopThickness);
				legSize = new Vector2(Mathf.Max(0.01f, legSize.x), Mathf.Max(0.01f, legSize.y));
				RebuildTable();
			}
		}

		private void OnSceneGUI(SceneView sceneView)
		{
			if (mode == ToolMode.None)
			{
				return;
			}

			if (activeRoot != null)
			{
				DrawAnchorHandles();
			}

			Event evt = Event.current;
			if (evt == null)
			{
				return;
			}

			if (mode == ToolMode.Create)
			{
				HandleUtility.AddDefaultControl(GUIUtility.GetControlID(FocusType.Passive));
				if (evt.type == EventType.MouseDown && evt.button == 0 && !evt.alt)
				{
					Ray ray = HandleUtility.GUIPointToWorldRay(evt.mousePosition);
					if (TryGetScenePosition(ray, out Vector3 position))
					{
						CreateTable(position);
						evt.Use();
					}
				}
			}
		}

		private void DrawAnchorHandles()
		{
			if (anchorsContainer == null)
			{
				return;
			}

			for (int i = 0; i < anchorsContainer.childCount; i++)
			{
				Transform anchor = anchorsContainer.GetChild(i);
				Vector3 world = anchor.position;
				DrawAnchorGizmo(world, anchor == selectedAnchor ? new Color(1f, 0.95f, 0.2f, 1f) : new Color(0.1f, 0.9f, 1f, 1f));

				float size = HandleUtility.GetHandleSize(world) * AnchorSphereRadius;
				if (Handles.Button(world, Quaternion.identity, size, size, Handles.SphereHandleCap))
				{
					selectedAnchor = anchor;
					Selection.activeObject = anchor.gameObject;
					Repaint();
				}

				if (mode != ToolMode.Edit)
				{
					continue;
				}

				EditorGUI.BeginChangeCheck();
				Vector3 moved = Handles.FreeMoveHandle(world, size, Vector3.zero, Handles.SphereHandleCap);
				if (EditorGUI.EndChangeCheck())
				{
					selectedAnchor = anchor;
					ApplyDraggedCorner(i, moved);
					RebuildTable();
				}
			}
		}

		private void CreateTable(Vector3 position)
		{
			GameObject root = new GameObject(GetUniqueTableName());
			Undo.RegisterCreatedObjectUndo(root, "Create Table");
			root.transform.position = position;

			activeRoot = root;
			anchorsContainer = null;
			geometryContainer = null;
			selectedAnchor = null;
			EnsureContainers();

			for (int i = 0; i < defaultCorners.Length; i++)
			{
				GameObject anchorObject = new GameObject($"Anchor_{i + 1}");
				Undo.RegisterCreatedObjectUndo(anchorObject, "Create Table Anchor");
				anchorObject.transform.SetParent(anchorsContainer);
				anchorObject.tag = EditorOnlyTag;
				anchorObject.transform.localPosition = new Vector3(defaultCorners[i].x, tableHeight, defaultCorners[i].y);
				anchorObject.transform.localRotation = Quaternion.identity;
				anchorObject.transform.localScale = Vector3.one;
			}

			Selection.activeObject = root;
			if (mode == ToolMode.None)
			{
				mode = ToolMode.Edit;
			}
			RebuildTable();
		}

		private void RebuildTable()
		{
			if (activeRoot == null)
			{
				return;
			}

			EnsureContainers();
			EnsureAnchorCount();
			NormalizeRectangularAnchors();

			Bounds footprint = GetAnchorBounds();
			Vector2 size = new Vector2(Mathf.Max(0.1f, footprint.size.x), Mathf.Max(0.1f, footprint.size.z));
			Vector3 center = new Vector3(footprint.center.x, 0f, footprint.center.z);

			Transform tabletop = EnsureChild(geometryContainer, TabletopObjectName);
			tabletop.localPosition = center;
			tabletop.localRotation = Quaternion.identity;
			tabletop.localScale = Vector3.one;

			Mesh tabletopMesh = tableShape == TableShape.Square
				? BuildRoundedRectanglePrism(size.x, size.y, roundedCorners ? cornerRadius : 0f, cornerSegments, tableHeight, tabletopThickness, "TabletopMesh")
				: BuildEllipsoidPrism(size.x * 0.5f, size.y * 0.5f, ellipsoidSegments, tableHeight, tabletopThickness, "TabletopMesh");

			ApplyMesh(tabletop.gameObject, tabletopMesh);

			Transform legs = EnsureChild(geometryContainer, LegsObjectName);
			legs.localPosition = center;
			legs.localRotation = Quaternion.identity;
			legs.localScale = Vector3.one;
			ApplyMesh(legs.gameObject, BuildLegsMesh(size.x, size.y, tableHeight, tableShape, "TableLegsMesh"));

			SceneView.RepaintAll();
			Repaint();
		}

		private void ChooseExportFolder()
		{
			string absoluteSelection = EditorUtility.OpenFolderPanel("Select Mesh Export Folder", Application.dataPath, string.Empty);
			if (string.IsNullOrEmpty(absoluteSelection))
			{
				return;
			}

			string normalizedDataPath = Application.dataPath.Replace("\\", "/");
			string normalizedSelection = absoluteSelection.Replace("\\", "/");
			if (!normalizedSelection.StartsWith(normalizedDataPath))
			{
				EditorUtility.DisplayDialog("Invalid Folder", "Please choose a folder inside this Unity project's Assets directory.", "OK");
				return;
			}

			meshExportFolder = "Assets" + normalizedSelection.Substring(normalizedDataPath.Length);
		}

		private void ExportMeshAssets()
		{
			if (activeRoot == null)
			{
				EditorUtility.DisplayDialog("No Active Table", "Create or select a table before exporting meshes.", "OK");
				return;
			}

			RebuildTable();

			Transform tabletopTransform = geometryContainer != null ? geometryContainer.Find(TabletopObjectName) : null;
			Transform legsTransform = geometryContainer != null ? geometryContainer.Find(LegsObjectName) : null;
			if (tabletopTransform == null || legsTransform == null)
			{
				EditorUtility.DisplayDialog("Missing Geometry", "Could not find generated tabletop and legs geometry to export.", "OK");
				return;
			}

			MeshFilter tabletopFilter = tabletopTransform.GetComponent<MeshFilter>();
			MeshFilter legsFilter = legsTransform.GetComponent<MeshFilter>();
			if (tabletopFilter == null || legsFilter == null || tabletopFilter.sharedMesh == null || legsFilter.sharedMesh == null)
			{
				EditorUtility.DisplayDialog("Missing Mesh", "Generated meshes are missing. Rebuild the table and try again.", "OK");
				return;
			}

			if (!AssetDatabase.IsValidFolder(meshExportFolder))
			{
				EditorUtility.DisplayDialog("Invalid Folder", "The export folder is not valid. Choose a folder under Assets.", "OK");
				return;
			}

			string safeRootName = SanitizeAssetName(activeRoot.name);
			string tabletopAssetPath = AssetDatabase.GenerateUniqueAssetPath($"{meshExportFolder}/{safeRootName}_Tabletop.asset");
			string legsAssetPath = AssetDatabase.GenerateUniqueAssetPath($"{meshExportFolder}/{safeRootName}_Legs.asset");

			Mesh tabletopAsset = Object.Instantiate(tabletopFilter.sharedMesh);
			tabletopAsset.name = System.IO.Path.GetFileNameWithoutExtension(tabletopAssetPath);
			AssetDatabase.CreateAsset(tabletopAsset, tabletopAssetPath);

			Mesh legsAsset = Object.Instantiate(legsFilter.sharedMesh);
			legsAsset.name = System.IO.Path.GetFileNameWithoutExtension(legsAssetPath);
			AssetDatabase.CreateAsset(legsAsset, legsAssetPath);

			tabletopFilter.sharedMesh = tabletopAsset;
			MeshCollider tabletopCollider = tabletopTransform.GetComponent<MeshCollider>();
			if (tabletopCollider != null)
			{
				tabletopCollider.sharedMesh = tabletopAsset;
			}

			legsFilter.sharedMesh = legsAsset;
			MeshCollider legsCollider = legsTransform.GetComponent<MeshCollider>();
			if (legsCollider != null)
			{
				legsCollider.sharedMesh = legsAsset;
			}

			AssetDatabase.SaveAssets();
			EditorSceneManager.MarkSceneDirty(activeRoot.scene);
			EditorUtility.DisplayDialog("Mesh Export Complete", $"Saved mesh assets:\n{tabletopAssetPath}\n{legsAssetPath}", "OK");
			Selection.activeObject = activeRoot;
		}

		private void EnsureAnchorCount()
		{
			for (int i = anchorsContainer.childCount; i < 4; i++)
			{
				GameObject anchorObject = new GameObject($"Anchor_{i + 1}");
				Undo.RegisterCreatedObjectUndo(anchorObject, "Create Table Anchor");
				anchorObject.transform.SetParent(anchorsContainer);
				anchorObject.tag = EditorOnlyTag;
				anchorObject.transform.localPosition = new Vector3(defaultCorners[i].x, tableHeight, defaultCorners[i].y);
			}
		}

		private void NormalizeRectangularAnchors()
		{
			if (anchorsContainer == null || anchorsContainer.childCount < 4)
			{
				return;
			}

			float minX = float.MaxValue;
			float maxX = float.MinValue;
			float minZ = float.MaxValue;
			float maxZ = float.MinValue;
			for (int i = 0; i < anchorsContainer.childCount; i++)
			{
				Vector3 local = anchorsContainer.GetChild(i).localPosition;
				minX = Mathf.Min(minX, local.x);
				maxX = Mathf.Max(maxX, local.x);
				minZ = Mathf.Min(minZ, local.z);
				maxZ = Mathf.Max(maxZ, local.z);
			}

			Vector2[] corners =
			{
				new Vector2(minX, minZ),
				new Vector2(maxX, minZ),
				new Vector2(maxX, maxZ),
				new Vector2(minX, maxZ)
			};

			for (int i = 0; i < 4; i++)
			{
				Transform anchor = anchorsContainer.GetChild(i);
				anchor.localPosition = new Vector3(corners[i].x, tableHeight, corners[i].y);
			}
		}

		private void ApplyDraggedCorner(int anchorIndex, Vector3 movedWorldPosition)
		{
			if (anchorsContainer == null || anchorsContainer.childCount < 4)
			{
				return;
			}

			int oppositeIndex = (anchorIndex + 2) % 4;
			Vector3 oppositeLocal = anchorsContainer.GetChild(oppositeIndex).localPosition;
			Vector3 movedLocal = activeRoot.transform.InverseTransformPoint(movedWorldPosition);
			float minX = Mathf.Min(movedLocal.x, oppositeLocal.x);
			float maxX = Mathf.Max(movedLocal.x, oppositeLocal.x);
			float minZ = Mathf.Min(movedLocal.z, oppositeLocal.z);
			float maxZ = Mathf.Max(movedLocal.z, oppositeLocal.z);

			Vector2[] corners =
			{
				new Vector2(minX, minZ),
				new Vector2(maxX, minZ),
				new Vector2(maxX, maxZ),
				new Vector2(minX, maxZ)
			};

			for (int i = 0; i < 4; i++)
			{
				Transform anchor = anchorsContainer.GetChild(i);
				anchor.localPosition = new Vector3(corners[i].x, tableHeight, corners[i].y);
			}
		}

		private Bounds GetAnchorBounds()
		{
			Bounds bounds = new Bounds(Vector3.zero, Vector3.zero);
			bool initialized = false;
			for (int i = 0; i < anchorsContainer.childCount; i++)
			{
				Vector3 local = anchorsContainer.GetChild(i).localPosition;
				local.y = 0f;
				if (!initialized)
				{
					bounds = new Bounds(local, Vector3.zero);
					initialized = true;
				}
				else
				{
					bounds.Encapsulate(local);
				}
			}

			return initialized ? bounds : new Bounds(Vector3.zero, new Vector3(1.5f, 0f, 1.5f));
		}

		private Mesh BuildLegsMesh(float width, float depth, float height, TableShape shape, string meshName)
		{
			int legCount = GetLegCount(width, depth, shape);
			float halfWidth = width * 0.5f;
			float halfDepth = depth * 0.5f;
			float insetX = Mathf.Max(0f, legInset);
			float insetZ = Mathf.Max(0f, legInset);
			float usableWidth = Mathf.Max(0.05f, halfWidth - insetX);
			float usableDepth = Mathf.Max(0.05f, halfDepth - insetZ);

			List<Vector3> vertices = new List<Vector3>();
			List<int> triangles = new List<int>();
			List<Vector3> normals = new List<Vector3>();
			List<Vector2> uvs = new List<Vector2>();

			for (int i = 0; i < legCount; i++)
			{
				Vector2 legCenter = shape == TableShape.Ellipsoid
					? GetEllipsoidLegPoint(i, legCount, usableWidth, usableDepth)
					: GetRectanglePerimeterPoint(i, legCount, usableWidth, usableDepth);

				Vector3 min = new Vector3(legCenter.x - legSize.x * 0.5f, 0f, legCenter.y - legSize.y * 0.5f);
				Vector3 max = new Vector3(legCenter.x + legSize.x * 0.5f, height, legCenter.y + legSize.y * 0.5f);
				AddBox(vertices, triangles, normals, uvs, min, max);
			}

			Mesh mesh = new Mesh();
			mesh.name = meshName;
			mesh.indexFormat = vertices.Count > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16;
			mesh.SetVertices(vertices);
			mesh.SetTriangles(triangles, 0);
			mesh.SetNormals(normals);
			mesh.SetUVs(0, uvs);
			mesh.RecalculateNormals();
			mesh.RecalculateBounds();
			return mesh;
		}

		private int GetLegCount(float width, float depth, TableShape shape)
		{
			if (legCountMode == LegCountMode.Total)
			{
				return Mathf.Clamp(totalLegs, 1, 64);
			}

			float perimeter = shape == TableShape.Ellipsoid
				? ApproximateEllipsePerimeter(width * 0.5f, depth * 0.5f)
				: (width + depth) * 2f;
			return Mathf.Clamp(Mathf.RoundToInt(perimeter * Mathf.Max(0.1f, legsPerMetre)), 1, 64);
		}

		private static Vector2 GetRectanglePerimeterPoint(int index, int count, float halfWidth, float halfDepth)
		{
			if (count <= 4)
			{
				switch (index % 4)
				{
					case 0:
						return new Vector2(-halfWidth, -halfDepth);
					case 1:
						return new Vector2(halfWidth, -halfDepth);
					case 2:
						return new Vector2(halfWidth, halfDepth);
					default:
						return new Vector2(-halfWidth, halfDepth);
				}
			}

			float perimeter = (halfWidth + halfDepth) * 4f;
			float distance = (index / (float)count) * perimeter;
			float sideWidth = halfWidth * 2f;
			float sideDepth = halfDepth * 2f;

			if (distance < sideWidth)
			{
				return new Vector2(-halfWidth + distance, -halfDepth);
			}

			distance -= sideWidth;
			if (distance < sideDepth)
			{
				return new Vector2(halfWidth, -halfDepth + distance);
			}

			distance -= sideDepth;
			if (distance < sideWidth)
			{
				return new Vector2(halfWidth - distance, halfDepth);
			}

			distance -= sideWidth;
			return new Vector2(-halfWidth, halfDepth - distance);
		}

		private static Vector2 GetEllipsoidLegPoint(int index, int count, float radiusX, float radiusZ)
		{
			float angle = (index / (float)count) * Mathf.PI * 2f;
			return new Vector2(Mathf.Cos(angle) * radiusX, Mathf.Sin(angle) * radiusZ);
		}

		private static float ApproximateEllipsePerimeter(float radiusX, float radiusZ)
		{
			float a = Mathf.Max(radiusX, radiusZ);
			float b = Mathf.Min(radiusX, radiusZ);
			float h = Mathf.Pow((a - b) / Mathf.Max(0.0001f, a + b), 2f);
			return Mathf.PI * (a + b) * (1f + (3f * h / (10f + Mathf.Sqrt(4f - 3f * h))));
		}

		private Mesh BuildRoundedRectanglePrism(float width, float depth, float radius, int segments, float height, float thickness, string meshName)
		{
			float halfWidth = width * 0.5f;
			float halfDepth = depth * 0.5f;
			float clampedRadius = Mathf.Clamp(radius, 0f, Mathf.Min(halfWidth, halfDepth) * 0.95f);
			List<Vector2> outline = new List<Vector2>();

			if (clampedRadius <= 0.001f)
			{
				outline.Add(new Vector2(-halfWidth, -halfDepth));
				outline.Add(new Vector2(halfWidth, -halfDepth));
				outline.Add(new Vector2(halfWidth, halfDepth));
				outline.Add(new Vector2(-halfWidth, halfDepth));
			}
			else
			{
				int arcSegments = Mathf.Max(2, segments);
				AddArc(outline, new Vector2(halfWidth - clampedRadius, halfDepth - clampedRadius), clampedRadius, 0f, 90f, arcSegments);
				AddArc(outline, new Vector2(-halfWidth + clampedRadius, halfDepth - clampedRadius), clampedRadius, 90f, 180f, arcSegments);
				AddArc(outline, new Vector2(-halfWidth + clampedRadius, -halfDepth + clampedRadius), clampedRadius, 180f, 270f, arcSegments);
				AddArc(outline, new Vector2(halfWidth - clampedRadius, -halfDepth + clampedRadius), clampedRadius, 270f, 360f, arcSegments);
			}

			return BuildPrismFromOutline(outline, height, thickness, meshName);
		}

		private Mesh BuildEllipsoidPrism(float radiusX, float radiusZ, int segments, float height, float thickness, string meshName)
		{
			int count = Mathf.Max(12, segments);
			List<Vector2> outline = new List<Vector2>(count);
			for (int i = 0; i < count; i++)
			{
				float angle = (i / (float)count) * Mathf.PI * 2f;
				outline.Add(new Vector2(Mathf.Cos(angle) * radiusX, Mathf.Sin(angle) * radiusZ));
			}

			return BuildPrismFromOutline(outline, height, thickness, meshName);
		}

		private static Mesh BuildPrismFromOutline(List<Vector2> outline, float height, float thickness, string meshName)
		{
			List<Vector3> vertices = new List<Vector3>();
			List<int> triangles = new List<int>();
			List<Vector3> normals = new List<Vector3>();
			List<Vector2> uvs = new List<Vector2>();
			int count = outline.Count;
			if (count < 3 || !TryTriangulatePolygon(outline, out List<int> polygonTriangles))
			{
				return new Mesh { name = meshName };
			}

			float bottom = height;
			float top = height + thickness;
			for (int i = 0; i < count; i++)
			{
				vertices.Add(new Vector3(outline[i].x, top, outline[i].y));
				normals.Add(Vector3.up);
				uvs.Add(outline[i]);
			}

			for (int i = 0; i < count; i++)
			{
				vertices.Add(new Vector3(outline[i].x, bottom, outline[i].y));
				normals.Add(Vector3.down);
				uvs.Add(outline[i]);
			}

			for (int i = 0; i < polygonTriangles.Count; i += 3)
			{
				triangles.Add(polygonTriangles[i]);
				triangles.Add(polygonTriangles[i + 2]);
				triangles.Add(polygonTriangles[i + 1]);
			}

			int bottomOffset = count;
			for (int i = 0; i < polygonTriangles.Count; i += 3)
			{
				triangles.Add(bottomOffset + polygonTriangles[i]);
				triangles.Add(bottomOffset + polygonTriangles[i + 1]);
				triangles.Add(bottomOffset + polygonTriangles[i + 2]);
			}

			for (int i = 0; i < count; i++)
			{
				int next = (i + 1) % count;
				Vector3 aTop = new Vector3(outline[i].x, top, outline[i].y);
				Vector3 bTop = new Vector3(outline[next].x, top, outline[next].y);
				Vector3 aBottom = new Vector3(outline[i].x, bottom, outline[i].y);
				Vector3 bBottom = new Vector3(outline[next].x, bottom, outline[next].y);
				Vector3 normal = Vector3.Cross(bTop - aTop, bBottom - aTop).normalized;
				AddQuad(vertices, triangles, normals, uvs, aTop, bTop, bBottom, aBottom, normal);
			}

			Mesh mesh = new Mesh();
			mesh.name = meshName;
			mesh.indexFormat = vertices.Count > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16;
			mesh.SetVertices(vertices);
			mesh.SetTriangles(triangles, 0);
			mesh.SetNormals(normals);
			mesh.SetUVs(0, uvs);
			mesh.RecalculateNormals();
			mesh.RecalculateBounds();
			return mesh;
		}

		private static void AddArc(List<Vector2> points, Vector2 center, float radius, float startDegrees, float endDegrees, int segments)
		{
			for (int i = 0; i <= segments; i++)
			{
				float t = i / (float)segments;
				float angle = Mathf.Lerp(startDegrees, endDegrees, t) * Mathf.Deg2Rad;
				points.Add(center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius);
			}
		}

		private void ApplyMesh(GameObject target, Mesh mesh)
		{
			MeshFilter meshFilter = GetOrAddComponent<MeshFilter>(target);
			MeshRenderer meshRenderer = GetOrAddComponent<MeshRenderer>(target);
			Mesh previousMesh = meshFilter.sharedMesh;
			if (previousMesh != null && previousMesh != mesh && !AssetDatabase.Contains(previousMesh))
			{
				DestroyImmediate(previousMesh);
			}

			meshFilter.sharedMesh = mesh;

			if (generateMeshColliders)
			{
				MeshCollider meshCollider = GetOrAddComponent<MeshCollider>(target);
				meshCollider.sharedMesh = mesh;
			}
			else
			{
				MeshCollider existingCollider = target.GetComponent<MeshCollider>();
				if (existingCollider != null)
				{
					DestroyImmediate(existingCollider);
				}
			}

			meshRenderer.sharedMaterial = tableMaterial != null ? tableMaterial : GetDefaultMaterial();
		}

		private static string SanitizeAssetName(string value)
		{
			if (string.IsNullOrEmpty(value))
			{
				return "Table";
			}

			char[] invalidChars = System.IO.Path.GetInvalidFileNameChars();
			for (int i = 0; i < invalidChars.Length; i++)
			{
				value = value.Replace(invalidChars[i], '_');
			}

			return value.Trim();
		}

		private static Material GetDefaultMaterial()
		{
			return AssetDatabase.GetBuiltinExtraResource<Material>("Default-Material.mat");
		}

		private void EnsureContainers()
		{
			if (activeRoot == null)
			{
				return;
			}

			anchorsContainer = EnsureChild(activeRoot.transform, AnchorsContainerName);
			if (anchorsContainer != null)
			{
				anchorsContainer.gameObject.tag = EditorOnlyTag;
			}
			geometryContainer = EnsureChild(activeRoot.transform, GeometryContainerName);
		}

		private bool TryBindRootFromSelection(Transform selectedTransform)
		{
			Transform current = selectedTransform;
			while (current != null)
			{
				if (IsTableRoot(current))
				{
					return BindToRoot(current.gameObject);
				}

				current = current.parent;
			}

			return false;
		}

		private bool BindToRoot(GameObject candidateRoot)
		{
			if (candidateRoot == null || !IsTableRoot(candidateRoot.transform))
			{
				return false;
			}

			activeRoot = candidateRoot;
			anchorsContainer = activeRoot.transform.Find(AnchorsContainerName);
			geometryContainer = activeRoot.transform.Find(GeometryContainerName);
			selectedAnchor = null;
			return true;
		}

		private void UnbindActiveRoot()
		{
			activeRoot = null;
			anchorsContainer = null;
			geometryContainer = null;
			selectedAnchor = null;
		}

		private static bool IsTableRoot(Transform candidate)
		{
			return candidate != null
				&& candidate.Find(AnchorsContainerName) != null
				&& candidate.Find(GeometryContainerName) != null;
		}

		private static Transform EnsureChild(Transform parent, string childName)
		{
			Transform child = parent.Find(childName);
			if (child == null)
			{
				GameObject childObject = new GameObject(childName);
				Undo.RegisterCreatedObjectUndo(childObject, $"Create {childName}");
				childObject.transform.SetParent(parent);
				childObject.transform.localPosition = Vector3.zero;
				childObject.transform.localRotation = Quaternion.identity;
				childObject.transform.localScale = Vector3.one;
				child = childObject.transform;
			}

			return child;
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

		private static bool TryGetScenePosition(Ray ray, out Vector3 worldPosition)
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

		private static string GetUniqueTableName()
		{
			if (GameObject.Find(RootName) == null)
			{
				return RootName;
			}

			int index = 1;
			while (GameObject.Find($"{RootName}_{index}") != null)
			{
				index++;
			}

			return $"{RootName}_{index}";
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
					int previous = verts[(i - 1 + verts.Count) % verts.Count];
					int current = verts[i];
					int next = verts[(i + 1) % verts.Count];
					if (!IsConvex(polygon[previous], polygon[current], polygon[next]))
					{
						continue;
					}

					bool hasPointInside = false;
					for (int j = 0; j < verts.Count; j++)
					{
						int test = verts[j];
						if (test == previous || test == current || test == next)
						{
							continue;
						}

						if (PointInTriangle(polygon[test], polygon[previous], polygon[current], polygon[next]))
						{
							hasPointInside = true;
							break;
						}
					}

					if (hasPointInside)
					{
						continue;
					}

					triangles.Add(previous);
					triangles.Add(current);
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

		private static void AddBox(List<Vector3> vertices, List<int> triangles, List<Vector3> normals, List<Vector2> uvs, Vector3 min, Vector3 max)
		{
			Vector3[] pts =
			{
				new Vector3(min.x, min.y, min.z),
				new Vector3(max.x, min.y, min.z),
				new Vector3(max.x, max.y, min.z),
				new Vector3(min.x, max.y, min.z),
				new Vector3(min.x, min.y, max.z),
				new Vector3(max.x, min.y, max.z),
				new Vector3(max.x, max.y, max.z),
				new Vector3(min.x, max.y, max.z)
			};

			AddQuad(vertices, triangles, normals, uvs, pts[4], pts[5], pts[6], pts[7], Vector3.forward);
			AddQuad(vertices, triangles, normals, uvs, pts[1], pts[0], pts[3], pts[2], Vector3.back);
			AddQuad(vertices, triangles, normals, uvs, pts[0], pts[4], pts[7], pts[3], Vector3.left);
			AddQuad(vertices, triangles, normals, uvs, pts[5], pts[1], pts[2], pts[6], Vector3.right);
			AddQuad(vertices, triangles, normals, uvs, pts[3], pts[7], pts[6], pts[2], Vector3.up);
			AddQuad(vertices, triangles, normals, uvs, pts[0], pts[1], pts[5], pts[4], Vector3.down);
		}

		private static void AddQuad(List<Vector3> vertices, List<int> triangles, List<Vector3> normals, List<Vector2> uvs, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 normal)
		{
			int start = vertices.Count;
			vertices.Add(a);
			vertices.Add(b);
			vertices.Add(c);
			vertices.Add(d);
			normals.Add(normal);
			normals.Add(normal);
			normals.Add(normal);
			normals.Add(normal);
			uvs.Add(new Vector2(0f, 0f));
			uvs.Add(new Vector2(1f, 0f));
			uvs.Add(new Vector2(1f, 1f));
			uvs.Add(new Vector2(0f, 1f));
			triangles.Add(start);
			triangles.Add(start + 1);
			triangles.Add(start + 2);
			triangles.Add(start);
			triangles.Add(start + 2);
			triangles.Add(start + 3);
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
