using System.Collections.Generic;
using UnityEngine;

public class HygieneLog : MonoBehaviour
{
    public static HygieneLog Instance { get; private set; }

    [SerializeField] private TextMesh countLabel;

    public int ViolationCount { get; private set; }
    public List<string> Violations { get; } = new List<string>();

    void Awake()
    {
        Instance = this;
        UpdateLabel();
    }

    public void ReportViolation(string message)
    {
        ViolationCount++;
        Violations.Add(message);
        Debug.Log($"[위생 기록 {ViolationCount}] {message} (최종 평가에서 판단)");
        UpdateLabel();
    }

    /// <summary>Recorded only: no count shown while cooking - judged and shown
    /// after submission (ExamEvaluation / ExamResultView).</summary>
    private void UpdateLabel()
    {
        if (countLabel != null) countLabel.text = "";
    }
}
