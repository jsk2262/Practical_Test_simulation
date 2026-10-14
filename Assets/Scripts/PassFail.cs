using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 합격 / 불합격 - for now (the detailed points come later): it fails if a
/// dish wasn't handed in on a serving plate (미완성), if any item is 실격, or
/// if the official score (total minus verified official deductions) is under
/// the pass mark. Practice results (연습 감점) don't count toward it. The
/// pass mark and total are in ExamScoring.
/// </summary>
public static class PassFail
{
    public static bool Decided { get; private set; }
    public static bool Passed { get; private set; }
    public static float Score { get; private set; }
    public static bool TimedOut { get; set; }
    public static readonly List<string> Reasons = new List<string>();

    public static void Decide()
    {
        Reasons.Clear();
        ExamScoring cfg = ExamScoring.Current;
        float off = 0f;
        bool dq = false;
        foreach (ExamEvaluation.Item it in ExamEvaluation.Items)
        {
            if (it.status == ExamEvaluation.Status.실격) { dq = true; Reasons.Add($"실격: {it.dish} {it.title} - {it.observed}"); }
            else if (it.status == ExamEvaluation.Status.감점 && it.official && !it.duplicate) off += it.points;
        }
        foreach (string missing in DishEvaluation.MissingDishes)
            Reasons.Add($"미완성: {missing}을(를) 완성 접시에 담아 제출하지 않음");
        if (TimedOut) Reasons.Add("시험 시간 종료");
        Score = Mathf.Max(0f, cfg.totalScore - off);
        bool scoreOk = Score >= cfg.passScore;
        if (!scoreOk) Reasons.Add($"점수 {Score:0.#}점 - 합격 기준 {cfg.passScore:0}점 미만");
        Passed = !dq && DishEvaluation.MissingDishes.Count == 0 && scoreOk;
        Decided = true;
        Debug.Log($"[최종 평가] {(Passed ? "합격" : "불합격")} - 점수 {Score:0.#} / {cfg.totalScore:0} {(Reasons.Count > 0 ? "(" + string.Join(", ", Reasons) + ")" : "")}");
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetForPlay()
    {
        Decided = Passed = TimedOut = false;
        Score = 0f;
        Reasons.Clear();
    }
}
