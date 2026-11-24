using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// Freehand pixel drawing on the cartography map, like the pencil in Paint, with a weighted brush:
/// the brush trails the cursor with some inertia (smooths shaky strokes) and draws heavier when moving slowly.
/// Sits on a transparent "ink" RawImage over the generated grid, so the eraser only removes ink.
/// Left mouse draws with the current tool, right mouse always erases. Drawing throws off pixel particles.
/// Built by Tools > Dungeon > Create Map Drawing.
/// </summary>
[RequireComponent(typeof(RawImage))]
public class MapCanvas : MonoBehaviour
{
    public enum Tool { Pen, Eraser }

    [Header("Canvas")]
    [Tooltip("Ink resolution comes from this grid so lines up with its cells. Found in the parent if empty.")]
    [SerializeField] private MapGrid grid;
    [Tooltip("Used only when there's no MapGrid.")]
    [SerializeField] private Vector2Int fallbackResolution = new Vector2Int(221, 221);

    [Header("Weighted Brush")]
    [Tooltip("How heavy the brush feels. 0 = sticks to the cursor, 0.9 = drags far behind (very smooth lines).")]
    [SerializeField, Range(0f, 0.95f)] private float weight = 0.55f;
    [Tooltip("Brush speed (ink pixels per second) at which the line is at its thinnest.")]
    [SerializeField, Min(1f)] private float speedForThinnest = 220f;

    [Header("Pen")]
    [Tooltip("Sepia ink, readable on the paper.")]
    [SerializeField] private Color penColor = new Color(0.20f, 0.13f, 0.08f);
    [Tooltip("Radius in ink pixels of the dot where a stroke starts.")]
    [SerializeField, Range(0, 8)] private int penRadiusStart = 1;
    [Tooltip("Radius in ink pixels when moving slowly (heavy).")]
    [SerializeField, Range(0, 8)] private int penRadiusSlow = 1;
    [Tooltip("Radius in ink pixels when moving fast (light).")]
    [SerializeField, Range(0, 8)] private int penRadiusFast = 0;

    [Header("Eraser")]
    [SerializeField, Range(0, 12)] private int eraserRadius = 3;

    [Header("Particles")]
    [SerializeField] private MapParticles particles;
    [Tooltip("Particles per ink pixel travelled.")]
    [SerializeField, Range(0f, 2f)] private float particlesPerPixel = 0.35f;
    [SerializeField] private Color eraserCrumbColor = new Color(0.86f, 0.58f, 0.56f, 0.95f); // pink rubber

    [Header("Tool Buttons (optional)")]
    [SerializeField] private Button penButton;
    [SerializeField] private Button eraserButton;
    [SerializeField] private Color buttonNormal = new Color(0.25f, 0.19f, 0.13f);
    [SerializeField] private Color buttonSelected = new Color(0.93f, 0.84f, 0.68f);
    [SerializeField] private Color labelNormal = new Color(0.93f, 0.84f, 0.68f);
    [SerializeField] private Color labelSelected = new Color(0.12f, 0.09f, 0.06f);

    public Tool CurrentTool { get; private set; } = Tool.Pen;

    private RawImage _image;
    private Texture2D _texture;
    private Color32[] _pixels;
    private Vector2Int _res;
    private bool _dirty;

    // Stroke state, in ink-pixel space (floats so the weighted brush can move smoothly).
    private bool _stroking;
    private bool _strokeErases;
    private Vector2 _brush;
    private Vector2 _target;
    private float _particleDebt;

    private static readonly Color32 Clear = new Color32(0, 0, 0, 0);

    private void Awake()
    {
        _image = GetComponent<RawImage>();
        if (grid == null) grid = GetComponentInParent<MapGrid>();
        _res = grid != null ? grid.Resolution : fallbackResolution;

        _texture = new Texture2D(_res.x, _res.y, TextureFormat.RGBA32, false)
        {
            name = "Map Ink",
            filterMode = FilterMode.Point, // crisp pixels
            wrapMode = TextureWrapMode.Clamp,
        };
        _pixels = new Color32[_res.x * _res.y];
        ClearInk();
        _image.texture = _texture;

        if (penButton != null) penButton.onClick.AddListener(() => SetTool(Tool.Pen));
        if (eraserButton != null) eraserButton.onClick.AddListener(() => SetTool(Tool.Eraser));
        SetTool(Tool.Pen);
    }

