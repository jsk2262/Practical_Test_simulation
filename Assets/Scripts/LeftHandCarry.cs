using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 왼손으로 재료 옮기기 - with the knife kept in the right hand.
///
/// While the knife is in the right hand, a RIGHT click on an ingredient takes
/// it up in the LEFT hand (the knife isn't put down): the left hand reaches
/// over to it and closes round it, and it rides in that hand, following the
/// mouse; the right hand, knife and all, steps aside meanwhile (not through the
/// food). A right click where it should go sets it down there - the cutting
/// board (a free spot), a bowl or plate, a pot or pan - by the very same rules
/// as setting it down from the right hand (room on the board, what a bowl
/// takes and how much, ...): for that one moment it's handed to the right hand
/// and the place takes it the usual way, then the knife goes straight back.
/// Somewhere it can't go: it stays in the left hand. The right hand then comes
/// back to where it was.
///
/// Held down (right button) on cut food in a bowl: just one piece of it.
/// While it's carried, left clicks (cutting) do nothing (ClickDetector asks
/// IsCarrying); nothing starts while a cut or kneading is going on.
/// Without the knife in hand, right clicks do nothing here - picking things up
/// stays as it was.
/// </summary>
public class LeftHandCarry : MonoBehaviour
{
    public static LeftHandCarry Instance { get; private set; }

    /// <summary>Something's in the left hand (ClickDetector: no cutting meanwhile).</summary>
    public static bool IsCarrying => Instance != null && Instance.held != null;
    public static IngredientItem Held => Instance != null ? Instance.held : null;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Create()
    {
        if (Instance != null) return;
        new GameObject("LeftHandCarry").AddComponent<LeftHandCarry>();
    }

    [SerializeField] private float reachSeconds = 0.25f;
    [Tooltip("How far right (world) the knife hand steps aside while the left hand carries.")]
    [SerializeField] private float knifeAside = 1.1f;
    [Tooltip("Right button held this long on cut food in a bowl: one piece only.")]
    [SerializeField] private float onePieceHold = 0.45f;

    private IngredientItem held;
    private Vector3 gripOffset;      // the item's pivot from the left hand's knuckles
    private float reachT;            // 0..1: the hand going over to it and back with it
    private Vector3 reachFrom, carryAt;
    private bool rightAside;
    private Vector3 rightRest;

    private IngredientItem pressed;  // right button down on this
    private float pressTime;
    private TextMesh hint;

    void Awake() { Instance = this; }

    void OnDestroy() { if (Instance == this) Instance = null; }

    private static ToolItem KnifeInRightHand =>
        HandCarry.Instance != null && HandCarry.Instance.Current is ToolItem t && t.Kind == ToolKind.Knife && t.GetType() == typeof(ToolItem) ? t : null;

    /// <summary>Nothing else going on that the left hand is busy with or that
    /// this would cut across: a cut, kneading, a menu, a dialog.</summary>
    private static bool Free =>
        HandCarry.Instance != null && !HandCarry.Instance.IsLocked && !MeatKneader.InputCaptured
        && !ConfirmDialog.IsOpen && !ExamResultView.IsOpen
        && !(CutMethodChooser.Instance != null && CutMethodChooser.Instance.IsOpen) && !EggPartChooser.IsOpen;

    void Update()
    {
        Mouse mouse = Mouse.current;
        Camera cam = Camera.main;
        if (mouse == null || cam == null) return;
        Vector2 screen = mouse.position.ReadValue();
        bool overUi = UnityEngine.EventSystems.EventSystem.current != null && UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject();

        // the knife gone from the right hand (put down some other way) with food in the left: it's kept, still carried
        if (held == null) UpdateHint(cam, screen, overUi);
        else ShowHint(null, "");

        if (overUi) { pressed = null; return; }

        if (held == null)
        {
            if (KnifeInRightHand == null || !Free) { pressed = null; return; }
            if (mouse.rightButton.wasPressedThisFrame)
            {
                pressed = IngredientUnder(cam, screen);
                pressTime = Time.unscaledTime;
            }
            if (pressed != null && mouse.rightButton.isPressed && Time.unscaledTime - pressTime >= onePieceHold && CanTakeOnePiece(pressed))
            {
                // held down on cut food in a bowl: one piece of it
                IngredientItem one = pressed.SplitOffPiece(NearestPiece(pressed, cam, screen));
                pressed = null;
                if (one != null) TakeUp(one);
            }
            else if (pressed != null && mouse.rightButton.wasReleasedThisFrame)
            {
                IngredientItem what = pressed;
                pressed = null;
                if (what == IngredientUnder(cam, screen)) TakeUp(what);
            }
            return;
        }

        // carrying: following the mouse; a right click sets it down where it's pointed
        if (mouse.rightButton.wasPressedThisFrame && reachT >= 1f && Free) TrySetDown(cam, screen);
    }

