using System;
using System.Collections;
using UnityEngine;

/// <summary>
/// 깬 달걀 in a bowl (or pan): its whites and yolks, counted in eggs. Broken
/// in by EggCracker (the egg in hand, held down on the bowl). Clicked with an
/// empty hand, the bowl offers 계란 사용 - 흰자만 / 노른자만 / 전체(전란) - and is
/// then picked up with that part chosen; nothing moves until it's poured
/// into another container (MovableBowl.PourEggFrom), the rest staying here.
/// Beaten (풀기 - chopsticks, whisk, spoon or spatula) it's 계란물, and from
/// then on - or once mixed with anything else in the bowl - it's poured all
/// together, no choosing.
/// </summary>
public class EggContents : MonoBehaviour
{
    public enum Part { All, White, Yolk }

    public float Whites { get; private set; }
    public float Yolks { get; private set; }
    public bool Beaten { get; private set; }
    /// <summary>The part picked in 계란 사용, while the bowl is in hand.</summary>
    public Part? Chosen { get; set; }
    public bool Busy { get; private set; }

    private MovableBowl bowl;
    private bool mixed;
    private Transform look;

    public bool HasAny => Whites > 0.005f || Yolks > 0.005f; // (less than a fifth of a mL: none)
    public bool HasWhite => Whites > 0.005f;
    public bool HasYolk => Yolks > 0.005f;

    /// <summary>Anything else in the bowl with it (pieces, seasonings, sauce, water)?</summary>
    public bool Mixed => mixed || bowl == null || bowl.HasIngredients || bowl.AppliedSeasonings.Count > 0
                         || bowl.WaterAmount > 0f || bowl.HasPortion || PanSauce.Of(bowl, false) != null;

    /// <summary>White and yolk can still be poured apart.</summary>
    public bool IsSeparable => !Beaten && !Mixed && HasWhite && HasYolk;

    public string Label => Beaten ? (HasWhite && HasYolk ? "계란물" : HasYolk ? "푼 노른자" : "푼 흰자")
                         : HasWhite && HasYolk ? "전란" : HasYolk ? "노른자" : "흰자";

    public static EggContents Of(MovableBowl bowl, bool create)
    {
        if (bowl == null) return null;
        EggContents e = bowl.GetComponent<EggContents>();
        if (e == null && create)
        {
            e = bowl.gameObject.AddComponent<EggContents>();
            e.bowl = bowl;
        }
        return e;
    }

    void Awake() { if (bowl == null) bowl = GetComponent<MovableBowl>(); }

    /// <summary>One whole egg broken in.</summary>
    public void AddEgg()
    {
        Whites += 1f;
        Yolks += 1f;
        Rebuild();
    }

    /// <summary>Takes the chosen part out (to pour elsewhere).</summary>
    public void Take(Part part, out float whites, out float yolks, out bool beaten)
    {
        beaten = Beaten;
        // Beaten or mixed in, it's all one: it goes all together.
        if (Beaten || Mixed) part = Part.All;
        whites = part == Part.Yolk ? 0f : Whites;
        yolks = part == Part.White ? 0f : Yolks;
        Whites -= whites;
        Yolks -= yolks;
        if (!HasAny) Clear();
        else Rebuild();
    }

    /// <summary>An egg's white is about 30 mL (2 큰술), its yolk about 15 mL (1 큰술).</summary>
    public const float WhiteMlPerEgg = 30f, YolkMlPerEgg = 15f;

    /// <summary>Beaten or mixed with something: it's all one, whichever part is asked for.</summary>
    public Part Effective(Part part) => Beaten || Mixed ? Part.All : part;

    /// <summary>mL of that part there is to measure out.</summary>
    public float AvailableMl(Part part)
    {
        part = Effective(part);
        return part == Part.White ? WhiteMl : part == Part.Yolk ? YolkMl : WhiteMl + YolkMl;
    }

    /// <summary>What a spoonful of that part is called ("흰자", "노른자",
    /// "전란", "계란물") - recorded as what it is, never turned into another part.</summary>
    public string SpoonName(Part part)
    {
        part = Effective(part);
        if (part == Part.White) return "흰자";
        if (part == Part.Yolk) return "노른자";
        return Beaten || Mixed ? (HasWhite && HasYolk ? "계란물" : Label) : Label;
    }

