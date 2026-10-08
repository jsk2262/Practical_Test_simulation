using UnityEngine;

public class HygieneTracker : MonoBehaviour
{
    public IngredientCategory LastCategory { get; private set; } = IngredientCategory.None;

    public bool RegisterUse(IngredientCategory category)
    {
        if (category == IngredientCategory.None) return false;

        bool violated = LastCategory != IngredientCategory.None && LastCategory != category;
        LastCategory = category;
        return violated;
    }

    /// <summary>Used for something that needs it washed straight after (the knife
    /// that carried the scraps to the waste bag) - cleared by a wash.</summary>
    public bool NeedsWash { get; private set; }
    public void MarkNeedsWash() => NeedsWash = true;

    public void Wash()
    {
        LastCategory = IngredientCategory.None;
        NeedsWash = false;
    }

    /// <summary>Restores a previously-saved state directly, with no violation
    /// check and no implication of washing anything - for the cutting board
    /// swapping which of its two sides is currently active on a flip, where
    /// the side just turned away needs to keep whatever mess it had (not get
    /// silently washed) and the side coming up needs its own saved state back.</summary>
    public void SetState(IngredientCategory category)
    {
        LastCategory = category;
    }
}
