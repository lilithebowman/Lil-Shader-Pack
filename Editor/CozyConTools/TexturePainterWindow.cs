using System.IO;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Lilithe.Tools
{
    public class TexturePainterWindow : EditorWindow
    {
        private enum PaintMode
        {
            None,
            Texture
        }

        private const string DefaultGeneratedFolder = "Assets/Generated/LilitheTexturePainter";
        private const int DefaultTextureSize = 1024;
        private const int PaletteCapacity = 10;
        private const int TextureHistoryCapacity = 10;
        private const double QuickSaveIntervalSeconds = 5.0d;
        private const string QuickSaveSuffix = "_quickcopy";

        private GameObject targetObject;
        private Renderer targetRenderer;
        private Material targetMaterial;
        private Texture2D activeTexture;
        private PaintMode paintMode = PaintMode.None;
        private UvUnwrapMode unwrapMode = UvUnwrapMode.Standard;
        private Color brushColor = Color.red;
        private float brushOpacity = 0.5f;
        private float brushRadius = 0.05f;
        private int uvExportResolution = 2048;
        private int aoBakeResolution = 1024;
        private int aoSampleCount = 16;
        private float aoRayDistance = 0.5f;
        private bool isPainting;
        private bool isPreviewPainting;
        private bool hasLastPaintUv;
        private Vector2 lastPaintUv;
        private bool hasLastPaintTriangleIndex;
        private int lastPaintTriangleIndex = -1;
        private Vector2 scrollPosition;
        private int cachedFaceTriangleIndex = -1;
        private float cachedMetricM00;
        private float cachedMetricM01;
        private float cachedMetricM11;
        private bool hasCachedFaceMetric;
        private Mesh cachedUvIslandMesh;
        private int[] cachedUvIslandIds;
        private readonly List<Color> recentPaletteColors = new List<Color>(PaletteCapacity);
        private readonly List<Color32[]> textureUndoHistory = new List<Color32[]>();
        private readonly List<Color32[]> textureRedoHistory = new List<Color32[]>();
        private Texture2D historyTexture;
        private int historyTextureWidth;
        private int historyTextureHeight;
        private Texture2D quickSaveTexture;
        private bool hasPendingQuickSave;
        private double nextQuickSaveTime;

        [MenuItem("Lilithe/Texture Painter")]
        public static void ShowWindow()
        {
            var window = GetWindow<TexturePainterWindow>("Texture Painter");
            window.minSize = new Vector2(360f, 500f);
        }

        private void OnEnable()
        {
            if (Selection.activeGameObject != null)
            {
                targetObject = Selection.activeGameObject;
                SyncTargetRenderer();
            }

            SceneView.duringSceneGui += OnSceneGUI;
            EditorApplication.update += OnEditorUpdate;
        }

        private void OnDisable()
        {
            SceneView.duringSceneGui -= OnSceneGUI;
            EditorApplication.update -= OnEditorUpdate;
            hasPendingQuickSave = false;
        }

        private void OnEditorUpdate()
        {
            TryWriteQuickSaveCopyIfDue();
        }

        private void OnSelectionChange()
        {
            if (Selection.activeGameObject != null)
            {
                targetObject = Selection.activeGameObject;
                SyncTargetRenderer();
            }

            Repaint();
        }

        private void OnGUI()
        {
            if (paintMode != PaintMode.None)
            {
                UnityEditor.Tools.current = Tool.None;
                UnityEditor.Tools.hidden = true;
            }
            else
            {
                UnityEditor.Tools.hidden = false;
            }

            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition, GUILayout.ExpandHeight(true));
            try
            {
                DrawContent();
            }
            finally
            {
                EditorGUILayout.EndScrollView();
            }
        }

        private void DrawContent()
        {
            EditorGUILayout.LabelField("Texture Painter", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Paint a diffuse map directly onto the selected mesh using its UVs.", MessageType.Info);

            targetObject = (GameObject)EditorGUILayout.ObjectField("Target Object", targetObject, typeof(GameObject), true);
            if (targetObject != null)
            {
                SyncTargetRenderer();
            }

            EditorGUILayout.Space();
            if (GUILayout.Button("Create New Material"))
            {
                CreateNewMaterial();
                return;
            }

            if (GUILayout.Button("Create New Diffuse Texture"))
            {
                CreateNewDiffuseTexture();
                return;
            }

            EditorGUILayout.Space();
            unwrapMode = (UvUnwrapMode)EditorGUILayout.EnumPopup("UV Mapping Mode", unwrapMode);
            switch (unwrapMode)
            {
                case UvUnwrapMode.Standard:
                    EditorGUILayout.HelpBox("Use standard unwrap for general meshes with existing UV seams or simple texture projection.", MessageType.None);
                    break;
                case UvUnwrapMode.CubeProjection:
                    EditorGUILayout.HelpBox("Use cube projection for box-like objects or when you want a projection that wraps from the object volume inward.", MessageType.None);
                    break;
                case UvUnwrapMode.MinimizeStretch:
                    EditorGUILayout.HelpBox("Use minimize stretch when you want to reduce UV distortion and keep the map more even across curved surfaces.", MessageType.None);
                    break;
            }

            if (GUILayout.Button("UV Unwrap Target Mesh"))
            {
                UnwrapTargetMesh();
                return;
            }

            uvExportResolution = EditorGUILayout.IntField("UV Export Resolution", uvExportResolution);
            uvExportResolution = Mathf.Clamp(uvExportResolution, 256, 8192);

            if (GUILayout.Button("Export UV Layout PNG"))
            {
                ExportUvLayoutPng();
            }

            aoBakeResolution = EditorGUILayout.IntField("AO Bake Resolution", aoBakeResolution);
            aoBakeResolution = Mathf.Clamp(aoBakeResolution, 64, 8192);
            aoSampleCount = EditorGUILayout.IntSlider("AO Samples", aoSampleCount, 1, 64);
            aoRayDistance = EditorGUILayout.Slider("AO Ray Distance", aoRayDistance, 0.01f, 10f);

            if (GUILayout.Button("Bake Ambient Occlusion Map"))
            {
                BakeAmbientOcclusionMap();
            }

            EditorGUILayout.Space();
            paintMode = (PaintMode)EditorGUILayout.EnumPopup("Paint Mode", paintMode);

            if (targetObject == null || targetRenderer == null)
            {
                EditorGUILayout.HelpBox("Select a mesh GameObject with a Renderer to begin painting.", MessageType.Warning);
                return;
            }

            if (targetMaterial == null)
            {
                targetMaterial = targetRenderer.sharedMaterial;
            }

            if (paintMode == PaintMode.None)
            {
                EditorGUILayout.HelpBox("No paint mode selected. Choose Texture to begin painting.", MessageType.None);
                return;
            }

            if (activeTexture == null)
            {
                activeTexture = GetCurrentTextureForMode();
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Active Map", EditorStyles.boldLabel);

            if (targetMaterial != null)
            {
                Texture2D currentDiffuse = targetMaterial.GetTexture("_MainTex") as Texture2D;
                Texture2D assignedDiffuse = (Texture2D)EditorGUILayout.ObjectField("Diffuse Texture", currentDiffuse, typeof(Texture2D), false);

                if (assignedDiffuse != currentDiffuse)
                {
                    targetMaterial.SetTexture("_MainTex", assignedDiffuse);
                }

                activeTexture = assignedDiffuse;
            }
            else
            {
                activeTexture = null;
            }

            EditorGUILayout.Space();
            Color selectedBrushColor = EditorGUILayout.ColorField("Paint Color", brushColor);
            if (!AreColorsApproximatelyEqual(selectedBrushColor, brushColor))
            {
                AddColorToRecentPalette(brushColor);
                brushColor = selectedBrushColor;
            }

            DrawRecentPalette();
            brushOpacity = EditorGUILayout.Slider("Opacity", brushOpacity, 0f, 1f);

            brushRadius = EditorGUILayout.Slider("Brush Radius (World)", brushRadius, 0.005f, 1f);

            if (paintMode == PaintMode.Texture && activeTexture != null)
            {
                EditorGUILayout.Space();
                EditorGUILayout.LabelField("Texture + UV Preview", EditorStyles.boldLabel);
                Rect previewRect = DrawUvOverlayPreview(activeTexture);
                HandlePreviewPainting(previewRect);
            }

            EditorGUILayout.Space();
            DrawTextureUndoControls();

            EditorGUILayout.Space();
            if (GUILayout.Button("Clear Active Texture"))
            {
                CaptureTextureUndoSnapshot();
                TexturePainterUtility.ClearTexture(activeTexture);
            }

            if (GUILayout.Button("Save Active Diffuse Texture To Disk"))
            {
                SaveActiveDiffuseTextureToDisk();
            }
        }

        private void SaveActiveDiffuseTextureToDisk()
        {
            if (activeTexture == null)
            {
                EditorUtility.DisplayDialog("Texture Painter", "No active diffuse texture is assigned.", "OK");
                return;
            }

            if (TexturePainterUtility.SaveTextureAssetToDisk(activeTexture, out string error))
            {
                Debug.Log("[Texture Painter] Saved diffuse texture to disk: " + AssetDatabase.GetAssetPath(activeTexture));
                return;
            }

            EditorUtility.DisplayDialog("Texture Painter", error ?? "Failed to save active diffuse texture to disk.", "OK");
        }

        private void SyncTargetRenderer()
        {
            if (targetObject == null)
            {
                targetRenderer = null;
                targetMaterial = null;
                activeTexture = null;
                quickSaveTexture = null;
                hasPendingQuickSave = false;
                return;
            }

            targetRenderer = targetObject.GetComponent<Renderer>();
            targetMaterial = targetRenderer != null ? targetRenderer.sharedMaterial : null;
            activeTexture = GetCurrentTextureForMode();
            if (quickSaveTexture != activeTexture)
            {
                quickSaveTexture = activeTexture;
                hasPendingQuickSave = false;
                nextQuickSaveTime = 0d;
            }
        }

        private Texture2D GetCurrentTextureForMode()
        {
            if (targetMaterial == null)
            {
                return null;
            }

            return targetMaterial.GetTexture("_MainTex") as Texture2D;
        }

        private void CreateNewMaterial()
        {
            if (targetObject == null)
            {
                EditorUtility.DisplayDialog("Texture Painter", "Select a mesh GameObject before creating a new material.", "OK");
                return;
            }

            var renderer = targetObject.GetComponent<Renderer>();
            if (renderer == null)
            {
                EditorUtility.DisplayDialog("Texture Painter", "The selected GameObject must have a Renderer component.", "OK");
                return;
            }

            string materialPath = EditorUtility.SaveFilePanelInProject(
                "Save New Material",
                targetObject.name + "_Material.mat",
                "mat",
                "Choose a path for the new material asset.");
            if (string.IsNullOrEmpty(materialPath))
            {
                return;
            }

            Material material = new Material(Shader.Find("Standard"));
            if (material == null)
            {
                EditorUtility.DisplayDialog("Texture Painter", "The Standard shader was not found in this project.", "OK");
                return;
            }

            material.name = Path.GetFileNameWithoutExtension(materialPath);
            AssetDatabase.CreateAsset(material, materialPath);
            AssetDatabase.SaveAssets();

            renderer.sharedMaterial = material;
            targetRenderer = renderer;
            targetMaterial = material;
            activeTexture = null;
            paintMode = PaintMode.None;
            EditorUtility.SetDirty(renderer);
        }

        private void CreateNewDiffuseTexture()
        {
            if (targetObject == null)
            {
                EditorUtility.DisplayDialog("Texture Painter", "Select a mesh GameObject before creating a new diffuse texture.", "OK");
                return;
            }

            var renderer = targetObject.GetComponent<Renderer>();
            if (renderer == null)
            {
                EditorUtility.DisplayDialog("Texture Painter", "The selected GameObject must have a Renderer component.", "OK");
                return;
            }

            if (renderer.sharedMaterial == null)
            {
                EditorUtility.DisplayDialog("Texture Painter", "Create a new material first, then create the diffuse texture.", "OK");
                return;
            }

            string texturePath = EditorUtility.SaveFilePanelInProject(
                "Save New Diffuse Texture",
                targetObject.name + "_DiffuseTexture.png",
                "png",
                "Choose a path for the new diffuse texture asset.");
            if (string.IsNullOrEmpty(texturePath))
            {
                return;
            }

            Texture2D texture = TexturePainterUtility.CreateTextureAsset(texturePath);
            if (texture == null)
            {
                return;
            }

            renderer.sharedMaterial.SetTexture("_MainTex", texture);
            renderer.sharedMaterial.SetColor("_Color", Color.white);
            targetRenderer = renderer;
            targetMaterial = renderer.sharedMaterial;
            activeTexture = texture;
            paintMode = PaintMode.Texture;
            EditorUtility.SetDirty(renderer);
        }

        private void UnwrapTargetMesh()
        {
            if (targetObject == null)
            {
                EditorUtility.DisplayDialog("Texture Painter", "Select a mesh GameObject to unwrap.", "OK");
                return;
            }

            var meshFilter = targetObject.GetComponent<MeshFilter>();
            if (meshFilter == null || meshFilter.sharedMesh == null)
            {
                EditorUtility.DisplayDialog("Texture Painter", "The selected object does not have a MeshFilter or a mesh to unwrap.", "OK");
                return;
            }

            var mesh = EnsurePersistentEditableMeshForUnwrap(meshFilter);
            if (mesh == null)
            {
                EditorUtility.DisplayDialog("Texture Painter", "Failed to prepare a writable mesh for UV unwrap.", "OK");
                return;
            }

            TextureUvUtility.GenerateUvLayout(mesh, unwrapMode);
            EditorUtility.SetDirty(mesh);
            AssetDatabase.SaveAssets();
            if (targetObject.scene.IsValid())
            {
                EditorSceneManager.MarkSceneDirty(targetObject.scene);
            }

            Debug.Log("[Texture Painter] Generated UV layout for " + mesh.name + " using " + unwrapMode + ".");
        }

        private Mesh EnsurePersistentEditableMeshForUnwrap(MeshFilter meshFilter)
        {
            Mesh currentMesh = meshFilter.sharedMesh;
            if (currentMesh == null)
            {
                return null;
            }

            if (CanEditMeshInPlace(currentMesh) && !IsMeshSharedByMultipleObjects(currentMesh, meshFilter))
            {
                return currentMesh;
            }

            string targetFolder = EnsureGeneratedFolderExists();
            if (string.IsNullOrEmpty(targetFolder))
            {
                return null;
            }

            Mesh meshCopy = Instantiate(currentMesh);
            meshCopy.name = currentMesh.name + "_UvUnwrapped";

            string meshAssetPath = AssetDatabase.GenerateUniqueAssetPath(Path.Combine(targetFolder, meshCopy.name + ".asset"));
            AssetDatabase.CreateAsset(meshCopy, meshAssetPath);

            Undo.RecordObject(meshFilter, "Assign Unwrapped Mesh");
            meshFilter.sharedMesh = meshCopy;
            EditorUtility.SetDirty(meshFilter);

            MeshCollider meshCollider = meshFilter.GetComponent<MeshCollider>();
            if (meshCollider != null && meshCollider.sharedMesh == currentMesh)
            {
                Undo.RecordObject(meshCollider, "Assign Unwrapped Mesh");
                meshCollider.sharedMesh = meshCopy;
                EditorUtility.SetDirty(meshCollider);
            }

            return meshCopy;
        }

        private static bool CanEditMeshInPlace(Mesh mesh)
        {
            if (mesh == null)
            {
                return false;
            }

            string meshPath = AssetDatabase.GetAssetPath(mesh);
            if (string.IsNullOrEmpty(meshPath))
            {
                return false;
            }

            if (!meshPath.EndsWith(".asset"))
            {
                return false;
            }

            return AssetDatabase.IsMainAsset(mesh);
        }

        private static bool IsMeshSharedByMultipleObjects(Mesh mesh, MeshFilter owner)
        {
            MeshFilter[] allMeshFilters = Object.FindObjectsByType<MeshFilter>(FindObjectsSortMode.None);
            int usageCount = 0;
            for (int i = 0; i < allMeshFilters.Length; i++)
            {
                MeshFilter filter = allMeshFilters[i];
                if (filter == null || filter.sharedMesh != mesh)
                {
                    continue;
                }

                usageCount++;
                if (usageCount > 1)
                {
                    return true;
                }
            }

            return false;
        }

        private static string EnsureGeneratedFolderExists()
        {
            if (AssetDatabase.IsValidFolder(DefaultGeneratedFolder))
            {
                return DefaultGeneratedFolder;
            }

            string[] parts = DefaultGeneratedFolder.Split('/');
            if (parts.Length == 0 || parts[0] != "Assets")
            {
                return null;
            }

            string currentPath = "Assets";
            for (int i = 1; i < parts.Length; i++)
            {
                string nextPath = currentPath + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(nextPath))
                {
                    AssetDatabase.CreateFolder(currentPath, parts[i]);
                }

                currentPath = nextPath;
            }

            return currentPath;
        }

        private void ExportUvLayoutPng()
        {
            if (targetObject == null)
            {
                EditorUtility.DisplayDialog("Texture Painter", "Select a mesh GameObject before exporting UV layout.", "OK");
                return;
            }

            if (targetRenderer == null)
            {
                targetRenderer = targetObject.GetComponent<Renderer>();
            }

            if (targetRenderer == null)
            {
                EditorUtility.DisplayDialog("Texture Painter", "The selected GameObject must have a Renderer component.", "OK");
                return;
            }

            string path = EditorUtility.SaveFilePanel(
                "Export UV Layout PNG",
                Application.dataPath,
                targetObject.name + "_UVLayout",
                "png");
            if (string.IsNullOrEmpty(path))
            {
                return;
            }

            bool success = TexturePainterUtility.ExportUvLayoutPng(targetObject, targetRenderer, path, uvExportResolution, new Color(1f, 0.95f, 0.2f, 1f));
            if (!success)
            {
                EditorUtility.DisplayDialog("Texture Painter", "Failed to export UV layout. Ensure the target has a valid mesh with UVs.", "OK");
                return;
            }

            Debug.Log("[Texture Painter] UV layout exported to " + path);
            AssetDatabase.Refresh();
        }

        private void BakeAmbientOcclusionMap()
        {
            if (targetObject == null)
            {
                EditorUtility.DisplayDialog("Texture Painter", "Select a mesh GameObject before baking ambient occlusion.", "OK");
                return;
            }

            if (targetRenderer == null)
            {
                targetRenderer = targetObject.GetComponent<Renderer>();
            }

            if (targetRenderer == null)
            {
                EditorUtility.DisplayDialog("Texture Painter", "The selected GameObject must have a Renderer component.", "OK");
                return;
            }

            string path = EditorUtility.SaveFilePanel(
                "Bake Ambient Occlusion PNG",
                Application.dataPath,
                targetObject.name + "_AO",
                "png");
            if (string.IsNullOrEmpty(path))
            {
                return;
            }

            if (AmbientOcclusionBaker.BakeAndSaveAmbientOcclusion(targetObject, targetRenderer, path, aoBakeResolution, aoSampleCount, aoRayDistance, out string error))
            {
                Debug.Log("[Texture Painter] Ambient occlusion baked to " + path);
                AssetDatabase.Refresh();
                return;
            }

            EditorUtility.DisplayDialog("Texture Painter", error ?? "Failed to bake ambient occlusion map.", "OK");
        }

        private void OnSceneGUI(SceneView sceneView)
        {
            Event e = Event.current;

            if (paintMode == PaintMode.None || targetObject == null || targetRenderer == null)
            {
                return;
            }

            if (e.type == EventType.Layout)
            {
                HandleUtility.AddDefaultControl(GUIUtility.GetControlID(FocusType.Passive));
            }

            if (activeTexture == null)
            {
                activeTexture = GetCurrentTextureForMode();
                if (activeTexture == null)
                {
                    return;
                }
            }

            if (e.type == EventType.MouseDown && e.button == 0)
            {
                isPainting = true;
                hasLastPaintUv = false;
                hasLastPaintTriangleIndex = false;
                lastPaintTriangleIndex = -1;
                hasCachedFaceMetric = false;
                e.Use();

                if (TryGetHitInfo(out RaycastHit hit, out Vector2 uv, out int triangleIndex))
                {
                    CaptureTextureUndoSnapshot();
                    PaintAtUv(uv, false, triangleIndex);
                }
                return;
            }

            if (e.type == EventType.MouseDrag && e.button == 0 && isPainting)
            {
                e.Use();
                if (TryGetHitInfo(out RaycastHit hit, out Vector2 uv, out int triangleIndex))
                {
                    PaintAtUv(uv, true, triangleIndex);
                }
                return;
            }

            if (e.type == EventType.MouseUp && e.button == 0)
            {
                isPainting = false;
                hasLastPaintUv = false;
                hasLastPaintTriangleIndex = false;
                lastPaintTriangleIndex = -1;
                hasCachedFaceMetric = false;
                cachedFaceTriangleIndex = -1;
                e.Use();
                return;
            }

            if (e.type == EventType.Repaint)
            {
                if (TryGetHitInfo(out RaycastHit hit, out Vector2 uv, out int triangleIndex))
                {
                    DrawBrushPreview(hit);
                }
            }
        }

        private bool TryGetHitInfo(out RaycastHit hit, out Vector2 uv, out int triangleIndex)
        {
            hit = default;
            uv = Vector2.zero;
            triangleIndex = -1;

            if (targetObject == null || targetRenderer == null)
            {
                return false;
            }

            Ray ray = HandleUtility.GUIPointToWorldRay(Event.current.mousePosition);
            if (!Physics.Raycast(ray, out hit, 10000f, ~0, QueryTriggerInteraction.Ignore))
            {
                return false;
            }

            if (!TexturePainterUtility.IsTargetRendererHit(targetRenderer, hit))
            {
                return false;
            }

            if (TryGetUvAndTriangleFromHit(ray, hit, out uv, out triangleIndex))
            {
                return true;
            }

            return false;
        }

        private bool TryGetUvAndTriangleFromHit(Ray worldRay, RaycastHit hit, out Vector2 uv, out int triangleIndex)
        {
            uv = hit.textureCoord;
            triangleIndex = hit.triangleIndex;

            if (triangleIndex >= 0)
            {
                return true;
            }

            MeshFilter filter = targetRenderer != null ? targetRenderer.GetComponent<MeshFilter>() : null;
            Mesh mesh = filter != null ? filter.sharedMesh : null;
            if (mesh == null)
            {
                return false;
            }

            Vector3[] vertices = mesh.vertices;
            Vector2[] uvs = mesh.uv;
            int[] triangles = mesh.triangles;
            if (vertices == null || uvs == null || triangles == null || vertices.Length == 0 || uvs.Length == 0 || triangles.Length < 3)
            {
                return false;
            }

            Transform meshTransform = targetRenderer.transform;
            Ray localRay = new Ray(
                meshTransform.InverseTransformPoint(worldRay.origin),
                meshTransform.InverseTransformDirection(worldRay.direction));

            if (!TryRaycastMeshLocal(localRay, vertices, uvs, triangles, out triangleIndex, out uv))
            {
                return false;
            }

            return true;
        }

        private static bool TryRaycastMeshLocal(
            Ray localRay,
            Vector3[] vertices,
            Vector2[] uvs,
            int[] triangles,
            out int triangleIndex,
            out Vector2 uv)
        {
            triangleIndex = -1;
            uv = Vector2.zero;

            float bestDistance = float.PositiveInfinity;
            bool hitAny = false;

            for (int triBase = 0; triBase + 2 < triangles.Length; triBase += 3)
            {
                int aIndex = triangles[triBase];
                int bIndex = triangles[triBase + 1];
                int cIndex = triangles[triBase + 2];
                if (aIndex < 0 || bIndex < 0 || cIndex < 0 || aIndex >= vertices.Length || bIndex >= vertices.Length || cIndex >= vertices.Length)
                {
                    continue;
                }

                if (aIndex >= uvs.Length || bIndex >= uvs.Length || cIndex >= uvs.Length)
                {
                    continue;
                }

                if (!TryIntersectRayTriangle(localRay, vertices[aIndex], vertices[bIndex], vertices[cIndex], out float distance, out Vector3 barycentric))
                {
                    continue;
                }

                if (distance >= bestDistance)
                {
                    continue;
                }

                bestDistance = distance;
                triangleIndex = triBase / 3;
                uv = uvs[aIndex] * barycentric.x + uvs[bIndex] * barycentric.y + uvs[cIndex] * barycentric.z;
                hitAny = true;
            }

            return hitAny;
        }

        private static bool TryIntersectRayTriangle(
            Ray ray,
            Vector3 v0,
            Vector3 v1,
            Vector3 v2,
            out float distance,
            out Vector3 barycentric)
        {
            const float epsilon = 0.000001f;
            distance = 0f;
            barycentric = Vector3.zero;

            Vector3 edge1 = v1 - v0;
            Vector3 edge2 = v2 - v0;
            Vector3 pVec = Vector3.Cross(ray.direction, edge2);
            float det = Vector3.Dot(edge1, pVec);
            if (Mathf.Abs(det) < epsilon)
            {
                return false;
            }

            float invDet = 1f / det;
            Vector3 tVec = ray.origin - v0;
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
            barycentric = new Vector3(1f - u - v, u, v);
            return true;
        }

        private void DrawBrushPreview(RaycastHit hit)
        {
            Handles.color = new Color(brushColor.r, brushColor.g, brushColor.b, 0.75f);
            Handles.DrawWireDisc(hit.point, hit.normal, brushRadius);
        }

        private void PaintAtUv(Vector2 uv, bool interpolateFromLast, int triangleIndexHint)
        {
            if (activeTexture == null)
            {
                return;
            }

            if (!TexturePainterUtility.EnsureTextureReadable(activeTexture))
            {
                return;
            }

            if (!TryGetOrUpdateFaceMetric(uv, triangleIndexHint, out int resolvedTriangleIndex, out float m00, out float m01, out float m11))
            {
                return;
            }

            Color[] pixels = activeTexture.GetPixels();
            bool changed = false;
            bool canInterpolate =
                interpolateFromLast &&
                hasLastPaintUv &&
                hasLastPaintTriangleIndex &&
                resolvedTriangleIndex >= 0 &&
                AreTrianglesInSameUvIsland(lastPaintTriangleIndex, resolvedTriangleIndex);

            if (canInterpolate)
            {
                float uvRadius = TexturePainterUtility.EstimateUvRadiusFromMetric(brushRadius, m00, m11);
                float spacing = Mathf.Max(0.0005f, uvRadius * 0.35f);
                float distance = Vector2.Distance(lastPaintUv, uv);
                int steps = Mathf.Max(1, Mathf.CeilToInt(distance / spacing));
                steps = Mathf.Min(24, steps);

                for (int i = 1; i <= steps; i++)
                {
                    float t = i / (float)steps;
                    Vector2 sampleUv = Vector2.Lerp(lastPaintUv, uv, t);
                    changed |= TexturePainterUtility.PaintTexturePixelsUvBrushWorldCircular(
                        pixels,
                        activeTexture.width,
                        activeTexture.height,
                        sampleUv,
                        brushRadius,
                        m00,
                        m01,
                        m11,
                        brushColor,
                        brushOpacity);
                }
            }
            else
            {
                changed = TexturePainterUtility.PaintTexturePixelsUvBrushWorldCircular(
                    pixels,
                    activeTexture.width,
                    activeTexture.height,
                    uv,
                    brushRadius,
                    m00,
                    m01,
                    m11,
                    brushColor,
                    brushOpacity);
            }

            if (changed)
            {
                activeTexture.SetPixels(pixels);
                activeTexture.Apply();
                MarkTextureDirtyAndQueueQuickSave();
            }

            lastPaintUv = uv;
            hasLastPaintUv = true;
            lastPaintTriangleIndex = resolvedTriangleIndex;
            hasLastPaintTriangleIndex = resolvedTriangleIndex >= 0;
            Repaint();
        }

        private void HandlePreviewPainting(Rect previewRect)
        {
            if (activeTexture == null || targetObject == null || targetRenderer == null)
            {
                return;
            }

            Event e = Event.current;
            if (e.button != 0)
            {
                return;
            }

            if (e.type == EventType.MouseDown && previewRect.Contains(e.mousePosition))
            {
                isPreviewPainting = true;
                hasLastPaintUv = false;
                hasLastPaintTriangleIndex = false;
                lastPaintTriangleIndex = -1;
                hasCachedFaceMetric = false;
                CaptureTextureUndoSnapshot();
                PaintPreviewAtMouse(previewRect, e.mousePosition);
                e.Use();
                return;
            }

            if (e.type == EventType.MouseDrag && isPreviewPainting)
            {
                PaintPreviewAtMouse(previewRect, e.mousePosition);
                e.Use();
                return;
            }

            if (e.type == EventType.MouseUp && isPreviewPainting)
            {
                isPreviewPainting = false;
                hasLastPaintUv = false;
                hasLastPaintTriangleIndex = false;
                lastPaintTriangleIndex = -1;
                hasCachedFaceMetric = false;
                cachedFaceTriangleIndex = -1;
                e.Use();
            }
        }

        private void PaintPreviewAtMouse(Rect previewRect, Vector2 mousePosition)
        {
            if (previewRect.width <= 0f || previewRect.height <= 0f)
            {
                return;
            }

            float u = Mathf.Clamp01((mousePosition.x - previewRect.x) / previewRect.width);
            float v = 1f - Mathf.Clamp01((mousePosition.y - previewRect.y) / previewRect.height);
            Vector2 uv = new Vector2(u, v);

            int triangleIndexHint = -1;
            if (TexturePainterUtility.TryFindTriangleIndexByUv(targetObject, targetRenderer, uv, out int foundTriangleIndex))
            {
                triangleIndexHint = foundTriangleIndex;
            }

            PaintAtUv(uv, true, triangleIndexHint);
            Repaint();
        }

        private bool TryGetOrUpdateFaceMetric(Vector2 uv, int triangleIndexHint, out int resolvedTriangleIndex, out float m00, out float m01, out float m11)
        {
            resolvedTriangleIndex = -1;
            m00 = 0f;
            m01 = 0f;
            m11 = 0f;

            int triangleIndex = triangleIndexHint;
            if (triangleIndex < 0)
            {
                if (!TexturePainterUtility.TryFindTriangleIndexByUv(targetObject, targetRenderer, uv, out triangleIndex))
                {
                    return false;
                }
            }

            resolvedTriangleIndex = triangleIndex;

            if (hasCachedFaceMetric && triangleIndex == cachedFaceTriangleIndex)
            {
                m00 = cachedMetricM00;
                m01 = cachedMetricM01;
                m11 = cachedMetricM11;
                return true;
            }

            if (!TexturePainterUtility.TryGetTriangleUvMetric(targetObject, targetRenderer, triangleIndex, out m00, out m01, out m11))
            {
                return false;
            }

            cachedFaceTriangleIndex = triangleIndex;
            cachedMetricM00 = m00;
            cachedMetricM01 = m01;
            cachedMetricM11 = m11;
            hasCachedFaceMetric = true;
            return true;
        }

        private bool AreTrianglesInSameUvIsland(int firstTriangleIndex, int secondTriangleIndex)
        {
            if (firstTriangleIndex < 0 || secondTriangleIndex < 0)
            {
                return false;
            }

            MeshFilter filter = targetRenderer != null ? targetRenderer.GetComponent<MeshFilter>() : null;
            Mesh mesh = filter != null ? filter.sharedMesh : null;
            if (mesh == null)
            {
                return false;
            }

            if (cachedUvIslandMesh != mesh || cachedUvIslandIds == null)
            {
                if (!TexturePainterUtility.TryBuildTriangleUvIslandMap(targetObject, targetRenderer, out cachedUvIslandIds))
                {
                    return false;
                }

                cachedUvIslandMesh = mesh;
            }

            if (cachedUvIslandIds == null)
            {
                return false;
            }

            if (firstTriangleIndex >= cachedUvIslandIds.Length || secondTriangleIndex >= cachedUvIslandIds.Length)
            {
                return false;
            }

            return cachedUvIslandIds[firstTriangleIndex] == cachedUvIslandIds[secondTriangleIndex];
        }

        private Rect DrawUvOverlayPreview(Texture2D texture)
        {
            float availableWidth = Mathf.Max(1f, EditorGUIUtility.currentViewWidth - 24f);
            float previewSize = Mathf.Min(256f, availableWidth);
            Rect previewRect = GUILayoutUtility.GetRect(previewSize, previewSize, GUILayout.ExpandWidth(true));

            EditorGUI.DrawPreviewTexture(previewRect, texture, null, ScaleMode.StretchToFill);

            MeshFilter meshFilter = targetObject != null ? targetObject.GetComponent<MeshFilter>() : null;
            Mesh mesh = meshFilter != null ? meshFilter.sharedMesh : null;
            if (mesh == null || mesh.uv == null || mesh.uv.Length == 0 || mesh.triangles == null || mesh.triangles.Length < 3)
            {
                return previewRect;
            }

            Vector2[] uvs = mesh.uv;
            int[] triangles = mesh.triangles;

            Handles.BeginGUI();
            Color previousColor = Handles.color;
            Handles.color = new Color(1f, 0.95f, 0.2f, 0.95f);

            const float dotSpacing = 3f;
            for (int i = 0; i + 2 < triangles.Length; i += 3)
            {
                int aIndex = triangles[i];
                int bIndex = triangles[i + 1];
                int cIndex = triangles[i + 2];

                if (aIndex < 0 || bIndex < 0 || cIndex < 0 || aIndex >= uvs.Length || bIndex >= uvs.Length || cIndex >= uvs.Length)
                {
                    continue;
                }

                Vector3 a = TexturePainterUtility.UvToPreviewPoint(uvs[aIndex], previewRect);
                Vector3 b = TexturePainterUtility.UvToPreviewPoint(uvs[bIndex], previewRect);
                Vector3 c = TexturePainterUtility.UvToPreviewPoint(uvs[cIndex], previewRect);

                Handles.DrawDottedLine(a, b, dotSpacing);
                Handles.DrawDottedLine(b, c, dotSpacing);
                Handles.DrawDottedLine(c, a, dotSpacing);
            }

            Handles.color = previousColor;
            Handles.EndGUI();

            return previewRect;
        }

        private void DrawRecentPalette()
        {
            EditorGUILayout.Space(2f);
            EditorGUILayout.LabelField("Recent Palette", EditorStyles.miniBoldLabel);

            Rect rowRect = GUILayoutUtility.GetRect(1f, 24f, GUILayout.ExpandWidth(true));
            float slotSpacing = 4f;
            float slotSize = 20f;
            float x = rowRect.x;
            float y = rowRect.y + 2f;

            for (int i = 0; i < PaletteCapacity; i++)
            {
                Rect swatchRect = new Rect(x, y, slotSize, slotSize);
                if (i < recentPaletteColors.Count)
                {
                    Color swatchColor = recentPaletteColors[i];
                    EditorGUI.DrawRect(swatchRect, swatchColor);
                    DrawSwatchOutline(swatchRect, Color.black);

                    if (GUI.Button(swatchRect, GUIContent.none, GUIStyle.none))
                    {
                        if (!AreColorsApproximatelyEqual(brushColor, swatchColor))
                        {
                            AddColorToRecentPalette(brushColor);
                            brushColor = swatchColor;
                            Repaint();
                        }
                    }
                }
                else
                {
                    EditorGUI.DrawRect(swatchRect, new Color(0f, 0f, 0f, 0.08f));
                    DrawSwatchOutline(swatchRect, new Color(0f, 0f, 0f, 0.25f));
                }

                x += slotSize + slotSpacing;
            }
        }

        private static void DrawSwatchOutline(Rect rect, Color outlineColor)
        {
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, 1f), outlineColor);
            EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - 1f, rect.width, 1f), outlineColor);
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, 1f, rect.height), outlineColor);
            EditorGUI.DrawRect(new Rect(rect.xMax - 1f, rect.y, 1f, rect.height), outlineColor);
        }

        private void AddColorToRecentPalette(Color color)
        {
            for (int i = 0; i < recentPaletteColors.Count; i++)
            {
                if (AreColorsApproximatelyEqual(recentPaletteColors[i], color))
                {
                    recentPaletteColors.RemoveAt(i);
                    break;
                }
            }

            recentPaletteColors.Insert(0, color);
            if (recentPaletteColors.Count > PaletteCapacity)
            {
                recentPaletteColors.RemoveAt(recentPaletteColors.Count - 1);
            }
        }

        private static bool AreColorsApproximatelyEqual(Color a, Color b)
        {
            return Mathf.Abs(a.r - b.r) < 0.0001f
                && Mathf.Abs(a.g - b.g) < 0.0001f
                && Mathf.Abs(a.b - b.b) < 0.0001f
                && Mathf.Abs(a.a - b.a) < 0.0001f;
        }

        private void DrawTextureUndoControls()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                bool canUndo = activeTexture != null && textureUndoHistory.Count > 0;
                bool canRedo = activeTexture != null && textureRedoHistory.Count > 0;

                EditorGUI.BeginDisabledGroup(!canUndo);
                if (GUILayout.Button("Undo Paint"))
                {
                    UndoTexturePaint();
                }
                EditorGUI.EndDisabledGroup();

                EditorGUI.BeginDisabledGroup(!canRedo);
                if (GUILayout.Button("Redo Paint"))
                {
                    RedoTexturePaint();
                }
                EditorGUI.EndDisabledGroup();
            }

            EditorGUILayout.LabelField("History: " + textureUndoHistory.Count + " undo / " + textureRedoHistory.Count + " redo", EditorStyles.miniLabel);
        }

        private void EnsureTextureHistoryTarget()
        {
            if (activeTexture == historyTexture)
            {
                return;
            }

            historyTexture = activeTexture;
            textureUndoHistory.Clear();
            textureRedoHistory.Clear();
            historyTextureWidth = historyTexture != null ? historyTexture.width : 0;
            historyTextureHeight = historyTexture != null ? historyTexture.height : 0;
        }

        private void CaptureTextureUndoSnapshot()
        {
            if (activeTexture == null)
            {
                return;
            }

            if (!TexturePainterUtility.EnsureTextureReadable(activeTexture))
            {
                return;
            }

            EnsureTextureHistoryTarget();
            if (activeTexture.width != historyTextureWidth || activeTexture.height != historyTextureHeight)
            {
                textureUndoHistory.Clear();
                textureRedoHistory.Clear();
                historyTextureWidth = activeTexture.width;
                historyTextureHeight = activeTexture.height;
            }

            textureUndoHistory.Add(activeTexture.GetPixels32());
            if (textureUndoHistory.Count > TextureHistoryCapacity)
            {
                textureUndoHistory.RemoveAt(0);
            }

            textureRedoHistory.Clear();
        }

        private void UndoTexturePaint()
        {
            if (activeTexture == null || textureUndoHistory.Count == 0)
            {
                return;
            }

            EnsureTextureHistoryTarget();
            textureRedoHistory.Add(activeTexture.GetPixels32());
            if (textureRedoHistory.Count > TextureHistoryCapacity)
            {
                textureRedoHistory.RemoveAt(0);
            }

            int lastIndex = textureUndoHistory.Count - 1;
            Color32[] snapshot = textureUndoHistory[lastIndex];
            textureUndoHistory.RemoveAt(lastIndex);
            ApplyTextureSnapshot(snapshot);
        }

        private void RedoTexturePaint()
        {
            if (activeTexture == null || textureRedoHistory.Count == 0)
            {
                return;
            }

            EnsureTextureHistoryTarget();
            textureUndoHistory.Add(activeTexture.GetPixels32());
            if (textureUndoHistory.Count > TextureHistoryCapacity)
            {
                textureUndoHistory.RemoveAt(0);
            }

            int lastIndex = textureRedoHistory.Count - 1;
            Color32[] snapshot = textureRedoHistory[lastIndex];
            textureRedoHistory.RemoveAt(lastIndex);
            ApplyTextureSnapshot(snapshot);
        }

        private void ApplyTextureSnapshot(Color32[] snapshot)
        {
            if (activeTexture == null || snapshot == null)
            {
                return;
            }

            if (snapshot.Length != activeTexture.width * activeTexture.height)
            {
                return;
            }

            activeTexture.SetPixels32(snapshot);
            activeTexture.Apply();
            EditorUtility.SetDirty(activeTexture);
            Repaint();
            SceneView.RepaintAll();
        }

        private void MarkTextureDirtyAndQueueQuickSave()
        {
            if (activeTexture == null)
            {
                return;
            }

            if (!EditorUtility.IsDirty(activeTexture))
            {
                EditorUtility.SetDirty(activeTexture);
            }

            if (quickSaveTexture != activeTexture)
            {
                quickSaveTexture = activeTexture;
                nextQuickSaveTime = 0d;
            }

            hasPendingQuickSave = true;
            TryWriteQuickSaveCopyIfDue();
        }

        private void TryWriteQuickSaveCopyIfDue()
        {
            if (!hasPendingQuickSave || activeTexture == null || quickSaveTexture != activeTexture)
            {
                return;
            }

            double now = EditorApplication.timeSinceStartup;
            if (now < nextQuickSaveTime)
            {
                return;
            }

            if (!TexturePainterUtility.EnsureTextureReadable(activeTexture))
            {
                return;
            }

            if (!TryGetQuickSavePaths(activeTexture, out string quickAssetPath, out string quickAbsolutePath))
            {
                return;
            }

            try
            {
                byte[] bytes = activeTexture.EncodeToPNG();
                File.WriteAllBytes(quickAbsolutePath, bytes);
                AssetDatabase.ImportAsset(quickAssetPath, ImportAssetOptions.ForceUpdate);
                hasPendingQuickSave = false;
                nextQuickSaveTime = now + QuickSaveIntervalSeconds;
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning("[Texture Painter] Failed to write quick-save texture copy: " + ex.Message);
            }
        }

        private static bool TryGetQuickSavePaths(Texture2D texture, out string quickAssetPath, out string quickAbsolutePath)
        {
            quickAssetPath = null;
            quickAbsolutePath = null;

            if (texture == null)
            {
                return false;
            }

            string assetPath = AssetDatabase.GetAssetPath(texture);
            if (string.IsNullOrEmpty(assetPath) || !assetPath.EndsWith(".png", System.StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            string directory = Path.GetDirectoryName(assetPath);
            string fileName = Path.GetFileNameWithoutExtension(assetPath);
            if (string.IsNullOrEmpty(directory) || string.IsNullOrEmpty(fileName))
            {
                return false;
            }

            quickAssetPath = Path.Combine(directory, fileName + QuickSaveSuffix + ".png").Replace('\\', '/');
            string projectRoot = Path.GetDirectoryName(Application.dataPath);
            quickAbsolutePath = Path.GetFullPath(Path.Combine(projectRoot, quickAssetPath));
            return true;
        }

    }
}
