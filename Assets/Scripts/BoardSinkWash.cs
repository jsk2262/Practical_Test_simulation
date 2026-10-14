using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 도마 씻기 (정리 mode), by hand:
/// - "씻기" on the board (CuttingBoardWashZone) picks the board up - no
///   animation, it just follows the mouse. Only an empty board (no
///   ingredient, scraps or towel on it), and the knife kept
///   apart (not resting on the board) - washed before it or not (that order is
///   only recorded, for the final evaluation).
/// - Clicked over the sink: it's leant in the sink, corner-ways, and washed -
///   water run over it (down its slope and off the low end), dish soap
///   squeezed on (DishSoap), scrubbed back and forth with the Sponge, turned
///   over and the other face the same, rinsed, then dried with a dry cloth.
///   It stays there, washed; clicking it picks it up again.
/// - Clicked on the counter: put down there. Esc (or 정리 off) while carrying:
///   back where it was picked up.
/// While it's carried, ClickDetector leaves the mouse to it (InputCaptured).
/// </summary>
[DefaultExecutionOrder(-90)]
[RequireComponent(typeof(CuttingBoardController))]
public class BoardSinkWash : MonoBehaviour
{
    [Header("씻을 때 쓰는 것 (비우면 이름으로 찾음)")]
    [SerializeField] private Transform dishSoap;
    [SerializeField] private Transform sponge;
    [Tooltip("The dry cloth it's wiped with last (a copy of this model is used).")]
    [SerializeField] private Transform dryCloth;

    [Header("개수대에 기대 놓기")]
    [Tooltip("How far it's turned (degrees, on screen) leaning in the sink.")]
    [SerializeField] private float sinkLean = 32f;
    [Tooltip("Where its middle goes, from the middle of the basin.")]
    [SerializeField] private Vector3 sinkOffset = new Vector3(0.55f, 0.1f, 0f);
    [Tooltip("Toward the camera from where it sits on the counter (so the sink's walls don't hide it).")]
    [SerializeField] private float sinkDepthShift = -0.45f;

    [Header("시간")]
    [SerializeField] private float rinseSeconds = 1.5f;
    [SerializeField] private float scrubSeconds = 2.4f;
    [SerializeField] private float drySeconds = 1.6f;

    public static BoardSinkWash Instance { get; private set; }
    /// <summary>The board is being carried: the mouse is its own (ClickDetector stands aside).</summary>
    public static bool InputCaptured => Instance != null && Instance.state == State.Carrying;

    private enum State { Home, Carrying, InSink }
    private State state = State.Home;
    private bool washing;
    /// <summary>Not on the counter where it's used (carried, or in the sink).</summary>
    public bool IsAway => state != State.Home;

    private CuttingBoardController board;
    private Collider col;
    private Quaternion homeRot;
    private float zOffset, halfLen, halfWid;
    private Vector3 pickedPos;
    private Quaternion pickedRot;
    private State pickedFrom;
    private bool waitRelease;
    private Vector3 grabOffset; // board pivot - its visible middle
    private readonly List<GameObject> zones = new List<GameObject>();
    private readonly List<Vector3> zoneOffsets = new List<Vector3>();
    private readonly List<GameObject> foam = new List<GameObject>();
    private readonly List<GameObject> soapBlobs = new List<GameObject>();
    private static Material dropMat, soapMat, foamMat;

    void Awake()
    {
        Instance = this;
        board = GetComponent<CuttingBoardController>();
        col = GetComponent<Collider>();
        homeRot = transform.rotation;
        zOffset = transform.position.z - HandCarry.CounterZ(transform.position.y);
        Bounds home = Box; halfLen = home.extents.x * 0.92f; halfWid = home.extents.y * 0.55f;
        if (dishSoap == null) dishSoap = GameObject.Find("DishSoap")?.transform;
        if (sponge == null) sponge = GameObject.Find("Sponge")?.transform;
        if (dryCloth == null) dryCloth = GameObject.Find("CleaningCloth_Top")?.transform;
        if (transform.parent != null)
            foreach (CuttingBoardZoneBase z in transform.parent.GetComponentsInChildren<CuttingBoardZoneBase>(true))
            {
                zones.Add(z.gameObject);
                zoneOffsets.Add(z.transform.position - transform.position);
            }
    }

