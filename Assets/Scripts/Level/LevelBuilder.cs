using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
#endif

/// <summary>
/// Turns a <see cref="LevelData"/> asset into scene geometry and places the player on its start cell.
/// Geometry follows the level's warped lattice: open cells become irregular ground quads, solid cells become
/// buildings extruded to their (randomized) height, open sky above. Keep this object at the world origin.
/// </summary>
public class LevelBuilder : MonoBehaviour
{
    private const string RootName = "[Generated Level]";
    private static readonly Color RockColor = new Color(0.78f, 0.66f, 0.52f); // sun-baked plaster
    private static readonly Color RoofColor = new Color(0.55f, 0.40f, 0.28f);
    private static readonly Color BeamColor = new Color(0.45f, 0.27f, 0.14f); // old wood (warm enough to survive the cool sky light)
    private static readonly Color GroundFallbackColor = new Color(0.72f, 0.60f, 0.46f);

    [SerializeField] private LevelData level;
    [Tooltip("Player to place on the level's start cell. Found automatically if empty.")]
    [SerializeField] private GridPlayerController player;
    [Tooltip("Regenerate geometry when entering Play mode so the scene always matches the asset.")]
    [SerializeField] private bool rebuildOnPlay = true;
    [Tooltip("Editor only: rebuild the scene preview whenever the level asset is edited.")]
    [SerializeField] private bool livePreview = true;

    public LevelData Level => level;
    public bool LivePreview => livePreview;

    private static Material _fallbackMaterial;

    private void Start()
    {
        if (level == null)
        {
            Debug.LogWarning($"{name}: no LevelData assigned.", this);
            return;
        }

        if (rebuildOnPlay) Build();

        if (player == null) player = FindAnyObjectByType<GridPlayerController>();
        if (player != null)
        {
            player.SetLevel(level);
            player.Teleport(level.PlayerStart, level.StartFacing);
        }
    }

    // ------------------------------------------------------------------ build

