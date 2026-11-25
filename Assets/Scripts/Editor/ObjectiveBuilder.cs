using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using B = SettingsMenuBuilder;

/// <summary>
/// Sets up the objective loop in the open scene: message box over the 3D view, Interact on the player,
/// and the GameObjective tracker on the Level object. Safe to run again (it only adds what's missing).
/// </summary>
public static class ObjectiveBuilder
{
    [MenuItem("Tools/Dungeon/Create Objective UI", priority = 64)]
    public static void Create()
    {
        RetroViewport viewport = Object.FindAnyObjectByType<RetroViewport>();
        GridPlayerController player = Object.FindAnyObjectByType<GridPlayerController>();
        LevelBuilder level = Object.FindAnyObjectByType<LevelBuilder>();
        if (viewport == null || player == null || level == null)
        {
            EditorUtility.DisplayDialog("Objective", "Needs the game layout, the player rig and a Level in the scene.", "OK");
            return;
        }

        if (player.GetComponent<PlayerInteractor>() == null) Undo.AddComponent<PlayerInteractor>(player.gameObject);
        if (level.GetComponent<GameObjective>() == null) Undo.AddComponent<GameObjective>(level.gameObject);
        if (Object.FindAnyObjectByType<MessageBox>(FindObjectsInactive.Include) == null) CreateMessageBox(viewport.transform);
    }

    private static void CreateMessageBox(Transform viewport)
    {
        Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        // Always-active root covering the 3D view; the box under it is toggled.
        RectTransform root = B.Rect("Messages", viewport);
        B.Stretch(root);
        var messageBox = Undo.AddComponent<MessageBox>(root.gameObject);

        RectTransform box = B.Rect("Box", root);
        box.anchorMin = new Vector2(0, 0);
        box.anchorMax = new Vector2(1, 0);
        box.pivot = new Vector2(0.5f, 0);
        box.offsetMin = new Vector2(48, 48);
        box.offsetMax = new Vector2(-48, 48 + 210);
        B.AddImage(box, new Color(0.06f, 0.05f, 0.04f, 0.92f));
        B.AddOutline(box.gameObject, B.Sand);

        Text title = B.AddText(B.Rect("Title", box), font, "Objective", 28, TextAnchor.UpperLeft);
        var titleRt = (RectTransform)title.transform;
        titleRt.anchorMin = new Vector2(0, 1);
        titleRt.anchorMax = new Vector2(1, 1);
        titleRt.pivot = new Vector2(0.5f, 1);
        titleRt.offsetMin = new Vector2(24, -56);
        titleRt.offsetMax = new Vector2(-24, -16);
        title.color = new Color(1f, 0.78f, 0.45f);
        title.fontStyle = FontStyle.Bold;

        Text body = B.AddText(B.Rect("Body", box), font, "", 26, TextAnchor.UpperLeft);
        var bodyRt = (RectTransform)body.transform;
        B.Stretch(bodyRt);
        bodyRt.offsetMin = new Vector2(24, 40);
        bodyRt.offsetMax = new Vector2(-24, -64);
        body.horizontalOverflow = HorizontalWrapMode.Wrap;
        body.verticalOverflow = VerticalWrapMode.Truncate;
        body.lineSpacing = 1.15f;

        Text hint = B.AddText(B.Rect("Continue", box), font, "[Space]", 20, TextAnchor.LowerRight);
        var hintRt = (RectTransform)hint.transform;
        hintRt.anchorMin = hintRt.anchorMax = hintRt.pivot = new Vector2(1, 0);
        hintRt.sizeDelta = new Vector2(160, 30);
        hintRt.anchoredPosition = new Vector2(-18, 10);
        hint.color = new Color(B.Sand.r, B.Sand.g, B.Sand.b, 0.6f);

        var so = new SerializedObject(messageBox);
        so.FindProperty("box").objectReferenceValue = box.gameObject;
        so.FindProperty("titleText").objectReferenceValue = title;
        so.FindProperty("bodyText").objectReferenceValue = body;
        so.FindProperty("continueHint").objectReferenceValue = hint.gameObject;
        so.ApplyModifiedPropertiesWithoutUndo();

        box.gameObject.SetActive(false);
    }
}
