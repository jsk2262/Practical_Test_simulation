using UnityEngine;

/// <summary>
/// A seasoning measured into a bowl spoon by spoon (계량스푼). For 감자전분 it
/// then goes the exam's 앙금녹말 way (for 튀김):
///   Powder  - 감자전분 spooned in (3 spoons: the 종지 used on the empty bowl
///             loads the measuring spoon, the spoon tips it in)
///   Wet     - water spooned in from the measuring cup (3 spoons)
///   Slurry  - stirred with the (empty) measuring spoon: milky 전분물 - and from
///             the end of the stirring, the settling time counts (the bowl's
///             starchSettleMinutes: 5 min, or 10), whatever else is being
///             cooked meanwhile; the starch sinks bit by bit (white layer
///             growing at the bottom, the water above clearing)
///   Settled - the time's up: starch at the bottom, clear water on top
///   Drained - the water poured off at the sink (WashBasin): thick 앙금녹말, ready to use
/// Stirred again (milky or settled), the wait starts over. Just picking the
/// bowl up or moving it doesn't. Paused (timeScale 0), it waits too.
/// </summary>
public class PortionedCondiment : MonoBehaviour
{
    public enum Stage { Powder, Wet, Slurry, Settled, Drained }

    public string CondimentName { get; private set; }
    /// <summary>mL of the seasoning in the bowl (큰술 1T = 15 mL).</summary>
    public float StarchMl { get; private set; }
    public float WaterMl { get; private set; }
    /// <summary>The 앙금녹말 left to spoon out once drained (mL).</summary>
    public float SettledMl { get; private set; }
    /// <summary>The 앙금 there was once drained (mL) - and how much of it has been spooned out.</summary>
    public float SettledGivenMl { get; private set; }
    public float SettledUsedMl => Mathf.Max(0f, SettledGivenMl - SettledMl);
    public Stage CurrentStage { get; private set; }
    public bool Removed { get; private set; }

    public const string Starch = "감자전분";
    public const string SettledStarch = "앙금녹말";
    /// <summary>Spooned up settled but with its water still on top: the 앙금 and water together.</summary>
    public const string WetSettledStarch = "물기 많은 앙금";
    /// <summary>The usual amounts: 3 큰술 (45 mL) of starch, 3 of water.</summary>
    public const float UsualMl = 3f * ToolItem.TablespoonMl;
    private const float SlurryWater = 0.25f;

    /// <summary>How long the stirred starch water has to stand to settle (set
    /// from the bowl's starchSettleMinutes).</summary>
    [SerializeField] private float settleSeconds = 300f;
    [SerializeField] private float stirSeconds = 1.2f;
    private int shownStep = -1; // (settling shown in tenths)

    /// <summary>Seconds left to settle (0 when not settling).</summary>
    public float SettleSecondsLeft => CurrentStage == Stage.Slurry ? Mathf.Max(0f, settleSeconds - settleTimer) : 0f;

    private MovableBowl bowl;
    private GameObject content, surface;
    private float settleTimer;
    private bool stirring;

    private bool IsStarch => CondimentName == Starch;
    public float SettleProgress => CurrentStage == Stage.Slurry ? Mathf.Clamp01(settleTimer / settleSeconds) : CurrentStage > Stage.Slurry ? 1f : 0f;

    public void Init(MovableBowl bowl, string condiment, float settleMinutes = 5f)
    {
        this.bowl = bowl;
        CondimentName = condiment;
        CurrentStage = Stage.Powder;
        settleSeconds = Mathf.Max(0.1f, settleMinutes) * 60f;
    }

    /// <summary>One measuring spoon of the seasoning tipped in. False once it's
    /// been mixed (or it's a different seasoning) - the spoon then works as usual.</summary>
    public bool AddSpoon(string seasoning, float ml)
    {
        if (Removed || seasoning != CondimentName || CurrentStage >= Stage.Drained) return false;
        StarchMl += Mathf.Max(0f, ml);
        if (CurrentStage == Stage.Powder && WaterMl > 0f) CurrentStage = Stage.Wet; // (water went in first)
        Disturbed("전분 추가");
        Rebuild();
        return true;
    }

