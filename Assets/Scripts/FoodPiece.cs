using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// One independent piece of a cut ingredient - one of the diced tofu's cubes:
/// its own transform, collider, cooking and sauce coating, and the source
/// ingredient's data (name, kind, washed, its share of the weight). Pieces
/// belong to their ingredient's FoodPieceGroup, which moves them together
/// (carried, on the board) or each on its own (in a pot, pan or on a plate).
/// Colors go through a MaterialPropertyBlock, so the shared material - and the
/// other pieces - are never changed by one piece's color.
/// </summary>
public class FoodPiece : MonoBehaviour
{
    public string SourceName { get; private set; } = "";
    public IngredientCategory Category { get; private set; }
    public bool WasWashed { get; private set; }
    public float Grams { get; private set; }

    /// <summary>Seconds of cooking (heat) this piece has had - see CookableItem.</summary>
    public float Cook { get; set; }

    /// <summary>How much sauce is on it, 0..1, and that sauce's color/thickness.</summary>
    public float Coat { get; private set; }
    public Color CoatColor { get; private set; } = Color.white;
    public float CoatViscosity { get; private set; }

    /// <summary>How broken up it is (0 whole .. 1 crumbled) - a soft piece (두부)
    /// knocked about by stirring. Shown by it wearing down (look only: its
    /// grams don't change).</summary>
    public float Damage { get; private set; }
    private Vector3 wholeScale = new Vector3(-1f, 0f, 0f);

    public void AddDamage(float amount)
    {
        if (amount <= 0f || Damage >= 1f) return;
        if (wholeScale.x < 0f) wholeScale = transform.localScale;
        float was = Damage;
        Damage = Mathf.Min(1f, Damage + amount);
        // worn smaller and a little flattened as it breaks
        transform.localScale = Vector3.Scale(wholeScale, new Vector3(1f - 0.2f * Damage, 1f - 0.35f * Damage, 1f - 0.2f * Damage));
        if (was < 0.5f && Damage >= 0.5f) Debug.Log($"[{SourceName}] 조각이 부서지기 시작함 (손상 {Damage:0.00})");
    }

    /// <summary>About half its size (world) - how close it comes to walls and other pieces.</summary>
    public float Radius { get; private set; } = 0.05f;

    // Simulation state, in the container's frame (x across, y up, z back).
    internal Vector3 simPos, simVel, simSpin;
    internal Quaternion simRot = Quaternion.identity;
    internal float seed;

    private Renderer rend;
    private MaterialPropertyBlock block;
    private Color shownColor = new Color(-1f, 0f, 0f);
    private float shownSmooth = -1f;

    public void Init(IngredientItem from, float grams, float cook, Vector3 worldSize)
    {
        SourceName = from.IngredientName;
        Category = from.Category;
        WasWashed = from.WasWashed;
        Grams = grams;
        Cook = cook;
        SetSize(worldSize);
        seed = Random.Range(0f, 100f);
    }

    public void SetSize(Vector3 worldSize)
    {
        Radius = (worldSize.x + worldSize.y + worldSize.z) / 6f;
        HalfSize = worldSize * 0.5f;
    }

    /// <summary>Half its size along its own axes (world) - a long strip reaches
    /// further toward a wall one way than the other.</summary>
    public Vector3 HalfSize { get; private set; }

    /// <summary>How far it reaches from its middle along dir (in the container's
    /// frame, as turned by simRot) - at least its Radius.</summary>
    internal float ReachAlong(Vector3 dir)
    {
        if (HalfSize == Vector3.zero) return Radius;
        float e = Mathf.Abs(Vector3.Dot(simRot * Vector3.right, dir)) * HalfSize.x
                + Mathf.Abs(Vector3.Dot(simRot * Vector3.up, dir)) * HalfSize.y
                + Mathf.Abs(Vector3.Dot(simRot * Vector3.forward, dir)) * HalfSize.z;
        return Mathf.Max(e, Radius);
    }

    /// <summary>Takes over another piece's whole state (its copy, split off into
    /// its own ingredient - IngredientItem.SplitOffPiece).</summary>
    public void CopyStateFrom(FoodPiece o)
    {
        SourceName = o.SourceName; Category = o.Category; WasWashed = o.WasWashed; Grams = o.Grams;
        Cook = o.Cook; Coat = o.Coat; CoatColor = o.CoatColor; CoatViscosity = o.CoatViscosity;
        HalfSize = o.HalfSize;
        Radius = o.Radius; Sear = o.Sear; Char = o.Char; PanCooked = o.PanCooked; Temp = o.Temp;
        Damage = o.Damage; SearBrown = o.SearBrown; CharBlack = o.CharBlack;
        seed = o.seed;
    }

    /// <summary>More sauce on it (amount is how much of the bare part takes it).</summary>
    public void AddCoat(float amount, Color color, float viscosity)
    {
        if (amount <= 0f) return;
        float before = Coat;
        Coat = Mathf.Clamp01(Coat + amount * (1f - Coat));
        float share = Coat > 1e-4f ? (Coat - before) / Coat : 0f;
        CoatColor = before <= 0f ? color : Color.Lerp(CoatColor, color, share);
        CoatViscosity = before <= 0f ? viscosity : Mathf.Lerp(CoatViscosity, viscosity, share);
    }

    /// <summary>A thin sauce slowly runs off; a thick one stays on.</summary>
    public void DripCoat(float dt)
    {
        if (Coat <= 0f) return;
        float floor = Coat * CoatViscosity; // what a thick sauce keeps
        Coat = Mathf.Max(floor, Coat - (1f - CoatViscosity) * 0.03f * dt);
    }

    /// <summary>Browned where it lay on the hot pan (0..1), and charred (0..1) -
    /// in a dry pan or oil (FoodPieceGroup). Neither is ever undone.</summary>
    public float Sear { get; set; }
    public float Char { get; set; }
    /// <summary>Has been cooked in a pan/wok (its colour is worked out from its
    /// cooking, not the blanching colour).</summary>
    public bool PanCooked { get; set; }
    /// <summary>Its own temperature (°C) - apart from the pan's (FoodPieceGroup).</summary>
    public float Temp { get; set; } = 20f;

    /// <summary>This ingredient's browned / charred colours (CookableItem.PanLook - set by its group).</summary>
    public Color SearBrown { get; set; } = new Color(0.62f, 0.38f, 0.18f);
    public Color CharBlack { get; set; } = new Color(0.13f, 0.09f, 0.07f);

    /// <summary>Shows its own cooked color - browned and charred where it is -
    /// with its sauce over it.</summary>
    /// <summary>썰린 채소 조각처럼 조각마다 제 색(대파 흰·초록...)이 있는 것: 한 색으로
    /// 덮지 않고, 제 재질 색에 익은 정도(raw → cooked 비율)를 곱해 칠한다.</summary>
    public bool UseOwnColors { get; set; }

    public void Show(Color cooked, Color raw)
    {
        if (!UseOwnColors) { Show(cooked); return; }
        if (rend == null) rend = GetComponent<Renderer>();
        if (rend == null) return;
        Material[] mats = rend.sharedMaterials;
        Color ratio = new Color(cooked.r / Mathf.Max(raw.r, 0.02f), cooked.g / Mathf.Max(raw.g, 0.02f), cooked.b / Mathf.Max(raw.b, 0.02f), 1f);
        float s = Mathf.Lerp(0.2f, 0.85f, Coat * Mathf.Lerp(0.35f, 1f, CoatViscosity));
        block ??= new MaterialPropertyBlock();
        for (int i = 0; i < mats.Length; i++)
        {
            if (mats[i] == null) continue;
            Color own = mats[i].HasProperty("_BaseColor") ? mats[i].GetColor("_BaseColor") : mats[i].color;
            Color c = new Color(Mathf.Clamp01(own.r * ratio.r), Mathf.Clamp01(own.g * ratio.g), Mathf.Clamp01(own.b * ratio.b), own.a);
            c = Color.Lerp(c, SearBrown, Sear * 0.8f);
            c = Color.Lerp(c, CharBlack, Char);
            c = Color.Lerp(c, CoatColor, Coat * 0.85f);
            block.Clear();
            block.SetColor("_BaseColor", c);
            block.SetColor("_Color", c);
            block.SetFloat("_Smoothness", s);
            rend.SetPropertyBlock(block, i);
        }
    }

    public void Show(Color cooked)
    {
        if (rend == null) rend = GetComponent<Renderer>();
        if (rend == null) return;
        cooked = Color.Lerp(cooked, SearBrown, Sear * 0.8f);
        cooked = Color.Lerp(cooked, CharBlack, Char);
        Color c = Color.Lerp(cooked, CoatColor, Coat * 0.85f);
        float s = Mathf.Lerp(0.2f, 0.85f, Coat * Mathf.Lerp(0.35f, 1f, CoatViscosity));
        if (Mathf.Abs(s - shownSmooth) < 0.005f && Approximately(c, shownColor)) return;
        shownColor = c;
        shownSmooth = s;
        block ??= new MaterialPropertyBlock();
        rend.GetPropertyBlock(block);
        block.SetColor("_BaseColor", c);
        block.SetColor("_Color", c);
        block.SetFloat("_Smoothness", s);
        rend.SetPropertyBlock(block);
    }

    private static bool Approximately(Color a, Color b) =>
        Mathf.Abs(a.r - b.r) < 0.004f && Mathf.Abs(a.g - b.g) < 0.004f && Mathf.Abs(a.b - b.b) < 0.004f;
}

/// <summary>
/// The pieces of one cut ingredient (on its IngredientItem, which stays the one
/// thing that's carried, put in containers, thrown away...). Two ways they move:
/// <list type="bullet">
/// <item>Together - carried, on the board, in the strainer, on the knife: each
/// keeps its place relative to the ingredient.</item>
/// <item>Each on its own - in a pot, pan, bowl or on a plate: a small bounded
/// simulation inside the container's round body (MovableBowl.GetInterior):
/// gravity along the counter's up, buoyancy and the boil's flow in water, the
/// container's own tilt and movement, the spatula's stir, pieces pushing each
/// other apart, never through the bottom or the wall or out over the rim.</item>
/// </list>
/// Not Unity rigidbodies: the containers ride the hand, tilt with the counter
/// and have solid box colliders, so the inside is computed from each vessel's
/// own shape instead.
/// </summary>
[RequireComponent(typeof(IngredientItem))]
public class FoodPieceGroup : MonoBehaviour
{
    [Header("In water")]
    [Tooltip("Buoyancy against gravity when raw (1 = hovers) and once cooked (more = floats higher).")]
    [SerializeField] private float rawBuoyancy = 1.0f;
    [SerializeField] private float cookedBuoyancy = 1.6f;
    [Tooltip("How hard the boiling water pushes the pieces around.")]
    [SerializeField] private float flowStrength = 0.9f;
    [SerializeField] private float swirl = 0.3f;
    [SerializeField] private float waterDrag = 3.5f;

    [Header("In a pan / on a plate")]
    [SerializeField] private float floorDrag = 6f;
    [SerializeField] private float stirImpulse = 0.8f;
    [Tooltip("Sauce taken on per stir by a piece touching it (times how much sauce there is).")]
    [SerializeField] private float coatPerStir = 0.14f;
    [Tooltip("How much a soft piece (두부) breaks up per spatula stroke (look tuning).")]
    [SerializeField] private float softDamagePerStir = 0.035f;
    [Tooltip("Sauce taken on while sliding through it, per unit of speed per second.")]
    [SerializeField] private float coatPerMove = 0.3f;

