using System;
using UnityEngine;

/// <summary>
/// "정리" (clean-up) mode, toggled by the 정리 tab (CleanupTabButton). Off, the
/// dishes stay put (MovableBowl.IsDish can't be picked up - ingredients still go
/// on and come off them as usual); on, they can be carried to the sink, washed
/// and put away on the plate shelves (DishShelfController).
/// </summary>
public static class CleanupMode
{
    public static bool IsOn { get; private set; }

    public static event Action<bool> Changed;

    /// <summary>A dish was clicked while clean-up is off - the tab can draw attention to itself.</summary>
    public static event Action Blocked;

    public static void Set(bool on)
    {
        if (IsOn == on) return;
        IsOn = on;
        Changed?.Invoke(on);
    }

    public static void Toggle() => Set(!IsOn);

    public static void NotifyBlocked() => Blocked?.Invoke();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetForPlay()
    {
        IsOn = false;
        Changed = null;
        Blocked = null;
    }
}