    /// <summary>Delete and regenerate all level geometry.</summary>
    [ContextMenu("Build")]
    public void Build()
    {
        Clear();
        if (level == null) return;

        var root = new GameObject(RootName).transform;
        root.SetParent(transform, false);

        // One mesh per tile type keeps draw calls low and lets each type get its own color/material.
        var ground = new Dictionary<TileDefinition, MeshBuilder>();
        var walls = new Dictionary<TileDefinition, MeshBuilder>();
        MeshBuilder rockWalls = new MeshBuilder();
        MeshBuilder roofs = new MeshBuilder();
        var fakeWalls = new Dictionary<TileDefinition, MeshBuilder>();
        MeshBuilder hiddenGround = new MeshBuilder(); // ground inside fake walls

        // Textured walls: one mesh per wall variant (one draw call each), plus one per decal material.
        WallStyle style = level.WallStyle;
        bool styled = style != null && style.HasWalls;
        bool seamless = styled && style.SeamlessMaterial != null;
        // Seamless walls are one mesh / one material: the shader does the variation in world space.
        int variants = !styled ? 0 : seamless ? 1 : style.WallVariants.Length;
        var styledWalls = new Dictionary<int, MeshBuilder>();
        var styledFakes = new Dictionary<(TileDefinition, int), MeshBuilder>();
        var decals = new Dictionary<Material, MeshBuilder>();
        MeshBuilder beams = new MeshBuilder(); // wooden beams under covered passages

        var q = new Vector3[4];

        // One cell past every edge so open cells on the border are still enclosed.
        for (int y = -1; y <= level.Height; y++)
        for (int x = -1; x <= level.Width; x++)
        {
            var c = new Vector2Int(x, y);
            TileDefinition tile = level.GetTile(c);
            level.CellCorners(c, q);

            if (level.IsOpen(c))
            {
                if (tile.HasFloor) Get(ground, tile).Floor(q, 0f, up: true);
                if (tile.Covered)
                {
                    if (styled)
                    {
                        // Sabat: a room built over the alley, in the same walls as the houses around it.
                        var srng = new System.Random(CellSeed(c));
                        int variant = srng.Next(variants);
                        AddSabat(c, q, Get(styledWalls, variant), srng);
                    }
                    else
                    {
                        roofs.Floor(q, level.CoveredHeight, up: false);
                        roofs.Floor(q, level.CoveredHeight + 0.15f, up: true);
                    }
                    AddBeams(c, q, beams);
                }
            }
            else if (IsExtruded(c))
            {
                bool fake = tile != null && tile.IsFakeWall;
                var rng = new System.Random(CellSeed(c));
                int variant = styled ? rng.Next(variants) : 0;
                Vector2 uvOffset = new Vector2((float)rng.NextDouble(), (float)rng.NextDouble());
                MeshBuilder mb = styled
                    ? (fake ? Get(styledFakes, (tile, variant)) : Get(styledWalls, variant))
                    : fake ? Get(fakeWalls, tile) : tile != null ? Get(walls, tile) : rockWalls;
                if (fake) hiddenGround.Floor(q, 0f, up: true);
                float h = level.BuildingHeight(c);

                // Sides: only the part that sticks out above the neighbor (open ground = 0).
                for (int side = 0; side < 4; side++)
                {
                    Vector2Int n = c + ((Facing)side).ToOffset();
                    // Fake walls count as open space for their neighbours, so walking inside one shows
                    // full walls around you instead of a gap.
                    bool nOpen = level.IsOpen(n) || level.IsFakeWall(n);
                    float bottom = nOpen ? 0f : (IsExtruded(n) ? level.BuildingHeight(n) : 0f);
                    if (bottom >= h) continue;
                    // Edge corners in CCW order so the face points outward.
                    (Vector3 a, Vector3 b) = side switch
                    {
                        0 => (q[2], q[3]), // North: NE -> NW
                        1 => (q[1], q[2]), // East:  SE -> NE
                        2 => (q[0], q[1]), // South: SW -> SE
                        _ => (q[3], q[0]), // West:  NW -> SW
                    };
                    if (styled)
                    {
                        // Each face gets its own offset / mirror, so repeats never line up between faces.
                        Vector2 faceOffset = uvOffset + new Vector2(side * 0.37f, side * 0.21f);
                        mb.Wall(a, b, bottom, h, faceOffset, style.TextureSize, rng.NextDouble() < 0.5);
                    }
                    else mb.Wall(a, b, bottom, h);

                    // Decals only on walls that face a street (that's where the player sees them).
                    if (style != null && level.IsOpen(n)) AddDecals(style, decals, a, b, h, rng);
                }
                mb.Floor(q, h, up: true); // flat roof
            }

            if (tile != null && tile.Prefab != null && level.InBounds(c))
                SpawnProp(tile, c, root);
            else if (tile != null && tile.Behavior == TileDefinition.TileBehavior.Objective && level.InBounds(c))
                SpawnObjectivePlaceholder(tile, c, root);
        }

        foreach (var kv in ground) Emit(root, $"Ground - {kv.Key.Label}", kv.Value, level.GroundMaterial, kv.Key.EditorColor);
        foreach (var kv in walls) Emit(root, $"Buildings - {kv.Key.Label}", kv.Value, level.WallMaterial, kv.Key.EditorColor);
        Emit(root, "Buildings - Rock", rockWalls, level.WallMaterial, RockColor);
        Emit(root, "Covered Roofs", roofs, level.WallMaterial, RoofColor);
        Emit(root, "Covered Beams", beams, null, BeamColor, collider: false, matte: true);
        Emit(root, "Ground - Under Fake Walls", hiddenGround, level.GroundMaterial, GroundFallbackColor);
        foreach (var kv in styledWalls)
        {
            Material m = seamless ? style.SeamlessMaterial : style.WallVariants[kv.Key];
            Emit(root, $"Buildings - {m.name}", kv.Value, m, Color.white);
        }
        foreach (var kv in styledFakes)
        {
            Material m = seamless ? style.SeamlessMaterial : style.WallVariants[kv.Key.Item2];
            Emit(root, $"Fake Walls - {kv.Key.Item1.Label} - {m.name}", kv.Value, m, kv.Key.Item1.DisguiseTint,
                collider: false, alwaysTint: true);
        }
        foreach (var kv in decals)
            Emit(root, $"Decals - {kv.Key.name}", kv.Value, kv.Key, Color.white, collider: false, shadows: false);
        foreach (var kv in fakeWalls)
        {
            // Rendered like the wall it imitates, slightly tinted, and with no collider.
            TileDefinition disguise = kv.Key.DisguiseAs;
            Color look = (disguise != null ? disguise.EditorColor : RockColor) * kv.Key.DisguiseTint;
            if (level.WallMaterial != null) look = kv.Key.DisguiseTint; // tint the texture only
            Emit(root, $"Fake Walls - {kv.Key.Label}", kv.Value, level.WallMaterial, look, collider: false, alwaysTint: true);
        }

#if UNITY_EDITOR
        if (!Application.isPlaying)
        {
            // Show the spawn in the Scene view too, so designers see where the player starts.
            if (player == null) player = FindAnyObjectByType<GridPlayerController>();
            if (player != null)
                player.transform.SetPositionAndRotation(
                    level.CellCenter(level.PlayerStart), level.FacingRotation(level.PlayerStart, level.StartFacing));
            EditorSceneManager.MarkSceneDirty(gameObject.scene);
        }
#endif
    }

