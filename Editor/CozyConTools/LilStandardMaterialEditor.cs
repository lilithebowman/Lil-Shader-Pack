using UnityEngine;
using UnityEditor;

[CustomEditor(typeof(Material))]
public class LilStandardMaterialEditor : MaterialEditor
{
	public override void OnInspectorGUI()
	{
		base.OnInspectorGUI();

		Material targetMat = target as Material;
		if (targetMat == null) return;

		// Only apply custom logic if using the correct shader
		if (targetMat.shader != null && targetMat.shader.name == "Lilithe/Lil Standard")
		{
			if (targetMat.HasProperty("_BlendMode"))
			{
				int blendMode = Mathf.RoundToInt(targetMat.GetFloat("_BlendMode"));
				switch (blendMode)
				{
					case 0: // Opaque
						targetMat.SetOverrideTag("RenderType", "Opaque");
						targetMat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Geometry;
						targetMat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.One);
						targetMat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.Zero);
						targetMat.SetInt("_ZWrite", 1);
						break;
					case 1: // Cutout
						targetMat.SetOverrideTag("RenderType", "TransparentCutout");
						targetMat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.AlphaTest;
						targetMat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.One);
						targetMat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.Zero);
						targetMat.SetInt("_ZWrite", 1);
						break;
					case 3: // Transparent
					case 4: // Fade
						targetMat.SetOverrideTag("RenderType", "Transparent");
						targetMat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
						targetMat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
						targetMat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
						targetMat.SetInt("_ZWrite", 0);
						break;
				}
			}
		}
	}
}
