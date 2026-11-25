using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// A light beam that can be spotted from anywhere in the level, through the fog.
/// The camera's far plane is cut at the fog's end, so anything farther than that isn't drawn at all. To stay
/// visible, the beam's visual is pulled toward the camera until it sits just inside the far plane, and shrunk by
/// the same factor: from the camera it looks exactly the same size and in the same place. Walls closer than that
/// still hide its base (depth test), so it reads as "over there, behind the houses".
/// The visual is a quad (child) that turns around Y to face the camera.
/// </summary>
[ExecuteAlways]
public class FarBeacon : MonoBehaviour
{
    [Tooltip("The beam quad (child). Its local position / scale are what you'd see up close.")]
    [SerializeField] private Transform visual;
    [Tooltip("Keep the visual this share of the camera's far plane away at most.")]
    [SerializeField, Range(0.5f, 0.98f)] private float farPlaneShare = 0.9f;

    private Vector3 _baseLocalPosition;
    private Vector3 _baseLocalScale;
    private bool _captured;

    private void OnEnable()
    {
        Capture();
        RenderPipelineManager.beginCameraRendering += OnBeginCamera;
    }

    private void OnDisable()
    {
        RenderPipelineManager.beginCameraRendering -= OnBeginCamera;
        if (_captured && visual != null)
        {
            visual.localPosition = _baseLocalPosition;
            visual.localScale = _baseLocalScale;
        }
    }

    private void Capture()
    {
        if (visual == null || _captured) return;
        _baseLocalPosition = visual.localPosition;
        _baseLocalScale = visual.localScale;
        _captured = true;
    }

    // Per camera, right before it renders: works for the game camera and the Scene view alike.
    private void OnBeginCamera(ScriptableRenderContext context, Camera cam)
    {
        if (visual == null) return;
        Capture();

        // Where the beam really is.
        visual.localPosition = _baseLocalPosition;
        visual.localScale = _baseLocalScale;
        Vector3 truePos = visual.position;
        Vector3 camPos = cam.transform.position;

        // Face the camera around the vertical axis.
        Vector3 toCam = camPos - truePos;
        toCam.y = 0f;
        if (toCam.sqrMagnitude > 1e-4f) visual.rotation = Quaternion.LookRotation(-toCam.normalized, Vector3.up);

        // Pull it inside the far plane, scaling about the camera so it projects identically.
        Vector3 offset = truePos - camPos;
        Vector3 forward = cam.transform.forward;
        float depth = Mathf.Max(Vector3.Dot(offset, forward), Vector3.ProjectOnPlane(offset, Vector3.up).magnitude);
        float limit = cam.farClipPlane * farPlaneShare;
        if (depth <= limit) return;
        float k = limit / depth;
        visual.position = camPos + offset * k;
        visual.localScale = _baseLocalScale * k;
    }
}
