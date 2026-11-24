using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Tiny pixel-square particles for the map (Unity's ParticleSystem doesn't render in an overlay canvas).
/// A fixed pool of UI Images, moved and faded each frame, with positions snapped to the map's pixel grid.
/// </summary>
public class MapParticles : MonoBehaviour
{
    [SerializeField, Range(8, 256)] private int poolSize = 96;
    [Tooltip("Size of one 'pixel' in local UI units. Set by MapCanvas to match an ink pixel.")]
    [SerializeField] private float pixelSize = 3f;

    private struct Particle
    {
        public Vector2 pos, vel;
        public float life, maxLife, gravity;
        public int size; // in pixels
        public Color color;
    }

    private Particle[] _particles;
    private RectTransform[] _rects;
    private Image[] _images;
    private int _next;

    public float PixelSize
    {
        get => pixelSize;
        set => pixelSize = Mathf.Max(0.5f, value);
    }

    private void Awake()
    {
        _particles = new Particle[poolSize];
        _rects = new RectTransform[poolSize];
        _images = new Image[poolSize];
        for (int i = 0; i < poolSize; i++)
        {
            var go = new GameObject("p", typeof(RectTransform), typeof(Image));
            go.hideFlags = HideFlags.DontSave;
            go.layer = gameObject.layer;
            var rt = (RectTransform)go.transform;
            rt.SetParent(transform, false);
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            var img = go.GetComponent<Image>();
            img.raycastTarget = false;
            go.SetActive(false);
            _rects[i] = rt;
            _images[i] = img;
        }
    }

    /// <summary>Spawn one particle at a local position (relative to this rect's center).</summary>
    public void Emit(Vector2 localPos, Vector2 velocity, Color color, float life, float gravity, int sizePixels = 1)
    {
        if (_particles == null) return;
        int i = _next;
        _next = (_next + 1) % _particles.Length;
        _particles[i] = new Particle
        {
            pos = localPos, vel = velocity, life = life, maxLife = life,
            gravity = gravity, size = Mathf.Max(1, sizePixels), color = color,
        };
        // Place it right away, otherwise it shows for one frame at the default 100x100 size.
        Apply(i, 1f);
        _rects[i].gameObject.SetActive(true);
    }

    private void Update()
    {
        if (_particles == null) return;
        float dt = Time.unscaledDeltaTime;
        for (int i = 0; i < _particles.Length; i++)
        {
            ref Particle p = ref _particles[i];
            if (p.life <= 0f) continue;

            p.life -= dt;
            if (p.life <= 0f)
            {
                _rects[i].gameObject.SetActive(false);
                continue;
            }

            p.vel.y -= p.gravity * dt;
            p.vel *= 1f - Mathf.Clamp01(2.5f * dt); // a little drag
            p.pos += p.vel * dt;

            Apply(i, p.life / p.maxLife);
        }
    }

    /// <summary>Push particle i's state to its Image; t = remaining life 1..0.</summary>
    private void Apply(int i, float t)
    {
        Particle p = _particles[i];
        // Shrink to a single pixel near the end, fade out.
        int size = t < 0.4f ? 1 : p.size;
        Color c = p.color;
        c.a *= Mathf.Clamp01(t * 1.5f);

        // Snap to the pixel grid so it reads as pixel art, not smooth sprites.
        Vector2 snapped = new Vector2(Mathf.Round(p.pos.x / pixelSize), Mathf.Round(p.pos.y / pixelSize)) * pixelSize;
        _rects[i].anchoredPosition = snapped;
        _rects[i].sizeDelta = Vector2.one * (size * pixelSize);
        _images[i].color = c;
    }
}
