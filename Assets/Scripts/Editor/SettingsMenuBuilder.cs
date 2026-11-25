using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Builds the settings overlay + tool bar button inside the existing GameUI canvas.</summary>
public static class SettingsMenuBuilder
{
    internal static readonly Color Dim = new Color(0f, 0f, 0f, 0.6f);
    internal static readonly Color PanelColor = new Color(0.10f, 0.08f, 0.06f, 0.97f);
    internal static readonly Color Sand = new Color(0.93f, 0.84f, 0.68f);
    internal static readonly Color ButtonColor = new Color(0.25f, 0.19f, 0.13f);
    internal static readonly Color BoxColor = new Color(0.20f, 0.16f, 0.12f);

    [MenuItem("Tools/Dungeon/Create Settings Menu", priority = 61)]
    public static void Create()
    {
        Canvas canvas = FindGameCanvas();
        if (canvas == null)
        {
            EditorUtility.DisplayDialog("Settings Menu", "Create the game layout first (Tools > Dungeon > Create Game Layout).", "OK");
            return;
        }
        if (Object.FindAnyObjectByType<SettingsMenu>(FindObjectsInactive.Include) != null)
        {
            Debug.LogWarning("The scene already has a SettingsMenu.");
            return;
        }

        Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        // Always-active root holds the controller; the overlay below it is toggled.
        RectTransform root = Rect("Settings", canvas.transform);
        Stretch(root);
        root.SetAsLastSibling(); // draw above everything else
        var menu = Undo.AddComponent<SettingsMenu>(root.gameObject);

        RectTransform overlay = Rect("Overlay", root);
        Stretch(overlay);
        AddImage(overlay, Dim).raycastTarget = true; // blocks clicks to whatever is behind

        RectTransform panel = Rect("Panel", overlay);
        panel.anchorMin = panel.anchorMax = panel.pivot = new Vector2(0.5f, 0.5f);
        panel.sizeDelta = new Vector2(560, 450);
        AddImage(panel, PanelColor);
        AddOutline(panel.gameObject, Sand);

        Text title = AddText(Rect("Title", panel), font, "SETTINGS", 40, TextAnchor.MiddleCenter);
        TopRow((RectTransform)title.transform, 30, 60);

        Toggle crt = CreateToggle(panel, font, "CRT Filter", 120);
        Toggle trails = CreateToggle(panel, font, "Motion Trails", 190);
        Toggle marker = CreateToggle(panel, font, "Player Marker on Map", 260);

        Button close = CreateButton(panel, font, "Close", new Vector2(200, 56));
        var closeRt = (RectTransform)close.transform;
        closeRt.anchorMin = closeRt.anchorMax = new Vector2(0.5f, 0f);
        closeRt.pivot = new Vector2(0.5f, 0f);
        closeRt.anchoredPosition = new Vector2(0, 30);

        Text hint = AddText(Rect("Hint", panel), font, "Esc to close", 18, TextAnchor.MiddleCenter);
        hint.color = new Color(Sand.r, Sand.g, Sand.b, 0.5f);
        var hintRt = (RectTransform)hint.transform;
        hintRt.anchorMin = new Vector2(0, 0);
        hintRt.anchorMax = new Vector2(1, 0);
        hintRt.pivot = new Vector2(0.5f, 0f);
        hintRt.sizeDelta = new Vector2(0, 24);
        hintRt.anchoredPosition = new Vector2(0, 4);

        // Keyboard / gamepad navigation order: CRT -> Trails -> Close.
        Chain(crt, trails, marker, close);

        // Small button in the black strip under the tool bar (the tool bar itself is reserved for
        // cartography tools).
        Button open = null;
        Transform sidePanel = canvas.transform.Find("SidePanel");
        if (sidePanel != null)
        {
            open = CreateButton(sidePanel, font, "Settings", new Vector2(140, 44));
            PlaceSettingsButton((RectTransform)open.transform);
        }

        var so = new SerializedObject(menu);
        so.FindProperty("overlay").objectReferenceValue = overlay.gameObject;
        so.FindProperty("crtToggle").objectReferenceValue = crt;
        so.FindProperty("trailsToggle").objectReferenceValue = trails;
        so.FindProperty("markerToggle").objectReferenceValue = marker;
        so.FindProperty("openButton").objectReferenceValue = open;
        so.FindProperty("closeButton").objectReferenceValue = close;
        so.FindProperty("viewport").objectReferenceValue = Object.FindAnyObjectByType<RetroViewport>();
        so.ApplyModifiedPropertiesWithoutUndo();

        overlay.gameObject.SetActive(false); // hidden until opened
        Selection.activeGameObject = root.gameObject;
    }

    /// <summary>Bottom-right of the side panel, inside the margin below the tool bar (tool bar ends 810 of 900).</summary>
    public static void PlaceSettingsButton(RectTransform rt)
    {
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(1f, 0f);
        rt.sizeDelta = new Vector2(140, 44);
        rt.anchoredPosition = new Vector2(-40, 23);
        foreach (Text t in rt.GetComponentsInChildren<Text>()) t.fontSize = 22;
    }

