using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace CozyCon.Tools
{
	public class TriangleSurfaceModelingTool : EditorWindow
	{
		private struct SurfaceHit
		{
			public Vector3 point;
			public Vector3 normal;
			public Transform hitTransform;
		}

		private struct EdgeSelection
		{
			public TriangleSurfacePatch patch;
			public int a;
			public int b;

			public bool IsValid => patch != null && a >= 0 && b >= 0 && a != b;
		}

		private struct EdgeKey
		{
			public int min;
			public int max;

			public EdgeKey(int a, int b)
			{
				if (a < b)
				{
					min = a;
					max = b;
				}
				else
				{
					min = b;
					max = a;
				}
			}
		}

		private const float AnchorHandleSize = 0.08f;
		private const float DefaultTriangleRadius = 0.35f;
		private const float Epsilon = 1e-5f;
		private const int NoControl = 0;

		private bool createMode = true;
		private bool editMode;
		private bool viewMode;
		private bool joinVertexMode;
		private bool parentPatchToHitRenderer = true;
		private float newTriangleRadius = DefaultTriangleRadius;
		private float maxUvStretch = 0.20f;
		private float normalSmoothingFactor = 1f;
		private Material defaultMaterial;
		private TriangleSurfacePatch activePatch;
		private GameObject editorProxyRoot;
		private Vector2 scroll;
		private EdgeSelection selectedEdge;
		private int joinSourceAnchor = -1;
		private int joinTargetAnchor = -1;

		private bool pendingTriangleExtend;
		private TriangleSurfacePatch pendingPatch;
		private int pendingAnchorA = -1;
		private int pendingAnchorB = -1;
		private Vector3 pendingPoint;
		private Vector3 pendingNormal = Vector3.up;

		[MenuItem("Lilithe/Triangle Surface Modeling Tool")]
		public static void ShowWindow()
		{
			GetWindow<TriangleSurfaceModelingTool>("Triangle Surface Tool");
		}

		private void OnEnable()
		{
			SceneView.duringSceneGui += OnSceneGUI;
			TryBindFromSelection();
		}

		private void OnDisable()
		{
			SceneView.duringSceneGui -= OnSceneGUI;
			CleanupEditorProxy();
		}

		private void OnSelectionChange()
		{
			TryBindFromSelection();
			Repaint();
		}

		private void OnGUI()
		{
			scroll = EditorGUILayout.BeginScrollView(scroll);

			EditorGUILayout.LabelField("Triangle Surface Modeling", EditorStyles.boldLabel);
			EditorGUILayout.HelpBox("CREATE mode: clicking creates triangles. EDIT mode: clicking does not create; it selects and edits anchors or uses Unity gizmos. Press X in EDIT mode to extend from 2 selected anchors/edge.", MessageType.Info);
			EditorGUILayout.Space();

			EditorGUILayout.BeginHorizontal();
			if (GUILayout.Button("CREATE", GUILayout.Height(28f)))
			{
				createMode = true;
				editMode = false;
				viewMode = false;
				joinVertexMode = false;
				ResetJoinSelection();
				CancelPendingExtend();
				UpdateActivePatchEditorState();
			}

			if (GUILayout.Button("EDIT", GUILayout.Height(28f)))
			{
				createMode = false;
				editMode = true;
				viewMode = false;
				UpdateActivePatchEditorState();
			}

			if (GUILayout.Button("VIEW", GUILayout.Height(28f)))
			{
				createMode = false;
				editMode = false;
				viewMode = true;
				joinVertexMode = false;
				ResetJoinSelection();
				CancelPendingExtend();
				UpdateActivePatchEditorState();
			}
			EditorGUILayout.EndHorizontal();
			EditorGUILayout.LabelField("Current Mode", viewMode ? "VIEW" : (createMode ? "CREATE" : "EDIT"));

			using (new EditorGUI.DisabledScope(!editMode))
			{
				bool joinModeToggled = GUILayout.Toggle(joinVertexMode, "Join Vertex Mode", "Button");
				if (joinModeToggled != joinVertexMode)
				{
					joinVertexMode = joinModeToggled;
					if (!joinVertexMode)
					{
						ResetJoinSelection();
					}
				}
			}

			EditorGUILayout.Space();
			activePatch = (TriangleSurfacePatch)EditorGUILayout.ObjectField("Active Patch", activePatch, typeof(TriangleSurfacePatch), true);
			EditorGUILayout.BeginHorizontal();
			if (GUILayout.Button("Use Selected", GUILayout.Width(120f)))
			{
				TryBindFromSelection();
			}
			if (GUILayout.Button("Use Selected Mesh", GUILayout.Width(140f)))
			{
				TryBindSelectedMeshGameObject();
			}
			EditorGUILayout.EndHorizontal();
			EditorGUILayout.BeginHorizontal();
			if (GUILayout.Button("Save Metadata (.txt)", GUILayout.Width(170f)))
			{
				SaveActivePatchMetadata();
			}
			if (GUILayout.Button("Load Metadata (.txt)", GUILayout.Width(170f)))
			{
				LoadMetadataFromFile();
			}
			EditorGUILayout.EndHorizontal();

			EditorGUILayout.Space();
			newTriangleRadius = EditorGUILayout.Slider("New Triangle Radius", newTriangleRadius, 0.05f, 2f);
			parentPatchToHitRenderer = EditorGUILayout.Toggle("Parent Patch To Hit Renderer", parentPatchToHitRenderer);
			EditorGUILayout.BeginHorizontal();
			defaultMaterial = (Material)EditorGUILayout.ObjectField("Default Material", defaultMaterial, typeof(Material), false);
			using (new EditorGUI.DisabledScope(activePatch == null || defaultMaterial == null))
			{
				if (GUILayout.Button("Apply Material", GUILayout.Width(120f)))
				{
					Undo.RecordObject(activePatch, "Apply Triangle Patch Material");
					activePatch.SurfaceMaterial = defaultMaterial;
					EditorUtility.SetDirty(activePatch);
				}
			}
			EditorGUILayout.EndHorizontal();
			maxUvStretch = EditorGUILayout.Slider("Max UV Stretch", maxUvStretch, 0f, 2f);
			normalSmoothingFactor = EditorGUILayout.Slider("Normal Smoothing Factor", normalSmoothingFactor, 0f, 1f);

			if (selectedEdge.IsValid)
			{
				EditorGUILayout.Space();
				EditorGUILayout.LabelField("Selected Edge", "Anchor " + selectedEdge.a + " - Anchor " + selectedEdge.b);
				EditorGUILayout.BeginHorizontal();
				if (GUILayout.Button("Arm Extend From Selected Edge (X)"))
				{
					ArmExtendFromEdge(selectedEdge.patch, selectedEdge.a, selectedEdge.b);
				}

				if (GUILayout.Button("Clear Edge Selection"))
				{
					ClearEdgeSelection();
				}
				EditorGUILayout.EndHorizontal();
			}

			if (joinVertexMode && editMode)
			{
				EditorGUILayout.Space();
				EditorGUILayout.LabelField("Join Vertex Selection", EditorStyles.boldLabel);
				EditorGUILayout.HelpBox("Click anchor Vertex 1, then click anchor Vertex 2. Vertex 1 will be remapped into Vertex 2 and removed.", MessageType.Warning);
				EditorGUILayout.LabelField("Vertex 1 (source)", joinSourceAnchor >= 0 ? joinSourceAnchor.ToString() : "Not Selected");
				EditorGUILayout.LabelField("Vertex 2 (target)", joinTargetAnchor >= 0 ? joinTargetAnchor.ToString() : "Not Selected");
				EditorGUILayout.BeginHorizontal();
				using (new EditorGUI.DisabledScope(activePatch == null || joinSourceAnchor < 0 || joinTargetAnchor < 0 || joinSourceAnchor == joinTargetAnchor))
				{
					if (GUILayout.Button("Join Now (V1 -> V2)"))
					{
						JoinVertices(joinSourceAnchor, joinTargetAnchor);
					}
				}

				if (GUILayout.Button("Clear Join Picks"))
				{
					ResetJoinSelection();
				}
				EditorGUILayout.EndHorizontal();
			}

			using (new EditorGUI.DisabledScope(activePatch == null))
			{
				if (GUILayout.Button("Smooth Normals"))
				{
					Undo.RecordObject(activePatch, "Smooth Triangle Patch Normals");
					activePatch.SmoothNormals(normalSmoothingFactor);
					EditorUtility.SetDirty(activePatch);
				}

				if (GUILayout.Button("Auto Unwrap UVs (Seam by Stretch)"))
				{
					Undo.RecordObject(activePatch, "Auto Unwrap Triangle Surface");
					activePatch.AutoUnwrap(maxUvStretch);
					EditorUtility.SetDirty(activePatch);
				}

				if (GUILayout.Button("Bake Mesh"))
				{
					BakeActivePatch();
				}

				if (GUILayout.Button("Bake to OBJ/MTL File"))
				{
					BakeActivePatchToFile();
				}
			}

			if (activePatch != null)
			{
				EditorGUILayout.Space();
				EditorGUILayout.LabelField("Anchors", activePatch.AnchorCount.ToString());
				EditorGUILayout.LabelField("Triangles", activePatch.FaceCount.ToString());
				EditorGUILayout.HelpBox("Select an edge by clicking its midpoint in Scene view, or click anchor points (Shift+click to multi-select) and press X.", MessageType.None);
			}

			if (pendingTriangleExtend)
			{
				EditorGUILayout.Space();
				EditorGUILayout.HelpBox("Pending extend: click a collider surface to place the new vertex and create a new triangle.", MessageType.Warning);
			}

			EditorGUILayout.EndScrollView();
		}

		private void OnSceneGUI(SceneView sceneView)
		{
			if (viewMode || (!editMode && !createMode))
			{
				if (activePatch != null)
				{
					activePatch.SetEditorHelperActive(false);
				}
				return;
			}

			if (editMode)
			{
				HandleMouseUpRebuild();
				DrawEdgeSelectionHandles();
				HandleAnchorDragging();
				HandleTriangleExtendHotkey();
				HandlePendingTrianglePlacement();
			}

			if (createMode)
			{
				HandleSpawnTriangleClick();
			}
		}

		private void HandleMouseUpRebuild()
		{
			if (activePatch == null)
			{
				return;
			}

			Event evt = Event.current;
			if (evt == null || evt.type != EventType.MouseUp || evt.button != 0)
			{
				return;
			}

			activePatch.RebuildMesh();
			EditorUtility.SetDirty(activePatch);
		}

		private void DrawEdgeSelectionHandles()
		{
			if (activePatch == null)
			{
				return;
			}

			HashSet<long> emitted = new HashSet<long>();
			IReadOnlyList<TriangleSurfacePatch.TriangleFace> faces = activePatch.Faces;
			for (int faceIndex = 0; faceIndex < faces.Count; faceIndex++)
			{
				TriangleSurfacePatch.TriangleFace face = faces[faceIndex];
				DrawSelectableEdge(face.a, face.b, emitted);
				DrawSelectableEdge(face.b, face.c, emitted);
				DrawSelectableEdge(face.c, face.a, emitted);
			}
		}

		private void DrawSelectableEdge(int a, int b, HashSet<long> emitted)
		{
			if (activePatch == null)
			{
				return;
			}

			Transform aTransform = activePatch.GetAnchor(a);
			Transform bTransform = activePatch.GetAnchor(b);
			if (aTransform == null || bTransform == null)
			{
				return;
			}

			EdgeKey key = new EdgeKey(a, b);
			long uniqueKey = ((long)key.min << 32) | (uint)key.max;
			if (emitted.Contains(uniqueKey))
			{
				return;
			}

			emitted.Add(uniqueKey);

			bool isSelected = selectedEdge.patch == activePatch &&
				((selectedEdge.a == key.min && selectedEdge.b == key.max) || (selectedEdge.a == key.max && selectedEdge.b == key.min));

			Handles.color = isSelected ? Color.yellow : new Color(0.2f, 1f, 0.95f, 0.85f);
			Handles.DrawAAPolyLine(4f, aTransform.position, bTransform.position);

			Vector3 midpoint = (aTransform.position + bTransform.position) * 0.5f;
			float buttonSize = HandleUtility.GetHandleSize(midpoint) * 0.07f;
			if (Handles.Button(midpoint, Quaternion.identity, buttonSize, buttonSize * 1.35f, Handles.RectangleHandleCap))
			{
				selectedEdge.patch = activePatch;
				selectedEdge.a = key.min;
				selectedEdge.b = key.max;
				Repaint();
				Event.current.Use();
			}
		}

		private void HandleAnchorDragging()
		{
			if (activePatch == null)
			{
				return;
			}

			Event evt = Event.current;

			for (int i = 0; i < activePatch.AnchorCount; i++)
			{
				Transform anchor = activePatch.GetAnchor(i);
				if (anchor == null)
				{
					continue;
				}

				Handles.color = Color.cyan;
				float handleSize = HandleUtility.GetHandleSize(anchor.position) * AnchorHandleSize;
				int handleControlId = GUIUtility.GetControlID(FocusType.Passive);

				if (evt != null && evt.type == EventType.MouseDown && evt.button == 0 && HandleUtility.nearestControl == handleControlId)
				{
					if (joinVertexMode)
					{
						HandleJoinAnchorClick(anchor);
					}
					else
					{
						SelectAnchor(anchor, evt.shift);
					}

					Repaint();
				}

				EditorGUI.BeginChangeCheck();
				var fmh_366_84_639226591239624824 = Quaternion.identity; Vector3 newPosition = Handles.FreeMoveHandle(handleControlId, anchor.position, handleSize, Vector3.zero, Handles.SphereHandleCap);
				if (EditorGUI.EndChangeCheck())
				{
					Undo.RecordObject(anchor, "Move Triangle Anchor");
					anchor.position = newPosition;
					Undo.RecordObject(activePatch, "Rebuild Triangle Surface");
					activePatch.RebuildMesh();
					EditorUtility.SetDirty(anchor);
					EditorUtility.SetDirty(activePatch);
				}
			}
		}

		private void HandleTriangleExtendHotkey()
		{
			Event evt = Event.current;
			if (evt == null || evt.type != EventType.KeyDown || evt.keyCode != KeyCode.X)
			{
				return;
			}

			if (selectedEdge.IsValid && selectedEdge.patch != null)
			{
				ArmExtendFromEdge(selectedEdge.patch, selectedEdge.a, selectedEdge.b);
				evt.Use();
				return;
			}

			List<TriangleSurfaceAnchor> selectedAnchors = GetSelectedAnchorsForSinglePatch(out TriangleSurfacePatch patch);
			if (selectedAnchors.Count != 2 || patch == null)
			{
				return;
			}

			ArmExtendFromEdge(patch, selectedAnchors[0].AnchorIndex, selectedAnchors[1].AnchorIndex);
			evt.Use();
			Repaint();
		}

		private void HandlePendingTrianglePlacement()
		{
			if (!pendingTriangleExtend || pendingPatch == null)
			{
				return;
			}

			Event evt = Event.current;
			if (evt == null)
			{
				return;
			}

			if (TryGetMouseSurfaceHit(evt, out SurfaceHit hit))
			{
				pendingPoint = hit.point;
				pendingNormal = hit.normal;
			}

			if (pendingAnchorA < 0 || pendingAnchorB < 0 || pendingPatch.GetAnchor(pendingAnchorA) == null || pendingPatch.GetAnchor(pendingAnchorB) == null)
			{
				CancelPendingExtend();
				return;
			}

			Transform a = pendingPatch.GetAnchor(pendingAnchorA);
			Transform b = pendingPatch.GetAnchor(pendingAnchorB);
			Handles.color = Color.yellow;
			Handles.DrawLine(a.position, pendingPoint);
			Handles.DrawLine(b.position, pendingPoint);
			Handles.SphereHandleCap(0, pendingPoint, Quaternion.identity, HandleUtility.GetHandleSize(pendingPoint) * 0.06f, EventType.Repaint);

			if (evt.type == EventType.MouseDown && evt.button == 0 && !evt.alt && TryGetMouseSurfaceHit(evt, out hit))
			{
				if (IsPointerOverSceneHandle(evt))
				{
					return;
				}

				Undo.IncrementCurrentGroup();
				int group = Undo.GetCurrentGroup();
				Undo.SetCurrentGroupName("Extend Triangle Surface");
				Undo.RecordObject(pendingPatch, "Extend Triangle Surface");

				Vector3 newAnchorPosition = hit.point + hit.normal * 0.0005f;
				int newAnchor = pendingPatch.AddAnchor(newAnchorPosition);
				Undo.RegisterCreatedObjectUndo(pendingPatch.GetAnchor(newAnchor).gameObject, "Create Triangle Anchor");

				int first = pendingAnchorA;
				int second = pendingAnchorB;
				if (TryGetReferenceNormalForEdge(pendingPatch, pendingAnchorA, pendingAnchorB, out Vector3 referenceNormal))
				{
					Transform aTransform = pendingPatch.GetAnchor(pendingAnchorA);
					Transform bTransform = pendingPatch.GetAnchor(pendingAnchorB);
					if (aTransform != null && bTransform != null)
					{
						Vector3 candidateNormal = Vector3.Cross(bTransform.position - aTransform.position, newAnchorPosition - aTransform.position);
						if (candidateNormal.sqrMagnitude > Epsilon && Vector3.Dot(candidateNormal, referenceNormal) < 0f)
						{
							first = pendingAnchorB;
							second = pendingAnchorA;
						}
					}
				}

				pendingPatch.AddTriangle(first, second, newAnchor);
				EditorUtility.SetDirty(pendingPatch);
				Undo.CollapseUndoOperations(group);

				CancelPendingExtend();
				evt.Use();
			}
		}

		private void HandleSpawnTriangleClick()
		{
			Event evt = Event.current;
			if (evt == null)
			{
				return;
			}

			if (evt.type != EventType.MouseDown || evt.button != 0 || evt.alt)
			{
				return;
			}

			if (IsPointerOverSceneHandle(evt))
			{
				return;
			}

			if (!TryGetMouseSurfaceHit(evt, out SurfaceHit hit))
			{
				return;
			}

			CreatePatchAt(hit);
			evt.Use();
		}

		private void CreatePatchAt(SurfaceHit hit)
		{
			GameObject root = new GameObject("TriangleSurfacePatch");
			root.hideFlags = HideFlags.HideAndDontSave | HideFlags.NotEditable;
			Undo.RegisterCreatedObjectUndo(root, "Create Triangle Surface Patch");

			if (parentPatchToHitRenderer && hit.hitTransform != null)
			{
				root.transform.SetParent(hit.hitTransform, true);
			}

			TriangleSurfacePatch patch = root.AddComponent<TriangleSurfacePatch>();
			patch.SurfaceMaterial = defaultMaterial;
			patch.InitializeTriangle(hit.point + hit.normal.normalized * 0.001f, hit.normal, newTriangleRadius);
			EditorUtility.SetDirty(patch);
			activePatch = patch;
			editorProxyRoot = root;
			ClearEdgeSelection();
			Selection.activeGameObject = root;
		}

		private static bool TryGetMouseSurfaceHit(Event evt, out SurfaceHit hit)
		{
			hit = default;
			Ray ray = HandleUtility.GUIPointToWorldRay(evt.mousePosition);
			if (Physics.Raycast(ray, out RaycastHit hitInfo, 10000f))
			{
				hit.point = hitInfo.point;
				hit.normal = hitInfo.normal.sqrMagnitude > Epsilon ? hitInfo.normal.normalized : Vector3.up;
				hit.hitTransform = hitInfo.collider != null ? hitInfo.collider.transform : null;
				return true;
			}

			return TryGetMeshRendererSurfaceHit(evt.mousePosition, ray, out hit);
		}

		private static bool TryRaycastPatchSurface(TriangleSurfacePatch patch, Ray worldRay, out SurfaceHit hit)
		{
			hit = default;
			if (patch == null)
			{
				return false;
			}

			MeshFilter meshFilter = patch.GetComponent<MeshFilter>();
			if (meshFilter == null || meshFilter.sharedMesh == null)
			{
				return false;
			}

			Mesh mesh = meshFilter.sharedMesh;
			Vector3[] vertices = mesh.vertices;
			Vector3[] normals = mesh.normals;
			int[] triangles = mesh.triangles;
			if (vertices == null || triangles == null || triangles.Length < 3)
			{
				return false;
			}

			Matrix4x4 localToWorld = meshFilter.transform.localToWorldMatrix;
			float bestDistance = float.MaxValue;
			Vector3 bestPoint = Vector3.zero;
			Vector3 bestNormal = Vector3.up;
			bool found = false;

			for (int i = 0; i <= triangles.Length - 3; i += 3)
			{
				int i0 = triangles[i];
				int i1 = triangles[i + 1];
				int i2 = triangles[i + 2];
				if (i0 < 0 || i1 < 0 || i2 < 0 || i0 >= vertices.Length || i1 >= vertices.Length || i2 >= vertices.Length)
				{
					continue;
				}

				Vector3 v0 = localToWorld.MultiplyPoint3x4(vertices[i0]);
				Vector3 v1 = localToWorld.MultiplyPoint3x4(vertices[i1]);
				Vector3 v2 = localToWorld.MultiplyPoint3x4(vertices[i2]);

				if (!RayIntersectsTriangle(worldRay, v0, v1, v2, out float distance, out float u, out float v))
				{
					continue;
				}

				if (distance < 0f || distance >= bestDistance)
				{
					continue;
				}

				bestDistance = distance;
				bestPoint = worldRay.origin + worldRay.direction * distance;
				Vector3 triangleNormal = Vector3.Cross(v1 - v0, v2 - v0).normalized;

				if (normals != null && normals.Length == vertices.Length)
				{
					float w = 1f - u - v;
					Vector3 n0 = localToWorld.MultiplyVector(normals[i0]);
					Vector3 n1 = localToWorld.MultiplyVector(normals[i1]);
					Vector3 n2 = localToWorld.MultiplyVector(normals[i2]);
					Vector3 blended = (n0 * w) + (n1 * u) + (n2 * v);
					if (blended.sqrMagnitude > Epsilon)
					{
						triangleNormal = blended.normalized;
					}
				}

				bestNormal = triangleNormal.sqrMagnitude > Epsilon ? triangleNormal : Vector3.up;
				found = true;
			}

			if (!found)
			{
				return false;
			}

			hit.point = bestPoint;
			hit.normal = bestNormal;
			hit.hitTransform = meshFilter.transform;
			return true;
		}

		private static bool TryGetMeshRendererSurfaceHit(Vector2 guiPosition, Ray worldRay, out SurfaceHit hit)
		{
			hit = default;

			GameObject picked = HandleUtility.PickGameObject(guiPosition, false);
			if (picked == null)
			{
				return false;
			}

			MeshFilter meshFilter = picked.GetComponentInParent<MeshFilter>();
			if (meshFilter == null || meshFilter.sharedMesh == null)
			{
				return false;
			}

			Mesh mesh = meshFilter.sharedMesh;
			Vector3[] vertices = mesh.vertices;
			Vector3[] normals = mesh.normals;
			int[] triangles = mesh.triangles;
			if (vertices == null || triangles == null || triangles.Length < 3)
			{
				return false;
			}

			Matrix4x4 localToWorld = meshFilter.transform.localToWorldMatrix;
			float bestDistance = float.MaxValue;
			Vector3 bestPoint = Vector3.zero;
			Vector3 bestNormal = Vector3.up;
			bool found = false;

			for (int i = 0; i <= triangles.Length - 3; i += 3)
			{
				int i0 = triangles[i];
				int i1 = triangles[i + 1];
				int i2 = triangles[i + 2];
				if (i0 < 0 || i1 < 0 || i2 < 0 || i0 >= vertices.Length || i1 >= vertices.Length || i2 >= vertices.Length)
				{
					continue;
				}

				Vector3 v0 = localToWorld.MultiplyPoint3x4(vertices[i0]);
				Vector3 v1 = localToWorld.MultiplyPoint3x4(vertices[i1]);
				Vector3 v2 = localToWorld.MultiplyPoint3x4(vertices[i2]);

				if (!RayIntersectsTriangle(worldRay, v0, v1, v2, out float distance, out float u, out float v))
				{
					continue;
				}

				if (distance < 0f || distance >= bestDistance)
				{
					continue;
				}

				bestDistance = distance;
				bestPoint = worldRay.origin + worldRay.direction * distance;
				Vector3 triangleNormal = Vector3.Cross(v1 - v0, v2 - v0).normalized;

				if (normals != null && normals.Length == vertices.Length)
				{
					float w = 1f - u - v;
					Vector3 n0 = localToWorld.MultiplyVector(normals[i0]);
					Vector3 n1 = localToWorld.MultiplyVector(normals[i1]);
					Vector3 n2 = localToWorld.MultiplyVector(normals[i2]);
					Vector3 blended = (n0 * w) + (n1 * u) + (n2 * v);
					if (blended.sqrMagnitude > Epsilon)
					{
						triangleNormal = blended.normalized;
					}
				}

				bestNormal = triangleNormal.sqrMagnitude > Epsilon ? triangleNormal : Vector3.up;
				found = true;
			}

			if (!found)
			{
				return false;
			}

			hit.point = bestPoint;
			hit.normal = bestNormal;
			hit.hitTransform = meshFilter.transform;
			return true;
		}

		private static bool RayIntersectsTriangle(Ray ray, Vector3 v0, Vector3 v1, Vector3 v2, out float t, out float u, out float v)
		{
			t = 0f;
			u = 0f;
			v = 0f;

			Vector3 edge1 = v1 - v0;
			Vector3 edge2 = v2 - v0;
			Vector3 pVec = Vector3.Cross(ray.direction, edge2);
			float det = Vector3.Dot(edge1, pVec);

			if (Mathf.Abs(det) < Epsilon)
			{
				return false;
			}

			float invDet = 1f / det;
			Vector3 tVec = ray.origin - v0;
			u = Vector3.Dot(tVec, pVec) * invDet;
			if (u < 0f || u > 1f)
			{
				return false;
			}

			Vector3 qVec = Vector3.Cross(tVec, edge1);
			v = Vector3.Dot(ray.direction, qVec) * invDet;
			if (v < 0f || u + v > 1f)
			{
				return false;
			}

			t = Vector3.Dot(edge2, qVec) * invDet;
			return t >= 0f;
		}

		private static bool IsPointerOverSceneHandle(Event evt)
		{
			if (evt == null)
			{
				return false;
			}

			// Only block while a handle/gizmo is actively captured.
			// Using nearestControl here also catches this tool's own passive handles
			// and can block intended click-to-place behavior.
			if (GUIUtility.hotControl != NoControl)
			{
				return true;
			}

			return false;
		}

		private static bool TryGetSceneViewCenterSurfaceHit(out SurfaceHit hit)
		{
			hit = default;
			SceneView sceneView = SceneView.lastActiveSceneView;
			if (sceneView == null || sceneView.camera == null)
			{
				return false;
			}

			Camera camera = sceneView.camera;
			Ray centerRay = camera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
			if (Physics.Raycast(centerRay, out RaycastHit hitInfo, 10000f))
			{
				hit.point = hitInfo.point;
				hit.normal = hitInfo.normal.sqrMagnitude > Epsilon ? hitInfo.normal.normalized : Vector3.up;
				hit.hitTransform = hitInfo.collider != null ? hitInfo.collider.transform : null;
				return true;
			}

			return TryGetAnyMeshRendererSurfaceHit(centerRay, out hit);
		}

		private static bool TryGetAnyMeshRendererSurfaceHit(Ray worldRay, out SurfaceHit hit)
		{
			hit = default;
			MeshFilter[] meshFilters = Object.FindObjectsByType<MeshFilter>(FindObjectsSortMode.None);
			if (meshFilters == null || meshFilters.Length == 0)
			{
				return false;
			}

			float bestDistance = float.MaxValue;
			Vector3 bestPoint = Vector3.zero;
			Vector3 bestNormal = Vector3.up;
			Transform bestTransform = null;
			bool found = false;

			for (int filterIndex = 0; filterIndex < meshFilters.Length; filterIndex++)
			{
				MeshFilter meshFilter = meshFilters[filterIndex];
				if (meshFilter == null || meshFilter.sharedMesh == null)
				{
					continue;
				}

				MeshRenderer meshRenderer = meshFilter.GetComponent<MeshRenderer>();
				if (meshRenderer == null || !meshRenderer.enabled)
				{
					continue;
				}

				Mesh mesh = meshFilter.sharedMesh;
				Vector3[] vertices = mesh.vertices;
				int[] triangles = mesh.triangles;
				if (vertices == null || triangles == null || triangles.Length < 3)
				{
					continue;
				}

				Matrix4x4 localToWorld = meshFilter.transform.localToWorldMatrix;
				for (int i = 0; i <= triangles.Length - 3; i += 3)
				{
					int i0 = triangles[i];
					int i1 = triangles[i + 1];
					int i2 = triangles[i + 2];
					if (i0 < 0 || i1 < 0 || i2 < 0 || i0 >= vertices.Length || i1 >= vertices.Length || i2 >= vertices.Length)
					{
						continue;
					}

					Vector3 v0 = localToWorld.MultiplyPoint3x4(vertices[i0]);
					Vector3 v1 = localToWorld.MultiplyPoint3x4(vertices[i1]);
					Vector3 v2 = localToWorld.MultiplyPoint3x4(vertices[i2]);
					if (!RayIntersectsTriangle(worldRay, v0, v1, v2, out float distance, out _, out _))
					{
						continue;
					}

					if (distance < 0f || distance >= bestDistance)
					{
						continue;
					}

					bestDistance = distance;
					bestPoint = worldRay.origin + worldRay.direction * distance;
					Vector3 triangleNormal = Vector3.Cross(v1 - v0, v2 - v0);
					bestNormal = triangleNormal.sqrMagnitude > Epsilon ? triangleNormal.normalized : Vector3.up;
					bestTransform = meshFilter.transform;
					found = true;
				}
			}

			if (!found)
			{
				return false;
			}

			hit.point = bestPoint;
			hit.normal = bestNormal;
			hit.hitTransform = bestTransform;
			return true;
		}

		private List<TriangleSurfaceAnchor> GetSelectedAnchorsForSinglePatch(out TriangleSurfacePatch patch)
		{
			patch = null;
			List<TriangleSurfaceAnchor> anchors = new List<TriangleSurfaceAnchor>();
			Transform[] selected = Selection.transforms;
			for (int i = 0; i < selected.Length; i++)
			{
				TriangleSurfaceAnchor anchor = selected[i].GetComponent<TriangleSurfaceAnchor>();
				if (anchor == null || anchor.Owner == null)
				{
					continue;
				}

				if (patch == null)
				{
					patch = anchor.Owner;
				}
				else if (patch != anchor.Owner)
				{
					anchors.Clear();
					patch = null;
					return anchors;
				}

				anchors.Add(anchor);
			}

			return anchors;
		}

		private void BakeActivePatch()
		{
			if (activePatch == null)
			{
				return;
			}

			MeshFilter sourceFilter = activePatch.GetComponent<MeshFilter>();
			if (sourceFilter == null || sourceFilter.sharedMesh == null)
			{
				EditorUtility.DisplayDialog("Triangle Surface Tool", "This patch has no mesh to bake.", "OK");
				return;
			}

			string bakedObjectName = activePatch.name + "_BakedMesh";
			GameObject bakedRoot = null;
			Transform parentTransform = activePatch.transform.parent;
			if (parentTransform != null)
			{
				bakedRoot = parentTransform.Find(bakedObjectName)?.gameObject;
			}

			if (bakedRoot == null)
			{
				bakedRoot = GameObject.Find(bakedObjectName);
			}

			if (bakedRoot == null)
			{
				bakedRoot = new GameObject(bakedObjectName);
				Undo.RegisterCreatedObjectUndo(bakedRoot, "Bake Triangle Surface Mesh");
				bakedRoot.transform.SetParent(parentTransform, true);
				bakedRoot.transform.SetPositionAndRotation(activePatch.transform.position, activePatch.transform.rotation);
				bakedRoot.transform.localScale = activePatch.transform.localScale;
			}
			else
			{
				Undo.RecordObject(bakedRoot.transform, "Update Baked Triangle Surface Mesh");
				bakedRoot.transform.SetParent(parentTransform, true);
				bakedRoot.transform.SetPositionAndRotation(activePatch.transform.position, activePatch.transform.rotation);
				bakedRoot.transform.localScale = activePatch.transform.localScale;
			}

			MeshFilter bakedFilter = bakedRoot.GetComponent<MeshFilter>();
			if (bakedFilter == null)
			{
				bakedFilter = bakedRoot.AddComponent<MeshFilter>();
			}

			MeshRenderer bakedRenderer = bakedRoot.GetComponent<MeshRenderer>();
			if (bakedRenderer == null)
			{
				bakedRenderer = bakedRoot.AddComponent<MeshRenderer>();
			}

			Mesh bakedMesh = Object.Instantiate(sourceFilter.sharedMesh);
			bakedMesh.name = sourceFilter.sharedMesh.name + "_Baked";
			Undo.RecordObject(bakedFilter, "Update Baked Triangle Surface Mesh");
			Undo.RecordObject(bakedRenderer, "Update Baked Triangle Surface Mesh");
			bakedFilter.sharedMesh = bakedMesh;
			bakedRenderer.sharedMaterial = activePatch.SurfaceMaterial != null ? activePatch.SurfaceMaterial : null;
			Selection.activeGameObject = bakedRoot;
			EditorUtility.SetDirty(bakedRoot);
			EditorUtility.SetDirty(bakedFilter);
			EditorUtility.SetDirty(bakedRenderer);
			EditorUtility.DisplayDialog("Triangle Surface Tool", "Baked mesh updated on: " + bakedRoot.name, "OK");
		}

		private void BakeActivePatchToFile()
		{
			if (activePatch == null)
			{
				EditorUtility.DisplayDialog("Triangle Surface Tool", "No active patch selected to export.", "OK");
				return;
			}

			activePatch.RebuildMesh();
			MeshFilter sourceFilter = activePatch.GetComponent<MeshFilter>();
			if (sourceFilter == null)
			{
				sourceFilter = activePatch.gameObject.AddComponent<MeshFilter>();
			}

			if (sourceFilter.sharedMesh == null)
			{
				EditorUtility.DisplayDialog("Triangle Surface Tool", "This patch has no mesh to export.", "OK");
				return;
			}

			string defaultName = string.IsNullOrEmpty(activePatch.name) ? "triangle_surface" : activePatch.name;
			string objPath = EditorUtility.SaveFilePanel("Save Triangle Surface OBJ", Application.dataPath, defaultName + ".obj", "obj");
			if (string.IsNullOrEmpty(objPath))
			{
				return;
			}

			ExportPatchAsObjMtl(activePatch, objPath);
		}

		private void ExportPatchAsObjMtl(TriangleSurfacePatch patch, string objPath)
		{
			if (patch == null)
			{
				return;
			}

			MeshFilter filter = patch.GetComponent<MeshFilter>();
			if (filter == null || filter.sharedMesh == null)
			{
				EditorUtility.DisplayDialog("Triangle Surface Tool", "Cannot export a patch without a mesh.", "OK");
				return;
			}

			Mesh mesh = filter.sharedMesh;
			string directory = Path.GetDirectoryName(objPath);
			if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
			{
				Directory.CreateDirectory(directory);
			}

			string mtlPath = Path.ChangeExtension(objPath, ".mtl");
			StringBuilder obj = new StringBuilder(1024 * 64);
			StringBuilder mtl = new StringBuilder(1024 * 16);

			obj.AppendLine("# Triangle Surface Export");
			obj.AppendLine("mtllib " + Path.GetFileName(mtlPath));

			Vector3[] vertices = mesh.vertices;
			Vector3[] normals = mesh.normals;
			Vector2[] uvs = mesh.uv;
			bool hasNormals = normals != null && normals.Length == vertices.Length;
			bool hasUvs = uvs != null && uvs.Length == vertices.Length;

			Matrix4x4 localToWorld = patch.transform.localToWorldMatrix;
			for (int i = 0; i < vertices.Length; i++)
			{
				Vector3 v = localToWorld.MultiplyPoint3x4(vertices[i]);
				obj.AppendLine("v " + FormatObjFloat(v.x) + " " + FormatObjFloat(v.y) + " " + FormatObjFloat(v.z));
			}

			for (int i = 0; i < (hasUvs ? uvs.Length : vertices.Length); i++)
			{
				Vector2 uv = hasUvs ? uvs[i] : Vector2.zero;
				obj.AppendLine("vt " + FormatObjFloat(uv.x) + " " + FormatObjFloat(uv.y));
			}

			for (int i = 0; i < vertices.Length; i++)
			{
				Vector3 n = hasNormals ? localToWorld.MultiplyVector(normals[i]).normalized : Vector3.up;
				obj.AppendLine("vn " + FormatObjFloat(n.x) + " " + FormatObjFloat(n.y) + " " + FormatObjFloat(n.z));
			}

			Material material = patch.SurfaceMaterial;
			string materialName = SanitizeObjName(material != null ? material.name : "TriangleSurfaceMaterial");
			mtl.AppendLine("newmtl " + materialName);
			Color color = material != null && material.HasProperty("_Color") ? material.color : Color.white;
			mtl.AppendLine("Kd " + FormatObjFloat(color.r) + " " + FormatObjFloat(color.g) + " " + FormatObjFloat(color.b));
			mtl.AppendLine("Ka " + FormatObjFloat(color.r * 0.1f) + " " + FormatObjFloat(color.g * 0.1f) + " " + FormatObjFloat(color.b * 0.1f));
			mtl.AppendLine("Ks 0.1 0.1 0.1");
			mtl.AppendLine("d 1.0");
			mtl.AppendLine();

			obj.AppendLine("usemtl " + materialName);
			obj.AppendLine("g " + SanitizeObjName(patch.name));

			for (int subMeshIndex = 0; subMeshIndex < mesh.subMeshCount; subMeshIndex++)
			{
				int[] triangles = mesh.GetTriangles(subMeshIndex);
				for (int i = 0; i < triangles.Length; i += 3)
				{
					int a = triangles[i] + 1;
					int b = triangles[i + 1] + 1;
					int c = triangles[i + 2] + 1;
					int ua = triangles[i] + 1;
					int ub = triangles[i + 1] + 1;
					int uc = triangles[i + 2] + 1;
					int na = triangles[i] + 1;
					int nb = triangles[i + 1] + 1;
					int nc = triangles[i + 2] + 1;
					obj.AppendLine("f " + a + "/" + ua + "/" + na + " " + b + "/" + ub + "/" + nb + " " + c + "/" + uc + "/" + nc);
				}
			}

			if (mesh.subMeshCount == 0)
			{
				int[] triangles = mesh.triangles;
				for (int i = 0; i < triangles.Length; i += 3)
				{
					int a = triangles[i] + 1;
					int b = triangles[i + 1] + 1;
					int c = triangles[i + 2] + 1;
					obj.AppendLine("f " + a + " " + b + " " + c);
				}
			}

			File.WriteAllText(objPath, obj.ToString(), Encoding.UTF8);
			File.WriteAllText(mtlPath, mtl.ToString(), Encoding.UTF8);
			AssetDatabase.Refresh();
			EditorUtility.DisplayDialog("Triangle Surface Tool", "Exported OBJ and MTL:\n" + objPath + "\n" + mtlPath, "OK");
		}

		private static string FormatObjFloat(float value)
		{
			return value.ToString("0.######", CultureInfo.InvariantCulture);
		}

		private static string SanitizeObjName(string name)
		{
			if (string.IsNullOrEmpty(name))
			{
				return "TriangleSurface";
			}

			StringBuilder sanitized = new StringBuilder(name.Length);
			for (int i = 0; i < name.Length; i++)
			{
				char c = name[i];
				if (char.IsLetterOrDigit(c) || c == '_' || c == '-')
				{
					sanitized.Append(c);
				}
				else if (c == ' ')
				{
					sanitized.Append('_');
				}
			}

			if (sanitized.Length == 0)
			{
				return "TriangleSurface";
			}

			return sanitized.ToString();
		}

		private void SaveActivePatchMetadata()
		{
			if (activePatch == null)
			{
				return;
			}

			string defaultName = string.IsNullOrEmpty(activePatch.name) ? "triangle_patch" : activePatch.name;
			string path = EditorUtility.SaveFilePanel("Save Triangle Metadata", "Assets", defaultName + ".txt", "txt");
			if (string.IsNullOrEmpty(path))
			{
				return;
			}

			activePatch.SaveMetadataToTextFile(path);
		}

		private void LoadMetadataFromFile()
		{
			string path = EditorUtility.OpenFilePanel("Load Triangle Metadata", "Assets", "txt");
			if (string.IsNullOrEmpty(path))
			{
				return;
			}

			GameObject proxy = new GameObject("TriangleSurfacePatchProxy");
			proxy.hideFlags = HideFlags.HideAndDontSave | HideFlags.NotEditable;
			TriangleSurfacePatch patch = proxy.AddComponent<TriangleSurfacePatch>();
			if (!patch.TryLoadMetadataFromTextFile(path))
			{
				DestroyImmediate(proxy);
				EditorUtility.DisplayDialog("Triangle Surface Tool", "Failed to load metadata from the selected text file.", "OK");
				return;
			}

			activePatch = patch;
			editorProxyRoot = proxy;
			UpdateActivePatchEditorState();
			Selection.activeGameObject = proxy;
		}

		private void TryBindSelectedMeshGameObject()
		{
			GameObject selected = Selection.activeGameObject;
			if (selected == null)
			{
				return;
			}

			MeshFilter filter = selected.GetComponent<MeshFilter>();
			MeshRenderer renderer = selected.GetComponent<MeshRenderer>();
			if (filter == null || renderer == null || filter.sharedMesh == null)
			{
				EditorUtility.DisplayDialog("Triangle Surface Tool", "Select a GameObject with a MeshFilter, MeshRenderer, and a Mesh.", "OK");
				return;
			}

			if (editorProxyRoot != null)
			{
				DestroyImmediate(editorProxyRoot);
				editorProxyRoot = null;
			}

			GameObject proxy = new GameObject("TriangleSurfacePatchProxy");
			proxy.hideFlags = HideFlags.HideAndDontSave | HideFlags.NotEditable;
			proxy.transform.SetParent(selected.transform.parent, true);
			TriangleSurfacePatch patch = proxy.AddComponent<TriangleSurfacePatch>();
			patch.LoadFromMesh(filter.sharedMesh, selected.transform);

			activePatch = patch;
			editorProxyRoot = proxy;
			UpdateActivePatchEditorState();
			Selection.activeGameObject = proxy;
		}

		private void TryBindFromSelection()
		{
			if (Selection.activeTransform == null)
			{
				if (activePatch != null)
				{
					activePatch.SetEditorHelperActive(false);
				}
				return;
			}

			TriangleSurfacePatch patch = Selection.activeTransform.GetComponent<TriangleSurfacePatch>();
			if (patch == null)
			{
				TriangleSurfaceAnchor anchor = Selection.activeTransform.GetComponent<TriangleSurfaceAnchor>();
				if (anchor != null)
				{
					patch = anchor.Owner;
				}
			}

			if (patch != null)
			{
				if (editorProxyRoot != null && patch.gameObject != editorProxyRoot)
				{
					DestroyImmediate(editorProxyRoot);
					editorProxyRoot = null;
				}

				activePatch = patch;
				UpdateActivePatchEditorState();
			}
			else if (activePatch != null)
			{
				activePatch.SetEditorHelperActive(false);
			}
		}

		private void CleanupEditorProxy()
		{
			if (editorProxyRoot != null)
			{
				if (editorProxyRoot)
				{
					DestroyImmediate(editorProxyRoot);
				}

				editorProxyRoot = null;
			}

			activePatch = null;
		}

		private void UpdateActivePatchEditorState()
		{
			if (activePatch == null)
			{
				return;
			}

			bool shouldEnable = editMode || createMode;
			if (viewMode || !shouldEnable)
			{
				activePatch.SetEditorHelperActive(false);
				return;
			}

			activePatch.SetEditorHelperActive(true);
		}

		private void ArmExtendFromEdge(TriangleSurfacePatch patch, int a, int b)
		{
			if (patch == null || patch.GetAnchor(a) == null || patch.GetAnchor(b) == null || a == b)
			{
				return;
			}

			pendingTriangleExtend = true;
			pendingPatch = patch;
			pendingAnchorA = a;
			pendingAnchorB = b;
			activePatch = patch;
			selectedEdge.patch = patch;
			selectedEdge.a = a;
			selectedEdge.b = b;
		}

		private void HandleJoinAnchorClick(Transform anchorTransform)
		{
			if (activePatch == null || anchorTransform == null)
			{
				return;
			}

			if (!activePatch.TryGetAnchorIndex(anchorTransform, out int clickedIndex))
			{
				return;
			}

			if (joinSourceAnchor < 0)
			{
				joinSourceAnchor = clickedIndex;
				joinTargetAnchor = -1;
				return;
			}

			if (clickedIndex == joinSourceAnchor)
			{
				return;
			}

			joinTargetAnchor = clickedIndex;
			JoinVertices(joinSourceAnchor, joinTargetAnchor);
		}

		private void JoinVertices(int sourceAnchorIndex, int targetAnchorIndex)
		{
			if (activePatch == null)
			{
				return;
			}

			if (sourceAnchorIndex == targetAnchorIndex)
			{
				return;
			}

			Transform sourceAnchor = activePatch.GetAnchor(sourceAnchorIndex);
			if (sourceAnchor == null)
			{
				ResetJoinSelection();
				return;
			}

			Undo.IncrementCurrentGroup();
			int group = Undo.GetCurrentGroup();
			Undo.SetCurrentGroupName("Join Triangle Vertices");
			Undo.RecordObject(activePatch, "Join Triangle Vertices");

			bool merged = activePatch.MergeAnchorInto(sourceAnchorIndex, targetAnchorIndex);
			if (merged)
			{
				Undo.DestroyObjectImmediate(sourceAnchor.gameObject);
				EditorUtility.SetDirty(activePatch);
				Selection.activeGameObject = activePatch.gameObject;
				ClearEdgeSelection();
				CancelPendingExtend();
			}

			Undo.CollapseUndoOperations(group);
			ResetJoinSelection();
		}

		private static void SelectAnchor(Transform anchorTransform, bool append)
		{
			if (anchorTransform == null)
			{
				return;
			}

			if (!append)
			{
				Selection.activeTransform = anchorTransform;
				return;
			}

			List<Object> objects = new List<Object>(Selection.objects);
			if (!objects.Contains(anchorTransform.gameObject))
			{
				objects.Add(anchorTransform.gameObject);
				Selection.objects = objects.ToArray();
			}
			else
			{
				Selection.activeTransform = anchorTransform;
			}
		}

		private void ClearEdgeSelection()
		{
			selectedEdge.patch = null;
			selectedEdge.a = -1;
			selectedEdge.b = -1;
		}

		private void ResetJoinSelection()
		{
			joinSourceAnchor = -1;
			joinTargetAnchor = -1;
		}

		private static bool TryGetReferenceNormalForEdge(TriangleSurfacePatch patch, int a, int b, out Vector3 normal)
		{
			normal = Vector3.up;
			if (patch == null)
			{
				return false;
			}

			IReadOnlyList<TriangleSurfacePatch.TriangleFace> faces = patch.Faces;
			for (int i = 0; i < faces.Count; i++)
			{
				TriangleSurfacePatch.TriangleFace face = faces[i];
				if (!FaceContainsEdge(face, a, b))
				{
					continue;
				}

				Transform ta = patch.GetAnchor(face.a);
				Transform tb = patch.GetAnchor(face.b);
				Transform tc = patch.GetAnchor(face.c);
				if (ta == null || tb == null || tc == null)
				{
					continue;
				}

				Vector3 faceNormal = Vector3.Cross(tb.position - ta.position, tc.position - ta.position);
				if (faceNormal.sqrMagnitude <= Epsilon)
				{
					continue;
				}

				normal = faceNormal.normalized;
				return true;
			}

			return false;
		}

		private static bool FaceContainsEdge(TriangleSurfacePatch.TriangleFace face, int a, int b)
		{
			int matches = 0;
			if (face.a == a || face.a == b)
			{
				matches++;
			}

			if (face.b == a || face.b == b)
			{
				matches++;
			}

			if (face.c == a || face.c == b)
			{
				matches++;
			}

			return matches == 2;
		}

		private void CancelPendingExtend()
		{
			pendingTriangleExtend = false;
			pendingPatch = null;
			pendingAnchorA = -1;
			pendingAnchorB = -1;
			pendingPoint = Vector3.zero;
			pendingNormal = Vector3.up;
		}
	}
}
