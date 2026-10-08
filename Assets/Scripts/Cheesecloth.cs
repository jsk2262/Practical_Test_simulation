using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 면보 (cheesecloth) - straining 고추기름:
/// small bowl set down → the cloth (picked up folded) clicked on it: spread
/// over it, sagging in the middle, its edges hanging over the rim → the pan
/// of oil fried with 고춧가루, in hand, clicked on the cloth: tipped, more as it
/// tips, the oil running out in a stream → the 고춧가루 stays in the cloth, the
/// oil soaks through and drips into the bowl at its own (slower) rate - also
/// after the pan is set upright → when it's all through, the cloth is lifted
/// off (clicked) → the oil in the bowl is 고추기름, used with the measuring
/// spoon or poured like any sauce.
///
/// Amounts are SaucePortion/PanSauce spoonfuls, moved, never copied or made
/// up: pan → (pour) → the cloth's solids + its liquid still to pass (pending)
/// → (filter) → the bowl. The oil keeps its colour, taste and heat. A full
/// bowl stops the pour (가득 참) and the liquid waits in the cloth.
/// The bowl under the cloth can't be taken away while it's there.
/// Lifted, the cloth stays used (stained, the chilli in it) until the chilli
/// is thrown in the food waste and the cloth rinsed at the sink.
/// </summary>
public class Cheesecloth : Carryable
{
    public enum State { Folded, Spread, Used }

    [Header("붓기 · 거르기 (스푼/초)")]
    [Tooltip("How fast the pan pours at full tilt.")]
    [SerializeField] private float pourRate = 1.2f;
    [Tooltip("How fast the oil passes through the cloth into the bowl.")]
    [SerializeField] private float filterRate = 0.45f;
    [Tooltip("Liquid the cloth can hold before the pour has to slow to the filtering.")]
    [SerializeField] private float clothHold = 1.5f;
    [SerializeField] private float maxTilt = 65f;

    [Header("모양")]
    [SerializeField] private Vector2 foldedSize = new Vector2(0.42f, 0.34f);
    [SerializeField] private float foldedThickness = 0.05f;
    [SerializeField] private Color clothColor = new Color(0.97f, 0.97f, 0.95f);

    private static readonly List<Cheesecloth> spreadOnes = new List<Cheesecloth>();

    /// <summary>The cloth spread over this bowl, if any.</summary>
    public static Cheesecloth On(MovableBowl bowl)
    {
        foreach (Cheesecloth c in spreadOnes) if (c != null && c.bowl == bowl) return c;
        return null;
    }

    public State CurrentState { get; private set; } = State.Folded;
    /// <summary>The bowl it's spread over (explicitly linked while spread).</summary>
    public MovableBowl Bowl => bowl;
    public bool IsPouring { get; private set; }
    /// <summary>Oil still in the cloth, not yet through.</summary>
    public float PendingLiquid => pending.Amount;
    public float ResidueAmount => residue.Amount;
    public bool HasResidue => residue.Amount > 0.001f;
    public bool IsFiltering => pending.Amount > 0.001f;
    public bool BowlFull { get; private set; }

    private MovableBowl bowl;
    private readonly SaucePortion pending = new SaucePortion();   // liquid waiting to pass
    private readonly SaucePortion residue = new SaucePortion();   // the solids kept back
    private float wet, rimR, sag, span;
    private bool used;
    private Color oilColor = new Color(0.8f, 0.2f, 0.05f);

    private MeshFilter mf;
    private MeshRenderer mr;
    private BoxCollider box;
    private Mesh foldedMesh, spreadMesh;
    private GameObject residueShown, poolShown, sign;
    private float residueShownAt = -1f, poolShownAt = -1f;
    private LineRenderer stream;
    private float dripTimer, lastFlow;
    private static Material dropMat;

