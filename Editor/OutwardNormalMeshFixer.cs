using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

public static class OutwardNormalMeshFixer
{
	private const string MenuPath = "Tools/Meshes/Inspect And Fix Outward Normals";
	private const string OutputRoot = "Assets/1574/1574/Generated";
	private const string OutputLeaf = "NormalFixedMeshes";

	[MenuItem(MenuPath)]
	private static void InspectAndFixOutwardNormals()
	{
		var selection = Selection.objects;
		if (selection == null || selection.Length == 0)
		{
			EditorUtility.DisplayDialog("Fix Outward Normals", "Select one or more GameObjects or Mesh assets first.", "OK");
			return;
		}

		var outputFolder = EnsureOutputFolder();
		var processedBySource = new Dictionary<Mesh, Mesh>();
		var reports = new List<MeshFixReport>();
		var assignedRenderers = 0;

		try
		{
			var selectedGameObjects = selection.OfType<GameObject>().ToArray();
			var selectedMeshes = selection.OfType<Mesh>().ToArray();

			for (var index = 0; index < selectedMeshes.Length; index++)
			{
				var mesh = selectedMeshes[index];
				EditorUtility.DisplayProgressBar("Fix Outward Normals", "Inspecting selected Mesh assets", (float)(index + 1) / Math.Max(1, selectedMeshes.Length));
				GetOrCreateProcessedMesh(mesh, outputFolder, processedBySource, reports);
			}

			foreach (var root in selectedGameObjects)
			{
				if (root == null)
				{
					continue;
				}

				foreach (var meshFilter in root.GetComponentsInChildren<MeshFilter>(true))
				{
					if (meshFilter == null || meshFilter.sharedMesh == null)
					{
						continue;
					}

					var fixedMesh = GetOrCreateProcessedMesh(meshFilter.sharedMesh, outputFolder, processedBySource, reports);
					if (fixedMesh == null)
					{
						continue;
					}

					Undo.RecordObject(meshFilter, "Assign Fixed Outward Normals Mesh");
					meshFilter.sharedMesh = fixedMesh;
					EditorUtility.SetDirty(meshFilter);
					assignedRenderers++;
				}

				foreach (var skinnedMeshRenderer in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
				{
					if (skinnedMeshRenderer == null || skinnedMeshRenderer.sharedMesh == null)
					{
						continue;
					}

					var fixedMesh = GetOrCreateProcessedMesh(skinnedMeshRenderer.sharedMesh, outputFolder, processedBySource, reports);
					if (fixedMesh == null)
					{
						continue;
					}

					Undo.RecordObject(skinnedMeshRenderer, "Assign Fixed Outward Normals Mesh");
					skinnedMeshRenderer.sharedMesh = fixedMesh;
					EditorUtility.SetDirty(skinnedMeshRenderer);
					assignedRenderers++;
				}
			}

			AssetDatabase.SaveAssets();
			AssetDatabase.Refresh();
		}
		finally
		{
			EditorUtility.ClearProgressBar();
		}

		var flippedCount = reports.Count(report => report.FlippedOrientation);
		var summary =
			$"Processed {reports.Count} mesh asset(s).\n" +
			$"Flipped orientation on {flippedCount} mesh asset(s).\n" +
			$"Reassigned {assignedRenderers} renderer component(s).\n\n" +
			$"Output folder:\n{outputFolder}";

		EditorUtility.DisplayDialog("Fix Outward Normals", summary, "OK");

		foreach (var report in reports)
		{
			Debug.Log(
				$"[OutwardNormalMeshFixer] {report.SourcePath} -> {report.OutputPath} | Before avg dot: {report.BeforeAverageDot:F4}, After avg dot: {report.AfterAverageDot:F4}, Flipped: {report.FlippedOrientation}");
		}
	}

	[MenuItem(MenuPath, true)]
	private static bool ValidateInspectAndFixOutwardNormals()
	{
		return Selection.objects != null && Selection.objects.Length > 0;
	}

	private static Mesh GetOrCreateProcessedMesh(
		Mesh sourceMesh,
		string outputFolder,
		Dictionary<Mesh, Mesh> processedBySource,
		List<MeshFixReport> reports)
	{
		if (sourceMesh == null)
		{
			return null;
		}

		if (processedBySource.TryGetValue(sourceMesh, out var existing))
		{
			return existing;
		}

		var clone = UnityEngine.Object.Instantiate(sourceMesh);
		clone.name = sourceMesh.name + "_OutwardNormals";

		var before = EvaluateOrientation(clone);
		clone.RecalculateNormals();

		var postRecalculate = EvaluateOrientation(clone);
		var shouldFlip = postRecalculate.AverageDot < 0f;
		if (shouldFlip)
		{
			FlipNormalsAndWinding(clone);
		}

		TryRecalculateTangents(clone);
		clone.RecalculateBounds();

		var after = EvaluateOrientation(clone);
		var outputPath = CreateMeshAsset(clone, sourceMesh, outputFolder);

		var report = new MeshFixReport(
			GetMeshPathOrName(sourceMesh),
			outputPath,
			before.AverageDot,
			after.AverageDot,
			shouldFlip);

		reports.Add(report);
		processedBySource.Add(sourceMesh, clone);
		return clone;
	}

	private static OrientationStats EvaluateOrientation(Mesh mesh)
	{
		var vertices = mesh.vertices;
		var normals = mesh.normals;
		if (vertices == null || normals == null || vertices.Length == 0 || normals.Length != vertices.Length)
		{
			return default;
		}

		var center = mesh.bounds.center;
		var sumDot = 0f;
		var validCount = 0;

		for (var i = 0; i < vertices.Length; i++)
		{
			var toVertex = vertices[i] - center;
			var toVertexSqr = toVertex.sqrMagnitude;
			if (toVertexSqr <= 1e-8f)
			{
				continue;
			}

			var normal = normals[i];
			var normalSqr = normal.sqrMagnitude;
			if (normalSqr <= 1e-8f)
			{
				continue;
			}

			var dot = Vector3.Dot(normal.normalized, toVertex.normalized);
			sumDot += dot;
			validCount++;
		}

		return validCount == 0 ? default : new OrientationStats(sumDot / validCount, validCount);
	}

	private static void FlipNormalsAndWinding(Mesh mesh)
	{
		var normals = mesh.normals;
		if (normals != null && normals.Length > 0)
		{
			for (var i = 0; i < normals.Length; i++)
			{
				normals[i] = -normals[i];
			}

			mesh.normals = normals;
		}

		for (var subMesh = 0; subMesh < mesh.subMeshCount; subMesh++)
		{
			var triangles = mesh.GetTriangles(subMesh);
			for (var i = 0; i < triangles.Length; i += 3)
			{
				var temp = triangles[i];
				triangles[i] = triangles[i + 1];
				triangles[i + 1] = temp;
			}

			mesh.SetTriangles(triangles, subMesh);
		}
	}

	private static void TryRecalculateTangents(Mesh mesh)
	{
		try
		{
			mesh.RecalculateTangents();
		}
		catch (Exception)
		{
			// Some meshes do not provide enough data for tangent reconstruction.
		}
	}

	private static string CreateMeshAsset(Mesh mesh, Mesh sourceMesh, string outputFolder)
	{
		var sourceName = string.IsNullOrWhiteSpace(sourceMesh.name) ? "Mesh" : sourceMesh.name;
		var fileName = sourceName + "_OutwardNormals.asset";
		var path = AssetDatabase.GenerateUniqueAssetPath(Path.Combine(outputFolder, fileName).Replace('\\', '/'));
		AssetDatabase.CreateAsset(mesh, path);
		return path;
	}

	private static string EnsureOutputFolder()
	{
		EnsureFolder("Assets", "1574");
		EnsureFolder("Assets/1574", "1574");
		EnsureFolder("Assets/1574/1574", "Generated");
		EnsureFolder(OutputRoot, OutputLeaf);
		return OutputRoot + "/" + OutputLeaf;
	}

	private static void EnsureFolder(string parent, string child)
	{
		var full = parent + "/" + child;
		if (!AssetDatabase.IsValidFolder(full))
		{
			AssetDatabase.CreateFolder(parent, child);
		}
	}

	private static string GetMeshPathOrName(Mesh mesh)
	{
		var path = AssetDatabase.GetAssetPath(mesh);
		return string.IsNullOrWhiteSpace(path) ? mesh.name : path;
	}

	private readonly struct OrientationStats
	{
		public OrientationStats(float averageDot, int sampledVertexCount)
		{
			AverageDot = averageDot;
			SampledVertexCount = sampledVertexCount;
		}

		public float AverageDot { get; }
		public int SampledVertexCount { get; }
	}

	private readonly struct MeshFixReport
	{
		public MeshFixReport(string sourcePath, string outputPath, float beforeAverageDot, float afterAverageDot, bool flippedOrientation)
		{
			SourcePath = sourcePath;
			OutputPath = outputPath;
			BeforeAverageDot = beforeAverageDot;
			AfterAverageDot = afterAverageDot;
			FlippedOrientation = flippedOrientation;
		}

		public string SourcePath { get; }
		public string OutputPath { get; }
		public float BeforeAverageDot { get; }
		public float AfterAverageDot { get; }
		public bool FlippedOrientation { get; }
	}
}