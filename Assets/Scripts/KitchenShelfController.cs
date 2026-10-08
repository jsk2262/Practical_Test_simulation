using UnityEngine;

/// <summary>
/// A tool-storage shelf (the pot/pan rack) that accepts a fixed, explicitly
/// listed set of items - unlike SauceShelfController's "any SeasoningBottle"
/// policy, a plate or bowl full of ingredients shouldn't end up parked here,
/// so membership is by direct reference rather than by type. Several of these
/// can share the same allowed-items list so any one of them can receive
/// whichever item the player is currently carrying.
/// An item sits with its body centered where it's put (clamped to the shelf)
/// and its bottom on the shelf's top, at its own resting depth.
/// </summary>
public class KitchenShelfController : Interactable, ISlotContainer
{
    [SerializeField] private float halfWidth = 0.6f;
    [SerializeField] private Carryable[] allowedItems;
    /// <summary>How much of the shelf's width a stored pan's body may take up.</summary>
    [SerializeField] private float shelfFill = 0.95f;

    private Transform contentRoot;

    /// <summary>A sibling of the shelf, not a child: the shelf's own non-uniform
    /// scale (1.5, 0.1, 1) can't be cancelled by a child's inverse scale once
    /// the shelf is tilted - pots and pans came out squashed and stood on end.</summary>
    private Transform ContentRoot
    {
        get
        {
            if (contentRoot == null)
            {
                string rootName = name + "_ContentRoot";
                Transform parent = transform.parent;
                Transform existing = parent != null ? parent.Find(rootName) : GameObject.Find(rootName)?.transform;
                if (existing != null)
                {
                    contentRoot = existing;
                }
                else
                {
                    contentRoot = new GameObject(rootName).transform;
                    contentRoot.SetParent(parent, false);
                    contentRoot.position = transform.position;
                    contentRoot.rotation = Quaternion.identity;
                    Vector3 ps = parent != null ? parent.lossyScale : Vector3.one;
                    contentRoot.localScale = new Vector3(1f / ps.x, 1f / ps.y, 1f / ps.z);
                }
            }

            return contentRoot;
        }
    }

    private bool IsAllowed(Carryable item)
    {
        if (item == null || allowedItems == null) return false;
        foreach (Carryable a in allowedItems)
            if ((object)a == item) return true;
        return false;
    }

    public override bool CanAcceptDrop(Carryable item) => IsAllowed(item);

    /// <summary>Whether any shelf here takes this item (the pots and pans
    /// that belong under the counter).</summary>
    public static bool AllowsAnywhere(Carryable item)
    {
        foreach (KitchenShelfController s in FindObjectsByType<KitchenShelfController>(FindObjectsSortMode.None))
            if (s.IsAllowed(item)) return true;
        return false;
    }

    protected override void Activate()
    {
        HandCarry hand = HandCarry.Instance;
        Carryable item = hand?.Current;
        if (!IsAllowed(item)) return;

        Bounds shelf = ShelfBounds();
        // Same reasoning as CuttingBoardController/SauceShelfController - a plain
        // click carries no aim, so it lands centered; only a real drag has a
        // position worth honoring.
        float x = hand.DropIsFromDrag ? BodyCenter(item).x : shelf.center.x;
        float edge = Mathf.Max(shelf.extents.x - halfWidth * 0.5f, 0f);
        x = Mathf.Clamp(x, shelf.center.x - edge, shelf.center.x + edge);

        if (!hand.PlaceAtFreeLocalPosition(ContentRoot, Vector3.zero)) return;
        item.CurrentContainer = this;

        Transform t = item.transform;
        Renderer r = item.GetComponent<Renderer>();
        float bottom;
        if (item is MovableBowl bowl && bowl.TryGetHandleFrame(out Vector3 up, out Vector3 handleDir, out Bounds body))
        {
            // Stored handle-out: the handle turned toward the front (the player),
            // the way pots sit in a cabinet - pointing sideways it reached over
            // the neighboring shelf. Turned about the pan's own up, so it still
            // lies flat.
            Vector3 front = Vector3.ProjectOnPlane(Vector3.back, up);
            if (front.sqrMagnitude > 1e-6f)
                t.RotateAround(body.center, up, Vector3.SignedAngle(handleDir, front, up));

            // A body wider than the shelf is shown a little smaller while stored
            // (the pickup restores its own size).
            bowl.TryGetHandleFrame(out _, out _, out body);
            float fit = shelf.size.x * shelfFill / Mathf.Max(body.size.x, 1e-4f);
            if (fit < 1f)
            {
                Vector3 pivot = body.center;
                t.localScale *= fit;
                t.position = pivot + (t.position - pivot) * fit;
                bowl.TryGetHandleFrame(out _, out _, out body);
            }

            bottom = body.min.y;
            t.position += new Vector3(x - body.center.x, 0f, 0f);
        }
        else
        {
            bottom = r != null ? r.bounds.min.y : t.position.y;
            t.position += new Vector3(x - t.position.x, 0f, 0f);
        }

        // Body over the spot, bottom on the shelf's top, at its own resting depth.
        t.position += new Vector3(0f, ShelfTopY(shelf) - bottom, item.RestZ - t.position.z);
    }

    /// <summary>The middle of the shelf's top face on screen. The shelf is a
    /// tilted box, so its bounds' max reaches the top face's far edge - items
    /// put there looked like they floated above the shelf.</summary>
    private float ShelfTopY(Bounds shelf)
    {
        BoxCollider box = GetComponent<BoxCollider>();
        if (box == null) return shelf.max.y;
        Vector3 h = box.size * 0.5f;
        float sum = 0f;
        for (int i = 0; i < 4; i++)
        {
            Vector3 corner = box.center + new Vector3((i & 1) == 0 ? -h.x : h.x, h.y, (i & 2) == 0 ? -h.z : h.z);
            sum += transform.TransformPoint(corner).y;
        }
        return sum * 0.25f;
    }

    private static Vector3 BodyCenter(Carryable item) =>
        item is MovableBowl b ? b.BodyCenterWorld() : item.transform.position;

    private Bounds ShelfBounds()
    {
        Collider col = GetComponent<Collider>();
        if (col != null) return col.bounds;
        Renderer rend = GetComponent<Renderer>();
        return rend != null ? rend.bounds : new Bounds(transform.position, Vector3.one);
    }

    public void Release(Carryable item)
    {
        // No single-occupant slot to clear - each item just tracks itself.
    }
}
