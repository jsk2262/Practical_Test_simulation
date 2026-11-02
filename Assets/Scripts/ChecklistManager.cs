using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 조리 순서 체크리스트 (화면 왼쪽 정리 버튼 아래): 지금 단계의 할 일을 보여 주고,
/// 그 행동을 하면 하나씩 체크된다. 한 단계를 다 하면 사라지고 다음 단계가 나온다.
/// 숨기기/보이기 버튼으로 끄고 켤 수 있다. 0.5초마다 재료 · 그릇 상태를 보고 판단한다.
/// </summary>
public class ChecklistManager : MonoBehaviour
{
    public static ChecklistManager Instance { get; private set; } // 매니저 하나

    private class Item
    {
        public string text;      // 보여 줄 할 일
        public Func<bool> done;  // 했는지 판단
        public bool isChecked;   // 한 번 하면 계속 체크
    }

    private class Stage
    {
        public string title;                         // 단계 제목
        public readonly List<Item> items = new List<Item>(); // 할 일들
    }

    private readonly List<Stage> stages = new List<Stage>(); // 1~7단계
    private readonly Dictionary<string, List<IngredientItem>> byName = new Dictionary<string, List<IngredientItem>>(); // 재료 이름 → 재료들 (시작할 때 한 번만 찾음)
    private HandWashSequence handWash;   // 손 씻기
    private Cheesecloth cloth;           // 고추기름 거르는 면보
    private MovableBowl loinStart;       // 등심이 처음 담겨 있던 그릇 (재료 나누기 판단용)
    private MovableBowl eggStart;        // 달걀이 처음 담겨 있던 그릇
    private IngredientItem egg;          // 달걀
    private int stageIndex;              // 지금 보여 주는 단계
    private float nextCheck;             // 다음 확인 시각
    private bool visible = true;         // 보이기/숨기기
    [Tooltip("체크리스트 글자 (정리 탭 아래 3D 글자 - 씬에서 위치 · 크기 조절).")]
    [SerializeField] private TextMesh listText;
    [Tooltip("숨기기/보이기 버튼 글자.")]
    [SerializeField] private TextMesh toggleText;
    [Tooltip("한 줄에 들어갈 최대 글자 수 - 왼쪽 칸 폭에 맞춰 조절 (넘으면 다음 줄로).")]
    [SerializeField] private int maxCharsPerLine = 12;
    private string shownText = "";      // 화면에 그릴 글 (바뀔 때만 새로 만듦)

    void Awake() => Instance = this; // 매니저 등록

    void Start()
    {
        foreach (IngredientItem ing in FindObjectsByType<IngredientItem>(FindObjectsSortMode.None)) // 시작할 때 재료 한 번만 모음
        {
            if (!byName.TryGetValue(ing.IngredientName, out var list)) byName[ing.IngredientName] = list = new List<IngredientItem>(); // 이름별 목록
            list.Add(ing); // 추가
        }
        handWash = FindFirstObjectByType<HandWashSequence>(); // 손 씻기 찾아 둠
        cloth = FindFirstObjectByType<Cheesecloth>();         // 면보 찾아 둠
        loinStart = First("돼지등심")?.Container;             // 등심의 처음 그릇
        egg = First("달걀");                                  // 달걀
        eggStart = egg != null ? egg.Container : null;        // 달걀의 처음 그릇
        Build();                                              // 단계 · 할 일 만들기
        Refresh();                                            // 첫 글 만들기
    }

