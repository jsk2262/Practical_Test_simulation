using UnityEngine;

/// <summary>Click to toggle the recommended-order hint popup - repurposes what
/// used to be an unused station-number marker (a plain sphere with no script).</summary>
public class SequenceHintButton : MonoBehaviour, IClickable
{
    public void OnClicked()
    {
        // (힌트판은 전체 화면 크기로 뜬다 - 확대 중이면 먼저 전체 보기로)
        if (KitchenCamera.IsZoomed) { KitchenCamera.Instance.ShowOverview(); return; }
        CookingPlanManager.Instance?.ToggleOrderHint();
    }
}
