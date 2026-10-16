using System.Collections;
using UnityEngine;

/// <summary>
/// The 정리 tab (next to 힌트/규격/양념): click to switch clean-up mode on/off
/// (CleanupMode) - lit yellow while on. When a dish is clicked with it off,
/// the tab gives a little bounce to point the player at it.
/// Made by Tools > Kitchen > Setup Cleanup Tab.
/// </summary>
public class CleanupTabButton : MonoBehaviour, IClickable
{
    [SerializeField] private Renderer tabRenderer;
    /// <summary>Look while on - the 힌트 tab's yellow (the tab models' own shader
    /// has no tint to change, so the whole material is swapped).</summary>
    [SerializeField] private Material onMaterial;

    private Material offMaterial, activeGreen;
    private Vector3 baseScale;
    private Coroutine bounce;

    void Awake()
    {
        if (tabRenderer == null) tabRenderer = GetComponent<Renderer>();
        if (tabRenderer != null) offMaterial = tabRenderer.sharedMaterial;
        if (onMaterial != null || offMaterial != null)
        {
            activeGreen = new Material(onMaterial != null ? onMaterial : offMaterial);
            activeGreen.color = new Color(0.16f, 0.72f, 0.18f);
            if (activeGreen.HasProperty("_BaseColor")) activeGreen.SetColor("_BaseColor", activeGreen.color);
        }
        baseScale = transform.localScale;
    }

    void OnEnable()
    {
        CleanupMode.Changed += Refresh;
        CleanupMode.Blocked += Nudge;
        Refresh(CleanupMode.IsOn);
    }

    void OnDisable()
    {
        CleanupMode.Changed -= Refresh;
        CleanupMode.Blocked -= Nudge;
    }

    public void OnClicked()
    {
        if (ExamFlow.Current == ExamFlow.Phase.Cleaning) CleanupMode.Set(true);
        else if (ExamFlow.Current == ExamFlow.Phase.Cooking) CleanupMode.Toggle();
    }
    void LateUpdate() => Refresh(CleanupMode.IsOn);
    void OnDestroy() { if (activeGreen != null) Destroy(activeGreen); }

    private void Refresh(bool on)
    {
        if (tabRenderer != null && activeGreen != null) tabRenderer.sharedMaterial = on ? activeGreen : offMaterial;
    }

    private void Nudge()
    {
        if (bounce != null) StopCoroutine(bounce);
        bounce = StartCoroutine(Bounce());
    }

    private IEnumerator Bounce()
    {
        for (float t = 0f; t < 0.4f; t += Time.deltaTime)
        {
            transform.localScale = baseScale * (1f + Mathf.Sin(t / 0.4f * Mathf.PI * 3f) * 0.15f * (1f - t / 0.4f));
            yield return null;
        }
        transform.localScale = baseScale;
        bounce = null;
    }
}
