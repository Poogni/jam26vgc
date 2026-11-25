using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Temporary pencil marks on the map for the walls right next to the player. They're full strength while you stand
/// beside a wall, then fade out over a few steps, so the player has to ink the walls they want to keep with the
/// (permanent) Wall tool. Lives on its own layer under the ink: the player's drawing always covers it.
/// </summary>
[RequireComponent(typeof(RawImage))]
public class MapWallHints : MonoBehaviour
{
    [Tooltip("Map grid this layer sits on. Found in the parent if empty.")]
    [SerializeField] private MapGrid grid;
    [Tooltip("Player whose surroundings are sketched. Found automatically if empty.")]
    [SerializeField] private GridPlayerController player;

    [Tooltip("Steps it takes a mark to fade out once you've walked away from that wall.")]
    [SerializeField, Min(1)] private int fadeAfterMoves = 2;
    [Tooltip("Also sketch the four diagonal neighbours, not just the walls you're touching.")]
    [SerializeField] private bool includeDiagonals;
    [Tooltip("How fast a mark eases to its new strength (per second).")]
    [SerializeField, Min(0.5f)] private float fadeSpeed = 4f;

    [Header("Look (graphite pencil)")]
    [SerializeField] private Color pencil = new Color(0.30f, 0.36f, 0.50f, 1f);
    [Tooltip("Opacity of the light wash filling the cell.")]
    [SerializeField, Range(0f, 1f)] private float wash = 0.18f;
    [Tooltip("Opacity of the hatching and outline.")]
    [SerializeField, Range(0f, 1f)] private float stroke = 0.75f;

    private static readonly Vector2Int[] Orthogonal = { Vector2Int.up, Vector2Int.right, Vector2Int.down, Vector2Int.left };
    private static readonly Vector2Int[] Diagonal =
        { new Vector2Int(1, 1), new Vector2Int(1, -1), new Vector2Int(-1, -1), new Vector2Int(-1, 1) };

    private RawImage _image;
    private Texture2D _texture;
    private Color32[] _pixels;
    private int _cols, _rows, _cp;
    private int[] _age;       // steps since the cell was last next to the player (int.MaxValue = never / gone)
    private float[] _alpha;   // shown strength, easing toward the target
    private bool _hasCell, _stepped, _dirty;
    private Vector2Int _lastCell;
    private GameObjective _objective;
    private bool _disabledForGood;

    private void Awake()
    {
        _image = GetComponent<RawImage>();
        _image.raycastTarget = false;
        if (grid == null) grid = GetComponentInParent<MapGrid>();
    }

    private void OnEnable()
    {
        if (player == null) player = FindAnyObjectByType<GridPlayerController>();
        if (player != null) player.Stepped += OnStepped;
        if (_objective == null) _objective = FindAnyObjectByType<GameObjective>();
        if (_objective != null)
        {
            _objective.StageChanged += OnStageChanged;
            if (_objective.Current != GameObjective.Stage.Searching) Shutdown();
        }
    }

    // Once the objective is found the neighbour hints stop for good (the player is on their own).
    private void OnStageChanged(GameObjective.Stage stage)
    {
        if (stage != GameObjective.Stage.Searching) Shutdown();
        else _disabledForGood = false;
    }

    private void Shutdown()
    {
        _disabledForGood = true;
        if (_texture != null)
        {
            System.Array.Clear(_pixels, 0, _pixels.Length);
            _texture.SetPixels32(_pixels);
            _texture.Apply(false);
            for (int i = 0; i < _age.Length; i++) { _age[i] = int.MaxValue; _alpha[i] = 0f; }
        }
    }

    private void OnDisable()
    {
        if (player != null) player.Stepped -= OnStepped;
        if (_objective != null) _objective.StageChanged -= OnStageChanged;
    }

    private void OnDestroy()
    {
        if (_texture != null) Destroy(_texture);
    }

    private void OnStepped(Vector3 dir, float duration) => _stepped = true;

    private void LateUpdate()
    {
        if (grid == null || player == null || _disabledForGood) return;
        if (_texture == null || _cols != grid.Columns || _rows != grid.Rows) Setup();

        Vector2Int cell = player.Cell;
        if (!_hasCell || cell != _lastCell)
        {
            // Walking ages every mark by one step; teleports / respawns just re-sketch around the new cell.
            if (_stepped)
                for (int i = 0; i < _age.Length; i++)
                    if (_age[i] != int.MaxValue) _age[i]++;
            _stepped = false;
            _hasCell = true;
            _lastCell = cell;
            Sketch(cell, Orthogonal);
            if (includeDiagonals) Sketch(cell, Diagonal);
        }

        // Ease every mark toward its strength for its age; redraw only while something changes.
        float k = 1f - Mathf.Exp(-fadeSpeed * Time.unscaledDeltaTime);
        for (int i = 0; i < _alpha.Length; i++)
        {
            float target = Target(_age[i]);
            if (Mathf.Approximately(_alpha[i], target)) continue;
            _alpha[i] = Mathf.Abs(_alpha[i] - target) < 0.01f ? target : Mathf.Lerp(_alpha[i], target, k);
            _dirty = true;
        }
        if (_dirty) Redraw();
    }

    private float Target(int age) => age == int.MaxValue ? 0f : Mathf.Clamp01(1f - age / (float)(fadeAfterMoves + 1));

    private void Sketch(Vector2Int cell, Vector2Int[] offsets)
    {
        LevelData level = player.Level != null ? player.Level : grid.Level;
        if (level == null) return;
        foreach (Vector2Int o in offsets)
        {
            Vector2Int n = cell + o;
            if (n.x < 0 || n.y < 0 || n.x >= _cols || n.y >= _rows) continue;
            // Anything that looks like a wall (fake walls included: the map shouldn't give the trick away).
            if (level.IsOpen(n)) continue;
            _age[n.y * _cols + n.x] = 0;
        }
    }

    private void Setup()
    {
        _cols = grid.Columns;
        _rows = grid.Rows;
        _cp = grid.CellPixels;
        Vector2Int res = grid.Resolution;
        if (_texture != null) Destroy(_texture);
        _texture = new Texture2D(res.x, res.y, TextureFormat.RGBA32, false)
        {
            name = "Map Wall Hints",
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp,
        };
        _pixels = new Color32[res.x * res.y];
        _age = new int[_cols * _rows];
        for (int i = 0; i < _age.Length; i++) _age[i] = int.MaxValue;
        _alpha = new float[_cols * _rows];
        _hasCell = false;
        _image.texture = _texture;
        _dirty = true;
    }

    private void Redraw()
    {
        _dirty = false;
        System.Array.Clear(_pixels, 0, _pixels.Length);
        int w = _texture.width;
        for (int cy = 0; cy < _rows; cy++)
        for (int cx = 0; cx < _cols; cx++)
        {
            float a = _alpha[cy * _cols + cx];
            if (a <= 0.001f) continue;
            int x0 = cx * _cp, y0 = cy * _cp;
            for (int y = 0; y <= _cp; y++)
            for (int x = 0; x <= _cp; x++)
            {
                bool edge = x == 0 || y == 0 || x == _cp || y == _cp;
                // Dashed pencil outline, diagonal hatching inside, a faint wash in between.
                float o = edge ? (((x + y) & 1) == 0 ? stroke : 0f)
                    : ((x + y) % 3 == 0 ? stroke * 0.8f : wash);
                if (o <= 0f) continue;
                Color c = pencil;
                c.a = o * a;
                _pixels[(y0 + y) * w + x0 + x] = c;
            }
        }
        _texture.SetPixels32(_pixels);
        _texture.Apply(false);
    }
}
