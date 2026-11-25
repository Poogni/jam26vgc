using UnityEngine;

/// <summary>
/// One kind of tile a designer can paint into a level (Floor, Wall, Door, Stairs...).
/// Create new ones from the level inspector ("New Tile…") or via Create > Dungeon > Tile.
/// </summary>
[CreateAssetMenu(menuName = "Dungeon/Tile", fileName = "Tile_New", order = 1)]
public class TileDefinition : ScriptableObject
{
    public enum TileShape
    {
        /// <summary>A street/courtyard cell: gets ground, can be walked on.</summary>
        Open,
        /// <summary>A building: extruded to its height, never walkable (unless it's a fake wall).</summary>
        Solid,
    }

    /// <summary>Gameplay behaviour of the tile. Values are stored in tile assets, so keep the numbers.</summary>
    public enum TileBehavior
    {
        None = 0,
        /// <summary>Stepping onto it moves you to its partner teleport (same row, else same column), keeping your facing.</summary>
        Teleport = 2,
        /// <summary>Looks like a wall, but you can walk straight through it.</summary>
        FakeWall = 3,
        /// <summary>The location to find: face it and press Interact, then return to the start to complete it.</summary>
        Objective = 4,
    }

    [Tooltip("Name shown in the level painter. Leave empty to use the asset name.")]
    [SerializeField] private string displayName;

    [Tooltip("Color of this tile in the level painter and on placeholder geometry.")]
    [SerializeField] private Color editorColor = new Color(0.55f, 0.55f, 0.55f);

    [SerializeField] private TileShape shape = TileShape.Open;

    [SerializeField] private TileBehavior behavior = TileBehavior.None;

    [Tooltip("Open tiles only: the cell has ground but the player can't enter it (statue, fountain...).")]
    [SerializeField] private bool blocksMovement;

    [Tooltip("Optional prop spawned at the cell center on the ground (door frame, stairs, fountain, stall...). " +
             "It faces the cell's north edge. For solid tiles it sits at the building's base.")]
    [SerializeField] private GameObject prefab;

    [Tooltip("Open tiles only: generate ground in this cell.")]
    [SerializeField] private bool hasFloor = true;

    [Tooltip("Open tiles only: put a roof over this cell (covered passage / souk). Open sky otherwise.")]
    [SerializeField] private bool covered;

    [Tooltip("Open tiles only: arch placed on this cell's thresholds between covered and open-sky ground, facing the " +
             "open side. Paint it on the first and last cell of a covered passage (sabat) to mark its entrance and " +
             "exit. With no such threshold, the arch stands across the alley in the middle of the cell.")]
    [SerializeField] private GameObject archway;

    [Tooltip("Solid tiles only: building height in stories. 0 = use the level's random min..max.")]
    [SerializeField, Min(0)] private int storiesOverride;

    [Tooltip("Fake Wall only: the wall it imitates. Empty = the plain rock/plaster wall.")]
    [SerializeField] private TileDefinition disguiseAs;
    [Tooltip("Fake Wall only: multiplied into the disguise's color. Keep it close to white so the difference is " +
             "only just noticeable.")]
    [SerializeField] private Color disguiseTint = new Color(1f, 0.9f, 0.88f);

    public string Label => string.IsNullOrEmpty(displayName) ? name : displayName;
    public Color EditorColor => editorColor;
    public TileShape Shape => shape;
    public TileBehavior Behavior => behavior;
    public bool IsOpen => shape == TileShape.Open;
    public bool IsFakeWall => behavior == TileBehavior.FakeWall;
    public bool IsWalkable => IsFakeWall || (shape == TileShape.Open && !blocksMovement);
    public GameObject Prefab => prefab;
    public bool HasFloor => hasFloor;
    public bool Covered => covered;
    public GameObject Archway => archway;
    public int StoriesOverride => storiesOverride;
    public TileDefinition DisguiseAs => disguiseAs;
    public Color DisguiseTint => disguiseTint;

#if UNITY_EDITOR
    /// <summary>Editor helper used when generating starter tiles.</summary>
    public void EditorSetup(string label, Color color, TileShape tileShape, bool isCovered = false, int stories = 0,
        TileBehavior tileBehavior = TileBehavior.None, bool blocks = false, TileDefinition disguise = null)
    {
        covered = isCovered;
        storiesOverride = stories;
        displayName = label;
        editorColor = color;
        shape = tileShape;
        behavior = tileBehavior;
        blocksMovement = blocks;
        disguiseAs = disguise;
    }
#endif
}