    private void Start()
    {
        // Particles snap to the same pixel size as the ink.
        if (particles != null) particles.PixelSize = ((RectTransform)transform).rect.width / _res.x;
    }

    private void OnDestroy()
    {
        if (_texture != null) Destroy(_texture);
    }

    public void SetTool(Tool tool)
    {
        CurrentTool = tool;
        Highlight(penButton, tool == Tool.Pen);
        Highlight(eraserButton, tool == Tool.Eraser);
    }

    /// <summary>Wipe all ink (the grid underneath is untouched).</summary>
    public void ClearInk()
    {
        for (int i = 0; i < _pixels.Length; i++) _pixels[i] = Clear;
        _dirty = true;
    }

    // ------------------------------------------------------------------ input / stroke

    private void Update()
    {
        Mouse mouse = Mouse.current;
        if (mouse == null) return;

        bool held = mouse.leftButton.isPressed || mouse.rightButton.isPressed;
        if (GameInputLock.IsLocked)
        {
            _stroking = false;
            return;
        }

        Vector2 cursor = ScreenToInk(mouse.position.ReadValue());

        if (!_stroking)
        {
            // A stroke only starts on a fresh press inside the map.
            bool fresh = mouse.leftButton.wasPressedThisFrame || mouse.rightButton.wasPressedThisFrame;
            if (!fresh || !Inside(cursor)) return;
            BeginStroke(cursor, mouse.rightButton.isPressed || CurrentTool == Tool.Eraser);
            return;
        }

        _target = ClampToCanvas(cursor); // leaving the map clips the line at its edge

        if (!held)
        {
            // Released: the weighted brush catches up to where the cursor stopped.
            MoveBrushTo(_target, Time.unscaledDeltaTime);
            _stroking = false;
            return;
        }

        // Weighted follow, frame-rate independent (weight is "how much stays behind per 1/60 s").
        float dt = Mathf.Max(Time.unscaledDeltaTime, 1e-4f);
        float follow = 1f - Mathf.Pow(weight, dt * 60f);
        MoveBrushTo(Vector2.Lerp(_brush, _target, follow), dt);
    }

    private void BeginStroke(Vector2 at, bool erase)
    {
        _stroking = true;
        _strokeErases = erase;
        _brush = _target = at;
        _particleDebt = 0f;
        Stamp(Round(at), erase ? eraserRadius : penRadiusStart);
        Burst(at, 4);
    }

    private void MoveBrushTo(Vector2 next, float dt)
    {
        float dist = Vector2.Distance(_brush, next);
        if (dist < 0.01f) return;

        // Heavier (thicker) when slow, lighter when fast.
        int radius = eraserRadius;
        if (!_strokeErases)
        {
            float speed01 = Mathf.Clamp01(dist / dt / speedForThinnest);
            radius = Mathf.RoundToInt(Mathf.Lerp(penRadiusSlow, penRadiusFast, speed01));
        }

        DrawLine(Round(_brush), Round(next), radius);
        EmitAlong(_brush, next, dist);
        _brush = next;
    }

    // ------------------------------------------------------------------ pixels

    private void LateUpdate()
    {
        if (!_dirty) return;
        _texture.SetPixels32(_pixels);
        _texture.Apply(false);
        _dirty = false;
    }

    private void DrawLine(Vector2Int a, Vector2Int b, int radius)
    {
        // Bresenham, so fast strokes stay continuous.
        int dx = Mathf.Abs(b.x - a.x), sx = a.x < b.x ? 1 : -1;
        int dy = -Mathf.Abs(b.y - a.y), sy = a.y < b.y ? 1 : -1;
        int err = dx + dy;
        while (true)
        {
            Stamp(a, radius);
            if (a == b) break;
            int e2 = 2 * err;
            if (e2 >= dy) { err += dy; a.x += sx; }
            if (e2 <= dx) { err += dx; a.y += sy; }
        }
    }

