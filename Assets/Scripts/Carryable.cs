using UnityEngine;
using UnityEngine.InputSystem;

public abstract class Carryable : Interactable, IDraggable
{
    // Tuned against the Gleechi hand model's own render bounds (roughly
    // Y -0.35..0.85, Z -0.29..0.41 relative to HandCursor) - the old (0, 0.9,
    // -0.15) predates that model and sat above/outside it entirely, which is
    // why held items floated out in front instead of resting in the hand.
    [SerializeField] private Vector3 handHoldOffset = new Vector3(0f, 0.35f, -0.25f);

    /// <summary>An extra offset layered on top of the base hand-hold position -
    /// HandCarry.LateUpdate reads this every frame same as the base offset, so
    /// animating this (see ToolItem.PlayChopMotion) moves the held item itself
    /// without fighting that per-frame position drive the way directly setting
    /// transform.position would.</summary>
    private Vector3 tempHoldOffset;
    protected void SetTempHoldOffset(Vector3 offset) => tempHoldOffset = offset;

    /// <summary>Same idea as tempHoldOffset but for rotation - identity by
    /// default (so every other held item's look is unchanged), applied AFTER
    /// BaseRotation (world-space, on top of however the item already looks)
    /// so ToolItem's chop animation can temporarily tip the knife upright
    /// without touching how any other Carryable is held. Left-multiplied
    /// deliberately - BaseRotation * tempHoldRotation would apply the temp
    /// rotation first instead, which rotates around the item's own resting
    /// orientation rather than the intended world axis (verified empirically:
    /// applying the knife's calibrated upright delta this way matched the
    /// screenshot-checked pose; the other order didn't).</summary>
    private Quaternion tempHoldRotation = Quaternion.identity;
    protected void SetTempHoldRotation(Quaternion rotation) => tempHoldRotation = rotation;

    /// <summary>Turns it in hand for a moment (rinsed under the tap, the knife
    /// flipped to wash its other side); identity puts it back as it was held.</summary>
    public void SetHoldTurn(Quaternion turn) => SetTempHoldRotation(turn);
    public Quaternion HoldTurn => tempHoldRotation;

    /// <summary>Pans/pots with a long handle: while carried, held by the LEFT
    /// hand gripping the handle's end, still lying the way it rests, instead of
    /// floating in the right hand by the body.</summary>
    [SerializeField] private bool holdByHandle;

    public Vector3 HandHoldOffset => handHoldOffset + tempHoldOffset;

    /// <summary>The player's own turn of this item (mouse wheel while holding it,
    /// 나이나이 레시피 style; laying it lengthwise on the board) - kept while it's
    /// carried and when it's set down, until turned again.</summary>
    public Quaternion UserSpin { get; private set; } = Quaternion.identity;
    public bool HasUserSpin => UserSpin != Quaternion.identity;

    /// <summary>"Up" of the (tilted) counter top - things lying on it turn about
    /// this. Turning about the screen axis instead tipped the 3D ingredients over
    /// onto their sides (their models carry the counter's tilt).</summary>
    public static readonly Vector3 CounterUp = Quaternion.Euler(-41f, 0f, 0f) * Vector3.up;

    public void Spin(float degrees) => UserSpin = Quaternion.AngleAxis(degrees, CounterUp) * UserSpin;

    /// <summary>How this item rests when set down: its authored resting pose,
    /// turned by the player's spin.</summary>
    public Quaternion RestingRotation => UserSpin * BaseRotation;
    /// <summary>The hold offset without any temporary animation offset.</summary>
    protected Vector3 BaseHandHoldOffset => handHoldOffset;
    public Quaternion HandRotation => tempHoldRotation * RestingRotation;
    public bool IsHeld { get; private set; }
    public ISlotContainer CurrentContainer { get; set; }

    private Collider[] dragColliders;
    private bool isDraggingSelf;
    private bool isShakingInPlace;

