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
        /// <summary>A building: extruded to its height, never walkable.</summary>
        Solid,
    }

    [Tooltip("Name shown in the level painter. Leave empty to use the asset name.")]
    [SerializeField] private string displayName;

    [Tooltip("Color of this tile in the level painter and on placeholder geometry.")]
    [SerializeField] private Color editorColor = new Color(0.55f, 0.55f, 0.55f);

    [SerializeField] private TileShape shape = TileShape.Open;

    [Tooltip("Open tiles only: the cell has ground but the player can't enter it (statue, fountain...).")]
    [SerializeField] private bool blocksMovement;

    [Tooltip("Optional prop spawned at the cell center on the ground (door frame, stairs, fountain, stall...). " +
             "It faces the cell's north edge. For solid tiles it sits at the building's base.")]
    [SerializeField] private GameObject prefab;

    [Tooltip("Open tiles only: generate ground in this cell.")]
    [SerializeField] private bool hasFloor = true;

    [Tooltip("Open tiles only: put a roof over this cell (covered passage / souk). Open sky otherwise.")]
    [SerializeField] private bool covered;

    [Tooltip("Solid tiles only: building height in stories. 0 = use the level's random min..max.")]
    [SerializeField, Min(0)] private int storiesOverride;

    public string Label => string.IsNullOrEmpty(displayName) ? name : displayName;
    public Color EditorColor => editorColor;
    public TileShape Shape => shape;
    public bool IsOpen => shape == TileShape.Open;
    public bool IsWalkable => shape == TileShape.Open && !blocksMovement;
    public GameObject Prefab => prefab;
    public bool HasFloor => hasFloor;
    public bool Covered => covered;
    public int StoriesOverride => storiesOverride;

#if UNITY_EDITOR
    /// <summary>Editor helper used when generating starter tiles.</summary>
    public void EditorSetup(string label, Color color, TileShape tileShape, bool isCovered = false, int stories = 0)
    {
        covered = isCovered;
        storiesOverride = stories;
        displayName = label;
        editorColor = color;
        shape = tileShape;
        blocksMovement = false;
    }
#endif
}