    [Header("Pan cooking (a dry pan, a wok, oil) - look tuning, not measured")]
    [Tooltip("x: the piece's temperature °C → y: cooking per second (1 = one second's worth of TimeToCooked at a good frying heat).")]
    [SerializeField] private AnimationCurve cookRateByTemp = new AnimationCurve(
        new Keyframe(45f, 0f), new Keyframe(80f, 0.35f), new Keyframe(110f, 0.7f), new Keyframe(160f, 1.1f), new Keyframe(220f, 1.5f));
    [Tooltip("x: °C where it lies on the pan → y: browning per second (only without a watery sauce round it).")]
    [SerializeField] private AnimationCurve searRateByTemp = new AnimationCurve(
        new Keyframe(140f, 0f), new Keyframe(170f, 0.03f), new Keyframe(200f, 0.07f), new Keyframe(240f, 0.12f));
    [Tooltip("x: °C where it lies on the pan → y: charring per second.")]
    [SerializeField] private AnimationCurve charRateByTemp = new AnimationCurve(
        new Keyframe(215f, 0f), new Keyframe(240f, 0.012f), new Keyframe(280f, 0.04f), new Keyframe(330f, 0.09f));
    [Tooltip("A piece on top of the others gets this much of the pan's heat (above room temperature).")]
    [SerializeField] private float offFloorHeat = 0.35f;
    [Tooltip("Seconds-scale for a piece's own temperature to follow its surroundings: this + per world unit of its radius (a 1 cm cube ≈ 5 s, a 3 cm one ≈ 13 s).")]
    [SerializeField] private float pieceTauBase = 2f, pieceTauPerRadius = 70f;
    [Tooltip("Cooked colour of meat (pork goes pale; brown comes from browning, not cooking).")]
    [SerializeField] private Color cookedMeatColor = new Color(0.86f, 0.78f, 0.7f);

    [Header("튀김 기름 속 (뜨기 · 붙기 · 타기) - 모양 조정값, 측정값 아님")]
    [Tooltip("익은 정도(TimeToCooked 대비)가 이만큼 되면 바닥에서 떠오른다 (뜨거운 기름에서만).")]
    [SerializeField] private float oilFloatAt = 0.45f;
    [Tooltip("떠오른 조각의 부력 (중력 대비, 1보다 크면 표면으로 올라옴).")]
    [SerializeField] private float oilBuoyancy = 1.7f;
    [SerializeField] private float oilDrag = 4.5f;
    [Tooltip("뜨거운 기름이 조각을 흔드는 세기 (기포).")]
    [SerializeField] private float oilBubble = 0.5f;
    [Tooltip("튀김옷 입힌 조각이 맞닿으면 1초에 붙을 확률 (넣은 직후, 아직 굳기 전).")]
    [SerializeField] private float stickPerSecond = 0.9f;
    [Tooltip("익은 정도가 이만큼 되면 튀김옷이 굳는다: 그때까지 붙어 있던 조각은 영영 붙은 채로.")]
    [SerializeField] private float stickSetAt = 0.35f;
    [Tooltip("젓가락·국자로 건드릴 때 밀어내는 세기.")]
    [SerializeField] private float pokeImpulse = 1.4f;
    [Tooltip("다 익은 뒤에도 (TimeToCooked의 이 배수를 넘겨) 기름에 두면 타기 시작한다.")]
    [SerializeField] private float overfryAfter = 1.6f;
    [Tooltip("과하게 튀길 때 1초에 타는 정도 (뜨거운 기름 기준, 0.6이면 '탐').")]
    [SerializeField] private float oilCharPerSecond = 0.025f;

    [Header("General")]
    [SerializeField] private float gravity = 3f;
    [Tooltip("How much the container's own movement (carried, shaken) throws the pieces about.")]
    [SerializeField] private float inertia = 0.2f;

    public readonly List<FoodPiece> Pieces = new List<FoodPiece>();

    /// <summary>The sauce on the pieces (taken from the pan's sauce as they pick
    /// it up) - the real amount, held once here for the whole ingredient; each
    /// piece's Coat is only how it looks. Pan sauce + this = all of it.</summary>
    private readonly SaucePortion coatSauce = new SaucePortion();
    public float CoatSauceMl => coatSauce.Amount * 15f;

    /// <summary>A share of the sauce on the pieces moved to another ingredient's
    /// (a piece split off with its coat) - moved, not copied.</summary>
    public void GiveCoatShareTo(FoodPieceGroup other, float share)
    {
        if (other == null || coatSauce.Amount <= 0f) return;
        other.coatSauce.Add(coatSauce.Take(coatSauce.Amount * Mathf.Clamp01(share)));
    }
    public IReadOnlyDictionary<string, float> CoatSauceParts => coatSauce.parts;

    private IngredientItem ingredient;
    private CookableItem cookable;
    private MovableBowl simBowl;
    private MovableBowl.Interior inside;
    private bool external;
    private Vector3 lastCenter, lastCenterVel;
    private bool haveLast;

    public int Count => Pieces.Count;
    public MovableBowl Container => simBowl;

    void Awake()
    {
        ingredient = GetComponent<IngredientItem>();
        cookable = GetComponent<CookableItem>();
    }

    public void Add(FoodPiece p)
    {
        if (p != null && !Pieces.Contains(p)) Pieces.Add(p);
    }

    /// <summary>The least-cooked piece's cooking time (the whole is done when it is).</summary>
    public float MinCook
    {
        get
        {
            float m = float.MaxValue;
            foreach (FoodPiece p in Pieces) if (p != null) m = Mathf.Min(m, p.Cook);
            return m == float.MaxValue ? 0f : m;
        }
    }

    /// <summary>How much of the heat reaches this piece: all of it under the
    /// boiling water, less riding up above it.</summary>
    public float HeatFactor(FoodPiece p)
    {
        if (simBowl == null || !inside.liquid) return 1f;
        return p.simPos.y - p.Radius * 0.3f < inside.surface ? 1f : 0.55f;
    }

    /// <summary>Someone else moves the pieces for now (FryingPanPlating's pour).</summary>
    public void SetExternallyMoved(bool on)
    {
        external = on;
        if (on && simBowl != null) { simBowl = null; haveLast = false; }
    }

    void LateUpdate()
    {
        Pieces.RemoveAll(p => p == null);
        MovableBowl bowl = null;
        // (in the strainer - even one resting across a bowl - they ride together in the basket)
        if (!external && !ingredient.IsHeld && transform.parent != null && transform.parent.GetComponentInParent<Strainer>() == null)
            bowl = transform.parent.GetComponentInParent<MovableBowl>();
        if (bowl != simBowl)
        {
            simBowl = bowl;
            if (bowl != null) EnterContainer();
        }
        if (simBowl != null) Step(Mathf.Min(Time.deltaTime, 0.05f));

        Color rawRef = cookable != null ? cookable.ColorAt(0f) : Color.white;
        foreach (FoodPiece p in Pieces)
            p.Show(p.PanCooked ? PanColor(p) : cookable != null ? cookable.ColorAt(p.Cook) : Color.white, rawRef);
    }

    /// <summary>In a pan/wok/oil (not in water): there's a pan's heat to cook by.</summary>
    public bool InDryHeat => simBowl != null && simBowl.IsCookingVessel && !inside.liquid;
    public bool InLiquid => simBowl != null && inside.liquid;

    public float AverageChar
    {
        get { float s = 0f; foreach (FoodPiece p in Pieces) if (p != null) s += p.Char; return Pieces.Count > 0 ? s / Pieces.Count : 0f; }
    }

    /// <summary>The colour cooking in a pan gives it (before browning): meat
    /// goes pale, a vegetable a little deeper, tofu stays as it is (it browns).</summary>
    private Color PanColor(FoodPiece p)
    {
        Color raw = cookable != null ? cookable.ColorAt(0f) : Color.white;
        float k = Mathf.Clamp01(p.Cook / Mathf.Max(cookable != null ? cookable.TimeToCooked : 6f, 0.01f));
        Color done = cookedMeatColor;
        if (cookable != null)
        {
            cookable.PanLook(raw, out done, out Color sear, out Color burnt);
            p.SearBrown = sear;
            p.CharBlack = burnt;
        }
        return Color.Lerp(raw, done, k);
    }

    /// <summary>Each piece heated by where it is: lying on the hot pan, the
    /// pan's own heat (browning, and charring if it's left there); up on top of
    /// the others, much less; in a watery sauce, no hotter than it boils;
    /// in the frying oil, the oil's. Stirring and shaking move them about,
    /// so they cook evenly - left still, the bottom ones brown, then burn.</summary>
    private void CookInPan(float dt)
    {
        PanSauce sauce = simBowl.GetComponent<PanSauce>();
        // (소스가 졸아붙어 타는 중이면 물기 소스로 치지 않는다 - 재료도 바닥에 눌어 탄다)
        bool watery = sauce != null && sauce.IsWatery && sauce.Amount > 0.5f && sauce.BurnRate <= 0f;
        float charring = 0f;
        float pan, room = 20f;
        bool oil = simBowl.HasFryingOil;
        if (oil) pan = Mathf.Lerp(room, 175f, simBowl.OilHeat01);
        else if (VesselHeat.Of(simBowl) is VesselHeat vh) { pan = vh.PanTemp; room = vh.RoomTemp; }
        else return;
        foreach (FoodPiece p in Pieces)
        {
            bool onFloor = p.simPos.y <= p.Radius * 1.25f;
            float t = oil ? pan                                   // in the oil: all round
                    : onFloor ? pan : room + (pan - room) * offFloorHeat;
            if (watery) t = Mathf.Min(t, onFloor ? 105f : 100f);  // (in the sauce: as hot as it boils)
            // The piece's own temperature follows what's around it - a big piece
            // slower than a small one; it cooks by its own temperature, browns
            // and chars by its surface (between its own and what it lies on).
            p.Temp += (t - p.Temp) * Mathf.Clamp01(dt / PieceTau(p));
            float surface = Mathf.Lerp(p.Temp, t, 0.7f);
            p.Cook += dt * Mathf.Max(0f, cookRateByTemp.Evaluate(p.Temp));
            if (p.Cook > 0f) p.PanCooked = true;
            if (!watery && (onFloor || oil))
            {
                p.Sear = Mathf.Min(1f, p.Sear + dt * Mathf.Max(0f, searRateByTemp.Evaluate(surface)));
                if (!oil && !SpeedMode.On)
                {
                    float cr = Mathf.Max(0f, charRateByTemp.Evaluate(surface));
                    // 소스가 바닥에 눌어붙어 타는 중이면, 바닥에 닿은 조각도 같이 탄다
                    if (sauce != null && sauce.BurnRate > 0f && onFloor) cr += sauce.BurnRate * 0.9f;
                    if (p.Char < 1f) charring += cr;
                    p.Char = Mathf.Min(1f, p.Char + dt * cr);
                }
            }
            // 튀김 기름에 너무 오래: 다 익고도 계속 두면 타 들어간다 (기름이 뜨거울수록 빨리)
            if (oil && !SpeedMode.On && p.Cook > TimeToCooked * overfryAfter)
            {
                if (p.Char < 1f) charring += oilCharPerSecond * simBowl.OilHeat01;
                p.Char = Mathf.Min(1f, p.Char + dt * oilCharPerSecond * simBowl.OilHeat01);
            }
        }
        // 타 들어가는 조각이 있으면 탄 연기 (많이·빨리 탈수록 진하게)
        if (charring > 0.005f && Pieces.Count > 0)
            BurnSmoke.For(simBowl)?.Emit(Mathf.Clamp01(0.3f + charring / Pieces.Count / 0.04f));
    }

