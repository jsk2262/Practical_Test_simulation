using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// A wall rail with hooks (HangingRack, HangingRack2) for cooking utensils
/// (ToolItem.IsHangable: ladles, spatulas, whisks, spoons, the strainer).
/// Holding one, click the rail (or drag onto it) and it hangs head-down from
/// the free hook nearest the cursor, its handle end at the hook. Click a
/// hung utensil empty-handed to take it back down.
/// Set up with Tools > Kitchen > Setup Hanging Racks (adds the click area).
/// </summary>
public class HangingRack : Interactable, ISlotContainer
{
    [SerializeField] private int hookCount = 5;
    /// <summary>How far below the rail's underside the hooks hang (world units).</summary>
    [SerializeField] private float hookDrop = 0.05f;
    /// <summary>How far in front of the rail the utensils hang, toward the camera.</summary>
    [SerializeField] private float frontOffset = 0.1f;

    /// <summary>Utensils already hanging here when the exam starts, hook by hook.</summary>
    [SerializeField] private Carryable[] startItems;

    private Carryable[] hooks;
    private Transform hookRoot;
    private Bounds rail;

    void Start()
    {
        ClaimAlreadyHanging();
        if (startItems == null) return;
        foreach (Carryable item in startItems)
        {
            if (item == null || !CanHang(item) || item.IsHeld) continue;
            int hook = NearestFreeHook(HookX(0));
            for (int i = 0; i < hooks.Length; i++) if (hooks[i] == null) { hook = i; break; }
            if (hook < 0) break;

            item.CurrentContainer?.Release(item);
            item.transform.SetParent(hookRoot, true);
            item.transform.localScale = item.BaseLocalScale; // hookRoot is unscaled in world terms
            hooks[hook] = item;
            item.CurrentContainer = this;
            HangAt(item, hook);
        }
    }

    void Awake()
    {
        hooks = new Carryable[Mathf.Max(hookCount, 1)];
        Renderer r = GetComponent<Renderer>();
        rail = r != null ? r.bounds : new Bounds(transform.position, Vector3.one);

        // Unrotated, unscaled parent for whatever hangs here (the rail model
        // itself is turned 180 on Y), so hung items keep their own size.
        hookRoot = new GameObject(name + "_Hooks").transform;
        hookRoot.SetParent(transform, false);
        hookRoot.rotation = Quaternion.identity;
        Vector3 s = transform.lossyScale;
        hookRoot.localScale = new Vector3(1f / s.x, 1f / s.y, 1f / s.z);
        hookRoot.position = new Vector3(rail.center.x, rail.min.y, rail.min.z);
    }

    /// <summary>Utensils placed hanging from this rail by hand in the scene take
    /// the hook nearest them (left where they are), so nothing else is hung
    /// onto an occupied hook and taking one down frees it.</summary>
    private void ClaimAlreadyHanging()
    {
        foreach (ToolItem t in FindObjectsByType<ToolItem>(FindObjectsSortMode.None))
        {
            if (!t.IsHangable || t.IsHeld || t.CurrentContainer != null) continue;
            if (startItems != null && System.Array.IndexOf(startItems, t) >= 0) continue; // hung below instead
            Renderer r = t.GetComponentInChildren<Renderer>();
            if (r == null) continue;
            Bounds b = r.bounds;
            bool underRail = b.center.x > rail.min.x && b.center.x < rail.max.x
                          && b.max.y > rail.min.y - 0.6f && b.max.y < rail.max.y + 0.2f
                          && Mathf.Abs(b.center.z - rail.center.z) < 1.5f;
            if (!underRail) continue;

            int hook = NearestFreeHook(b.center.x);
            if (hook < 0) return;
            hooks[hook] = t;
            t.CurrentContainer = this;
            t.LearnGripFromHangingPose(t.transform.rotation); // posed hanging: its up end is the grip
        }
    }

    private static bool CanHang(Carryable item) => item is ToolItem t && t.IsHangable;

    private float HookX(int i) => hooks.Length == 1 ? rail.center.x
        : Mathf.Lerp(rail.min.x + rail.size.x * 0.1f, rail.max.x - rail.size.x * 0.1f, i / (float)(hooks.Length - 1));

    private int NearestFreeHook(float x)
    {
        int best = -1;
        float bestD = float.MaxValue;
        for (int i = 0; i < hooks.Length; i++)
        {
            if (hooks[i] != null) continue;
            float d = Mathf.Abs(HookX(i) - x);
            if (d < bestD) { bestD = d; best = i; }
        }
        return best;
    }

    public override bool CanAcceptDrop(Carryable item) => CanHang(item) && NearestFreeHook(rail.center.x) >= 0;

    protected override void Activate()
    {
        HandCarry hand = HandCarry.Instance;
        if (hand == null || !CanHang(hand.Current)) return;

        Carryable item = hand.Current;
        float x = hand.DropIsFromDrag ? item.transform.position.x : CursorX(item.transform.position.x);
        int hook = NearestFreeHook(x);
        if (hook < 0) return;

        if (!hand.PlaceAtFreeLocalPosition(hookRoot, Vector3.zero)) return;
        hooks[hook] = item;
        item.CurrentContainer = this;
        HangAt(item, hook);
    }

    /// <summary>Handle up, head down: turned on screen (about the view axis) so
    /// its grip end points straight up - whatever way it was resting or was
    /// posed in the scene - then moved so that top end sits right at the hook.</summary>
    private void HangAt(Carryable item, int hook)
    {
        Transform t = item.transform;
        Quaternion rest = item.RestingRotation;
        Vector3 grip = rest * (item is ToolItem tool ? tool.GripLocalDir : Vector3.down);
        Vector2 g = new Vector2(grip.x, grip.y);
        float turn = g.sqrMagnitude < 1e-4f ? 180f : Vector2.SignedAngle(g, Vector2.up);
        t.rotation = Quaternion.AngleAxis(turn, Vector3.forward) * rest;

        Renderer r = item.GetComponentInChildren<Renderer>();
        Vector3 hookPoint = new Vector3(HookX(hook), rail.min.y - hookDrop, rail.min.z - frontOffset);
        if (r == null) { t.position = hookPoint; return; }

        Bounds b = r.bounds;
        Vector3 top = new Vector3(b.center.x, b.max.y, b.center.z);
        t.position += hookPoint - top;
    }

    public void Release(Carryable item)
    {
        if (hooks == null) return;
        for (int i = 0; i < hooks.Length; i++)
            if ((object)hooks[i] == item) hooks[i] = null;
    }

    private static float CursorX(float fallback)
    {
        Camera cam = Camera.main;
        if (cam == null || Mouse.current == null) return fallback;
        Vector2 screen = Mouse.current.position.ReadValue();
        return cam.ScreenToWorldPoint(new Vector3(screen.x, screen.y, 10f)).x;
    }
}
