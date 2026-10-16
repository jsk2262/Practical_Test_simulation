using UnityEngine;

/// <summary>One part of a burner's grate (a prong, the ring): a click on it is
/// a click on the whole grate (BurnerGrate). Set up by Tools > Kitchen >
/// Setup Burner2 Grate Cleaning.</summary>
public class BurnerGratePart : MonoBehaviour, IClickable
{
    [SerializeField] private BurnerGrate grate;

    public BurnerGrate Grate => grate;
    public IClickable HoldAction => new LiftAction(this);
    private sealed class LiftAction : IClickable
    {
        private readonly BurnerGratePart part;
        public LiftAction(BurnerGratePart part) { this.part = part; }
        public void OnClicked() { if (part.grate != null) part.grate.OnPartClicked(); }
    }

    public void OnClicked()
    {
        // Short clicks intentionally do not lift the grate.
    }
}
