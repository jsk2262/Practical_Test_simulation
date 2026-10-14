using UnityEngine;

/// <summary>
/// 빠른 모드 - for going through the steps in order without the waiting:
/// what takes minutes (boiling water, heating the frying oil or a pan, the
/// 앙금 settling, cooking through, 겨자 fermenting) goes many times faster
/// (the 앙금 settles at once), and the animations that hold the hand (cutting,
/// cracking an egg, scooping, stirring, washing ...) play fast. Nothing burns
/// or chars in it (it's for the order, not the timing). The exam clock keeps
/// real time. Results are marked as a 빠른 모드 run.
/// </summary>
public static class SpeedMode
{
    public static bool On { get; private set; }
    /// <summary>Was on at any point this run (the results say so).</summary>
    public static bool EverOn { get; private set; }
    public static event System.Action<bool> Changed;

    /// <summary>How many times faster the waiting goes.</summary>
    public const float WaitFactor = 20f;
    /// <summary>Time scale while an animation holds the hand.</summary>
    public const float AnimationTimeScale = 8f;

    /// <summary>Long animations that don't (only) lock the hand - cutting - say
    /// they're playing, so 빠른 모드 speeds them up too.</summary>
    public static int AnimationsPlaying { get; private set; }
    public static void BeginAnimation() => AnimationsPlaying++;
    public static void EndAnimation() => AnimationsPlaying = Mathf.Max(0, AnimationsPlaying - 1);

    /// <summary>A waiting process's time step (dt, or dt × WaitFactor in 빠른 모드).</summary>
    public static float Wait(float dt) => On ? dt * WaitFactor : dt;

    public static void Toggle()
    {
        On = !On;
        if (On) EverOn = true;
        if (!On) Time.timeScale = 1f;
        Debug.Log($"[빠른 모드] {(On ? "켜짐 - 기다리는 과정·애니메이션 생략, 탐 계산 생략" : "꺼짐")}");
        Changed?.Invoke(On);
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetForPlay()
    {
        On = false;
        EverOn = false;
        AnimationsPlaying = 0;
        Changed = null;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void MakeDriver()
    {
        var go = new GameObject("SpeedModeDriver");
        Object.DontDestroyOnLoad(go);
        go.AddComponent<SpeedModeDriver>();
    }
}

/// <summary>While 빠른 모드 is on: an animation holding the hand plays fast.</summary>
public class SpeedModeDriver : MonoBehaviour
{
    void Update()
    {
        if (!SpeedMode.On) return;
        bool animating = SpeedMode.AnimationsPlaying > 0 || (HandCarry.Instance != null && HandCarry.Instance.IsLocked);
        float want = animating ? SpeedMode.AnimationTimeScale : 1f;
        if (!Mathf.Approximately(Time.timeScale, want) && Time.timeScale > 0f) Time.timeScale = want;
    }
}
