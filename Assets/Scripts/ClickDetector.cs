using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Manual click + drag detection for the new Input System. Unity's legacy
/// OnMouseDown/OnMouseDrag never fire when Active Input Handling is set to
/// "Input System Package (New)" only, so this replaces both with explicit
/// raycasts driven off Mouse.current. A press that barely moves resolves as a
/// click; a press that moves past dragThreshold resolves as a drag - so the
/// same object (e.g. the wok) can support both "click to place" and "drag to
/// shake" without one gesture stealing the other's presses.
///
/// Press and release are checked independently (no early-return between them)
/// so a very quick click - press and release landing in the same frame - still
/// resolves; returning early after handling the press alone used to swallow
/// those clicks entirely.
/// </summary>
public class ClickDetector : MonoBehaviour
{
    [SerializeField] private Camera targetCamera;
    [SerializeField] private float maxDistance = 100f;
    [SerializeField] private float dragThreshold = 6f;

    private GameObject pressedTarget;
    private Vector2 pressStartPos;
    private Vector2 lastMousePos;
    private bool isDragging;
    private bool isPressing;
    private bool dragChecked;
    private IDraggable activeDraggable;
    private int raycastMask;

    /// <summary>Tools (ToolItem) are picked up and put down by press-and-hold
    /// (나이나이 레시피 style): a ring fills around the cursor for this long, and
    /// a plain click on an empty counter no longer drops the tool in hand - it
    /// stays held through use until deliberately held down somewhere.</summary>
    [SerializeField] private float holdSeconds = 0.4f;

    private enum HoldMode { None, PickUpTool, PutDownTool, UnfoldTowel }
    private HoldMode holdMode;
    private float holdTimer;
    private bool holdDone;
    private IClickable holdTarget; // the tool (or towel box) a pick-up hold acts on

    void Awake()
    {
        if (targetCamera == null) targetCamera = Camera.main;

        // The hand cursor model (HandCursor and its finger/palm/outline parts) sits
        // on the "Ignore Raycast" layer - without excluding it here, a click aimed
        // at whatever the hand happens to be resting on top of (including a held
        // item's intended target) can hit the hand's own collider first and resolve
        // to nothing, since the hand itself implements neither IClickable nor
        // IDraggable.
        raycastMask = ~(1 << LayerMask.NameToLayer("Ignore Raycast"));
    }

