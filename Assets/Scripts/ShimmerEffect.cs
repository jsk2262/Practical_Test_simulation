using UnityEngine;

/// <summary>Slow, gentle alpha/scale pulse - a "look here, this is clickable"
/// shimmer for markers that would otherwise blend into the scene or have no
/// visual of their own (e.g. the cutting board's flip zone).</summary>
public class ShimmerEffect : MonoBehaviour
{
    [SerializeField] private float period = 2.4f;
    [SerializeField] private float minAlpha = 0.35f;
    [SerializeField] private float maxAlpha = 0.95f;
    [SerializeField] private float minScale = 0.9f;
    [SerializeField] private float maxScale = 1.15f;

    private Renderer rend;
    private Vector3 baseScale;
    private Color baseColor;

    void Awake()
    {
        rend = GetComponent<Renderer>();
        baseScale = transform.localScale;
        if (rend != null) baseColor = rend.material.color;
    }

    void Update()
    {
        float t = (Mathf.Sin(Time.time * (Mathf.PI * 2f / period)) + 1f) * 0.5f;

        if (rend != null)
        {
            Color c = baseColor;
            c.a = Mathf.Lerp(minAlpha, maxAlpha, t);
            rend.material.color = c;
        }

        transform.localScale = baseScale * Mathf.Lerp(minScale, maxScale, t);
    }
}
