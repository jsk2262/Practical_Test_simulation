using System.Collections;
using UnityEngine;

/// <summary>
/// 달걀 깨기: the egg in hand, pressed and held on a bowl (or pan) - the same
/// filling ring as taking up a tool (ClickDetector) - it's knocked once on
/// the bowl's rim (톡), opened over it, and what's inside drops in
/// (EggContents: the white and the yolk, whole). The empty shell - two
/// halves - is set down beside the bowl, to be thrown out (일반쓰레기, 정리).
/// A quick click on the bowl still just sets the whole egg in it.
/// </summary>
[RequireComponent(typeof(IngredientItem))]
public class EggCracker : MonoBehaviour
{
    [SerializeField] private Color yolkColor = new Color(1f, 0.66f, 0.1f);
    [SerializeField] private Color whiteColor = new Color(0.96f, 0.95f, 0.86f);

    /// <summary>Broken: what's left is the empty shell.</summary>
    public bool IsCracked { get; private set; }
    public Transform LeftHalf { get; private set; }
    public Transform RightHalf { get; private set; }

    private IngredientItem egg;
    private bool busy;

    void Awake() => egg = GetComponent<IngredientItem>();

    /// <summary>Whole and raw, in hand; the bowl a plain one (not a serving dish,
    /// the frying oil, under a lid or a cheesecloth) lying somewhere.</summary>
    public bool CanCrackInto(MovableBowl bowl) =>
        !IsCracked && !busy && egg != null && egg.CurrentState == IngredientItem.State.Raw
        && HandCarry.Instance != null && HandCarry.Instance.Current == egg
        && bowl != null && !bowl.IsHeld && !bowl.IsDish && !bowl.HasFryingOil && !bowl.HasLid && Cheesecloth.On(bowl) == null
        && (EggContents.Of(bowl, false) == null || !EggContents.Of(bowl, false).Busy);

    /// <summary>What the press-and-hold on that bowl does once its ring fills.</summary>
    public IClickable CrackIntoAction(MovableBowl bowl) => new CrackInto(this, bowl);

    private sealed class CrackInto : IClickable
    {
        private readonly EggCracker egg;
        private readonly MovableBowl bowl;
        public CrackInto(EggCracker e, MovableBowl b) { egg = e; bowl = b; }
        public void OnClicked() { if (egg.CanCrackInto(bowl)) egg.StartCoroutine(egg.CrackRoutine(bowl)); }
    }

