using UnityEngine;

public class HandCarry : MonoBehaviour
{
    public static HandCarry Instance { get; private set; }

    public Carryable Current { get; private set; }
    public bool IsDragging { get; private set; }

    /// <summary>True only for the single ForceActivate() call a drag-release
    /// resolves into - lets a drop target (e.g. the cutting board) tell a real
    /// aimed drag apart from a plain click-to-place, since a click carries no
    /// meaningful position (the item's transform just tracks the hand's fixed
    /// resting spot) while a drag's position is exactly where the player aimed.</summary>
    public bool DropIsFromDrag { get; set; }

    /// <summary>True while a scripted hand sequence (a bottle scooping into the
    /// measuring spoon) is playing - ClickDetector ignores input meanwhile, so
    /// a stray click can't set down or swap the item mid-animation.</summary>
    public bool IsLocked { get; set; }

    void Awake()
    {
        Instance = this;
    }

    /// <summary>Degrees the held item turns per mouse-wheel notch.</summary>
    [SerializeField] private float wheelSpinStep = 15f;

    void Update()
    {
        // Mouse wheel turns whatever is in hand around the screen axis
        // (나이나이 레시피 style) - the turn stays with it when it's set down.
        if (Current == null || IsLocked || UnityEngine.InputSystem.Mouse.current == null) return;

        float scroll = UnityEngine.InputSystem.Mouse.current.scroll.ReadValue().y;
        if (Mathf.Abs(scroll) > 0.01f) Current.Spin(Mathf.Sign(scroll) * wheelSpinStep);
    }

    void LateUpdate()
    {
        if (Current == null) return;

        // Rotation even mid-drag, so a wheel turn shows while dragging too.
        if (!IsDragging) Current.transform.position = transform.position + Current.HandHoldOffset;
        Current.transform.rotation = Current.HandRotation;
    }

    public void BeginDrag() => IsDragging = true;
    public void EndDrag() => IsDragging = false;

    /// <summary>Sets rotation and localScale so the item's actual WORLD transform
    /// comes out to its own BaseRotation at exactly BaseLocalScale, regardless of
    /// whatever rotation/scale the new parent (an anchor like the sink, tilted
    /// and non-uniformly scaled to sit flush in the counter) carries - the same
    /// "screen-aligned, own true size" treatment MovableBowl's ContentRoot and
    /// CuttingBoardController's IngredientRoot already give their own contents.
    /// Restoring BaseRotation specifically (not a blanket Quaternion.identity)
    /// matters because a few props - the knife, laid flat at its own 90-degree
    /// tilt - aren't authored with identity as their correct resting orientation;
    /// forcing identity on those left them standing on edge instead of lying flat
    /// every time they were set back down.
    /// Dividing by the parent's raw lossyScale only works when the parent has no
    /// rotation of its own: once it's rotated (e.g. the sink sits tilted 90 on
    /// Z), that rotation permutes which of the parent's own X/Y/Z scale factors
    /// ends up along which world axis - dividing by lossyScale's components
    /// directly then cancels the wrong axis entirely (found by testing: a bowl
    /// placed at the sink came out visibly stretched, not merely mis-scaled).
    /// Measuring how far the parent actually stretches this item's own
    /// (now-fixed) local axes via TransformVector sidesteps the permutation
    /// instead of having to reason about the parent's rotation.</summary>
    private static void ResetToBaseScale(Carryable item)
    {
        Transform t = item.transform;
        t.rotation = item.RestingRotation;

        Vector3 baseScale = item.BaseLocalScale;
        Transform parent = t.parent;
        if (parent == null)
        {
            t.localScale = baseScale;
            return;
        }

        Quaternion localRot = t.localRotation;
        float stretchX = parent.TransformVector(localRot * Vector3.right).magnitude;
        float stretchY = parent.TransformVector(localRot * Vector3.up).magnitude;
        float stretchZ = parent.TransformVector(localRot * Vector3.forward).magnitude;

        t.localScale = new Vector3(
            SafeDivide(baseScale.x, stretchX),
            SafeDivide(baseScale.y, stretchY),
            SafeDivide(baseScale.z, stretchZ));
    }

    private static float SafeDivide(float a, float b) => Mathf.Approximately(b, 0f) ? a : a / b;

    /// <summary>A hand-over inside one action (LeftHandCarry passing food to the
    /// right hand to set it down, the knife taken straight back) - not a real
    /// pickup: not recorded for hygiene.</summary>
    public static bool QuietPickUp;

