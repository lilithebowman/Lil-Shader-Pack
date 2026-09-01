using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.Linq;

namespace CozyCon.Tools
{
	/// <summary>
	/// Results window for VRChat Mobile Shader Analyzer
	/// </summary>
	public class VRChatShaderAnalysisWindow : EditorWindow
	{
		private List<VRChatMobileShaderAnalyzer.MaterialAnalysis> materialAnalyses;
		private Vector2 scrollPosition;
		private int selectedTab = 0;
		private readonly string[] tabNames = { "Overview", "Critical Issues", "All Materials", "Statistics" };

		public static void ShowWindow(List<VRChatMobileShaderAnalyzer.MaterialAnalysis> analyses)
		{
			var window = GetWindow<VRChatShaderAnalysisWindow>();
			window.titleContent = new GUIContent("VRChat Shader Analysis Results");
			window.materialAnalyses = analyses;
			window.Show();
		}

		void OnGUI()
		{
			if (materialAnalyses == null || materialAnalyses.Count == 0)
			{
				EditorGUILayout.HelpBox("No analysis results available. Run the VRChat Mobile Shader Analyzer first.", MessageType.Info);
				return;
			}

			selectedTab = GUILayout.Toolbar(selectedTab, tabNames);
			EditorGUILayout.Space();

			scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);

			switch (selectedTab)
			{
				case 0: DrawOverviewTab(); break;
				case 1: DrawCriticalIssuesTab(); break;
				case 2: DrawAllMaterialsTab(); break;
				case 3: DrawStatisticsTab(); break;
			}

			EditorGUILayout.EndScrollView();
		}

		private void DrawOverviewTab()
		{
			GUILayout.Label("VRChat Mobile Shader Analysis Overview", EditorStyles.boldLabel);
			EditorGUILayout.Space();

			var stats = CalculateStatistics();

			// Summary cards
			DrawSummaryCard("Total Materials", stats.totalMaterials.ToString(), Color.cyan);
			DrawSummaryCard("Fully Compatible", stats.fullyCompatible.ToString(), Color.green);
			DrawSummaryCard("Need Replacement", stats.needReplacement.ToString(), Color.yellow);
			DrawSummaryCard("Critical Issues", stats.criticalIssues.ToString(), Color.red);

			EditorGUILayout.Space();

			// Quick actions
			GUILayout.Label("Quick Actions", EditorStyles.boldLabel);

			if (stats.safeToReplace > 0)
			{
				EditorGUILayout.HelpBox($"{stats.safeToReplace} materials can be safely auto-replaced", MessageType.Info);
				if (GUILayout.Button($"Replace {stats.safeToReplace} Safe Materials"))
				{
					// Call back to main analyzer
					var analyzer = EditorWindow.GetWindow<VRChatMobileShaderAnalyzer>();
					// Note: This would need a public method in the analyzer
				}
			}

			if (stats.criticalIssues > 0)
			{
				EditorGUILayout.HelpBox($"{stats.criticalIssues} materials have critical compatibility issues", MessageType.Warning);
			}

			if (stats.needsManualReview > 0)
			{
				EditorGUILayout.HelpBox($"{stats.needsManualReview} materials need manual review", MessageType.Info);
			}
		}

		private void DrawCriticalIssuesTab()
		{
			var criticalMaterials = materialAnalyses.Where(m =>
				m.compatibility == VRChatMobileShaderAnalyzer.CompatibilityLevel.Incompatible ||
				m.warnings.Any(w => w.Contains("invisible") || w.Contains("transparency"))).ToList();

			GUILayout.Label($"Critical Issues ({criticalMaterials.Count})", EditorStyles.boldLabel);
			EditorGUILayout.Space();

			if (criticalMaterials.Count == 0)
			{
				EditorGUILayout.HelpBox("✅ No critical issues found! All materials should work well on VRChat Quest.", MessageType.Info);
				return;
			}

			foreach (var analysis in criticalMaterials)
			{
				DrawCriticalIssueCard(analysis);
			}
		}

