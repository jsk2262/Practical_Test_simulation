using UnityEngine;

/// <summary>
/// What's on the hands: raw meat (from kneading it - 밑간, 버무리기) and batter.
/// Cleared only by washing the hands (HandWashSequence) - finishing the
/// kneading doesn't. Picking up other food or a tool with raw meat still on
/// the hands is recorded (once) for the final evaluation - nothing is shown
/// while cooking.
/// 손씻기 기록도 여기서: 조리 시작 전에 손을 씻었는지(안 씻고 무언가를 집으면
/// 위생 감점 기록), 마지막 정리 때 손을 씻었는지(최종 평가에서 확인).
/// </summary>
public static class HandHygiene
{
    public static bool RawMeatOnHands { get; private set; }
    public static bool BatterOnHands { get; private set; }
    private static bool recorded;

    /// <summary>시험 중 한 번이라도 손을 씻음.</summary>
    public static bool EverWashed { get; private set; }
    /// <summary>정리 단계(정리 모드)에서 손을 씻음.</summary>
    public static bool WashedInCleanup { get; private set; }
    private static bool startRecorded;

    public static void TouchedRawMeat(bool withBatter)
    {
        RawMeatOnHands = true;
        if (withBatter) BatterOnHands = true;
    }

    /// <summary>Something taken up by hand: with raw meat on the hands, anything
    /// but that meat is a cross-contamination record (once).</summary>
    public static void PickedUp(Carryable item)
    {
        if (item == null) return;
        // 조리 시작 전 손씻기: 손을 한 번도 안 씻고 재료·도구를 집으면 (한 번만)
        // (감독관 안내 중 · 순서 카드 화면은 시험 시작 전 - 재료 확인 · 나누기는 괜찮음)
        bool beforeStart = ExamBriefing.IsPlaying || (CookingPlanManager.Instance != null && CookingPlanManager.Instance.IsShowing)
                           || (CountdownTimer.Instance != null && !CountdownTimer.Instance.Started);
        if (!EverWashed && !startRecorded && !beforeStart && !CleanupMode.IsOn && ExamFlow.Current == ExamFlow.Phase.Cooking)
        {
            startRecorded = true;
            ExamDeductions.Add(ExamDeductions.Hygiene, "조리 시작 전에 손을 씻지 않음");
        }
        if (!RawMeatOnHands || recorded) return;
        if (item is IngredientItem ing && ing.Category == IngredientCategory.Meat) return;
        recorded = true;
        ExamDeductions.Add(ExamDeductions.Hygiene, $"생고기를 만진 손을 씻지 않고 {item.name}을(를) 집음");
    }

    public static void Washed()
    {
        if (RawMeatOnHands || BatterOnHands) Debug.Log("[손 위생] 손 씻음 - 생고기·반죽 제거");
        RawMeatOnHands = BatterOnHands = false;
        recorded = false;
        EverWashed = true;
        if (CleanupMode.IsOn) WashedInCleanup = true;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetForPlay() { RawMeatOnHands = BatterOnHands = recorded = EverWashed = WashedInCleanup = startRecorded = false; }
}
