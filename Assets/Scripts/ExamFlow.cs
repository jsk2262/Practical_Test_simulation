using UnityEngine;

/// <summary>
/// How the exam ends: cooking → 제출 (the dishes on the PlatingPlates are
/// handed in: judged as they are at that moment, the plates locked, 정리 on)
/// → 마무리 정리 → 정리 완료 (the clean-up judged, 합격/불합격, the results).
/// The clock running out: whatever's plated is handed in as it is and the
/// clean-up is judged as it's been left.
/// </summary>
public static class ExamFlow
{
    public enum Phase { Cooking, Cleaning, Finished }
    public static Phase Current { get; private set; }

    public static event System.Action Changed;

    /// <summary>제출: the dishes are judged now (they don't change after), the
    /// serving plates are locked, and clean-up mode comes on.</summary>
    public static void SubmitDishes()
    {
        if (Current != Phase.Cooking) return;
        Current = Phase.Cleaning;
        DishEvaluation.EvaluateDishes();
        foreach (PlatingDestination d in PlatingDestination.All) if (d != null) d.LockSubmitted();
        CleanupMode.Set(true);
        KitchenCamera.Instance?.ShowOverview();
        Debug.Log("[제출] 요리 제출 - 마무리 정리 후 '정리 완료'를 누르세요");
        Changed?.Invoke();
    }

    /// <summary>정리 완료: the clean-up judged, 합격/불합격, the results shown.</summary>
    public static void FinishCleanup()
    {
        if (Current == Phase.Cooking) SubmitDishes();
        if (Current == Phase.Finished) { ExamResultView.ShowResults(); return; }
        Current = Phase.Finished;
        ExamEvaluation.RunFinal();
        Changed?.Invoke();
    }

    /// <summary>Time's up (CountdownTimer).</summary>
    public static void TimeUp()
    {
        if (Current == Phase.Finished) return;
        PassFail.TimedOut = true;
        FinishCleanup();
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetForPlay()
    {
        Current = Phase.Cooking;
        Changed = null;
    }
}
