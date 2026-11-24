
using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;

public class GridDrawing : MonoBehaviour
{

    [Header("References")]
    public RawImage mapUI;
    public Texture2D gridMap;

    [Header("Grid Settings")]
    public int columns = 21;
    public int rows = 22;

    [Header("Path Settings")]
    public Color pathColor = Color.yellow;
    public int brushSize = 5;

    private Texture2D generatedTexture;
    private Color[] originalPixels;

    private List<Vector2Int> path = new List<Vector2Int>();

    void Start()
    {
        if (mapUI == null || gridMap == null)
        {
            Debug.LogError("Map UI or Grid Map is missing!");
            enabled = false;
            return;
        }

        generatedTexture = new Texture2D(
            gridMap.width,
            gridMap.height,
            TextureFormat.RGBA32,
            false
        );

        originalPixels = gridMap.GetPixels();

        generatedTexture.SetPixels(originalPixels);

        generatedTexture.filterMode = FilterMode.Point;
        generatedTexture.wrapMode = TextureWrapMode.Clamp;

        generatedTexture.Apply();

        // Display the generated texture directly in the UI
        mapUI.texture = generatedTexture;
    }

    void Update()
    {
        if (Input.GetMouseButton(0))
        {
            DetectCell();
        }

        if (Input.GetKeyDown(KeyCode.R))
        {
            ResetPath();
        }
    }


    void DetectCell()
    {
        Vector2 localPoint;

        RectTransform rectTransform = mapUI.rectTransform;

        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
            rectTransform,
            Input.mousePosition,
            null,
            out localPoint))
        {
            return;
        }

        Rect rect = rectTransform.rect;

        // Ignore clicks outside the map
        if (!rect.Contains(localPoint))
            return;

        // Convert mouse position to UV coordinates
        float u = (localPoint.x - rect.xMin) / rect.width;
        float v = (localPoint.y - rect.yMin) / rect.height;

        // Convert UV coordinates to grid coordinates
        float pixelX = u * 1060f;
        float pixelY = v * 1110f;

        int x = Mathf.FloorToInt((pixelX - 5f) / 50f);
        int y = Mathf.FloorToInt((pixelY - 5f) / 50f);

        if (x < 0 || x >= columns ||
            y < 0 || y >= rows)
            return;

        AddCell(new Vector2Int(x, y));
    }


    void AddCell(Vector2Int cell)
    {
        // First cell of the path
        if (path.Count == 0)
        {
            path.Add(cell);
            DrawPoint(GetCellCenter(cell));
            generatedTexture.Apply();
            return;
        }

        Vector2Int lastCell = path[path.Count - 1];

        // Ignore the current cell
        if (cell == lastCell)
            return;

        // Only allow horizontal and vertical neighbors
        int distance =
            Mathf.Abs(cell.x - lastCell.x) +
            Mathf.Abs(cell.y - lastCell.y);

        if (distance != 1)
            return;

        // Prevent visiting the same cell twice
        if (path.Contains(cell))
            return;

        path.Add(cell);

        DrawLine(
            GetCellCenter(lastCell),
            GetCellCenter(cell)
        );

        generatedTexture.Apply();

        Debug.Log("Cell: " + cell);
    }

    Vector2Int GetCellCenter(Vector2Int cell)
    {
        float u = (30f + cell.x * 50f) / 1060f;
        float v = (30f + cell.y * 50f) / 1110f;

        int x = Mathf.RoundToInt(u * generatedTexture.width);
        int y = Mathf.RoundToInt(v * generatedTexture.height);

        return new Vector2Int(x, y);
    }

    void DrawLine(Vector2Int start, Vector2Int end)
    {
        int distance = Mathf.Max(
            Mathf.Abs(end.x - start.x),
            Mathf.Abs(end.y - start.y)
        );

        for (int i = 0; i <= distance; i++)
        {
            float t = distance == 0 ? 0 : (float)i / distance;

            int x = Mathf.RoundToInt(
                Mathf.Lerp(start.x, end.x, t)
            );

            int y = Mathf.RoundToInt(
                Mathf.Lerp(start.y, end.y, t)
            );

            DrawPoint(new Vector2Int(x, y));
        }
    }

    void DrawPoint(Vector2Int point)
    {
        for (int x = -brushSize; x <= brushSize; x++)
        {
            for (int y = -brushSize; y <= brushSize; y++)
            {
                if (x * x + y * y > brushSize * brushSize)
                    continue;

                int px = point.x + x;
                int py = point.y + y;

                if (px >= 0 && px < generatedTexture.width &&
                    py >= 0 && py < generatedTexture.height)
                {
                    generatedTexture.SetPixel(px, py, pathColor);
                }
            }
        }
    }

    public void ResetPath()
    {
        path.Clear();

        generatedTexture.SetPixels(originalPixels);
        generatedTexture.Apply();
    }
}
