using System;
using PrimeTween;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// The level's goal (first version, subject to change):
///   1. Searching  - find an Objective tile and interact with it.
///      Finding it costs you: the live player marker goes dark and part of your map is smudged away.
///   2. Returning  - a beacon rises on the starting cell; get back there with what's left of your map.
///   3. Complete   - interact with the beacon (facing it or standing on it) to end the game.
/// Messages come from the LevelData (editable in the level painter). Lives next to the LevelBuilder.
/// </summary>
public class GameObjective : MonoBehaviour
{
    public enum Stage { Searching, Returning, Complete }

    [Tooltip("Found automatically if empty.")]
    [SerializeField] private LevelBuilder levelBuilder;
    [Tooltip("Found automatically if empty.")]
    [SerializeField] private GridPlayerController player;
    [Tooltip("Found automatically if empty.")]
    [SerializeField] private MapCanvas map;
    [Tooltip("Found automatically if empty.")]
    [SerializeField] private MapPlayerMarker playerMarker;
    [Tooltip("Found automatically if empty.")]
    [SerializeField] private MapStickers stickers;

    [Header("On Finding The Objective")]
    [Tooltip("Hide the live player marker on the map for the rest of the game.")]
    [SerializeField] private bool hidePlayerMarker = true;
    [Tooltip("Share of the map's inked cells (walls and pen strokes) that get wiped.")]
    [SerializeField, Range(0f, 1f)] private float eraseFraction = 0.35f;
    [Tooltip("Never wipe the outer ring of cells.")]
    [SerializeField] private bool keepBorder = true;
    [Tooltip("Seconds the wipe takes, cell after cell.")]
    [SerializeField, Min(0f)] private float eraseDuration = 1.2f;
    [Tooltip("Added after the level's 'found' text.")]
    [SerializeField, TextArea(2, 3)] private string mapDamageText =
        "But the way here has cost you: parts of your map are smudged, and you can no longer tell where you stand.";

    [Header("Landmark Beacons (stickers)")]
    [Tooltip("Spawned over every landmark that has a sticker on its map cell when the objective is found.")]
    [SerializeField] private GameObject landmarkBeaconPrefab;
    [Tooltip("Added to the 'found' message when beacons rise. {0} = how many.")]
    [SerializeField, TextArea(1, 3)] private string beaconsText =
        "Far away, {0} beam(s) of light rise over the landmarks you marked on your map.";

    [Header("Return Beacon")]
    [Tooltip("Spawned on the starting cell once the objective is found. Interact with it to end the game.")]
    [SerializeField] private GameObject returnBeaconPrefab;
    [SerializeField] private string beaconTitle = "The Way Home";
    [Tooltip("Interact pop-up shown when facing (or standing on) the return beacon.")]
    [SerializeField] private string beaconPrompt = "Head home";
    [Tooltip("Shown after the completion text closes. Interact again restarts the level.")]
    [SerializeField, TextArea(2, 3)] private string endText = "Thanks for playing. Press Interact to start again.";

    public Stage Current { get; private set; } = Stage.Searching;

    /// <summary>Raised when the stage changes.</summary>
    public event Action<Stage> StageChanged;

    private LevelData Level => levelBuilder != null ? levelBuilder.Level : null;
    private GameObject _beacon;

    private void Awake()
    {
        if (levelBuilder == null) levelBuilder = GetComponent<LevelBuilder>();
        if (levelBuilder == null) levelBuilder = FindAnyObjectByType<LevelBuilder>();
        if (player == null) player = FindAnyObjectByType<GridPlayerController>();
        if (map == null) map = FindAnyObjectByType<MapCanvas>();
        if (playerMarker == null) playerMarker = FindAnyObjectByType<MapPlayerMarker>();
        if (stickers == null) stickers = FindAnyObjectByType<MapStickers>();
    }

    private void OnEnable()
    {
        PlayerInteractor.Interacted += OnInteracted;
        PlayerInteractor.InteractPressed += OnInteractPressed;
        PlayerInteractor.PromptProviders.Add(BeaconPrompt);
    }

    /// <summary>Interact prompt for the return beacon (it isn't a level tile, so the interactor can't know it).</summary>
    private string BeaconPrompt(Vector2Int faced, Vector2Int standing) =>
        Current == Stage.Returning && AtBeacon(faced, standing) ? beaconPrompt : null;

    private void OnDisable()
    {
        PlayerInteractor.Interacted -= OnInteracted;
        PlayerInteractor.InteractPressed -= OnInteractPressed;
        PlayerInteractor.PromptProviders.Remove(BeaconPrompt);
        if (MessageBox.Instance != null) MessageBox.Instance.Closed -= OnCompleteMessageClosed;
        if (MessageBox.Instance != null) MessageBox.Instance.Closed -= OnEndMessageClosed;
        GameInputLock.Unlock(this);
    }

