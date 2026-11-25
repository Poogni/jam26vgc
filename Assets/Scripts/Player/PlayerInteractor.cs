using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

/// <summary>
/// Interact (Space / F / Left Click / gamepad A) with the tile the player is facing.
/// Pressing it while a message is open advances / closes the message instead.
/// Clicks on UI (the map canvas, buttons...) don't count, so drawing on the map never interacts.
/// </summary>
[RequireComponent(typeof(GridPlayerController))]
public class PlayerInteractor : MonoBehaviour
{
    /// <summary>Raised when the player interacts with a tile: cell, tile.</summary>
    public static event System.Action<Vector2Int, TileDefinition> Interacted;

    /// <summary>
    /// Raised on every interact press (before the tile check): the cell faced, then the cell stood on.
    /// For things that aren't tiles in the level asset, like the return beacon spawned at runtime.
    /// </summary>
    public static event System.Action<Vector2Int, Vector2Int> InteractPressed;

    /// <summary>
    /// Extra things to interact with that aren't level tiles (e.g. the return beacon): given the cell faced and the
    /// cell stood on, return the prompt to show ("Head home"), or null. Registered by whoever owns them.
    /// </summary>
    public static readonly System.Collections.Generic.List<System.Func<Vector2Int, Vector2Int, string>> PromptProviders =
        new System.Collections.Generic.List<System.Func<Vector2Int, Vector2Int, string>>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => PromptProviders.Clear();

    public GridPlayerController Player => _player;

    private GridPlayerController _player;
    private InputAction _interact;

    private void Awake()
    {
        _player = GetComponent<GridPlayerController>();
        _interact = new InputAction("Interact", InputActionType.Button);
        _interact.AddBinding("<Keyboard>/space");
        _interact.AddBinding("<Keyboard>/f");
        _interact.AddBinding("<Mouse>/leftButton");
        _interact.AddBinding("<Gamepad>/buttonSouth");
    }

    private void OnEnable() => _interact.Enable();
    private void OnDisable() => _interact.Disable();
    private void OnDestroy() => _interact.Dispose();

    private void Update()
    {
        if (!_interact.WasPressedThisFrame()) return;

        bool isClick = _interact.activeControl?.device is Mouse;
        if (isClick && EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) return;

        MessageBox box = MessageBox.Instance;
        if (box != null && box.IsOpen)
        {
            box.Advance();
            return;
        }

        if (GameInputLock.IsLocked || _player.IsBusy) return;
        TryInteract();
    }

    /// <summary>
    /// What pressing Interact would do right now ("Examine Door"), or null if there's nothing to interact with.
    /// Null too while the player can't act (moving, a message open, a menu open).
    /// </summary>
    public string CurrentPrompt()
    {
        LevelData level = _player != null ? _player.Level : null;
        if (level == null || _player.IsBusy || GameInputLock.IsLocked) return null;
        if (MessageBox.Instance != null && MessageBox.Instance.IsOpen) return null;

        Vector2Int target = _player.FacingCell;
        foreach (var provider in PromptProviders)
        {
            string extra = provider(target, _player.Cell);
            if (!string.IsNullOrEmpty(extra)) return extra;
        }
        TileDefinition tile = level.GetTile(target);
        if (tile == null) return null;
        if (tile.IsLandmark)
        {
            Landmark landmark = Landmark.At(target);
            return landmark != null ? landmark.Prompt : null;
        }
        if (tile.Behavior == TileDefinition.TileBehavior.Objective)
            return "Examine " + (string.IsNullOrEmpty(level.ObjectiveName) ? "it" : level.ObjectiveName);
        return null;
    }

    /// <summary>Interact with the tile in front of the player. Returns true if it's interactable.</summary>
    public bool TryInteract()
    {
        LevelData level = _player.Level;
        if (level == null) return false;
        Vector2Int target = _player.FacingCell;
        InteractPressed?.Invoke(target, _player.Cell);
        TileDefinition tile = level.GetTile(target);
        if (tile != null && tile.IsLandmark)
        {
            Landmark landmark = Landmark.At(target);
            if (landmark == null) return false;
            landmark.Interact();
            return true;
        }
        if (tile == null || tile.Behavior != TileDefinition.TileBehavior.Objective) return false;
        Interacted?.Invoke(target, tile);
        return true;
    }
}