    protected override void Awake()
    {
        mf = GetComponent<MeshFilter>();
        if (mf == null) mf = gameObject.AddComponent<MeshFilter>();
        mr = GetComponent<MeshRenderer>();
        if (mr == null) mr = gameObject.AddComponent<MeshRenderer>();
        box = GetComponent<BoxCollider>();
        Shader lit = Shader.Find("Universal Render Pipeline/Lit");
        mr.sharedMaterial = new Material(lit != null ? lit : Shader.Find("Standard")) { color = clothColor };
        if (mr.sharedMaterial.HasProperty("_Smoothness")) mr.sharedMaterial.SetFloat("_Smoothness", 0.05f);
        foldedMesh = ClothWiper.PadMesh(foldedSize.x, foldedSize.y, foldedThickness, 0.04f, Vector2.zero);
        ShowFolded();
        base.Awake();
    }

    void OnDisable() => spreadOnes.Remove(this);

    // ---- spreading and lifting ----

    /// <summary>Whether it can go over this bowl (a deep bowl, empty of food, not a pot/pan).</summary>
    public bool Supports(MovableBowl b, out string why)
    {
        why = null;
        if (b == null || b.IsCookingVessel || !b.IsPlateOrBowl) { why = "작은 그릇 위에 펼치세요"; return false; }
        MovableBowl.Interior i = b.GetInterior();
        if (i.rim <= i.radius * 0.35f || i.radius < 0.12f) { why = "오목한 그릇 위에 펼치세요"; return false; }
        if (b.HasIngredients) { why = "빈 그릇에 펼치세요"; return false; }
        if (On(b) != null) { why = "이미 면보가 있어요"; return false; }
        return true;
    }

    /// <summary>The cloth in hand spread over the bowl.</summary>
    public bool TrySpreadOn(MovableBowl target)
    {
        if (CurrentState == State.Spread || HandCarry.Instance == null || (object)HandCarry.Instance.Current != this) return false;
        if (HasResidue) { Say("찌꺼기를 먼저 음식물쓰레기에 버리세요"); return false; }
        if (!Supports(target, out string why)) { Say(why); return false; }

        HandCarry.Instance.Detach();
        CurrentContainer = null;
        transform.SetParent(null, true);
        bowl = target;
        spreadOnes.Add(this);
        CurrentState = State.Spread;
        BuildSpread();
        Debug.Log($"[면보] {target.name} 위에 펼침 (받을 수 있는 양 {PanSauce.CapacityOf(target):0}스푼)");
        return true;
    }

    /// <summary>Lifted off the bowl (only once nothing is passing through it).</summary>
    private bool TryLift()
    {
        if (IsPouring || IsFiltering) { Say(IsPouring ? "붓는 중이에요" : "아직 거르는 중이에요"); return false; }
        spreadOnes.Remove(this);
        Debug.Log($"[면보] {bowl.name}에서 들어냄 - 그릇에 {(PanSauce.Of(bowl, false)?.LiquidAmount ?? 0f):0.#}스푼, 면보에 찌꺼기 {residue.Amount:0.#}스푼");
        bowl = null;
        BowlFull = false;
        CurrentState = used ? State.Used : State.Folded;
        ShowFolded();
        return true;
    }

    protected override bool OnClickedWhileHandEmpty()
    {
        if (CurrentState != State.Spread) return false;  // folded: picked up as usual
        return !TryLift();                               // spread: lifted, then picked up
    }

    protected override void OnClickedWhileHandFull(Carryable held)
    {
        if (CurrentState == State.Spread) ClickedWith(held);
    }

    public override bool CanAcceptDrop(Carryable item) =>
        CurrentState == State.Spread && item is MovableBowl pan && PanSauce.Of(pan, false) is PanSauce s && s.Amount > 0.01f;

    /// <summary>Something in hand clicked on the spread cloth (or its bowl):
    /// a pan with sauce in it is poured through.</summary>
    public void ClickedWith(Carryable held)
    {
        if (IsPouring) return;
        if (held is MovableBowl pan && pan != bowl && PanSauce.Of(pan, false) is PanSauce sauce && sauce.Amount > 0.01f)
        {
            if (RoomInBowl() <= 0.001f) { BowlFull = true; Say("그릇이 가득 찼어요"); return; }
            StartCoroutine(Pour(pan, sauce));
            return;
        }
        Say("팬의 기름을 부어 거르세요");
    }

