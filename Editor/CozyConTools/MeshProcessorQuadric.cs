// MeshProcessorQuadric.cs
// Fast Quadric Mesh Simplification for Unity
// Based on Garland & Heckbert algorithm
// Ported from https://github.com/sp4cerat/Fast-Quadric-Mesh-Simplification
// CC0-Attribution License by Lilithe for CozyCon 2025

using UnityEngine;
using System.Collections.Generic;
using System.Linq;
using System;
using CozyCon.Tools;

namespace CozyCon.Tools
{
	/// <summary>
	/// Fast Quadric Mesh Simplification processor
	/// </summary>
	public static class MeshProcessorQuadric
	{
		/// <summary>
		/// Decimates a mesh using the Fast Quadric Mesh Simplification algorithm.
		/// </summary>
		/// <param name="originalMesh">The mesh to decimate</param>
		/// <param name="targetReduction">Target reduction ratio (0.5 = 50% reduction)</param>
		/// <returns>New decimated mesh</returns>
		public static Mesh DecimateMesh(Mesh originalMesh, float targetReduction)
		{
			Debug.Log($"[QuadricProcessor] Starting decimation on '{originalMesh.name}': {originalMesh.triangles.Length / 3} triangles, target reduction: {targetReduction:P0}");

			var vertices = originalMesh.vertices;
			var triangles = originalMesh.triangles;

			if (triangles.Length < 12) // Need at least 4 triangles
			{
				Debug.Log($"[QuadricProcessor] Mesh too small, returning copy");
				return UnityEngine.Object.Instantiate(originalMesh);
			}

			float quality = 1f - targetReduction;
			int targetTriangleCount = Mathf.RoundToInt(triangles.Length / 3 * quality);
			targetTriangleCount = Mathf.Max(targetTriangleCount, 4);

			Debug.Log($"[QuadricProcessor] Target: {targetTriangleCount} triangles (from {triangles.Length / 3})");

			// Initialize data structures
			var vertList = new List<QVertex>();
			var triList = new List<QTriangle>();

			// Create vertices
			for (int i = 0; i < vertices.Length; i++)
			{
				vertList.Add(new QVertex { p = vertices[i], q = new SymmetricMatrix() });
			}

			// Create triangles and compute initial quadrics
			for (int i = 0; i < triangles.Length; i += 3)
			{
				int v0 = triangles[i], v1 = triangles[i + 1], v2 = triangles[i + 2];
				var tri = new QTriangle
				{
					v = new int[] { v0, v1, v2 },
					deleted = false,
					dirty = false,
					err = new double[4]
				};

				// Calculate triangle normal and plane equation
				Vector3 p0 = vertices[v0], p1 = vertices[v1], p2 = vertices[v2];
				Vector3 normal = Vector3.Cross(p1 - p0, p2 - p0).normalized;
				float d = -Vector3.Dot(normal, p0);

				// Add plane quadric to all vertices
				var plane = new SymmetricMatrix(normal.x, normal.y, normal.z, d);
				vertList[v0].q += plane;
				vertList[v1].q += plane;
				vertList[v2].q += plane;

				triList.Add(tri);
			}

			// Main decimation loop
			int deletedTriangles = 0;
			int triangleCount = triList.Count;

			for (int iteration = 0; iteration < 100 && triangleCount - deletedTriangles > targetTriangleCount; iteration++)
			{
				// Calculate threshold for this iteration (adaptive)
				double threshold = 0.000000001 * Math.Pow(iteration + 3, 7.0);

				if (iteration % 10 == 0)
				{
					int remaining = triangleCount - deletedTriangles;
					Debug.Log($"[QuadricProcessor] Iteration {iteration}: {remaining} triangles remaining (target: {targetTriangleCount})");
				}

				// Calculate edge errors for all triangles
				for (int i = 0; i < triList.Count; i++)
				{
					var tri = triList[i];
					if (tri.deleted) continue;

					// Calculate errors for all 3 edges
					for (int j = 0; j < 3; j++)
					{
						int v0 = tri.v[j];
						int v1 = tri.v[(j + 1) % 3];

						Vector3 p;
						double error = CalculateError(vertList, v0, v1, out p);
						tri.err[j] = error;
					}

					// Store minimum error
					tri.err[3] = Math.Min(Math.Min(tri.err[0], tri.err[1]), tri.err[2]);
				}

				// Collapse edges below threshold
				for (int i = 0; i < triList.Count; i++)
				{
					var tri = triList[i];
					if (tri.deleted || tri.err[3] > threshold) continue;

					// Find edge with minimum error
					for (int j = 0; j < 3; j++)
					{
						if (tri.err[j] == tri.err[3])
						{
							int v0 = tri.v[j];
							int v1 = tri.v[(j + 1) % 3];

							Vector3 optimalPos;
							CalculateError(vertList, v0, v1, out optimalPos);

							// Perform edge collapse: v1 -> v0
							vertList[v0].p = optimalPos;
							vertList[v0].q += vertList[v1].q;

							// Update all triangles containing v1
							for (int k = 0; k < triList.Count; k++)
							{
								var t = triList[k];
								if (t.deleted) continue;

								bool changed = false;
								for (int l = 0; l < 3; l++)
								{
									if (t.v[l] == v1)
									{
										t.v[l] = v0;
										changed = true;
									}
								}

								if (changed)
								{
									// Check for degenerate triangle
									if (t.v[0] == t.v[1] || t.v[1] == t.v[2] || t.v[2] == t.v[0])
									{
										t.deleted = true;
										deletedTriangles++;
									}
								}
							}
							break;
						}
					}

					if (triangleCount - deletedTriangles <= targetTriangleCount) break;
				}

				if (triangleCount - deletedTriangles <= targetTriangleCount) break;
			}

			int finalTriCount = triangleCount - deletedTriangles;
			float actualReduction = 1f - (float)finalTriCount / triangleCount;
			Debug.Log($"[QuadricProcessor] Decimation complete: {finalTriCount} triangles ({actualReduction:P1} reduction)");

			// Build final mesh
			return BuildOptimizedMesh(originalMesh, vertList, triList);
		}

