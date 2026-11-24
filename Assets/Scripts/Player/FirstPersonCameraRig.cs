using PrimeTween;
using UnityEngine;

/// <summary>
/// Camera: sits on the "Head" pivot (child of the player at eye height) and adds juice
/// on top of the grid motion — a small head bob per step and a punch when bumping a wall.
/// Expected hierarchy: Player (GridPlayerController) > Head (this) > Main Camera.
/// </summary>
public class FirstPersonCameraRig : MonoBehaviour
{
    [SerializeField] private GridPlayerController player;
    [Tooltip("Transform that receives the bump punch. Defaults to the child Camera.")]
    [SerializeField] private Transform shakePivot;

    [Header("View")]
    [SerializeField] private float eyeHeight = 1.6f;

    [Header("Step bob")]
    [SerializeField] private float bobDepth = 0.05f;
    [SerializeField] private Ease bobEase = Ease.OutSine;

    [Header("Turn sway")]
    [Tooltip("Degrees the view rolls into a turn. 0 disables it.")]
    [SerializeField] private float turnRollDegrees = 1.5f;

    [Header("Wall bump")]
    [SerializeField] private float bumpDistance = 0.12f;
    [SerializeField] private float bumpDuration = 0.22f;
    [SerializeField] private float bumpFrequency = 8f;

    private Tween _bob;
    private Tween _roll;
    private Tween _bump;

    private void Awake()
    {
        if (player == null) player = GetComponentInParent<GridPlayerController>();
        if (shakePivot == null)
        {
            Camera cam = GetComponentInChildren<Camera>();
            if (cam != null) shakePivot = cam.transform;
        }
        transform.localPosition = new Vector3(0f, eyeHeight, 0f);
    }

    private void OnEnable()
    {
        if (player == null) return;
        player.Stepped += OnStepped;
        player.Turned += OnTurned;
        player.Bumped += OnBumped;
    }

    private void OnDisable()
    {
        if (player == null) return;
        player.Stepped -= OnStepped;
        player.Turned -= OnTurned;
        player.Bumped -= OnBumped;
    }

    private void OnStepped(Vector3 worldDir, float duration)
    {
        if (bobDepth <= 0f) return;
        _bob.Stop();
        // Dip down and back up over the course of the step.
        _bob = Tween.LocalPositionY(transform, eyeHeight, eyeHeight - bobDepth, duration * 0.5f,
            bobEase, cycles: 2, cycleMode: CycleMode.Yoyo);
    }

    private void OnTurned(int sign, float duration)
    {
        if (shakePivot == null || Mathf.Approximately(turnRollDegrees, 0f)) return;
        _roll.Stop();
        // Lean slightly into the turn (roll away from the turn direction), then settle.
        _roll = Tween.LocalEulerAngles(shakePivot, Vector3.zero, new Vector3(0f, 0f, -sign * turnRollDegrees),
            duration * 0.5f, Ease.OutSine, cycles: 2, cycleMode: CycleMode.Yoyo);
    }

    private void OnBumped(Vector3 worldDir)
    {
        if (shakePivot == null) return;
        _bump.Stop();
        shakePivot.localPosition = Vector3.zero;
        Vector3 localDir = shakePivot.parent.InverseTransformDirection(worldDir);
        _bump = Tween.PunchLocalPosition(shakePivot, localDir * bumpDistance, bumpDuration, bumpFrequency);
    }
}
