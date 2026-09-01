// BridgeGeneratorEditor.cs
//
// Allows a user to select two GameObjects in the scene and generate a bridge mesh connecting them based on selected vertices.
// Behavior:
// - The user selects two GameObjects (Object A and Object B).
// - The user can select vertices on each object (e.g., via a custom selection tool).
// - The script generates a bridge mesh connecting the selected vertices from Object A to Object B.

using UnityEngine;
using UnityEditor;
using System.Collections.Generic;

public class BridgeGeneratorEditor : EditorWindow
{
    private struct EdgeKey
    {
        public int Min;
        public int Max;

        public EdgeKey(int a, int b)
        {
            if (a < b)
            {
                Min = a;
                Max = b;
            }
            else
            {
                Min = b;
                Max = a;
            }
        }
    }

    private struct SelectedVertexData
    {
        public int Index;
        public Vector3 WorldPosition;
        public Vector3 WorldNormal;
    }

    private enum MarkerSide
    {
        A,
        B
    }

    // --- Public Fields for User Input ---
    private GameObject objectA;
    private GameObject objectB;

    private const string MarkerRootName = "Bridge_Vertex_Selectors";
    private const float DefaultMarkerRadius = 0.025f;
    private const float MarkerDrawScale = 0.08f;

    private readonly List<int> selectedVertexIndicesA = new List<int>();
    private readonly List<int> selectedVertexIndicesB = new List<int>();

    private GameObject markerRoot;
    private float markerRadius = DefaultMarkerRadius;
    private bool sceneSelectionEnabled = true;
    private bool autoOrderVertexChains = true;
    private bool closeLoopWhenManifold = true;
    private bool alignPairsByWorldDistance = true;

    [MenuItem("Lilithe/Bridge Generator/Create Bridge Mesh")]
    public static void ShowWindow()
    {
        GetWindow<BridgeGeneratorEditor>("Bridge Generator");
    }

    private void OnEnable()
    {
        SceneView.duringSceneGui += OnSceneGUI;
    }

    private void OnDisable()
    {
        SceneView.duringSceneGui -= OnSceneGUI;
        ClearAllVertexMarkers();
    }

