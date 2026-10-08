using UnityEngine;

public enum ProcessStage { None, Prep, Cutting, Blanching, Boiling, Frying, StirFrying, Plating }

public class ProcessStageManager : MonoBehaviour
{
    public static ProcessStageManager Instance { get; private set; }

    public ProcessStage CurrentStage { get; private set; } = ProcessStage.None;

    private bool wipedSinceStageStart = true;

    void Awake()
    {
        Instance = this;
    }

    public void MarkWiped()
    {
        wipedSinceStageStart = true;
    }

    public void SetStage(ProcessStage newStage)
    {
        if (newStage != CurrentStage && !wipedSinceStageStart)
        {
            HygieneLog.Instance?.ReportViolation(
                $"단계 전환({CurrentStage}→{newStage}) 전에 테이블 정리(행주/키친타월) 안 함");
        }

        CurrentStage = newStage;
        wipedSinceStageStart = false;
    }
}
