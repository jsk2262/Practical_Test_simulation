using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The fixed floor under a burner's grate (화구 받침 아래), as a cleaning area of
/// its own (with a WipeableSurface on the same object). Cooking on that burner
/// soils it - frying (hot oil) leaves oil spatters, sauce leaves sauce marks -
/// through Soil, called from the cooking code (MovableBowl's frying oil,
/// PanSauce). Never just because 정리 was pressed. The stains lie on this
/// floor, not on the grate: lifting the grate off leaves them where they are,
/// and with the grate on, the floor is covered and can't be wiped
/// (ClothWiper doesn't reach through the grate's parts). Wiped with the
/// cloth, only where it goes. Cooking again after cleaning soils it again.
/// At the final evaluation (ExamEvaluation): soiled and still more dirt than
/// cleanThreshold → the deduction '화구 받침 아래 청소 미완료' (once).
/// </summary>
[RequireComponent(typeof(WipeableSurface))]
public class BurnerFloorDirt : MonoBehaviour
{
    public enum SoilKind { Oil, Sauce }

    [SerializeField] private StoveBurner burner;
    [SerializeField] private BurnerGrate grate;

    [Header("오염 생김")]
    [Tooltip("Seconds of frying / sauce cooking on the burner per new stain (the first comes sooner).")]
    [SerializeField] private float secondsPerStain = 8f;
    [SerializeField] private float firstStainSeconds = 2f;
    [SerializeField] private int maxStains = 8;
    [Tooltip("Where stains land, from the burner's middle (world units): between the burner body and the grate's ring.")]
    [SerializeField] private Vector2 ringRadius = new Vector2(0.62f, 0.9f);

    [Header("청소 완료 · 감점")]
    [Tooltip("Done when the stains left add up to no more than this (1 = one whole stain).")]
    [SerializeField] private float cleanThreshold = 0.25f;
    [Tooltip("Scoring category of the deduction (ExamDeductions).")]
    [SerializeField] private string deductionCategory = ExamDeductions.Hygiene;
    [SerializeField] private string deductionReason = "화구 받침 아래 청소 미완료";

    private static readonly List<BurnerFloorDirt> all = new List<BurnerFloorDirt>();
    private WipeableSurface surface;
    private readonly float[] cooked = new float[2], nextAt = new float[2];


    /// <summary>It's been soiled (cooked on) - so it has to be cleaned.</summary>
    public bool NeedsCleaning { get; private set; }
    public float DirtLeft => surface != null ? surface.DirtLeft : 0f;
    public bool IsClean => DirtLeft <= cleanThreshold;
    public StoveBurner Burner => burner;

    void Awake() => surface = GetComponent<WipeableSurface>();

    void OnEnable()
    {
        all.Add(this);
        ExamEvaluation.Evaluating += Evaluate;
    }

    void OnDisable()
    {
        all.Remove(this);
        ExamEvaluation.Evaluating -= Evaluate;
    }

    /// <summary>Cooking on this burner for dt seconds: frying (Oil) or a sauce (Sauce).
    /// Called every frame it goes on, from the cooking code.</summary>
    public static void Soil(StoveBurner on, SoilKind kind, float dt)
    {
        if (on == null) return;
        foreach (BurnerFloorDirt f in all) if (f.burner == on) f.AddCooking(kind, dt);
    }

    private void AddCooking(SoilKind kind, float dt)
    {
        int k = (int)kind;
        cooked[k] += dt;
        NeedsCleaning = true;
        // the first stain soon, then one every secondsPerStain (up to maxStains on it at once)
        if (nextAt[k] <= 0f) nextAt[k] = firstStainSeconds;
        if (cooked[k] < nextAt[k]) return;
        nextAt[k] += secondsPerStain;
        if (surface.StainCount < maxStains) Spatter(kind);
    }

    private void Spatter(SoilKind kind)
    {
        // a spot round the burner's body, inside the grate's ring, on this floor
        Vector3 up = transform.up;
        Vector3 right = Vector3.ProjectOnPlane(Vector3.right, up).normalized;
        Vector3 back = Vector3.Cross(right, up).normalized;
        Vector3 center = burner != null ? new Plane(up, transform.position).ClosestPointOnPlane(BurnerCenter()) : transform.position;
        // Only where the cloth can get at it once the grate is off - not under
        // the knob or anything else fixed in front of the floor (a stain there
        // could never be wiped off).
        Vector3 p = center;
        Camera cam = Camera.main;
        for (int tries = 0; tries < 12; tries++)
        {
            float a = Random.Range(0f, Mathf.PI * 2f), r = Random.Range(ringRadius.x, ringRadius.y);
            p = center + right * (Mathf.Cos(a) * r) + back * (Mathf.Sin(a) * r);
            Collider over = cam != null ? ClothWiper.Blocker(cam, p) : null;
            if (over == null || over.GetComponent<BurnerGratePart>() != null) break;
        }
        Color c = kind == SoilKind.Oil
            ? (Random.value < 0.3f ? new Color(0.28f, 0.2f, 0.1f, 0.6f) : new Color(0.78f, 0.6f, 0.2f, 0.6f)) // oil, some of it burnt
            : new Color(0.5f, 0.13f, 0.06f, 0.65f);                                                          // sauce
        surface.AddStain(p, Random.Range(0.12f, 0.2f), c, Random.Range(0, int.MaxValue));

        Debug.Log($"[화구 오염] {name}: {(kind == SoilKind.Oil ? "기름 얼룩" : "소스 자국")} (남은 오염 {DirtLeft:0.##})");
    }

    /// <summary>The burner's middle, on the floor (the grate's ring centre if set).</summary>
    private Vector3 BurnerCenter() => grate != null ? grate.HomeCenter : burner.transform.position;

    public static IReadOnlyList<BurnerFloorDirt> All => all;
    public bool GrateInstalled => grate == null || grate.IsInstalled;
    /// <summary>Was clean at some point after being soiled - and soiled again since.</summary>
    public bool WasCleanedOnce { get; private set; }
    public bool SoiledAgainAfterCleaning { get; private set; }

    void Update()
    {
        if (NeedsCleaning && !WasCleanedOnce && IsClean) { WasCleanedOnce = true; Debug.Log($"[화구 청소] {name}: 청소됨 (기록)"); }
        if (WasCleanedOnce && !IsClean) SoiledAgainAfterCleaning = true;
    }

    /// <summary>Judged at the end on its final state only (DishEvaluation - cleaned
    /// once then soiled again and handed in so: not done). Logged here.</summary>
    private void Evaluate()
    {
        Debug.Log($"[최종 평가] {name}: 청소 필요 {NeedsCleaning}, 남은 오염 {DirtLeft:0.##} (완료 기준 {cleanThreshold:0.##} 이하), 받침 원위치 {(GrateInstalled ? "장착됨" : "장착 안 됨")}");
    }

    public float CleanThreshold => cleanThreshold;
    public string DeductionCategory => deductionCategory;
    public string DeductionReason => deductionReason;
}
