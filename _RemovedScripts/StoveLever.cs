using UnityEngine;

/// <summary>
/// The physical control knob for one burner - clicking it rotates through the
/// same off/weak/medium/strong sequence a real stove dial would step through,
/// rather than the burner's own click-to-toggle. Turning it up from off lights
/// the burner at whatever level it lands on; turning it back to off shuts it.
/// </summary>
public class StoveLever : MonoBehaviour, IClickable
{
    [SerializeField] private StoveBurner burner;

    public void OnClicked()
    {
        if (burner == null) return;

        if (!burner.IsOn)
        {
            burner.SetLevel(StoveBurner.FlameLevel.Weak);
            burner.ForceActivate();
            return;
        }

        switch (burner.Level)
        {
            case StoveBurner.FlameLevel.Weak:
                burner.SetLevel(StoveBurner.FlameLevel.Medium);
                break;
            case StoveBurner.FlameLevel.Medium:
                burner.SetLevel(StoveBurner.FlameLevel.Strong);
                break;
            case StoveBurner.FlameLevel.Strong:
                burner.ForceActivate(); // back to off
                break;
        }
    }
}
