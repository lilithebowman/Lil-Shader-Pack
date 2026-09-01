using UnityEngine;

namespace CozyCon.Tools
{
	public static class BuildingEditorUtility
	{
		public static void GetOpeningMinMax(BuildingOpeningData opening, out float minX, out float maxX, out float minY, out float maxY)
		{
			minX = opening.center.x - opening.size.x * 0.5f;
			maxX = opening.center.x + opening.size.x * 0.5f;
			minY = opening.center.y - opening.size.y * 0.5f;
			maxY = opening.center.y + opening.size.y * 0.5f;
		}

		public static Vector2 ClampOpeningRect(Vector2 center, Vector2 size, float minSize, float maxX, float maxY)
		{
			float width = Mathf.Clamp(size.x, minSize, maxX);
			float height = Mathf.Clamp(size.y, minSize, maxY);
			float halfWidth = width * 0.5f;
			float halfHeight = height * 0.5f;

			float xMin = Mathf.Clamp(center.x - halfWidth, 0f, Mathf.Max(0f, maxX - width));
			float xMax = Mathf.Clamp(center.x + halfWidth, width, maxX);
			float yMin = Mathf.Clamp(center.y - halfHeight, 0f, Mathf.Max(0f, maxY - height));
			float yMax = Mathf.Clamp(center.y + halfHeight, height, maxY);

			float clampedCenterX = Mathf.Clamp((xMin + xMax) * 0.5f, halfWidth, maxX - halfWidth);
			float clampedCenterY = Mathf.Clamp((yMin + yMax) * 0.5f, halfHeight, maxY - halfHeight);
			return new Vector2(clampedCenterX, clampedCenterY);
		}

		public static Vector2 ResizeOpeningFromCorner(Vector2 movedCorner, Vector2 oppositeCorner, float minSize, float minX, float maxX, float minY, float maxY)
		{
			float nextMinX = Mathf.Min(movedCorner.x, oppositeCorner.x);
			float nextMaxX = Mathf.Max(movedCorner.x, oppositeCorner.x);
			float nextMinY = Mathf.Min(movedCorner.y, oppositeCorner.y);
			float nextMaxY = Mathf.Max(movedCorner.y, oppositeCorner.y);

			nextMinX = Mathf.Clamp(nextMinX, minX, maxX - minSize);
			nextMaxX = Mathf.Clamp(nextMaxX, nextMinX + minSize, maxX);
			nextMinY = Mathf.Clamp(nextMinY, minY, maxY - minSize);
			nextMaxY = Mathf.Clamp(nextMaxY, nextMinY + minSize, maxY);

			return new Vector2((nextMinX + nextMaxX) * 0.5f, (nextMinY + nextMaxY) * 0.5f);
		}

		public static Vector2 ResizeOpeningFromCorner(Vector2 movedCorner, Vector2 oppositeCorner, float minSize, float maxX, float maxY)
		{
			return ResizeOpeningFromCorner(movedCorner, oppositeCorner, minSize, 0f, maxX, 0f, maxY);
		}

		public static Vector2 SizeFromBounds(Vector2 min, Vector2 max)
		{
			return new Vector2(max.x - min.x, max.y - min.y);
		}

		public static Vector2 SnapVector2(Vector2 value, float size)
		{
			float snap = Mathf.Max(0.01f, size);
			return new Vector2(
				Mathf.Round(value.x / snap) * snap,
				Mathf.Round(value.y / snap) * snap);
		}
	}
}
