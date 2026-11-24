using UnityEngine;

/// <summary>Cardinal facing on the dungeon grid. Values are ordered clockwise so (int)facing * 90 = yaw.</summary>
public enum Facing
{
    North = 0, // +Z
    East = 1,  // +X
    South = 2, // -Z
    West = 3,  // -X
}

/// <summary>Grid constants and conversions between grid cells and world space.</summary>
public static class DungeonGrid
{
    /// <summary>Size of one grid cell in meters.</summary>
    public const float CellSize = 2.5f;

    /// <summary>World-space center of a cell (on the floor, y = 0 unless given).</summary>
    public static Vector3 CellToWorld(Vector2Int cell, float y = 0f) =>
        new Vector3(cell.x * CellSize, y, cell.y * CellSize);

    /// <summary>Cell containing a world position.</summary>
    public static Vector2Int WorldToCell(Vector3 world) =>
        new Vector2Int(Mathf.RoundToInt(world.x / CellSize), Mathf.RoundToInt(world.z / CellSize));

    /// <summary>Grid offset of one step in the given facing.</summary>
    public static Vector2Int ToOffset(this Facing facing) => facing switch
    {
        Facing.North => Vector2Int.up,
        Facing.East => Vector2Int.right,
        Facing.South => Vector2Int.down,
        _ => Vector2Int.left,
    };

    /// <summary>Facing rotated by a number of clockwise quarter turns (negative = counter-clockwise).</summary>
    public static Facing Rotate(this Facing facing, int quarterTurns) =>
        (Facing)((((int)facing + quarterTurns) % 4 + 4) % 4);

    /// <summary>World rotation that looks along the given facing.</summary>
    public static Quaternion ToRotation(this Facing facing) =>
        Quaternion.Euler(0f, (int)facing * 90f, 0f);

    /// <summary>World-space unit direction of the given facing.</summary>
    public static Vector3 ToWorldDirection(this Facing facing)
    {
        Vector2Int o = facing.ToOffset();
        return new Vector3(o.x, 0f, o.y);
    }
}