    void LateUpdate()
    {
        if (held == null) return;
        Camera cam = Camera.main;
        Mouse mouse = Mouse.current;
        if (cam == null || mouse == null) return;

        // 1) the left hand reaching over to it (the food staying where it lies)
        if (reachT < 1f)
        {
            reachT = Mathf.Min(1f, reachT + Time.deltaTime / Mathf.Max(reachSeconds, 0.01f));
            HoldPose(Vector3.Lerp(reachFrom, carryAt, Mathf.SmoothStep(0f, 1f, reachT)));
            return;
        }
        // 2) then carried, following the cursor (eased - no jumps), over the counter's slope
        Vector2 sp = mouse.position.ReadValue();
        Ray ray = cam.ScreenPointToRay(sp);
        Plane plane = new Plane(cam.transform.forward, carryAt); // 카메라와 평행한 손의 현재 깊이
        Vector3 want = plane.Raycast(ray, out float distance) ? ray.GetPoint(distance) : carryAt; // 고정 월드 Z를 쓰지 않는다.
        carryAt = Vector3.Lerp(carryAt, want, 1f - Mathf.Exp(-14f * Time.deltaTime));
        HoldPose(carryAt);
        held.transform.position = carryAt + gripOffset;
    }

    // ---- taking up ----

    private void TakeUp(IngredientItem item)
    {
        // (not while a cut or kneading is going on - the left hand is in use then)
        if (item == null || item.IsHeld || held != null || !Free || KnifeInRightHand == null) return;
        // off wherever it was (the board's list, a bowl's) - put on the new place's when set down
        if (item.CurrentContainer != null)
        {
            item.CurrentContainer.Release(item);
            item.CurrentContainer = null;
        }
        item.transform.SetParent(null, true);
        item.OnPickedUp(); // (its colliders out of the way of clicks while carried; its pieces stop moving on their own)
        held = item;

        // the left hand reaches from where the food lies; grips it by its middle top
        Bounds b = item.VisibleBounds;
        Vector3 up = Carryable.CounterUp; // 재료의 실제 윗면을 쥔다.
        Vector3 ext = new Vector3(Mathf.Abs(up.x), Mathf.Abs(up.y), Mathf.Abs(up.z));
        Vector3 top = b.center + up * (Vector3.Dot(ext, b.extents) + 0.03f);
        gripOffset = item.transform.position - top;
        carryAt = top;
        reachFrom = top + new Vector3(-0.7f, -0.9f, 0f); // (coming in from the lower left - the left arm's side)
        reachT = 0f;
        HoldPose(reachFrom);

        // the knife hand steps aside, out of the food's way
        HandController hc = HandController.Instance;
        if (hc != null && !rightAside)
        {
            rightRest = hc.RestPosition;
            hc.SlideTo(rightRest + Vector3.right * knifeAside, 0.25f);
            rightAside = true;
        }
        // raw meat in the hand: on the hands now (only washing takes it off)
        if (item.Category == IngredientCategory.Meat && !(item.GetComponent<CookableItem>() is CookableItem ck && ck.CurrentStage != CookableItem.Stage.Raw))
            HandHygiene.TouchedRawMeat(BatterCoating.Of(item, false) is BatterCoating bc && bc.HasAny);
        Debug.Log($"[왼손] {item.IngredientName} 집음 (오른손 칼은 그대로)");
    }

    /// <summary>The left hand closed round it - more for a small piece, less for a big one.</summary>
    private void HoldPose(Vector3 at)
    {
        SupportHandController s = SupportHandController.Instance;
        if (s == null || held == null) return;
        Vector3 up = Carryable.CounterUp;
        Bounds b = held.VisibleBounds;
        float size = Mathf.Max(b.size.x, b.size.y);
        float curl = Mathf.Lerp(0.85f, 0.35f, Mathf.InverseLerp(0.1f, 0.6f, size));
        Vector3 fingers = Vector3.ProjectOnPlane(Camera.main != null ? Camera.main.transform.up : Vector3.forward, up).normalized; // (reaching down over it)
        Vector3 thumb = Quaternion.AngleAxis(90f, up) * Vector3.ProjectOnPlane(fingers, up).normalized;
        s.BeginHolding(fingers, thumb, curl);
        s.HoldKnucklesAt(at);
    }

    // ---- setting down ----

