using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// The pre-start "순서도" mini-game. Before the exam timer begins, the player drags
/// 25 floating, semi-transparent step-card windows - covering both dishes' prep,
/// cutting, cooking and finishing work - into a 25-slot sequence board while the
/// examiner's announcements scroll in the background (placeholder text for now,
/// TTS later).
///
/// Ordering isn't checked against one strict answer key: each step only belongs to
/// a phase (준비 -> 사전 손질 -> 본조리 준비 -> 튀기기 -> 팬 마무리 -> 최종 점검), and a card is
/// only flagged wrong if it ends up placed AFTER another card from a later phase -
/// small reorderings within the same phase are always fine. "주변정돈" has no phase
/// and is always valid wherever it lands. A flagged card turns red; clicking it
/// explains which other step it needs to come before.
/// </summary>
public class CookingPlanManager : MonoBehaviour
{
    public static CookingPlanManager Instance { get; private set; }

    private const int FreePhase = -1;

    private readonly struct StepDef
    {
        public readonly string Label;
        public readonly int Phase;
        public StepDef(string label, int phase) { Label = label; Phase = phase; }
    }

    private static readonly StepDef[] Steps =
    {
        new StepDef("손씻기", 0),
        new StepDef("재료\n분리", 0),
        new StepDef("전분 2T 나눠\n앙금 만들기", 1),
        new StepDef("달걀\n분리", 1),
        new StepDef("고기\n키친타올", 1),
        new StepDef("다진고기\n키친타올", 1),
        new StepDef("목이버섯\n불리기", 1),
        new StepDef("데칠 물\n올리기", 1),
        new StepDef("두부\n썰기", 1),
        new StepDef("완두콩 데치고\n찬물 헹굼", 1),
        new StepDef("두부 데치고\n물기 제거", 1),
        new StepDef("기름\n예열", 2),
        new StepDef("고기 손질\n밑간", 2),
        new StepDef("다진고기\n다지기", 2),
        new StepDef("도마·칼\n세척", 2),
        new StepDef("채소\n썰기", 2),
        new StepDef("튀김반죽에\n고기 버무리기", 2),
        new StepDef("두번 튀긴 후\n키친타올", 3),
        new StepDef("고추기름\n만들기", 3),
        new StepDef("팬에 탕수육\n완성", 4),
        new StepDef("팬\n세척", 4),
        new StepDef("팬에 마파두부\n완성", 4),
        new StepDef("주변\n정돈", FreePhase),
        new StepDef("마무리\n정리", 5),          // (맨 마지막 - 최종 점검)
    };

    private const int GridCols = 5;
    private const int GridRows = 5;
    private const float CellW = 1.9f;
    private const float CellH = 1.05f;
    private const float GridOriginX = 1.0f;
    private const float GridTopY = 3.0f;

    private const float PoolLeft = -10.8f;
    private const float PoolRight = -0.6f;
    private const float PoolTop = 3.3f;
    private const float PoolBottom = -5.35f;

    private const float CrawlBandTop = 5.35f;
    private const float CrawlBandBottom = 4.15f;

    private const float PanelZ = -3f;

    private GameObject panel;
    private readonly PlanSlot[] slots = new PlanSlot[GridCols * GridRows];
    private readonly List<PlanStepCard> cards = new List<PlanStepCard>();

    private GameObject explainPopup;
    private TextMesh explainText;
    private Coroutine explainHideRoutine;

    private Transform crawlGroup;

    private readonly List<Renderer> hiddenLabelRenderers = new List<Renderer>();

    void Awake()
    {
        Instance = this;
    }

    void Start()
    {
        var selection = GetComponent<ChineseExamSelectionManager>(); // 메뉴 추첨 관리자를 재사용한다.
        if (selection == null) selection = gameObject.AddComponent<ChineseExamSelectionManager>(); // 같은 관리자 오브젝트에 추가한다.
        selection.Open(Show); // 두 과제를 선택한 뒤 기존 순서 화면을 연다.
    }

