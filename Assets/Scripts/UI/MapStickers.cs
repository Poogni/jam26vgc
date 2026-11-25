using System;
using System.Collections.Generic;
using PrimeTween;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Stickers the player puts on landmarks on their map. With the Sticker tool selected, the map highlights the
/// cell of the closest landmark near the player; clicking it sticks a sticker there. Clicking a placed sticker takes
/// it back. Once the objective is found, every stickered landmark raises a far-visible beacon (see GameObjective).
/// Stickers live on their own layer above the ink, so the eraser and the objective's smudging never remove them.
/// </summary>
[RequireComponent(typeof(RawImage))]
public class MapStickers : MonoBehaviour
{
    [Tooltip("Map grid this layer sits on. Found in the parent if empty.")]
    [SerializeField] private MapGrid grid;
    [Tooltip("Map canvas whose Sticker tool drives the highlight. Found next to this layer if empty.")]
    [SerializeField] private MapCanvas canvas;
    [Tooltip("Player whose surroundings are searched for landmarks. Found automatically if empty.")]
    [SerializeField] private GridPlayerController player;
    [Tooltip("How many stickers the player has.")]
    [SerializeField, Min(0)] private int stickerCount = 2;
    [Tooltip("A landmark counts as 'near' if it's within this many cells of the player (diagonals count as 1).")]
    [SerializeField, Min(1)] private int vicinity = 2;

    [Header("Look")]
    [SerializeField] private Color fill = new Color(0.86f, 0.24f, 0.18f, 1f);   // red, like a wax seal
    [SerializeField] private Color rim = new Color(0.98f, 0.96f, 0.90f, 1f);    // white sticker border
    [SerializeField] private Color shine = new Color(1f, 0.85f, 0.55f, 1f);
    [SerializeField] private Color shadow = new Color(0.15f, 0.09f, 0.05f, 0.45f);
    [Tooltip("Outline that shows where the sticker can go.")]
    [SerializeField] private Color highlight = new Color(0.86f, 0.24f, 0.18f, 1f);
    [SerializeField, Min(0.1f)] private float highlightPulseSpeed = 4f;

    [Header("Tool Button (optional)")]
    [Tooltip("Its label shows how many stickers are left.")]
    [SerializeField] private Text countLabel;
    [SerializeField] private string labelFormat = "Sticker {0}";

    private readonly List<Vector2Int> _placed = new List<Vector2Int>();
    private RawImage _image;
    private Texture2D _texture;
    private Color32[] _pixels;
    private Tween _pop, _labelPop;

    private RawImage _highlight;
    private Texture2D _highlightTexture;
    private Vector2Int? _target;

    /// <summary>Raised whenever a sticker is placed or taken back.</summary>
    public event Action Changed;

    public IReadOnlyList<Vector2Int> Placed => _placed;
    public int Remaining => Mathf.Max(0, stickerCount - _placed.Count);
    public int Total => stickerCount;

    /// <summary>The landmark cell a sticker can go on right now (Sticker tool selected, one nearby), if any.</summary>
    public Vector2Int? Target => _target;

    private void Awake()
    {
        _image = GetComponent<RawImage>();
        _image.raycastTarget = false;
        if (grid == null) grid = GetComponentInParent<MapGrid>();
        if (canvas == null && transform.parent != null) canvas = transform.parent.GetComponentInChildren<MapCanvas>(true);
        Vector2Int res = grid != null ? grid.Resolution : new Vector2Int(221, 221);
        _texture = new Texture2D(res.x, res.y, TextureFormat.RGBA32, false)
        {
            name = "Map Stickers",
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp,
        };
        _pixels = new Color32[res.x * res.y];
        _image.texture = _texture;
        BuildHighlight();
        Redraw();
    }

    private void OnDestroy()
    {
        if (_pop.isAlive) _pop.Stop();
        if (_labelPop.isAlive) _labelPop.Stop();
        if (_texture != null) Destroy(_texture);
        if (_highlightTexture != null) Destroy(_highlightTexture);
    }

    private void Update()
    {
        if (player == null) player = FindAnyObjectByType<GridPlayerController>();
        bool active = canvas != null && canvas.CurrentTool == MapCanvas.Tool.Sticker && !GameInputLock.IsLocked;
        _target = active && Remaining > 0 ? FindTarget() : null;
        UpdateHighlight();
    }

    /// <summary>Closest unstickered landmark within the vicinity of the player.</summary>
    private Vector2Int? FindTarget()
    {
        if (player == null) return null;
        Vector2Int at = player.Cell;
        Vector2Int? best = null;
        float bestDist = float.MaxValue;
        for (int dy = -vicinity; dy <= vicinity; dy++)
        for (int dx = -vicinity; dx <= vicinity; dx++)
        {
            Vector2Int c = at + new Vector2Int(dx, dy);
            if (Landmark.At(c) == null || _placed.Contains(c)) continue;
            float d = dx * dx + dy * dy;
            if (d < bestDist) { best = c; bestDist = d; }
        }
        return best;
    }

