using System;
using UnityEngine;

/// <summary>Player-facing settings, saved between sessions (PlayerPrefs).</summary>
public static class GameSettings
{
    private const string CrtKey = "settings.crtFilter";
    private const string TrailsKey = "settings.motionTrails";

    /// <summary>Raised after any setting changes.</summary>
    public static event Action Changed;

    public static bool CrtFilter
    {
        get => PlayerPrefs.GetInt(CrtKey, 1) == 1;
        set => Set(CrtKey, value);
    }

    public static bool MotionTrails
    {
        get => PlayerPrefs.GetInt(TrailsKey, 1) == 1;
        set => Set(TrailsKey, value);
    }

    private static void Set(string key, bool value)
    {
        PlayerPrefs.SetInt(key, value ? 1 : 0);
        PlayerPrefs.Save();
        Changed?.Invoke();
    }
}
