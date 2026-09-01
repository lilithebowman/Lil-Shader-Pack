using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(StyleGuideDocument))]
public class StyleGuideDocumentEditor : Editor
{
	private string folderPath = "Assets/"; // default

	public override void OnInspectorGUI()
	{
		DrawDefaultInspector();

		StyleGuideDocument doc = (StyleGuideDocument)target;

		EditorGUILayout.Space();
		EditorGUILayout.LabelField("Auto‑Populate Pages", EditorStyles.boldLabel);

		folderPath = EditorGUILayout.TextField("Folder Path", folderPath);

		if (GUILayout.Button("Load Sprites From Folder"))
		{
			LoadSprites(doc);
		}
	}

	private void LoadSprites(StyleGuideDocument doc)
	{
		string[] guids = AssetDatabase.FindAssets("t:Sprite", new[] { folderPath });

		Sprite[] sprites = new Sprite[guids.Length];

		for (int i = 0; i < guids.Length; i++)
		{
			string path = AssetDatabase.GUIDToAssetPath(guids[i]);
			sprites[i] = AssetDatabase.LoadAssetAtPath<Sprite>(path);
		}

		doc.pages = sprites;

		EditorUtility.SetDirty(doc);
		Debug.Log($"Loaded {sprites.Length} sprites into pages array.");
	}
}