    private void OnGUI()
    {
        GUILayout.Label("Bridge Mesh Generator Settings", EditorStyles.boldLabel);
        EditorGUILayout.Space();

        // --- 1. Object Selection ---
        objectA = (GameObject)EditorGUILayout.ObjectField("Object A (Start)", objectA, typeof(GameObject), true);
        objectB = (GameObject)EditorGUILayout.ObjectField("Object B (End)", objectB, typeof(GameObject), true);

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Use 2 Selected Objects"))
        {
            if (Selection.gameObjects != null && Selection.gameObjects.Length == 2)
            {
                objectA = Selection.gameObjects[0];
                objectB = Selection.gameObjects[1];
            }
            else
            {
                Debug.LogWarning("Please select exactly 2 GameObjects in the hierarchy.");
            }
        }

        if (GUILayout.Button("Refresh Vertex Colliders"))
        {
            RefreshVertexMarkers();
        }
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Clear Vertex Colliders"))
        {
            ClearAllVertexMarkers();
        }

        if (GUILayout.Button("Clear Vertex Picks"))
        {
            selectedVertexIndicesA.Clear();
            selectedVertexIndicesB.Clear();
            SceneView.RepaintAll();
        }
        EditorGUILayout.EndHorizontal();

        markerRadius = EditorGUILayout.Slider("Collider Radius", markerRadius, 0.005f, 0.2f);
        sceneSelectionEnabled = EditorGUILayout.Toggle("Enable Scene Click Selection", sceneSelectionEnabled);
        autoOrderVertexChains = EditorGUILayout.Toggle("Auto Order Vertex Chains", autoOrderVertexChains);
        closeLoopWhenManifold = EditorGUILayout.Toggle("Close Loop When Manifold", closeLoopWhenManifold);
        alignPairsByWorldDistance = EditorGUILayout.Toggle("Align Pairs By World Distance", alignPairsByWorldDistance);

        EditorGUILayout.HelpBox("Click 'Refresh Vertex Colliders' to generate small sphere colliders on every vertex of both meshes. In Scene view, click a marker to add/remove that vertex from Object A or Object B selection.", MessageType.Info);

        EditorGUILayout.Space(10);

        // --- 2. Vertex Selection Data ---
        EditorGUILayout.LabelField("Selected Vertices Data", EditorStyles.boldLabel);
        
        GUILayout.BeginVertical(EditorStyles.helpBox);
        
        GUILayout.Label("Vertices for Object A:", EditorStyles.boldLabel);

        for (int i = 0; i < selectedVertexIndicesA.Count; i++)
        {
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label($"Vertex {i}:", GUILayout.Width(80));
            int index = selectedVertexIndicesA[i];
            EditorGUILayout.LabelField($"Index {index}", GUILayout.Width(100));
            Vector3 worldPos = GetWorldVertexPosition(objectA, index);
            EditorGUILayout.Vector3Field("World Position", worldPos);
            EditorGUILayout.EndHorizontal();
        }
        
        GUILayout.Space(5);
        if (selectedVertexIndicesA.Count == 0)
        {
             EditorGUILayout.HelpBox("Please select or add vertices for Object A.", MessageType.Warning);
        }

        GUILayout.Label("Vertices for Object B:", EditorStyles.boldLabel);
        for (int i = 0; i < selectedVertexIndicesB.Count; i++)
        {
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label($"Vertex {i}:", GUILayout.Width(80));
            int index = selectedVertexIndicesB[i];
            EditorGUILayout.LabelField($"Index {index}", GUILayout.Width(100));
            Vector3 worldPos = GetWorldVertexPosition(objectB, index);
            EditorGUILayout.Vector3Field("World Position", worldPos);
            EditorGUILayout.EndHorizontal();
        }

        if (selectedVertexIndicesB.Count == 0)
        {
             EditorGUILayout.HelpBox("Please select or add vertices for Object B.", MessageType.Warning);
        }

        if (selectedVertexIndicesA.Count != selectedVertexIndicesB.Count)
        {
            EditorGUILayout.HelpBox("Object A and B need the same number of selected vertices for bridge generation.", MessageType.Warning);
        }

        GUILayout.EndVertical();

        EditorGUILayout.Space(20);

        // --- 3. Execution Button ---
        if (objectA != null && objectB != null && selectedVertexIndicesA.Count > 1 && selectedVertexIndicesB.Count > 1 && selectedVertexIndicesA.Count == selectedVertexIndicesB.Count)
        {
            if (GUILayout.Button("Generate Bridge Mesh"))
            {
                GenerateBridgeMesh();
            }
        }
        else
        {
            EditorGUILayout.HelpBox("Please assign both GameObjects and select matching vertex counts (minimum 2 each) before generating.", MessageType.Info);
        }
    }

    private void OnSceneGUI(SceneView sceneView)
    {
        DrawVertexMarkerPreview();

        if (!sceneSelectionEnabled)
        {
            return;
        }

        Event evt = Event.current;
        if (evt == null || evt.type != EventType.MouseDown || evt.button != 0 || evt.alt)
        {
            return;
        }

        Ray ray = HandleUtility.GUIPointToWorldRay(evt.mousePosition);
        if (!Physics.Raycast(ray, out RaycastHit hit, 100000f))
        {
            return;
        }

        BridgeVertexMarker marker = hit.collider != null ? hit.collider.GetComponent<BridgeVertexMarker>() : null;
        if (marker == null)
        {
            return;
        }

        ToggleVertexSelection(marker);
        evt.Use();
    }