    void Update()
    {
        Mouse mouse = Mouse.current;
        if (mouse == null || targetCamera == null) return;

        // The cloth is out (정리 - wiping): its presses and drags are its own.
        // (A press on something else puts it away first - ClothWiper runs
        // before this - and is then taken here as usual.)
        // (Likewise the cutting board carried to the sink - BoardSinkWash.)
        if ((HandCarry.Instance != null && HandCarry.Instance.IsLocked) || ClothWiper.InputCaptured || BoardSinkWash.InputCaptured || BurnerGrate.InputCaptured)
        {
            HoldProgressRing.Hide();
            holdMode = HoldMode.None;
            pressedTarget = null;
            isPressing = false;
            isDragging = false;
            activeDraggable = null;
            return;
        }

        if (!mouse.leftButton.isPressed)
        {
            UpdateHoverHighlight(mouse.position.ReadValue());
        }

        // The cut menu is up and the click lands beside it: it just closes.
        if (mouse.leftButton.wasPressedThisFrame && CutMethodChooser.Instance != null && CutMethodChooser.Instance.IsOpen
            && !CutMethodChooser.Instance.IsOver(targetCamera.ScreenPointToRay(mouse.position.ReadValue())))
        {
            CutMethodChooser.Instance.Close();
            pressedTarget = null;
            isPressing = false;
            isDragging = false;
            activeDraggable = null;
            return;
        }

        if (mouse.leftButton.wasPressedThisFrame)
        {
            Vector2 screenPos = mouse.position.ReadValue();
            Ray ray = targetCamera.ScreenPointToRay(screenPos);

            pressedTarget = PickTarget(ray);
            // While something is in hand a press counts even over empty space,
            // so the held item can be dragged out from anywhere.
            isPressing = pressedTarget != null || HandCarry.Instance?.Current != null;
            pressStartPos = screenPos;
            lastMousePos = screenPos;
            isDragging = false;
            dragChecked = false;
            activeDraggable = null;

            // Hold to pick a tool up (empty hand, pressing on a tool), or to put
            // the tool in hand down (pressing on empty counter). A wok/pan/pot or
            // bowl is picked up the same way, but set down with a plain click.
            HandCarry hand = HandCarry.Instance;
            holdMode = HoldMode.None;
            holdTimer = 0f;
            holdDone = false;
            holdTarget = null;
            if (hand != null && hand.Current == null && pressedTarget != null)
            {
                // Pulling a kitchen-towel sheet from the tissue box takes the same
                // hold as picking up any other tool.
                ToolItem tool = pressedTarget.GetComponentInParent<ToolItem>();
                KitchenTowelDispenser box = pressedTarget.GetComponentInParent<KitchenTowelDispenser>();
                if (tool != null && !tool.IsHeld) holdTarget = tool;
                else if (box != null) holdTarget = box;
                else if (HeldVesselTarget(pressedTarget) is MovableBowl vessel) holdTarget = vessel;
                if (holdTarget != null) holdMode = HoldMode.PickUpTool;

                // A towel folded round a piece: held down it opens out; a plain
                // click carries the bundle (see the release below).
                if (pressedTarget.GetComponentInParent<TowelSpread>() is TowelSpread folded && folded.IsFolded)
                {
                    holdTarget = folded.UnfoldAction;
                    holdMode = HoldMode.UnfoldTowel;
                }
            }
            else if (hand != null && hand.Current is ToolItem heldTool && heldTool.KnifeLoad == null
                     && (pressedTarget == null || pressedTarget.GetComponentInParent<IClickable>() == null))
            {
                holdMode = HoldMode.PutDownTool;
            }
        }

        if (isPressing && mouse.leftButton.isPressed)
        {
            Vector2 pos = mouse.position.ReadValue();

            if (!dragChecked && Vector2.Distance(pos, pressStartPos) >= dragThreshold)
            {
                dragChecked = true;
                activeDraggable = StartDrag();
                isDragging = activeDraggable != null;
                if (isDragging) { holdMode = HoldMode.None; HoldProgressRing.Hide(); }
            }

            if (holdMode != HoldMode.None && !holdDone && !isDragging)
            {
                holdTimer += Time.deltaTime;
                HoldProgressRing.Show(targetCamera, pos, holdTimer / Mathf.Max(holdSeconds, 0.01f));
                if (holdTimer >= holdSeconds)
                {
                    holdDone = true;
                    HoldProgressRing.Hide();
                    if ((holdMode == HoldMode.PickUpTool || holdMode == HoldMode.UnfoldTowel) && holdTarget != null) holdTarget.OnClicked();
                    else if (holdMode == HoldMode.PutDownTool) DropHeldAt(pos);
                }
            }

            if (isDragging) activeDraggable.OnDragMove(pos - lastMousePos, Time.deltaTime);

            lastMousePos = pos;
        }

        if (isPressing && mouse.leftButton.wasReleasedThisFrame)
        {
            HoldProgressRing.Hide();
            if (isDragging)
            {
                activeDraggable.OnDragEnd();
            }
            else if (holdDone || (holdMode != HoldMode.None && holdMode != HoldMode.UnfoldTowel))
            {
                // The hold already did its pick-up/put-down - or was let go too
                // early, which does nothing (a tool isn't grabbed or dropped by a
                // quick click).
            }
            else
            {
                // Includes a press that wobbled past dragThreshold on something
                // that refused the drag - it still lands as the click it was.
                IClickable clickable = pressedTarget != null ? pressedTarget.GetComponentInParent<IClickable>() : null;
                if (clickable != null) clickable.OnClicked();
                else DropHeldAt(mouse.position.ReadValue());
            }

            pressedTarget = null;
            isPressing = false;
            isDragging = false;
            activeDraggable = null;
        }
    }

