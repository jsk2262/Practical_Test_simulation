using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 작업 영역 확대 + 확대 중 화면 이동. Main Camera(직교, 정면)에 붙는다 - 카메라는
/// 이것 하나: 위치와 orthographicSize만 바꾸고, 클릭 판정(ClickDetector)도 같은
/// 카메라로 레이를 쏘므로 클릭 위치와 실제 대상이 늘 맞다. 물건 크기·규격은 그대로.
///
/// 시점: 전체 보기 / 도마 포커스(도마 + 옆 재료 그릇) / 화구 포커스(그 화구 +
/// 팬 손잡이 자리 + 불조절 손잡이 + 옆 그릇).
/// - 빈손으로 도마·화구의 빈 곳(재료·도구·팬이 아닌 그 판 자체)을 더블클릭하면 그곳으로.
///   (빈손으로 그 판을 짧게 클릭하는 건 원래 아무 동작도 없어 첫 클릭이 다른 일을 하지
///   않는다) 운반·칼질·버무림·드래그 중, 창이 열려 있을 땐 받지 않는다.
/// - 확대 중 가운데 버튼 드래그: 카메라를 평행 이동 (시선·확대율 유지, 작업장 범위 안).
/// - '작업 위치로': 고른 영역의 안정된 기준점(화구/도마 자리 - 흔드는 팬이 아님)으로,
///   '전체 보기': 원래 화면으로. Esc는 다른 조작·창이 없을 때만 전체 보기.
/// - 화면 고정 UI(ETC 아래 버튼·타이머·감점칸)는 카메라를 따라가 같은 화면 자리·크기로.
/// - 전환은 실제 시간(unscaled)으로 부드럽게, 하나만 (새 요청이 이어받음). 전환·이동
///   중엔 월드 클릭을 잠시 막는다(ClickDetector가 BlocksWorldInput을 본다).
/// Time.timeScale은 건드리지 않는다 - 가열·시험 시간은 시점과 상관없이 흐른다.
/// </summary>
[DefaultExecutionOrder(-100)] // (Esc를 다른 조작보다 먼저 살펴서, 그쪽이 쓸 Esc면 양보)
[RequireComponent(typeof(Camera))]
public class KitchenCamera : MonoBehaviour
{
    public static KitchenCamera Instance { get; private set; }

    public enum View { Overview, Board, Burner }
    public View Current { get; private set; } = View.Overview;
    public static bool IsZoomed => Instance != null && Instance.Current != View.Overview;

    /// <summary>전환 중이거나 화면을 끌어 옮기는 중: 월드 클릭(집기·놓기·칼질...)을 받지 않는다.</summary>
    public static bool BlocksWorldInput => Instance != null && (Instance.transitioning || Instance.panning);

    [Header("확대 구도")]
    [Tooltip("확대 시 화면 너비 중 실제로 작업 대상을 담을 비율 (좌우의 화면 고정 버튼·타이머를 피해서).")]
    [SerializeField, Range(0.4f, 1f)] private float usableWidth = 0.62f;
    [Tooltip("확대 시 화면 높이 중 작업 대상을 담을 비율.")]
    [SerializeField, Range(0.5f, 1f)] private float usableHeight = 0.9f;
    [Tooltip("도마 포커스: 도마 둘레에서 이 거리(월드) 안의 재료 그릇·접시를 함께 담는다.")]
    [SerializeField] private float boardNeighborReach = 1.2f;
    [Tooltip("화구 포커스: 화구 둘레에서 이 거리 안의 그릇을 함께 담는다.")]
    [SerializeField] private float burnerNeighborReach = 1.6f;
    [Tooltip("화구 포커스의 기본 상자 (화구 중심 기준, 월드): 팬 몸통 + 오른쪽으로 뻗은 손잡이 자리.")]
    [SerializeField] private Vector2 burnerBoxMin = new Vector2(-1.3f, -1.1f), burnerBoxMax = new Vector2(2.0f, 1.1f);
    [Tooltip("확대 크기 제한 (orthographicSize). 화구: 팬 속 작은 조각이 구분될 만큼.")]
    [SerializeField] private float minZoomSize = 1.6f, maxBurnerSize = 2.0f, maxBoardSize = 3.8f;

