using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// 담기 (plating) from the frying pan / wok: once everything in it is cooked
/// and the pan is in hand, a white arrow bobs over each empty serving plate
/// (PlatingPlate1/2); clicking the arrow - or the plate itself - tips the pan
/// and slides the food over onto that plate.
/// Set up automatically at start on the FryingPan and Wok (and PlatingDestination
/// on the serving plates) - nothing to wire in the Inspector.
/// </summary>
[RequireComponent(typeof(MovableBowl))]
public class FryingPanPlating : MonoBehaviour
{
    /// <summary>Vessels that plate this way.</summary>
    public static readonly string[] PanNames = { "FryingPan", "Wok" };

    [Header("Pour animation")]
    [SerializeField] private float tiltDegrees = -35f;
    [SerializeField, Min(0.05f)] private float tiltDuration = 0.3f;
    [SerializeField, Min(0.05f)] private float transferDuration = 0.6f;
    [SerializeField] private float arcHeight = 0.5f;
    [SerializeField] private float arrowHeight = 0.75f;

    public UnityEvent onPlated;

    private MovableBowl pan;
    private bool busy;
    private readonly Dictionary<PlatingDestination, PlatingArrow> arrows = new Dictionary<PlatingDestination, PlatingArrow>();

    public bool IsBusy => busy;

    /// <summary>Every piece (FoodPiece) moved over in the last 담기 - each slid
    /// across on its own, keeping its cooking and sauce.</summary>
    public readonly List<FoodPiece> PlatedPieces = new List<FoodPiece>();

    /// <summary>In hand, with food in it (cooked or not - judged as handed in).</summary>
    public bool CanPlate
    {
        get
        {
            if (busy || pan == null || HandCarry.Instance == null || (object)HandCarry.Instance.Current != pan) return false;
            bool any = false;
            foreach (Carryable c in pan.Contents)
            {
                if (!(c is IngredientItem ing)) continue;
                any = true;
                // (undercooked or burnt, it can still be plated - it's judged as handed in)
            }
            return any;
        }
    }

    public bool CanPlateOnto(MovableBowl plate) =>
        CanPlate && plate != null && plate.GetComponent<PlatingDestination>() is PlatingDestination d && d.CanReceive;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void SetUpScene()
    {
        foreach (MovableBowl b in Object.FindObjectsByType<MovableBowl>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (System.Array.IndexOf(PanNames, b.name) >= 0 && b.GetComponent<FryingPanPlating>() == null) b.gameObject.AddComponent<FryingPanPlating>();
            if (System.Array.IndexOf(PlatingDestination.PlateNames, b.name) >= 0 && b.GetComponent<PlatingDestination>() == null) b.gameObject.AddComponent<PlatingDestination>();
        }
    }

    void Awake() => pan = GetComponent<MovableBowl>();

    void LateUpdate()
    {
        bool can = CanPlate;
        foreach (PlatingDestination d in PlatingDestination.All)
        {
            if (!arrows.TryGetValue(d, out PlatingArrow arrow) || arrow == null)
                arrows[d] = arrow = PlatingArrow.Create(this, d);
            bool show = can && d.CanReceive;
            if (show != arrow.gameObject.activeSelf) arrow.gameObject.SetActive(show);
            if (show)
            {
                Renderer r = d.Plate.GetComponent<Renderer>();
                Bounds b = r != null ? r.bounds : new Bounds(d.transform.position, Vector3.one);
                arrow.Place(new Vector3(b.center.x, b.max.y + arrowHeight * 0.4f, -3.2f));
            }
        }
    }

    void OnDisable()
    {
        foreach (var kv in arrows) if (kv.Value != null) kv.Value.gameObject.SetActive(false);
    }

    /// <summary>Tips the food onto the plate.</summary>
    public void PourInto(PlatingDestination plate)
    {
        if (plate == null || !CanPlateOnto(plate.Plate)) return;
        StartCoroutine(Pour(plate));
    }

