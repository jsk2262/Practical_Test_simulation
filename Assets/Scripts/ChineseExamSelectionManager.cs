using System; // 시험 메뉴 추첨에 사용할 난수 생성기다.
using UnityEngine; // 화면과 게임 컴포넌트를 제어한다.

/// <summary>조리 순서 화면 전에 두 과제를 추첨한다. 미구현 과제는 기존 요리로 몰래 대체하지 않는다.</summary>
public sealed class ChineseExamSelectionManager : MonoBehaviour
{
    private readonly System.Random random = new System.Random(); // 한 화면 안에서 계속 사용할 난수 생성기다.
    public string[] SelectedDishes { get; private set; } // 이번 추첨 결과를 보관한다.
    private Action onContinue; // 현재 지원하는 조리 화면을 여는 동작이다.
    private ClickDetector[] detectors; // 추첨 중 조리대 클릭을 막을 컴포넌트다.
    private bool[] detectorStates; // 원래 활성 상태로 복원할 때 사용한다.
    private Font font; // 한글 표시용 글꼴이다.
    private bool open; // 추첨 화면이 열렸는지 나타낸다.

    public void Open(Action continueCooking) // 순서 화면 대신 추첨 화면부터 연다.
    {
        onContinue = continueCooking; // 조리 화면으로 돌아갈 동작을 저장한다.
        detectors = FindObjectsByType<ClickDetector>(FindObjectsSortMode.None); // 처음 열 때만 입력 컴포넌트를 수집한다.
        detectorStates = new bool[detectors.Length]; // 복원할 상태를 준비한다.
        for (int i = 0; i < detectors.Length; i++) // 추첨 화면 뒤의 물체를 클릭하지 못하게 한다.
        {
            detectorStates[i] = detectors[i].enabled; // 원래 상태를 기록한다.
            detectors[i].enabled = false; // 조리대 입력을 잠시 끈다.
        }
        font = Font.CreateDynamicFontFromOSFont("Malgun Gothic", 24); // 한글을 표시한다.
        SelectedDishes = ChineseExamMenuCatalog.DrawPair(random); // 구현된 메뉴 중 중복 없이 두 개를 뽑는다.
        open = true; // 추첨 결과를 보여준다.
    }

    private static bool Supported(string dish) => ChineseExamMenuCatalog.IsImplemented(dish); // 구현 목록은 메뉴 카탈로그 한 곳에서 관리한다.

    private void ContinueCooking(bool practice) // 지원되는 시험 또는 기존 연습을 시작한다.
    {
        if (practice) SelectedDishes = new[] { "탕수육", "마파두부" }; // 명시적으로 선택한 연습 모드만 고정 조합을 사용한다.
        if (!Supported(SelectedDishes[0]) || !Supported(SelectedDishes[1])) return; // 미구현 조합이 기존 시험으로 잘못 진입하지 않도록 한다.
        open = false; // 추첨 화면을 닫는다.
        RestoreInput(); // 조리대의 원래 입력 상태로 돌아간다.
        onContinue?.Invoke(); // 기존 순서 정리 화면을 연다.
    }

    private void RestoreInput() // 꺼 둔 입력 컴포넌트를 복원한다.
    {
        if (detectors == null) return; // 아직 화면을 열지 않았다면 복원할 것이 없다.
        for (int i = 0; i < detectors.Length; i++) // 기존에 켜져 있던 컴포넌트만 다시 켠다.
            if (detectors[i] != null) detectors[i].enabled = detectorStates[i]; // 삭제된 물체는 건너뛴다.
        detectors = null; // 중복 복원을 방지한다.
    }

    private void OnDestroy() // 장면 종료 시 임시 자원을 정리한다.
    {
        RestoreInput(); // 남아 있는 입력을 복원한다.
        if (font != null) Destroy(font); // 생성한 글꼴 자원을 해제한다.
    }

    private void OnGUI() // 화면 크기에 맞게 추첨 UI를 그린다.
    {
        if (!open) return; // 조리 중에는 UI 작업을 하지 않는다.
        Matrix4x4 previous = GUI.matrix; // 기존 GUI 배율을 기억한다.
        GUI.matrix = Matrix4x4.Scale(new Vector3(Screen.width / 1280f, Screen.height / 720f, 1f)); // 해상도에 맞춰 배치한다.
        GUI.depth = -1000; // 다른 안내보다 앞에 표시한다.
        GUI.Box(new Rect(0, 0, 1280, 720), GUIContent.none); // 전체 영역에 배경을 그린다.
        var title = new GUIStyle(GUI.skin.label) { font = font, fontSize = 36, alignment = TextAnchor.MiddleCenter }; // 제목 글꼴을 정한다.
        var body = new GUIStyle(title) { fontSize = 22, wordWrap = true }; // 설명 글꼴을 정한다.
        var button = new GUIStyle(GUI.skin.button) { font = font, fontSize = 23 }; // 버튼 글꼴을 정한다.
        GUI.Label(new Rect(100, 75, 1080, 70), "중식 실기 · 오늘의 두 과제", title); // 추첨 제목이다.
        GUI.Label(new Rect(100, 145, 1080, 50), $"조리 가능한 {ChineseExamMenuCatalog.Implemented.Count}개 메뉴 중 서로 다른 2개를 무작위로 선택합니다 (전체 {ChineseExamMenuCatalog.Names.Count}개)", body); // 추첨 규칙과 현재 추첨 대상 수를 설명한다.
        for (int i = 0; i < 2; i++) // 두 과제를 나란히 표시한다.
        {
            float x = 180 + i * 480; // 카드 좌우 위치다.
            GUI.Box(new Rect(x, 240, 440, 180), GUIContent.none); // 메뉴 카드 배경이다.
            GUI.Label(new Rect(x, 260, 440, 70), SelectedDishes[i], title); // 추첨된 이름을 표시한다.
            GUI.Label(new Rect(x, 335, 440, 50), Supported(SelectedDishes[i]) ? "조리 가능" : "조리 콘텐츠 준비 중", body); // 지원 여부를 표시한다.
        }
        bool ready = Supported(SelectedDishes[0]) && Supported(SelectedDishes[1]); // 현재 조합을 시작할 수 있는지 판단한다.
        GUI.Label(new Rect(120, 445, 1040, 65), ready ? "두 메뉴의 조리 순서를 계획한 뒤 시험을 시작하세요." : "메뉴 추첨 기능이 준비되었습니다. 나머지 메뉴의 재료·조리·채점은 추후 추가됩니다.", body); // 구현 범위를 명확히 알린다.
        if (GUI.Button(new Rect(180, 540, 280, 60), "다시 추첨", button)) SelectedDishes = ChineseExamMenuCatalog.DrawPair(random); // 새 조합을 뽑는다.
        GUI.enabled = ready; // 미구현 조합은 시작 버튼을 비활성화한다.
        bool begin = GUI.Button(new Rect(480, 540, 280, 60), "선택한 과제 시작", button); // 지원 조합으로 진입한다.
        GUI.enabled = true; // 뒤의 버튼은 다시 활성화한다.
        bool practice = GUI.Button(new Rect(780, 540, 320, 60), "탕수육·마파두부 연습", button); // 기존 연습으로 명시적으로 들어간다.
        GUI.matrix = previous; // 다른 GUI에 영향을 주지 않도록 배율을 복원한다.
        if (begin || practice) ContinueCooking(practice); // 화면 그리기를 끝낸 뒤 조리 화면으로 전환한다.
    }
}
