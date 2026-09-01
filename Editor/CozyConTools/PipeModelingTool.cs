using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace CozyCon.Tools
{
	public class PipeModelingTool : EditorWindow
	{
		private Vector2 scrollPosition;

		private enum CornerAngleMode
		{
			Auto,
			Degree45,
			Degree90
		}

		private enum ToolMode
		{
			None,
			Create,
			Edit
		}

		private class PipeNode
		{
			public Transform transform;
			public CornerAngleMode cornerAngleMode = CornerAngleMode.Auto;
			public float cornerSize = 0.35f;
			public Material segmentMaterialOverride;
			public Material cornerMaterialOverride;
		}

		private struct CornerData
		{
			public bool hasCorner;
			public Vector3 entryPoint;
			public Vector3 exitPoint;
			public Vector3 controlPoint1;
			public Vector3 controlPoint2;
			public float cornerAngle;
		}

		private const string RootName = "PipeDraft";
		private const string NodesContainerName = "Nodes";
		private const string SegmentsContainerName = "Segments";

		private readonly List<PipeNode> nodes = new List<PipeNode>();

		private ToolMode mode = ToolMode.None;
		private PipeNode selectedNode;

		private GameObject rootObject;
		private Transform nodesContainer;
		private Transform segmentsContainer;

		private float nodeSphereRadius = 0.08f;
		private float nodeAxisLength = 0.32f;
		private float pipeRadius = 0.1f;
		private int radialSegments = 10;
		private int straightPathSegments = 1;
		private int cornerPathSegments = 14;
		private bool showJunctionCouplers = true;
		private bool couplerFacesMatchPipe = true;
		private int couplerRadialSegments = 10;
		private float couplerRadiusMultiplier = 1.15f;
		private float couplerLengthMultiplier = 2.2f;
		private int couplerLengthSegments = 1;
		private Material globalPipeMaterial;
		private static Material defaultMaterial;
		private int lastGeometryStateHash;

		[MenuItem("Lilithe/Pipe Modeling Tool")]
		public static void ShowWindow()
		{
			GetWindow<PipeModelingTool>("Pipe Modeling Tool");
		}

		private void OnEnable()
		{
			SceneView.duringSceneGui += OnSceneGUI;
			EditorApplication.update += OnEditorUpdate;
			TryBindExistingRoot();
			lastGeometryStateHash = ComputeGeometryStateHash();
		}

		private void OnDisable()
		{
			SceneView.duringSceneGui -= OnSceneGUI;
			EditorApplication.update -= OnEditorUpdate;
		}

		private void OnSelectionChange()
		{
			Transform selectedTransform = Selection.activeTransform;
			if (selectedTransform == null)
			{
				return;
			}

			if (TryBindRootFromSelection(selectedTransform))
			{
				for (int i = 0; i < nodes.Count; i++)
				{
					PipeNode node = nodes[i];
					if (node != null && node.transform == selectedTransform)
					{
						selectedNode = node;
						Repaint();
						return;
					}
				}

				selectedNode = null;
				Repaint();
				return;
			}

			for (int i = 0; i < nodes.Count; i++)
			{
				PipeNode node = nodes[i];
				if (node != null && node.transform == selectedTransform)
				{
					selectedNode = node;
					Repaint();
					return;
				}
			}
		}

		private void OnGUI()
		{
			scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);

			EditorGUILayout.LabelField("Pipe Modeling", EditorStyles.boldLabel);
			EditorGUILayout.HelpBox("Create mode: click in scene to add nodes. Nodes are shown as debug axes with a clickable sphere center. Corners are bezier elbows; straight runs stay straight.", MessageType.Info);
			EditorGUILayout.Space();

			EditorGUILayout.BeginHorizontal();
			GameObject selectedRoot = (GameObject)EditorGUILayout.ObjectField("Active Pipe", rootObject, typeof(GameObject), true);
			if (selectedRoot != rootObject)
			{
				if (selectedRoot == null)
				{
					UnbindActiveRoot();
				}
				else if (!BindToRoot(selectedRoot))
				{
					EditorUtility.DisplayDialog("Invalid Pipe", "Selected object is not a valid pipe draft root.", "OK");
				}
			}

			if (GUILayout.Button("Use Selected", GUILayout.Width(110f)))
			{
				TryBindRootFromSelection(Selection.activeTransform);
			}
			EditorGUILayout.EndHorizontal();
			EditorGUILayout.Space();

			DrawModeButtons();
			EditorGUILayout.Space();

			EditorGUI.BeginChangeCheck();
			pipeRadius = EditorGUILayout.Slider("Pipe Radius", pipeRadius, 0.02f, 1f);
			radialSegments = EditorGUILayout.IntSlider("Tube Radial Segments", radialSegments, 6, 24);
			straightPathSegments = EditorGUILayout.IntSlider("Pipe Segment Count", straightPathSegments, 1, 16);
			cornerPathSegments = EditorGUILayout.IntSlider("Corner Curve Segments", cornerPathSegments, 3, 48);
			showJunctionCouplers = EditorGUILayout.Toggle("Show Junction Couplers", showJunctionCouplers);
			couplerLengthMultiplier = EditorGUILayout.Slider("Coupler Length Multiplier", couplerLengthMultiplier, 0.25f, 8f);
			couplerLengthSegments = EditorGUILayout.IntSlider("Coupler Length Segments", couplerLengthSegments, 1, 16);
			couplerFacesMatchPipe = EditorGUILayout.Toggle("Coupler Faces Match Pipe", couplerFacesMatchPipe);
			if (!couplerFacesMatchPipe)
			{
				couplerRadialSegments = EditorGUILayout.IntSlider("Coupler Faces", couplerRadialSegments, 3, 48);
			}
			globalPipeMaterial = (Material)EditorGUILayout.ObjectField("Pipe Material", globalPipeMaterial, typeof(Material), false);
			if (EditorGUI.EndChangeCheck())
			{
				if (couplerFacesMatchPipe)
				{
					couplerRadialSegments = radialSegments;
				}
				RebuildPipe();
			}

			EditorGUILayout.Space();
			EditorGUILayout.LabelField($"Nodes: {nodes.Count}");

			EditorGUILayout.BeginHorizontal();
			if (GUILayout.Button("New Pipe"))
			{
				CreateNewPipe();
			}

			if (GUILayout.Button("Duplicate Pipe"))
			{
				DuplicatePipeDraft();
			}

			if (GUILayout.Button("Rebuild Geometry"))
			{
				RebuildPipe();
			}
			EditorGUILayout.EndHorizontal();

			EditorGUILayout.BeginHorizontal();
			if (GUILayout.Button("Delete Last Node"))
			{
				DeleteLastNode();
			}

			if (GUILayout.Button("Clear Pipe"))
			{
				ClearPipe();
			}
			EditorGUILayout.EndHorizontal();

			if (GUILayout.Button("Reset All Materials To Default"))
			{
				ResetAllMaterialsToDefault();
			}

			if (selectedNode != null && selectedNode.transform != null)
			{
				EditorGUILayout.Space();
				EditorGUILayout.LabelField("Selected Node", EditorStyles.boldLabel);
				EditorGUILayout.LabelField(selectedNode.transform.name);

				if (mode == ToolMode.Edit)
				{
					EditorGUI.BeginChangeCheck();
					Vector3 manualPosition = EditorGUILayout.Vector3Field("Position", selectedNode.transform.position);
					if (EditorGUI.EndChangeCheck())
					{
						Undo.RecordObject(selectedNode.transform, "Edit Pipe Node Position");
						selectedNode.transform.position = manualPosition;
						RebuildPipe();
					}

					EditorGUILayout.Space(2f);

					EditorGUI.BeginChangeCheck();
					CornerAngleMode newCornerMode = (CornerAngleMode)EditorGUILayout.EnumPopup("Corner Angle", selectedNode.cornerAngleMode);
					float newCornerSize = EditorGUILayout.Slider("Corner Size", selectedNode.cornerSize, 0.05f, 0.9f);
					Material newSegmentMaterial = (Material)EditorGUILayout.ObjectField("Segment Material", selectedNode.segmentMaterialOverride, typeof(Material), false);
					Material newCornerMaterial = (Material)EditorGUILayout.ObjectField("Corner Material", selectedNode.cornerMaterialOverride, typeof(Material), false);
					EditorGUILayout.HelpBox("Corners use 45 or 90 degree bezier elbow pieces. Straight runs between corners remain straight.", MessageType.None);

					if (EditorGUI.EndChangeCheck())
					{
						selectedNode.cornerAngleMode = newCornerMode;
						selectedNode.cornerSize = newCornerSize;
						selectedNode.segmentMaterialOverride = newSegmentMaterial;
						selectedNode.cornerMaterialOverride = newCornerMaterial;
						RebuildPipe();
					}
				}
				else
				{
					EditorGUILayout.LabelField($"Corner Mode: {selectedNode.cornerAngleMode}");
					EditorGUILayout.LabelField($"Corner Size: {selectedNode.cornerSize:F2}");
				}
			}

			EditorGUILayout.EndScrollView();
		}

		private void DrawModeButtons()
		{
			EditorGUILayout.BeginHorizontal();

			bool createPressed = GUILayout.Toggle(mode == ToolMode.Create, "Create Pipe Mode", "Button");
			if (createPressed && mode != ToolMode.Create)
			{
				mode = ToolMode.Create;
			}
			else if (!createPressed && mode == ToolMode.Create)
			{
				mode = ToolMode.None;
			}

			bool editPressed = GUILayout.Toggle(mode == ToolMode.Edit, "Edit Pipe Mode", "Button");
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

		private void OnSceneGUI(SceneView sceneView)
		{
			if (mode == ToolMode.None)
			{
				return;
			}

			EnsureRoot();
			DrawNodeHandles();

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
					Ray worldRay = HandleUtility.GUIPointToWorldRay(evt.mousePosition);
					if (TryGetScenePosition(worldRay, out Vector3 worldPosition))
					{
						AddNode(worldPosition);
						evt.Use();
					}
				}
			}

			if (mode == ToolMode.Edit && selectedNode != null && selectedNode.transform != null)
			{
				EditorGUI.BeginChangeCheck();
				Vector3 newPosition = Handles.PositionHandle(selectedNode.transform.position, Quaternion.identity);
				if (EditorGUI.EndChangeCheck())
				{
					Undo.RecordObject(selectedNode.transform, "Move Pipe Node");
					selectedNode.transform.position = newPosition;
					RebuildPipe();
				}
			}
		}

		private void OnEditorUpdate()
		{
			int currentStateHash = ComputeGeometryStateHash();
			if (currentStateHash != lastGeometryStateHash)
			{
				lastGeometryStateHash = currentStateHash;
				if (nodes.Count >= 2 && segmentsContainer != null)
				{
					RebuildPipe();
				}
				else
				{
					SceneView.RepaintAll();
					Repaint();
				}
			}
		}

		private int ComputeGeometryStateHash()
		{
			unchecked
			{
				int hash = 17;
				hash = hash * 31 + nodes.Count;
				hash = hash * 31 + mode.GetHashCode();
				hash = hash * 31 + pipeRadius.GetHashCode();
				hash = hash * 31 + radialSegments;
				hash = hash * 31 + straightPathSegments;
				hash = hash * 31 + cornerPathSegments;
				hash = hash * 31 + showJunctionCouplers.GetHashCode();
				hash = hash * 31 + couplerFacesMatchPipe.GetHashCode();
				hash = hash * 31 + couplerRadialSegments;
				hash = hash * 31 + couplerRadiusMultiplier.GetHashCode();
				hash = hash * 31 + couplerLengthMultiplier.GetHashCode();
				hash = hash * 31 + couplerLengthSegments;

				for (int i = 0; i < nodes.Count; i++)
				{
					PipeNode node = nodes[i];
					if (node == null || node.transform == null)
					{
						hash = hash * 31 + i;
						continue;
					}

					Vector3 position = node.transform.position;
					hash = hash * 31 + position.x.GetHashCode();
					hash = hash * 31 + position.y.GetHashCode();
					hash = hash * 31 + position.z.GetHashCode();
					hash = hash * 31 + node.cornerAngleMode.GetHashCode();
					hash = hash * 31 + node.cornerSize.GetHashCode();
				}

				return hash;
			}
		}

		private void DrawNodeHandles()
		{
			for (int i = 0; i < nodes.Count; i++)
			{
				PipeNode node = nodes[i];
				if (node == null || node.transform == null)
				{
					continue;
				}

				Vector3 nodePos = node.transform.position;
				float visualScale = HandleUtility.GetHandleSize(nodePos);
				float axisLength = nodeAxisLength * visualScale;
				float sphereSize = nodeSphereRadius * visualScale;

				Color previousColor = Handles.color;
				Handles.color = Color.red;
				Handles.DrawLine(nodePos - Vector3.right * axisLength, nodePos + Vector3.right * axisLength);
				Handles.color = Color.green;
				Handles.DrawLine(nodePos - Vector3.up * axisLength, nodePos + Vector3.up * axisLength);
				Handles.color = Color.blue;
				Handles.DrawLine(nodePos - Vector3.forward * axisLength, nodePos + Vector3.forward * axisLength);

				Handles.color = mode == ToolMode.Edit ? new Color(0.1f, 0.9f, 1f, 1f) : new Color(1f, 0.85f, 0.1f, 1f);
				if (Handles.Button(nodePos, Quaternion.identity, sphereSize, sphereSize, Handles.SphereHandleCap))
				{
					selectedNode = node;
					Selection.activeObject = node.transform.gameObject;
					Repaint();
				}

				Handles.color = previousColor;
			}
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

		private void CreateNewPipe()
		{
			CreateAndBindNewPipeRoot();
			mode = ToolMode.Create;
			lastGeometryStateHash = ComputeGeometryStateHash();
		}

		private void DuplicatePipeDraft()
		{
			if (nodes.Count == 0)
			{
				return;
			}

			List<PipeNode> sourceNodes = new List<PipeNode>(nodes.Count);
			for (int i = 0; i < nodes.Count; i++)
			{
				PipeNode source = nodes[i];
				if (source == null || source.transform == null)
				{
					continue;
				}

				sourceNodes.Add(new PipeNode
				{
					transform = source.transform,
					cornerAngleMode = source.cornerAngleMode,
					cornerSize = source.cornerSize,
					segmentMaterialOverride = source.segmentMaterialOverride,
					cornerMaterialOverride = source.cornerMaterialOverride
				});
			}

			if (sourceNodes.Count == 0)
			{
				return;
			}

			GameObject duplicatedRoot = new GameObject(GetUniquePipeDraftName());
			Undo.RegisterCreatedObjectUndo(duplicatedRoot, "Duplicate Pipe Draft");

			GameObject duplicatedNodesContainer = new GameObject(NodesContainerName);
			duplicatedNodesContainer.transform.SetParent(duplicatedRoot.transform);

			GameObject duplicatedSegmentsContainer = new GameObject(SegmentsContainerName);
			duplicatedSegmentsContainer.transform.SetParent(duplicatedRoot.transform);

			rootObject = duplicatedRoot;
			nodesContainer = duplicatedNodesContainer.transform;
			segmentsContainer = duplicatedSegmentsContainer.transform;
			nodes.Clear();
			selectedNode = null;

			for (int i = 0; i < sourceNodes.Count; i++)
			{
				PipeNode source = sourceNodes[i];
				GameObject nodeObject = new GameObject($"Node_{i:00}");
				nodeObject.transform.SetParent(nodesContainer);
				nodeObject.transform.position = source.transform.position;

				nodes.Add(new PipeNode
				{
					transform = nodeObject.transform,
					cornerAngleMode = source.cornerAngleMode,
					cornerSize = source.cornerSize,
					segmentMaterialOverride = source.segmentMaterialOverride,
					cornerMaterialOverride = source.cornerMaterialOverride
				});
			}

			Selection.activeObject = rootObject;
			RebuildPipe();
		}

		private static string GetUniquePipeDraftName()
		{
			if (GameObject.Find(RootName) == null)
			{
				return RootName;
			}

			int suffix = 1;
			while (GameObject.Find($"{RootName}_{suffix}") != null)
			{
				suffix++;
			}

			return $"{RootName}_{suffix}";
		}

		private void EnsureRoot()
		{
			if (rootObject == null)
			{
				CreateAndBindNewPipeRoot();
				return;
			}

			if (nodesContainer == null)
			{
				nodesContainer = rootObject.transform.Find(NodesContainerName);
				if (nodesContainer == null)
				{
					GameObject container = new GameObject(NodesContainerName);
					Undo.RegisterCreatedObjectUndo(container, "Create Pipe Nodes Container");
					container.transform.SetParent(rootObject.transform);
					nodesContainer = container.transform;
				}
			}

			if (segmentsContainer == null)
			{
				segmentsContainer = rootObject.transform.Find(SegmentsContainerName);
				if (segmentsContainer == null)
				{
					GameObject container = new GameObject(SegmentsContainerName);
					Undo.RegisterCreatedObjectUndo(container, "Create Pipe Segments Container");
					container.transform.SetParent(rootObject.transform);
					segmentsContainer = container.transform;
				}
			}
		}

		private void TryBindExistingRoot()
		{
			if (TryBindRootFromSelection(Selection.activeTransform))
			{
				return;
			}

			GameObject discoveredRoot = FindAnyPipeRootInScene();
			if (discoveredRoot == null)
			{
				return;
			}

			BindToRoot(discoveredRoot);
		}

		private bool TryBindRootFromSelection(Transform selectedTransform)
		{
			Transform current = selectedTransform;
			while (current != null)
			{
				if (IsPipeRootTransform(current))
				{
					return BindToRoot(current.gameObject);
				}

				current = current.parent;
			}

			return false;
		}

		private static bool IsPipeRootTransform(Transform candidate)
		{
			if (candidate == null)
			{
				return false;
			}

			return candidate.Find(NodesContainerName) != null && candidate.Find(SegmentsContainerName) != null;
		}

		private GameObject FindAnyPipeRootInScene()
		{
			GameObject exactRoot = GameObject.Find(RootName);
			if (exactRoot != null && IsPipeRootTransform(exactRoot.transform))
			{
				return exactRoot;
			}

			Transform[] allTransforms = Object.FindObjectsOfType<Transform>();
			for (int i = 0; i < allTransforms.Length; i++)
			{
				Transform candidate = allTransforms[i];
				if (candidate == null || candidate.parent != null)
				{
					continue;
				}

				if (!candidate.name.StartsWith(RootName))
				{
					continue;
				}

				if (IsPipeRootTransform(candidate))
				{
					return candidate.gameObject;
				}
			}

			return null;
		}

		private bool BindToRoot(GameObject candidateRoot)
		{
			if (candidateRoot == null)
			{
				return false;
			}

			Transform candidateNodesContainer = candidateRoot.transform.Find(NodesContainerName);
			Transform candidateSegmentsContainer = candidateRoot.transform.Find(SegmentsContainerName);
			if (candidateNodesContainer == null || candidateSegmentsContainer == null)
			{
				return false;
			}

			rootObject = candidateRoot;
			nodesContainer = candidateNodesContainer;
			segmentsContainer = candidateSegmentsContainer;
			nodes.Clear();

			for (int i = 0; i < nodesContainer.childCount; i++)
			{
				Transform child = nodesContainer.GetChild(i);
				nodes.Add(new PipeNode { transform = child });
			}

			selectedNode = null;
			lastGeometryStateHash = ComputeGeometryStateHash();

			return true;
		}

		private void UnbindActiveRoot()
		{
			rootObject = null;
			nodesContainer = null;
			segmentsContainer = null;
			nodes.Clear();
			selectedNode = null;
			lastGeometryStateHash = ComputeGeometryStateHash();
		}

		private void CreateAndBindNewPipeRoot()
		{
			GameObject newRoot = new GameObject(GetUniquePipeDraftName());
			Undo.RegisterCreatedObjectUndo(newRoot, "Create Pipe Root");

			rootObject = newRoot;
			nodesContainer = null;
			segmentsContainer = null;
			nodes.Clear();
			selectedNode = null;

			EnsureRoot();
			Selection.activeObject = rootObject;
			lastGeometryStateHash = ComputeGeometryStateHash();
		}

		private void AddNode(Vector3 position)
		{
			EnsureRoot();
			GameObject nodeObject = new GameObject();
			nodeObject.name = $"Node_{nodes.Count:00}";
			nodeObject.transform.SetParent(nodesContainer);
			nodeObject.transform.position = position;

			Undo.RegisterCreatedObjectUndo(nodeObject, "Create Pipe Node");

			PipeNode node = new PipeNode { transform = nodeObject.transform };
			nodes.Add(node);
			selectedNode = node;

			RebuildPipe();
		}

		private void DeleteLastNode()
		{
			if (nodes.Count == 0)
			{
				return;
			}

			PipeNode last = nodes[nodes.Count - 1];
			nodes.RemoveAt(nodes.Count - 1);
			if (last != null && last.transform != null)
			{
				Undo.DestroyObjectImmediate(last.transform.gameObject);
			}

			selectedNode = null;
			RebuildPipe();
		}

		private void ClearPipe()
		{
			nodes.Clear();
			selectedNode = null;

			if (rootObject != null)
			{
				Undo.DestroyObjectImmediate(rootObject);
			}

			rootObject = null;
			nodesContainer = null;
			segmentsContainer = null;
			lastGeometryStateHash = ComputeGeometryStateHash();
		}

		public void RebuildPipe()
		{
			if (segmentsContainer == null)
			{
				lastGeometryStateHash = ComputeGeometryStateHash();
				return;
			}

			for (int i = segmentsContainer.childCount - 1; i >= 0; i--)
			{
				DestroyImmediate(segmentsContainer.GetChild(i).gameObject);
			}

			if (nodes.Count < 2)
			{
				lastGeometryStateHash = ComputeGeometryStateHash();
				SceneView.RepaintAll();
				Repaint();
				return;
			}

			CornerData[] corners = BuildCornerData();
			Vector3 carryNormal = Vector3.zero;
			bool hasCarryNormal = false;

			for (int i = 0; i < nodes.Count - 1; i++)
			{
				if (nodes[i] == null || nodes[i + 1] == null || nodes[i].transform == null || nodes[i + 1].transform == null)
				{
					continue;
				}

				Vector3 start = nodes[i].transform.position;
				Vector3 end = nodes[i + 1].transform.position;

				if (i > 0 && corners[i].hasCorner)
				{
					start = corners[i].exitPoint;
				}

				if (corners[i + 1].hasCorner)
				{
					end = corners[i + 1].entryPoint;
				}

				if (CreateStraightSegment(start, end, i, carryNormal, hasCarryNormal, out Vector3 straightEndNormal))
				{
					carryNormal = straightEndNormal;
					hasCarryNormal = true;
				}

				if (corners[i + 1].hasCorner)
				{
					Material cornerMaterial = nodes[i + 1].cornerMaterialOverride != null ? nodes[i + 1].cornerMaterialOverride : globalPipeMaterial;
					if (CreateCornerSegment(corners[i + 1], i + 1, cornerMaterial, carryNormal, hasCarryNormal, out Vector3 cornerEndNormal))
					{
						carryNormal = cornerEndNormal;
						hasCarryNormal = true;
					}
				}
			}

			if (showJunctionCouplers)
			{
				CreateJunctionCouplers(corners);
			}

			SceneView.RepaintAll();
			Repaint();
			lastGeometryStateHash = ComputeGeometryStateHash();
		}

		private CornerData[] BuildCornerData()
		{
			CornerData[] result = new CornerData[nodes.Count];
			for (int i = 1; i < nodes.Count - 1; i++)
			{
				PipeNode prevNode = nodes[i - 1];
				PipeNode currentNode = nodes[i];
				PipeNode nextNode = nodes[i + 1];
				if (prevNode == null || currentNode == null || nextNode == null || prevNode.transform == null || currentNode.transform == null || nextNode.transform == null)
				{
					continue;
				}

				Vector3 a = prevNode.transform.position;
				Vector3 b = currentNode.transform.position;
				Vector3 c = nextNode.transform.position;

				Vector3 incomingDir = (b - a).normalized;
				Vector3 outgoingDir = (c - b).normalized;
				if (incomingDir.sqrMagnitude < 0.0001f || outgoingDir.sqrMagnitude < 0.0001f)
				{
					continue;
				}

				float actualAngle = Vector3.Angle(incomingDir, outgoingDir);
				if (actualAngle < 10f || Mathf.Abs(180f - actualAngle) < 5f)
				{
					continue;
				}

				float selectedAngle = GetSelectedCornerAngle(currentNode.cornerAngleMode, actualAngle);
				if (!IsSupportedAngle(actualAngle, selectedAngle))
				{
					continue;
				}

				float incomingDistance = Vector3.Distance(a, b);
				float outgoingDistance = Vector3.Distance(b, c);
				float trimDistance = Mathf.Min(incomingDistance, outgoingDistance) * Mathf.Clamp(currentNode.cornerSize, 0.05f, 0.9f);
				if (trimDistance < 0.001f)
				{
					continue;
				}

				Vector3 entry = b - incomingDir * trimDistance;
				Vector3 exit = b + outgoingDir * trimDistance;

				float thetaRad = selectedAngle * Mathf.Deg2Rad;
				float tanHalf = Mathf.Max(Mathf.Tan(thetaRad * 0.5f), 0.0001f);
				float handleFactor = (4f / 3f) * Mathf.Tan(thetaRad * 0.25f) / tanHalf;
				float handleLength = trimDistance * handleFactor;

				result[i] = new CornerData
				{
					hasCorner = true,
					entryPoint = entry,
					exitPoint = exit,
					controlPoint1 = entry + incomingDir * handleLength,
					controlPoint2 = exit - outgoingDir * handleLength,
					cornerAngle = selectedAngle
				};
			}

			return result;
		}

		private static float GetSelectedCornerAngle(CornerAngleMode mode, float actualAngle)
		{
			if (mode == CornerAngleMode.Degree45)
			{
				return 45f;
			}

			if (mode == CornerAngleMode.Degree90)
			{
				return 90f;
			}

			return Mathf.Abs(actualAngle - 45f) <= Mathf.Abs(actualAngle - 90f) ? 45f : 90f;
		}

		private static bool IsSupportedAngle(float actualAngle, float selectedAngle)
		{
			return Mathf.Abs(actualAngle - selectedAngle) <= 20f;
		}

		private bool CreateStraightSegment(Vector3 start, Vector3 end, int segmentIndex, Vector3 startNormal, bool hasStartNormal, out Vector3 endNormal)
		{
			endNormal = Vector3.zero;
			float distance = Vector3.Distance(start, end);
			if (distance < 0.001f)
			{
				return false;
			}

			Vector3 direction = (end - start).normalized;
			Vector3 p1 = start + direction * (distance / 3f);
			Vector3 p2 = start + direction * (distance * (2f / 3f));
			Mesh tube = BuildTubeMesh(start, p1, p2, end, pipeRadius, radialSegments, straightPathSegments, startNormal, hasStartNormal, out endNormal);
			Material segmentMaterial = nodes[segmentIndex].segmentMaterialOverride != null ? nodes[segmentIndex].segmentMaterialOverride : globalPipeMaterial;
			CreateMeshObject(tube, $"Straight_{segmentIndex:00}", segmentMaterial);
			return true;
		}

		private bool CreateCornerSegment(CornerData cornerData, int segmentIndex, Material materialOverride, Vector3 startNormal, bool hasStartNormal, out Vector3 endNormal)
		{
			endNormal = Vector3.zero;
			Mesh cornerMesh = BuildTubeMesh(cornerData.entryPoint, cornerData.controlPoint1, cornerData.controlPoint2, cornerData.exitPoint, pipeRadius, radialSegments, cornerPathSegments, startNormal, hasStartNormal, out endNormal);
			CreateMeshObject(cornerMesh, $"Corner_{segmentIndex:00}_{cornerData.cornerAngle:0}", materialOverride);
			return cornerMesh != null;
		}

		private void CreateJunctionCouplers(CornerData[] corners)
		{
			int couplerFaces = couplerFacesMatchPipe ? radialSegments : couplerRadialSegments;
			for (int i = 1; i < nodes.Count - 1; i++)
			{
				PipeNode node = nodes[i];
				if (node == null || node.transform == null)
				{
					continue;
				}

				if (!corners[i].hasCorner)
				{
					continue;
				}

				Transform prev = nodes[i - 1].transform;
				Transform next = nodes[i + 1].transform;
				if (prev == null || next == null)
				{
					continue;
				}

				Vector3 inDir = (node.transform.position - prev.position).normalized;
				Vector3 outDir = (next.position - node.transform.position).normalized;
				float couplerHalfLength = Mathf.Max(pipeRadius * couplerLengthMultiplier * 0.5f, 0.001f);
				Vector3 entryAxis = inDir.sqrMagnitude > 0.0001f ? inDir : Vector3.up;
				Vector3 entryCenter = corners[i].entryPoint;
				Vector3 entryStart = entryCenter - entryAxis * couplerHalfLength;
				Vector3 entryEnd = entryCenter + entryAxis * couplerHalfLength;
				Mesh entryCouplerMesh = BuildCappedCylinderMesh(entryStart, entryEnd, pipeRadius * couplerRadiusMultiplier, couplerFaces, couplerLengthSegments);
				Material couplerMaterial = node.cornerMaterialOverride != null ? node.cornerMaterialOverride : globalPipeMaterial;
				CreateMeshObject(entryCouplerMesh, $"Coupler_{i:00}_Start", couplerMaterial);

				Vector3 exitAxis = outDir.sqrMagnitude > 0.0001f ? outDir : Vector3.up;
				Vector3 exitCenter = corners[i].exitPoint;
				Vector3 exitStart = exitCenter - exitAxis * couplerHalfLength;
				Vector3 exitEnd = exitCenter + exitAxis * couplerHalfLength;
				Mesh exitCouplerMesh = BuildCappedCylinderMesh(exitStart, exitEnd, pipeRadius * couplerRadiusMultiplier, couplerFaces, couplerLengthSegments);
				CreateMeshObject(exitCouplerMesh, $"Coupler_{i:00}_End", couplerMaterial);
			}
		}

		private void CreateMeshObject(Mesh mesh, string objectName, Material materialOverride = null)
		{
			if (mesh == null)
			{
				return;
			}

			GameObject segmentObject = new GameObject(objectName);
			segmentObject.transform.SetParent(segmentsContainer);

			MeshFilter meshFilter = segmentObject.AddComponent<MeshFilter>();
			meshFilter.sharedMesh = mesh;

			MeshRenderer meshRenderer = segmentObject.AddComponent<MeshRenderer>();
			meshRenderer.sharedMaterial = materialOverride != null ? materialOverride : GetDefaultMaterial();
		}

		private void ResetAllMaterialsToDefault()
		{
			globalPipeMaterial = null;
			for (int i = 0; i < nodes.Count; i++)
			{
				if (nodes[i] == null)
				{
					continue;
				}

				nodes[i].segmentMaterialOverride = null;
				nodes[i].cornerMaterialOverride = null;
			}

			RebuildPipe();
		}

		private static Material GetDefaultMaterial()
		{
			if (defaultMaterial != null)
			{
				return defaultMaterial;
			}

			Shader shader = Shader.Find("Standard");
			if (shader == null)
			{
				shader = Shader.Find("Universal Render Pipeline/Lit");
			}

			if (shader == null)
			{
				return null;
			}

			defaultMaterial = new Material(shader)
			{
				hideFlags = HideFlags.HideAndDontSave
			};
			return defaultMaterial;
		}

		private static Mesh BuildTubeMesh(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float radius, int radial, int path, Vector3 startNormal, bool hasStartNormal, out Vector3 endNormal)
		{
			Mesh mesh = new Mesh();
			mesh.name = "BezierTube";
			endNormal = Vector3.up;

			int ringCount = path + 1;
			Vector3[] vertices = new Vector3[ringCount * radial];
			Vector3[] normals = new Vector3[ringCount * radial];
			Vector2[] uvs = new Vector2[ringCount * radial];
			int[] triangles = new int[path * radial * 6];
			Vector3 previousNormal = Vector3.zero;
			bool hasPreviousNormal = false;

			for (int i = 0; i < ringCount; i++)
			{
				float t = i / (float)path;
				Vector3 center = EvaluateBezier(p0, p1, p2, p3, t);
				Vector3 tangent = EvaluateBezierTangent(p0, p1, p2, p3, t).normalized;
				if (tangent.sqrMagnitude < 0.0001f)
				{
					tangent = Vector3.forward;
				}

				Vector3 normal;
				if (!hasPreviousNormal)
				{
					normal = hasStartNormal ? Vector3.ProjectOnPlane(startNormal, tangent).normalized : Vector3.zero;
					if (normal.sqrMagnitude < 0.0001f)
					{
						Vector3 up = Vector3.up;
						if (Mathf.Abs(Vector3.Dot(up, tangent)) > 0.95f)
						{
							up = Vector3.right;
						}

						normal = Vector3.Cross(tangent, up).normalized;
					}
				}
				else
				{
					normal = Vector3.ProjectOnPlane(previousNormal, tangent).normalized;
					if (normal.sqrMagnitude < 0.0001f)
					{
						Vector3 fallback = Vector3.Cross(tangent, Vector3.up);
						if (fallback.sqrMagnitude < 0.0001f)
						{
							fallback = Vector3.Cross(tangent, Vector3.right);
						}
						normal = fallback.normalized;
					}
				}

				Vector3 binormal = Vector3.Cross(tangent, normal).normalized;
				previousNormal = normal;
				hasPreviousNormal = true;
				if (i == ringCount - 1)
				{
					endNormal = normal;
				}

				for (int j = 0; j < radial; j++)
				{
					float angle = (j / (float)radial) * Mathf.PI * 2f;
					Vector3 circleOffset = (normal * Mathf.Cos(angle) + binormal * Mathf.Sin(angle)) * radius;
					int index = i * radial + j;

					vertices[index] = center + circleOffset;
					normals[index] = circleOffset.normalized;
					uvs[index] = new Vector2(j / (float)radial, t);
				}
			}

			int tri = 0;
			for (int i = 0; i < path; i++)
			{
				for (int j = 0; j < radial; j++)
				{
					int current = i * radial + j;
					int next = i * radial + (j + 1) % radial;
					int currentUp = (i + 1) * radial + j;
					int nextUp = (i + 1) * radial + (j + 1) % radial;

					triangles[tri++] = current;
					triangles[tri++] = next;
					triangles[tri++] = currentUp;

					triangles[tri++] = next;
					triangles[tri++] = nextUp;
					triangles[tri++] = currentUp;
				}
			}

			mesh.vertices = vertices;
			mesh.normals = normals;
			mesh.uv = uvs;
			mesh.triangles = triangles;
			mesh.RecalculateBounds();
			return mesh;
		}

		private static Mesh BuildCappedCylinderMesh(Vector3 start, Vector3 end, float radius, int radialSegments, int lengthSegments)
		{
			Mesh mesh = new Mesh();
			mesh.name = "CouplerCylinder";
			radialSegments = Mathf.Max(3, radialSegments);
			lengthSegments = Mathf.Max(1, lengthSegments);

			Vector3 axis = (end - start).normalized;
			if (axis.sqrMagnitude < 0.0001f)
			{
				return mesh;
			}

			Vector3 reference = Mathf.Abs(Vector3.Dot(axis, Vector3.up)) > 0.95f ? Vector3.right : Vector3.up;
			Vector3 right = Vector3.Cross(axis, reference).normalized;
			Vector3 forward = Vector3.Cross(right, axis).normalized;

			int ringCount = lengthSegments + 1;
			int sideVertexCount = radialSegments * ringCount;
			int capVertexCount = (radialSegments * 2) + 2;
			Vector3[] vertices = new Vector3[sideVertexCount + capVertexCount];
			Vector3[] normals = new Vector3[sideVertexCount + capVertexCount];
			Vector2[] uvs = new Vector2[sideVertexCount + capVertexCount];
			int[] triangles = new int[(lengthSegments * radialSegments * 6) + (radialSegments * 6)];

			for (int ring = 0; ring < ringCount; ring++)
			{
				float t = ring / (float)lengthSegments;
				Vector3 ringCenter = Vector3.Lerp(start, end, t);
				for (int i = 0; i < radialSegments; i++)
				{
					float angle = (i / (float)radialSegments) * Mathf.PI * 2f;
					Vector3 radialDir = (right * Mathf.Cos(angle) + forward * Mathf.Sin(angle)).normalized;
					int sideIndex = ring * radialSegments + i;
					vertices[sideIndex] = ringCenter + radialDir * radius;
					normals[sideIndex] = radialDir;
					uvs[sideIndex] = new Vector2(i / (float)radialSegments, t);
				}
			}

			int tri = 0;
			for (int ring = 0; ring < lengthSegments; ring++)
			{
				int currentRingBase = ring * radialSegments;
				int nextRingBase = (ring + 1) * radialSegments;
				for (int i = 0; i < radialSegments; i++)
				{
					int next = (i + 1) % radialSegments;
					int currentStart = currentRingBase + i;
					int currentEnd = nextRingBase + i;
					int nextStart = currentRingBase + next;
					int nextEnd = nextRingBase + next;

					triangles[tri++] = currentStart;
					triangles[tri++] = currentEnd;
					triangles[tri++] = nextStart;

					triangles[tri++] = nextStart;
					triangles[tri++] = currentEnd;
					triangles[tri++] = nextEnd;
				}
			}

			int startCapRingBase = sideVertexCount;
			int startCapCenter = startCapRingBase + radialSegments;
			int endCapRingBase = startCapCenter + 1;
			int endCapCenter = endCapRingBase + radialSegments;

			for (int i = 0; i < radialSegments; i++)
			{
				vertices[startCapRingBase + i] = vertices[i];
				normals[startCapRingBase + i] = -axis;
				uvs[startCapRingBase + i] = new Vector2(0.5f, 0.5f);

				int endRingIndex = (ringCount - 1) * radialSegments + i;
				vertices[endCapRingBase + i] = vertices[endRingIndex];
				normals[endCapRingBase + i] = axis;
				uvs[endCapRingBase + i] = new Vector2(0.5f, 0.5f);
			}

			vertices[startCapCenter] = start;
			normals[startCapCenter] = -axis;
			uvs[startCapCenter] = new Vector2(0.5f, 0.5f);

			vertices[endCapCenter] = end;
			normals[endCapCenter] = axis;
			uvs[endCapCenter] = new Vector2(0.5f, 0.5f);

			for (int i = 0; i < radialSegments; i++)
			{
				int next = (i + 1) % radialSegments;

				triangles[tri++] = startCapCenter;
				triangles[tri++] = startCapRingBase + i;
				triangles[tri++] = startCapRingBase + next;

				triangles[tri++] = endCapCenter;
				triangles[tri++] = endCapRingBase + next;
				triangles[tri++] = endCapRingBase + i;
			}

			mesh.vertices = vertices;
			mesh.normals = normals;
			mesh.uv = uvs;
			mesh.triangles = triangles;
			mesh.RecalculateBounds();
			return mesh;
		}

		private static Vector3 EvaluateBezier(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
		{
			float oneMinusT = 1f - t;
			return (oneMinusT * oneMinusT * oneMinusT * p0)
				+ (3f * oneMinusT * oneMinusT * t * p1)
				+ (3f * oneMinusT * t * t * p2)
				+ (t * t * t * p3);
		}

		private static Vector3 EvaluateBezierTangent(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
		{
			float oneMinusT = 1f - t;
			return (3f * oneMinusT * oneMinusT * (p1 - p0))
				+ (6f * oneMinusT * t * (p2 - p1))
				+ (3f * t * t * (p3 - p2));
		}

	}
}
