using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(LevelBuilder))]
public class LevelBuilderEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        var builder = (LevelBuilder)target;

        EditorGUILayout.Space(6);
        if (builder.Level == null)
        {
            EditorGUILayout.HelpBox("Assign a Level asset (Create > Dungeon > Level).", MessageType.Info);
            return;
        }

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Edit Level", GUILayout.Height(26))) Selection.activeObject = builder.Level;
            if (GUILayout.Button("Build", GUILayout.Height(26))) builder.Build();
            if (GUILayout.Button("Clear", GUILayout.Height(26))) builder.Clear();
        }
    }
}