    void OnDisable()
    {
        if (state == State.Carrying) PutDown(pickedPos, pickedRot, pickedFrom);
    }

    // ---- picking up ----

    /// <summary>"씻기" pressed: picked up, if it can go. Otherwise why not.</summary>
    public bool TryPickUp(out string reason)
    {
        reason = null;
        if (washing) { reason = "씻는 중"; return false; }
        if (HandCarry.Instance != null && HandCarry.Instance.Current != null) { reason = "손을 먼저 비우세요"; return false; }
        if (board.HasIngredients) { reason = "재료를 먼저 치우세요"; return false; }
        if (board.HasTowelLiner) { reason = "키친타올을 먼저 치우세요"; return false; }
        foreach (FoodScrap s in FindObjectsByType<FoodScrap>(FindObjectsSortMode.None))
            if ((object)s.CurrentContainer == board) { reason = "자투리를 먼저 치우세요"; return false; }
        var falling = new List<ToolItem>();
        foreach (ToolItem t in FindObjectsByType<ToolItem>(FindObjectsSortMode.None))
        {
            if (t.GetType() != typeof(ToolItem) || t.Kind != ToolKind.Knife) continue;
            // The knife left lying on the board: lifted with it, it slides off
            // and falls to the floor - a safety record, nothing said.
            if ((object)t.CurrentContainer == board || t.transform.IsChildOf(board.Anchor)) { falling.Add(t); continue; }
            // (the knife not washed yet: the board can still be washed first -
            // nothing said, only recorded for the final evaluation)
            HygieneTracker h = t.GetComponent<HygieneTracker>();
            if (h != null && (h.NeedsWash || h.LastCategory != IngredientCategory.None))
                ExamDeductions.Add(ExamDeductions.Hygiene, "칼보다 도마를 먼저 씻음 (칼은 아직 씻지 않음)");
        }
        foreach (ToolItem t in falling) KnifeFall.Begin(t, transform, floorY, floorZ, maxKnifeDropRecords);
        pickedPos = transform.position;
        pickedRot = transform.rotation;
        pickedFrom = state;
        state = State.Carrying;
        transform.rotation = homeRot;
        grabOffset = transform.position - Box.center;
        foreach (GameObject z in zones) z.SetActive(false);
        waitRelease = Mouse.current != null && Mouse.current.leftButton.isPressed;
        return true;
    }

    /// <summary>The board clicked while it's away (in the sink): picked up again.</summary>
    public void OnBoardClicked()
    {
        if (state != State.InSink || washing) return;
        if (!TryPickUp(out string reason)) Toast(reason);
    }

    void Update()
    {
        if (state != State.Carrying) return;
        Mouse mouse = Mouse.current;
        Camera cam = Camera.main;
        if (mouse == null || cam == null) return;
        bool escape = Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame;
        if (escape || (!CleanupMode.IsOn && pickedFrom == State.Home)) { PutDown(pickedPos, pickedRot, pickedFrom); return; }
        if (waitRelease && !mouse.leftButton.isPressed) waitRelease = false;

        // just follows the mouse, lying as on the counter
        Vector2 sp = mouse.position.ReadValue();
        Vector3 cursor = cam.ScreenToWorldPoint(new Vector3(sp.x, sp.y, transform.position.z - cam.transform.position.z));
        float y = cursor.y + grabOffset.y;
        transform.SetPositionAndRotation(new Vector3(cursor.x + grabOffset.x, y, HandCarry.CounterZ(y) + zOffset), homeRot);
        HandController.Instance?.SlideTo(Grip(), 0.05f);

        if (!mouse.leftButton.wasPressedThisFrame || waitRelease) return;
        Ray ray = cam.ScreenPointToRay(sp);
        if (OverSink(cursor)) { PlaceInSink(); StartCoroutine(Wash()); }
        else if (OnCounter(ray)) PutDown(transform.position, homeRot, State.Home);
        else Toast("여기에 둘 수 없어요");
    }