		private void DrawAllMaterialsTab()
		{
			GUILayout.Label($"All Materials ({materialAnalyses.Count})", EditorStyles.boldLabel);
			EditorGUILayout.Space();

			foreach (var analysis in materialAnalyses)
			{
				DrawMaterialCard(analysis);
			}
		}

		private void DrawStatisticsTab()
		{
			GUILayout.Label("Detailed Statistics", EditorStyles.boldLabel);
			EditorGUILayout.Space();

			var stats = CalculateStatistics();

			// Compatibility breakdown
			DrawStatisticSection("Compatibility Levels", new Dictionary<string, int>
			{
				{ "Fully Compatible", stats.fullyCompatible },
				{ "Highly Compatible", stats.highlyCompatible },
				{ "Partially Compatible", stats.partiallyCompatible },
				{ "Incompatible", stats.incompatible },
				{ "Unknown", stats.unknown }
			});

			EditorGUILayout.Space();

			// Shader usage
			var shaderUsage = materialAnalyses.GroupBy(m => m.currentShader)
				.OrderByDescending(g => g.Count())
				.Take(10)
				.ToDictionary(g => g.Key, g => g.Count());

			DrawStatisticSection("Most Used Shaders", shaderUsage);

			EditorGUILayout.Space();

			// Feature analysis
			var featureLoss = new Dictionary<string, int>();
			foreach (var analysis in materialAnalyses)
			{
				foreach (var feature in analysis.unsupportedFeatures)
				{
					if (!featureLoss.ContainsKey(feature))
						featureLoss[feature] = 0;
					featureLoss[feature]++;
				}
			}

			if (featureLoss.Count > 0)
			{
				DrawStatisticSection("Features That Will Be Lost", featureLoss.OrderByDescending(kvp => kvp.Value).ToDictionary(kvp => kvp.Key, kvp => kvp.Value));
			}
		}

		private void DrawSummaryCard(string title, string value, Color color)
		{
			var originalColor = GUI.backgroundColor;
			GUI.backgroundColor = color;

			EditorGUILayout.BeginVertical("box");
			EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
			EditorGUILayout.LabelField(value, EditorStyles.largeLabel);
			EditorGUILayout.EndVertical();

			GUI.backgroundColor = originalColor;
			EditorGUILayout.Space();
		}

		private void DrawCriticalIssueCard(VRChatMobileShaderAnalyzer.MaterialAnalysis analysis)
		{
			var originalColor = GUI.backgroundColor;
			GUI.backgroundColor = new Color(1f, 0.8f, 0.8f); // Light red

			EditorGUILayout.BeginVertical("box");

			EditorGUILayout.LabelField($"🔴 {analysis.material.name}", EditorStyles.boldLabel);
			EditorGUILayout.LabelField($"Shader: {analysis.currentShader}");

			EditorGUILayout.LabelField("Issues:", EditorStyles.boldLabel);
			foreach (var warning in analysis.warnings)
			{
				EditorGUILayout.LabelField($"• {warning}", EditorStyles.wordWrappedLabel);
			}

			EditorGUILayout.BeginHorizontal();
			if (GUILayout.Button("Select Material"))
			{
				Selection.activeObject = analysis.material;
				EditorGUIUtility.PingObject(analysis.material);
			}

			if (analysis.usedByObjects.Count > 0 && GUILayout.Button("Select Objects"))
			{
				Selection.objects = analysis.usedByObjects.ToArray();
			}
			EditorGUILayout.EndHorizontal();

			EditorGUILayout.EndVertical();
			GUI.backgroundColor = originalColor;
			EditorGUILayout.Space();
		}

