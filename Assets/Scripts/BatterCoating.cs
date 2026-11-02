using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 튀김옷 on a piece of meat (탕수육's 돼지등심): what's been measured onto it
/// for the batter - 앙금녹말, 흰자 and anything else spooned in (노른자,
/// 전란 ... kept as what they are, never counted as 흰자) - in mL; how far
/// it's been mixed through (버무림); and how it looks.
///
/// Data: this component, on the meat's IngredientItem, is the one record of
/// the batter - the bowl's total. It goes with the meat wherever the meat
/// goes (the frying pan, the plate), so nothing is copied per piece and the
/// total never grows. The pieces only show it (a coat over each, spread by
/// how well it's been mixed), and so does the bowl (batter left at the
/// bottom when it's runny) - looks only, the amounts don't change.
///
/// The look (stiff / right / runny, thick / thin) comes from the recipe's
/// BatterLook curves - tuning for the eye, not a physical model; how a mix is
/// graded is BatterEvaluation's job, kept apart from this.
/// </summary>
public class BatterCoating : MonoBehaviour
{
    public const string Settled = "앙금녹말", DryStarch = "감자전분", White = "흰자", BeatenWhite = "푼 흰자";

    private static readonly HashSet<string> batterNames = new HashSet<string>
        { Settled, DryStarch, White, BeatenWhite, "노른자", "푼 노른자", "전란", "계란물", "물", PortionedCondiment.WetSettledStarch };
    private static readonly HashSet<string> liquids = new HashSet<string>
        { White, BeatenWhite, "노른자", "푼 노른자", "전란", "계란물", "물" };

    /// <summary>Goes into the 튀김옷 (not the marinade).</summary>
    public static bool IsBatterIngredient(string name) => name != null && batterNames.Contains(name);

    /// <summary>What 버무리기 is done with: a spatula, a spoon, a whisk, chopsticks
    /// (not the long frying ones - they pick pieces up).</summary>
    public static bool IsMixingTool(ToolItem t) =>
        t != null && (t.Kind == ToolKind.Spatula || t.Kind == ToolKind.Spoon || t.Kind == ToolKind.Whisk
                      || (t.Kind == ToolKind.Chopsticks && !(t is FryingChopsticks)));

    public static BatterCoating Of(IngredientItem meat, bool create)
    {
        if (meat == null) return null;
        BatterCoating c = meat.GetComponent<BatterCoating>();
        if (c == null && create)
        {
            c = meat.gameObject.AddComponent<BatterCoating>();
            c.Init(meat);
        }
        return c;
    }

    private IngredientItem meat;
    private CookingRecipe recipe;
    private readonly Dictionary<string, float> parts = new Dictionary<string, float>();
    private readonly List<string> order = new List<string>();

    public CookingRecipe Recipe => recipe;
    public IngredientItem Meat => meat;
    public IReadOnlyDictionary<string, float> Parts => parts;
    /// <summary>The order things went in ("앙금녹말 15", "흰자 5" ...).</summary>
    public IReadOnlyList<string> Log => order;

    /// <summary>How evenly it's mixed through, 0..1 (the bowl as a whole).</summary>
    public float MixProgress { get; private set; }
    /// <summary>Was mixed, then had more put in - needs mixing again.</summary>
    public bool NeedsRemix { get; private set; }
    public int Strokes { get; private set; }
    public bool Busy { get; private set; }

    public bool HasAny => BatterMl > 0.01f;
    public float Amount(string name) => parts.TryGetValue(name, out float v) ? v : 0f;
    public float SettledStarchMl => Amount(Settled);
    /// <summary>Only what went in as 흰자 (beaten or not).</summary>
    public float EggWhiteMl => Amount(White) + Amount(BeatenWhite);
    /// <summary>Everything else in it - outside the 앙금녹말 + 흰자 recipe.</summary>
    public float OffRecipeMl => BatterMl - SettledStarchMl - EggWhiteMl;
    public float BatterMl { get { float s = 0f; foreach (float v in parts.Values) s += v; return s; } }
    /// <summary>The starch in it - 물기 많은 앙금 counted by its assumed starch share (KitchenDevSettings).</summary>
    public float SolidMl => Amount(Settled) + Amount(DryStarch) + Amount(PortionedCondiment.WetSettledStarch) * KitchenDevSettings.Current.wetSettledStarchShare;
    /// <summary>Spooned up before the 앙금's water was poured off (mL).</summary>
    public float WetSettledMl => Amount(PortionedCondiment.WetSettledStarch);

    /// <summary>The liquids in it: the egg and water put into the batter, plus
    /// the marinade's liquids on the meat (간장, 청주 - their mL as put on; how
    /// much the meat soaks up isn't modelled - KitchenDevSettings).</summary>
    public float LiquidMl
    {
        get
        {
            float l = 0f;
            foreach (var kv in parts) if (liquids.Contains(kv.Key)) l += kv.Value;
            // (the water in 물기 많은 앙금 - the share that isn't starch)
            l += Amount(PortionedCondiment.WetSettledStarch) * (1f - KitchenDevSettings.Current.wetSettledStarchShare);
            return l + MarinadeLiquidMl;
        }
    }

    public float MarinadeLiquidMl
    {
        get
        {
            float l = 0f;
            if (meat != null)
                foreach (var kv in meat.AppliedSeasonings)
                    if (Condiments.Get(kv.Key).form == CondimentForm.Liquid) l += kv.Value;
            return l;
        }
    }

    /// <summary>The meat it's on (g) now - 0 if the scene doesn't say.</summary>
    public float MeatGrams => meat != null ? meat.AmountGrams : 0f;

    /// <summary>Batter (mL) per 100 g of meat - how much there is to go round.
    /// Twice the starch and twice the white is twice this (a thicker coat),
    /// though the ratio is the same. -1 without the meat's weight.</summary>
    public float LoadPer100g => MeatGrams > 0f ? BatterMl / MeatGrams * 100f : -1f;

    /// <summary>Liquid ÷ starch (mL ÷ mL) - more white: runnier, more starch: stiffer.</summary>
    public float LiquidRatio => SolidMl > 0.01f ? LiquidMl / SolidMl : 99f;

    private BatterLook Look => recipe != null ? recipe.batterLook : fallbackLook ?? (fallbackLook = new BatterLook());
    private static BatterLook fallbackLook;

    /// <summary>0 runny .. 1 stiff (look only).</summary>
    public float Stiffness => SolidMl > 0.01f ? Mathf.Clamp01(Look.stiffnessByLiquidRatio.Evaluate(LiquidRatio)) : 0f;

    /// <summary>0 .. 1 how thick the coat is (look only).</summary>
    public float Thickness => LoadPer100g >= 0f ? Mathf.Clamp01(Look.thicknessByLoad.Evaluate(LoadPer100g)) : 0.5f;

    public bool MixDone => MixProgress >= (recipe != null ? recipe.mixDoneAt : 0.95f);

    /// <summary>뻑뻑함 / 적당함 / 묽음 - practice feedback from the look, not a grade.</summary>
    public string TextureWord => Stiffness >= Look.lumpyAbove ? "뻑뻑함" : Stiffness <= Look.runnyBelow ? "묽음" : "적당함";

    private void Init(IngredientItem m)
    {
        meat = m;
        recipe = RecipeBook.BatterFor(m.IngredientName);
        if (recipe == null)
            Debug.LogWarning($"[튀김옷] {m.IngredientName}: 레시피(Resources/Recipes)가 없어 기본 질감 설정으로 표시합니다");
        else if (!recipe.BaseMeatSet)
            Debug.LogWarning($"[레시피 설정 누락] {recipe.displayName}: 기준 고기량이 미설정이라 배합 비율 평가를 하지 않습니다 (CookingRecipe.baseMeatGrams)");
        if (m.AmountGrams <= 0f)
            Debug.LogWarning($"[레시피 설정 누락] {m.name}: 고기량(IngredientItem.amountGrams)이 0이라 고기 대비 튀김옷 양을 계산할 수 없습니다");
    }

    /// <summary>A measured spoonful into the batter (its whole mL). Once mixed,
    /// anything new has to be mixed in again.</summary>
    public void Add(string name, float ml)
    {
        if (!(ml > 0f) || string.IsNullOrEmpty(name)) return;
        float before = BatterMl;
        parts.TryGetValue(name, out float had);
        parts[name] = had + ml;
        order.Add($"{name} {ml:0}");
        if (MixProgress > 0f)
        {
            bool wasDone = MixDone;
            MixProgress *= before / Mathf.Max(BatterMl, 0.01f); // (the new part isn't mixed in yet)
            if (wasDone && !MixDone) NeedsRemix = true;
        }
        Debug.Log($"[튀김옷] {meat.IngredientName}: {name} +{ml:0}mL → 앙금녹말 {SettledStarchMl:0}, 흰자 {EggWhiteMl:0}, 기타 {OffRecipeMl:0} (mL); " +
                  $"버무림 {MixProgress * 100f:0}%");
        Refresh();
    }

    // ---- 버무리기 ----

    /// <summary>One squeeze of the two-handed kneading (MeatKneader) - counted
    /// as one stroke, only at the moment the hands are on the meat.</summary>
    public void KneadStroke()
    {
        if (!HasAny) return;
        bool wasDone = MixDone;
        int needed = recipe != null ? recipe.strokesToMix : 6;
        MixProgress = Mathf.Min(1f, MixProgress + 1f / Mathf.Max(1, needed));
        Strokes++;
        if (MixDone) NeedsRemix = false;
        Refresh();
        if (MixDone && !wasDone) Debug.Log($"[버무리기] {meat.IngredientName}: 손으로 버무림 완료 (질감 {TextureWord} - 기록만)");
    }

    /// <summary>반죽 조각 하나 덜어내기 (기름 온도 확인용 - BowlMixInteraction): 모든
    /// 재료를 비율대로 ml만큼 줄인다. 그만큼 없으면 아무것도 바꾸지 않고 false.</summary>
    public bool TakeMl(float ml)
    {
        float total = BatterMl;
        if (!(ml > 0f) || total < ml) return false;
        float keep = (total - ml) / total;
        foreach (string k in new List<string>(parts.Keys)) parts[k] *= keep;
        Debug.Log($"[튀김옷] {meat.IngredientName}: 반죽 조각 {ml:0.#}mL 덜어냄 - 남은 튀김옷 {BatterMl:0.#}mL");
        Refresh();
        return true;
    }

    /// <summary>A piece taken off on its own (IngredientItem.SplitOffPiece): its
    /// share of the batter (by weight) goes with it - moved, not copied, so the
    /// total stays the same - and it's as mixed as the rest.</summary>
    public void GiveShareTo(BatterCoating other, float share)
    {
        if (other == null || share <= 0f) return;
        share = Mathf.Clamp01(share);
        foreach (string k in new List<string>(parts.Keys))
        {
            float take = parts[k] * share;
            parts[k] -= take;
            other.parts.TryGetValue(k, out float had);
            other.parts[k] = had + take;
            if (!other.order.Contains(k)) other.order.Add($"{k} {take:0.#}");
        }
        other.MixProgress = MixProgress;
        other.NeedsRemix = NeedsRemix;
        other.Strokes = Strokes;
        other.FriedSeconds = FriedSeconds;
        other.ColdOilSeconds = ColdOilSeconds;
        other.Refresh();
        Refresh();
    }

    /// <summary>One stroke through the bowl - with the tool in hand, or (tool
    /// null) by hand, squeezing the batter onto the meat: a click per stroke -
    /// holding the button still does nothing more.</summary>
    public void MixStroke(ToolItem tool, MovableBowl bowl)
    {
        if (Busy || !HasAny || bowl == null) return;
        StartCoroutine(MixRoutine(tool, bowl));
    }

    /// <summary>Under-mixed batter comes off in the oil: 1 once it's mixed
    /// through (MixDone), dropping steeply below that (look/estimate tuning).</summary>
    public float UnderMixFactor => MixDone ? 1f : Mathf.Lerp(0.3f, 0.9f, MixProgress);

    private IEnumerator MixRoutine(ToolItem tool, MovableBowl bowl)
    {
        Busy = true;
        HandCarry hand = HandCarry.Instance;
        if (hand != null) hand.IsLocked = true;
        HandController right = HandController.Instance;
        Vector3 c = bowl.BodyCenterWorld();
        // (by hand: right down into the bowl)
        right?.GoTo(c + (tool != null ? new Vector3(0.3f, 0.45f, -0.4f) : new Vector3(0.1f, 0.15f, -0.4f)), null);
        yield return new WaitForSeconds(0.2f);
        const float seconds = 0.6f;
        for (float t = 0f; t < seconds; t += Time.deltaTime)
        {
            float a = t / seconds * Mathf.PI * 2f; // one turn round the bowl, folding through
            Vector3 move = tool != null
                ? new Vector3(Mathf.Cos(a), Mathf.Sin(a) * 0.5f, 0f) * 0.12f
                : new Vector3(Mathf.Cos(a) * 0.08f, -Mathf.Abs(Mathf.Sin(a * 2f)) * 0.06f, 0f); // squeeze and turn over
            right?.SetGestureOffset(move);
            yield return null;
        }
        right?.SetGestureOffset(Vector3.zero);

        bool wasDone = MixDone;
        int needed = recipe != null ? recipe.strokesToMix : 6;
        MixProgress = Mathf.Min(1f, MixProgress + 1f / Mathf.Max(1, needed));
        Strokes++;
        if (MixDone) NeedsRemix = false;
        Refresh();
        if (tool != null) StartCoroutine(Drip(tool, bowl)); // (by hand: on the hand, no drip)
        if (hand != null) hand.IsLocked = false;
        Busy = false;
        Debug.Log($"[버무리기] {meat.IngredientName}: {Strokes}번째{(tool == null ? " (손)" : "")} - {MixProgress * 100f:0}%");
        // (no verdict shown while cooking - the look is the feedback; graded only at the end)
        if (MixDone && !wasDone) Debug.Log($"[버무리기] {meat.IngredientName}: 버무림 완료 (질감 {TextureWord} - 기록만)");
    }

    /// <summary>A drop of batter off the tool: a stiff one hangs and falls slowly,
    /// a runny one drips straight off.</summary>
    private IEnumerator Drip(ToolItem tool, MovableBowl bowl)
    {
        Renderer tr = tool.GetComponentInChildren<Renderer>();
        if (tr == null) yield break;
        Bounds tb = tr.bounds;
        Vector3 from = new Vector3(tb.center.x, tb.min.y, tb.min.z);
        MovableBowl.Interior inside = bowl.GetInterior();
        Vector3 to = inside.floorCenter + inside.up * inside.rim * 0.2f;
        GameObject drop = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        drop.name = "BatterDrip";
        Destroy(drop.GetComponent<Collider>());
        float s = Mathf.Lerp(0.03f, 0.06f, Stiffness);
        drop.transform.localScale = new Vector3(s, s * Mathf.Lerp(1.6f, 1.1f, Stiffness), s);
        drop.GetComponent<Renderer>().sharedMaterial = CoatMaterial();
        float hang = Mathf.Lerp(0.05f, 0.7f, Stiffness), fall = Mathf.Lerp(0.25f, 1.2f, Stiffness);
        for (float t = 0f; t < hang; t += Time.deltaTime) { drop.transform.position = from; yield return null; }
        for (float t = 0f; t < fall; t += Time.deltaTime)
        {
            float k = t / fall;
            drop.transform.position = Vector3.Lerp(from, to, k * k);
            yield return null;
        }
        Destroy(drop);
    }

    // ---- look ----

    private Material coatMat;
    private readonly List<GameObject> shown = new List<GameObject>();
    private int shownPieces = -1;
    private float nextPieceCheck;
    private GameObject pool;
    private MovableBowl poolBowl;

    /// <summary>This coating's own material (never a shared one - another bowl's
    /// meat isn't changed with it); each piece's shade via a property block.</summary>
    private Material CoatMaterial()
    {
        if (coatMat != null) return coatMat;
        Shader lit = Shader.Find("Universal Render Pipeline/Lit");
        coatMat = new Material(lit != null ? lit : Shader.Find("Standard")) { name = "BatterCoat (instance)" };
        coatMat.color = Look.batterColor;
        // (불투명 - 반투명은 이 화면에서 색이 바랬다. 얇은 곳은 색을 고기 색과 섞어 표현)
        return coatMat;
    }

    private List<Renderer> PieceRenderers()
    {
        var list = new List<Renderer>();
        foreach (FoodPiece p in GetComponentsInChildren<FoodPiece>())
            if (p.GetComponent<Renderer>() is Renderer r && p.GetComponent<MeshFilter>() != null) list.Add(r);
        if (list.Count == 0 && GetComponent<Renderer>() is Renderer own && own.enabled && GetComponent<MeshFilter>() != null) list.Add(own);
        return list;
    }

    // ---- 튀김 (frying: how much of the coat stays on) ----

    /// <summary>Seconds in hot oil (the batter-test temperature), and in oil not hot yet.</summary>
    public float FriedSeconds { get; private set; }
    public float ColdOilSeconds { get; private set; }
    public bool Fried => FriedSeconds > 0.5f;
    public int FryPasses { get; private set; }
    private float secondFryStart; // 2차 튀김을 시작할 때까지 튀긴 시간
    [Tooltip("고기 100g당 앙금(전분) mL가 이 값 이하면 얇은 옷 - 고기 200g에 앙금 1T(15mL)면 7.5.")]
    [SerializeField] private float fullCoatFrom = 10f;
    [Tooltip("고기 100g당 앙금(전분) mL가 이 값 이상이면 풍성한 옷 - 고기 200g에 앙금 2T(30mL)면 15.")]
    [SerializeField] private float fullCoatAt = 14f;
    private bool wasInOil;
    private float nextFryRefresh;

    /// <summary>How much of the coat stayed on in the frying (0..1, estimate):
    /// how stiff the batter was (BatterLook curve), how well it was mixed, and
    /// how much of the time the oil wasn't hot yet. Look/estimate tuning, not physics.</summary>
    public float Retention
    {
        get
        {
            if (!Fried) return 1f;
            float cold = ColdOilSeconds / Mathf.Max(FriedSeconds + ColdOilSeconds, 0.01f);
            return Mathf.Clamp01(Mathf.Clamp01(Look.retentionByStiffness.Evaluate(Stiffness))
                                 * UnderMixFactor * (1f - 0.5f * cold));
        }
    }

    private void TrackFrying()
    {
        MovableBowl v = meat.Container;
        bool inOil = v != null && v.HasFryingOil && !v.IsHeld && v.CurrentBurner != null && v.CurrentBurner.IsOn;
        if (inOil)
        {
            if (v.IsOilHot) FriedSeconds += SpeedMode.Wait(Time.deltaTime);
            else ColdOilSeconds += Time.deltaTime;
            if (!wasInOil) { FryPasses++; if (FryPasses == 2) secondFryStart = FriedSeconds; Debug.Log($"[튀김] {meat.IngredientName}: {FryPasses}번째 튀김 (기름 {(v.IsOilHot ? "적온" : "온도 미달")}) - 기록"); }
            if (Time.time >= nextFryRefresh) { nextFryRefresh = Time.time + 1f; Refresh(); }
        }
        wasInOil = inOil;
    }

    void LateUpdate()
    {
        if (meat == null) return;
        if (HasAny) TrackFrying();
        // cut further, or pieces gone: drawn again (checked now and then, not every frame)
        if (Time.time >= nextPieceCheck)
        {
            nextPieceCheck = Time.time + 0.5f;
            if (PieceRenderers().Count != shownPieces) Refresh();
        }
        UpdatePool();
        ApplyGlaze();
    }

    /// <summary>튀김옷 하나와 그 위에 묻은 소스.</summary>
    private class Glaze { public MeshRenderer mr; public FoodPiece piece; public Color col; public float smooth, shown; }
    private readonly List<Glaze> glazes = new List<Glaze>();
    private MaterialPropertyBlock glazeBlock;

    /// <summary>걸쭉한 소스에 버무린 튀김: 얼룩진 튀김옷이 소스에 덮여 노릇하고 윤기
    /// 나게 (조각에 묻은 소스 양 · 걸쭉함만큼). 접시로 옮겨도 조각의 소스를 따라간다.</summary>
    private void ApplyGlaze()
    {
        foreach (Glaze g in glazes)
        {
            if (g.mr == null || g.piece == null) continue;
            float k = Mathf.Clamp01(g.piece.Coat * 2f) * Mathf.InverseLerp(0.12f, 0.35f, g.piece.CoatViscosity); // 소스가 묻어 덮은 정도 (걸쭉한 소스일수록)
            if (Mathf.Abs(k - g.shown) < 0.01f) continue;               // 바뀐 게 없으면 그대로
            g.shown = k;
            Color gold = Color.Lerp(Look.friedColor, g.piece.CoatColor, 0.35f) * 0.92f; // 튀김색에 소스색이 살짝 든 노릇한 색
            gold.a = 1f;
            Color c = Color.Lerp(g.col, gold, k * 0.85f);  // 덮일수록 얼룩이 가려짐
            glazeBlock ??= new MaterialPropertyBlock();
            glazeBlock.Clear();
            glazeBlock.SetColor("_BaseColor", c);
            glazeBlock.SetColor("_Color", c);
            glazeBlock.SetFloat("_Smoothness", Mathf.Lerp(g.smooth, 0.82f, k)); // 소스 윤기
            g.mr.SetPropertyBlock(glazeBlock);
        }
    }

    /// <summary>Draws the coat over each piece: thicker with more batter for the
    /// meat, even once mixed (patchy before), lumpy when stiff, thin and glossy
    /// when runny.</summary>
    public void Refresh()
    {
        foreach (GameObject g in shown) if (g != null) Destroy(g);
        shown.Clear();
        glazes.Clear(); // 소스 윤기 입힐 목록도 새로
        foreach (Mesh m in coatMeshes) if (m != null) Destroy(m);
        coatMeshes.Clear();
        List<Renderer> pieces = PieceRenderers();
        shownPieces = pieces.Count;
        if (!HasAny) return;

        BatterLook look = Look;
        float stiff = Stiffness, thick = Thickness;
        if (stiff <= look.runnyBelow) thick *= 0.55f; // (a runny batter doesn't stay on thick)
        var block = new MaterialPropertyBlock();
        for (int i = 0; i < pieces.Count; i++)
        {
            Renderer r = pieces[i];
            MeshFilter mf = r.GetComponent<MeshFilter>();
            if (mf == null || mf.sharedMesh == null) continue;
            // unmixed: some pieces barely touched, some heavy; mixed: all alike
            float rnd = Mathf.Repeat(Mathf.Sin((i + 1) * 12.9898f + shownPieces * 78.233f) * 43758.5453f, 1f);
            float cover = Mathf.Clamp01(Mathf.Lerp(rnd * 0.6f, 1f, MixProgress));
            // fried: where it wasn't mixed on well, the coat came off in the oil (벗겨짐)
            if (Fried && !MixDone && rnd > MixProgress * 0.8f) cover *= Mathf.Lerp(0.15f, 0.6f, UnderMixFactor);
            if (cover < 0.05f) continue;

            // 앙금이 넉넉하면(2T) 풍성한 튀김옷, 부족하면(1T) 얇은 예전 모습 - 그 사이는 섞음
            // 튀김옷이 넉넉한 정도 (0 얇음 .. 1 풍성): 반죽 전체 양이 아니라 앙금(전분) 자체의 양으로 -
            // 물기 많은 앙금(물을 덜 따름)은 전분이 적게 쳐져 얇은 옷, 앙금 2T면 풍성한 옷
            float starchPer100g = MeatGrams > 0f ? SolidMl / MeatGrams * 100f : 0f;     // 고기 100g당 전분 mL
            float full = Mathf.InverseLerp(fullCoatFrom, fullCoatAt, starchPer100g);    // 1T 이하 0 → 2T 근처 1
            if (stiff <= look.runnyBelow) full *= 0.5f;                                 // 묽은 반죽은 덜 풍성
            float s = Mathf.Lerp(1f + look.maxInflate * thick * cover,                                   // 얇은 옷 크기
                                 1.12f + Mathf.Max(look.maxInflate, 0.35f) * Mathf.Max(thick, 0.4f) * cover, full); // 풍성한 옷 크기
            // 튀김옷 모양: 조각(네모)을 그대로 키운 상자가 아니라, 조각을 감싸는 둥근
            // 덩어리에 울퉁불퉁한 결 - 튀길수록 더 오돌토돌 (튀김 반죽이 부풀어 굳은 모양)
            float fry = Fried ? Mathf.Clamp01(FriedSeconds / Mathf.Max(look.fryBrownSeconds, 0.1f)) : 0f;
            var go = new GameObject("BatterCoat");
            go.transform.SetParent(r.transform, false);
            Mesh coatMesh = CoatMesh(mf.sharedMesh.bounds, s, i * 37 + shownPieces, Mathf.Lerp(Mathf.Lerp(0.12f, 0.38f, fry), Mathf.Lerp(0.25f, 0.45f, fry), full) * (0.6f + 0.4f * cover)); // 넉넉할수록 반죽부터 울퉁불퉁
            coatMeshes.Add(coatMesh);
            go.AddComponent<MeshFilter>().sharedMesh = coatMesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = CoatMaterial();
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            // how much it hides the meat: more batter, better mixed - less when runny
            // fried: going golden brown, and only as much of it as stayed on (Retention)
            // 1차 튀김: 하얗게 굳음 → 2차 튀김부터: 노릇하게
            float fry2 = FryPasses >= 2 ? Mathf.Clamp01((FriedSeconds - secondFryStart) / Mathf.Max(look.fryBrownSeconds * 0.5f, 0.1f)) : 0f; // 2차 튀김에서 튀긴 정도
            Color col = !Fried ? look.batterColor // 튀기기 전: 반죽 색
                : FryPasses >= 2 ? Color.Lerp(look.firstFryColor, look.friedColor, fry2) // 2차: 흰색 → 노릇하게
                : Color.Lerp(look.batterColor, look.firstFryColor, fry); // 1차: 하얗게 굳음
            col.a = Mathf.Clamp01(cover * Mathf.Lerp(0.65f, 0.97f, thick) * (stiff <= look.runnyBelow ? 0.75f : 1f) * (Fried ? Mathf.Lerp(0.3f, 1f, Retention) : 1f));
            // (튀겨지며 반죽이 굳어 불투명해진다 - 속 고기가 비치지 않게)
            if (Fried) col.a = Mathf.Lerp(col.a, Mathf.Max(col.a, 0.96f * Retention), fry);
            // (반투명은 이 화면에서 색이 바래 보여 불투명으로 - 옷이 얇을수록 고기 색이 섞여 비쳐 보이게)
            col = Color.Lerp(look.meatShowColor, col, Mathf.Lerp(Mathf.Sqrt(col.a), Mathf.Clamp01(Mathf.Sqrt(col.a) * 1.25f), full)); // 덮인 만큼 튀김옷 색 (넉넉하면 고기가 안 비침)
            col.a = 1f; // 불투명
            block.Clear();
            block.SetColor("_BaseColor", col);
            block.SetColor("_Color", col);
            // (반죽은 촉촉하게 반짝, 튀기면 바삭하게 - 덜 반짝)
            block.SetFloat("_Smoothness", Mathf.Lerp(Mathf.Lerp(0.85f, 0.25f, stiff), 0.18f, fry));
            mr.SetPropertyBlock(block);
            shown.Add(go);
            glazes.Add(new Glaze { mr = mr, piece = r.GetComponent<FoodPiece>(), col = col, smooth = block.GetFloat("_Smoothness"), shown = -1f }); // 소스가 묻으면 이 색 위에 윤기

            // stiff: clumps of batter stuck on it
            if (stiff >= look.lumpyAbove)
            {
                int lumps = 2 + (int)(rnd * 2f);
                Bounds lb = mf.sharedMesh.bounds;
                for (int k = 0; k < lumps; k++)
                {
                    float u = Mathf.Repeat(rnd * (k + 3) * 7.31f, 1f), v = Mathf.Repeat(rnd * (k + 5) * 3.77f, 1f);
                    Vector3 at = lb.center + Vector3.Scale(lb.extents, new Vector3(u * 2f - 1f, 1f, v * 2f - 1f)) * s;
                    GameObject lump = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                    lump.name = "BatterLump";
                    Destroy(lump.GetComponent<Collider>());
                    lump.transform.SetParent(r.transform, false);
                    lump.transform.localPosition = at;
                    float ls = Mathf.Min(lb.size.x, lb.size.z) * Mathf.Lerp(0.25f, 0.45f, (stiff - look.lumpyAbove) / Mathf.Max(0.01f, 1f - look.lumpyAbove));
                    lump.transform.localScale = new Vector3(ls, ls * 0.6f, ls);
                    var lr = lump.GetComponent<Renderer>();
                    lr.sharedMaterial = CoatMaterial();
                    lr.SetPropertyBlock(block);
                    shown.Add(lump);
                }
            }
        }
        RebuildPool();
    }

    private readonly List<Mesh> coatMeshes = new List<Mesh>();
    private static Mesh sphereTemplate;

    /// <summary>튀김옷 한 조각의 모양 (조각 메쉬 공간): 조각 상자(lb)를 감싸는 모서리가
    /// 둥근 덩어리(초타원체 - 네모 조각의 모서리까지 덮으면서 둥글게), inflate만큼
    /// 두툼하게, rough만큼 울퉁불퉁 (조각마다 seed로 다른 결). 보이는 모양만.</summary>
    private static Mesh CoatMesh(Bounds lb, float inflate, int seed, float rough)
    {
        if (sphereTemplate == null)
        {
            GameObject tmp = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            sphereTemplate = tmp.GetComponent<MeshFilter>().sharedMesh;
            Destroy(tmp);
        }
        Vector3[] src = sphereTemplate.vertices;
        var v = new Vector3[src.Length];
        Vector3 e = lb.extents;
        float minE = Mathf.Max(Mathf.Min(e.x, Mathf.Min(e.y, e.z)), 1e-4f);
        float pad = minE * (inflate - 1f) * 1.6f + minE * 0.12f; // (두께: 조각의 가는 쪽 기준)
        float ph = seed * 1.618f;
        for (int k = 0; k < src.Length; k++)
        {
            Vector3 d = src[k].normalized;
            // 둥근 상자: 방향 성분을 0.55제곱 - 1이면 구, 0에 가까울수록 상자
            Vector3 p = new Vector3(Sp(d.x) * e.x, Sp(d.y) * e.y, Sp(d.z) * e.z);
            // 울퉁불퉁한 결 (큰 혹 + 잔 결) - 방향이 아니라 실제 자리(가는 쪽 굵기 단위)로
            // 계산해서 긴 조각에서도 길게 늘어지지 않고 고르게. 같은 조각은 늘 같은 모양
            Vector3 q = p / minE;
            float n = Mathf.Sin(q.x * 2.3f + ph) * Mathf.Sin(q.y * 2.1f + ph * 1.7f) * Mathf.Sin(q.z * 2.5f + ph * 0.6f)
                    + 0.55f * Mathf.Sin(q.x * 5.3f + q.z * 1.7f + ph * 2.3f) * Mathf.Sin(q.y * 4.9f - q.z * 3.1f - ph)
                    + 0.3f * Mathf.Sin((q.x + q.y * 1.3f - q.z * 0.8f) * 9.7f + ph * 3.1f);
            float bump = pad + minE * rough * n;
            v[k] = lb.center + p + d * Mathf.Max(minE * 0.05f, bump);
        }
        var m = new Mesh { name = "BatterCoatMesh", vertices = v, triangles = sphereTemplate.triangles, uv = sphereTemplate.uv };
        m.RecalculateNormals();
        m.RecalculateBounds();
        return m;
    }

    private static float Sp(float x) => Mathf.Sign(x) * Mathf.Pow(Mathf.Abs(x), 0.55f);

    /// <summary>Batter that doesn't cling - left at the bottom of the bowl when
    /// it's runny (and more of it the more there is).</summary>
    private void RebuildPool()
    {
        if (pool != null) Destroy(pool);
        pool = null;
        poolBowl = meat != null ? meat.Container : null;
        if (poolBowl == null || poolBowl.IsCookingVessel || !HasAny) return;
        BatterLook look = Look;
        float excess = Mathf.Clamp01((look.runnyBelow + 0.25f - Stiffness) / 0.5f);
        if (excess <= 0.02f) return;
        MovableBowl.Interior inside = poolBowl.GetInterior();
        float r = inside.radius * Mathf.Clamp(0.35f + 0.5f * excess * Mathf.Sqrt(BatterMl / 45f), 0.3f, 0.92f);
        pool = new GameObject("BatterPool_" + meat.name);
        Condiments.BuildContent(pool.transform, Vector3.zero, Vector3.up, r, CondimentForm.Liquid,
            Color.Lerp(look.batterColor, new Color(0.92f, 0.9f, 0.8f), 0.4f), "Pool");
        UpdatePool();
    }

    private void UpdatePool()
    {
        MovableBowl now = meat.Container;
        if (now != poolBowl) { RebuildPool(); return; }
        if (pool == null || poolBowl == null) return;
        MovableBowl.Interior inside = poolBowl.GetInterior();
        pool.transform.SetPositionAndRotation(inside.floorCenter + inside.up * inside.rim * 0.06f,
            Quaternion.FromToRotation(Vector3.up, inside.up));
    }

    void OnDestroy()
    {
        foreach (GameObject g in shown) if (g != null) Destroy(g);
        foreach (Mesh m in coatMeshes) if (m != null) Destroy(m);
        if (pool != null) Destroy(pool);
        if (coatMat != null) Destroy(coatMat);
    }

    private void Note(string text)
    {
        Debug.Log($"[튀김옷] {text}");
        var go = new GameObject("BatterNote");
        Bounds b = new Bounds(transform.position, Vector3.zero);
        foreach (Renderer r in GetComponentsInChildren<Renderer>()) if (r.enabled) b.Encapsulate(r.bounds);
        go.transform.position = new Vector3(b.center.x, b.max.y + 0.5f, b.min.z - 1.2f);
        TextMesh tm = go.AddComponent<TextMesh>();
        KoreanFontFallback.Apply(tm); // 웹에서도 한글이 보이게 한글 글꼴
        tm.text = text;
        tm.fontSize = 36;
        tm.characterSize = 0.06f;
        tm.anchor = TextAnchor.MiddleCenter;
        tm.color = new Color(0.2f, 0.35f, 0.15f);
        Destroy(go, 3f);
    }
}
