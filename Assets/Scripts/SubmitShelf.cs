using UnityEngine;

/// <summary>
/// 제출대 (제출용 선반): 화면 오른쪽, 제출 버튼 위. 요리가 담긴 완성 접시
/// (PlatingPlate1/2 - PlatingDestination)를 요리째 들어 와서 올려 둔다 (칸 2개,
/// 위·아래). 제출 버튼(SubmitButton)을 누르면 여기 올라간 접시만 제출된 것으로
/// 보고 요리를 채점한 뒤 마무리 정리로 넘어간다 (DishEvaluation: 제출대에 없는 접시는 미제출).
/// 씬에 없으면 플레이 시작 때 제출 버튼과 타이머 사이에 저절로 만들어진다.
/// </summary>
public class SubmitShelf : Interactable, ISlotContainer
{
    public static SubmitShelf Instance { get; private set; }

    [SerializeField] private Transform[] slots = new Transform[2];
    private readonly MovableBowl[] held = new MovableBowl[2];

    [Tooltip("접시와 선반 판 사이 간격 (월드, 접시가 판 앞에 보이게).")]
    [SerializeField] private float surfaceGap = 0.05f;

    void Awake() => Instance = this;
    void OnEnable() => Instance = this;
    void OnDestroy() { if (Instance == this) Instance = null; }

    /// <summary>이 접시가 지금 제출대에 올라가 있는지.</summary>
    public static bool Holds(MovableBowl plate)
    {
        if (Instance == null || plate == null) return false;
        foreach (MovableBowl b in Instance.held) if ((object)b == plate) return true;
        return false;
    }

    /// <summary>올라가 있는 접시 수.</summary>
    public int Count { get { int n = 0; foreach (MovableBowl b in held) if (b != null) n++; return n; } }

    private static bool IsServedPlate(Carryable item) =>
        item is MovableBowl plate && plate.GetComponent<PlatingDestination>() is PlatingDestination destination
        && !destination.Submitted && ExamFlow.Current == ExamFlow.Phase.Cooking;

    private int FreeSlot()
    {
        // (마우스에 가까운 빈 칸, 없으면 첫 빈 칸)
        int best = -1;
        float bestD = float.MaxValue;
        Camera cam = Camera.main;
        Vector2 mouse = UnityEngine.InputSystem.Mouse.current != null ? UnityEngine.InputSystem.Mouse.current.position.ReadValue() : Vector2.zero;
        for (int i = 0; i < slots.Length && i < held.Length; i++)
        {
            if (held[i] != null || slots[i] == null) continue;
            float d = cam != null ? Vector2.Distance(mouse, cam.WorldToScreenPoint(slots[i].position)) : i;
            if (d < bestD) { bestD = d; best = i; }
        }
        return best;
    }

    public override bool CanAcceptDrop(Carryable item) => IsServedPlate(item) && FreeSlot() >= 0;

    protected override void Activate()
    {
        HandCarry hand = HandCarry.Instance;
        if (hand == null) return;
        if (!(hand.Current is MovableBowl plate)) return;
        if (!IsServedPlate(plate))
        {
            Debug.Log("[제출대] 완성 접시(PlatingPlate1/2)를 올려 주세요");
            return;
        }
        int slot = FreeSlot();
        if (slot < 0) { Debug.Log("[제출대] 자리가 없어요"); return; }
        if (!hand.PlaceAtFreeLocalPosition(slots[slot], Vector3.zero)) return;
        held[slot] = plate;
        plate.CurrentContainer = this;

        // 보이는 접시 가운데를 칸 가운데에, 선반 판 앞에 (접시 피벗은 가운데가 아닐 수 있다)
        Renderer r = plate.VisibleDishRenderer();
        if (r == null || !r.enabled) r = plate.GetComponentInChildren<Renderer>();
        if (r != null)
        {
            Vector3 c = r.bounds.center, at = slots[slot].position;
            plate.transform.position += new Vector3(at.x - c.x, at.y - c.y, 0f);
            float front = BoardFrontZ() - surfaceGap;
            float over = r.bounds.max.z - front;
            plate.transform.position -= new Vector3(0f, 0f, over);
        }
        Debug.Log($"[제출대] {plate.name} 올림 ({Count}/2)");
    }

    public void Release(Carryable item)
    {
        for (int i = 0; i < held.Length; i++) if ((object)held[i] == item) held[i] = null;
    }

    private float BoardFrontZ()
    {
        Collider col = GetComponent<Collider>();
        return col != null ? col.bounds.min.z : transform.position.z;
    }

