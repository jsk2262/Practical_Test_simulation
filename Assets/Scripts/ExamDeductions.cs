using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Procedure points to be deducted (e.g. meat cooked without draining its
/// blood on a paper towel first). There's no score system yet - this just
/// collects them, once each, for it to read later (Items / Entries / Added),
/// each under its scoring category (위생, 조리태도, 조리과정...).
/// Separate from HygieneLog, which counts cross-contamination.
/// </summary>
public static class ExamDeductions
{
    /// <summary>The usual categories (any other text works too).</summary>
    public const string Hygiene = "위생", Safety = "안전", Attitude = "조리태도", Process = "조리과정";

    public struct Entry
    {
        public string category, reason;
        public float time;
    }

    /// <summary>Each deduction once, as "[category] reason".</summary>
    public static readonly List<string> Items = new List<string>();
    public static readonly List<Entry> Entries = new List<Entry>();
    public static event Action<string> Added;

    /// <summary>A 조리과정 deduction.</summary>
    public static void Add(string reason) => Add(Process, reason);

    public static void Add(string category, string reason)
    {
        string key = $"[{category}] {reason}";
        if (Items.Contains(key)) return;
        Items.Add(key);
        Entries.Add(new Entry { category = category, reason = reason, time = Time.time });
        Debug.Log($"[기록] {key} (최종 평가에서 판단 - 조리 중에는 표시하지 않음)");
        Added?.Invoke(key);
    }

    /// <summary>조리 중에 일어난 실격 사유 (예: 젖은 체망을 뜨거운 기름에 넣어 기름이 튐).
    /// 최종 평가에서 실격 항목이 된다. 같은 사유는 한 번만.</summary>
    public static readonly List<string> Disqualifications = new List<string>();

    public static void Disqualify(string reason)
    {
        if (Disqualifications.Contains(reason)) return;
        Disqualifications.Add(reason);
        Debug.Log($"[기록 · 실격 사유] {reason}");
    }

    /// <summary>How many deductions there are so far in a category.</summary>
    public static int CountOf(string category)
    {
        int n = 0;
        foreach (Entry e in Entries) if (e.category == category) n++;
        return n;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetForPlay()
    {
        Items.Clear();
        Entries.Clear();
        Disqualifications.Clear();
        Added = null;
    }
}
