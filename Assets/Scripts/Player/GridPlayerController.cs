using System;
using PrimeTween;
using UnityEngine;

/// <summary>
/// Character: grid-locked first-person mover (Etrian Odyssey style).
/// One command = one cell step or one 90° turn, animated with PrimeTween.
/// </summary>
[RequireComponent(typeof(DungeonInputReader))]
public class GridPlayerController : MonoBehaviour
{
    [Header("Spawn")]
    [SerializeField] private Vector2Int startCell = Vector2Int.zero;
    [SerializeField] private Facing startFacing = Facing.North;

    [Header("Step")]
    [SerializeField, Min(0.01f)] private float stepDuration = 0.18f;
    [SerializeField] private Ease stepEase = Ease.OutCubic;

    [Header("Turn")]
    [SerializeField, Min(0.01f)] private float turnDuration = 0.14f;
    [SerializeField] private Ease turnEase = Ease.OutCubic;

    [Header("Collision")]
    [Tooltip("When a level is loaded its tile matrix decides walkability. This physics probe is an extra check " +
             "for props/obstacles placed in the scene. Turn off to trust the level data only.")]
    [SerializeField] private bool usePhysicsProbe = true;
    [Tooltip("Layers that block movement (walls, pillars, closed doors).")]
    [SerializeField] private LayerMask obstacleMask = ~0;
    [Tooltip("Height above the floor at which the wall probe is cast.")]
    [SerializeField] private float probeHeight = 1.25f;
    [Tooltip("Time the player is locked after bumping into a wall, so holding a key doesn't spam bumps.")]
    [SerializeField, Min(0f)] private float bumpLockDuration = 0.25f;

    /// <summary>Raised when a step starts: world direction, duration.</summary>
    public event Action<Vector3, float> Stepped;
    /// <summary>Raised when a turn starts: +1 clockwise (right), -1 counter-clockwise (left), duration.</summary>
    public event Action<int, float> Turned;
    /// <summary>Raised when a step is blocked: world direction of the attempted step.</summary>
    public event Action<Vector3> Bumped;

    public Vector2Int Cell { get; private set; }
    public Facing Facing { get; private set; }
    public bool IsBusy => _motion.isAlive;

    private DungeonInputReader _input;
    private Tween _motion;
    private LevelData _level;

    private void Awake()
    {
        _input = GetComponent<DungeonInputReader>();
        Teleport(startCell, startFacing);
    }

    private void OnDisable() => _motion.Complete();

    private void Update()
    {
        if (IsBusy) return;

        switch (_input.Consume())
        {
            case GridCommand.MoveForward: TryStep(Facing); break;
            case GridCommand.MoveBack: TryStep(Facing.Rotate(2)); break;
            case GridCommand.StrafeLeft: TryStep(Facing.Rotate(-1)); break;
            case GridCommand.StrafeRight: TryStep(Facing.Rotate(1)); break;
            case GridCommand.TurnLeft: Turn(-1); break;
            case GridCommand.TurnRight: Turn(1); break;
        }
    }

    /// <summary>Instantly place the player on a cell with a facing (spawns, stairs, warps).</summary>
    public void Teleport(Vector2Int cell, Facing facing)
    {
        _motion.Stop();
        Cell = cell;
        Facing = facing;
        transform.SetPositionAndRotation(DungeonGrid.CellToWorld(cell), facing.ToRotation());
    }

    /// <summary>Use this level's tile matrix for walkability (set by <see cref="LevelBuilder"/>).</summary>
    public void SetLevel(LevelData level) => _level = level;

    /// <summary>True if nothing blocks a step from the current cell in the given direction.</summary>
    public bool CanStep(Facing direction)
    {
        if (_level != null && !_level.IsWalkable(Cell + direction.ToOffset())) return false;
        if (!usePhysicsProbe) return true;

        Vector3 origin = DungeonGrid.CellToWorld(Cell, probeHeight);
        return !Physics.Raycast(origin, direction.ToWorldDirection(), DungeonGrid.CellSize,
            obstacleMask, QueryTriggerInteraction.Ignore);
    }

    private void TryStep(Facing direction)
    {
        Vector3 worldDir = direction.ToWorldDirection();

        if (!CanStep(direction))
        {
            Bumped?.Invoke(worldDir);
            if (bumpLockDuration > 0f) _motion = Tween.Delay(bumpLockDuration);
            return;
        }

        Cell += direction.ToOffset();
        _motion = Tween.Position(transform, DungeonGrid.CellToWorld(Cell), stepDuration, stepEase);
        Stepped?.Invoke(worldDir, stepDuration);
    }

    private void Turn(int sign)
    {
        Facing = Facing.Rotate(sign);
        _motion = Tween.Rotation(transform, Facing.ToRotation(), turnDuration, turnEase);
        Turned?.Invoke(sign, turnDuration);
    }

    private void OnDrawGizmos()
    {
        Vector2Int cell = Application.isPlaying ? Cell : startCell;
        Facing facing = Application.isPlaying ? Facing : startFacing;
        Vector3 center = DungeonGrid.CellToWorld(cell, 0.02f);

        Gizmos.color = new Color(0.2f, 1f, 0.4f, 0.8f);
        Gizmos.DrawWireCube(center, new Vector3(DungeonGrid.CellSize, 0f, DungeonGrid.CellSize));
        Gizmos.DrawLine(center, center + facing.ToWorldDirection() * DungeonGrid.CellSize * 0.45f);
    }
}