    /// <summary>Water onto the starch - a spoonful, or a cup poured in. Before
    /// the starch (water first) too.</summary>
    public bool AddSpoonOfWater(float ml)
    {
        if (!IsStarch || Removed || CurrentStage >= Stage.Drained) return false;
        WaterMl += Mathf.Max(0f, ml);
        if (CurrentStage == Stage.Powder && StarchMl > 0f) CurrentStage = Stage.Wet;
        Disturbed("물 추가");
        Rebuild();
        return true;
    }

    /// <summary>All of it poured out (into a pan): what kind of starch it is -
    /// dry powder, drained 앙금, or 전분물 (starch in its water) - and how
    /// much starch and water, kept apart. The bowl is empty after.</summary>
    public void TakeAllForPour(out string starchKind, out float starchMl, out float waterMl)
    {
        float ratio = KitchenDevSettings.Current.settledMlPerDryStarchMl;
        switch (CurrentStage)
        {
            case Stage.Powder: starchKind = PanSauce.DryStarch; starchMl = StarchMl; waterMl = WaterMl; break;
            case Stage.Drained: starchKind = PanSauce.Settled; starchMl = SettledMl; waterMl = 0f; break;
            default: starchKind = PanSauce.SlurryStarch; starchMl = StarchMl * ratio; waterMl = WaterMl; break;
        }
        Remove();
    }

    /// <summary>Dry starch taken back out with the spoon (no water in it yet).
    /// All of it gone: the bowl's empty again.</summary>
    public void TakeDry(float ml)
    {
        if (CurrentStage != Stage.Powder || !(ml > 0f)) return;
        StarchMl = Mathf.Max(0f, StarchMl - ml);
        if (StarchMl <= 0.05f && WaterMl <= 0f) { Remove(); return; }
        Rebuild();
    }

    /// <summary>More put in after it was stirred: not mixed through any more -
    /// it wants stirring again (and the settling starts over).</summary>
    private void Disturbed(string what)
    {
        if (CurrentStage != Stage.Slurry && CurrentStage != Stage.Settled) return;
        CurrentStage = WaterMl > 0f || bowl.WaterAmount > 0f ? Stage.Wet : Stage.Powder;
        settleTimer = 0f;
        Debug.Log($"[앙금녹말] {bowl.name}: {what} - 다시 저어야 함 (침전 처음부터)");
    }

    /// <summary>Stirred with the empty measuring spoon: starch and water mixed - or,
    /// milky or already settled, stirred up again (the settling starts over).</summary>
    public bool TryStir()
    {
        if (!IsStarch || Removed || stirring || CurrentStage < Stage.Wet || CurrentStage > Stage.Settled) return false;
        StartCoroutine(Stir());
        return true;
    }

    private System.Collections.IEnumerator Stir()
    {
        stirring = true;
        HandCarry hand = HandCarry.Instance;
        if (hand != null) hand.IsLocked = true;
        HandController right = HandController.Instance;
        yield return new WaitForSeconds(0.3f); // the hand arriving at the bowl
        // The spoon's bowl - not the hand - goes into the middle of the bowl
        // (stirring by the hand's spot had the spoon whisking the air beside it).
        // (followed every frame, so the spoon's bowl - its front - stays in the
        // middle of the bowl, not the handle end the hand holds)
        Vector3 reach = Vector3.zero;
        for (float t = 0f; t < stirSeconds; t += Time.deltaTime)
        {
            float a = t / stirSeconds * Mathf.PI * 2f * 3f; // three turns
            Vector3 circle = new Vector3(Mathf.Cos(a), Mathf.Sin(a) * 0.6f, 0f) * 0.06f;
            if (ToolItem.MeasuringSpoon != null && HandCarry.Instance != null && HandCarry.Instance.Current == ToolItem.MeasuringSpoon)
                reach += ToolItem.TowardSpoonHead(ToolItem.MeasuringSpoon, bowl.BodyCenterWorld() + circle, Time.deltaTime);
            right?.SetGestureOffset(reach);
            yield return null;
        }
        right?.SetGestureOffset(Vector3.zero);
        if (hand != null) hand.IsLocked = false;
        // the stirring's done: the settling counts from now (from zero again if it was stirred up)
        CurrentStage = Stage.Slurry;
        settleTimer = 0f;
        stirring = false;
        Rebuild();
        Debug.Log($"[앙금녹말] {bowl.name}: 저음 - 침전 {settleSeconds / 60f:0.#}분 시작");
    }

