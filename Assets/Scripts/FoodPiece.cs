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

    public void SetSize(Vector3 worldSize) => Radius = (worldSize.x + worldSize.y + worldSize.z) / 6f;

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

    /// <summary>Shows its own cooked color with its sauce over it.</summary>
    public void Show(Color cooked)
    {
        if (rend == null) rend = GetComponent<Renderer>();
        if (rend == null) return;
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
    [Tooltip("Sauce taken on while sliding through it, per unit of speed per second.")]
    [SerializeField] private float coatPerMove = 0.3f;

    [Header("General")]
    [SerializeField] private float gravity = 3f;
    [Tooltip("How much the container's own movement (carried, shaken) throws the pieces about.")]
    [SerializeField] private float inertia = 0.2f;

    public readonly List<FoodPiece> Pieces = new List<FoodPiece>();

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

        foreach (FoodPiece p in Pieces)
            p.Show(cookable != null ? cookable.ColorAt(p.Cook) : Color.white);
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
        Vector3 shove = Vector3.ClampMagnitude(inv * (-accel * inertia), 6f);

        // "Down" is into the counter (the scene's counter is tilted to the camera);
        // a container tilted off it lets the pieces slide.
        Vector3 g = inv * (-Carryable.CounterUp * gravity);

        PanSauce sauce = simBowl.GetComponent<PanSauce>();
        float sauceAvail = sauce != null ? Mathf.Clamp01(sauce.Amount / 3f) : 0f;
        float ttc = cookable != null ? Mathf.Max(cookable.TimeToCooked, 0.01f) : 1f;
        float time = Time.time;

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
                else
                {
                    bool onFloor = p.simPos.y <= p.Radius + 0.01f;
                    drag = onFloor ? floorDrag : 1f;
                }
                p.simVel += a * h;
                p.simVel *= Mathf.Exp(-drag * h);
                p.simPos += p.simVel * h;
                p.simSpin *= Mathf.Exp(-(inside.liquid ? 2f : 7f) * h);
                if (p.simSpin.sqrMagnitude > 1e-6f)
                    p.simRot = Quaternion.AngleAxis(p.simSpin.magnitude * h * Mathf.Rad2Deg, p.simSpin.normalized) * p.simRot;
                // A cube settles back onto a face (keeping which way it's turned).
                Vector3 fwd = Vector3.ProjectOnPlane(p.simRot * Vector3.forward, Vector3.up);
                if (fwd.sqrMagnitude > 1e-4f)
                    p.simRot = Quaternion.Slerp(p.simRot, Quaternion.LookRotation(fwd, Vector3.up), (inside.liquid ? 1.5f : 8f) * h);
                Contain(p);
            }
            Separate();
        }

        // Sliding through the sauce in a pan picks it up (where it touches - unevenly).
        if (sauce != null && sauceAvail > 0f && !inside.liquid)
        {
            float used = 0f;
            foreach (FoodPiece p in Pieces)
            {
                if (p.simPos.y > p.Radius * 1.6f) continue;
                float take = coatPerMove * p.simVel.magnitude * dt * sauceAvail;
                p.AddCoat(take, sauce.Color, sauce.Viscosity);
                used += take;
            }
            sauce.Consume(used * 0.15f);
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
        float top = Mathf.Max(inside.rim - r, r);
        if (p.simPos.y > top)
        {
            p.simPos.y = top;
            if (p.simVel.y > 0f) p.simVel.y = 0f;
        }
        Vector2 flat = new Vector2(p.simPos.x, p.simPos.z);
        float wall = Mathf.Max(inside.radius - r, 0.01f);
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
            }
        }
        foreach (FoodPiece p in Pieces) Contain(p);
    }

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
            if (avail > 0f)
            {
                // the ones at the bottom, in the sauce, get most; the rest a little
                float touch = p.simPos.y <= p.Radius * 1.6f ? 1f : 0.3f;
                float take = coatPerStir * avail * touch * Random.Range(0.4f, 1f);
                p.AddCoat(take, sauce.Color, sauce.Viscosity);
                used += take;
            }
        }
        sauce?.Consume(used * 0.15f);
    }

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
    public readonly Dictionary<string, float> parts = new Dictionary<string, float>();
    /// <summary>Per solid: how much of its colour and taste is in the liquid (0..1).</summary>
    public readonly Dictionary<string, float> infusion = new Dictionary<string, float>();
    public float heat;

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
        var p = new SaucePortion { heat = heat };
        foreach (var kv in infusion) p.infusion[kv.Key] = kv.Value;
        float pool = liquidOnly ? LiquidAmount : Amount;
        if (spoons <= 0f || pool <= 0f) return p;
        float k = Mathf.Min(1f, spoons / pool);
        foreach (string key in new List<string>(parts.Keys))
        {
            if (liquidOnly && IsSolid(key)) continue;
            float take = parts[key] * k;
            p.parts[key] = take;
            parts[key] -= take;
            if (parts[key] <= 1e-5f) parts.Remove(key);
        }
        return p;
    }

    /// <summary>Mixed in: the parts added; heat by amount and infusion by the
    /// liquid it's in, averaged (strained 고추기름 stays 고추기름).</summary>
    public void Add(SaucePortion p)
    {
        if (p == null || p.Amount <= 0f) return;
        float a = Amount, b = p.Amount, la = LiquidAmount, lb = p.LiquidAmount;
        heat = a + b > 0f ? (heat * a + p.heat * b) / (a + b) : p.heat;
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
        var p = new SaucePortion { heat = heat };
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
            Color c = Color.black;
            float w = 0f;
            foreach (var kv in parts)
                if (!IsSolid(kv.Key)) { c += Condiments.Get(kv.Key).color * kv.Value; w += kv.Value; }
            Color liquid = w > 0f ? c / w : new Color(0.95f, 0.82f, 0.35f);
            foreach (var kv in infusion)
                liquid = Color.Lerp(liquid, Condiments.Get(kv.Key).color * 0.95f, kv.Value * 0.85f);
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
            if (InfusionOf("고춧가루") >= 0.3f && parts.ContainsKey("식용유")) return "고추기름";
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

    [Tooltip("Seconds on a strong flame from cold to full heat.")]
    [SerializeField] private float heatSeconds = 30f;
    [Tooltip("Seconds, hot, for a solid's colour and taste to go fully into the oil.")]
    [SerializeField] private float infuseSeconds = 20f;

    private MovableBowl bowl;
    private GameObject pool;
    private float poolRadius = -1f;
    private Color poolColor;

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
        Normalize();
        sauce.parts.TryGetValue(seasoning, out float had);
        sauce.parts[seasoning] = had + spoons;
        Amount += spoons;
        Recompute();
        Debug.Log($"[소스] {bowl.name}: {seasoning} +{spoons:0.#} (총 {Amount:0.#}스푼, 점성 {Viscosity:0.00})");
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
        float sum = sauce.Amount;
        if (sum <= 0f || Mathf.Abs(sum - Amount) < 1e-4f) return;
        float k = Amount / sum;
        foreach (string key in new List<string>(sauce.parts.Keys)) sauce.parts[key] *= k;
    }

    void Update()
    {
        if (bowl == null || Amount <= 0.001f) return;
        StoveBurner b = bowl.CurrentBurner;
        bool flame = !bowl.IsHeld && b != null && b.IsOn;
        float rate = b == null ? 0f : b.Level == StoveBurner.FlameLevel.Strong ? 1f : b.Level == StoveBurner.FlameLevel.Medium ? 0.7f : 0.45f;
        sauce.heat = flame ? Mathf.Min(1f, sauce.heat + Time.deltaTime * rate / Mathf.Max(heatSeconds, 0.1f))
                           : Mathf.Max(0f, sauce.heat - Time.deltaTime / 180f);
        // Cooking hot on the flame spatters the burner's floor (under its grate):
        // oil (with only 식용유 for liquid - frying, 고추기름) or a sauce.
        if (flame && sauce.heat > 0.3f)
        {
            bool sauceLike = false;
            foreach (string key in sauce.parts.Keys)
                if (!SaucePortion.IsSolid(key) && key != "식용유") { sauceLike = true; break; }
            BurnerFloorDirt.Soil(b, sauceLike ? BurnerFloorDirt.SoilKind.Sauce : BurnerFloorDirt.SoilKind.Oil, Time.deltaTime);
        }

        // Hot oil draws the colour and taste out of a solid in it.
        if (sauce.heat < 0.35f || sauce.LiquidAmount <= 0.01f) return;
        bool changed = false;
        foreach (string key in new List<string>(sauce.parts.Keys))
        {
            if (!SaucePortion.IsSolid(key)) continue;
            float had = sauce.InfusionOf(key);
            if (had >= 1f) continue;
            float strength = Mathf.Min(1f, sauce.parts[key] / (sauce.LiquidAmount * 0.25f));
            float now = Mathf.Min(1f, had + Time.deltaTime * sauce.heat * strength / Mathf.Max(infuseSeconds, 0.1f));
            sauce.infusion[key] = now;
            if (Mathf.FloorToInt(now * 20f) != Mathf.FloorToInt(had * 20f)) changed = true;
            if (now >= 0.3f && had < 0.3f) Debug.Log($"[고추기름] {bowl.name}: 기름에 {key} 색·맛이 우러남");
        }
        if (changed) Recompute();
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
            c += (info.form == CondimentForm.Liquid ? liquid : info.color) * weight;
            visc += weight * (info.form == CondimentForm.Paste ? 0.9f : info.form == CondimentForm.Powder ? 0.5f : 0.15f);
            w += weight;
        }
        if (w > 0f) { Color c2 = c / w; c2.a = 1f; Color = c2; Viscosity = visc / w; }
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
        if (r <= 0f)
        {
            if (pool != null) { Destroy(pool); pool = null; poolRadius = -1f; }
            return;
        }
        if (pool == null || Mathf.Abs(r - poolRadius) > poolRadius * 0.08f || !SameColor(poolColor, Color))
        {
            if (pool != null) Destroy(pool);
            pool = new GameObject("PanSauce");
            pool.transform.SetParent(transform, true);
            Condiments.BuildContent(pool.transform, Vector3.zero, Vector3.up, r,
                Viscosity > 0.55f ? CondimentForm.Paste : CondimentForm.Liquid, Color, "Pool");
            // the paste's dollop flattened into a spread-out sauce
            foreach (Transform t in pool.GetComponentsInChildren<Transform>())
                if (t.name != "PanSauce" && t.name != "Pool") t.localScale = Vector3.Scale(t.localScale, new Vector3(1f, 0.25f, 1f));
            poolRadius = r;
            poolColor = Color;
        }
        float height = level ? inside.rim * fill * 0.92f : 0.004f;
        pool.transform.SetPositionAndRotation(inside.floorCenter + inside.up * Mathf.Max(height, 0.004f), Quaternion.FromToRotation(Vector3.up, inside.up));
    }

    private static bool SameColor(Color a, Color b) =>
        Mathf.Abs(a.r - b.r) < 0.02f && Mathf.Abs(a.g - b.g) < 0.02f && Mathf.Abs(a.b - b.b) < 0.02f;

    void OnDestroy()
    {
        if (pool != null) Destroy(pool);
    }
}