    /// <summary>Delete all generated geometry (and the meshes it owns).</summary>
    [ContextMenu("Clear")]
    public void Clear()
    {
        for (int i = transform.childCount - 1; i >= 0; i--)
        {
            Transform child = transform.GetChild(i);
            if (child.name != RootName) continue;

            foreach (MeshFilter mf in child.GetComponentsInChildren<MeshFilter>(true))
            {
                Mesh m = mf.sharedMesh;
                if (m != null && m.name.StartsWith("Generated")) DestroySafe(m);
            }
            DestroySafe(child.gameObject);
        }
    }

    /// <summary>Solid cells inside the level are always buildings; outside, only the ring that walls in open cells.</summary>
    // ------------------------------------------------------------------ covered passages (sabat)

    private bool IsCovered(Vector2Int c)
    {
        TileDefinition t = level.GetTile(c);
        return t != null && t.IsOpen && t.Covered;
    }

    /// <summary>Top of the room built over a covered cell: house height, but always at least a storey above the ceiling.</summary>
    private float SabatTop(Vector2Int c) => Mathf.Max(level.BuildingHeight(c), level.CoveredHeight + DungeonGrid.CellSize);

    /// <summary>Block from the passage ceiling up to SabatTop, with faces only where they can be seen.</summary>
    private void AddSabat(Vector2Int c, Vector3[] q, MeshBuilder mb, System.Random rng)
    {
        float ceiling = level.CoveredHeight;
        float top = SabatTop(c);
        WallStyle style = level.WallStyle;

        mb.Floor(q, ceiling, up: false); // the passage's ceiling
        mb.Floor(q, top, up: true);      // its roof

        for (int side = 0; side < 4; side++)
        {
            Vector2Int n = c + ((Facing)side).ToOffset();
            float neighbourTop = IsCovered(n) ? SabatTop(n)
                : level.IsOpen(n) ? 0f
                : IsExtruded(n) ? level.BuildingHeight(n) : 0f;
            float bottom = Mathf.Max(ceiling, neighbourTop);
            if (bottom >= top) continue;
            (Vector3 a, Vector3 b) = side switch
            {
                0 => (q[2], q[3]),
                1 => (q[1], q[2]),
                2 => (q[0], q[1]),
                _ => (q[3], q[0]),
            };
            var offset = new Vector2((float)rng.NextDouble(), (float)rng.NextDouble());
            mb.Wall(a, b, bottom, top, offset, style.TextureSize, rng.NextDouble() < 0.5);
        }
    }

