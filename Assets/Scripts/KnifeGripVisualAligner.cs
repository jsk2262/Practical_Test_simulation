using UnityEngine;

// Moves only a dedicated visual wrapper, never HandController or the knife.
// Runs after HandCarry.LateUpdate, so the knife remains the authoritative pose.
[DefaultExecutionOrder(10000)]
public sealed class KnifeGripVisualAligner : MonoBehaviour
{
    [SerializeField] private Transform visualRoot;
    [SerializeField] private Transform palmGripPoint;
    [SerializeField] private string knifeAnchorName = "KnifeGripAnchor";
    private Vector3 restPosition;
    private Quaternion restRotation;
    private bool initialized;
    private Transform lastKnife;
    private Transform anchor;

    private void Awake()
    {
        if (visualRoot == null || palmGripPoint == null || visualRoot == transform ||
            !visualRoot.IsChildOf(transform) || !palmGripPoint.IsChildOf(visualRoot) ||
            visualRoot.GetComponentInChildren<HandCarry>(true) != null)
        {
            Debug.LogError("Assign a dedicated hand-visual child and a grip marker inside it. Do not include HandCarry or the knife.", this);
            enabled = false;
            return;
        }
        restPosition = visualRoot.localPosition;
        restRotation = visualRoot.localRotation;
        initialized = true;
    }

    private void LateUpdate()
    {
        Restore();
        Carryable held = HandCarry.Instance != null ? HandCarry.Instance.Current : null;
        if (!(held is ToolItem tool) || tool.Kind != ToolKind.Knife)
        {
            lastKnife = null;
            anchor = null;
            return;
        }
        if (lastKnife != tool.transform)
        {
            lastKnife = tool.transform;
            anchor = null;
            foreach (Transform child in tool.GetComponentsInChildren<Transform>(true))
                if (child.name == knifeAnchorName) { anchor = child; break; }
            if (anchor == null)
                Debug.LogWarning("KnifeGripAnchor is missing; using the original hand placement.", tool);
        }
        if (anchor == null) return;
        // Prevent transform feedback if the knife was accidentally parented to the visual.
        if (anchor.IsChildOf(visualRoot)) return;
        Quaternion correction = anchor.rotation * Quaternion.Inverse(palmGripPoint.rotation);
        visualRoot.rotation = correction * visualRoot.rotation;
        visualRoot.position += anchor.position - palmGripPoint.position;
    }

    private void Restore()
    {
        if (!initialized || visualRoot == null) return;
        visualRoot.localPosition = restPosition;
        visualRoot.localRotation = restRotation;
    }
    private void OnDisable() { Restore(); }
}
