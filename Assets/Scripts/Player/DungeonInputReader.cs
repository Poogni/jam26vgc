using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>A single discrete action on the grid.</summary>
public enum GridCommand
{
    None,
    MoveForward,
    MoveBack,
    StrafeLeft,
    StrafeRight,
    TurnLeft,
    TurnRight,
}

/// <summary>
/// Controls: turns keyboard/gamepad input into discrete grid commands.
/// A press made while the player is still moving is buffered for a short window,
/// and holding a key repeats the command as soon as the previous move finishes.
/// </summary>
[DefaultExecutionOrder(-100)]
public class DungeonInputReader : MonoBehaviour
{
    [Tooltip("How long (seconds) a press made mid-move is remembered and executed once the move ends.")]
    [SerializeField] private float bufferWindow = 0.2f;

    [Tooltip("When true, holding a key keeps repeating its command.")]
    [SerializeField] private bool repeatWhileHeld = true;

    private (InputAction action, GridCommand command)[] _bindings;
    private GridCommand _buffered;
    private float _bufferedTime;

    // Built lazily: after a script reload in Play mode Awake does not run again,
    // but OnEnable/Update do, and the non-serialized array is gone.
    private (InputAction action, GridCommand command)[] Bindings => _bindings ??= CreateBindings();

    private static (InputAction, GridCommand)[] CreateBindings()
    {
        return new[]
        {
            (Create("Forward", "<Keyboard>/w", "<Keyboard>/upArrow", "<Gamepad>/dpad/up"), GridCommand.MoveForward),
            (Create("Back", "<Keyboard>/s", "<Keyboard>/downArrow", "<Gamepad>/dpad/down"), GridCommand.MoveBack),
            (Create("StrafeLeft", "<Keyboard>/a", "<Gamepad>/dpad/left"), GridCommand.StrafeLeft),
            (Create("StrafeRight", "<Keyboard>/d", "<Gamepad>/dpad/right"), GridCommand.StrafeRight),
            (Create("TurnLeft", "<Keyboard>/q", "<Keyboard>/leftArrow", "<Gamepad>/leftShoulder"), GridCommand.TurnLeft),
            (Create("TurnRight", "<Keyboard>/e", "<Keyboard>/rightArrow", "<Gamepad>/rightShoulder"), GridCommand.TurnRight),
        };
    }

    private static InputAction Create(string name, params string[] paths)
    {
        var action = new InputAction(name, InputActionType.Button);
        foreach (string path in paths) action.AddBinding(path);
        return action;
    }

    private void OnEnable()
    {
        foreach (var b in Bindings) b.action.Enable();
    }

    private void OnDisable()
    {
        if (_bindings == null) return;
        foreach (var b in _bindings) b.action.Disable();
        _buffered = GridCommand.None;
    }

    private void OnDestroy()
    {
        if (_bindings == null) return;
        foreach (var b in _bindings) b.action.Dispose();
        _bindings = null;
    }

    private void Update()
    {
        // Remember the most recent fresh press so quick taps during a move are not lost.
        foreach (var b in Bindings)
        {
            if (b.action.WasPressedThisFrame())
            {
                _buffered = b.command;
                _bufferedTime = Time.unscaledTime;
            }
        }
    }

    /// <summary>
    /// Returns the next command to execute, or None. Call only when the player is ready to act.
    /// </summary>
    public GridCommand Consume()
    {
        if (GameInputLock.IsLocked)
        {
            _buffered = GridCommand.None; // don't replay presses made while a menu was open
            return GridCommand.None;
        }

        if (_buffered != GridCommand.None)
        {
            GridCommand cmd = _buffered;
            _buffered = GridCommand.None;
            if (Time.unscaledTime - _bufferedTime <= bufferWindow) return cmd;
        }

        if (!repeatWhileHeld) return GridCommand.None;

        foreach (var b in Bindings)
        {
            if (b.action.IsPressed()) return b.command;
        }
        return GridCommand.None;
    }
}