    /// <summary>A measured spoonful (ml) of that part taken out - the caller has
    /// checked there's that much (AvailableMl). The whole: white and yolk in
    /// the proportion they're in.</summary>
    public void TakeMl(Part part, float ml)
    {
        part = Effective(part);
        float w = 0f, y = 0f;
        if (part == Part.White) w = Mathf.Min(Whites, ml / WhiteMlPerEgg);
        else if (part == Part.Yolk) y = Mathf.Min(Yolks, ml / YolkMlPerEgg);
        else
        {
            float total = WhiteMl + YolkMl;
            if (total <= 0f) return;
            float f = Mathf.Min(1f, ml / total);
            w = Whites * f;
            y = Yolks * f;
        }
        Whites -= w;
        Yolks -= y;
        if (!HasAny) Clear();
        else Rebuild();
    }

    /// <summary>mL of each part still in the bowl.</summary>
    public float WhiteMl => Whites * WhiteMlPerEgg;
    public float YolkMl => Yolks * YolkMlPerEgg;

    /// <summary>What was poured in from another bowl.</summary>
    public void Put(float whites, float yolks, bool beaten)
    {
        if (whites <= 0f && yolks <= 0f) return;
        if (HasAny && (beaten != Beaten)) mixed = true; // (whole yolks into 계란물: one mix)
        if (Mixed) mixed = true;
        Whites += whites;
        Yolks += yolks;
        Beaten = Beaten || beaten;
        Rebuild();
    }

    /// <summary>Thrown out or washed away.</summary>
    public void Clear()
    {
        Whites = Yolks = 0f;
        if (look != null) Destroy(look.gameObject);
        Destroy(this);
    }

    void OnDestroy() { if (look != null) Destroy(look.gameObject); }

    void Update()
    {
        // The choice lasts while the bowl is in hand.
        if (Chosen.HasValue && !bowl.IsHeld) Chosen = null;
    }

    void LateUpdate()
    {
        if (look == null || bowl == null) return;
        MovableBowl.Interior inside = bowl.GetInterior();
        look.SetPositionAndRotation(inside.floorCenter + inside.up * inside.rim * 0.12f,
            Quaternion.FromToRotation(Vector3.up, inside.up));
    }

    // ---- 풀기 ----

    /// <summary>Beaten with the tool in hand: whites and yolks into one 계란물.</summary>
    public bool TryBeat(ToolItem tool)
    {
        if (Busy || Beaten || !HasAny || tool == null) return false;
        StartCoroutine(BeatRoutine(tool));
        return true;
    }

    private IEnumerator BeatRoutine(ToolItem tool)
    {
        Busy = true;
        HandCarry hand = HandCarry.Instance;
        if (hand != null) hand.IsLocked = true;
        HandController right = HandController.Instance;
        Vector3 c = bowl.BodyCenterWorld();
        right?.GoTo(c + new Vector3(0.3f, 0.45f, -0.4f), null);
        yield return new WaitForSeconds(0.25f);
        const float seconds = 1.4f;
        Vector3 reach = Vector3.zero;
        for (float t = 0f; t < seconds; t += Time.deltaTime)
        {
            float a = t / seconds * Mathf.PI * 2f * 5f; // quick small circles
            Vector3 circle = new Vector3(Mathf.Cos(a), Mathf.Sin(a) * 0.5f, 0f) * 0.07f;
            // (the spoon's bowl - not the hand - kept in the middle; other tools just circle)
            if (tool.Kind == ToolKind.MeasuringSpoon) reach += ToolItem.TowardSpoonHead(tool, c + circle, Time.deltaTime);
            else reach = circle;
            right?.SetGestureOffset(reach);
            yield return null;
        }
        right?.SetGestureOffset(Vector3.zero);
        Beaten = true;
        Rebuild();
        if (hand != null) hand.IsLocked = false;
        Busy = false;
        Debug.Log($"[달걀] {bowl.name}: 풀어서 {Label} ({Whites:0.#}개분)");
    }

    // ---- look ----

