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
        /// <summary>A room/corridor cell: gets floor + ceiling, can be walked on.</summary>
        Open,
        /// <summary>A filled cell: rendered as a wall block, never walkable.</summary>
        Solid,
    }

    [Tooltip("Name shown in the level painter. Leave empty to use the asset name.")]
    [SerializeField] private string displayName;

    [Tooltip("Color of this tile in the level painter and on placeholder geometry.")]
    [SerializeField] private Color editorColor = new Color(0.55f, 0.55f, 0.55f);

    [SerializeField] private TileShape shape = TileShape.Open;

    [Tooltip("Open tiles only: the cell has a floor/ceiling but the player can't enter it (statue, fountain...).")]
    [SerializeField] private bool blocksMovement;

    [Tooltip("Optional.\nOpen: spawned at the cell center on top of the floor (door frame, stairs, prop).\n" +
             "Solid: replaces the level's default wall block. Pivot = floor center of the cell.")]
    [SerializeField] private GameObject prefab;

    [Tooltip("Open tiles only: spawn the level's floor in this cell.")]
    [SerializeField] private bool hasFloor = true;

    [Tooltip("Open tiles only: spawn the level's ceiling in this cell.")]
    [SerializeField] private bool hasCeiling = true;

    public string Label => string.IsNullOrEmpty(displayName) ? name : displayName;
    public Color EditorColor => editorColor;
    public TileShape Shape => shape;
    public bool IsOpen => shape == TileShape.Open;
    public bool IsWalkable => shape == TileShape.Open && !blocksMovement;
    public GameObject Prefab => prefab;
    public bool HasFloor => hasFloor;
    public bool HasCeiling => hasCeiling;

#if UNITY_EDITOR
    /// <summary>Editor helper used when generating starter tiles.</summary>
    public void EditorSetup(string label, Color color, TileShape tileShape, bool blocks = false)
    {
        displayName = label;
        editorColor = color;
        shape = tileShape;
        blocksMovement = blocks;
    }
#endif
}
