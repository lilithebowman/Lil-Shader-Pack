using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

public class SceneMemoryAnalyzerWindow : EditorWindow
{
	private Vector2 scrollPosition;
	private string resultsText = "";
	private List<MemoryAnalysisResult> results;

	public static void ShowWindow(List<MemoryAnalysisResult> analysisResults)
	{
		var window = GetWindow<SceneMemoryAnalyzerWindow>("Scene Memory Analysis");
		window.results = analysisResults;
		window.GenerateResultsText();
		window.Show();
	}

	private void GenerateResultsText()
	{
		if (results == null || results.Count == 0)
		{
			resultsText = "No memory-consuming assets found in scene.";
			return;
		}

		var sb = new StringBuilder();
		sb.AppendLine("SCENE MEMORY ANALYSIS RESULTS");
		sb.AppendLine("Generated: " + System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
		sb.AppendLine();
		sb.AppendLine("Size (MB)\tType\t\tAsset Path\t\tDetails");
		sb.AppendLine(new string('=', 120));

		long totalRAM = 0;
		foreach (var result in results)
		{
			totalRAM += result.EstimatedRAMBytes;
			sb.AppendLine($"{result.EstimatedRAMBytes / (1024f * 1024f):F2}\t\t{result.AssetType}\t\t{result.AssetPath}\t\t{result.Details}");
		}

		sb.AppendLine(new string('=', 120));
		sb.AppendLine($"TOTAL ESTIMATED RAM: {totalRAM / (1024f * 1024f):F2} MB");
		sb.AppendLine($"Assets analyzed: {results.Count}");
		sb.AppendLine();
		sb.AppendLine("LARGEST ASSETS:");

		for (int i = 0; i < Mathf.Min(10, results.Count); i++)
		{
			var result = results[i];
			sb.AppendLine($"{i + 1}. {result.EstimatedRAMBytes / (1024f * 1024f):F2} MB - {System.IO.Path.GetFileName(result.AssetPath)} ({result.AssetType})");
		}

		resultsText = sb.ToString();
	}

	private void OnGUI()
	{
		EditorGUILayout.BeginVertical();

		// Header
		EditorGUILayout.LabelField("Scene Memory Analysis Results", EditorStyles.boldLabel);
		EditorGUILayout.Space();

		// Controls
		EditorGUILayout.BeginHorizontal();
		if (GUILayout.Button("Copy to Clipboard", GUILayout.Width(150)))
		{
			EditorGUIUtility.systemCopyBuffer = resultsText;
			Debug.Log("[SceneMemoryAnalyzer] Results copied to clipboard!");
		}

		if (GUILayout.Button("Refresh Analysis", GUILayout.Width(150)))
		{
			SceneMemoryAnalyzer.AnalyzeSceneMemory();
		}

		GUILayout.FlexibleSpace();

		if (results != null)
		{
			EditorGUILayout.LabelField($"Total Assets: {results.Count}", GUILayout.Width(100));
			long totalRAM = 0;
			foreach (var result in results)
				totalRAM += result.EstimatedRAMBytes;
			EditorGUILayout.LabelField($"Total RAM: {totalRAM / (1024f * 1024f):F1} MB", GUILayout.Width(120));
		}

		EditorGUILayout.EndHorizontal();

		EditorGUILayout.Space();

		// Results text area
		scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);

		GUIStyle textAreaStyle = new GUIStyle(EditorStyles.textArea)
		{
			font = EditorStyles.miniFont,
			fontSize = 10,
			wordWrap = false
		};

		string newText = EditorGUILayout.TextArea(resultsText, textAreaStyle, GUILayout.ExpandHeight(true));

		// Allow manual editing of the text area (useful for copying specific parts)
		if (newText != resultsText)
		{
			resultsText = newText;
		}

		EditorGUILayout.EndScrollView();

		// Bottom info
		EditorGUILayout.Space();
		EditorGUILayout.BeginHorizontal();
		EditorGUILayout.LabelField("Tip: Select text above and Ctrl+C to copy, or use 'Copy to Clipboard' button.", EditorStyles.miniLabel);
		EditorGUILayout.EndHorizontal();

		EditorGUILayout.EndVertical();
	}

	private void OnInspectorUpdate()
	{
		// Refresh the window periodically
		Repaint();
	}
}