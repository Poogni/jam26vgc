using UnityEditor;
using UnityEngine;

/// <summary>
/// Tools > Dungeon > Create Landmarks: makes a prefab per landmark (root with a Landmark component + a "Model" child)
/// and a Landmark tile for each, ready to paint in the level. The models are placeholder boxes for now: replace the
/// prefab's "Model" child with the real art when it's ready (keep the root: back of the prop at local z = 0 against
/// the wall, +Z toward the street, feet at y = 0).
/// Safe to run again: existing prefabs and tiles are kept as they are (your text, lights and sounds stay).
/// </summary>
public static class LandmarkBuilder
{
    private const string PrefabFolder = "Assets/Level/Landmarks";
    private const string TileFolder = "Assets/Level/Tiles";
    private const string ModelName = "Model";

    private struct Box
    {
        public Vector3 Center, Size;
        public Color Color;
        public Box(Vector3 center, Vector3 size, Color color) { Center = center; Size = size; Color = color; }
    }

    private struct Spec
    {
        public string Label;
        public bool AgainstWall;
        public Box[] Boxes;      // placeholder shape, in prefab space (back on z = 0, front toward +Z)
        public string[] Lines;
        public Color TileColor;
    }

    private static readonly Color Plaster = new Color(0.86f, 0.80f, 0.70f);

    private static readonly Spec[] Specs =
    {
        new Spec
        {
            Label = "Door", AgainstWall = true, TileColor = new Color(0.55f, 0.32f, 0.75f),
            Boxes = new[]
            {
                new Box(new Vector3(0f, 1.3f, 0.08f), new Vector3(1.9f, 2.6f, 0.16f), Plaster),                  // frame
                new Box(new Vector3(0f, 1.15f, 0.12f), new Vector3(1.3f, 2.3f, 0.16f), new Color(0.45f, 0.26f, 0.13f)), // door leaf
                new Box(new Vector3(0f, 2.75f, 0.25f), new Vector3(2.1f, 0.15f, 0.5f), new Color(0.20f, 0.42f, 0.30f)), // green tile canopy
            },
            Lines = new[]
            {
                "A heavy cedar door, studded with iron. The brass hand of the knocker is worn smooth.",
                "Someone has scratched a tiny arrow into the frame, pointing back the way you came.",
            },
        },
        new Spec
        {
            Label = "Hanout", AgainstWall = true, TileColor = new Color(0.62f, 0.40f, 0.85f),
            Boxes = new[]
            {
                new Box(new Vector3(0f, 1.4f, 0.4f), new Vector3(2.2f, 2.8f, 0.8f), new Color(0.30f, 0.50f, 0.42f)),   // shelves
                new Box(new Vector3(0f, 0.55f, 0.95f), new Vector3(2.0f, 1.1f, 0.35f), new Color(0.55f, 0.38f, 0.22f)), // counter
                new Box(new Vector3(0f, 3.0f, 0.55f), new Vector3(2.4f, 0.12f, 1.1f), new Color(0.85f, 0.45f, 0.20f)),  // awning
            },
            Lines = new[]
            {
                "A cramped hanout, shelves stacked to the ceiling with tea, sugar cones and biscuits.",
                "The shopkeeper doesn't look up. \"Everyone gets lost here. Everyone finds the hanout.\"",
            },
        },
        new Spec
        {
            Label = "Zellige", AgainstWall = true, TileColor = new Color(0.48f, 0.36f, 0.88f),
            Boxes = new[]
            {
                new Box(new Vector3(0f, 1.3f, 0.05f), new Vector3(2.3f, 2.6f, 0.1f), new Color(0.12f, 0.45f, 0.55f)),  // tiled panel
                new Box(new Vector3(0f, 1.5f, 0.11f), new Vector3(0.9f, 0.9f, 0.06f), new Color(0.90f, 0.75f, 0.25f)), // star medallion
            },
            Lines = new[]
            {
                "A wall of zellige, thousands of tiny glazed tiles cut by hand into stars.",
                "One star is missing. You'd recognise this wall anywhere.",
            },
        },
        new Spec
        {
            Label = "Fountain", AgainstWall = true, TileColor = new Color(0.70f, 0.50f, 0.95f),
            Boxes = new[]
            {
                new Box(new Vector3(0f, 1.3f, 0.1f), new Vector3(1.6f, 2.6f, 0.2f), new Color(0.16f, 0.24f, 0.55f)),   // blue back panel
                new Box(new Vector3(0f, 0.35f, 0.5f), new Vector3(1.4f, 0.7f, 0.8f), Plaster),                         // basin
                new Box(new Vector3(0f, 2.7f, 0.3f), new Vector3(1.8f, 0.15f, 0.6f), new Color(0.20f, 0.42f, 0.30f)),  // canopy
            },
            Lines = new[]
            {
                "A small fountain. The water is cold and tastes faintly of stone.",
                "Children have left a ring of pebbles around its base.",
            },
        },
    };

