using UnityEngine;

[RequireComponent(typeof(Collider))]
public abstract class Interactable : MonoBehaviour, IClickable
{
    [SerializeField] private Vector3 handOffset = new Vector3(0f, 0f, -0.55f);

    /// <summary>Reacts the moment it's clicked - no reaction delay, no waiting
    /// for the hand to travel there first. The hand still moves to the target
    /// for visual feedback, but purely cosmetically now: it no longer gates
    /// when Activate() actually runs.</summary>
    public void OnClicked()
    {
        // Frying chopsticks holding a piece: the click sets the piece down here
        // (as if carried by hand) instead of using the chopsticks on it.
        // Likewise cut pieces carried on the knife's blade are tipped in here.
        Carryable held = HandCarry.Instance?.Current;
        bool handedOver = held is FryingChopsticks chop && chop.Pinched != null && (object)chop != this && chop.TryDropPinchedOn(this)
                       || held is ToolItem knife && knife.KnifeLoad != null && (object)knife != this && (object)knife.KnifeLoad != this && knife.TryDropLoadOn(this);
        if (!handedOver) Activate();

        if (HandController.Instance != null)
        {
            Vector3 target = transform.position + handOffset;
            HandController.Instance.GoTo(target, null);
        }
    }

    protected abstract void Activate();

    /// <summary>Runs this interactable's effect immediately, skipping the click
    /// delay and hand travel. Used by drag-and-drop release resolution, where
    /// the dragged item is already visually at the drop point.</summary>
    public void ForceActivate() => Activate();

    /// <summary>Whether dropping the given item on this interactable right now would
    /// actually do something (as opposed to silently no-opping). Used to light up a
    /// highlight ring under the cursor's current target while a drag is in progress,
    /// so a failed drop doesn't come as a surprise. Default false: most Interactables
    /// (labels, decorative fixtures) are never valid drop targets.</summary>
    public virtual bool CanAcceptDrop(Carryable item) => false;
}