    /// <summary>
    /// A click with the Sticker tool: takes back a sticker on that cell, or sticks one on the highlighted landmark.
    /// False (and a little shake of the button) if nothing can happen there.
    /// </summary>
    public bool Toggle(Vector2Int cell)
    {
        if (_placed.Remove(cell)) { }
        else if (_target.HasValue && cell == _target.Value && Remaining > 0) _placed.Add(cell);
        else
        {
            if (countLabel != null)
            {
                if (_labelPop.isAlive) _labelPop.Complete();
                _labelPop = Tween.PunchLocalPosition(countLabel.transform, new Vector3(6f, 0f, 0f), 0.3f, frequency: 8);
            }
            return false;
        }
        _target = FindTarget();
        Redraw();
        if (_pop.isAlive) _pop.Complete();
        _pop = Tween.PunchScale(transform, Vector3.one * 0.035f, 0.25f, frequency: 6);
        Changed?.Invoke();
        return true;
    }

    // ------------------------------------------------------------------ drawing

    private void Redraw()
    {
        if (countLabel != null) countLabel.text = string.Format(labelFormat, Remaining);
        if (_texture == null) return;
        Array.Clear(_pixels, 0, _pixels.Length);
        int w = _texture.width, h = _texture.height, cp = CellPixels;
        foreach (Vector2Int c in _placed)
        {
            // Round sticker a touch smaller than the cell: shadow, white rim, red face, a glint.
            var center = new Vector2(c.x * cp + (cp + 1) * 0.5f, c.y * cp + (cp + 1) * 0.5f);
            float r = cp * 0.48f;
            PixelArt.Disc(_pixels, w, h, center + new Vector2(0.7f, -0.7f), r, shadow);
            PixelArt.Disc(_pixels, w, h, center, r, rim);
            PixelArt.Disc(_pixels, w, h, center, r - 1.2f, fill);
            PixelArt.Set(_pixels, w, h, Mathf.FloorToInt(center.x) - 1, Mathf.FloorToInt(center.y) + 1, shine);
        }
        _texture.SetPixels32(_pixels);
        _texture.Apply(false);
    }

    private int CellPixels => grid != null ? grid.CellPixels : 10;

    /// <summary>Pulsing frame over the target cell: corner brackets and a faint wash, pixel art like the rest.</summary>
    private void BuildHighlight()
    {
        int s = CellPixels + 1;
        _highlightTexture = new Texture2D(s, s, TextureFormat.RGBA32, false)
        {
            name = "Sticker Highlight",
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp,
        };
        var px = new Color32[s * s];
        Color32 line = highlight;
        Color32 wash = new Color(highlight.r, highlight.g, highlight.b, 0.22f);
        int arm = Mathf.Max(2, s / 3);
        for (int y = 0; y < s; y++)
        for (int x = 0; x < s; x++)
        {
            bool edgeX = x == 0 || x == s - 1, edgeY = y == 0 || y == s - 1;
            bool nearCornerX = x < arm || x >= s - arm, nearCornerY = y < arm || y >= s - arm;
            bool bracket = (edgeX && nearCornerY) || (edgeY && nearCornerX);
            px[y * s + x] = bracket ? line : wash;
        }
        _highlightTexture.SetPixels32(px);
        _highlightTexture.Apply(false);

        var go = new GameObject("Sticker Highlight", typeof(RectTransform), typeof(RawImage));
        go.transform.SetParent(transform, false);
        _highlight = go.GetComponent<RawImage>();
        _highlight.texture = _highlightTexture;
        _highlight.raycastTarget = false;
        _highlight.enabled = false;
        var rt = (RectTransform)go.transform;
        rt.anchorMin = rt.anchorMax = Vector2.zero;
        rt.pivot = Vector2.zero;
    }

    private void UpdateHighlight()
    {
        if (_highlight == null) return;
        _highlight.enabled = _target.HasValue;
        if (!_target.HasValue || grid == null) return;

        var frame = (RectTransform)grid.transform;
        Vector2Int res = grid.Resolution;
        Vector2 pixel = frame.rect.size / res;
        int cp = CellPixels;
        var rt = (RectTransform)_highlight.transform;
        rt.sizeDelta = new Vector2((cp + 1) * pixel.x, (cp + 1) * pixel.y);
        rt.anchoredPosition = new Vector2(_target.Value.x * cp * pixel.x, _target.Value.y * cp * pixel.y);
        float a = 0.55f + 0.45f * (0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * highlightPulseSpeed));
        _highlight.color = new Color(1f, 1f, 1f, a);
    }
}
