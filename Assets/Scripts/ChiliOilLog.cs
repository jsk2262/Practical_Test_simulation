using UnityEngine;

/// <summary>
/// How the 고추기름 was made - recorded while it happens (PanSauce, Cheesecloth),
/// judged only at the end (DishEvaluation): how long the 식용유 and 고춧가루
/// were frying hot, how much of that time they were being stirred (the
/// spatula keeps it from catching), how long it was burning and how burnt it
/// got (a safety matter - smoking oil), and whether it was strained through
/// the cheesecloth. Every batch counts, including one burnt and thrown away
/// (the finished dish isn't marked for that oil - DishEvaluation - but the
/// burning itself is a process/safety record).
/// </summary>
public static class ChiliOilLog
{
    public static int Batches;
    /// <summary>Seconds frying hot (on the flame, hot, with 고춧가루 in the oil).</summary>
    public static float HotSeconds;
    /// <summary>Of those, seconds being stirred with the spatula.</summary>
    public static float StirredSeconds;
    /// <summary>Seconds it was catching (burning) - and the most burnt any batch got.</summary>
    public static float BurningSeconds;
    public static float MaxBurn;
    /// <summary>Longest stretch frying hot without a stir (s).</summary>
    public static float LongestUnstirred;
    /// <summary>Oil strained off through the cheesecloth (spoonfuls, 15 mL).</summary>
    public static float StrainedSpoons;

    public static bool Made => HotSeconds > 3f;
    public static float StirredShare => HotSeconds > 0f ? StirredSeconds / HotSeconds : 0f;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetForPlay()
    {
        Batches = 0;
        HotSeconds = StirredSeconds = BurningSeconds = MaxBurn = LongestUnstirred = StrainedSpoons = 0f;
    }
}
