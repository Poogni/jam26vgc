using UnityEngine;

/// <summary>
/// Tiny software rasterizer for the map's pixel art (Color32 buffers, origin bottom-left, y up).
/// Shapes are hard-edged on purpose: they should look like pixel art, not smooth vector graphics.
/// </summary>
public static class PixelArt
{
    public static void Set(Color32[] px, int w, int h, int x, int y, Color32 c)
    {
        if (x < 0 || y < 0 || x >= w || y >= h) return;
        if (c.a == 255) { px[y * w + x] = c; return; }
        // Alpha-blend over what's there (keeps the destination's alpha at least as opaque).
        Color32 d = px[y * w + x];
        float a = c.a / 255f;
        px[y * w + x] = new Color32(
            (byte)Mathf.Lerp(d.r, c.r, a), (byte)Mathf.Lerp(d.g, c.g, a), (byte)Mathf.Lerp(d.b, c.b, a),
            (byte)Mathf.Max(d.a, c.a));
    }

    public static void Line(Color32[] px, int w, int h, Vector2Int a, Vector2Int b, Color32 c)
    {
        int dx = Mathf.Abs(b.x - a.x), sx = a.x < b.x ? 1 : -1;
        int dy = -Mathf.Abs(b.y - a.y), sy = a.y < b.y ? 1 : -1;
        int err = dx + dy;
        while (true)
        {
            Set(px, w, h, a.x, a.y, c);
            if (a == b) break;
            int e2 = 2 * err;
            if (e2 >= dy) { err += dy; a.x += sx; }
            if (e2 <= dx) { err += dx; a.y += sy; }
        }
    }

    /// <summary>Filled disc (pixel centers within radius).</summary>
    public static void Disc(Color32[] px, int w, int h, Vector2 center, float radius, Color32 c)
    {
        int r = Mathf.CeilToInt(radius);
        for (int y = -r; y <= r; y++)
        for (int x = -r; x <= r; x++)
        {
            int ix = Mathf.FloorToInt(center.x) + x, iy = Mathf.FloorToInt(center.y) + y;
            if (Vector2.Distance(new Vector2(ix + 0.5f, iy + 0.5f), center) <= radius) Set(px, w, h, ix, iy, c);
        }
    }

    /// <summary>Ring of the given thickness (outer radius).</summary>
    public static void Ring(Color32[] px, int w, int h, Vector2 center, float radius, float thickness, Color32 c)
    {
        int r = Mathf.CeilToInt(radius);
        for (int y = -r; y <= r; y++)
        for (int x = -r; x <= r; x++)
        {
            int ix = Mathf.FloorToInt(center.x) + x, iy = Mathf.FloorToInt(center.y) + y;
            float d = Vector2.Distance(new Vector2(ix + 0.5f, iy + 0.5f), center);
            if (d <= radius && d > radius - thickness) Set(px, w, h, ix, iy, c);
        }
    }

    public static void Triangle(Color32[] px, int w, int h, Vector2 a, Vector2 b, Vector2 c, Color32 color)
    {
        int minX = Mathf.FloorToInt(Mathf.Min(a.x, Mathf.Min(b.x, c.x)));
        int maxX = Mathf.CeilToInt(Mathf.Max(a.x, Mathf.Max(b.x, c.x)));
        int minY = Mathf.FloorToInt(Mathf.Min(a.y, Mathf.Min(b.y, c.y)));
        int maxY = Mathf.CeilToInt(Mathf.Max(a.y, Mathf.Max(b.y, c.y)));
        for (int y = minY; y <= maxY; y++)
        for (int x = minX; x <= maxX; x++)
        {
            var p = new Vector2(x + 0.5f, y + 0.5f);
            float d1 = Cross(p, a, b), d2 = Cross(p, b, c), d3 = Cross(p, c, a);
            bool neg = d1 < 0 || d2 < 0 || d3 < 0, pos = d1 > 0 || d2 > 0 || d3 > 0;
            if (!(neg && pos)) Set(px, w, h, x, y, color);
        }
    }

    private static float Cross(Vector2 p, Vector2 a, Vector2 b) => (p.x - b.x) * (a.y - b.y) - (a.x - b.x) * (p.y - b.y);

    // ------------------------------------------------------------------ 5x5 letters

    private static readonly string[] N = { "10001", "11001", "10101", "10011", "10001" };
    private static readonly string[] E = { "11111", "10000", "11110", "10000", "11111" };
    private static readonly string[] S = { "01111", "10000", "01110", "00001", "11110" };
    private static readonly string[] W = { "10001", "10001", "10101", "11011", "10001" };

    /// <summary>Draws N, E, S or W (5x5) centered on a pixel position.</summary>
    public static void Letter(Color32[] px, int w, int h, char letter, Vector2 center, Color32 c)
    {
        string[] g = letter switch { 'N' => N, 'E' => E, 'S' => S, _ => W };
        int x0 = Mathf.RoundToInt(center.x) - 2, y0 = Mathf.RoundToInt(center.y) + 2;
        for (int row = 0; row < 5; row++)
        for (int col = 0; col < 5; col++)
            if (g[row][col] == '1') Set(px, w, h, x0 + col, y0 - row, c);
    }
}
