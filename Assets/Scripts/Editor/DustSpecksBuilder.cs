using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Atmospheric dust motes around the player: sparse, tiny, warm specks drifting lazily in the air.
/// The emitter follows the player but particles live in world space, so they hang in the air and
/// slide past as you walk. Additive + fogged, so they glow softly against walls and melt into the haze.
/// Tweak everything afterwards on the "Dust Specks" particle system under the Player.
/// </summary>
public static class DustSpecksBuilder
{
    private const string TexturePath = "Assets/Textures/FX/dust_dot.png";
    private const string MaterialPath = "Assets/Materials/FX_DustSpeck.mat";

    [MenuItem("Tools/Dungeon/Create Dust Specks", priority = 65)]
    public static void Create()
    {
        GridPlayerController player = Object.FindAnyObjectByType<GridPlayerController>();
        if (player == null)
        {
            EditorUtility.DisplayDialog("Dust Specks", "Create the player rig first.", "OK");
            return;
        }

        Transform old = player.transform.Find("Dust Specks");
        if (old != null) Undo.DestroyObjectImmediate(old.gameObject);

        Material mat = CreateMaterial(CreateDotTexture());

        var go = new GameObject("Dust Specks", typeof(ParticleSystem));
        Undo.RegisterCreatedObjectUndo(go, "Create Dust Specks");
        go.transform.SetParent(player.transform, false);
        go.transform.localPosition = new Vector3(0f, 2.2f, 1.5f); // a bit ahead of the player, around eye level
        go.transform.localRotation = Quaternion.identity;

        var ps = go.GetComponent<ParticleSystem>();
        Configure(ps);
        var renderer = go.GetComponent<ParticleSystemRenderer>();
        renderer.sharedMaterial = mat;
        renderer.renderMode = ParticleSystemRenderMode.Billboard;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.minParticleSize = 0f;
        renderer.maxParticleSize = 0.05f; // never fill the screen when one drifts into the camera

        Selection.activeGameObject = go;
    }

    private static void Configure(ParticleSystem ps)
    {
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var main = ps.main;
        main.loop = true;
        main.prewarm = true;                                   // dust is already there on the first frame
        main.duration = 10f;
        main.startLifetime = new ParticleSystem.MinMaxCurve(7f, 12f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0f, 0.05f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.02f, 0.04f); // ~1-2 px at 240p, a few cells away
        main.startColor = new ParticleSystem.MinMaxGradient(
            new Color(1f, 0.86f, 0.62f, 0.75f),                // warm gold
            new Color(1f, 0.95f, 0.85f, 0.5f));                // pale cream
        main.gravityModifier = 0f;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.scalingMode = ParticleSystemScalingMode.Hierarchy;
        main.maxParticles = 180;
        main.playOnAwake = true;

        var emission = ps.emission;
        emission.rateOverTime = 15f;

        var shape = ps.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale = new Vector3(9f, 4f, 11f);               // a few cells around and ahead of the player

        // Lazy drift: a faint breeze plus slow turbulence.
        var vel = ps.velocityOverLifetime;
        vel.enabled = true;
        vel.space = ParticleSystemSimulationSpace.World;
        vel.x = new ParticleSystem.MinMaxCurve(0.02f, 0.08f);
        vel.y = new ParticleSystem.MinMaxCurve(-0.02f, 0.03f);
        vel.z = new ParticleSystem.MinMaxCurve(-0.03f, 0.03f);

        var noise = ps.noise;
        noise.enabled = true;
        noise.strength = 0.12f;
        noise.frequency = 0.25f;
        noise.scrollSpeed = 0.15f;
        noise.damping = true;
        noise.quality = ParticleSystemNoiseQuality.Medium;

        // Fade in, twinkle a little, fade out: nothing pops.
        var col = ps.colorOverLifetime;
        col.enabled = true;
        var g = new Gradient();
        g.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[]
            {
                new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.2f), new GradientAlphaKey(0.55f, 0.45f),
                new GradientAlphaKey(1f, 0.65f), new GradientAlphaKey(0f, 1f),
            });
        col.color = new ParticleSystem.MinMaxGradient(g);

        ps.Play();
    }

    // ------------------------------------------------------------------ assets

    /// <summary>16x16 soft round dot, so specks read as motes rather than squares when close.</summary>
    private static Texture2D CreateDotTexture()
    {
        if (!File.Exists(TexturePath))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(TexturePath));
            const int s = 16;
            var tex = new Texture2D(s, s, TextureFormat.RGBA32, false);
            var c = new Vector2(s * 0.5f - 0.5f, s * 0.5f - 0.5f);
            for (int y = 0; y < s; y++)
            for (int x = 0; x < s; x++)
            {
                float d = Vector2.Distance(new Vector2(x, y), c) / (s * 0.5f);
                float a = Mathf.Clamp01(1f - d);
                a = a * a * (3f - 2f * a);                     // smooth falloff
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
            }
            tex.Apply();
            File.WriteAllBytes(TexturePath, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(TexturePath);
            var imp = (TextureImporter)AssetImporter.GetAtPath(TexturePath);
            imp.alphaIsTransparency = true;
            imp.mipmapEnabled = false;
            imp.wrapMode = TextureWrapMode.Clamp;
            imp.SaveAndReimport();
        }
        return AssetDatabase.LoadAssetAtPath<Texture2D>(TexturePath);
    }

    /// <summary>URP Particles/Unlit, transparent, additive, fogged.</summary>
    private static Material CreateMaterial(Texture2D dot)
    {
        var mat = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
        if (mat == null)
        {
            mat = new Material(shader) { name = "FX_DustSpeck" };
            AssetDatabase.CreateAsset(mat, MaterialPath);
        }
        mat.shader = shader;
        mat.SetTexture("_BaseMap", dot);
        mat.SetColor("_BaseColor", Color.white);

        mat.SetFloat("_Surface", 1f);   // transparent
        mat.SetFloat("_Blend", 2f);     // additive
        mat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
        mat.SetFloat("_DstBlend", (float)BlendMode.One);
        mat.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
        mat.SetFloat("_DstBlendAlpha", (float)BlendMode.One);
        mat.SetFloat("_ZWrite", 0f);
        mat.SetOverrideTag("RenderType", "Transparent");
        mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        mat.DisableKeyword("_ALPHAMODULATE_ON");
        mat.renderQueue = (int)RenderQueue.Transparent;
        EditorUtility.SetDirty(mat);
        AssetDatabase.SaveAssets();
        return mat;
    }
}
