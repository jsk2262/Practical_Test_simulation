using UnityEngine;

/// <summary>
/// What's on the hands: raw meat (from kneading it - 밑간, 버무리기) and batter.
/// Cleared only by washing the hands (HandWashSequence) - finishing the
/// kneading doesn't. Picking up other food or a tool with raw meat still on
/// the hands is recorded (once) for the final evaluation - nothing is shown
/// while cooking.
/// </summary>
public static class HandHygiene
{
    public static bool RawMeatOnHands { get; private set; }
    public static bool BatterOnHands { get; private set; }
    private static bool recorded;

    public static void TouchedRawMeat(bool withBatter)
    {
        RawMeatOnHands = true;
        if (withBatter) BatterOnHands = true;
    }

    /// <summary>Something taken up by hand: with raw meat on the hands, anything
    /// but that meat is a cross-contamination record (once).</summary>
    public static void PickedUp(Carryable item)
    {
        if (!RawMeatOnHands || recorded || item == null) return;
        if (item is IngredientItem ing && ing.Category == IngredientCategory.Meat) return;
        recorded = true;
        ExamDeductions.Add(ExamDeductions.Hygiene, $"생고기를 만진 손을 씻지 않고 {item.name}을(를) 집음");
    }

    public static void Washed()
    {
        if (RawMeatOnHands || BatterOnHands) Debug.Log("[손 위생] 손 씻음 - 생고기·반죽 제거");
        RawMeatOnHands = BatterOnHands = false;
        recorded = false;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetForPlay() { RawMeatOnHands = BatterOnHands = recorded = false; }
}