    private IEnumerator CrackRoutine(MovableBowl bowl)
    {
        busy = true;
        HandCarry hand = HandCarry.Instance;
        hand.IsLocked = true;
        HandController right = HandController.Instance;
        Renderer shell = GetComponent<Renderer>();
        Bounds b = shell.bounds;
        float r = Mathf.Min(b.extents.x, b.extents.y) * 0.95f;
        float len = Mathf.Max(b.extents.x, b.extents.y);
        Vector3 up = Carryable.CounterUp;

        MovableBowl.Interior inside = bowl.GetInterior();
        Vector3 rimTop = inside.floorCenter + inside.up * inside.rim;
        Vector3 rimEdge = rimTop + Vector3.right * inside.radius;           // the right-hand rim
        Vector3 handFromEgg = right != null ? right.transform.position - transform.position : Vector3.zero;

        // to the bowl's rim...
        right?.GoTo(rimEdge + up * (r * 1.4f) + handFromEgg, null);
        yield return new WaitForSeconds(0.4f);

        // ...톡: knocked once on it, a crack round its middle
        for (float t = 0f; t < 0.07f; t += Time.deltaTime) { right?.SetGestureOffset(-up * (0.12f * t / 0.07f)); yield return null; }
        Vector3 s0 = transform.localScale;
        transform.localScale = new Vector3(s0.x * 1.04f, s0.y * 0.93f, s0.z * 1.04f);
        GameObject crack = CrackLine(shell.bounds.center, r);
        for (float t = 0f; t < 0.12f; t += Time.deltaTime) { right?.SetGestureOffset(-up * (0.12f * (1f - t / 0.12f))); yield return null; }
        right?.SetGestureOffset(Vector3.zero);
        transform.localScale = s0;
        yield return new WaitForSeconds(0.15f);

        // over the middle of the bowl, opened downward: what's inside drops in
        Vector3 over = rimTop + up * (r * 1.6f);
        right?.GoTo(over + handFromEgg, null);
        yield return new WaitForSeconds(0.35f);
        Destroy(crack);
        Vector3 c = shell.bounds.center;
        shell.enabled = false;
        Material shellMat = new Material(shell.sharedMaterial);
        LeftHalf = Half("Shell_L", c, r, len, shellMat);
        RightHalf = Half("Shell_R", c, r, len, shellMat);
        Transform drop = new GameObject("EggDrop").transform;
        drop.position = c;
        Transform white = Blob(drop, "White", whiteColor);
        white.localScale = new Vector3(r * 1.5f, r * 0.9f, r * 1.5f);
        Transform yolk = Blob(drop, "Yolk", yolkColor);
        yolk.localScale = Vector3.one * r * 0.9f;
        yolk.localPosition = new Vector3(0f, 0f, -r * 0.25f);

        Quaternion closedL = Quaternion.LookRotation(Vector3.forward, Vector3.right);  // open sides facing each other
        Quaternion closedR = Quaternion.LookRotation(Vector3.forward, Vector3.left);
        Quaternion openDown = Quaternion.LookRotation(Vector3.Cross(Vector3.right, -up), -up);
        Vector3 floor = inside.floorCenter + inside.up * inside.rim * 0.12f;
        for (float t = 0f; t < 0.45f; t += Time.deltaTime)
        {
            float k = Mathf.SmoothStep(0f, 1f, t / 0.45f);
            LeftHalf.SetPositionAndRotation(c + Vector3.left * (r * 1.3f * k), Quaternion.Slerp(closedL, openDown, k));
            RightHalf.SetPositionAndRotation(c + Vector3.right * (r * 1.3f * k), Quaternion.Slerp(closedR, openDown, k));
            float f = Mathf.Clamp01((t / 0.45f - 0.2f) / 0.8f);
            drop.position = Vector3.Lerp(c, floor, f * f);                     // falling
            drop.localScale = new Vector3(1f + f * 0.8f, 1f - f * 0.5f, 1f + f * 0.8f); // and spreading as it lands
            yield return null;
        }
        Destroy(drop.gameObject);
        EggContents.Of(bowl, true).AddEgg();
        yield return new WaitForSeconds(0.25f);

        // the empty shell set down beside the bowl, open sides up
        Vector3 aside = inside.floorCenter + Vector3.right * (inside.radius + 0.55f);
        hand.IsLocked = false;
        hand.PlaceFreeAt(aside);
        Vector3 at = transform.position;
        float depth = Mathf.Min(len, r * 1.35f);
        Quaternion openUp = Quaternion.LookRotation(Vector3.Cross(Vector3.right, up), up);
        LeftHalf.SetPositionAndRotation(at + Vector3.left * r * 1.1f + up * (depth - r), openUp);
        RightHalf.SetPositionAndRotation(at + Vector3.right * r * 1.1f + up * (depth - r), openUp);
        right?.GoTo(at + new Vector3(0.4f, 0.3f, -0.55f), null);
        // the egg's click box over both halves now
        if (GetComponent<BoxCollider>() is BoxCollider box)
        {
            Bounds both = new Bounds(LeftHalf.position, Vector3.one * r * 2f);
            both.Encapsulate(new Bounds(RightHalf.position, Vector3.one * r * 2f));
            box.center = transform.InverseTransformPoint(both.center);
            Vector3 sz = transform.InverseTransformVector(both.size);
            box.size = new Vector3(Mathf.Abs(sz.x), Mathf.Abs(sz.y), Mathf.Abs(sz.z));
        }
        IsCracked = true;
        busy = false;
        Debug.Log($"[{egg.IngredientName}] {bowl.name}에 깨 넣음 - 껍데기는 옆에 (일반쓰레기)");
    }

