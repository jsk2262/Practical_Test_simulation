using UnityEngine;

/// <summary>Scene button that opens the cooking-plan panel. Placed near the exam
/// papers so it reads as "read the assignment, then plan the order".</summary>
public class CookingPlanTrigger : MonoBehaviour, IClickable
{
    public void OnClicked()
    {
        CookingPlanManager.Instance?.Toggle();
    }
}
