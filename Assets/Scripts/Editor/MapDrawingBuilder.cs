using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using B = SettingsMenuBuilder;

/// <summary>
/// Turns the side panel's MapArea into the freehand cartography map:
/// generated paper + subtle grid with the start point + ink layer + pixel particles, with Pen / Eraser buttons on the tool bar.
/// Running it again rebuilds the map (existing map objects and tool buttons are replaced).
/// </summary>
public static class MapDrawingBuilder
{
    private static readonly Color MapBackground = new Color(0.62f, 0.54f, 0.40f); // shows only if the paper doesn't fill

    [MenuItem("Tools/Dungeon/Create Map Drawing", priority = 62)]
    public static void Create()
    {
        Transform mapArea = FindInGameUI("SidePanel/MapArea");
        Transform toolBar = FindInGameUI("SidePanel/ToolBar");
        if (mapArea == null || toolBar == null)
        {
            EditorUtility.DisplayDialog("Map Drawing", "Create the game layout first (Tools > Dungeon > Create Game Layout).", "OK");
            return;
        }

        RemoveOld(mapArea, toolBar);
        Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        var areaImage = mapArea.GetComponent<Image>();
        if (areaImage != null)
        {
            Undo.RecordObject(areaImage, "Create Map Drawing");
            areaImage.color = MapBackground;
        }

        // Frame: sized by MapGrid to the level's aspect, centered in the MapArea.
        RectTransform frame = B.Rect("MapFrame", mapArea);
        var mapGrid = Undo.AddComponent<MapGrid>(frame.gameObject);

        RectTransform paperRt = B.Rect("Paper", frame);
        B.Stretch(paperRt);
        var paperImage = paperRt.gameObject.AddComponent<RawImage>();
        paperImage.raycastTarget = false;

        RectTransform gridRt = B.Rect("Grid", frame);
        B.Stretch(gridRt);
        var gridImage = gridRt.gameObject.AddComponent<RawImage>();
        gridImage.raycastTarget = false;

        RectTransform ink = B.Rect("Ink", frame);
        B.Stretch(ink);
        var inkImage = ink.gameObject.AddComponent<RawImage>();
        inkImage.raycastTarget = true; // clicks on the map count as "over UI"
        inkImage.color = Color.white;
        var canvas = Undo.AddComponent<MapCanvas>(ink.gameObject);

        RectTransform markersRt = B.Rect("Markers", frame);
        B.Stretch(markersRt);
        var markerImage = markersRt.gameObject.AddComponent<RawImage>();
        markerImage.raycastTarget = false;

        RectTransform fx = B.Rect("Particles", frame);
        B.Stretch(fx);
        var particles = Undo.AddComponent<MapParticles>(fx.gameObject);

        // Tool buttons, left-aligned on the tool bar.
        Button pen = B.CreateButton(toolBar, font, "Pen", new Vector2(130, 64));
        Place((RectTransform)pen.transform, 18);
        Button eraser = B.CreateButton(toolBar, font, "Eraser", new Vector2(130, 64));
        Place((RectTransform)eraser.transform, 18 + 130 + 14);

        LevelBuilder builder = Object.FindAnyObjectByType<LevelBuilder>();
        var gso = new SerializedObject(mapGrid);
        gso.FindProperty("gridImage").objectReferenceValue = gridImage;
        gso.FindProperty("paperImage").objectReferenceValue = paperImage;
        gso.FindProperty("markerImage").objectReferenceValue = markerImage;
        gso.FindProperty("level").objectReferenceValue = builder != null ? builder.Level : null;
        gso.ApplyModifiedPropertiesWithoutUndo();
        mapGrid.Rebuild();

        var so = new SerializedObject(canvas);
        so.FindProperty("grid").objectReferenceValue = mapGrid;
        so.FindProperty("particles").objectReferenceValue = particles;
        so.FindProperty("penButton").objectReferenceValue = pen;
        so.FindProperty("eraserButton").objectReferenceValue = eraser;
        so.ApplyModifiedPropertiesWithoutUndo();

        Selection.activeGameObject = ink.gameObject;
    }

    private static void RemoveOld(Transform mapArea, Transform toolBar)
    {
        Transform oldFrame = mapArea.Find("MapFrame");
        if (oldFrame != null) Undo.DestroyObjectImmediate(oldFrame.gameObject);
        foreach (string name in new[] { "Pen Button", "Eraser Button" })
        {
            Transform old = toolBar.Find(name);
            if (old != null) Undo.DestroyObjectImmediate(old.gameObject);
        }
    }

    private static void Place(RectTransform rt, float left)
    {
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0f, 0.5f);
        rt.anchoredPosition = new Vector2(left, 0);
    }

    private static Transform FindInGameUI(string path)
    {
        RetroViewport vp = Object.FindAnyObjectByType<RetroViewport>();
        Canvas canvas = vp != null ? vp.GetComponentInParent<Canvas>() : null;
        return canvas != null ? canvas.transform.Find(path) : null;
    }
}