    private IEnumerator Pour(PlatingDestination dest)
    {
        busy = true;
        HandCarry hand = HandCarry.Instance;
        if (hand != null) hand.IsLocked = true;
        foreach (var kv in arrows) if (kv.Value != null) kv.Value.gameObject.SetActive(false);

        // The hand brings the pan over beside the plate first.
        Vector3 over = dest.SpotFor(0, 1) + new Vector3(0.9f, 0.6f, 0f);
        HandController.Instance?.GoTo(new Vector3(over.x, over.y, HandController.Instance.transform.position.z), null);
        yield return new WaitForSeconds(0.3f);

        yield return Tilt(0f, 1f);

        var food = new List<IngredientItem>();
        foreach (Carryable c in pan.Contents) if (c is IngredientItem ing) food.Add(ing);
        var starts = new Vector3[food.Count];
        var ends = new Vector3[food.Count];
        PlatedPieces.Clear();
        var pieceFrom = new List<Vector3>();
        var pieceTo = new List<Vector3>();
        for (int i = 0; i < food.Count; i++)
        {
            pan.Release(food[i]);
            food[i].CurrentContainer = null;
            // A cut ingredient's pieces each slide over on their own (one after another).
            if (food[i].GetComponent<FoodPieceGroup>() is FoodPieceGroup group)
            {
                group.SetExternallyMoved(true);
                foreach (FoodPiece p in group.Pieces)
                {
                    PlatedPieces.Add(p);
                    pieceFrom.Add(p.transform.position);
                    Vector2 disc = Random.insideUnitCircle * 0.22f;
                    pieceTo.Add(dest.SpotFor(i, food.Count) + new Vector3(disc.x, disc.y * 0.6f, 0f));
                }
            }
            food[i].transform.SetParent(null, true);
            starts[i] = food[i].transform.position;
            ends[i] = dest.SpotFor(i, food.Count);
        }
        const float stagger = 0.012f;
        float total = transferDuration + stagger * PlatedPieces.Count;
        for (float t = 0f; t < total; t += Time.deltaTime)
        {
            float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / transferDuration));
            for (int i = 0; i < food.Count; i++)
                if (food[i] != null)
                    food[i].transform.position = Vector3.Lerp(starts[i], ends[i], k) + Vector3.up * Mathf.Sin(k * Mathf.PI) * arcHeight;
            for (int j = 0; j < PlatedPieces.Count; j++)
            {
                if (PlatedPieces[j] == null) continue;
                float kj = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((t - j * stagger) / transferDuration));
                PlatedPieces[j].transform.position = Vector3.Lerp(pieceFrom[j], pieceTo[j], kj) + Vector3.up * Mathf.Sin(kj * Mathf.PI) * arcHeight;
            }
            yield return null;
        }
        MovableBowl plate = dest.Plate;
        // The sauce left in the pan goes over with the food.
        PanSauce.Of(pan, false)?.MoveAllTo(plate);
        foreach (IngredientItem ing in food)
        {
            if (ing == null || plate.IsFull) continue;
            plate.PlaceFree(ing, ing.transform.position);
            ing.transform.localScale = ing.BaseLocalScale;
            ing.CurrentContainer = plate;
            ing.OnPlaced();
            // It rode in the pan while the pan was in hand (colliders off with it).
            Collider col = ing.GetComponent<Collider>();
            if (col != null) col.enabled = true;
            // (the pieces settle on the plate on their own from here)
            ing.GetComponent<FoodPieceGroup>()?.SetExternallyMoved(false);
        }
        if (PlatedPieces.Count > 0)
        {
            float minCoat = 1f, maxCoat = 0f;
            foreach (FoodPiece p in PlatedPieces) if (p != null) { minCoat = Mathf.Min(minCoat, p.Coat); maxCoat = Mathf.Max(maxCoat, p.Coat); }
            Debug.Log($"[담기] {plate.name}: 조각 {PlatedPieces.Count}개 (소스 코팅 {minCoat:0.00}~{maxCoat:0.00})");
        }

        yield return Tilt(1f, 0f);
        if (hand != null) hand.IsLocked = false;
        busy = false;
        onPlated?.Invoke();
    }

    private IEnumerator Tilt(float from, float to)
    {
        for (float t = 0f; t < tiltDuration; t += Time.deltaTime)
        {
            pan.SetPourTilt(Quaternion.AngleAxis(tiltDegrees * Mathf.Lerp(from, to, Mathf.SmoothStep(0f, 1f, t / tiltDuration)), Vector3.forward));
            yield return null;
        }
        pan.SetPourTilt(Quaternion.AngleAxis(tiltDegrees * to, Vector3.forward));
    }
}
