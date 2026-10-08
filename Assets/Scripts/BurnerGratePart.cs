using UnityEngine;

/// <summary>One part of a burner's grate (a prong, the ring): a click on it is
/// a click on the whole grate (BurnerGrate). Set up by Tools > Kitchen >
/// Setup Burner2 Grate Cleaning.</summary>
public class BurnerGratePart : MonoBehaviour, IClickable
{
    [SerializeField] private BurnerGrate grate;

    public BurnerGrate Grate => grate;

    public void OnClicked()
    {
        if (grate != null) grate.OnPartClicked();
    }
}
