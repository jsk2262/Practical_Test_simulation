using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A plate the finished dish is served onto (완성 접시: PlatingPlate1/2). Added to
/// those plates automatically (FryingPanPlating) - nothing to wire by hand.
/// Takes one pan's worth of food: empty -> it can receive; once served onto,
/// it's taken until emptied again.
/// </summary>
[RequireComponent(typeof(MovableBowl))]
public class PlatingDestination : MonoBehaviour
{
    /// <summary>Plates with these names are serving plates.</summary>
    public static readonly string[] PlateNames = { "PlatingPlate1", "PlatingPlate2" };

    public static readonly List<PlatingDestination> All = new List<PlatingDestination>();

    public MovableBowl Plate { get; private set; }

    /// <summary>Empty, on the counter (not in hand, not on a shelf), ready for food.</summary>
    public bool CanReceive
    {
        get
        {
            if (Plate == null || !isActiveAndEnabled || Plate.IsHeld) return false;
            foreach (Carryable c in Plate.Contents) return false;
            return !(Plate.CurrentContainer is DishShelfController);
        }
    }

    void Awake() => Plate = GetComponent<MovableBowl>();
    void OnEnable() => All.Add(this);
    void OnDisable() => All.Remove(this);

    /// <summary>Where the index-th of count pieces lands (world): a sunflower
    /// spread round the middle of the plate.</summary>
    public Vector3 SpotFor(int index, int count)
    {
        Renderer r = Plate.GetComponent<Renderer>();
        Vector3 center = r != null ? r.bounds.center : transform.position;
        float radius = r != null ? Mathf.Min(r.bounds.extents.x, r.bounds.extents.y) * 0.45f : 0.3f;
        float angle = index * 2.399963f;
        float rr = radius * Mathf.Sqrt((index + 0.5f) / Mathf.Max(1, count));
        return center + new Vector3(Mathf.Cos(angle) * rr, Mathf.Sin(angle) * rr * 0.65f, -0.05f);
    }
}
