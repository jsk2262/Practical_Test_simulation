using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 행주로 닦기 (정리 mode). The cloth set in the Inspector (targetCloth - the top
/// one of the stack in CleaningCloth) clicked with an empty hand while 정리 is
/// on: the folded cloth is hidden and a spread one (spreadClothModel, or a
/// thin soft-cornered one made from the folded cloth's material) takes over.
/// - That click doesn't wipe: the button has to be let go and pressed again.
/// - Pointing at a WipeableSurface, the cloth lies on it (turned to its slope,
///   just above it); held down and moved, it wipes - only where it goes, and
///   only while it moves (pressed but still: nothing). Let go: it stops, the
///   cloth stays in hand.
/// - Not through what stands on the surface (bowls, the board, tools, food):
///   there it doesn't wipe, and a press on any such thing puts the cloth away
///   first, then that press goes on as usual (picking it up etc.).
/// - Esc, 정리 switched off, or this switched off: put away - the spread cloth
///   hidden, the folded one back where it was, the input given back.
/// While out, ClickDetector leaves the mouse to it (InputCaptured).
/// </summary>
[DefaultExecutionOrder(-100)] // (before ClickDetector: a press on something else is let through the same frame)
public class ClothWiper : MonoBehaviour, IClickable
{
    [Header("대상 (Inspector에서 지정)")]
    [Tooltip("The folded cloth that's picked up (the top one of the three).")]
    [SerializeField] private Transform targetCloth;
    [Tooltip("The cloth spread out, if there's a model for it - otherwise one is made.")]
    [SerializeField] private GameObject spreadClothModel;

    [Header("펼친 행주 (자동 생성 시)")]
    [SerializeField] private Vector2 spreadSize = new Vector2(0.62f, 0.46f);
    [SerializeField] private float spreadThickness = 0.016f;
    [SerializeField] private float cornerRadius = 0.07f;

    [Header("닦기")]
    [Tooltip("Gap kept between the cloth and the surface.")]
    [SerializeField] private float hoverGap = 0.006f;
    [Tooltip("How much of a stain one pass right over it takes off (0-1).")]
    [SerializeField] private float rubPerPass = 0.25f;
    [Tooltip("Smallest move (world units) that counts as rubbing.")]
    [SerializeField] private float minMove = 0.002f;
    [SerializeField] private Vector3 handOffset = new Vector3(0.05f, 0.04f, -0.4f);

    private static ClothWiper active;
    /// <summary>The cloth is out: the mouse is its own (ClickDetector stands aside).</summary>
    public static bool InputCaptured => active != null;
    public static ClothWiper Active => active;

    private Camera cam;
    private Vector3 homeLocalPos;
    private Quaternion homeLocalRot;
    private Renderer[] foldedRenderers;
    private Collider[] foldedColliders;
    private Transform spread;
    private bool spreadMade;

    private bool waitRelease;   // the selecting click's button not let go yet
    private bool stroking;      // held down (a press that began on a surface)
    private WipeableSurface strokeSurface;
    private Vector3 lastWipe;
    private WipeableSurface poseSurface;
    private Vector3 posePoint;
    private bool hasPose;
    private Vector3 moveDir;
    private float press;

    public bool IsStroking => stroking;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetForPlay() => active = null;

    void Awake()
    {
        cam = Camera.main;
        if (targetCloth != null)
        {
            homeLocalPos = targetCloth.localPosition;
            homeLocalRot = targetCloth.localRotation;
            foldedRenderers = targetCloth.GetComponentsInChildren<Renderer>(true);
            foldedColliders = targetCloth.GetComponentsInChildren<Collider>(true);
        }
    }

    void OnEnable() => CleanupMode.Changed += OnCleanup;

    void OnDisable()
    {
        CleanupMode.Changed -= OnCleanup;
        if (active == this) End();
    }

    private void OnCleanup(bool on)
    {
        if (!on && active == this) End();
    }

    /// <summary>The folded cloth clicked (ClickDetector, on the release).</summary>
    public void OnClicked()
    {
        if (active == this || targetCloth == null) return;
        if (!CleanupMode.IsOn) { CleanupMode.NotifyBlocked(); return; }
        if (HandCarry.Instance != null && HandCarry.Instance.Current != null)
        {
            Debug.Log("[행주] 손에 든 것을 먼저 내려놓으세요");
            return;
        }
        Begin();
    }

    private void Begin()
    {
        if (active != null && active != this) active.End();
        active = this;
        if (spread == null) spread = MakeSpread();
        foreach (Renderer r in foldedRenderers) r.enabled = false;
        foreach (Collider c in foldedColliders) c.enabled = false; // (never in the way of the surface under the mouse)
        spread.SetPositionAndRotation(targetCloth.position + Carryable.CounterUp * 0.08f, Quaternion.LookRotation(Vector3.forward, Carryable.CounterUp));
        spread.gameObject.SetActive(true);
        waitRelease = true;
        stroking = false;
        hasPose = false;
        press = 0f;
        Debug.Log("[행주] 펼침 - 버튼을 놓았다가 다시 눌러 문지르세요 (Esc: 내려놓기)");
    }

    /// <summary>Put away: spread cloth hidden, the folded one back at its spot,
    /// the mouse given back. Safe to call any time.</summary>
    public void End()
    {
        if (active == this) active = null;
        stroking = false;
        waitRelease = false;
        if (spread != null) spread.gameObject.SetActive(false);
        if (targetCloth != null)
        {
            targetCloth.localPosition = homeLocalPos;
            targetCloth.localRotation = homeLocalRot;
        }
        if (foldedRenderers != null) foreach (Renderer r in foldedRenderers) if (r != null) r.enabled = true;
        if (foldedColliders != null) foreach (Collider c in foldedColliders) if (c != null) c.enabled = true;
        Debug.Log("[행주] 제자리에 접어 둠");
    }

    void Update()
    {
        if (active != this) return;
        Mouse mouse = Mouse.current;
        if (cam == null) cam = Camera.main;
        if (!CleanupMode.IsOn || mouse == null || cam == null) { End(); return; }
        if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame) { End(); return; }
        if (HandCarry.Instance != null && HandCarry.Instance.Current != null) { End(); return; } // (something got into the hand)

        bool held = mouse.leftButton.isPressed;
        if (waitRelease && !held) waitRelease = false;

        Ray ray = cam.ScreenPointToRay(mouse.position.ReadValue());
        bool overUI = UnityEngine.EventSystems.EventSystem.current != null
                      && UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject();
        WipeableSurface surface = null;
        Vector3 point = default;
        Collider inFront = null;
        if (!overUI) surface = FindSurface(ray, out point, out inFront);

        if (mouse.leftButton.wasPressedThisFrame && !waitRelease && !overUI)
        {
            if (inFront != null && Selectable(inFront))
            {
                // Another thing chosen: the cloth put away first; ClickDetector
                // (later this frame) takes the press as it would have.
                End();
                return;
            }
            if (surface != null)
            {
                stroking = true;
                strokeSurface = surface;
                lastWipe = point;
            }
        }
        if (!held || overUI) stroking = false;

        if (surface != null)
        {
            if (stroking)
            {
                if (surface != strokeSurface) { strokeSurface = surface; lastWipe = point; } // onto the next counter: carries on from there
                Vector3 step = point - lastWipe;
                if (step.magnitude >= minMove)
                {
                    WipeAlong(surface, lastWipe, point);
                    moveDir = Vector3.Lerp(moveDir, step.normalized, 0.5f);
                    lastWipe = point;
                    press = 1f;
                }
            }
            poseSurface = surface;
            posePoint = point;
            hasPose = true;
        }
        else
        {
            strokeSurface = null; // off it / behind something: no wiping - and back on, it starts afresh there (no jump across)
            // Still follows the mouse (held a little up, on the last surface's
            // level) - it stayed stuck at the last spot and seemed to circle
            // round where it couldn't go.
            if (hasPose && poseSurface != null && new Plane(poseSurface.Normal, posePoint).Raycast(ray, out float d))
                posePoint = ray.GetPoint(d);
        }

        // Pressed down while held: weighted, flat to the surface.
        press = Mathf.MoveTowards(press, stroking ? 1f : 0f, Time.deltaTime * 8f);
        PoseCloth(surface != null);
    }

    private Vector3 followVelocity;

    /// <summary>The path's wiping, cut into short pieces so a fast stroke
    /// covers everything in between.</summary>
    private void WipeAlong(WipeableSurface surface, Vector3 from, Vector3 to)
    {
        float radius = WipeRadius;
        float len = Vector3.Distance(from, to);
        int n = Mathf.Max(1, Mathf.CeilToInt(len / (radius * 0.25f)));
        for (int i = 0; i < n; i++)
        {
            Vector3 a = Vector3.Lerp(from, to, (float)i / n), b = Vector3.Lerp(from, to, (float)(i + 1) / n);
            // over a whole pass (2 radii of travel past a spot) about rubPerPass comes off it
            surface.Wipe(a, b, radius, rubPerPass * 0.5f * (len / n) / radius);
        }
    }

    private float WipeRadius => Mathf.Min(spreadSize.x, spreadSize.y) * 0.45f;

    private void PoseCloth(bool onSurface)
    {
        if (spread == null) return;
        Vector3 pos;
        Quaternion rot;
        if (hasPose && poseSurface != null)
        {
            Vector3 n = poseSurface.Normal;
            // pressed: right down on it (no gap); lifted a little where it can't go
            float lift = spreadThickness * 0.5f * (1f - press * 0.55f) + hoverGap * (1f - press) + (onSurface ? 0f : 0.06f);
            // Held down it doesn't swing about - no wobble, no turning with the
            // stroke: just its leading edge pressed a touch harder into the
            // surface in the direction it's pushed (the hand leaning on it).
            Vector3 along = Vector3.ProjectOnPlane(moveDir, n);
            Quaternion lean = stroking && along.sqrMagnitude > 0.01f
                ? Quaternion.AngleAxis(4f * press, Vector3.Cross(n, along.normalized))
                : Quaternion.identity;
            pos = posePoint + n * lift;
            rot = lean * Quaternion.LookRotation(poseSurface.Forward, n);
        }
        else
        {
            pos = spread.position;
            rot = spread.rotation;
        }
        // Weighted: pushed along steadily (a little behind the hand, never
        // springing past it) while pressed; quicker when just carried over.
        spread.position = Vector3.SmoothDamp(spread.position, pos, ref followVelocity, stroking ? 0.07f : 0.035f);
        spread.rotation = Quaternion.Slerp(spread.rotation, rot, 1f - Mathf.Exp(-(stroking ? 10f : 20f) * Time.deltaTime));
        // pressed down while rubbing: flattened and spread out under the hand
        spread.localScale = new Vector3(1f + press * 0.08f, 1f - press * 0.55f, 1f + press * 0.06f);
        // the hand on top of it, lower while it presses
        HandController.Instance?.SlideTo(spread.position + handOffset + Vector3.down * (0.05f * press), 0.03f);
    }

    /// <summary>The nearest WipeableSurface under the ray - unless something
    /// stands on it there (then null, and inFront is that thing). inFront is
    /// also what's hit when there's no surface at all.</summary>
    private static WipeableSurface FindSurface(Ray ray, out Vector3 point, out Collider inFront)
    {
        WipeableSurface best = null;
        float bestDist = float.MaxValue;
        point = default;
        foreach (WipeableSurface s in WipeableSurface.All)
            if (s.Raycast(ray, out Vector3 p, out float d) && d < bestDist) { best = s; bestDist = d; point = p; }
        inFront = Blocker(ray, best != null ? bestDist : float.MaxValue);
        if (inFront != null) return null;
        return best;
    }

    /// <summary>What stands over this point as the camera sees it, if anything.</summary>
    public static Collider Blocker(Camera cam, Vector3 point)
    {
        Ray ray = cam.ScreenPointToRay(cam.WorldToScreenPoint(point));
        return Blocker(ray, Vector3.Dot(point - ray.origin, ray.direction));
    }

    /// <summary>The nearest collider before the surface (a bit past it, too: a
    /// bowl's click box sits at its back) - the hand cursor, the cloth and the
    /// sink's own catch-all click plane don't count.</summary>
    private static Collider Blocker(Ray ray, float surfaceDist)
    {
        const float pastSurface = 0.6f;
        Collider nearest = null;
        float nd = float.MaxValue;
        int mask = ~(1 << LayerMask.NameToLayer("Ignore Raycast"));
        foreach (RaycastHit h in Physics.RaycastAll(ray, 200f, mask, QueryTriggerInteraction.Ignore))
        {
            if (h.distance > surfaceDist + pastSurface || h.distance >= nd) continue;
            if (h.collider.GetComponentInParent<WashBasin>() != null && h.collider.GetComponentInParent<Carryable>() == null) continue;
            // (a burner's click box is just that - what covers its floor is its grate, BurnerGratePart)
            if (h.collider.GetComponent<StoveBurner>() != null) continue;
            if (active != null && active.targetCloth != null && h.collider.transform.IsChildOf(active.targetCloth)) continue;
            nearest = h.collider;
            nd = h.distance;
        }
        return nearest;
    }

    private static bool Selectable(Collider c) =>
        c.GetComponentInParent<IClickable>() != null || c.GetComponentInParent<IDraggable>() != null || c.GetComponentInParent<Carryable>() != null;

    /// <summary>The spread cloth: the given model, or a thin soft-cornered pad in
    /// the folded cloth's own material (its colour from the folded mesh's UVs).</summary>
    private Transform MakeSpread()
    {
        if (spreadClothModel != null)
        {
            GameObject go = spreadClothModel.scene.IsValid() ? spreadClothModel : Instantiate(spreadClothModel);
            foreach (Collider c in go.GetComponentsInChildren<Collider>(true)) c.enabled = false;
            return go.transform;
        }
        spreadMade = true;
        var spreadGo = new GameObject("ClothSpread");
        Renderer src = foldedRenderers != null && foldedRenderers.Length > 0 ? foldedRenderers[0] : null;
        Vector2 uv = new Vector2(0.5f, 0.5f);
        if (src != null && src.GetComponent<MeshFilter>() is MeshFilter mf && mf.sharedMesh != null && mf.sharedMesh.isReadable && mf.sharedMesh.uv.Length > 0)
        {
            Vector2 sum = Vector2.zero;
            foreach (Vector2 u in mf.sharedMesh.uv) sum += u;
            uv = sum / mf.sharedMesh.uv.Length;
        }
        spreadGo.AddComponent<MeshFilter>().sharedMesh = PadMesh(spreadSize.x, spreadSize.y, spreadThickness, cornerRadius, uv);
        var mr = spreadGo.AddComponent<MeshRenderer>();
        if (src != null) mr.sharedMaterials = src.sharedMaterials;
        spreadGo.SetActive(false);
        return spreadGo.transform;
    }

    /// <summary>A rounded-corner pad on x-z, t thick, its top a little domed.</summary>
    internal static Mesh PadMesh(float w, float d, float t, float r, Vector2 uv)
    {
        const int seg = 6;
        r = Mathf.Min(r, Mathf.Min(w, d) * 0.5f);
        var ring = new System.Collections.Generic.List<Vector2>();
        Vector2[] corners = { new Vector2(w / 2 - r, d / 2 - r), new Vector2(-w / 2 + r, d / 2 - r), new Vector2(-w / 2 + r, -d / 2 + r), new Vector2(w / 2 - r, -d / 2 + r) };
        for (int c = 0; c < 4; c++)
            for (int i = 0; i <= seg; i++)
            {
                float a = (c * 90f + 90f * i / seg) * Mathf.Deg2Rad;
                ring.Add(corners[c] + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r);
            }
        int n = ring.Count;
        var v = new System.Collections.Generic.List<Vector3>();
        var tris = new System.Collections.Generic.List<int>();
        // top (centre + ring, domed) and bottom
        v.Add(new Vector3(0f, t * 0.5f + t * 0.35f, 0f));
        foreach (Vector2 p in ring) v.Add(new Vector3(p.x, t * 0.5f, p.y));
        v.Add(new Vector3(0f, -t * 0.5f, 0f));
        foreach (Vector2 p in ring) v.Add(new Vector3(p.x, -t * 0.5f, p.y));
        int bc = n + 1;
        for (int i = 0; i < n; i++)
        {
            int a = 1 + i, b = 1 + (i + 1) % n;
            tris.AddRange(new[] { 0, b, a });
            tris.AddRange(new[] { bc, bc + 1 + i, bc + 1 + (i + 1) % n });
            // side
            int ta = a, tb = b, ba = bc + 1 + i, bb = bc + 1 + (i + 1) % n;
            tris.AddRange(new[] { ta, tb, ba, tb, bb, ba });
        }
        var mesh = new Mesh { name = "ClothSpreadPad" };
        mesh.SetVertices(v);
        mesh.SetTriangles(tris, 0);
        var uvs = new Vector2[v.Count];
        for (int i = 0; i < uvs.Length; i++) uvs[i] = uv;
        mesh.uv = uvs;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    void OnDestroy()
    {
        if (active == this) active = null;
        if (spreadMade && spread != null) Destroy(spread.gameObject);
    }
}
