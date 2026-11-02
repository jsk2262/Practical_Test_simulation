using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// A burner's grate (화구 받침: Burner2_Prong0-3 + Burner2_Ring) as one piece,
/// for the clean-up (정리): taken off the burner so the floor under it can be
/// wiped (BurnerFloorDirt), and put back.
/// - The parts are set in the Inspector (never looked up by name while
///   running); their parent isn't changed - this moves all of them together,
///   each keeping its place, turn and size against the others. The burner
///   itself, its flame, the knob and the dirt on its floor don't move.
/// - Only in 정리, empty-handed: pointing at any part lights all of them;
///   a click takes the whole grate (not while the flame is on or a pot/pan
///   is on it). Carried, it follows the mouse: clicked by the storage spot
///   (asideSpot, marked meanwhile) it's set down there; clicked on its
///   burner it goes back on - exactly where and as it was (always from the
///   recorded pose, so on and off again never drifts). Esc: back to where
///   it was taken from.
/// - Off the burner: the burner can't be lit or have a pot/pan put on it.
/// </summary>
[DefaultExecutionOrder(-90)]
public class BurnerGrate : MonoBehaviour
{
    [Header("받침 부품 (Inspector에서 지정)")]
    [SerializeField] private Transform[] parts;
    [SerializeField] private StoveBurner burner;
    [Tooltip("Where it's set down while the floor is cleaned.")]
    [SerializeField] private Transform asideSpot;
    [SerializeField] private float dropRadius = 0.8f;
    [SerializeField] private Color highlight = new Color(0.95f, 0.8f, 0.35f);

    private static readonly List<BurnerGrate> all = new List<BurnerGrate>();
    private static BurnerGrate carried;
    /// <summary>A grate is being carried: the mouse is its own (ClickDetector stands aside).</summary>
    public static bool InputCaptured => carried != null;

    public enum Place { Installed, Carried, Aside }
    public Place Where { get; private set; } = Place.Installed;
    public bool IsInstalled => Where == Place.Installed;
    public StoveBurner Burner => burner;

    private Vector3[] homePos;
    private Quaternion[] homeRot;
    private Renderer[] rends;
    private Collider[] cols;
    private Vector3 homeCenter;
    private Vector3 offset;          // where the parts are now, from home
    private Place takenFrom;
    private bool waitRelease, lit;
    private GameObject marker;
    private MaterialPropertyBlock block;

    /// <summary>The grate's middle, in place on the burner (world).</summary>
    public Vector3 HomeCenter => homeCenter;

    public static BurnerGrate For(StoveBurner b)
    {
        foreach (BurnerGrate g in all) if (g.burner == b) return g;
        return null;
    }

    /// <summary>The burner's grate is off it (no lighting, no pot on it).</summary>
    public static bool IsDetached(StoveBurner b) => For(b) is BurnerGrate g && !g.IsInstalled;

    void Awake()
    {
        int n = parts != null ? parts.Length : 0;
        homePos = new Vector3[n];
        homeRot = new Quaternion[n];
        var r = new List<Renderer>();
        var c = new List<Collider>();
        Bounds b = default;
        bool any = false;
        for (int i = 0; i < n; i++)
        {
            homePos[i] = parts[i].position;
            homeRot[i] = parts[i].rotation;
            foreach (Renderer x in parts[i].GetComponentsInChildren<Renderer>())
            {
                r.Add(x);
                if (!any) { b = x.bounds; any = true; } else b.Encapsulate(x.bounds);
            }
            c.AddRange(parts[i].GetComponentsInChildren<Collider>());
        }
        rends = r.ToArray();
        cols = c.ToArray();
        homeCenter = any ? b.center : transform.position;
        block = new MaterialPropertyBlock();
    }

    void OnEnable() => all.Add(this);

