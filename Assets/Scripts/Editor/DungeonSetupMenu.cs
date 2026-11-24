using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>Editor helpers to drop the 3C rig, starter tiles and a sample level into the project.</summary>
public static class DungeonSetupMenu
{
    private const string TileFolder = "Assets/Level/Tiles";
    private const string LevelFolder = "Assets/Level/Levels";

    [MenuItem("Tools/Dungeon/Setup Sample Scene", priority = 0)]
    public static void SetupSampleScene()
    {
        LevelData level = CreateStarterLevel();
        if (Object.FindAnyObjectByType<GridPlayerController>() == null) CreatePlayerRig();
        LevelBuilder builder = Object.FindAnyObjectByType<LevelBuilder>() ?? CreateLevelBuilder(level);
        Selection.activeObject = level;
        EditorGUIUtility.PingObject(level);
        Debug.Log($"Dungeon sample ready. Edit '{level.name}' in the Inspector; '{builder.name}' rebuilds the scene as you paint.");
    }

    [MenuItem("Tools/Dungeon/Create Player Rig", priority = 20)]
    public static void CreatePlayerRig()
    {
        var player = new GameObject("Player");
        Undo.RegisterCreatedObjectUndo(player, "Create Player Rig");
        player.AddComponent<GridPlayerController>(); // adds DungeonInputReader via RequireComponent

        var head = new GameObject("Head");
        Undo.RegisterCreatedObjectUndo(head, "Create Player Rig");
        head.transform.SetParent(player.transform, false);
        head.transform.localPosition = new Vector3(0f, 1.6f, 0f);

        // Reuse the scene's main camera if there is one, otherwise make a new one.
        Camera cam = Camera.main;
        if (cam == null)
        {
            var camGo = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener)) { tag = "MainCamera" };
            Undo.RegisterCreatedObjectUndo(camGo, "Create Player Rig");
            cam = camGo.GetComponent<Camera>();
        }
        Undo.SetTransformParent(cam.transform, head.transform, "Create Player Rig");
        Undo.RecordObject(cam.transform, "Create Player Rig");
        cam.transform.localPosition = Vector3.zero;
        cam.transform.localRotation = Quaternion.identity;
        Undo.RecordObject(cam, "Create Player Rig");
        cam.fieldOfView = 75f;
        cam.nearClipPlane = 0.05f;

        Undo.AddComponent<FirstPersonCameraRig>(head);
        AddTorch(head.transform);

        Selection.activeGameObject = player;
    }

    /// <summary>Warm point light carried by the player; ceilings block the sun, so this is the main light.</summary>
    [MenuItem("Tools/Dungeon/Add Torch To Player", priority = 22)]
    public static void AddTorchMenu()
    {
        var rig = Object.FindAnyObjectByType<FirstPersonCameraRig>();
        if (rig == null) { Debug.LogWarning("No player rig in the scene."); return; }
        if (rig.transform.Find("Torch") != null) { Debug.Log("Player already has a torch."); return; }
        AddTorch(rig.transform);
    }

    private static void AddTorch(Transform head)
    {
        var torch = new GameObject("Torch", typeof(Light));
        Undo.RegisterCreatedObjectUndo(torch, "Add Torch");
        torch.transform.SetParent(head, false);
        torch.transform.localPosition = new Vector3(0.3f, 0.1f, 0.2f);
        var light = torch.GetComponent<Light>();
        light.type = LightType.Point;
        light.color = new Color(1f, 0.82f, 0.6f);
        light.range = DungeonGrid.CellSize * 4f;
        light.intensity = 4f;
        light.shadows = LightShadows.None;
    }

    [MenuItem("Tools/Dungeon/Create Level Builder", priority = 21)]
    public static void CreateLevelBuilderMenu()
    {
        LevelData level = Selection.activeObject as LevelData;
        if (level == null)
        {
            string guid = AssetDatabase.FindAssets("t:" + nameof(LevelData)).FirstOrDefault();
            if (guid != null) level = AssetDatabase.LoadAssetAtPath<LevelData>(AssetDatabase.GUIDToAssetPath(guid));
        }
        Selection.activeGameObject = CreateLevelBuilder(level).gameObject;
    }

    private static LevelBuilder CreateLevelBuilder(LevelData level)
    {
        var go = new GameObject("Level");
        Undo.RegisterCreatedObjectUndo(go, "Create Level Builder");
        var builder = go.AddComponent<LevelBuilder>();
        var so = new SerializedObject(builder);
        so.FindProperty("level").objectReferenceValue = level;
        so.ApplyModifiedPropertiesWithoutUndo();
        if (level != null) builder.Build();
        return builder;
    }

    [MenuItem("Tools/Dungeon/Create Starter Tiles", priority = 40)]
    public static void CreateStarterTilesMenu() => CreateStarterTiles();

    public static Dictionary<char, TileDefinition> CreateStarterTiles()
    {
        EnsureFolder(TileFolder);
        var tiles = new Dictionary<char, TileDefinition>
        {
            ['.'] = Tile("Floor", new Color(0.62f, 0.60f, 0.56f), TileDefinition.TileShape.Open),
            ['#'] = Tile("Wall", new Color(0.36f, 0.33f, 0.30f), TileDefinition.TileShape.Solid),
            ['D'] = Tile("Door", new Color(0.60f, 0.38f, 0.16f), TileDefinition.TileShape.Open),
            ['<'] = Tile("Stairs Up", new Color(0.30f, 0.62f, 1.00f), TileDefinition.TileShape.Open),
            ['>'] = Tile("Stairs Down", new Color(0.95f, 0.55f, 0.20f), TileDefinition.TileShape.Open),
        };
        AssetDatabase.SaveAssets();
        return tiles;
    }

    [MenuItem("Tools/Dungeon/Create Starter Level", priority = 41)]
    public static void CreateStarterLevelMenu() => Selection.activeObject = CreateStarterLevel();

    private static LevelData CreateStarterLevel()
    {
        Dictionary<char, TileDefinition> tiles = CreateStarterTiles();
        EnsureFolder(LevelFolder);

        string path = $"{LevelFolder}/Level_01.asset";
        var existing = AssetDatabase.LoadAssetAtPath<LevelData>(path);
        if (existing != null) return existing;

        // Top line = north. 'S' = player start (on floor).
        string[] map =
        {
            "############",
            "#<...#.....#",
            "#....#.....#",
            "#....D.....#",
            "#....#..#..#",
            "###D##.....#",
            "#....#.....#",
            "#.S..####D##",
            "#....#.....#",
            "#....D....>#",
            "#....#.....#",
            "############",
        };

        var level = ScriptableObject.CreateInstance<LevelData>();
        int h = map.Length, w = map[0].Length;
        level.Resize(w, h);
        for (int row = 0; row < h; row++)
        for (int x = 0; x < w; x++)
        {
            char ch = map[row][x];
            var cell = new Vector2Int(x, h - 1 - row);
            if (ch == 'S')
            {
                level.SetPlayerStart(cell, Facing.North);
                ch = '.';
            }
            level.SetTile(cell, tiles.TryGetValue(ch, out TileDefinition t) ? t : null);
        }

        AssetDatabase.CreateAsset(level, path);
        AssetDatabase.SaveAssets();
        return level;
    }

    private static TileDefinition Tile(string label, Color color, TileDefinition.TileShape shape)
    {
        string path = $"{TileFolder}/Tile_{label.Replace(" ", "")}.asset";
        var existing = AssetDatabase.LoadAssetAtPath<TileDefinition>(path);
        if (existing != null) return existing;

        var tile = ScriptableObject.CreateInstance<TileDefinition>();
        tile.EditorSetup(label, color, shape);
        AssetDatabase.CreateAsset(tile, path);
        return tile;
    }

    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = Path.GetDirectoryName(path).Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
    }
}
