using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 합격 / 불합격: 실격 항목이 하나라도 있으면 실격(채점 제외 - 불합격). 아니면
/// 점수표(ScoreSheet: 위생상태 및 안전관리 · 조리기술 · 작품의 평가)의 점수가
/// 합격 기준(ExamScoring.passScore, 60점) 이상이면 합격.
/// </summary>
public static class PassFail
{
    public static bool Decided { get; private set; }
    public static bool Passed { get; private set; }
    /// <summary>실격 - 점수는 참고로만 보여 준다.</summary>
    public static bool Disqualified { get; private set; }
    public static float Score { get; private set; }
    public static bool TimedOut { get; set; }
    public static readonly List<string> Reasons = new List<string>();

    public static void Decide()
    {
        Reasons.Clear();
        ExamScoring cfg = ExamScoring.Current;
        ScoreSheet.Apply();
        bool dq = false;
        foreach (ExamEvaluation.Item it in ExamEvaluation.Items)
            if (it.status == ExamEvaluation.Status.실격) { dq = true; Reasons.Add($"실격: {it.dish} {it.title} - {it.observed}"); }
        if (TimedOut) Reasons.Add("시험 시간 종료");
        Score = ScoreSheet.Total;
        bool scoreOk = Score >= cfg.passScore;
        if (!dq && !scoreOk) Reasons.Add($"점수 {Score:0.#}점 - 합격 기준 {cfg.passScore:0}점 미만");
        Disqualified = dq;
        Passed = !dq && DishEvaluation.MissingDishes.Count == 0 && scoreOk;
        Decided = true;
        Debug.Log($"[최종 평가] {(dq ? "실격" : Passed ? "합격" : "불합격")} - 점수 {Score:0.#} / {ScoreSheet.MaxTotal:0} {(Reasons.Count > 0 ? "(" + string.Join(", ", Reasons) + ")" : "")}");
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetForPlay()
    {
        Decided = Passed = Disqualified = TimedOut = false;
        Score = 0f;
        Reasons.Clear();
    }
}
