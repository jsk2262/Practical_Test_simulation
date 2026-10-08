using UnityEngine;

/// <summary>
/// A dish rack shelf (PlateShelf1/PlateShelf2) - takes plates and bowls, but
/// only ones that were used and then washed at the sink (MovableBowl.IsWashed),
/// and only when empty: the exam's clean-up step of putting finished dishes
/// away after 설거지. Unused clean dishes and dirty ones are refused.
/// A dish lands right where it's clicked/dropped (clamped onto the shelf),
/// just in front of the shelf's surface.
/// </summary>
public class DishShelfController : Interactable, ISlotContainer
{
    /// <summary>Gap kept between a dish and the shelf's surface (world units).</summary>
    [SerializeField] private float surfaceGap = 0.02f;

    private Transform contentRoot;

    private Transform ContentRoot
    {
        get
        {
            if (contentRoot == null)
            {
                // Runtime-only field, resets on reload while the child stays.
                Transform existing = transform.Find("ContentRoot");
                if (existing != null)
                {
                    contentRoot = existing;
                }
                else
                {
                    var go = new GameObject("ContentRoot");
                    contentRoot = go.transform;
                    contentRoot.SetParent(transform, false);

                    // Cancel the shelf mesh's own (non-uniform) scale/rotation so
                    // dishes keep their true size and resting orientation.
                    Vector3 s = transform.lossyScale;
                    contentRoot.localScale = new Vector3(
                        Mathf.Approximately(s.x, 0f) ? 1f : 1f / s.x,
                        Mathf.Approximately(s.y, 0f) ? 1f : 1f / s.y,
                        Mathf.Approximately(s.z, 0f) ? 1f : 1f / s.z);
                    contentRoot.rotation = Quaternion.identity;
                }
            }

            return contentRoot;
        }
    }

    /// <summary>Putting dishes away is clean-up work: only in clean-up mode (정리).</summary>
    private static bool CanShelve(Carryable item) =>
        CleanupMode.IsOn && item is MovableBowl dish && dish.IsPlateOrBowl && dish.IsWashed && dish.IsEmpty;

    public override bool CanAcceptDrop(Carryable item) => CanShelve(item);

    protected override void Activate()
    {
        HandCarry hand = HandCarry.Instance;
        if (hand == null || !(hand.Current is MovableBowl dish) || !CanShelve(dish)) return;

        Bounds shelf = ShelfBounds();
        Vector3 target = CursorWorldPoint(shelf.min.z, dish.transform.position);
        target.x = Mathf.Clamp(target.x, shelf.min.x, shelf.max.x);
        target.y = Mathf.Clamp(target.y, shelf.min.y, shelf.max.y);

        Transform root = ContentRoot;
        if (!hand.PlaceAtFreeLocalPosition(root, root.InverseTransformPoint(target))) return;
        dish.CurrentContainer = this;

        // Bring the dish forward until it's fully in front of the shelf surface
        // (its own thickness varies by model), so it never sinks into the shelf.
        Renderer dishRend = dish.GetComponent<Renderer>();
        if (dishRend != null)
        {
            float overlap = dishRend.bounds.max.z - (shelf.min.z - surfaceGap);
            if (overlap > 0f) dish.transform.position -= new Vector3(0f, 0f, overlap);
        }
    }

    private Bounds ShelfBounds()
    {
        Collider col = GetComponent<Collider>();
        if (col != null) return col.bounds;
        Renderer rend = GetComponent<Renderer>();
        return rend != null ? rend.bounds : new Bounds(transform.position, Vector3.one);
    }

    private static Vector3 CursorWorldPoint(float worldZ, Vector3 fallback)
    {
        Camera cam = Camera.main;
        if (cam == null || UnityEngine.InputSystem.Mouse.current == null) return fallback;

        Vector2 screen = UnityEngine.InputSystem.Mouse.current.position.ReadValue();
        return cam.ScreenToWorldPoint(new Vector3(screen.x, screen.y, worldZ - cam.transform.position.z));
    }

    public void Release(Carryable item)
    {
        // No single-occupant slot to clear - each dish just tracks itself.
    }
}
