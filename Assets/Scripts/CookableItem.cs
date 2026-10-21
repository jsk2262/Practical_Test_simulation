using UnityEngine;

public enum CookProcess { Grill, Blanch, Boil, StirFry, DeepFry }

/// <summary>
/// Unified fire-based cooking for an ingredient sitting in a pot/wok on a burner.
/// Grill/Blanch/Boil/DeepFry progress passively over time while the burner is on.
/// StirFry instead needs the wok actively shaken (see WokDragHandler) - fast
/// dragging builds cookProgress, leaving it still builds burnProgress instead.
/// </summary>
public class CookableItem : MonoBehaviour
{
    /// <summary>Overcooked: left in the boiling water long past done (peas
    /// blanched 40 s+ go olive-brown) - appended last so saved values keep meaning.</summary>
    public enum Stage { Raw, InProgress, Done, Burnt, Overcooked }

    [SerializeField] private CookProcess process = CookProcess.Grill;
    [SerializeField] private StoveBurner burner;
    [SerializeField] private WokDragHandler wok;
    [SerializeField] private float timeToCooked = 6f;
    [SerializeField] private float timeToBurnt = 14f;
    [SerializeField] private float shakeThreshold = 300f;
    [SerializeField] private int requiredFryPasses = 2;

    [Header("Blanch/boil: over-cooking")]
    /// <summary>Total seconds in the boiling water after which a done item is
    /// overcooked (0 = never; it just stays done). It keeps counting while it
    /// stays in the water past done, fading toward overcookedColor over the
    /// last overcookFadeSeconds.</summary>
    [SerializeField] private float timeToOvercooked = 0f;
    [SerializeField] private float overcookFadeSeconds = 12f;
    /// <summary>Color once done; alpha 0 = the shared default (light green for blanching).</summary>
    [SerializeField] private Color doneColor = new Color(0f, 0f, 0f, 0f);
    [SerializeField] private Color overcookedColor = new Color(0.42f, 0.33f, 0.12f);

    [Header("팬 조리 외형 (색 alpha 0 = 재료 종류별 기본값)")]
    [Tooltip("Cooked in a pan (before browning): multiplies its raw colour. Meat default: pale; tofu: as it is; vegetables: a little deeper.")]
    [SerializeField] private Color panCookedColor = new Color(0f, 0f, 0f, 0f);
    [Tooltip("Browned where it lay on the hot pan.")]
    [SerializeField] private Color searColor = new Color(0f, 0f, 0f, 0f);
    [Tooltip("Charred.")]
    [SerializeField] private Color charColor = new Color(0f, 0f, 0f, 0f);

    /// <summary>This ingredient's pan-cooked / browned / charred look (its own
    /// settings, or the default for its kind - not one brown for everything).</summary>
    public void PanLook(Color raw, out Color cooked, out Color sear, out Color burnt)
    {
        IngredientItem ing = GetComponent<IngredientItem>();
        bool tofu = ing != null && ing.IngredientName == "두부";
        IngredientCategory cat = ing != null ? ing.Category : IngredientCategory.None;
        if (panCookedColor.a > 0f) cooked = panCookedColor;
        else if (tofu) cooked = raw;
        else if (cat == IngredientCategory.Meat || cat == IngredientCategory.Seafood) cooked = new Color(0.86f, 0.78f, 0.7f);
        else if (cat == IngredientCategory.Vegetable) cooked = new Color(raw.r * 0.88f, raw.g * 0.92f, raw.b * 0.85f, raw.a);
        else cooked = DoneColor;
        sear = searColor.a > 0f ? searColor
             : tofu ? new Color(0.88f, 0.64f, 0.3f)                                   // golden
             : cat == IngredientCategory.Vegetable ? new Color(0.5f, 0.4f, 0.18f)     // wilted, browned edges
             : new Color(0.62f, 0.38f, 0.18f);                                        // meat
        burnt = charColor.a > 0f ? charColor : new Color(0.13f, 0.09f, 0.07f);
    }

