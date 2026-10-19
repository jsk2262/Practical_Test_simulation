using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 점수표: 공개과제 문제지의 배점 구조 그대로 - 과제마다 위생상태 및 안전관리 5점,
/// 조리기술 30점, 작품의 평가 15점 (두 과제 합 100점, 60점 이상 합격).
/// 공단은 항목별 세부 감점 점수를 공개하지 않으므로, 감점 한 건의 크기는 이
/// 시뮬레이션의 기준(아래 상수)이다. ExamScoring에 공식·검증된 배점이 있는
/// 항목은 그 점수를 쓴다. 영역별로 감점은 그 영역 배점까지만 깎인다.
/// 실격 항목이 하나라도 있으면 점수와 관계없이 실격(채점 제외).
/// </summary>
public static class ScoreSheet
{
    public enum Part { 위생안전, 조리기술, 작품평가 }

    /// <summary>과제 수 (탕수육 · 마파두부).</summary>
    public const int Dishes = 2;
    public const float HygienePerDish = 5f, SkillPerDish = 30f, WorkPerDish = 15f;

    // 감점 한 건의 크기 (시뮬레이션 기준)
    public const float HygieneDeduction = 1f, SafetyDeduction = 2f, SkillDeduction = 3f, ProcessRecordDeduction = 2f, WorkDeduction = 3f;

    public static float Max(Part p) =>
        (p == Part.위생안전 ? HygienePerDish : p == Part.조리기술 ? SkillPerDish : WorkPerDish) * Dishes;

    public static string Label(Part p) =>
        p == Part.위생안전 ? "위생상태 및 안전관리" : p == Part.조리기술 ? "조리기술" : "작품의 평가";

    /// <summary>영역별 깎인 점수 (배점 넘게는 안 깎임).</summary>
    public static readonly Dictionary<Part, float> Lost = new Dictionary<Part, float>();

    public static float Total
    {
        get
        {
            float t = 0f;
            foreach (Part p in System.Enum.GetValues(typeof(Part))) t += Got(p);
            return t;
        }
    }

    public static float MaxTotal => (HygienePerDish + SkillPerDish + WorkPerDish) * Dishes;

    public static float Got(Part p) => Mathf.Max(0f, Max(p) - (Lost.TryGetValue(p, out float l) ? l : 0f));

    /// <summary>항목이 어느 영역인지: 정리·위생 기록은 위생안전, 완성품 상태(익힘·소스·
    /// 두부 모양·튀김옷 겉모습)는 작품의 평가, 만드는 과정은 조리기술.</summary>
    public static Part PartOf(ExamEvaluation.Item it)
    {
        string id = it.ruleId ?? "";
        if (id.StartsWith("record."))
        {
            string cat = RecordCategory(id);
            return cat == ExamDeductions.Process ? Part.조리기술 : Part.위생안전;
        }
        if (it.dish == DishEvaluation.Cleanup || id.Contains("overheat")) return Part.위생안전;
        if (id.Contains(".submitted") || id.Contains(".doneness") || id.Contains(".sauce.") || id.Contains("tofu.shape")
            || id.Contains("chilioil.burn") || id.Contains("batter.coat") || id.Contains("batter.even") || id.Contains("batter.retention"))
            return Part.작품평가;
        return Part.조리기술;
    }

    private static string RecordCategory(string id)
    {
        // record.(category).(reason)
        int a = id.IndexOf('.') + 1, b = id.IndexOf('.', a);
        return b > a ? id.Substring(a, b - a) : "";
    }

    private static float DefaultPoints(ExamEvaluation.Item it, Part p)
    {
        if (p == Part.위생안전)
            return it.ruleId.StartsWith("record.") && RecordCategory(it.ruleId) == ExamDeductions.Safety || it.ruleId.Contains("overheat")
                ? SafetyDeduction : HygieneDeduction;
        if (p == Part.조리기술) return it.ruleId.StartsWith("record.") ? ProcessRecordDeduction : SkillDeduction;
        return WorkDeduction;
    }

    /// <summary>감점 항목마다 영역과 점수를 매기고 영역별로 합산한다 (최종 평가 때 한 번).</summary>
    public static void Apply()
    {
        Lost.Clear();
        foreach (ExamEvaluation.Item it in ExamEvaluation.Items)
        {
            if (it.status != ExamEvaluation.Status.감점) continue;
            Part p = PartOf(it);
            if (it.duplicate)
            {
                it.points = 0f;
                continue;
            }
            ScoringRule rule = ExamScoring.Current.Rule(it.ruleId);
            bool officialPoints = rule != null && rule.official && rule.rangeVerified && rule.pointsVerified && rule.points > 0f;
            it.points = officialPoints ? rule.points : DefaultPoints(it, p);
            it.pointsText = $"-{it.points:0.#}점 ({Label(p)})";
            Lost[p] = (Lost.TryGetValue(p, out float l) ? l : 0f) + it.points;
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetForPlay() => Lost.Clear();
}
