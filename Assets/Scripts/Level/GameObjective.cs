using System;
using UnityEngine;

/// <summary>
/// The level's goal (first version, subject to change):
///   1. Searching  - find an Objective tile and interact with it.
///   2. Returning  - make your way back to the starting cell (this is where your map earns its keep).
///   3. Complete   - you're back at the start.
/// Messages come from the LevelData (editable in the level painter). Lives next to the LevelBuilder.
/// </summary>
public class GameObjective : MonoBehaviour
{
    public enum Stage { Searching, Returning, Complete }

    [Tooltip("Found automatically if empty.")]
    [SerializeField] private LevelBuilder levelBuilder;
    [Tooltip("Found automatically if empty.")]
    [SerializeField] private GridPlayerController player;

    public Stage Current { get; private set; } = Stage.Searching;

    /// <summary>Raised when the stage changes.</summary>
    public event Action<Stage> StageChanged;

    private LevelData Level => levelBuilder != null ? levelBuilder.Level : null;

    private void Awake()
    {
        if (levelBuilder == null) levelBuilder = GetComponent<LevelBuilder>();
        if (levelBuilder == null) levelBuilder = FindAnyObjectByType<LevelBuilder>();
        if (player == null) player = FindAnyObjectByType<GridPlayerController>();
    }

    private void OnEnable() => PlayerInteractor.Interacted += OnInteracted;
    private void OnDisable() => PlayerInteractor.Interacted -= OnInteracted;

    private void OnInteracted(Vector2Int cell, TileDefinition tile)
    {
        if (tile.Behavior != TileDefinition.TileBehavior.Objective || Level == null) return;

        switch (Current)
        {
            case Stage.Searching:
                SetStage(Stage.Returning);
                Say(Capitalize(Level.ObjectiveName), Level.ObjectiveFoundText);
                break;
            case Stage.Returning:
                Say(Capitalize(Level.ObjectiveName), "You've already been here. Head back to where you started.");
                break;
            case Stage.Complete:
                Say(Capitalize(Level.ObjectiveName), Level.ObjectiveCompleteText);
                break;
        }
    }

    private void Update()
    {
        if (Current != Stage.Returning || player == null || Level == null) return;
        // Arrived (step finished) on the starting cell.
        if (!player.IsBusy && player.Cell == Level.PlayerStart)
        {
            SetStage(Stage.Complete);
            Say("Journey's End", Level.ObjectiveCompleteText);
        }
    }

    private void SetStage(Stage stage)
    {
        Current = stage;
        StageChanged?.Invoke(stage);
    }

    private static void Say(string title, string text)
    {
        if (MessageBox.Instance != null) MessageBox.Instance.Show(title, text);
        else Debug.Log($"[{title}] {text}");
    }

    private static string Capitalize(string s) =>
        string.IsNullOrEmpty(s) ? "Objective" : char.ToUpperInvariant(s[0]) + s.Substring(1);
}