    /// <summary>Seconds-scale for a piece to take on the heat around it: bigger, slower.</summary>
    private float PieceTau(FoodPiece p) => pieceTauBase + pieceTauPerRadius * p.Radius;

    /// <summary>Out of the heat (a plate, a bowl): the pieces cool toward the room.</summary>
    private void CoolOff(float dt)
    {
        foreach (FoodPiece p in Pieces) p.Temp += (20f - p.Temp) * Mathf.Clamp01(dt / 120f);
    }

    // ---- loose, inside a container ----

    private Quaternion Frame(out Vector3 right, out Vector3 back)
    {
        Vector3 up = inside.up;
        right = Vector3.ProjectOnPlane(Vector3.right, up);
        if (right.sqrMagnitude < 1e-4f) right = Vector3.ProjectOnPlane(Vector3.forward, up);
        right.Normalize();
        back = Vector3.Cross(right, up).normalized;
        return Quaternion.LookRotation(back, up);
    }

    private void EnterContainer()
    {
        inside = simBowl.GetInterior();
        // into a hot pan: cold food takes some of its heat
        if (VesselHeat.Of(simBowl) is VesselHeat vh)
        {
            float g = 0f, t = 0f;
            foreach (FoodPiece p in Pieces) { g += Mathf.Max(p.Grams, 1f); t += p.Temp * Mathf.Max(p.Grams, 1f); }
            if (g > 0f) vh.AddColdMass(g, t / g);
        }
        Quaternion frame = Frame(out _, out _);
        Quaternion inv = Quaternion.Inverse(frame);
        foreach (FoodPiece p in Pieces)
        {
            p.simPos = inv * (p.transform.position - inside.floorCenter);
            p.simVel = Vector3.zero;
            p.simSpin = Vector3.zero;
            p.simRot = inv * p.transform.rotation;
        }
        haveLast = false;
    }

    private void Step(float dt)
    {
        if (dt <= 0f || Pieces.Count == 0) return;
        inside = simBowl.GetInterior();
        Quaternion frame = Frame(out _, out _);
        Quaternion inv = Quaternion.Inverse(frame);

        // The container's own movement (carried, shaken) throws them the other way.
        Vector3 accel = Vector3.zero;
        if (haveLast)
        {
            Vector3 vel = (inside.floorCenter - lastCenter) / dt;
            accel = (vel - lastCenterVel) / dt;
            lastCenterVel = vel;
        }
        else lastCenterVel = Vector3.zero;
        lastCenter = inside.floorCenter;
        haveLast = true;
        // (only while it's carried or shaken - a bowl set down or moved in one go
        // doesn't fling everything in it about)
        Vector3 shove = simBowl.IsHeld || simBowl.GetComponent<WokDragHandler>() != null
            ? Vector3.ClampMagnitude(inv * (-accel * inertia), 6f) : Vector3.zero;

        // "Down" is into the counter (the scene's counter is tilted to the camera);
        // a container tilted off it lets the pieces slide.
        Vector3 g = inv * (-Carryable.CounterUp * gravity);
        // 놓여 있는 그릇은 바닥이 평평하다 (팬 모델의 '위'가 조리대 기울기와 조금 달라서,
        // 그대로면 가만히 둬도 조각이 앞쪽 벽 밑으로 미끄러져 가려졌다) - 손에 들고
        // 기울일 때만 기운 쪽으로 미끄러진다
        if (!simBowl.IsHeld) g = new Vector3(0f, -gravity, 0f);

        PanSauce sauce = simBowl.GetComponent<PanSauce>();
        float sauceAvail = sauce != null ? Mathf.Clamp01(sauce.Amount / 3f) : 0f;
        float ttc = cookable != null ? Mathf.Max(cookable.TimeToCooked, 0.01f) : 1f;
        float time = Time.time;
        // 튀김 기름: 표면 높이 (없으면 0), 뜨거운지
        float oilLevel = simBowl.HasFryingOil ? simBowl.OilSurfaceHeight : 0f;
        bool hotOil = oilLevel > 0f && simBowl.IsOilHot;
        if (oilLevel > 0f) FormBonds(dt);

        int steps = Mathf.Clamp(Mathf.CeilToInt(dt / 0.012f), 1, 4);
        float h = dt / steps;
        for (int s = 0; s < steps; s++)
        {
            foreach (FoodPiece p in Pieces)
            {
                Vector3 a = g + shove;
                float drag;
                if (inside.liquid)
                {
                    float sub = Mathf.Clamp01((inside.surface - (p.simPos.y - p.Radius)) / (2f * p.Radius));
                    float buoy = Mathf.Lerp(rawBuoyancy, cookedBuoyancy, Mathf.Clamp01(p.Cook / ttc));
                    a.y += gravity * buoy * sub;
                    if (sub > 0f)
                    {
                        float t = time * 0.6f + p.seed;
                        float boil = inside.boil;
                        a.x += (Mathf.PerlinNoise(t, p.seed) - 0.5f) * 2f * flowStrength * boil;
                        a.z += (Mathf.PerlinNoise(p.seed, t) - 0.5f) * 2f * flowStrength * boil;
                        a.y += (Mathf.PerlinNoise(t + 3.1f, p.seed + 7.7f) - 0.45f) * 1.4f * flowStrength * boil;
                        Vector3 tangent = new Vector3(-p.simPos.z, 0f, p.simPos.x);
                        if (tangent.sqrMagnitude > 1e-6f) a += tangent.normalized * swirl * boil;
                        p.simSpin += new Vector3(Mathf.PerlinNoise(t, 1.3f) - 0.5f, Mathf.PerlinNoise(2.1f, t) - 0.5f, Mathf.PerlinNoise(t, 4.7f) - 0.5f) * (6f * boil * h);
                    }
                    drag = sub > 0f ? waterDrag : 1.5f;
                }
                else if (oilLevel > 0f)
                {
                    // 튀김 기름 속: 넣으면 가라앉았다가, 어느 정도 익으면(뜨거운 기름에서)
                    // 떠올라 표면에 반쯤 잠겨 떠 있다. 식은 기름에선 가라앉은 채.
                    float sub = Mathf.Clamp01((oilLevel - (p.simPos.y - p.Radius)) / (2f * p.Radius));
                    bool floats = hotOil && p.Cook / ttc >= oilFloatAt;
                    if (floats) a.y += gravity * oilBuoyancy * sub;
                    if (hotOil && sub > 0f)
                    {
                        // 기포에 살짝살짝 흔들림
                        float t = time * 1.3f + p.seed;
                        float heat = simBowl.OilHeat01;
                        a.x += (Mathf.PerlinNoise(t, p.seed) - 0.5f) * 2f * oilBubble * heat;
                        a.z += (Mathf.PerlinNoise(p.seed, t) - 0.5f) * 2f * oilBubble * heat;
                        p.simSpin += new Vector3(Mathf.PerlinNoise(t, 1.3f) - 0.5f, 0f, Mathf.PerlinNoise(t, 4.7f) - 0.5f) * (2f * heat * h);
                    }
                    bool onFloor = p.simPos.y <= p.Radius + 0.01f;
                    drag = sub > 0f ? oilDrag : 1.5f;
                    if (onFloor && !floats) drag = Mathf.Max(drag, floorDrag);
                }
                else
                {
                    bool onFloor = p.simPos.y <= p.Radius + 0.01f;
                    drag = onFloor ? floorDrag : 1f;
                }
                p.simVel += a * h;
                p.simVel *= Mathf.Exp(-drag * h);
                p.simPos += p.simVel * h;
                p.simSpin *= Mathf.Exp(-(inside.liquid ? 4f : 8f) * h);
                if (p.simSpin.sqrMagnitude > 1e-6f)
                    p.simRot = Quaternion.AngleAxis(p.simSpin.magnitude * h * Mathf.Rad2Deg, p.simSpin.normalized) * p.simRot;
                // A cube settles back onto a face (keeping which way it's turned).
                Vector3 fwd = Vector3.ProjectOnPlane(p.simRot * Vector3.forward, Vector3.up);
                if (fwd.sqrMagnitude > 1e-4f)
                    p.simRot = Quaternion.Slerp(p.simRot, Quaternion.LookRotation(fwd, Vector3.up), (inside.liquid ? 1.5f : 8f) * h);
                Contain(p);
                // nearly still and nothing moving it: at rest (no endless slow drifting or turning)
                if (!simBowl.IsHeld && !BeingWorked && !hotOil && p.simVel.sqrMagnitude < 0.0004f && p.simSpin.sqrMagnitude < 0.04f && inside.boil < 0.05f)
                {
                    p.simVel = Vector3.zero;
                    p.simSpin = Vector3.zero;
                }
            }
            Separate();
            if (oilLevel > 0f) HoldBonds();
        }

        if (InDryHeat) CookInPan(SpeedMode.Wait(dt));
        else if (!inside.liquid) CoolOff(dt);

        // Sliding through the sauce in a pan picks it up (where it touches - unevenly).
        // Thickened with starch, it clings more and stays on, glossy.
        if (sauce != null && sauceAvail > 0f && !inside.liquid)
        {
            float used = 0f;
            float cling = Mathf.Max(sauce.Viscosity, sauce.Thickness);
            foreach (FoodPiece p in Pieces)
            {
                if (p.simPos.y > p.Radius * 1.6f) continue;
                float take = coatPerMove * p.simVel.magnitude * dt * sauceAvail * Mathf.Lerp(0.6f, 1.6f, cling);
                p.AddCoat(take, sauce.Color, cling);
                used += take;
            }
            coatSauce.Add(sauce.Take(used * 0.15f)); // (moved onto the pieces - not lost, not counted twice)
        }
        foreach (FoodPiece p in Pieces) if (sauce == null || sauceAvail <= 0f) p.DripCoat(dt);

        // Placed back in the world; the ingredient itself sits at their middle.
        Vector3 mid = Vector3.zero;
        foreach (FoodPiece p in Pieces) mid += p.simPos;
        mid /= Pieces.Count;
        transform.position = inside.floorCenter + frame * mid;
        foreach (FoodPiece p in Pieces)
        {
            p.transform.position = inside.floorCenter + frame * p.simPos;
            p.transform.rotation = frame * p.simRot;
        }
    }

    /// <summary>Kept inside: above the bottom, within the wall, below the rim.</summary>
    private void Contain(FoodPiece p)
    {
        float r = p.Radius;
        if (p.simPos.y < r)
        {
            p.simPos.y = r;
            if (p.simVel.y < 0f) p.simVel.y *= -0.1f;
        }
        // (a crowded bowl heaps up a little over its rim - held down in one layer
        // they had no room, kept shoving each other and went round and round)
        float top = Mathf.Max(inside.rim - r, r) + r * 3f;
        if (p.simPos.y > top)
        {
            p.simPos.y = top;
            if (p.simVel.y > 0f) p.simVel.y = 0f;
        }
        Vector2 flat = new Vector2(p.simPos.x, p.simPos.z);
        float wall = Mathf.Max(inside.radius - (flat.sqrMagnitude > 1e-6f ? p.ReachAlong(new Vector3(flat.x, 0f, flat.y).normalized) : r), 0.01f);
        if (flat.magnitude > wall)
        {
            Vector2 n = flat.normalized;
            flat = n * wall;
            p.simPos.x = flat.x;
            p.simPos.z = flat.y;
            float vn = p.simVel.x * n.x + p.simVel.z * n.y;
            if (vn > 0f)
            {
                p.simVel.x -= 1.2f * vn * n.x;
                p.simVel.z -= 1.2f * vn * n.y;
            }
        }
    }

