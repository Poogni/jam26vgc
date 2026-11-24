using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Level painter: select a LevelData asset and paint its tile matrix directly in the Inspector.
/// Left mouse paints with the selected tile, right mouse erases to rock. North is up.
/// </summary>
[CustomEditor(typeof(LevelData))]
public class LevelDataEditor : Editor
{
    private enum PaintTool { Brush, Rectangle, Fill, PlayerStart }

    private static readonly string[] ToolNames = { "Brush", "Rectangle", "Fill", "Player Start" };
    private static readonly Color RockColor = new Color(0.12f, 0.12f, 0.13f);
    private static readonly Color GridLineColor = new Color(0.05f, 0.05f, 0.05f);
    private static readonly Color SelectedColor = new Color(0.25f, 0.5f, 0.95f);

    // Static so the chosen tool/brush survives selecting another level.
    private static PaintTool _tool = PaintTool.Brush;
    private static TileDefinition _brush;
    private static bool _brushIsRock;
    private static float _zoom = 24f;

    private TileDefinition[] _palette = new TileDefinition[0];
    private int _newWidth, _newHeight;

    private bool _rectActive, _strokeErase, _strokeChanged;
    private Vector2Int _rectStart, _rectEnd;
    private Vector2Int? _lastCell;

    private LevelData Level => (LevelData)target;

    private void OnEnable()
    {
        RefreshPalette();
        _newWidth = Level.Width;
        _newHeight = Level.Height;
        Undo.undoRedoPerformed += OnUndoRedo;
    }

    private void OnDisable() => Undo.undoRedoPerformed -= OnUndoRedo;

    private void OnUndoRedo()
    {
        if (target == null) return;
        _newWidth = Level.Width;
        _newHeight = Level.Height;
        LevelPreview.Rebuild(Level);
        Repaint();
    }

    private void RefreshPalette()
    {
        _palette = AssetDatabase.FindAssets("t:" + nameof(TileDefinition))
            .Select(g => AssetDatabase.LoadAssetAtPath<TileDefinition>(AssetDatabase.GUIDToAssetPath(g)))
            .Where(t => t != null)
            .OrderBy(t => t.name)
            .ToArray();
        if (_brush == null && !_brushIsRock && _palette.Length > 0) _brush = _palette[0];
    }

    public override void OnInspectorGUI()
    {
        DrawSettings();
        EditorGUILayout.Space(8);
        DrawSize();
        EditorGUILayout.Space(8);
        DrawPalette();
        EditorGUILayout.Space(4);
        DrawToolbar();
        EditorGUILayout.Space(4);
        DrawGrid();
        EditorGUILayout.Space(4);
        DrawFooter();
    }

    // ------------------------------------------------------------------ settings