    /// <summary>Debug: what a press at this screen point would land on, and
    /// whether it'd start a press-and-hold pick-up.</summary>
    public string DescribePress(Vector2 screenPos)
    {
        GameObject target = PickTarget(targetCamera.ScreenPointToRay(screenPos));
        if (target == null) return "nothing";
        string hold = "-";
        if (HandCarry.Instance != null && HandCarry.Instance.Current == null)
        {
            ToolItem tool = target.GetComponentInParent<ToolItem>();
            KitchenTowelDispenser box = target.GetComponentInParent<KitchenTowelDispenser>();
            hold = tool != null && !tool.IsHeld ? "tool " + tool.name : box != null ? "towel box" : HeldVesselTarget(target) is MovableBowl v ? "vessel " + v.name : "-";
        }
        return $"{target.name} (clickable {target.GetComponentInParent<IClickable>()?.GetType().Name}) hold={hold}";
    }

    /// <summary>What a press lands on: the nearest collider, except that a stove
    /// knob anywhere under the cursor wins - a pot's long handle on the burner
    /// reaches over the knob, and pressing the knob kept grabbing the pot.</summary>
    private GameObject PickTarget(Ray ray)
    {
        RaycastHit[] hits = Physics.RaycastAll(ray, maxDistance, raycastMask);
        if (hits.Length == 0) return null;
        // The 순서도 screen up: only it takes clicks - the kitchen behind it
        // doesn't (the prepped plate behind the "완두콩 데치고 찬물 헹굼" card
        // took that card's clicks: picked over it as "a thing to pick up").
        if (CookingPlanManager.Instance != null && CookingPlanManager.Instance.IsShowing)
        {
            GameObject onPanel = null;
            float d = float.MaxValue;
            foreach (RaycastHit h in hits)
                if (h.distance < d && h.collider.transform.IsChildOf(CookingPlanManager.Instance.transform)) { d = h.distance; onPanel = h.collider.gameObject; }
            return onPanel;
        }
        RaycastHit nearest = hits[0];
        // Food (or the knife with scraps on it) in hand: the waste bag under the
        // cursor wins over the sink it sits in - the sink's big box took the
        // click and started washing the knife, scraps and all.
        if (HandCarry.Instance != null && WasteRules.HasFoodToDump(HandCarry.Instance.Current))
            foreach (RaycastHit h in hits)
                if (h.collider.GetComponentInParent<FoodWasteBag>() != null) return h.collider.gameObject;
        // (likewise a used paper towel and the general trash can)
        if (HandCarry.Instance != null && GeneralTrashCan.IsGeneralWaste(HandCarry.Instance.Current))
            foreach (RaycastHit h in hits)
                if (h.collider.GetComponentInParent<GeneralTrashCan>() != null) return h.collider.gameObject;
        // The cutting board leaning in the sink: it, not the sink's click plane in front of it.
        if (BoardSinkWash.Instance != null && BoardSinkWash.Instance.IsAway)
            foreach (RaycastHit h in hits)
                if (h.collider.GetComponent<BoardSinkWash>() != null) return h.collider.gameObject;
        // 정리, empty-handed: the burner's grate (any of its parts) - not the
        // burner's own click box in front of it.
        if (CleanupMode.IsOn && HandCarry.Instance != null && HandCarry.Instance.Current == null)
            foreach (RaycastHit h in hits)
                if (h.collider.GetComponent<BurnerGratePart>() != null) return h.collider.gameObject;
        // The cutting board only where it really shows: its box reaches over a
        // plate set beside it, and the plate's clicks (and food) went to the board.
        hits = CuttingBoardController.HitsShowing(ray, hits);
        if (hits.Length == 0) return null;
        nearest = hits[0];
        foreach (RaycastHit h in hits)
        {
            if (h.collider.GetComponentInParent<StoveKnob>() != null) return h.collider.gameObject;
            // A towel folded round a piece is clicked as the bundle, not the piece inside.
            if (h.collider.GetComponentInParent<TowelSpread>() is TowelSpread ts && ts.IsFolded) return h.collider.gameObject;
            if (h.distance < nearest.distance) nearest = h;
        }

        // Something resting in a bowl/plate beats the bowl's own box, which can
        // reach in front of it (an ingredient on the prepped plate was taken as
        // a click on the plate - refused outside clean-up).
        if (nearest.collider.GetComponentInParent<MovableBowl>() is MovableBowl bowl
            && nearest.collider.GetComponentInParent<Carryable>() == bowl)
        {
            foreach (RaycastHit h in hits)
            {
                Carryable c = h.collider.GetComponentInParent<Carryable>();
                if (c != null && c != bowl && ((object)c.CurrentContainer == bowl || c.transform.IsChildOf(bowl.transform))) return h.collider.gameObject;
            }
        }
        // Empty-handed, a thing to pick up beats the furniture it rests in: a
        // plate stored on a PlateShelf sits inside the shelf's box, which took
        // the click - the plates could never be taken back out.
        if (HandCarry.Instance != null && HandCarry.Instance.Current == null
            && nearest.collider.GetComponentInParent<Carryable>() == null)
        {
            RaycastHit? best = null;
            foreach (RaycastHit h in hits)
            {
                if (h.collider.GetComponentInParent<Carryable>() == null) continue;
                if (best == null || h.distance < best.Value.distance) best = h;
            }
            if (best != null) return best.Value.collider.gameObject;
        }
        return nearest.collider.gameObject;
    }

