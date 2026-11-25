using UnityEditor;
using UnityEngine;

/// <summary>Attaches the in-world, Skyrim-style compass strip to the game camera.</summary>
public static class WorldCompassBuilder
{
    [MenuItem("Tools/Dungeon/Create World Compass", priority = 63)]
    public static void Create()
    {
        Camera cam = Camera.main;
        if (cam == null)
        {
            EditorUtility.DisplayDialog("World Compass", "No Main Camera in the scene (create the player rig first).", "OK");
            return;
        }
        WorldCompass existing = Object.FindAnyObjectByType<WorldCompass>(FindObjectsInactive.Include);
        if (existing != null) Undo.DestroyObjectImmediate(existing.gameObject);

        var go = new GameObject("World Compass", typeof(MeshFilter), typeof(MeshRenderer));
        Undo.RegisterCreatedObjectUndo(go, "Create World Compass");
        go.transform.SetParent(cam.transform, false);
        go.GetComponent<MeshFilter>().sharedMesh = Resources.GetBuiltinResource<Mesh>("Quad.fbx");

        var compass = Undo.AddComponent<WorldCompass>(go);
        var so = new SerializedObject(compass);
        so.FindProperty("overlayShader").objectReferenceValue = Shader.Find("Retro/World Overlay");
        GridPlayerController player = Object.FindAnyObjectByType<GridPlayerController>();
        so.FindProperty("headingSource").objectReferenceValue = player != null ? player.transform : null;
        so.ApplyModifiedPropertiesWithoutUndo();

        Selection.activeGameObject = go;
    }
}