    /// <summary>Half of the shell: a cup, its rim at the pivot, in the egg's own colours.</summary>
    private Transform Half(string name, Vector3 at, float r, float len, Material m)
    {
        var go = new GameObject(name);
        go.transform.SetParent(transform, true);
        go.transform.position = at;
        go.AddComponent<MeshFilter>().sharedMesh = CupMesh(r, Mathf.Min(len, r * 1.35f), 14, 7);
        go.AddComponent<MeshRenderer>().sharedMaterial = m;
        go.transform.localScale = new Vector3(1f / Mathf.Max(transform.lossyScale.x, 1e-4f), 1f / Mathf.Max(transform.lossyScale.y, 1e-4f), 1f / Mathf.Max(transform.lossyScale.z, 1e-4f));
        return go.transform;
    }

    private static Transform Blob(Transform parent, string name, Color color)
    {
        GameObject g = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        g.name = name;
        Destroy(g.GetComponent<Collider>());
        g.transform.SetParent(parent, false);
        Shader lit = Shader.Find("Universal Render Pipeline/Lit");
        var m = new Material(lit != null ? lit : Shader.Find("Standard")) { color = color };
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", color);
        if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.8f);
        g.GetComponent<Renderer>().sharedMaterial = m;
        return g.transform;
    }

    /// <summary>The crack: a thin dark zigzag round the egg's middle, a moment before it opens.</summary>
    private GameObject CrackLine(Vector3 c, float r)
    {
        var go = new GameObject("Crack");
        go.transform.SetParent(transform, true);
        var lr = go.AddComponent<LineRenderer>();
        Shader sh = Shader.Find("Universal Render Pipeline/Unlit");
        lr.material = new Material(sh != null ? sh : Shader.Find("Sprites/Default")) { color = new Color(0.35f, 0.28f, 0.2f) };
        lr.widthMultiplier = 0.01f;
        lr.useWorldSpace = false;
        lr.positionCount = 9;
        for (int i = 0; i < 9; i++)
        {
            float x = Mathf.Lerp(-r, r, i / 8f);
            lr.SetPosition(i, transform.InverseTransformPoint(c + new Vector3(x, (i % 2 == 0 ? 1f : -1f) * r * 0.08f, -r * 1.05f)));
        }
        return go;
    }

    /// <summary>A cup (half an egg's shell) opening up along +y: rim at y=0, its
    /// bottom `depth` below; inside and out (so the inside shows too).</summary>
    private static Mesh CupMesh(float r, float depth, int around, int rings)
    {
        var v = new System.Collections.Generic.List<Vector3>();
        var t = new System.Collections.Generic.List<int>();
        for (int j = 0; j <= rings; j++)
        {
            float a = j / (float)rings * Mathf.PI * 0.5f; // 0 rim .. 90° bottom
            float rr = r * Mathf.Cos(a), y = -depth * Mathf.Sin(a);
            for (int i = 0; i <= around; i++)
            {
                float b = i / (float)around * Mathf.PI * 2f;
                v.Add(new Vector3(Mathf.Cos(b) * rr, y, Mathf.Sin(b) * rr));
            }
        }
        int row = around + 1, count = v.Count;
        for (int j = 0; j < rings; j++)
            for (int i = 0; i < around; i++)
            {
                int a0 = j * row + i, a1 = a0 + 1, b0 = a0 + row, b1 = b0 + 1;
                t.AddRange(new[] { a0, a1, b0, a1, b1, b0 });           // outside
            }
        for (int i = 0; i < count; i++) v.Add(v[i] * 0.97f);           // inside, a hair smaller
        for (int j = 0; j < rings; j++)
            for (int i = 0; i < around; i++)
            {
                int a0 = count + j * row + i, a1 = a0 + 1, b0 = a0 + row, b1 = b0 + 1;
                t.AddRange(new[] { a0, b0, a1, a1, b0, b1 });
            }
        var m = new Mesh { name = "EggShellHalf" };
        m.SetVertices(v);
        m.SetTriangles(t, 0);
        m.RecalculateNormals();
        m.RecalculateBounds();
        return m;
    }
}