		private void DrawMaterialCard(VRChatMobileShaderAnalyzer.MaterialAnalysis analysis)
		{
			Color cardColor;
			switch (analysis.compatibility)
			{
				case VRChatMobileShaderAnalyzer.CompatibilityLevel.FullyCompatible:
					cardColor = new Color(0.8f, 1f, 0.8f); // Light green
					break;
				case VRChatMobileShaderAnalyzer.CompatibilityLevel.HighlyCompatible:
					cardColor = new Color(1f, 1f, 0.8f); // Light yellow
					break;
				case VRChatMobileShaderAnalyzer.CompatibilityLevel.PartiallyCompatible:
					cardColor = new Color(1f, 0.9f, 0.8f); // Light orange
					break;
				default:
					cardColor = new Color(1f, 0.8f, 0.8f); // Light red
					break;
			}

			var originalColor = GUI.backgroundColor;
			GUI.backgroundColor = cardColor;

			EditorGUILayout.BeginVertical("box");

			EditorGUILayout.BeginHorizontal();
			EditorGUILayout.LabelField(GetCompatibilityIcon(analysis.compatibility) + " " + analysis.material.name, EditorStyles.boldLabel);
			EditorGUILayout.LabelField($"({analysis.compatibility})", GUILayout.Width(120));
			EditorGUILayout.EndHorizontal();

			EditorGUILayout.LabelField($"Current: {analysis.currentShader}");
			if (analysis.currentShader != analysis.recommendedShader)
			{
				EditorGUILayout.LabelField($"→ Recommended: {analysis.recommendedShader}");
			}

			if (analysis.usedByObjects.Count > 0)
			{
				EditorGUILayout.LabelField($"Used by: {string.Join(", ", analysis.usedByObjects.Take(3).Select(o => o.name))}");
				if (analysis.usedByObjects.Count > 3)
				{
					EditorGUILayout.LabelField($"...and {analysis.usedByObjects.Count - 3} more objects");
				}
			}

			EditorGUILayout.EndVertical();
			GUI.backgroundColor = originalColor;
		}

		private void DrawStatisticSection(string title, Dictionary<string, int> data)
		{
			EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
			EditorGUI.indentLevel++;

			foreach (var kvp in data)
			{
				EditorGUILayout.BeginHorizontal();
				EditorGUILayout.LabelField(kvp.Key, GUILayout.Width(200));
				EditorGUILayout.LabelField(kvp.Value.ToString(), EditorStyles.boldLabel);
				EditorGUILayout.EndHorizontal();
			}

			EditorGUI.indentLevel--;
		}

		private string GetCompatibilityIcon(VRChatMobileShaderAnalyzer.CompatibilityLevel level)
		{
			switch (level)
			{
				case VRChatMobileShaderAnalyzer.CompatibilityLevel.FullyCompatible: return "✅";
				case VRChatMobileShaderAnalyzer.CompatibilityLevel.HighlyCompatible: return "🟡";
				case VRChatMobileShaderAnalyzer.CompatibilityLevel.PartiallyCompatible: return "🟠";
				case VRChatMobileShaderAnalyzer.CompatibilityLevel.Incompatible: return "🔴";
				default: return "❓";
			}
		}

		private AnalysisStatistics CalculateStatistics()
		{
			var stats = new AnalysisStatistics();
			stats.totalMaterials = materialAnalyses.Count;

			foreach (var analysis in materialAnalyses)
			{
				switch (analysis.compatibility)
				{
					case VRChatMobileShaderAnalyzer.CompatibilityLevel.FullyCompatible:
						stats.fullyCompatible++;
						break;
					case VRChatMobileShaderAnalyzer.CompatibilityLevel.HighlyCompatible:
						stats.highlyCompatible++;
						stats.needReplacement++;
						if (analysis.canReplaceAutomatically)
							stats.safeToReplace++;
						else
							stats.needsManualReview++;
						break;
					case VRChatMobileShaderAnalyzer.CompatibilityLevel.PartiallyCompatible:
						stats.partiallyCompatible++;
						stats.needReplacement++;
						stats.needsManualReview++;
						break;
					case VRChatMobileShaderAnalyzer.CompatibilityLevel.Incompatible:
						stats.incompatible++;
						stats.criticalIssues++;
						break;
					default:
						stats.unknown++;
						stats.needsManualReview++;
						break;
				}
			}

			return stats;
		}

		private struct AnalysisStatistics
		{
			public int totalMaterials;
			public int fullyCompatible;
			public int highlyCompatible;
			public int partiallyCompatible;
			public int incompatible;
			public int unknown;
			public int needReplacement;
			public int safeToReplace;
			public int criticalIssues;
			public int needsManualReview;
		}
	}
}