    // The hand cursor model sits on "Ignore Raycast" (see ClickDetector) - without
    // excluding it here too, a drag ending right over where the hand happens to be
    // resting can hit the hand's own collider instead of the real drop target.
    private static int dropRaycastMask = -1;
    private static int DropRaycastMask =>
        dropRaycastMask != -1 ? dropRaycastMask : (dropRaycastMask = ~(1 << LayerMask.NameToLayer("Ignore Raycast")));

    /// <summary>Each item's own fixed local scale, captured once at startup. Re-parenting
    /// (hand <-> slot anchors, whose lossyScale differs per anchor) must always reset back
    /// to this exact value rather than letting Unity's SetParent(worldPositionStays: true)
    /// compensate for the new parent's scale - otherwise repeated pickup/place cycles ratchet
    /// the visual size larger (or smaller) every time.</summary>
    public Vector3 BaseLocalScale { get; private set; }

    /// <summary>Each item's own "lying correctly" world rotation, captured once at
    /// startup - most props are authored so identity rotation looks right, but a
    /// few (the knife, laid flat at a 90-degree tilt) aren't. Placing an item
    /// always restores this exact rotation rather than assuming identity, or
    /// those few end up standing on edge instead of lying flat every time
    /// they're set down.</summary>
    public Quaternion BaseRotation { get; private set; }

    /// <summary>Depth (world z) the item starts at - where it lands again when
    /// set down freely on an empty spot of the counter (HandCarry.PlaceFreeAt).</summary>
    public float RestZ { get; private set; }

    /// <summary>Height on screen (world y) the item starts at, paired with RestZ.</summary>
    public float RestY { get; private set; }

    protected virtual void Awake()
    {
        BaseLocalScale = ComputeUnparentedScale();
        BaseRotation = transform.rotation;
        RestZ = transform.position.z;
        RestY = transform.position.y;
    }

    /// <summary>transform.localScale alone is only the item's true (unparented)
    /// size when it happens to have no parent at Awake time - an item saved
    /// while resting at a tool anchor (a child of some other prop, possibly
    /// itself rotated/non-uniformly scaled) has a localScale that's already
    /// compensating for THAT parent, and capturing it as-is would corrupt
    /// BaseLocalScale permanently (every future pickup/reset then reapplies
    /// the wrong, still-compensated number with no parent left to compensate
    /// against - reproduced with the knife shrinking a little further every
    /// time it was picked up after being saved resting on the cutting board).
    /// Undoes exactly the stretch ResetToBaseScale would apply for the
    /// current parent, recovering the size the item would render at with no
    /// parent at all, regardless of what it's resting in when the scene loads.</summary>
    private Vector3 ComputeUnparentedScale()
    {
        Transform parent = transform.parent;
        if (parent == null) return transform.localScale;

        Vector3 localScale = transform.localScale;
        Quaternion localRot = transform.localRotation;
        float stretchX = parent.TransformVector(localRot * Vector3.right).magnitude;
        float stretchY = parent.TransformVector(localRot * Vector3.up).magnitude;
        float stretchZ = parent.TransformVector(localRot * Vector3.forward).magnitude;

        return new Vector3(localScale.x * stretchX, localScale.y * stretchY, localScale.z * stretchZ);
    }

    public virtual void OnPickedUp()
    {
        IsHeld = true;

        // Held items ride right in front of the camera (the hand's own fixed
        // offset), so their own collider can end up sitting between the camera
        // and whatever the player is actually trying to click next (a faucet,
        // another ingredient) - nothing needs to raycast-hit an item that's
        // already in hand (clicking it while held is a no-op, see Activate()),
        // so it can just stay out of the way until it's set back down.
        // Children too (a lid resting on a held pot would otherwise still catch
        // the next click meant for the drop target), and only the ones enabled
        // right now - restoring every collider on place used to wake a bowl's
        // permanently-disabled leftover SphereCollider (see OnDragStart), whose
        // oversized sphere then swallowed clicks and drops around the bowl.
        heldColliders = System.Array.FindAll(GetComponentsInChildren<Collider>(), c => c.enabled);
        foreach (Collider c in heldColliders) c.enabled = false;

        if (holdByHandle) BeginHandleGrip();
        else if (CarriedByLeftHand) BeginLeftCarry();
    }

