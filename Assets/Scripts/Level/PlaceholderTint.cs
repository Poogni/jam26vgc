using UnityEngine;

/// <summary>
/// Colors placeholder (primitive) level geometry without creating materials.
/// Property blocks aren't saved with the scene, so this re-applies the color on load.
/// </summary>
[ExecuteAlways, DisallowMultipleComponent]
public class PlaceholderTint : MonoBehaviour
{
    private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");
    private static readonly int LegacyColor = Shader.PropertyToID("_Color");

    [SerializeField] private Color color = Color.gray;

    public Color Color
    {
        get => color;
        set { color = value; Apply(); }
    }

    private void OnEnable() => Apply();
    private void OnValidate() => Apply();

    private void Apply()
    {
        var r = GetComponent<Renderer>();
        if (r == null) return;
        var block = new MaterialPropertyBlock();
        r.GetPropertyBlock(block);
        block.SetColor(BaseColor, color);
        block.SetColor(LegacyColor, color);
        r.SetPropertyBlock(block);
    }
}
