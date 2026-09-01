// Assets/Editor/CozyConTools/GenerateAO/DebugAORenderer.cs
// Editor utility to draw a debug sphere and per-sample rays in the Scene view.
// Includes diagnostics and a menu toggle to help ensure gizmos are visible.

#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

namespace CozyConTools.GenerateAO
{
	[InitializeOnLoad]
	public static class DebugAORenderer
	{
		struct RayDebug
		{
			public Vector3 origin;
			public Vector3 dir;
			public float length;
			public Color color;
			public bool hit;
			public double createdTime;
		}

		static List<RayDebug> s_rays = new List<RayDebug>(1024);
		static bool s_showSphere = false;
		static Vector3 s_sphereCenter = Vector3.zero;
		static float s_sphereRadius = 1f;
		static Color s_sphereColor = new Color(0f, 0.6f, 1f, 0.9f);
		static float s_rayLifetime = 60f; // keep rays visible longer by default for debugging
		static bool s_autoExpireRays = false; // default off while debugging
		static bool s_enabled = true;

		// Diagnostic counters (helpful to see activity in Console)
		static int s_totalRaysAdded = 0;
		static int s_logRaysThreshold = 1; // log first few rays

		// Static ctor subscribes to SceneView drawing
		static DebugAORenderer()
		{
			SceneView.duringSceneGui += OnSceneGUI;
			EditorApplication.update += OnEditorUpdate;
			Debug.Log("[DebugAORenderer] Initialized and subscribed to SceneView.duringSceneGui.");
		}

		// Public API ----------------------------------------------------

		public static void SetEnabled(bool enabled)
		{
			if (s_enabled == enabled) return;
			s_enabled = enabled;
			SceneView.RepaintAll();
			Debug.Log($"[DebugAORenderer] SetEnabled: {enabled}");
		}

		public static bool IsEnabled() => s_enabled;

		public static void SetSphere(Vector3 center, float radius, Color? color = null)
		{
			s_sphereCenter = center;
			s_sphereRadius = Mathf.Max(0f, radius);
			s_showSphere = true;
			if (color.HasValue) s_sphereColor = color.Value;
			SceneView.RepaintAll();
			Debug.Log($"[DebugAORenderer] Sphere set: center={center}, radius={s_sphereRadius:F3}");
		}

		public static void ClearSphere()
		{
			s_showSphere = false;
			SceneView.RepaintAll();
			Debug.Log("[DebugAORenderer] Sphere cleared.");
		}

		public static void AddRay(Vector3 origin, Vector3 direction, float length, bool hit, Color? hitColor = null, Color? missColor = null)
		{
			if (!s_enabled) return;
			RayDebug r = new RayDebug
			{
				origin = origin,
				dir = (direction.sqrMagnitude > 0f) ? direction.normalized : Vector3.forward,
				length = Mathf.Max(0f, length),
				hit = hit,
				createdTime = EditorApplication.timeSinceStartup
			};
			r.color = hit ? (hitColor ?? new Color(1f, 0.4f, 0.2f, 0.95f)) : (missColor ?? new Color(1f, 1f, 1f, 0.9f));
			lock (s_rays)
			{
				s_rays.Add(r);
				s_totalRaysAdded++;
				// Keep list from growing unbounded
				if (s_rays.Count > 20000) s_rays.RemoveRange(0, s_rays.Count - 20000);
			}

			// Log the first few rays for diagnostics
			if (s_totalRaysAdded <= s_logRaysThreshold)
			{
				Debug.Log($"[DebugAORenderer] AddRay #{s_totalRaysAdded}: origin={origin}, dir={r.dir}, len={length:F3}, hit={hit}");
			}

			SceneView.RepaintAll();
		}

		public static void ClearRays()
		{
			lock (s_rays) { s_rays.Clear(); }
			s_totalRaysAdded = 0;
			SceneView.RepaintAll();
			Debug.Log("[DebugAORenderer] Rays cleared.");
		}

		public static void ClearAll()
		{
			ClearRays();
			ClearSphere();
		}

		public static void SetAutoExpireRays(bool enabled, float lifetimeSeconds = 60f)
		{
			float clampedLifetime = Mathf.Max(0.01f, lifetimeSeconds);
			if (s_autoExpireRays == enabled && Mathf.Approximately(s_rayLifetime, clampedLifetime)) return;
			s_autoExpireRays = enabled;
			s_rayLifetime = clampedLifetime;
			Debug.Log($"[DebugAORenderer] Auto-expire rays: {enabled}, lifetime={s_rayLifetime}s");
		}

