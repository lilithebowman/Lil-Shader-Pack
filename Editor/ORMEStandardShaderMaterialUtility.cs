using UnityEngine;
using UnityEngine.Rendering;

public static class ORMEStandardShaderMaterialUtility
{
	private enum RenderMode
	{
		Opaque = 0,
		Cutout = 1,
		Fade = 2,
		Transparent = 3,
	}

	public static void ApplyRenderMode(Material material)
	{
		if (material == null || !material.HasProperty("_Mode"))
		{
			return;
		}

		switch ((RenderMode)Mathf.RoundToInt(material.GetFloat("_Mode")))
		{
			case RenderMode.Opaque:
				SetOverrideTag(material, "RenderType", "Opaque");
				SetFloat(material, "_SrcBlend", (float)BlendMode.One);
				SetFloat(material, "_DstBlend", (float)BlendMode.Zero);
				SetFloat(material, "_ZWrite", 1.0f);
				SetRenderQueue(material, (int)RenderQueue.Geometry);
				SetKeyword(material, "_ALPHATEST_ON", false);
				SetKeyword(material, "_ALPHABLEND_ON", false);
				SetKeyword(material, "_ALPHAPREMULTIPLY_ON", false);
				break;

			case RenderMode.Cutout:
				SetOverrideTag(material, "RenderType", "TransparentCutout");
				SetFloat(material, "_SrcBlend", (float)BlendMode.One);
				SetFloat(material, "_DstBlend", (float)BlendMode.Zero);
				SetFloat(material, "_ZWrite", 1.0f);
				SetRenderQueue(material, (int)RenderQueue.AlphaTest);
				SetKeyword(material, "_ALPHATEST_ON", true);
				SetKeyword(material, "_ALPHABLEND_ON", false);
				SetKeyword(material, "_ALPHAPREMULTIPLY_ON", false);
				break;

			case RenderMode.Fade:
			case RenderMode.Transparent:
				SetOverrideTag(material, "RenderType", "Transparent");
				SetFloat(material, "_SrcBlend", (float)BlendMode.SrcAlpha);
				SetFloat(material, "_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
				SetFloat(material, "_ZWrite", 0.0f);
				SetRenderQueue(material, (int)RenderQueue.Transparent);
				SetKeyword(material, "_ALPHATEST_ON", false);
				SetKeyword(material, "_ALPHABLEND_ON", true);
				SetKeyword(material, "_ALPHAPREMULTIPLY_ON", false);
				break;
		}
	}

	private static void SetFloat(Material material, string propertyName, float value)
	{
		if (!material.HasProperty(propertyName) || Mathf.Approximately(material.GetFloat(propertyName), value))
		{
			return;
		}

		material.SetFloat(propertyName, value);
	}

	private static void SetRenderQueue(Material material, int renderQueue)
	{
		if (material.renderQueue == renderQueue)
		{
			return;
		}

		material.renderQueue = renderQueue;
	}

	private static void SetOverrideTag(Material material, string tagName, string value)
	{
		if (material.GetTag(tagName, false, string.Empty) == value)
		{
			return;
		}

		material.SetOverrideTag(tagName, value);
	}

	private static void SetKeyword(Material material, string keyword, bool enabled)
	{
		bool currentlyEnabled = material.IsKeywordEnabled(keyword);
		if (currentlyEnabled == enabled)
		{
			return;
		}

		if (enabled)
		{
			material.EnableKeyword(keyword);
		}
		else
		{
			material.DisableKeyword(keyword);
		}
	}
}