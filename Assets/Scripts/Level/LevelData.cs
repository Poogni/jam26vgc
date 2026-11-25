using UnityEngine;

/// <summary>
/// A dungeon floor stored as a width x height matrix of tiles.
/// Cell (0,0) is the south-west corner; +X is east, +Y is north. An empty cell (null) is solid rock / building.
/// Edit it by selecting the asset: the inspector shows a paintable grid.
///
/// Gameplay always uses the regular matrix. The *world* shape is a warped lattice: every grid corner is
/// nudged by a smooth bend plus a small jitter (Townscaper-like), so streets wobble and buildings are
/// irregular quads. Use <see cref="CellCenter"/> / <see cref="FacingRotation"/> to place things in the world.
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

    [Tooltip("Small random push of every grid corner, as a fraction of a cell. 0 = perfectly square cells.")]
    [SerializeField, Range(0f, 0.3f)] private float jitter = 0.12f;
    [Tooltip("Large, smooth bending of the whole grid, in cells. Makes streets curve gently.")]
    [SerializeField, Range(0f, 1.5f)] private float bend = 0.5f;
    [Tooltip("How many cells one bend wave spans. Bigger = gentler, longer curves.")]
    [SerializeField, Range(2f, 20f)] private float bendScale = 7f;
    [Tooltip("Change to get a different irregular layout for the same tile matrix.")]
    [SerializeField] private int seed = 1;

    [Tooltip("Height of one story (one 'block') in meters.")]
    [SerializeField, Min(0.5f)] private float storyHeight = DungeonGrid.CellSize;
    [SerializeField, Min(1)] private int minStories = 2;
    [SerializeField, Min(1)] private int maxStories = 4;
    [Tooltip("How many cells a group of similar-height houses spans. Bigger = larger building masses.")]
    [SerializeField, Range(1f, 10f)] private float heightClusterSize = 3f;
    [Tooltip("0 = heights follow the clusters smoothly, 1 = every cell picks its own random height.")]
    [SerializeField, Range(0f, 1f)] private float heightRandomness = 0.25f;
    [Tooltip("Extra random 0..N meters on each roof so rooflines are uneven.")]
    [SerializeField, Min(0f)] private float roofJitter = 0.35f;
    [Tooltip("Height of the roof over 'covered' tiles (covered passages / souk roofs).")]
    [SerializeField, Min(2f)] private float coveredHeight = 3.2f;

    [Tooltip("Optional. Leave empty to use grey-box materials tinted with each tile's color.")]
    [SerializeField] private Material groundMaterial;
    [Tooltip("Optional. Leave empty to use grey-box materials tinted with each tile's color.")]
    [SerializeField] private Material wallMaterial;
    [Tooltip("Optional. Textured walls (one variant per cell) plus bottom/mid/top decals. Overrides Wall Material.")]
    [SerializeField] private WallStyle wallStyle;

    [Tooltip("What the objective is called in messages.")]
    [SerializeField] private string objectiveName = "the hidden courtyard";
    [Tooltip("Shown when the player interacts with the objective. {0} = objective name.")]
    [SerializeField, TextArea(2, 4)] private string objectiveFoundText =
        "You found {0}. Now find your way back to where you started. Trust your map.";
    [Tooltip("Shown when the player gets back to the start after finding the objective.")]
    [SerializeField, TextArea(2, 4)] private string objectiveCompleteText =
        "You made it back to where you began. Your map held true.";

    public string ObjectiveName => objectiveName;
    public string ObjectiveFoundText => string.Format(objectiveFoundText ?? "", objectiveName);
    public string ObjectiveCompleteText => string.Format(objectiveCompleteText ?? "", objectiveName);

    public int Width => width;
    public int Height => height;
    public Vector2Int PlayerStart => playerStart;
    public Facing StartFacing => startFacing;
    public float CoveredHeight => coveredHeight;
    public Material GroundMaterial => groundMaterial;
    public Material WallMaterial => wallMaterial;
    public WallStyle WallStyle => wallStyle;
    public int Seed => seed;

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

    /// <summary>Number of objective tiles in the level.</summary>
    public int CountBehavior(TileDefinition.TileBehavior behavior)
    {
        int n = 0;
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
            if (BehaviorAt(new Vector2Int(x, y)) == behavior) n++;
        return n;
    }

    public bool IsFakeWall(Vector2Int c)
    {
        TileDefinition t = GetTile(c);
        return t != null && t.IsFakeWall;
    }

    public TileDefinition.TileBehavior BehaviorAt(Vector2Int c)
    {
        TileDefinition t = GetTile(c);
        return t != null ? t.Behavior : TileDefinition.TileBehavior.None;
    }

    /// <summary>
    /// The side a Face Street prop on this cell turns to: the first walkable neighbour in N, E, S, W order.
    /// Null if none (the prop then faces north).
    /// </summary>
    public Facing? StreetSide(Vector2Int c)
    {
        for (int side = 0; side < 4; side++)
            if (IsWalkable(c + ((Facing)side).ToOffset())) return (Facing)side;
        return null;
    }

    // ---------------------------------------------------------------- teleports

    /// <summary>
    /// Where a teleport cell sends you: the farthest other teleport in the same row (wrap-around east/west),
    /// otherwise the farthest in the same column (north/south). Null if it has no partner.
    /// </summary>
    public Vector2Int? FindTeleportPartner(Vector2Int c)
    {
        if (BehaviorAt(c) != TileDefinition.TileBehavior.Teleport) return null;

        Vector2Int? best = null;
        int bestDist = 0;
        for (int x = 0; x < width; x++)
        {
            var o = new Vector2Int(x, c.y);
            int d = Mathf.Abs(x - c.x);
            if (d > bestDist && BehaviorAt(o) == TileDefinition.TileBehavior.Teleport) { best = o; bestDist = d; }
        }
        if (best.HasValue) return best;

        for (int y = 0; y < height; y++)
        {
            var o = new Vector2Int(c.x, y);
            int d = Mathf.Abs(y - c.y);
            if (d > bestDist && BehaviorAt(o) == TileDefinition.TileBehavior.Teleport) { best = o; bestDist = d; }
        }
        return best;
    }

    // ---------------------------------------------------------------- world shape

    /// <summary>
    /// World position of grid corner (i, j). Cell (x, y) has corners (x, y) SW, (x+1, y) SE, (x+1, y+1) NE, (x, y+1) NW.
    /// </summary>
    public Vector3 Corner(int i, int j)
    {
        float cs = DungeonGrid.CellSize;
        float s = Mathf.Repeat(seed * 17.31f, 1000f);

        // Smooth, coherent bend so whole streets curve together.
        float bx = Mathf.PerlinNoise(i / bendScale + s, j / bendScale + 11.3f) - 0.5f;
        float bz = Mathf.PerlinNoise(i / bendScale + 47.1f, j / bendScale + s) - 0.5f;

        // Per-corner jitter for the hand-built, irregular look.
        float jx = Hash01(i, j, seed) * 2f - 1f;
        float jz = Hash01(i, j, seed + 7919) * 2f - 1f;

        return new Vector3(
            (i - 0.5f) * cs + (bx * 2f * bend + jx * jitter) * cs,
            0f,
            (j - 0.5f) * cs + (bz * 2f * bend + jz * jitter) * cs);
    }

    /// <summary>Corners of a cell in SW, SE, NE, NW order (counter-clockwise seen from above).</summary>
    public void CellCorners(Vector2Int c, Vector3[] into)
    {
        into[0] = Corner(c.x, c.y);
        into[1] = Corner(c.x + 1, c.y);
        into[2] = Corner(c.x + 1, c.y + 1);
        into[3] = Corner(c.x, c.y + 1);
    }

    /// <summary>World center of a (warped) cell, on the ground.</summary>
    public Vector3 CellCenter(Vector2Int c) =>
        (Corner(c.x, c.y) + Corner(c.x + 1, c.y) + Corner(c.x + 1, c.y + 1) + Corner(c.x, c.y + 1)) * 0.25f;

    /// <summary>Midpoint of the cell edge on the given side.</summary>
    public Vector3 EdgeMidpoint(Vector2Int c, Facing side) => side switch
    {
        Facing.North => (Corner(c.x, c.y + 1) + Corner(c.x + 1, c.y + 1)) * 0.5f,
        Facing.East => (Corner(c.x + 1, c.y) + Corner(c.x + 1, c.y + 1)) * 0.5f,
        Facing.South => (Corner(c.x, c.y) + Corner(c.x + 1, c.y)) * 0.5f,
        _ => (Corner(c.x, c.y) + Corner(c.x, c.y + 1)) * 0.5f,
    };

    /// <summary>Rotation that looks from the cell center toward the edge on the given side.</summary>
    public Quaternion FacingRotation(Vector2Int c, Facing facing)
    {
        Vector3 dir = EdgeMidpoint(c, facing) - CellCenter(c);
        dir.y = 0f;
        return dir.sqrMagnitude > 1e-6f ? Quaternion.LookRotation(dir) : facing.ToRotation();
    }

    /// <summary>Height of the building standing on a solid cell.</summary>
    public float BuildingHeight(Vector2Int c)
    {
        int lo = Mathf.Min(minStories, maxStories), hi = Mathf.Max(minStories, maxStories);
        TileDefinition tile = GetTile(c);
        int stories = tile != null && tile.StoriesOverride > 0
            ? tile.StoriesOverride
            : lo + (int)(HeightNoise(c) * (hi - lo + 1) - 1e-4f);
        return stories * storyHeight + Hash01(c.x, c.y, seed + 1299709) * roofJitter;
    }

    /// <summary>0..1 height noise that clusters, so neighboring cells read as one building mass.</summary>
    private float HeightNoise(Vector2Int c)
    {
        float s = Mathf.Repeat(seed * 23.17f, 1000f) + 300f;
        float smooth = Mathf.PerlinNoise(c.x / heightClusterSize + s, c.y / heightClusterSize + s);
        // Perlin rarely reaches its extremes; stretch it so min/max stories both show up.
        smooth = Mathf.Clamp01((smooth - 0.5f) * 1.8f + 0.5f);
        return Mathf.Clamp01(Mathf.Lerp(smooth, Hash01(c.x, c.y, seed + 104729), heightRandomness));
    }

    private static float Hash01(int x, int y, int s)
    {
        unchecked
        {
            uint h = (uint)x * 374761393u + (uint)y * 668265263u + (uint)s * 2246822519u;
            h = (h ^ (h >> 13)) * 1274126177u;
            h ^= h >> 16;
            return (h & 0xFFFFFF) / 16777216f;
        }
    }

    // ---------------------------------------------------------------- editing API (Undo handled by caller)

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
