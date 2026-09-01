using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

public class Quest2CompatibilityWindow : EditorWindow
{
	private Vector2 scrollPosition;
	private string resultsText = "";
	private List<Quest2Issue> issues;
	private List<Quest2Issue> warnings;
	private List<Quest2Issue> info;
	private int selectedTab = 0;
	private string[] tabNames = { "Summary", "Critical Issues", "Warnings", "Information", "Full Report" };

	public static void ShowWindow(List<Quest2Issue> issues, List<Quest2Issue> warnings, List<Quest2Issue> info)
	{
		var window = GetWindow<Quest2CompatibilityWindow>("Quest 2 Compatibility");
		window.issues = issues ?? new List<Quest2Issue>();
		window.warnings = warnings ?? new List<Quest2Issue>();
		window.info = info ?? new List<Quest2Issue>();
		window.GenerateResultsText();
		window.Show();
	}

	private void GenerateResultsText()
	{
		var sb = new StringBuilder();
		sb.AppendLine("QUEST 2 COMPATIBILITY ANALYSIS");
		sb.AppendLine("Generated: " + System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
		sb.AppendLine();

		// Summary
		sb.AppendLine("=== SUMMARY ===");
		sb.AppendLine($"Critical Issues: {issues.Count}");
		sb.AppendLine($"Warnings: {warnings.Count}");
		sb.AppendLine($"Information: {info.Count}");
		sb.AppendLine();

		if (issues.Count == 0 && warnings.Count == 0)
		{
			sb.AppendLine("✅ Your world appears to be Quest 2 compatible!");
			sb.AppendLine("No critical issues or warnings detected.");
		}
		else if (issues.Count > 0)
		{
			sb.AppendLine("❌ Critical issues found that may prevent Quest 2 compatibility.");
			sb.AppendLine("Address these issues before uploading to VRChat.");
		}
		else
		{
			sb.AppendLine("⚠️ Some warnings detected. Your world may work on Quest 2 but performance could be impacted.");
		}
		sb.AppendLine();

		// Critical Issues
		if (issues.Count > 0)
		{
			sb.AppendLine("=== CRITICAL ISSUES ===");
			foreach (var issue in issues)
			{
				sb.AppendLine($"❌ [{issue.Category}] {issue.Title}");
				sb.AppendLine($"   Problem: {issue.Description}");
				sb.AppendLine($"   Solution: {issue.Recommendation}");
				sb.AppendLine();
			}
		}

		// Warnings
		if (warnings.Count > 0)
		{
			sb.AppendLine("=== WARNINGS ===");
			foreach (var warning in warnings)
			{
				sb.AppendLine($"⚠️ [{warning.Category}] {warning.Title}");
				sb.AppendLine($"   Issue: {warning.Description}");
				sb.AppendLine($"   Recommendation: {warning.Recommendation}");
				sb.AppendLine();
			}
		}

		// Information
		if (info.Count > 0)
		{
			sb.AppendLine("=== INFORMATION ===");
			foreach (var infoItem in info)
			{
				sb.AppendLine($"ℹ️ [{infoItem.Category}] {infoItem.Title}");
				sb.AppendLine($"   Details: {infoItem.Description}");
				sb.AppendLine($"   Tip: {infoItem.Recommendation}");
				sb.AppendLine();
			}
		}

		// Footer
		sb.AppendLine("=== NEXT STEPS ===");
		if (issues.Count > 0)
		{
			sb.AppendLine("1. Fix all critical issues first");
			sb.AppendLine("2. Address warnings for better performance");
			sb.AppendLine("3. Test on Quest 2 device if available");
		}
		else if (warnings.Count > 0)
		{
			sb.AppendLine("1. Consider addressing warnings for optimal performance");
			sb.AppendLine("2. Use VRChat's build size analyzer");
			sb.AppendLine("3. Test on Quest 2 device if available");
		}
		else
		{
			sb.AppendLine("1. Your world looks good for Quest 2!");
			sb.AppendLine("2. Final test: Upload and verify on Quest device");
			sb.AppendLine("3. Monitor user feedback for performance issues");
		}

		resultsText = sb.ToString();
	}

	private void OnGUI()
	{
		EditorGUILayout.BeginVertical();

		// Header
		EditorGUILayout.LabelField("Quest 2 Compatibility Analysis", EditorStyles.boldLabel);
		EditorGUILayout.Space();

		// Status indicator
		DrawStatusIndicator();

		EditorGUILayout.Space();

		// Controls
		EditorGUILayout.BeginHorizontal();
		if (GUILayout.Button("Copy Full Report", GUILayout.Width(120)))
		{
			EditorGUIUtility.systemCopyBuffer = resultsText;
			Debug.Log("[Quest2Analyzer] Full report copied to clipboard!");
		}

		if (GUILayout.Button("Refresh Analysis", GUILayout.Width(120)))
		{
			Quest2CompatibilityAnalyzer.AnalyzeQuest2Compatibility();
		}

		if (GUILayout.Button("Optimize Textures", GUILayout.Width(120)))
		{
			if (EditorUtility.DisplayDialog("Run Texture Optimizer",
				"This will run the Mobile Texture Optimizer to help fix Quest compatibility issues. Continue?",
				"Yes", "Cancel"))
			{
				OptimizeMobileTextures.Run();
			}
		}

		GUILayout.FlexibleSpace();
		EditorGUILayout.EndHorizontal();

		EditorGUILayout.Space();

		// Tab selection
		selectedTab = GUILayout.Toolbar(selectedTab, tabNames);

		EditorGUILayout.Space();

		// Content area
		scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);

		switch (selectedTab)
		{
			case 0: DrawSummaryTab(); break;
			case 1: DrawIssuesTab(issues, "Critical Issues", "❌"); break;
			case 2: DrawIssuesTab(warnings, "Warnings", "⚠️"); break;
			case 3: DrawIssuesTab(info, "Information", "ℹ️"); break;
			case 4: DrawFullReportTab(); break;
		}

		EditorGUILayout.EndScrollView();

		EditorGUILayout.EndVertical();
	}

