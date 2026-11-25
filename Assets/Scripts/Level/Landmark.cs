using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A landmark of the medina (door, hanout, zellige fountain...). Put it on the root of a landmark prefab and set
/// that prefab on a tile with the Landmark behaviour; the level builder places it and tells it its cell.
/// Facing its cell and pressing Interact plays its lines, one message box after another.
///
/// Prefab layout: this root sits on the ground. For wall landmarks its back (local z = 0) goes against the wall and
/// +Z points into the street; free-standing ones are centred on their cell. Add lights, 3D sounds, etc. as children.
/// </summary>
public class Landmark : MonoBehaviour
{
    [Tooltip("Title of the message box.")]
    [SerializeField] private string displayName = "Landmark";

    [Tooltip("Pop-up shown when the player faces it. {0} = display name.")]
    [SerializeField] private string prompt = "Examine {0}";

    [Tooltip("What the landmark says, in order. Each entry is one message box; Interact moves to the next.")]
    [SerializeField, TextArea(2, 5)] private string[] lines = { "..." };

    private static readonly Dictionary<Vector2Int, Landmark> ByCell = new Dictionary<Vector2Int, Landmark>();

    /// <summary>Raised when the player starts talking to a landmark (later: stickers, dialogue audio...).</summary>
    public static event Action<Landmark> Interacted;

    public string DisplayName => displayName;
    public IReadOnlyList<string> Lines => lines;
    public string Prompt => string.Format(prompt ?? "{0}", displayName);

    /// <summary>Level cell this landmark belongs to. Set by the level builder.</summary>
    public Vector2Int Cell
    {
        get => _cell;
        set
        {
            if (_registered && ByCell.TryGetValue(_cell, out Landmark old) && old == this) ByCell.Remove(_cell);
            _cell = value;
            _hasCell = true;
            if (isActiveAndEnabled) Register();
        }
    }

    [SerializeField, HideInInspector] private Vector2Int _cell;
    [SerializeField, HideInInspector] private bool _hasCell;
    private bool _registered;
    private int _next;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        ByCell.Clear();
        Interacted = null;
    }

    /// <summary>The landmark on a cell, if any.</summary>
    public static Landmark At(Vector2Int cell) =>
        ByCell.TryGetValue(cell, out Landmark l) && l != null ? l : null;

    private void OnEnable()
    {
        if (_hasCell) Register();
    }

    private void OnDisable()
    {
        if (_registered && ByCell.TryGetValue(_cell, out Landmark l) && l == this) ByCell.Remove(_cell);
        _registered = false;
        if (MessageBox.Instance != null) MessageBox.Instance.Closed -= ShowNext;
    }

    private void Register()
    {
        ByCell[_cell] = this;
        _registered = true;
    }

    /// <summary>Plays the landmark's lines from the start.</summary>
    public void Interact()
    {
        Interacted?.Invoke(this);
        _next = 0;
        if (MessageBox.Instance != null) MessageBox.Instance.Closed -= ShowNext;
        ShowNext();
    }

    private void ShowNext()
    {
        MessageBox box = MessageBox.Instance;
        if (box != null) box.Closed -= ShowNext;
        if (lines == null || _next >= lines.Length) return;

        string text = lines[_next++];
        if (box == null)
        {
            Debug.Log($"[{displayName}] {text}");
            ShowNext();
            return;
        }
        box.Show(displayName, text);
        if (_next < lines.Length) box.Closed += ShowNext;
    }
}