    private void RefreshVertexMarkers()
    {
        if (objectA == null || objectB == null)
        {
            Debug.LogWarning("Assign both Object A and Object B before refreshing vertex colliders.");
            return;
        }

        if (!TryGetObjectMeshData(objectA, out Mesh meshA, out Matrix4x4 localToWorldA))
        {
            Debug.LogWarning("Object A has no MeshFilter or SkinnedMeshRenderer with a mesh.");
            return;
        }

        if (!TryGetObjectMeshData(objectB, out Mesh meshB, out Matrix4x4 localToWorldB))
        {
            Debug.LogWarning("Object B has no MeshFilter or SkinnedMeshRenderer with a mesh.");
            return;
        }

        EnsureMarkerRoot();
        ClearMarkersForSide(MarkerSide.A);
        ClearMarkersForSide(MarkerSide.B);
        CreateMarkersForMesh(meshA, localToWorldA, MarkerSide.A);
        CreateMarkersForMesh(meshB, localToWorldB, MarkerSide.B);

        selectedVertexIndicesA.Clear();
        selectedVertexIndicesB.Clear();
        SceneView.RepaintAll();
        Repaint();
    }

    private void EnsureMarkerRoot()
    {
        if (markerRoot != null)
        {
            return;
        }

        markerRoot = GameObject.Find(MarkerRootName);
        if (markerRoot != null)
        {
            return;
        }

        markerRoot = new GameObject(MarkerRootName);
        Undo.RegisterCreatedObjectUndo(markerRoot, "Create Bridge Vertex Marker Root");
    }

    private void CreateMarkersForMesh(Mesh mesh, Matrix4x4 localToWorld, MarkerSide side)
    {
        if (mesh == null)
        {
            return;
        }

        Vector3[] vertices = mesh.vertices;
        for (int i = 0; i < vertices.Length; i++)
        {
            GameObject markerObject = new GameObject($"{side}_Vertex_{i}");
            markerObject.transform.SetParent(markerRoot.transform, false);
            markerObject.transform.position = localToWorld.MultiplyPoint3x4(vertices[i]);
            markerObject.transform.rotation = Quaternion.identity;
            markerObject.transform.localScale = Vector3.one;

            SphereCollider sphereCollider = markerObject.AddComponent<SphereCollider>();
            sphereCollider.radius = markerRadius;
            sphereCollider.isTrigger = true;

            BridgeVertexMarker marker = markerObject.AddComponent<BridgeVertexMarker>();
            marker.Side = side == MarkerSide.A ? BridgeVertexMarker.MarkerSideRuntime.A : BridgeVertexMarker.MarkerSideRuntime.B;
            marker.VertexIndex = i;
        }
    }

    private void ClearMarkersForSide(MarkerSide side)
    {
        if (markerRoot == null)
        {
            return;
        }

        List<GameObject> toRemove = new List<GameObject>();
        for (int i = 0; i < markerRoot.transform.childCount; i++)
        {
            Transform child = markerRoot.transform.GetChild(i);
            if (child == null)
            {
                continue;
            }

            BridgeVertexMarker marker = child.GetComponent<BridgeVertexMarker>();
            if (marker == null)
            {
                continue;
            }

            if ((side == MarkerSide.A && marker.Side == BridgeVertexMarker.MarkerSideRuntime.A)
                || (side == MarkerSide.B && marker.Side == BridgeVertexMarker.MarkerSideRuntime.B))
            {
                toRemove.Add(child.gameObject);
            }
        }

        for (int i = 0; i < toRemove.Count; i++)
        {
            Undo.DestroyObjectImmediate(toRemove[i]);
        }
    }

    private void ClearAllVertexMarkers()
    {
        if (markerRoot == null)
        {
            markerRoot = GameObject.Find(MarkerRootName);
            if (markerRoot == null)
            {
                return;
            }
        }

        Undo.DestroyObjectImmediate(markerRoot);
        markerRoot = null;
    }

    private void ToggleVertexSelection(BridgeVertexMarker marker)
    {
        if (marker.Side == BridgeVertexMarker.MarkerSideRuntime.A)
        {
            ToggleSelectionIndex(selectedVertexIndicesA, marker.VertexIndex);
        }
        else
        {
            ToggleSelectionIndex(selectedVertexIndicesB, marker.VertexIndex);
        }

        SceneView.RepaintAll();
        Repaint();
    }

