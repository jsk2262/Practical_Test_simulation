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

    // (감점 기록이 늘 때마다 감점칸 숫자도 - 구독은 Start에서: ExamDeductions.Added는
    // 씬이 뜨기 전에 비워지므로)
    void Start() => ExamDeductions.Added += OnDeductionAdded;
    void OnDestroy() => ExamDeductions.Added -= OnDeductionAdded;
    private void OnDeductionAdded(string _) => UpdateLabel();

    public void ReportViolation(string message)
    {
        ViolationCount++;
        Violations.Add(message);
        Debug.Log($"[위생 기록 {ViolationCount}] {message} (최종 평가에서 판단)");
        UpdateLabel();
    }

    /// <summary>감점칸 (Label_HygieneCount, 씬에서 옮겨 둔 그 자리): 지금까지 기록된
    /// 감점 사유 수 (조리과정·위생 등 ExamDeductions + 교차오염 위생 기록).
    /// 내용과 판정은 제출 후 결과 화면(ExamResultView)에서.</summary>
    private void UpdateLabel()
    {
        if (countLabel != null) countLabel.text = $"감점: {ExamDeductions.Items.Count + ViolationCount}";
    }
}