    void Update()
    {
        if (crawlGroup != null && panel != null && panel.activeSelf)
        {
            Vector3 p = crawlGroup.localPosition;
            p.y += Time.deltaTime * 0.3f;
            if (p.y > CrawlBandTop + 1.0f) p.y = CrawlBandBottom - 1.0f;
            crawlGroup.localPosition = p;
        }
    }

    /// <summary>The 순서도 screen is up (PlanAnnouncer speaks while it is).</summary>
    public bool IsShowing => panel != null && panel.activeSelf;

    public void Show()
    {
        if (panel == null) Build();
        AlignToCamera(panel.transform, 0.2f, 22.5f, 12.6f); // 현재 화면에 맞춘다.
        panel.SetActive(true);
        HideOtherLabels();
    }

    public void Hide()
    {
        if (panel != null) panel.SetActive(false);
        RestoreOtherLabels();
    }

    public void Toggle()
    {
        if (panel != null && panel.activeSelf) Hide();
        else Show();
    }

    // ---- Order hint popup (SequenceHintButton) ----

    private GameObject hintPanel;

    public void ToggleOrderHint()
    {
        if (hintPanel == null) BuildHintPanel();
        hintPanel.SetActive(!hintPanel.activeSelf);
        if (!hintPanel.activeSelf) return; // 닫을 때는 끝
        // 확대 중에도 지금 화면 가운데에, 화면에 맞는 크기로
        AlignToCamera(hintPanel.transform, 0f, 14f, 11f); // 힌트도 카메라 정면에 배치한다.
    }

    private void BuildHintPanel()
    {
        hintPanel = new GameObject("OrderHintPanel");
        hintPanel.transform.SetParent(transform, false);
        AlignToCamera(hintPanel.transform, 0f, 14f, 11f);

        CreateBackgroundQuad(hintPanel.transform, new Vector3(0f, 0f, 0.1f), new Vector2(14f, 11f), new Color(0.12f, 0.12f, 0.1f, 0.97f), false, false);
        CreateLabel(hintPanel.transform, "추천 조리 순서 (힌트)", new Vector3(0f, 4.6f, -0.05f), 32, new Color(1f, 0.85f, 0.3f));

        int half = Mathf.CeilToInt(Steps.Length / 2f);
        for (int i = 0; i < Steps.Length; i++)
        {
            int col = i < half ? 0 : 1;
            int row = i < half ? i : i - half;
            float x = col == 0 ? -5.6f : 0.8f;
            float y = 3.9f - row * 0.64f; // (13 rows a column now - clear of the 닫기 button)
            string line = (i + 1) + ". " + Steps[i].Label.Replace("\n", " ");
            TextMesh tm = CreateLabel(hintPanel.transform, line, new Vector3(x, y, -0.05f), 20, Color.white);
            tm.anchor = TextAnchor.MiddleLeft;
        }

        CreateButton(hintPanel.transform, "닫기", new Vector3(0f, -4.7f, -0.05f), () => hintPanel.SetActive(false), new Vector2(1.8f, 0.55f));

        hintPanel.SetActive(false);
    }

