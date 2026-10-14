using UnityEngine;

public class ThicknessButton : MonoBehaviour, IClickable
{
    private CutMethodChooser owner;
    private float thicknessCm;

    public void Init(CutMethodChooser owner, float thicknessCm)
    {
        this.owner = owner;
        this.thicknessCm = thicknessCm;
    }

    public void OnClicked()
    {
        owner.ChooseThickness(thicknessCm);
    }
}