    /// <summary>Three wooden beams across the passage, just under its ceiling.</summary>
    private void AddBeams(Vector2Int c, Vector3[] q, MeshBuilder mb)
    {
        // Beams span across the alley: if it runs north-south they go west-east, and vice versa.
        bool northSouth = level.IsWalkable(c + Vector2Int.up) || level.IsWalkable(c + Vector2Int.down);
        bool eastWest = level.IsWalkable(c + Vector2Int.right) || level.IsWalkable(c + Vector2Int.left);
        bool spanWestEast = northSouth || !eastWest;

        // Start/end edges of each beam (two opposite cell edges), and how far along the alley each sits.
        (Vector3 a0, Vector3 a1, Vector3 b0, Vector3 b1) = spanWestEast
            ? (q[0], q[3], q[1], q[2])   // west edge SW->NW, east edge SE->NE
            : (q[0], q[1], q[3], q[2]);  // south edge SW->SE, north edge NW->NE
        float y = level.CoveredHeight - 0.02f;
        foreach (float t in new[] { 0.18f, 0.5f, 0.82f })
            mb.Box(Vector3.Lerp(a0, a1, t), Vector3.Lerp(b0, b1, t), y, 0.16f, 0.2f);
    }

    private bool IsExtruded(Vector2Int c)
    {
        if (level.IsOpen(c)) return false;
        if (level.InBounds(c)) return true;
        for (int dy = -1; dy <= 1; dy++)
        for (int dx = -1; dx <= 1; dx++)
            if (level.IsOpen(c + new Vector2Int(dx, dy))) return true;
        return false;
    }

    private static MeshBuilder Get<TKey>(Dictionary<TKey, MeshBuilder> map, TKey key)
    {
        if (!map.TryGetValue(key, out MeshBuilder mb)) map[key] = mb = new MeshBuilder();
        return mb;
    }

    /// <summary>Repeatable per-cell random seed (same level + seed = same look every build).</summary>
    private int CellSeed(Vector2Int c)
    {
        unchecked { return (c.x * 73856093) ^ (c.y * 19349663) ^ (level.Seed * 83492791); }
    }

    // ------------------------------------------------------------------ decals

    /// <summary>Bottom (on the ground), top (from the roofline) and mid (on the body) decals for one wall face.</summary>
    private static void AddDecals(WallStyle style, Dictionary<Material, MeshBuilder> decals,
        Vector3 a, Vector3 b, float wallHeight, System.Random rng)
    {
        Vector3 along = b - a;
        float len = along.magnitude;
        if (len < 0.1f) return;
        along /= len;
        Vector3 normal = Vector3.Cross(Vector3.up, along); // outward, for counter-clockwise edges
        float maxWidth = len * style.MaxWidthOfWall;

        float bottomTop = 0f, topBottom = wallHeight;

        if (Roll(rng, style.BottomChance, style.BottomDecals))
        {
            float hgt = Mathf.Min(style.BottomHeight, wallHeight);
            if (PlaceDecal(style.BottomDecals, decals, a, along, normal, len, maxWidth, 0f, hgt, style.Offset, rng, out float top))
                bottomTop = top;
        }
        if (Roll(rng, style.TopChance, style.TopDecals))
        {
            float hgt = Mathf.Min(style.TopHeight, wallHeight * 0.5f);
            if (PlaceDecal(style.TopDecals, decals, a, along, normal, len, maxWidth, wallHeight - hgt, hgt, style.Offset * 1.5f, rng, out _))
                topBottom = wallHeight - hgt;
        }
        if (Roll(rng, style.MidChance, style.MidDecals))
        {
            // Somewhere on the body, between the bottom and top decals when there's room.
            float size = style.MidSize;
            float lo = Mathf.Max(bottomTop * 0.6f, 0.4f), hi = Mathf.Min(topBottom, wallHeight) - size;
            if (hi > lo)
            {
                float y = Mathf.Lerp(lo, hi, (float)rng.NextDouble());
                PlaceDecal(style.MidDecals, decals, a, along, normal, len, maxWidth, y, size, style.Offset * 2f, rng, out _);
            }
        }
    }

