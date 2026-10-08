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
        Debug.LogWarning($"[위생 감점 {ViolationCount}] {message}");
        UpdateLabel();
    }

    private void UpdateLabel()
    {
        if (countLabel != null)
        {
            countLabel.text = $"감점: {ViolationCount}";
        }
    }
}