    /// <summary>The chilli in it thrown away (into the food waste bag); the cloth
    /// itself stays - used, until it's washed.</summary>
    public void EmptyResidue()
    {
        foreach (var kv in residue.parts) WasteLog.Add(kv.Key, "면보 찌꺼기");
        residue.parts.Clear();
        UpdateResidueLook(true);
    }

    /// <summary>Rinsed at the sink: clean again.</summary>
    public void Wash()
    {
        if (HasResidue || CurrentState == State.Spread) return;
        used = false;
        wet = 0f;
        CurrentState = State.Folded;
        UpdateTint();
        Debug.Log("[면보] 빨아서 깨끗해짐");
    }

    // ---- pouring (pan → cloth) ----

    private float RoomInBowl() => bowl == null ? 0f
        : PanSauce.CapacityOf(bowl) - (PanSauce.Of(bowl, false)?.LiquidAmount ?? 0f) - pending.Amount;

    private IEnumerator Pour(MovableBowl pan, PanSauce sauce)
    {
        IsPouring = true;
        HandCarry hand = HandCarry.Instance;
        if (hand != null) hand.IsLocked = true;
        HandController hc = HandController.Instance;
        Vector3 c = Sag(0f);
        MovableBowl.Interior panIn = pan.GetInterior();

        // The pan brought up beside the cloth, to its upper right.
        if (hc != null)
        {
            // (where its body is from the hand holding it - by its hold offset:
            // just picked up, it hasn't been brought to the hand yet)
            Vector3 bodyFromHand = pan.HandHoldOffset + (pan.BodyCenterWorld() - pan.transform.position);
            Vector3 want = c + new Vector3(rimR + panIn.radius * 0.8f, rimR + 0.35f, 0f);
            Vector3 to = want - bodyFromHand;
            hc.GoTo(new Vector3(to.x, to.y, hc.transform.position.z), null);
        }
        yield return new WaitForSeconds(0.4f);

        float tilt = 0f, poured = 0f;
        bool stop = false;
        while (!stop)
        {
            Mouse m = Mouse.current;
            if ((m != null && m.leftButton.wasPressedThisFrame) || (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame))
                break; // (stopped part way)
            tilt = Mathf.MoveTowards(tilt, maxTilt, maxTilt * Time.deltaTime);
            pan.SetPourTilt(Quaternion.AngleAxis(tilt, Vector3.forward));

            // The more it's tipped, the faster it runs out - none until it's tipped enough.
            float want = pourRate * Mathf.InverseLerp(20f, maxTilt, tilt) * Time.deltaTime;
            float total = sauce.Amount;
            float liquidShare = total > 0f ? sauce.LiquidAmount / total : 0f;
            float room = RoomInBowl();
            float clothRoom = clothHold - pending.Amount;
            float amount = Mathf.Min(want, total);
            if (liquidShare > 0.001f) amount = Mathf.Min(amount, Mathf.Max(0f, room) / liquidShare, Mathf.Max(0f, clothRoom) / liquidShare);
            if (amount > 0f)
            {
                SaucePortion poured1 = sauce.Take(amount);
                residue.Add(poured1.TakeSolids());   // the 고춧가루 stays in the cloth
                pending.Add(poured1);               // the oil, on its way through
                oilColor = pending.LiquidColor;
                poured += poured1.Amount;
                used = true;
            }
            ShowStream(pan, panIn, amount / Mathf.Max(Time.deltaTime, 1e-4f), sauce.LiquidColor);

            if (sauce.Amount <= 0.001f) stop = true;                                        // all out
            else if (room <= 0.001f) { BowlFull = true; Say("그릇이 가득 찼어요"); stop = true; }  // no more fits
            yield return null;
        }
        HideStream();
        for (float t = 0f; t < 0.35f; t += Time.deltaTime)
        {
            pan.SetPourTilt(Quaternion.AngleAxis(Mathf.Lerp(tilt, 0f, t / 0.35f), Vector3.forward));
            yield return null;
        }
        pan.SetPourTilt(Quaternion.identity);
        Debug.Log($"[면보] 부음 {poured:0.##}스푼 - 팬에 남음 {sauce.Amount:0.##}, 면보 위 기름 {pending.Amount:0.##}, 찌꺼기 {residue.Amount:0.##}");
        if (hand != null) hand.IsLocked = false;
        IsPouring = false;
    }

