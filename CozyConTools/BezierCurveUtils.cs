using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Shared bezier and handle helper utilities used by CozyCon curve components.
/// </summary>
public static class BezierCurveUtils
{
	/// <summary>
	/// Evaluate cubic Bezier point at t in [0..1].
	/// </summary>
	public static Vector3 EvaluateCubic(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
	{
		float u = 1f - t;
		return u * u * u * p0 +
			   3f * u * u * t * p1 +
			   3f * u * t * t * p2 +
			   t * t * t * p3;
	}

	/// <summary>
	/// Evaluate (non-normalized) cubic Bezier tangent at t in [0..1].
	/// </summary>
	public static Vector3 EvaluateCubicTangent(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
	{
		float u = 1f - t;
		return 3f * u * u * (p1 - p0) +
			   6f * u * t * (p2 - p1) +
			   3f * t * t * (p3 - p2);
	}

	/// <summary>
	/// Compute an automatic local forward direction from neighboring anchor points.
	/// </summary>
	public static Vector3 ComputeAutoHandleForward(IReadOnlyList<Vector3> points, int index, bool loopPath, Vector3 fallback)
	{
		int count = points == null ? 0 : points.Count;
		if (count == 0) return fallback;

		if (count == 1)
		{
			return fallback;
		}

		Vector3 prev;
		Vector3 next;

		if (loopPath)
		{
			prev = points[(index - 1 + count) % count];
			next = points[(index + 1) % count];
		}
		else
		{
			prev = points[Mathf.Max(0, index - 1)];
			next = points[Mathf.Min(count - 1, index + 1)];
		}

		Vector3 current = points[index];
		Vector3 forward;

		if (!loopPath && index == 0) forward = (next - current);
		else if (!loopPath && index == count - 1) forward = (current - prev);
		else
		{
			Vector3 inDir = (current - prev).normalized;
			Vector3 outDir = (next - current).normalized;
			forward = inDir + outDir;
			if (forward.sqrMagnitude < 1e-6f) forward = outDir;
		}

		if (forward.sqrMagnitude < 1e-6f) forward = fallback;
		return forward.normalized;
	}
}