	private void DrawStatusIndicator()
	{
		EditorGUILayout.BeginHorizontal();

		// Status color box
		Color statusColor = issues.Count > 0 ? Color.red : (warnings.Count > 0 ? Color.yellow : Color.green);
		string statusText = issues.Count > 0 ? "CRITICAL ISSUES" : (warnings.Count > 0 ? "WARNINGS" : "COMPATIBLE");

		var originalColor = GUI.backgroundColor;
		GUI.backgroundColor = statusColor;
		GUILayout.Box("", GUILayout.Width(20), GUILayout.Height(20));
		GUI.backgroundColor = originalColor;

		GUILayout.Space(5);
		EditorGUILayout.LabelField($"Status: {statusText}", EditorStyles.boldLabel);

		GUILayout.FlexibleSpace();

		EditorGUILayout.LabelField($"Issues: {issues.Count}", GUILayout.Width(70));
		EditorGUILayout.LabelField($"Warnings: {warnings.Count}", GUILayout.Width(80));
		EditorGUILayout.LabelField($"Info: {info.Count}", GUILayout.Width(60));

		EditorGUILayout.EndHorizontal();
	}

	private void DrawSummaryTab()
	{
		EditorGUILayout.LabelField("Compatibility Summary", EditorStyles.boldLabel);
		EditorGUILayout.Space();

		if (issues.Count == 0 && warnings.Count == 0)
		{
			EditorGUILayout.HelpBox("✅ Your world appears to be Quest 2 compatible! No critical issues detected.", MessageType.Info);
		}
		else if (issues.Count > 0)
		{
			EditorGUILayout.HelpBox($"❌ {issues.Count} critical issue(s) found that may prevent Quest 2 compatibility.", MessageType.Error);
		}
		else
		{
			EditorGUILayout.HelpBox($"⚠️ {warnings.Count} warning(s) detected. Performance may be impacted on Quest 2.", MessageType.Warning);
		}

		EditorGUILayout.Space();

		// Quick stats
		EditorGUILayout.LabelField("Issue Breakdown by Category:", EditorStyles.boldLabel);

		var categories = new Dictionary<string, (int issues, int warnings)>();

		foreach (var issue in issues)
		{
			if (!categories.ContainsKey(issue.Category))
				categories[issue.Category] = (0, 0);
			categories[issue.Category] = (categories[issue.Category].issues + 1, categories[issue.Category].warnings);
		}

		foreach (var warning in warnings)
		{
			if (!categories.ContainsKey(warning.Category))
				categories[warning.Category] = (0, 0);
			categories[warning.Category] = (categories[warning.Category].issues, categories[warning.Category].warnings + 1);
		}

		foreach (var kvp in categories)
		{
			EditorGUILayout.BeginHorizontal();
			EditorGUILayout.LabelField($"  {kvp.Key}:", GUILayout.Width(150));
			if (kvp.Value.issues > 0)
				EditorGUILayout.LabelField($"{kvp.Value.issues} critical", GUILayout.Width(80));
			if (kvp.Value.warnings > 0)
				EditorGUILayout.LabelField($"{kvp.Value.warnings} warnings", GUILayout.Width(80));
			EditorGUILayout.EndHorizontal();
		}

		EditorGUILayout.Space();

		// Next steps
		EditorGUILayout.LabelField("Next Steps:", EditorStyles.boldLabel);
		if (issues.Count > 0)
		{
			EditorGUILayout.LabelField("• Fix critical issues first (see Critical Issues tab)");
			EditorGUILayout.LabelField("• Use 'Optimize Textures' button above for texture issues");
			EditorGUILayout.LabelField("• Address warnings for better performance");
		}
		else if (warnings.Count > 0)
		{
			EditorGUILayout.LabelField("• Consider addressing warnings for optimal performance");
			EditorGUILayout.LabelField("• Test on actual Quest 2 device if available");
		}
		else
		{
			EditorGUILayout.LabelField("• Your world looks ready for Quest 2!");
			EditorGUILayout.LabelField("• Consider final testing on Quest device");
		}
	}

