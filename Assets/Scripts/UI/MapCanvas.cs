using PrimeTween;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// Freehand pixel drawing on the cartography map. The pen is a neon highlighter with a chisel tip that blends over
/// the paper and the walls, with a weighted brush: it trails the cursor with some inertia (smooths shaky strokes)
/// and draws at a constant size.
/// Sits on a transparent "ink" RawImage over the generated grid, so the eraser only removes ink.
/// Left mouse draws with the current tool, right mouse always erases. Drawing throws off pixel particles.
/// The Wall tool fills a whole grid cell with solid ink per click (drag to paint a run of cells); right-click
/// clears a whole cell. Built by Tools > Dungeon > Create Map Drawing.
/// </summary>
[RequireComponent(typeof(RawImage))]
public class MapCanvas : MonoBehaviour
{
    public enum Tool { Pen, Eraser, Wall }

    [Header("Canvas")]
    [Tooltip("Ink resolution comes from this grid so lines up with its cells. Found in the parent if empty.")]
    [SerializeField] private MapGrid grid;
    [Tooltip("Used only when there's no MapGrid.")]
    [SerializeField] private Vector2Int fallbackResolution = new Vector2Int(221, 221);

    [Header("Weighted Brush")]
    [Tooltip("How heavy the brush feels. 0 = sticks to the cursor, 0.9 = drags far behind (very smooth lines).")]
    [SerializeField, Range(0f, 0.95f)] private float weight = 0.55f;

    [Header("Pen (highlighter)")]
    [Tooltip("Electric cyan: nothing else on the map is this colour, so it pops off both the parchment and the dark walls.")]
    [SerializeField] private Color highlighterColor = new Color(0.0f, 0.95f, 1.0f);
    [Tooltip("1 = opaque. Lower lets the paper and walls show through like a real highlighter.")]
    [SerializeField, Range(0.3f, 1f)] private float highlighterAlpha = 0.78f;
    [Tooltip("Half-width of the chisel tip in ink pixels (the tip is twice as wide as it is tall).")]
    [SerializeField, Range(1, 8)] private int highlighterRadius = 3;

    [Header("Eraser")]
    [SerializeField, Range(0, 12)] private int eraserRadius = 3;

    [Header("Wall Paint (hand-inked look)")]
    [Tooltip("Base ink wash of a wall cell.")]
    [SerializeField] private Color wallWash = new Color(0.34f, 0.24f, 0.15f);
    [Tooltip("Darker ink: the pencil hatching and the outline along exposed edges.")]
    [SerializeField] private Color wallHatch = new Color(0.14f, 0.09f, 0.05f);
    [Tooltip("Per-pixel ink grain.")]
    [SerializeField, Range(0f, 0.3f)] private float wallGrain = 0.12f;
    [Tooltip("How ragged the outline is where a wall meets open floor (chance of a missing edge pixel).")]
    [SerializeField, Range(0f, 0.6f)] private float wallRoughEdge = 0.3f;
    [Tooltip("Share of pixels where the paper shows through the ink.")]
    [SerializeField, Range(0f, 0.3f)] private float wallSpeckle = 0.08f;

    [Header("Wall Juice")]
    [SerializeField] private bool wallJuice = true;
    [Tooltip("Particles thrown when a cell is filled or cleared.")]
    [SerializeField, Range(0, 24)] private int wallBurst = 8;
    [Tooltip("A dark stamp lands on the cell: starts this much bigger, settles to 1 with a bounce.")]
    [SerializeField, Range(1f, 2.5f)] private float stampOvershoot = 1.55f;
    [SerializeField, Min(0.05f)] private float stampDuration = 0.28f;
    [Tooltip("Tiny thump of the whole map when a wall lands. 0 = off.")]
    [SerializeField, Range(0f, 0.04f)] private float mapPunch = 0.012f;

    [Header("Particles")]
    [SerializeField] private MapParticles particles;
    [Tooltip("Particles per ink pixel travelled.")]
    [SerializeField, Range(0f, 2f)] private float particlesPerPixel = 0.35f;
    [SerializeField] private Color eraserCrumbColor = new Color(0.86f, 0.58f, 0.56f, 0.95f); // pink rubber

    [Header("Tool Buttons (optional)")]
    [SerializeField] private Button penButton;
    [SerializeField] private Button eraserButton;
    [SerializeField] private Button wallButton;
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

