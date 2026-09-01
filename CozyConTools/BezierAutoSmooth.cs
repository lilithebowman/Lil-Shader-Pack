using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Computes smooth Bezier handle offsets from anchor points.
/// </summary>
public static class BezierAutoSmooth
{
	const float kEpsilon = 1e-6f;

	public static void Apply(IReadOnlyList<Vector3> points, List<Vector3> handleA, List<Vector3> handleB, bool loopPath, float smoothness)
	{
		if (points == null || handleA == null || handleB == null) return;

		int count = points.Count;
		ResizeHandleLists(handleA, handleB, count);
		if (count == 0) return;

		smoothness = Mathf.Max(0f, smoothness);
		if (count == 1)
		{
			Vector3 offset = Vector3.forward * (0.5f * smoothness);
			handleA[0] = -offset;
			handleB[0] = offset;
			return;
		}

		float scale = smoothness / 3f;
		for (int i = 0; i < count; i++)
		{
			Vector3 prev = loopPath ? points[(i - 1 + count) % count] : points[Mathf.Max(0, i - 1)];
			Vector3 next = loopPath ? points[(i + 1) % count] : points[Mathf.Min(count - 1, i + 1)];

			Vector3 tangent;
			if (!loopPath && i == 0) tangent = (next - points[i]);
			else if (!loopPath && i == count - 1) tangent = (points[i] - prev);
			else tangent = 0.5f * (next - prev);

			if (tangent.sqrMagnitude < kEpsilon)
			{
				Vector3 fallback = BezierCurveUtils.ComputeAutoHandleForward(points, i, loopPath, Vector3.forward);
				tangent = fallback * 0.5f;
			}

			Vector3 offset = tangent * scale;
			handleA[i] = -offset;
			handleB[i] = offset;
		}
	}

	static void ResizeHandleLists(List<Vector3> handleA, List<Vector3> handleB, int count)
	{
		while (handleA.Count < count) handleA.Add(Vector3.zero);
		while (handleB.Count < count) handleB.Add(Vector3.zero);
		while (handleA.Count > count) handleA.RemoveAt(handleA.Count - 1);
		while (handleB.Count > count) handleB.RemoveAt(handleB.Count - 1);
	}
}