    private static void ToggleSelectionIndex(List<int> list, int index)
    {
        int existing = list.IndexOf(index);
        if (existing >= 0)
        {
            list.RemoveAt(existing);
            return;
        }

        list.Add(index);
    }

    private void DrawVertexMarkerPreview()
    {
        if (markerRoot == null)
        {
            return;
        }

        for (int i = 0; i < markerRoot.transform.childCount; i++)
        {
            Transform child = markerRoot.transform.GetChild(i);
            if (child == null)
            {
                continue;
            }

            BridgeVertexMarker marker = child.GetComponent<BridgeVertexMarker>();
            if (marker == null)
            {
                continue;
            }

            bool selected = marker.Side == BridgeVertexMarker.MarkerSideRuntime.A
                ? selectedVertexIndicesA.Contains(marker.VertexIndex)
                : selectedVertexIndicesB.Contains(marker.VertexIndex);

            if (selected)
            {
                Handles.color = marker.Side == BridgeVertexMarker.MarkerSideRuntime.A ? Color.yellow : new Color(1f, 0.55f, 0f, 1f);
            }
            else
            {
                Handles.color = marker.Side == BridgeVertexMarker.MarkerSideRuntime.A ? new Color(0.2f, 0.95f, 1f, 0.85f) : new Color(0.95f, 0.35f, 0.35f, 0.85f);
            }

            float size = HandleUtility.GetHandleSize(child.position) * MarkerDrawScale;
            Handles.SphereHandleCap(0, child.position, Quaternion.identity, size, EventType.Repaint);
        }
    }

    private static bool TryGetObjectMeshData(GameObject target, out Mesh mesh, out Matrix4x4 localToWorld)
    {
        mesh = null;
        localToWorld = Matrix4x4.identity;

        if (target == null)
        {
            return false;
        }

        MeshFilter meshFilter = target.GetComponentInChildren<MeshFilter>();
        if (meshFilter != null && meshFilter.sharedMesh != null)
        {
            mesh = meshFilter.sharedMesh;
            localToWorld = meshFilter.transform.localToWorldMatrix;
            return true;
        }

        SkinnedMeshRenderer skinned = target.GetComponentInChildren<SkinnedMeshRenderer>();
        if (skinned != null && skinned.sharedMesh != null)
        {
            mesh = skinned.sharedMesh;
            localToWorld = skinned.transform.localToWorldMatrix;
            return true;
        }

        return false;
    }

    private static Vector3 GetWorldVertexPosition(GameObject target, int vertexIndex)
    {
        if (!TryGetObjectMeshData(target, out Mesh mesh, out Matrix4x4 localToWorld))
        {
            return Vector3.zero;
        }

        if (vertexIndex < 0 || vertexIndex >= mesh.vertexCount)
        {
            return Vector3.zero;
        }

        return localToWorld.MultiplyPoint3x4(mesh.vertices[vertexIndex]);
    }

    private static List<SelectedVertexData> GetSelectedVertexData(GameObject target, List<int> selectedVertexIndices, out bool isManifoldLoop)
    {
        List<SelectedVertexData> result = new List<SelectedVertexData>();
        isManifoldLoop = false;

        if (!TryGetObjectMeshData(target, out Mesh mesh, out Matrix4x4 localToWorld))
        {
            return result;
        }

        Vector3[] vertices = mesh.vertices;
        Vector3[] normals = mesh.normals;
        for (int i = 0; i < selectedVertexIndices.Count; i++)
        {
            int index = selectedVertexIndices[i];
            if (index < 0 || index >= vertices.Length)
            {
                continue;
            }

            Vector3 worldNormal = Vector3.up;
            if (normals != null && normals.Length == vertices.Length)
            {
                worldNormal = localToWorld.MultiplyVector(normals[index]).normalized;
            }

            result.Add(new SelectedVertexData
            {
                Index = index,
                WorldPosition = localToWorld.MultiplyPoint3x4(vertices[index]),
                WorldNormal = worldNormal
            });
        }

        isManifoldLoop = IsSelectionManifoldLoop(mesh, selectedVertexIndices);
        if (isManifoldLoop)
        {
            OrientNormalsOutwardFromSelectionCenter(result);
        }

        return result;
    }