    private void Rebuild()
    {
        if (look != null) Destroy(look.gameObject);
        look = null;
        if (!HasAny) return;
        MovableBowl.Interior inside = bowl.GetInterior();
        float R = Mathf.Max(inside.radius, 0.05f);
        look = new GameObject("EggContents_" + bowl.name).transform;

        float cm = 0.0978f; // (world units per cm, as the board)
        float eggs = Mathf.Max(Whites, Yolks);
        if (Beaten || Mixed)
        {
            // one pool: yellow as there's yolk in it
            float yolkShare = Yolks / Mathf.Max(0.01f, Whites + Yolks);
            Color c = Color.Lerp(new Color(0.95f, 0.93f, 0.8f), new Color(1f, 0.72f, 0.15f), Mathf.Clamp01(yolkShare * 1.6f));
            float r = Mathf.Min(R * 0.85f, cm * 4f * Mathf.Sqrt(Mathf.Max(1f, eggs)));
            Condiments.BuildContent(look, Vector3.zero, Vector3.up, r, CondimentForm.Liquid, c, Beaten ? "EggBeaten" : "EggMixed");
            return;
        }
        // Whole: each egg's white spread a little, its yolk standing up in it.
        int whites = Mathf.CeilToInt(Whites - 0.01f), yolks = Mathf.CeilToInt(Yolks - 0.01f);
        int n = Mathf.Max(whites, yolks);
        float spread = n > 1 ? Mathf.Min(R * 0.4f, cm * 2.5f) : 0f;
        float wr = Mathf.Min(R * (n > 1 ? 0.55f : 0.8f), cm * 3.8f);
        float yr = Mathf.Min(R * 0.28f, cm * 1.5f);
        for (int i = 0; i < n; i++)
        {
            float a = i * Mathf.PI * 2f / Mathf.Max(1, n) + 0.4f;
            Vector3 at = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * spread;
            // (an egg's worth partly spooned out: that one smaller)
            float wPart = Mathf.Clamp01(Whites - i), yPart = Mathf.Clamp01(Yolks - i);
            if (i < whites)
                Condiments.BuildContent(look, at, Vector3.up, wr * Mathf.Max(0.35f, Mathf.Sqrt(wPart)), CondimentForm.Liquid, new Color(0.9f, 0.89f, 0.74f), "White"); // (a little yellowish-clear: pure white was lost on a white bowl)
            if (i < yolks)
            {
                float k = Mathf.Max(0.45f, Mathf.Pow(yPart, 1f / 3f));
                Transform yolk = Blob(look, "Yolk", new Color(1f, 0.66f, 0.1f));
                yolk.localPosition = at + Vector3.up * yr * 0.35f * k;
                yolk.localScale = new Vector3(yr * 2f, yr * 1.2f, yr * 2f) * k;
            }
        }
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
        if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.85f);
        Renderer r = g.GetComponent<Renderer>();
        r.sharedMaterial = m;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        return g.transform;
    }

    // ---- 계란 사용 (the choice) ----

    /// <summary>Shows 흰자만 / 노른자만 / 전체(전란) round the bowl. onPick (the measuring
    /// spoon: one spoonful of that part) - or, by default, the bowl picked up
    /// with that part chosen to pour.</summary>
    public void ShowChooser(Action<Part> onPick = null, string title = "계란 사용")
    {
        // round the bowl as it shows on screen (its visible body), in front of everything
        Bounds seen = new Bounds(bowl.BodyCenterWorld(), Vector3.zero);
        foreach (Renderer r in bowl.GetComponentsInChildren<Renderer>())
            if (r.enabled && (look == null || !r.transform.IsChildOf(look))) seen.Encapsulate(r.bounds);
        Vector3 at = new Vector3(seen.center.x, seen.center.y, seen.min.z - 1.2f);
        EggPartChooser.Show(at, Mathf.Max(seen.extents.x, seen.extents.y), title, part =>
        {
            if (this == null || bowl == null) return;
            if (onPick != null) { onPick(part); return; }
            HandCarry hand = HandCarry.Instance;
            if (hand == null || hand.Current != null) return;
            if (hand.PickUp(bowl))
            {
                Chosen = part;
                Debug.Log($"[달걀] {bowl.name}: {EggPartChooser.Name(part)} 선택 - 부을 그릇을 누르세요");
            }
        });
    }

    /// <summary>Recorded only (the console) - no message on screen while cooking.</summary>
    public void Say(string text)
    {
        Debug.Log($"[달걀] {bowl.name}: {text}");
    }
}

/// <summary>계란 사용: three round buttons on a half ring over the bowl (like the
/// cut menu's ring), in front of everything. A click anywhere else closes it (ClickDetector).</summary>
public static class EggPartChooser
{
    private static GameObject panel;
    private static Action<EggContents.Part> onChosen;

    public static bool IsOpen => panel != null && panel.activeSelf;