    [Header("Blanch: floats up when done")]
    /// <summary>Lies low in the pot while it blanches and comes up to the
    /// surface once done (두부: about 2 minutes in the boiling water, then it floats).</summary>
    [SerializeField] private bool floatsWhenDone;
    public bool FloatsWhenDone => floatsWhenDone; // 썬 조각도 다 데쳐질 때까지 가라앉아 있게 한다 (FoodPieceGroup)

    [Tooltip("너무 익으면 썬 조각이 줄어들고 표면에 구멍이 숭숭 난다 (두부).")]
    [SerializeField] private bool roughWhenOvercooked; // 너무 익은 모습을 보일 재료인지
    public bool RoughWhenOvercooked => roughWhenOvercooked; // 조각 쪽에서 읽는다

    /// <summary>너무 익은 정도 0..1 - 색이 overcookedColor로 바뀌는 구간과 같다.</summary>
    public float OvercookAt(float progress) =>
        CanOvercook ? Mathf.InverseLerp(Mathf.Max(timeToCooked, timeToOvercooked - overcookFadeSeconds), timeToOvercooked, progress) : 0f; // 색 변화 구간에서 0 → 1

    /// <summary>Where it sits in water, as a fraction of its half height above
    /// the surface (MovableBowl.SinkIntoWater): mostly under while raw/cooking
    /// for a floater, riding high once it's floated up.</summary>
    public float WaterSinkFactor => !floatsWhenDone ? 0.15f
        : CurrentStage == Stage.Done || CurrentStage == Stage.Overcooked ? 0.55f : -0.45f;

    private static readonly Color CookedColor = new Color(0.45f, 0.28f, 0.15f);
    private static readonly Color BurntColor = new Color(0.08f, 0.08f, 0.08f);
    private static readonly Color BlanchedColor = new Color(0.35f, 0.75f, 0.3f);

    private float cookProgress;
    public float CookProgress => cookProgress;
    private float burnProgress;
    private Renderer rend;
    private Color rawColor;

    public Stage CurrentStage { get; private set; } = Stage.Raw;
    public bool IsColdShocked { get; private set; }
    public int FryPasses { get; private set; }

    /// <summary>실행 중에 붙인 것 (익는 설정이 없던 채소 - IngredientItem.EnsurePieceGroup):
    /// 팬에서 볶이고, 익은 색은 제 색보다 살짝 숨이 죽은 정도.</summary>
    public void SetupAsVegetable(Color raw)
    {
        process = CookProcess.Grill;
        doneColor = new Color(raw.r * 0.86f, raw.g * 0.9f, raw.b * 0.82f, 1f);
    }

    /// <summary>Takes over another's cooking state (a piece split off from it).</summary>
    public void CopyFrom(CookableItem o)
    {
        CurrentStage = o.CurrentStage; FryPasses = o.FryPasses;
        cookProgress = o.cookProgress; burnProgress = o.burnProgress; IsColdShocked = o.IsColdShocked;
    }

    /// <summary>Which burner (if any) this item's cooking progresses under -
    /// set dynamically by whatever pot/pan/wok it's currently sitting in, since
    /// that vessel can be picked up and moved between burners rather than being
    /// wired to one fixed burner at design time.</summary>
    public void SetBurner(StoveBurner newBurner)
    {
        burner = newBurner;
    }

    public void SetWok(WokDragHandler newWok)
    {
        wok = newWok;
    }

    /// <summary>The pot/bowl this item is sitting in (set by MovableBowl).</summary>
    public void SetVessel(MovableBowl newVessel)
    {
        vessel = newVessel;
    }

    private MovableBowl vessel;

    /// <summary>Blanching/boiling happens in boiling water, not over a bare
    /// flame - until now a pea in a dry pot on a lit burner "blanched" too.</summary>
    private bool NeedsBoilingWater => process == CookProcess.Blanch || process == CookProcess.Boil;