    // ---- 단계 (사용자 요청 순서 1~7) ----
    private void Build()
    {
        Stage s = Add("1. 손 씻기");                                                  // 가장 처음 무조건
        Do(s, "손 씻기", () => handWash != null && handWash.HandsWashed);              // 손 씻기 완료

        s = Add("2. 재료를 메뉴별로 나누기");                                          // 탕수육 · 마파두부
        Do(s, "탕수육 · 마파두부 재료를 접시에 따로", Sorted);                          // 등심과 두부가 서로 다른 새 접시에

        s = Add("3. 밑준비");                                                          // 오래 걸리는 것 먼저
        Do(s, "탕) 앙금녹말: 전분 + 물 저어 두기", StarchSlurryReady);                 // 전분물을 저었음
        Do(s, "탕) 등심 키친타올", () => Drained("돼지등심"));                           // 핏물 제거
        Do(s, "탕) 달걀 다른 접시에 따로", EggMoved);                                   // 달걀 옮김
        Do(s, "탕) 물 받아 목이버섯 불리기", () => Soaking("목이버섯"));                 // 불리는 중 이상
        Do(s, "탕) 냄비 물 올리기 (완두콩 · 두부 데칠 물)", WaterPotOnFire);             // 물 냄비가 켠 불 위에
        Do(s, "마) 다진고기 키친타올", () => Drained("돼지고기"));                       // 다진 고기 핏물 제거

        s = Add("4. 채소 손질 · 데치기");                                              // 썰기 · 데치기
        Do(s, "탕) 대파 · 양파 · 오이 · 당근 썰기", () => AllCut("대파") && AllCut("양파") && AllCut("오이") && AllCut("당근")); // 탕수육 채소
        Do(s, "탕) 완두콩 데치기 → 찬물", () => Cooked("완두콩"));                       // 데쳐짐
        Do(s, "탕) 목이버섯 뜯기", () => AllCut("목이버섯"));                            // 손으로 찢음
        Do(s, "마) 두부 1.5cm 주사위 썰기", () => AllCut("두부"));                       // 사각썰기
        Do(s, "마) 두부 데치기", () => Cooked("두부"));                                  // 데쳐짐
        Do(s, "마) 마늘 · 생강 · 홍고추 다지기", () => AllCut("마늘") && AllCut("생강") && AllCut("고추")); // 다지기

        s = Add("5. 고기 손질 · 고추기름");                                            // 고기 · 기름
        Do(s, "탕) 등심 4×1×1 썰기", () => AllCut("돼지등심"));                          // 썰기
        Do(s, "탕) 밑간 (간장 · 청주)", Marinated);                                      // 밑간함
        Do(s, "탕) 튀김반죽 (흰자 + 앙금) 버무리기", Battered);                          // 반죽 묻힘
        Do(s, "마) 고추기름 만들어 면보에 거르기", () => cloth != null && cloth.HasResidue); // 면보에 고춧가루가 걸러짐

        s = Add("6. 튀기기");                                                          // 탕수육 튀김
        Do(s, "튀김 기름 올리기", OilOnFire);                                          // 튀김 기름이 켠 불 위에
        Do(s, "반죽 넣어 기름 온도 확인", () => MovableBowl.BatterTestCount > 0);         // 반죽 조각으로 확인
        Do(s, "1차 튀김", () => FryPasses() >= 1);                                     // 한 번 튀김
        Do(s, "2차 튀김", () => FryPasses() >= 2);                                     // 두 번 튀김

        s = Add("7. 소스 · 제출");                                                     // 마무리
        Do(s, "탕수육 소스 버무려 제출", () => SubmittedWith("돼지등심"));                // 탕수육 담은 접시 제출
        Do(s, "마파두부 제출", () => SubmittedWith("두부"));                             // 마파두부 담은 접시 제출
    }

    private Stage Add(string title) { var s = new Stage { title = title }; stages.Add(s); return s; } // 단계 추가
    private static void Do(Stage s, string text, Func<bool> done) => s.items.Add(new Item { text = text, done = done }); // 할 일 추가

    // ---- 판단 ----
    private IngredientItem First(string name) => byName.TryGetValue(name, out var l) && l.Count > 0 ? l[0] : null; // 그 이름의 첫 재료

    private bool AllCut(string name) // 그 이름의 재료가 모두 손질(썰기 · 다지기 · 뜯기)됨
    {
        if (!byName.TryGetValue(name, out var l)) return true;                        // 없는 재료는 넘어감
        foreach (IngredientItem i in l) if (i != null && i.CurrentState != IngredientItem.State.Cut) return false; // 하나라도 안 썰었으면 아직
        return true;                                                                  // 다 썰었음
    }

    private bool Drained(string name) { var i = First(name); return i == null || i.BloodDrained || i.IsTowelDrained; } // 키친타올로 핏물 · 물기 뺌
    private bool Soaking(string name) { var i = First(name); return i == null || i.CurrentSoakState == IngredientItem.SoakState.Soaking || i.CurrentSoakState == IngredientItem.SoakState.Soaked; } // 불리는 중 · 다 불음
    private bool Cooked(string name) // 데쳐서 익음
    {
        var i = First(name);                                                          // 재료
        if (i == null) return true;                                                   // 없으면 넘어감
        var c = i.GetComponent<CookableItem>();                                       // 익힘 상태
        return c != null && c.CurrentStage >= CookableItem.Stage.Done;                // 다 익었으면
    }

    private bool Sorted() // 등심과 두부가 처음 그릇이 아닌, 서로 다른 그릇에 있음
    {
        var loin = First("돼지등심"); var tofu = First("두부");                         // 메뉴 대표 재료
        if (loin == null || tofu == null) return true;                                // 없으면 넘어감
        var a = loin.Container; var b = tofu.Container;                                // 지금 담긴 그릇
        return a != null && b != null && a != b && a != loinStart && b != loinStart;  // 둘 다 새 그릇, 서로 다름
    }

    private bool EggMoved() => egg == null || egg.Container != eggStart;              // 달걀을 깼거나(사라짐) 다른 접시로 옮김

    private bool StarchSlurryReady() // 전분 + 물을 저어 둠
    {
        foreach (PortionedCondiment p in FindObjectsByType<PortionedCondiment>(FindObjectsSortMode.None)) // 0.5초마다만 (매 프레임 아님)
            if (p.CurrentStage >= PortionedCondiment.Stage.Slurry) return true;        // 저어서 우유빛 이상
        return false;
    }

    private bool WaterPotOnFire() => AnyVessel(v => v.CanHoldWater && v.WaterAmount > 0f && v.CurrentBurner != null && v.CurrentBurner.IsOn); // 물 담은 냄비가 켠 불 위
    private bool OilOnFire() => AnyVessel(v => v.HasFryingOil && v.CurrentBurner != null && v.CurrentBurner.IsOn); // 튀김 기름이 켠 불 위

