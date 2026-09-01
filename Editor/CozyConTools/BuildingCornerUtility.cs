using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace CozyCon.Tools
{
	public static class BuildingCornerUtility
	{
		private const float CornerJoinerSizeMultiplier = 1.05f;
		private const float AxisAlignmentThreshold = 0.92f;

		public static bool TryApplyBoxLikeWallSizing(BuildingDraftData draft, Vector3 wallDirection, float wallLength, out float adjustedLength, out float startMiter, out float endMiter)
		{
			adjustedLength = wallLength;
			startMiter = 0f;
			endMiter = 0f;

			if (draft == null || draft.FootprintCorners == null || draft.FootprintCorners.Count != 4)
			{
				return false;
			}

			if (!TryGetBoxAxes(draft.FootprintCorners, out Vector2 axisA, out Vector2 axisB))
			{
				return false;
			}

			float wallThickness = Mathf.Max(0.01f, draft.WallThickness);
			if (draft.WallJoinerStyle == BuildingJoinerStyle.Sharp)
			{
				float joinerSize = Mathf.Max(0.01f, wallThickness * CornerJoinerSizeMultiplier);
				adjustedLength = Mathf.Max(0.01f, wallLength - joinerSize);
				startMiter = 0f;
				endMiter = 0f;
				return true;
			}

			bool frontBackWall = IsAlignedWithAxis(wallDirection, axisA) || IsAlignedWithAxis(wallDirection, axisB);
			if (frontBackWall)
			{
				adjustedLength = wallLength + wallThickness;
				startMiter = wallThickness * 0.5f;
				endMiter = wallThickness * 0.5f;
			}
			else
			{
				float rectTrim = Mathf.Min(wallThickness, wallLength * 0.45f);
				adjustedLength = Mathf.Max(0.01f, wallLength - rectTrim);
			}

			return true;
		}

		public static bool TryRebuildSharpCornerCaps(BuildingDraftData draft, Transform joinersContainer)
		{
			if (draft == null || joinersContainer == null)
			{
				// Then create a new joiners container if it doesn't exist
                if (joinersContainer == null)
                {
                    GameObject newContainer = new GameObject("WallJoiners");
                    newContainer.transform.SetParent(draft.transform);
                    joinersContainer = newContainer.transform;
                }
                // and create a new draft if it doesn't exist
                if (draft == null)
                {
                    GameObject newDraft = new GameObject("BuildingDraft");
                    draft = newDraft.AddComponent<BuildingDraftData>();
                }
			}

			int count = draft.FootprintCorners.Count;
			Vector2 footprintCenter = Vector2.zero;
			for (int i = 0; i < count; i++)
			{
				footprintCenter += draft.FootprintCorners[i];
			}
			footprintCenter /= Mathf.Max(1, count);

			for (int i = 0; i < count; i++)
			{
				int prevIndex = (i - 1 + count) % count;
				int nextIndex = (i + 1) % count;
				Vector2 corner2 = draft.FootprintCorners[i];
				Vector2 prev2 = draft.FootprintCorners[prevIndex];
				Vector2 next2 = draft.FootprintCorners[nextIndex];
				Vector3 corner = new Vector3(corner2.x, 0f, corner2.y);
				Vector3 prevWallDir = (corner - new Vector3(prev2.x, 0f, prev2.y)).normalized;
				Vector3 nextWallDir = (new Vector3(next2.x, 0f, next2.y) - corner).normalized;
				Vector3 prevOutward = ComputeOutwardLocal(prevWallDir, corner, footprintCenter);
				Vector3 nextOutward = ComputeOutwardLocal(nextWallDir, corner, footprintCenter);

				GameObject joinerObject;
				if (i < joinersContainer.childCount)
				{
					joinerObject = joinersContainer.GetChild(i).gameObject;
				}
				else
				{
					joinerObject = new GameObject();
					joinerObject.transform.SetParent(joinersContainer);
				}

				joinerObject.name = $"WallJoiner_{i}";
				joinerObject.transform.SetParent(joinersContainer);

				float capSize = Mathf.Max(0.01f, draft.WallThickness * 0.5f * CornerJoinerSizeMultiplier);
				float cornerMeshSize = Mathf.Max(0.01f, draft.WallThickness * CornerJoinerSizeMultiplier);
				Vector2 capCenter2 = corner2;
				Vector3 capLocal = new Vector3(capCenter2.x, draft.WallHeight * 0.5f, capCenter2.y);
				joinerObject.transform.position = draft.transform.TransformPoint(capLocal);
				joinerObject.transform.rotation = draft.transform.rotation;
				joinerObject.transform.localScale = Vector3.one;

				Material material = draft.GetWallMaterial(i);
				MeshFilter meshFilter = GetOrAddComponent<MeshFilter>(joinerObject);
				MeshRenderer meshRenderer = GetOrAddComponent<MeshRenderer>(joinerObject);
				MeshCollider meshCollider = GetOrAddComponent<MeshCollider>(joinerObject);
				Mesh joinerMesh = BuildingMeshUtility.BuildCenteredBoxMesh(cornerMeshSize, draft.WallHeight, cornerMeshSize, $"WallJoinerMesh_{i}");
				BuildingMeshUtility.ApplyWorldScaleQuadUvsAndPackedLightmap(joinerMesh, draft.WallUvScaleMultiplier);
				meshFilter.sharedMesh = joinerMesh;
				meshCollider.sharedMesh = joinerMesh;
				meshRenderer.sharedMaterial = material;
			}

			return true;
		}

		private static bool TryGetBoxAxes(IReadOnlyList<Vector2> corners, out Vector2 axisA, out Vector2 axisB)
		{
			if (corners == null || corners.Count != 4)
			{
				axisA = Vector2.right;
				axisB = Vector2.up;
				return false;
			}

			Vector2 edge0 = corners[1] - corners[0];
			Vector2 edge1 = corners[2] - corners[1];
			Vector2 edge2 = corners[3] - corners[2];
			Vector2 edge3 = corners[0] - corners[3];

			if (edge0.sqrMagnitude < 0.000001f || edge1.sqrMagnitude < 0.000001f || edge2.sqrMagnitude < 0.000001f || edge3.sqrMagnitude < 0.000001f)
			{
				axisA = Vector2.right;
				axisB = Vector2.up;
				return false;
			}

			Vector2 dir0 = edge0.normalized;
			Vector2 dir1 = edge1.normalized;
			Vector2 dir2 = edge2.normalized;
			Vector2 dir3 = edge3.normalized;

			if (!AreParallel(dir0, dir2) || !AreParallel(dir1, dir3) || !ArePerpendicular(dir0, dir1))
			{
				axisA = Vector2.right;
				axisB = Vector2.up;
				return false;
			}

			axisA = dir0;
			axisB = dir1;
			return true;
		}

		private static bool AreParallel(Vector2 a, Vector2 b)
		{
			return Mathf.Abs(Vector2.Dot(a.normalized, b.normalized)) >= AxisAlignmentThreshold;
		}

		private static bool ArePerpendicular(Vector2 a, Vector2 b)
		{
			return Mathf.Abs(Vector2.Dot(a.normalized, b.normalized)) <= (1f - AxisAlignmentThreshold);
		}

		private static bool IsAlignedWithAxis(Vector3 dir, Vector2 axis)
		{
			Vector2 projected = new Vector2(dir.x, dir.z).normalized;
			return Mathf.Abs(Vector2.Dot(projected, axis.normalized)) >= AxisAlignmentThreshold;
		}

		private static Vector3 ComputeOutwardLocal(Vector3 wallDir, Vector3 corner, Vector2 footprintCenter)
		{
			Vector3 outward = Vector3.Cross(Vector3.up, wallDir).normalized;
			Vector3 centerOffset = corner - new Vector3(footprintCenter.x, 0f, footprintCenter.y);
			if (Vector3.Dot(outward, centerOffset) < 0f)
			{
				outward *= -1f;
			}

			return outward;
		}

		private static T GetOrAddComponent<T>(GameObject target) where T : Component
		{
			if (target == null)
			{
				return null;
			}

			T component = target.GetComponent<T>();
			if (component == null)
			{
				component = target.AddComponent<T>();
			}

			return component;
		}
	}
}