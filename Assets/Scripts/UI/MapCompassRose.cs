using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Static compass rose for the cartography tool bar: shows that north is up on the map.
/// Pixel art on a small parchment disc, generated into this RawImage (also visible in Edit mode).
/// </summary>
[ExecuteAlways]
[RequireComponent(typeof(RawImage))]
public class MapCompassRose : MonoBehaviour
{
    [Tooltip("Texture size in pixels (shown upscaled).")]
    [SerializeField, Range(24, 96)] private int size = 40;
    [SerializeField] private Color paperColor = new Color(0.86f, 0.79f, 0.63f, 1f);
    [SerializeField] private Color rimColor = new Color(0.45f, 0.33f, 0.18f, 1f);
    [SerializeField] private Color inkColor = new Color(0.30f, 0.20f, 0.12f, 1f);
    [SerializeField] private Color inkLight = new Color(0.62f, 0.49f, 0.33f, 1f);
    [SerializeField] private Color northColor = new Color(0.66f, 0.14f, 0.08f, 1f);

    private Texture2D _texture;

    private void OnEnable() => Draw();
    private void OnValidate() { if (isActiveAndEnabled) Draw(); }

    private void OnDisable()
    {
        var image = GetComponent<RawImage>();
        if (image != null && image.texture == _texture) image.texture = null;
        if (_texture == null) return;
        if (Application.isPlaying) Destroy(_texture);
        else DestroyImmediate(_texture);
        _texture = null;
    }

    private void Draw()
    {
        int w = size, h = size;
        if (_texture == null || _texture.width != w)
        {
            if (_texture != null) DestroyImmediate(_texture);
            _texture = new Texture2D(w, h, TextureFormat.RGBA32, false)
            {
                name = "Map Compass Rose",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.DontSave,
            };
        }

        var px = new Color32[w * h];
        Vector2 c = new Vector2(w * 0.5f, h * 0.5f);
        float R = w * 0.5f - 0.5f;

        PixelArt.Disc(px, w, h, c, R, rimColor);
        PixelArt.Disc(px, w, h, c, R - 2f, paperColor);
        PixelArt.Ring(px, w, h, c, R - 3.5f, 1f, inkLight);

        // Short diagonal points, then the long cardinal points (north in red), then the N inside the face.
        float L = R - 9f;
        for (int i = 0; i < 4; i++) Point(px, w, h, c, 45f + i * 90f, L * 0.6f, L * 0.18f, inkLight, inkLight);
        for (int i = 0; i < 4; i++) Point(px, w, h, c, i * 90f, L, L * 0.28f, i == 0 ? northColor : inkColor, inkLight);
        PixelArt.Letter(px, w, h, 'N', c + new Vector2(0.5f, R - 6f), northColor);

        _texture.SetPixels32(px);
        _texture.Apply(false);
        GetComponent<RawImage>().texture = _texture;
    }

    /// <summary>One kite-shaped point: one half dark, one half light, like an engraved rose.</summary>
    private static void Point(Color32[] px, int w, int h, Vector2 c, float degreesFromNorth, float length, float width,
        Color32 dark, Color32 light)
    {
        float rad = degreesFromNorth * Mathf.Deg2Rad;
        Vector2 dir = new Vector2(Mathf.Sin(rad), Mathf.Cos(rad));
        Vector2 side = new Vector2(dir.y, -dir.x);
        Vector2 tip = c + dir * length;
        PixelArt.Triangle(px, w, h, tip, c, c + side * width, dark);
        PixelArt.Triangle(px, w, h, tip, c, c - side * width, light);
    }
}
