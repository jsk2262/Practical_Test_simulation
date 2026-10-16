using UnityEngine;

/// <summary>
/// Long wooden frying chopsticks (튀김젓가락). Held, click an ingredient sitting
/// in a pan/pot/plate (or on the board) and they pinch it up at their tips;
/// click (or drag onto) wherever it should go next - another plate, the
/// paper towel, the wok - and it's set down there exactly as if the hand had
/// carried it (that place's own placing rules apply). Lets fried pieces come
/// out of hot oil without bare hands.
/// </summary>
public class FryingChopsticks : ToolItem
{
    /// <summary>Where along the held chopsticks (local, from the pivot) a pinched piece sits.</summary>
    [SerializeField] private Vector3 tipLocal = new Vector3(0f, 0.9f, -0.02f);

    public IngredientItem Pinched { get; private set; }
    private Collider[] pinchedColliders;
    public float GripClosure { get; private set; }
    private MeshFilter stickFilter;
    private Mesh originalMesh, animatedMesh;
    private Vector3[] originalVertices, posedVertices;

    void Update()
    {
        bool pressing = IsHeld && UnityEngine.InputSystem.Mouse.current != null
            && UnityEngine.InputSystem.Mouse.current.leftButton.isPressed;
        GripClosure = Mathf.MoveTowards(GripClosure, Pinched != null || pressing ? 1f : 0f, Time.deltaTime * 8f);
        if (animatedMesh != null)
        {
            Bounds bounds = originalMesh.bounds;
            Vector3 pivot = new Vector3(bounds.center.x, bounds.min.y + bounds.size.y * 0.2f, bounds.center.z);
            Quaternion open = Quaternion.AngleAxis(IsHeld ? -4f * (1f - GripClosure) : 0f, Vector3.forward);
            for (int i = 0; i < originalVertices.Length; i++)
                posedVertices[i] = originalVertices[i].x > bounds.center.x
                    ? pivot + open * (originalVertices[i] - pivot) : originalVertices[i];
            animatedMesh.vertices = posedVertices;
            animatedMesh.RecalculateBounds();
            animatedMesh.RecalculateNormals();
        }
    }

    protected override void Awake()
    {
        kind = ToolKind.Chopsticks;
        base.Awake();
        stickFilter = GetComponent<MeshFilter>();
        if (stickFilter != null && stickFilter.sharedMesh != null && stickFilter.sharedMesh.isReadable)
        {
            originalMesh = stickFilter.sharedMesh;
            animatedMesh = Instantiate(originalMesh);
            originalVertices = originalMesh.vertices;
            posedVertices = new Vector3[originalVertices.Length];
            stickFilter.sharedMesh = animatedMesh;
        }
    }

    void OnDestroy()
    {
        if (stickFilter != null && originalMesh != null) stickFilter.sharedMesh = originalMesh;
        if (animatedMesh != null) Destroy(animatedMesh);
    }

    public bool TryPinch(IngredientItem ing)
    {
        if (Pinched != null || ing == null || ing.IsHeld) return false;

        if (ing.CurrentContainer != null)
        {
            ing.CurrentContainer.Release(ing);
            ing.CurrentContainer = null;
        }

        Pinched = ing;
        pinchedColliders = System.Array.FindAll(ing.GetComponentsInChildren<Collider>(), c => c.enabled);
        foreach (Collider c in pinchedColliders) c.enabled = false;

        ing.transform.SetParent(transform, true);
        ing.transform.position = TipPosition();
        ing.transform.localScale = Vector3.Scale(ing.BaseLocalScale, InverseScale(transform.lossyScale));
        HandController.Instance?.PlayChopGesture();
        return true;
    }

    void LateUpdate()
    {
        if (Pinched != null) Pinched.transform.position = TipPosition();
    }

    private Vector3 TipPosition()
    {
        Renderer r = GetComponent<Renderer>();
        // The tips are the chopsticks' top while standing; when carried they
        // lie head-left, so follow the actual far end of the render bounds.
        if (r == null) return transform.TransformPoint(tipLocal);
        Bounds b = r.bounds;
        Vector3 head = transform.rotation * Vector3.up;
        Vector3 tip = b.center + Vector3.Scale(b.extents, new Vector3(Mathf.Sign(head.x), Mathf.Sign(head.y), 0f)) * 0.85f;
        tip.z = b.min.z - 0.02f;
        return tip;
    }

    /// <summary>Sets the pinched piece down on target as if carried there by
    /// hand (the target's own click handling places it). Stays pinched if the
    /// target doesn't take it.</summary>
    public bool TryDropPinchedOn(Interactable target)
    {
        HandCarry hand = HandCarry.Instance;
        if (Pinched == null || target == null || hand == null || (object)hand.Current != this) return false;
        if (!target.CanAcceptDrop(Pinched) && !(target is Carryable c && c.CurrentContainer is Interactable under && under.CanAcceptDrop(Pinched)))
            return false;

        IngredientItem ing = Pinched;
        Interactable dest = target.CanAcceptDrop(ing) ? target : ((Carryable)target).CurrentContainer as Interactable;

        // Hand the piece to the hand for one placement, then take the chopsticks back.
        hand.Detach();
        foreach (Collider col in pinchedColliders) if (col != null) col.enabled = true;
        Pinched = null;
        pinchedColliders = null;
        hand.PickUp(ing); // keeps its spot at the tips for this placement
        hand.DropIsFromDrag = true; // placed where it actually is (at the tips), not snapped to center
        dest.ForceActivate();
        hand.DropIsFromDrag = false;

        if ((object)hand.Current == ing) // not taken after all - back between the tips
        {
            hand.Detach();
            hand.PickUp(this);
            TryPinch(ing);
            return false;
        }

        hand.PickUp(this);
        return true;
    }

    private static Vector3 InverseScale(Vector3 s) => new Vector3(
        Mathf.Approximately(s.x, 0f) ? 1f : 1f / s.x,
        Mathf.Approximately(s.y, 0f) ? 1f : 1f / s.y,
        Mathf.Approximately(s.z, 0f) ? 1f : 1f / s.z);
}