    // ---- filtering (cloth → bowl) ----

    void Update()
    {
        if (CurrentState == State.Spread && bowl != null)
        {
            lastFlow = 0f;
            if (pending.Amount > 0.0001f)
            {
                PanSauce into = PanSauce.Of(bowl, true);
                float room = PanSauce.CapacityOf(bowl) - into.LiquidAmount;
                float flow = Mathf.Min(filterRate * Time.deltaTime, pending.Amount, Mathf.Max(0f, room));
                if (flow > 0f)
                {
                    into.Put(pending.Take(flow));
                    lastFlow = flow / Mathf.Max(Time.deltaTime, 1e-4f);
                    if (pending.Amount <= 0.0001f) Debug.Log($"[면보] 다 걸러짐 - 그릇에 {into.LiquidAmount:0.##}스푼 ({into.LiquidName})");
                }
                BowlFull = room <= 0.001f;
            }
            Drips();
            UpdatePool();
            UpdateResidueLook(false);
        }
        wet = IsFiltering || IsPouring ? Mathf.Min(1f, wet + Time.deltaTime * 0.8f)
                                       : Mathf.Max(used ? 0.45f : 0f, wet - Time.deltaTime * 0.02f);
        UpdateTint();
        UpdateSign();
    }

    // ---- looks ----

    private void ShowFolded()
    {
        mf.sharedMesh = foldedMesh;
        if (box != null)
        {
            box.center = Vector3.zero;
            box.size = new Vector3(foldedSize.x, Mathf.Max(foldedThickness, 0.06f), foldedSize.y);
        }
        if (poolShown != null) { Destroy(poolShown); poolShown = null; poolShownAt = -1f; }
        UpdateResidueLook(true);
    }

    /// <summary>Spread over the bowl: centred on its rim, sagging into it, the
    /// square's edges hanging down over the rim (as in the photo).</summary>
    private void BuildSpread()
    {
        MovableBowl.Interior i = bowl.GetInterior();
        Renderer br = bowl.GetComponent<Renderer>();
        rimR = Mathf.Max(i.radius * 1.08f, br != null && br.enabled ? Mathf.Min(br.bounds.extents.x, i.radius * 1.4f) : i.radius * 1.1f);
        sag = Mathf.Min(i.rim * 0.6f, rimR * 0.55f);
        span = rimR * 2.5f;
        Vector3 right = Vector3.ProjectOnPlane(Vector3.right, i.up).normalized;
        Vector3 back = Vector3.Cross(right, i.up).normalized;
        transform.SetPositionAndRotation(i.floorCenter + i.up * (i.rim + 0.01f), Quaternion.LookRotation(back, i.up));
        if (spreadMesh != null) Destroy(spreadMesh);
        spreadMesh = DrapeMesh(rimR, sag, span, 22);
        mf.sharedMesh = spreadMesh;
        if (box != null)
        {
            box.center = new Vector3(0f, -sag * 0.5f, 0f);
            box.size = new Vector3(span, sag + 0.08f, span);
        }
        UpdateResidueLook(true);
    }

    /// <summary>Where the cloth's middle sags to (world), a little above it by lift.</summary>
    private Vector3 Sag(float lift) => transform.TransformPoint(new Vector3(0f, -sag + lift, 0f));

