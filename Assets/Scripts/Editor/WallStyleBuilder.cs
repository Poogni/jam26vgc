using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Turns the wall / decal textures in Assets/Textures/Walls and Assets/Textures/Decals into materials and a
/// WallStyle asset. Decals are sorted by name: decal_bottom_*, decal_mid_*, decal_top_*.
/// Re-run it after adding textures: existing materials are updated, new ones created.
/// </summary>
public static class WallStyleBuilder
{
    private const string WallTextures = "Assets/Textures/Walls";
    private const string DecalTextures = "Assets/Textures/Decals";
    private const string MaterialFolder = "Assets/Materials/Walls";
    private const string StylePath = "Assets/Level/WallStyle_Medina.asset";

    [MenuItem("Tools/Dungeon/Create Wall Style From Textures", priority = 45)]
    public static void CreateMenu()
    {
        WallStyle style = Create();
        if (style == null) return;
        Selection.activeObject = style;
        EditorGUIUtility.PingObject(style);
    }

    public static WallStyle Create()
    {
        EnsureFolder(MaterialFolder);

        // Walls: mirrored tiling hides the seams of textures that weren't made to tile.
        var walls = Textures(WallTextures, "").Select(t =>
        {
            Configure(t, TextureWrapMode.Mirror, alpha: false);
            return WallMaterial(t);
        }).ToArray();

        // Decals: keep their true proportions (no power-of-two rescale) and clean transparent edges.
        Material[] Decals(string kind) => Textures(DecalTextures, "decal_" + kind).Select(t =>
        {
            Configure(t, TextureWrapMode.Clamp, alpha: true);
            return DecalMaterial(t);
        }).ToArray();

        Material[] bottom = Decals("bottom"), mid = Decals("mid"), top = Decals("top");

        if (walls.Length == 0)
        {
            EditorUtility.DisplayDialog("Wall Style", $"No wall textures found in {WallTextures}.", "OK");
            return null;
        }

        var style = AssetDatabase.LoadAssetAtPath<WallStyle>(StylePath);
        if (style == null)
        {
            style = ScriptableObject.CreateInstance<WallStyle>();
            AssetDatabase.CreateAsset(style, StylePath);
        }
        Material seamless = SeamlessMaterial(walls.Select(m => (Texture2D)m.GetTexture("_BaseMap")).ToArray(), style.TextureSize);
        style.EditorSetup(seamless, walls, bottom, mid, top);
        EditorUtility.SetDirty(style);
        AssetDatabase.SaveAssets();
        Debug.Log($"Wall style: {walls.Length} wall variants, {bottom.Length} bottom / {mid.Length} mid / {top.Length} top decals.");
        return style;
    }

    // ------------------------------------------------------------------ textures

    private static List<Texture2D> Textures(string folder, string namePrefix)
    {
        if (!AssetDatabase.IsValidFolder(folder)) return new List<Texture2D>();
        return AssetDatabase.FindAssets("t:Texture2D", new[] { folder })
            .Select(AssetDatabase.GUIDToAssetPath)
            .Where(p => Path.GetFileName(p).StartsWith(namePrefix))
            .OrderBy(p => p)
            .Select(AssetDatabase.LoadAssetAtPath<Texture2D>)
            .Where(t => t != null)
            .ToList();
    }

    private static void Configure(Texture2D tex, TextureWrapMode wrap, bool alpha)
    {
        string path = AssetDatabase.GetAssetPath(tex);
        var imp = (TextureImporter)AssetImporter.GetAtPath(path);
        bool changed = imp.wrapMode != wrap || imp.alphaIsTransparency != alpha ||
                       (alpha && imp.npotScale != TextureImporterNPOTScale.None) || imp.maxTextureSize != 1024;
        if (!changed) return;
        imp.wrapMode = wrap;
        imp.alphaIsTransparency = alpha;          // bleeds edge colour into transparent pixels: no dark halos
        if (alpha) imp.npotScale = TextureImporterNPOTScale.None; // decal width/height ratio matters
        imp.mipmapEnabled = true;
        imp.filterMode = FilterMode.Bilinear;     // the 240p render pixelates it anyway
        imp.maxTextureSize = 1024;
        imp.SaveAndReimport();
    }

