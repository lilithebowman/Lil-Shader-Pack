using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

public class ORMEStandardShaderGUI : ShaderGUI
{
	private enum RenderMode
	{
		Opaque = 0,
		Cutout = 1,
		Fade = 2,
		Transparent = 3,
	}

	public override void OnGUI(MaterialEditor materialEditor, MaterialProperty[] properties)
	{
		base.OnGUI(materialEditor, properties);

		foreach (Object target in materialEditor.targets)
		{
			Material material = target as Material;
			if (material == null)
			{
				continue;
			}

			ValidateMaterial(material);
		}
	}

	public override void AssignNewShaderToMaterial(Material material, Shader oldShader, Shader newShader)
	{
		base.AssignNewShaderToMaterial(material, oldShader, newShader);
		ValidateMaterial(material);
	}

	private static void ValidateMaterial(Material material)
	{
		if (!material.HasProperty("_Mode"))
		{
			return;
		}

		bool changed = false;
		RenderMode mode = (RenderMode)Mathf.RoundToInt(material.GetFloat("_Mode"));

		switch (mode)
		{
			case RenderMode.Opaque:
				changed |= SetOverrideTag(material, "RenderType", "Opaque");
				changed |= SetFloat(material, "_SrcBlend", (float)BlendMode.One);
				changed |= SetFloat(material, "_DstBlend", (float)BlendMode.Zero);
				changed |= SetFloat(material, "_ZWrite", 1.0f);
				changed |= SetRenderQueue(material, (int)RenderQueue.Geometry);
				changed |= SetKeyword(material, "_ALPHATEST_ON", false);
				changed |= SetKeyword(material, "_ALPHABLEND_ON", false);
				changed |= SetKeyword(material, "_ALPHAPREMULTIPLY_ON", false);
				break;

			case RenderMode.Cutout:
				changed |= SetOverrideTag(material, "RenderType", "TransparentCutout");
				changed |= SetFloat(material, "_SrcBlend", (float)BlendMode.One);
				changed |= SetFloat(material, "_DstBlend", (float)BlendMode.Zero);
				changed |= SetFloat(material, "_ZWrite", 1.0f);
				changed |= SetRenderQueue(material, (int)RenderQueue.AlphaTest);
				changed |= SetKeyword(material, "_ALPHATEST_ON", true);
				changed |= SetKeyword(material, "_ALPHABLEND_ON", false);
				changed |= SetKeyword(material, "_ALPHAPREMULTIPLY_ON", false);
				break;

			case RenderMode.Fade:
			case RenderMode.Transparent:
				changed |= SetOverrideTag(material, "RenderType", "Transparent");
				changed |= SetFloat(material, "_SrcBlend", (float)BlendMode.SrcAlpha);
				changed |= SetFloat(material, "_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
				changed |= SetFloat(material, "_ZWrite", 0.0f);
				changed |= SetRenderQueue(material, (int)RenderQueue.Transparent);
				changed |= SetKeyword(material, "_ALPHATEST_ON", false);
				changed |= SetKeyword(material, "_ALPHABLEND_ON", true);
				changed |= SetKeyword(material, "_ALPHAPREMULTIPLY_ON", false);
				break;
		}

		if (changed)
		{
			EditorUtility.SetDirty(material);
		}
	}

	private static bool SetFloat(Material material, string propertyName, float value)
	{
		if (!material.HasProperty(propertyName) || Mathf.Approximately(material.GetFloat(propertyName), value))
		{
			return false;
		}

		material.SetFloat(propertyName, value);
		return true;
	}

	private static bool SetRenderQueue(Material material, int renderQueue)
	{
		if (material.renderQueue == renderQueue)
		{
			return false;
		}

		material.renderQueue = renderQueue;
		return true;
	}

	private static bool SetOverrideTag(Material material, string tagName, string value)
	{
		if (material.GetTag(tagName, false, string.Empty) == value)
		{
			return false;
		}

		material.SetOverrideTag(tagName, value);
		return true;
	}

	private static bool SetKeyword(Material material, string keyword, bool enabled)
	{
		bool currentlyEnabled = material.IsKeywordEnabled(keyword);
		if (currentlyEnabled == enabled)
		{
			return false;
		}

		if (enabled)
		{
			material.EnableKeyword(keyword);
		}
		else
		{
			material.DisableKeyword(keyword);
		}

		return true;
	}
}