		/// <summary>
		/// Calculate quadric error for collapsing edge v0->v1
		/// </summary>
		private static double CalculateError(List<QVertex> vertices, int v0, int v1, out Vector3 result)
		{
			SymmetricMatrix q = vertices[v0].q + vertices[v1].q;

			// Try to find optimal point by solving linear system
			double det = q.Det(0, 1, 2, 1, 4, 5, 2, 5, 7);

			if (Math.Abs(det) > 1e-10)
			{
				// Invertible - compute optimal position
				result.x = (float)(-q.Det(1, 2, 3, 4, 5, 6, 5, 7, 8) / det);
				result.y = (float)(q.Det(0, 2, 3, 1, 5, 6, 2, 7, 8) / det);
				result.z = (float)(-q.Det(0, 1, 3, 1, 4, 6, 2, 5, 8) / det);
			}
			else
			{
				// Singular matrix - choose best of endpoint or midpoint
				Vector3 p0 = vertices[v0].p;
				Vector3 p1 = vertices[v1].p;
				Vector3 p2 = (p0 + p1) * 0.5f;

				double err0 = VertexError(q, p0);
				double err1 = VertexError(q, p1);
				double err2 = VertexError(q, p2);

				double minErr = Math.Min(err0, Math.Min(err1, err2));

				if (minErr == err0) result = p0;
				else if (minErr == err1) result = p1;
				else result = p2;

				return minErr;
			}

			return VertexError(q, result);
		}