    /// <summary>Pieces push each other apart (they can rest touching, stacked).</summary>
    private void Separate()
    {
        int n = Pieces.Count;
        for (int i = 0; i < n; i++)
        {
            FoodPiece a = Pieces[i];
            for (int j = i + 1; j < n; j++)
            {
                FoodPiece b = Pieces[j];
                Vector3 d = b.simPos - a.simPos;
                float min = (a.Radius + b.Radius) * 0.95f;
                float dist2 = d.sqrMagnitude;
                if (dist2 >= min * min) continue;
                float dist = Mathf.Sqrt(dist2);
                Vector3 nrm = dist > 1e-5f ? d / dist : new Vector3(Random.Range(-1f, 1f), 0.3f, Random.Range(-1f, 1f)).normalized;
                Vector3 push = nrm * ((min - dist) * 0.5f);
                a.simPos -= push;
                b.simPos += push;
                float rel = Vector3.Dot(b.simVel - a.simVel, nrm);
                if (rel < 0f)
                {
                    Vector3 imp = nrm * (rel * 0.5f);
                    a.simVel += imp;
                    b.simVel -= imp;
                }
                // pieces pressed together rub: drifting slowly, they slow each other
                // down (no endless sliding round) - stirred or shaken, they fly on
                if (!BeingWorked && a.simVel.sqrMagnitude < 0.0225f && b.simVel.sqrMagnitude < 0.0225f)
                {
                    a.simVel *= 0.9f;
                    b.simVel *= 0.9f;
                    a.simSpin *= 0.9f;
                    b.simSpin *= 0.9f;
                }
            }
        }
        // 튀김 기름: 한 조각씩 따로 넣은 다른 재료(다른 묶음)의 조각과도 서로 밀어낸다
        // (각자 반씩 - 저쪽도 같은 계산을 한다)
        if (simBowl != null && simBowl.HasFryingOil)
            foreach (FoodPieceGroup other in Neighbors())
                foreach (FoodPiece b in other.Pieces)
                {
                    if (b == null) continue;
                    foreach (FoodPiece a in Pieces)
                    {
                        Vector3 d = b.simPos - a.simPos;
                        float min = (a.Radius + b.Radius) * 0.95f;
                        if (d.sqrMagnitude >= min * min) continue;
                        float dist = d.magnitude;
                        Vector3 nrm = dist > 1e-5f ? d / dist : new Vector3(Random.Range(-1f, 1f), 0.3f, Random.Range(-1f, 1f)).normalized;
                        a.simPos -= nrm * ((min - dist) * 0.5f);
                        float rel = Vector3.Dot(b.simVel - a.simVel, nrm);
                        if (rel < 0f) a.simVel += nrm * (rel * 0.5f);
                    }
                }
        foreach (FoodPiece p in Pieces) Contain(p);
    }

    /// <summary>같은 그릇 안의 다른 조각 묶음들 (한 프레임에 한 번만 찾는다).</summary>
    private List<FoodPieceGroup> Neighbors()
    {
        if (neighborsFrame != Time.frameCount)
        {
            neighborsFrame = Time.frameCount;
            neighbors.Clear();
            if (simBowl != null)
                foreach (FoodPieceGroup g in simBowl.GetComponentsInChildren<FoodPieceGroup>())
                    if (g != this && g.simBowl == simBowl && g.Count > 0) neighbors.Add(g);
        }
        return neighbors;
    }
    private readonly List<FoodPieceGroup> neighbors = new List<FoodPieceGroup>();
    private int neighborsFrame = -1;

    /// <summary>One stroke of the spatula through the pan: the pieces are
    /// pushed round and turned over, and the ones touching the sauce take some on.</summary>
    public void Stir()
    {
        if (simBowl == null) return;
        PanSauce sauce = simBowl.GetComponent<PanSauce>();
        float avail = sauce != null ? Mathf.Clamp01(sauce.Amount / 3f) : 0f;
        float dir = Random.value < 0.5f ? -1f : 1f;
        float used = 0f;
        foreach (FoodPiece p in Pieces)
        {
            Vector3 tangent = new Vector3(-p.simPos.z, 0f, p.simPos.x);
            tangent = tangent.sqrMagnitude > 1e-6f ? tangent.normalized * dir : Vector3.right;
            p.simVel += tangent * stirImpulse * Random.Range(0.5f, 1.2f)
                      + new Vector3(Random.Range(-0.3f, 0.3f), Random.Range(0.4f, 1f), Random.Range(-0.3f, 0.3f)) * stirImpulse;
            p.simSpin += Random.onUnitSphere * Random.Range(4f, 10f);
            // a soft piece (두부) wears with every stroke - more once it's hot and soft
            if (p.SourceName == "두부")
                p.AddDamage(softDamagePerStir * Random.Range(0.6f, 1.4f) * (p.Cook > 0f ? 1.3f : 1f));
            if (avail > 0f)
            {
                // the ones at the bottom, in the sauce, get most; the rest a little
                float touch = p.simPos.y <= p.Radius * 1.6f ? 1f : 0.3f;
                float take = coatPerStir * avail * touch * Random.Range(0.4f, 1f);
                p.AddCoat(take * Mathf.Lerp(0.6f, 1.6f, Mathf.Max(sauce.Viscosity, sauce.Thickness)), sauce.Color, Mathf.Max(sauce.Viscosity, sauce.Thickness));
                used += take;
            }
        }
        if (sauce != null) coatSauce.Add(sauce.Take(used * 0.15f));
        // (튀김 기름에서 휘저으면 아직 안 굳은 붙음은 반쯤 떨어진다)
        if (simBowl.HasFryingOil) bonds.RemoveAll(x => !x.set && Random.value < 0.5f);
    }

    // ---- 튀김 기름 속: 서로 붙기 · 떼기 ----

    private float TimeToCooked => cookable != null ? Mathf.Max(cookable.TimeToCooked, 0.01f) : 6f;

    /// <summary>맞닿아 붙은 두 조각. set: 튀김옷이 굳어 더는 뗄 수 없음.</summary>
    private sealed class Bond { public FoodPiece a, b; public bool set; }
    private readonly List<Bond> bonds = new List<Bond>();
    private bool stuckReported;

    /// <summary>지금 서로 붙어 있는 쌍 수 (굳은 것 포함).</summary>
    public int StuckPairs => bonds.Count;

    [Tooltip("젓가락으로 건드린 조각은 이 시간(초) 동안 다시 붙지 않는다.")]
    [SerializeField] private float pokeLooseSeconds = 1.5f;
    // (모든 묶음이 같이 본다 - 다른 묶음 조각과 붙을지 볼 때도)
    private static readonly Dictionary<FoodPiece, float> pokedAt = new Dictionary<FoodPiece, float>();
    private bool RecentlyPoked(FoodPiece p) => p != null && pokedAt.TryGetValue(p, out float t) && Time.time - t < pokeLooseSeconds;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetPokes() => pokedAt.Clear();

    private bool Bonded(FoodPiece a, FoodPiece b)
    {
        foreach (Bond x in bonds) if ((x.a == a && x.b == b) || (x.a == b && x.b == a)) return true;
        return false;
    }

    /// <summary>이 쌍이 이미 다른 묶음 쪽 목록에 붙어 있는지 (두 번 붙지 않게).</summary>
    private bool OthersHold(FoodPiece a, FoodPiece b)
    {
        foreach (FoodPieceGroup g in Neighbors()) if (g.Bonded(a, b)) return true;
        return false;
    }

    /// <summary>튀김옷 입힌 조각을 기름에 넣은 직후(아직 안 굳었을 때) 맞닿아 있으면
    /// 붙는다. 둘 다 stickSetAt만큼 익으면 굳어서 영영 붙은 채 - 한 번 기록.</summary>
    private void FormBonds(float dt)
    {
        bonds.RemoveAll(x => x.a == null || x.b == null);
        bool coated = ingredient != null && BatterCoating.Of(ingredient, false) is BatterCoating bc && bc.HasAny;
        float ttc = TimeToCooked;
        if (coated)
        {
            // 짝 후보: 이 묶음 안의 조각끼리 + 따로 넣은 다른 묶음의 조각 (그쪽도 튀김옷이
            // 있을 때, 한 쌍을 두 번 세지 않게 InstanceID가 작은 쪽 묶음만)
            var all = new List<FoodPiece>(Pieces);
            int own = all.Count;
            foreach (FoodPieceGroup other in Neighbors())
                if (other.GetInstanceID() > GetInstanceID() && other.ingredient != null
                    && BatterCoating.Of(other.ingredient, false) is BatterCoating obc && obc.HasAny)
                    all.AddRange(other.Pieces);
            for (int i = 0; i < own; i++)
                for (int j = i + 1; j < all.Count; j++)
                {
                    FoodPiece a = all[i], b = all[j];
                    if (a == null || b == null || OthersHold(a, b)) continue;
                    float set = Mathf.Max(a.Cook, b.Cook) / ttc / stickSetAt;
                    if (set >= 1f) continue; // (이미 굳어 가는 건 새로 안 붙음)
                    // (방금 젓가락으로 떼어 놓은 조각은 잠시 안 붙는다)
                    if (RecentlyPoked(a) || RecentlyPoked(b)) continue;
                    // 익어 갈수록(겉이 굳어 갈수록) 잘 안 붙는다
                    float chance = 1f - Mathf.Exp(-stickPerSecond * (1f - set) * dt);
                    float touch = (a.Radius + b.Radius) * 1.05f;
                    if ((b.simPos - a.simPos).sqrMagnitude > touch * touch || Bonded(a, b)) continue;
                    if (Random.value < chance) bonds.Add(new Bond { a = a, b = b });
                }
        }
        int newlySet = 0;
        foreach (Bond x in bonds)
            if (!x.set && x.a.Cook / ttc >= stickSetAt && x.b.Cook / ttc >= stickSetAt) { x.set = true; newlySet++; }
        if (newlySet > 0 && !stuckReported)
        {
            stuckReported = true;
            string what = ingredient != null ? ingredient.IngredientName : name;
            ExamDeductions.Add(ExamDeductions.Process, $"{what} 튀김: 기름에서 서로 붙은 채 굳음 (젓가락으로 떼지 않음)");
            Debug.Log($"[튀김] {what}: 붙은 채 굳음 - 이제 뗄 수 없어요");
        }
    }

    /// <summary>붙은 두 조각은 맞닿은 채 같이 움직인다.</summary>
    private void HoldBonds()
    {
        foreach (Bond x in bonds)
        {
            if (x.a == null || x.b == null) continue;
            Vector3 d = x.b.simPos - x.a.simPos;
            float dist = d.magnitude, want = (x.a.Radius + x.b.Radius) * 0.95f;
            if (dist > 1e-5f && dist > want)
            {
                Vector3 corr = d / dist * ((dist - want) * 0.5f);
                x.a.simPos += corr;
                x.b.simPos -= corr;
            }
            Vector3 avg = (x.a.simVel + x.b.simVel) * 0.5f;
            x.a.simVel = Vector3.Lerp(x.a.simVel, avg, 0.6f);
            x.b.simVel = Vector3.Lerp(x.b.simVel, avg, 0.6f);
        }
    }