    /// <summary>The wok/pan/pot or bowl a press would pick up - held down
    /// like a tool. The pressed thing itself (not something sitting in it: an
    /// ingredient on a plate is taken with a plain click), or the vessel on a
    /// pressed burner. Null when the click wouldn't pick it up anyway.</summary>
    private static MovableBowl HeldVesselTarget(GameObject pressed)
    {
        Carryable c = pressed.GetComponentInParent<Carryable>();
        MovableBowl vessel = c as MovableBowl;
        if (c == null && pressed.GetComponentInParent<StoveBurner>() is StoveBurner burner) vessel = burner.CurrentVessel;
        // Plates (dishes) only move in clean-up mode, so there they're taken and
        // set down with a plain click - no hold.
        return vessel != null && !vessel.IsDish && vessel.WouldPickUp ? vessel : null;
    }

    /// <summary>A click on empty counter (nothing interactable under the cursor)
    /// while holding something sets it down right there.</summary>
    private void DropHeldAt(Vector2 screenPos)
    {
        HandCarry hand = HandCarry.Instance;
        if (hand == null || hand.Current == null) return;

        float depth = hand.Current.RestZ - targetCamera.transform.position.z;
        Vector3 world = targetCamera.ScreenToWorldPoint(new Vector3(screenPos.x, screenPos.y, depth));

        // Cut pieces on the knife's blade are slid off right here; the knife stays in hand.
        if (hand.Current is ToolItem knife && knife.KnifeLoad != null)
        {
            knife.DropLoadAt(world);
            return;
        }
        if (hand.PlaceFreeAt(world))
            HandController.Instance?.GoTo(world + new Vector3(0f, 0f, -0.55f), null);
    }

    /// <summary>Starts a drag on whatever was pressed; if that refuses (a
    /// Carryable while the hand already holds something else - the common case
    /// of pressing on the drop target itself while holding a bowl), the drag
    /// goes to the held item instead, so it follows the cursor and drops
    /// wherever it's released. Previously that press was swallowed entirely:
    /// neither a drag nor a click, and the bowl stayed stuck in hand.</summary>
    private IDraggable StartDrag()
    {
        IDraggable pressed = pressedTarget != null ? pressedTarget.GetComponentInParent<IDraggable>() : null;
        if (pressed != null && pressed.OnDragStart()) return pressed;

        IDraggable held = HandCarry.Instance != null ? HandCarry.Instance.Current : null;
        if (held != null && held != pressed && held.OnDragStart()) return held;

        return null;
    }

    /// <summary>Lights up whatever's under the cursor while idle (mouse not
    /// pressed) - a "this is what you're about to grab" cue shown before the
    /// click, distinct from DropHighlightRing's other job of marking a valid
    /// drop target while something is already being dragged. Reuses the same
    /// ring since the two situations never overlap (this only runs when the
    /// button is up; the drag-highlight only runs while it's held).</summary>
    private void UpdateHoverHighlight(Vector2 screenPos)
    {
        DropHighlightRing ring = DropHighlightRing.Instance;
        if (ring == null) return;

        Ray ray = targetCamera.ScreenPointToRay(screenPos);
        if (Physics.Raycast(ray, out RaycastHit hit, maxDistance, raycastMask))
        {
            Interactable target = hit.collider.GetComponentInParent<Interactable>();
            if (target != null)
            {
                ring.Show(target.transform);
                return;
            }
        }

        ring.Hide();
    }
}