    private static Mesh DrapeMesh(float rim, float sag, float size, int n)
    {
        var v = new List<Vector3>();
        var uv = new List<Vector2>();
        for (int side = 0; side < 2; side++)
            for (int j = 0; j <= n; j++)
                for (int i = 0; i <= n; i++)
                {
                    float x = -size * 0.5f + size * i / n, z = -size * 0.5f + size * j / n;
                    float d = Mathf.Sqrt(x * x + z * z);
                    float y = d <= rim ? -sag * (1f - (d / rim) * (d / rim))         // in the bowl: sagging
                                       : -Mathf.Min((d - rim) * 0.8f, rim * 0.9f);   // past the rim: hanging down
                    // soft folds running out to the edges
                    y += 0.012f * Mathf.Sin(Mathf.Atan2(z, x) * 5f) * Mathf.Clamp01((d - rim * 0.5f) / (rim * 0.5f));
                    v.Add(new Vector3(x, y, z));
                    uv.Add(new Vector2((float)i / n, (float)j / n));
                }
        var t = new List<int>();
        int per = (n + 1) * (n + 1);
        for (int j = 0; j < n; j++)
            for (int i = 0; i < n; i++)
            {
                int a = j * (n + 1) + i, b = a + 1, c = a + (n + 1), d = c + 1;
                t.AddRange(new[] { a, c, b, c, d, b });                                  // top
                t.AddRange(new[] { per + a, per + b, per + c, per + c, per + b, per + d }); // underside
            }
        var m = new Mesh { name = "CheeseclothSpread" };
        m.SetVertices(v);
        m.SetUVs(0, uv);
        m.SetTriangles(t, 0);
        m.RecalculateNormals();
        m.RecalculateBounds();
        return m;
    }

    /// <summary>The cloth darkens as the oil soaks it (and stays stained once used).</summary>
    private void UpdateTint()
    {
        Color c = Color.Lerp(clothColor, oilColor, wet * 0.55f);
        if (mr != null && mr.sharedMaterial.color != c) mr.sharedMaterial.color = c;
    }

    /// <summary>The chilli kept back, heaped in the cloth's sagging middle (or on the folded cloth).</summary>
    private void UpdateResidueLook(bool force)
    {
        float amount = residue.Amount;
        if (!force && Mathf.Abs(amount - residueShownAt) < Mathf.Max(0.05f, residueShownAt * 0.12f)) return;
        if (residueShown != null) { Destroy(residueShown); residueShown = null; }
        residueShownAt = amount;
        if (amount <= 0.001f) return;
        bool spread = CurrentState == State.Spread;
        float baseR = spread ? rimR : Mathf.Min(foldedSize.x, foldedSize.y) * 0.5f;
        float r = Mathf.Clamp(baseR * 0.3f * Mathf.Sqrt(amount), baseR * 0.12f, baseR * 0.6f);
        residueShown = new GameObject("ChiliResidue");
        residueShown.transform.SetParent(transform, false);
        residueShown.transform.localPosition = spread ? new Vector3(0f, -sag + 0.006f, 0f) : new Vector3(0f, foldedThickness * 0.5f + 0.003f, 0f);
        Condiments.BuildContent(residueShown.transform, Vector3.zero, Vector3.up, r, CondimentForm.Powder, new Color(0.55f, 0.1f, 0.04f), "Residue");
    }

    /// <summary>Oil sitting in the cloth, not through yet: a pool in its middle.</summary>
    private void UpdatePool()
    {
        float amount = pending.Amount;
        if (Mathf.Abs(amount - poolShownAt) < Mathf.Max(0.04f, poolShownAt * 0.12f)) return;
        if (poolShown != null) { Destroy(poolShown); poolShown = null; }
        poolShownAt = amount;
        if (amount <= 0.01f) return;
        poolShown = new GameObject("PendingOil");
        poolShown.transform.SetParent(transform, false);
        poolShown.transform.localPosition = new Vector3(0f, -sag + 0.004f, 0f);
        float r = Mathf.Clamp(rimR * 0.25f * Mathf.Sqrt(amount), rimR * 0.15f, rimR * 0.75f);
        Condiments.BuildContent(poolShown.transform, Vector3.zero, Vector3.up, r, CondimentForm.Liquid, oilColor, "Pool");
    }

    /// <summary>Drops from under the cloth's middle into the bowl, as fast as it filters.</summary>
    private void Drips()
    {
        if (lastFlow <= 0f) return;
        dripTimer -= Time.deltaTime;
        if (dripTimer > 0f) return;
        dripTimer = Mathf.Lerp(0.35f, 0.06f, Mathf.Clamp01(lastFlow / Mathf.Max(filterRate, 0.01f)));
        StartCoroutine(Drip());
    }