    /// <summary>튀김젓가락·국자로 건드림 (world: 도구 끝): 가까운 조각을 밀어내고,
    /// 아직 굳지 않은 붙음은 떼어 놓는다 (굳은 건 같이 밀릴 뿐).</summary>
    public void Poke(Vector3 world, float radius)
    {
        if (simBowl == null || Pieces.Count == 0) return;
        workedUntil = Time.time + 0.4f;
        Quaternion inv = Quaternion.Inverse(Frame(out _, out _));
        Vector3 at = inv * (world - inside.floorCenter);
        int freed = 0;
        // 다른 묶음의 조각과 붙은 것도: 둘 중 하나라도 건드린 자리에 있으면 뗀다
        bool Near(FoodPiece p) { Vector3 d = p.simPos - at; d.y = 0f; return d.magnitude < radius; }
        freed += bonds.RemoveAll(x => !x.set && x.a != null && x.b != null && (Near(x.a) || Near(x.b)));
        foreach (FoodPiece p in Pieces)
        {
            Vector3 d = p.simPos - at; d.y = 0f;
            float near = 1f - d.magnitude / Mathf.Max(radius, 0.01f);
            if (near <= 0f) continue;
            Vector3 push = d.sqrMagnitude > 1e-6f ? d.normalized : Random.onUnitSphere;
            push.y = Mathf.Abs(push.y) * 0.3f;
            p.simVel = Vector3.ClampMagnitude(p.simVel + push * (pokeImpulse * near), 1.5f);
            p.simSpin += Random.onUnitSphere * (6f * near);
            pokedAt[p] = Time.time;
            freed += bonds.RemoveAll(x => !x.set && (x.a == p || x.b == p));
        }
        if (freed > 0) Debug.Log($"[튀김] 젓가락으로 붙은 조각 {freed}쌍 떼어냄 (남은 붙음 {bonds.Count})");
    }

    /// <summary>One moment of a hand working the meat in the bowl (MeatKneader):
    /// the pieces near the hand are drawn in toward where it closes, pushed up
    /// over each other and turned, each its own way; with lift, carried up with
    /// the hand and turned over, to tumble back when it lets go. Nudged hard
    /// enough to see, never flung - the bowl's walls still hold them.</summary>
    public void KneadAt(Vector3 handWorld, Vector3 towardWorld, float reach, float strength, float lift = 0f)
    {
        if (simBowl == null) return;
        workedUntil = Time.time + 0.5f;
        Quaternion inv = Quaternion.Inverse(Frame(out _, out _));
        Vector3 hand = inv * (handWorld - inside.floorCenter);
        Vector3 to = inv * (towardWorld - inside.floorCenter);
        foreach (FoodPiece p in Pieces)
        {
            Vector3 d = p.simPos - hand; d.y = 0f;
            float near = 1f - d.magnitude / Mathf.Max(reach, 0.01f);
            if (near <= 0f) continue;
            Vector3 pull = to - p.simPos; pull.y = 0f;
            Vector3 v = Vector3.ClampMagnitude(pull * 4f, 1f) * (strength * near);
            // (squeezed up over the others / lifted with the hand - but never above the
            // hand itself: it's in the hand, not riding on top of it)
            float roof = hand.y - p.Radius;
            if (p.simPos.y < roof) v.y += (0.6f + 1.4f * lift) * strength * near * Random.Range(0.5f, 1f);
            else v.y = Mathf.Min(v.y, -0.3f * strength * near);
            p.simVel = Vector3.ClampMagnitude(p.simVel + v, 1.2f);
            p.simSpin += Random.onUnitSphere * ((8f + 10f * lift) * strength * near);
        }
    }

    /// <summary>Being worked by hand right now (kneaded): left moving - not
    /// slowed by rubbing or put to rest.</summary>
    public bool BeingWorked => Time.time < workedUntil;
    private float workedUntil;

    // ---- together ----

    /// <summary>Gathers pieces spread wider than radius (world) into a loose heap
    /// round their middle (scooped into the strainer, onto a towel, into a
    /// smaller dish) - from here they move together again until a container
    /// takes them.</summary>
    public void Gather(float radius)
    {
        if (Pieces.Count < 2) return;
        simBowl = null;
        haveLast = false;
        Bounds all = default;
        bool any = false;
        foreach (FoodPiece p in Pieces)
        {
            if (!p.TryGetComponent(out Renderer r)) continue;
            if (!any) { all = r.bounds; any = true; } else all.Encapsulate(r.bounds);
        }
        if (!any || Mathf.Max(all.extents.x, all.extents.y) <= radius) return;

        Vector3 up = Carryable.CounterUp, right = Vector3.right, back = Vector3.Cross(right, up).normalized;
        float spread = Mathf.Max(radius * 0.8f, 0.05f);
        for (int k = 0; k < Pieces.Count; k++)
        {
            float r = spread * Mathf.Sqrt((k + 0.5f) / Pieces.Count);
            float a = k * 2.39996f;
            Pieces[k].transform.position = all.center + right * (Mathf.Cos(a) * r) + back * (Mathf.Sin(a) * r)
                                         + up * ((1f - r / spread) * 0.04f + Random.Range(0f, 0.02f));
        }
    }
}

/// <summary>
/// A measure of sauce on the move - poured from a pan, held in a cheesecloth,
/// strained into a bowl: what's in it (spoonfuls per seasoning), how hot it
/// is, and the colour/taste the oil has drawn out of a solid (고춧가루 →
/// 고추기름). Liquids and solids are told apart by their Condiments form
/// (a powder is a solid). Nothing in here is ever made up or lost: Take
/// removes exactly what it hands over.
/// </summary>
public class SaucePortion
{
    public void Clear()
    {
        parts.Clear(); infusion.Clear(); notes.Clear();
        heat = burn = gel = lumps = evaporatedMl = 0f;
        temp = 20f;
    }
    public readonly Dictionary<string, float> parts = new Dictionary<string, float>();
    /// <summary>Per solid: how much of its colour and taste is in the liquid (0..1).</summary>
    public readonly Dictionary<string, float> infusion = new Dictionary<string, float>();
    public float heat;
    /// <summary>How burnt the liquid is (0..1) - a solid fried in it too long
    /// (고춧가루). It stays with the liquid wherever it goes, strained or not.</summary>
    public float burn;
    /// <summary>How much of its starch has thickened (0..1), and its clumps (0..1) - go with it.</summary>
    public float gel, lumps;
    /// <summary>Its temperature (°C) - heat (0..1) follows it (100 °C = 1, where water boils).</summary>
    public float temp = 20f;
    /// <summary>Water boiled off from it on the way (mL) - its share goes with it.</summary>
    public float evaporatedMl;
    /// <summary>What happened to it (e.g. why it clumped) - for the final evaluation.</summary>
    public readonly HashSet<string> notes = new HashSet<string>();

    public float Amount { get { float a = 0f; foreach (float v in parts.Values) a += v; return a; } }
    public float LiquidAmount => Sum(false);
    public float SolidAmount => Sum(true);

    public static bool IsSolid(string seasoning) => Condiments.Get(seasoning).form == CondimentForm.Powder;

    private float Sum(bool solid)
    {
        float a = 0f;
        foreach (var kv in parts) if (IsSolid(kv.Key) == solid) a += kv.Value;
        return a;
    }

    /// <summary>Takes up to this much out (of everything, or of the liquid only),
    /// in proportion; what's taken keeps the heat and the infusion.</summary>
    public SaucePortion Take(float spoons, bool liquidOnly = false)
    {
        var p = new SaucePortion { heat = heat, burn = burn, gel = gel, lumps = lumps, temp = temp };
        foreach (var kv in infusion) p.infusion[kv.Key] = kv.Value;
        p.notes.UnionWith(notes);
        float pool = liquidOnly ? LiquidAmount : Amount;
        if (spoons <= 0f || pool <= 0f) return p;
        float k = Mathf.Min(1f, spoons / pool);
        p.evaporatedMl = evaporatedMl * k;
        evaporatedMl -= p.evaporatedMl;
        foreach (string key in new List<string>(parts.Keys))
        {
            if (liquidOnly && IsSolid(key)) continue;
            float take = parts[key] * k;
            p.parts[key] = take;
            parts[key] -= take;
            if (parts[key] <= 1e-5f) parts.Remove(key);
        }
        if (Amount <= 1e-5f) Clear();
        return p;
    }

    /// <summary>Mixed in: the parts added; heat by amount and infusion by the
    /// liquid it's in, averaged (strained 고추기름 stays 고추기름).</summary>
    public void Add(SaucePortion p)
    {
        if (p == null || p.Amount <= 0f) return;
        float a = Amount, b = p.Amount, la = LiquidAmount, lb = p.LiquidAmount;
        heat = a + b > 0f ? (heat * a + p.heat * b) / (a + b) : p.heat;
        burn = la + lb > 0f ? (burn * la + p.burn * lb) / (la + lb) : Mathf.Max(burn, p.burn);
        gel = a + b > 0f ? (gel * a + p.gel * b) / (a + b) : p.gel;
        temp = a + b > 0f ? (temp * a + p.temp * b) / (a + b) : p.temp;
        lumps = a + b > 0f ? (lumps * a + p.lumps * b) / (a + b) : p.lumps;
        evaporatedMl += p.evaporatedMl;
        notes.UnionWith(p.notes);
        var keys = new HashSet<string>(infusion.Keys);
        keys.UnionWith(p.infusion.Keys);
        foreach (string s in keys)
        {
            infusion.TryGetValue(s, out float x);
            p.infusion.TryGetValue(s, out float y);
            infusion[s] = la + lb > 0f ? (x * la + y * lb) / (la + lb) : Mathf.Max(x, y);
        }
        foreach (var kv in p.parts)
        {
            parts.TryGetValue(kv.Key, out float had);
            parts[kv.Key] = had + kv.Value;
        }
    }

    /// <summary>Just the solids of it, taken out (what a cloth keeps back).</summary>
    public SaucePortion TakeSolids()
    {
        var p = new SaucePortion { heat = heat, burn = burn, gel = gel, lumps = lumps, temp = temp };
        p.notes.UnionWith(notes);
        foreach (string key in new List<string>(parts.Keys))
            if (IsSolid(key)) { p.parts[key] = parts[key]; parts.Remove(key); }
        return p;
    }

    public float InfusionOf(string solid) => infusion.TryGetValue(solid, out float v) ? v : 0f;

    /// <summary>The liquid's colour: its own liquids', taking on the colour of
    /// what's been drawn into it (oil gone red with 고춧가루).</summary>
    public Color LiquidColor
    {
        get
        {
            // Water is clear: it doesn't tint the sauce (its blue is only its
            // own look in a bowl) - it thins the colour of what's in it toward
            // a pale broth.
            Color c = Color.black;
            float w = 0f, water = 0f;
            foreach (var kv in parts)
            {
                if (IsSolid(kv.Key)) continue;
                if (kv.Key == Condiments.Water) { water += kv.Value; continue; }
                c += Condiments.Get(kv.Key).color * kv.Value; w += kv.Value;
            }
            Color pale = new Color(0.93f, 0.9f, 0.84f);
            Color liquid = w > 0f ? c / w : water > 0f ? pale : new Color(0.95f, 0.82f, 0.35f);
            if (w > 0f && water > 0f)
            {
                // diluted: strongly coloured liquids (soy) still show through a lot of water
                float share = water / (water + w);
                liquid = Color.Lerp(liquid, pale, share * 0.75f);
            }
            foreach (var kv in infusion)
                liquid = Color.Lerp(liquid, Condiments.Get(kv.Key).color * 0.95f, kv.Value * 0.85f);
            liquid = Color.Lerp(liquid, new Color(0.2f, 0.09f, 0.04f), burn * 0.8f); // burnt: dark
            liquid.a = 1f;
            return liquid;
        }
    }

