using UnityEditor;
using UnityEngine;

public class ORMEStandardShaderGUI : ShaderGUI
{
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

			ORMEStandardShaderMaterialUtility.ApplyRenderMode(material);
		}
	}

	public override void AssignNewShaderToMaterial(Material material, Shader oldShader, Shader newShader)
	{
		base.AssignNewShaderToMaterial(material, oldShader, newShader);
		ORMEStandardShaderMaterialUtility.ApplyRenderMode(material);
	}
}