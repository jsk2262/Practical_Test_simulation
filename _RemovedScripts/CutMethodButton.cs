using UnityEngine;

public class CutMethodButton : MonoBehaviour, IClickable
{
    private CutMethodChooser owner;
    private IngredientItem.CutMethod method;

    public void Init(CutMethodChooser owner, IngredientItem.CutMethod method)
    {
        this.owner = owner;
        this.method = method;
    }

    public void OnClicked()
    {
        owner.Choose(method);
    }
}
