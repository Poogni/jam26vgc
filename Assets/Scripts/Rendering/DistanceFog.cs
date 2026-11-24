using UnityEngine;

/// <summary>
/// Retro distance fog: drives the scene's fog settings from one place and, optionally, clears the camera to
/// the fog color so the sky and far buildings melt into the same haze (fog doesn't affect the skybox).
/// Put it on the game camera.
/// </summary>
[ExecuteAlways]
[RequireComponent(typeof(Camera))]
public class DistanceFog : MonoBehaviour
{
    [SerializeField] private Color fogColor = new Color(0.86f, 0.76f, 0.62f); // dusty warm haze

    [Tooltip("Linear: hard start/end, the classic retro look. Exponential: softer, more natural falloff.")]
    [SerializeField] private FogMode mode = FogMode.Linear;

    [Tooltip("Linear: distance (m) where fog begins.")]
    [SerializeField, Min(0f)] private float startDistance = 5f;
    [Tooltip("Linear: distance (m) where everything is fully fogged.")]
    [SerializeField, Min(0.1f)] private float endDistance = 25f;
    [Tooltip("Exponential modes only.")]
    [SerializeField, Range(0f, 0.3f)] private float density = 0.06f;

    [Tooltip("Replace the skybox with a flat fog-colored background so the sky matches the haze.")]
    [SerializeField] private bool skyMatchesFog = true;
    [Tooltip("Pull the camera's far clip in to just past the fog end; nothing past it is visible anyway.")]
    [SerializeField] private bool clipAtFogEnd = true;

    private Camera _camera;

    // The camera's own settings before fog took over, restored when this component is disabled/removed.
    // Serialized so a script reload doesn't capture the already-fogged values as the "originals".
    [SerializeField, HideInInspector] private CameraClearFlags _originalClear;
    [SerializeField, HideInInspector] private Color _originalBackground;
    [SerializeField, HideInInspector] private float _originalFar;
    [SerializeField, HideInInspector] private bool _saved;

    public Color FogColor
    {
        get => fogColor;
        set { fogColor = value; Apply(); }
    }

    private void OnEnable()
    {
        _camera = GetComponent<Camera>();
        if (!_saved)
        {
            _originalClear = _camera.clearFlags;
            _originalBackground = _camera.backgroundColor;
            _originalFar = _camera.farClipPlane;
            _saved = true;
        }
        Apply();
    }

    private void OnDisable()
    {
        RenderSettings.fog = false;
        if (_camera != null && _saved)
        {
            _camera.clearFlags = _originalClear;
            _camera.backgroundColor = _originalBackground;
            _camera.farClipPlane = _originalFar;
        }
    }

    private void OnValidate()
    {
        endDistance = Mathf.Max(endDistance, startDistance + 0.1f);
        if (isActiveAndEnabled) Apply();
    }

    private void Apply()
    {
        RenderSettings.fog = true;
        RenderSettings.fogMode = mode;
        RenderSettings.fogColor = fogColor;
        RenderSettings.fogStartDistance = startDistance;
        RenderSettings.fogEndDistance = endDistance;
        RenderSettings.fogDensity = density;

        if (_camera == null) _camera = GetComponent<Camera>();
        if (skyMatchesFog)
        {
            _camera.clearFlags = CameraClearFlags.SolidColor;
            _camera.backgroundColor = fogColor;
        }
        else if (_saved)
        {
            _camera.clearFlags = _originalClear;
            _camera.backgroundColor = _originalBackground;
        }

        if (clipAtFogEnd && mode == FogMode.Linear)
            _camera.farClipPlane = Mathf.Max(_camera.nearClipPlane + 1f, endDistance + 2f);
        else if (_saved)
            _camera.farClipPlane = _originalFar;
    }
}
