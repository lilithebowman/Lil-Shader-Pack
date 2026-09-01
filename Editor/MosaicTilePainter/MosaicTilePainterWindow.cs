using UnityEngine;
using UnityEditor;

public class MosaicTilePainterWindow : EditorWindow
{
	private TilePlacementSettings _settings;
	private TilePlacer _placer;
	private bool _enabled;

	private TilePreviewGhost _ghost = new TilePreviewGhost();
	private TileDragPainter _dragPainter = new TileDragPainter();
	private bool _deleteMode;
	private bool _snapRotation = true;
	private bool _painterMode;

	// Painter Y lock
	private float _painterLockedY;
	private bool _painterYLocked;

	// Painter brush + pattern
	private int _brushSize = 1;
	private PaintPattern _paintPattern = PaintPattern.Solid;

	[MenuItem("Lilithe/Mosaic/Mosaic Tile Painter")]
	public static void ShowWindow()
	{
		GetWindow<MosaicTilePainterWindow>("Mosaic Tile Painter");
	}

	private void OnEnable()
	{
		SceneView.duringSceneGui += OnSceneGUI;
	}

	private void OnDisable()
	{
		SceneView.duringSceneGui -= OnSceneGUI;
	}

	private void OnGUI()
	{
		EditorGUILayout.LabelField("Mosaic Tile Painter", EditorStyles.boldLabel);

		_snapRotation = EditorGUILayout.Toggle("Snap Rotation", _snapRotation);
		_painterMode = EditorGUILayout.Toggle("Painter Mode", _painterMode);
		_deleteMode = EditorGUILayout.Toggle("Delete Mode", _deleteMode);
		_enabled = EditorGUILayout.Toggle("Editor Mode Enabled", _enabled);

		// Make modes mutually exclusive
		if (_painterMode) _deleteMode = false;
		if (_deleteMode) _painterMode = false;

		// Painter options
		if (_painterMode)
		{
			EditorGUILayout.Space();
			EditorGUILayout.LabelField("Painter Options", EditorStyles.boldLabel);

			_brushSize = EditorGUILayout.IntSlider("Brush Size", _brushSize, 1, 9);
			if (_brushSize < 1) _brushSize = 1;

			_paintPattern = (PaintPattern)EditorGUILayout.EnumPopup("Pattern", _paintPattern);
		}

		_settings = (TilePlacementSettings)EditorGUILayout.ObjectField(
			"Placement Settings",
			_settings,
			typeof(TilePlacementSettings),
			false
		);

		if (_settings != null)
		{
			EditorGUILayout.Space();
			EditorGUILayout.LabelField("Material Overrides", EditorStyles.boldLabel);

			_settings.overrideTileMaterial = (Material)EditorGUILayout.ObjectField(
				"Tile Material",
				_settings.overrideTileMaterial != null ? _settings.overrideTileMaterial : _settings.defaultTileMaterial,
				typeof(Material),
				false
			);

			_settings.overrideGroutMaterial = (Material)EditorGUILayout.ObjectField(
				"Grout Material",
				_settings.overrideGroutMaterial != null ? _settings.overrideGroutMaterial : _settings.groutMaterial,
				typeof(Material),
				false
			);

			EditorUtility.SetDirty(_settings);
		}

		if (_placer == null)
		{
			if (GUILayout.Button("Find / Create TilePlacer"))
				FindOrCreatePlacer();
		}
		else
		{
			EditorGUILayout.ObjectField("Active Placer", _placer, typeof(TilePlacer), true);
		}
	}

	private void FindOrCreatePlacer()
	{
		_placer = Object.FindObjectOfType<TilePlacer>();
		if (_placer == null)
		{
			GameObject go = new GameObject("TilePlacer");
			_placer = go.AddComponent<TilePlacer>();
		}

		if (_settings != null)
			_placer.settings = _settings;
	}

	private void OnSceneGUI(SceneView sceneView)
	{
		if (!_enabled || _placer == null || _placer.settings == null)
			return;

		Event e = Event.current;
		Ray ray = HandleUtility.GUIPointToWorldRay(e.mousePosition);

		if (!Physics.Raycast(ray, out RaycastHit hit))
			return;

		float spacing = _placer.settings.tileWidth;
		Vector2Int cell = new Vector2Int(
			Mathf.RoundToInt(hit.point.x / spacing),
			Mathf.RoundToInt(hit.point.z / spacing)
		);

		// Ghost: snap to grid in painter mode, otherwise follow surface
		if (_painterMode)
		{
			if (!_painterYLocked)
				_painterLockedY = hit.point.y;

			_ghost.UpdateGhostGrid(cell, _painterLockedY, _placer.settings);
		}
		else
		{
			_ghost.UpdateGhost(hit, _placer.settings);
		}

		if (_deleteMode)
		{
			HandleDeleteMode(e, hit);
			return;
		}

		if (_painterMode)
		{
			HandlePainterMode(e, cell);
			return;
		}

		HandlePlacerMode(e, hit);
	}

	private void HandleDeleteMode(Event e, RaycastHit hit)
	{
		if (e.type == EventType.MouseDown && e.button == 0)
		{
			if (TileDeletionTool.TryDeleteTile(hit))
				GroutGenerator.RebuildAllGrout(_placer.GetTiles(), _placer.settings);

			e.Use();
		}
	}

	private void HandlePlacerMode(Event e, RaycastHit hit)
	{
		// Placer mode: ONLY place on MouseDown
		if (e.type == EventType.MouseDown && e.button == 0)
		{
			GameObject tile = _placer.PlaceTileAtHit(hit);

			if (_snapRotation && tile != null)
				TileRotationSnapper.SnapRotation(tile.transform);

			GroutGenerator.RebuildAllGrout(_placer.GetTiles(), _placer.settings);

			e.Use();
		}
	}

	private void HandlePainterMode(Event e, Vector2Int centerCell)
	{
		if (!_placer.settings.snapToGrid)
		{
			Debug.LogWarning("Painter Mode requires Snap To Grid enabled.");
			return;
		}

		// Lock Y on first click
		if (!_painterYLocked && e.type == EventType.MouseDown && e.button == 0)
			_painterYLocked = true;

		// Painter mode: MouseDown + MouseDrag
		if ((e.type == EventType.MouseDown && e.button == 0) ||
			(e.type == EventType.MouseDrag && e.button == 0))
		{
			foreach (var cell in PainterBrush.GetCells(centerCell, _brushSize))
			{
				if (!PainterPattern.ShouldPaint(cell, _paintPattern))
					continue;

				GameObject tile = _placer.PaintTileAtCell(cell, _painterLockedY);

				if (tile != null && _snapRotation)
					TileRotationSnapper.SnapRotation(tile.transform);
			}

			GroutGenerator.RebuildAllGrout(_placer.GetTiles(), _placer.settings);

			e.Use();
		}

		// Reset lock when mouse is released
		if (e.type == EventType.MouseUp)
			_painterYLocked = false;
	}
}
