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
    private bool picking; // 원하는 과제를 직접 고르는 화면인지 나타낸다.
    private readonly System.Collections.Generic.List<string> picked = new System.Collections.Generic.List<string>(2); // 직접 고른 과제 (최대 2개)
    private string pickedLabel = ""; // 고른 과제 안내 문구 (고를 때만 새로 만든다)
    private static readonly Color BackColor = new Color(0.13f, 0.14f, 0.16f); // 뒤가 안 보이는 불투명 배경색
    private static readonly Color PickedColor = new Color(1f, 0.82f, 0.3f); // 고른 메뉴 버튼 색

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
        font = KoreanFontFallback.Font; // 게임에 넣은 한글 글꼴 (웹에서는 PC 글꼴을 쓸 수 없다)
        SelectedDishes = ChineseExamMenuCatalog.DrawPair(random); // 구현된 메뉴 중 중복 없이 두 개를 뽑는다.
        open = true; // 추첨 결과를 보여준다.
    }

    private static bool Supported(string dish) => ChineseExamMenuCatalog.IsImplemented(dish); // 구현 목록은 메뉴 카탈로그 한 곳에서 관리한다.

    private void ContinueCooking() // 추첨했거나 직접 고른 두 과제로 시험을 시작한다.
    {
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
        RestoreInput(); // 남아 있는 입력을 복원한다. (글꼴은 함께 쓰는 자원이라 지우지 않는다)
    }

    private void OnGUI() // 화면 크기에 맞게 추첨 UI를 그린다.
    {
        if (!open) return; // 조리 중에는 UI 작업을 하지 않는다.
        Matrix4x4 previous = GUI.matrix; // 기존 GUI 배율을 기억한다.
        GUI.matrix = Matrix4x4.Scale(new Vector3(Screen.width / 1280f, Screen.height / 720f, 1f)); // 해상도에 맞춰 배치한다.
        GUI.depth = -1000; // 다른 안내보다 앞에 표시한다.
        GUI.color = BackColor; // 배경색으로 칠할 준비
        GUI.DrawTexture(new Rect(0, 0, 1280, 720), Texture2D.whiteTexture); // 전체 영역을 불투명하게 덮어 뒤의 조리대를 가린다.
        GUI.color = Color.white; // 다음 그리기는 원래 색으로
        var title = new GUIStyle(GUI.skin.label) { font = font, fontSize = 36, alignment = TextAnchor.MiddleCenter }; // 제목 글꼴을 정한다.
        var body = new GUIStyle(title) { fontSize = 22, wordWrap = true }; // 설명 글꼴을 정한다.
        var button = new GUIStyle(GUI.skin.button) { font = font, fontSize = 23 }; // 버튼 글꼴을 정한다.
        if (picking) { DrawPicker(title, body, button); GUI.matrix = previous; return; } // 직접 고르는 화면이면 그것만 그린다.
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
        bool begin = GUI.Button(new Rect(500, 540, 280, 60), "선택된 과제 시작", button); // 추첨된 두 과제로 진입한다.
        GUI.enabled = true; // 뒤의 버튼은 다시 활성화한다.
        if (GUI.Button(new Rect(820, 540, 280, 60), "원하는 과제 선택", button)) { picking = true; picked.Clear(); UpdatePickedLabel(); } // 20개 중 직접 고르는 화면으로 간다.
        GUI.matrix = previous; // 다른 GUI에 영향을 주지 않도록 배율을 복원한다.
        if (begin) ContinueCooking(); // 화면 그리기를 끝낸 뒤 조리 화면으로 전환한다.
    }

    private void DrawPicker(GUIStyle title, GUIStyle body, GUIStyle button) // 20개 메뉴 중 두 개를 직접 고르는 화면을 그린다.
    {
        GUI.Label(new Rect(100, 40, 1080, 60), "원하는 과제 선택", title); // 화면 제목
        GUI.Label(new Rect(100, 100, 1080, 40), $"{ChineseExamMenuCatalog.Names.Count}개 메뉴 중 두 개를 고르세요 (준비 중인 메뉴는 아직 시작할 수 없습니다)", body); // 고르는 방법 안내
        var cell = new GUIStyle(button) { fontSize = 21 }; // 메뉴 버튼 글꼴
        for (int i = 0; i < ChineseExamMenuCatalog.Names.Count; i++) // 20개 메뉴를 5열 4줄로 놓는다.
        {
            string dish = ChineseExamMenuCatalog.Names[i]; // 이 칸의 메뉴 이름
            var rect = new Rect(110 + (i % 5) * 216, 160 + (i / 5) * 86, 200, 72); // 칸 위치
            bool on = picked.Contains(dish); // 이미 고른 메뉴인지
            GUI.backgroundColor = on ? PickedColor : Color.white; // 고른 메뉴는 노랗게
            string text = Supported(dish) ? dish : dish + "\n(준비 중)"; // 미구현 메뉴는 준비 중 표시
            if (GUI.Button(rect, text, cell)) TogglePick(dish); // 누르면 고르거나 취소한다.
        }
        GUI.backgroundColor = Color.white; // 버튼 색을 원래대로
        GUI.Label(new Rect(100, 515, 1080, 50), pickedLabel, body); // 지금 고른 두 과제
        if (GUI.Button(new Rect(330, 600, 280, 60), "돌아가기", button)) picking = false; // 추첨 화면으로 돌아간다.
        bool ready = picked.Count == 2 && Supported(picked[0]) && Supported(picked[1]); // 두 개 다 골랐고 둘 다 조리 가능한지
        GUI.enabled = ready; // 시작할 수 없으면 버튼을 끈다.
        bool begin = GUI.Button(new Rect(670, 600, 280, 60), "이 두 과제로 시작", button); // 고른 두 과제로 진입한다.
        GUI.enabled = true; // 다른 GUI에 영향이 없게 되돌린다.
        if (!begin) return; // 시작을 누르지 않았으면 끝
        SelectedDishes = picked.ToArray(); // 고른 두 과제를 이번 시험 과제로 정한다.
        picking = false; // 고르는 화면을 닫는다.
        ContinueCooking(); // 조리 순서 화면으로 넘어간다.
    }

    private void TogglePick(string dish) // 메뉴 하나를 고르거나 취소한다.
    {
        if (!picked.Remove(dish)) // 이미 골랐던 메뉴면 취소
        {
            if (picked.Count == 2) picked.RemoveAt(0); // 두 개가 차 있으면 먼저 고른 것을 빼고
            picked.Add(dish); // 새 메뉴를 고른다.
        }
        UpdatePickedLabel(); // 안내 문구를 새로 만든다.
    }

    private void UpdatePickedLabel() // 고른 과제 안내 문구를 만든다 (매 프레임 만들지 않도록 바뀔 때만).
    {
        if (picked.Count < 2) { pickedLabel = $"선택: {string.Join(" · ", picked)}  ({picked.Count}/2)"; return; } // 아직 덜 고름
        bool ok = Supported(picked[0]) && Supported(picked[1]); // 둘 다 조리 가능한지
        pickedLabel = $"선택: {picked[0]} · {picked[1]}" + (ok ? "" : "  — 준비 중인 메뉴가 있어 아직 시작할 수 없습니다"); // 시작 가능 여부까지 알린다.
    }
}
