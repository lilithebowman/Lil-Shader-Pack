using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Generates end-cap geometry for BezierPathway meshes.
/// This version uses reversed winding for end-cap quads so the generated
/// cap normals point outward instead of inward.
/// </summary>
public static class BezierPathwayEndCapsBuilder
{
	// Helper to add a quad with consistent winding (a,b,c,d) -> (a,b,c) (a,c,d)
	static void AddQuad(List<int> tris, int a, int b, int c, int d)
	{
		tris.Add(a); tris.Add(b); tris.Add(c);
		tris.Add(a); tris.Add(c); tris.Add(d);
	}

	/// <summary>
	/// Generate end caps for the path submesh (top/bottom/vertical walls).
	/// Matches the vertex layout used by BezierPathwayMeshBuilder:
	/// per-slice first 4 verts: 0=leftBase, 1=rightBase, 2=leftTop, 3=rightTop.
	/// Appends triangles into trisPath.
	/// Winding here is chosen so normals point outward from the mesh.
	/// </summary>
	public static void GenerateEndCaps(
		BezierPathway path,
		int[] sliceBaseIndex,
		List<Vector3> vertsLocal,
		List<Vector3> normalsLocal,
		List<Vector2> uv0,
		List<Vector2> uv1,
		List<int> trisPath)
	{
		if (path == null || sliceBaseIndex == null || sliceBaseIndex.Length < 2 || trisPath == null) return;

		int firstBase = sliceBaseIndex[0];
		int lastBase = sliceBaseIndex[sliceBaseIndex.Length - 1];

		// Per-slice path vertex layout:
		// base + 0 = leftBase
		// base + 1 = rightBase
		// base + 2 = leftTop
		// base + 3 = rightTop

		// START CAP (slice 0) - face backwards along path (winding reversed to point outward)
		int s_leftBase = firstBase + 0;
		int s_rightBase = firstBase + 1;
		int s_leftTop = firstBase + 2;
		int s_rightTop = firstBase + 3;

		// Reverse winding compared to inward-facing version
		AddQuad(trisPath, s_leftBase, s_leftTop, s_rightTop, s_rightBase);

		// END CAP (last slice) - face forwards along path (winding reversed to point outward)
		int e_leftBase = lastBase + 0;
		int e_rightBase = lastBase + 1;
		int e_leftTop = lastBase + 2;
		int e_rightTop = lastBase + 3;

		AddQuad(trisPath, e_rightBase, e_rightTop, e_leftTop, e_leftBase);
	}

	/// <summary>
	/// Generate end caps for the left and right side geometry (siding submesh).
	/// Assumes the per-slice side layout used in BezierPathwayMeshBuilder when generateSides == true:
	/// base+4..base+11:
	/// 4=leftInnerBottom, 5=leftInnerTop, 6=leftOuterBottom, 7=leftOuterTop,
	/// 8=rightInnerBottom, 9=rightInnerTop, 10=rightOuterBottom, 11=rightOuterTop.
	/// Appends triangles into trisSides. Winding chosen so normals point outward.
	/// </summary>
	public static void GenerateSideEndCaps(
		BezierPathway path,
		int[] sliceBaseIndex,
		List<int> trisSides)
	{
		if (path == null || !path.generateSides || sliceBaseIndex == null || sliceBaseIndex.Length < 2 || trisSides == null) return;

		int firstBase = sliceBaseIndex[0];
		int lastBase = sliceBaseIndex[sliceBaseIndex.Length - 1];

		// START CAP - LEFT SIDE
		int s_leftInnerB = firstBase + 4;
		int s_leftInnerT = firstBase + 5;
		int s_leftOuterB = firstBase + 6;
		int s_leftOuterT = firstBase + 7;

		// Reverse winding so cap normal points outward
		AddQuad(trisSides, s_leftOuterB, s_leftOuterT, s_leftInnerT, s_leftInnerB);

		// START CAP - RIGHT SIDE
		int s_rightInnerB = firstBase + 8;
		int s_rightInnerT = firstBase + 9;
		int s_rightOuterB = firstBase + 10;
		int s_rightOuterT = firstBase + 11;

		// Reverse winding so cap normal points outward
		AddQuad(trisSides, s_rightInnerB, s_rightInnerT, s_rightOuterT, s_rightOuterB);

		// END CAP - LEFT SIDE (reverse winding relative to start, but still outward)
		int e_leftInnerB = lastBase + 4;
		int e_leftInnerT = lastBase + 5;
		int e_leftOuterB = lastBase + 6;
		int e_leftOuterT = lastBase + 7;

		AddQuad(trisSides, e_leftInnerB, e_leftInnerT, e_leftOuterT, e_leftOuterB);

		// END CAP - RIGHT SIDE (reverse winding relative to start, but still outward)
		int e_rightInnerB = lastBase + 8;
		int e_rightInnerT = lastBase + 9;
		int e_rightOuterB = lastBase + 10;
		int e_rightOuterT = lastBase + 11;

		AddQuad(trisSides, e_rightOuterB, e_rightOuterT, e_rightInnerT, e_rightInnerB);
	}
}
