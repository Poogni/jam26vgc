using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

/// <summary>
/// Renders a camera into a small render texture and shows it in this RawImage, upscaled with
/// nearest-neighbor filtering. Pixels are always an exact whole number of screen pixels, so the
/// image stays crisp at any window size.
///
/// Optional phosphor persistence: each new frame is blended with the previous one (at the low source
/// resolution, right after the camera renders), so fast motion such as turning leaves a short CRT-style
/// ghost trail. A still image is unaffected.
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

    [Header("Phosphor Persistence")]
    [Tooltip("Ghost trails on fast motion (turning), like a CRT's phosphors fading out.")]
    [SerializeField] private bool persistence = true;
    [Tooltip("How much of the previous frame lingers (at 60 fps; corrected for other frame rates). " +
             "0.4 = subtle, 0.6 = clearly visible on turns, 0.8+ = heavy smear.")]
    [SerializeField, Range(0f, 0.95f)] private float persistenceAmount = 0.55f;
    [Tooltip("0 = everything smears evenly. 1 = only bright things leave glowing trails (real phosphor behaviour).")]
    [SerializeField, Range(0f, 1f)] private float brightTrailBias = 0.3f;
    [SerializeField] private Shader persistenceShader;

    [Header("CRT")]
    [Tooltip("CRT material for the RawImage. Picked up automatically from the RawImage if it already uses a CRT shader.")]
    [SerializeField] private Material crtMaterial;

    /// <summary>How many screen pixels one game pixel covers.</summary>
    public int PixelScale { get; private set; } = 1;
    /// <summary>Current render resolution of the 3D view.</summary>
    public Vector2Int Resolution => _rt != null ? new Vector2Int(_rt.width, _rt.height) : Vector2Int.zero;

    private static readonly int UVRectId = Shader.PropertyToID("_CRT_UVRect");
    private static readonly int HistoryId = Shader.PropertyToID("_History");
    private static readonly int PersistenceId = Shader.PropertyToID("_Persistence");
    private static readonly int BrightBiasId = Shader.PropertyToID("_BrightBias");

    private RawImage _image;
    private RenderTexture _rt;                    // what the camera renders into
    private RenderTexture _history, _historyNext; // persisted frames (ping-pong)
    private bool _historyValid;
    private Material _persistenceMaterial;
    private Vector2Int _lastScreenSize;
    private int _lastTargetHeight;
    private readonly Vector3[] _corners = new Vector3[4];

    private bool UsePersistence => persistence && persistenceAmount > 0f && PersistenceMaterial != null;

    /// <summary>The texture shown on screen: the persisted frame, or the raw camera frame.</summary>
    private Texture Shown => UsePersistence && _history != null ? _history : _rt;

    private void Reset() => persistenceShader = Shader.Find("Hidden/Retro/PhosphorPersistence");

    /// <summary>Runtime switch for the CRT filter (settings menu). Off = plain pixelated image.</summary>
    public bool CrtEnabled
    {
        get => _image != null && crtMaterial != null && _image.material == crtMaterial;
        set
        {
            if (_image == null) _image = GetComponent<RawImage>();
            CaptureCrtMaterial();
            _image.material = value ? crtMaterial : null; // null = default UI material
        }
    }

    /// <summary>Runtime switch for phosphor persistence / motion trails (settings menu).</summary>
    public bool PersistenceEnabled
    {
        get => persistence;
        set
        {
            if (persistence == value) return;
            persistence = value;
            _historyValid = false; // don't blend with a stale frame when switching back on
        }
    }

    private void CaptureCrtMaterial()
    {
        if (crtMaterial != null || _image == null) return;
        Material current = _image.material;
        if (current != null && current.shader != null && current.shader.name.StartsWith("Retro/CRT"))
            crtMaterial = current;
    }

    private void OnEnable()
    {
        _image = GetComponent<RawImage>();
        CaptureCrtMaterial();
        _lastScreenSize = Vector2Int.zero; // force a rebuild
        RenderPipelineManager.endCameraRendering += OnEndCameraRendering;
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
        RenderPipelineManager.endCameraRendering -= OnEndCameraRendering;
        Camera cam = Source;
        if (cam != null && cam.targetTexture == _rt) cam.targetTexture = null;
        if (_image != null) _image.texture = null;
        ReleaseTextures();
        if (_persistenceMaterial != null) DestroySafe(_persistenceMaterial);
        _persistenceMaterial = null;
    }

    private Camera Source => sourceCamera != null ? sourceCamera : Camera.main;

    private Material PersistenceMaterial
    {
        get
        {
            if (_persistenceMaterial != null) return _persistenceMaterial;
            if (persistenceShader == null) persistenceShader = Shader.Find("Hidden/Retro/PhosphorPersistence");
            if (persistenceShader == null || !persistenceShader.isSupported) return null;
            _persistenceMaterial = new Material(persistenceShader) { hideFlags = HideFlags.HideAndDontSave };
            return _persistenceMaterial;
        }
    }

    private void LateUpdate() => Refresh();

    private void Refresh()
    {
        if (_image == null) _image = GetComponent<RawImage>();
        Camera cam = Source;
        if (cam == null) return;

        Vector2Int size = ScreenPixelSize();
        if (size.x <= 0 || size.y <= 0) return;

        bool historyMissing = UsePersistence && _history == null;
        if (_rt == null || historyMissing || size != _lastScreenSize || targetHeight != _lastTargetHeight ||
            _rt.filterMode != filterMode)
            Rebuild(size);

        // Something else (e.g. a domain reload or a screenshot tool) may have cleared these.
        if (cam.targetTexture != _rt) cam.targetTexture = _rt;
        if (_image.texture != Shown) _image.texture = Shown;

        // The CRT shader needs the crop to find the real screen edges (for curvature and corners).
        Rect uv = _image.uvRect;
        Shader.SetGlobalVector(UVRectId, new Vector4(uv.x, uv.y, uv.width, uv.height));
    }

    /// <summary>Right after the game camera renders, fold the new frame into the persisted image.</summary>
    private void OnEndCameraRendering(ScriptableRenderContext context, Camera cam)
    {
        if (cam != Source || _rt == null || !UsePersistence || _history == null) return;

        if (!_historyValid)
        {
            // First frame: nothing to blend with yet.
            Graphics.Blit(_rt, _history);
            _historyValid = true;
            return;
        }

        // Make the decay feel the same at any frame rate (the amount is tuned for 60 fps).
        float dt = Application.isPlaying ? Mathf.Clamp(Time.unscaledDeltaTime, 1f / 240f, 0.1f) : 1f / 60f;
        float amount = Mathf.Pow(persistenceAmount, dt * 60f);

        Material mat = PersistenceMaterial;
        mat.SetTexture(HistoryId, _history);
        mat.SetFloat(PersistenceId, amount);
        mat.SetFloat(BrightBiasId, brightTrailBias);
        Graphics.Blit(_rt, _historyNext, mat);

        (_history, _historyNext) = (_historyNext, _history);
        _image.texture = _history;
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

        ReleaseTextures();
        _rt = CreateTexture("RetroViewport RT", w, h, 24);
        if (UsePersistence)
        {
            _history = CreateTexture("RetroViewport History A", w, h, 0);
            _historyNext = CreateTexture("RetroViewport History B", w, h, 0);
            _historyValid = false;
        }

        // The texture is rounded up, so crop the extra fraction of a pixel to keep pixels square.
        _image.texture = Shown;
        _image.uvRect = new Rect(0f, 0f, screenSize.x / (float)(w * PixelScale), screenSize.y / (float)(h * PixelScale));

        Camera cam = Source;
        if (cam != null) cam.targetTexture = _rt;
    }

    private RenderTexture CreateTexture(string textureName, int w, int h, int depth)
    {
        var tex = new RenderTexture(w, h, depth, RenderTextureFormat.Default)
        {
            name = textureName,
            filterMode = filterMode,
            antiAliasing = 1,
            useMipMap = false,
            hideFlags = HideFlags.DontSave,
        };
        tex.Create();
        return tex;
    }

    private void ReleaseTextures()
    {
        Camera cam = Source;
        if (_rt != null && cam != null && cam.targetTexture == _rt) cam.targetTexture = null;
        Release(ref _rt);
        Release(ref _history);
        Release(ref _historyNext);
        _historyValid = false;
    }

    private static void Release(ref RenderTexture tex)
    {
        if (tex == null) return;
        tex.Release();
        DestroySafe(tex);
        tex = null;
    }

    private static void DestroySafe(Object o)
    {
        if (Application.isPlaying) Destroy(o);
        else DestroyImmediate(o);
    }
}