    // ---- 씬에 없을 때 자동으로 만들기 ----

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Create()
    {
        // (보통은 씬에 미리 놓여 있다 - Tools > Kitchen > Setup Submit Shelf. 없을 때만)
        if (FindFirstObjectByType<SubmitShelf>() != null) return;
        Shader lit = Shader.Find("Universal Render Pipeline/Lit");
        Material Mat(Color c)
        {
            var m = new Material(lit != null ? lit : Shader.Find("Standard")) { color = c };
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            return m;
        }
        Build(Mat(BoardColor), Mat(FrameColor), Mat(LedgeColor));
    }

    public static readonly Color BoardColor = new Color(0.62f, 0.6f, 0.58f), FrameColor = new Color(0.35f, 0.34f, 0.33f), LedgeColor = new Color(0.4f, 0.39f, 0.38f);

    /// <summary>제출 버튼 위 ~ 타이머 아래에 제출대를 만든다 (플레이 중이든 에디터든).</summary>
    public static SubmitShelf Build(Material boardMat, Material frameMat, Material ledgeMat)
    {
        SubmitButton button = FindFirstObjectByType<SubmitButton>();
        CountdownTimer timer = FindFirstObjectByType<CountdownTimer>();
        if (button == null) return null;

        // 제출 버튼 위 ~ 타이머 아래, 버튼과 같은 너비
        Renderer br = button.GetComponent<Renderer>();
        Bounds bb = br != null ? br.bounds : new Bounds(button.transform.position, new Vector3(4f, 1.5f, 0f));
        float bottom = bb.max.y + 0.4f;
        float top = bottom + 5.6f;
        if (timer != null && timer.GetComponentInChildren<Renderer>() is Renderer tr) top = tr.bounds.min.y - 0.3f;
        float width = bb.size.x * 0.92f, height = Mathf.Max(2.5f, top - bottom);
        Vector3 center = new Vector3(bb.center.x, (top + bottom) * 0.5f, 5.6f);

        var root = new GameObject("SubmitShelf");
        root.transform.position = center;
        var box = root.AddComponent<BoxCollider>();
        box.size = new Vector3(width, height, 0.2f);
        var shelf = root.AddComponent<SubmitShelf>();

        void Part(string name, Vector3 local, Vector3 size, Material mat)
        {
            GameObject p = GameObject.CreatePrimitive(PrimitiveType.Cube);
            p.name = name;
            // (에디터에서 만들 땐 DestroyImmediate - Destroy는 플레이 중에만)
            if (Application.isPlaying) Object.Destroy(p.GetComponent<Collider>());
            else Object.DestroyImmediate(p.GetComponent<Collider>());
            p.transform.SetParent(root.transform, false);
            p.transform.localPosition = local;
            p.transform.localScale = size;
            p.GetComponent<Renderer>().sharedMaterial = mat;
        }
        // 판 + 테두리 + 칸 구분 선반 두 줄
        Part("Board", new Vector3(0f, 0f, 0.05f), new Vector3(width, height, 0.1f), boardMat);
        Part("FrameTop", new Vector3(0f, height * 0.5f, -0.05f), new Vector3(width, 0.1f, 0.12f), frameMat);
        float labelH = 0.55f;
        float slotArea = height - labelH;
        for (int i = 0; i < 2; i++)
        {
            // 위 칸 = 0, 아래 칸 = 1
            float cy = height * 0.5f - labelH - slotArea * (i + 0.5f) / 2f;
            Part($"Ledge{i + 1}", new Vector3(0f, cy - slotArea * 0.25f - 0.05f, -0.1f), new Vector3(width * 0.95f, 0.08f, 0.3f), ledgeMat);
            var slot = new GameObject($"Slot{i + 1}").transform;
            slot.SetParent(root.transform, false);
            slot.localPosition = new Vector3(0f, cy, -0.3f);
            shelf.slots[i] = slot;
        }

        // 제목 글씨 (제출 버튼 글씨와 같은 글꼴)
        var t = new GameObject("Label");
        t.transform.SetParent(root.transform, false);
        t.transform.localPosition = new Vector3(0f, height * 0.5f - labelH * 0.5f, -0.1f);
        var text = t.AddComponent<TextMesh>();
        KoreanFontFallback.Apply(text); // 웹에서도 한글이 보이게 한글 글꼴
        text.text = "제출대";
        text.fontSize = 60;
        text.characterSize = 0.08f;
        text.anchor = TextAnchor.MiddleCenter;
        text.alignment = TextAlignment.Center;
        text.color = new Color(0.15f, 0.15f, 0.15f);
        if (button.GetComponentInChildren<TextMesh>() is TextMesh bl && bl.font != null)
        {
            text.font = bl.font;
            t.GetComponent<MeshRenderer>().sharedMaterial = bl.font.material;
        }
        // (제출 버튼과 같은 부모 아래 - 화면 오른쪽 UI 묶음)
        root.transform.SetParent(button.transform.parent, true);
        return shelf;
    }
}