    void Update()
    {
        if (bowl == null || Removed || !IsStarch) return;

        // Plenty of water from the faucet works too (it's mixed by the stream).
        if (CurrentStage <= Stage.Wet && StarchMl > 0f && bowl.WaterAmount >= SlurryWater)
        {
            CurrentStage = Stage.Slurry;
            settleTimer = 0f;
            Rebuild();
        }
        else if (CurrentStage == Stage.Slurry && !stirring)
        {
            // Game time (paused: it waits too), whatever else goes on meanwhile -
            // picked up or moved, it keeps settling; only stirring starts it over.
            settleTimer += SpeedMode.On ? settleSeconds : Time.deltaTime; // (빠른 모드: settles at once)
            if (settleTimer >= settleSeconds)
            {
                CurrentStage = Stage.Settled;
                Rebuild();
                Debug.Log($"[앙금녹말] {bowl.name}: 침전 완료 - 개수대에서 윗물을 버리세요");
            }
            else if (Mathf.FloorToInt(SettleProgress * 10f) != shownStep) Rebuild(); // (the starch sinking, a tenth at a time)
        }

        // The milky surface rides on faucet water (which only appears once filling starts).
        if (CurrentStage == Stage.Slurry && bowl.WaterAmount > 0f && surface == null && bowl.WaterSurface != null) Rebuild();
    }

    /// <summary>At the sink with the bowl in hand: the water over the settled
    /// starch is poured off, leaving thick 앙금녹말. False if it isn't settled yet.</summary>
    public bool TryDrain()
    {
        if (!IsStarch || Removed || StarchMl <= 0f) return false;
        if (CurrentStage == Stage.Powder || CurrentStage == Stage.Drained) return false;
        if (CurrentStage == Stage.Wet)
        {
            // water on top, never stirred in: it just runs off - the powder stays
            bowl.PourOffWater();
            WaterMl = 0f;
            CurrentStage = Stage.Powder;
            Rebuild();
            Debug.Log($"[앙금녹말] {bowl.name}: 섞지 않은 물만 따라 버림 - 전분 가루가 남음");
            return true;
        }
        // Poured off before it had settled: the starch still in the milky water
        // goes with it - recorded (DrainedEarly).
        float kept = CurrentStage == Stage.Settled ? 1f : Mathf.Lerp(0.3f, 1f, SettleProgress);
        if (kept < 1f)
        {
            DrainedEarly = true;
            Debug.Log($"[앙금녹말] {bowl.name}: 덜 가라앉은 채 물을 버림 - 전분 {(1f - kept) * 100f:0}%가 물과 함께 버려짐");
        }
        StarchMl *= kept;
        bowl.PourOffWater();
        WaterMl = 0f;
        // (가정 - KitchenDevSettings: the 앙금 left is always as wet, so its mL
        // follows the dry starch put in by a fixed ratio; not measured yet)
        SettledGivenMl = SettledMl = StarchMl * KitchenDevSettings.Current.settledMlPerDryStarchMl;
        CurrentStage = Stage.Drained;
        // (not also added to the bowl's seasonings: the 앙금 left is SettledMl - counted once)
        Rebuild();
        return true;
    }

    /// <summary>Settled, water not poured off: what a spoon can take up (the
    /// 앙금 and the water over it together, mL).</summary>
    public float UndrainedMl => CurrentStage == Stage.Settled ? StarchMl * KitchenDevSettings.Current.settledMlPerDryStarchMl + WaterMl : 0f;

    /// <summary>A spoonful of the undrained 앙금 taken: starch and water both
    /// go down by their share - what's left is still undrained.</summary>
    public void TakeUndrained(float ml)
    {
        float total = UndrainedMl;
        if (total <= 0f || !(ml > 0f)) return;
        float k = Mathf.Clamp01(1f - ml / total);
        StarchMl *= k;
        WaterMl *= k;
        WetUsedMl += ml;
        Rebuild();
    }

    /// <summary>Spooned out before the water was poured off (mL) - recorded.</summary>
    public float WetUsedMl { get; private set; }
    /// <summary>Its water poured off before it had settled (starch lost with it) - recorded.</summary>
    public bool DrainedEarly { get; private set; }