    private void DrawSettings()
    {
        serializedObject.Update();
        EditorGUILayout.LabelField("Look", EditorStyles.boldLabel);
        EditorGUI.BeginChangeCheck();
        EditorGUILayout.PropertyField(serializedObject.FindProperty("wallHeight"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("hasCeiling"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("wallPrefab"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("floorPrefab"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("ceilingPrefab"));
        bool changed = EditorGUI.EndChangeCheck();
        serializedObject.ApplyModifiedProperties();
        if (changed) LevelPreview.Rebuild(Level);
    }

    private void DrawSize()
    {
        EditorGUILayout.LabelField("Size (cells)", EditorStyles.boldLabel);
        using (new EditorGUILayout.HorizontalScope())
        {
            EditorGUIUtility.labelWidth = 45;
            _newWidth = Mathf.Clamp(EditorGUILayout.IntField("Width", _newWidth), 1, LevelData.MaxSize);
            _newHeight = Mathf.Clamp(EditorGUILayout.IntField("Height", _newHeight), 1, LevelData.MaxSize);
            EditorGUIUtility.labelWidth = 0;

            bool dirty = _newWidth != Level.Width || _newHeight != Level.Height;
            using (new EditorGUI.DisabledScope(!dirty))
            {
                if (GUILayout.Button("Apply", GUILayout.Width(60)))
                {
                    Undo.RecordObject(Level, "Resize Level");
                    Level.Resize(_newWidth, _newHeight);
                    Commit();
                }
            }
        }
    }

    // ------------------------------------------------------------------ palette

    private void DrawPalette()
    {
        using (new EditorGUILayout.HorizontalScope())
        {
            EditorGUILayout.LabelField("Tiles", EditorStyles.boldLabel);
            if (GUILayout.Button("New Tile…", EditorStyles.miniButtonLeft, GUILayout.Width(70))) CreateTile();
            if (GUILayout.Button("Refresh", EditorStyles.miniButtonRight, GUILayout.Width(60))) RefreshPalette();
        }

        if (_palette.Length == 0)
        {
            EditorGUILayout.HelpBox("No tiles in the project yet.", MessageType.Info);
            if (GUILayout.Button("Create Starter Tiles")) { DungeonSetupMenu.CreateStarterTiles(); RefreshPalette(); }
        }

        // Entry 0 is "Rock" (null = erase).
        var entries = new List<(TileDefinition tile, string label, Color color)> { (null, "Rock (erase)", RockColor) };
        entries.AddRange(_palette.Select(t => (t, t.Label, t.EditorColor)));

        const float buttonWidth = 120f, buttonHeight = 22f;
        int perRow = Mathf.Max(1, Mathf.FloorToInt((EditorGUIUtility.currentViewWidth - 30f) / (buttonWidth + 4f)));

        for (int i = 0; i < entries.Count; i += perRow)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                for (int j = i; j < Mathf.Min(i + perRow, entries.Count); j++)
                {
                    var (tile, label, color) = entries[j];
                    bool selected = tile == null ? _brushIsRock : (!_brushIsRock && _brush == tile);
                    Rect r = GUILayoutUtility.GetRect(buttonWidth, buttonHeight, GUILayout.Width(buttonWidth));
                    if (PaletteButton(r, label, color, selected, j <= 9 ? (j == 9 ? 0 : j + 1) : -1))
                        SelectBrush(tile);
                    if (tile != null && Event.current.type == EventType.MouseDown && Event.current.clickCount == 2 &&
                        r.Contains(Event.current.mousePosition))
                    {
                        EditorGUIUtility.PingObject(tile);
                    }
                }
                GUILayout.FlexibleSpace();
            }
        }

        // Number keys 1-9 / 0 pick a brush while the inspector has focus.
        Event e = Event.current;
        if (e.type == EventType.KeyDown && e.keyCode >= KeyCode.Alpha0 && e.keyCode <= KeyCode.Alpha9)
        {
            int index = e.keyCode == KeyCode.Alpha0 ? 9 : e.keyCode - KeyCode.Alpha1;
            if (index < entries.Count)
            {
                SelectBrush(entries[index].tile);
                e.Use();
            }
        }
    }

    private void SelectBrush(TileDefinition tile)
    {
        _brushIsRock = tile == null;
        _brush = tile;
        if (_tool == PaintTool.PlayerStart) _tool = PaintTool.Brush;
        GUI.FocusControl(null);
    }

    private static bool PaletteButton(Rect r, string label, Color color, bool selected, int hotkey)
    {
        EditorGUI.DrawRect(r, selected ? SelectedColor : new Color(0f, 0f, 0f, 0.15f));
        var swatch = new Rect(r.x + 3, r.y + 3, r.height - 6, r.height - 6);
        EditorGUI.DrawRect(swatch, color);
        var labelRect = new Rect(swatch.xMax + 4, r.y, r.width - swatch.width - 10, r.height);
        string text = hotkey >= 0 ? $"{hotkey}  {label}" : label;
        GUI.Label(labelRect, text, selected ? EditorStyles.whiteLabel : EditorStyles.label);
        return GUI.Button(r, GUIContent.none, GUIStyle.none);
    }

    private void CreateTile()
    {
        string folder = _palette.Length > 0
            ? System.IO.Path.GetDirectoryName(AssetDatabase.GetAssetPath(_palette[0]))
            : "Assets";
        string path = EditorUtility.SaveFilePanelInProject("New Tile", "Tile_New", "asset", "Name the new tile", folder);
        if (string.IsNullOrEmpty(path)) return;

        var tile = CreateInstance<TileDefinition>();
        AssetDatabase.CreateAsset(tile, path);
        AssetDatabase.SaveAssets();
        RefreshPalette();
        SelectBrush(tile);
        Selection.activeObject = tile; // jump to it so the designer can set color/shape
    }

    // ------------------------------------------------------------------ toolbar

    private void DrawToolbar()
    {
        _tool = (PaintTool)GUILayout.Toolbar((int)_tool, ToolNames);
        _zoom = EditorGUILayout.Slider("Zoom", _zoom, 8f, 48f);

        string help = _tool switch
        {
            PaintTool.Brush => "Left-drag: paint   •   Right-drag: erase to rock",
            PaintTool.Rectangle => "Left-drag: fill a rectangle   •   Right-drag: erase a rectangle",
            PaintTool.Fill => "Left-click: flood fill   •   Right-click: flood erase",
            _ => "Click a cell to place the start. Click it again to rotate the facing.",
        };
        EditorGUILayout.LabelField(help, EditorStyles.centeredGreyMiniLabel);
    }

    // ------------------------------------------------------------------ grid

    private void DrawGrid()
    {
        LevelData level = Level;
        int w = level.Width, h = level.Height;

        float available = EditorGUIUtility.currentViewWidth - 40f;
        float cs = Mathf.Max(4f, Mathf.Floor(Mathf.Min(_zoom, available / w)));

        Rect area;
        using (new EditorGUILayout.HorizontalScope())
        {
            GUILayout.FlexibleSpace();
            area = GUILayoutUtility.GetRect(w * cs, h * cs, GUILayout.Width(w * cs), GUILayout.Height(h * cs));
            GUILayout.FlexibleSpace();
        }

        HandleGridInput(area, cs);

        if (Event.current.type == EventType.Repaint) PaintGrid(area, cs);
    }

    private Vector2Int MouseToCell(Rect area, float cs, Vector2 mouse)
    {
        int x = Mathf.FloorToInt((mouse.x - area.x) / cs);
        int row = Mathf.FloorToInt((mouse.y - area.y) / cs);
        return new Vector2Int(x, Level.Height - 1 - row); // row 0 is the north edge
    }

    private Rect CellRect(Rect area, float cs, Vector2Int c) =>
        new Rect(area.x + c.x * cs, area.y + (Level.Height - 1 - c.y) * cs, cs, cs);

    private void HandleGridInput(Rect area, float cs)
    {
        int id = GUIUtility.GetControlID(FocusType.Passive);
        Event e = Event.current;
        LevelData level = Level;

        switch (e.GetTypeForControl(id))
        {
            case EventType.MouseDown:
            {
                if (!area.Contains(e.mousePosition) || (e.button != 0 && e.button != 1)) break;
                Vector2Int cell = MouseToCell(area, cs, e.mousePosition);
                _lastCell = cell;
                _strokeErase = e.button == 1;
                _strokeChanged = false;
                GUIUtility.hotControl = id;
                Undo.RecordObject(level, "Paint Level");

                switch (_tool)
                {
                    case PaintTool.Brush:
                        _strokeChanged |= level.SetTile(cell, StrokeTile);
                        break;
                    case PaintTool.Rectangle:
                        _rectActive = true;
                        _rectStart = _rectEnd = cell;
                        break;
                    case PaintTool.Fill:
                        _strokeChanged |= FloodFill(cell, StrokeTile);
                        break;
                    case PaintTool.PlayerStart:
                        if (!_strokeErase)
                        {
                            Facing facing = cell == level.PlayerStart ? level.StartFacing.Rotate(1) : level.StartFacing;
                            level.SetPlayerStart(cell, facing);
                            _strokeChanged = true;
                        }
                        break;
                }
                e.Use();
                Repaint();
                break;
            }

            case EventType.MouseDrag:
            {
                if (GUIUtility.hotControl != id) break;
                Vector2Int cell = MouseToCell(area, cs, e.mousePosition);
                _lastCell = cell;
                if (_tool == PaintTool.Brush) _strokeChanged |= level.SetTile(cell, StrokeTile);
                else if (_tool == PaintTool.Rectangle)
                    _rectEnd = new Vector2Int(Mathf.Clamp(cell.x, 0, level.Width - 1), Mathf.Clamp(cell.y, 0, level.Height - 1));
                e.Use();
                Repaint();
                break;
            }

            case EventType.MouseUp:
            {
                if (GUIUtility.hotControl != id) break;
                GUIUtility.hotControl = 0;
                if (_tool == PaintTool.Rectangle && _rectActive)
                {
                    _rectActive = false;
                    _strokeChanged |= FillRect(_rectStart, _rectEnd, StrokeTile);
                }
                if (_strokeChanged) Commit();
                e.Use();
                Repaint();
                break;
            }
        }
    }

    private TileDefinition StrokeTile => _strokeErase || _brushIsRock ? null : _brush;

    private bool FillRect(Vector2Int a, Vector2Int b, TileDefinition tile)
    {
        bool changed = false;
        for (int y = Mathf.Min(a.y, b.y); y <= Mathf.Max(a.y, b.y); y++)
        for (int x = Mathf.Min(a.x, b.x); x <= Mathf.Max(a.x, b.x); x++)
            changed |= Level.SetTile(new Vector2Int(x, y), tile);
        return changed;
    }

    private bool FloodFill(Vector2Int start, TileDefinition tile)
    {
        LevelData level = Level;
        if (!level.InBounds(start)) return false;
        TileDefinition from = level.GetTile(start);
        if (from == tile) return false;

        var stack = new Stack<Vector2Int>();
        stack.Push(start);
        while (stack.Count > 0)
        {
            Vector2Int c = stack.Pop();
            if (!level.InBounds(c) || level.GetTile(c) != from) continue;
            level.SetTile(c, tile);
            stack.Push(c + Vector2Int.up);
            stack.Push(c + Vector2Int.down);
            stack.Push(c + Vector2Int.left);
            stack.Push(c + Vector2Int.right);
        }
        return true;
    }

    private void PaintGrid(Rect area, float cs)
    {
        LevelData level = Level;
        EditorGUI.DrawRect(area, GridLineColor);

        GUIStyle letterStyle = new GUIStyle(EditorStyles.miniBoldLabel)
        {
            alignment = TextAnchor.MiddleCenter,
            fontSize = Mathf.Clamp(Mathf.RoundToInt(cs * 0.45f), 8, 16),
        };

        for (int y = 0; y < level.Height; y++)
        for (int x = 0; x < level.Width; x++)
        {
            var c = new Vector2Int(x, y);
            TileDefinition tile = level.GetTile(c);
            Rect r = CellRect(area, cs, c);
            Rect inner = new Rect(r.x + 0.5f, r.y + 0.5f, r.width - 1f, r.height - 1f);
            Color color = tile != null ? tile.EditorColor : RockColor;
            EditorGUI.DrawRect(inner, color);

            // First letter of the tile name helps tell similar colors apart.
            if (tile != null && cs >= 14f && !string.IsNullOrEmpty(tile.Label))
            {
                letterStyle.normal.textColor = Luminance(color) > 0.5f
                    ? new Color(0f, 0f, 0f, 0.55f)
                    : new Color(1f, 1f, 1f, 0.6f);
                GUI.Label(inner, tile.Label.Substring(0, 1).ToUpperInvariant(), letterStyle);
            }
        }

        // Rectangle preview.
        if (_rectActive)
        {
            Vector2Int min = Vector2Int.Min(_rectStart, _rectEnd), max = Vector2Int.Max(_rectStart, _rectEnd);
            Rect a = CellRect(area, cs, new Vector2Int(min.x, max.y));
            Rect b = CellRect(area, cs, new Vector2Int(max.x, min.y));
            Rect preview = Rect.MinMaxRect(a.xMin, a.yMin, b.xMax, b.yMax);
            Color tint = StrokeTile != null ? StrokeTile.EditorColor : RockColor;
            tint.a = 0.6f;
            EditorGUI.DrawRect(preview, tint);
            DrawOutline(preview, Color.white);
        }

        // Every 5 cells a slightly brighter line, for counting.
        var major = new Color(1f, 1f, 1f, 0.12f);
        for (int x = 5; x < level.Width; x += 5)
            EditorGUI.DrawRect(new Rect(area.x + x * cs - 0.5f, area.y, 1f, area.height), major);
        for (int y = 5; y < level.Height; y += 5)
            EditorGUI.DrawRect(new Rect(area.x, area.yMax - y * cs - 0.5f, area.width, 1f), major);

        DrawStartArrow(CellRect(area, cs, level.PlayerStart), level.StartFacing);

        // North marker.
        GUI.Label(new Rect(area.xMax - 20, area.y - 16, 20, 16), "N ↑", EditorStyles.miniBoldLabel);
    }

    private static void DrawStartArrow(Rect cell, Facing facing)
    {
        Vector2 center = cell.center;
        float s = cell.width * 0.35f;
        // Screen-space: up is -y.
        Vector2 dir = facing switch
        {
            Facing.North => Vector2.down,
            Facing.East => Vector2.right,
            Facing.South => Vector2.up,
            _ => Vector2.left,
        };
        Vector2 side = new Vector2(-dir.y, dir.x);
        Vector3 tip = center + dir * s;
        Vector3 left = center - dir * s * 0.7f + side * s * 0.8f;
        Vector3 right = center - dir * s * 0.7f - side * s * 0.8f;

        Handles.color = new Color(0.15f, 1f, 0.35f);
        Handles.DrawAAConvexPolygon(tip, left, right);
        Handles.color = Color.black;
        Handles.DrawAAPolyLine(2f, tip, left, right, tip);
    }

    private static void DrawOutline(Rect r, Color c)
    {
        EditorGUI.DrawRect(new Rect(r.x, r.y, r.width, 1), c);
        EditorGUI.DrawRect(new Rect(r.x, r.yMax - 1, r.width, 1), c);
        EditorGUI.DrawRect(new Rect(r.x, r.y, 1, r.height), c);
        EditorGUI.DrawRect(new Rect(r.xMax - 1, r.y, 1, r.height), c);
    }

    private static float Luminance(Color c) => 0.299f * c.r + 0.587f * c.g + 0.114f * c.b;

    // ------------------------------------------------------------------ footer

    private void DrawFooter()
    {
        LevelData level = Level;

        string hover = _lastCell.HasValue && level.InBounds(_lastCell.Value)
            ? $"Last cell: ({_lastCell.Value.x}, {_lastCell.Value.y})  {(level.GetTile(_lastCell.Value)?.Label ?? "Rock")}"
            : " ";
        EditorGUILayout.LabelField(hover, EditorStyles.miniLabel);
        EditorGUILayout.LabelField(
            $"Player start: ({level.PlayerStart.x}, {level.PlayerStart.y}) facing {level.StartFacing}",
            EditorStyles.miniLabel);

        if (!level.IsWalkable(level.PlayerStart))
            EditorGUILayout.HelpBox("The player start is not on a walkable tile.", MessageType.Warning);

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Fill All With Brush") &&
                EditorUtility.DisplayDialog("Fill level", "Replace every cell with the selected tile?", "Fill", "Cancel"))
            {
                Undo.RecordObject(level, "Fill Level");
                FillRect(Vector2Int.zero, new Vector2Int(level.Width - 1, level.Height - 1), _brushIsRock ? null : _brush);
                Commit();
            }
            if (GUILayout.Button("Clear To Rock") &&
                EditorUtility.DisplayDialog("Clear level", "Turn every cell back into rock?", "Clear", "Cancel"))
            {
                Undo.RecordObject(level, "Clear Level");
                FillRect(Vector2Int.zero, new Vector2Int(level.Width - 1, level.Height - 1), null);
                Commit();
            }
        }

        if (GUILayout.Button("Rebuild Scene Preview")) LevelPreview.Rebuild(level, force: true);
    }

    private void Commit()
    {
        EditorUtility.SetDirty(Level);
        LevelPreview.Rebuild(Level);
    }
}

/// <summary>Rebuilds every LevelBuilder in the open scenes that shows the given level.</summary>
public static class LevelPreview
{
    public static void Rebuild(LevelData level, bool force = false)
    {
        if (level == null || EditorApplication.isPlayingOrWillChangePlaymode) return;
        foreach (LevelBuilder builder in Object.FindObjectsByType<LevelBuilder>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
        {
            if (builder.Level == level && (force || builder.LivePreview)) builder.Build();
        }
    }
}
