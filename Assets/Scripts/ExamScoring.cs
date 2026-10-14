using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The project's scoring settings (Resources/ExamScoring): one rule per thing
/// that's judged at the end, by ID. Each rule says what's measured on the
/// finished dish, the range taken as fine, whether that range and the points
/// have been checked (verified), whether it's an official exam rule or our
/// own practice rule, and what to tell the player. Nothing here is a known
/// official number: until a rule is verified its range is tuning only - an
/// out-of-range result shows as 연습 감점 (practice) with no official points;
/// a rule with no range at all is 평가 보류. 실격 rules are off unless each
/// is enabled, verified and given its evidence.
/// </summary>
[CreateAssetMenu(menuName = "Kitchen/Exam Scoring", fileName = "ExamScoring")]
public class ExamScoring : ScriptableObject
{
    [Header("합격 / 불합격 (임시 - 세부 배점 전)")]
    public float totalScore = 100f;
    public float passScore = 60f;

    public List<ScoringRule> rules = new List<ScoringRule>();
    public List<DisqualifyRule> disqualifyRules = new List<DisqualifyRule>();

    private static ExamScoring cached;
    public static ExamScoring Current
    {
        get
        {
            if (cached == null) cached = Resources.Load<ExamScoring>("ExamScoring");
            if (cached == null) cached = CreateInstance<ExamScoring>();
            return cached;
        }
    }

    public ScoringRule Rule(string id)
    {
        foreach (ScoringRule r in rules) if (r.id == id) return r;
        return null;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetForPlay() => cached = null;
}

[Serializable]
public class ScoringRule
{
    [Tooltip("Unique - a rule is scored once per submission.")]
    public string id = "";
    public string dish = "";
    public string title = "";
    [Tooltip("What's measured (shown to developers).")]
    public string metric = "";
    [Tooltip("Has a range at all - without one the item is 평가 보류.")]
    public bool hasRange = true;
    public float okMin = float.NegativeInfinity;
    public float okMax = float.PositiveInfinity;
    [Tooltip("The range has been checked (instructor / tested) - not just tuned by eye.")]
    public bool rangeVerified;
    [Tooltip("An official exam rule (vs. this project's own practice feedback).")]
    public bool official;
    [Tooltip("Points off when out of range - only used when verified.")]
    public float points;
    public bool pointsVerified;
    [TextArea] public string evidence = "";
    [TextArea] public string tip = "";

    public bool InRange(float v) => v >= okMin && v <= okMax;
}

[Serializable]
public class DisqualifyRule
{
    public string id = "";
    public string dish = "";
    public string title = "";
    [Tooltip("The condition, by its code key (DishEvaluation checks the keys it knows).")]
    public string conditionKey = "";
    public float threshold;
    [Tooltip("Off unless enabled AND verified with evidence.")]
    public bool enabled;
    public bool verified;
    [TextArea] public string evidence = "";

    public bool Active => enabled && verified && !string.IsNullOrEmpty(evidence);
}
