using UnityEngine;

/// <summary>
/// The station starts clean; while cooking it gets dirty, a little at a time,
/// where it would in a real kitchen:
/// - 물얼룩: drops round the sink while the tap runs;
/// - 자투리 튐: bits flicked off beside the board while chopping;
/// - 기름/소스 튐: splashes round a lit burner with something cooking on it
///   (sauce-coloured when there's sauce in the pan, else oil).
/// Each lands on a WipeableSurface, only where nothing stands, and is wiped
/// off with the cloth (ClothWiper). Never more than MaxStains out at once.
/// </summary>
public class CookingMess : MonoBehaviour
{
    private const int MaxStains = 12;

    [Tooltip("물얼룩: chance per second while a tap runs.")]
    [SerializeField] private float waterPerSecond = 0.2f;
    [Tooltip("기름/소스 튐: chance per second per lit burner with something cooking.")]
    [SerializeField] private float splashPerSecond = 0.12f;
    [Tooltip("자투리 튐: chance per knife chop.")]
    [SerializeField] private float scrapPerChop = 0.07f;

    private static CookingMess instance;
    private WaterTap[] taps;
    private StoveBurner[] burners;
    private float tick;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Create()
    {
        if (FindAnyObjectByType<CookingMess>() != null) return;
        new GameObject("CookingMess").AddComponent<CookingMess>();
    }

    void Awake() => instance = this;

    void Start()
    {
        taps = FindObjectsByType<WaterTap>(FindObjectsSortMode.None);
        burners = FindObjectsByType<StoveBurner>(FindObjectsSortMode.None);
    }

    void Update()
    {
        tick += Time.deltaTime;
        if (tick < 1f) return;
        tick -= 1f;

        foreach (WaterTap tap in taps)
            if (tap != null && tap.IsOn && Random.value < waterPerSecond)
                AroundPoint(tap.StreamPoint, 0.75f, 1.35f, new Color(0.52f, 0.6f, 0.68f, 0.5f), 0.07f, 0.15f);

        foreach (StoveBurner b in burners)
        {
            if (b == null || !b.IsOn || b.CurrentVessel == null) continue;
            MovableBowl v = b.CurrentVessel;
            PanSauce sauce = PanSauce.Of(v, false);
            bool hasSauce = sauce != null && sauce.Amount > 0f;
            if (!(hasSauce || v.HasFryingOil || v.HasIngredients) || Random.value >= splashPerSecond) continue;
            Color c = hasSauce ? new Color(sauce.Color.r * 0.6f, sauce.Color.g * 0.45f, sauce.Color.b * 0.35f, 0.6f)
                               : new Color(0.76f, 0.58f, 0.18f, 0.5f);
            Renderer r = v.GetComponent<Renderer>();
            float reach = r != null ? Mathf.Max(r.bounds.extents.x, r.bounds.extents.y) : 0.6f;
            AroundPoint(b.transform.position, reach + 0.05f, reach + 0.45f, c, 0.04f, hasSauce ? 0.09f : 0.11f);
        }
    }

    /// <summary>A knife chop: now and then a bit flicked off beside the board.</summary>
    public static void Chop(Color ingredient)
    {
        if (instance == null || Random.value >= instance.scrapPerChop) return;
        Collider board = CuttingBoardController.Instance != null ? CuttingBoardController.Instance.GetComponent<Collider>() : null;
        if (board == null) return;
        Bounds b = board.bounds; // (x, y as the camera sees it - all that matters for where it lands)
        Vector3 at = b.center;
        if (Random.value < 0.6f) // (mostly off the sides, where the knife sends them)
        {
            at.x += (Random.value < 0.5f ? -1f : 1f) * (b.extents.x + Random.Range(0.05f, 0.35f));
            at.y += Random.Range(-b.extents.y, b.extents.y) * 0.8f;
        }
        else
        {
            at.y += (Random.value < 0.5f ? -1f : 1f) * (b.extents.y + Random.Range(0.05f, 0.25f));
            at.x += Random.Range(-b.extents.x, b.extents.x) * 0.8f;
        }
        Color bit = new Color(ingredient.r * 0.85f, ingredient.g * 0.85f, ingredient.b * 0.8f, 0.85f);
        Drop(at, bit, 0.025f, 0.05f);
    }

    /// <summary>A few tries at a stain in a ring round a point (as seen from the camera).</summary>
    private static void AroundPoint(Vector3 center, float rMin, float rMax, Color color, float sizeMin, float sizeMax)
    {
        for (int i = 0; i < 4; i++)
            if (Drop(center + (Vector3)(Random.insideUnitCircle.normalized * Random.Range(rMin, rMax)), color, sizeMin, sizeMax)) return;
    }

    /// <summary>A stain on the counter at this point as the camera sees it - if
    /// it's open counter there.</summary>
    public static bool Drop(Vector3 screenAligned, Color color, float sizeMin, float sizeMax)
    {
        Camera cam = Camera.main;
        if (cam == null || FindObjectsByType<SurfaceStain>(FindObjectsSortMode.None).Length >= MaxStains) return false;
        Ray ray = cam.ScreenPointToRay(cam.WorldToScreenPoint(screenAligned));
        foreach (WipeableSurface s in WipeableSurface.All)
        {
            if (!s.Raycast(ray, out Vector3 p, out _)) continue;
            if (ClothWiper.Blocker(cam, p) != null) return false; // under something: not there
            s.AddStain(p, Random.Range(sizeMin, sizeMax), color, Random.Range(0, int.MaxValue));
            return true;
        }
        return false;
    }
}