    public bool PickUp(Carryable item)
    {
        if (Current != null || item == null || item.IsHeld) return false;
        if (KnifeFall.IsFalling(item)) return false; // (still falling - picked up once it's down)

        if (item.CurrentContainer != null)
        {
            item.CurrentContainer.Release(item);
            item.CurrentContainer = null;
        }

        Current = item;

        // Not parented under the hand: LateUpdate below already drives its
        // world position every frame regardless, and the hand model's own
        // scale is small and non-uniform (its mesh is authored that way) - as
        // a parent that would visibly stretch/squash whatever's held, in a way
        // dividing it back out couldn't reliably undo once the item's own
        // rotation differs from the hand's (lossyScale isn't exact under a
        // rotation mismatch). Left unparented, the item just keeps its own
        // scale and rotation untouched, which is simpler and always correct.
        item.transform.SetParent(null, true);
        item.transform.localScale = item.BaseLocalScale;
        item.OnPickedUp();
        if (!QuietPickUp) HandHygiene.PickedUp(item);
        return true;
    }

    /// <summary>Lets go of the held item right where it is, on nothing - for a
    /// tool briefly handing something over (FryingChopsticks swaps in the
    /// piece it pinched for one placement, then takes itself back).</summary>
    public Carryable Detach()
    {
        Carryable item = Current;
        if (item == null) return null;
        Current = null;
        item.OnPlaced();
        return item;
    }

    public bool PlaceAt(PlaceSlot slot)
    {
        if (Current == null || slot == null || slot.Current != null) return false;
        if (!slot.Accepts(Current)) return false;

        Carryable item = Current;
        item.transform.SetParent(slot.Anchor, false);
        item.transform.localPosition = Vector3.zero;
        ResetToBaseScale(item);
        slot.Current = item;
        item.CurrentContainer = slot;
        Current = null;
        item.OnPlaced();
        return true;
    }

    public bool PlaceAtTray(TraySlot tray)
    {
        if (Current == null || tray == null || tray.IsFull) return false;
        if (!tray.Accepts(Current)) return false;

        Carryable item = Current;
        Transform anchor = tray.ClaimAnchor();
        item.transform.SetParent(anchor, false);
        item.transform.localPosition = Vector3.zero;
        ResetToBaseScale(item);
        tray.AddItem(item);
        item.CurrentContainer = tray;
        Current = null;
        item.OnPlaced();
        return true;
    }

    /// <summary>Parks the held item at an arbitrary anchor without going through
    /// a PlaceSlot's own single-occupant tracking - used for a tool (like the
    /// knife) resting somewhere that must stay independent of whatever else
    /// that spot's owner is separately tracking (e.g. the cutting board's own
    /// Current, reserved for the ingredient being cut). The caller is
    /// responsible for linking the item's CurrentContainer itself.</summary>
    public bool PlaceAtAnchor(Transform anchor)
    {
        if (Current == null || anchor == null) return false;

        Carryable item = Current;
        item.transform.SetParent(anchor, false);
        item.transform.localPosition = Vector3.zero;
        ResetToBaseScale(item);
        Current = null;
        item.OnPlaced();
        return true;
    }

    /// <summary>Parks the held item at a specific local position under the given
    /// parent - used for slots that should keep the item roughly where the
    /// player actually dropped it, rather than snapping to one fixed spot.</summary>
    public bool PlaceAtFreeLocalPosition(Transform parent, Vector3 localPosition)
    {
        if (Current == null || parent == null) return false;

        Carryable item = Current;
        item.transform.SetParent(parent, false);
        item.transform.localPosition = localPosition;
        ResetToBaseScale(item);
        Current = null;
        item.OnPlaced();
        return true;
    }

    /// <summary>Sets the held item down right at the given spot (x/y; its own
    /// resting depth) on no container at all - a click or drop on an empty
    /// part of the counter, where nothing else would take it. Without this a
    /// bowl picked up off the counter (e.g. to fill at the faucet) could only
    /// ever go to the sink or a burner, and read as stuck in hand.</summary>
    public bool PlaceFreeAt(Vector3 worldPoint)
    {
        if (Current == null) return false;

        Carryable item = Current;
        item.transform.SetParent(null, true);
        if (item is IngredientItem || item is FoodScrap) worldPoint = OnCounter(item, worldPoint);
        item.transform.position = new Vector3(worldPoint.x, worldPoint.y, FreeDropZ(item, worldPoint.y));
        ResetToBaseScale(item);
        KeepAboveCounterEdge(item);
        item.CurrentContainer = null;
        Current = null;
        item.OnPlaced();
        return true;
    }

