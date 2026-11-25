using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Retro text box over the bottom of the 3D view for game messages (objective found / complete...).
/// Text types out; Interact once finishes the line, again closes. Movement is paused while it's open.
/// Built by Tools > Dungeon > Create Objective UI.
/// </summary>
public class MessageBox : MonoBehaviour
{
    public static MessageBox Instance { get; private set; }

    [SerializeField] private GameObject box;
    [SerializeField] private Text titleText;
    [SerializeField] private Text bodyText;
    [SerializeField] private GameObject continueHint;
    [Tooltip("Characters per second for the typewriter effect. 0 = show instantly.")]
    [SerializeField, Min(0f)] private float charactersPerSecond = 60f;

    public bool IsOpen => box != null && box.activeSelf;

    /// <summary>Raised when the box closes.</summary>
    public event System.Action Closed;

    private string _fullText = "";
    private float _shown;
    private bool Typing => _shown < _fullText.Length;

    private void Awake()
    {
        Instance = this;
        box.SetActive(false);
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
        GameInputLock.Unlock(this);
    }

    public void Show(string title, string text)
    {
        titleText.text = title;
        _fullText = text ?? "";
        _shown = charactersPerSecond > 0f ? 0f : _fullText.Length;
        Refresh();
        box.SetActive(true);
        GameInputLock.Lock(this);
    }

    /// <summary>Finish typing, or close if the text is already complete.</summary>
    public void Advance()
    {
        if (Typing)
        {
            _shown = _fullText.Length;
            Refresh();
        }
        else Hide();
    }

    public void Hide()
    {
        if (!IsOpen) return;
        box.SetActive(false);
        GameInputLock.Unlock(this);
        Closed?.Invoke();
    }

    private void Update()
    {
        if (!IsOpen || !Typing) return;
        _shown = Mathf.Min(_fullText.Length, _shown + charactersPerSecond * Time.unscaledDeltaTime);
        Refresh();
    }

    private void Refresh()
    {
        bodyText.text = _fullText.Substring(0, Mathf.FloorToInt(_shown));
        if (continueHint != null) continueHint.SetActive(!Typing);
    }
}
