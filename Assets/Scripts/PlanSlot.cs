using UnityEngine;

/// <summary>One numbered cell in the cooking-plan sequence board. Pure position and
/// occupancy marker - CookingPlanManager does all the placement/validation logic.</summary>
public class PlanSlot : MonoBehaviour
{
    public int Index { get; set; }
    public PlanStepCard Occupant { get; set; }
}