    public static string Name(EggContents.Part p) => p == EggContents.Part.White ? "흰자만" : p == EggContents.Part.Yolk ? "노른자만" : "전체(전란)";

    /// <summary>A half ring over the bowl (centred on it, its radius on screen
    /// bowlRadius) - the bowl itself left showing in the open middle.</summary>
    public static void Show(Vector3 worldPos, float bowlRadius, string title, Action<EggContents.Part> callback)
    {
        // 흰자만 on the left, 노른자만 at the top, 전체 on the right
        var options = new[]
        {
            new Option(Name(EggContents.Part.White), new Color(0.98f, 0.97f, 0.92f, 0.97f), true, () => Choose(EggContents.Part.White)),
            new Option(Name(EggContents.Part.Yolk), new Color(1f, 0.82f, 0.4f, 0.97f), true, () => Choose(EggContents.Part.Yolk), 18),
            new Option("전체\n(전란)", new Color(1f, 0.92f, 0.7f, 0.97f), true, () => Choose(EggContents.Part.All)),
        };
        Open(worldPos, bowlRadius, title, options); // (Choose가 창을 닫고 onChosen을 부른다)
        onChosen = callback;
    }

    /// <summary>선택창의 버튼 하나: 글자, 색, 누를 수 있는지(아니면 회색, 눌러도 무시), 눌렀을 때.</summary>
    public struct Option
    {
        public string label;
        public Color color;
        public bool enabled;
        public Action act;
        public int fontSize;
        public Option(string label, Color color, bool enabled, Action act, int fontSize = 20)
        {
            this.label = label; this.color = color; this.enabled = enabled; this.act = act; this.fontSize = fontSize;
        }
    }

    /// <summary>계란 사용과 같은 모양·입력의 선택창에 아무 버튼이나 (그릇 위 반원,
    /// 왼쪽부터) - 버무리기 끝난 그릇의 '반죽 조각 / 고기 한 점 / 닫기' 등.
    /// 버튼을 누르면 창이 닫히고 그 동작. 창 밖을 누르면 닫힘 (ClickDetector).</summary>
    public static void ShowOptions(Vector3 worldPos, float bowlRadius, string title, Option[] options)
    {
        var wrapped = new Option[options.Length];
        for (int i = 0; i < options.Length; i++)
        {
            wrapped[i] = options[i];
            Action act = options[i].act;
            wrapped[i].act = () => { Close(); act?.Invoke(); };
        }
        Open(worldPos, bowlRadius, title, wrapped);
    }

    private static void Open(Vector3 worldPos, float bowlRadius, string title, Option[] options)
    {
        if (panel != null) UnityEngine.Object.Destroy(panel); // (sized to each bowl)
        Build(bowlRadius, title, options);
        onChosen = null;
        panel.transform.position = worldPos;
        panel.SetActive(true);
    }

    public static void Close()
    {
        onChosen = null;
        if (panel != null) panel.SetActive(false);
    }

    public static bool IsOver(Ray ray)
    {
        if (!IsOpen) return false;
        foreach (RaycastHit h in Physics.RaycastAll(ray, 500f))
            if (h.collider.transform.IsChildOf(panel.transform)) return true;
        return false;
    }

    private static void Choose(EggContents.Part p)
    {
        Action<EggContents.Part> cb = onChosen;
        Close();
        cb?.Invoke(p);
    }

    private const float ButtonRadius = 0.62f;

