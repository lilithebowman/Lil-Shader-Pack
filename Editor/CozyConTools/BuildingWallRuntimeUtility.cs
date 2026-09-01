using System;
using System.Collections.Generic;
using UnityEngine;

namespace CozyCon.Tools
{
	public struct WallRuntimeInfo
	{
		public int wallIndex;
		public Vector3 worldPosition;
		public Vector3 worldCenterlinePosition;
		public Quaternion worldRotation;
		public float length;
		public float startMiter;
		public float endMiter;
		public Vector3 startLocal;
		public Vector3 endLocal;
	}

	public static class BuildingWallRuntimeUtility
	{
		public const string WallsContainerName = "Walls";

		public static void BuildWallRuntimeCache(BuildingDraftData draft, List<WallRuntimeInfo> target)
		{
			target.Clear();
			if (draft == null || draft.FootprintCorners.Count < 2)
			{
				return;
			}

			int count = draft.FootprintCorners.Count;
			Vector3 footprintCenter = Vector3.zero;
			for (int i = 0; i < count; i++)
			{
				Vector2 p = draft.FootprintCorners[i];
				footprintCenter += new Vector3(p.x, 0f, p.y);
			}

			footprintCenter /= count;

			for (int i = 0; i < count; i++)
			{
				int prevIndex = (i - 1 + count) % count;
				int nextIndex = (i + 1) % count;
				int nextNextIndex = (i + 2) % count;

				Vector2 prev2 = draft.FootprintCorners[prevIndex];
				Vector2 a2 = draft.FootprintCorners[i];
				Vector2 b2 = draft.FootprintCorners[nextIndex];
				Vector2 c2 = draft.FootprintCorners[nextNextIndex];
				Vector3 a = new Vector3(a2.x, 0f, a2.y);
				Vector3 b = new Vector3(b2.x, 0f, b2.y);
				Vector3 prev = new Vector3(prev2.x, 0f, prev2.y);
				Vector3 c = new Vector3(c2.x, 0f, c2.y);

				Vector3 edge = b - a;
				float length = edge.magnitude;
				if (length < 0.01f)
				{
					continue;
				}

				Vector3 dir = edge / length;
				Vector3 prevDir = (a - prev).normalized;
				Vector3 nextDir = (c - b).normalized;
				float halfThickness = draft.WallThickness * 0.5f;
				float startMiter = ComputeCornerMiterExtension(prevDir, dir, halfThickness);
				float endMiter = ComputeCornerMiterExtension(dir, nextDir, halfThickness);
				float adjustedLength = length;

				if (draft.WallJoinerStyle == BuildingJoinerStyle.Sharp && BuildingCornerUtility.TryApplyBoxLikeWallSizing(draft, dir, length, out float boxAdjustedLength, out float boxStartMiter, out float boxEndMiter))
				{
					adjustedLength = boxAdjustedLength;
					startMiter = boxStartMiter;
					endMiter = boxEndMiter;
				}

				if (draft.WallJoinerStyle == BuildingJoinerStyle.Beveled)
				{
					float trimBack = draft.WallThickness;
					float maxTrim = length * 0.45f;
					float clampedTrim = Mathf.Min(trimBack, maxTrim);
					startMiter = -clampedTrim;
					endMiter = -clampedTrim;
				}

				Vector3 midpoint = (a + b) * 0.5f + Vector3.up * (draft.WallHeight * 0.5f);
				Vector3 centerlineMidpoint = midpoint;
				midpoint += dir * ((endMiter - startMiter) * 0.5f);

				Vector3 outward = Vector3.Cross(Vector3.up, dir).normalized;
				if (Vector3.Dot(outward, centerlineMidpoint - footprintCenter) < 0f)
				{
					outward *= -1f;
				}

				Quaternion rotation = Quaternion.LookRotation(outward, Vector3.up);

				target.Add(new WallRuntimeInfo
				{
					wallIndex = i,
					worldPosition = draft.transform.TransformPoint(midpoint),
					worldCenterlinePosition = draft.transform.TransformPoint(centerlineMidpoint),
					worldRotation = draft.transform.rotation * rotation,
					length = adjustedLength,
					startMiter = startMiter,
					endMiter = endMiter,
					startLocal = a,
					endLocal = b
				});
			}
		}

		public static float ComputeCornerMiterExtension(Vector3 inDir, Vector3 outDir, float halfThickness)
		{
			float angle = Vector3.Angle(inDir, outDir);
			float clamped = Mathf.Clamp(angle, 10f, 170f) * Mathf.Deg2Rad;
			float denom = Mathf.Max(0.01f, Mathf.Tan(clamped * 0.5f));
			float ext = halfThickness / denom;
			return Mathf.Clamp(ext, 0f, halfThickness * 3f);
		}

		public static bool TryGetWallInfo(int wallIndex, List<WallRuntimeInfo> cache, out WallRuntimeInfo info)
		{
			for (int i = 0; i < cache.Count; i++)
			{
				if (cache[i].wallIndex == wallIndex)
				{
					info = cache[i];
					return true;
				}
			}

			info = default;
			return false;
		}

		public static bool TryGetWallHit(Ray ray, BuildingDraftData draft, List<WallRuntimeInfo> cache, out RaycastHit hit, out WallRuntimeInfo wall)
		{
			hit = default;
			wall = default;

			if (draft == null)
			{
				return false;
			}

			Transform wallsContainer = draft.transform.Find(WallsContainerName);
			if (wallsContainer == null)
			{
				return false;
			}

			RaycastHit[] hits = Physics.RaycastAll(ray, 20000f);
			if (hits == null || hits.Length == 0)
			{
				return false;
			}

			Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
			for (int i = 0; i < hits.Length; i++)
			{
				Transform hitTransform = hits[i].transform;
				if (!hitTransform.IsChildOf(wallsContainer))
				{
					continue;
				}

				int index = ParseWallIndex(hitTransform.name);
				if (index < 0)
				{
					index = ParseWallIndex(hitTransform.parent != null ? hitTransform.parent.name : string.Empty);
				}

				if (index < 0 || !TryGetWallInfo(index, cache, out wall))
				{
					continue;
				}

				hit = hits[i];
				return true;
			}

			return false;
		}

		public static int ParseWallIndex(string name)
		{
			const string prefix = "Wall_";
			if (string.IsNullOrEmpty(name) || !name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
			{
				return -1;
			}

			string value = name.Substring(prefix.Length);
			return int.TryParse(value, out int parsed) ? parsed : -1;
		}

		public static Vector2 WorldToWallUv(WallRuntimeInfo wall, float wallHeight, Vector3 world)
		{
			Vector3 local = Quaternion.Inverse(wall.worldRotation) * (world - wall.worldCenterlinePosition);
			float u = local.x + wall.length * 0.5f;
			float v = local.y + wallHeight * 0.5f;
			return new Vector2(u, v);
		}

		public static Vector3 WallUvToWorld(WallRuntimeInfo wall, float wallHeight, float u, float v, float z)
		{
			Vector3 local = new Vector3(u - wall.length * 0.5f, v - wallHeight * 0.5f, z);
			return wall.worldCenterlinePosition + wall.worldRotation * local;
		}
	}
}