    void Start()
    {
        rend = GetComponent<Renderer>();
        rawColor = rend.sharedMaterial.color;
        rend.material = new Material(rend.sharedMaterial);
    }

    private bool CanOvercook => NeedsBoilingWater && timeToOvercooked > timeToCooked;

    public float TimeToCooked => timeToCooked;

    /// <summary>The color after this much cooking (raw, done, then fading to
    /// overcooked) - a cut piece's own color (FoodPiece).</summary>
    public Color ColorAt(float progress)
    {
        if (rend == null) Start();
        if (progress < timeToCooked) return Color.Lerp(rawColor, DoneColor, Mathf.Clamp01(progress / Mathf.Max(timeToCooked, 0.01f)));
        if (!CanOvercook) return DoneColor;
        float fadeStart = Mathf.Max(timeToCooked, timeToOvercooked - overcookFadeSeconds);
        return Color.Lerp(DoneColor, overcookedColor, Mathf.InverseLerp(fadeStart, timeToOvercooked, progress));
    }

    /// <summary>Cut into independent pieces (FoodPieceGroup): each piece cooks by
    /// the heat that reaches it; the whole is done when its least-cooked piece is.</summary>
    private void UpdatePieces(FoodPieceGroup group)
    {
        if (CurrentStage == Stage.Burnt || CurrentStage == Stage.Overcooked || IsColdShocked) return;
        // In a pan / wok / frying oil the pieces cook each by its own heat
        // (FoodPieceGroup: on the pan's floor or up on the others, stirred or
        // not) - here only the stage is read from them: done when the
        // least-cooked piece is, burnt when they've mostly charred.
        if (group.InDryHeat)
        {
            cookProgress = group.MinCook;
            if (CurrentStage == Stage.Raw && cookProgress > 0.05f) { CheckDrainedBeforeCooking(); CurrentStage = Stage.InProgress; }
            if (CurrentStage == Stage.InProgress && cookProgress >= timeToCooked)
            {
                CurrentStage = Stage.Done;
                Debug.Log($"{name}: 팬에서 익음 (조각 {group.Count}개 모두)");
            }
            if (group.AverageChar >= 0.6f) { CurrentStage = Stage.Burnt; Debug.Log($"{name}: 탐 (조각 대부분이 눌어붙어 탐)"); }
            return;
        }
        if (burner == null || !burner.IsOn) return;
        if (NeedsBoilingWater && (vessel == null || !vessel.IsBoiling)) return;
        if (process == CookProcess.DeepFry && (vessel == null || !vessel.IsOilHot)) return;
        if (process == CookProcess.StirFry) return; // the pieces aren't stir-fried as a whole

        foreach (FoodPiece p in group.Pieces) p.Cook += SpeedMode.Wait(Time.deltaTime) * group.HeatFactor(p);
        cookProgress = group.MinCook;

        if (CurrentStage == Stage.Raw) { CheckDrainedBeforeCooking(); CurrentStage = Stage.InProgress; }
        if (CurrentStage == Stage.InProgress && cookProgress >= timeToCooked)
        {
            CurrentStage = Stage.Done;
            Debug.Log($"{name}: 데치기 완료 (조각 {group.Count}개 모두)");
        }
        if (CurrentStage == Stage.Done && CanOvercook && cookProgress >= timeToOvercooked) CurrentStage = Stage.Overcooked;
    }

    private FoodPieceGroup pieceGroup;

