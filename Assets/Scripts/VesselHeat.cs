using UnityEngine;

/// <summary>
/// The temperature of a pan/wok/pot itself (°C) - on every cooking vessel,
/// whether there's anything in it or not, so a wok heated empty first
/// (예열) is hot when the oil goes in. On a lit burner it heats toward that
/// flame's level (약·중·강 = how much heat goes in), slower the more is in
/// it; off the flame or lifted it keeps its heat a while (잔열) and cools.
/// What's in it (PanSauce) takes its own temperature from this.
/// A game model tuned by eye - not measured.
/// </summary>
public class VesselHeat : MonoBehaviour
{
    [Header("가열 (튜닝값 - 실측 아님)")]
    [Tooltip("Where the pan's surface heads on each flame (°C).")]
    [SerializeField] private float weakTarget = 185f;
    [SerializeField] private float mediumTarget = 240f;
    [SerializeField] private float strongTarget = 330f;
    [Tooltip("Seconds-scale of the pan heating up (empty).")]
    [SerializeField] private float heatTau = 35f;
    [Tooltip("What's in it slows it: the time scale grows by 1× per this many mL.")]
    [SerializeField] private float loadMlPerTau = 400f;
    [Tooltip("Seconds-scale of cooling off the flame (잔열).")]
    [SerializeField] private float coolTau = 90f;
    [SerializeField] private float roomTemp = 20f;

    public float PanTemp { get; private set; } = 20f;
    /// <summary>mL in it (set by its PanSauce) - the more, the slower it heats.</summary>
    public float LoadMl { get; set; }
    public float RoomTemp => roomTemp;

    private MovableBowl bowl;

    public static VesselHeat Of(MovableBowl b) => b != null ? b.GetComponent<VesselHeat>() : null;

    void Awake()
    {
        bowl = GetComponent<MovableBowl>();
        PanTemp = roomTemp;
    }

    public float Target(StoveBurner.FlameLevel level) =>
        level == StoveBurner.FlameLevel.Strong ? strongTarget : level == StoveBurner.FlameLevel.Medium ? mediumTarget : weakTarget;

    [Tooltip("The pan's own heat, as grams of food it equals (a cold load of this many grams halves the pan's heat above it).")]
    [SerializeField] private float panHeatAsGrams = 600f;

    /// <summary>The food in it (g) - it takes heat too (slower to heat).</summary>
    public float FoodGrams { get; private set; }
    private float nextFoodCheck;

    /// <summary>Something cold put in (g, at °C): the pan's heat shared out -
    /// it drops, more for more food (a handful of cold meat into a hot pan).</summary>
    public void AddColdMass(float grams, float temp)
    {
        if (!(grams > 0f)) return;
        float was = PanTemp;
        PanTemp = (PanTemp * panHeatAsGrams + temp * grams * 0.5f) / (panHeatAsGrams + grams * 0.5f);
        if (was - PanTemp > 3f) Debug.Log($"[팬 온도] {name}: 재료 {grams:0}g 넣음 → {was:0}°C → {PanTemp:0}°C");
    }

    void Update()
    {
        if (bowl == null) return;
        if (Time.time >= nextFoodCheck)
        {
            nextFoodCheck = Time.time + 0.5f;
            float g = 0f;
            foreach (Carryable c in bowl.Contents)
                if (c != null) foreach (FoodPiece p in c.GetComponentsInChildren<FoodPiece>()) g += p.Grams;
            FoodGrams = g;
        }
        StoveBurner b = bowl.CurrentBurner;
        bool flame = !bowl.IsHeld && b != null && b.IsOn;
        float dt = SpeedMode.Wait(Time.deltaTime);
        if (flame)
        {
            float tau = heatTau * (1f + (LoadMl + FoodGrams) / Mathf.Max(loadMlPerTau, 1f));
            PanTemp += (Target(b.Level) - PanTemp) * Mathf.Clamp01(dt / tau);
        }
        else PanTemp += (roomTemp - PanTemp) * Mathf.Clamp01(dt / Mathf.Max(coolTau, 0.1f));
    }
}
