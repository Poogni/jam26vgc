using UnityEngine;

/// <summary>
/// How a level's buildings are dressed: which wall textures they use (one picked per cell) and which decals
/// are scattered on them. Assign it to a level in the level painter (Materials > Wall Style).
/// Create one with Create > Dungeon > Wall Style, or Tools > Dungeon > Create Wall Style From Textures.
/// </summary>
[CreateAssetMenu(menuName = "Dungeon/Wall Style", fileName = "WallStyle_New", order = 2)]
public class WallStyle : ScriptableObject
{
    [Header("Walls")]
    [Tooltip("Seamless walls: one material (Retro/Medina Wall shader) that projects and blends the wall textures in " +
             "world space, so faces and cells join without seams. Leave empty to use the per-cell variants below.")]
    [SerializeField] private Material seamlessMaterial;
    [Tooltip("Used when there's no seamless material: each building cell picks one of these.")]
    [SerializeField] private Material[] wallVariants = new Material[0];
    [Tooltip("Meters of wall covered by one texture repeat (horizontally and vertically).")]
    [SerializeField, Min(0.5f)] private float textureSize = 2.5f;

    [Header("Bottom Decals (stand on the ground)")]
    [SerializeField] private Material[] bottomDecals = new Material[0];
    [Tooltip("Chance a street-facing wall gets a bottom decal.")]
    [SerializeField, Range(0f, 1f)] private float bottomChance = 0.75f;
    [Tooltip("Height of a bottom decal in meters (width follows the texture's shape).")]
    [SerializeField, Min(0.1f)] private float bottomHeight = 1.7f;

    [Header("Mid Decals (on the body of the wall)")]
    [SerializeField] private Material[] midDecals = new Material[0];
    [SerializeField, Range(0f, 1f)] private float midChance = 0.45f;
    [SerializeField, Min(0.1f)] private float midSize = 1.4f;

    [Header("Top Decals (hang from the roofline)")]
    [SerializeField] private Material[] topDecals = new Material[0];
    [SerializeField, Range(0f, 1f)] private float topChance = 0.65f;
    [SerializeField, Min(0.1f)] private float topHeight = 1.5f;

    [Header("Placement")]
    [Tooltip("How far decals float off the wall, so they never flicker against it.")]
    [SerializeField, Range(0.002f, 0.1f)] private float offset = 0.015f;
    [Tooltip("Decals are at most this fraction of the wall's width.")]
    [SerializeField, Range(0.3f, 1.2f)] private float maxWidthOfWall = 1f;

    public Material[] WallVariants => wallVariants;
    public Material SeamlessMaterial => seamlessMaterial;
    public bool HasWalls => seamlessMaterial != null || (wallVariants != null && wallVariants.Length > 0);
    public float TextureSize => textureSize;

    public Material[] BottomDecals => bottomDecals;
    public float BottomChance => bottomChance;
    public float BottomHeight => bottomHeight;
    public Material[] MidDecals => midDecals;
    public float MidChance => midChance;
    public float MidSize => midSize;
    public Material[] TopDecals => topDecals;
    public float TopChance => topChance;
    public float TopHeight => topHeight;
    public float Offset => offset;
    public float MaxWidthOfWall => maxWidthOfWall;

#if UNITY_EDITOR
    public void EditorSetup(Material seamless, Material[] walls, Material[] bottom, Material[] mid, Material[] top)
    {
        seamlessMaterial = seamless;
        wallVariants = walls;
        bottomDecals = bottom;
        midDecals = mid;
        topDecals = top;
    }
#endif
}
