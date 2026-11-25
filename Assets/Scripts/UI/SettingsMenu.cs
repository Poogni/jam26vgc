using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// Simple settings overlay. Opens with Esc / gamepad Start or the tool bar's Settings button.
/// While open, player movement input is paused. Built by Tools > Dungeon > Create Settings Menu.
/// </summary>
public class SettingsMenu : MonoBehaviour
{
    [SerializeField] private GameObject overlay;
    [SerializeField] private Toggle crtToggle;
    [SerializeField] private Toggle trailsToggle;
    [SerializeField] private Toggle markerToggle;
    [SerializeField] private Button openButton;
    [SerializeField] private Button closeButton;
    [Tooltip("Found automatically if empty.")]
    [SerializeField] private RetroViewport viewport;

    public bool IsOpen => overlay != null && overlay.activeSelf;

    private InputAction _menuAction;

    private void Awake()
    {
        if (viewport == null) viewport = FindAnyObjectByType<RetroViewport>();

        _menuAction = new InputAction("Menu", InputActionType.Button);
        _menuAction.AddBinding("<Keyboard>/escape");
        _menuAction.AddBinding("<Gamepad>/start");

        crtToggle.SetIsOnWithoutNotify(GameSettings.CrtFilter);
        trailsToggle.SetIsOnWithoutNotify(GameSettings.MotionTrails);
        crtToggle.onValueChanged.AddListener(on => GameSettings.CrtFilter = on);
        trailsToggle.onValueChanged.AddListener(on => GameSettings.MotionTrails = on);
        if (markerToggle != null)
        {
            markerToggle.SetIsOnWithoutNotify(GameSettings.PlayerMarker);
            markerToggle.onValueChanged.AddListener(on => GameSettings.PlayerMarker = on);
        }
        if (openButton != null) openButton.onClick.AddListener(Open);
        if (closeButton != null) closeButton.onClick.AddListener(Close);

        overlay.SetActive(false);
    }

    private void Start() => ApplySettings(); // after RetroViewport has set itself up

    private void OnEnable()
    {
        _menuAction?.Enable();
        GameSettings.Changed += ApplySettings;
    }

    private void OnDisable()
    {
        _menuAction?.Disable();
        GameSettings.Changed -= ApplySettings;
        GameInputLock.Unlock(this);
    }

    private void OnDestroy() => _menuAction?.Dispose();

    private void Update()
    {
        if (_menuAction.WasPressedThisFrame())
        {
            // Esc closes an open message first.
            if (!IsOpen && MessageBox.Instance != null && MessageBox.Instance.IsOpen)
            {
                MessageBox.Instance.Hide();
                return;
            }
            if (IsOpen) Close();
            else Open();
        }
    }

    public void Open()
    {
        overlay.SetActive(true);
        GameInputLock.Lock(this);
        // Keyboard / gamepad navigation starts on the first option.
        if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(crtToggle.gameObject);
    }

    public void Close()
    {
        overlay.SetActive(false);
        GameInputLock.Unlock(this);
        if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
    }

    private void ApplySettings()
    {
        if (viewport == null) return;
        viewport.CrtEnabled = GameSettings.CrtFilter;
        viewport.PersistenceEnabled = GameSettings.MotionTrails;
    }
}