    /// <summary>
    /// Core function to create the bridge GameObject and its mesh.
    /// </summary>
    private void GenerateBridgeMesh()
    {
        List<SelectedVertexData> verticesA = GetSelectedVertexData(objectA, selectedVertexIndicesA, out bool manifoldA);
        List<SelectedVertexData> verticesB = GetSelectedVertexData(objectB, selectedVertexIndicesB, out bool manifoldB);

        if (objectA == null || objectB == null || verticesA.Count < 2 || verticesB.Count < 2)
        {
            Debug.LogError("Missing required inputs for mesh generation.");
            return;
        }

        if (verticesA.Count != verticesB.Count)
        {
            Debug.LogError("Object A and Object B selected vertices must have the same count.");
            return;
        }

        bool closeLoop = closeLoopWhenManifold && manifoldA && manifoldB && verticesA.Count > 2;

        if (autoOrderVertexChains)
        {
            verticesA = OrderVertexChain(verticesA, manifoldA);
            verticesB = OrderVertexChain(verticesB, manifoldB);

            if (alignPairsByWorldDistance && manifoldA && manifoldB)
            {
                verticesB = AlignClosedLoopPairing(verticesA, verticesB);
            }
            else if (ComputePairingCost(verticesA, verticesB) > ComputePairingCost(verticesA, Reversed(verticesB)))
            {
                verticesB.Reverse();
            }
        }

        Vector3 origin = ComputeCentroid(verticesA, verticesB);

        // 1. Create the Bridge GameObject
        GameObject bridgeGO = new GameObject("Bridge_Mesh");
        bridgeGO.transform.position = origin;
        Undo.RegisterCreatedObjectUndo(bridgeGO, "Create Bridge Mesh");
        
        MeshFilter meshFilter = bridgeGO.AddComponent<MeshFilter>();
        MeshRenderer meshRenderer = bridgeGO.AddComponent<MeshRenderer>();
        
        Mesh mesh = new Mesh();
        meshFilter.mesh = mesh;

        // 2. Generate Bridge Vertices
        List<Vector3> bridgeVertices = new List<Vector3>();
        List<int> bridgeTriangles = new List<int>();
        List<Vector2> bridgeUVs = new List<Vector2>();
        List<Vector3> bridgeNormals = new List<Vector3>();

        // Build a continuous quad strip from ordered A/B vertex pairs.
        for (int i = 0; i < verticesA.Count; i++)
        {
            Vector3 localA = verticesA[i].WorldPosition - origin;
            Vector3 localB = verticesB[i].WorldPosition - origin;
            bridgeVertices.Add(localA);
            bridgeVertices.Add(localB);

            float v = i / Mathf.Max(1f, verticesA.Count - 1f);
            bridgeUVs.Add(new Vector2(0f, v));
            bridgeUVs.Add(new Vector2(1f, v));

            bridgeNormals.Add(verticesA[i].WorldNormal.normalized);
            bridgeNormals.Add(verticesB[i].WorldNormal.normalized);
        }

        int segmentCount = closeLoop ? verticesA.Count : verticesA.Count - 1;
        bool flipWinding = ShouldFlipBridgeWinding(bridgeVertices, bridgeNormals, segmentCount);
        for (int i = 0; i < segmentCount; i++)
        {
            int a0 = (i * 2) + 0;
            int b0 = (i * 2) + 1;
            int next = (i + 1) % verticesA.Count;
            int a1 = (next * 2) + 0;
            int b1 = (next * 2) + 1;

            if (!flipWinding)
            {
                bridgeTriangles.Add(a0);
                bridgeTriangles.Add(a1);
                bridgeTriangles.Add(b0);

                bridgeTriangles.Add(b0);
                bridgeTriangles.Add(a1);
                bridgeTriangles.Add(b1);
            }
            else
            {
                bridgeTriangles.Add(a0);
                bridgeTriangles.Add(b0);
                bridgeTriangles.Add(a1);

                bridgeTriangles.Add(b0);
                bridgeTriangles.Add(b1);
                bridgeTriangles.Add(a1);
            }
        }

        // 3. Apply Mesh Data
        mesh.vertices = bridgeVertices.ToArray();
        mesh.triangles = bridgeTriangles.ToArray();
        mesh.uv = bridgeUVs.ToArray();
        mesh.normals = bridgeNormals.ToArray();
        
        // Calculate normals for proper lighting
        mesh.RecalculateBounds();

        // Optional: Add basic materials
        meshRenderer.sharedMaterial = new Material(Shader.Find("Standard"));
        
        Debug.Log($"Successfully created bridge mesh with {bridgeVertices.Count} vertices and {bridgeTriangles.Count / 3} triangles.");
    }

