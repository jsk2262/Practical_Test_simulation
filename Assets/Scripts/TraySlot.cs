using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A wide container that holds several Carryables at once (e.g. a prep tray
/// where multiple washed vegetables sit side by side), unlike PlaceSlot which
/// holds exactly one.
/// </summary>
public class TraySlot : Interactable, ISlotContainer
{
    [SerializeField] private SlotAccepts accepts = SlotAccepts.IngredientOnly;
    [SerializeField] private Transform[] anchors;

    private readonly Dictionary<int, Carryable> slots = new Dictionary<int, Carryable>();
    private int pendingAnchorIndex = -1;

    public bool IsFull => slots.Count >= anchors.Length;

    protected override void Activate()
    {
        HandCarry hand = HandCarry.Instance;
        if (hand == null || hand.Current == null || IsFull) return;
        if (!Accepts(hand.Current)) return;

        hand.PlaceAtTray(this);
    }

    public bool Accepts(Carryable c)
    {
        switch (accepts)
        {
            case SlotAccepts.IngredientOnly: return c is IngredientItem;
            case SlotAccepts.ToolOnly: return c is ToolItem;
            default: return true;
        }
    }

    public Transform ClaimAnchor()
    {
        for (int i = 0; i < anchors.Length; i++)
        {
            if (!slots.ContainsKey(i))
            {
                pendingAnchorIndex = i;
                return anchors[i];
            }
        }

        pendingAnchorIndex = -1;
        return transform;
    }

    public void AddItem(Carryable item)
    {
        if (pendingAnchorIndex < 0) return;

        slots[pendingAnchorIndex] = item;
        pendingAnchorIndex = -1;
    }

    public void Release(Carryable item)
    {
        int keyToRemove = -1;
        foreach (KeyValuePair<int, Carryable> kv in slots)
        {
            if (kv.Value == item)
            {
                keyToRemove = kv.Key;
                break;
            }
        }

        if (keyToRemove >= 0) slots.Remove(keyToRemove);
    }
}
