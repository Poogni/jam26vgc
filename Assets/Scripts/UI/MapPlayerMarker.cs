using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Live "you are here" marker on the cartography map: a small pixel arrow on the player's cell, pointing the way
/// they face. Glides between cells as they walk (snapped to the map's pixels), pulses gently, and sits above the
/// ink so drawing never hides it. Shown or hidden by the "Player Marker" setting.
/// </summary>
[RequireComponent(typeof(RawImage))]
public class MapPlayerMarker : MonoBehaviour
{
    [Tooltip("Map grid this marker sits on. Found in the parent if empty.")]
    [SerializeField] private MapGrid grid;
    [Tooltip("Player to follow. Found automatically if empty.")]
    [SerializeField] private GridPlayerController player;

    [SerializeField] private Color markerColor = new Color(0.12f, 0.30f, 0.62f, 1f);
    [SerializeField] private Color outlineColor = new Color(0.93f, 0.88f, 0.76f, 1f);
    [Tooltip("How quickly the marker glides to the player's cell (higher = snappier).")]
    [SerializeField, Min(1f)] private float followSpeed = 14f;
    [Tooltip("Gentle opacity pulse so the marker reads as live. 0 = steady.")]
    [SerializeField, Range(0f, 0.6f)] private float pulse = 0.25f;
    [SerializeField, Min(0.1f)] private float pulseSpeed = 3f;

    private RawImage _image;
    private Texture2D _texture;
    private Facing _drawnFacing = (Facing)(-1);
    private Vector2 _pos;          // map pixels (float), lags behind the target for the glide
    private bool _hasPos;
    private Vector2Int _lastCell;

    private void Awake()
    {
        _image = GetComponent<RawImage>();
        _image.raycastTarget = false;
        if (grid == null) grid = GetComponentInParent<MapGrid>();
    }

    private void OnEnable()
    {
        GameSettings.Changed += ApplySetting;
        ApplySetting();
    }

    private void OnDisable() => GameSettings.Changed -= ApplySetting;

    private void OnDestroy()
    {
        if (_texture != null) Destroy(_texture);
    }

    private void ApplySetting() => _image.enabled = GameSettings.PlayerMarker;

    private void LateUpdate()
    {
        if (!_image.enabled || grid == null) return;
        if (player == null) player = FindAnyObjectByType<GridPlayerController>();
        if (player == null) return;

        int cp = grid.CellPixels;
        Vector2Int res = grid.Resolution;
        Vector2Int cell = player.Cell;
        // Center of the cell's (cp+1)-pixel span including both grid lines, so the marker covers the cell exactly.
        Vector2 target = new Vector2(cell.x * cp + (cp + 1) * 0.5f, cell.y * cp + (cp + 1) * 0.5f);

        // Glide between neighbouring cells; jump straight there on teleports.
        bool jump = !_hasPos || (cell - _lastCell).sqrMagnitude > 1;
        _pos = jump ? target : Vector2.Lerp(_pos, target, 1f - Mathf.Exp(-followSpeed * Time.unscaledDeltaTime));
        _hasPos = true;
        _lastCell = cell;

        if (player.Facing != _drawnFacing) Draw(player.Facing, cp);

        // Place on the map, snapped to whole map pixels so it stays pixel art while it moves.
        var frame = (RectTransform)grid.transform;
        Vector2 pixel = frame.rect.size / res;
        Vector2 snapped = new Vector2(Mathf.Round(_pos.x - 0.5f) + 0.5f, Mathf.Round(_pos.y - 0.5f) + 0.5f);
        var rt = (RectTransform)transform;
        rt.anchorMin = rt.anchorMax = Vector2.zero;
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(_texture.width * pixel.x, _texture.height * pixel.y);
        rt.anchoredPosition = snapped * pixel;

        Color c = Color.white;
        c.a = 1f - pulse * (0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * pulseSpeed * Mathf.PI));
        _image.color = c;
    }

    /// <summary>Arrow pointing the player's facing, with a light outline so it reads on ink and paper.</summary>
    private void Draw(Facing facing, int cellPixels)
    {
        _drawnFacing = facing;
        int s = cellPixels + 1; // a touch bigger than a cell's interior, so it covers the lines around it
        if (_texture == null || _texture.width != s)
        {
            if (_texture != null) Destroy(_texture);
            _texture = new Texture2D(s, s, TextureFormat.RGBA32, false)
            {
                name = "Map Player Marker",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
            };
        }

        var px = new Color32[s * s];
        Vector2 c = new Vector2(s * 0.5f, s * 0.5f);
        float half = s * 0.5f - 1f;
        Vector2 dir = facing switch
        {
            Facing.North => Vector2.up,
            Facing.East => Vector2.right,
            Facing.South => Vector2.down,
            _ => Vector2.left,
        };
        Vector2 side = new Vector2(-dir.y, dir.x);

        // Outline: the arrow drawn slightly bigger in the light colour, then the arrow on top.
        Arrow(px, s, c, dir, side, half + 1f, outlineColor);
        Arrow(px, s, c, dir, side, half, markerColor);

        _texture.SetPixels32(px);
        _texture.Apply(false);
        _image.texture = _texture;
    }

    private static void Arrow(Color32[] px, int s, Vector2 c, Vector2 dir, Vector2 side, float half, Color32 color)
    {
        Vector2 tip = c + dir * half;
        Vector2 back = c - dir * half * 0.75f;
        PixelArt.Triangle(px, s, s, tip, back + side * half * 0.9f, back - side * half * 0.9f, color);
    }
}
