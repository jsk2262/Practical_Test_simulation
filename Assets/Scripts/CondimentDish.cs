using UnityEngine;

/// <summary>
/// A 종지 (small dish) holding a seasoning that doesn't come in a shelf bottle -
/// 감자전분, 고춧가루, 겨자 (powders) and 두반장, 춘장, 굴소스, 케첩 (thick sauces).
/// Used exactly like a shelf bottle (SeasoningBottle): the empty measuring
/// spoon in hand clicking it scoops one spoonful, or the dish carried onto a
/// bowl/ingredient has the spoon dip into it and come out loaded - the dish
/// going back to where it was taken from.
/// 겨자 개기, as in the exam: a spoonful of water (scooped from the measuring
/// cup) goes onto the powder, then the empty spoon stirs it into a stiff paste
/// (갠 겨자); left near a lit burner it ferments (발효 겨자).
/// Set up with Tools > Kitchen > Build Condiment Dishes.
/// </summary>
public class CondimentDish : SeasoningBottle
{
    [SerializeField] private float fillRadius = 0.2f;
    /// <summary>Where the surface sits, in this dish's local space (its model up = local Y).</summary>
    [SerializeField] private Vector3 fillLocalCenter;
    [SerializeField] private Vector3 fillLocalUp = Vector3.up;

    [Header("겨자 (mustard)")]
    [SerializeField] private bool isMustard;
    [SerializeField] private float fermentSeconds = 15f;
    [SerializeField] private float burnerReach = 2.2f;

    [SerializeField] private float stirSeconds = 1.4f;

    public enum MustardState { Powder, Wet, Mixed, Fermented }
    public MustardState Mustard { get; private set; }

    /// <summary>How much is left in the dish (1 = full); 소분 takes half out.</summary>
    public float Amount { get; private set; } = 1f;

    private float fermentTimer;
    private GameObject content;
    private Vector3 homePosition;

    public override string SeasoningName => !isMustard ? base.SeasoningName
        : Mustard == MustardState.Powder || Mustard == MustardState.Wet ? "겨자"
        : Mustard == MustardState.Mixed ? "갠 겨자" : "발효 겨자";

    void Start()
    {
        homePosition = transform.position;
        RebuildContent();
    }

    private void RebuildContent()
    {
        if (content != null) Destroy(content);
        Condiments.Info info = Condiments.Get(SeasoningName);
        Color c = Mustard == MustardState.Fermented ? info.color * 0.85f : info.color;
        c.a = 1f;
        content = Condiments.BuildContent(transform, fillLocalCenter, fillLocalUp, fillRadius * Mathf.Sqrt(Mathf.Max(Amount, 0.15f)), info.form, c);

        // Water just poured on, not yet stirred: a wet film over the powder.
        if (Mustard == MustardState.Wet)
        {
            Condiments.Info water = Condiments.Get(Condiments.Water);
            Condiments.BuildContent(content.transform, Vector3.up * fillRadius * 0.12f, Vector3.up, fillRadius * 0.6f,
                CondimentForm.Liquid, water.color, "WaterFilm");
        }
    }

    public override void OnPickedUp()
    {
        homePosition = transform.position;
        base.OnPickedUp();
    }

    protected override void OnClickedWhileHandFull(Carryable held)
    {
        if (isMustard && held is ToolItem spoon && spoon.Kind == ToolKind.MeasuringSpoon)
        {
            // 1) A spoonful of water onto the powder.
            if (Mustard == MustardState.Powder && spoon.LoadedSeasoning == Condiments.Water)
            {
                spoon.ConsumeSeasoning();
                Mustard = MustardState.Wet;
                RebuildContent();
                return;
            }
            // 2) Stirred with the spoon into a stiff paste (a second spoonful of
            //    water still on it just goes in too).
            if (Mustard == MustardState.Wet && (!spoon.IsSpoonLoaded || spoon.LoadedSeasoning == Condiments.Water))
            {
                if (spoon.IsSpoonLoaded) spoon.ConsumeSeasoning();
                if (!stirring) StartCoroutine(StirRoutine());
                return;
            }
            if (Mustard == MustardState.Wet) return; // loaded with something else: don't scoop the wet mess
        }
        base.OnClickedWhileHandFull(held);
    }

    public override bool CanAcceptDrop(Carryable item)
    {
        if (isMustard && item is ToolItem spoon && spoon.Kind == ToolKind.MeasuringSpoon)
            return Mustard == MustardState.Powder || !spoon.IsSpoonLoaded || spoon.LoadedSeasoning == Condiments.Water;
        return base.CanAcceptDrop(item);
    }

    private bool stirring;

    /// <summary>The spoon goes round and round in the dish (the hand circling),
    /// the powder and water turning into a stiff paste by the end.</summary>
    private System.Collections.IEnumerator StirRoutine()
    {
        stirring = true;
        HandCarry hand = HandCarry.Instance;
        if (hand != null) hand.IsLocked = true;
        HandController right = HandController.Instance;
        yield return new WaitForSeconds(0.3f); // the hand arriving at the dish
        // The spoon's bowl into the middle of the dish, not the hand.
        // (followed every frame: the spoon's bowl - its front - goes round the dish)
        Vector3 reach = Vector3.zero;
        for (float t = 0f; t < stirSeconds; t += Time.deltaTime)
        {
            float a = t / stirSeconds * Mathf.PI * 2f * 3f; // three turns
            Vector3 circle = new Vector3(Mathf.Cos(a), Mathf.Sin(a) * 0.6f, 0f) * fillRadius * 0.5f;
            if (ToolItem.MeasuringSpoon != null)
                reach += ToolItem.TowardSpoonHead(ToolItem.MeasuringSpoon, transform.TransformPoint(fillLocalCenter) + circle, Time.deltaTime);
            right?.SetGestureOffset(reach);
            yield return null;
        }
        right?.SetGestureOffset(Vector3.zero);
        if (hand != null) hand.IsLocked = false;
        Mustard = MustardState.Mixed;
        fermentTimer = 0f;
        RebuildContent();
        stirring = false;
    }

    void Update()
    {
        if (!isMustard || Mustard != MustardState.Mixed || IsHeld) return;
        if (!NearLitBurner()) return;
        fermentTimer += SpeedMode.Wait(Time.deltaTime);
        if (fermentTimer < fermentSeconds) return;
        Mustard = MustardState.Fermented;
        RebuildContent();
    }

    private bool NearLitBurner()
    {
        foreach (StoveBurner b in StoveBurner.All)
        {
            if (b == null || !b.IsOn) continue;
            Vector2 d = b.transform.position - transform.position;
            if (d.magnitude <= burnerReach) return true;
        }
        return false;
    }

    // Back to where it was taken from, after a scoop.
    protected override bool HasHome => true;
    protected override Vector3 HomeWorldPoint() => new Vector3(homePosition.x, homePosition.y, RestZ);
    protected override void ReturnHome() => HandCarry.Instance?.PlaceFreeAt(homePosition);
}