		/// <summary>
		/// Calculate quadric error at a point
		/// </summary>
		private static double VertexError(SymmetricMatrix q, Vector3 p)
		{
			double x = p.x, y = p.y, z = p.z;
			return q[0] * x * x + 2 * q[1] * x * y + 2 * q[2] * x * z + 2 * q[3] * x + q[4] * y * y
				+ 2 * q[5] * y * z + 2 * q[6] * y + q[7] * z * z + 2 * q[8] * z + q[9];
		}

		/// <summary>
		/// Build final optimized mesh from simplified data
		/// </summary>
		private static Mesh BuildOptimizedMesh(Mesh originalMesh, List<QVertex> vertices, List<QTriangle> triangles)
		{
			// Collect valid triangles and vertices
			var validTriangles = triangles.Where(t => !t.deleted).ToList();
			var usedVertices = new HashSet<int>();

			foreach (var tri in validTriangles)
			{
				usedVertices.Add(tri.v[0]);
				usedVertices.Add(tri.v[1]);
				usedVertices.Add(tri.v[2]);
			}

			// Create vertex remapping
			var vertexMap = new Dictionary<int, int>();
			var newVertices = new List<Vector3>();
			int newIndex = 0;

			foreach (int oldIndex in usedVertices.OrderBy(x => x))
			{
				vertexMap[oldIndex] = newIndex++;
				newVertices.Add(vertices[oldIndex].p);
			}

			// Build new triangle array
			var newTriangles = new List<int>();
			foreach (var tri in validTriangles)
			{
				newTriangles.Add(vertexMap[tri.v[0]]);
				newTriangles.Add(vertexMap[tri.v[1]]);
				newTriangles.Add(vertexMap[tri.v[2]]);
			}

			// Create final mesh
			var mesh = new Mesh();
			mesh.name = originalMesh.name + "_Decimated_Quadric";
			mesh.vertices = newVertices.ToArray();
			mesh.triangles = newTriangles.ToArray();

			// Copy UVs if available
			if (originalMesh.uv != null && originalMesh.uv.Length > 0)
			{
				var newUVs = new List<Vector2>();
				foreach (int oldIndex in usedVertices.OrderBy(x => x))
				{
					if (oldIndex < originalMesh.uv.Length)
						newUVs.Add(originalMesh.uv[oldIndex]);
					else
						newUVs.Add(Vector2.zero);
				}
				mesh.uv = newUVs.ToArray();
			}

			mesh.RecalculateNormals();
			mesh.RecalculateBounds();

			return mesh;
		}

		// Data structures for quadric simplification
		private class QVertex
		{
			public Vector3 p;
			public SymmetricMatrix q;
		}

		private class QTriangle
		{
			public int[] v = new int[3];
			public double[] err = new double[4];
			public bool deleted;
			public bool dirty;
		}

		/// <summary>
		/// Symmetric 4x4 matrix for quadric error calculation
		/// </summary>
		private class SymmetricMatrix
		{
			private double[] m = new double[10];

			public SymmetricMatrix(double c = 0)
			{
				for (int i = 0; i < 10; i++) m[i] = c;
			}

			public SymmetricMatrix(double a, double b, double c, double d)
			{
				m[0] = a * a; m[1] = a * b; m[2] = a * c; m[3] = a * d;
				m[4] = b * b; m[5] = b * c; m[6] = b * d;
				m[7] = c * c; m[8] = c * d;
				m[9] = d * d;
			}

			public double this[int index] => m[index];

			public static SymmetricMatrix operator +(SymmetricMatrix a, SymmetricMatrix b)
			{
				var result = new SymmetricMatrix();
				for (int i = 0; i < 10; i++)
					result.m[i] = a.m[i] + b.m[i];
				return result;
			}

			public double Det(int a11, int a12, int a13, int a21, int a22, int a23, int a31, int a32, int a33)
			{
				return m[a11] * m[a22] * m[a33] + m[a13] * m[a21] * m[a32] + m[a12] * m[a23] * m[a31]
					 - m[a13] * m[a22] * m[a31] - m[a11] * m[a23] * m[a32] - m[a12] * m[a21] * m[a33];
			}
		}
	}
}