using UnityEngine;

/// <summary>
/// Lets the player shake the wok by holding and dragging the mouse over it.
/// Exposes ShakeSpeed (screen px/sec, decaying when not actively dragged) for
/// CookableItem to read - fast dragging = stir-frying, staying still = burning.
/// </summary>
public class WokDragHandler : MonoBehaviour, IDraggable
{
    [SerializeField] private float wobbleStrength = 0.15f;
    [SerializeField] private float springBack = 12f;
    [SerializeField] private float decaySpeed = 4f;

    private Vector3 homeLocalPos;
    private StoveBurner homeBurner;
    private Vector3 currentOffset;
    private Carryable carryable;
    private MovableBowl vessel;

    public float ShakeSpeed { get; private set; }

    void Awake()
    {
        homeLocalPos = transform.localPosition;
        carryable = GetComponent<Carryable>();
        vessel = GetComponent<MovableBowl>();
    }

    void Update()
    {
        // This wobble/spring-back only makes sense while the wok is actually
        // resting on a lit burner being shaken - the same CurrentBurner gate
        // Carryable.OnDragStart already uses to decide "shake in place" vs.
        // "pick up and carry". Gating on IsHeld alone (an earlier attempt)
        // wasn't enough: once set back down anywhere else - the rack, a
        // shelf - this kept firing and forced transform.localPosition back to
        // homeLocalPos (a stale Awake-time snapshot, meaningless once
        // reparented under a new container) every single frame, silently
        // overriding whatever actually placed it there.
        bool onBurner = vessel != null && vessel.CurrentBurner != null;
        if (!onBurner)
        {
            homeBurner = null;
            return;
        }

        // Rest spot = where it was just set down on this burner. The Awake-time
        // localPosition was in the shelf's space; reused under the burner it
        // threw the wok far off every frame (it "kept going somewhere else").
        if (homeBurner != vessel.CurrentBurner)
        {
            homeBurner = vessel.CurrentBurner;
            homeLocalPos = transform.localPosition;
            currentOffset = Vector3.zero;
        }

        currentOffset = Vector3.Lerp(currentOffset, Vector3.zero, Time.deltaTime * springBack);
        transform.localPosition = homeLocalPos + currentOffset;

        ShakeSpeed = Mathf.Lerp(ShakeSpeed, 0f, Time.deltaTime * decaySpeed);

        HandController.Instance?.SetHandShake(ShakeSpeed, 300f);
    }

    public bool OnDragStart()
    {
        return true;
    }

    public void OnDragMove(Vector2 mouseDelta, float deltaTime)
    {
        if (deltaTime <= 0f) return;

        ShakeSpeed = mouseDelta.magnitude / deltaTime;

        Vector3 worldDelta = new Vector3(mouseDelta.x, mouseDelta.y, 0f) * 0.002f * wobbleStrength;
        currentOffset = Vector3.ClampMagnitude(currentOffset + worldDelta, 0.35f);
    }

    public void OnDragEnd()
    {
    }
}