    // ------------------------------------------------------------------ materials

    private static Shader Lit => Shader.Find("Universal Render Pipeline/Lit");

    private static Material GetOrCreate(string name)
    {
        string path = $"{MaterialFolder}/{name}.mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat == null)
        {
            mat = new Material(Lit) { name = name };
            AssetDatabase.CreateAsset(mat, path);
        }
        return mat;
    }

    /// <summary>One material that blends up to 3 wall textures in world space (Retro/Medina Wall).</summary>
    private static Material SeamlessMaterial(Texture2D[] textures, float textureSize)
    {
        Shader shader = Shader.Find("Retro/Medina Wall");
        if (shader == null || textures.Length == 0) return null;
        Material mat = GetOrCreate("Wall_Seamless");
        mat.shader = shader;
        string[] slots = { "_TexA", "_TexB", "_TexC" };
        for (int i = 0; i < slots.Length; i++) mat.SetTexture(slots[i], textures[i % textures.Length]);
        mat.SetFloat("_TextureSize", textureSize);
        EditorUtility.SetDirty(mat);
        return mat;
    }

    private static Material WallMaterial(Texture2D tex)
    {
        Material mat = GetOrCreate("Wall_" + tex.name);
        mat.shader = Lit;
        mat.SetTexture("_BaseMap", tex);
        mat.SetTexture("_MainTex", tex);
        mat.SetColor("_BaseColor", Color.white);
        mat.SetFloat("_Smoothness", 0.05f);   // dry plaster
        mat.SetFloat("_Metallic", 0f);
        EditorUtility.SetDirty(mat);
        return mat;
    }

    /// <summary>URP Lit, transparent alpha-blend, no shadows / depth (sits just in front of the wall).</summary>
    private static Material DecalMaterial(Texture2D tex)
    {
        Material mat = GetOrCreate("Decal_" + tex.name);
        mat.shader = Lit;
        mat.SetTexture("_BaseMap", tex);
        mat.SetTexture("_MainTex", tex);
        mat.SetColor("_BaseColor", Color.white);
        mat.SetFloat("_Smoothness", 0.05f);
        mat.SetFloat("_Metallic", 0f);

        mat.SetFloat("_Surface", 1f);   // Transparent
        mat.SetFloat("_Blend", 0f);     // Alpha
        mat.SetFloat("_AlphaClip", 0f);
        // URP's default "preserve specular" mode premultiplies alpha but still draws reflections / highlights
        // on fully transparent pixels, which shows the decal's rectangle. Plain alpha blending, no specular.
        mat.SetFloat("_BlendModePreserveSpecular", 0f);
        mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        mat.DisableKeyword("_ALPHAMODULATE_ON");
        mat.SetFloat("_SpecularHighlights", 0f);
        mat.EnableKeyword("_SPECULARHIGHLIGHTS_OFF");
        mat.SetFloat("_EnvironmentReflections", 0f);
        mat.EnableKeyword("_ENVIRONMENTREFLECTIONS_OFF");
        mat.SetFloat("_Smoothness", 0f);
        mat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
        mat.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
        mat.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
        mat.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
        mat.SetFloat("_ZWrite", 0f);
        mat.SetFloat("_CastShadows", 0f);
        mat.SetOverrideTag("RenderType", "Transparent");
        mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        mat.DisableKeyword("_ALPHATEST_ON");
        mat.renderQueue = (int)RenderQueue.Transparent;
        mat.SetShaderPassEnabled("DepthOnly", false);
        mat.SetShaderPassEnabled("ShadowCaster", false);
        EditorUtility.SetDirty(mat);
        return mat;
    }

    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = Path.GetDirectoryName(path).Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
    }
}