    private static void Build(float bowlRadius, string title, Option[] options)
    {
        panel = new GameObject("EggPartChooser");
        // the half ring (like the cut menu's ring), the bowl showing inside it
        float inner = Mathf.Max(bowlRadius * 1.08f + 0.05f, 0.6f);
        float outer = inner + ButtonRadius * 2f + 0.25f;
        float orbit = (inner + outer) * 0.5f;
        GameObject ring = new GameObject("Ring");
        ring.transform.SetParent(panel.transform, false);
        ring.transform.localPosition = new Vector3(0f, 0f, -0.02f);
        Mesh arc = Arc(inner, outer, -8f, 188f, 48);
        ring.AddComponent<MeshFilter>().sharedMesh = arc;
        ring.AddComponent<MeshRenderer>().sharedMaterial = Mat(new Color(0.9f, 0.88f, 0.8f, 0.6f));
        ring.AddComponent<MeshCollider>().sharedMesh = arc; // (a click on the ring itself does nothing)
        Label(panel.transform, title, new Vector3(0f, outer + 0.32f, -0.08f), 24);

        // 왼쪽부터 반원을 따라 고르게 (3개면 155° · 90° · 25°)
        int n = options.Length;
        for (int i = 0; i < n; i++)
        {
            Option o = options[i];
            float deg = n == 1 ? 90f : Mathf.Lerp(155f, 25f, i / (float)(n - 1));
            GameObject btn = new GameObject("Btn_" + o.label.Replace("\n", " "));
            btn.transform.SetParent(panel.transform, false);
            float a = deg * Mathf.Deg2Rad;
            btn.transform.localPosition = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f) * orbit + new Vector3(0f, 0f, -0.1f);
            Mesh disc = Disc(ButtonRadius, 40);
            btn.AddComponent<MeshFilter>().sharedMesh = disc;
            // (누를 수 없는 버튼은 흐린 회색 - 창 위라 눌러도 창은 닫히지 않고 아무 일도 없음)
            btn.AddComponent<MeshRenderer>().sharedMaterial = Mat(o.enabled ? o.color : new Color(0.72f, 0.72f, 0.72f, 0.6f));
            btn.AddComponent<MeshCollider>().sharedMesh = disc;
            if (o.enabled) btn.AddComponent<SimpleActionButton>().Init(o.act);
            TextMesh tm = Label(btn.transform, o.label, new Vector3(0f, 0f, -0.05f), o.fontSize);
            if (!o.enabled) tm.color = new Color(0f, 0f, 0f, 0.35f);
        }
        panel.SetActive(false);
    }

    private static TextMesh Label(Transform parent, string text, Vector3 local, int size)
    {
        var go = new GameObject("Label");
        go.transform.SetParent(parent, false);
        go.transform.localPosition = local;
        TextMesh tm = go.AddComponent<TextMesh>();
        tm.text = text;
        tm.fontSize = size;
        tm.characterSize = 0.16f;
        tm.anchor = TextAnchor.MiddleCenter;
        tm.color = Color.black;
        return tm;
    }

    /// <summary>A flat band between two radii from angle a0 to a1 (degrees, 0 =
    /// right, counter-clockwise), facing the camera.</summary>
    private static Mesh Arc(float inner, float outer, float a0, float a1, int segments)
    {
        var v = new Vector3[(segments + 1) * 2];
        var t = new int[segments * 6];
        for (int i = 0; i <= segments; i++)
        {
            float a = Mathf.Lerp(a0, a1, i / (float)segments) * Mathf.Deg2Rad;
            Vector3 d = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f);
            v[i * 2] = d * inner;
            v[i * 2 + 1] = d * outer;
            if (i == segments) break;
            int k = i * 6, n = i + 1;
            t[k] = i * 2; t[k + 1] = n * 2; t[k + 2] = i * 2 + 1;
            t[k + 3] = i * 2 + 1; t[k + 4] = n * 2; t[k + 5] = n * 2 + 1;
        }
        return FaceCamera(new Mesh { vertices = v, triangles = t });
    }

    /// <summary>Faces the triangles toward -Z (the camera) whichever way they were wound.</summary>
    private static Mesh FaceCamera(Mesh m)
    {
        m.RecalculateNormals();
        if (m.normals.Length > 0 && m.normals[0].z > 0f)
        {
            int[] tt = m.triangles;
            for (int i = 0; i < tt.Length; i += 3) (tt[i + 1], tt[i + 2]) = (tt[i + 2], tt[i + 1]);
            m.triangles = tt;
            m.RecalculateNormals();
        }
        m.RecalculateBounds();
        return m;
    }

    private static Material Mat(Color c)
    {
        Shader s = Shader.Find("Sprites/Default") ?? Shader.Find("Universal Render Pipeline/Unlit");
        return new Material(s) { color = c };
    }

    private static Mesh Disc(float radius, int segments)
    {
        var v = new Vector3[segments + 1];
        var t = new int[segments * 3];
        for (int i = 0; i < segments; i++)
        {
            float a = i * Mathf.PI * 2f / segments;
            v[i + 1] = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f) * radius;
            // (wound to face -Z, the camera)
            t[i * 3] = 0; t[i * 3 + 1] = i + 1; t[i * 3 + 2] = (i + 1) % segments + 1;
        }
        return FaceCamera(new Mesh { vertices = v, triangles = t });
    }
}