    void OnDisable()
    {
        all.Remove(this);
        if (carried == this) { carried = null; MoveTo(takenFrom == Place.Aside ? AsideOffset() : Vector3.zero); Where = takenFrom; }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetForPlay() => carried = null;

    // ---- clicked (any part - BurnerGratePart) ----

    public void OnPartClicked()
    {
        if (Where == Place.Carried) return;
        if (!CleanupMode.IsOn) { CleanupMode.NotifyBlocked(); return; }
        if (HandCarry.Instance != null && HandCarry.Instance.Current != null) { Say("손을 먼저 비우세요"); return; }
        if (Where == Place.Installed)
        {
            if (burner != null && burner.IsOn) { Say("불을 먼저 끄세요"); return; }
            if (burner != null && burner.CurrentVessel != null) { Say("냄비·팬을 먼저 내리세요"); return; }
        }
        takenFrom = Where;
        Where = Place.Carried;
        carried = this;
        waitRelease = Mouse.current != null && Mouse.current.leftButton.isPressed;
        ShowMarker(true);
        Debug.Log($"[화구 받침] {name} 듦 ({(takenFrom == Place.Installed ? "화구에서" : "보관 위치에서")})");
    }

    void Update()
    {
        if (carried == this) Carry();
        else Hover();
    }

    /// <summary>Lit up - all of it - while the mouse is on any part (정리, empty hand).</summary>
    private void Hover()
    {
        bool on = false;
        Camera cam = Camera.main;
        if (CleanupMode.IsOn && cam != null && Mouse.current != null && !InputCaptured
            && (HandCarry.Instance == null || HandCarry.Instance.Current == null))
        {
            Ray ray = cam.ScreenPointToRay(Mouse.current.position.ReadValue());
            foreach (Collider c in cols)
                if (c != null && c.enabled && c.Raycast(ray, out _, 200f)) { on = true; break; }
        }
        SetLit(on);
    }

    private void SetLit(bool on)
    {
        if (on == lit) return;
        lit = on;
        foreach (Renderer r in rends)
        {
            if (r == null) continue;
            if (on)
            {
                block.Clear();
                block.SetColor("_BaseColor", highlight);
                block.SetColor("_Color", highlight);
                r.SetPropertyBlock(block);
            }
            else r.SetPropertyBlock(null);
        }
    }

    private void Carry()
    {
        SetLit(true);
        Mouse m = Mouse.current;
        Camera cam = Camera.main;
        if (m == null || cam == null) return;
        if ((Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame) || !CleanupMode.IsOn)
        {
            PutDown(takenFrom);
            return;
        }
        if (waitRelease && !m.leftButton.isPressed) waitRelease = false;

        // follows the mouse, lifted a little off the counter
        Vector2 sp = m.position.ReadValue();
        float depth = Vector3.Dot(homeCenter - cam.transform.position, cam.transform.forward); // 제자리 깊이를 카메라 축 기준으로 잰다
        Vector3 cursor = cam.ScreenToWorldPoint(new Vector3(sp.x, sp.y, depth)); // 커서가 가리키는 실제 월드 지점
        Vector3 at = cursor - cam.transform.forward * 0.25f; // 조리대에서 카메라 쪽으로 살짝 띄워 든다
        MoveTo(at - homeCenter);
        HandController.Instance?.SlideTo(at + new Vector3(0.5f, 0.25f, -0.6f), 0.04f);

        if (!m.leftButton.wasPressedThisFrame || waitRelease) return;
        if (Near(cursor, homeCenter)) PutDown(Place.Installed);
        else if (asideSpot != null && Near(cursor, asideSpot.position)) PutDown(Place.Aside);
        else Say("보관 위치(표시된 곳)나 화구 위에 놓으세요");
    }

    private bool Near(Vector3 a, Vector3 b) => ((Vector2)(a - b)).magnitude <= dropRadius;

    private void PutDown(Place to)
    {
        MoveTo(to == Place.Aside ? AsideOffset() : Vector3.zero);
        Where = to;
        carried = null;
        ShowMarker(false);
        SetLit(false);
        Debug.Log($"[화구 받침] {name} {(to == Place.Installed ? "화구에 장착 (원위치)" : "보관 위치에 놓음")}");
    }

    private Vector3 AsideOffset()
    {
        if (asideSpot == null) return Vector3.zero;
        return asideSpot.position - homeCenter; // 보관 지점의 실제 좌표까지의 이동량
    }

    /// <summary>Every part to its recorded place plus the offset (never from where it is now).</summary>
    private void MoveTo(Vector3 by)
    {
        offset = by;
        for (int i = 0; i < parts.Length; i++)
            if (parts[i] != null) parts[i].SetPositionAndRotation(homePos[i] + by, homeRot[i]);
    }

    private void ShowMarker(bool show)
    {
        if (asideSpot == null) return;
        if (marker == null)
        {
            marker = GameObject.CreatePrimitive(PrimitiveType.Quad);
            marker.name = "GrateAsideMarker";
            Destroy(marker.GetComponent<Collider>());
            Shader sh = Shader.Find("Sprites/Default");
            marker.GetComponent<Renderer>().sharedMaterial = new Material(sh) { color = new Color(1f, 0.85f, 0.3f, 0.35f) };
            marker.transform.localScale = Vector3.one * dropRadius * 1.6f;
            var label = new GameObject("Label");
            label.transform.SetParent(marker.transform, false);
            label.transform.localPosition = new Vector3(0f, 0f, -0.01f);
            label.transform.localScale = Vector3.one / (dropRadius * 1.6f);
            TextMesh tm = label.AddComponent<TextMesh>();
            KoreanFontFallback.Apply(tm); // 웹에서도 한글이 보이게 한글 글꼴
            tm.text = "받침 보관";
            tm.fontSize = 36;
            tm.characterSize = 0.07f;
            tm.anchor = TextAnchor.MiddleCenter;
            tm.color = new Color(0.25f, 0.2f, 0.1f);
        }
        marker.transform.position = asideSpot.position + Carryable.CounterUp * 0.02f; // 보관 지점 바로 위에 표시가 뜨게 한다
        marker.SetActive(show);
    }

    public void Say(string text)
    {
        Debug.Log("[화구 받침] " + text);
        var go = new GameObject("GrateNote");
        go.transform.position = homeCenter + offset + new Vector3(0f, 1.0f, -1.5f);
        TextMesh tm = go.AddComponent<TextMesh>();
        KoreanFontFallback.Apply(tm); // 웹에서도 한글이 보이게 한글 글꼴
        tm.text = text;
        tm.fontSize = 40;
        tm.characterSize = 0.08f;
        tm.anchor = TextAnchor.MiddleCenter;
        tm.color = new Color(0.15f, 0.15f, 0.15f);
        Destroy(go, 1.5f);
    }
}