    private void OnInteracted(Vector2Int cell, TileDefinition tile)
    {
        if (tile.Behavior != TileDefinition.TileBehavior.Objective || Level == null) return;
        if (Current == Stage.Returning && AtBeacon(cell, player.Cell)) return; // the beacon answers instead

        switch (Current)
        {
            case Stage.Searching:
                SetStage(Stage.Returning);
                int beacons = SpawnLandmarkBeacons();
                Say(Capitalize(Level.ObjectiveName), Level.ObjectiveFoundText +
                    (string.IsNullOrEmpty(mapDamageText) ? "" : "\n" + mapDamageText) +
                    (beacons > 0 && !string.IsNullOrEmpty(beaconsText) ? "\n" + string.Format(beaconsText, beacons) : ""));
                DamageMap();
                SpawnBeacon();
                break;
            case Stage.Returning:
                Say(Capitalize(Level.ObjectiveName), "You've already been here. Head back to where you started.");
                break;
        }
    }

    private void OnInteractPressed(Vector2Int faced, Vector2Int standing)
    {
        if (Current != Stage.Returning || Level == null || !AtBeacon(faced, standing)) return;
        SetStage(Stage.Complete);
        // Input stays locked from here on; the message box handles its own input.
        GameInputLock.Lock(this);
        Say(beaconTitle, Level.ObjectiveCompleteText);
        if (MessageBox.Instance != null) MessageBox.Instance.Closed += OnCompleteMessageClosed;
    }

    private bool AtBeacon(Vector2Int faced, Vector2Int standing) =>
        Level != null && (faced == Level.PlayerStart || standing == Level.PlayerStart);

    // ------------------------------------------------------------------ finding the objective

    private void DamageMap()
    {
        if (hidePlayerMarker && playerMarker != null) playerMarker.Suppressed = true;
        if (map != null && eraseFraction > 0f) map.EraseRandomCells(eraseFraction, keepBorder, eraseDuration);
    }

    /// <summary>A beacon over each landmark that has a sticker on its map cell. Returns how many rose.</summary>
    private int SpawnLandmarkBeacons()
    {
        if (stickers == null || landmarkBeaconPrefab == null)
        {
            if (stickers != null && stickers.Placed.Count > 0)
                Debug.LogWarning($"{name}: stickers placed but no Landmark Beacon Prefab is set, so no beacons rise.", this);
            return 0;
        }
        var lit = new System.Collections.Generic.HashSet<Landmark>();
        foreach (Vector2Int cell in stickers.Placed)
        {
            Landmark best = Landmark.At(cell);
            if (best == null)
            {
                Debug.Log($"Sticker at {cell}: no landmark on that cell, no beacon.");
                continue;
            }
            if (!lit.Add(best)) continue;

            // Just in front of the landmark, so the beam rises from the street beside it.
            Transform t = best.transform;
            GameObject beacon = Instantiate(landmarkBeaconPrefab, t.position + t.forward * 0.6f, Quaternion.identity);
            beacon.name = $"Landmark Beacon - {best.DisplayName}";
            Vector3 scale = beacon.transform.localScale;
            beacon.transform.localScale = new Vector3(scale.x, 0.01f, scale.z);
            Tween.Scale(beacon.transform, scale, 2f, Ease.OutCubic, startDelay: 0.3f * (lit.Count - 1));
        }
        return lit.Count;
    }

    private void SpawnBeacon()
    {
        if (returnBeaconPrefab == null || Level == null) return;
        _beacon = Instantiate(returnBeaconPrefab, Level.CellCenter(Level.PlayerStart), Quaternion.identity);
        _beacon.name = "Return Beacon";
        // Rise out of the ground.
        Vector3 scale = _beacon.transform.localScale;
        _beacon.transform.localScale = new Vector3(scale.x, 0.01f, scale.z);
        Tween.Scale(_beacon.transform, scale, 1.5f, Ease.OutCubic);
        foreach (Light l in _beacon.GetComponentsInChildren<Light>())
            Tween.LightIntensity(l, l.intensity * 0.55f, 1.2f, cycles: -1, cycleMode: CycleMode.Yoyo, ease: Ease.InOutSine);
    }

    // ------------------------------------------------------------------ ending

    private void OnCompleteMessageClosed()
    {
        MessageBox.Instance.Closed -= OnCompleteMessageClosed;
        Say("The End", endText);
        MessageBox.Instance.Closed += OnEndMessageClosed;
    }

    private void OnEndMessageClosed()
    {
        MessageBox.Instance.Closed -= OnEndMessageClosed;
        GameInputLock.Unlock(this);
        Scene scene = SceneManager.GetActiveScene();
        if (scene.buildIndex >= 0) SceneManager.LoadScene(scene.buildIndex);
        else SceneManager.LoadScene(scene.name);
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
