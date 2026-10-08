using UnityEngine;

public enum SlotAccepts { Any, IngredientOnly, ToolOnly, BowlOnly, SeasoningOnly }

public class PlaceSlot : Interactable, ISlotContainer
{
    [SerializeField] private SlotAccepts accepts = SlotAccepts.Any;
    [SerializeField] private Transform anchor;

    public Carryable Current { get; set; }
    public Transform Anchor => anchor != null ? anchor : transform;

    protected virtual void Awake()
    {
        // A scene-authored occupant (e.g. a knife resting on the cutting board
        // from the start) never goes through PlaceAt, so its CurrentContainer
        // stays null - the sibling-collision drop-redirect in Carryable.OnDragEnd
        // (used when a raycast lands on that occupant instead of the slot itself)
        // then can't find its way back to this slot, and drops silently fail.
        // Deliberately not setting Current here: a resting tool shouldn't consume
        // the slot's one-occupant tracking and block a real item from being placed.
        if (Anchor.childCount > 0)
        {
            Carryable existing = Anchor.GetChild(0).GetComponent<Carryable>();
            if (existing != null) existing.CurrentContainer = this;
        }
    }

    protected override void Activate()
    {
        HandCarry hand = HandCarry.Instance;
        if (hand == null || hand.Current == null || Current != null) return;
        if (!Accepts(hand.Current)) return;

        hand.PlaceAt(this);
    }

    public bool Accepts(Carryable c)
    {
        switch (accepts)
        {
            case SlotAccepts.IngredientOnly: return c is IngredientItem;
            case SlotAccepts.ToolOnly: return c is ToolItem;
            case SlotAccepts.BowlOnly: return c is MovableBowl;
            case SlotAccepts.SeasoningOnly: return c is SeasoningBottle;
            default: return true;
        }
    }

    public override bool CanAcceptDrop(Carryable item) => Current == null && Accepts(item);

    public virtual void Release(Carryable item)
    {
        if (Current == item) Current = null;
    }
}
