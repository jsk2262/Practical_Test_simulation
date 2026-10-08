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

    private Material offMaterial;
    private Vector3 baseScale;
    private Coroutine bounce;

    void Awake()
    {
        if (tabRenderer == null) tabRenderer = GetComponent<Renderer>();
        if (tabRenderer != null) offMaterial = tabRenderer.sharedMaterial;
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

    public void OnClicked() => CleanupMode.Toggle();

    private void Refresh(bool on)
    {
        if (tabRenderer != null && onMaterial != null) tabRenderer.sharedMaterial = on ? onMaterial : offMaterial;
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