    /// <summary>Where the board shows, right now: its renderer's box (a
    /// collider's bounds lag a frame behind a move - adding up corrections read
    /// from them sent the board flying off).</summary>
    private Bounds Box
    {
        get
        {
            if (rend == null) rend = GetComponent<Renderer>();
            return rend != null ? rend.bounds : col != null ? col.bounds : new Bounds(transform.position, Vector3.one);
        }
    }
    private Renderer rend;

    private Vector3 Grip()
    {
        Bounds b = Box;
        return new Vector3(b.max.x - 0.25f, b.center.y + 0.05f, b.min.z - 0.4f);
    }

    private void PutDown(Vector3 pos, Quaternion rot, State to)
    {
        transform.SetPositionAndRotation(pos, rot);
        state = to;
        if (to == State.Home)
            for (int i = 0; i < zones.Count; i++)
            {
                zones[i].transform.position = transform.position + zoneOffsets[i];
                zones[i].SetActive(true);
            }
    }

    private static bool OverSink(Vector3 cursor)
    {
        WashBasin sink = FindFirstObjectByType<WashBasin>();
        Collider c = sink != null ? sink.GetComponent<Collider>() : null;
        if (c == null) return false;
        Bounds b = c.bounds;
        return cursor.x > b.min.x && cursor.x < b.max.x && cursor.y > b.min.y && cursor.y < b.max.y;
    }

    private static bool OnCounter(Ray ray)
    {
        GameObject counter = GameObject.Find("Counter_R");
        WipeableSurface s = counter != null ? counter.GetComponent<WipeableSurface>() : null;
        return s == null || s.Raycast(ray, out _, out _);
    }

    // ---- in the sink ----

    private Vector3 BasinCenter()
    {
        GameObject back = GameObject.Find("Sink_WallB");
        if (back != null && back.GetComponent<Renderer>() is Renderer r) return r.bounds.center;
        WashBasin sink = FindFirstObjectByType<WashBasin>();
        return sink != null ? sink.transform.position : transform.position;
    }

    private void PlaceInSink()
    {
        state = State.InSink;
        Vector3 basin = BasinCenter();
        Quaternion rot = Quaternion.AngleAxis(sinkLean, Vector3.forward) * homeRot;
        transform.rotation = rot;
        Vector3 mid = Box.center;
        Vector3 target = new Vector3(basin.x + sinkOffset.x, basin.y + sinkOffset.y, 0f);
        transform.position += new Vector3(target.x - mid.x, target.y - mid.y, 0f);
        transform.position = new Vector3(transform.position.x, transform.position.y, HandCarry.CounterZ(transform.position.y) + zOffset + sinkDepthShift);
    }

    /// <summary>Its long side, on screen, pointing up the lean (the low end at -).</summary>
    private Vector3 Along()
    {
        Vector3 u = Vector3.ProjectOnPlane(transform.right, Vector3.forward).normalized;
        return u.x < 0f ? -u : u;
    }

    /// <summary>A point on its face as seen: s along it (-1 low end .. 1 high end), t across.</summary>
    private Vector3 Face(float s, float t)
    {
        Vector3 u = Along(), v = new Vector3(-u.y, u.x, 0f);
        Bounds box = Box;
        Vector3 c = box.center;
        float z = box.min.z - 0.05f;
        Vector3 p = c + u * (s * halfLen) + v * (t * halfWid);
        return new Vector3(p.x, p.y, z);
    }

