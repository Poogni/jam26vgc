using UnityEngine;

/// <summary>
/// A dungeon floor stored as a width x height matrix of tiles.
/// Cell (0,0) is the south-west corner; +X is east, +Y is north. An empty cell (null) is solid rock.
/// Edit it by selecting the asset: the inspector shows a paintable grid.
/// </summary>
[CreateAssetMenu(menuName = "Dungeon/Level", fileName = "Level_New", order = 0)]
public class LevelData : ScriptableObject
{
    public const int MaxSize = 128;

    [SerializeField] private int width = 16;
    [SerializeField] private int height = 16;
    [Tooltip("Row-major (index = y * width + x). Null = solid rock.")]
    [SerializeField] private TileDefinition[] cells = new TileDefinition[16 * 16];

    [SerializeField] private Vector2Int playerStart = new Vector2Int(1, 1);
    [SerializeField] private Facing startFacing = Facing.North;

    [Tooltip("Height of walls / ceiling in meters.")]
    [SerializeField, Min(0.5f)] private float wallHeight = 3f;
    [SerializeField] private bool hasCeiling = true;

    [Tooltip("Optional. Spawned for every solid cell bordering open space. Pivot = floor center of the cell, size 2.5 x wallHeight x 2.5.")]
    [SerializeField] private GameObject wallPrefab;
    [Tooltip("Optional. Spawned in every open cell. Pivot = top-center of the floor, size 2.5 x 2.5.")]
    [SerializeField] private GameObject floorPrefab;
    [Tooltip("Optional. Spawned at wallHeight in every open cell. Pivot = bottom-center of the ceiling.")]
    [SerializeField] private GameObject ceilingPrefab;

    public int Width => width;
    public int Height => height;
    public Vector2Int PlayerStart => playerStart;
    public Facing StartFacing => startFacing;
    public float WallHeight => wallHeight;
    public bool HasCeiling => hasCeiling;
    public GameObject WallPrefab => wallPrefab;
    public GameObject FloorPrefab => floorPrefab;
    public GameObject CeilingPrefab => ceilingPrefab;

    public bool InBounds(Vector2Int c) => c.x >= 0 && c.y >= 0 && c.x < width && c.y < height;

    /// <summary>Tile at a cell, or null for rock / out of bounds.</summary>
    public TileDefinition GetTile(Vector2Int c) => InBounds(c) ? cells[c.y * width + c.x] : null;

    public bool IsOpen(Vector2Int c)
    {
        TileDefinition t = GetTile(c);
        return t != null && t.IsOpen;
    }

    public bool IsWalkable(Vector2Int c)
    {
        TileDefinition t = GetTile(c);
        return t != null && t.IsWalkable;
    }

    // ---- Editing API (used by the level painter; Undo is handled by the caller) ----

    public bool SetTile(Vector2Int c, TileDefinition tile)
    {
        if (!InBounds(c)) return false;
        int i = c.y * width + c.x;
        if (cells[i] == tile) return false;
        cells[i] = tile;
        return true;
    }

    public void SetPlayerStart(Vector2Int cell, Facing facing)
    {
        playerStart = new Vector2Int(Mathf.Clamp(cell.x, 0, width - 1), Mathf.Clamp(cell.y, 0, height - 1));
        startFacing = facing;
    }

    /// <summary>Resize the matrix, keeping existing tiles anchored to the south-west corner.</summary>
    public void Resize(int newWidth, int newHeight)
    {
        newWidth = Mathf.Clamp(newWidth, 1, MaxSize);
        newHeight = Mathf.Clamp(newHeight, 1, MaxSize);
        var next = new TileDefinition[newWidth * newHeight];

        int oldCount = cells?.Length ?? 0;
        for (int y = 0; y < Mathf.Min(height, newHeight); y++)
        for (int x = 0; x < Mathf.Min(width, newWidth); x++)
        {
            int oldIndex = y * width + x;
            if (oldIndex < oldCount) next[y * newWidth + x] = cells[oldIndex];
        }

        width = newWidth;
        height = newHeight;
        cells = next;
        SetPlayerStart(playerStart, startFacing);
    }

    private void OnValidate()
    {
        width = Mathf.Clamp(width, 1, MaxSize);
        height = Mathf.Clamp(height, 1, MaxSize);
        if (cells == null || cells.Length != width * height)
        {
            // Keep whatever data fits rather than wiping the level.
            var old = cells ?? new TileDefinition[0];
            cells = new TileDefinition[width * height];
            System.Array.Copy(old, cells, Mathf.Min(old.Length, cells.Length));
        }
    }
}