    /// <summary>Where it was last set down from (call stack) - for the
    /// vanished-food check (IngredientVanishWatch) to say what put it there.</summary>
    public string LastPlacedBy { get; private set; }

    public virtual void OnPlaced()
    {
        if (this is IngredientItem || this is FoodScrap) LastPlacedBy = System.Environment.StackTrace;
        IsHeld = false;
        if (holdByHandle || CarriedByLeftHand) EndHandleGrip();
        if (heldColliders == null) return;
        foreach (Collider c in heldColliders) if (c != null) c.enabled = true;
        heldColliders = null;
    }

    /// <summary>Measured at pickup, while the item still sits at its resting
    /// pose: the handle is on the side the render bounds stick out toward
    /// (from the model's pivot, which sits at the body's center). It isn't
    /// turned (it stays lying as it rested); the offset shifts the body so the
    /// handle's end - not the body - sits at the hand's usual holding spot,
    /// and the left hand's knuckles are fitted right onto that end.</summary>
    private void BeginHandleGrip()
    {
        Renderer rend = GetComponent<Renderer>();
        if (rend == null) return;

        Bounds b = rend.bounds;
        // Snapped to the nearest screen axis - a pot tilted toward the camera
        // also shifts its bounds up/down a little, which would otherwise skew
        // a sideways handle into a diagonal.
        Vector2 dir = b.center - transform.position;
        Vector2 n = Mathf.Abs(dir.x) >= Mathf.Abs(dir.y)
            ? new Vector2(dir.x < 0f ? -1f : 1f, 0f)
            : new Vector2(0f, dir.y < 0f ? -1f : 1f);
        SetTempHoldRotation(Quaternion.identity);
        SetTempHoldOffset(-GripPointAlong(n, transform.rotation));

        HandController hand = HandController.Instance;
        if (hand == null) return;
        hand.SetVisualHidden(true);
        SupportHandController.Instance?.ShowGripping(hand.transform, BaseHandHoldOffset, n);
    }

