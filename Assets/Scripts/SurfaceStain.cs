using UnityEngine;

/// <summary>
/// A stain lying on a WipeableSurface: a flat patch just above it with its own
/// small mask texture, so the cloth takes it off only where it actually went -
/// a little more each time it passes (never all at once). No collider: it
/// never gets in the way of a click. Food, peel and tools are separate objects
/// and are never touched by wiping.
/// </summary>
public class SurfaceStain : MonoBehaviour
{
    private const int Res = 48;
    private const float Lift = 0.003f;

    private Texture2D mask;
    private Color32[] px;
    private float radius;
    private float total0, total;
    private bool dirty;

    /// <summary>How much is left (1 = as it was put down, 0 = gone).</summary>
    public float Remaining => total0 > 0f ? total / total0 : 0f;
    public bool Cleared => Remaining < 0.04f;

    public static SurfaceStain Create(WipeableSurface surface, Vector3 point, float radius, Color color, int seed)
    {
        // At the scene root, unscaled: the counters are scaled boxes, and as their
        // child the patch (and its round mask) would be stretched.
        var go = new GameObject("Stain");
        go.transform.SetPositionAndRotation(point + surface.Normal * Lift, Quaternion.LookRotation(surface.Forward, surface.Normal));
        var stain = go.AddComponent<SurfaceStain>();
        stain.Build(radius, color, seed);
        return stain;
    }

    private void Build(float r, Color color, int seed)
    {
        radius = r;
        // flat quad on local x-z, facing +y
        var mesh = new Mesh { name = "StainQuad" };
        mesh.vertices = new[] { new Vector3(-r, 0f, -r), new Vector3(r, 0f, -r), new Vector3(-r, 0f, r), new Vector3(r, 0f, r) };
        mesh.uv = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f) };
        mesh.triangles = new[] { 0, 2, 1, 1, 2, 3 };
        mesh.RecalculateNormals();
        gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;

        // an uneven blob, a few drops round it
        var rnd = new System.Random(seed);
        float p1 = (float)rnd.NextDouble() * 6.3f, p2 = (float)rnd.NextDouble() * 6.3f, p3 = (float)rnd.NextDouble() * 6.3f;
        float nx = (float)rnd.NextDouble() * 50f, ny = (float)rnd.NextDouble() * 50f;
        var drops = new Vector3[3];
        for (int i = 0; i < drops.Length; i++)
        {
            float a = (float)rnd.NextDouble() * 6.3f, d = 0.62f + (float)rnd.NextDouble() * 0.25f;
            drops[i] = new Vector3(Mathf.Cos(a) * d, Mathf.Sin(a) * d, 0.05f + (float)rnd.NextDouble() * 0.06f);
        }
        px = new Color32[Res * Res];
        byte cr = (byte)(color.r * 255f), cg = (byte)(color.g * 255f), cb = (byte)(color.b * 255f);
        for (int y = 0; y < Res; y++)
            for (int x = 0; x < Res; x++)
            {
                float u = (x + 0.5f) / Res * 2f - 1f, v = (y + 0.5f) / Res * 2f - 1f;
                float d = Mathf.Sqrt(u * u + v * v), ang = Mathf.Atan2(v, u);
                float edge = 0.5f + 0.08f * Mathf.Sin(ang * 2f + p1) + 0.06f * Mathf.Sin(ang * 3f + p2) + 0.04f * Mathf.Sin(ang * 5f + p3);
                float a = Mathf.InverseLerp(edge, edge - 0.12f, d);
                foreach (Vector3 dr in drops)
                    a = Mathf.Max(a, Mathf.InverseLerp(dr.z, dr.z * 0.5f, Vector2.Distance(new Vector2(u, v), dr)));
                a *= 0.7f + 0.3f * Mathf.PerlinNoise(nx + u * 3f, ny + v * 3f);
                px[y * Res + x] = new Color32(cr, cg, cb, (byte)(Mathf.Clamp01(a) * color.a * 255f));
            }
        foreach (Color32 c in px) total0 += c.a;
        total = total0;

        mask = new Texture2D(Res, Res, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
        mask.SetPixels32(px);
        mask.Apply();
        Shader sh = Shader.Find("Sprites/Default");
        if (sh == null) sh = Shader.Find("Unlit/Transparent");
        var mat = new Material(sh) { mainTexture = mask, renderQueue = 3000 };
        gameObject.AddComponent<MeshRenderer>().sharedMaterial = mat;
    }

    /// <summary>The cloth's path a -> b (world, on the surface), radius wide:
    /// each spot it covers loses amount of its colour (less toward the cloth's
    /// edge). The path is taken as a segment, so a fast stroke leaves no gaps.</summary>
    public void Wipe(Vector3 a, Vector3 b, float wipeRadius, float amount)
    {
        if (px == null) return;
        Vector3 la = transform.InverseTransformPoint(a), lb = transform.InverseTransformPoint(b);
        float scale = Res / (2f * radius);
        Vector2 pa = new Vector2((la.x + radius) * scale, (la.z + radius) * scale);
        Vector2 pb = new Vector2((lb.x + radius) * scale, (lb.z + radius) * scale);
        float R = wipeRadius * scale;
        int x0 = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(pa.x, pb.x) - R)), x1 = Mathf.Min(Res - 1, Mathf.CeilToInt(Mathf.Max(pa.x, pb.x) + R));
        int y0 = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(pa.y, pb.y) - R)), y1 = Mathf.Min(Res - 1, Mathf.CeilToInt(Mathf.Max(pa.y, pb.y) + R));
        if (x0 > x1 || y0 > y1) return; // the cloth went nowhere near it

        Vector2 ab = pb - pa;
        float len2 = Mathf.Max(ab.sqrMagnitude, 1e-6f);
        for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
            {
                int i = y * Res + x;
                if (px[i].a == 0) continue;
                Vector2 p = new Vector2(x + 0.5f, y + 0.5f);
                float t = Mathf.Clamp01(Vector2.Dot(p - pa, ab) / len2);
                float d = Vector2.Distance(p, pa + ab * t);
                if (d >= R) continue;
                float k = 1f - (d / R) * (d / R);
                int na = Mathf.Max(0, px[i].a - Mathf.CeilToInt(amount * k * 255f));
                total -= px[i].a - na;
                px[i].a = (byte)na;
                dirty = true;
            }
    }

    void LateUpdate()
    {
        if (!dirty) return;
        dirty = false;
        mask.SetPixels32(px);
        mask.Apply();
    }

    void OnDestroy()
    {
        if (mask != null) Destroy(mask);
        if (GetComponent<MeshRenderer>() is MeshRenderer r && r.sharedMaterial != null) Destroy(r.sharedMaterial);
        if (GetComponent<MeshFilter>() is MeshFilter f && f.sharedMesh != null) Destroy(f.sharedMesh);
    }
}