    /// <summary>Screen center at the panels' depth, so they stay centered
    /// wherever the camera has been framed.</summary>
    private static void AlignToCamera(Transform target, float yOffset, float width, float height) // 화면 기준 배치
    {
        Camera cam = Camera.main; // 현재 카메라
        if (cam == null) return; // 카메라 생성 전에는 기다린다.
        float depth = Mathf.Max(cam.nearClipPlane + 1f, 5f); // 주방 앞쪽에 표시한다.
        float halfHeight = cam.orthographic ? cam.orthographicSize : depth * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad); // 화면 높이
        float scale = Mathf.Min(halfHeight * 2f / height, halfHeight * 2f * cam.aspect / width) * 0.94f; // 화면 안에 여백을 두고 맞춘다.
        target.rotation = cam.transform.rotation; // 화면과 평행하게 맞춘다.
        target.localScale = Vector3.one * scale; // 모든 카드와 슬롯 크기를 함께 조정한다.
        target.position = cam.transform.position + cam.transform.forward * depth + cam.transform.up * (yOffset * scale); // 화면 중앙
    }

    // ---- Build ----

    private void Build()
    {
        panel = new GameObject("CookingPlanPanel");
        panel.transform.SetParent(transform, false);
        // The panel's content spans local y -6.5..6.1 (center -0.2).
        AlignToCamera(panel.transform, 0.2f, 22.5f, 12.6f);

        string dish1 = FindDishName("ExamPage_1", "탕수육");
        string dish2 = FindDishName("ExamPage_2", "마파두부");

        // Kept collider (unlike every other cosmetic quad here) - this is the one
        // thing standing between "clicking empty panel space" and the raycast
        // falling through to whatever kitchen object happens to sit behind the
        // panel, since this is a mandatory full-screen gate rather than an
        // optional overlay.
        CreateBackgroundQuad(panel.transform, new Vector3(0f, -0.2f, 0.1f), new Vector2(22.5f, 12.6f), new Color(0.16f, 0.18f, 0.22f), false, false);

        CreateLabel(panel.transform, $"{dish1} · {dish2} - 조리 순서 계획", new Vector3(0f, 5.9f, -0.15f), 30, Color.white);

        // (no scrolling announcement text: the announcements are voiced now - PlanAnnouncer)
        BuildSlots();
        BuildPool();

        CreateLabel(panel.transform,
            "카드를 슬롯으로 드래그해서 조리 순서를 배열하세요. 순서가 조금 달라도 괜찮지만, 먼저 해야 할 작업이 뒤로 가면 카드가 빨갛게 변해요 (클릭하면 이유가 나와요).",
            new Vector3(0f, -5.55f, -0.15f), 15, new Color(0.85f, 0.85f, 0.8f));

        CreateButton(panel.transform, "시작하세요", new Vector3(0f, -6.15f, -0.15f), OnStartClicked, new Vector2(3.0f, 0.7f));

        BuildExplainPopup();

        panel.SetActive(false);
    }

    private void BuildCrawl(string dish1, string dish2)
    {
        CreateBackgroundQuad(panel.transform, new Vector3(0f, 4.75f, -0.12f), new Vector2(21f, 1.3f), new Color(0.05f, 0.06f, 0.08f), false);

        string[] lines =
        {
            "감독위원 유의사항: 시험시간 미준수·위생상태 불량·의도적 행동 시 실격 처리됩니다.",
            $"오늘의 과제: {dish1} · {dish2} - 지급재료의 상태와 수량을 확인하세요.",
            "두 메뉴의 요구사항과 조리시간을 숙지한 뒤, 조리 순서를 계획하세요. (TTS 음성 안내 적용 예정)",
        };

        GameObject group = new GameObject("CrawlGroup");
        group.transform.SetParent(panel.transform, false);
        group.transform.localPosition = new Vector3(0f, CrawlBandBottom, -0.08f);

        for (int i = 0; i < lines.Length; i++)
        {
            CreateLabel(group.transform, lines[i], new Vector3(0f, i * 0.42f, 0f), 16, new Color(0.9f, 0.9f, 0.85f));
        }

        crawlGroup = group.transform;
    }

    private void BuildSlots()
    {
        for (int row = 0; row < GridRows; row++)
        {
            for (int col = 0; col < GridCols; col++)
            {
                int index = row * GridCols + col;
                float x = GridOriginX + col * CellW + CellW * 0.5f;
                float y = GridTopY - row * CellH - CellH * 0.5f;

                GameObject go = new GameObject("Slot_" + (index + 1));
                go.transform.SetParent(panel.transform, false);
                go.transform.localPosition = new Vector3(x, y, -0.05f);

                CreateBackgroundQuad(go.transform, new Vector3(0f, 0f, 0f),
                    new Vector2(CellW - 0.15f, CellH - 0.15f), new Color(1f, 1f, 1f, 0.12f), false, true);

                TextMesh numLabel = CreateLabel(go.transform, (index + 1).ToString(),
                    new Vector3(-(CellW - 0.15f) * 0.5f + 0.18f, (CellH - 0.15f) * 0.5f - 0.15f, -0.02f),
                    14, new Color(1f, 1f, 1f, 0.55f));
                numLabel.anchor = TextAnchor.MiddleCenter;

                PlanSlot slot = go.AddComponent<PlanSlot>();
                slot.Index = index;
                slots[index] = slot;
            }
        }
    }

    private void BuildPool()
    {
        // Laid out in the same reading order as Steps (left-to-right, top-to-
        // bottom) with no position scatter - randomizing position turns this
        // into a "find the card" hunt instead of the actual exercise, which is
        // deciding the right order. A small per-card tilt is kept (seeded, so
        // it's the same every time) purely for the "floating loose windows"
        // look - it never moves the card off its grid cell.
        int n = Steps.Length;
        const int cols = 5;
        const int rows = 5;
        float cellW = (PoolRight - PoolLeft) / cols;
        float cellH = (PoolTop - PoolBottom) / rows;

        System.Random rng = new System.Random(12345);

        for (int i = 0; i < n; i++)
        {
            int col = i % cols;
            int row = i / cols;
            float x = PoolLeft + col * cellW + cellW * 0.5f;
            float y = PoolTop - row * cellH - cellH * 0.5f;
            float rot = ((float)rng.NextDouble() - 0.5f) * 12f;

            Vector3 localPos = new Vector3(x, y, -0.15f);
            PlanStepCard card = CreateCard(Steps[i].Label, Steps[i].Phase, localPos, rot);
            cards.Add(card);
        }
    }

    private PlanStepCard CreateCard(string label, int phase, Vector3 localPos, float rotZ)
    {
        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Quad);
        go.name = "Card_" + label.Replace("\n", " ");
        go.transform.SetParent(panel.transform, false);
        go.transform.localPosition = localPos;
        go.transform.localRotation = Quaternion.Euler(0f, 0f, rotZ);
        go.transform.localScale = new Vector3(1.75f, 0.85f, 1f);

        Material mat = MakeMaterial(PlanStepCard.ValidColor, true);
        go.GetComponent<Renderer>().material = mat;

        TextMesh tm = CreateLabel(go.transform, label, new Vector3(0f, 0f, -0.05f), 20, Color.black);
        tm.fontStyle = FontStyle.Bold;
        tm.transform.localScale = new Vector3(1f / 1.75f, 1f / 0.85f, 1f);
        tm.transform.localRotation = Quaternion.identity;

        PlanStepCard card = go.AddComponent<PlanStepCard>();
        card.Init(this, label, phase, mat, localPos);
        return card;
    }

    private void BuildExplainPopup()
    {
        explainPopup = new GameObject("ExplainPopup");
        explainPopup.transform.SetParent(panel.transform, false);

        CreateBackgroundQuad(explainPopup.transform, Vector3.zero, new Vector2(4.6f, 0.9f), new Color(0.08f, 0.08f, 0.08f, 0.95f), false);
        explainText = CreateLabel(explainPopup.transform, "", new Vector3(0f, 0f, -0.05f), 16, Color.white);

        explainPopup.SetActive(false);
    }

    // ---- Drag / drop / validation ----

    /// <summary>Double-clicked card: into the first empty slot in number order,
    /// or - if it's already in a slot - back out to its pool spot.</summary>
    public void PlaceInNextSlot(PlanStepCard card)
    {
        if (card.SlotIndex >= 0 && slots[card.SlotIndex] != null && slots[card.SlotIndex].Occupant == card)
        {
            slots[card.SlotIndex].Occupant = null;
            card.ReturnToPool();
            Revalidate();
            return;
        }

        foreach (PlanSlot slot in slots)
        {
            if (slot.Occupant != null) continue;
            slot.Occupant = card;
            card.SetSlot(slot.Index);
            card.transform.localPosition = slot.transform.localPosition + new Vector3(0f, 0f, -0.03f);
            Revalidate();
            return;
        }
    }

    public void OnCardDropped(PlanStepCard card)
    {
        float bestDist = float.MaxValue;
        PlanSlot best = null;

        foreach (PlanSlot slot in slots)
        {
            float d = Vector3.Distance(slot.transform.localPosition, card.transform.localPosition);
            if (d < bestDist) { bestDist = d; best = slot; }
        }

        const float snapThreshold = 1.1f;

        if (best != null && bestDist <= snapThreshold)
        {
            if (best.Occupant != null && best.Occupant != card)
            {
                best.Occupant.ReturnToPool();
                best.Occupant = null;
            }

            if (card.SlotIndex >= 0 && slots[card.SlotIndex] != null && slots[card.SlotIndex].Occupant == card)
            {
                slots[card.SlotIndex].Occupant = null;
            }

            best.Occupant = card;
            card.SetSlot(best.Index);
            card.transform.localPosition = best.transform.localPosition + new Vector3(0f, 0f, -0.03f);
        }
        else if (card.SlotIndex >= 0 && slots[card.SlotIndex] != null)
        {
            // Missed every slot while already placed - settle back into the slot
            // it was already in rather than snapping all the way back to the pool.
            card.transform.localPosition = slots[card.SlotIndex].transform.localPosition + new Vector3(0f, 0f, -0.03f);
        }
        else
        {
            card.ReturnToPool();
        }

        Revalidate();
    }

    private void Revalidate()
    {
        int runningMax = -1;
        PlanStepCard runningMaxCard = null;

        for (int i = 0; i < slots.Length; i++)
        {
            PlanStepCard card = slots[i].Occupant;
            if (card == null) continue;

            if (card.Phase == FreePhase)
            {
                card.SetValidity(false, null);
                continue;
            }

            if (card.Phase < runningMax)
            {
                card.SetValidity(true, runningMaxCard);
            }
            else
            {
                card.SetValidity(false, null);
                if (card.Phase > runningMax)
                {
                    runningMax = card.Phase;
                    runningMaxCard = card;
                }
            }
        }
    }

    public void ShowExplanation(PlanStepCard card)
    {
        string cardLabel = card.Label.Replace("\n", " ");
        string blockerLabel = card.BlockedBy != null ? card.BlockedBy.Label.Replace("\n", " ") : "다음 작업";
        explainText.text = $"'{cardLabel}'은(는) '{blockerLabel}'보다 앞에 와야 해요.";

        explainPopup.transform.localPosition = card.transform.localPosition + new Vector3(0f, 0.85f, -0.2f);
        explainPopup.SetActive(true);

        if (explainHideRoutine != null) StopCoroutine(explainHideRoutine);
        explainHideRoutine = StartCoroutine(HideExplainAfterDelay());
    }

    private IEnumerator HideExplainAfterDelay()
    {
        yield return new WaitForSeconds(3.5f);
        explainPopup.SetActive(false);
    }

    private void OnStartClicked()
    {
        Hide();
        // The examiner's ingredient briefing (subtitles) first - the clock only
        // starts once it ends with "문제없다면 시험 시작하겠습니다."
        ExamBriefing.Run(() => CountdownTimer.Instance?.BeginCountdown());
    }

    // ---- Shared label-hiding (this panel's own text would otherwise get buried
    // under every other TextMesh in the scene, which all render in the same
    // Transparent queue regardless of actual depth) ----

    private void HideOtherLabels()
    {
        hiddenLabelRenderers.Clear();
        foreach (TextMesh tm in FindObjectsByType<TextMesh>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
        {
            if (tm.transform.IsChildOf(panel.transform)) continue;

            Renderer r = tm.GetComponent<Renderer>();
            if (r != null && r.enabled)
            {
                r.enabled = false;
                hiddenLabelRenderers.Add(r);
            }
        }
    }

    private void RestoreOtherLabels()
    {
        foreach (Renderer r in hiddenLabelRenderers)
        {
            if (r != null) r.enabled = true;
        }
        hiddenLabelRenderers.Clear();
    }

    // ---- Small helpers ----

    private string FindDishName(string examPageName, string fallback)
    {
        GameObject go = GameObject.Find(examPageName);
        ExamPaperFlip flip = go != null ? go.GetComponent<ExamPaperFlip>() : null;
        if (flip != null && !string.IsNullOrEmpty(flip.DishName)) return flip.DishName;
        return fallback;
    }

    private void CreateButton(Transform parent, string label, Vector3 localPos, System.Action onClick, Vector2 size)
    {
        GameObject btn = GameObject.CreatePrimitive(PrimitiveType.Quad);
        btn.name = "Btn_" + label;
        btn.transform.SetParent(parent, false);
        btn.transform.localPosition = localPos;
        btn.transform.localScale = new Vector3(size.x, size.y, 1f);

        btn.GetComponent<Renderer>().sharedMaterial = MakeMaterial(new Color(0.85f, 0.65f, 0.25f), false);

        TextMesh tm = CreateLabel(btn.transform, label, new Vector3(0f, 0f, -0.05f), 24, Color.black);
        tm.transform.localScale = new Vector3(1f / size.x, 1f / size.y, 1f);

        btn.AddComponent<SimpleActionButton>().Init(onClick);
    }

    private TextMesh CreateLabel(Transform parent, string text, Vector3 localPos, int fontSize, Color color)
    {
        GameObject go = new GameObject("Label");
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;

        TextMesh tm = go.AddComponent<TextMesh>();
        KoreanFontFallback.Apply(tm); // 웹에서도 한글이 보이게 한글 글꼴
        tm.text = text;
        tm.fontSize = fontSize;
        tm.characterSize = 0.1f;
        tm.anchor = TextAnchor.MiddleCenter;
        tm.color = color;
        return tm;
    }

    /// <summary>A non-clickable cosmetic quad. By default its collider is removed
    /// (so it never steals a raycast from whatever's actually in front of it);
    /// pass keepCollider=true for the one full-panel backdrop that must instead
    /// block clicks from falling through to the kitchen behind it.</summary>
    private GameObject CreateBackgroundQuad(Transform parent, Vector3 localPos, Vector2 size, Color color, bool transparent = false, bool destroyCollider = true)
    {
        GameObject go = GameObject.CreatePrimitive(PrimitiveType.Quad);
        go.name = "Bg";
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        go.transform.localScale = new Vector3(size.x, size.y, 1f);
        go.GetComponent<Renderer>().sharedMaterial = MakeMaterial(color, transparent || color.a < 1f);
        if (destroyCollider) Object.Destroy(go.GetComponent<Collider>());
        return go;
    }

    private static Material MakeMaterial(Color color, bool transparent)
    {
        Material mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        if (transparent)
        {
            mat.SetFloat("_Surface", 1f);
            mat.SetFloat("_Blend", 0f);
            mat.SetOverrideTag("RenderType", "Transparent");
            mat.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
            mat.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
            mat.SetInt("_ZWrite", 0);
            mat.DisableKeyword("_ALPHATEST_ON");
            mat.EnableKeyword("_ALPHABLEND_ON");
            mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            mat.renderQueue = (int)RenderQueue.Transparent;
        }
        mat.color = color;
        return mat;
    }
}
