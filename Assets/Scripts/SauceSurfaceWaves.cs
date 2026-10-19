using UnityEngine;

/// <summary>Bounded visual ripples; never changes the amount or composition of sauce.</summary>
public sealed class SauceSurfaceWaves : MonoBehaviour
{
    public float Motion;
    private Mesh surface;
    private Vector3[] rest, vertices;
    private float radius;
    public void Initialize(float r)
    {
        radius = r;
        MeshRenderer original = GetComponentInChildren<MeshRenderer>();
        if (original == null) return;
        original.enabled = false;
        var go = new GameObject("Pool");
        go.transform.SetParent(transform, false);
        var renderer = go.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = original.sharedMaterial;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        const int rings = 10, segments = 64;
        rest = new Vector3[(rings + 1) * segments];
        vertices = new Vector3[rest.Length];
        int[] indices = new int[rings * segments * 6];
        int t = 0;
        for (int ring = 0; ring <= rings; ring++)
            for (int j = 0; j < segments; j++)
            {
                float a = j * Mathf.PI * 2f / segments;
                rest[ring * segments + j] = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * (r * ring / rings);
                if (ring == rings) continue;
                int a0 = ring * segments + j, a1 = ring * segments + (j + 1) % segments;
                indices[t++] = a0; indices[t++] = a1; indices[t++] = a0 + segments;
                indices[t++] = a1; indices[t++] = a1 + segments; indices[t++] = a0 + segments;
            }
        surface = new Mesh { name = "Sauce ripples" };
        surface.MarkDynamic();
        surface.vertices = rest;
        surface.triangles = indices;
        surface.RecalculateNormals();
        go.AddComponent<MeshFilter>().sharedMesh = surface;
    }
    void LateUpdate()
    {
        if (surface == null) return;
        float amplitude = Mathf.Min(0.018f, radius * 0.04f) * Motion;
        for (int i = 0; i < rest.Length; i++)
        {
            Vector3 p = rest[i];
            float distance = new Vector2(p.x, p.z).magnitude / Mathf.Max(radius, 0.001f);
            p.y = amplitude * (Mathf.Sin(p.x / radius * 5f - Time.time * 7f)
                + Mathf.Cos(p.z / radius * 6f + Time.time * 5f)) * (1f - distance * distance);
            vertices[i] = p;
        }
        surface.vertices = vertices;
        surface.RecalculateNormals();
        surface.RecalculateBounds();
    }
    void OnDestroy() { if (surface != null) Destroy(surface); }
}