    private static bool ShouldFlipBridgeWinding(List<Vector3> bridgeVertices, List<Vector3> bridgeNormals, int segmentCount)
    {
        if (bridgeVertices == null || bridgeNormals == null || segmentCount <= 0 || bridgeVertices.Count < 4 || bridgeNormals.Count < 4)
        {
            return false;
        }

        int a0 = 0;
        int b0 = 1;
        int a1 = 2;
        Vector3 avg = (bridgeNormals[a0] + bridgeNormals[b0] + bridgeNormals[a1]) / 3f;
        if (avg.sqrMagnitude < 0.000001f)
        {
            return false;
        }

        Vector3 triNormal = Vector3.Cross(bridgeVertices[a1] - bridgeVertices[a0], bridgeVertices[b0] - bridgeVertices[a0]);
        if (triNormal.sqrMagnitude < 0.000001f)
        {
            return false;
        }

        return Vector3.Dot(triNormal.normalized, avg.normalized) < 0f;
    }

    private static List<SelectedVertexData> OrderVertexChain(List<SelectedVertexData> source, bool preferClosedLoopOrdering)
    {
        List<SelectedVertexData> ordered = new List<SelectedVertexData>(source);
        if (ordered.Count < 3)
        {
            return ordered;
        }

        if (preferClosedLoopOrdering)
        {
            Vector3 center = ComputeCentroid(ordered);
            Vector3 axis = ComputeAverageNormal(ordered);
            if (axis.sqrMagnitude < 0.000001f)
            {
                axis = Vector3.up;
            }

            Vector3 basisX = (ordered[0].WorldPosition - center).normalized;
            if (basisX.sqrMagnitude < 0.000001f)
            {
                basisX = Vector3.right;
            }

            Vector3 basisY = Vector3.Cross(axis.normalized, basisX).normalized;
            if (basisY.sqrMagnitude < 0.000001f)
            {
                basisY = Vector3.forward;
            }

            ordered.Sort((left, right) =>
            {
                Vector3 l = left.WorldPosition - center;
                Vector3 r = right.WorldPosition - center;
                float lAngle = Mathf.Atan2(Vector3.Dot(l, basisY), Vector3.Dot(l, basisX));
                float rAngle = Mathf.Atan2(Vector3.Dot(r, basisY), Vector3.Dot(r, basisX));
                return lAngle.CompareTo(rAngle);
            });
            return ordered;
        }

        Vector3 direction = FindChainDirection(ordered);
        ordered.Sort((left, right) => Vector3.Dot(left.WorldPosition, direction).CompareTo(Vector3.Dot(right.WorldPosition, direction)));
        return ordered;
    }

    private static Vector3 FindChainDirection(List<SelectedVertexData> points)
    {
        if (points == null || points.Count < 2)
        {
            return Vector3.right;
        }

        int first = 0;
        int second = FindFarthestPointIndex(points, first);
        int third = FindFarthestPointIndex(points, second);
        Vector3 direction = points[third].WorldPosition - points[second].WorldPosition;
        if (direction.sqrMagnitude < 0.000001f)
        {
            return Vector3.right;
        }

        return direction.normalized;
    }

