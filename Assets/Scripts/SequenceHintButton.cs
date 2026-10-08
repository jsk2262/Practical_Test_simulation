using UnityEngine;

/// <summary>Click to toggle the recommended-order hint popup - repurposes what
/// used to be an unused station-number marker (a plain sphere with no script).</summary>
public class SequenceHintButton : MonoBehaviour, IClickable
{
    public void OnClicked()
    {
        CookingPlanManager.Instance?.ToggleOrderHint();
    }
}
