using UnityEngine;

/// <summary>
/// Sits on each spawned slice/strip so later grading logic can read back what
/// was actually cut (method + achieved cm spec) without touching the original
/// ingredient, which no longer exists.
/// </summary>
public class CutResultPiece : MonoBehaviour
{
    public string IngredientName { get; private set; }
    public IngredientItem.CutMethod Method { get; private set; }
    public CutSpec AchievedSpec { get; private set; }

    public void Init(string ingredientName, IngredientItem.CutMethod method, CutSpec spec)
    {
        IngredientName = ingredientName;
        Method = method;
        AchievedSpec = spec;
    }
}