    private static bool Roll(System.Random rng, float chance, Material[] set) =>
        set != null && set.Length > 0 && rng.NextDouble() < chance;

    private static bool PlaceDecal(Material[] set, Dictionary<Material, MeshBuilder> decals, Vector3 a, Vector3 along,
        Vector3 normal, float faceLength, float maxWidth, float y0, float height, float offset, System.Random rng, out float yTop)
    {
        yTop = y0;
        Material mat = set[rng.Next(set.Length)];
        if (mat == null) return false;

        // Keep the texture's proportions; shrink to fit the wall if needed.
        Texture tex = mat.mainTexture;
        float aspect = tex != null && tex.height > 0 ? (float)tex.width / tex.height : 1f;
        float w = height * aspect;
        if (w > maxWidth) { height *= maxWidth / w; w = maxWidth; }

        float centre = Mathf.Lerp(w * 0.5f, faceLength - w * 0.5f, (float)rng.NextDouble());
        Vector3 p0 = a + along * (centre - w * 0.5f) + normal * offset;
        Vector3 p1 = p0 + along * w;
        Get(decals, mat).Decal(p0, p1, y0, y0 + height, rng.NextDouble() < 0.5);
        yTop = y0 + height;
        return true;
    }

    private static void Emit(Transform root, string objectName, MeshBuilder mb, Material material, Color tint,
        bool collider = true, bool alwaysTint = false, bool shadows = true, bool matte = false)
    {
        if (mb.IsEmpty) return;

        var go = new GameObject(objectName);
        go.transform.SetParent(root, false);
        go.isStatic = true;

        Mesh mesh = mb.ToMesh("Generated " + objectName);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var renderer = go.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = material != null ? material : FallbackMaterial;
        if (!shadows) renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        if (collider) go.AddComponent<MeshCollider>().sharedMesh = mesh;
        if (material == null || alwaysTint)
        {
            var t = go.AddComponent<PlaceholderTint>();
            t.Matte = matte;
            t.Color = tint;
        }
    }

    private void SpawnProp(TileDefinition tile, Vector2Int c, Transform root)
    {
        GameObject go;
#if UNITY_EDITOR
        go = Application.isPlaying
            ? Instantiate(tile.Prefab, root)
            : (GameObject)PrefabUtility.InstantiatePrefab(tile.Prefab, root);
#else
        go = Instantiate(tile.Prefab, root);
#endif
        go.transform.SetPositionAndRotation(level.CellCenter(c), level.FacingRotation(c, Facing.North));
        go.name = $"{tile.Label} {c.x},{c.y}";
    }

    /// <summary>Stand-in landmark for an objective tile without a prefab: a gold pillar on a low plinth.</summary>
    private void SpawnObjectivePlaceholder(TileDefinition tile, Vector2Int c, Transform root)
    {
        var holder = new GameObject($"{tile.Label} {c.x},{c.y}").transform;
        holder.SetParent(root, false);
        holder.SetPositionAndRotation(level.CellCenter(c), level.FacingRotation(c, Facing.North));

        Part(PrimitiveType.Cube, holder, new Vector3(0f, 0.15f, 0f), new Vector3(1.4f, 0.3f, 1.4f), new Color(0.55f, 0.45f, 0.32f));
        Part(PrimitiveType.Cylinder, holder, new Vector3(0f, 1.1f, 0f), new Vector3(0.5f, 0.8f, 0.5f), tile.EditorColor);
        Part(PrimitiveType.Sphere, holder, new Vector3(0f, 2.05f, 0f), new Vector3(0.55f, 0.55f, 0.55f), tile.EditorColor);
    }

