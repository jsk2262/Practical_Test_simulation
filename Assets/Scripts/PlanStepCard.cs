using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// One draggable, semi-transparent "step card" window in the cooking-plan sequence
/// board. Drag it onto a numbered slot to place it in the plan; drag it off again
/// (or onto empty pool space) to send it back to its pool spot. Whenever
/// CookingPlanManager finds a later-phase step already sitting earlier in the
/// board than this one, it marks this card invalid and it turns red - clicking a
/// red card shows why.
/// </summary>
public class PlanStepCard : MonoBehaviour, IDraggable, IClickable
{
    // Brighter and more opaque than a "true" translucent window would be -
    // black text needs that much contrast to stay legible over the dark panel
    // backdrop showing through.
    public static readonly Color ValidColor = new Color(0.72f, 0.85f, 0.98f, 0.88f);
    public static readonly Color InvalidColor = new Color(0.98f, 0.45f, 0.42f, 0.9f);

    private CookingPlanManager owner;
    private Material mat;
    private Vector3 poolHomeLocalPos;
    private bool isDragging;
    private Camera cam;

    public string Label { get; private set; }
    public int Phase { get; private set; }
    public int SlotIndex { get; private set; } = -1;
    public bool IsInvalid { get; private set; }
    public PlanStepCard BlockedBy { get; private set; }

    public void Init(CookingPlanManager owner, string label, int phase, Material mat, Vector3 poolHomeLocalPos)
    {
        this.owner = owner;
        this.mat = mat;
        this.poolHomeLocalPos = poolHomeLocalPos;
        Label = label;
        Phase = phase;
    }

    public void SetSlot(int index)
    {
        SlotIndex = index;
    }

    /// <summary>Sends the card back to its scattered starting spot in the pool -
    /// used both when a drag misses every slot and when another card displaces
    /// this one out of its slot.</summary>
    public void ReturnToPool()
    {
        SlotIndex = -1;
        transform.localPosition = poolHomeLocalPos;
    }

    public void SetValidity(bool invalid, PlanStepCard blockedBy)
    {
        IsInvalid = invalid;
        BlockedBy = blockedBy;
        mat.color = invalid ? InvalidColor : ValidColor;
    }

    public bool OnDragStart()
    {
        isDragging = true;
        cam = Camera.main;

        // Bump toward the camera while dragging so this card draws in front of
        // every other card and slot instead of z-fighting/hiding behind them.
        Vector3 p = transform.localPosition;
        transform.localPosition = new Vector3(p.x, p.y, -0.3f);
        return true;
    }

    public void OnDragMove(Vector2 mouseDelta, float deltaTime)
    {
        if (!isDragging || cam == null || Mouse.current == null) return;

        Vector2 screenPos = Mouse.current.position.ReadValue();
        Ray ray = cam.ScreenPointToRay(screenPos); // 화면 위치를 레이로 바꾼다.
        Plane plane = new Plane(transform.parent.forward, transform.position); // 순서도와 평행한 평면
        if (plane.Raycast(ray, out float distance)) // 회전된 화면에서도 교차점을 구한다.
            transform.position = ray.GetPoint(distance); // 카드 깊이를 유지하며 이동한다.
    }

    public void OnDragEnd()
    {
        isDragging = false;
        owner.OnCardDropped(this);
    }

    /// <summary>Two clicks within this many seconds count as a double-click.</summary>
    private const float DoubleClickSeconds = 0.35f;
    private float lastClickTime = -1f;

    /// <summary>Click: a red card explains why. Double-click: the card goes
    /// straight into the next empty numbered slot (or, already placed, back
    /// to the pool) - building the plan in order without dragging.</summary>
    public void OnClicked()
    {
        bool isDouble = lastClickTime >= 0f && Time.unscaledTime - lastClickTime <= DoubleClickSeconds;
        lastClickTime = isDouble ? -1f : Time.unscaledTime;

        if (isDouble) owner.PlaceInNextSlot(this);
        else if (IsInvalid) owner.ShowExplanation(this);
    }
}