    /// <summary>Adds the "Player Marker on Map" toggle to an existing settings menu (older scenes).</summary>
    public static void EnsureMarkerToggle()
    {
        SettingsMenu menu = Object.FindAnyObjectByType<SettingsMenu>(FindObjectsInactive.Include);
        if (menu == null) return;
        var so = new SerializedObject(menu);
        if (so.FindProperty("markerToggle").objectReferenceValue != null) return;

        var crt = (Toggle)so.FindProperty("crtToggle").objectReferenceValue;
        var trails = (Toggle)so.FindProperty("trailsToggle").objectReferenceValue;
        var close = (Button)so.FindProperty("closeButton").objectReferenceValue;
        var panel = (RectTransform)crt.transform.parent;
        Undo.RecordObject(panel, "Add Marker Toggle");
        panel.sizeDelta = new Vector2(panel.sizeDelta.x, 450);

        Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        Toggle marker = CreateToggle(panel, font, "Player Marker on Map", 260);
        Chain(crt, trails, marker, close);
        so.FindProperty("markerToggle").objectReferenceValue = marker;
        so.ApplyModifiedProperties();
    }

    // ------------------------------------------------------------------ widgets

    internal static Toggle CreateToggle(Transform parent, Font font, string label, float top)
    {
        RectTransform rt = Rect(label + " Toggle", parent);
        TopRow(rt, top, 56, 60);
        var toggle = rt.gameObject.AddComponent<Toggle>();

        // Whole row is the hit area (and shows the selection highlight).
        Image row = AddImage(rt, new Color(1f, 1f, 1f, 0f));
        toggle.targetGraphic = row;
        var colors = toggle.colors;
        colors.normalColor = new Color(1f, 1f, 1f, 0f);
        colors.highlightedColor = new Color(1f, 1f, 1f, 0.08f);
        colors.selectedColor = new Color(1f, 1f, 1f, 0.12f);
        colors.pressedColor = new Color(1f, 1f, 1f, 0.18f);
        toggle.colors = colors;

        RectTransform box = Rect("Box", rt);
        box.anchorMin = box.anchorMax = box.pivot = new Vector2(1f, 0.5f);
        box.sizeDelta = new Vector2(40, 40);
        box.anchoredPosition = new Vector2(-10, 0);
        AddImage(box, BoxColor);
        AddOutline(box.gameObject, Sand);

        RectTransform check = Rect("Check", box);
        Stretch(check, 8);
        toggle.graphic = AddImage(check, Sand);

        Text text = AddText(Rect("Label", rt), font, label, 30, TextAnchor.MiddleLeft);
        var textRt = (RectTransform)text.transform;
        Stretch(textRt);
        textRt.offsetMin = new Vector2(14, 0);
        textRt.offsetMax = new Vector2(-60, 0);

        toggle.isOn = true;
        return toggle;
    }

    internal static Button CreateButton(Transform parent, Font font, string label, Vector2 size)
    {
        RectTransform rt = Rect(label + " Button", parent);
        rt.sizeDelta = size;
        Image bg = AddImage(rt, ButtonColor);
        AddOutline(rt.gameObject, Sand);
        var button = rt.gameObject.AddComponent<Button>();
        button.targetGraphic = bg;
        var colors = button.colors;
        colors.highlightedColor = new Color(1.3f, 1.3f, 1.3f);
        colors.selectedColor = new Color(1.4f, 1.4f, 1.4f);
        colors.pressedColor = new Color(0.8f, 0.8f, 0.8f);
        colors.colorMultiplier = 1.5f;
        button.colors = colors;

        Text text = AddText(Rect("Label", rt), font, label, 28, TextAnchor.MiddleCenter);
        Stretch((RectTransform)text.transform);
        return button;
    }

    internal static void Chain(params Selectable[] items)
    {
        for (int i = 0; i < items.Length; i++)
        {
            var nav = new Navigation
            {
                mode = Navigation.Mode.Explicit,
                selectOnUp = items[(i - 1 + items.Length) % items.Length],
                selectOnDown = items[(i + 1) % items.Length],
            };
            items[i].navigation = nav;
        }
    }

    // ------------------------------------------------------------------ helpers

    private static Canvas FindGameCanvas()
    {
        RetroViewport vp = Object.FindAnyObjectByType<RetroViewport>();
        if (vp != null) return vp.GetComponentInParent<Canvas>();
        return Object.FindAnyObjectByType<Canvas>();
    }

    internal static RectTransform Rect(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        Undo.RegisterCreatedObjectUndo(go, "Create Settings Menu");
        go.layer = LayerMask.NameToLayer("UI");
        go.transform.SetParent(parent, false);
        return (RectTransform)go.transform;
    }

    internal static void Stretch(RectTransform rt, float inset = 0f)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = new Vector2(inset, inset);
        rt.offsetMax = new Vector2(-inset, -inset);
    }

    /// <summary>Full-width row (minus side margins), <paramref name="top"/> units below the parent's top.</summary>
    private static void TopRow(RectTransform rt, float top, float height, float sideMargin = 30f)
    {
        rt.anchorMin = new Vector2(0, 1);
        rt.anchorMax = new Vector2(1, 1);
        rt.pivot = new Vector2(0.5f, 1);
        rt.offsetMin = new Vector2(sideMargin, 0);
        rt.offsetMax = new Vector2(-sideMargin, 0);
        rt.sizeDelta = new Vector2(rt.sizeDelta.x, height);
        rt.anchoredPosition = new Vector2(0, -top);
    }

    internal static Image AddImage(RectTransform rt, Color color)
    {
        var img = rt.gameObject.AddComponent<Image>();
        img.color = color;
        return img;
    }

    internal static void AddOutline(GameObject go, Color color)
    {
        var outline = go.AddComponent<Outline>();
        outline.effectColor = color;
        outline.effectDistance = new Vector2(2, -2);
    }

    internal static Text AddText(RectTransform rt, Font font, string value, int size, TextAnchor anchor)
    {
        var text = rt.gameObject.AddComponent<Text>();
        text.font = font;
        text.text = value;
        text.fontSize = size;
        text.alignment = anchor;
        text.color = Sand;
        text.raycastTarget = false;
        return text;
    }
}