    private IEnumerator Wash()
    {
        washing = true;
        HandCarry hand = HandCarry.Instance;
        if (hand != null) hand.IsLocked = true;
        HandController hc = HandController.Instance;
        WaterTap tap = FindFirstObjectByType<WaterTap>();
        bool wasOn = tap != null && tap.IsOn;
        Vector3 stream = tap != null ? tap.StreamPoint : Face(0f, 0f) + Vector3.up * 1.5f;
        // the hand holds its high end
        hc?.GoTo(Face(0.85f, 0.2f) + new Vector3(0f, 0f, -0.3f), null);

        tap?.SetRunning(true);
        yield return Rinse(stream, rinseSeconds);
        yield return Soap();
        yield return Scrub();
        yield return TurnOver(true);
        yield return Scrub();
        yield return TurnOver(false);
        ClearSoap();
        yield return Rinse(stream, rinseSeconds);
        if (!wasOn) tap?.SetRunning(false);
        yield return Dry();

        SupportHandController.Instance?.Hide();
        board.MarkWashedBothSides();
        Debug.Log("[도마 씻기] 물 → 세제 → 수세미 앞·뒤 → 헹굼 → 마른행주 완료");
        if (hand != null) hand.IsLocked = false;
        washing = false;
    }

    /// <summary>Water from the tap onto its face, running down the lean and off the low end.</summary>
    private IEnumerator Rinse(Vector3 stream, float seconds)
    {
        float next = 0f;
        for (float k = 0f; k < seconds; k += Time.deltaTime)
        {
            if (k >= next) { next = k + 0.05f; StartCoroutine(Drop(stream)); }
            // foam washed off as the water runs
            if (foam.Count > 0 && Random.value < 0.25f) { int i = Random.Range(0, foam.Count); if (foam[i] != null) Destroy(foam[i]); foam.RemoveAt(i); }
            SupportHandController.Instance?.ShowNear(Face(0f, 0.9f) + new Vector3(0f, 0.1f, -0.1f));
            yield return null;
        }
        foreach (GameObject f in foam) if (f != null) Destroy(f);
        foam.Clear();
    }

    private IEnumerator Drop(Vector3 stream)
    {
        if (dropMat == null) dropMat = Unlit(new Color(0.62f, 0.82f, 1f));
        GameObject d = Ball(dropMat, Random.Range(0.025f, 0.045f));
        Vector3 u = Along();
        float s = Mathf.Clamp((stream.x - Face(0f, 0f).x) / Mathf.Max(u.x * halfLen, 0.01f), -0.9f, 0.9f);
        float t = Random.Range(-0.4f, 0.4f);
        Vector3 land = Face(s, t);
        Vector3 start = new Vector3(land.x + Random.Range(-0.03f, 0.03f), stream.y, land.z);
        Vector3 low = Face(-1.02f, t);                     // down its slope to the low end...
        Vector3 gone = low + Vector3.down * 0.8f;          // ...and off
        Vector3[] path = { start, land, low, gone };
        float[] times = { 0.12f, 0.35f + 0.25f * (s + 1f), 0.25f };
        for (int seg = 0; seg < 3; seg++)
            for (float k = 0f; k < times[seg]; k += Time.deltaTime)
            {
                if (d == null) yield break;
                d.transform.position = Vector3.Lerp(path[seg], path[seg + 1], k / times[seg]);
                yield return null;
            }
        Destroy(d);
    }