    private IEnumerator Drip()
    {
        if (dropMat == null)
        {
            Shader sh = Shader.Find("Universal Render Pipeline/Unlit");
            dropMat = new Material(sh != null ? sh : Shader.Find("Standard"));
        }
        GameObject d = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        Destroy(d.GetComponent<Collider>());
        Renderer r = d.GetComponent<Renderer>();
        r.sharedMaterial = dropMat;
        var block = new MaterialPropertyBlock();
        block.SetColor("_BaseColor", oilColor);
        block.SetColor("_Color", oilColor);
        r.SetPropertyBlock(block);
        d.transform.localScale = Vector3.one * Random.Range(0.02f, 0.032f);
        Vector3 from = Sag(-0.01f) + new Vector3(Random.Range(-0.03f, 0.03f), 0f, -0.05f);
        Vector3 to = from + Vector3.down * Mathf.Max(sag, 0.08f) * 1.4f;
        for (float t = 0f; t < 0.3f; t += Time.deltaTime)
        {
            if (d == null) yield break;
            d.transform.position = Vector3.Lerp(from, to, t * t / 0.09f);
            yield return null;
        }
        Destroy(d);
    }

    /// <summary>The stream from the pan's lip into the cloth, as thick as it runs.</summary>
    private void ShowStream(MovableBowl pan, MovableBowl.Interior panIn, float rate, Color color)
    {
        if (stream == null)
        {
            var go = new GameObject("PourStream");
            stream = go.AddComponent<LineRenderer>();
            Shader sh = Shader.Find("Universal Render Pipeline/Unlit");
            stream.material = new Material(sh != null ? sh : Shader.Find("Sprites/Default"));
            stream.positionCount = 3;
            stream.numCapVertices = 3;
        }
        if (rate <= 0.001f) { stream.enabled = false; return; }
        stream.enabled = true;
        stream.material.color = color;
        if (stream.material.HasProperty("_BaseColor")) stream.material.SetColor("_BaseColor", color);
        Vector3 body = pan.BodyCenterWorld();
        Vector3 into = Sag(0.02f);
        Vector3 toward = into - body;
        toward.z = 0f;
        Vector3 lip = body + toward.normalized * panIn.radius * 0.95f + Vector3.forward * -0.1f;
        Vector3 end = new Vector3(into.x, into.y, Mathf.Min(into.z, lip.z));
        stream.SetPosition(0, lip);
        stream.SetPosition(1, Vector3.Lerp(lip, end, 0.5f) + Vector3.down * 0.05f);
        stream.SetPosition(2, end);
        stream.widthMultiplier = Mathf.Lerp(0.012f, 0.045f, Mathf.Clamp01(rate / Mathf.Max(pourRate, 0.01f)));
    }

    private void HideStream()
    {
        if (stream != null) stream.enabled = false;
    }

    /// <summary>"가득 참" over the bowl while it's full and oil is waiting in the cloth.</summary>
    private void UpdateSign()
    {
        bool show = CurrentState == State.Spread && BowlFull;
        if (!show) { if (sign != null) sign.SetActive(false); return; }
        if (sign == null)
        {
            sign = new GameObject("BowlFullSign");
            TextMesh tm = sign.AddComponent<TextMesh>();
            tm.text = "가득 참";
            tm.fontSize = 40;
            tm.characterSize = 0.08f;
            tm.anchor = TextAnchor.MiddleCenter;
            tm.color = new Color(0.9f, 0.2f, 0.1f);
        }
        sign.SetActive(true);
        sign.transform.position = transform.position + new Vector3(0f, rimR + 0.35f, -1f);
    }

    /// <summary>A short note over the cloth.</summary>
    public void Say(string text)
    {
        if (string.IsNullOrEmpty(text)) return;
        Debug.Log("[면보] " + text);
        var go = new GameObject("ClothNote");
        go.transform.position = transform.position + new Vector3(0f, 0.6f, -1.2f);
        TextMesh tm = go.AddComponent<TextMesh>();
        tm.text = text;
        tm.fontSize = 40;
        tm.characterSize = 0.08f;
        tm.anchor = TextAnchor.MiddleCenter;
        tm.color = new Color(0.15f, 0.15f, 0.15f);
        Destroy(go, 1.5f);
    }

    void OnDestroy()
    {
        spreadOnes.Remove(this);
        if (stream != null) Destroy(stream.gameObject);
        if (sign != null) Destroy(sign);
    }
}