    [Header("화면 이동 (가운데 버튼 드래그)")]
    [Tooltip("감도: 1이면 끈 만큼 (화면 기준) 그대로 따라 움직인다.")]
    [SerializeField] private float panSensitivity = 1f;
    [Tooltip("켜면 '잡아 끌기' (끄는 방향으로 작업장이 따라 움직임), 끄면 반대 (끄는 방향으로 카메라가 감).")]
    [SerializeField] private bool dragMovesContent = true;

    [Header("이동 범위 (월드, 비우면 작업장에서 자동)")]
    [Tooltip("카메라가 보여 줄 수 있는 범위 - 화면 가장자리까지 이 안. 아래쪽 보관 선반은 기본 구도에서만 빠지고, 내려 보는 건 된다.")]
    [SerializeField] private Rect panBounds;

    [Header("전환 · 더블클릭")]
    [SerializeField] private float transitionSeconds = 0.35f;
    [SerializeField] private float doubleClickSeconds = 0.32f;
    [SerializeField] private float doubleClickPixels = 14f;

    [Header("화면 고정 UI (ETC 아래, 이름) - 확대해도 같은 화면 자리·크기")]
    [SerializeField] private string[] hudNames =
    {
        "RecipesSauce", "Label_Timer", "Label_HygieneCount", "Label_SpecInfoButton", "Label_HintButton",
        "Label_SauceInfoButton", "CutSpecification", "Hint", "CleanupTab", "Label_CleanupTab",
        "SubmitButton", "SpeedModeButton",
    };

    private Camera cam;
    private Vector3 fullPos;
    private float fullSize;

    private bool transitioning, panning;
    private Vector3 fromPos, toPos;
    private float fromSize, toSize, t;

    private StoveBurner focusBurner;
    private StoveBurner pendingBurner;
    private StoveBurner[] zoomBurners;
    private Vector2 lastPanMouse;

    private float lastClickTime = -10f;
    private Vector2 lastClickPos;
    private Object lastClickZone;

    private struct Hud { public Transform t; public Vector3 pos, scale; }
    private readonly List<Hud> huds = new List<Hud>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void EnsureOnMainCamera()
    {
        // (보통은 씬의 Main Camera에 붙어 있다 - 없을 때만)
        Camera c = Camera.main;
        if (c != null && c.GetComponent<KitchenCamera>() == null) c.gameObject.AddComponent<KitchenCamera>();
    }

    void Awake()
    {
        Instance = this;
        cam = GetComponent<Camera>();
    }

    void OnEnable() { Instance = this; if (cam == null) cam = GetComponent<Camera>(); }

    void Start()
    {
        var allBurners = FindObjectsByType<StoveBurner>(FindObjectsSortMode.None);
        StoveBurner lower = null;
        foreach (StoveBurner b in allBurners)
            if (lower == null || b.transform.position.y < lower.transform.position.y) lower = b;
        zoomBurners = lower != null ? new[] { lower } : new StoveBurner[0];
        fullPos = transform.position;
        fullSize = cam.orthographic ? cam.orthographicSize : 6.8f;
        if (!cam.orthographic) Debug.LogWarning("[카메라] Main Camera가 Perspective예요 - 확대는 직교 카메라 기준으로 만들어졌어요");
        if (panBounds.width <= 0f || panBounds.height <= 0f) panBounds = WorkBounds();
        foreach (string n in hudNames)
        {
            GameObject g = GameObject.Find(n);
            if (g != null) huds.Add(new Hud { t = g.transform, pos = g.transform.position, scale = g.transform.localScale });
        }
    }

    void OnDisable()
    {
        // (꺼질 때 입력 잠금이 남지 않게)
        transitioning = panning = false;
        if (Instance == this) { transform.position = fullPos; if (cam != null && fullSize > 0f) cam.orthographicSize = fullSize; PlaceHuds(); }
    }

    void OnDestroy() { if (Instance == this) Instance = null; }