    /// <summary>The dish soap taken over, tipped, and a few drops squeezed along the board.</summary>
    private IEnumerator Soap()
    {
        if (dishSoap == null) yield break;
        if (soapMat == null) soapMat = Unlit(new Color(0.55f, 0.85f, 0.35f));
        Vector3 p0 = dishSoap.position;
        Quaternion r0 = dishSoap.rotation;
        Vector3 over = Face(0f, 0f) + new Vector3(0f, 0.9f, -0.35f);
        Quaternion tipped = Quaternion.AngleAxis(150f, Vector3.forward) * r0;
        yield return Move(dishSoap, p0, over, r0, r0, 0.45f);
        yield return Move(dishSoap, over, over, r0, tipped, 0.3f);
        foreach (float s in new[] { -0.55f, 0f, 0.55f })
        {
            Vector3 at = Face(s, Random.Range(-0.2f, 0.2f));
            Vector3 from = over + new Vector3(s * halfLen * 0.6f, 0f, 0f);
            dishSoap.position = from;
            SupportHandController.Instance?.ShowNear(from + new Vector3(0f, 0.2f, -0.1f));
            GameObject blob = Ball(soapMat, 0.07f);
            for (float k = 0f; k < 0.25f; k += Time.deltaTime)
            {
                blob.transform.position = Vector3.Lerp(from, at, k / 0.25f);
                yield return null;
            }
            blob.transform.position = at;
            blob.transform.localScale = new Vector3(0.12f, 0.08f, 0.04f);
            soapBlobs.Add(blob);
            yield return new WaitForSeconds(0.12f);
        }
        yield return Move(dishSoap, dishSoap.position, over, tipped, r0, 0.25f);
        yield return Move(dishSoap, over, p0, r0, r0, 0.45f);
        dishSoap.SetPositionAndRotation(p0, r0);
    }

    /// <summary>The sponge back and forth along the board, foam where it goes.</summary>
    private IEnumerator Scrub()
    {
        if (foamMat == null) foamMat = Unlit(new Color(0.97f, 0.98f, 1f));
        Vector3 p0 = sponge != null ? sponge.position : Vector3.zero;
        Quaternion r0 = sponge != null ? sponge.rotation : Quaternion.identity;
        Quaternion onBoard = Quaternion.AngleAxis(sinkLean, Vector3.forward) * r0;
        if (sponge != null) yield return Move(sponge, p0, Face(-0.75f, 0f) + new Vector3(0f, 0f, -0.08f), r0, onBoard, 0.4f);
        float nextFoam = 0f;
        for (float k = 0f; k < scrubSeconds; k += Time.deltaTime)
        {
            // three times up and back, a little zigzag across
            float s = Mathf.Lerp(-0.75f, 0.75f, Mathf.PingPong(k / scrubSeconds * 6f, 1f));
            float t = Mathf.Sin(k * 9f) * 0.35f;
            Vector3 at = Face(s, t) + new Vector3(0f, 0f, -0.08f);
            if (sponge != null) sponge.SetPositionAndRotation(at, onBoard * Quaternion.AngleAxis(Mathf.Sin(k * 18f) * 6f, Vector3.forward));
            SupportHandController.Instance?.ShowNear(at + new Vector3(0f, 0.15f, -0.08f));
            if (k >= nextFoam)
            {
                nextFoam = k + 0.07f;
                GameObject f = Ball(foamMat, Random.Range(0.04f, 0.08f));
                f.transform.position = Face(s + Random.Range(-0.08f, 0.08f), t + Random.Range(-0.3f, 0.3f)) + new Vector3(0f, 0f, -0.02f);
                foam.Add(f);
            }
            for (int i = soapBlobs.Count - 1; i >= 0; i--)
            {
                GameObject b = soapBlobs[i];
                if (b == null) { soapBlobs.RemoveAt(i); continue; }
                if (((Vector2)(b.transform.position - at)).magnitude < 0.35f) b.transform.localScale *= 0.97f;
            }
            yield return null;
        }
        if (sponge != null) yield return Move(sponge, sponge.position, p0, sponge.rotation, r0, 0.4f);
        if (sponge != null) sponge.SetPositionAndRotation(p0, r0);
    }

    /// <summary>Turned over about its long side, to do the other face (and back after).</summary>
    private IEnumerator TurnOver(bool over)
    {
        foreach (GameObject f in foam) if (f != null) f.SetActive(!over);
        Quaternion r0 = transform.rotation;
        Vector3 axis = transform.right;
        Vector3 mid = Box.center, p0 = transform.position;
        for (float k = 0f; k <= 0.5f; k += Time.deltaTime)
        {
            // about its middle (worked out, not read back from bounds - those lag a frame)
            Quaternion d = Quaternion.AngleAxis(180f * Mathf.SmoothStep(0f, 1f, Mathf.Min(k / 0.5f, 1f)), axis);
            transform.SetPositionAndRotation(mid + d * (p0 - mid), d * r0);
            yield return null;
        }
        Quaternion end = Quaternion.AngleAxis(180f, axis);
        transform.SetPositionAndRotation(mid + end * (p0 - mid), end * r0);
    }

