using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Generates the printed part of the cartography map: a parchment paper layer, a subtle grid with one cell per
/// level cell, and the player's starting point. Lives on the MapFrame; sizes the frame to the grid's aspect
/// inside the MapArea. The ink layer (MapCanvas) uses the same pixel resolution, so drawings line up with cells,
/// and since all of this is underneath the ink, the eraser never removes it.
/// </summary>
[ExecuteAlways]
public class MapGrid : MonoBehaviour
{
    [Tooltip("Read the size and start cell from this level. Found from the scene's LevelBuilder if empty.")]
    [SerializeField] private LevelData level;
    [Tooltip("Used when there's no level.")]
    [SerializeField] private Vector2Int fallbackSize = new Vector2Int(22, 22);

    [Tooltip("Pixels per cell (the ink canvas uses the same pixels).")]
    [SerializeField, Range(4, 32)] private int cellPixels = 10;

    [Header("Paper")]
    [SerializeField] private RawImage paperImage;
    [SerializeField] private Color paperColor = new Color(0.86f, 0.79f, 0.63f);
    [Tooltip("Per-pixel grain.")]
    [SerializeField, Range(0f, 0.15f)] private float grain = 0.035f;
    [Tooltip("Large faint stains / uneven tone.")]
    [SerializeField, Range(0f, 0.3f)] private float stains = 0.09f;
    [Tooltip("Darkening toward the edges, like old paper.")]
    [SerializeField, Range(0f, 0.6f)] private float edgeDarkening = 0.28f;
    [SerializeField, Range(0, 400)] private int fibers = 120;
    [SerializeField] private int paperSeed = 7;

    [Header("Grid")]
    [SerializeField] private RawImage gridImage;
    [SerializeField] private Color lineColor = new Color(0.36f, 0.26f, 0.16f, 0.15625f);
    [Tooltip("Every Nth line is drawn a bit stronger, for counting. 0 = off.")]
    [SerializeField, Min(0)] private int majorEvery = 5;
    [SerializeField] private Color majorLineColor = new Color(0.36f, 0.26f, 0.16f, 0.265625f);

    [Header("Start Point")]
    [Tooltip("Layer drawn above the ink, so the start mark is never hidden or erased.")]
    [SerializeField] private RawImage markerImage;
    [SerializeField] private bool showStart = true;
    [SerializeField] private Color startColor = new Color(0.62f, 0.13f, 0.08f, 0.95f);

    private Texture2D _paper;
    private Texture2D _grid;
    private Texture2D _markers;

    public int Columns => Level != null ? Level.Width : fallbackSize.x;
    public int Rows => Level != null ? Level.Height : fallbackSize.y;
    public int CellPixels => cellPixels;
    /// <summary>Pixel size of the map (and the ink canvas): one shared line between cells and around the edge.</summary>
    public Vector2Int Resolution => new Vector2Int(Columns * cellPixels + 1, Rows * cellPixels + 1);

    /// <summary>The level whose size the grid follows (looked up on demand, so other scripts' Awake can use it).</summary>
    public LevelData Level
    {
        get
        {
            if (level == null)
            {
                LevelBuilder builder = FindAnyObjectByType<LevelBuilder>();
                if (builder != null) level = builder.Level;
            }
            return level;
        }
    }

    private void OnEnable() => Rebuild();

    private void OnValidate()
    {
        if (isActiveAndEnabled) Rebuild();
    }

    private void OnDisable()
    {
        Release(ref _paper, paperImage);
        Release(ref _grid, gridImage);
        Release(ref _markers, markerImage);
    }

    public void Rebuild()
    {
        FitToParent();
        Vector2Int res = Resolution;
        if (paperImage != null) paperImage.texture = Generate(ref _paper, "Map Paper", res, FillPaper);
        if (gridImage != null) gridImage.texture = Generate(ref _grid, "Map Grid", res, FillGrid);
        if (markerImage != null) markerImage.texture = Generate(ref _markers, "Map Markers", res, FillMarkers);
    }

    // ------------------------------------------------------------------ paper

    private void FillPaper(Color32[] px, Vector2Int res)
    {
        var rng = new System.Random(paperSeed);
        float ox = paperSeed * 13.7f, oy = paperSeed * 7.3f;

        for (int y = 0; y < res.y; y++)
        for (int x = 0; x < res.x; x++)
        {
            float u = (float)x / (res.x - 1), v = (float)y / (res.y - 1);

            // Uneven tone: two octaves of soft noise.
            float stain = (Mathf.PerlinNoise(u * 3f + ox, v * 3f + oy) - 0.5f) * 1.0f
                        + (Mathf.PerlinNoise(u * 9f + oy, v * 9f + ox) - 0.5f) * 0.5f;

            // Edge darkening: strongest in the corners.
            float edge = Mathf.Min(Mathf.Min(u, 1f - u), Mathf.Min(v, 1f - v)) * 2f; // 0 at edge, 1 at center
            float darken = edgeDarkening * Mathf.Pow(1f - Mathf.Clamp01(edge * 1.6f), 2f);

            float g = ((float)rng.NextDouble() - 0.5f) * 2f * grain;
            float shade = 1f + stain * stains + g - darken;

            Color c = paperColor * shade;
            c.a = 1f;
            px[y * res.x + x] = c;
        }

        // A few short fibers: slightly lighter or darker 2-4 pixel dashes.
        for (int i = 0; i < fibers; i++)
        {
            int x = rng.Next(res.x), y = rng.Next(res.y), len = rng.Next(2, 5);
            bool horizontal = rng.NextDouble() < 0.5;
            float tone = rng.NextDouble() < 0.5 ? 0.93f : 1.05f;
            for (int k = 0; k < len; k++)
            {
                int fx = horizontal ? x + k : x, fy = horizontal ? y : y + k;
                if (fx >= res.x || fy >= res.y) break;
                Color c = px[fy * res.x + fx];
                px[fy * res.x + fx] = new Color(c.r * tone, c.g * tone, c.b * tone, 1f);
            }
        }
    }