		public static int GetRayCount()
		{
			lock (s_rays) { return s_rays.Count; }
		}

		// Menu toggle for convenience
		[MenuItem("Window/AO Debug/Toggle AO Debug Renderer")]
		static void ToggleMenu()
		{
			SetEnabled(!s_enabled);
		}

		// Internal drawing ------------------------------------------------

		static void OnEditorUpdate()
		{
			if (!s_enabled) return;
			if (s_autoExpireRays)
			{
				double now = EditorApplication.timeSinceStartup;
				bool removed = false;
				lock (s_rays)
				{
					for (int i = s_rays.Count - 1; i >= 0; --i)
					{
						if (now - s_rays[i].createdTime > s_rayLifetime)
						{
							s_rays.RemoveAt(i);
							removed = true;
						}
					}
				}
				if (removed) SceneView.RepaintAll();
			}
		}

		static void OnSceneGUI(SceneView sv)
		{
			if (!s_enabled) return;

			// Make sure gizmos are visible even if occluded
			Handles.zTest = UnityEngine.Rendering.CompareFunction.Always;

			// Draw sphere as three wire discs (XZ, XY, YZ)
			if (s_showSphere && s_sphereRadius > 0f)
			{
				Color prev = Handles.color;
				Handles.color = s_sphereColor;
				Handles.DrawWireDisc(s_sphereCenter, Vector3.up, s_sphereRadius);
				Handles.DrawWireDisc(s_sphereCenter, Vector3.forward, s_sphereRadius);
				Handles.DrawWireDisc(s_sphereCenter, Vector3.right, s_sphereRadius);

				Handles.color = new Color(s_sphereColor.r, s_sphereColor.g, s_sphereColor.b, 0.08f);
				Handles.DrawSolidDisc(s_sphereCenter, sv.camera.transform.forward, s_sphereRadius * 0.02f);

				Handles.color = s_sphereColor;
				Handles.Label(s_sphereCenter + Vector3.up * (s_sphereRadius + 0.1f), $"AO Radius: {s_sphereRadius:F3}");
				Handles.color = prev;
			}

			// Draw rays
			lock (s_rays)
			{
				for (int i = 0; i < s_rays.Count; ++i)
				{
					DrawDebugRay(s_rays[i]);
				}
			}
		}

		static void DrawDebugRay(RayDebug r)
		{
			Color prev = Handles.color;
			Handles.color = r.color;

			Vector3 end = r.origin + r.dir * r.length;
			Handles.DrawAAPolyLine(3f, new Vector3[] { r.origin, end });

			DrawArrowHead(end, -r.dir, 0.06f * Mathf.Max(1f, r.length * 0.02f), r.color);

			if (r.hit)
			{
				Handles.color = Color.yellow;
				Handles.SphereHandleCap(0, end, Quaternion.identity, Mathf.Max(0.01f, r.length * 0.01f), EventType.Repaint);
			}

			Handles.color = prev;
		}

		static void DrawArrowHead(Vector3 pos, Vector3 dir, float size, Color color)
		{
			Vector3 n = dir.normalized;
			Vector3 tangent = Vector3.Cross(n, Vector3.up);
			if (tangent.sqrMagnitude < 1e-6f) tangent = Vector3.Cross(n, Vector3.right);
			tangent.Normalize();
			Vector3 bit = Vector3.Cross(n, tangent).normalized;

			Vector3 p1 = pos + (tangent * size * 0.6f) + (n * size * 0.2f);
			Vector3 p2 = pos - (tangent * size * 0.6f) + (n * size * 0.2f);
			Vector3 p3 = pos + (bit * size * 0.6f) + (n * size * 0.2f);
			Vector3 p4 = pos - (bit * size * 0.6f) + (n * size * 0.2f);

			Handles.DrawAAConvexPolygon(new Vector3[] { pos, p1, p3 });
			Handles.DrawAAConvexPolygon(new Vector3[] { pos, p3, p2 });
			Handles.DrawAAConvexPolygon(new Vector3[] { pos, p2, p4 });
			Handles.DrawAAConvexPolygon(new Vector3[] { pos, p4, p1 });
		}
	}
}
#endif