    /// <summary>What a spoonful of the liquid is called: 고추기름 once the oil
    /// has taken on the chilli, else its main liquid.</summary>
    public string LiquidName
    {
        get
        {
            if (InfusionOf("고춧가루") >= 0.3f && parts.ContainsKey("식용유")) return burn >= 0.5f ? "탄 고추기름" : "고추기름";
            string best = null;
            float most = 0f;
            foreach (var kv in parts) if (!IsSolid(kv.Key) && kv.Value > most) { most = kv.Value; best = kv.Key; }
            return best;
        }
    }
}

/// <summary>
/// The sauce in a pan (or, plated, on a plate; strained, in a bowl): which
/// seasonings went in and how much (a SaucePortion: liquids and solids told
/// apart), its colour and thickness, how hot it is, shown as a pool on the
/// bottom - in a deep bowl, as the liquid's surface at its level. On a lit
/// burner it heats, and hot, a solid in the oil gives the oil its colour and
/// taste (고춧가루 → 고추기름). Pieces stirred through it take it on
/// (FoodPieceGroup), using it up.
/// </summary>
public class PanSauce : MonoBehaviour
{
    private readonly SaucePortion sauce = new SaucePortion();
    public IReadOnlyDictionary<string, float> Parts => sauce.parts;

    /// <summary>Spoonfuls left in the pan.</summary>
    public float Amount { get; private set; }
    public Color Color { get; private set; } = Color.white;
    /// <summary>0 thin (soy sauce) .. 1 thick (두반장, 굴소스).</summary>
    public float Viscosity { get; private set; }

    /// <summary>How hot: 0 cold .. 1 as hot as it gets on the flame.</summary>
    public float Heat => sauce.heat;
    public float LiquidAmount { get { Normalize(); return sauce.LiquidAmount; } }
    public float SolidAmount { get { Normalize(); return sauce.SolidAmount; } }
    public float InfusionOf(string solid) => sauce.InfusionOf(solid);
    public Color LiquidColor => sauce.LiquidColor;
    public string LiquidName => sauce.LiquidName;

    // ---- 온도 · 추출 · 탐 (a game model tuned by eye toward the test targets
    // - cold wok, 30 mL oil, 1T 고춧가루: 약불 1~3분, 중불 30~90초, 강불 15~45초
    // to colour, then overheating if it's kept on - not measured cooking data) ----

    [Tooltip("Seconds-scale for what's in the pan to follow the pan's temperature: this + per mL below (30 mL oil ≈ 6 s, 1 L ≈ 2 min).")]
    [SerializeField] private float contentTauBase = 2f;
    [SerializeField] private float contentTauPerMl = 0.13f;
    [Tooltip("Seconds-scale of what's in it losing heat to the air.")]
    [SerializeField] private float contentLossTau = 240f;
    [Tooltip("x: oil temperature °C → y: share of a solid's colour/taste drawn out per second.")]
    [SerializeField] private AnimationCurve extractionPerSecondByTemp = new AnimationCurve(
        new Keyframe(60f, 0f), new Keyframe(90f, 0.012f), new Keyframe(120f, 0.035f), new Keyframe(150f, 0.065f), new Keyframe(180f, 0.1f), new Keyframe(220f, 0.13f));
    [Tooltip("x: the solid's own temperature °C (the oil's, plus the pan's bottom heat where it lies unstirred) → y: burnt per second.")]
    [SerializeField] private AnimationCurve burnPerSecondByTemp = new AnimationCurve(
        new Keyframe(185f, 0f), new Keyframe(205f, 0.006f), new Keyframe(225f, 0.018f), new Keyframe(250f, 0.04f), new Keyframe(280f, 0.09f), new Keyframe(330f, 0.22f));
    [Tooltip("How much of the pan's bottom heat reaches a solid lying still (1) / being stirred.")]
    [SerializeField] private float bottomContactStill = 1f, bottomContactStirred = 0.3f;
    [Tooltip("Extra °C at a solid stuck to the hot bottom unstirred (local hot spot).")]
    [SerializeField] private float stuckHotSpot = 15f;
    [Tooltip("Seconds-scale for the solid itself to take on that temperature.")]
    [SerializeField] private float solidTau = 4f;
    private float solidTemp = 20f;
    [SerializeField] private float smokeAboveBurnRate = 0.02f;
    [Tooltip("물기 소스가 이만큼(큰술) 이하로 적고 물이 거의 없으면, 뜨거운 팬에서 졸아붙어 탄다.")]
    [SerializeField] private float scorchBelowSpoons = 4f;
    [Tooltip("x: 팬 온도 °C → y: 물기 소스가 1초에 타는 정도 (0.5 넘으면 '탐').")]
    [SerializeField] private AnimationCurve scorchPerSecondByPanTemp = new AnimationCurve(
        new Keyframe(130f, 0f), new Keyframe(160f, 0.008f), new Keyframe(200f, 0.025f), new Keyframe(250f, 0.06f));

    /// <summary>What's in the pan (°C) - its own, apart from the pan's.</summary>
    public float Temperature => sauce.temp;
    public float PanTemperature => VesselHeat.Of(bowl) is VesselHeat vh ? vh.PanTemp : sauce.temp;
    /// <summary>Burnt per second right now (0 when it isn't catching).</summary>
    public float BurnRate { get; private set; }

    private float stirUntil;
    private bool chiliBatch;   // (this pan's 고추기름 counted as a batch - ChiliOilLog)
    private float unstirred;

    /// <summary>How burnt (0..1) - stays with it, strained or not.</summary>
    public float Burn => sauce.burn;

    /// <summary>Stirred (the spatula): what's frying in it catches more slowly for a moment.</summary>
    public void Stir()
    {
        stirUntil = Time.time + 1.5f;
        pendingStarch = 0f;                       // (the starch just added: stirred through in time)
        if (Lumps > 0f) Lumps = Mathf.Max(0f, Lumps - 0.04f); // a few clumps broken up
        MixProgress = Mathf.Min(1f, MixProgress + mixPerStir);
    }

    [Tooltip("How far one stir mixes what was just added through (0..1).")]
    [SerializeField] private float mixPerStir = 0.25f;

    /// <summary>How evenly what's in it is mixed (0..1): something new added (a
    /// sauce, 전분물) isn't, until it's stirred - or boiled a good while. The
    /// starch thickens only as far as it's mixed through.</summary>
    public float MixProgress { get; private set; } = 1f;

    private MovableBowl bowl;
    private GameObject pool;
    private float poolRadius = -1f;
    private Color poolColor;
    private int poolLumps;
    private bool poolThick;
    private int poolBurnStep = -1;

    public static PanSauce Of(MovableBowl bowl, bool create)
    {
        if (bowl == null) return null;
        PanSauce s = bowl.GetComponent<PanSauce>();
        if (s == null && create) s = bowl.gameObject.AddComponent<PanSauce>();
        return s;
    }

    /// <summary>How many spoonfuls of liquid fit in this bowl (its inside, up to the rim).</summary>
    public static float CapacityOf(MovableBowl b)
    {
        if (b == null) return 0f;
        MovableBowl.Interior i = b.GetInterior();
        return Mathf.Max(3f, Mathf.Round(Mathf.PI * i.radius * i.radius * i.rim / 0.0015f));
    }

    void Awake() => bowl = GetComponent<MovableBowl>();

    public void Add(string seasoning, float spoons = 1f)
    {
        if (!(spoons > 0f)) return;
        Normalize();
        // what goes in is at room temperature: it cools what's there by its share
        if (Amount + spoons > 0f) sauce.temp = (sauce.temp * Amount + 20f * spoons) / (Amount + spoons);
        // the new part isn't mixed in yet
        if (Amount + spoons > 0f) MixProgress *= Amount / (Amount + spoons);
        sauce.parts.TryGetValue(seasoning, out float had);
        sauce.parts[seasoning] = had + spoons;
        Amount += spoons;
        // Starch into a hot sauce: dry powder straight in clumps; 앙금녹말 has to
        // be stirred through at once (within lumpWindowSeconds) or it clumps.
        if (IsStarch(seasoning) && sauce.heat >= 0.5f)
        {
            StarchAddedHot++;
            if (seasoning == DryStarch) AddLumps(0.6f * spoons / Mathf.Max(StarchSpoons, 0.01f), "마른 전분을 뜨거운 소스에 바로 넣음");
            else { pendingStarch += spoons; pendingSince = Time.time; }
        }
        Recompute();
        Debug.Log($"[소스] {bowl.name}: {seasoning} +{spoons * 15f:0}mL (총 {Amount * 15f:0}mL, 점성 {Viscosity:0.00})");
    }

    // ---- 농도 · 뭉침 · 졸이기 (recorded for the final evaluation; looks only, not physics) ----

    [Tooltip("Seconds, hot, for the starch in it to thicken the sauce fully.")]
    [SerializeField] private float gelSeconds = 20f;
    [Tooltip("Starch added hot must be stirred within this many seconds, or it clumps.")]
    [SerializeField] private float lumpWindowSeconds = 2.5f;
    [Tooltip("Water boiled off per second on a strong flame, hot (mL) - 졸이기.")]
    [SerializeField] private float evaporateMlPerSecond = 0.8f;
    [Tooltip("How strongly gelled starch thickens the liquid (look tuning).")]
    [SerializeField] private float thickenStrength = 2.5f;

    private float pendingStarch, pendingSince;

    /// <summary>How much of the starch in it has thickened (0..1) - kept in the sauce itself, so it goes with it.</summary>
    public float Gel { get => sauce.gel; private set => sauce.gel = value; }
    /// <summary>Clumps of starch in it (0..1) - stirring breaks a few up.</summary>
    public float Lumps { get => sauce.lumps; private set => sauce.lumps = value; }
    public int LumpEvents { get; private set; }
    public int StarchAddedHot { get; private set; }
    /// <summary>What happened to it on the way (why it clumped ...).</summary>
    public IEnumerable<string> Notes => sauce.notes;
    /// <summary>Water boiled off (mL).</summary>
    public float EvaporatedMl { get => sauce.evaporatedMl; private set => sauce.evaporatedMl = value; }
    public float SecondsOnFlame { get; private set; }

    /// <summary>The three starches, told apart: dry powder (감자전분), drained 앙금 (앙금녹말), and the
    /// starch of 전분물 (its water is kept as 물 - starch and water stored separately).</summary>
    public const string DryStarch = "감자전분", Settled = "앙금녹말", SlurryStarch = "전분(전분물)";
    public static bool IsStarch(string name) => name == Settled || name == DryStarch || name == SlurryStarch;

    public float StarchSpoons
    {
        get { Normalize(); float s = 0f; foreach (var kv in sauce.parts) if (IsStarch(kv.Key)) s += kv.Value; return s; }
    }

