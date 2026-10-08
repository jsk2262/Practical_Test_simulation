using UnityEngine;

/// <summary>
/// A seasoning measured into a bowl spoon by spoon (계량스푼). For 감자전분 it
/// then goes the exam's 앙금녹말 way (for 튀김):
///   Powder  - 감자전분 spooned in (3 spoons: the 종지 used on the empty bowl
///             loads the measuring spoon, the spoon tips it in)
///   Wet     - water spooned in from the measuring cup (3 spoons)
///   Slurry  - stirred once with the (empty) measuring spoon: milky 전분물
///   Settled - left standing a long while (10 min): the starch sinks, water on top
///   Drained - the water poured off at the sink (WashBasin): thick 앙금녹말, ready to use
/// Picking the bowl up while it settles stirs it back up (the wait starts over).
/// </summary>
public class PortionedCondiment : MonoBehaviour
{
    public enum Stage { Powder, Wet, Slurry, Settled, Drained }

    public string CondimentName { get; private set; }
    /// <summary>Spoonfuls of the seasoning in the bowl.</summary>
    public int Spoons { get; private set; }
    public int WaterSpoons { get; private set; }
    public Stage CurrentStage { get; private set; }
    public bool Removed { get; private set; }

    public const string Starch = "감자전분";
    public const string SettledStarch = "앙금녹말";
    /// <summary>The usual amounts: 3 spoons of starch, 3 of water.</summary>
    public const int UsualSpoons = 3;
    private const float SlurryWater = 0.25f;

    /// <summary>How long the stirred starch water has to stand to settle.</summary>
    [SerializeField] private float settleSeconds = 600f;
    [SerializeField] private float stirSeconds = 1.2f;

    private MovableBowl bowl;
    private GameObject content, surface;
    private float settleTimer;
    private bool stirring;

    private bool IsStarch => CondimentName == Starch;
    public float SettleProgress => CurrentStage == Stage.Slurry ? Mathf.Clamp01(settleTimer / settleSeconds) : CurrentStage > Stage.Slurry ? 1f : 0f;

    public void Init(MovableBowl bowl, string condiment)
    {
        this.bowl = bowl;
        CondimentName = condiment;
        CurrentStage = Stage.Powder;
    }

    /// <summary>One measuring spoon of the seasoning tipped in. False once it's
    /// been mixed (or it's a different seasoning) - the spoon then works as usual.</summary>
    public bool AddSpoon(string seasoning)
    {
        if (Removed || seasoning != CondimentName || CurrentStage > Stage.Wet) return false;
        Spoons++;
        Rebuild();
        return true;
    }

    /// <summary>One spoonful of water (from the measuring cup) onto the starch.</summary>
    public bool AddSpoonOfWater()
    {
        if (!IsStarch || Removed || Spoons == 0 || CurrentStage > Stage.Wet) return false;
        WaterSpoons++;
        CurrentStage = Stage.Wet;
        Rebuild();
        return true;
    }

    /// <summary>Stirred once with the empty measuring spoon: starch and water mixed.</summary>
    public bool TryStir()
    {
        if (!IsStarch || Removed || stirring || CurrentStage != Stage.Wet) return false;
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
            if (ToolItem.MeasuringSpoon != null)
                reach += ToolItem.TowardSpoonHead(ToolItem.MeasuringSpoon, bowl.BodyCenterWorld() + circle, Time.deltaTime);
            right?.SetGestureOffset(reach);
            yield return null;
        }
        right?.SetGestureOffset(Vector3.zero);
        if (hand != null) hand.IsLocked = false;
        CurrentStage = Stage.Slurry;
        settleTimer = 0f;
        stirring = false;
        Rebuild();
    }

    void Update()
    {
        if (bowl == null || Removed || !IsStarch) return;

        // Plenty of water from the faucet works too (it's mixed by the stream).
        if (CurrentStage <= Stage.Wet && Spoons > 0 && bowl.WaterAmount >= SlurryWater)
        {
            CurrentStage = Stage.Slurry;
            settleTimer = 0f;
            Rebuild();
        }
        else if (CurrentStage == Stage.Slurry)
        {
            // Carried around, it's stirred up again: settling only counts while it stands.
            if (bowl.IsHeld) { settleTimer = 0f; return; }
            settleTimer += Time.deltaTime;
            if (settleTimer >= settleSeconds)
            {
                CurrentStage = Stage.Settled;
                Rebuild();
            }
        }

        // The milky surface rides on faucet water (which only appears once filling starts).
        if (CurrentStage == Stage.Slurry && bowl.WaterAmount > 0f && surface == null && bowl.WaterSurface != null) Rebuild();
    }

    /// <summary>At the sink with the bowl in hand: the water over the settled
    /// starch is poured off, leaving thick 앙금녹말. False if it isn't settled yet.</summary>
    public bool TryDrain()
    {
        if (!IsStarch || CurrentStage != Stage.Settled) return false;
        bowl.PourOffWater();
        WaterSpoons = 0;
        CurrentStage = Stage.Drained;
        bowl.AddSeasoning(SettledStarch);
        Rebuild();
        return true;
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
        float radius = full * Mathf.Sqrt(Mathf.Clamp01(Mathf.Max(Spoons, 1) / (float)UsualSpoons));
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
                    radius * Mathf.Min(1.3f, 0.75f + 0.2f * WaterSpoons), CondimentForm.Liquid, water, "Water");
                break;
            case Stage.Slurry when bowl.WaterAmount > 0f:
                if (bowl.WaterSurface != null)
                    surface = Condiments.BuildContent(bowl.WaterSurface, new Vector3(0f, 0.6f, 0f), Vector3.up, 0.5f,
                        CondimentForm.Liquid, new Color(0.93f, 0.94f, 0.93f), "Milky");
                break;
            case Stage.Slurry:
                // Mixed: milky 전분물.
                content = Condiments.BuildContent(bowl.transform, bottom + Vector3.up * radius * 0.1f, Vector3.up, radius * 1.3f, CondimentForm.Liquid, new Color(0.84f, 0.87f, 0.9f), "StarchSlurry");
                break;
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
