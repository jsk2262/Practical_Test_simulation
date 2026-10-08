using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A surface the cloth (ClothWiper) can wipe - the counter tops and the strips
/// round the sink. Its top face is the top of its mesh's box (local +y), found
/// by a plane test, not a collider: no collider is added, so nothing else's
/// clicks or drops are changed by it. Holds the stains (SurfaceStain) on it;
/// testStains of them are put down at the start (test stains - there's no
/// other source of mess yet), where nothing stands on the surface.
/// </summary>
public class WipeableSurface : MonoBehaviour
{
    [Tooltip("Test stains put down at the start (0: none).")]
    [SerializeField] private int testStains = 0;
    [SerializeField] private Vector2 stainRadius = new Vector2(0.12f, 0.24f);
    [Tooltip("How far in from the edge (world units) still counts as on it.")]
    [SerializeField] private float edgeInset = 0.04f;

    public static readonly List<WipeableSurface> All = new List<WipeableSurface>();

    private readonly List<SurfaceStain> stains = new List<SurfaceStain>();
    private Bounds box;

    public Vector3 Normal => transform.up;
    public Vector3 Forward => Vector3.ProjectOnPlane(transform.forward, Normal).normalized;
    public int StainCount { get { stains.RemoveAll(s => s == null); return stains.Count; } }

    /// <summary>What's left of all its stains together (1 = as they were).</summary>
    public float DirtLeft
    {
        get
        {
            stains.RemoveAll(s => s == null);
            float sum = 0f;
            foreach (SurfaceStain s in stains) sum += s.Remaining;
            return sum;
        }
    }

    void Awake()
    {
        MeshFilter mf = GetComponent<MeshFilter>();
        box = mf != null && mf.sharedMesh != null ? mf.sharedMesh.bounds : new Bounds(Vector3.zero, Vector3.one);
    }

    void OnEnable() => All.Add(this);
    void OnDisable() => All.Remove(this);

    void Start()
    {
        if (testStains <= 0) return;
        var rnd = new System.Random(name.GetHashCode());
        Color[] tints =
        {
            new Color(0.42f, 0.26f, 0.12f, 0.8f),  // sauce
            new Color(0.55f, 0.45f, 0.2f, 0.65f),  // oil
            new Color(0.35f, 0.3f, 0.28f, 0.6f),   // grime
        };
        for (int i = 0, tries = 0; i < testStains && tries < 60; tries++)
        {
            float r = Mathf.Lerp(stainRadius.x, stainRadius.y, (float)rnd.NextDouble());
            Vector3 s = transform.lossyScale;
            float ix = (r + edgeInset) / Mathf.Abs(s.x), iz = (r + edgeInset) / Mathf.Abs(s.z);
            if (box.size.x - 2f * ix <= 0f || box.size.z - 2f * iz <= 0f) break; // too narrow for one
            Vector3 local = new Vector3(Mathf.Lerp(box.min.x + ix, box.max.x - ix, (float)rnd.NextDouble()), box.max.y,
                                        Mathf.Lerp(box.min.z + iz, box.max.z - iz, (float)rnd.NextDouble()));
            Vector3 p = transform.TransformPoint(local);
            if (Covered(p, r)) continue; // under the board, a bowl, the stove... - not there
            AddStain(p, r, tints[rnd.Next(tints.Length)], rnd.Next());
            i++;
        }
    }

    /// <summary>A stain put down here (a spill, a drip - or a test stain).</summary>
    public SurfaceStain AddStain(Vector3 point, float radius, Color color, int seed)
    {
        SurfaceStain s = SurfaceStain.Create(this, point, radius, color, seed);
        stains.Add(s);
        return s;
    }

    /// <summary>The ray onto its top face, if it lands inside it.</summary>
    public bool Raycast(Ray ray, out Vector3 point, out float distance)
    {
        Vector3 top = transform.TransformPoint(new Vector3(box.center.x, box.max.y, box.center.z));
        point = default;
        if (!new Plane(Normal, top).Raycast(ray, out distance)) return false;
        point = ray.GetPoint(distance);
        Vector3 l = transform.InverseTransformPoint(point);
        Vector3 s = transform.lossyScale;
        float ix = edgeInset / Mathf.Abs(s.x), iz = edgeInset / Mathf.Abs(s.z);
        return l.x > box.min.x + ix && l.x < box.max.x - ix && l.z > box.min.z + iz && l.z < box.max.z - iz;
    }

    /// <summary>The cloth went from a to b (on this face) - the stains it passed
    /// over lose some of their colour where it went; gone ones are removed.</summary>
    public void Wipe(Vector3 a, Vector3 b, float radius, float amount)
    {
        for (int i = stains.Count - 1; i >= 0; i--)
        {
            SurfaceStain s = stains[i];
            if (s == null) { stains.RemoveAt(i); continue; }
            s.Wipe(a, b, radius, amount);
            if (s.Cleared) { stains.RemoveAt(i); Destroy(s.gameObject); }
        }
    }

    /// <summary>Something stands over this point (seen from the camera) - a
    /// test stain isn't put where nobody could reach it.</summary>
    private static bool Covered(Vector3 p, float r)
    {
        Camera cam = Camera.main;
        if (cam == null) return false;
        foreach (Vector3 o in new[] { Vector3.zero, Vector3.right * r, Vector3.left * r, Vector3.up * r * 0.7f, Vector3.down * r * 0.7f })
            if (ClothWiper.Blocker(cam, p + o) != null) return true;
        return false;
    }
}