    /// <summary>Some of the drained 앙금녹말 spooned out (mL, no more than is left).</summary>
    public void TakeSettled(float ml)
    {
        if (CurrentStage != Stage.Drained || !(ml > 0f)) return;
        SettledMl = Mathf.Max(0f, SettledMl - ml);
        Rebuild();
    }

    /// <summary>Washed away (clean-up).</summary>
    public void Remove()
    {
        Removed = true;
        if (content != null) Destroy(content);
        if (surface != null) Destroy(surface);
        Destroy(this);
    }

    private void Rebuild()
    {
        if (content != null) Destroy(content);
        if (surface != null) Destroy(surface);
        content = surface = null;

        Renderer r = bowl.GetComponent<Renderer>();
        if (r == null) return;
        Bounds lb = r.localBounds;
        float full = Mathf.Min(lb.size.x, lb.size.z) * 0.5f * 0.62f;
        float radius = full * Mathf.Sqrt(Mathf.Clamp01(Mathf.Max(CurrentStage == Stage.Drained ? SettledMl : StarchMl, ToolItem.TeaspoonMl) / UsualMl));
        // Mid-height: lower down, the bowl's front wall hid it all on the tilted view.
        Vector3 bottom = lb.center + Vector3.down * lb.size.y * 0.05f;
        Color white = new Color(0.97f, 0.97f, 0.95f);
        Color water = new Color(0.62f, 0.78f, 0.92f);

        switch (CurrentStage)
        {
            case Stage.Powder:
                Condiments.Info info = Condiments.Get(CondimentName);
                content = Condiments.BuildContent(bowl.transform, bottom, Vector3.up, radius, info.form, info.color, "Portion");
                break;
            case Stage.Wet:
                // Water poured on, not mixed yet: the powder slumps into a low wet
                // mound with a pool of water spreading round it (wider each spoon),
                // only the top of the mound showing above it.
                content = Condiments.BuildContent(bowl.transform, bottom, Vector3.up, radius, CondimentForm.Paste, new Color(0.9f, 0.91f, 0.92f), "Portion");
                Condiments.BuildContent(content.transform, Vector3.up * radius * 0.14f, Vector3.up,
                    radius * Mathf.Min(1.3f, 0.75f + 0.2f * WaterMl / ToolItem.TablespoonMl), CondimentForm.Liquid, water, "Water");
                break;
            case Stage.Slurry when bowl.WaterAmount > 0f:
                if (bowl.WaterSurface != null)
                    surface = Condiments.BuildContent(bowl.WaterSurface, new Vector3(0f, 0.6f, 0f), Vector3.up, 0.5f,
                        CondimentForm.Liquid, new Color(0.93f, 0.94f, 0.93f), "Milky");
                break;
            case Stage.Slurry:
            {
                // Mixed: milky 전분물 - settling as it stands: the white starch
                // layer at the bottom spreading, the water above it clearing.
                float p = SettleProgress;
                shownStep = Mathf.FloorToInt(p * 10f);
                Color milky = new Color(0.84f, 0.87f, 0.9f);
                content = Condiments.BuildContent(bowl.transform, bottom + Vector3.up * radius * 0.1f, Vector3.up, radius * 1.3f,
                    CondimentForm.Liquid, Color.Lerp(milky, water, p * 0.85f), "StarchSlurry");
                if (p > 0.05f)
                    Condiments.BuildContent(content.transform, Vector3.down * radius * 0.06f, Vector3.up, radius * 1.15f * Mathf.Sqrt(p),
                        CondimentForm.Liquid, white, "StarchSinking");
                break;
            }
            case Stage.Settled:
                // White starch sunk to the bottom, clear water over it.
                content = Condiments.BuildContent(bowl.transform, bottom, Vector3.up, radius * 1.15f, CondimentForm.Liquid, white, "StarchLayer");
                if (bowl.WaterAmount <= 0f)
                    Condiments.BuildContent(content.transform, Vector3.up * radius * 0.12f, Vector3.up, radius * 1.3f, CondimentForm.Liquid, water, "ClearWater");
                break;
            case Stage.Drained:
                content = Condiments.BuildContent(bowl.transform, bottom, Vector3.up, radius * 1.05f, CondimentForm.Paste, white, "SettledStarch");
                break;
        }
    }
}
