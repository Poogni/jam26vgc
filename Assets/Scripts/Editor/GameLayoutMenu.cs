using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

/// <summary>
/// Builds the base screen layout (reference 1600x900):
///   left  = low-res 3D view (fills whatever width is left),
///   right = fixed 690-wide black panel holding the map (630x630) and the tool bar (610x100).
/// </summary>
public static class GameLayoutMenu
{
    private static readonly Vector2 Reference = new Vector2(1600, 900);
    private const float PanelWidth = 690f;

    private static readonly Color PanelColor = Color.black;
    private static readonly Color MapColor = new Color32(0x60, 0xCD, 0xE3, 0xFF);   // placeholder
    private static readonly Color ToolsColor = new Color32(0x6B, 0xBE, 0x32, 0xFF); // placeholder

    [MenuItem("Tools/Dungeon/Create Game Layout", priority = 60)]
    public static void CreateGameLayout()
    {
        if (Object.FindAnyObjectByType<RetroViewport>() != null)
        {
            Debug.LogWarning("The scene already has a game layout (RetroViewport found).");
            return;
        }

        // ---- Canvas: scales with screen height, so the 690-wide panel keeps its proportions at any aspect.
        var canvasGo = new GameObject("GameUI", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        Undo.RegisterCreatedObjectUndo(canvasGo, "Create Game Layout");
        var canvas = canvasGo.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.pixelPerfect = true;
        var scaler = canvasGo.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = Reference;
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 1f;

        // ---- Left: 3D view = everything left of the panel.
        var view = Child("Viewport3D", canvasGo.transform, typeof(RawImage), typeof(RetroViewport));
        Stretch(view, new Vector2(0, 0), new Vector2(1, 1));
        view.offsetMax = new Vector2(-PanelWidth, 0);
        var raw = view.GetComponent<RawImage>();
        raw.color = Color.white;
        raw.raycastTarget = false;

        // ---- Right: fixed-width panel.
        var panel = Child("SidePanel", canvasGo.transform, typeof(Image));
        panel.anchorMin = new Vector2(1, 0);
        panel.anchorMax = new Vector2(1, 1);
        panel.pivot = new Vector2(1, 0.5f);
        panel.sizeDelta = new Vector2(PanelWidth, 0);
        panel.anchoredPosition = Vector2.zero;
        panel.GetComponent<Image>().color = PanelColor;

        var map = Child("MapArea", panel, typeof(Image));
        TopCentered(map, new Vector2(630, 630), 60);
        map.GetComponent<Image>().color = MapColor;

        var tools = Child("ToolBar", panel, typeof(Image));
        TopCentered(tools, new Vector2(610, 100), 710);
        tools.GetComponent<Image>().color = ToolsColor;

        // ---- Something must draw to the actual screen, or Unity shows "No cameras rendering".
        CreateScreenCamera();
        ConfigureGameCamera();
        EnsureEventSystem();

        Selection.activeGameObject = canvasGo;
    }

    private static RectTransform Child(string name, Transform parent, params System.Type[] components)
    {
        var types = new System.Type[components.Length + 1];
        types[0] = typeof(RectTransform);
        components.CopyTo(types, 1);
        var go = new GameObject(name, types);
        Undo.RegisterCreatedObjectUndo(go, "Create Game Layout");
        go.transform.SetParent(parent, false);
        go.layer = LayerMask.NameToLayer("UI");
        return (RectTransform)go.transform;
    }

    private static void Stretch(RectTransform rt, Vector2 min, Vector2 max)
    {
        rt.anchorMin = min;
        rt.anchorMax = max;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }

    /// <summary>Fixed size, horizontally centered in the parent, <paramref name="top"/> units from its top edge.</summary>
    private static void TopCentered(RectTransform rt, Vector2 size, float top)
    {
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.sizeDelta = size;
        rt.anchoredPosition = new Vector2(0, -top);
    }

    private static void CreateScreenCamera()
    {
        var go = new GameObject("ScreenCamera", typeof(Camera));
        Undo.RegisterCreatedObjectUndo(go, "Create Game Layout");
        var cam = go.GetComponent<Camera>();
        cam.cullingMask = 0;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = Color.black;
        cam.depth = -100;
        cam.allowHDR = false;
        cam.allowMSAA = false;
        var data = go.AddComponent<UniversalAdditionalCameraData>();
        data.renderPostProcessing = false;
        data.antialiasing = AntialiasingMode.None;
    }

    /// <summary>Crisp, aliased pixels: no MSAA / post AA on the game camera.</summary>
    private static void ConfigureGameCamera()
    {
        Camera cam = Camera.main;
        if (cam == null) return;
        Undo.RecordObject(cam, "Create Game Layout");
        cam.allowMSAA = false;
        cam.allowHDR = false;
        var data = cam.GetComponent<UniversalAdditionalCameraData>();
        if (data == null) data = Undo.AddComponent<UniversalAdditionalCameraData>(cam.gameObject);
        Undo.RecordObject(data, "Create Game Layout");
        data.antialiasing = AntialiasingMode.None;
    }

    private static void EnsureEventSystem()
    {
        if (Object.FindAnyObjectByType<EventSystem>() != null) return;
        var go = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
        Undo.RegisterCreatedObjectUndo(go, "Create Game Layout");
    }
}
