
using UnityEngine;

public class Draw : MonoBehaviour
{
    [Header("References")]
    public Camera cam;
    public MeshCollider drawingSurface;

    [Header("Canvas Settings")]
    public int totalXPixels = 1024;
    public int totalYPixels = 512;

    [Header("Brush Settings")]
    public int brushSize = 5;
    public Color brushColor = Color.black;
    public bool useInterpolation = true;

    private Texture2D generatedTexture;
    private Color[] colorMap;
    private Material drawingMaterial;

    private int xPixel;
    private int yPixel;

    private bool pressedLastFrame = false;
    private int lastX;
    private int lastY;

    void Start()
    {
        if (cam == null || drawingSurface == null)
        {
            Debug.LogError("Missing Camera or Drawing Surface!");
            enabled = false;
            return;
        }

        MeshRenderer meshRenderer =
            drawingSurface.GetComponent<MeshRenderer>();

        if (meshRenderer == null)
        {
            Debug.LogError("DrawingPlane needs a Mesh Renderer!");
            enabled = false;
            return;
        }

        drawingMaterial = meshRenderer.material;

        colorMap = new Color[totalXPixels * totalYPixels];

        generatedTexture = new Texture2D(
            totalXPixels,
            totalYPixels,
            TextureFormat.RGBA32,
            false
        );

        generatedTexture.filterMode = FilterMode.Bilinear;

        drawingMaterial.mainTexture = generatedTexture;

        ResetCanvas();

        Debug.Log("Drawing system initialized!");
    }

    void Update()
    {
        if (Input.GetMouseButton(0))
        {
            CalculatePixel();
        }
        else
        {
            pressedLastFrame = false;
        }

        // Press R to clear the canvas
        if (Input.GetKeyDown(KeyCode.R))
        {
            ResetCanvas();
        }
    }

    void CalculatePixel()
    {
        Ray ray = cam.ScreenPointToRay(Input.mousePosition);

        RaycastHit hit;

        if (Physics.Raycast(ray, out hit, 1000f))
        {
            if (hit.collider != drawingSurface)
            {
                pressedLastFrame = false;
                return;
            }

            // Get the UV coordinates of the mouse position
            Vector2 uv = hit.textureCoord;

            xPixel = Mathf.Clamp(
                Mathf.FloorToInt(uv.x * totalXPixels),
                0,
                totalXPixels - 1
            );

            yPixel = Mathf.Clamp(
                Mathf.FloorToInt(uv.y * totalYPixels),
                0,
                totalYPixels - 1
            );

            ChangePixelsAroundPoint();
        }
        else
        {
            pressedLastFrame = false;
        }
    }

    void ChangePixelsAroundPoint()
    {
        if (useInterpolation && pressedLastFrame)
        {
            int dist = Mathf.Max(1, Mathf.RoundToInt(
                Vector2.Distance(
                    new Vector2(xPixel, yPixel),
                    new Vector2(lastX, lastY)
                )
            ));

            for (int i = 1; i <= dist; i++)
            {
                int x = Mathf.RoundToInt(
                    Mathf.Lerp(lastX, xPixel, (float)i / dist)
                );

                int y = Mathf.RoundToInt(
                    Mathf.Lerp(lastY, yPixel, (float)i / dist)
                );

                DrawBrush(x, y);
            }
        }
        else
        {
            DrawBrush(xPixel, yPixel);
        }

        lastX = xPixel;
        lastY = yPixel;

        pressedLastFrame = true;

        SetTexture();
    }

    void DrawBrush(int xPix, int yPix)
    {
        int minX = Mathf.Max(0, xPix - brushSize);
        int maxX = Mathf.Min(totalXPixels - 1, xPix + brushSize);

        int minY = Mathf.Max(0, yPix - brushSize);
        int maxY = Mathf.Min(totalYPixels - 1, yPix + brushSize);

        for (int x = minX; x <= maxX; x++)
        {
            for (int y = minY; y <= maxY; y++)
            {
                int dx = x - xPix;
                int dy = y - yPix;

                if (dx * dx + dy * dy <= brushSize * brushSize)
                {
                    colorMap[y * totalXPixels + x] = brushColor;
                }
            }
        }
    }

    void SetTexture()
    {
        generatedTexture.SetPixels(colorMap);
        generatedTexture.Apply();
    }

    public void ResetCanvas()
    {
        for (int i = 0; i < colorMap.Length; i++)
        {
            colorMap[i] = Color.white;
        }

        SetTexture();
        pressedLastFrame = false;
    }
}
