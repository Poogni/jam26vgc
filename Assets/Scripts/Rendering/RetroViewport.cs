using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Renders a camera into a small render texture and shows it in this RawImage, upscaled with
/// nearest-neighbor filtering. Pixels are always an exact whole number of screen pixels, so the
/// image stays crisp at any window size.
///
/// The 3D view is a UI element, so the screen layout (3D view + side panel) is plain uGUI and the
/// game camera never draws directly to the screen.
/// </summary>
[ExecuteAlways]
[RequireComponent(typeof(RawImage))]
public class RetroViewport : MonoBehaviour
{
    [Tooltip("Camera to render. Uses Camera.main if empty.")]
    [SerializeField] private Camera sourceCamera;

    [Tooltip("Roughly how many pixels tall the 3D view should be. The actual value is snapped so every " +
             "game pixel is a whole number of screen pixels.")]
    [SerializeField, Range(90, 720)] private int targetHeight = 240;

    [SerializeField] private FilterMode filterMode = FilterMode.Point;

    /// <summary>How many screen pixels one game pixel covers.</summary>
    public int PixelScale { get; private set; } = 1;
    /// <summary>Current render resolution of the 3D view.</summary>
    public Vector2Int Resolution => _rt != null ? new Vector2Int(_rt.width, _rt.height) : Vector2Int.zero;

    private RawImage _image;
    private RenderTexture _rt;
    private Vector2Int _lastScreenSize;
    private int _lastTargetHeight;
    private readonly Vector3[] _corners = new Vector3[4];

    private void OnEnable()
    {
        _image = GetComponent<RawImage>();
        _lastScreenSize = Vector2Int.zero; // force a rebuild
        Refresh();
    }

    // Window / layout resized: rebuild immediately instead of waiting for the next LateUpdate
    // (which, in Edit mode, only runs when the scene changes).
    private void OnRectTransformDimensionsChange()
    {
        if (isActiveAndEnabled) Refresh();
    }

    // Can't safely create textures inside OnValidate; just flag for a rebuild.
    private void OnValidate() => _lastTargetHeight = -1;

    private void OnDisable()
    {
        Camera cam = Source;
        if (cam != null && cam.targetTexture == _rt) cam.targetTexture = null;
        if (_image != null) _image.texture = null;
        ReleaseTexture();
    }

    private Camera Source => sourceCamera != null ? sourceCamera : Camera.main;

    private void LateUpdate() => Refresh();

    private void Refresh()
    {
        if (_image == null) _image = GetComponent<RawImage>();
        Camera cam = Source;
        if (cam == null) return;

        Vector2Int size = ScreenPixelSize();
        if (size.x <= 0 || size.y <= 0) return;

        if (_rt == null || size != _lastScreenSize || targetHeight != _lastTargetHeight || _rt.filterMode != filterMode)
            Rebuild(size);

        // Something else (e.g. a domain reload) may have cleared these.
        if (cam.targetTexture != _rt) cam.targetTexture = _rt;
        if (_image.texture != _rt) _image.texture = _rt;
    }

    /// <summary>Size of this RawImage on screen, in real pixels.</summary>
    private Vector2Int ScreenPixelSize()
    {
        var rt = (RectTransform)transform;
        rt.GetWorldCorners(_corners);
        Canvas canvas = _image.canvas;
        Camera uiCam = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
        Vector2 min = RectTransformUtility.WorldToScreenPoint(uiCam, _corners[0]);
        Vector2 max = RectTransformUtility.WorldToScreenPoint(uiCam, _corners[2]);
        return new Vector2Int(Mathf.RoundToInt(max.x - min.x), Mathf.RoundToInt(max.y - min.y));
    }

    private void Rebuild(Vector2Int screenSize)
    {
        _lastScreenSize = screenSize;
        _lastTargetHeight = targetHeight;

        // Whole-number scale closest to the requested height.
        PixelScale = Mathf.Max(1, Mathf.RoundToInt((float)screenSize.y / targetHeight));
        int w = Mathf.CeilToInt((float)screenSize.x / PixelScale);
        int h = Mathf.CeilToInt((float)screenSize.y / PixelScale);

        ReleaseTexture();
        _rt = new RenderTexture(w, h, 24, RenderTextureFormat.Default)
        {
            name = "RetroViewport RT",
            filterMode = filterMode,
            antiAliasing = 1,
            useMipMap = false,
            hideFlags = HideFlags.DontSave,
        };
        _rt.Create();

        // The texture is rounded up, so crop the extra fraction of a pixel to keep pixels square.
        _image.texture = _rt;
        _image.uvRect = new Rect(0f, 0f, screenSize.x / (float)(w * PixelScale), screenSize.y / (float)(h * PixelScale));

        Camera cam = Source;
        if (cam != null) cam.targetTexture = _rt;
    }

    private void ReleaseTexture()
    {
        if (_rt == null) return;
        Camera cam = Source;
        if (cam != null && cam.targetTexture == _rt) cam.targetTexture = null;
        _rt.Release();
        if (Application.isPlaying) Destroy(_rt);
        else DestroyImmediate(_rt);
        _rt = null;
    }
}
