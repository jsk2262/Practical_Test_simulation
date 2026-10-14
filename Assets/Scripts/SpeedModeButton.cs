using UnityEngine;

/// <summary>
/// 빠른 모드 on/off (SpeedMode) - under the 제출 button. Lit when on.
/// </summary>
public class SpeedModeButton : MonoBehaviour, IClickable
{
    [SerializeField] private TextMesh label;
    [SerializeField] private Renderer back;
    private Material mat;

    private static readonly Color Off = new Color(0.32f, 0.36f, 0.4f), OnColor = new Color(0.85f, 0.6f, 0.15f);

    void Awake()
    {
        if (label == null) label = GetComponentInChildren<TextMesh>();
        if (back == null) back = GetComponent<Renderer>();
        if (back != null) mat = back.material; // (its own copy - the asset isn't changed)
    }

    void OnEnable()
    {
        SpeedMode.Changed += OnChanged;
        Refresh();
    }

    void OnDisable() => SpeedMode.Changed -= OnChanged;

    private void OnChanged(bool on) => Refresh();

    public void OnClicked() => SpeedMode.Toggle();

    private void Refresh()
    {
        if (label != null) label.text = SpeedMode.On ? "빠른 모드 ON" : "빠른 모드 OFF";
        if (mat != null) mat.color = SpeedMode.On ? OnColor : Off;
    }
}
