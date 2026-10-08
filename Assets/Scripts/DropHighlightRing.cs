using UnityEngine;

/// <summary>
/// A single reusable glow shown under whatever valid drop target the cursor is
/// currently over during a drag, so a failed drop doesn't come as a surprise -
/// the player can see, while still dragging, whether releasing here will work.
/// </summary>
public class DropHighlightRing : MonoBehaviour
{
    public static DropHighlightRing Instance { get; private set; }

    [SerializeField] private Renderer glowRenderer;

    private Transform currentTarget;

    void Awake()
    {
        Instance = this;
        if (glowRenderer == null) glowRenderer = GetComponent<Renderer>();
        Hide();
    }

    public void Show(Transform target)
    {
        if (target == currentTarget) return;
        currentTarget = target;

        Vector3 scale = target.lossyScale;
        transform.position = new Vector3(target.position.x, target.position.y, target.position.z + 0.03f);
        transform.localScale = new Vector3(Mathf.Abs(scale.x) * 1.15f, Mathf.Abs(scale.y) * 1.15f, 1f);
        gameObject.SetActive(true);
    }

    public void Hide()
    {
        currentTarget = null;
        gameObject.SetActive(false);
    }
}
