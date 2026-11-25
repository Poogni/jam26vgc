using PrimeTween;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Pop-up over the 3D view when the player faces something they can interact with: "[Space]  Examine Door".
/// Builds its own little panel at runtime as a child of this RectTransform (put it on the 3D viewport).
/// Hidden while moving, while a message is open, or while a menu has input locked.
/// </summary>
[RequireComponent(typeof(RectTransform))]
public class InteractPrompt : MonoBehaviour
{
    [Tooltip("Found automatically if empty.")]
    [SerializeField] private PlayerInteractor interactor;

    [Tooltip("Key hint in front of the action.")]
    [SerializeField] private string keyHint = "[Space]";
    [SerializeField, Min(8)] private int fontSize = 26;
    [Tooltip("Distance of the pop-up from the bottom of the view.")]
    [SerializeField] private float bottomOffset = 70f;
    [SerializeField] private Color panelColor = new Color(0.10f, 0.07f, 0.05f, 0.85f);
    [SerializeField] private Color outlineColor = new Color(0.93f, 0.84f, 0.68f, 1f);
    [SerializeField] private Color textColor = new Color(0.97f, 0.92f, 0.80f, 1f);
    [SerializeField] private Color keyColor = new Color(1f, 0.78f, 0.35f, 1f);
    [SerializeField, Min(0.01f)] private float fadeDuration = 0.12f;

    private CanvasGroup _group;
    private RectTransform _panel;
    private Text _text;
    private string _shown;
    private bool _visible;
    private Tween _fade, _pop;

    private void Awake()
    {
        Build();
        _group.alpha = 0f;
    }

    private void OnDestroy()
    {
        if (_fade.isAlive) _fade.Stop();
        if (_pop.isAlive) _pop.Stop();
    }

    private void Update()
    {
        if (interactor == null) interactor = FindAnyObjectByType<PlayerInteractor>();
        string prompt = interactor != null ? interactor.CurrentPrompt() : null;

        if (!string.IsNullOrEmpty(prompt))
        {
            if (prompt != _shown)
            {
                _shown = prompt;
                _text.text = $"<color=#{ColorUtility.ToHtmlStringRGB(keyColor)}>{keyHint}</color>  {prompt}";
                if (_pop.isAlive) _pop.Complete();
                _panel.localScale = Vector3.one;
                _pop = Tween.PunchScale(_panel, Vector3.one * 0.08f, 0.22f, frequency: 5);
            }
            SetVisible(true);
        }
        else SetVisible(false);
    }

    private void SetVisible(bool visible)
    {
        if (visible == _visible) return;
        _visible = visible;
        if (_fade.isAlive) _fade.Stop();
        _fade = Tween.Alpha(_group, visible ? 1f : 0f, fadeDuration, useUnscaledTime: true);
        if (!visible) _shown = null;
    }

    private void Build()
    {
        var go = new GameObject("Interact Prompt", typeof(RectTransform), typeof(CanvasGroup), typeof(Image),
            typeof(Outline), typeof(ContentSizeFitter), typeof(HorizontalLayoutGroup));
        go.transform.SetParent(transform, false);
        _panel = (RectTransform)go.transform;
        _panel.anchorMin = _panel.anchorMax = new Vector2(0.5f, 0f);
        _panel.pivot = new Vector2(0.5f, 0f);
        _panel.anchoredPosition = new Vector2(0f, bottomOffset);

        _group = go.GetComponent<CanvasGroup>();
        _group.blocksRaycasts = false;
        _group.interactable = false;
        var bg = go.GetComponent<Image>();
        bg.color = panelColor;
        bg.raycastTarget = false;
        var outline = go.GetComponent<Outline>();
        outline.effectColor = outlineColor;
        outline.effectDistance = new Vector2(2f, -2f);

        // The panel hugs its text.
        var layout = go.GetComponent<HorizontalLayoutGroup>();
        layout.padding = new RectOffset(18, 18, 8, 8);
        layout.childControlWidth = layout.childControlHeight = true;
        layout.childForceExpandWidth = layout.childForceExpandHeight = false;
        var fitter = go.GetComponent<ContentSizeFitter>();
        fitter.horizontalFit = fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        var textGo = new GameObject("Text", typeof(RectTransform), typeof(Text));
        textGo.transform.SetParent(go.transform, false);
        _text = textGo.GetComponent<Text>();
        _text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        _text.fontSize = fontSize;
        _text.color = textColor;
        _text.supportRichText = true;
        _text.alignment = TextAnchor.MiddleCenter;
        _text.horizontalOverflow = HorizontalWrapMode.Overflow;
        _text.raycastTarget = false;
    }
}