    /// <summary>How thick the finished sauce is, 0 thin .. 1 set - its pastes
    /// plus the starch that's thickened it (look tuning, not a rheology model).</summary>
    public float Thickness =>
        Mathf.Clamp01(Viscosity * 0.6f + Gel * thickenStrength * StarchSpoons / Mathf.Max(LiquidAmount, 0.5f));

    private void AddLumps(float share, string cause)
    {
        Lumps = Mathf.Clamp01(Lumps + Mathf.Clamp01(share));
        LumpEvents++;
        sauce.notes.Add(cause);
        Debug.Log($"[소스] {bowl.name}: 전분 뭉침 ({cause}) → {Lumps:0.00}");
    }

    /// <summary>Simulated taste balance, from what went in (spoonfuls, per
    /// spoonful of liquid) - an estimate from the recipe data, not a taste.</summary>
    public void TasteEstimate(out float sweet, out float sour, out float salty)
    {
        Normalize();
        float Get(string n) => sauce.parts.TryGetValue(n, out float v) ? v : 0f;
        float liquid = Mathf.Max(LiquidAmount, 0.5f);
        sweet = (Get("설탕") + Get("케첩") * 0.3f) / liquid;
        sour = (Get("식초") + Get("케첩") * 0.15f) / liquid;
        salty = (Get("간장") + Get("소금") * 3f + Get("두반장") * 0.8f + Get("굴소스") * 0.6f + Get("춘장") * 0.7f) / liquid;
    }

    public void Consume(float spoons)
    {
        if (spoons <= 0f || Amount <= 0f) return;
        Amount = Mathf.Max(0f, Amount - spoons);
    }

    /// <summary>Takes this much out (all of it mixed, or the liquid only) - e.g.
    /// poured from the pan, or a spoonful of the strained oil.</summary>
    public SaucePortion Take(float spoons, bool liquidOnly = false)
    {
        Normalize();
        SaucePortion p = sauce.Take(Mathf.Min(spoons, liquidOnly ? sauce.LiquidAmount : Amount), liquidOnly);
        Amount = Mathf.Max(0f, Amount - p.Amount);
        Recompute();
        return p;
    }

    /// <summary>A portion mixed in (keeps its heat and its infusion).</summary>
    public void Put(SaucePortion p)
    {
        if (p == null || p.Amount <= 0f) return;
        Normalize();
        sauce.Add(p);
        Amount += p.Amount;
        Recompute();
    }

    /// <summary>All of it goes over onto the plate with the food (담기).</summary>
    public void MoveAllTo(MovableBowl plate)
    {
        if (plate == null || Amount <= 0f) return;
        Of(plate, true).Put(Take(Amount));
    }

    /// <summary>The pieces soaking sauce up (Consume) take from the amount only:
    /// the parts are scaled to match before any are taken or added.</summary>
    private void Normalize()
    {
        if (Amount <= 1e-5f)
        {
            sauce.Clear();
            Amount = 0f;
            pendingStarch = pendingSince = 0f;
            chiliBatch = false;
            unstirred = BurnRate = 0f;
            solidTemp = 20f;
            MixProgress = 0f;
            return;
        }
        float sum = sauce.Amount;
        if (sum <= 0f || Mathf.Abs(sum - Amount) < 1e-4f) return;
        float k = Amount / sum;
        foreach (string key in new List<string>(sauce.parts.Keys)) sauce.parts[key] *= k;
    }

    void Update()
    {
        if (bowl == null) return;
        VesselHeat vh = VesselHeat.Of(bowl);
        if (Amount <= 0.001f) { chiliBatch = false; unstirred = 0f; BurnRate = 0f; solidTemp = 20f; if (vh != null) vh.LoadMl = 0f; return; } // (emptied: a new batch next time)
        StoveBurner b = bowl.CurrentBurner;
        bool flame = !bowl.IsHeld && b != null && b.IsOn;
        float dt = SpeedMode.Wait(Time.deltaTime); // (빠른 모드: heating, thickening, boiling down go fast)
        if (flame) SecondsOnFlame += dt;

        // ---- temperature: what's in it follows the pan (its own heat: 예열/잔열 -
        // VesselHeat), slower the more there is; with water in it, it boils at
        // 100 °C and goes no higher - oil alone keeps rising with the pan ----
        Normalize();
        float ml = Amount * 15f;
        float pan = vh != null ? vh.PanTemp : sauce.temp;
        float room = vh != null ? vh.RoomTemp : 20f;
        if (vh != null) vh.LoadMl = ml;
        float tau = contentTauBase + contentTauPerMl * ml;
        sauce.temp += ((pan - sauce.temp) / Mathf.Max(tau, 0.1f) - (sauce.temp - room) / Mathf.Max(contentLossTau, 1f)) * dt;
        bool watery = HasWaterBasedLiquid();
        if (watery && sauce.temp > 100f) sauce.temp = 100f;
        sauce.heat = Mathf.Clamp01((sauce.temp - room) / (100f - room));
        // boiling moves it about a little by itself (much slower than stirring)
        if (sauce.temp >= 99f && MixProgress < 1f) MixProgress = Mathf.Min(1f, MixProgress + dt * 0.02f);

        // starch left unstirred after going in hot: clumped
        if (pendingStarch > 0f && Time.time - pendingSince > lumpWindowSeconds)
        {
            AddLumps(0.5f * pendingStarch / Mathf.Max(StarchSpoons, 0.01f), "전분을 넣은 뒤 바로 젓지 않음");
            pendingStarch = 0f;
        }
        // hot, the starch thickens the sauce
        if (sauce.heat >= 0.5f && Gel < 1f && sauce.parts.Keys.Count > 0)
        {
            bool anyStarch = false;
            foreach (string k in sauce.parts.Keys) if (IsStarch(k)) { anyStarch = true; break; }
            if (anyStarch)
            {
                float was = Gel;
                // (only as far as it's mixed through - unmixed starch sits in clumps)
                Gel = Mathf.Min(1f, Gel + dt * sauce.heat * Mathf.Lerp(0.2f, 1f, MixProgress) / Mathf.Max(gelSeconds, 0.1f));
                if (Mathf.FloorToInt(Gel * 10f) != Mathf.FloorToInt(was * 10f)) Recompute();
            }
        }
        // 졸이기: boiling, the water goes off - faster the hotter the pan under it
        if (watery && sauce.temp >= 99f && sauce.parts.TryGetValue(Condiments.Water, out float water) && water > 0f)
        {
            float push = Mathf.Clamp01((pan - 100f) / 150f);
            float off = Mathf.Min(water, dt * push * evaporateMlPerSecond / 15f);
            sauce.parts[Condiments.Water] = water - off;
            if (sauce.parts[Condiments.Water] <= 1e-5f) sauce.parts.Remove(Condiments.Water);
            Amount = Mathf.Max(0f, Amount - off);
            EvaporatedMl += off * 15f;
        }
        // Cooking hot on the flame spatters the burner's floor (under its grate):
        // oil (with only 식용유 for liquid - frying, 고추기름) or a sauce.
        if (flame && sauce.heat > 0.3f)
            BurnerFloorDirt.Soil(b, watery ? BurnerFloorDirt.SoilKind.Sauce : BurnerFloorDirt.SoilKind.Oil, dt);

        // ---- 물기 소스가 졸아 타기: 물이 거의 없는 적은 양의 소스(간장·식초·설탕...)가
        // 뜨거운 팬에 오래 있으면 바닥에 눌어붙어 검게 탄다 (저으면 덜). 연기도. ----
        if (watery && flame && !SpeedMode.On
            && !(sauce.parts.TryGetValue(Condiments.Water, out float wetLeft) && wetLeft > 0.2f) && Amount <= scorchBelowSpoons)
        {
            bool stirringNow = Time.time < stirUntil;
            BurnRate = Mathf.Max(0f, scorchPerSecondByPanTemp.Evaluate(pan)) * (stirringNow ? 0.4f : 1f)
                       * Mathf.Lerp(1.5f, 0.6f, Mathf.Clamp01(Amount / scorchBelowSpoons)); // (적을수록 빨리)
            if (BurnRate > 0f && sauce.burn < 1f)
            {
                float had = sauce.burn;
                sauce.burn = Mathf.Min(1f, had + BurnRate * dt);
                if (Mathf.FloorToInt(sauce.burn * 20f) != Mathf.FloorToInt(had * 20f)) Recompute();
                if (had < 0.5f && sauce.burn >= 0.5f)
                {
                    sauce.notes.Add("소스가 졸아붙어 탐");
                    Debug.Log($"[소스] {bowl.name}: 졸아붙어 탐 (불을 줄이거나 저으세요)");
                }
            }
            UpdateSmoke();
            return;
        }
        if (sauce.LiquidAmount <= 0.01f || sauce.SolidAmount <= 0.001f) { BurnRate = 0f; return; }
        bool changed = false;

        // ---- a solid in the hot liquid: its colour/taste drawn out (by the
        // liquid's temperature) - and, past that, catching (by the solid's own
        // temperature: lying still on the hot bottom it's hotter than the oil;
        // stirred, nearer the oil's - stirring slows it, never stops it). Burnt
        // stays burnt - stirring or cooling never undoes it. ----
        bool stirring = Time.time < stirUntil;
        float contact = stirring ? bottomContactStirred : bottomContactStill;
        float towards = sauce.temp + contact * (Mathf.Max(0f, pan - sauce.temp) + stuckHotSpot);
        solidTemp += (towards - solidTemp) * Mathf.Clamp01(dt / Mathf.Max(solidTau, 0.05f));
        BurnRate = watery || SpeedMode.On ? 0f : Mathf.Max(0f, burnPerSecondByTemp.Evaluate(solidTemp)); // (빠른 모드: nothing burns)
        bool chili = sauce.parts.ContainsKey("식용유") && sauce.parts.ContainsKey("고춧가루");
        if (chili && sauce.temp >= 100f)
        {
            // (recorded for the final evaluation - nothing shown now)
            if (!chiliBatch) { chiliBatch = true; ChiliOilLog.Batches++; }
            ChiliOilLog.HotSeconds += dt;
            if (stirring) { ChiliOilLog.StirredSeconds += dt; unstirred = 0f; }
            else { unstirred += dt; ChiliOilLog.LongestUnstirred = Mathf.Max(ChiliOilLog.LongestUnstirred, unstirred); }
        }
        if (BurnRate > 0f && sauce.burn < 1f)
        {
            float had = sauce.burn;
            sauce.burn = Mathf.Min(1f, had + BurnRate * dt);
            if (chili)
            {
                ChiliOilLog.BurningSeconds += dt;
                ChiliOilLog.MaxBurn = Mathf.Max(ChiliOilLog.MaxBurn, sauce.burn);
            }
            if (Mathf.FloorToInt(sauce.burn * 20f) != Mathf.FloorToInt(had * 20f)) changed = true;
            if (had < 0.5f && sauce.burn >= 0.5f) { sauce.notes.Add("고춧가루를 기름에 오래 볶아 탐"); Debug.Log($"[고추기름] {bowl.name}: 탐 (걸러도 탄 맛·색은 남아요)"); }
        }
        float extract = watery ? 0f : Mathf.Max(0f, extractionPerSecondByTemp.Evaluate(sauce.temp));
        if (extract > 0f)
            foreach (string key in new List<string>(sauce.parts.Keys))
            {
                if (!SaucePortion.IsSolid(key)) continue;
                float had = sauce.InfusionOf(key);
                if (had >= 1f) continue;
                float strength = Mathf.Min(1f, sauce.parts[key] / (sauce.LiquidAmount * 0.25f));
                float now = Mathf.Min(1f, had + dt * extract * strength);
                sauce.infusion[key] = now;
                if (Mathf.FloorToInt(now * 20f) != Mathf.FloorToInt(had * 20f)) changed = true;
                if (now >= 0.3f && had < 0.3f) Debug.Log($"[고추기름] {bowl.name}: 기름에 {key} 색·맛이 우러남 ({sauce.temp:0}°C)");
            }
        if (changed) Recompute();
        UpdateSmoke();
    }