    private void TrySetDown(Camera cam, Vector2 screen)
    {
        HandCarry hand = HandCarry.Instance;
        ToolItem knife = KnifeInRightHand;
        Interactable target = PlaceUnder(cam, screen);
        if (target == null || knife == null || hand == null) return; // (nowhere it can go: still in the left hand)

        // For this one moment it's in the right hand, and the place takes it the
        // usual way - every rule of that place applies; then the knife comes back.
        IngredientItem item = held;
        item.OnPlaced();                 // (its own colliders back first - taken again by the right hand)
        hand.Detach();                   // the knife let go for a moment, where it is
        HandCarry.QuietPickUp = true;
        hand.PickUp(item);
        HandCarry.QuietPickUp = false;
        target.ForceActivate();          // the place's own set-down (no hand travel)
        bool placed = hand.Current != item;
        if (!placed) { hand.Detach(); item.OnPickedUp(); }   // it wouldn't take it: back in the left hand
        HandCarry.QuietPickUp = true;
        hand.PickUp(knife);              // the knife straight back in the right hand
        HandCarry.QuietPickUp = false;
        if (!placed) { Debug.Log($"[왼손] {item.IngredientName}: 여기엔 놓을 수 없음 - 계속 들고 있음"); return; }

        held = null;
        SupportHandController.Instance?.Hide();
        if (rightAside && HandController.Instance != null)
        {
            HandController.Instance.SlideTo(rightRest, 0.25f); // the knife back to where it was working
            rightAside = false;
        }
        Debug.Log($"[왼손] {item.IngredientName} 내려놓음 → {(item.CurrentContainer as Component)?.name ?? "?"}");
    }

    /// <summary>Where a right click lands: the board, a bowl/plate/pot/pan (or a
    /// burner with one on it), whatever's under the cursor that takes food - nearest first.</summary>
    private Interactable PlaceUnder(Camera cam, Vector2 screen)
    {
        Ray ray = cam.ScreenPointToRay(screen);
        foreach (RaycastHit h in CuttingBoardController.HitsShowing(ray, Physics.RaycastAll(ray, 200f)))
        {
            if (held != null && h.collider.transform.IsChildOf(held.transform)) continue;
            if (h.collider.GetComponentInParent<CuttingBoardController>() is CuttingBoardController board) return board;
            if (h.collider.GetComponentInParent<StoveBurner>() is StoveBurner burner) return burner.CurrentVessel;
            if (h.collider.GetComponentInParent<MovableBowl>() is MovableBowl bowl) return bowl;
            if (h.collider.GetComponentInParent<Interactable>() != null) return null; // (something else first: not here)
        }
        return null;
    }

    private static IngredientItem IngredientUnder(Camera cam, Vector2 screen)
    {
        Ray ray = cam.ScreenPointToRay(screen);
        foreach (RaycastHit h in CuttingBoardController.HitsShowing(ray, Physics.RaycastAll(ray, 200f)))
        {
            if (h.collider.GetComponentInParent<IngredientItem>() is IngredientItem ing && !ing.IsHeld) return ing;
            if (h.collider.GetComponentInParent<Interactable>() != null && h.collider.GetComponentInParent<IngredientItem>() == null
                && h.collider.GetComponentInParent<CuttingBoardController>() == null && h.collider.GetComponentInParent<MovableBowl>() == null) return null;
        }
        return null;
    }

    private static bool CanTakeOnePiece(IngredientItem ing) =>
        ing.GetComponent<FoodPieceGroup>() is FoodPieceGroup g && g.Count >= 2 && ing.Container is MovableBowl b && !b.HasFryingOil;

    private static FoodPiece NearestPiece(IngredientItem ing, Camera cam, Vector2 screen)
    {
        FoodPiece best = null;
        float d = float.MaxValue;
        foreach (FoodPiece p in ing.GetComponent<FoodPieceGroup>().Pieces)
        {
            if (p == null) continue;
            float dd = Vector2.Distance(screen, cam.WorldToScreenPoint(p.transform.position));
            if (dd < d) { d = dd; best = p; }
        }
        return best;
    }

    // ---- "우클릭: 왼손으로 집기" over the food the left hand could take ----

    private void UpdateHint(Camera cam, Vector2 screen, bool overUi)
    {
        IngredientItem over = !overUi && KnifeInRightHand != null && Free ? IngredientUnder(cam, screen) : null;
        ShowHint(over, over != null ? (CanTakeOnePiece(over) ? "우클릭: 왼손으로 집기 (꾹: 한 점)" : "우클릭: 왼손으로 집기") : "");
    }

    private void ShowHint(IngredientItem over, string text)
    {
        if (over == null)
        {
            if (hint != null) hint.gameObject.SetActive(false);
            return;
        }
        if (hint == null)
        {
            var go = new GameObject("LeftHandHint");
            hint = go.AddComponent<TextMesh>();
            KoreanFontFallback.Apply(hint); // 웹에서도 한글이 보이게 한글 글꼴
            hint.fontSize = 36;
            hint.characterSize = 0.05f;
            hint.anchor = TextAnchor.LowerCenter;
            hint.color = new Color(0.15f, 0.15f, 0.15f, 0.9f);
        }
        Bounds b = over.VisibleBounds;
        hint.text = text;
        hint.transform.position = new Vector3(b.center.x, b.max.y + 0.08f, b.min.z - 0.5f);
        hint.gameObject.SetActive(true);
    }
}