    private static int FindFarthestPointIndex(List<SelectedVertexData> points, int fromIndex)
    {
        float bestDistance = float.MinValue;
        int bestIndex = fromIndex;
        Vector3 origin = points[fromIndex].WorldPosition;
        for (int i = 0; i < points.Count; i++)
        {
            float sqr = (points[i].WorldPosition - origin).sqrMagnitude;
            if (sqr > bestDistance)
            {
                bestDistance = sqr;
                bestIndex = i;
            }
        }

        return bestIndex;
    }

    private static float ComputePairingCost(List<SelectedVertexData> a, List<SelectedVertexData> b)
    {
        int count = Mathf.Min(a.Count, b.Count);
        float cost = 0f;
        for (int i = 0; i < count; i++)
        {
            cost += (a[i].WorldPosition - b[i].WorldPosition).sqrMagnitude;
        }

        return cost;
    }

    private static List<SelectedVertexData> Reversed(List<SelectedVertexData> source)
    {
        List<SelectedVertexData> reversed = new List<SelectedVertexData>(source);
        reversed.Reverse();
        return reversed;
    }

    private static Vector3 ComputeCentroid(List<SelectedVertexData> a, List<SelectedVertexData> b)
    {
        Vector3 sum = Vector3.zero;
        int count = 0;
        for (int i = 0; i < a.Count; i++)
        {
            sum += a[i].WorldPosition;
            count++;
        }

        for (int i = 0; i < b.Count; i++)
        {
            sum += b[i].WorldPosition;
            count++;
        }

        return count > 0 ? sum / count : Vector3.zero;
    }

    private static Vector3 ComputeCentroid(List<SelectedVertexData> vertices)
    {
        Vector3 sum = Vector3.zero;
        for (int i = 0; i < vertices.Count; i++)
        {
            sum += vertices[i].WorldPosition;
        }

        return vertices.Count > 0 ? sum / vertices.Count : Vector3.zero;
    }

    private static Vector3 ComputeAverageNormal(List<SelectedVertexData> vertices)
    {
        Vector3 sum = Vector3.zero;
        for (int i = 0; i < vertices.Count; i++)
        {
            sum += vertices[i].WorldNormal;
        }

        return sum.normalized;
    }

    private static List<SelectedVertexData> AlignClosedLoopPairing(List<SelectedVertexData> a, List<SelectedVertexData> b)
    {
        if (a == null || b == null || a.Count != b.Count || a.Count < 3)
        {
            return b;
        }

        int count = a.Count;
        float bestCost = float.MaxValue;
        List<SelectedVertexData> best = new List<SelectedVertexData>(b);

        for (int direction = 0; direction < 2; direction++)
        {
            for (int offset = 0; offset < count; offset++)
            {
                float cost = 0f;
                for (int i = 0; i < count; i++)
                {
                    int bIndex = direction == 0
                        ? (i + offset) % count
                        : ((count - 1 - i + offset) % count + count) % count;
                    cost += (a[i].WorldPosition - b[bIndex].WorldPosition).sqrMagnitude;
                }

                if (cost < bestCost)
                {
                    bestCost = cost;
                    best.Clear();
                    for (int i = 0; i < count; i++)
                    {
                        int bIndex = direction == 0
                            ? (i + offset) % count
                            : ((count - 1 - i + offset) % count + count) % count;
                        best.Add(b[bIndex]);
                    }
                }
            }
        }

        return best;
    }

    private static void OrientNormalsOutwardFromSelectionCenter(List<SelectedVertexData> vertices)
    {
        if (vertices == null || vertices.Count == 0)
        {
            return;
        }

        Vector3 center = ComputeCentroid(vertices);
        for (int i = 0; i < vertices.Count; i++)
        {
            SelectedVertexData data = vertices[i];
            Vector3 outward = (data.WorldPosition - center).normalized;
            if (outward.sqrMagnitude < 0.000001f)
            {
                outward = data.WorldNormal.sqrMagnitude > 0.000001f ? data.WorldNormal.normalized : Vector3.up;
            }

            Vector3 normal = data.WorldNormal.sqrMagnitude > 0.000001f ? data.WorldNormal.normalized : outward;
            if (Vector3.Dot(normal, outward) < 0f)
            {
                normal = -normal;
            }

            data.WorldNormal = normal.normalized;
            vertices[i] = data;
        }
    }

