using System.Collections;
using UnityEngine;

/// <summary>
/// A shelf seasoning bottle (설탕/소금/식초 etc). Picking it up and dropping it back
/// on the shelf just repositions it there. "Using" it - dropping it onto an
/// ingredient or a bowl - measures one spoonful instead of pouring straight
/// in: the scene's measuring spoon is summoned, the bottle tips while the
/// spoon dips in and comes back out loaded, the bottle goes back to its spot
/// on the SauceShelf, and the hand ends up holding the loaded spoon, ready to
/// be tipped into whatever it's used on next.
/// </summary>
public class SeasoningBottle : Carryable
{
    [SerializeField] private string seasoningName = "조미료";

    [Header("Scoop gesture")]
    /// <summary>How far the left hand tips the bottle toward the body (degrees).</summary>
    [SerializeField] private float bodyTiltAngle = 25f;

    public virtual string SeasoningName => seasoningName;

    /// <summary>True while the scoop sequence is playing.</summary>
    public bool IsScooping { get; private set; }

    private SauceShelfController shelfHome;
    private Vector3 shelfHomeLocal;

    /// <summary>Set by the shelf when this bottle is taken off it - where to
    /// send it back after a scoop.</summary>
    public void SetShelfHome(SauceShelfController shelf, Vector3 localPosition)
    {
        shelfHome = shelf;
        shelfHomeLocal = localPosition;
    }

    /// <summary>An empty measuring spoon, held in hand, clicking this bottle
    /// (wherever it happens to be resting) scoops one dose from it - the real
    /// exam move of measuring out a spoonful rather than pouring straight from
    /// the bottle.</summary>
    protected override void OnClickedWhileHandFull(Carryable held)
    {
        if (held is ToolItem spoon && spoon.Kind == ToolKind.MeasuringSpoon && !spoon.IsSpoonLoaded)
        {
            spoon.LoadSeasoning(SeasoningName);
            return;
        }

        // Clicking another bottle on the shelf while holding one means "put it
        // here", not "use it on this bottle" - hand it to the shelf.
        if (held is SeasoningBottle && CurrentContainer is SauceShelfController shelf)
        {
            shelf.ForceActivate();
        }
    }

    public override bool CanAcceptDrop(Carryable item) =>
        item is ToolItem spoon && spoon.Kind == ToolKind.MeasuringSpoon && !spoon.IsSpoonLoaded;

    /// <summary>Starts the scoop sequence (bottle held, used on something).
    /// Returns false if it can't run - no measuring spoon in the scene, no
    /// shelf spot to return to, or one already playing.</summary>
    public bool TryScoopWithSpoon()
    {
        HandCarry hand = HandCarry.Instance;
        ToolItem spoon = ToolItem.MeasuringSpoon;
        if (IsScooping || hand == null || hand.Current != this) return false;

        if (spoon == null || !spoon.isActiveAndEnabled)
        {
            Debug.LogWarning("[SeasoningBottle] No measuringSpoon (ToolItem, kind MeasuringSpoon) in the scene - run Tools/Kitchen/Setup Measuring Spoon.");
            return false;
        }
        if (!HasHome) return false;

        StartCoroutine(ScoopRoutine(hand, spoon));
        return true;
    }