    // Wall paint state: the last cell touched this stroke, so a drag fills each cell once.
    private bool _wallStroke;
    private Vector2Int _lastWallCell;
    private int[] _strokeMark;            // stroke id per pixel: a highlighter stroke blends each pixel only once
    private int _strokeId;
    private bool[] _wall;                // which cells are walls (drives the ragged edges)
    private int _cols, _rows;

    // Juice.
    private const int StampPool = 16;
    private Image[] _stamps;
    private Tween[] _stampTweens;
    private int _nextStamp;
    private Tween _punch;
    private bool _toolsReady;
    private System.Action<Image, float> _paintStep, _eraseStep;

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
        _strokeMark = new int[_res.x * _res.y];
        int cp = CellPixels;
        _cols = grid != null ? grid.Columns : (_res.x - 1) / cp;
        _rows = grid != null ? grid.Rows : (_res.y - 1) / cp;
        _wall = new bool[_cols * _rows];
        ClearInk();
        _image.texture = _texture;
        BuildStamps();

        EnsureWallButton();

        if (penButton != null) penButton.onClick.AddListener(() => SetTool(Tool.Pen));
        if (eraserButton != null) eraserButton.onClick.AddListener(() => SetTool(Tool.Eraser));
        if (wallButton != null) wallButton.onClick.AddListener(() => SetTool(Tool.Wall));
        SetTool(Tool.Pen);
        _toolsReady = true; // button pulses only for the player's own clicks
    }

    /// <summary>
    /// The Wall button is a normal part of the tool bar: if the scene doesn't have it (e.g. it was never saved, or an
    /// undo removed it), it is cloned from the Eraser button one slot to the right, so the tool is always available.
    /// </summary>
    private void EnsureWallButton()
    {
        if (wallButton != null || eraserButton == null) return;

        var source = (RectTransform)eraserButton.transform;
        Button clone = Instantiate(eraserButton, source.parent);
        clone.name = "Wall Button";
        clone.onClick.RemoveAllListeners();
        var rt = (RectTransform)clone.transform;
        rt.anchoredPosition = source.anchoredPosition + new Vector2(source.sizeDelta.x + 14f, 0f);
        foreach (Text t in clone.GetComponentsInChildren<Text>()) t.text = "Wall";
        wallButton = clone;
    }

    private void Start()
    {
        // Particles snap to the same pixel size as the ink.
        if (particles != null) particles.PixelSize = ((RectTransform)transform).rect.width / _res.x;
    }

    private void OnDestroy()
    {
        if (_punch.isAlive) _punch.Stop();
        if (_stampTweens != null)
            foreach (Tween t in _stampTweens) if (t.isAlive) t.Stop();
        if (_texture != null) Destroy(_texture);
    }

    public void SetTool(Tool tool)
    {
        bool changed = tool != CurrentTool;
        CurrentTool = tool;
        if (changed) PulseButton(tool == Tool.Pen ? penButton : tool == Tool.Eraser ? eraserButton : wallButton);
        Highlight(penButton, tool == Tool.Pen);
        Highlight(eraserButton, tool == Tool.Eraser);
        Highlight(wallButton, tool == Tool.Wall);
    }

    /// <summary>Wipe all ink (the grid underneath is untouched).</summary>
    public void ClearInk()
    {
        for (int i = 0; i < _pixels.Length; i++) _pixels[i] = Clear;
        if (_wall != null) System.Array.Clear(_wall, 0, _wall.Length);
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
            bool rightClick = mouse.rightButton.isPressed;
            if (CurrentTool == Tool.Wall) BeginWallStroke(cursor, rightClick);
            else BeginStroke(cursor, rightClick || CurrentTool == Tool.Eraser);
            return;
        }

        if (_wallStroke)
        {
            // Wall paint: every new cell the cursor enters gets filled (or cleared with right mouse).
            if (!held) { _stroking = _wallStroke = false; return; }
            Vector2 at = ClampToCanvas(cursor);
            Vector2Int cell = CellAt(at);
            if (cell != _lastWallCell) PaintWallCell(cell, at, _strokeErases);
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
        _strokeId++;
        _brush = _target = at;
        _particleDebt = 0f;
        Stamp(Round(at), erase ? eraserRadius : highlighterRadius);
        Burst(at, 4);
    }

    // ------------------------------------------------------------------ wall paint

    private void BeginWallStroke(Vector2 at, bool erase)
    {
        _stroking = _wallStroke = _wallFirst = true;
        _strokeErases = erase;
        PaintWallCell(CellAt(at), at, erase);
    }

    private void PaintWallCell(Vector2Int cell, Vector2 inkPos, bool erase)
    {
        bool firstOfStroke = _wallFirst;
        _wallFirst = false;
        _lastWallCell = cell;

        _wall[cell.y * _cols + cell.x] = !erase;
        if (erase) ClearCellPixels(cell);
        else RenderWallCell(cell);

        // Neighbours' exposed edges just changed (a wall next to a wall has no outline between them), so redraw them.
        for (int i = 0; i < 4; i++)
        {
            Vector2Int n = cell + Neighbour[i];
            if (IsWall(n.x, n.y)) RenderWallCell(n);
        }
        _dirty = true;

        if (wallJuice) WallJuice(cell, erase, firstOfStroke);
    }

    private static readonly Vector2Int[] Neighbour =
        { Vector2Int.left, Vector2Int.right, Vector2Int.down, Vector2Int.up };

    private bool _wallFirst; // the next painted cell is the first of a stroke (a click gets the bigger thump)

    private bool IsWall(int cx, int cy) => cx >= 0 && cy >= 0 && cx < _cols && cy < _rows && _wall[cy * _cols + cx];

    /// <summary>Wipes a cell's pixels, including its border lines (neighbouring walls redraw their own edges).</summary>
    private void ClearCellPixels(Vector2Int cell)
    {
        int cp = CellPixels;
        for (int y = cell.y * cp; y <= (cell.y + 1) * cp; y++)
        for (int x = cell.x * cp; x <= (cell.x + 1) * cp; x++)
        {
            if (x < 0 || y < 0 || x >= _res.x || y >= _res.y) continue;
            _pixels[y * _res.x + x] = Clear;
        }
    }

    /// <summary>
    /// Draws one wall cell like hand-inked cartography: an ink wash with grain, pencil hatching (direction and spacing
    /// vary per cell), paper specks, and a darker, ragged outline wherever the wall meets open floor. Everything is
    /// derived from the cell/pixel coordinates, so a cell always looks the same however often it is redrawn.
    /// </summary>
    private void RenderWallCell(Vector2Int cell)
    {
        int cp = CellPixels;
        uint seed = Hash(cell.x, cell.y, 17u);
        bool slashHatch = (seed & 1u) == 0u;
        int spacing = ((seed >> 1) & 1u) == 0u ? 3 : 4;
        int phase = (int)((seed >> 2) % (uint)spacing);

        bool openL = !IsWall(cell.x - 1, cell.y), openR = !IsWall(cell.x + 1, cell.y);
        bool openD = !IsWall(cell.x, cell.y - 1), openU = !IsWall(cell.x, cell.y + 1);

        for (int ly = 0; ly <= cp; ly++)
        for (int lx = 0; lx <= cp; lx++)
        {
            int x = cell.x * cp + lx, y = cell.y * cp + ly;
            if (x >= _res.x || y >= _res.y) continue;

            // Distance (in pixels) to the nearest edge that borders open floor; large if the cell is fully enclosed.
            int d = 99;
            if (openL) d = Mathf.Min(d, lx);
            if (openR) d = Mathf.Min(d, cp - lx);
            if (openD) d = Mathf.Min(d, ly);
            if (openU) d = Mathf.Min(d, cp - ly);

            uint n = Hash(x, y, 91u);
            float r = (n & 0xFFFFu) / 65535f;                 // edge / speck randomness
            float g = ((n >> 16) & 0xFFu) / 255f;             // grain

            // Ragged ink edge: pixels along an exposed edge go missing, and fewer just inside it.
            if ((d == 0 && r < wallRoughEdge) || (d == 1 && r < wallRoughEdge * 0.3f))
            {
                _pixels[y * _res.x + x] = Clear;
                continue;
            }

            float grain = 1f + (g - 0.5f) * 2f * wallGrain;
            Color c = new Color(wallWash.r * grain, wallWash.g * grain, wallWash.b * grain, 1f);

            int h = slashHatch ? lx + ly : lx - ly + cp;      // diagonal pencil strokes
            if ((h + phase) % spacing == 0) c = Color.Lerp(c, wallHatch, 0.85f);
            if (d == 0) c = Color.Lerp(c, wallHatch, 0.7f);   // ink pools along the outline

            // Paper showing through the ink.
            if (r > 1f - wallSpeckle) c.a = 0.35f + g * 0.3f;

            _pixels[y * _res.x + x] = c;
        }
    }

    private static uint Hash(int x, int y, uint salt)
    {
        unchecked
        {
            uint h = (uint)x * 374761393u + (uint)y * 668265263u + salt * 2246822519u;
            h = (h ^ (h >> 13)) * 1274126177u;
            return h ^ (h >> 16);
        }
    }

    // ------------------------------------------------------------------ wall juice

    private void WallJuice(Vector2Int cell, bool erase, bool firstOfStroke)
    {
        SpawnStamp(cell, erase);
        PunchMap(firstOfStroke ? mapPunch : mapPunch * 0.45f);
        WallDebris(cell, erase);
    }

    /// <summary>A pooled overlay that stamps down on the cell (paint) or crumbles away (erase).</summary>
    private void BuildStamps()
    {
        _paintStep = StampPaintStep;
        _eraseStep = StampEraseStep;
        _stamps = new Image[StampPool];
        _stampTweens = new Tween[StampPool];
        for (int i = 0; i < StampPool; i++)
        {
            var go = new GameObject("Wall Stamp", typeof(RectTransform), typeof(Image));
            go.hideFlags = HideFlags.DontSave;
            go.layer = gameObject.layer;
            var rt = (RectTransform)go.transform;
            rt.SetParent(transform, false);
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
            var img = go.GetComponent<Image>();
            img.raycastTarget = false;
            go.SetActive(false);
            _stamps[i] = img;
        }
    }

    private void SpawnStamp(Vector2Int cell, bool erase)
    {
        int i = _nextStamp;
        _nextStamp = (_nextStamp + 1) % StampPool;
        if (_stampTweens[i].isAlive) _stampTweens[i].Stop();

        int cp = CellPixels;
        float px = ((RectTransform)transform).rect.width / _res.x;
        Image img = _stamps[i];
        RectTransform rt = img.rectTransform;
        rt.anchoredPosition = InkToLocal(new Vector2((cell.x + 0.5f) * cp, (cell.y + 0.5f) * cp));
        rt.sizeDelta = Vector2.one * ((cp + 1) * px);
        float start = erase ? 1f : stampOvershoot;
        rt.localScale = new Vector3(start, start, 1f);
        Color c = erase ? eraserCrumbColor : wallHatch;
        c.a = erase ? 0.7f : 0.6f;
        img.color = c;
        img.gameObject.SetActive(true);

        // OutBack on the stamp makes it land a touch small and spring back: a squash-and-settle.
        _stampTweens[i] = Tween.Custom(img, 0f, 1f, stampDuration, erase ? _eraseStep : _paintStep,
                erase ? Ease.OutQuad : Ease.OutBack, useUnscaledTime: true)
            .OnComplete(img, im => im.gameObject.SetActive(false));
    }

    private void StampPaintStep(Image im, float t)
    {
        float s = Mathf.LerpUnclamped(stampOvershoot, 1f, t);
        im.rectTransform.localScale = new Vector3(s, s, 1f);
        Color c = im.color;
        c.a = 0.6f * Mathf.Clamp01(1f - t * 1.15f);
        im.color = c;
    }

    private void StampEraseStep(Image im, float t)
    {
        float s = Mathf.Lerp(1f, 0.45f, t);
        im.rectTransform.localScale = new Vector3(s, s, 1f);
        Color c = im.color;
        c.a = 0.7f * Mathf.Clamp01(1f - t);
        im.color = c;
    }

    /// <summary>The whole map gives a little thump when a wall lands.</summary>
    private void PunchMap(float strength)
    {
        if (strength <= 0f || grid == null) return;
        Transform t = grid.transform;
        if (_punch.isAlive) { _punch.Stop(); t.localScale = Vector3.one; }
        _punch = Tween.PunchScale(t, new Vector3(strength, strength, 0f), 0.16f, frequency: 12f, useUnscaledTime: true);
    }

    /// <summary>Chips of ink and paper dust flying out of the cell (crumbs on erase).</summary>
    private void WallDebris(Vector2Int cell, bool erase)
    {
        if (particles == null || wallBurst <= 0) return;
        int cp = CellPixels;
        float px = particles.PixelSize;
        for (int i = 0; i < wallBurst; i++)
        {
            Vector2 inCell = new Vector2((cell.x + Random.value) * cp, (cell.y + Random.value) * cp);
            Vector2 center = new Vector2((cell.x + 0.5f) * cp, (cell.y + 0.5f) * cp);
            Vector2 dir = (inCell - center).sqrMagnitude < 0.01f ? Random.insideUnitCircle.normalized : (inCell - center).normalized;
            Vector2 vel = dir * Random.Range(12f, 30f) * px;
            Color c;
            if (erase) c = Random.value < 0.5f ? eraserCrumbColor : new Color(0.93f, 0.86f, 0.68f, 0.95f);
            else c = Random.value < 0.55f ? Color.Lerp(wallWash, wallHatch, Random.value)
                                          : new Color(0.93f, 0.86f, 0.68f, 0.9f); // pale paper dust
            particles.Emit(InkToLocal(inCell), vel, c, Random.Range(0.25f, 0.55f), 55f * px, Random.value < 0.3f ? 2 : 1);
        }
    }

    private void PulseButton(Button b)
    {
        if (!_toolsReady || b == null) return;
        Tween.StopAll(b.transform);
        b.transform.localScale = Vector3.one;
        Tween.PunchScale(b.transform, Vector3.one * 0.16f, 0.3f, frequency: 8f, useUnscaledTime: true);
    }

    private int CellPixels => grid != null ? grid.CellPixels : 10;

    /// <summary>The grid cell under an ink-pixel position (the last pixel line belongs to the last cell).</summary>
    private Vector2Int CellAt(Vector2 ink)
    {
        int cp = CellPixels;
        int cols = grid != null ? grid.Columns : (_res.x - 1) / cp;
        int rows = grid != null ? grid.Rows : (_res.y - 1) / cp;
        return new Vector2Int(Mathf.Clamp(Mathf.FloorToInt(ink.x / cp), 0, cols - 1),
                              Mathf.Clamp(Mathf.FloorToInt(ink.y / cp), 0, rows - 1));
    }

    private void MoveBrushTo(Vector2 next, float dt)
    {
        float dist = Vector2.Distance(_brush, next);
        if (dist < 0.01f) return;

        int radius = _strokeErases ? eraserRadius : highlighterRadius;

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
        if (_strokeErases)
        {
            int r2 = r * r + r; // slightly fuller circles at small radii
            for (int y = -r; y <= r; y++)
            for (int x = -r; x <= r; x++)
            {
                if (x * x + y * y > r2) continue;
                int px = c.x + x, py = c.y + y;
                if (px < 0 || py < 0 || px >= _res.x || py >= _res.y) continue;
                _pixels[py * _res.x + px] = Clear;
            }
        }
        else
        {
            // Chisel tip: twice as wide as tall. Blended over what's there (once per pixel per stroke), so the
            // highlighter lets the paper and the walls show through instead of hiding them.
            int hy = Mathf.Max(1, r / 2);
            for (int y = -hy; y <= hy; y++)
            for (int x = -r; x <= r; x++)
            {
                int px = c.x + x, py = c.y + y;
                if (px < 0 || py < 0 || px >= _res.x || py >= _res.y) continue;
                int i = py * _res.x + px;
                if (_strokeMark[i] == _strokeId) continue;
                _strokeMark[i] = _strokeId;
                _pixels[i] = Over(_pixels[i], highlighterColor, highlighterAlpha);
            }
        }
        _dirty = true;
    }

    /// <summary>Straight-alpha "source over" of a colour onto an existing ink pixel.</summary>
    private static Color32 Over(Color32 dst, Color src, float a)
    {
        float da = dst.a / 255f;
        float outA = a + da * (1f - a);
        float k = da * (1f - a);
        return new Color32(
            (byte)Mathf.RoundToInt((src.r * a + dst.r / 255f * k) / outA * 255f),
            (byte)Mathf.RoundToInt((src.g * a + dst.g / 255f * k) / outA * 255f),
            (byte)Mathf.RoundToInt((src.b * a + dst.b / 255f * k) / outA * 255f),
            (byte)Mathf.RoundToInt(outA * 255f));
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
            // Neon sparkle: flicked off the tip, slight drift, quick fade; some motes glow white-hot.
            Vector2 vel = (Random.insideUnitCircle * 10f - strokeDir * 4f) * px;
            Color c = Random.value < 0.3f ? Color.Lerp(highlighterColor, Color.white, 0.7f) : highlighterColor;
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