    /// <summary>Food set down loose stays on the counter top (Counter_R): set
    /// down past its edges - beyond the back wall, out over the sink's side,
    /// off the front - it ended up behind or under something and was gone.</summary>
    private static Vector3 OnCounter(Carryable item, Vector3 p)
    {
        if (!counterTop.HasValue)
        {
            GameObject c = GameObject.Find("Counter_R");
            Renderer r = c != null ? c.GetComponent<Renderer>() : null;
            counterTop = r != null ? new Rect(r.bounds.min.x, r.bounds.min.y, r.bounds.size.x, r.bounds.size.y) : Rect.zero;
        }
        Rect top = counterTop.Value;
        if (top.width <= 0f) return p;
        Vector3 ext = Vector3.zero;
        foreach (Renderer r in item.GetComponentsInChildren<Renderer>())
            if (r.enabled) ext = Vector3.Max(ext, r.bounds.extents);
        float yMin = Mathf.Max(top.yMin, CounterEdgeY()) + ext.y;
        p.x = Mathf.Clamp(p.x, top.xMin + ext.x, Mathf.Max(top.xMin + ext.x, top.xMax - ext.x));
        p.y = Mathf.Clamp(p.y, yMin, Mathf.Max(yMin, top.yMax - ext.y));
        return p;
    }

    private static Rect? counterTop;

    /// <summary>The counter top is tilted toward the camera, so its depth
    /// changes with height on screen: z = CounterZ0 + CounterSlope * y (fitted
    /// to where the plates rest on it).</summary>
    public const float CounterZ0 = 3.63f, CounterSlope = 1.22f;
    public static float CounterZ(float y) => CounterZ0 + CounterSlope * y;

    /// <summary>Depth for an item set down at height y. On the counter it follows
    /// the tilted top, keeping the item's own height above it - keeping its
    /// starting depth instead put an item carried forward (toward the board)
    /// underneath the counter top, where it vanished. Below the counter (the
    /// cabinet shelves) it keeps its own resting depth.</summary>
    private static float FreeDropZ(Carryable item, float y)
    {
        float edge = CounterEdgeY();
        if (y < edge - 0.3f) return item.RestZ;
        float lift = item.RestY >= edge - 0.3f ? item.RestZ - CounterZ(item.RestY) : 0f;
        return CounterZ(y) + Mathf.Clamp(lift, -0.8f, 0.1f);
    }

    private static float CounterEdgeY()
    {
        if (!counterEdgeY.HasValue)
        {
            GameObject lip = GameObject.Find("Counter_FrontLip");
            Renderer lr = lip != null ? lip.GetComponent<Renderer>() : null;
            counterEdgeY = lr != null ? lr.bounds.max.y : float.NegativeInfinity;
        }
        return counterEdgeY.Value;
    }

    private static float? counterEdgeY;

    /// <summary>Only the pots, pans and lids are kept under the counter (on the
    /// KitchenShelves) - anything else set down below the cutting board's
    /// counter is pushed back up onto it, its bottom at the counter's front
    /// edge. (Dishes go down to the PlateShelves only through
    /// DishShelfController, in clean-up mode.)</summary>
    private static void KeepAboveCounterEdge(Carryable item)
    {
        if (item is PotLid || KitchenShelfController.AllowsAnywhere(item)) return;
        CounterEdgeY();

        Renderer r = item.GetComponentInChildren<Renderer>();
        float bottom = r != null ? r.bounds.min.y : item.transform.position.y;
        if (bottom < counterEdgeY.Value)
            item.transform.position += new Vector3(0f, counterEdgeY.Value - bottom, 0f);
    }

    public bool PlaceAtBowl(MovableBowl bowl)
    {
        if (Current == null || bowl == null || bowl.IsFull) return false;
        if (!bowl.Accepts(Current)) return false;

        Carryable item = Current;
        Vector3 dropWorldPos = item.transform.position;
        bowl.PlaceFree(item, dropWorldPos);
        ResetToBaseScale(item);
        item.CurrentContainer = bowl;
        Current = null;
        item.OnPlaced();
        return true;
    }
}