	private void DrawIssuesTab(List<Quest2Issue> issueList, string title, string icon)
	{
		EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
		EditorGUILayout.Space();

		if (issueList.Count == 0)
		{
			EditorGUILayout.HelpBox($"No {title.ToLower()} found! ✅", MessageType.Info);
			return;
		}

		foreach (var issue in issueList)
		{
			EditorGUILayout.BeginVertical("box");

			EditorGUILayout.LabelField($"{icon} [{issue.Category}] {issue.Title}", EditorStyles.boldLabel);
			EditorGUILayout.Space(2);

			EditorGUILayout.LabelField("Problem:", EditorStyles.miniLabel);
			EditorGUILayout.LabelField(issue.Description, EditorStyles.wordWrappedLabel);
			EditorGUILayout.Space(2);

			EditorGUILayout.LabelField("Solution:", EditorStyles.miniLabel);
			EditorGUILayout.LabelField(issue.Recommendation, EditorStyles.wordWrappedLabel);

			EditorGUILayout.EndVertical();
			EditorGUILayout.Space();
		}
	}

	private void DrawFullReportTab()
	{
		EditorGUILayout.LabelField("Full Report (Copy/Paste Ready)", EditorStyles.boldLabel);
		EditorGUILayout.Space();

		GUIStyle textAreaStyle = new GUIStyle(EditorStyles.textArea)
		{
			font = EditorStyles.miniFont,
			fontSize = 10,
			wordWrap = true
		};

		string newText = EditorGUILayout.TextArea(resultsText, textAreaStyle, GUILayout.ExpandHeight(true));

		// Allow manual editing
		if (newText != resultsText)
		{
			resultsText = newText;
		}

		EditorGUILayout.Space();
		EditorGUILayout.LabelField("Tip: Select text above and Ctrl+C to copy, or use 'Copy Full Report' button.", EditorStyles.miniLabel);
	}

	private void OnInspectorUpdate()
	{
		Repaint();
	}
}