    private static void Part(PrimitiveType type, Transform parent, Vector3 localPos, Vector3 scale, Color color)
    {
        GameObject go = GameObject.CreatePrimitive(type);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        go.transform.localScale = scale;
        go.AddComponent<PlaceholderTint>().Color = color;
        go.isStatic = true;
    }

    private static Material FallbackMaterial
    {
        get
        {
            if (_fallbackMaterial != null) return _fallbackMaterial;
            // Borrow the pipeline's default lit material from a throwaway primitive.
            GameObject tmp = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _fallbackMaterial = tmp.GetComponent<Renderer>().sharedMaterial;
            DestroySafe(tmp);
            return _fallbackMaterial;
        }
    }

    private static void DestroySafe(Object o)
    {
        if (Application.isPlaying) Destroy(o);
        else DestroyImmediate(o);
    }

    // ------------------------------------------------------------------ gizmos

    private void OnDrawGizmosSelected()
    {
        if (level == null) return;
        Gizmos.color = new Color(1f, 0.85f, 0.2f, 0.6f);
        // Draw the warped lattice slightly above the ground.
        Vector3 lift = Vector3.up * 0.05f;
        for (int j = 0; j <= level.Height; j++)
        for (int i = 0; i <= level.Width; i++)
        {
            Vector3 p = level.Corner(i, j) + lift;
            if (i < level.Width) Gizmos.DrawLine(p, level.Corner(i + 1, j) + lift);
            if (j < level.Height) Gizmos.DrawLine(p, level.Corner(i, j + 1) + lift);
        }
    }

    // ------------------------------------------------------------------ mesh helper

    /// <summary>Accumulates flat-shaded quads (4 unshared vertices each).</summary>
    private class MeshBuilder
    {
        private readonly List<Vector3> _verts = new List<Vector3>();
        private readonly List<Vector2> _uvs = new List<Vector2>();
        private readonly List<int> _tris = new List<int>();

        public bool IsEmpty => _verts.Count == 0;

        /// <summary>Horizontal quad from corners in SW, SE, NE, NW order. up=false faces downward.</summary>
        public void Floor(Vector3[] c, float y, bool up)
        {
            Vector3 sw = c[0] + Vector3.up * y, se = c[1] + Vector3.up * y,
                    ne = c[2] + Vector3.up * y, nw = c[3] + Vector3.up * y;
            // Clockwise seen from above = facing up.
            if (up) Quad(sw, nw, ne, se, WorldUV(sw), WorldUV(nw), WorldUV(ne), WorldUV(se));
            else Quad(sw, se, ne, nw, WorldUV(sw), WorldUV(se), WorldUV(ne), WorldUV(nw));
        }

        /// <summary>Vertical face along edge a->b (CCW around the building) from y0 to y1, facing outward.</summary>
        public void Wall(Vector3 a, Vector3 b, float y0, float y1)
        {
            float cs = DungeonGrid.CellSize;
            float len = Vector3.Distance(a, b) / cs;
            // Offset u by world position so neighboring walls don't line up texture seams identically.
            float u0 = (a.x + a.z) / cs;
            Quad(a + Vector3.up * y0, a + Vector3.up * y1, b + Vector3.up * y1, b + Vector3.up * y0,
                new Vector2(u0, y0 / cs), new Vector2(u0, y1 / cs),
                new Vector2(u0 + len, y1 / cs), new Vector2(u0 + len, y0 / cs));
        }

