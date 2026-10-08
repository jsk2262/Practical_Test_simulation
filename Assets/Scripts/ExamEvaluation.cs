using System;
using UnityEngine;

/// <summary>
/// The final evaluation (최종 제출/평가) - once per exam: when the clock runs
/// out (CountdownTimer), or when a submit button calls RunFinal. Checks that
/// can only be judged at the end (the burner floor still dirty, ...) listen to
/// Evaluating and add their deductions (ExamDeductions) then - once, not every
/// frame or on every 정리 click.
/// </summary>
public static class ExamEvaluation
{
    public static event Action Evaluating;
    public static bool Done { get; private set; }

    public static void RunFinal()
    {
        if (Done) return;
        Done = true;
        Debug.Log("[최종 평가] 시작");
        Evaluating?.Invoke();
        Debug.Log($"[최종 평가] 감점 항목 {ExamDeductions.Items.Count}개: {string.Join(" / ", ExamDeductions.Items)}");
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetForPlay()
    {
        Done = false;
        Evaluating = null;
    }
}