    /// <summary>Two-handed scoop, like measuring at the exam counter: the LEFT
    /// hand takes the bottle beside the right hand and tips it slightly toward
    /// the body (the top leaning toward the camera, so the opening faces the
    /// spoon); the RIGHT hand brings the measuring spoon over, dips it into
    /// the bottle and draws it back out holding one level spoonful. The
    /// bottle then goes back to its SauceShelf spot and the right hand keeps
    /// the loaded spoon.
    /// The bottle is still HandCarry's held item throughout (its position is
    /// driven off the right hand every frame), so its world position is set
    /// through tempHoldOffset, compensating for the right hand's own reach.</summary>
    private IEnumerator ScoopRoutine(HandCarry hand, ToolItem spoon)
    {
        IsScooping = true;
        hand.IsLocked = true;

        HandController rightHand = HandController.Instance;
        SupportHandController leftHand = SupportHandController.Instance;

        Renderer bottleRend = GetComponent<Renderer>();
        float height = bottleRend != null ? Mathf.Max(bottleRend.bounds.size.y, 0.2f) : 0.6f;
        float halfWidth = bottleRend != null ? Mathf.Max(bottleRend.bounds.extents.x, 0.05f) : 0.15f;

        // Summon the spoon: off whatever it was resting on, colliders quiet
        // (only the ones on now - a permanently-off leftover must stay off).
        spoon.CurrentContainer?.Release(spoon);
        spoon.CurrentContainer = null;
        spoon.transform.SetParent(null, true);
        spoon.transform.rotation = spoon.RestingRotation;
        Collider[] spoonColliders = System.Array.FindAll(spoon.GetComponentsInChildren<Collider>(), c => c.enabled);
        foreach (Collider c in spoonColliders) c.enabled = false;
        Vector3 spoonScale = spoon.BaseLocalScale;

        Vector3 handRest = rightHand != null ? rightHand.transform.position : hand.transform.position;
        Vector3 bottleStart = handRest + BaseHandHoldOffset;
        Vector3 bottleHeld = bottleStart + new Vector3(-height * 0.8f, -height * 0.15f, 0f);
        Vector3 spoonRest = handRest + spoon.HandHoldOffset;
        Quaternion tilt = Quaternion.AngleAxis(-bodyTiltAngle, Vector3.right); // top toward the camera/body

        Vector3 reach = Vector3.zero; // right hand's current offset from its rest spot
        Vector3 bottleAt = bottleStart;
        float tiltT = 0f;

        // Left hand wraps the bottle from its left side: fingers toward 3
        // o'clock around it, back of the hand facing the camera.
        leftHand?.BeginHolding(Vector3.right, Vector3.down, 0.8f);

        void Pose()
        {
            rightHand?.SetGestureOffset(reach);
            SetTempHoldRotation(Quaternion.Slerp(Quaternion.identity, tilt, tiltT));
            // Bottle rides the right hand - cancel out the reach so it stays put.
            SetTempHoldOffset(bottleAt - (handRest + reach + BaseHandHoldOffset));
            leftHand?.HoldKnucklesAt(bottleAt + new Vector3(-halfWidth, height * 0.4f, -0.1f));
            spoon.transform.position = handRest + reach + spoon.HandHoldOffset;
        }

        Vector3 Mouth(float depth) =>
            bottleHeld + tilt * Vector3.up * (height * (0.95f + depth)) + new Vector3(0f, 0f, -0.05f);

        // 1) Left hand takes the bottle aside and tips it toward the body; the
        //    spoon appears in the right hand.
        yield return Animate(0.3f, t =>
        {
            bottleAt = Vector3.Lerp(bottleStart, bottleHeld, t);
            tiltT = t;
            spoon.transform.localScale = spoonScale * t;
            Pose();
        });

        // 2) Right hand brings the spoon over the bottle's mouth - its bowl (the
        //    front end), not its pivot: aimed by the pivot, the bowl stuck out
        //    to the side, away from the dish.
        Quaternion spoonDip = Quaternion.Euler(0f, 0f, 35f) * spoon.RestingRotation;
        Quaternion spoonWas = spoon.transform.rotation;
        spoon.transform.rotation = spoonDip;
        Vector3 bowlOffset = spoon.SpoonBowlWorld() - spoon.transform.position;
        bowlOffset.z = 0f;
        spoon.transform.rotation = spoonWas;
        spoonRest += bowlOffset;
        Vector3 reachAbove = Mouth(0.25f) - spoonRest;
        yield return Animate(0.25f, t =>
        {
            reach = Vector3.Lerp(Vector3.zero, reachAbove, t);
            spoon.transform.rotation = Quaternion.Slerp(spoon.RestingRotation, spoonDip, t);
            Pose();
        });

        // 3) Dip in, scoop, draw back out - loaded on the way out.
        Vector3 reachIn = Mouth(-0.3f) - spoonRest;
        yield return Animate(0.18f, t => { reach = Vector3.Lerp(reachAbove, reachIn, t); Pose(); });
        spoon.ConsumeSeasoning();
        spoon.LoadSeasoning(SeasoningName);
        spoon.transform.localScale = spoonScale * 1.15f; // LoadSeasoning's own bulge, relative to true size
        yield return Animate(0.18f, t => { reach = Vector3.Lerp(reachIn, reachAbove, t); Pose(); });

        // 4) The bottle is set upright and goes back to its shelf spot (the
        //    left hand lets go); the right hand brings the loaded spoon back.
        leftHand?.Hide();
        Vector3 bottleHome = HomeWorldPoint();
        yield return Animate(0.35f, t =>
        {
            reach = Vector3.Lerp(reachAbove, Vector3.zero, t);
            bottleAt = Vector3.Lerp(bottleHeld, bottleHome, t);
            tiltT = 1f - t;
            spoon.transform.rotation = Quaternion.Slerp(spoonDip, spoon.RestingRotation, t);
            rightHand?.SetGestureOffset(reach);
            SetTempHoldRotation(Quaternion.Slerp(Quaternion.identity, tilt, tiltT));
            SetTempHoldOffset(bottleAt - (handRest + reach + BaseHandHoldOffset));
            spoon.transform.position = handRest + reach + spoon.HandHoldOffset;
        });

        rightHand?.SetGestureOffset(Vector3.zero);
        SetTempHoldRotation(Quaternion.identity);
        SetTempHoldOffset(Vector3.zero);
        foreach (Collider c in spoonColliders) if (c != null) c.enabled = true;

        // Trade: bottle back on its shelf spot, spoon into the right hand.
        ReturnHome();
        hand.PickUp(spoon);
        spoon.transform.localScale = spoonScale * 1.15f;

        hand.IsLocked = false;
        IsScooping = false;
    }
    /// <summary>Where this goes back to after a scoop: its SauceShelf spot.</summary>
    protected virtual bool HasHome => shelfHome != null;
    protected virtual Vector3 HomeWorldPoint() => shelfHome.ShelfWorldPoint(shelfHomeLocal);
    protected virtual void ReturnHome() => shelfHome.ReturnBottle(this, shelfHomeLocal);

    private static IEnumerator Animate(float duration, System.Action<float> step)
    {
        for (float e = 0f; e < duration; e += Time.deltaTime)
        {
            float t = e / duration;
            step(t * t * (3f - 2f * t));
            yield return null;
        }
        step(1f);
    }

    public override void OnPlaced()
    {
        base.OnPlaced();
        SetTempHoldRotation(Quaternion.identity);
        SetTempHoldOffset(Vector3.zero);
    }
}
