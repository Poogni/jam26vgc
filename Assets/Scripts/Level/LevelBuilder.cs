using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
#endif

/// <summary>
/// Turns a <see cref="LevelData"/> asset into scene geometry and places the player on its start cell.
/// Keep this object at the world origin: cell (0,0) is built at world (0,0,0).
/// Cells without a prefab get grey-box cubes tinted with the tile's editor color.
/// </summary>
public class LevelBuilder : MonoBehaviour
{
    private const string RootName = "[Generated Level]";
    private static readonly Color RockColor = new Color(0.32f, 0.30f, 0.28f);
    private static readonly Color CeilingColor = new Color(0.22f, 0.22f, 0.24f);

    [SerializeField] private LevelData level;
    [Tooltip("Player to place on the level's start cell. Found automatically if empty.")]
    [SerializeField] private GridPlayerController player;
    [Tooltip("Regenerate geometry when entering Play mode so the scene always matches the asset.")]
    [SerializeField] private bool rebuildOnPlay = true;
    [Tooltip("Editor only: rebuild the scene preview whenever the level asset is edited.")]
    [SerializeField] private bool livePreview = true;

    public LevelData Level => level;
    public bool LivePreview => livePreview;

    private void Start()
    {
        if (level == null)
        {
            Debug.LogWarning($"{name}: no LevelData assigned.", this);
            return;
        }

        if (rebuildOnPlay) Build();

        if (player == null) player = FindAnyObjectByType<GridPlayerController>();
        if (player != null)
        {
            player.SetLevel(level);
            player.Teleport(level.PlayerStart, level.StartFacing);
        }
    }

    /// <summary>Delete and regenerate all level geometry.</summary>
    [ContextMenu("Build")]
    public void Build()
    {
        Clear();
        if (level == null) return;

        var root = new GameObject(RootName).transform;
        root.SetParent(transform, false);

        float cs = DungeonGrid.CellSize;
        float wh = level.WallHeight;

        // Go one cell past every edge so open cells on the border still get walls.
        for (int y = -1; y <= level.Height; y++)
        for (int x = -1; x <= level.Width; x++)
        {
            var c = new Vector2Int(x, y);
            TileDefinition tile = level.GetTile(c);
            Vector3 pos = DungeonGrid.CellToWorld(c);

            if (level.IsOpen(c))
            {
                if (tile.HasFloor)
                    Spawn(level.FloorPrefab, root, $"Floor {x},{y}", pos,
                        new Vector3(0f, -0.05f, 0f), new Vector3(cs, 0.1f, cs), tile.EditorColor);

                if (level.HasCeiling && tile.HasCeiling)
                    Spawn(level.CeilingPrefab, root, $"Ceiling {x},{y}", pos + Vector3.up * wh,
                        new Vector3(0f, 0.05f, 0f), new Vector3(cs, 0.1f, cs), CeilingColor);

                if (tile.Prefab != null)
                    Spawn(tile.Prefab, root, $"{tile.Label} {x},{y}", pos, Vector3.zero, Vector3.one, Color.white);
            }
            else if (TouchesOpen(c))
            {
                GameObject prefab = tile != null && tile.Prefab != null ? tile.Prefab : level.WallPrefab;
                Color color = tile != null ? tile.EditorColor : RockColor;
                Spawn(prefab, root, $"Wall {x},{y}", pos,
                    new Vector3(0f, wh * 0.5f, 0f), new Vector3(cs, wh, cs), color);
            }
        }

#if UNITY_EDITOR
        if (!Application.isPlaying)
        {
            // Show the spawn in the Scene view too, so designers see where the player starts.
            if (player == null) player = FindAnyObjectByType<GridPlayerController>();
            if (player != null)
                player.transform.SetPositionAndRotation(
                    DungeonGrid.CellToWorld(level.PlayerStart), level.StartFacing.ToRotation());
            EditorSceneManager.MarkSceneDirty(gameObject.scene);
        }
#endif
    }

    /// <summary>Delete all generated geometry.</summary>
    [ContextMenu("Clear")]
    public void Clear()
    {
        for (int i = transform.childCount - 1; i >= 0; i--)
        {
            Transform child = transform.GetChild(i);
            if (child.name != RootName) continue;
            if (Application.isPlaying) Destroy(child.gameObject);
            else DestroyImmediate(child.gameObject);
        }
    }

    private bool TouchesOpen(Vector2Int c) =>
        level.IsOpen(c + Vector2Int.up) || level.IsOpen(c + Vector2Int.down) ||
        level.IsOpen(c + Vector2Int.left) || level.IsOpen(c + Vector2Int.right);

    /// <summary>Instantiate a prefab at the cell, or a tinted cube placeholder if there is none.</summary>
    private static void Spawn(GameObject prefab, Transform parent, string objectName, Vector3 cellPos,
        Vector3 placeholderOffset, Vector3 placeholderScale, Color placeholderColor)
    {
        GameObject go;
        if (prefab != null)
        {
#if UNITY_EDITOR
            go = Application.isPlaying
                ? Instantiate(prefab, parent)
                : (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
#else
            go = Instantiate(prefab, parent);
#endif
            go.transform.SetPositionAndRotation(cellPos, Quaternion.identity);
        }
        else
        {
            go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.transform.SetParent(parent, false);
            go.transform.position = cellPos + placeholderOffset;
            go.transform.localScale = placeholderScale;
            go.AddComponent<PlaceholderTint>().Color = placeholderColor;
        }
        go.name = objectName;
        go.isStatic = true;
    }

    private void OnDrawGizmosSelected()
    {
        if (level == null) return;
        float cs = DungeonGrid.CellSize;
        Vector3 min = DungeonGrid.CellToWorld(Vector2Int.zero) - new Vector3(cs, 0f, cs) * 0.5f;
        Vector3 size = new Vector3(level.Width * cs, 0f, level.Height * cs);
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireCube(min + size * 0.5f, size);
    }
}