    private static bool IsSelectionManifoldLoop(Mesh mesh, List<int> selectedIndices)
    {
        if (mesh == null || selectedIndices == null || selectedIndices.Count < 3)
        {
            return false;
        }

        int[] triangles = mesh.triangles;
        if (triangles == null || triangles.Length < 3)
        {
            return false;
        }

        HashSet<int> selectedSet = new HashSet<int>(selectedIndices);
        Dictionary<EdgeKey, int> selectedTriangleEdgeCounts = new Dictionary<EdgeKey, int>();
        for (int i = 0; i <= triangles.Length - 3; i += 3)
        {
            int a = triangles[i];
            int b = triangles[i + 1];
            int c = triangles[i + 2];
            if (!selectedSet.Contains(a) || !selectedSet.Contains(b) || !selectedSet.Contains(c))
            {
                continue;
            }

            AddEdgeCount(selectedTriangleEdgeCounts, new EdgeKey(a, b));
            AddEdgeCount(selectedTriangleEdgeCounts, new EdgeKey(b, c));
            AddEdgeCount(selectedTriangleEdgeCounts, new EdgeKey(c, a));
        }

        if (selectedTriangleEdgeCounts.Count == 0)
        {
            return false;
        }

        Dictionary<int, HashSet<int>> boundaryAdjacency = new Dictionary<int, HashSet<int>>();
        foreach (KeyValuePair<EdgeKey, int> edgeCount in selectedTriangleEdgeCounts)
        {
            if (edgeCount.Value != 1)
            {
                continue;
            }

            AddBoundaryNeighbor(boundaryAdjacency, edgeCount.Key.Min, edgeCount.Key.Max);
            AddBoundaryNeighbor(boundaryAdjacency, edgeCount.Key.Max, edgeCount.Key.Min);
        }

        if (boundaryAdjacency.Count != selectedSet.Count)
        {
            return false;
        }

        foreach (int selectedIndex in selectedSet)
        {
            if (!boundaryAdjacency.TryGetValue(selectedIndex, out HashSet<int> neighbors) || neighbors.Count != 2)
            {
                return false;
            }
        }

        int start = selectedIndices[0];
        HashSet<int> visited = new HashSet<int>();
        Queue<int> queue = new Queue<int>();
        visited.Add(start);
        queue.Enqueue(start);
        while (queue.Count > 0)
        {
            int current = queue.Dequeue();
            if (!boundaryAdjacency.TryGetValue(current, out HashSet<int> neighbors))
            {
                continue;
            }

            foreach (int neighbor in neighbors)
            {
                if (visited.Add(neighbor))
                {
                    queue.Enqueue(neighbor);
                }
            }
        }

        return visited.Count == selectedSet.Count;
    }

    private static void AddEdgeCount(Dictionary<EdgeKey, int> edgeCounts, EdgeKey key)
    {
        if (key.Min == key.Max)
        {
            return;
        }

        if (edgeCounts.TryGetValue(key, out int count))
        {
            edgeCounts[key] = count + 1;
        }
        else
        {
            edgeCounts.Add(key, 1);
        }
    }

    private static void AddBoundaryNeighbor(Dictionary<int, HashSet<int>> adjacency, int vertex, int neighbor)
    {
        if (!adjacency.TryGetValue(vertex, out HashSet<int> neighbors))
        {
            neighbors = new HashSet<int>();
            adjacency[vertex] = neighbors;
        }

        neighbors.Add(neighbor);
    }
}

public class BridgeVertexMarker : MonoBehaviour
{
    public enum MarkerSideRuntime
    {
        A,
        B
    }

    public MarkerSideRuntime Side;
    public int VertexIndex;
}