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
                    roofs.Floor(q, level.CoveredHeight, up: false);
                    roofs.Floor(q, level.CoveredHeight + 0.15f, up: true);
                }
            }
            else if (IsExtruded(c))
            {
                bool fake = tile != null && tile.IsFakeWall;
                MeshBuilder mb = fake ? Get(fakeWalls, tile) : tile != null ? Get(walls, tile) : rockWalls;
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
                    mb.Wall(a, b, bottom, h);
                }
                mb.Floor(q, h, up: true); // flat roof
            }

            if (tile != null && tile.Prefab != null && level.InBounds(c))
                SpawnProp(tile, c, root);
        }

        foreach (var kv in ground) Emit(root, $"Ground - {kv.Key.Label}", kv.Value, level.GroundMaterial, kv.Key.EditorColor);
        foreach (var kv in walls) Emit(root, $"Buildings - {kv.Key.Label}", kv.Value, level.WallMaterial, kv.Key.EditorColor);
        Emit(root, "Buildings - Rock", rockWalls, level.WallMaterial, RockColor);
        Emit(root, "Covered Roofs", roofs, level.WallMaterial, RoofColor);
        Emit(root, "Ground - Under Fake Walls", hiddenGround, level.GroundMaterial, GroundFallbackColor);
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
    private bool IsExtruded(Vector2Int c)
    {
        if (level.IsOpen(c)) return false;
        if (level.InBounds(c)) return true;
        for (int dy = -1; dy <= 1; dy++)
        for (int dx = -1; dx <= 1; dx++)
            if (level.IsOpen(c + new Vector2Int(dx, dy))) return true;
        return false;
    }

    private static MeshBuilder Get(Dictionary<TileDefinition, MeshBuilder> map, TileDefinition tile)
    {
        if (!map.TryGetValue(tile, out MeshBuilder mb)) map[tile] = mb = new MeshBuilder();
        return mb;
    }

    private static void Emit(Transform root, string objectName, MeshBuilder mb, Material material, Color tint,
        bool collider = true, bool alwaysTint = false)
    {
        if (mb.IsEmpty) return;

        var go = new GameObject(objectName);
        go.transform.SetParent(root, false);
        go.isStatic = true;

        Mesh mesh = mb.ToMesh("Generated " + objectName);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        go.AddComponent<MeshRenderer>().sharedMaterial = material != null ? material : FallbackMaterial;
        if (collider) go.AddComponent<MeshCollider>().sharedMesh = mesh;
        if (material == null || alwaysTint) go.AddComponent<PlaceholderTint>().Color = tint;
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
