using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using VRC.Core;

public sealed class PipelineManagerWorldIdSetterWindow : EditorWindow
{
	private const string MenuPath = "Tools/VRChat/Set World ID On Pipeline Manager";
	private const string LastWorldIdKey = "PipelineManagerWorldIdSetterWindow.LastWorldId";

	private string _worldId = string.Empty;
	private bool _onlyActiveScene = true;

	[MenuItem(MenuPath)]
	private static void OpenWindow()
	{
		var window = GetWindow<PipelineManagerWorldIdSetterWindow>("Pipeline World ID");
		window.minSize = new Vector2(430f, 170f);
		window.Show();
	}

	private void OnEnable()
	{
		_worldId = EditorPrefs.GetString(LastWorldIdKey, _worldId);
	}

	private void OnGUI()
	{
		EditorGUILayout.LabelField("Assign World ID To VRChat Pipeline Manager", EditorStyles.boldLabel);
		EditorGUILayout.Space();

		EditorGUILayout.HelpBox(
			"Enter a world ID (usually starts with wrld_) and click Find.\n" +
			"The tool searches the scene for VRC Pipeline Manager and writes the ID.",
			MessageType.Info);

		_worldId = EditorGUILayout.TextField("World ID", _worldId);
		_onlyActiveScene = EditorGUILayout.ToggleLeft("Only search in active scene", _onlyActiveScene);

		EditorGUILayout.Space();
		using (new EditorGUI.DisabledScope(string.IsNullOrWhiteSpace(_worldId)))
		{
			if (GUILayout.Button("Find && Apply", GUILayout.Height(30f)))
			{
				FindAndApplyWorldId();
			}
		}
	}

	private void FindAndApplyWorldId()
	{
		var trimmedWorldId = _worldId.Trim();
		if (string.IsNullOrWhiteSpace(trimmedWorldId))
		{
			EditorUtility.DisplayDialog("Pipeline World ID", "Please enter a world ID.", "OK");
			return;
		}

		if (!trimmedWorldId.StartsWith("wrld_", StringComparison.OrdinalIgnoreCase))
		{
			var continueWithoutPrefix = EditorUtility.DisplayDialog(
				"World ID Format",
				"World IDs usually start with 'wrld_'.\nDo you want to continue anyway?",
				"Continue",
				"Cancel");

			if (!continueWithoutPrefix)
			{
				return;
			}
		}

		var pipelineManagers = FindPipelineManagers(_onlyActiveScene);
		if (pipelineManagers.Count == 0)
		{
			var scopeText = _onlyActiveScene ? "the active scene" : "loaded scenes";
			EditorUtility.DisplayDialog("Pipeline World ID", $"No VRC Pipeline Manager was found in {scopeText}.", "OK");
			return;
		}

		if (pipelineManagers.Count > 1)
		{
			var names = string.Join("\n", pipelineManagers.Select(pm => $"- {BuildPath(pm.transform)}"));
			var applyToFirst = EditorUtility.DisplayDialog(
				"Multiple Pipeline Managers Found",
				$"Found {pipelineManagers.Count} pipeline managers:\n\n{names}\n\nApply to the first one in the list?",
				"Apply To First",
				"Cancel");

			if (!applyToFirst)
			{
				return;
			}
		}

		var target = pipelineManagers[0];
		if (!TrySetWorldId(target, trimmedWorldId, out var errorMessage))
		{
			EditorUtility.DisplayDialog("Pipeline World ID", errorMessage, "OK");
			return;
		}

		EditorPrefs.SetString(LastWorldIdKey, trimmedWorldId);
		Selection.activeGameObject = target.gameObject;
		EditorGUIUtility.PingObject(target.gameObject);

		EditorUtility.DisplayDialog(
			"Pipeline World ID",
			$"World ID set successfully.\n\nTarget: {BuildPath(target.transform)}\nWorld ID: {trimmedWorldId}",
			"OK");
	}

	private static List<PipelineManager> FindPipelineManagers(bool onlyActiveScene)
	{
		var activeScene = SceneManager.GetActiveScene();
		return Resources.FindObjectsOfTypeAll<PipelineManager>()
			.Where(pm => pm != null)
			.Where(pm => pm.gameObject != null)
			.Where(pm => pm.gameObject.scene.IsValid())
			.Where(pm => !EditorUtility.IsPersistent(pm))
			.Where(pm => !onlyActiveScene || pm.gameObject.scene == activeScene)
			.OrderBy(pm => BuildPath(pm.transform), StringComparer.Ordinal)
			.ToList();
	}

	private static bool TrySetWorldId(PipelineManager target, string worldId, out string errorMessage)
	{
		errorMessage = string.Empty;
		if (target == null)
		{
			errorMessage = "Pipeline Manager reference is null.";
			return false;
		}

		Undo.RecordObject(target, "Set VRChat World ID");

		var serialized = new SerializedObject(target);
		var blueprintProperty = serialized.FindProperty("blueprintId")
			?? serialized.FindProperty("_blueprintId")
			?? serialized.FindProperty("contentId")
			?? serialized.FindProperty("pipelineId");

		if (blueprintProperty != null && blueprintProperty.propertyType == SerializedPropertyType.String)
		{
			blueprintProperty.stringValue = worldId;
			serialized.ApplyModifiedProperties();
			EditorUtility.SetDirty(target);
			PrefabUtility.RecordPrefabInstancePropertyModifications(target);
			return true;
		}

		var targetType = target.GetType();
		var field = targetType.GetField("blueprintId")
			?? targetType.GetField("_blueprintId")
			?? targetType.GetField("contentId")
			?? targetType.GetField("pipelineId");

		if (field != null && field.FieldType == typeof(string))
		{
			field.SetValue(target, worldId);
			EditorUtility.SetDirty(target);
			PrefabUtility.RecordPrefabInstancePropertyModifications(target);
			return true;
		}

		var property = targetType.GetProperty("blueprintId")
			?? targetType.GetProperty("contentId")
			?? targetType.GetProperty("pipelineId");

		if (property != null && property.PropertyType == typeof(string) && property.CanWrite)
		{
			property.SetValue(target, worldId, null);
			EditorUtility.SetDirty(target);
			PrefabUtility.RecordPrefabInstancePropertyModifications(target);
			return true;
		}

		errorMessage =
			"Could not find a writable world ID field on the Pipeline Manager.\n" +
			"Expected one of: blueprintId, _blueprintId, contentId, pipelineId.";
		return false;
	}

	private static string BuildPath(Transform transform)
	{
		if (transform == null)
		{
			return "<null>";
		}

		var parts = new List<string>();
		while (transform != null)
		{
			parts.Add(transform.name);
			transform = transform.parent;
		}

		parts.Reverse();
		return string.Join("/", parts);
	}
}