    private static bool AnyVessel(Func<MovableBowl, bool> ok) // 조건에 맞는 그릇이 있나
    {
        foreach (MovableBowl v in FindObjectsByType<MovableBowl>(FindObjectsSortMode.None)) if (ok(v)) return true; // 0.5초마다만
        return false;
    }

    private bool Marinated() { var i = First("돼지등심"); return i == null || i.HasMarinade; } // 밑간을 했음
    private bool Battered() { var i = First("돼지등심"); var b = i != null ? BatterCoating.Of(i, false) : null; return i == null || (b != null && b.HasAny); } // 반죽이 묻음

    private int FryPasses() // 튀긴 횟수 (조각마다 따로라 가장 많이 튀긴 것)
    {
        int n = 0;                                                                     // 최대 횟수
        foreach (BatterCoating b in FindObjectsByType<BatterCoating>(FindObjectsSortMode.None)) n = Mathf.Max(n, b.FryPasses); // 0.5초마다만
        return n;
    }

    private static bool SubmittedWith(string name) // 그 재료가 담긴 접시를 제출함
    {
        foreach (PlatingDestination d in PlatingDestination.All)                      // 담는 접시들
        {
            if (d == null || !d.Submitted || d.Plate == null) continue;                // 제출한 것만
            foreach (IngredientItem i in d.Plate.GetComponentsInChildren<IngredientItem>()) // 담긴 재료
                if (i.IngredientName == name) return true;                             // 그 재료가 있으면
        }
        return false;
    }

    // ---- 갱신 · 화면 ----
    void Update()
    {
        if (Time.time < nextCheck || stageIndex >= stages.Count) return;              // 0.5초마다만 확인
        nextCheck = Time.time + 0.5f;                                                  // 다음 확인 시각
        bool changed = false;                                                          // 글을 다시 만들지
        foreach (Item it in stages[stageIndex].items)                                  // 지금 단계의 할 일
            if (!it.isChecked && it.done()) { it.isChecked = true; changed = true; }   // 했으면 체크 (한 번 체크되면 유지)
        bool all = true;                                                               // 단계 다 했나
        foreach (Item it in stages[stageIndex].items) if (!it.isChecked) { all = false; break; } // 하나라도 남으면 아직
        if (all) { stageIndex++; changed = true; }                                     // 다 했으면 다음 단계
        if (changed) Refresh();                                                        // 바뀔 때만 글 새로 만듦
    }

    private void Refresh() // 화면에 그릴 글 만들기
    {
        if (stageIndex >= stages.Count) { shownText = ""; Show(); return; }                   // 다 끝나면 비움 (사라짐)
        Stage s = stages[stageIndex];                                                  // 지금 단계
        var sb = new System.Text.StringBuilder();                                      // 글 모으기
        Wrap(sb, "", s.title);                                                          // 제목 (길면 줄바꿈)
        foreach (Item it in s.items) Wrap(sb, it.isChecked ? "☑ " : "☐ ", it.text);     // 할 일마다 체크 표시 (길면 줄바꿈)
        shownText = sb.ToString().TrimEnd();                                           // 완성
        Show();                                                                        // 화면 글자에 반영
    }

    /// <summary>한 줄을 maxCharsPerLine 글자에서 띄어쓰기 기준으로 끊어 붙인다 (3D 글자는 스스로 줄바꿈을 못 함).</summary>
    private void Wrap(System.Text.StringBuilder sb, string head, string text)
    {
        string indent = new string(' ', head.Length * 2);                              // 다음 줄은 체크 표시만큼 들여쓰기
        int row = head.Length;                                                          // 지금 줄 글자 수
        sb.Append(head);                                                                // 체크 표시
        foreach (string word in text.Split(' '))                                        // 띄어쓰기 단위로
        {
            if (row > head.Length && row + 1 + word.Length > maxCharsPerLine)           // 이 단어가 넘치면
            {
                sb.Append('\n').Append(indent);                                         // 다음 줄로
                row = head.Length;                                                      // 줄 글자 수 처음부터
            }
            else if (row > head.Length) { sb.Append(' '); row++; }                      // 같은 줄이면 띄어쓰기
            sb.Append(word);                                                            // 단어
            row += word.Length;                                                         // 글자 수 더함
        }
        sb.Append('\n');                                                                // 항목 끝
    }


    /// <summary>숨기기/보이기 버튼 (ChecklistToggle)에서 부름.</summary>
    public void ToggleVisible()
    {
        visible = !visible; // 보이기 ↔ 숨기기
        Show();             // 화면 글자에 반영
    }

    private void Show() // 3D 글자에 지금 상태 반영 (바뀔 때만 부름)
    {
        if (listText != null) listText.text = visible ? shownText : "";                  // 숨기면 목록 비움
        if (toggleText != null) toggleText.text = visible ? "체크리스트 숨기기" : "체크리스트 보이기"; // 버튼 글자
    }
}
