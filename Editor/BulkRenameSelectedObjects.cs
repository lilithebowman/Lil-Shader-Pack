using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

public sealed class BulkRenameSelectedObjects : ScriptableWizard
{
	[SerializeField] private string baseName = "Object_";
	[SerializeField] private int startIndex = 1;
	[SerializeField] private int numberPadding = 3;

	[MenuItem("Lilithe/Bulk Rename Selected Objects")]
	private static void ShowWizard()
	{
		DisplayWizard<BulkRenameSelectedObjects>("Bulk Rename Selected Objects", "Rename");
	}

	[MenuItem("Lilithe/Bulk Rename Selected Objects", true)]
	private static bool ValidateShowWizard()
	{
		return Selection.gameObjects.Length > 0;
	}

	private void OnWizardUpdate()
	{
		helpString = "Renames selected GameObjects in hierarchy order using a sequential pattern.";
		numberPadding = Mathf.Clamp(numberPadding, 1, 8);
	}

	private void OnWizardCreate()
	{
		var selectedTransforms = Selection.transforms
			.Where(t => t != null)
			.Distinct()
			.OrderBy(GetHierarchySortKey, StringComparer.Ordinal)
			.ToList();

		if (selectedTransforms.Count == 0)
		{
			EditorUtility.DisplayDialog("Bulk Rename Selected Objects", "Select one or more GameObjects in the Hierarchy.", "OK");
			return;
		}

		if (string.IsNullOrWhiteSpace(baseName))
		{
			EditorUtility.DisplayDialog("Bulk Rename Selected Objects", "Base Name cannot be empty.", "OK");
			return;
		}

		if (startIndex < 0)
		{
			EditorUtility.DisplayDialog("Bulk Rename Selected Objects", "Start Index must be 0 or greater.", "OK");
			return;
		}

		Undo.RecordObjects(selectedTransforms.Select(t => t.gameObject).ToArray(), "Bulk Rename Selected Objects");

		var selectedIds = new HashSet<int>(selectedTransforms.Select(t => t.GetInstanceID()));
		var reservedNames = new HashSet<string>(StringComparer.Ordinal);

		foreach (var transform in Resources.FindObjectsOfTypeAll<Transform>())
		{
			if (transform == null || EditorUtility.IsPersistent(transform) || selectedIds.Contains(transform.GetInstanceID()))
			{
				continue;
			}

			reservedNames.Add(transform.name);
		}

		var nextIndex = startIndex;
		var renamedCount = 0;

		foreach (var transform in selectedTransforms)
		{
			var targetName = BuildUniqueName(baseName.Trim(), numberPadding, ref nextIndex, reservedNames);
			if (!string.Equals(transform.name, targetName, StringComparison.Ordinal))
			{
				transform.name = targetName;
				renamedCount++;
			}
		}

		EditorUtility.DisplayDialog(
			"Bulk Rename Selected Objects",
			$"Renamed {renamedCount} of {selectedTransforms.Count} selected object(s).",
			"OK");
	}

	private static string BuildUniqueName(string baseNameValue, int padding, ref int nextIndex, HashSet<string> reservedNames)
	{
		while (true)
		{
			var candidate = baseNameValue + nextIndex.ToString("D" + padding);
			nextIndex++;

			if (reservedNames.Add(candidate))
			{
				return candidate;
			}
		}
	}

	private static string GetHierarchySortKey(Transform transform)
	{
		var pathParts = new List<string>(8);
		var current = transform;

		while (current != null)
		{
			pathParts.Add(current.GetSiblingIndex().ToString("D4"));
			current = current.parent;
		}

		pathParts.Reverse();
		return string.Join("/", pathParts) + "/" + transform.name;
	}
}