    private void Stamp(Vector2Int c, int r)
    {
        Color32 color = _strokeErases ? Clear : (Color32)penColor;
        int r2 = r * r + r; // slightly fuller circles at small radii
        for (int y = -r; y <= r; y++)
        for (int x = -r; x <= r; x++)
        {
            if (x * x + y * y > r2) continue;
            int px = c.x + x, py = c.y + y;
            if (px < 0 || py < 0 || px >= _res.x || py >= _res.y) continue;
            _pixels[py * _res.x + px] = color;
        }
        _dirty = true;
    }

    // ------------------------------------------------------------------ particles

    private void EmitAlong(Vector2 from, Vector2 to, float dist)
    {
        if (particles == null) return;
        _particleDebt += dist * particlesPerPixel;
        while (_particleDebt >= 1f)
        {
            _particleDebt -= 1f;
            EmitOne(Vector2.Lerp(from, to, Random.value), (to - from).normalized);
        }
    }

    private void Burst(Vector2 at, int count)
    {
        if (particles == null) return;
        for (int i = 0; i < count; i++) EmitOne(at, Random.insideUnitCircle.normalized);
    }

    private void EmitOne(Vector2 inkPos, Vector2 strokeDir)
    {
        float px = particles.PixelSize;
        Vector2 local = InkToLocal(inkPos);
        if (_strokeErases)
        {
            // Eraser crumbs: kicked sideways, then fall.
            Vector2 side = new Vector2(-strokeDir.y, strokeDir.x) * (Random.value < 0.5f ? -1f : 1f);
            Vector2 vel = (side * Random.Range(6f, 14f) + Vector2.up * Random.Range(2f, 8f)) * px;
            particles.Emit(local, vel, eraserCrumbColor, Random.Range(0.35f, 0.7f), 60f * px, Random.Range(1, 3));
        }
        else
        {
            // Ink droplets / sparkle: flicked off the nib, slight drift, quick fade.
            Vector2 vel = (Random.insideUnitCircle * 10f - strokeDir * 4f) * px;
            Color c = penColor;
            c.a = Random.Range(0.6f, 1f);
            particles.Emit(local, vel, c, Random.Range(0.25f, 0.5f), 12f * px, 1);
        }
    }

    // ------------------------------------------------------------------ coordinates

    private static Vector2Int Round(Vector2 p) => new Vector2Int(Mathf.FloorToInt(p.x), Mathf.FloorToInt(p.y));

    private bool Inside(Vector2 ink) => ink.x >= 0f && ink.y >= 0f && ink.x < _res.x && ink.y < _res.y;

    private Vector2 ClampToCanvas(Vector2 ink) =>
        new Vector2(Mathf.Clamp(ink.x, 0f, _res.x - 0.01f), Mathf.Clamp(ink.y, 0f, _res.y - 0.01f));

    /// <summary>Screen position to ink-pixel coordinates (may be outside the canvas).</summary>
    private Vector2 ScreenToInk(Vector2 screenPos)
    {
        var rt = (RectTransform)transform;
        Canvas canvas = _image.canvas;
        Camera uiCam = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(rt, screenPos, uiCam, out Vector2 local);
        Rect r = rt.rect;
        return new Vector2((local.x - r.xMin) / r.width * _res.x, (local.y - r.yMin) / r.height * _res.y);
    }

    /// <summary>Ink-pixel coordinates to a position relative to the ink rect's center (for particles).</summary>
    private Vector2 InkToLocal(Vector2 ink)
    {
        Rect r = ((RectTransform)transform).rect;
        return new Vector2(ink.x / _res.x * r.width + r.xMin, ink.y / _res.y * r.height + r.yMin) - r.center;
    }

    private void Highlight(Button button, bool selected)
    {
        if (button == null) return;
        if (button.targetGraphic != null) button.targetGraphic.color = selected ? buttonSelected : buttonNormal;
        foreach (Text t in button.GetComponentsInChildren<Text>()) t.color = selected ? labelSelected : labelNormal;
    }
}