    /// <summary>작업장 범위: 조리대 전체 + 쓰레기통 + 제출대 (바깥 바닥·벽 제외).</summary>
    private static Rect WorkBounds()
    {
        bool any = false;
        Bounds b = default;
        foreach (string n in new[] { "Station3D", "TrashCan", "SubmitShelf" })
        {
            GameObject g = GameObject.Find(n);
            if (g == null) continue;
            foreach (Renderer r in g.GetComponentsInChildren<Renderer>())
            {
                if (!r.enabled || r is ParticleSystemRenderer || r.GetComponent<TextMesh>() != null) continue;
                if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
            }
        }
        return any ? Rect.MinMaxRect(b.min.x - 0.3f, b.min.y - 0.2f, b.max.x + 0.3f, b.max.y + 0.3f) : new Rect(-12f, -6f, 24f, 14f);
    }

    // ---- 요청 ----

    public void ShowOverview()
    {
        pendingBurner = null;
        Go(View.Overview, fullPos, fullSize);
    }

    /// <summary>확대 버튼 요청. 손동작과 입력이 끝난 뒤 선택한 화구로 이동.</summary>
    public void RequestBurnerFocus(StoveBurner burner)
    {
        if (!CanZoomBurner(burner)) return;
        pendingBurner = burner;
    }

    private void UpdateRequestedFocus(Mouse mouse)
    {
        if (pendingBurner == null || transitioning || panning || mouse.leftButton.isPressed
            || SomethingOpenOrBusy() || ExamBriefing.IsPlaying) return;
        HandCarry hand = HandCarry.Instance;
        if (hand != null && hand.IsDragging) return;
        if (HandController.Instance != null && HandController.Instance.IsMoving) return;
        CookingPlanManager plan = CookingPlanManager.Instance;
        if (plan != null && plan.IsShowing) return;
        StoveBurner burner = pendingBurner;
        pendingBurner = null;
        FocusBurner(burner);
    }

    /// <summary>'작업 위치로': 지금 고른 작업 영역의 기본 확대 자리 (안정된 기준점에서 다시 계산).</summary>
    public void BackToWork()
    {
        if (Current == View.Burner && focusBurner != null) FocusBurner(focusBurner);
        else if (Current == View.Board) FocusBoard();
    }

    public void FocusBoard()
    {
        CuttingBoardController board = CuttingBoardController.Instance;
        if (board == null) return;
        Rect r = RectOf(board.gameObject);
        Vector2 c = r.center;
        foreach (MovableBowl b in FindObjectsByType<MovableBowl>(FindObjectsSortMode.None))
        {
            if (b.IsHeld || b.IsCookingVessel || b.CurrentContainer is DishShelfController) continue;
            Rect br = RectOf(b.gameObject);
            if (Vector2.Distance(br.center, c) > boardNeighborReach + r.width * 0.5f) continue;
            if (br.center.y < r.yMin - 0.6f) continue; // (아래쪽 보관 선반은 빼고)
            r = Union(r, br);
        }
        Frame(View.Board, Pad(r, 0.35f), maxBoardSize);
    }

    private bool CanZoomBurner(StoveBurner burner) => burner != null && zoomBurners != null
        && System.Array.IndexOf(zoomBurners, burner) >= 0;

    public void FocusBurner(StoveBurner burner)
    {
        if (!CanZoomBurner(burner)) return;
        focusBurner = burner;
        // 기준: 화구 자리 (흔드는 팬이 아니라) + 손잡이 자리 상자 + 불조절 손잡이 + 옆 그릇
        Vector2 p = burner.transform.position;
        Rect r = Rect.MinMaxRect(p.x + burnerBoxMin.x, p.y + burnerBoxMin.y, p.x + burnerBoxMax.x, p.y + burnerBoxMax.y);
        foreach (StoveKnob k in FindObjectsByType<StoveKnob>(FindObjectsSortMode.None))
            if (k.Burner == burner) r = Union(r, RectOf(k.gameObject));
        foreach (MovableBowl b in FindObjectsByType<MovableBowl>(FindObjectsSortMode.None))
        {
            if (b.IsHeld || b.IsCookingVessel || b.CurrentContainer is DishShelfController) continue;
            Rect br = RectOf(b.gameObject);
            if (Vector2.Distance(br.center, p) <= burnerNeighborReach + 1f && br.center.y >= p.y - 2f) r = Union(r, br);
        }
        Frame(View.Burner, Pad(r, 0.2f), maxBurnerSize);
    }