    /// <summary>Where a hand closes on this item's far end along screenDir
    /// (a pan's handle, a utensil's grip), as an offset from the pivot with the
    /// item turned to the given rotation: the mesh's outermost vertices that way
    /// (the actual tip, which a tilted handle puts above/below the bounds'
    /// center line), pulled back a little so the fist sits on the handle
    /// rather than past its end.</summary>
    protected Vector3 GripPointAlong(Vector2 screenDir, Quaternion rotation)
    {
        MeshFilter mf = GetComponent<MeshFilter>();
        Vector3 dir = new Vector3(screenDir.x, screenDir.y, 0f).normalized;
        Vector3[] world;
        if (mf != null && mf.sharedMesh != null && mf.sharedMesh.isReadable)
        {
            Vector3[] verts = mf.sharedMesh.vertices;
            Vector3 scale = transform.lossyScale;
            world = new Vector3[verts.Length];
            for (int i = 0; i < verts.Length; i++) world[i] = rotation * Vector3.Scale(verts[i], scale);
        }
        else
        {
            // Mesh not readable: the render bounds' corners, turned to the target rotation.
            Renderer rend = GetComponent<Renderer>();
            if (rend == null) return Vector3.zero;
            Bounds b = rend.bounds;
            Quaternion turn = rotation * Quaternion.Inverse(transform.rotation);
            world = new Vector3[8];
            for (int i = 0; i < 8; i++)
            {
                Vector3 corner = b.center + Vector3.Scale(b.extents,
                    new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                world[i] = turn * (corner - transform.position);
            }
        }

        float max = float.MinValue, min = float.MaxValue;
        for (int i = 0; i < world.Length; i++)
        {
            float d = Vector3.Dot(world[i], dir);
            max = Mathf.Max(max, d);
            min = Mathf.Min(min, d);
        }

        float cut = max - (max - min) * 0.04f;
        Vector3 tip = Vector3.zero;
        int count = 0;
        for (int i = 0; i < world.Length; i++)
        {
            if (Vector3.Dot(world[i], dir) < cut) continue;
            tip += world[i];
            count++;
        }
        tip /= Mathf.Max(count, 1);
        tip -= dir * (max - min) * 0.06f;
        tip.z = 0f; // depth stays the item's own
        return tip;
    }

    public bool HoldsByHandle => holdByHandle;

    /// <summary>Plates and bowls are carried in the left hand, like the pots
    /// and pans (by their handles) - the right hand is kept for the tools.</summary>
    private bool CarriedByLeftHand => this is MovableBowl;

    private void BeginLeftCarry()
    {
        HandController hand = HandController.Instance;
        if (hand == null || SupportHandController.Instance == null) return;
        hand.SetVisualHidden(true);
        SupportHandController.Instance.ShowCarrying(hand.transform);
    }

    /// <summary>A long-handled pot set down somewhere it's still being tended
    /// (e.g. in the sink under the faucet): the left hand stays on its handle's
    /// end, following the pot, until it's picked up or the hand is needed elsewhere.</summary>
    public void ShowHandleHeldInPlace()
    {
        if (!holdByHandle) return;
        Renderer rend = GetComponent<Renderer>();
        if (rend == null) return;
        Vector2 dir = rend.bounds.center - transform.position;
        Vector2 n = Mathf.Abs(dir.x) >= Mathf.Abs(dir.y)
            ? new Vector2(dir.x < 0f ? -1f : 1f, 0f)
            : new Vector2(0f, dir.y < 0f ? -1f : 1f);
        SupportHandController.Instance?.ShowGripping(transform, GripPointAlong(n, transform.rotation), n);
    }

    private void EndHandleGrip()
    {
        SetTempHoldRotation(Quaternion.identity);
        SetTempHoldOffset(Vector3.zero);
        HandController.Instance?.SetVisualHidden(false);
        SupportHandController.Instance?.Hide();
    }

    private Collider[] heldColliders;

    protected sealed override void Activate()
    {
        HandCarry hand = HandCarry.Instance;
        if (hand == null) return;

        if (hand.Current == this) return;

        if (hand.Current == null)
        {
            if (!OnClickedWhileHandEmpty())
            {
                hand.PickUp(this);
            }
        }
        else
        {
            OnClickedWhileHandFull(hand.Current);
        }
    }

    protected virtual void OnClickedWhileHandFull(Carryable held)
    {
    }

    /// <summary>Return true to suppress the default pickup-on-click behavior.</summary>
    protected virtual bool OnClickedWhileHandEmpty()
    {
        return false;
    }

    // ---- Drag and drop ----
    // Picking something up and dragging it directly onto a destination (sink,
    // board, bowl, etc.) resolves in one motion, instead of needing a separate
    // click-to-pick-up then click-to-place. Release resolution just re-invokes
    // whatever the drop target's own click already does (ForceActivate), so
    // wash/wipe/cut-trigger/place logic all stay defined in exactly one place.

    public bool OnDragStart()
    {
        // A wok/pan sitting on a lit burner is being shaken in place, not
        // picked up and carried - WokDragHandler and Carryable both implement
        // IDraggable on the same object, and only one can ever be found via
        // GetComponent, so this has to branch before the normal pickup-drag
        // path below even starts.
        WokDragHandler wok = GetComponent<WokDragHandler>();
        if (wok != null && this is MovableBowl vessel && vessel.CurrentBurner != null)
        {
            isShakingInPlace = true;
            wok.OnDragStart();
            return true;
        }

        HandCarry hand = HandCarry.Instance;
        if (hand == null) return false;

        if (hand.Current != this)
        {
            if (hand.Current != null) return false; // hand full with something else - can't drag this one

            if (OnClickedWhileHandEmpty()) return false; // e.g. soaked tear-only ingredients
            if (!hand.PickUp(this)) return false;
        }

        // else: already holding this item - re-drag it to redirect where it drops

        hand.BeginDrag();
        isDraggingSelf = true;

        // A bowl can carry a leftover disabled SphereCollider alongside the thin
        // BoxCollider we swap in for it (see MovableBowl's collider fix) - grabbing
        // only GetComponent<Collider>() would toggle the wrong (already-disabled)
        // one and leave the real, active collider blocking the drop raycast. Only
        // the ones actually enabled right now get disabled (and later restored),
        // so a permanently-disabled leftover collider doesn't get woken back up.
        // (and its own children's - a cut ingredient's pieces each have one)
        dragColliders = System.Array.FindAll(this is MovableBowl ? GetComponents<Collider>() : GetComponentsInChildren<Collider>(), c => c.enabled);
        foreach (Collider c in dragColliders) c.enabled = false;

        // If this is a container (e.g. a bowl) being dragged with something in it,
        // its contents ride along too - their colliders must go quiet as well, or a
        // raycast aimed at the drop destination hits an ingredient sitting on top of
        // the bowl instead, and the bowl (not the ingredient) ends up stuck in hand.
        if (this is MovableBowl selfBowl) selfBowl.SetContentsCollidersEnabled(false);
        return true;
    }

    public void OnDragMove(Vector2 mouseDelta, float deltaTime)
    {
        if (isShakingInPlace)
        {
            GetComponent<WokDragHandler>()?.OnDragMove(mouseDelta, deltaTime);
            return;
        }

        if (!isDraggingSelf) return;

        Camera cam = Camera.main;
        if (cam == null || Mouse.current == null) return;

        Vector2 screenPos = Mouse.current.position.ReadValue();
        float depth = transform.position.z - cam.transform.position.z;
        Vector3 world = cam.ScreenToWorldPoint(new Vector3(screenPos.x, screenPos.y, depth));
        transform.position = new Vector3(world.x, world.y, transform.position.z);

        UpdateDropHighlight(cam, screenPos);
    }

    private void UpdateDropHighlight(Camera cam, Vector2 screenPos)
    {
        DropHighlightRing ring = DropHighlightRing.Instance;
        if (ring == null) return;

        Ray ray = cam.ScreenPointToRay(screenPos);
        if (Physics.Raycast(ray, out RaycastHit hit, 100f, DropRaycastMask))
        {
            Interactable target = hit.collider.GetComponentInParent<Interactable>();
            if (target != null && (object)target != this && target.CanAcceptDrop(this))
            {
                ring.Show(target.transform);
                return;
            }
        }

        ring.Hide();
    }

    public void OnDragEnd()
    {
        if (isShakingInPlace)
        {
            isShakingInPlace = false;
            GetComponent<WokDragHandler>()?.OnDragEnd();
            return;
        }

        if (!isDraggingSelf) return;
        isDraggingSelf = false;

        DropHighlightRing.Instance?.Hide();

        HandCarry hand = HandCarry.Instance;
        hand?.EndDrag();

        // Raycast for the drop target BEFORE re-enabling this item's own collider -
        // otherwise, if this item's z-depth happens to be closer to the camera than
        // the actual target's, the raycast hits itself instead and the drop silently
        // no-ops (reproduced: dragging onto the sink, whose z sits behind raw-zone
        // ingredients, failed this way even though dragging onto the bowl did not).
        Camera cam = Camera.main;
        if (cam != null && Mouse.current != null)
        {
            Ray ray = cam.ScreenPointToRay(Mouse.current.position.ReadValue());
            // (nearest first - but the cutting board only where it really shows:
            // its box reaches over a plate set beside it, and took that plate's food)
            RaycastHit[] showing = CuttingBoardController.HitsShowing(ray, Physics.RaycastAll(ray, 100f, DropRaycastMask));
            bool hitSomething = showing.Length > 0;
            RaycastHit hit = hitSomething ? showing[0] : default;
            Interactable dropTarget = hitSomething ? hit.collider.GetComponentInParent<Interactable>() : null;
            // Food dragged onto the waste bag: the bag, not the sink round it.
            if (CleanupMode.IsOn && WasteRules.HasFoodToDump(this))
                foreach (RaycastHit h in Physics.RaycastAll(ray, 100f, DropRaycastMask))
                    if (h.collider.GetComponentInParent<FoodWasteBag>() is FoodWasteBag bag) { hit = h; hitSomething = true; dropTarget = bag; break; }
            if (GeneralTrashCan.IsGeneralWaste(this))
                foreach (RaycastHit h in Physics.RaycastAll(ray, 100f, DropRaycastMask))
                    if (h.collider.GetComponentInParent<GeneralTrashCan>() is GeneralTrashCan can) { hit = h; hitSomething = true; dropTarget = can; break; }

            // A pot/pan carried by its handle has the cursor at the handle's end,
            // well off its body - aimed by its body (onto a burner, into the sink),
            // so what's under the body wins when it takes the drop.
            if (holdByHandle && this is MovableBowl self)
            {
                Ray bodyRay = cam.ScreenPointToRay(cam.WorldToScreenPoint(self.BodyCenterWorld()));
                if (Physics.Raycast(bodyRay, out RaycastHit bodyHit, 100f, DropRaycastMask))
                {
                    Interactable underBody = bodyHit.collider.GetComponentInParent<Interactable>();
                    if (underBody != null && (object)underBody != this && underBody.CanAcceptDrop(this))
                    {
                        hitSomething = true;
                        hit = bodyHit;
                        dropTarget = underBody;
                    }
                }
            }

            // Dropped on empty counter - nothing there to take it, so just set
            // it down where it was released: at the cursor, not the item's own
            // position (a press-release with no mouse move never ran OnDragMove,
            // so the item was still at the hand's resting spot up by the wall).
            if (dropTarget == null && hand != null && hand.Current == this)
            {
                Vector2 sp = Mouse.current.position.ReadValue();
                Vector3 at = cam.ScreenToWorldPoint(new Vector3(sp.x, sp.y, transform.position.z - cam.transform.position.z));
                hand.PlaceFreeAt(new Vector3(at.x, at.y, transform.position.z));
            }
            else if (hitSomething)
            {
                Interactable target = dropTarget;
                if (this is FryingChopsticks chop && chop.Pinched != null && target != null && (object)target != this)
                {
                    chop.TryDropPinchedOn(target); // dragged over somewhere: the pinched piece goes there
                }
                else if (this is ToolItem knife && knife.KnifeLoad != null && target != null && (object)target != this)
                {
                    knife.TryDropLoadOn(target); // the pieces on the blade go there
                }
                else if (target != null && (object)target != this)
                {
                    // If the raycast landed on something that's itself sitting in a
                    // container (e.g. another ingredient already on a plate) and that
                    // thing has no use for this drop, fall through to its container -
                    // the player was aiming at "the plate", not specifically at
                    // whatever else happens to already be sitting on top of it there.
                    if (!target.CanAcceptDrop(this) && target is Carryable hitCarryable
                        && hitCarryable.CurrentContainer is Interactable containerInteractable
                        && containerInteractable.CanAcceptDrop(this))
                    {
                        target = containerInteractable;
                    }

                    hand.DropIsFromDrag = true;
                    target.ForceActivate();
                    hand.DropIsFromDrag = false;
                }
            }
        }

        if (dragColliders != null) foreach (Collider c in dragColliders) c.enabled = true;
        if (this is MovableBowl endedBowl) endedBowl.SetContentsCollidersEnabled(true);

        // if nothing accepted the drop, hand.Current is still this item, and it
        // will simply resume following the hand next frame (IsDragging is false again).
    }
}
