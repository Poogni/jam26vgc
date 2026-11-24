using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Pauses player movement / interaction while something (settings menu, dialogue box...) holds a lock.
/// Several things can hold it at once; input resumes when the last one releases.
/// </summary>
public static class GameInputLock
{
    private static readonly HashSet<object> Holders = new HashSet<object>();

    public static bool IsLocked => Holders.Count > 0;

    public static void Lock(object holder) => Holders.Add(holder);
    public static void Unlock(object holder) => Holders.Remove(holder);

    // Survive "Enter Play Mode" without a domain reload.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetOnPlay() => Holders.Clear();
}
