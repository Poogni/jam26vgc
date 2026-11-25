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

    /// <summary>Interact with the tile in front of the player. Returns true if it's interactable.</summary>
    public bool TryInteract()
    {
        LevelData level = _player.Level;
        if (level == null) return false;
        Vector2Int target = _player.FacingCell;
        TileDefinition tile = level.GetTile(target);
        if (tile == null || tile.Behavior != TileDefinition.TileBehavior.Objective) return false;
        Interacted?.Invoke(target, tile);
        return true;
    }
}
