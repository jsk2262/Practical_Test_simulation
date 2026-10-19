using UnityEngine;

/// <summary>
/// 지급 재료 안내 중 경고 자막: 감독관이 재료를 읽기 시작할 때부터 "재료가 없거나
/// 상태가 좋지 않다면 말해주세요"까지 화면 아래에 "칼 · 화구 · 배수구 사용 금지"를
/// 크게 띄운다. 안내 줄이 바뀔 때(ExamBriefing.LineStarted) 켜고 끈다.
/// </summary>
public class BriefingWarning : MonoBehaviour
{
    [Tooltip("켜고 끌 경고 자막 (씬에서 위치 · 크기 조절).")]
    [SerializeField] private GameObject warning;

    void Awake() { if (warning != null) warning.SetActive(false); } // 처음엔 숨김

    void OnEnable() => ExamBriefing.LineStarted += OnLine;   // 안내 줄 바뀔 때 받기
    void OnDisable() => ExamBriefing.LineStarted -= OnLine;  // 구독 해제

    private void OnLine(string line)
    {
        if (warning == null) return;                          // 자막 없으면 무시
        if (line.Contains("지급재료") || line.Contains("재료입니다")) warning.SetActive(true); // 지급 재료를 읽기 시작 → 켬 (음성 · 글자 안내 둘 다)
        else if (line.Contains("문제없다면")) warning.SetActive(false); // "말해주세요" 다음 줄 → 끔
    }

    void Update()
    {
        if (warning != null && warning.activeSelf && !ExamBriefing.IsPlaying) warning.SetActive(false); // 안내를 건너뛰어 끝나도 끔
    }
}
