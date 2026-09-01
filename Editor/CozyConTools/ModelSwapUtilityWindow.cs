using System;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

public class ModelSwapUtilityWindow : EditorWindow
{
	GameObject replacementModelAsset;
	bool preserveObjectName = true;
	bool preserveNonModelChildren = true;

	[MenuItem("Lilithe/Model Swap Utility")]
	public static void ShowWindow()
	{
		GetWindow<ModelSwapUtilityWindow>("Model Swap Utility");
	}

	void OnGUI()
	{
		EditorGUILayout.LabelField("Model Swap Utility", EditorStyles.boldLabel);
		EditorGUILayout.HelpBox(
			"Select one or more scene GameObjects, choose a replacement model asset (FBX/prefab), then click Replace Model. " +
			"The tool preserves non-mesh root properties/components and can keep non-model children.",
			MessageType.Info);

		replacementModelAsset = (GameObject)EditorGUILayout.ObjectField(
			"Replacement Model",
			replacementModelAsset,
			typeof(GameObject),
			false);

		preserveObjectName = EditorGUILayout.Toggle("Preserve Object Name", preserveObjectName);
		preserveNonModelChildren = EditorGUILayout.Toggle("Preserve Non-Model Children", preserveNonModelChildren);

		using (new EditorGUI.DisabledScope(!CanReplace()))
		{
			if (GUILayout.Button("REPLACE MODEL", GUILayout.Height(32f)))
			{
				ReplaceSelectedModels();
			}
		}

		if (!HasValidSelection())
		{
			EditorGUILayout.HelpBox("Select at least one scene GameObject to enable replacement.", MessageType.Warning);
		}
		if (replacementModelAsset == null)
		{
			EditorGUILayout.HelpBox("Assign a model asset (FBX or prefab) as replacement.", MessageType.Warning);
		}
	}

	bool CanReplace()
	{
		return replacementModelAsset != null && HasValidSelection();
	}

	bool HasValidSelection()
	{
		GameObject[] selected = Selection.gameObjects;
		for (int i = 0; i < selected.Length; i++)
		{
			if (selected[i] != null && selected[i].scene.IsValid()) return true;
		}
		return false;
	}

	void ReplaceSelectedModels()
	{
		GameObject[] selected = Selection.gameObjects;
		int processed = 0;
		System.Collections.Generic.List<GameObject> createdObjects = new System.Collections.Generic.List<GameObject>();

		Undo.IncrementCurrentGroup();
		int undoGroup = Undo.GetCurrentGroup();
		Undo.SetCurrentGroupName("Replace Model Instances");

		for (int i = 0; i < selected.Length; i++)
		{
			GameObject source = selected[i];
			if (source == null || !source.scene.IsValid()) continue;

			if (TryReplaceSingle(source, replacementModelAsset, out GameObject created))
			{
				processed++;
				createdObjects.Add(created);
			}
		}

		Undo.CollapseUndoOperations(undoGroup);
		if (createdObjects.Count > 0)
		{
			Selection.objects = createdObjects.ToArray();
		}
		EditorUtility.DisplayDialog("Model Swap", "Replaced " + processed + " object(s).", "OK");
	}

	bool TryReplaceSingle(GameObject source, GameObject replacementAsset, out GameObject replacementInstance)
	{
		replacementInstance = null;
		if (source == null || replacementAsset == null) return false;

		Transform sourceTransform = source.transform;
		Transform parent = sourceTransform.parent;
		int siblingIndex = sourceTransform.GetSiblingIndex();

		replacementInstance = InstantiateReplacement(replacementAsset, parent);
		if (replacementInstance == null) return false;

		Undo.RegisterCreatedObjectUndo(replacementInstance, "Create Replacement Model");

		CopyTransform(sourceTransform, replacementInstance.transform);
		replacementInstance.transform.SetSiblingIndex(siblingIndex);

		CopyGameObjectState(source, replacementInstance);
		CopyNonMeshComponents(source, replacementInstance);

		if (preserveNonModelChildren)
		{
			MovePreservedChildren(source.transform, replacementInstance.transform);
		}

		Undo.DestroyObjectImmediate(source);
		EditorUtility.SetDirty(replacementInstance);
		return true;
	}

	GameObject InstantiateReplacement(GameObject replacementAsset, Transform parent)
	{
		GameObject instance = PrefabUtility.InstantiatePrefab(replacementAsset, parent) as GameObject;
		if (instance == null)
		{
			instance = Instantiate(replacementAsset, parent);
		}
		return instance;
	}

	void CopyTransform(Transform source, Transform destination)
	{
		destination.position = source.position;
		destination.rotation = source.rotation;
		destination.localScale = source.localScale;
	}

	void CopyGameObjectState(GameObject source, GameObject destination)
	{
		destination.name = preserveObjectName ? source.name : destination.name;
		destination.tag = source.tag;
		destination.layer = source.layer;
		destination.isStatic = source.isStatic;
		GameObjectUtility.SetStaticEditorFlags(destination, GameObjectUtility.GetStaticEditorFlags(source));
		destination.SetActive(source.activeSelf);
	}

	void CopyNonMeshComponents(GameObject source, GameObject destination)
	{
		Component[] sourceComponents = source.GetComponents<Component>();
		for (int i = 0; i < sourceComponents.Length; i++)
		{
			Component component = sourceComponents[i];
			if (component == null) continue;

			Type componentType = component.GetType();
			if (componentType == typeof(Transform)) continue;
			if (IsMeshComponentType(componentType)) continue;

			CopyComponent(component, destination, componentType);
		}
	}

	void CopyComponent(Component sourceComponent, GameObject destination, Type componentType)
	{
		if (!ComponentUtility.CopyComponent(sourceComponent)) return;

		Component existing = destination.GetComponent(componentType);
		if (existing != null && IsDisallowMultiple(componentType))
		{
			ComponentUtility.PasteComponentValues(existing);
		}
		else
		{
			ComponentUtility.PasteComponentAsNew(destination);
		}
	}

	void MovePreservedChildren(Transform sourceRoot, Transform destinationRoot)
	{
		for (int i = sourceRoot.childCount - 1; i >= 0; i--)
		{
			Transform child = sourceRoot.GetChild(i);
			if (child == null) continue;

			if (ContainsMeshHierarchy(child)) continue;

			Undo.SetTransformParent(child, destinationRoot, "Preserve Child");
		}
	}

	bool ContainsMeshHierarchy(Transform root)
	{
		if (root.GetComponent<MeshFilter>() != null) return true;
		if (root.GetComponent<MeshRenderer>() != null) return true;
		if (root.GetComponent<SkinnedMeshRenderer>() != null) return true;

		for (int i = 0; i < root.childCount; i++)
		{
			if (ContainsMeshHierarchy(root.GetChild(i))) return true;
		}

		return false;
	}

	bool IsMeshComponentType(Type componentType)
	{
		return componentType == typeof(MeshFilter)
			|| componentType == typeof(MeshRenderer)
			|| componentType == typeof(SkinnedMeshRenderer)
			|| componentType == typeof(MeshCollider);
	}

	bool IsDisallowMultiple(Type componentType)
	{
		return Attribute.IsDefined(componentType, typeof(DisallowMultipleComponent), true);
	}
}