    /// <summary>Water, soy sauce, vinegar ... in it (it boils at 100 °C) - not
    /// only oil (식용유, 참기름, 고추기름: on up with the pan).</summary>
    /// <summary>Has water, soy sauce, vinegar ... in it - boils at 100 °C (no searing in it).</summary>
    public bool IsWatery => HasWaterBasedLiquid();

    private bool HasWaterBasedLiquid()
    {
        foreach (var kv in sauce.parts)
        {
            if (kv.Value <= 0.001f || SaucePortion.IsSolid(kv.Key)) continue;
            string k = kv.Key;
            if (k == "식용유" || k == "참기름" || k.Contains("고추기름")) continue;
            if (Condiments.Get(k).form == CondimentForm.Liquid) return true;
        }
        return false;
    }

    // ---- 연기: 타는 동안 탄 연기 (파티클 - Vefects 연기 텍스처, BurnSmoke) ----
    private void UpdateSmoke()
    {
        if (BurnRate < smokeAboveBurnRate * 0.25f) return;
        BurnSmoke.For(bowl)?.Emit(Mathf.Clamp01(0.25f + BurnRate / 0.06f));
    }

    private void Recompute()
    {
        float w = 0f, visc = 0f;
        Color c = Color.black;
        Color liquid = sauce.LiquidColor;
        foreach (var kv in sauce.parts)
        {
            Condiments.Info info = Condiments.Get(kv.Key);
            // Powders color less than pastes and liquids do; a liquid shows as
            // the liquid it's become (oil gone red).
            float weight = kv.Value * (info.form == CondimentForm.Powder ? 0.5f : 1f);
            // a solid fried too hot goes brown, then black - not just a deeper red
            Color own = info.form == CondimentForm.Powder ? BurntSolid(info.color, sauce.burn) : info.color;
            c += (info.form == CondimentForm.Liquid ? liquid : own) * weight;
            visc += weight * (info.form == CondimentForm.Paste ? 0.9f : info.form == CondimentForm.Powder ? 0.5f : 0.15f);
            w += weight;
        }
        if (w > 0f) { Color c2 = c / w; c2.a = 1f; Color = c2; Viscosity = visc / w; }
    }

    /// <summary>A solid's colour as it burns: its own → brown → near black.</summary>
    private static Color BurntSolid(Color own, float burn)
    {
        Color brown = new Color(0.36f, 0.18f, 0.08f), black = new Color(0.08f, 0.06f, 0.05f);
        return burn < 0.6f ? Color.Lerp(own, brown, burn / 0.6f) : Color.Lerp(brown, black, (burn - 0.6f) / 0.4f);
    }

    void LateUpdate()
    {
        if (bowl == null) return;
        MovableBowl.Interior inside = bowl.GetInterior();
        // A deep bowl (not a pan) shows the liquid's surface at its level.
        bool level = !bowl.IsCookingVessel && inside.rim > inside.radius * 0.35f;
        float fill = level ? Mathf.Clamp01(Amount / CapacityOf(bowl)) : 0f;
        float r = Amount <= 0.01f ? 0f
                : level ? inside.radius * Mathf.Lerp(0.55f, 1f, fill)
                : Mathf.Min(Mathf.Sqrt(Amount) * 0.13f, inside.radius * 0.95f);
        // 기울여 붓는 중: 입구 쪽 옆면에 모여 고인다 (펼쳐진 것보다 좁게)
        if (PourLip.HasValue && !level) r = Mathf.Min(r, inside.radius * 0.45f);
        if (r <= 0f)
        {
            if (pool != null) { Destroy(pool); pool = null; poolRadius = -1f; }
            return;
        }
        int lumpCount = Mathf.RoundToInt(Lumps * 10f);
        bool thick = Thickness > 0.55f;
        bool tooThick = Thickness > 0.85f;
        int burnStep = Mathf.FloorToInt(sauce.burn * 10f) + (tooThick ? 100 : 0);
        if (pool == null || Mathf.Abs(r - poolRadius) > poolRadius * 0.08f || !SameColor(poolColor, Color) || lumpCount != poolLumps || thick != poolThick
            || burnStep != poolBurnStep)
        {
            poolBurnStep = burnStep;
            if (pool != null) Destroy(pool);
            pool = new GameObject("PanSauce");
            pool.transform.SetParent(transform, true);
            Condiments.BuildContent(pool.transform, Vector3.zero, Vector3.up, r,
                thick ? CondimentForm.Paste : CondimentForm.Liquid, Color, "Pool");
            // the paste's dollop flattened into a spread-out sauce
            foreach (Transform t in pool.GetComponentsInChildren<Transform>())
                if (t.name != "PanSauce" && t.name != "Pool") t.localScale = Vector3.Scale(t.localScale, new Vector3(1f, 0.25f, 1f));
            // clumps of starch showing in it
            for (int i = 0; i < lumpCount; i++)
            {
                float a = i * 2.39996f, d = r * 0.75f * Mathf.Sqrt((i + 0.5f) / Mathf.Max(lumpCount, 1));
                Condiments.BuildContent(pool.transform, new Vector3(Mathf.Cos(a) * d, r * 0.04f, Mathf.Sin(a) * d), Vector3.up,
                    r * 0.09f, CondimentForm.Powder, Color.Lerp(Color, new Color(0.95f, 0.94f, 0.9f), 0.6f), "Lump");
            }
            // the solids (고춧가루) as grains in the oil: red - some brown, then
            // black ones, the more it's burnt
            if (sauce.SolidAmount > 0.001f && !HasWaterBasedLiquid())
            {
                const int grains = 14;
                int dark = Mathf.RoundToInt(Mathf.Clamp01(sauce.burn) * grains);
                Color solid = Color.red;
                foreach (var kv in sauce.parts) if (SaucePortion.IsSolid(kv.Key)) { solid = Condiments.Get(kv.Key).color; break; }
                for (int i = 0; i < grains; i++)
                {
                    float a = i * 2.39996f + 0.7f, d = r * 0.8f * Mathf.Sqrt((i + 0.5f) / grains);
                    Color gc = i < dark ? BurntSolid(solid, Mathf.Min(1f, sauce.burn + 0.25f)) : BurntSolid(solid, sauce.burn * 0.3f);
                    Condiments.BuildContent(pool.transform, new Vector3(Mathf.Cos(a) * d, r * 0.03f, Mathf.Sin(a) * d), Vector3.up,
                        r * 0.06f, CondimentForm.Powder, gc, "Grain");
                }
            }
            // too thick: it sits in heavy mounds instead of spreading
            if (tooThick)
                for (int i = 0; i < 4; i++)
                {
                    float a = i * 1.7f + 0.4f, d = r * 0.45f;
                    Condiments.BuildContent(pool.transform, new Vector3(Mathf.Cos(a) * d, r * 0.05f, Mathf.Sin(a) * d), Vector3.up,
                        r * 0.32f, CondimentForm.Paste, Color * 0.95f, "Mound");
                }
            poolRadius = r;
            poolColor = Color;
            poolLumps = lumpCount;
            poolThick = thick;
        }
        // Tipped, the sauce runs to the low side - quickly when thin, slowly when
        // thick, hardly at all when it's set (a look, not a fluid simulation).
        Vector3 downhill = Vector3.ProjectOnPlane(-Carryable.CounterUp, inside.up);
        float tilt = Mathf.Clamp01(downhill.magnitude * 3f);
        Vector3 target = level || tilt < 0.05f ? Vector3.zero : downhill.normalized * inside.radius * 0.45f * tilt;
        if (PourLip.HasValue && !level)
        {
            // 붓는 입구 쪽 옆면으로 (가장자리에서 고인 웅덩이 반지름만큼 안쪽)
            Vector3 toLip = Vector3.ProjectOnPlane(PourLip.Value - inside.floorCenter, inside.up);
            target = toLip.sqrMagnitude > 1e-6f ? toLip.normalized * Mathf.Max(0f, inside.radius - r * 0.9f) : Vector3.zero;
        }
        float speed = Mathf.Lerp(6f, 0.25f, Mathf.Clamp01(Thickness));
        poolShift = Vector3.Lerp(poolShift, target, Mathf.Clamp01(Time.deltaTime * speed));
        // (그릇 안쪽 바닥은 깊이의 약 17% 높이 - 그보다 낮으면 조금 남은 양이
        // 바닥 속에 묻혀 안 보이므로, 남아 있는 동안은 바닥 위에서 시작한다)
        float height = level ? inside.rim * Mathf.Lerp(0.22f, 0.92f, fill) : 0.004f;
        pool.transform.SetPositionAndRotation(inside.floorCenter + poolShift + inside.up * Mathf.Max(height, 0.004f), Quaternion.FromToRotation(Vector3.up, inside.up));
    }

    private Vector3 poolShift;

    /// <summary>기울여 붓는 중인 입구 (월드) - 그쪽 옆면에 소스가 모여 고인다 (Cheesecloth의 붓기). 아니면 null.</summary>
    public Vector3? PourLip { get; set; }

    /// <summary>A drop off the spatula after a stir: a thin sauce runs straight
    /// off, a thick one hangs a while and falls slowly.</summary>
    public void DripFrom(Vector3 from)
    {
        if (Amount <= 0.01f) return;
        StartCoroutine(Drip(from));
    }

    private System.Collections.IEnumerator Drip(Vector3 from)
    {
        MovableBowl.Interior inside = bowl.GetInterior();
        Vector3 to = inside.floorCenter + inside.up * inside.rim * 0.15f;
        GameObject drop = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        drop.name = "SauceDrip";
        Destroy(drop.GetComponent<Collider>());
        float th = Mathf.Clamp01(Thickness);
        float s = Mathf.Lerp(0.025f, 0.05f, th);
        drop.transform.localScale = new Vector3(s, s * Mathf.Lerp(1.7f, 1.1f, th), s);
        var m = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard")) { color = Color };
        if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", Mathf.Lerp(0.6f, 0.9f, th));
        drop.GetComponent<Renderer>().sharedMaterial = m;
        float hang = Mathf.Lerp(0f, 0.8f, th), fall = Mathf.Lerp(0.2f, 1.1f, th);
        for (float t = 0f; t < hang; t += Time.deltaTime) { drop.transform.position = from; yield return null; }
        for (float t = 0f; t < fall; t += Time.deltaTime) { float k = t / fall; drop.transform.position = Vector3.Lerp(from, to, k * k); yield return null; }
        Destroy(drop);
        Destroy(m);
    }

    private static bool SameColor(Color a, Color b) =>
        Mathf.Abs(a.r - b.r) < 0.02f && Mathf.Abs(a.g - b.g) < 0.02f && Mathf.Abs(a.b - b.b) < 0.02f;

    void OnDestroy()
    {
        if (pool != null) Destroy(pool);
    }
}
