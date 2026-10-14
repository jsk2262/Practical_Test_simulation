using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The final evaluation (최종 제출/평가) - once per exam: when the clock runs
/// out (CountdownTimer) or the 제출 button is pressed (SubmitButton). Nothing
/// is judged or shown while cooking: what happens is only recorded (amounts,
/// heat, mixing, cleaning ...), and here, once, the finished state is judged
/// (DishEvaluation) - one result per rule ID, so reopening the results never
/// takes anything off again. Each result: the item, 적정 / 감점 / 평가 보류 /
/// 실격, points (only from verified scoring settings - ExamScoring), what was
/// seen, the cause if the records show it, and what to try next time.
/// </summary>
public static class ExamEvaluation
{
    public static event Action Evaluating;
    public static bool Done { get; private set; }

    public enum Status { 적정, 감점, 평가보류, 실격 }

    public class Item
    {
        public string ruleId, dish, title;
        public Status status;
        /// <summary>An official exam rule, verified - else this project's own practice rule.</summary>
        public bool official;
        /// <summary>Points off (verified official rules only; 0 otherwise).</summary>
        public float points;
        public string pointsText = "";
        public string observed = "", cause = "", tip = "";
        /// <summary>The same problem as another item - shown, not counted again.</summary>
        public bool duplicate;
    }

    /// <summary>The judged items, one per rule ID.</summary>
    public static readonly List<Item> Items = new List<Item>();

    public struct Result
    {
        public string section, key, text;
    }

    /// <summary>Notes that aren't judged items (what's 미설정 / 미검증), one per key.</summary>
    public static readonly List<Result> Results = new List<Result>();

    public static void Report(string section, string key, string text)
    {
        for (int i = 0; i < Results.Count; i++)
            if (Results[i].key == key)
            {
                Results[i] = new Result { section = section, key = key, text = text };
                return;
            }
        Results.Add(new Result { section = section, key = key, text = text });
        Debug.Log($"[최종 평가 · {section}] {text}");
    }

    /// <summary>Adds (or replaces - never doubles) the result for this rule ID.</summary>
    public static void Add(Item item)
    {
        if (item == null || string.IsNullOrEmpty(item.ruleId)) return;
        for (int i = 0; i < Items.Count; i++)
            if (Items[i].ruleId == item.ruleId) { Items[i] = item; return; }
        Items.Add(item);
        Debug.Log($"[최종 평가] {item.dish} {item.title} — {item.status} {item.pointsText}: {item.observed}");
    }

    /// <summary>A measured value judged by its rule (ExamScoring). No rule or no
    /// range: 평가 보류. In range: 적정. Out: 감점 - official points only when
    /// the rule is official and both its range and points are verified;
    /// otherwise a practice result with no official points.</summary>
    public static Item Judge(string ruleId, string dish, string title, float? value, string observedOk, string observedBad,
                             string cause, string tipFallback = "")
    {
        ScoringRule rule = ExamScoring.Current.Rule(ruleId);
        var item = new Item { ruleId = ruleId, dish = dish, title = rule != null && !string.IsNullOrEmpty(rule.title) ? rule.title : title };
        string tip = rule != null && !string.IsNullOrEmpty(rule.tip) ? rule.tip : tipFallback;
        if (value == null)
        {
            item.status = Status.평가보류;
            item.observed = observedBad;
            item.cause = cause;
            item.pointsText = "판단할 데이터 없음";
            return item;
        }
        if (rule == null || !rule.hasRange)
        {
            item.status = Status.평가보류;
            item.observed = observedOk;
            item.pointsText = "판정 기준 미설정";
            item.tip = tip;
            return item;
        }
        if (rule.InRange(value.Value))
        {
            item.status = Status.적정;
            item.observed = observedOk;
            item.pointsText = rule.official && rule.rangeVerified ? "공식 기준 충족" : "연습 기준 충족";
            return item;
        }
        item.status = Status.감점;
        item.observed = observedBad;
        item.cause = cause;
        item.tip = tip;
        item.official = rule.official && rule.rangeVerified;
        if (item.official && rule.pointsVerified && rule.points > 0f)
        {
            item.points = rule.points;
            item.pointsText = $"-{rule.points:0.#}점 (공식)";
        }
        else item.pointsText = item.official ? "공식 · 배점 미설정" : "연습 감점 · 공식 점수 아님";
        return item;
    }

    public static void RunFinal()
    {
        if (!Done)
        {
            Done = true;
            Debug.Log("[최종 평가] 시작");
            Evaluating?.Invoke();
            Debug.Log($"[최종 평가] 항목 {Items.Count}개, 기록된 감점 사유 {ExamDeductions.Items.Count}개");
        }
        ExamResultView.ShowResults(); // (again after closing: the same results, nothing taken off twice)
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetForPlay()
    {
        Done = false;
        Evaluating = null;
        Results.Clear();
        Items.Clear();
    }
}