    void Update()
    {
        if (pieceGroup == null) pieceGroup = GetComponent<FoodPieceGroup>();
        if (pieceGroup != null && pieceGroup.Count > 0) { UpdatePieces(pieceGroup); return; }
        if (CurrentStage == Stage.Burnt || CurrentStage == Stage.Overcooked) return;
        if (CurrentStage == Stage.Done && (!CanOvercook || IsColdShocked)) return;
        if (burner == null || !burner.IsOn) return;
        if (NeedsBoilingWater && (vessel == null || !vessel.IsBoiling)) return;
        // Deep-frying only once the oil is at frying heat (the batter-drop test).
        if (process == CookProcess.DeepFry && (vessel == null || !vessel.IsOilHot)) return;

        if (CurrentStage == Stage.Done)
        {
            // Still sitting in the boiling water past done.
            if (SpeedMode.On) return; // (빠른 모드: done stays done - no overcooking from the sped-up wait)
            cookProgress += Time.deltaTime;
            float fadeStart = Mathf.Max(timeToCooked, timeToOvercooked - overcookFadeSeconds);
            float f = Mathf.InverseLerp(fadeStart, timeToOvercooked, cookProgress);
            SetColor(Color.Lerp(DoneColor, overcookedColor, f));
            if (cookProgress >= timeToOvercooked) CurrentStage = Stage.Overcooked;
            return;
        }

        if (process == CookProcess.StirFry)
        {
            float shakeSpeed = wok != null ? wok.ShakeSpeed : 0f;
            if (shakeSpeed >= shakeThreshold) cookProgress += SpeedMode.Wait(Time.deltaTime);
            else if (!SpeedMode.On) burnProgress += Time.deltaTime; // (빠른 모드: no burning)
        }
        else
        {
            cookProgress += SpeedMode.Wait(Time.deltaTime);
        }

        if (CurrentStage == Stage.Raw) CheckDrainedBeforeCooking();
        CurrentStage = Stage.InProgress;
        UpdateVisual();

        if (burnProgress >= timeToBurnt)
        {
            CurrentStage = Stage.Burnt;
            SetColor(BurntColor);
            return;
        }

        if (cookProgress >= timeToCooked)
        {
            FryPasses++;

            if (process == CookProcess.DeepFry && FryPasses < requiredFryPasses)
            {
                cookProgress = 0f; // needs another pass in the oil
                return;
            }

            CurrentStage = Stage.Done;
            if (floatsWhenDone && vessel != null)
            {
                vessel.Refloat(GetComponent<Carryable>());
                Debug.Log($"{name}: 떠올라 데치기 완료");
            }
        }
    }

    /// <summary>Meat/tofu that should have drained on a paper towel first
    /// (IngredientItem.drainOnTowel) but is going on the heat without it.</summary>
    private void CheckDrainedBeforeCooking()
    {
        IngredientItem ing = GetComponent<IngredientItem>();
        if (ing == null || !ing.NeedsTowelDrain) return;
        string what = ing.DrainOnTowel == IngredientItem.DrainNeed.Blood ? "핏물" : "물기";
        ExamDeductions.Add($"{ing.IngredientName}: 키친타올로 {what} 제거 없이 조리");
    }

    private Color DoneColor => doneColor.a > 0f ? doneColor
        : (process == CookProcess.Blanch || process == CookProcess.Boil) ? BlanchedColor : CookedColor;

    private void UpdateVisual()
    {
        float t = Mathf.Clamp01(cookProgress / timeToCooked);
        SetColor(Color.Lerp(rawColor, DoneColor, t));
    }

    /// <summary>The item, and its cut pieces if it's been cut (those render
    /// instead of it, from their own copy of its material).</summary>
    private void SetColor(Color c)
    {
        rend.material.color = c;
        foreach (Transform child in GetComponentsInChildren<Transform>()) // (also under a "Pieces" holder)
            if (child.name.StartsWith("Piece_") && child.GetComponent<FoodPiece>() == null // (those color themselves)
                && child.TryGetComponent(out Renderer r)) r.material.color = c;
    }

    /// <summary>Shocks a blanched/boiled item in cold water, locking in its color.</summary>
    public void ColdShock()
    {
        if (process != CookProcess.Blanch && process != CookProcess.Boil) return;
        if (CurrentStage != Stage.Done) return;

        IsColdShocked = true;
        SetColor(Color.Lerp(rend.material.color, Color.white, 0.15f));
    }
}