    [MenuItem("Tools/Dungeon/Create Landmarks", priority = 45)]
    public static void CreateAllMenu() => CreateAll(false);

    /// <summary>Puts the placeholder boxes back into existing prefabs (their text and other children are kept).</summary>
    [MenuItem("Tools/Dungeon/Reset Landmark Placeholders", priority = 46)]
    public static void ResetPlaceholdersMenu()
    {
        if (EditorUtility.DisplayDialog("Reset landmark placeholders",
                "Replace each landmark prefab's \"Model\" child with the placeholder boxes? Text, lights and sounds stay.",
                "Replace", "Cancel"))
            CreateAll(true);
    }

    public static void CreateAll(bool replaceModels)
    {
        EnsureFolder(PrefabFolder);
        EnsureFolder(TileFolder);
        foreach (Spec spec in Specs)
        {
            GameObject prefab = CreateOrUpdatePrefab(spec, replaceModels);
            CreateTile(spec, prefab);
        }
        AssetDatabase.SaveAssets();
    }

    private static GameObject CreateOrUpdatePrefab(Spec spec, bool replaceModel)
    {
        string path = $"{PrefabFolder}/Landmark_{spec.Label}.prefab";
        if (AssetDatabase.LoadAssetAtPath<GameObject>(path) != null)
        {
            if (!replaceModel) return AssetDatabase.LoadAssetAtPath<GameObject>(path);
            // Same asset (GUID) so tiles and levels keep pointing at it; only the model changes.
            GameObject contents = PrefabUtility.LoadPrefabContents(path);
            Transform old = contents.transform.Find(ModelName);
            if (old != null) Object.DestroyImmediate(old.gameObject);
            BuildPlaceholder(spec, contents.transform).SetSiblingIndex(0);
            PrefabUtility.SaveAsPrefabAsset(contents, path);
            PrefabUtility.UnloadPrefabContents(contents);
            return AssetDatabase.LoadAssetAtPath<GameObject>(path);
        }

        var root = new GameObject($"Landmark_{spec.Label}");
        BuildPlaceholder(spec, root.transform);
        var landmark = root.AddComponent<Landmark>();
        var so = new SerializedObject(landmark);
        so.FindProperty("displayName").stringValue = spec.Label;
        SerializedProperty lines = so.FindProperty("lines");
        lines.arraySize = spec.Lines.Length;
        for (int i = 0; i < spec.Lines.Length; i++) lines.GetArrayElementAtIndex(i).stringValue = spec.Lines[i];
        so.ApplyModifiedPropertiesWithoutUndo();

        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
        Object.DestroyImmediate(root);
        return prefab;
    }

    /// <summary>"Model" child made of tinted boxes (no colliders: movement is grid based).</summary>
    private static Transform BuildPlaceholder(Spec spec, Transform parent)
    {
        var model = new GameObject(ModelName).transform;
        model.SetParent(parent, false);
        foreach (Box b in spec.Boxes)
        {
            GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Object.DestroyImmediate(cube.GetComponent<Collider>());
            cube.name = "Placeholder";
            cube.transform.SetParent(model, false);
            cube.transform.localPosition = b.Center;
            cube.transform.localScale = b.Size;
            cube.isStatic = true;
            var tint = cube.AddComponent<PlaceholderTint>();
            tint.Matte = true;
            tint.Color = b.Color;
        }
        return model;
    }

    private static void CreateTile(Spec spec, GameObject prefab)
    {
        string path = $"{TileFolder}/Tile_Landmark_{spec.Label}.asset";
        if (AssetDatabase.LoadAssetAtPath<TileDefinition>(path) != null) return;

        var tile = ScriptableObject.CreateInstance<TileDefinition>();
        // Wall landmarks are part of a building (they don't block an alley); free-standing ones take a street cell.
        tile.EditorSetup($"Landmark: {spec.Label}", spec.TileColor,
            spec.AgainstWall ? TileDefinition.TileShape.Solid : TileDefinition.TileShape.Open,
            tileBehavior: TileDefinition.TileBehavior.Landmark, blocks: !spec.AgainstWall);
        tile.EditorSetProp(prefab, TileDefinition.PropPlacement.FaceStreet);
        AssetDatabase.CreateAsset(tile, path);
    }

    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = System.IO.Path.GetDirectoryName(path).Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, System.IO.Path.GetFileName(path));
    }
}