    // ------------------------------------------------------------------ grid + markers

    private void FillGrid(Color32[] px, Vector2Int res)
    {
        Color32 line = lineColor, major = majorLineColor;
        for (int y = 0; y < res.y; y++)
        for (int x = 0; x < res.x; x++)
        {
            bool onX = x % cellPixels == 0, onY = y % cellPixels == 0;
            if (!onX && !onY) continue;
            bool isMajor = majorEvery > 0 &&
                           ((onX && (x / cellPixels) % majorEvery == 0) || (onY && (y / cellPixels) % majorEvery == 0));
            px[y * res.x + x] = isMajor ? major : line;
        }

    }

    private void FillMarkers(Color32[] px, Vector2Int res)
    {
        if (showStart && Level != null) DrawStart(px, res, Level.PlayerStart, Level.StartFacing);
    }

    /// <summary>A filled arrow inside the start cell, pointing in the start facing.</summary>
    private void DrawStart(Color32[] px, Vector2Int res, Vector2Int cell, Facing facing)
    {
        Color32 c = startColor;
        float half = (cellPixels - 2) * 0.5f;
        Vector2 center = new Vector2(cell.x * cellPixels + cellPixels * 0.5f, cell.y * cellPixels + cellPixels * 0.5f);
        Vector2 dir = facing switch
        {
            Facing.North => Vector2.up,
            Facing.East => Vector2.right,
            Facing.South => Vector2.down,
            _ => Vector2.left,
        };
        Vector2 side = new Vector2(-dir.y, dir.x);
        Vector2 tip = center + dir * half;
        Vector2 a = center - dir * half * 0.8f + side * half * 0.85f;
        Vector2 b = center - dir * half * 0.8f - side * half * 0.85f;

        for (int y = cell.y * cellPixels + 1; y < (cell.y + 1) * cellPixels; y++)
        for (int x = cell.x * cellPixels + 1; x < (cell.x + 1) * cellPixels; x++)
        {
            if (x < 0 || y < 0 || x >= res.x || y >= res.y) continue;
            Vector2 p = new Vector2(x + 0.5f, y + 0.5f);
            if (InTriangle(p, tip, a, b)) px[y * res.x + x] = c;
        }
    }

    private static bool InTriangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
    {
        float d1 = Cross(p, a, b), d2 = Cross(p, b, c), d3 = Cross(p, c, a);
        bool neg = d1 < 0 || d2 < 0 || d3 < 0, pos = d1 > 0 || d2 > 0 || d3 > 0;
        return !(neg && pos);
    }

    private static float Cross(Vector2 p, Vector2 a, Vector2 b) => (p.x - b.x) * (a.y - b.y) - (a.x - b.x) * (p.y - b.y);

    // ------------------------------------------------------------------ helpers

    private delegate void Filler(Color32[] px, Vector2Int res);

    private static Texture2D Generate(ref Texture2D tex, string texName, Vector2Int res, Filler fill)
    {
        if (tex == null || tex.width != res.x || tex.height != res.y)
        {
            if (tex != null) DestroyImmediate(tex);
            tex = new Texture2D(res.x, res.y, TextureFormat.RGBA32, false)
            {
                name = texName,
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.DontSave,
            };
        }
        var pixels = new Color32[res.x * res.y];
        fill(pixels, res);
        tex.SetPixels32(pixels);
        tex.Apply(false);
        return tex;
    }

    private static void Release(ref Texture2D tex, RawImage image)
    {
        if (image != null && image.texture == tex) image.texture = null;
        if (tex == null) return;
        if (Application.isPlaying) Destroy(tex);
        else DestroyImmediate(tex);
        tex = null;
    }

    /// <summary>Largest rect with the grid's aspect that fits the parent (MapArea), centered.</summary>
    private void FitToParent()
    {
        var rt = (RectTransform)transform;
        if (!(rt.parent is RectTransform parent)) return;
        float aspect = (float)Columns / Rows;
        Vector2 area = parent.rect.size;
        if (area.x <= 0f || area.y <= 0f) return;
        Vector2 size = area.x / area.y > aspect
            ? new Vector2(Mathf.Round(area.y * aspect), area.y)
            : new Vector2(area.x, Mathf.Round(area.x / aspect));
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = size;
        rt.anchoredPosition = Vector2.zero;
    }
}