    private void ClearSoap()
    {
        foreach (GameObject b in soapBlobs) if (b != null) Destroy(b);
        soapBlobs.Clear();
    }

    /// <summary>Last, a dry cloth wiped back and forth over it.</summary>
    private IEnumerator Dry()
    {
        if (dryCloth == null) yield break;
        GameObject c = Instantiate(dryCloth.gameObject);
        foreach (Collider x in c.GetComponentsInChildren<Collider>(true)) Destroy(x);
        foreach (MonoBehaviour m in c.GetComponentsInChildren<MonoBehaviour>(true)) Destroy(m);
        foreach (Renderer r in c.GetComponentsInChildren<Renderer>(true)) r.enabled = true;
        c.transform.localScale = dryCloth.lossyScale;
        Vector3 p0 = dryCloth.position;
        Quaternion r0 = dryCloth.rotation, onBoard = Quaternion.AngleAxis(sinkLean, Vector3.forward) * r0;
        yield return Move(c.transform, p0, Face(-0.7f, 0f) + new Vector3(0f, 0f, -0.1f), r0, onBoard, 0.4f);
        for (float k = 0f; k < drySeconds; k += Time.deltaTime)
        {
            float s = Mathf.Lerp(-0.7f, 0.7f, Mathf.PingPong(k / drySeconds * 4f, 1f));
            Vector3 at = Face(s, Mathf.Sin(k * 6f) * 0.25f) + new Vector3(0f, 0f, -0.1f);
            c.transform.SetPositionAndRotation(at, onBoard);
            SupportHandController.Instance?.ShowNear(at + new Vector3(0f, 0.15f, -0.08f));
            yield return null;
        }
        yield return Move(c.transform, c.transform.position, p0, onBoard, r0, 0.4f);
        Destroy(c);
    }

    // ---- small helpers ----

    private static IEnumerator Move(Transform t, Vector3 a, Vector3 b, Quaternion ra, Quaternion rb, float seconds)
    {
        for (float k = 0f; k < seconds; k += Time.deltaTime)
        {
            float e = Mathf.SmoothStep(0f, 1f, k / seconds);
            t.SetPositionAndRotation(Vector3.Lerp(a, b, e), Quaternion.Slerp(ra, rb, e));
            yield return null;
        }
        t.SetPositionAndRotation(b, rb);
    }

    private static GameObject Ball(Material m, float size)
    {
        GameObject g = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        Destroy(g.GetComponent<Collider>());
        g.GetComponent<Renderer>().sharedMaterial = m;
        g.transform.localScale = Vector3.one * size;
        return g;
    }

    private static Material Unlit(Color c)
    {
        Shader sh = Shader.Find("Universal Render Pipeline/Unlit");
        var m = new Material(sh != null ? sh : Shader.Find("Standard")) { color = c };
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
        return m;
    }

    /// <summary>Why it didn't go - for developers only (the console): nothing
    /// is shown on screen, as at the real counter.</summary>
    private static void Toast(string text)
    {
        if (!string.IsNullOrEmpty(text)) Debug.Log("[도마] " + text);
    }

    // ---- the knife left on the board ----

    [Header("도마 위 칼이 떨어질 때")]
    [Tooltip("Where it comes to rest on the floor (world y), in front of the cabinet.")]
    [SerializeField] private float floorY = -6.0f;
    [Tooltip("Its depth there - in front of the counter's lip and the cabinet doors.")]
    [SerializeField] private float floorZ = 1.3f;

    [Tooltip("How many falls are recorded at most (each its own safety record).")]
    [SerializeField] private int maxKnifeDropRecords = 3;
}