    /// <summary>이 사각형이 화면의 쓸 수 있는 부분(좌우 고정 UI를 피한 가운데)에 다 들어오게.</summary>
    private void Frame(View view, Rect r, float maxSize)
    {
        float aspect = Mathf.Max(cam.aspect, 0.1f);
        float size = Mathf.Max(r.height * 0.5f / usableHeight, r.width * 0.5f / (aspect * usableWidth));
        size = Mathf.Clamp(size, minZoomSize, Mathf.Max(minZoomSize, Mathf.Min(maxSize, fullSize)));
        Vector3 pos = new Vector3(r.center.x, r.center.y, fullPos.z);
        Go(view, ClampToBounds(pos, size), size);
    }

    private void Go(View view, Vector3 pos, float size)
    {
        EggPartChooser.Close(); // (화면이 바뀌면 그릇 위 선택창은 닫는다)
        Current = view;
        fromPos = transform.position;
        fromSize = cam.orthographicSize;
        toPos = pos;
        toSize = size;
        t = 0f;
        transitioning = true; // (진행 중이던 전환은 지금 자리에서 이어받는다 - 겹치지 않음)
        panning = false;
    }

    // ---- 매 프레임 ----

    void Update()
    {
        Mouse mouse = Mouse.current;
        if (mouse == null || cam == null) return;
        Vector2 screen = mouse.position.ReadValue();

        if (transitioning)
        {
            t += Time.unscaledDeltaTime / Mathf.Max(transitionSeconds, 0.01f);
            float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t));
            transform.position = Vector3.Lerp(fromPos, toPos, k);
            cam.orthographicSize = Mathf.Lerp(fromSize, toSize, k);
            if (t >= 1f) { transitioning = false; BringHandIntoView(); }
        }

        // Esc: 다른 조작·창이 쓰는 Esc면 양보, 아무것도 없을 때만 전체 보기
        if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame && IsZoomed && !SomethingOpenOrBusy())
            ShowOverview();

        UpdateRequestedFocus(mouse);
        UpdatePan(mouse, screen);
        UpdateDoubleClick(mouse, screen);
    }

    void LateUpdate() => PlaceHuds();

    private void UpdatePan(Mouse mouse, Vector2 screen)
    {
        if (mouse.middleButton.wasPressedThisFrame && IsZoomed && !transitioning && !OverUi(screen)
            && !mouse.leftButton.isPressed && !SomethingOpenOrBusy()
            && (HandCarry.Instance == null || !HandCarry.Instance.IsDragging))
        {
            panning = true;
            lastPanMouse = screen;
        }
        if (!panning) return;
        if (!mouse.middleButton.isPressed || !IsZoomed) { panning = false; BringHandIntoView(); return; }
        // 화면 픽셀 → 월드: 화면 높이에 대한 비율로 (해상도·프레임과 무관하게 끈 거리만큼)
        Vector2 delta = screen - lastPanMouse;
        lastPanMouse = screen;
        float worldPerPixel = cam.orthographicSize * 2f / Mathf.Max(cam.pixelHeight, 1);
        Vector3 move = new Vector3(delta.x, delta.y, 0f) * worldPerPixel * panSensitivity;
        Vector3 pos = transform.position + (dragMovesContent ? -move : move);
        transform.position = ClampToBounds(pos, cam.orthographicSize);
    }

    private void UpdateDoubleClick(Mouse mouse, Vector2 screen)
    {
        if (!mouse.leftButton.wasPressedThisFrame) return;
        if (OverUi(screen) || transitioning || panning || !CanFocusNow()) { lastClickZone = null; return; }
        Object zone = ZoneUnder(screen);
        if (zone == null) { lastClickZone = null; return; }
        float now = Time.unscaledTime;
        bool second = zone == lastClickZone && now - lastClickTime <= doubleClickSeconds
                      && Vector2.Distance(screen, lastClickPos) <= doubleClickPixels;
        if (!second)
        {
            lastClickZone = zone;
            lastClickTime = now;
            lastClickPos = screen;
            return;
        }
        lastClickZone = null;
        if (zone is StoveBurner b) FocusBurner(b);
        else FocusBoard();
    }

    /// <summary>누른 곳 맨 앞의 것이 도마·화구 판 자체(그 위의 재료·도구·팬·손잡이가 아니라)면 그 판.</summary>
    private Object ZoneUnder(Vector2 screen)
    {
        Ray ray = cam.ScreenPointToRay(screen);
        int mask = ~(1 << LayerMask.NameToLayer("Ignore Raycast"));
        RaycastHit[] hits = Physics.RaycastAll(ray, 200f, mask);
        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
        foreach (RaycastHit h in CuttingBoardController.HitsShowing(ray, hits))
        {
            Transform x = h.collider.transform;
            if (x.GetComponentInParent<Carryable>() != null || x.GetComponentInParent<StoveKnob>() != null) return null;
            if (x.GetComponentInParent<StoveBurner>() is StoveBurner burner) return burner;
            if (x.GetComponentInParent<CuttingBoardController>() is CuttingBoardController board) return board;
            if (x.GetComponentInParent<IClickable>() != null) return null; // (다른 버튼·물건이 먼저)
        }
        return null;
    }

    /// <summary>더블클릭으로 시점을 바꿔도 되는 때: 빈손, 진행 중인 조작·창 없음.</summary>
    private static bool CanFocusNow()
    {
        HandCarry hand = HandCarry.Instance;
        if (hand == null || hand.Current != null || hand.IsLocked || hand.IsDragging) return false;
        if (ExamBriefing.IsPlaying) return false;
        CookingPlanManager plan = FindFirstObjectByType<CookingPlanManager>();
        if (plan != null && plan.IsShowing) return false;
        return !SomethingOpenOrBusy();
    }

    /// <summary>창이 열려 있거나, 다른 조작이 마우스·Esc를 쓰는 중.</summary>
    private static bool SomethingOpenOrBusy()
    {
        if (ConfirmDialog.IsOpen || ExamResultView.IsOpen || EggPartChooser.IsOpen) return true;
        if (CutMethodChooser.Instance != null && CutMethodChooser.Instance.IsOpen) return true;
        if (HandCarry.Instance != null && HandCarry.Instance.IsLocked) return true; // (썰기·붓기 같은 애니메이션 중)
        return InputCapturedElsewhere();
    }

    private static bool InputCapturedElsewhere() =>
        MeatKneader.InputCaptured || ClothWiper.InputCaptured || BoardSinkWash.InputCaptured || BurnerGrate.InputCaptured || LeftHandCarry.IsCarrying;

    /// <summary>시점이 바뀐 뒤 손(과 든 물건)이 화면 밖에 쉬고 있으면, 화면 아래쪽
    /// 안으로 부드럽게 데려온다 (기존 손 이동 - 순간이동·관통 없이 따라옴).
    /// 조작 중(잠김·드래그·다른 조작)이거나 이미 보이면 그대로.</summary>
    private void BringHandIntoView()
    {
        HandController hc = HandController.Instance;
        HandCarry hand = HandCarry.Instance;
        if (hc == null || hc.IsMoving || (hand != null && (hand.IsLocked || hand.IsDragging)) || InputCapturedElsewhere()) return;
        float h = cam.orthographicSize, w = h * cam.aspect;
        Vector3 c = transform.position;
        Vector3 rest = hc.RestPosition;
        bool inView = rest.x > c.x - w * 0.9f && rest.x < c.x + w * 0.9f && rest.y > c.y - h * 0.95f && rest.y < c.y + h * 0.9f;
        if (inView) return;
        // (화면 아래 가운데에서 조금 위 - 작업 대상은 가리지 않게)
        Vector3 to = new Vector3(Mathf.Clamp(rest.x, c.x - w * 0.5f, c.x + w * 0.5f), c.y - h * 0.72f, rest.z);
        hc.SlideTo(to, 0.25f);
    }

    // ---- 화면 고정 UI ----

    /// <summary>ETC 버튼·타이머·감점칸: 확대율만큼 줄이고 카메라를 따라가 같은 화면 자리에.</summary>
    private void PlaceHuds()
    {
        if (cam == null || fullSize <= 0f) return;
        float k = cam.orthographicSize / fullSize;
        Vector3 c = transform.position;
        foreach (Hud h in huds)
        {
            if (h.t == null) continue;
            h.t.position = new Vector3(c.x + (h.pos.x - fullPos.x) * k, c.y + (h.pos.y - fullPos.y) * k, h.pos.z);
            h.t.localScale = h.scale * k;
        }
    }

    // ---- 조작 안내 · 버튼 (확대 중에만, 화면 왼쪽 아래 작게) ----

    private GUIStyle buttonStyle, hintStyle, overviewStyle, burnerStyle;

    // 화면 왼쪽 아래, '작업 위치로' 위 · '전체 보기' 아래로 크게 쌓아서 (화면 높이에 맞춰 커짐)
    private static float UiScale => Mathf.Max(1f, Screen.height / 900f);
    private static float NavW => 160f * UiScale;
    private static float NavH => 48f * UiScale;
    private Rect OverviewRect => new Rect(6f * UiScale, Screen.height - 6f * UiScale - NavH, NavW, NavH);
    private Rect BackRect => new Rect(6f * UiScale, OverviewRect.y - 6f * UiScale - NavH, NavW, NavH);
    private Rect HintRect => new Rect(6f * UiScale, BackRect.y - 24f * UiScale, 340f * UiScale, 22f * UiScale);

    /// <summary>이 화면 위치(픽셀, 아래가 0)가 확대 중 버튼 위인지 - 월드 클릭으로 넘기지 않게.</summary>
    private bool PlanIsShowing => FindFirstObjectByType<CookingPlanManager>() is CookingPlanManager plan && plan.IsShowing;
    private bool CameraButtonsVisible => !ConfirmDialog.IsOpen && !ExamResultView.IsOpen
        && !ExamBriefing.IsPlaying
        && !PlanIsShowing;

    [Tooltip("'화구확대' 버튼을 화구 중심에서 이만큼(월드) 아래에 - 위 화구 버튼이 두 화구 사이 틈에 오도록.")]
    [SerializeField] private float burnerButtonDrop = 0.72f;

    private bool TryBurnerButtonRect(StoveBurner burner, out Rect rect)
    {
        rect = default;
        if (burner == null || cam == null) return false;
        // A screen-sized button just below its burner (under the grate, out of the
        // pan's way); never scale the burner itself.
        Vector3 p = cam.WorldToScreenPoint(burner.transform.position + Vector3.down * burnerButtonDrop);
        float scale = Mathf.Max(1f, Screen.height / 900f);
        float w = 120f * scale, h = 38f * scale;
        if (p.z <= 0f || p.x < w * 0.5f || p.x > Screen.width - w * 0.5f
            || p.y < h || p.y > Screen.height - h) return false;
        rect = new Rect(p.x - w * 0.5f, Screen.height - p.y - h * 0.5f, w, h);
        return true;
    }

    public static bool IsOverButtons(Vector2 screen)
    {
        if (Instance == null || !Instance.CameraButtonsVisible) return false;
        Vector2 gui = new Vector2(screen.x, Screen.height - screen.y);
        if (IsZoomed && (Instance.BackRect.Contains(gui) || Instance.OverviewRect.Contains(gui))) return true;
        if (Instance.zoomBurners != null)
            foreach (StoveBurner b in Instance.zoomBurners)
                if (Instance.TryBurnerButtonRect(b, out Rect rect) && rect.Contains(gui)) return true;
        return false;
    }
    private static bool OverUi(Vector2 screen) =>
        IsOverButtons(screen)
        || (UnityEngine.EventSystems.EventSystem.current != null && UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject());

    void OnGUI()
    {
        if (!CameraButtonsVisible) return;
        if (buttonStyle == null)
        {
            buttonStyle = new GUIStyle(GUI.skin.button) { fontSize = 15 };
            hintStyle = new GUIStyle(GUI.skin.label) { fontSize = 13 };
            hintStyle.normal.textColor = new Color(0.15f, 0.15f, 0.15f, 0.85f);
        }
        bool previousEnabled = GUI.enabled;
        GUI.enabled = previousEnabled && !transitioning && !panning && !SomethingOpenOrBusy()
            && (HandCarry.Instance == null || !HandCarry.Instance.IsDragging);
        // '화구확대': 다른 화면 버튼(정리·제출·빠른 모드)과 같은 진회색 바탕 + 흰 글씨 -
        // 밝은 조리대 위에서 잘 보이고, 불·음식 색과 겹치지 않게
        if (burnerStyle == null)
        {
            burnerStyle = new GUIStyle(buttonStyle) { fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            foreach (GUIStyleState s in new[] { burnerStyle.normal, burnerStyle.hover, burnerStyle.active, burnerStyle.focused })
            {
                s.background = Texture2D.whiteTexture;
                s.textColor = Color.white;
            }
        }
        burnerStyle.fontSize = Mathf.RoundToInt(17f * UiScale);
        Color oldBg = GUI.backgroundColor;
        if (zoomBurners != null)
            foreach (StoveBurner b in zoomBurners)
                if (TryBurnerButtonRect(b, out Rect rect))
                {
                    bool hover = rect.Contains(Event.current.mousePosition);
                    GUI.backgroundColor = hover ? new Color(0.24f, 0.28f, 0.32f, 0.95f) : new Color(0.32f, 0.36f, 0.40f, 0.95f);
                    if (GUI.Button(rect, "화구확대", burnerStyle)) RequestBurnerFocus(b);
                }
        GUI.backgroundColor = oldBg;
        if (!IsZoomed) { GUI.enabled = previousEnabled; return; }
        hintStyle.fontSize = Mathf.RoundToInt(13f * UiScale);
        GUI.Label(HintRect, "휠 버튼을 누른 채 끌기: 화면 이동", hintStyle);
        int navFont = Mathf.RoundToInt(22f * UiScale);
        buttonStyle.fontSize = navFont;
        if (GUI.Button(BackRect, "작업 위치로", buttonStyle)) BackToWork();
        buttonStyle.fontSize = 15;
        if (overviewStyle == null)
        {
            overviewStyle = new GUIStyle(buttonStyle) { fontStyle = FontStyle.Bold };
            overviewStyle.normal.background = Texture2D.whiteTexture;
            overviewStyle.hover.background = Texture2D.whiteTexture;
            overviewStyle.active.background = Texture2D.whiteTexture;
            overviewStyle.focused.background = Texture2D.whiteTexture;
            overviewStyle.normal.textColor = new Color(0.12f, 0.09f, 0.03f);
            overviewStyle.hover.textColor = overviewStyle.normal.textColor;
            overviewStyle.active.textColor = overviewStyle.normal.textColor;
            overviewStyle.focused.textColor = overviewStyle.normal.textColor;
        }
        overviewStyle.fontSize = navFont;
        Color oldBackground = GUI.backgroundColor;
        GUI.backgroundColor = new Color(1f, 0.78f, 0.12f);
        if (GUI.Button(OverviewRect, "전체 보기", overviewStyle)) ShowOverview();
        GUI.backgroundColor = oldBackground;
        GUI.enabled = previousEnabled;
    }

    // ---- 도움 ----

    /// <summary>카메라 중심을 옮겨도 화면 전체가 작업장 범위 안에 있게 (범위보다 넓으면 가운데).</summary>
    private Vector3 ClampToBounds(Vector3 pos, float size)
    {
        float halfH = size, halfW = size * cam.aspect;
        Rect b = panBounds;
        pos.x = b.width > halfW * 2f ? Mathf.Clamp(pos.x, b.xMin + halfW, b.xMax - halfW) : b.center.x;
        pos.y = b.height > halfH * 2f ? Mathf.Clamp(pos.y, b.yMin + halfH, b.yMax - halfH) : b.center.y;
        pos.z = fullPos.z;
        return pos;
    }

    private static Rect RectOf(GameObject g)
    {
        bool any = false;
        Bounds b = new Bounds(g.transform.position, Vector3.zero);
        foreach (Renderer r in g.GetComponentsInChildren<Renderer>())
        {
            if (!r.enabled || r is ParticleSystemRenderer || r.GetComponent<TextMesh>() != null) continue;
            if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
        }
        return Rect.MinMaxRect(b.min.x, b.min.y, b.max.x, b.max.y);
    }

    private static Rect Union(Rect a, Rect b) =>
        Rect.MinMaxRect(Mathf.Min(a.xMin, b.xMin), Mathf.Min(a.yMin, b.yMin), Mathf.Max(a.xMax, b.xMax), Mathf.Max(a.yMax, b.yMax));

    private static Rect Pad(Rect r, float p) => Rect.MinMaxRect(r.xMin - p, r.yMin - p, r.xMax + p, r.yMax + p);
}