        /// <summary>Textured wall face: UVs in meters / textureSize, shifted by offset, optionally mirrored.</summary>
        public void Wall(Vector3 a, Vector3 b, float y0, float y1, Vector2 offset, float textureSize, bool mirror)
        {
            float len = Vector3.Distance(a, b) / textureSize;
            float u0 = offset.x, u1 = offset.x + (mirror ? -len : len);
            float v0 = y0 / textureSize + offset.y, v1 = y1 / textureSize + offset.y;
            Quad(a + Vector3.up * y0, a + Vector3.up * y1, b + Vector3.up * y1, b + Vector3.up * y0,
                new Vector2(u0, v0), new Vector2(u0, v1), new Vector2(u1, v1), new Vector2(u1, v0));
        }

        /// <summary>A decal quad from p0 to p1 (along the wall), y0..y1, showing the whole texture once.</summary>
        /// <summary>A beam from a to b (horizontal), hanging down from y: width w, depth h. Faces point outward.</summary>
        public void Box(Vector3 a, Vector3 b, float y, float w, float h)
        {
            a.y = b.y = 0f;
            Vector3 dir = (b - a).normalized;
            Vector3 side = Vector3.Cross(Vector3.up, dir) * (w * 0.5f);
            Vector3 top = Vector3.up * y, bot = Vector3.up * (y - h);
            Vector3 a0 = a - side, a1 = a + side, b0 = b - side, b1 = b + side;
            Vector3 centre = (a + b) * 0.5f + Vector3.up * (y - h * 0.5f);

            Outward(a0 + bot, a1 + bot, b1 + bot, b0 + bot, centre); // bottom
            Outward(a0 + top, b0 + top, b1 + top, a1 + top, centre); // top
            Outward(a0 + bot, b0 + bot, b0 + top, a0 + top, centre); // side
            Outward(a1 + bot, a1 + top, b1 + top, b1 + bot, centre); // other side
            Outward(a0 + bot, a0 + top, a1 + top, a1 + bot, centre); // end a
            Outward(b0 + bot, b1 + bot, b1 + top, b0 + top, centre); // end b
        }

        /// <summary>Quad wound so it faces away from centre.</summary>
        private void Outward(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, Vector3 centre)
        {
            Vector3 normal = Vector3.Cross(p1 - p0, p2 - p0); // Unity front face = clockwise
            Vector3 mid = (p0 + p1 + p2 + p3) * 0.25f;
            Vector2 uv = Vector2.zero;
            if (Vector3.Dot(normal, mid - centre) > 0f) Quad(p0, p1, p2, p3, uv, uv, uv, uv);
            else Quad(p0, p3, p2, p1, uv, uv, uv, uv);
        }

        public void Decal(Vector3 p0, Vector3 p1, float y0, float y1, bool flip)
        {
            float u0 = flip ? 1f : 0f, u1 = flip ? 0f : 1f;
            Quad(p0 + Vector3.up * y0, p0 + Vector3.up * y1, p1 + Vector3.up * y1, p1 + Vector3.up * y0,
                new Vector2(u0, 0f), new Vector2(u0, 1f), new Vector2(u1, 1f), new Vector2(u1, 0f));
        }

        private void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector2 ua, Vector2 ub, Vector2 uc, Vector2 ud)
        {
            int i = _verts.Count;
            _verts.Add(a); _verts.Add(b); _verts.Add(c); _verts.Add(d);
            _uvs.Add(ua); _uvs.Add(ub); _uvs.Add(uc); _uvs.Add(ud);
            _tris.Add(i); _tris.Add(i + 1); _tris.Add(i + 2);
            _tris.Add(i); _tris.Add(i + 2); _tris.Add(i + 3);
        }

        private static Vector2 WorldUV(Vector3 p) => new Vector2(p.x, p.z) / DungeonGrid.CellSize;

        public Mesh ToMesh(string meshName)
        {
            var mesh = new Mesh { name = meshName };
            if (_verts.Count > 65000) mesh.indexFormat = IndexFormat.UInt32;
            mesh.SetVertices(_verts);
            mesh.SetUVs(0, _uvs);
            mesh.SetTriangles(_tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
