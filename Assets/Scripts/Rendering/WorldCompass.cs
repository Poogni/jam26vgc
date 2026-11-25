using UnityEngine;

/// <summary>
/// Skyrim-style compass strip that lives in the world: a quad attached to the game camera, near the top of the
/// view, rendered by the game camera itself, so it gets the same low-res pixels, CRT curve, scanlines and
/// motion trails as everything else. Cardinal letters and ticks slide along it as the player turns.
/// Put it on a child of the game camera (Tools > Dungeon > Create World Compass).
/// </summary>
[ExecuteAlways]
[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class WorldCompass : MonoBehaviour
{
    [Tooltip("Whose heading to show. Uses the player if empty, else the camera.")]
    [SerializeField] private Transform headingSource;
    [SerializeField] private Shader overlayShader;

    [Header("Placement (fractions of the view)")]
    [SerializeField, Range(0.2f, 1f)] private float widthOfView = 0.62f;
    [Tooltip("Gap between the top of the view and the strip.")]
    [SerializeField, Range(0f, 0.3f)] private float topMargin = 0.07f;
    [Tooltip("Distance in front of the camera. Only needs to be past the near plane; it's always drawn on top.")]
    [SerializeField, Min(0.06f)] private float distance = 0.3f;

    [Header("Strip")]
    [Tooltip("Pixels across the strip. Roughly match the 3D view's pixels for crisp results.")]
    [SerializeField, Range(64, 256)] private int widthPixels = 128;
    [Tooltip("Degrees of heading visible across the strip.")]
    [SerializeField, Range(90f, 360f)] private float span = 180f;

    [Header("Look")]
    [SerializeField] private Color bandColor = new Color(0.07f, 0.05f, 0.03f, 0.6f);
    [SerializeField] private Color lineColor = new Color(0.93f, 0.84f, 0.68f, 0.45f);
    [SerializeField] private Color tickColor = new Color(0.93f, 0.84f, 0.68f, 0.85f);
    [SerializeField] private Color letterColor = new Color(0.96f, 0.89f, 0.74f, 1f);
    [SerializeField] private Color northColor = new Color(0.86f, 0.22f, 0.14f, 1f);
    [SerializeField] private Color markerColor = new Color(0.85f, 0.66f, 0.32f, 1f);

    private const int HeightPixels = 15;

    private Texture2D _texture;
    private Color32[] _pixels;
    private Material _material;
    private float _drawnHeading = float.NaN;

    /// <summary>Heading shown right now, in degrees (0 = north, 90 = east).</summary>
    public float Heading { get; private set; }

    private void OnEnable()
    {
        if (overlayShader == null) overlayShader = Shader.Find("Retro/World Overlay");
        _texture = new Texture2D(widthPixels, HeightPixels, TextureFormat.RGBA32, false)
        {
            name = "World Compass",
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp,
            hideFlags = HideFlags.DontSave,
        };
        _pixels = new Color32[widthPixels * HeightPixels];
        _material = new Material(overlayShader) { name = "World Compass (instance)", hideFlags = HideFlags.DontSave };
        _material.mainTexture = _texture;

        var r = GetComponent<MeshRenderer>();
        r.sharedMaterial = _material;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        r.receiveShadows = false;
        _drawnHeading = float.NaN;
    }

    private void OnDisable()
    {
        GetComponent<MeshRenderer>().sharedMaterial = null;
        DestroySafe(_material);
        DestroySafe(_texture);
    }

    // Settings changed: redraw on the next LateUpdate (textures can't be safely recreated inside OnValidate).
    private void OnValidate() => _drawnHeading = float.NaN;

    private void LateUpdate()
    {
        if (_texture == null || _texture.width != widthPixels)
        {
            OnDisable();
            OnEnable();
        }

        Camera cam = GetComponentInParent<Camera>();
        if (cam == null) return;
        Place(cam);

        if (headingSource == null)
        {
            GridPlayerController player = FindAnyObjectByType<GridPlayerController>();
            headingSource = player != null ? player.transform : cam.transform;
        }
        Vector3 f = headingSource.forward;
        Heading = Mathf.Atan2(f.x, f.z) * Mathf.Rad2Deg;

        // Redraw only when the strip would move by at least half a pixel.
        float halfPixelDegrees = span / widthPixels * 0.5f;
        if (!float.IsNaN(_drawnHeading) && Mathf.Abs(Mathf.DeltaAngle(Heading, _drawnHeading)) < halfPixelDegrees) return;
        _drawnHeading = Heading;
        Draw(Heading);
    }

    /// <summary>Keep the quad at the top-center of the view, whatever the FOV / aspect.</summary>
    private void Place(Camera cam)
    {
        float halfH = distance * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
        float halfW = halfH * cam.aspect;
        float width = 2f * halfW * widthOfView;
        float height = width * HeightPixels / widthPixels; // square pixels
        transform.localPosition = new Vector3(0f, halfH - topMargin * 2f * halfH - height * 0.5f, distance);
        transform.localRotation = Quaternion.identity;
        transform.localScale = new Vector3(width, height, 1f);
    }

    private void Draw(float heading)
    {
        int w = widthPixels, h = HeightPixels;
        for (int i = 0; i < _pixels.Length; i++) _pixels[i] = default;

        float pxPerDeg = w / span;
        float cx = w * 0.5f;
        const int bandLo = 2, bandHi = 10, mid = 6;

        // Dark band and center line, fading out at both ends.
        for (int x = 0; x < w; x++)
        {
            float fade = Mathf.Clamp01(Mathf.Min(x, w - 1 - x) / (w * 0.18f));
            fade *= fade;
            Color band = bandColor; band.a *= fade;
            Color line = lineColor; line.a *= fade;
            for (int y = bandLo; y <= bandHi; y++) PixelArt.Set(_pixels, w, h, x, y, band);
            PixelArt.Set(_pixels, w, h, x, mid, line);
        }

        // Ticks and letters for every bearing in view.
        int first = Mathf.FloorToInt((heading - span * 0.5f) / 15f) * 15;
        for (int b = first; b <= heading + span * 0.5f + 15f; b += 15)
        {
            float x = cx + Mathf.DeltaAngle(heading, b) * pxPerDeg;
            int ix = Mathf.FloorToInt(x);
            if (ix < 1 || ix > w - 2) continue;
            float fade = Mathf.Clamp01(Mathf.Min(ix, w - 1 - ix) / (w * 0.18f));
            int bearing = ((b % 360) + 360) % 360;

            if (bearing % 90 == 0)
            {
                char letter = bearing == 0 ? 'N' : bearing == 90 ? 'E' : bearing == 180 ? 'S' : 'W';
                Color c = bearing == 0 ? northColor : letterColor;
                if (fade < 0.15f) continue; // fully faded at the ends: skip rather than leave a ghost box
                c.a *= fade;
                // Clear the line behind the letter so it reads.
                for (int y = mid - 3; y <= mid + 3; y++)
                for (int dx = -3; dx <= 3; dx++)
                {
                    int px = ix + dx;
                    if (px < 0 || px >= w) continue;
                    // Same per-column fade as the band, so no box shows near the ends.
                    float colFade = Mathf.Clamp01(Mathf.Min(px, w - 1 - px) / (w * 0.18f));
                    Color band = bandColor; band.a *= colFade * colFade;
                    _pixels[y * w + px] = band;
                }
                PixelArt.Letter(_pixels, w, h, letter, new Vector2(ix, mid), c);
            }
            else
            {
                bool major = bearing % 45 == 0;
                Color t = tickColor; t.a *= fade * (major ? 1f : 0.6f);
                int reach = major ? 2 : 1;
                for (int y = mid - reach; y <= mid + reach; y++) PixelArt.Set(_pixels, w, h, ix, y, t);
            }
        }

        // Center marker: a small brass notch above and below the band.
        int c0 = Mathf.FloorToInt(cx);
        Color32 m = markerColor;
        for (int dx = -2; dx <= 2; dx++) PixelArt.Set(_pixels, w, h, c0 + dx, bandHi + 2, m);
        for (int dx = -1; dx <= 1; dx++) PixelArt.Set(_pixels, w, h, c0 + dx, bandHi + 1, m);
        PixelArt.Set(_pixels, w, h, c0, bandHi + 0, m);
        for (int dx = -1; dx <= 1; dx++) PixelArt.Set(_pixels, w, h, c0 + dx, bandLo - 1, m);
        PixelArt.Set(_pixels, w, h, c0, bandLo, m);

        _texture.SetPixels32(_pixels);
        _texture.Apply(false);
    }

    private static void DestroySafe(Object o)
    {
        if (o == null) return;
        if (Application.isPlaying) Destroy(o);
        else DestroyImmediate(o);
    }
}
